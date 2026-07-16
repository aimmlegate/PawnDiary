// Impure emitter for one exact canonical Biotech birth. ApplyBirthOutcome already proved the child,
// outcome, method, family arc, and adult writer order; this adapter resolves localized prose, creates
// one solo/pair page at the original birth tick, attaches shared bond evidence, and queues generation.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Carries one family-owned birth through the shared Event Catalog dispatcher.</summary>
    internal sealed class FamilyBirthSignal : DiarySignal
    {
        private readonly FamilyBirthEventData payload;
        private readonly BirthMutationSnapshot snapshot;
        private readonly BirthWriterSelection writers;
        private readonly List<Pawn> writerPawns;
        private readonly Pawn child;
        private readonly bool enabledAtBirth;

        public FamilyBirthSignal(
            FamilyBirthEventData payload,
            BirthMutationSnapshot snapshot,
            BirthWriterSelection writers,
            List<Pawn> writerPawns,
            Pawn child,
            bool enabledAtBirth)
        {
            this.payload = payload;
            this.snapshot = snapshot;
            this.writers = writers;
            this.writerPawns = writerPawns ?? new List<Pawn>();
            this.child = child;
            this.enabledAtBirth = enabledAtBirth;
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: writerPawns.Count > 0,
                userEnabled: enabledAtBirth,
                signalEnabled: true,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => snapshot?.correlationId ?? string.Empty;

        public override int DedupWindowTicks => DiaryBiotechPolicy.Snapshot().birthCorrelationExpiryTicks;

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (sink == null || snapshot == null || writers?.writers == null
                || writerPawns.Count == 0 || writerPawns.Count != writers.writers.Count
                || (decision != CaptureDecision.GenerateSolo && decision != CaptureDecision.GeneratePair))
            {
                return;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ByKey("biotechFamilyBirth");
            string label = group == null || string.IsNullOrWhiteSpace(group.label)
                ? "PawnDiary.Event.Biotech.Birth.Label".Translate().Resolve()
                : group.LabelCap.Resolve();
            string instruction = InteractionGroups.InstructionForGroup(group);
            string summary = OutcomeSummary(snapshot);
            string childName = string.IsNullOrWhiteSpace(snapshot.currentChildName)
                ? "PawnDiary.Event.Biotech.Birth.ChildFallback".Translate().Resolve()
                : snapshot.currentChildName;
            string gameContext = BirthContextFormatter.Build(snapshot, writers);

            bool pair = decision == CaptureDecision.GeneratePair
                && writerPawns.Count > 1 && writers.writers.Count > 1;
            DiaryEvent diaryEvent;
            if (pair)
            {
                string text = "PawnDiary.Event.Biotech.Birth.PairFallback".Translate(
                    writerPawns[0].LabelShortCap,
                    writerPawns[1].LabelShortCap,
                    childName,
                    summary).Resolve();
                diaryEvent = sink.AddPairwiseEvent(
                    writerPawns[0],
                    writerPawns[1],
                    FamilyBirthEventData.DefName,
                    label,
                    text,
                    text,
                    instruction,
                    gameContext);
            }
            else
            {
                string text = "PawnDiary.Event.Biotech.Birth.Fallback".Translate(
                    writerPawns[0].LabelShortCap,
                    childName,
                    summary).Resolve();
                diaryEvent = sink.AddSoloEvent(
                    writerPawns[0],
                    child,
                    FamilyBirthEventData.DefName,
                    label,
                    text,
                    instruction,
                    gameContext);
            }

            RestoreBirthTime(diaryEvent, snapshot.birthTick);
            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            ApplyNarrativeEvidence(
                diaryEvent,
                writerPawns[0],
                DiaryEvent.InitiatorRole,
                snapshot,
                child,
                policy);
            if (pair)
            {
                ApplyNarrativeEvidence(
                    diaryEvent,
                    writerPawns[1],
                    DiaryEvent.RecipientRole,
                    snapshot,
                    child,
                    policy);
            }

            try
            {
                if (pair)
                {
                    sink.QueuePair(diaryEvent);
                }
                else
                {
                    sink.QueueSolo(diaryEvent, DiaryEvent.InitiatorRole);
                }
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Canonical Biotech birth page was saved but its initial generation "
                    + "queue failed; normal recovery may retry it: " + exception,
                    "PawnDiary.BiotechBirth.Queue".GetHashCode());
            }
        }

        private static string OutcomeSummary(BirthMutationSnapshot value)
        {
            string key = value.outcomeToken == BiotechBirthOutcomeTokens.Stillbirth
                ? "PawnDiary.Event.Biotech.Birth.Outcome.Stillbirth"
                : value.outcomeToken == BiotechBirthOutcomeTokens.InfantIllness
                    ? "PawnDiary.Event.Biotech.Birth.Outcome.InfantIllness"
                    : "PawnDiary.Event.Biotech.Birth.Outcome.Healthy";
            string summary = key.Translate().Resolve();
            if (value.birtherDied)
            {
                summary += "; " + "PawnDiary.Event.Biotech.Birth.BirtherDied".Translate().Resolve();
            }
            return summary;
        }

        private static void RestoreBirthTime(DiaryEvent diaryEvent, int birthTick)
        {
            if (diaryEvent == null || birthTick < 0 || Find.TickManager == null)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            int elapsed = Math.Max(0, now - birthTick);
            long absolute = Math.Max(0L, (long)Find.TickManager.TicksAbs - elapsed);
            diaryEvent.tick = birthTick;
            diaryEvent.date = GenDate.DateFullStringAt(
                absolute > int.MaxValue ? int.MaxValue : (int)absolute,
                Vector2.zero);
        }

        private static void ApplyNarrativeEvidence(
            DiaryEvent diaryEvent,
            Pawn povPawn,
            string povRole,
            BirthMutationSnapshot value,
            Pawn child,
            BiotechPolicySnapshot policy)
        {
            if (diaryEvent == null || povPawn == null || value == null)
            {
                return;
            }

            try
            {
                List<string> topics = new List<string> { "family" };
                if (value.outcomeToken == BiotechBirthOutcomeTokens.Stillbirth || value.birtherDied)
                {
                    topics.Add("death");
                }

                NarrativeContextBuildResult result = NarrativeContextBuilder.Build(
                    new NarrativeContextBuildRequest
                    {
                        eventId = diaryEvent.eventId,
                        eventTick = value.birthTick,
                        povPawnId = povPawn.GetUniqueLoadID(),
                        povRole = povRole,
                        contextDetailLevel = PawnDiarySettings.NormalizeContextDetailLevel(
                            PawnDiaryMod.Settings?.contextDetailLevel ?? PromptContextDetailLevel.Full),
                        biotech = BuildBiotechSnapshot(povPawn, child, value, policy),
                        evidence = new List<NarrativeEvidence>
                        {
                            new NarrativeEvidence
                            {
                                facet = NarrativeFacetTokens.BondLifecycle,
                                phase = value.outcomeToken,
                                subjectKind = NarrativeSubjectKindTokens.Family,
                                subjectId = value.childId,
                                subjectLabel = value.currentChildName,
                                arcKey = value.familyArcId,
                                beliefTopics = topics,
                                salience = value.outcomeToken == BiotechBirthOutcomeTokens.Stillbirth
                                    || value.birtherDied
                                    ? NarrativeSalienceTokens.Terminal
                                    : NarrativeSalienceTokens.Major,
                                pawnCanKnow = true,
                                sourceDomain = "biotech_birth",
                                sourceDefName = FamilyBirthEventData.DefName
                            }
                        }
                    });
                if (result.evidence.Count > 0)
                {
                    diaryEvent.ApplyNarrativeContext(povRole, result);
                }
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Biotech birth Narrative Continuity evidence failed; the birth page remains: "
                    + exception,
                    "PawnDiary.BiotechBirth.NarrativeEvidence".GetHashCode());
            }
        }

        private static BiotechNarrativeSnapshot BuildBiotechSnapshot(
            Pawn povPawn,
            Pawn child,
            BirthMutationSnapshot value,
            BiotechPolicySnapshot policy)
        {
            if (!ModsConfig.BiotechActive || povPawn == null || value == null)
            {
                return null;
            }

            string xenotypeDefName = value.outcomeToken == BiotechBirthOutcomeTokens.Stillbirth
                ? string.Empty
                : DlcContext.XenotypeDefName(child);
            string xenotypeLabel = value.outcomeToken == BiotechBirthOutcomeTokens.Stillbirth
                ? string.Empty
                : DlcContext.Xenotype(child);
            string identityText = string.IsNullOrWhiteSpace(xenotypeDefName)
                || string.IsNullOrWhiteSpace(xenotypeLabel)
                ? string.Empty
                : FormatNarrative(
                    policy?.identityNarrativeFormat,
                    value.currentChildName,
                    xenotypeLabel);
            return new BiotechNarrativeSnapshot
            {
                providerAvailable = true,
                povPawnId = povPawn.GetUniqueLoadID(),
                childId = value.childId,
                xenotypeDefName = xenotypeDefName,
                identityText = identityText,
                sourceTick = value.birthTick,
                pawnCanKnow = true,
                hasVerifiedPovConnection = true
            };
        }

        private static string FormatNarrative(string format, params object[] values)
        {
            if (string.IsNullOrWhiteSpace(format))
            {
                return string.Empty;
            }

            try
            {
                return DiaryLineCleaner.CleanLine(string.Format(format, values));
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }
    }
}

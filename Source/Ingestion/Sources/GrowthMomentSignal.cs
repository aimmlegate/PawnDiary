// Impure emitter for one verified canonical Biotech growth mutation. The component already captured
// and diffed event-time facts; this adapter resolves localized XML/Keyed prose, creates the truthful
// child/supporter writer shape, attaches Narrative Continuity evidence, and queues normal generation.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Carries one age-7/10/13 growth mutation through the shared Event Catalog dispatcher.</summary>
    internal sealed class GrowthMomentSignal : DiarySignal
    {
        private readonly GrowthMomentEventData payload;
        private readonly Pawn child;
        private readonly Pawn supporter;
        private readonly GrowthMomentMutation mutation;
        private readonly BiotechFamilyArcState familyArc;

        public GrowthMomentSignal(
            GrowthMomentEventData payload,
            Pawn child,
            Pawn supporter,
            GrowthMomentMutation mutation,
            BiotechFamilyArcState familyArc)
        {
            this.payload = payload;
            this.child = child;
            this.supporter = supporter;
            this.mutation = mutation;
            this.familyArc = familyArc;
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            bool enabled = PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsBiotechGrowthMomentEnabled();
            return DiaryGameComponent.BuildCaptureContext(
                eligible: payload != null && (payload.ChildEligible || payload.SupporterEligible),
                userEnabled: enabled,
                signalEnabled: DiarySignalPolicies.Enabled(DiarySignalPolicies.Progression),
                ambientSignalEnabled: true);
        }

        public override string DedupKey => mutation?.correlationId ?? string.Empty;

        public override int DedupWindowTicks => DiaryBiotechPolicy.Snapshot().growthPendingExpiryTicks;

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if ((decision != CaptureDecision.GenerateSolo && decision != CaptureDecision.GeneratePair)
                || sink == null || child == null || mutation == null || payload == null)
            {
                return;
            }

            GrowthWriterShape shape = GrowthWriterPolicy.Decide(
                payload.ChildId,
                payload.ChildEligible,
                payload.SupporterEligible ? mutation.supporter : null);
            if (shape == GrowthWriterShape.Drop
                || (shape == GrowthWriterShape.Pair && supporter == null)
                || (shape == GrowthWriterShape.SupporterSolo && supporter == null))
            {
                return;
            }

            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyProgression(GrowthMomentEventData.DefName);
            string label = group == null || string.IsNullOrWhiteSpace(group.label)
                ? "PawnDiary.Event.Biotech.Growth.Label".Translate().Resolve()
                : group.LabelCap.Resolve();
            string instruction = InteractionGroups.InstructionForProgression(group);
            string opportunityDescription = OpportunityDescription(policy, mutation.opportunityBand);
            string upbringingDescription = ObservationDescription(
                policy,
                mutation.supporter?.observationBand);
            string initiatorFamilyRole = shape == GrowthWriterShape.SupporterSolo
                ? mutation.supporter?.roleToken ?? string.Empty
                : BiotechFamilyRoleTokens.Child;
            string recipientFamilyRole = shape == GrowthWriterShape.Pair
                ? mutation.supporter?.roleToken ?? string.Empty
                : string.Empty;
            string gameContext = GrowthMomentContextFormatter.Build(
                mutation,
                opportunityDescription,
                upbringingDescription,
                initiatorFamilyRole,
                recipientFamilyRole,
                newInterestDescription: policy.newInterestDescription,
                deepenedInterestDescription: policy.deepenedInterestDescription);
            string summary = GrowthSummary(mutation, opportunityDescription);
            string childText = "PawnDiary.Event.Biotech.Growth.Fallback".Translate(
                child.LabelShortCap,
                mutation.age,
                summary).Resolve();
            string supporterText = supporter == null
                ? string.Empty
                : "PawnDiary.Event.Biotech.Growth.SupporterFallback".Translate(
                    supporter.LabelShortCap,
                    child.LabelShortCap,
                    summary).Resolve();

            DiaryEvent diaryEvent;
            if (shape == GrowthWriterShape.Pair)
            {
                diaryEvent = sink.AddPairwiseEvent(
                    child,
                    supporter,
                    GrowthMomentEventData.DefName,
                    label,
                    childText,
                    supporterText,
                    instruction,
                    gameContext);
                ApplyNarrativeEvidence(diaryEvent, child, DiaryEvent.InitiatorRole, child, mutation, familyArc, policy);
                ApplyNarrativeEvidence(diaryEvent, supporter, DiaryEvent.RecipientRole, child, mutation, familyArc, policy);
            }
            else if (shape == GrowthWriterShape.SupporterSolo)
            {
                diaryEvent = sink.AddSoloEvent(
                    supporter,
                    child,
                    GrowthMomentEventData.DefName,
                    label,
                    supporterText,
                    instruction,
                    gameContext);
                ApplyNarrativeEvidence(diaryEvent, supporter, DiaryEvent.InitiatorRole, child, mutation, familyArc, policy);
            }
            else
            {
                diaryEvent = sink.AddSoloEvent(
                    child,
                    supporter,
                    GrowthMomentEventData.DefName,
                    label,
                    childText,
                    instruction,
                    gameContext);
                ApplyNarrativeEvidence(diaryEvent, child, DiaryEvent.InitiatorRole, child, mutation, familyArc, policy);
            }

            try
            {
                if (shape == GrowthWriterShape.Pair)
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
                // The canonical DiaryEvent already owns the birthday at this point. Keep that durable
                // event and let ordinary orphan recovery handle generation instead of releasing a second
                // Birthday page for the same growth moment.
                Log.ErrorOnce(
                    "[Pawn Diary] Biotech growth page was created but its initial generation queue failed; "
                    + "normal recovery may retry it: " + exception,
                    "PawnDiary.BiotechGrowth.Queue".GetHashCode());
            }
        }

        private static string OpportunityDescription(BiotechPolicySnapshot policy, string token)
        {
            List<BiotechOpportunityBandRule> bands = policy?.opportunityBands;
            if (bands == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < bands.Count; i++)
            {
                BiotechOpportunityBandRule band = bands[i];
                if (band != null && string.Equals(band.token, token, StringComparison.Ordinal))
                {
                    return band.description ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static string ObservationDescription(BiotechPolicySnapshot policy, string token)
        {
            List<BiotechObservationBandRule> bands = policy?.observationBands;
            if (bands == null || string.IsNullOrWhiteSpace(token)) return string.Empty;
            for (int i = 0; i < bands.Count; i++)
            {
                BiotechObservationBandRule band = bands[i];
                if (band != null && string.Equals(band.token, token, StringComparison.Ordinal))
                {
                    return band.description ?? string.Empty;
                }
            }
            return string.Empty;
        }

        private static string GrowthSummary(GrowthMomentMutation mutation, string opportunityDescription)
        {
            List<string> facts = new List<string>();
            if (mutation.selectedTrait != null && !string.IsNullOrWhiteSpace(mutation.selectedTrait.label))
            {
                facts.Add("PawnDiary.Event.Biotech.Growth.Summary.Trait"
                    .Translate(mutation.selectedTrait.label).Resolve());
            }
            else if (mutation.additionalTraitKeysToConsume != null
                && mutation.additionalTraitKeysToConsume.Count > 0)
            {
                facts.Add("PawnDiary.Event.Biotech.Growth.Summary.Identity".Translate().Resolve());
            }

            int interestCount = Math.Min(4, mutation.passionChanges?.Count ?? 0);
            for (int i = 0; i < interestCount; i++)
            {
                PassionMutation passion = mutation.passionChanges[i];
                string key = BiotechPassionTokens.Rank(passion.beforePassion) == 0
                    ? "PawnDiary.Event.Biotech.Growth.Summary.NewInterest"
                    : "PawnDiary.Event.Biotech.Growth.Summary.DeepenedInterest";
                facts.Add(key.Translate(passion.label).Resolve());
            }

            if (mutation.nicknameChanged)
            {
                facts.Add("PawnDiary.Event.Biotech.Growth.Summary.Name"
                    .Translate(mutation.currentShortName).Resolve());
            }

            if (mutation.newResponsibilities)
            {
                facts.Add("PawnDiary.Event.Biotech.Growth.Summary.Responsibilities".Translate().Resolve());
            }

            if (!string.IsNullOrWhiteSpace(opportunityDescription))
            {
                facts.Add(opportunityDescription);
            }

            return facts.Count == 0
                ? "PawnDiary.Event.Biotech.Growth.Summary.Generic".Translate().Resolve()
                : string.Join("; ", facts.ToArray());
        }

        private static void ApplyNarrativeEvidence(
            DiaryEvent diaryEvent,
            Pawn povPawn,
            string povRole,
            Pawn child,
            GrowthMomentMutation mutation,
            BiotechFamilyArcState familyArc,
            BiotechPolicySnapshot policy)
        {
            if (diaryEvent == null || povPawn == null || child == null || mutation == null)
            {
                return;
            }

            try
            {
                NarrativeContextBuildResult result = NarrativeContextBuilder.Build(
                    new NarrativeContextBuildRequest
                    {
                        eventId = diaryEvent.eventId,
                        eventTick = diaryEvent.tick,
                        povPawnId = povPawn.GetUniqueLoadID(),
                        povRole = povRole,
                        // Provider selection is frozen before endpoint routing. Use the player's global
                        // preset here so Compact/Balanced lens caps apply before the factual unit is saved.
                        contextDetailLevel = PawnDiarySettings.NormalizeContextDetailLevel(
                            PawnDiaryMod.Settings?.contextDetailLevel ?? PromptContextDetailLevel.Full),
                        biotech = BuildBiotechNarrativeSnapshot(
                            povPawn,
                            child,
                            mutation,
                            familyArc,
                            policy,
                            diaryEvent.tick),
                        evidence = new List<NarrativeEvidence>
                        {
                            new NarrativeEvidence
                            {
                                facet = NarrativeFacetTokens.IdentityTransition,
                                phase = mutation.stageToken,
                                subjectKind = NarrativeSubjectKindTokens.Pawn,
                                subjectId = mutation.childId,
                                subjectLabel = child.LabelShortCap,
                                arcKey = mutation.familyArcId,
                                salience = NarrativeSalienceTokens.Meaningful,
                                pawnCanKnow = true,
                                sourceDomain = "biotech_growth",
                                sourceDefName = GrowthMomentEventData.DefName
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
                    "[Pawn Diary] Biotech growth Narrative Continuity evidence failed; the growth page remains: "
                    + exception,
                    "PawnDiary.BiotechGrowth.NarrativeEvidence".GetHashCode());
            }
        }

        /// <summary>
        /// Copies only visible/current Biotech facts into the pure provider contract. The family arc is
        /// already source-owned saved evidence; the xenotype accessor is double-gated in DlcContext.
        /// </summary>
        private static BiotechNarrativeSnapshot BuildBiotechNarrativeSnapshot(
            Pawn povPawn,
            Pawn child,
            GrowthMomentMutation mutation,
            BiotechFamilyArcState familyArc,
            BiotechPolicySnapshot policy,
            int sourceTick)
        {
            if (!ModsConfig.BiotechActive || povPawn == null || child == null || mutation == null)
            {
                return null;
            }

            string continuity = BiotechNarrativeProvider.FamilyContinuity(
                familyArc != null && familyArc.birthTick > 0 && !familyArc.baselineOnly,
                FamilyArcPolicy.HasObservedUpbringing(familyArc),
                FamilyArcPolicy.HasExactFamilyConnection(familyArc));
            string childName = DiaryLineCleaner.CleanLine(child.LabelShortCap);
            string familyFormat = FamilyNarrativeFormat(policy, continuity);
            string familyText = FormatNarrative(familyFormat, childName);

            string xenotypeDefName = DlcContext.XenotypeDefName(child);
            string xenotypeLabel = DlcContext.Xenotype(child);
            string identityText = string.IsNullOrWhiteSpace(xenotypeDefName)
                || string.IsNullOrWhiteSpace(xenotypeLabel)
                ? string.Empty
                : FormatNarrative(policy?.identityNarrativeFormat, childName, xenotypeLabel);

            return new BiotechNarrativeSnapshot
            {
                providerAvailable = true,
                povPawnId = povPawn.GetUniqueLoadID(),
                childId = mutation.childId,
                familyArcId = continuity.Length == 0 ? string.Empty : mutation.familyArcId,
                familyContinuity = continuity,
                familyText = familyText,
                xenotypeDefName = xenotypeDefName,
                identityText = identityText,
                sourceTick = sourceTick,
                pawnCanKnow = true,
                hasVerifiedPovConnection = povPawn == child
                    || string.Equals(
                        povPawn.GetUniqueLoadID(),
                        mutation.supporter?.adultId,
                        StringComparison.Ordinal)
            };
        }

        private static string FamilyNarrativeFormat(BiotechPolicySnapshot policy, string continuity)
        {
            if (policy == null) return string.Empty;
            if (continuity == BiotechNarrativeContinuityTokens.SinceBirth)
                return policy.familySinceBirthNarrativeFormat;
            if (continuity == BiotechNarrativeContinuityTokens.ObservedChildhood)
                return policy.familyObservedNarrativeFormat;
            if (continuity == BiotechNarrativeContinuityTokens.BaselineFamily)
                return policy.familyBaselineNarrativeFormat;
            return string.Empty;
        }

        private static string FormatNarrative(string format, params object[] values)
        {
            if (string.IsNullOrWhiteSpace(format)) return string.Empty;
            try
            {
                return DiaryLineCleaner.CleanLine(string.Format(format, values));
            }
            catch (FormatException)
            {
                // A malformed custom translation disables only this optional lens.
                return string.Empty;
            }
        }
    }
}

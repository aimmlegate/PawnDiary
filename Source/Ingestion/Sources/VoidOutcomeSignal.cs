// Impure adapter for one verified A3.0 terminal void answer. The pure policy has already verified the
// exact terminal level, chosen branch, and single choosing author; this class only applies XML/settings
// gates, localized fallback text, one solo event creation, and generation handoff. The choosing pawn is
// the sole canonical author, so this signal never fans out to pocket-map companions.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Creates one actor-authored solo page for a verified terminal void outcome.</summary>
    internal sealed class VoidOutcomeSignal : DiarySignal
    {
        private readonly VoidOutcomeFacts facts;
        private readonly VoidOutcomePlan plan;
        private readonly AnomalyPolicySnapshot policy;
        private readonly DiaryInteractionGroupDef group;
        private readonly Pawn actor;
        private readonly AnomalyEventData payload;

        /// <summary>Non-null only after the dedicated terminal ending event was actually created.</summary>
        internal DiaryEvent CreatedEvent { get; private set; }

        internal VoidOutcomeSignal(
            VoidOutcomeFacts facts,
            VoidOutcomePlan plan,
            AnomalyPolicySnapshot policy,
            DiaryInteractionGroupDef group,
            Pawn actor)
        {
            this.facts = facts;
            this.plan = plan;
            this.policy = policy ?? AnomalyPolicySnapshot.CreateDefault();
            this.group = group;
            this.actor = actor;
            string writerId = plan?.selectedWriter?.pawnId ?? string.Empty;
            payload = new AnomalyEventData
            {
                PawnId = writerId,
                Tick = Math.Max(0, facts?.tick ?? 0),
                DefName = AnomalyEventDefNames.VoidOutcome,
                Kind = AnomalyKindTokens.VoidOutcome,
                SourceKey = plan?.sourceKey ?? string.Empty,
                HasVerifiedSource = AnomalyVoidOutcomePolicy.OwnsTerminalTale(plan),
                PlayerVisible = facts?.playerVisible == true,
                AlreadyRecorded = false,
                FirstWriterId = writerId,
                FirstWriterEligible = WriterMatches(writerId),
                SecondWriterId = string.Empty,
                SecondWriterEligible = false
            };
            PreserveHistoricalOrdering(payload.Tick);
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            bool enabled = group != null && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
            return DiaryGameComponent.BuildCaptureContext(
                eligible: payload.FirstWriterEligible,
                userEnabled: enabled,
                signalEnabled: ModsConfig.AnomalyActive && policy.voidOutcomeEnabled,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload.DedupKey();

        // A terminal answer occurs once per game (the committed terminal token is the real barrier);
        // this positive window is only a defensive secondary guard against a same-tick repeat.
        public override int DedupWindowTicks => Math.Max(1, policy.taleOwnershipExpiryTicks);

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (sink == null || facts == null || plan == null || group == null || actor == null
                || plan.selectedWriter == null
                || (decision != CaptureDecision.GenerateSolo
                    && decision != CaptureDecision.GeneratePair)) return;
            string label = string.IsNullOrWhiteSpace(group.label)
                ? "PawnDiary.Event.Anomaly.Void.Label".Translate().Resolve()
                : group.LabelCap.Resolve();
            string instruction = InteractionGroups.InstructionForGroup(group);
            string context = VoidOutcomeContextFormatter.Format(facts, plan);
            CreatedEvent = CreateSoloEvent(
                sink,
                actor,
                null,
                payload.DefName,
                label,
                Fallback(),
                instruction,
                context);
            if (CreatedEvent != null)
            {
                ApplyNarrativeEvidence(sink);
                FinishCreatedEvent(sink, actor);
            }
        }

        /// <summary>Freezes the exact actor/branch/monolith terminal row before N4 can inspect it.</summary>
        private void ApplyNarrativeEvidence(DiaryGameComponent sink)
        {
            try
            {
                string pawnId = actor.GetUniqueLoadID();
                NarrativeContextBuildResult result = NarrativeContextBuilder.Build(
                    new NarrativeContextBuildRequest
                    {
                        eventId = CreatedEvent.eventId,
                        eventTick = CreatedEvent.tick,
                        povPawnId = pawnId,
                        povRole = DiaryEvent.InitiatorRole,
                        evidence = new List<NarrativeEvidence>
                        {
                            new NarrativeEvidence
                            {
                                facet = NarrativeFacetTokens.JourneyChapter,
                                phase = plan.outcomeToken,
                                subjectKind = NarrativeSubjectKindTokens.Colony,
                                subjectId = "anomaly_void",
                                arcKey = AnomalyVoidOutcomePolicy.NarrativeArcKey,
                                beliefTopics = new List<string> { "anomaly", "void", "home" },
                                salience = NarrativeSalienceTokens.Terminal,
                                pawnCanKnow = true,
                                sourceDomain = AnomalyVoidOutcomePolicy.NarrativeSourceDomain,
                                sourceDefName = payload.DefName
                            }
                        }
                    });
                if (result.evidence.Count > 0)
                {
                    CreatedEvent.ApplyNarrativeContext(DiaryEvent.InitiatorRole, result);
                }
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Terminal void Narrative Continuity evidence failed; the page remains: "
                        + exception,
                    "PawnDiary.VoidOutcome.NarrativeEvidence".GetHashCode());
            }
        }

        /// <summary>
        /// Restores the preverified diary reference and starts generation after the durable page
        /// exists. The choosing pawn may already be despawned into the closed metal hell, so the
        /// reference is inserted from the exact frozen event-time eligibility rather than live state.
        /// </summary>
        private void FinishCreatedEvent(DiaryGameComponent sink, Pawn writer)
        {
            try
            {
                sink.AddPreverifiedEventRef(writer, CreatedEvent.eventId, true);
                sink.QueueSolo(CreatedEvent, DiaryEvent.InitiatorRole);
            }
            catch (Exception exception)
            {
                // The repository already owns the fallback-text page and the committed terminal token.
                // Releasing the deferred Tale here would create a duplicate ending owner.
                Log.ErrorOnce(
                    "[Pawn Diary] Terminal void page was created but its exact reference or initial "
                    + "generation handoff failed; the dedicated page remains authoritative: "
                    + exception,
                    "PawnDiary.VoidOutcome.PostCreate".GetHashCode());
            }
        }

        private string Fallback()
        {
            return plan.outcomeToken == AnomalyOutcomeTokens.Embraced
                ? "PawnDiary.Event.Anomaly.Void.EmbracedFallback"
                    .Translate(actor.LabelShortCap).Resolve()
                : "PawnDiary.Event.Anomaly.Void.DisruptedFallback"
                    .Translate(actor.LabelShortCap).Resolve();
        }

        private bool WriterMatches(string writerId)
        {
            return actor != null && !string.IsNullOrEmpty(writerId)
                && string.Equals(actor.GetUniqueLoadID(), writerId, StringComparison.Ordinal);
        }
    }
}

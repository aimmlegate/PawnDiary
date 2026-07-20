// Impure adapter for one already-verified containment Escape call tree. DlcContext and the synchronous
// scope supplied event-time facts; the pure policy selected at most two deterministic writers. This
// class only applies the package/group gates, localized fallback, event creation, and queue handoff.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Creates one solo or pairwise page for one logical containment breach.</summary>
    internal sealed class AnomalyContainmentBreachSignal : DiarySignal
    {
        private readonly ContainmentEscapeFacts facts;
        private readonly ContainmentBreachPlan plan;
        private readonly AnomalyPolicySnapshot policy;
        private readonly DiaryInteractionGroupDef group;
        private readonly List<Pawn> writers;
        private readonly Pawn escapedSubject;
        private readonly AnomalyEventData payload;

        /// <summary>Non-null only after the canonical containment page was actually created.</summary>
        internal DiaryEvent CreatedEvent { get; private set; }

        /// <summary>Copies the exact plan into the shared Anomaly catalog envelope.</summary>
        internal AnomalyContainmentBreachSignal(
            ContainmentEscapeFacts facts,
            ContainmentBreachPlan plan,
            AnomalyPolicySnapshot policy,
            DiaryInteractionGroupDef group,
            List<Pawn> writers,
            Pawn escapedSubject)
        {
            this.facts = facts;
            this.plan = plan;
            this.policy = policy ?? AnomalyPolicySnapshot.CreateDefault();
            this.group = group;
            this.writers = writers ?? new List<Pawn>();
            this.escapedSubject = escapedSubject;

            AnomalyWriterSelection first = plan?.selectedWriters.Count > 0
                ? plan.selectedWriters[0] : null;
            AnomalyWriterSelection second = plan?.selectedWriters.Count > 1
                ? plan.selectedWriters[1] : null;
            bool firstResolved = WriterMatches(0, first);
            bool secondResolved = WriterMatches(1, second);
            payload = new AnomalyEventData
            {
                PawnId = first?.pawnId ?? string.Empty,
                Tick = Math.Max(0, facts?.tick ?? 0),
                DefName = AnomalyEventDefNames.ContainmentBreach,
                Kind = AnomalyKindTokens.ContainmentBreach,
                SourceKey = plan?.dedupKey ?? string.Empty,
                HasVerifiedSource = plan != null && plan.valid && plan.escapedCount > 0,
                PlayerVisible = true,
                AlreadyRecorded = false,
                FirstWriterId = first?.pawnId ?? string.Empty,
                FirstWriterEligible = firstResolved,
                SecondWriterId = second?.pawnId ?? string.Empty,
                SecondWriterEligible = secondResolved
            };
            PreserveHistoricalOrdering(payload.Tick);
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            bool enabled = group != null && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
            return DiaryGameComponent.BuildCaptureContext(
                eligible: payload.FirstWriterEligible || payload.SecondWriterEligible,
                userEnabled: enabled,
                signalEnabled: ModsConfig.AnomalyActive && policy.containmentEnabled,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload.DedupKey();

        public override int DedupWindowTicks => Math.Max(1, policy.containmentDedupTicks);

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (sink == null || facts == null || plan == null || group == null
                || writers.Count == 0
                || (decision != CaptureDecision.GenerateSolo
                    && decision != CaptureDecision.GeneratePair))
            {
                return;
            }

            string label = string.IsNullOrWhiteSpace(group.label)
                ? "PawnDiary.Event.Anomaly.Containment.Label".Translate().Resolve()
                : group.LabelCap.Resolve();
            string instruction = InteractionGroups.InstructionForGroup(group);
            string entitySummary = ContainmentBreachContextFormatter.EntitySummary(plan);
            string gameContext = ContainmentBreachContextFormatter.Format(plan);

            bool pair = decision == CaptureDecision.GeneratePair && writers.Count > 1;
            if (pair)
            {
                string firstText = "PawnDiary.Event.Anomaly.Containment.Fallback"
                    .Translate(writers[0].LabelShortCap, entitySummary).Resolve();
                string secondText = "PawnDiary.Event.Anomaly.Containment.Fallback"
                    .Translate(writers[1].LabelShortCap, entitySummary).Resolve();
                CreatedEvent = CreatePairwiseEvent(
                    sink,
                    writers[0],
                    writers[1],
                    payload.DefName,
                    label,
                    firstText,
                    secondText,
                    instruction,
                    gameContext);
                if (CreatedEvent != null) sink.QueuePair(CreatedEvent);
                return;
            }

            Pawn writer = writers[0];
            string text = "PawnDiary.Event.Anomaly.Containment.Fallback"
                .Translate(writer.LabelShortCap, entitySummary).Resolve();
            CreatedEvent = CreateSoloEvent(
                sink,
                writer,
                escapedSubject,
                payload.DefName,
                label,
                text,
                instruction,
                gameContext);
            if (CreatedEvent != null) sink.QueueSolo(CreatedEvent, DiaryEvent.InitiatorRole);
        }

        private bool WriterMatches(int index, AnomalyWriterSelection selection)
        {
            return selection != null && index >= 0 && index < writers.Count && writers[index] != null
                && string.Equals(
                    writers[index].GetUniqueLoadID(),
                    selection.pawnId,
                    StringComparison.Ordinal);
        }
    }
}

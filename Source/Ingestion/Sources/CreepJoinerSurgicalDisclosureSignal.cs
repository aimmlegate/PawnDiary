// Impure adapter for one verified A2.1 creepjoiner surgical disclosure. The pure policy already
// committed non-terminal history and selected exact surgeon/subject roles; this class only applies
// XML/settings gates, localized fallback text, event creation, and queue handoff.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Creates one surgeon-first solo/pair event for a generic visible disclosure.</summary>
    internal sealed class CreepJoinerSurgicalDisclosureSignal : DiarySignal
    {
        private readonly CreepJoinerSurgicalDisclosureFacts facts;
        private readonly CreepJoinerSurgicalDisclosurePlan plan;
        private readonly AnomalyPolicySnapshot policy;
        private readonly DiaryInteractionGroupDef group;
        private readonly List<Pawn> writers;
        private readonly Pawn subject;
        private readonly Pawn surgeon;
        private readonly AnomalyEventData payload;

        /// <summary>Non-null only after the dedicated disclosure event was actually created.</summary>
        internal DiaryEvent CreatedEvent { get; private set; }

        internal CreepJoinerSurgicalDisclosureSignal(
            CreepJoinerSurgicalDisclosureFacts facts,
            CreepJoinerSurgicalDisclosurePlan plan,
            AnomalyPolicySnapshot policy,
            DiaryInteractionGroupDef group,
            List<Pawn> writers,
            Pawn subject,
            Pawn surgeon)
        {
            this.facts = facts;
            this.plan = plan;
            this.policy = policy ?? AnomalyPolicySnapshot.CreateDefault();
            this.group = group;
            this.writers = writers ?? new List<Pawn>();
            this.subject = subject;
            this.surgeon = surgeon;
            AnomalyWriterSelection first = plan?.selectedWriters.Count > 0
                ? plan.selectedWriters[0] : null;
            AnomalyWriterSelection second = plan?.selectedWriters.Count > 1
                ? plan.selectedWriters[1] : null;
            payload = new AnomalyEventData
            {
                PawnId = first?.pawnId ?? second?.pawnId ?? string.Empty,
                Tick = Math.Max(0, facts?.tick ?? 0),
                DefName = AnomalyEventDefNames.CreepJoinerOutcome,
                Kind = AnomalyKindTokens.CreepJoinerOutcome,
                SourceKey = plan?.sourceKey ?? string.Empty,
                HasVerifiedSource = plan != null && plan.valid && plan.advanceArc,
                PlayerVisible = facts != null && facts.playerVisible,
                AlreadyRecorded = plan?.replaySuppressed == true,
                FirstWriterId = first?.pawnId ?? string.Empty,
                FirstWriterEligible = WriterMatches(0, first),
                SecondWriterId = second?.pawnId ?? string.Empty,
                SecondWriterEligible = WriterMatches(1, second)
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
                signalEnabled: ModsConfig.AnomalyActive && policy.creepJoinerEnabled,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload.DedupKey();

        public override int DedupWindowTicks => Math.Max(1, policy.creepJoinerOutcomeDedupTicks);

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (sink == null || facts == null || plan == null || group == null
                || subject == null || surgeon == null || writers.Count == 0
                || (decision != CaptureDecision.GenerateSolo
                    && decision != CaptureDecision.GeneratePair)) return;
            string label = string.IsNullOrWhiteSpace(group.label)
                ? "PawnDiary.Event.Anomaly.CreepJoiner.Label".Translate().Resolve()
                : group.LabelCap.Resolve();
            string instruction = InteractionGroups.InstructionForGroup(group);
            string context = CreepJoinerSurgicalDisclosureContextFormatter.Format(facts, plan);
            bool pair = decision == CaptureDecision.GeneratePair && writers.Count > 1;
            if (pair)
            {
                CreatedEvent = CreatePairwiseEvent(
                    sink,
                    writers[0],
                    writers[1],
                    payload.DefName,
                    label,
                    FallbackFor(plan.selectedWriters[0]),
                    FallbackFor(plan.selectedWriters[1]),
                    instruction,
                    context);
                if (CreatedEvent != null) sink.QueuePair(CreatedEvent);
                return;
            }

            AnomalyWriterSelection selected = plan.selectedWriters[0];
            Pawn writer = writers[0];
            Pawn other = selected.roleToken == AnomalyWitnessRoleTokens.Surgeon
                ? subject : surgeon;
            CreatedEvent = CreateSoloEvent(
                sink,
                writer,
                ReferenceEquals(writer, other) ? null : other,
                payload.DefName,
                label,
                FallbackFor(selected),
                instruction,
                context);
            if (CreatedEvent != null) sink.QueueSolo(CreatedEvent, DiaryEvent.InitiatorRole);
        }

        private string FallbackFor(AnomalyWriterSelection selected)
        {
            return selected?.roleToken == AnomalyWitnessRoleTokens.Subject
                ? "PawnDiary.Event.Anomaly.CreepJoiner.Surgical.SubjectFallback"
                    .Translate(subject.LabelShortCap, surgeon.LabelShortCap).Resolve()
                : "PawnDiary.Event.Anomaly.CreepJoiner.Surgical.SurgeonFallback"
                    .Translate(surgeon.LabelShortCap, subject.LabelShortCap).Resolve();
        }

        private bool WriterMatches(int index, AnomalyWriterSelection selection)
        {
            return selection != null && index >= 0 && index < writers.Count && writers[index] != null
                && string.Equals(
                    writers[index].GetUniqueLoadID(), selection.pawnId, StringComparison.Ordinal);
        }
    }
}

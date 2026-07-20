// Impure adapter for one already-verified visible creepjoiner lifecycle outcome. DlcContext supplied
// event-time facts and the pure policy selected exactly one truthful POV; this class applies only
// package/group gates, localized fallback prose, event creation, and transport handoff.
using System;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Creates one solo page for a verified rejection, aggression, or departure.</summary>
    internal sealed class CreepJoinerOutcomeSignal : DiarySignal
    {
        private readonly CreepJoinerOutcomeFacts facts;
        private readonly CreepJoinerOutcomePlan plan;
        private readonly AnomalyPolicySnapshot policy;
        private readonly DiaryInteractionGroupDef group;
        private readonly Pawn writer;
        private readonly Pawn subject;
        private readonly AnomalyEventData payload;

        /// <summary>Non-null only after the canonical visible-outcome page was actually created.</summary>
        internal DiaryEvent CreatedEvent { get; private set; }

        internal CreepJoinerOutcomeSignal(
            CreepJoinerOutcomeFacts facts,
            CreepJoinerOutcomePlan plan,
            AnomalyPolicySnapshot policy,
            DiaryInteractionGroupDef group,
            Pawn writer,
            Pawn subject)
        {
            this.facts = facts;
            this.plan = plan;
            this.policy = policy ?? AnomalyPolicySnapshot.CreateDefault();
            this.group = group;
            this.writer = writer;
            this.subject = subject;
            string writerId = writer?.GetUniqueLoadID() ?? string.Empty;
            payload = new AnomalyEventData
            {
                PawnId = writerId,
                Tick = Math.Max(0, facts?.tick ?? 0),
                DefName = AnomalyEventDefNames.CreepJoinerOutcome,
                Kind = AnomalyKindTokens.CreepJoinerOutcome,
                SourceKey = plan?.sourceKey ?? string.Empty,
                HasVerifiedSource = plan != null && plan.valid && plan.advanceArc,
                PlayerVisible = facts != null && facts.playerVisible,
                AlreadyRecorded = plan?.replaySuppressed == true,
                FirstWriterId = writerId,
                FirstWriterEligible = plan?.selectedWriter != null
                    && string.Equals(
                        plan.selectedWriter.pawnId, writerId, StringComparison.Ordinal)
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
                signalEnabled: ModsConfig.AnomalyActive && policy.creepJoinerEnabled,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload.DedupKey();

        public override int DedupWindowTicks => Math.Max(1, policy.creepJoinerOutcomeDedupTicks);

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (sink == null || writer == null || subject == null || facts == null || plan == null
                || group == null || decision != CaptureDecision.GenerateSolo) return;

            string label = string.IsNullOrWhiteSpace(group.label)
                ? "PawnDiary.Event.Anomaly.CreepJoiner.Label".Translate().Resolve()
                : group.LabelCap.Resolve();
            string text = FallbackText();
            Pawn otherPawn = ReferenceEquals(writer, subject) ? null : subject;
            CreatedEvent = CreateSoloEvent(
                sink,
                writer,
                otherPawn,
                payload.DefName,
                label,
                text,
                InteractionGroups.InstructionForGroup(group),
                CreepJoinerOutcomeContextFormatter.Format(facts, plan));
            if (CreatedEvent != null) sink.QueueSolo(CreatedEvent, DiaryEvent.InitiatorRole);
        }

        private string FallbackText()
        {
            if (plan.selectedWriter?.roleToken == AnomalyWitnessRoleTokens.Subject
                && plan.phase == AnomalyOutcomeTokens.Departed)
            {
                return "PawnDiary.Event.Anomaly.CreepJoiner.SubjectDeparted"
                    .Translate(writer.LabelShortCap).Resolve();
            }

            string resultKey = facts.aggressionFollowedRejection
                ? "PawnDiary.Event.Anomaly.CreepJoiner.Result.RejectedAggressive"
                : plan.phase == AnomalyOutcomeTokens.Rejected
                    ? "PawnDiary.Event.Anomaly.CreepJoiner.Result.Rejected"
                    : plan.phase == AnomalyOutcomeTokens.Aggressive
                        ? "PawnDiary.Event.Anomaly.CreepJoiner.Result.Aggressive"
                        : "PawnDiary.Event.Anomaly.CreepJoiner.Result.Departed";
            string result = resultKey.Translate().Resolve();
            return "PawnDiary.Event.Anomaly.CreepJoiner.Fallback"
                .Translate(writer.LabelShortCap, subject.LabelShortCap, result).Resolve();
        }
    }
}

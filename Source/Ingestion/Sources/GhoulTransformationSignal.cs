// Impure adapter for one verified A2.2 ghoul transformation. The pure policy has already proven the
// non-ghoul-to-ghoul transition and selected exact surgeon/subject roles; this class only applies
// XML/settings gates, localized fallback text, event creation, and queue handoff.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Creates one surgeon-first solo/pair event for a verified ghoul transformation.</summary>
    internal sealed class GhoulTransformationSignal : DiarySignal
    {
        private readonly GhoulTransformationFacts facts;
        private readonly GhoulTransformationPlan plan;
        private readonly AnomalyPolicySnapshot policy;
        private readonly DiaryInteractionGroupDef group;
        private readonly List<Pawn> writers;
        private readonly Pawn subject;
        private readonly Pawn surgeon;
        private readonly AnomalyEventData payload;

        /// <summary>Non-null only after the dedicated transformation event was actually created.</summary>
        internal DiaryEvent CreatedEvent { get; private set; }

        internal GhoulTransformationSignal(
            GhoulTransformationFacts facts,
            GhoulTransformationPlan plan,
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
                DefName = AnomalyEventDefNames.GhoulTransformation,
                Kind = AnomalyKindTokens.GhoulTransformation,
                SourceKey = plan?.sourceKey ?? string.Empty,
                HasVerifiedSource = GhoulTransformationPolicy.OwnsDidSurgery(plan),
                PlayerVisible = facts?.playerVisible == true,
                AlreadyRecorded = false,
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
                signalEnabled: ModsConfig.AnomalyActive && policy.ghoulTransformationEnabled,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload.DedupKey();

        public override int DedupWindowTicks => Math.Max(1, policy.taleOwnershipExpiryTicks);

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (sink == null || facts == null || plan == null || group == null
                || subject == null || surgeon == null || writers.Count == 0
                || (decision != CaptureDecision.GenerateSolo
                    && decision != CaptureDecision.GeneratePair)) return;
            string label = string.IsNullOrWhiteSpace(group.label)
                ? "PawnDiary.Event.Anomaly.Ghoul.Label".Translate().Resolve()
                : group.LabelCap.Resolve();
            string instruction = InteractionGroups.InstructionForGroup(group);
            string context = GhoulTransformationContextFormatter.Format(facts, plan);
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
                if (CreatedEvent != null)
                {
                    // The subject is no longer a normal colonist after vanilla returns. The pure plan
                    // already froze both exact writers' eligibility before that irreversible change,
                    // so restore the subject's reference without re-reading the new ghoul state.
                    sink.AddPreverifiedEventRef(writers[0], CreatedEvent.eventId, true);
                    sink.AddPreverifiedEventRef(writers[1], CreatedEvent.eventId, true);
                    sink.QueuePair(CreatedEvent);
                }
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
            if (CreatedEvent != null)
            {
                sink.AddPreverifiedEventRef(writer, CreatedEvent.eventId, true);
                sink.QueueSolo(CreatedEvent, DiaryEvent.InitiatorRole);
            }
        }

        private string FallbackFor(AnomalyWriterSelection selected)
        {
            return selected?.roleToken == AnomalyWitnessRoleTokens.Subject
                ? "PawnDiary.Event.Anomaly.Ghoul.SubjectFallback"
                    .Translate(subject.LabelShortCap, surgeon.LabelShortCap).Resolve()
                : "PawnDiary.Event.Anomaly.Ghoul.SurgeonFallback"
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

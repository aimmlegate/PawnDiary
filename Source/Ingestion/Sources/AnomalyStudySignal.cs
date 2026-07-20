// Impure adapter for one already-proven Anomaly study milestone. DlcContext supplied detached,
// event-time facts and the pure policy chose the stage; this class only applies settings/catalog
// gates, localized fallback text, event creation, and LLM transport for the exact researcher.
using System;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Creates one solo Anomaly study page owned by the exact successful researcher.</summary>
    internal sealed class AnomalyStudySignal : DiarySignal
    {
        private readonly Pawn studier;
        private readonly AnomalyStudyFacts facts;
        private readonly AnomalyStudyPlan plan;
        private readonly AnomalyPolicySnapshot policy;
        private readonly DiaryInteractionGroupDef group;
        private readonly AnomalyEventData payload;

        /// <summary>Non-null only after the canonical dedicated page was actually created.</summary>
        internal DiaryEvent CreatedEvent { get; private set; }

        /// <summary>Copies detached study evidence into the common Anomaly catalog envelope.</summary>
        public AnomalyStudySignal(
            Pawn studier,
            AnomalyStudyFacts facts,
            AnomalyStudyPlan plan,
            AnomalyPolicySnapshot policy,
            DiaryInteractionGroupDef group)
        {
            this.studier = studier;
            this.facts = facts;
            this.plan = plan;
            this.policy = policy ?? AnomalyPolicySnapshot.CreateDefault();
            this.group = group;
            payload = new AnomalyEventData
            {
                PawnId = facts?.studierPawnId ?? string.Empty,
                Tick = Math.Max(0, facts?.tick ?? 0),
                DefName = AnomalyEventDefNames.StudyBreakthrough,
                Kind = AnomalyKindTokens.StudyBreakthrough,
                SourceKey = AnomalyStudyContextFormatter.SourceKey(facts, plan),
                HasVerifiedSource = facts != null && facts.noteThresholdsCrossed > 0,
                PlayerVisible = true,
                AlreadyRecorded = false,
                FirstWriterId = facts?.studierPawnId ?? string.Empty,
                FirstWriterEligible = facts != null && facts.studierEligible
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
                signalEnabled: ModsConfig.AnomalyActive && policy.studyEnabled,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => payload.DedupKey();

        public override int DedupWindowTicks => Math.Max(1, policy.studyTaleSuppressionTicks);

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (sink == null || studier == null || facts == null || plan == null || group == null
                || decision != CaptureDecision.GenerateSolo) return;

            string studiedLabel = string.IsNullOrWhiteSpace(facts.studiedLabel)
                ? facts.studiedDefName : facts.studiedLabel;
            string label = string.IsNullOrWhiteSpace(group.label)
                ? "PawnDiary.Event.Anomaly.Study.Label".Translate().Resolve()
                : group.LabelCap.Resolve();
            string text = "PawnDiary.Event.Anomaly.Study.Fallback"
                .Translate(studier.LabelShortCap, studiedLabel).Resolve();
            CreatedEvent = CreateSoloEvent(
                sink,
                studier,
                null,
                payload.DefName,
                label,
                text,
                InteractionGroups.InstructionForGroup(group),
                AnomalyStudyContextFormatter.FormatStudy(facts, plan));
            if (CreatedEvent != null) sink.QueueSolo(CreatedEvent, DiaryEvent.InitiatorRole);
        }
    }
}

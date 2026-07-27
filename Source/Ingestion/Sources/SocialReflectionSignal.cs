// Impure emit adapter for Quality Wave H7's delayed social reflection.
//
// The scheduler has already frozen the source excerpt and qualitative subject snapshot before it
// builds this signal. The ordinary dispatcher still owns the final catalog decision, dedup markers,
// event registration, and telemetry. Only the initiating pawn receives a diary page.
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>Creates and queues one delayed writer-only social-reflection event.</summary>
    internal sealed class SocialReflectionSignal : DiarySignal
    {
        private readonly Pawn writer;
        private readonly Pawn subject;
        private readonly string sourceKey;
        private readonly string gameContext;
        private readonly string colorCue;
        private readonly SocialReflectionEventData payload;

        public SocialReflectionSignal(
            Pawn writer,
            Pawn subject,
            string sourceKey,
            string gameContext,
            string colorCue,
            int eventTick,
            bool featureEnabled)
        {
            this.writer = writer;
            this.subject = subject;
            this.sourceKey = sourceKey ?? string.Empty;
            this.gameContext = gameContext ?? string.Empty;
            this.colorCue = colorCue ?? string.Empty;

            if (writer == null || subject == null || string.IsNullOrWhiteSpace(this.sourceKey))
            {
                return;
            }

            payload = new SocialReflectionEventData
            {
                PawnId = writer.GetUniqueLoadID(),
                Tick = eventTick,
                WriterPawnId = writer.GetUniqueLoadID(),
                SubjectPawnId = subject.GetUniqueLoadID(),
                SourceKey = this.sourceKey,
                WriterEligible = DiaryGameComponent.IsDiaryEligible(writer),
                SubjectEligible = DiaryGameComponent.IsDiaryEligible(subject),
                FeatureEnabled = featureEnabled
            };
        }

        /// <summary>The registered event, or null when the dispatcher rejected before persistence.</summary>
        public DiaryEvent CreatedEvent { get; private set; }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            bool bothEligible = payload != null
                && payload.WriterEligible
                && payload.SubjectEligible;
            return DiaryGameComponent.BuildCaptureContext(
                eligible: bothEligible,
                userEnabled: payload != null && payload.FeatureEnabled,
                signalEnabled: true,
                ambientSignalEnabled: true);
        }

        public override string DedupKey => string.IsNullOrWhiteSpace(sourceKey)
            ? string.Empty
            : "social-reflection|" + sourceKey;

        public override int DedupWindowTicks =>
            DiarySocialReflectionPolicy.Snapshot().pairCooldownTicks;

        /// <summary>Creates the current-tick solo page and queues only the initiating writer.</summary>
        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo || sink == null
                || writer == null || subject == null)
            {
                return;
            }

            string label = "PawnDiary.Event.SocialReflection.Label"
                .Translate(subject.LabelShort).Resolve();
            string text = "PawnDiary.Event.SocialReflection.Text"
                .Translate(writer.LabelShort, subject.LabelShort).Resolve();
            string instruction = "PawnDiary.Event.SocialReflection.Instruction"
                .Translate(writer.LabelShort, subject.LabelShort).Resolve();
            CreatedEvent = sink.AddSocialReflectionEvent(
                writer,
                subject,
                SocialReflectionEventData.DefNameToken,
                label,
                text,
                instruction,
                gameContext,
                colorCue);
            sink.QueueSolo(CreatedEvent, DiaryEvent.InitiatorRole);
        }
    }
}

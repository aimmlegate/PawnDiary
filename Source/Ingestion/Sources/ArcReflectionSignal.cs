// Arc-reflection ingestion signal. The component selects existing diary pages as memory evidence and
// this signal emits the finished life-chapter prompt through the same catalog dispatcher as other
// sources.
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Carries one selected pawn arc reflection into the shared dispatch path.
    /// </summary>
    public sealed class ArcReflectionSignal : DiarySignal
    {
        private readonly ArcReflectionEventData payload;
        private readonly Pawn pawn;
        private readonly string label;
        private readonly string text;
        private readonly string instruction;
        private readonly string gameContext;

        public ArcReflectionSignal(ArcReflectionEventData payload, Pawn pawn, string label,
            string text, string instruction, string gameContext)
        {
            this.payload = payload;
            this.pawn = pawn;
            this.label = label;
            this.text = text;
            this.instruction = instruction;
            this.gameContext = gameContext;
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: DiaryGameComponent.IsDiaryEligible(pawn),
                userEnabled: true,
                signalEnabled: DiaryTuning.Current.arcReflectionEnabled,
                ambientSignalEnabled: true);
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo)
            {
                return;
            }

            DiaryEvent diaryEvent = sink.AddSoloEvent(pawn, null, ArcReflectionEventData.DefNameToken,
                label, text, instruction, gameContext);
            sink.QueueSolo(diaryEvent, DiaryEvent.InitiatorRole);
        }
    }
}

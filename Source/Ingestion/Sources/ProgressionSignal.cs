// Progression ingestion signal — the impure emit half of the pawn progression scanner. The scanner
// observes live pawn state, updates small saved bookkeeping, then submits this signal when a future
// change should become an ordinary solo diary page.
using PawnDiary.Capture;
using Verse;

namespace PawnDiary.Ingestion
{
    /// <summary>
    /// Carries one already-detected progression moment through the shared catalog dispatcher.
    /// </summary>
    internal sealed class ProgressionSignal : DiarySignal
    {
        private readonly ProgressionEventData payload;
        private readonly Pawn pawn;
        private readonly string label;
        private readonly string text;
        private readonly string instruction;
        private readonly string gameContext;
        private readonly bool eligible;
        private readonly bool userEnabled;
        private readonly bool signalEnabled;

        public ProgressionSignal(ProgressionEventData payload, Pawn pawn, string label, string text,
            string instruction, string gameContext, bool eligible, bool userEnabled, bool signalEnabled)
        {
            this.payload = payload;
            this.pawn = pawn;
            this.label = label;
            this.text = text;
            this.instruction = instruction;
            this.gameContext = gameContext;
            this.eligible = eligible;
            this.userEnabled = userEnabled;
            this.signalEnabled = signalEnabled;
        }

        public override DiaryEventData Payload => payload;

        public override CaptureContext BuildContext()
        {
            return DiaryGameComponent.BuildCaptureContext(
                eligible: eligible,
                userEnabled: userEnabled,
                signalEnabled: signalEnabled,
                ambientSignalEnabled: true);
        }

        public override void Emit(DiaryGameComponent sink, CaptureDecision decision)
        {
            if (decision != CaptureDecision.GenerateSolo)
            {
                return;
            }

            DiaryEvent diaryEvent = sink.AddSoloEvent(pawn, null, payload.DefName,
                label, text, instruction, gameContext);
            sink.QueueSolo(diaryEvent, DiaryEvent.InitiatorRole);
        }
    }
}

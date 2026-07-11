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
        private readonly string dedupKey;
        private readonly int dedupWindowTicks;

        public ProgressionSignal(ProgressionEventData payload, Pawn pawn, string label, string text,
            string instruction, string gameContext, bool eligible, bool userEnabled, bool signalEnabled,
            string dedupKey = null, int dedupWindowTicks = 0)
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
            this.dedupKey = dedupKey;
            this.dedupWindowTicks = dedupWindowTicks;
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

        // Scalar progression kinds (skill, psylink, xenotype, royal title) emit at most one page per
        // pawn per scan, so they leave this empty and rely on the dispatcher's generic type+subject
        // safety key. The trait-gain scanner sets a per-trait key so that a pawn gaining more than one
        // trait in a single scan produces a distinct page for each instead of colliding on that generic
        // key. Empty (the default) keeps the pre-existing behavior for every other caller.
        public override string DedupKey => string.IsNullOrEmpty(dedupKey) ? string.Empty : dedupKey;

        public override int DedupWindowTicks => dedupWindowTicks;

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

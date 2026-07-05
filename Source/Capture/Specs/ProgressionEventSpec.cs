// Spec for Progression. Thin wrapper around the pure ProgressionEventData.Decide.
namespace PawnDiary.Capture
{
    internal class ProgressionEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.Progression;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return ProgressionEventData.Decide(data as ProgressionEventData, ctx);
        }
    }
}

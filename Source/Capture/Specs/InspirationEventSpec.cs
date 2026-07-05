// Spec for Inspiration events. Thin wrapper around the pure InspirationEventData.Decide.
namespace PawnDiary.Capture
{
    internal class InspirationEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.Inspiration;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return InspirationEventData.Decide(data as InspirationEventData, ctx);
        }
    }
}

// Spec for External (integration-API events). Thin wrapper around the pure ExternalEventData.Decide.
namespace PawnDiary.Capture
{
    public class ExternalEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.External;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return ExternalEventData.Decide(data as ExternalEventData, ctx);
        }
    }
}

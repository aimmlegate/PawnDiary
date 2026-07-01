// Spec for ArcReflection. Thin wrapper around the pure ArcReflectionEventData.Decide.
namespace PawnDiary.Capture
{
    public class ArcReflectionEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.ArcReflection;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return ArcReflectionEventData.Decide(data as ArcReflectionEventData, ctx);
        }
    }
}

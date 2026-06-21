// Spec for DayReflection. Thin wrapper around the pure DayReflectionEventData.Decide.
namespace PawnDiary.Capture
{
    public class DayReflectionEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.DayReflection;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return DayReflectionEventData.Decide(data as DayReflectionEventData, ctx);
        }
    }
}

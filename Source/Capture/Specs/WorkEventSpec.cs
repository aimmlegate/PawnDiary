// Spec for Work. Thin wrapper around the pure WorkEventData.Decide.
namespace PawnDiary.Capture
{
    public class WorkEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.Work;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return WorkEventData.Decide(data as WorkEventData, ctx);
        }
    }
}

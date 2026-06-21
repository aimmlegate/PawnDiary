// Spec for Arrival. Thin wrapper around the pure ArrivalEventData.Decide.
namespace PawnDiary.Capture
{
    public class ArrivalEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.Arrival;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return ArrivalEventData.Decide(data as ArrivalEventData, ctx);
        }
    }
}

// Spec for Hediff. Thin wrapper around the pure HediffEventData.Decide.
namespace PawnDiary.Capture
{
    public class HediffEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.Hediff;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return HediffEventData.Decide(data as HediffEventData, ctx);
        }
    }
}

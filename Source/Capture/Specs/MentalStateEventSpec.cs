// Spec for MentalState. Thin wrapper around the pure MentalStateEventData.Decide.
namespace PawnDiary.Capture
{
    internal class MentalStateEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.MentalState;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return MentalStateEventData.Decide(data as MentalStateEventData, ctx);
        }
    }
}

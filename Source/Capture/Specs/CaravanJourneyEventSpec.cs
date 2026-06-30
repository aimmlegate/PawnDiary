// Spec for player caravan departure/arrival diary moments.
namespace PawnDiary.Capture
{
    public class CaravanJourneyEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.CaravanJourney;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return CaravanJourneyEventData.Decide(data as CaravanJourneyEventData, ctx);
        }
    }
}

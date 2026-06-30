// Spec for significant completed trade/gift deals.
namespace PawnDiary.Capture
{
    public class TradeDealEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.TradeDeal;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return TradeDealEventData.Decide(data as TradeDealEventData, ctx);
        }
    }
}

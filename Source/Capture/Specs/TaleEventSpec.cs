// Spec for Tale. Thin wrapper around the pure TaleEventData.Decide.
namespace PawnDiary.Capture
{
    internal class TaleEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.Tale;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return TaleEventData.Decide(data as TaleEventData, ctx);
        }
    }
}

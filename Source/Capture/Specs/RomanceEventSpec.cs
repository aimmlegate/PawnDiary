// Spec for Romance. Thin wrapper around the pure RomanceEventData.Decide.
namespace PawnDiary.Capture
{
    internal class RomanceEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.Romance;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return RomanceEventData.Decide(data as RomanceEventData, ctx);
        }
    }
}

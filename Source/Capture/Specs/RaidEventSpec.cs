// Spec for Raid. Thin wrapper around the pure RaidEventData.Decide.
namespace PawnDiary.Capture
{
    internal class RaidEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.Raid;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return RaidEventData.Decide(data as RaidEventData, ctx);
        }
    }
}

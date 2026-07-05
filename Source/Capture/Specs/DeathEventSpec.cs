// Spec for Death fallback. Thin wrapper around the pure DeathEventData.Decide.
namespace PawnDiary.Capture
{
    internal class DeathEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.Death;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return DeathEventData.Decide(data as DeathEventData, ctx);
        }
    }
}

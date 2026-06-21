// Spec for Interaction. Thin wrapper around the pure InteractionEventData.Decide.
namespace PawnDiary.Capture
{
    public class InteractionEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.Interaction;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return InteractionEventData.Decide(data as InteractionEventData, ctx);
        }
    }
}

// Spec for ThoughtProgression. Thin wrapper around the pure ThoughtProgressionEventData.Decide.
namespace PawnDiary.Capture
{
    internal class ThoughtProgressionEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.ThoughtProgression;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return ThoughtProgressionEventData.Decide(data as ThoughtProgressionEventData, ctx);
        }
    }
}

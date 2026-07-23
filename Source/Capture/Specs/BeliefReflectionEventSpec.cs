// Catalog adapter for the pure belief-reflection decision.
namespace PawnDiary.Capture
{
    internal sealed class BeliefReflectionEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.BeliefReflection;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext context)
        {
            return BeliefReflectionEventData.Decide(data as BeliefReflectionEventData, context);
        }
    }
}

// Spec for Thought events. Thin wrapper around the pure ThoughtEventData.Decide — kept as its own
// class (not inlined into the catalog) because the Spec class is the future home for per-source
// metadata (weight, prompt-template key, RNG filters) once we need it.
namespace PawnDiary.Capture
{
    public class ThoughtEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.Thought;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return ThoughtEventData.Decide(data as ThoughtEventData, ctx);
        }
    }
}

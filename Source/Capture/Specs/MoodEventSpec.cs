// Spec for MoodEvent. Thin wrapper around the pure MoodEventData.Decide. The Spec class is the
// future home for per-source metadata (weight, prompt-template key, RNG filters) once we need it.
namespace PawnDiary.Capture
{
    public class MoodEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.MoodEvent;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return MoodEventData.Decide(data as MoodEventData, ctx);
        }
    }
}

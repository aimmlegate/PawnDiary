// Catalog spec for Quality Wave H7 delayed social reflections.
namespace PawnDiary.Capture
{
    /// <summary>Thin catalog wrapper around the pure social-reflection decision.</summary>
    internal sealed class SocialReflectionEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.SocialReflection;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext context)
        {
            return SocialReflectionEventData.Decide(data as SocialReflectionEventData, context);
        }
    }
}

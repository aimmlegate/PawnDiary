// Spec for ArtImmortalized (Quality Wave H6). Thin wrapper around the pure
// ArtImmortalizedEventData.Decide, mirroring MoodEventSpec.
namespace PawnDiary.Capture
{
    internal class ArtImmortalizedEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.ArtImmortalized;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return ArtImmortalizedEventData.Decide(data as ArtImmortalizedEventData, ctx);
        }
    }
}

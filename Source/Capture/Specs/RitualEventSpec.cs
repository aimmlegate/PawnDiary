// Spec for Ideology ritual completion entries. The live hook emits one RitualEventData per
// organizer/target/participant/spectator; all valid entries route to solo generation.
namespace PawnDiary.Capture
{
    public class RitualEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.Ritual;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return RitualEventData.Decide(data as RitualEventData, ctx);
        }
    }
}

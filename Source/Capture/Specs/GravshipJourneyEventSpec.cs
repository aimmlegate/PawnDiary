// Catalog Spec for Odyssey's canonical successful-landing event.
namespace PawnDiary.Capture
{
    /// <summary>Delegates GravshipJourney catalog decisions to its detached payload.</summary>
    internal sealed class GravshipJourneyEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.GravshipJourney;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext context)
        {
            return GravshipJourneyEventData.Decide(data as GravshipJourneyEventData, context);
        }
    }
}

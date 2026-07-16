// Catalog Spec for live canonical Biotech growth moments.
namespace PawnDiary.Capture
{
    internal class GrowthMomentEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.GrowthMoment;

        /// <summary>Delegates catalog policy to the typed inert growth payload.</summary>
        public override CaptureDecision Decide(DiaryEventData data, CaptureContext context)
        {
            return GrowthMomentEventData.Decide(data as GrowthMomentEventData, context);
        }
    }
}

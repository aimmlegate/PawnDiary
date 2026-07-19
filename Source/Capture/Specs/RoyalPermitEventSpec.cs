// Catalog Spec for successful dramatic Royalty permit pages.
namespace PawnDiary.Capture
{
    /// <summary>Delegates dramatic-permit page policy to its detached payload.</summary>
    internal sealed class RoyalPermitEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.RoyalPermit;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext context)
        {
            return RoyalPermitEventData.Decide(data as RoyalPermitEventData, context);
        }
    }
}

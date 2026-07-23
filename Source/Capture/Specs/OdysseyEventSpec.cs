// Catalog Spec for verified Odyssey source-owned events beyond gravship landing.
namespace PawnDiary.Capture
{
    /// <summary>Delegates Odyssey event decisions to the detached payload.</summary>
    internal sealed class OdysseyEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.OdysseyEvent;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext context)
        {
            return OdysseyEventData.Decide(data as OdysseyEventData, context);
        }
    }
}

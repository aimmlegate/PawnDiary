// Catalog Spec for source-owned Anomaly study, containment, reveal, transformation, and void pages.
namespace PawnDiary.Capture
{
    /// <summary>Delegates final Anomaly dispatch policy to its detached common envelope.</summary>
    internal sealed class AnomalyEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.AnomalyEvent;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext context)
        {
            return AnomalyEventData.Decide(data as AnomalyEventData, context);
        }
    }
}

// Catalog payload for Odyssey's one canonical successful-landing chapter. The live adapter has
// already applied novelty/cooldown policy and resolved at most two exact writers; this plain payload
// decides only whether the resulting DiaryEvent is solo, pair-shaped, or safely dropped.
namespace PawnDiary.Capture
{
    /// <summary>Primitive capture facts for one novelty-authorized gravship landing.</summary>
    internal sealed class GravshipJourneyEventData : DiaryEventData
    {
        public const string DefName = OdysseyEventDefNames.Landing;

        public override DiaryEventType EventType => DiaryEventType.GravshipJourney;

        public string JourneyId;
        public string ShipStableId;
        public int DepartureTick;
        public bool HasValidPlan;
        public bool WritePage;
        public bool AlreadyRecorded;
        public string FirstWriterId;
        public bool FirstWriterEligible;
        public string SecondWriterId;
        public bool SecondWriterEligible;

        /// <summary>Applies the final pure gates and chooses one canonical event shape.</summary>
        public static CaptureDecision Decide(GravshipJourneyEventData data, CaptureContext context)
        {
            if (data == null || context == null || !context.UserEnabled || !context.SignalEnabled
                || !data.HasValidPlan || !data.WritePage || data.AlreadyRecorded
                || string.IsNullOrWhiteSpace(data.JourneyId)
                || string.IsNullOrWhiteSpace(data.ShipStableId)
                || data.DepartureTick < 0)
            {
                return CaptureDecision.Drop;
            }

            bool first = data.FirstWriterEligible && !string.IsNullOrWhiteSpace(data.FirstWriterId);
            bool second = data.SecondWriterEligible && !string.IsNullOrWhiteSpace(data.SecondWriterId)
                && !string.Equals(data.FirstWriterId, data.SecondWriterId,
                    System.StringComparison.Ordinal);
            if (first && second) return CaptureDecision.GeneratePair;
            return first || second ? CaptureDecision.GenerateSolo : CaptureDecision.Drop;
        }

        /// <summary>Returns the frozen successful-landing owner key.</summary>
        public string DedupKey()
        {
            return OdysseyArcKeys.Landing(ShipStableId, DepartureTick);
        }
    }
}

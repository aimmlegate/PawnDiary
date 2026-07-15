// Catalog payload for one verified Biotech growth mutation. Exact before/after capture supplies the
// child; Phase-2 family policy may add one truthful supporter so pure capture can choose solo or pair.
namespace PawnDiary.Capture
{
    /// <summary>Plain catalog payload for the canonical age-7/10/13 growth event.</summary>
    internal class GrowthMomentEventData : DiaryEventData
    {
        public const string DefName = BiotechEventDefNames.GrowthMoment;

        public override DiaryEventType EventType => DiaryEventType.GrowthMoment;

        public string ChildId;
        public int Age;
        public bool ChildEligible;
        public string SupporterId;
        public bool SupporterEligible;
        public bool HasVerifiedMutation;
        public bool AlreadyRecorded;

        /// <summary>Applies package/user/signal truth gates and the pure growth writer-shape policy.</summary>
        public static CaptureDecision Decide(GrowthMomentEventData data, CaptureContext context)
        {
            if (data == null || context == null || !context.UserEnabled || !context.SignalEnabled
                || data.AlreadyRecorded || !data.HasVerifiedMutation
                || string.IsNullOrWhiteSpace(data.ChildId)
                || string.IsNullOrWhiteSpace(BiotechGrowthStageTokens.ForAge(data.Age)))
            {
                return CaptureDecision.Drop;
            }

            FamilySupportSelection supporter = data.SupporterEligible && !string.IsNullOrWhiteSpace(data.SupporterId)
                ? new FamilySupportSelection { adultId = data.SupporterId }
                : null;
            GrowthWriterShape shape = GrowthWriterPolicy.Decide(data.ChildId, data.ChildEligible, supporter);
            return shape == GrowthWriterShape.Pair
                ? CaptureDecision.GeneratePair
                : shape == GrowthWriterShape.ChildSolo || shape == GrowthWriterShape.SupporterSolo
                    ? CaptureDecision.GenerateSolo
                    : CaptureDecision.Drop;
        }

        /// <summary>Returns the frozen once-per-child-and-age correlation key.</summary>
        public string DedupKey()
        {
            return BiotechArcKeys.GrowthCorrelation(ChildId, Age);
        }
    }
}

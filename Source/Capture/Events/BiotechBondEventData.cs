// Catalog payload for one canonical Biotech psychic-bond formation or rupture. The recursive live
// owner constructs it only after the pair's reciprocal before/after state has been verified.
namespace PawnDiary.Capture
{
    /// <summary>Plain pair/solo catalog payload for a verified psychic-bond lifecycle transition.</summary>
    internal sealed class BiotechBondEventData : DiaryEventData
    {
        public override DiaryEventType EventType => DiaryEventType.BiotechBond;

        public string FirstPawnId;
        public string SecondPawnId;
        public string DefName;
        public int BondEpoch;
        public string Phase;
        public bool FirstPawnEligible;
        public bool SecondPawnEligible;
        public bool HasVerifiedTransition;

        public static CaptureDecision Decide(BiotechBondEventData data, CaptureContext context)
        {
            if (data == null || context == null || !context.UserEnabled || !context.SignalEnabled
                || !data.HasVerifiedTransition || !PsychicBondPhaseTokens.IsKnown(data.Phase)
                || data.BondEpoch < 1
                || PsychicBondPairPolicy.Create(data.FirstPawnId, data.SecondPawnId) == null)
            {
                return CaptureDecision.Drop;
            }

            if (data.FirstPawnEligible && data.SecondPawnEligible)
                return CaptureDecision.GeneratePair;
            if (data.FirstPawnEligible || data.SecondPawnEligible)
                return CaptureDecision.GenerateSolo;
            return CaptureDecision.Drop;
        }

        public string DedupKey()
        {
            PsychicBondPair pair = PsychicBondPairPolicy.Create(FirstPawnId, SecondPawnId);
            return pair == null || BondEpoch < 1 || !PsychicBondPhaseTokens.IsKnown(Phase)
                ? string.Empty
                : "biotech-bond|" + pair.Key + "|" + BondEpoch + "|" + Phase;
        }
    }
}

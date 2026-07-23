// Catalog Spec for verified Biotech psychic-bond formation and rupture.
namespace PawnDiary.Capture
{
    internal sealed class BiotechBondEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.BiotechBond;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext context)
        {
            return BiotechBondEventData.Decide(data as BiotechBondEventData, context);
        }
    }
}

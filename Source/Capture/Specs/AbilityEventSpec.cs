// Spec for successful pawn ability-use entries. The live hook handles RimWorld Ability details and
// random sampling; this Spec delegates the final pure decision to AbilityEventData.
namespace PawnDiary.Capture
{
    internal class AbilityEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.Ability;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return AbilityEventData.Decide(data as AbilityEventData, ctx);
        }
    }
}

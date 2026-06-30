// Spec for player-facing faction relation transitions.
namespace PawnDiary.Capture
{
    public class FactionRelationEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.FactionRelation;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return FactionRelationEventData.Decide(data as FactionRelationEventData, ctx);
        }
    }
}

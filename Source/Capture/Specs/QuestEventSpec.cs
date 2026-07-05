// Spec for Quest. Thin wrapper around the pure QuestEventData.Decide. One spec dispatches all
// three lifecycle signals (accepted/completed/failed); the signal field on the payload routes the
// prompt group, not the DiaryEventType.
namespace PawnDiary.Capture
{
    internal class QuestEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.Quest;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return QuestEventData.Decide(data as QuestEventData, ctx);
        }
    }
}

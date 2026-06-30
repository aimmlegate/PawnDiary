// Spec for skill milestone entries. The live hook snapshots old/new levels; the pure payload decides
// whether an XML milestone was actually crossed.
namespace PawnDiary.Capture
{
    public class SkillMilestoneEventSpec : DiaryEventSpec
    {
        public override DiaryEventType EventType => DiaryEventType.SkillMilestone;

        public override CaptureDecision Decide(DiaryEventData data, CaptureContext ctx)
        {
            return SkillMilestoneEventData.Decide(data as SkillMilestoneEventData, ctx);
        }
    }
}

// Payload + pure decision for a pawn crossing a skill-level milestone. The live SkillRecord.Learn
// hook supplies old/new levels and XML-owned milestone thresholds; this class keeps the threshold
// crossing and context format testable without RimWorld assemblies.
using System.Collections.Generic;
using System.Globalization;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one pawn skill level crossing an XML-configured milestone.
    /// </summary>
    public class SkillMilestoneEventData : DiaryEventData
    {
        public override DiaryEventType EventType => DiaryEventType.SkillMilestone;

        public string DefName;
        public string Label;
        public string Passion;
        public int OldLevel;
        public int NewLevel;
        public int MilestoneLevel;
        public List<int> MilestoneLevels;

        public static CaptureDecision Decide(SkillMilestoneEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.DefName))
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled || !ctx.SignalEnabled)
            {
                return CaptureDecision.Drop;
            }

            if (data.NewLevel <= data.OldLevel)
            {
                return CaptureDecision.Drop;
            }

            int milestone = data.MilestoneLevel > 0
                ? data.MilestoneLevel
                : CrossedMilestone(data.OldLevel, data.NewLevel, data.MilestoneLevels);
            return milestone > 0 ? CaptureDecision.GenerateSolo : CaptureDecision.Drop;
        }

        /// <summary>
        /// Returns the highest configured milestone crossed by a level increase, or 0 when none was
        /// crossed. The list may be unsorted because XML load order should not matter.
        /// </summary>
        public static int CrossedMilestone(int oldLevel, int newLevel, IList<int> milestones)
        {
            if (newLevel <= oldLevel || milestones == null || milestones.Count == 0)
            {
                return 0;
            }

            int crossed = 0;
            for (int i = 0; i < milestones.Count; i++)
            {
                int level = milestones[i];
                if (level > oldLevel && level <= newLevel && level > crossed)
                {
                    crossed = level;
                }
            }

            return crossed;
        }

        public string DedupKey()
        {
            return "skill_milestone|" + (PawnId ?? string.Empty) + "|" + (DefName ?? string.Empty)
                + "|" + MilestoneLevel.ToString(CultureInfo.InvariantCulture);
        }

        public static string BuildGameContext(
            string defName,
            string label,
            int oldLevel,
            int newLevel,
            int milestoneLevel,
            string passion)
        {
            return "skill_milestone=" + Clean(defName)
                + "; skill_label=" + Fallback(label, defName)
                + "; old_level=" + oldLevel.ToString(CultureInfo.InvariantCulture)
                + "; skill_level=" + newLevel.ToString(CultureInfo.InvariantCulture)
                + "; milestone_level=" + milestoneLevel.ToString(CultureInfo.InvariantCulture)
                + "; passion=" + Fallback(passion, "none");
        }

        private static string Fallback(string value, string fallback)
        {
            string clean = Clean(value);
            return string.IsNullOrWhiteSpace(clean) ? Clean(fallback) : clean;
        }

        private static string Clean(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}

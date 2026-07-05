// Payload + pure decision for sampled work diary events. RimWorld-specific work inspection, skill
// reads, cooldown scans, settings reads, and RNG rolls stay in DiaryGameComponent.Work; this payload
// records their primitive results so the catalog owns the final record/drop decision.
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one sampled current-work signal.
    /// </summary>
    internal class WorkEventData : DiaryEventData
    {
        public const string PassionDefName = "PawnDiary_WorkPassion";
        public const string StrainDefName = "PawnDiary_WorkStrain";
        public const string RoutineDefName = "PawnDiary_WorkRoutine";
        public const string DarkStudyDefName = "PawnDiary_WorkDarkStudy";

        public override DiaryEventType EventType => DiaryEventType.Work;

        public string DefName;
        public string WorkTypeDefName;
        public string WorkGiverDefName;
        public string MoodImpact;
        public bool HasCurrentWork;
        public bool IgnoredWorkType;
        public bool SameWorkCooldownClear;
        public bool PassedChanceRoll;
        public bool HasPassion;
        public bool HasLowSkill;
        public bool IsNegativeChore;
        public bool IsDarkStudy;

        public static CaptureDecision Decide(WorkEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.DefName))
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled || !ctx.SignalEnabled)
            {
                return CaptureDecision.Drop;
            }

            if (!data.HasCurrentWork || data.IgnoredWorkType || !data.SameWorkCooldownClear || !data.PassedChanceRoll)
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSolo;
        }

        public static string EventDefName(bool isDarkStudy, bool isPositive, bool isNegative)
        {
            if (isDarkStudy)
            {
                return DarkStudyDefName;
            }

            if (isPositive)
            {
                return PassionDefName;
            }

            return isNegative ? StrainDefName : RoutineDefName;
        }

        public static string BuildGameContext(
            string workTypeDefName, string workGiverDefName, string moodImpact,
            bool hasPassion, bool hasLowSkill, bool isNegativeChore, bool isDarkStudy)
        {
            List<string> parts = new List<string>
            {
                "work=" + (workTypeDefName ?? string.Empty),
                "work_giver=" + (workGiverDefName ?? string.Empty),
                "mood_impact=" + (moodImpact ?? string.Empty),
                "passion=" + (hasPassion ? "true" : "false"),
                "low_skill=" + (hasLowSkill ? "true" : "false"),
                "dumb_or_cleaning=" + (isNegativeChore ? "true" : "false"),
                "dark_study=" + (isDarkStudy ? "true" : "false")
            };

            return string.Join("; ", parts.ToArray());
        }
    }
}

// Pure progression policies. Runtime scanners pass simple observed levels and saved bookkeeping
// values; these helpers decide whether a new milestone page should emit and which value to persist.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Result of evaluating a milestone-style progression rule.
    /// </summary>
    internal class ProgressionMilestoneDecision
    {
        public int newHighestMilestone;
        public bool shouldEmit;
        public int milestoneToEmit;
    }

    /// <summary>
    /// Result of evaluating a bounded level increase such as psylink level.
    /// </summary>
    internal class ProgressionLevelDecision
    {
        public int newHighestLevel;
        public bool shouldEmit;
        public int levelToEmit;
    }

    /// <summary>
    /// Pure helpers for scanner bookkeeping. They know nothing about Pawn, SkillRecord, Hediff, or DLC.
    /// </summary>
    internal static class ProgressionMilestonePolicy
    {
        public static ProgressionMilestoneDecision EvaluateSkillMilestone(int currentLevel,
            bool hasPassion, IList<int> configuredMilestones, int previousRecordedMilestone,
            bool baselineMode)
        {
            ProgressionMilestoneDecision result = new ProgressionMilestoneDecision
            {
                newHighestMilestone = Math.Max(0, previousRecordedMilestone),
                shouldEmit = false,
                milestoneToEmit = 0
            };

            if (!hasPassion || configuredMilestones == null || configuredMilestones.Count == 0)
            {
                return result;
            }

            int reached = HighestReachedMilestone(currentLevel, configuredMilestones);
            if (reached <= 0)
            {
                return result;
            }

            if (baselineMode)
            {
                result.newHighestMilestone = Math.Max(result.newHighestMilestone, reached);
                return result;
            }

            if (reached > result.newHighestMilestone)
            {
                result.newHighestMilestone = reached;
                result.shouldEmit = true;
                result.milestoneToEmit = reached;
            }

            return result;
        }

        public static ProgressionLevelDecision EvaluateLevelIncrease(int currentLevel,
            int previousHighestLevel, bool baselineMode, int minimumLevel, int maximumLevel)
        {
            int min = Math.Max(1, minimumLevel);
            int max = Math.Max(min, maximumLevel);
            ProgressionLevelDecision result = new ProgressionLevelDecision
            {
                newHighestLevel = Clamp(previousHighestLevel, 0, max),
                shouldEmit = false,
                levelToEmit = 0
            };

            if (currentLevel <= 0)
            {
                return result;
            }

            int observed = Clamp(currentLevel, min, max);
            if (baselineMode)
            {
                result.newHighestLevel = Math.Max(result.newHighestLevel, observed);
                return result;
            }

            if (observed > result.newHighestLevel)
            {
                result.newHighestLevel = observed;
                result.shouldEmit = true;
                result.levelToEmit = observed;
            }

            return result;
        }

        private static int HighestReachedMilestone(int currentLevel, IList<int> milestones)
        {
            int reached = 0;
            for (int i = 0; i < milestones.Count; i++)
            {
                int milestone = milestones[i];
                if (milestone > 0 && milestone <= currentLevel && milestone > reached)
                {
                    reached = milestone;
                }
            }

            return reached;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}

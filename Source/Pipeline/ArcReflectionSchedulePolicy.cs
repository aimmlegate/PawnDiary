// Pure cadence policy for rare pawn life-arc reflections. Runtime code snapshots the saved schedule
// and current date into these plain contracts, then applies the returned decision.
using System;

namespace PawnDiary
{
    /// <summary>
    /// XML/default cadence knobs copied into the pure schedule evaluator.
    /// </summary>
    internal class ArcReflectionScheduleTuning
    {
        public bool enabled = true;
        public int maxEntriesPerYear = 2;
        public bool allowSecondMajorEntry = true;
        public int secondEntryMinGapDays = 30;
        public int forceAfterYearDay = 45;
        public int ticksPerDay = 60000;
    }

    /// <summary>
    /// Plain copy of the pawn's saved arc schedule state for one decision.
    /// </summary>
    internal class ArcReflectionScheduleSnapshot
    {
        public int lastArcEntryTick = -1;
        public int lastArcEntryYear = int.MinValue;
        public int arcEntriesThisYear;
        public int forcedArcYear = int.MinValue;
    }

    /// <summary>
    /// The schedule decision plus diagnostics for dev tooling.
    /// </summary>
    internal class ArcReflectionScheduleDecision
    {
        public bool allowed;
        public bool forced;
        public string blockReason;
        public int normalizedEntriesThisYear;
        public int maxAllowedThisYear;
    }

    /// <summary>
    /// Decides whether annual forced or major-event arc reflection cadence allows an entry.
    /// </summary>
    internal static class ArcReflectionSchedulePolicy
    {
        public static ArcReflectionScheduleDecision Evaluate(ArcReflectionScheduleSnapshot state,
            ArcReflectionScheduleTuning tuning, int currentTick, int currentYear, int dayOfYear,
            bool majorEventTrigger)
        {
            state = state ?? new ArcReflectionScheduleSnapshot();
            tuning = tuning ?? new ArcReflectionScheduleTuning();

            int normalizedEntries = state.lastArcEntryYear == currentYear
                ? Math.Max(0, state.arcEntriesThisYear)
                : 0;
            int maxAllowed = MaxAllowedThisYear(tuning);
            ArcReflectionScheduleDecision result = new ArcReflectionScheduleDecision
            {
                allowed = false,
                forced = false,
                blockReason = string.Empty,
                normalizedEntriesThisYear = normalizedEntries,
                maxAllowedThisYear = maxAllowed
            };

            if (!tuning.enabled)
            {
                result.blockReason = "disabled";
                return result;
            }

            if (normalizedEntries >= maxAllowed)
            {
                result.blockReason = "year_cap";
                return result;
            }

            if (normalizedEntries == 0 && dayOfYear >= Math.Max(0, tuning.forceAfterYearDay)
                && state.forcedArcYear != currentYear)
            {
                result.allowed = true;
                result.forced = true;
                return result;
            }

            if (!majorEventTrigger)
            {
                result.blockReason = "not_due";
                return result;
            }

            if (normalizedEntries == 0)
            {
                result.allowed = true;
                return result;
            }

            if (!tuning.allowSecondMajorEntry)
            {
                result.blockReason = "second_disabled";
                return result;
            }

            int gapTicks = Math.Max(0, tuning.secondEntryMinGapDays)
                * Math.Max(1, tuning.ticksPerDay);
            if (state.lastArcEntryTick >= 0 && currentTick - state.lastArcEntryTick < gapTicks)
            {
                result.blockReason = "gap";
                return result;
            }

            result.allowed = true;
            return result;
        }

        private static int MaxAllowedThisYear(ArcReflectionScheduleTuning tuning)
        {
            int configuredMax = Math.Max(1, tuning.maxEntriesPerYear);
            return tuning.allowSecondMajorEntry ? configuredMax : Math.Min(1, configuredMax);
        }
    }
}

// Pure scheduling rules for the periodic Royalty persona-bond reconciliation. Keeping the tick
// arithmetic here makes elapsed-time behavior testable without loading Verse or RimWorld.
using System;

namespace PawnDiary
{
    /// <summary>Calculates due checks and overflow-safe deadlines for Royalty reconciliation.</summary>
    internal static class RoyaltyReconciliationSchedule
    {
        internal const int MinimumCadenceTicks = 250;

        /// <summary>Returns whether the current game tick has reached an initialized deadline.</summary>
        public static bool IsDue(int now, long deadline)
        {
            // A zero/negative deadline is the intentional "run on the next eligible tick" sentinel.
            // Game ticks should not be negative, but normalizing test/modded input keeps this helper
            // deterministic without changing that sentinel contract.
            long normalizedNow = Math.Max(0L, (long)now);
            return deadline <= 0L || normalizedNow >= deadline;
        }

        /// <summary>Rebases one deadline from now while preventing Int32 tick overflow.</summary>
        public static long NextDeadline(int now, int configuredCadenceTicks)
        {
            long normalizedNow = Math.Max(0L, (long)now);
            long cadence = Math.Max(
                (long)MinimumCadenceTicks,
                (long)configuredCadenceTicks);
            return normalizedNow + cadence;
        }
    }
}

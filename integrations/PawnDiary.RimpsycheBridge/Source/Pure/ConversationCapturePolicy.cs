// PURE Tier-C admission/cooldown policy for charged Rimpsyche conversations.
//
// The Harmony edge supplies alignment, ticks, and pawn ids. This helper decides whether the signed
// alignment clears the XML threshold and whether the order-independent pair is outside its cooldown.
// Keeping the decisions here makes boundary cases testable without Harmony, Verse, or a running game.
using System;

namespace PawnDiaryRimpsyche.Pure
{
    /// <summary>Deterministic charged-conversation threshold and pair-cooldown helpers.</summary>
    public static class ConversationCapturePolicy
    {
        /// <summary>True only when finite |alignment| is strictly above the configured threshold.</summary>
        public static bool PassesAlignment(float alignment, float threshold)
        {
            if (float.IsNaN(alignment) || float.IsInfinity(alignment)
                || float.IsNaN(threshold) || float.IsInfinity(threshold))
            {
                return false;
            }

            float safeThreshold = Math.Max(0f, threshold);
            return Math.Abs(alignment) > safeThreshold;
        }

        /// <summary>
        /// True when no accepted event exists for the pair, the cooldown is disabled, or enough
        /// forward game time elapsed. A backwards clock never bypasses the gate.
        /// </summary>
        public static bool CooldownElapsed(int nowTick, bool hasLastAcceptedTick, int lastAcceptedTick, int cooldownTicks)
        {
            if (!hasLastAcceptedTick || cooldownTicks <= 0)
            {
                return true;
            }

            long elapsed = (long)nowTick - lastAcceptedTick;
            return elapsed >= cooldownTicks;
        }

        /// <summary>Combined threshold + cooldown decision used by the runtime hook.</summary>
        public static bool ShouldCapture(
            float alignment,
            float threshold,
            int nowTick,
            bool hasLastAcceptedTick,
            int lastAcceptedTick,
            int cooldownTicks)
        {
            return PassesAlignment(alignment, threshold)
                && CooldownElapsed(nowTick, hasLastAcceptedTick, lastAcceptedTick, cooldownTicks);
        }

        /// <summary>
        /// Stable order-independent pawn-pair key, or empty for missing/same ids. Load ids are opaque:
        /// ordinal comparison is deliberate and culture-independent.
        /// </summary>
        public static string PairKey(string firstPawnId, string secondPawnId)
        {
            string first = Clean(firstPawnId);
            string second = Clean(secondPawnId);
            if (first.Length == 0 || second.Length == 0
                || string.Equals(first, second, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return string.CompareOrdinal(first, second) < 0
                ? first + "|" + second
                : second + "|" + first;
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}

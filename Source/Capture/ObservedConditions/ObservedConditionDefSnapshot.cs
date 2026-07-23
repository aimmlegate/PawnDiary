// Observed conditions, part 2 of the pure layer: the plain projection of one DiaryObservedConditionDef
// that the pure policy needs. The real Def (Source/Defs/DiaryObservedConditionDef.cs) is an XML-backed
// Verse type, so the impure layer flattens just the timing/enabled facts into this DTO before calling
// the policy — keeping the policy free of Verse and unit-testable. New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary
{
    /// <summary>
    /// Timing policy for one observed condition, copied from XML before the pure diff runs. Debounce
    /// ticks are the only "time" the policy is allowed to use: they delay recording the start/end so a
    /// brief flicker in live state does not write a page. Ticks never decide *truth* — whether the
    /// condition is active is always answered by the latest live observation, not the clock.
    /// </summary>
    internal sealed class ObservedConditionDefSnapshot
    {
        public string conditionKey;

        // A new observation must persist this many ticks before its start is recorded; a missing
        // observation must stay missing this many ticks before its end is recorded. Both clamp to >= 0
        // (0 means "act on the first scan"). 60000 ticks is one RimWorld day.
        public int startDebounceTicks;
        public int endDebounceTicks;
        // Optional pollution/family/page policy. Defaults are deliberately neutral so every existing
        // observed-condition Def behaves exactly as it did before MapPollution existed.
        public float minPollutionFraction;
        public float maxPollutionFraction = -1f;
        public string exclusiveFamilyKey;
        public int severityRank;
        public int maxPagePawns;

        /// <summary>
        /// Builds a snapshot with both debounce values clamped to a safe non-negative range. The policy
        /// trusts these values directly, so clamping here keeps a stray negative XML value from making a
        /// condition start/end "in the past".
        /// </summary>
        public static ObservedConditionDefSnapshot Create(string conditionKey, int startDebounceTicks,
            int endDebounceTicks, float minPollutionFraction = 0f, float maxPollutionFraction = -1f,
            string exclusiveFamilyKey = null, int severityRank = 0, int maxPagePawns = 0)
        {
            return new ObservedConditionDefSnapshot
            {
                conditionKey = conditionKey,
                startDebounceTicks = startDebounceTicks < 0 ? 0 : startDebounceTicks,
                endDebounceTicks = endDebounceTicks < 0 ? 0 : endDebounceTicks,
                minPollutionFraction = Clamp01(minPollutionFraction),
                maxPollutionFraction = maxPollutionFraction < 0f ? -1f : Clamp01(maxPollutionFraction),
                exclusiveFamilyKey = (exclusiveFamilyKey ?? string.Empty).Trim(),
                severityRank = severityRank < 0 ? 0 : severityRank,
                maxPagePawns = maxPagePawns < 0 ? 0 : maxPagePawns
            };
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            return value < 0f ? 0f : value > 1f ? 1f : value;
        }
    }
}

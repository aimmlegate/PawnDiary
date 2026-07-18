// Pure exact string routing for Royalty mutation owners. All identifiers come from XML-owned lists;
// there are no live Def references, localized-label comparisons, or DLC assembly reads here.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Maps exact ritual/item defName strings to stable mutation causes.</summary>
    internal static class RoyalMutationRoutePolicy
    {
        public static string RitualCause(string ritualDefName, RoyaltyPolicySnapshot policy)
        {
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            if (Contains(effective.bestowingRitualDefNames, ritualDefName))
                return RoyalMutationCauseTokens.ImperialBestowing;
            if (Contains(effective.animaRitualDefNames, ritualDefName))
                return RoyalMutationCauseTokens.AnimaLinking;
            return RoyalMutationCauseTokens.Unknown;
        }

        public static bool IsNeuroformer(string thingDefName, RoyaltyPolicySnapshot policy)
        {
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            return Contains(effective.neuroformerThingDefNames, thingDefName);
        }

        private static bool Contains(IList<string> values, string candidate)
        {
            string expected = (candidate ?? string.Empty).Trim();
            if (values == null || expected.Length == 0) return false;
            for (int i = 0; i < values.Count; i++)
                if (string.Equals((values[i] ?? string.Empty).Trim(), expected,
                    StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}

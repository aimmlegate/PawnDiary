// Pure exact-match ownership policy. XML identifies generic ability routes whose later visible
// interaction or ritual already owns the canonical diary page. No substring or doctrine inference.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Determines whether an ability route is demonstrably redundant downstream.</summary>
    internal static class BeliefCanonicalEventOwnershipPolicy
    {
        /// <summary>Matches one ability against the XML exact-name set only when Ideology is active.</summary>
        public static bool IsDownstreamCoveredAbility(
            string abilityDefName,
            bool ideologyActive,
            IReadOnlyList<string> exactAbilityDefNames)
        {
            if (!ideologyActive || string.IsNullOrWhiteSpace(abilityDefName)
                || exactAbilityDefNames == null) return false;
            string wanted = abilityDefName.Trim();
            for (int i = 0; i < exactAbilityDefNames.Count; i++)
            {
                string candidate = exactAbilityDefNames[i];
                if (!string.IsNullOrWhiteSpace(candidate)
                    && string.Equals(wanted, candidate.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}

// Pure exact-match ownership policy. XML identifies generic source routes whose later visible event
// owns the canonical diary page and names that downstream settings group. No substring or doctrine
// inference occurs here.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Finds the effective downstream group which may own one generic source route.</summary>
    internal static class BeliefCanonicalEventOwnershipPolicy
    {
        /// <summary>
        /// Returns one downstream group for an exact source-domain/Def-name match. The caller still
        /// must verify that group is effectively enabled before it suppresses the generic source.
        /// </summary>
        public static string DownstreamGroupFor(
            string sourceDomain,
            string sourceDefName,
            bool ideologyActive,
            bool policyEnabled,
            IReadOnlyList<BeliefCanonicalEventOwnershipRule> rules)
        {
            if (!ideologyActive || !policyEnabled || string.IsNullOrWhiteSpace(sourceDomain)
                || string.IsNullOrWhiteSpace(sourceDefName) || rules == null) return string.Empty;
            string wantedDomain = sourceDomain.Trim();
            string wantedDefName = sourceDefName.Trim();
            for (int i = 0; i < rules.Count; i++)
            {
                BeliefCanonicalEventOwnershipRule row = rules[i];
                if (row != null
                    && string.Equals(wantedDomain, row.sourceDomain?.Trim(), StringComparison.Ordinal)
                    && string.Equals(wantedDefName, row.sourceDefName?.Trim(), StringComparison.Ordinal))
                    return row.downstreamGroupDefName?.Trim() ?? string.Empty;
            }
            return string.Empty;
        }
    }
}

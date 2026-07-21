// Pure helper for resolving a selected doctrine's currently-active typed thought correlations.
// The live Ideology adapter supplies only ThoughtDef identities; this policy never reads a Pawn,
// Precept, DefDatabase, language text, or DLC object.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Resolves mechanical valence from active thoughts belonging to selected stances.</summary>
    internal static class BeliefActiveThoughtPolicy
    {
        /// <summary>
        /// Returns positive/negative when an active ThoughtDef exactly matches a selected precept's
        /// projected correlation. Negative wins a conflict; no reliable match returns unknown.
        /// </summary>
        public static string ResolveValence(
            BeliefStanceResolution resolution,
            IEnumerable<string> activeThoughtDefNames)
        {
            HashSet<string> active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (activeThoughtDefNames != null)
            {
                foreach (string defName in activeThoughtDefNames)
                    if (!string.IsNullOrWhiteSpace(defName)) active.Add(defName.Trim());
            }
            if (active.Count == 0 || resolution?.stances == null)
                return BeliefValenceTokens.Unknown;

            bool positive = false;
            for (int i = 0; i < resolution.stances.Count; i++)
            {
                BeliefPreceptFact precept = resolution.stances[i]?.precept;
                if (precept?.correlations == null) continue;
                for (int j = 0; j < precept.correlations.Count; j++)
                {
                    BeliefCorrelationFact correlation = precept.correlations[j];
                    if (correlation == null
                        || !string.Equals(correlation.kind, BeliefCorrelationKindTokens.Thought,
                            StringComparison.OrdinalIgnoreCase)
                        || !active.Contains(correlation.defName ?? string.Empty))
                        continue;

                    string valence = BeliefValenceTokens.Normalize(correlation.valence);
                    if (valence == BeliefValenceTokens.Negative) return valence;
                    if (valence == BeliefValenceTokens.Positive) positive = true;
                }
            }
            return positive ? BeliefValenceTokens.Positive : BeliefValenceTokens.Unknown;
        }
    }
}

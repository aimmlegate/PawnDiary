// Pure selection policy for the quadrum reflection's "same season last year" callback.
//
// RimWorld adapters collect hot DiaryEvents and compact ArchivedDiaryEntries, then normalize both
// sources into the primitive DTO below. Keeping deduplication and max-weight selection here makes the
// archive/hot overlap behavior deterministic and testable without loading Verse or Unity.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// One normalized prior-year event that may become the quadrum callback line.
    /// </summary>
    internal sealed class QuadrumAnniversaryMemoryCandidate
    {
        public readonly string sourceIdentity;
        public readonly int tick;
        public readonly float weight;
        public readonly string evidenceLine;

        public QuadrumAnniversaryMemoryCandidate(
            string sourceIdentity,
            int tick,
            float weight,
            string evidenceLine)
        {
            this.sourceIdentity = sourceIdentity;
            this.tick = tick;
            this.weight = weight;
            this.evidenceLine = evidenceLine;
        }
    }

    /// <summary>
    /// Deduplicates hot/archive representations of the same event and selects one strongest memory.
    /// </summary>
    internal static class QuadrumAnniversaryMemoryPolicy
    {
        internal const int QuadrumsPerYear = 4;

        /// <summary>True once a complete previous year can contain the same quadrum.</summary>
        public static bool HasPreviousYear(int currentQuadrum)
        {
            return currentQuadrum >= QuadrumsPerYear;
        }

        /// <summary>Returns the matching quadrum one RimWorld year earlier.</summary>
        public static int PreviousYearQuadrum(int currentQuadrum)
        {
            return currentQuadrum - QuadrumsPerYear;
        }

        /// <summary>
        /// Builds an identity shared by a hot event and its compact archive row. Current saves use the
        /// event id; the structured fallback keeps legacy/corrupt rows deterministic without matching
        /// translated prose.
        /// </summary>
        public static string IdentityFor(string eventId, int tick, string defName, string povRole)
        {
            if (!string.IsNullOrWhiteSpace(eventId))
            {
                return eventId.Trim();
            }

            return "legacy|"
                + tick
                + "|"
                + NormalizeIdentityPart(defName)
                + "|"
                + NormalizeIdentityPart(povRole);
        }

        /// <summary>
        /// Returns one valid representative per stable source identity. When hot and archive rows
        /// overlap, the higher-weight/newer deterministic representative wins.
        /// </summary>
        public static List<QuadrumAnniversaryMemoryCandidate> Deduplicate(
            IReadOnlyList<QuadrumAnniversaryMemoryCandidate> candidates)
        {
            Dictionary<string, QuadrumAnniversaryMemoryCandidate> byIdentity =
                new Dictionary<string, QuadrumAnniversaryMemoryCandidate>(StringComparer.Ordinal);
            if (candidates == null)
            {
                return new List<QuadrumAnniversaryMemoryCandidate>();
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                QuadrumAnniversaryMemoryCandidate candidate = candidates[i];
                if (!IsValid(candidate))
                {
                    continue;
                }

                QuadrumAnniversaryMemoryCandidate current;
                if (!byIdentity.TryGetValue(candidate.sourceIdentity, out current)
                    || IsBetter(candidate, current))
                {
                    byIdentity[candidate.sourceIdentity] = candidate;
                }
            }

            return new List<QuadrumAnniversaryMemoryCandidate>(byIdentity.Values);
        }

        /// <summary>
        /// Selects the maximum-weight unique memory. Ties prefer the newer event, then stable ordinal
        /// identity/text ordering so source enumeration order cannot change a saved prompt.
        /// </summary>
        public static QuadrumAnniversaryMemoryCandidate SelectBest(
            IReadOnlyList<QuadrumAnniversaryMemoryCandidate> candidates)
        {
            List<QuadrumAnniversaryMemoryCandidate> unique = Deduplicate(candidates);
            QuadrumAnniversaryMemoryCandidate best = null;
            for (int i = 0; i < unique.Count; i++)
            {
                if (best == null || IsBetter(unique[i], best))
                {
                    best = unique[i];
                }
            }

            return best;
        }

        private static bool IsValid(QuadrumAnniversaryMemoryCandidate candidate)
        {
            return candidate != null
                && !string.IsNullOrWhiteSpace(candidate.sourceIdentity)
                && !string.IsNullOrWhiteSpace(candidate.evidenceLine)
                && !float.IsNaN(candidate.weight)
                && !float.IsInfinity(candidate.weight);
        }

        private static bool IsBetter(
            QuadrumAnniversaryMemoryCandidate candidate,
            QuadrumAnniversaryMemoryCandidate current)
        {
            int weightComparison = candidate.weight.CompareTo(current.weight);
            if (weightComparison != 0)
            {
                return weightComparison > 0;
            }

            if (candidate.tick != current.tick)
            {
                return candidate.tick > current.tick;
            }

            int identityComparison = string.CompareOrdinal(candidate.sourceIdentity, current.sourceIdentity);
            if (identityComparison != 0)
            {
                return identityComparison < 0;
            }

            return string.CompareOrdinal(candidate.evidenceLine, current.evidenceLine) < 0;
        }

        private static string NormalizeIdentityPart(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "unknown"
                : value.Trim().ToLowerInvariant();
        }
    }
}

// Pure normalization, equality, and deduplication for NarrativeReference. N1 will use these helpers
// when it persists per-POV evidence into hot diary rows and compact archive rows.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable" and "architecture barriers").
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Builds and compares prose-free continuity references without live game objects.</summary>
    internal static class NarrativeReferencePolicy
    {
        /// <summary>Creates the reference that lets a later event find this source-owned event again.</summary>
        public static NarrativeReference FromEvidence(NarrativeEvidence evidence)
        {
            if (evidence == null)
            {
                return new NarrativeReference();
            }

            return new NarrativeReference
            {
                facet = Normalize(evidence.facet),
                phase = Normalize(evidence.phase),
                subjectKind = Normalize(evidence.subjectKind),
                subjectId = Normalize(evidence.subjectId),
                arcKey = Normalize(evidence.arcKey),
                sourceEventId = Normalize(evidence.eventId),
                sourceTick = evidence.tick
            };
        }

        /// <summary>
        /// Full equality is ordinal and case-sensitive: IDs/arc keys are stable schema identities, not
        /// localized labels. This prevents a culture-sensitive comparison from merging separate arcs.
        /// </summary>
        public static bool AreEquivalent(NarrativeReference left, NarrativeReference right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return EqualsOrdinal(left.facet, right.facet)
                && EqualsOrdinal(left.phase, right.phase)
                && EqualsOrdinal(left.subjectKind, right.subjectKind)
                && EqualsOrdinal(left.subjectId, right.subjectId)
                && EqualsOrdinal(left.arcKey, right.arcKey)
                && EqualsOrdinal(left.sourceEventId, right.sourceEventId)
                && left.sourceTick == right.sourceTick;
        }

        /// <summary>Returns true only for two non-empty, exactly equal arc identities.</summary>
        public static bool SameArc(NarrativeReference left, NarrativeReference right)
        {
            return left != null && right != null && !string.IsNullOrEmpty(left.arcKey)
                && EqualsOrdinal(left.arcKey, right.arcKey);
        }

        /// <summary>Returns true only for two non-empty, exactly equal kind-and-ID subject identities.</summary>
        public static bool SameSubject(NarrativeReference left, NarrativeReference right)
        {
            return left != null && right != null
                && !string.IsNullOrEmpty(left.subjectKind) && !string.IsNullOrEmpty(left.subjectId)
                && EqualsOrdinal(left.subjectKind, right.subjectKind)
                && EqualsOrdinal(left.subjectId, right.subjectId);
        }

        /// <summary>Copies non-null unique references in source order; no live save/model mutation occurs.</summary>
        public static List<NarrativeReference> Unique(List<NarrativeReference> source)
        {
            List<NarrativeReference> result = new List<NarrativeReference>();
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                NarrativeReference current = source[i];
                if (current == null || ContainsEquivalent(result, current))
                {
                    continue;
                }

                result.Add(new NarrativeReference
                {
                    facet = Normalize(current.facet),
                    phase = Normalize(current.phase),
                    subjectKind = Normalize(current.subjectKind),
                    subjectId = Normalize(current.subjectId),
                    arcKey = Normalize(current.arcKey),
                    sourceEventId = Normalize(current.sourceEventId),
                    sourceTick = current.sourceTick
                });
            }

            return result;
        }

        private static bool ContainsEquivalent(List<NarrativeReference> values, NarrativeReference target)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (AreEquivalent(values[i], target))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EqualsOrdinal(string left, string right)
        {
            return string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}

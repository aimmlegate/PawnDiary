// Pure persistence boundaries for Narrative Continuity. Sources and save adapters use this module to
// copy only bounded, POV-safe narrative metadata into a DiaryEvent or compact archive row. It knows
// nothing about RimWorld, Verse, XML, live DLC state, or Scribe, which keeps corrupted/old-save input
// handling testable in NarrativeContinuityTests.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable" and "DLC-safety").
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Pure normalization and defensive caps for the small narrative payload that N1 persists. The
    /// caps are schema safety limits, not gameplay tuning: XML still chooses how much usable evidence
    /// the selector considers, while a malformed save can never turn one diary page into unbounded data.
    /// </summary>
    internal static class NarrativePersistencePolicy
    {
        // These match the N0 contract's documented per-POV / archive limits. They are defensive save
        // caps rather than player-facing policy, so corrupted XML or a hand-edited save cannot grow
        // the hot event or archive indefinitely.
        public const int HardEvidenceCap = 3;
        public const int HardReferenceCap = 3;
        public const int HardSelectedCandidateKeyCap = 12;
        private const int HardTopicTokenCap = 8;

        /// <summary>
        /// Copies explicit-knowledge evidence in source order, stamps missing event/POV identity from
        /// the authorized page, and drops hidden/invalid rows. A null knowledge value fails closed.
        /// </summary>
        public static List<NarrativeEvidence> NormalizeEvidence(
            IList<NarrativeEvidence> source,
            string fallbackEventId,
            int fallbackTick,
            string fallbackPawnId,
            string fallbackPovRole,
            int policyCap)
        {
            List<NarrativeEvidence> result = new List<NarrativeEvidence>();
            if (source == null)
            {
                return result;
            }

            int cap = Math.Min(HardEvidenceCap, Math.Max(0, policyCap));
            for (int i = 0; i < source.Count && result.Count < cap; i++)
            {
                NarrativeEvidence evidence = source[i];
                if (evidence == null
                    || evidence.pawnCanKnow != true
                    || !NarrativeFacetTokens.IsKnown(evidence.facet)
                    || !NarrativeSubjectKindTokens.IsKnownOrEmpty(evidence.subjectKind))
                {
                    continue;
                }

                result.Add(new NarrativeEvidence
                {
                    eventId = Fallback(evidence.eventId, fallbackEventId),
                    tick = evidence.tick > 0 ? evidence.tick : fallbackTick,
                    povPawnId = Fallback(evidence.povPawnId, fallbackPawnId),
                    povRole = Fallback(evidence.povRole, fallbackPovRole),
                    facet = Trim(evidence.facet),
                    phase = Trim(evidence.phase),
                    subjectKind = Trim(evidence.subjectKind),
                    subjectId = Trim(evidence.subjectId),
                    subjectLabel = Trim(evidence.subjectLabel),
                    arcKey = Trim(evidence.arcKey),
                    relatedEventId = Trim(evidence.relatedEventId),
                    beliefTopics = NormalizeTokens(evidence.beliefTopics, HardTopicTokenCap),
                    salience = NarrativeSalienceTokens.IsKnown(evidence.salience)
                        ? evidence.salience
                        : NarrativeSalienceTokens.Minor,
                    pawnCanKnow = true,
                    sourceDomain = Trim(evidence.sourceDomain),
                    sourceDefName = Trim(evidence.sourceDefName)
                });
            }

            return result;
        }

        /// <summary>
        /// Copies only unique, prose-free references in source order. Reference equality remains
        /// ordinal/case-sensitive through <see cref="NarrativeReferencePolicy"/>.
        /// </summary>
        public static List<NarrativeReference> NormalizeReferences(IList<NarrativeReference> source)
        {
            List<NarrativeReference> copied = new List<NarrativeReference>();
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    if (source[i] != null)
                    {
                        copied.Add(source[i]);
                    }
                }
            }

            List<NarrativeReference> unique = NarrativeReferencePolicy.Unique(copied);
            if (unique.Count > HardReferenceCap)
            {
                unique.RemoveRange(HardReferenceCap, unique.Count - HardReferenceCap);
            }

            return unique;
        }

        /// <summary>
        /// Returns unique, nonblank selected-candidate keys in newest/source order, capped for save and
        /// archive safety. Keys retain ordinal spelling because case-distinct arc grammar is meaningful.
        /// </summary>
        public static List<string> NormalizeSelectedCandidateKeys(IList<string> source, int cap = HardSelectedCandidateKeyCap)
        {
            List<string> result = new List<string>();
            if (source == null)
            {
                return result;
            }

            int boundedCap = Math.Min(HardSelectedCandidateKeyCap, Math.Max(0, cap));
            for (int i = 0; i < source.Count && result.Count < boundedCap; i++)
            {
                string key = Trim(source[i]);
                if (key.Length == 0 || ContainsOrdinal(result, key))
                {
                    continue;
                }

                result.Add(key);
            }

            return result;
        }

        /// <summary>
        /// Clamps the XML recent-key request before a runtime store scan begins. The selector cannot
        /// consume more than the saved-key hard cap, so collecting additional rows would only allocate
        /// and scan data that normalization immediately discards.
        /// </summary>
        public static int RecentSelectionKeyScanCap(int configuredCap)
        {
            return Math.Min(HardSelectedCandidateKeyCap, Math.Max(1, configuredCap));
        }

        /// <summary>Builds the stable exact-subject index token, or empty when either identity part is absent.</summary>
        public static string SubjectIndexKey(string subjectKind, string subjectId)
        {
            string kind = Trim(subjectKind);
            string id = Trim(subjectId);
            return kind.Length == 0 || id.Length == 0 ? string.Empty : kind + "|" + id;
        }

        /// <summary>Copies the candidate keys selected by a pure selection result.</summary>
        public static List<string> SelectedCandidateKeys(NarrativeContextSelection selection)
        {
            List<string> keys = new List<string>();
            if (selection != null && selection.selectedCandidates != null)
            {
                for (int i = 0; i < selection.selectedCandidates.Count; i++)
                {
                    NarrativeLensCandidate candidate = selection.selectedCandidates[i];
                    keys.Add(candidate == null ? string.Empty : candidate.candidateKey);
                }
            }

            return NormalizeSelectedCandidateKeys(keys);
        }

        private static List<string> NormalizeTokens(IList<string> source, int cap)
        {
            List<string> result = new List<string>();
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count && result.Count < cap; i++)
            {
                string token = Trim(source[i]);
                if (token.Length > 0 && !ContainsOrdinal(result, token))
                {
                    result.Add(token);
                }
            }

            return result;
        }

        private static string Fallback(string value, string fallback)
        {
            string normalized = Trim(value);
            return normalized.Length == 0 ? Trim(fallback) : normalized;
        }

        private static string Trim(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static bool ContainsOrdinal(List<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

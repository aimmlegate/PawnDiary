// MemorySummaryFingerprint.cs — pure canonical fingerprinting for Summary payloads
// (design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md §T7.3).
//
// The canonical-facts fingerprint is SHA-256 over a versioned, length-prefixed, ordinal
// serialization of the reducer revision, ordered bucket keys, and every retained contribution's
// exact identity/tick/category/importance/value/flags/subject/provenance data. originChapterId is
// deliberately NOT an input (placement metadata); labels, wording, provider settings, usage
// counters, and suppression are excluded too. The projection fingerprint is the separate
// domain-prefixed variant over only enabled-category contributions plus their mask and revisions;
// THAT one validates cached optional LLM prose.
//
// Pure plain C#: no Verse/Unity types. Framing reuses OrdinalSegmentCodec so every segment has the
// shipped length-prefix grammar; hashes are BOM-less UTF-8 lowercase hexadecimal.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PawnDiary
{
    /// <summary>One detached contribution descriptor consumed by the §T7.3 fingerprint. Mirrors the
    /// saved SavedMemoryFactContribution fields the fingerprint is allowed to see.</summary>
    internal sealed class MemorySummaryFingerprintContribution
    {
        public string contributionId = string.Empty;
        public string originRecordId = string.Empty;
        public int originFactOrdinal = -1;
        public string originFactId = string.Empty;
        public long originalEventTick;
        public bool ageUnknown;
        public string category = string.Empty;
        public string importance = string.Empty;
        public string canonicalValue = string.Empty;
        public bool majorTurningPoint;
        public bool reversal;
        /// <summary>Exact ordered subject-ref IDs disclosed by this contribution.</summary>
        public List<string> subjectRefIds = new List<string>();
        /// <summary>Exact ordered provenance-ref IDs disclosed by this contribution.</summary>
        public List<string> provenanceRefIds = new List<string>();
    }

    /// <summary>Builds and validates the canonical Summary fingerprints.</summary>
    internal static class MemorySummaryFingerprint
    {
        // Code-owned stable schema domains (AGENTS.md: stable schema tokens stay C# constants).
        private const string CanonicalFactsDomain = "memory-summary-canonical-facts-v1";
        private const string ProjectionDomain = "memory-summary-projection-v1";

        private const int KnownCategoryMask =
            (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3);

        /// <summary>
        /// Creates the all-facts canonical fingerprint (§T7.3). Bucket keys must be distinct,
        /// nonblank, and supplied in their canonical ordinal order; contributions follow in their
        /// retained order. Any invalid input refuses the fingerprint instead of hashing garbage.
        /// </summary>
        public static bool TryCreateCanonicalFactsFingerprint(
            int reducerRevision,
            IReadOnlyList<string> orderedBucketKeys,
            IReadOnlyList<MemorySummaryFingerprintContribution> contributions,
            out string fingerprint)
        {
            List<string> fields;
            if (!TryBuildSharedFields(reducerRevision, CanonicalFactsDomain, contributions, out fields))
            {
                fingerprint = string.Empty;
                return false;
            }

            if (orderedBucketKeys == null)
            {
                fingerprint = string.Empty;
                return false;
            }

            HashSet<string> seenBuckets = new HashSet<string>(StringComparer.Ordinal);
            fields.Add(orderedBucketKeys.Count.ToString(CultureInfo.InvariantCulture));
            foreach (string bucketKey in orderedBucketKeys)
            {
                if (string.IsNullOrEmpty(bucketKey)
                    || bucketKey.Length > MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters
                    || !MemoryIdentityCodec.IsWellFormedUtf16(bucketKey)
                    || !seenBuckets.Add(bucketKey))
                {
                    fingerprint = string.Empty;
                    return false;
                }

                fields.Add(bucketKey);
            }

            return Finish(fields, out fingerprint);
        }

        /// <summary>
        /// Creates the domain-prefixed projection fingerprint over ONLY currently enabled-category
        /// contributions plus their exact category mask, reducer revision, and format revision
        /// (§T7.3). This — not the all-facts fingerprint alone — validates cached optional prose.
        /// Callers pass the already-filtered contribution list; a settings-only mask change with an
        /// unchanged filtered set yields the same fingerprint and creates no request.
        /// </summary>
        public static bool TryCreateProjectionFingerprint(
            int reducerRevision,
            long formatRevision,
            int categoryMask,
            IReadOnlyList<MemorySummaryFingerprintContribution> enabledContributions,
            out string fingerprint)
        {
            // Only the four known low bits may ever be set (§T6.0 category-mask rule).
            if ((categoryMask & ~KnownCategoryMask) != 0 || categoryMask < 0)
            {
                fingerprint = string.Empty;
                return false;
            }

            List<string> fields;
            if (!TryBuildSharedFields(
                    reducerRevision, ProjectionDomain, enabledContributions, out fields))
            {
                fingerprint = string.Empty;
                return false;
            }

            fields.Add(formatRevision.ToString(CultureInfo.InvariantCulture));
            fields.Add(categoryMask.ToString(CultureInfo.InvariantCulture));
            return Finish(fields, out fingerprint);
        }

        private static bool TryBuildSharedFields(
            int reducerRevision,
            string domain,
            IReadOnlyList<MemorySummaryFingerprintContribution> contributions,
            out List<string> fields)
        {
            fields = null;
            if (reducerRevision <= 0 || contributions == null)
            {
                return false;
            }

            fields = new List<string>
            {
                domain,
                reducerRevision.ToString(CultureInfo.InvariantCulture),
                contributions.Count.ToString(CultureInfo.InvariantCulture)
            };

            for (int index = 0; index < contributions.Count; index++)
            {
                MemorySummaryFingerprintContribution contribution = contributions[index];
                if (!IsValid(contribution))
                {
                    fields = null;
                    return false;
                }

                // The codec-derived identity must equal the supplied contributionId exactly; a
                // disagreement is a collision to repair, never a second identity (§T6.5).
                string expectedId;
                if (!MemoryIdentityCodec.TryCreateContributionId(
                        contribution.originRecordId,
                        contribution.originFactOrdinal,
                        contribution.originFactId,
                        out expectedId)
                    || !string.Equals(
                        contribution.contributionId,
                        expectedId,
                        StringComparison.Ordinal))
                {
                    fields = null;
                    return false;
                }

                fields.Add(contribution.contributionId);
                fields.Add(contribution.originRecordId);
                fields.Add(contribution.originFactOrdinal.ToString(CultureInfo.InvariantCulture));
                fields.Add(contribution.originFactId);
                fields.Add(contribution.ageUnknown
                    ? "1"
                    : "0");
                fields.Add(contribution.originalEventTick.ToString(CultureInfo.InvariantCulture));
                fields.Add(contribution.category);
                fields.Add(contribution.importance);
                fields.Add(contribution.canonicalValue);
                fields.Add(contribution.majorTurningPoint ? "1" : "0");
                fields.Add(contribution.reversal ? "1" : "0");

                if (!AddRefFields(fields, contribution.subjectRefIds)
                    || !AddRefFields(fields, contribution.provenanceRefIds))
                {
                    fields = null;
                    return false;
                }
            }

            return true;
        }

        private static bool AddRefFields(List<string> fields, List<string> refs)
        {
            if (refs == null)
            {
                return false;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            fields.Add(refs.Count.ToString(CultureInfo.InvariantCulture));
            foreach (string reference in refs)
            {
                if (string.IsNullOrEmpty(reference)
                    || reference.Length > MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters
                    || !MemoryIdentityCodec.IsWellFormedUtf16(reference)
                    || !seen.Add(reference))
                {
                    return false;
                }

                fields.Add(reference);
            }

            return true;
        }

        private static bool IsValid(MemorySummaryFingerprintContribution contribution)
        {
            return contribution != null
                && !string.IsNullOrWhiteSpace(contribution.originRecordId)
                && contribution.originRecordId.Length
                    <= MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters
                && contribution.originFactOrdinal >= 0
                && !string.IsNullOrWhiteSpace(contribution.originFactId)
                && contribution.originFactId.Length
                    <= MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters
                && !string.IsNullOrWhiteSpace(contribution.contributionId)
                && MemoryContractTokens.IsKnownCategory(contribution.category)
                && MemoryContractTokens.IsKnownImportance(contribution.importance)
                && contribution.canonicalValue != null
                && contribution.canonicalValue.Length
                    <= MemoryIdentityCodec.MaximumEmbeddedCompositeCharacters
                && MemoryIdentityCodec.IsWellFormedUtf16(contribution.canonicalValue)
                && (contribution.ageUnknown || contribution.originalEventTick >= 0);
        }

        private static bool Finish(List<string> fields, out string fingerprint)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string field in fields)
            {
                builder.Append(OrdinalSegmentCodec.Segment(field ?? string.Empty));
            }

            fingerprint = ComputeSha256Utf8(builder.ToString());
            return fingerprint.Length == 64;
        }

        private static string ComputeSha256Utf8(string value)
        {
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(value);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                StringBuilder result = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                {
                    result.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
                }

                return result.ToString();
            }
        }
    }
}

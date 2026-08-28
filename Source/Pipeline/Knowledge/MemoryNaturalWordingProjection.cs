// MemoryNaturalWordingProjection.cs — pure selection of disposable optional memory prose.
//
// Saved facts and deterministic wording remain canonical. Recall adapters detach the exact current
// projection plus the optional cache into this plain snapshot; the prompt renderer may use the cache
// only when every fingerprint/revision/category guard still agrees.
using System;

namespace PawnDiary
{
    /// <summary>Detached optional-prose guards for one current natural-writing projection.</summary>
    internal sealed class MemoryRecallNaturalWordingSnapshot
    {
        public string currentProjectionFingerprint = string.Empty;
        public long currentFormatRevision;
        public int currentCategoryMask;
        public string optionalWording = string.Empty;
        public string optionalFingerprint = string.Empty;
        public long optionalFormatRevision;
        public int optionalCategoryMask;
        public bool optionalSucceeded;
    }

    /// <summary>Selects bounded optional prose or the deterministic fallback without mutating either.</summary>
    internal static class MemoryNaturalWordingProjection
    {
        /// <summary>
        /// Defensive ceiling for one Event/Landmark display sentence. XML may tune downward, but a
        /// malformed override cannot turn optional provider prose into unbounded saved data.
        /// </summary>
        public const int MaximumBlockWordingCharacters = 240;

        /// <summary>Clamps the XML display limit to the fixed saved-data safety ceiling.</summary>
        public static int EffectiveBlockWordingMaximumCharacters(int configuredMaximum)
        {
            int positive = configuredMaximum > 0
                ? configuredMaximum
                : MaximumBlockWordingCharacters;
            return Math.Min(positive, MaximumBlockWordingCharacters);
        }

        /// <summary>
        /// Returns no prompt text for suppression. Otherwise optional prose wins only for an exact
        /// successful current projection; every absent, stale, failed, or malformed cache falls back.
        /// </summary>
        public static string Select(
            bool suppressed,
            string deterministicWording,
            MemoryRecallNaturalWordingSnapshot wording,
            int maximumOptionalCharacters)
        {
            if (suppressed) return string.Empty;
            string fallback = deterministicWording ?? string.Empty;
            string normalized;
            return wording != null
                    && wording.optionalSucceeded
                    && string.Equals(
                        wording.optionalFingerprint,
                        wording.currentProjectionFingerprint,
                        StringComparison.Ordinal)
                    && wording.optionalFormatRevision == wording.currentFormatRevision
                    && wording.optionalCategoryMask == wording.currentCategoryMask
                    && TryNormalizeOptionalWording(
                        wording.optionalWording,
                        maximumOptionalCharacters,
                        out normalized)
                ? normalized
                : fallback;
        }

        /// <summary>Copies the detached guards so frozen selection never shares mutable DTO state.</summary>
        public static MemoryRecallNaturalWordingSnapshot Copy(
            MemoryRecallNaturalWordingSnapshot source)
        {
            return source == null ? null : new MemoryRecallNaturalWordingSnapshot
            {
                currentProjectionFingerprint = source.currentProjectionFingerprint ?? string.Empty,
                currentFormatRevision = source.currentFormatRevision,
                currentCategoryMask = source.currentCategoryMask,
                optionalWording = source.optionalWording ?? string.Empty,
                optionalFingerprint = source.optionalFingerprint ?? string.Empty,
                optionalFormatRevision = source.optionalFormatRevision,
                optionalCategoryMask = source.optionalCategoryMask,
                optionalSucceeded = source.optionalSucceeded
            };
        }

        /// <summary>Accepts one bounded, well-formed paragraph with no control-line injection.</summary>
        public static bool TryNormalizeOptionalWording(
            string value,
            int maximumCharacters,
            out string normalized)
        {
            normalized = string.Empty;
            if (maximumCharacters <= 0 || string.IsNullOrWhiteSpace(value)) return false;
            string candidate = value.Trim();
            if (candidate.Length == 0 || candidate.Length > maximumCharacters
                || !MemoryIdentityCodec.IsWellFormedUtf16(candidate)) return false;
            for (int index = 0; index < candidate.Length; index++)
            {
                char current = candidate[index];
                if (current == '\r' || current == '\n' || current == '\0'
                    || (char.IsControl(current) && current != '\t')) return false;
            }
            normalized = candidate;
            return true;
        }
    }
}

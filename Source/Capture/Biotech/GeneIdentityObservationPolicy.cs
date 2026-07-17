// Pure normalization and silent-baseline policy for Biotech Phase 5 gene observation. The live
// adapter supplies a detached GeneIdentitySnapshot; the saved model supplies only primitive fields.
// No Pawn, Gene, Def, Verse, settings, or Scribe type belongs here.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary.Capture
{
    /// <summary>Builds bounded, canonical gene-membership baselines without emitting an event.</summary>
    internal static class GeneIdentityObservationPolicy
    {
        // Version 2 adds an explicit incomplete-membership marker. Version-1 rows are silently
        // rebaselined because their configured alphabetical cap could make a new gene look like a
        // simultaneous removal when it displaced the last saved name.
        public const int CurrentVersion = 2;
        public const int HardMaximumGeneDefNames = 2048;

        /// <summary>
        /// Copies the current xenotype and bounded installed-gene membership into a versioned
        /// observation row. Empty membership is valid and still receives the version marker.
        /// </summary>
        public static GeneIdentityObservationSnapshot Observe(
            GeneIdentitySnapshot identity,
            int maximumGeneDefNames,
            int labelCharacterLimit)
        {
            GeneIdentityObservationSnapshot observed = new GeneIdentityObservationSnapshot
            {
                observationVersion = CurrentVersion,
                xenotypeDefName = CleanOneLine(identity == null ? null : identity.xenotypeDefName),
                xenotypeLabel = Limit(
                    CleanOneLine(identity == null ? null : identity.xenotypeLabel),
                    labelCharacterLimit)
            };

            if (identity?.installedGeneDefNames != null)
            {
                for (int i = 0; i < identity.installedGeneDefNames.Count; i++)
                {
                    string defName = CleanOneLine(identity.installedGeneDefNames[i]);
                    if (defName.Length > 0) observed.geneDefNames.Add(defName);
                }
            }

            int cap = Clamp(maximumGeneDefNames, 1, HardMaximumGeneDefNames);
            observed.membershipTruncated = identity?.installedMembershipTruncated == true
                || DistinctCleanCount(observed.geneDefNames) > cap;
            observed.geneDefNames = Canonicalize(observed.geneDefNames, maximumGeneDefNames);
            return observed;
        }

        /// <summary>Repairs a loaded/corrupt row without upgrading its version or inventing state.</summary>
        public static GeneIdentityObservationSnapshot Normalize(
            GeneIdentityObservationSnapshot state,
            int maximumGeneDefNames,
            int labelCharacterLimit)
        {
            int cap = Clamp(maximumGeneDefNames, 1, HardMaximumGeneDefNames);
            GeneIdentityObservationSnapshot normalized = new GeneIdentityObservationSnapshot
            {
                observationVersion = Math.Max(0, state?.observationVersion ?? 0),
                xenotypeDefName = CleanOneLine(state?.xenotypeDefName),
                xenotypeLabel = Limit(CleanOneLine(state?.xenotypeLabel), labelCharacterLimit),
                geneDefNames = Canonicalize(state?.geneDefNames, maximumGeneDefNames),
                membershipTruncated = state?.membershipTruncated == true
                    || DistinctCleanCount(state?.geneDefNames) > cap
            };
            return normalized;
        }

        /// <summary>True only after the current schema has captured a real empty-or-nonempty baseline.</summary>
        public static bool HasCurrentBaseline(GeneIdentityObservationSnapshot state)
        {
            return state != null && state.observationVersion >= CurrentVersion;
        }

        private static List<string> Canonicalize(List<string> values, int maximumGeneDefNames)
        {
            int cap = Clamp(maximumGeneDefNames, 1, HardMaximumGeneDefNames);
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    string value = CleanOneLine(values[i]);
                    if (value.Length > 0 && seen.Add(value)) result.Add(value);
                }
            }

            result.Sort(CompareStableIds);
            if (result.Count > cap) result.RemoveRange(cap, result.Count - cap);
            return result;
        }

        private static int DistinctCleanCount(List<string> values)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (values == null) return 0;
            for (int i = 0; i < values.Count; i++)
            {
                string value = CleanOneLine(values[i]);
                if (value.Length > 0) seen.Add(value);
            }
            return seen.Count;
        }

        private static int CompareStableIds(string left, string right)
        {
            int insensitive = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            return insensitive != 0 ? insensitive : string.CompareOrdinal(left, right);
        }

        private static string CleanOneLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            StringBuilder result = new StringBuilder(value.Length);
            bool priorWhitespace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (char.IsControl(character) || char.IsWhiteSpace(character))
                {
                    if (!priorWhitespace && result.Length > 0) result.Append(' ');
                    priorWhitespace = true;
                }
                else
                {
                    result.Append(character);
                    priorWhitespace = false;
                }
            }
            return result.ToString().Trim();
        }

        private static string Limit(string value, int requestedLimit)
        {
            int limit = Clamp(requestedLimit, 1, GeneSaliencePolicySnapshot.HardMaximumTextCharacters);
            return value.Length <= limit ? value : value.Substring(0, limit).TrimEnd();
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum) return minimum;
            return value > maximum ? maximum : value;
        }
    }
}

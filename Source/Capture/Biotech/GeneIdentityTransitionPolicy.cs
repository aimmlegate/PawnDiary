// Pure before/after gene-identity comparison and event-context formatting for Biotech Phase 5.
// Live Gene/Pawn access stays in DlcContext; this file receives detached snapshots only.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary.Capture
{
    /// <summary>Computes exact membership deltas and bounded prompt themes without game state.</summary>
    internal static class GeneIdentityTransitionPolicy
    {
        /// <summary>Compares two detached snapshots without mutating either source.</summary>
        public static GeneIdentityTransitionDecision Evaluate(
            GeneIdentitySnapshot before,
            GeneIdentitySnapshot after,
            GeneSaliencePolicySnapshot policy)
        {
            GeneIdentitySnapshot safeBefore = before ?? new GeneIdentitySnapshot();
            GeneIdentitySnapshot safeAfter = after ?? new GeneIdentitySnapshot();
            HashSet<string> beforeNames = Membership(safeBefore.installedGeneDefNames);
            HashSet<string> afterNames = Membership(safeAfter.installedGeneDefNames);
            Dictionary<string, GeneFact> beforeFacts = Facts(safeBefore.genes);
            Dictionary<string, GeneFact> afterFacts = Facts(safeAfter.genes);
            GeneIdentityTransitionDecision decision = new GeneIdentityTransitionDecision
            {
                xenotypeIdentityChanged = IdentityChanged(safeBefore, safeAfter)
            };

            foreach (string defName in afterNames)
            {
                if (beforeNames.Contains(defName)) continue;
                decision.addedGeneCount++;
                GeneFact fact;
                if (afterFacts.TryGetValue(defName, out fact)) decision.mutation.addedGenes.Add(fact);
            }
            foreach (string defName in beforeNames)
            {
                if (afterNames.Contains(defName)) continue;
                decision.removedGeneCount++;
                GeneFact fact;
                if (beforeFacts.TryGetValue(defName, out fact)) decision.mutation.removedGenes.Add(fact);
            }

            decision.mutation.addedGenes.Sort(CompareFacts);
            decision.mutation.removedGenes.Sort(CompareFacts);
            decision.themes = GeneSaliencePolicy.Select(safeAfter, decision.mutation, policy);
            return decision;
        }

        /// <summary>
        /// Fallback emits every stable xenotype transition, but membership-only churn must meet the
        /// XML-owned minimum. Exact hooks use <see cref="GeneIdentityTransitionDecision.HasAnyChange"/>.
        /// </summary>
        public static bool ShouldEmitFallback(
            GeneIdentityTransitionDecision decision,
            int minimumMembershipChanges)
        {
            if (decision == null || !decision.HasAnyChange) return false;
            if (decision.xenotypeIdentityChanged) return true;
            int minimum = Math.Max(1, minimumMembershipChanges);
            return decision.addedGeneCount + decision.removedGeneCount >= minimum;
        }

        /// <summary>Recreates the saved baseline as a detached snapshot for fallback comparison.</summary>
        public static GeneIdentitySnapshot FromObservation(GeneIdentityObservationSnapshot observation)
        {
            GeneIdentitySnapshot snapshot = new GeneIdentitySnapshot
            {
                xenotypeDefName = observation?.xenotypeDefName ?? string.Empty,
                xenotypeLabel = observation?.xenotypeLabel ?? string.Empty
            };
            if (observation?.geneDefNames != null)
            {
                snapshot.installedGeneDefNames.AddRange(observation.geneDefNames);
            }
            return snapshot;
        }

        private static bool IdentityChanged(GeneIdentitySnapshot before, GeneIdentitySnapshot after)
        {
            string beforeDef = Clean(before.xenotypeDefName);
            string afterDef = Clean(after.xenotypeDefName);
            if (beforeDef.Length > 0 || afterDef.Length > 0)
            {
                // Localized Def labels can change when the player switches language, so a stable Def
                // identity wins whenever one exists. Custom xenogerms are still detected by membership.
                return !string.Equals(beforeDef, afterDef, StringComparison.OrdinalIgnoreCase);
            }
            return !string.Equals(Clean(before.xenotypeLabel), Clean(after.xenotypeLabel),
                StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> Membership(List<string> values)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (values == null) return result;
            for (int i = 0; i < values.Count; i++)
            {
                string value = Clean(values[i]);
                if (value.Length > 0) result.Add(value);
            }
            return result;
        }

        private static Dictionary<string, GeneFact> Facts(List<GeneFact> values)
        {
            Dictionary<string, GeneFact> result = new Dictionary<string, GeneFact>(
                StringComparer.OrdinalIgnoreCase);
            if (values == null) return result;
            for (int i = 0; i < values.Count; i++)
            {
                GeneFact fact = values[i];
                string defName = Clean(fact?.defName);
                if (defName.Length > 0 && !result.ContainsKey(defName)) result.Add(defName, fact);
            }
            return result;
        }

        private static int CompareFacts(GeneFact left, GeneFact right)
        {
            return string.Compare(left?.defName, right?.defName, StringComparison.OrdinalIgnoreCase);
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    /// <summary>Formats selected event-time facts; complete gene membership never crosses this boundary.</summary>
    internal static class GeneIdentityContextFormatter
    {
        public const int HardMaximumFieldCharacters = 4096;

        /// <summary>Returns one separator-safe bounded value for outer progression fields.</summary>
        public static string CleanField(string value, int characterLimit)
        {
            return Safe(value, characterLimit);
        }

        /// <summary>Builds semicolon-delimited context from bounded labels and selected themes only.</summary>
        public static string Format(
            GeneIdentitySnapshot before,
            GeneIdentitySnapshot after,
            GeneIdentityTransitionDecision decision,
            string causeToken,
            string otherPawnName,
            string otherPawnId,
            int labelCharacterLimit)
        {
            if (decision == null || !decision.HasAnyChange) return string.Empty;
            int labelLimit = Clamp(labelCharacterLimit, 1, HardMaximumFieldCharacters);
            StringBuilder builder = new StringBuilder();
            Append(builder, "gene_identity_transition", "true", HardMaximumFieldCharacters);
            Append(builder, "previous_xenotype", before?.xenotypeLabel, labelLimit);
            Append(builder, "previous_xenotype_def", before?.xenotypeDefName, 256);
            Append(builder, "xenotype", after?.xenotypeLabel, labelLimit);
            Append(builder, "xenotype_def", after?.xenotypeDefName, 256);
            Append(builder, "gene_change_cause", causeToken, 64);
            Append(builder, "other_pawn", otherPawnName, labelLimit);
            Append(builder, "other_pawn_id", otherPawnId, 256);

            int count = Math.Min(GeneSaliencePolicySnapshot.HardMaximumThemes,
                decision.themes == null ? 0 : decision.themes.Count);
            for (int i = 0; i < count; i++)
            {
                GeneTheme theme = decision.themes[i];
                if (theme == null) continue;
                string suffix = (i + 1).ToString();
                Append(builder, "gene_theme_" + suffix, theme.label, HardMaximumFieldCharacters);
                Append(builder, "gene_theme_description_" + suffix,
                    theme.description, HardMaximumFieldCharacters);
                Append(builder, "gene_theme_change_" + suffix, theme.change, 32);
                Append(builder, "gene_theme_category_" + suffix, theme.category, 32);
            }
            Append(builder, "narrative_facets", "identity_transition", 64);
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string key, string value, int requestedLimit)
        {
            string safe = Safe(value, requestedLimit);
            if (safe.Length == 0) return;
            if (builder.Length > 0) builder.Append("; ");
            builder.Append(key).Append('=').Append(safe);
        }

        private static string Safe(string value, int requestedLimit)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            int limit = Clamp(requestedLimit, 1, HardMaximumFieldCharacters);
            StringBuilder result = new StringBuilder(Math.Min(value.Length, limit));
            bool priorWhitespace = false;
            for (int i = 0; i < value.Length && result.Length < limit; i++)
            {
                char character = value[i];
                if (character == ';') character = ',';
                else if (character == '=') character = ':';
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

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum) return minimum;
            return value > maximum ? maximum : value;
        }
    }
}

// Deterministic, assembly-free gene-theme selection for Biotech Phase 5. XML supplies every
// tunable weight and cap through GeneSaliencePolicySnapshot; this file contains only stable schema
// tokens, defensive limits, and the selection algorithm.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary.Capture
{
    /// <summary>One XML-owned score for a stable structural gene category.</summary>
    internal class GeneCategoryWeightRule
    {
        public string category = string.Empty;
        public int weight;
    }

    /// <summary>Detached XML policy consumed by <see cref="GeneSaliencePolicy"/>.</summary>
    internal class GeneSaliencePolicySnapshot
    {
        public const int HardMaximumThemes = 8;
        public const int HardMaximumTextCharacters = 4096;

        public int maximumThemes = 4;
        public int deltaBonus = 100;
        public int xenogeneBonus = 4;
        public int endogeneBonus = 2;
        public int duplicateCategoryPenalty = 30;
        public int forcedDefNameBonus = 1000;
        public int labelCharacterLimit = 80;
        public int descriptionCharacterLimit = 240;
        public int totalTextCharacterLimit = 640;
        public int maximumObservedGeneDefNames = 512;
        public int minimumFallbackGeneChanges = 2;
        public List<GeneCategoryWeightRule> categoryWeights = new List<GeneCategoryWeightRule>();
        public List<string> forceIncludeDefNames = new List<string>();
        public List<string> excludeDefNames = new List<string>();
        public List<string> allowDuplicateCategories = new List<string>();

        /// <summary>Creates non-prose fallbacks used if the XML Def is unavailable during startup.</summary>
        public static GeneSaliencePolicySnapshot CreateDefault()
        {
            GeneSaliencePolicySnapshot result = new GeneSaliencePolicySnapshot();
            result.categoryWeights.Add(Weight(GeneCategoryTokens.Ability, 60));
            result.categoryWeights.Add(Weight(GeneCategoryTokens.Trait, 55));
            result.categoryWeights.Add(Weight(GeneCategoryTokens.Resource, 50));
            result.categoryWeights.Add(Weight(GeneCategoryTokens.Need, 45));
            result.categoryWeights.Add(Weight(GeneCategoryTokens.Aging, 40));
            result.categoryWeights.Add(Weight(GeneCategoryTokens.Environment, 35));
            result.categoryWeights.Add(Weight(GeneCategoryTokens.Violence, 34));
            result.categoryWeights.Add(Weight(GeneCategoryTokens.Emotion, 33));
            result.categoryWeights.Add(Weight(GeneCategoryTokens.Social, 32));
            result.categoryWeights.Add(Weight(GeneCategoryTokens.Capacity, 30));
            result.categoryWeights.Add(Weight(GeneCategoryTokens.Appearance, 20));
            result.categoryWeights.Add(Weight(GeneCategoryTokens.Stat, 10));
            result.categoryWeights.Add(Weight(GeneCategoryTokens.Other, 0));
            return result;
        }

        private static GeneCategoryWeightRule Weight(string category, int weight)
        {
            return new GeneCategoryWeightRule { category = category, weight = weight };
        }
    }

    /// <summary>Selects a small, diverse, deterministic set of prompt-safe gene themes.</summary>
    internal static class GeneSaliencePolicy
    {
        private sealed class Candidate
        {
            public GeneFact fact;
            public string category;
            public string change;
            public int baseScore;
        }

        /// <summary>
        /// Returns zero to four useful themes. Exact added/removed facts outrank unchanged facts;
        /// hidden, inactive, suppressed, excluded, and malformed rows are ignored.
        /// </summary>
        public static List<GeneTheme> Select(
            GeneIdentitySnapshot snapshot,
            GeneMutationSnapshot mutation,
            GeneSaliencePolicySnapshot policy)
        {
            GeneSaliencePolicySnapshot safePolicy = policy ?? GeneSaliencePolicySnapshot.CreateDefault();
            int maximumThemes = Clamp(safePolicy.maximumThemes, 1, GeneSaliencePolicySnapshot.HardMaximumThemes);
            int labelLimit = Clamp(safePolicy.labelCharacterLimit, 1,
                GeneSaliencePolicySnapshot.HardMaximumTextCharacters);
            int descriptionLimit = Clamp(safePolicy.descriptionCharacterLimit, 1,
                GeneSaliencePolicySnapshot.HardMaximumTextCharacters);
            int totalLimit = Clamp(safePolicy.totalTextCharacterLimit, 1,
                GeneSaliencePolicySnapshot.HardMaximumTextCharacters);

            Dictionary<string, Candidate> candidates = new Dictionary<string, Candidate>(
                StringComparer.OrdinalIgnoreCase);
            AddCandidates(candidates, mutation == null ? null : mutation.addedGenes,
                GeneChangeTokens.Added, safePolicy);
            AddCandidates(candidates, mutation == null ? null : mutation.removedGenes,
                GeneChangeTokens.Removed, safePolicy);
            AddCandidates(candidates, snapshot == null ? null : snapshot.genes,
                GeneChangeTokens.Unchanged, safePolicy);

            List<Candidate> remaining = new List<Candidate>(candidates.Values);
            List<GeneTheme> selected = new List<GeneTheme>();
            HashSet<string> selectedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int textUsed = 0;
            while (selected.Count < maximumThemes && remaining.Count > 0)
            {
                Candidate best = null;
                int bestAdjustedScore = int.MinValue;
                for (int i = 0; i < remaining.Count; i++)
                {
                    Candidate candidate = remaining[i];
                    int adjustedScore = candidate.baseScore;
                    if (selectedCategories.Contains(candidate.category)
                        && !Contains(safePolicy.allowDuplicateCategories, candidate.category))
                    {
                        adjustedScore -= Math.Max(0, safePolicy.duplicateCategoryPenalty);
                    }

                    if (best == null || adjustedScore > bestAdjustedScore
                        || (adjustedScore == bestAdjustedScore && Compare(candidate, best) < 0))
                    {
                        best = candidate;
                        bestAdjustedScore = adjustedScore;
                    }
                }

                remaining.Remove(best);
                string label = CleanAndLimit(best.fact.label, labelLimit);
                string description = CleanAndLimit(best.fact.description, descriptionLimit);
                int remainingText = totalLimit - textUsed;
                if (remainingText <= 0) break;

                if (label.Length > remainingText) label = Limit(label, remainingText);
                remainingText -= label.Length;
                if (description.Length > remainingText) description = Limit(description, remainingText);
                if (label.Length == 0 && description.Length == 0) continue;

                textUsed += label.Length + description.Length;
                selected.Add(new GeneTheme
                {
                    defName = best.fact.defName.Trim(),
                    label = label,
                    description = description,
                    category = best.category,
                    change = best.change,
                    magnitudeBand = Clean(best.fact.magnitudeBand),
                    score = bestAdjustedScore
                });
                selectedCategories.Add(best.category);
            }

            return selected;
        }

        private static void AddCandidates(
            Dictionary<string, Candidate> candidates,
            List<GeneFact> facts,
            string change,
            GeneSaliencePolicySnapshot policy)
        {
            if (facts == null) return;
            for (int i = 0; i < facts.Count; i++)
            {
                GeneFact fact = facts[i];
                if (!Usable(fact) || Contains(policy.excludeDefNames, fact.defName)) continue;

                // The same exact gene may appear in the post-change membership and the added list.
                // The delta candidate wins because it carries the more useful truth boundary.
                string key = fact.defName.Trim();
                if (candidates.ContainsKey(key)) continue;
                string category = PrimaryCategory(fact, policy);
                int score = WeightFor(category, policy.categoryWeights);
                if (!string.Equals(change, GeneChangeTokens.Unchanged, StringComparison.Ordinal))
                    score += Math.Max(0, policy.deltaBonus);
                if (fact.isXenogene) score += policy.xenogeneBonus;
                if (fact.isEndogene) score += policy.endogeneBonus;
                if (Contains(policy.forceIncludeDefNames, fact.defName))
                    score += Math.Max(0, policy.forcedDefNameBonus);
                candidates.Add(key, new Candidate
                {
                    fact = fact,
                    category = category,
                    change = change,
                    baseScore = score
                });
            }
        }

        private static bool Usable(GeneFact fact)
        {
            return fact != null && fact.active && !fact.hidden && !fact.suppressed
                && !string.IsNullOrWhiteSpace(fact.defName)
                && (!string.IsNullOrWhiteSpace(fact.label) || !string.IsNullOrWhiteSpace(fact.description));
        }

        private static string PrimaryCategory(GeneFact fact, GeneSaliencePolicySnapshot policy)
        {
            string best = GeneCategoryTokens.Other;
            int bestWeight = WeightFor(best, policy.categoryWeights);
            Consider(fact.affectsAbility, GeneCategoryTokens.Ability, policy, ref best, ref bestWeight);
            Consider(fact.affectsTrait, GeneCategoryTokens.Trait, policy, ref best, ref bestWeight);
            Consider(fact.affectsResource, GeneCategoryTokens.Resource, policy, ref best, ref bestWeight);
            Consider(fact.affectsNeed, GeneCategoryTokens.Need, policy, ref best, ref bestWeight);
            Consider(fact.affectsAppearance, GeneCategoryTokens.Appearance, policy, ref best, ref bestWeight);
            Consider(fact.affectsAging, GeneCategoryTokens.Aging, policy, ref best, ref bestWeight);
            Consider(fact.affectsEnvironment, GeneCategoryTokens.Environment, policy, ref best, ref bestWeight);
            Consider(fact.affectsViolence, GeneCategoryTokens.Violence, policy, ref best, ref bestWeight);
            Consider(fact.affectsEmotion, GeneCategoryTokens.Emotion, policy, ref best, ref bestWeight);
            Consider(fact.affectsSocial, GeneCategoryTokens.Social, policy, ref best, ref bestWeight);
            Consider(fact.affectsCapacity, GeneCategoryTokens.Capacity, policy, ref best, ref bestWeight);
            Consider(fact.affectsStat, GeneCategoryTokens.Stat, policy, ref best, ref bestWeight);
            return best;
        }

        private static void Consider(
            bool applies,
            string category,
            GeneSaliencePolicySnapshot policy,
            ref string best,
            ref int bestWeight)
        {
            if (!applies) return;
            int weight = WeightFor(category, policy.categoryWeights);
            if (weight > bestWeight || (weight == bestWeight
                && string.CompareOrdinal(category, best) < 0))
            {
                best = category;
                bestWeight = weight;
            }
        }

        private static int WeightFor(string category, List<GeneCategoryWeightRule> rules)
        {
            if (rules == null) return 0;
            for (int i = 0; i < rules.Count; i++)
            {
                GeneCategoryWeightRule rule = rules[i];
                if (rule != null && string.Equals(rule.category, category,
                    StringComparison.OrdinalIgnoreCase)) return rule.weight;
            }
            return 0;
        }

        private static bool Contains(List<string> values, string sought)
        {
            if (values == null || string.IsNullOrWhiteSpace(sought)) return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals((values[i] ?? string.Empty).Trim(), sought.Trim(),
                    StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static int Compare(Candidate left, Candidate right)
        {
            int change = string.CompareOrdinal(left.change, right.change);
            if (change != 0) return change;
            return string.Compare(left.fact.defName, right.fact.defName, StringComparison.OrdinalIgnoreCase);
        }

        private static string CleanAndLimit(string value, int limit)
        {
            return Limit(Clean(value), limit);
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            StringBuilder result = new StringBuilder(value.Length);
            bool previousWhitespace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (char.IsControl(character) || char.IsWhiteSpace(character))
                {
                    if (!previousWhitespace && result.Length > 0) result.Append(' ');
                    previousWhitespace = true;
                }
                else
                {
                    result.Append(character);
                    previousWhitespace = false;
                }
            }
            return result.ToString().Trim();
        }

        private static string Limit(string value, int limit)
        {
            if (string.IsNullOrEmpty(value) || limit <= 0) return string.Empty;
            return value.Length <= limit ? value : value.Substring(0, limit).TrimEnd();
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum) return minimum;
            return value > maximum ? maximum : value;
        }
    }
}

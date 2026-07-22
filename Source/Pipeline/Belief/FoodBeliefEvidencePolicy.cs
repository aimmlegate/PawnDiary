// Pure food-evidence policy. Runtime capture supplies one exact primitive ingredient fact while XML
// supplies the resolver group and match-field vocabulary. This file never sees a Thing, ThingDef,
// Pawn, Ideo, quality, meal label, or RimWorld assembly type.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Adds exact food facts to an already-authorized event row. Missing, malformed, or ambiguous
    /// policy returns false without changing the ordinary evidence.
    /// </summary>
    internal static class FoodBeliefEvidencePolicy
    {
        /// <summary>
        /// Applies one uniquely matching XML rule. Exact source-precept and correlation fields remain
        /// untouched, so the shared resolver retains its established structural precedence.
        /// </summary>
        public static bool TryEnrich(
            BeliefEventEvidence evidence,
            FoodIngestionEvidenceFact fact,
            bool ideologyActive,
            BeliefPolicySnapshot policy)
        {
            if (!ideologyActive || policy == null || !policy.enabled || evidence?.narrative == null
                || evidence.narrative.pawnCanKnow != true || fact == null)
            {
                return false;
            }

            string ingredientKind = SafeToken(fact.ingredientKind);
            string ingredientDefName = SafeIdentifier(fact.ingredientDefName, policy);
            string ingredientLabel = BeliefContextFormatter.WholeWord(
                fact.ingredientLabel, policy.maximumFieldCharacters);
            if (ingredientKind.Length == 0 || ingredientDefName.Length == 0
                || ingredientLabel.Length == 0)
            {
                return false;
            }

            BeliefFoodEvidenceRule selected = null;
            IReadOnlyList<BeliefFoodEvidenceRule> rules = policy.foodEvidenceRules;
            if (rules == null) return false;
            for (int i = 0; i < rules.Count; i++)
            {
                BeliefFoodEvidenceRule candidate = rules[i];
                if (candidate == null || !string.Equals(
                        candidate.ingredientKind, ingredientKind, StringComparison.Ordinal))
                {
                    continue;
                }

                // A malformed matching row is ambiguity, not permission to continue to a later row.
                // Duplicate exact selectors likewise fail closed rather than depending on XML order.
                if (selected != null || !ValidRule(candidate)) return false;
                selected = candidate;
            }

            if (selected == null || evidence.matchFields == null || evidence.matchFields.Count >= 32)
                return false;
            string groupKey = SafeToken(selected.groupKey);
            string matchField = SafeToken(selected.matchField);
            if (groupKey.Length == 0 || matchField.Length == 0
                || (!string.IsNullOrWhiteSpace(evidence.groupKey)
                    && !string.Equals(evidence.groupKey, groupKey, StringComparison.Ordinal)))
            {
                return false;
            }

            // Commit only after every guard has passed. No failure path above can leave partial food
            // evidence on the ordinary thought page.
            evidence.groupKey = groupKey;
            evidence.matchFields.Add(new BeliefEvidenceTextFact
            {
                field = matchField,
                value = ingredientLabel
            });
            return true;
        }

        private static bool ValidRule(BeliefFoodEvidenceRule rule)
        {
            return SafeToken(rule.key).Length > 0
                && SafeToken(rule.ingredientKind).Length > 0
                && SafeToken(rule.groupKey).Length > 0
                && SafeToken(rule.matchField).Length > 0;
        }

        private static string SafeIdentifier(string value, BeliefPolicySnapshot policy)
        {
            string cleaned = BeliefContextFormatter.Clean(value, policy.maximumIdentifierCharacters);
            return cleaned.IndexOf('\n') >= 0 ? string.Empty : cleaned;
        }

        private static string SafeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string token = value.Trim();
            for (int i = 0; i < token.Length; i++)
            {
                char character = token[i];
                if (!(char.IsLetterOrDigit(character) || character == '_' || character == '-'))
                    return string.Empty;
            }
            return token;
        }
    }
}

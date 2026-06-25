// Pure matcher for XML-owned diary text-decoration rules.
//
// This file owns rule selection, condition matching, and gameContext tag extraction. It depends only
// on primitive decoration contracts and the shared DiaryContextFields parser, so it stays usable by
// pure console tests and cannot accidentally reach live Pawn, DefDatabase, settings, GUI, or IO state.
using System;
using System.Collections.Generic;
using static PawnDiary.DiaryTextDecorationText;

namespace PawnDiary
{
    /// <summary>
    /// Selects decoration rules and tests decoration conditions against captured DTO facts.
    /// </summary>
    internal static class DiaryTextDecorationMatcher
    {
        internal static DiaryTextDecorationPlan Select(
            DiaryTextDecorationContext context,
            IEnumerable<DiaryTextDecorationRule> rules,
            string scope)
        {
            DiaryTextDecorationPlan plan = new DiaryTextDecorationPlan();
            if (rules == null)
            {
                return plan;
            }

            foreach (DiaryTextDecorationRule rule in rules)
            {
                if (rule == null || !rule.enabled)
                {
                    continue;
                }

                if (!ScopeMatches(rule.scope, scope))
                {
                    continue;
                }

                if (!Matches(context, rule.when))
                {
                    continue;
                }

                plan.rules.Add(rule);
            }

            plan.rules.Sort(CompareRules);
            return plan;
        }

        internal static bool HediffMatchesStaggeredRules(
            IEnumerable<DiaryTextDecorationRule> rules,
            DiaryTextDecorationHediffFact fact)
        {
            if (rules == null || fact == null || !fact.visible)
            {
                return false;
            }

            foreach (DiaryTextDecorationRule rule in rules)
            {
                if (rule == null || !rule.enabled)
                {
                    continue;
                }

                if (!KindEquals(rule.decoration, DiaryTextDecorationKinds.StaggeredWordSizes))
                {
                    continue;
                }

                DiaryTextDecorationCondition when = rule.when;
                if (when == null)
                {
                    continue;
                }

                // A rule "names" a hediff only via a populated list that the fact actually hits.
                // Unlike the render-time matcher (where an unset category means "no constraint"),
                // here an unset list must NOT count as a match. Otherwise one populated list plus two
                // unset ones would classify every hediff as intoxicating.
                bool named = (HasAny(when.anyHediffDefName) && MatchesAny(when.anyHediffDefName, fact.defName))
                    || (HasAny(when.anyHediffDefNameContains) && MatchesAnyContains(when.anyHediffDefNameContains, fact.defName))
                    || (HasAny(when.anyHediffLabelContains) && MatchesAnyContains(when.anyHediffLabelContains, fact.label));
                if (named)
                {
                    return true;
                }
            }

            return false;
        }

        internal static void AddEventTagsFromContext(DiaryTextDecorationContext context, string gameContext)
        {
            if (context == null)
            {
                return;
            }

            if (context.eventTags == null)
            {
                context.eventTags = new List<string>();
            }

            if (string.IsNullOrWhiteSpace(gameContext))
            {
                return;
            }

            string[] parts = gameContext.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i] == null ? string.Empty : parts[i].Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                int equals = part.IndexOf('=');
                if (equals > 0)
                {
                    AddUnique(context.eventTags, part.Substring(0, equals).Trim());
                    AddUnique(context.eventTags, part);
                }
                else
                {
                    AddUnique(context.eventTags, part);
                }
            }
        }

        internal static bool Matches(DiaryTextDecorationContext context, DiaryTextDecorationCondition condition)
        {
            if (condition == null)
            {
                return true;
            }

            context = context ?? new DiaryTextDecorationContext();
            if (!MatchesAny(condition.anyPovRole, context.povRole)) return false;
            if (!MatchesAny(condition.anyDefName, context.defName)) return false;
            if (!MatchesAny(condition.anyDomain, context.domain)) return false;
            if (!MatchesAny(condition.anyColorCue, context.colorCue)) return false;
            if (!MatchesAny(condition.anyAtmosphereCue, context.atmosphereCue)) return false;
            if (!MatchesAnyInList(condition.anyEventTag, context.eventTags)) return false;
            if (!MatchesAnyContextKey(condition.anyContextKey, context.gameContext)) return false;
            if (!MatchesAnyContains(condition.anyContextValueContains, context.gameContext)) return false;
            if (!MatchesHediff(condition, context.hediffs)) return false;
            if (!MatchesTrait(condition, context.traits)) return false;
            return true;
        }

        internal static string ContextValue(string gameContext, string key)
        {
            return DiaryContextFields.Value(gameContext, key);
        }

        private static bool MatchesHediff(DiaryTextDecorationCondition condition, List<DiaryTextDecorationHediffFact> hediffs)
        {
            bool hasCriterion = HasAny(condition.anyHediffDefName)
                || HasAny(condition.anyHediffDefNameContains)
                || HasAny(condition.anyHediffLabelContains)
                || condition.minHediffSeverity >= 0f;
            if (!hasCriterion)
            {
                return true;
            }

            if (hediffs == null)
            {
                return false;
            }

            for (int i = 0; i < hediffs.Count; i++)
            {
                DiaryTextDecorationHediffFact hediff = hediffs[i];
                if (hediff == null || !hediff.visible)
                {
                    continue;
                }

                if (condition.minHediffSeverity >= 0f && hediff.severity < condition.minHediffSeverity)
                {
                    continue;
                }

                if (HediffNameMatches(condition, hediff))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HediffNameMatches(DiaryTextDecorationCondition condition, DiaryTextDecorationHediffFact hediff)
        {
            bool hasNameCriterion = HasAny(condition.anyHediffDefName)
                || HasAny(condition.anyHediffDefNameContains)
                || HasAny(condition.anyHediffLabelContains);
            if (!hasNameCriterion)
            {
                return true;
            }

            return MatchesAny(condition.anyHediffDefName, hediff.defName)
                || MatchesAnyContains(condition.anyHediffDefNameContains, hediff.defName)
                || MatchesAnyContains(condition.anyHediffLabelContains, hediff.label);
        }

        private static bool MatchesTrait(DiaryTextDecorationCondition condition, List<DiaryTextDecorationTraitFact> traits)
        {
            bool hasCriterion = HasAny(condition.anyTraitDefName)
                || HasAny(condition.anyTraitDefNameContains)
                || HasAny(condition.anyTraitLabelContains);
            if (!hasCriterion)
            {
                return true;
            }

            if (traits == null)
            {
                return false;
            }

            for (int i = 0; i < traits.Count; i++)
            {
                DiaryTextDecorationTraitFact trait = traits[i];
                if (trait == null)
                {
                    continue;
                }

                if (MatchesAny(condition.anyTraitDefName, trait.defName)
                    || MatchesAnyContains(condition.anyTraitDefNameContains, trait.defName)
                    || MatchesAnyContains(condition.anyTraitLabelContains, trait.label))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ScopeMatches(string ruleScope, string requestedScope)
        {
            string normalizedRule = string.IsNullOrWhiteSpace(ruleScope)
                ? DiaryTextDecorationScopes.DirectSpeech
                : ruleScope.Trim();
            string normalizedRequested = string.IsNullOrWhiteSpace(requestedScope)
                ? DiaryTextDecorationScopes.Body
                : requestedScope.Trim();

            return string.Equals(normalizedRule, DiaryTextDecorationScopes.All, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedRule, normalizedRequested, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesAny(List<string> expected, string actual)
        {
            if (!HasAny(expected))
            {
                return true;
            }

            for (int i = 0; i < expected.Count; i++)
            {
                if (string.Equals(Trim(expected[i]), Trim(actual), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesAnyContains(List<string> needles, string actual)
        {
            if (!HasAny(needles))
            {
                return true;
            }

            string text = actual ?? string.Empty;
            for (int i = 0; i < needles.Count; i++)
            {
                string needle = Trim(needles[i]);
                if (needle.Length > 0 && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesAnyInList(List<string> expected, List<string> actual)
        {
            if (!HasAny(expected))
            {
                return true;
            }

            if (actual == null)
            {
                return false;
            }

            for (int i = 0; i < expected.Count; i++)
            {
                string expectedValue = Trim(expected[i]);
                for (int j = 0; j < actual.Count; j++)
                {
                    if (string.Equals(expectedValue, Trim(actual[j]), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool MatchesAnyContextKey(List<string> keys, string gameContext)
        {
            if (!HasAny(keys))
            {
                return true;
            }

            for (int i = 0; i < keys.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(ContextValue(gameContext, keys[i])))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareRules(DiaryTextDecorationRule left, DiaryTextDecorationRule right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null) return -1;
            if (right == null) return 1;
            int sequence = left.sequence.CompareTo(right.sequence);
            if (sequence != 0)
            {
                return sequence;
            }

            return string.Compare(left.decoration, right.decoration, StringComparison.OrdinalIgnoreCase);
        }

        private static void AddUnique(List<string> values, string value)
        {
            value = Trim(value);
            if (value.Length == 0)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            values.Add(value);
        }

        // Trim and KindEquals now live in DiaryTextDecorationText (shared with the rich-text
        // decorators); see the `using static` at the top of this file.

        private static bool HasAny(List<string> values)
        {
            if (values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

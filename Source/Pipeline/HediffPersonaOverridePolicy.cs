// Pure policy for temporary hediff-driven writing-style overrides. Runtime code snapshots live
// RimWorld hediffs into these small DTOs, then this matcher decides which style Def should win.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Plain rule describing which active hediffs force a temporary writing style.
    /// </summary>
    public sealed class HediffPersonaOverrideRule
    {
        public string key;
        public int priority;
        public string personaDefName;
        public bool visibleOnly = true;
        public float minSeverity = -1f;
        public List<string> hediffDefNames = new List<string>();
        public List<string> hediffDefNameContains = new List<string>();
        public List<string> hediffLabelContains = new List<string>();
    }

    /// <summary>
    /// Plain snapshot of one active hediff on the prompt POV pawn.
    /// </summary>
    public sealed class HediffPersonaOverrideFact
    {
        public string defName;
        public string label;
        public float severity;
        public bool visible;
    }

    /// <summary>
    /// Result of the hediff-persona match. The matched hediff names let prompt-time callers avoid
    /// repeating the same condition as both a writing-style override and an "important context" cue.
    /// </summary>
    public sealed class HediffPersonaOverrideSelection
    {
        public string personaDefName = string.Empty;
        public List<string> matchedHediffDefNames = new List<string>();
    }

    /// <summary>
    /// Selects the temporary writing-style override that should apply to a prompt, if any.
    /// </summary>
    public static class HediffPersonaOverridePolicy
    {
        public static string SelectPersonaDefName(IList<HediffPersonaOverrideRule> rules,
            IList<HediffPersonaOverrideFact> hediffs)
        {
            return SelectOverride(rules, hediffs).personaDefName;
        }

        public static HediffPersonaOverrideSelection SelectOverride(IList<HediffPersonaOverrideRule> rules,
            IList<HediffPersonaOverrideFact> hediffs)
        {
            if (rules == null || rules.Count == 0 || hediffs == null || hediffs.Count == 0)
            {
                return new HediffPersonaOverrideSelection();
            }

            HediffPersonaOverrideRule selectedRule = null;
            int selectedPriority = int.MinValue;
            for (int i = 0; i < rules.Count; i++)
            {
                HediffPersonaOverrideRule rule = rules[i];
                if (rule == null
                    || string.IsNullOrWhiteSpace(rule.personaDefName)
                    || (selectedRule != null && rule.priority <= selectedPriority))
                {
                    continue;
                }

                if (MatchesAnyHediff(rule, hediffs))
                {
                    selectedRule = rule;
                    selectedPriority = rule.priority;
                }
            }

            if (selectedRule == null)
            {
                return new HediffPersonaOverrideSelection();
            }

            HediffPersonaOverrideSelection selection = new HediffPersonaOverrideSelection
            {
                personaDefName = selectedRule.personaDefName.Trim()
            };
            AddMatchedHediffDefNamesForAllRules(selection.matchedHediffDefNames, rules, hediffs);
            return selection;
        }

        private static bool MatchesAnyHediff(HediffPersonaOverrideRule rule,
            IList<HediffPersonaOverrideFact> hediffs)
        {
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (Matches(rule, hediffs[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Matches(HediffPersonaOverrideRule rule, HediffPersonaOverrideFact hediff)
        {
            if (rule == null || hediff == null)
            {
                return false;
            }

            if (rule.visibleOnly && !hediff.visible)
            {
                return false;
            }

            if (rule.minSeverity >= 0f && hediff.severity < rule.minSeverity)
            {
                return false;
            }

            return HediffNameMatches(rule, hediff);
        }

        private static void AddMatchedHediffDefNames(List<string> names,
            HediffPersonaOverrideRule rule, IList<HediffPersonaOverrideFact> hediffs)
        {
            if (names == null || rule == null || hediffs == null)
            {
                return;
            }

            for (int i = 0; i < hediffs.Count; i++)
            {
                HediffPersonaOverrideFact hediff = hediffs[i];
                if (Matches(rule, hediff) && !string.IsNullOrWhiteSpace(hediff.defName))
                {
                    AddUnique(names, hediff.defName);
                }
            }
        }

        private static void AddMatchedHediffDefNamesForAllRules(List<string> names,
            IList<HediffPersonaOverrideRule> rules, IList<HediffPersonaOverrideFact> hediffs)
        {
            if (names == null || rules == null || hediffs == null)
            {
                return;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                HediffPersonaOverrideRule rule = rules[i];
                if (rule == null || string.IsNullOrWhiteSpace(rule.personaDefName))
                {
                    continue;
                }

                AddMatchedHediffDefNames(names, rule, hediffs);
            }
        }

        private static bool HediffNameMatches(HediffPersonaOverrideRule rule,
            HediffPersonaOverrideFact hediff)
        {
            bool hasNameCriterion = HasAny(rule.hediffDefNames)
                || HasAny(rule.hediffDefNameContains)
                || HasAny(rule.hediffLabelContains);
            if (!hasNameCriterion)
            {
                return false;
            }

            return MatchesAny(rule.hediffDefNames, hediff.defName)
                || MatchesAnyContains(rule.hediffDefNameContains, hediff.defName)
                || MatchesAnyContains(rule.hediffLabelContains, hediff.label);
        }

        private static bool HasAny(List<string> values)
        {
            return values != null && values.Count > 0;
        }

        private static bool MatchesAny(List<string> values, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(candidate, values[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value))
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

        private static bool MatchesAnyContains(List<string> needles, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || needles == null)
            {
                return false;
            }

            for (int i = 0; i < needles.Count; i++)
            {
                string needle = needles[i];
                if (!string.IsNullOrWhiteSpace(needle)
                    && candidate.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

// Pure matching policy for colony-news reflection cues. RimWorld's Archive and DiaryEvent objects
// stay in DiaryGameComponent; this file receives only the stable strings copied from XML and saved
// context. Keeping the decision here makes letter classification and direct-story suppression
// independently testable without loading RimWorld, Verse, or Unity.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// One XML-authored colony-news category: which LetterDef names belong to it and which saved
    /// diary domains/context markers prove that a direct page already owns that kind of story.
    /// </summary>
    public sealed class ColonyNewsCategoryRule
    {
        public string category;
        public List<string> letterDefNames = new List<string>();
        public List<string> directEventDomains = new List<string>();
        public List<string> directEventMarkers = new List<string>();
    }

    /// <summary>
    /// Classifies stable letter defNames and detects same-category direct diary ownership.
    /// </summary>
    internal static class ColonyNewsPolicy
    {
        /// <summary>
        /// Returns the first XML category whose exact defName list contains the supplied letter.
        /// Rule order is meaningful: specific rows such as quest-threat letters belong before the
        /// broader threat row.
        /// </summary>
        public static string CategoryForLetter(
            string letterDefName,
            IReadOnlyList<ColonyNewsCategoryRule> rules)
        {
            if (string.IsNullOrWhiteSpace(letterDefName) || rules == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                ColonyNewsCategoryRule rule = rules[i];
                if (rule == null || string.IsNullOrWhiteSpace(rule.category))
                {
                    continue;
                }

                if (Contains(rule.letterDefNames, letterDefName))
                {
                    return rule.category.Trim();
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// True when a saved direct page owns <paramref name="category"/> by exact XML domain or
        /// context-marker policy. Translated labels are deliberately never consulted.
        /// </summary>
        public static bool EventOwnsCategory(
            string category,
            string eventDomain,
            string gameContext,
            IReadOnlyList<ColonyNewsCategoryRule> rules)
        {
            ColonyNewsCategoryRule rule = RuleForCategory(category, rules);
            if (rule == null)
            {
                return false;
            }

            if (Contains(rule.directEventDomains, eventDomain))
            {
                return true;
            }

            if (rule.directEventMarkers == null || string.IsNullOrWhiteSpace(gameContext))
            {
                return false;
            }

            for (int i = 0; i < rule.directEventMarkers.Count; i++)
            {
                string marker = rule.directEventMarkers[i];
                if (!string.IsNullOrWhiteSpace(marker)
                    && DiaryContextFields.HasMarker(gameContext, marker.Trim()))
                {
                    return true;
                }
            }

            return false;
        }

        private static ColonyNewsCategoryRule RuleForCategory(
            string category,
            IReadOnlyList<ColonyNewsCategoryRule> rules)
        {
            if (string.IsNullOrWhiteSpace(category) || rules == null)
            {
                return null;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                ColonyNewsCategoryRule rule = rules[i];
                if (rule != null
                    && string.Equals(rule.category, category, StringComparison.OrdinalIgnoreCase))
                {
                    return rule;
                }
            }

            return null;
        }

        private static bool Contains(IReadOnlyList<string> values, string candidate)
        {
            if (values == null || string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

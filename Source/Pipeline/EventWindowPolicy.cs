// Pure matching helpers for XML-controlled event windows. Runtime code snapshots RimWorld events into
// these plain facts, then the matcher decides whether a Def trigger starts or ends a window.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Plain, testable facts for a signal that may start or end an event window.
    /// </summary>
    public sealed class EventWindowSignalFacts
    {
        public string source;
        public string defName;
        public string signal;
        public string label;
        public string subjectPawnId;
        public string subjectLabel;
    }

    /// <summary>
    /// Plain trigger rule copied from XML before matching.
    /// </summary>
    public sealed class EventWindowTriggerRule
    {
        public string source;
        public string signal;
        public List<string> matchDefNames = new List<string>();
        public List<string> matchTokens = new List<string>();
    }

    /// <summary>
    /// Stateless trigger matching for event windows.
    /// </summary>
    public static class EventWindowPolicy
    {
        public static bool Matches(EventWindowTriggerRule rule, EventWindowSignalFacts facts)
        {
            if (rule == null || facts == null)
            {
                return false;
            }

            if (!BlankOrEquals(rule.source, facts.source))
            {
                return false;
            }

            if (!BlankOrEquals(rule.signal, facts.signal))
            {
                return false;
            }

            bool hasDefMatchers = HasAny(rule.matchDefNames);
            bool hasTokenMatchers = HasAny(rule.matchTokens);
            if (!hasDefMatchers && !hasTokenMatchers)
            {
                return HasText(rule.source) || HasText(rule.signal);
            }

            if (hasDefMatchers && MatchesExact(rule.matchDefNames, facts.defName))
            {
                return true;
            }

            return hasTokenMatchers && MatchesToken(rule.matchTokens, facts);
        }

        public static bool MatchesAny(IList<EventWindowTriggerRule> rules, EventWindowSignalFacts facts)
        {
            if (rules == null || rules.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                if (Matches(rules[i], facts))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool BlankOrEquals(string expected, string actual)
        {
            return string.IsNullOrWhiteSpace(expected)
                || string.Equals(expected.Trim(), actual ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesExact(IList<string> values, string actual)
        {
            if (values == null || string.IsNullOrWhiteSpace(actual))
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], actual, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesToken(IList<string> tokens, EventWindowSignalFacts facts)
        {
            string haystack = string.Join(" ", new[]
            {
                facts.source ?? string.Empty,
                facts.signal ?? string.Empty,
                facts.defName ?? string.Empty,
                facts.label ?? string.Empty,
                facts.subjectPawnId ?? string.Empty,
                facts.subjectLabel ?? string.Empty
            });

            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                if (!string.IsNullOrWhiteSpace(token)
                    && haystack.IndexOf(token.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAny(IList<string> values)
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

        private static bool HasText(string value)
        {
            return !string.IsNullOrWhiteSpace(value);
        }
    }
}

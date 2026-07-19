// Pure matching helpers for XML-controlled event windows. Runtime code snapshots RimWorld events into
// these plain facts, then the matcher decides whether a Def trigger starts or ends a window.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Plain, testable facts for a signal that may start or end an event window.
    /// </summary>
    internal sealed class EventWindowSignalFacts
    {
        public string source;
        public string defName;
        public string signal;
        public string label;
        public string subjectPawnId;
        public string subjectLabel;
        // Opaque source-instance identity and continuity arc. Neither field reaches prompt context.
        public string correlationId;
        public string narrativeArcKey;
    }

    /// <summary>
    /// Plain trigger rule copied from XML before matching.
    /// </summary>
    internal sealed class EventWindowTriggerRule
    {
        public string source;
        public string signal;
        public List<string> matchDefNames = new List<string>();
        public List<string> matchTokens = new List<string>();
    }

    /// <summary>
    /// Stateless trigger matching for event windows.
    /// </summary>
    internal static class EventWindowPolicy
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

        /// <summary>
        /// Cheap pre-filter for hot signal sources (e.g. every spawned Thing). Returns true only when
        /// at least one rule could match a signal with this <paramref name="source"/> and
        /// <paramref name="defName"/>, WITHOUT needing the signal's (possibly expensive) label. It
        /// deliberately ignores the signal field and over-approximates: rules that use token/substring
        /// matching, or that match any signal of a source, force a true result because those need the
        /// full facts to decide. This is a strict superset of <see cref="Matches"/> over
        /// source+defName, so a false result guarantees no rule can match — letting the caller skip
        /// resolving the label entirely. A true result just means "build full facts and run
        /// <see cref="MatchesAny"/>".
        /// </summary>
        public static bool CouldMatchByDefName(IList<EventWindowTriggerRule> rules, string source, string defName)
        {
            if (rules == null || rules.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                EventWindowTriggerRule rule = rules[i];
                if (rule == null || !BlankOrEquals(rule.source, source))
                {
                    continue;
                }

                bool hasDefMatchers = HasAny(rule.matchDefNames);
                bool hasTokenMatchers = HasAny(rule.matchTokens);
                if (!hasDefMatchers && !hasTokenMatchers)
                {
                    // Source/signal-only rule: matches any signal of this source, so it cannot be
                    // pre-filtered out by defName.
                    if (HasText(rule.source) || HasText(rule.signal))
                    {
                        return true;
                    }

                    continue;
                }

                // Token/substring matchers read the label and other free-text facts, so they need the
                // full check; only exact defName matchers can be decided cheaply here.
                if (hasTokenMatchers || MatchesExact(rule.matchDefNames, defName))
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

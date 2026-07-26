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
        // Optional adapter-owned, already-sanitized evidence. It never participates in matching.
        public string additionalContext;
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
    /// Pure deadline policy for persistent event windows. XML owns each intended timeout; the fallback
    /// exists only so a malformed third-party Def or legacy save row can never make prompt atmosphere
    /// immortal when its expected end signal is missed.
    /// </summary>
    internal static class EventWindowExpiryPolicy
    {
        // Defensive fallback only. Shipped persistent windows all provide an explicit XML timeout.
        public const int DefaultPersistentTimeoutTicks = 60000;

        /// <summary>Returns a finite positive timeout even when XML is missing or malformed.</summary>
        public static int EffectiveTimeoutTicks(int configuredTimeoutTicks)
        {
            return configuredTimeoutTicks > 0
                ? configuredTimeoutTicks
                : DefaultPersistentTimeoutTicks;
        }

        /// <summary>
        /// Resolves the saved deadline without ever extending it past the current Def policy. An earlier
        /// saved deadline remains authoritative, while a missing or overlong legacy value is repaired.
        /// </summary>
        public static int ResolveDeadline(int startedTick, int savedExpiresTick, int configuredTimeoutTicks)
        {
            int timeoutTicks = EffectiveTimeoutTicks(configuredTimeoutTicks);
            long configuredDeadline = (long)Math.Max(0, startedTick) + timeoutTicks;
            int boundedConfiguredDeadline = configuredDeadline >= int.MaxValue
                ? int.MaxValue
                : (int)configuredDeadline;

            return savedExpiresTick >= 0 && savedExpiresTick < boundedConfiguredDeadline
                ? savedExpiresTick
                : boundedConfiguredDeadline;
        }

        /// <summary>True at and after the resolved finite deadline.</summary>
        public static bool IsExpired(int nowTick, int deadlineTick)
        {
            return deadlineTick >= 0 && nowTick >= deadlineTick;
        }
    }

    /// <summary>
    /// Stateless trigger matching for event windows.
    /// </summary>
    internal static class EventWindowPolicy
    {
        /// <summary>
        /// Immutable-source, mutable-build index for one hot event-window signal shape. Runtime builds
        /// it once from the loaded Defs; each later lookup is a boolean check plus a hash lookup.
        /// Token rules and source/signal-only rules set the conservative wildcard because their final
        /// answer depends on facts that are intentionally absent from the cheap pre-check.
        /// </summary>
        internal sealed class DefNamePrecheckIndex
        {
            private readonly string source;
            private readonly string signal;
            private readonly HashSet<string> exactDefNames =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private bool couldMatchAnyDefName;

            /// <summary>Creates an index for one exact runtime source/signal pair.</summary>
            public DefNamePrecheckIndex(string source, string signal)
            {
                this.source = source ?? string.Empty;
                this.signal = signal ?? string.Empty;
            }

            /// <summary>
            /// Adds rules to the index. This is called while the lazy runtime cache is being built,
            /// never from the per-signal hot path.
            /// </summary>
            public void Include(IList<EventWindowTriggerRule> rules)
            {
                if (couldMatchAnyDefName || rules == null)
                {
                    return;
                }

                for (int i = 0; i < rules.Count; i++)
                {
                    EventWindowTriggerRule rule = rules[i];
                    if (rule == null
                        || !BlankOrEquals(rule.source, source)
                        || !BlankOrEquals(rule.signal, signal))
                    {
                        continue;
                    }

                    bool hasDefMatchers = HasAny(rule.matchDefNames);
                    bool hasTokenMatchers = HasAny(rule.matchTokens);
                    if (hasTokenMatchers
                        || (!hasDefMatchers
                            && (HasText(rule.source) || HasText(rule.signal))))
                    {
                        // Tokens can match the label/subject text, and a source/signal-only rule
                        // accepts every defName. Neither can be rejected by an exact-name index.
                        couldMatchAnyDefName = true;
                        exactDefNames.Clear();
                        return;
                    }

                    if (!hasDefMatchers)
                    {
                        // A completely blank trigger never matches.
                        continue;
                    }

                    for (int nameIndex = 0; nameIndex < rule.matchDefNames.Count; nameIndex++)
                    {
                        string defName = rule.matchDefNames[nameIndex];
                        if (!string.IsNullOrWhiteSpace(defName))
                        {
                            // Do not trim: MatchesExact intentionally compares the XML value as-is.
                            exactDefNames.Add(defName);
                        }
                    }
                }
            }

            /// <summary>
            /// Returns whether the full matcher could accept this defName. A false result is exact;
            /// a true result remains a conservative "run the full matcher" answer.
            /// </summary>
            public bool CouldMatch(string defName)
            {
                return couldMatchAnyDefName
                    || (!string.IsNullOrWhiteSpace(defName) && exactDefNames.Contains(defName));
            }
        }

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

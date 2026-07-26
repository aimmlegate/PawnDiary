// CultureAnnotationPlanner.cs — pure planning of inline culture annotations
// (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §4.3). Runs AFTER prompt-detail field selection and
// BEFORE final assembly: it looks only at fields that survived selection, detects at most two
// distinct topics from structured data (context keys, stable schema markers, event defNames) or
// localized XML-owned natural-language terms, and appends one parenthetical annotation to the end
// of the first rendered field that triggered each topic.
//
// Recursion/robustness guarantees:
//  - the planner runs exactly once per prompt, on pre-annotation values, so an annotation can
//    never trigger a topic;
//  - only XML-allowlisted sources are scanned, so system instructions, past-memory text
//    (MemoryContext), and generated text (prior entries) are structurally unreachable.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers"). No Verse/Unity/Def/settings here.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary
{
    /// <summary>Plans the ≤2 inline "(culture: …)" annotations for one prompt.</summary>
    internal static class CultureAnnotationPlanner
    {
        /// <summary>
        /// Detects topics and produces the annotation plan. Returns an empty plan whenever the
        /// injection switch is off, no culture profile applies, or nothing triggers.
        /// </summary>
        public static CultureAnnotationPlan Plan(
            List<AnnotationFieldView> fields,
            string eventDefName,
            List<CultureTopicRule> topics,
            CultureProfile originProfile,
            CultureProfile adoptedProfile,
            KnowledgePolicySnapshot policy)
        {
            CultureAnnotationPlan plan = new CultureAnnotationPlan();
            KnowledgePolicySnapshot safePolicy = policy ?? KnowledgePolicySnapshot.CreateDefault();
            if (!safePolicy.injectionEnabled
                || fields == null || fields.Count == 0
                || topics == null || topics.Count == 0
                || (originProfile == null && adoptedProfile == null))
            {
                return plan;
            }

            List<AnnotationFieldView> scannable = ScannableFields(fields, safePolicy);
            if (scannable.Count == 0)
            {
                return plan;
            }

            List<CultureTopicRule> ordered = OrderedTopics(topics);
            int maxTopics = Math.Max(0, safePolicy.maxCultureTopicsPerPrompt);
            for (int i = 0; i < ordered.Count && plan.entries.Count < maxTopics; i++)
            {
                CultureTopicRule topic = ordered[i];
                string text = AnnotationTextFor(topic.topicKey, originProfile, adoptedProfile, safePolicy);
                if (string.IsNullOrWhiteSpace(text))
                {
                    // A topic no profile can voice never consumes one of the two slots.
                    continue;
                }

                int fieldIndex = FirstTriggeringFieldIndex(topic, scannable, eventDefName);
                if (fieldIndex < 0)
                {
                    continue;
                }

                plan.entries.Add(new CultureAnnotationPlanEntry
                {
                    fieldIndex = fieldIndex,
                    topicKey = topic.topicKey,
                    text = text
                });
                plan.matchedTopics.Add(topic.topicKey);
            }

            return plan;
        }

        /// <summary>
        /// The annotation body (§4.3): "(culture: …)" for one effective profile, or
        /// "(origin: …; adopted: …)" when the pawn's origin and adopted profiles are DISTINCT
        /// and both voice the topic. A profile without a clause for the topic contributes
        /// nothing — never fallback prose.
        /// </summary>
        private static string AnnotationTextFor(
            string topicKey,
            CultureProfile originProfile,
            CultureProfile adoptedProfile,
            KnowledgePolicySnapshot policy)
        {
            string originClause = originProfile != null ? originProfile.ClauseFor(topicKey) : string.Empty;
            string adoptedClause = adoptedProfile != null ? adoptedProfile.ClauseFor(topicKey) : string.Empty;
            bool distinctProfiles = originProfile != null && adoptedProfile != null
                && !string.Equals(originProfile.cultureDefName, adoptedProfile.cultureDefName,
                    StringComparison.OrdinalIgnoreCase);

            if (distinctProfiles && originClause.Length > 0 && adoptedClause.Length > 0)
            {
                return SafeFormat(policy.annotationDualFormat, originClause, adoptedClause);
            }

            // One-profile cases: a lone clause from whichever side has one. Adopted wins when both
            // exist but the profiles are the same culture (identical clauses anyway).
            string clause = adoptedClause.Length > 0 ? adoptedClause : originClause;
            return clause.Length == 0 ? string.Empty : SafeFormat(policy.annotationSingleFormat, clause, null);
        }

        /// <summary>
        /// The first surviving field (template order) whose structured data triggers the topic:
        /// a GameContext field rendering one of the trigger context keys, any scannable field whose
        /// value carries a stable "marker=" schema token or localized XML-owned text term, or — for
        /// event-defName triggers — the first scannable field (the event itself has no field).
        /// </summary>
        private static int FirstTriggeringFieldIndex(
            CultureTopicRule topic, List<AnnotationFieldView> scannable, string eventDefName)
        {
            for (int i = 0; i < scannable.Count; i++)
            {
                AnnotationFieldView field = scannable[i];
                if (TriggersByContextKey(topic, field)
                    || TriggersByContextPair(topic, field)
                    || TriggersByValueMarker(topic, field)
                    || TriggersByTextTerm(topic, field))
                {
                    return field.index;
                }
            }

            if (TriggersByDefName(topic, eventDefName))
            {
                return scannable[0].index;
            }

            return -1;
        }

        private static bool TriggersByContextKey(CultureTopicRule topic, AnnotationFieldView field)
        {
            if (topic.triggerContextKeys == null
                || !string.Equals(field.source, "GameContext", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            for (int i = 0; i < topic.triggerContextKeys.Count; i++)
            {
                string triggerKey = topic.triggerContextKeys[i];
                if (string.IsNullOrWhiteSpace(triggerKey))
                {
                    continue;
                }

                triggerKey = triggerKey.Trim();
                if (string.Equals(field.contextKey, triggerKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string structuredValue = DiaryContextFields.Value(
                    field.structuredContext, triggerKey);
                if (!KnowledgeTokens.IsSentinelValue(structuredValue))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>"key=value" trigger: a GameContext field rendering exactly that stable token.</summary>
        private static bool TriggersByContextPair(CultureTopicRule topic, AnnotationFieldView field)
        {
            if (topic.triggerContextPairs == null
                || !string.Equals(field.source, "GameContext", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            for (int i = 0; i < topic.triggerContextPairs.Count; i++)
            {
                string pair = topic.triggerContextPairs[i];
                if (string.IsNullOrWhiteSpace(pair))
                {
                    continue;
                }

                int equalsIndex = pair.IndexOf('=');
                if (equalsIndex <= 0 || equalsIndex >= pair.Length - 1)
                {
                    continue;
                }

                string key = pair.Substring(0, equalsIndex).Trim();
                string expected = pair.Substring(equalsIndex + 1).Trim();
                bool displayedPair = string.Equals(
                        field.contextKey, key, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        field.resolvedValue?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
                if (displayedPair || DiaryContextFields.FieldEquals(
                    field.structuredContext, key, expected))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TriggersByValueMarker(CultureTopicRule topic, AnnotationFieldView field)
        {
            if (topic.triggerValueMarkers == null || string.IsNullOrEmpty(field.resolvedValue))
            {
                return false;
            }

            for (int i = 0; i < topic.triggerValueMarkers.Count; i++)
            {
                string marker = topic.triggerValueMarkers[i];
                if (!string.IsNullOrWhiteSpace(marker)
                    && field.resolvedValue.IndexOf(marker.Trim(),
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Matches localized XML terms against the already-localized selected field. Unlike stable
        /// schema-marker matching, this is word-boundary-aware: "empire" does not match "vampire".
        /// Individual pattern words may end in '*' to cover ordinary inflection and plurals.
        /// </summary>
        private static bool TriggersByTextTerm(CultureTopicRule topic, AnnotationFieldView field)
        {
            if (topic.triggerTextTerms == null || string.IsNullOrWhiteSpace(field.resolvedValue))
            {
                return false;
            }

            for (int i = 0; i < topic.triggerTextTerms.Count; i++)
            {
                if (CultureTextTermMatcher.Matches(
                    field.resolvedValue, topic.triggerTextTerms[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TriggersByDefName(CultureTopicRule topic, string eventDefName)
        {
            if (topic.triggerDefNames == null || string.IsNullOrWhiteSpace(eventDefName))
            {
                return false;
            }

            for (int i = 0; i < topic.triggerDefNames.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(topic.triggerDefNames[i])
                    && string.Equals(eventDefName, topic.triggerDefNames[i].Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Enabled fields with real values whose source is XML-allowlisted for scanning.</summary>
        private static List<AnnotationFieldView> ScannableFields(
            List<AnnotationFieldView> fields, KnowledgePolicySnapshot policy)
        {
            List<AnnotationFieldView> scannable = new List<AnnotationFieldView>();
            for (int i = 0; i < fields.Count; i++)
            {
                AnnotationFieldView field = fields[i];
                if (field == null
                    || KnowledgeTokens.IsSentinelValue(field.resolvedValue)
                    || !SourceAllowed(field.source, policy.scannableSources))
                {
                    continue;
                }

                scannable.Add(field);
            }

            return scannable;
        }

        private static bool SourceAllowed(string source, List<string> allowed)
        {
            if (allowed == null || string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            for (int i = 0; i < allowed.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(allowed[i])
                    && string.Equals(source, allowed[i].Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<CultureTopicRule> OrderedTopics(List<CultureTopicRule> topics)
        {
            List<CultureTopicRule> ordered = new List<CultureTopicRule>();
            for (int i = 0; i < topics.Count; i++)
            {
                if (topics[i] != null && topics[i].enabled
                    && !string.IsNullOrWhiteSpace(topics[i].topicKey))
                {
                    ordered.Add(topics[i]);
                }
            }

            ordered.Sort(CompareTopics);
            return ordered;
        }

        private static int CompareTopics(CultureTopicRule left, CultureTopicRule right)
        {
            int order = left.order.CompareTo(right.order);
            return order != 0
                ? order
                : string.Compare(left.topicKey, right.topicKey, StringComparison.Ordinal);
        }

        /// <summary>string.Format that survives an author's malformed XML format string.</summary>
        private static string SafeFormat(string format, string first, string second)
        {
            if (string.IsNullOrWhiteSpace(format))
            {
                return string.Empty;
            }

            try
            {
                return second == null
                    ? string.Format(format, first)
                    : string.Format(format, first, second);
            }
            catch (FormatException)
            {
                return first ?? string.Empty;
            }
        }
    }

    /// <summary>
    /// Pure, allocation-bounded lexical matcher for localized culture-topic terms. Text and patterns
    /// are split into Unicode letter/digit words, so punctuation and hyphens are harmless separators.
    /// A trailing '*' makes only that pattern word a prefix match; phrases remain contiguous.
    /// </summary>
    internal static class CultureTextTermMatcher
    {
        private sealed class PatternWord
        {
            public string text = string.Empty;
            public bool prefix;
        }

        /// <summary>True when one localized word/phrase pattern occurs in the supplied text.</summary>
        public static bool Matches(string text, string pattern)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }

            bool valid;
            List<PatternWord> patternWords = ParsePattern(pattern, out valid);
            if (!valid || patternWords.Count == 0)
            {
                return false;
            }

            List<string> textWords = Words(text);
            if (textWords.Count < patternWords.Count)
            {
                return false;
            }

            int lastStart = textWords.Count - patternWords.Count;
            for (int start = 0; start <= lastStart; start++)
            {
                bool allMatch = true;
                for (int offset = 0; offset < patternWords.Count; offset++)
                {
                    PatternWord expected = patternWords[offset];
                    string actual = textWords[start + offset];
                    bool matches = expected.prefix
                        ? actual.StartsWith(expected.text, StringComparison.OrdinalIgnoreCase)
                        : string.Equals(actual, expected.text, StringComparison.OrdinalIgnoreCase);
                    if (!matches)
                    {
                        allMatch = false;
                        break;
                    }
                }

                if (allMatch)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Valid patterns contain at least one word. '*' is allowed only immediately after a word of
        /// three or more characters; this prevents dangerously broad authoring such as "a*".
        /// </summary>
        public static bool IsValidPattern(string pattern)
        {
            bool valid;
            List<PatternWord> words = ParsePattern(pattern, out valid);
            return valid && words.Count > 0;
        }

        private static List<PatternWord> ParsePattern(string pattern, out bool valid)
        {
            List<PatternWord> words = new List<PatternWord>();
            StringBuilder current = new StringBuilder();
            valid = !string.IsNullOrWhiteSpace(pattern);
            if (!valid)
            {
                return words;
            }

            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (char.IsLetterOrDigit(c))
                {
                    current.Append(c);
                    continue;
                }

                if (c == '*')
                {
                    if (current.Length < 3
                        || (i + 1 < pattern.Length && char.IsLetterOrDigit(pattern[i + 1])))
                    {
                        valid = false;
                        return words;
                    }

                    AddPatternWord(words, current, true);
                    continue;
                }

                AddPatternWord(words, current, false);
            }

            AddPatternWord(words, current, false);
            return words;
        }

        private static void AddPatternWord(
            List<PatternWord> words, StringBuilder current, bool prefix)
        {
            if (current.Length == 0)
            {
                return;
            }

            words.Add(new PatternWord { text = current.ToString(), prefix = prefix });
            current.Length = 0;
        }

        private static List<string> Words(string text)
        {
            List<string> words = new List<string>();
            StringBuilder current = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c))
                {
                    current.Append(c);
                }
                else if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Length = 0;
                }
            }

            if (current.Length > 0)
            {
                words.Add(current.ToString());
            }

            return words;
        }
    }
}

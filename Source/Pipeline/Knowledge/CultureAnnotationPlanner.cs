// CultureAnnotationPlanner.cs — pure planning of inline culture annotations
// (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §4.3). Runs AFTER prompt-detail field selection and
// BEFORE final assembly: it looks only at fields that survived selection, detects at most two
// distinct topics from STRUCTURED data (context keys, stable schema markers, event defNames —
// never localized word forms), and appends one parenthetical annotation to the end of the first
// rendered field that triggered each topic.
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
        /// value carries a stable "marker=" schema token, or — for event-defName triggers — the
        /// first scannable field of the prompt (the event itself has no field of its own).
        /// </summary>
        private static int FirstTriggeringFieldIndex(
            CultureTopicRule topic, List<AnnotationFieldView> scannable, string eventDefName)
        {
            for (int i = 0; i < scannable.Count; i++)
            {
                AnnotationFieldView field = scannable[i];
                if (TriggersByContextKey(topic, field)
                    || TriggersByContextPair(topic, field)
                    || TriggersByValueMarker(topic, field))
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
                || !string.Equals(field.source, "GameContext", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(field.contextKey))
            {
                return false;
            }

            for (int i = 0; i < topic.triggerContextKeys.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(topic.triggerContextKeys[i])
                    && string.Equals(field.contextKey, topic.triggerContextKeys[i].Trim(),
                        StringComparison.OrdinalIgnoreCase))
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
                || !string.Equals(field.source, "GameContext", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(field.contextKey)
                || string.IsNullOrWhiteSpace(field.resolvedValue))
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

                if (string.Equals(field.contextKey, pair.Substring(0, equalsIndex).Trim(),
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(field.resolvedValue.Trim(),
                        pair.Substring(equalsIndex + 1).Trim(), StringComparison.OrdinalIgnoreCase))
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
}

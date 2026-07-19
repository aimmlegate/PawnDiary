// Pure Royal Ascent lifecycle and continuity policy. Runtime quest hooks copy the exact root,
// lifecycle signal, quest-instance identity, and tick into these detached values; this file decides
// ownership without touching Quest, Pawn, Map, DefDatabase, settings, or any paid-DLC object.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers" and "DLC-safety").
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Source-owned Royal Ascent phases saved as Narrative Continuity schema tokens.</summary>
    internal static class RoyalAscentPhaseTokens
    {
        public const string Started = "started";
        public const string Completed = "completed";
        public const string Failed = "failed";

        public static bool IsKnown(string value)
        {
            return value == Started || value == Completed || value == Failed;
        }
    }

    /// <summary>Plain facts copied from one canonical Quest lifecycle callback.</summary>
    internal sealed class RoyalAscentLifecycleFacts
    {
        public string questRootDefName = string.Empty;
        public string lifecycleSignal = string.Empty;
        public string correlationId = string.Empty;
        public int tick;
    }

    /// <summary>Pure ownership decision for one possible Royal Ascent lifecycle edge.</summary>
    internal sealed class RoyalAscentLifecycleDecision
    {
        public bool recognized;
        public bool opensWindow;
        public bool closesWindow;
        public bool emitsTerminalPage;
        public string phase = string.Empty;
        public string correlationId = string.Empty;
        public string arcKey = string.Empty;
    }

    /// <summary>
    /// Exact Royal Ascent ownership, bounded correlation identity, expiry, and migration policy.
    /// </summary>
    internal static class RoyalAscentPolicy
    {
        // Stable Def identity used to distinguish this saved event-window schema from generic windows.
        // Tunable behavior remains XML-owned; the Def name itself is a persistence/classification token.
        public const string WindowDefName = "RoyalAscent";

        /// <summary>True only when a detached Quest root matches the XML-owned Royal Ascent root.</summary>
        public static bool IsExactQuestRoot(string questRootDefName, RoyaltyPolicySnapshot policy)
        {
            return policy != null && !string.IsNullOrWhiteSpace(policy.royalAscentQuestDefName)
                && string.Equals(
                    Clean(questRootDefName),
                    Clean(policy.royalAscentQuestDefName),
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Recognizes only facts proven by Quest.Accept or Quest.End for the exact Royal Ascent root.
        /// Acceptance starts the window but owns no generic Quest page; Success/Fail own one terminal
        /// Quest page and close the window. No result represents arrival, boarding, or escape.
        /// </summary>
        public static RoyalAscentLifecycleDecision Evaluate(
            RoyalAscentLifecycleFacts facts,
            RoyaltyPolicySnapshot policy,
            bool royaltyActive)
        {
            RoyalAscentLifecycleDecision result = new RoyalAscentLifecycleDecision();
            if (!royaltyActive || facts == null || policy == null || !policy.enabled
                || !IsExactQuestRoot(facts.questRootDefName, policy))
            {
                return result;
            }

            string signal = Clean(facts.lifecycleSignal).ToLowerInvariant();
            if (signal == "accepted")
            {
                result.recognized = true;
                result.opensWindow = true;
                result.phase = RoyalAscentPhaseTokens.Started;
            }
            else if (signal == "completed" || signal == "failed")
            {
                result.recognized = true;
                result.closesWindow = true;
                result.emitsTerminalPage = true;
                result.phase = signal == "completed"
                    ? RoyalAscentPhaseTokens.Completed
                    : RoyalAscentPhaseTokens.Failed;
            }
            else
            {
                return result;
            }

            result.correlationId = NormalizeCorrelationId(
                facts.correlationId, policy.maximumRoyalAscentCorrelationCharacters);
            result.arcKey = BuildArcKey(result.correlationId, policy);
            return result;
        }

        /// <summary>
        /// True only for a current, bounded window whose saved root, correlation ID, and shared arc
        /// still agree. Missing Phase-7 fields on an old save therefore provide no inferred pressure.
        /// </summary>
        public static bool ActivePressureApplies(
            string startQuestRootDefName,
            string correlationId,
            string arcKey,
            int startedTick,
            int expiresTick,
            int now,
            RoyaltyPolicySnapshot policy,
            bool royaltyActive)
        {
            if (!royaltyActive || policy == null || !policy.enabled
                || startedTick < 0 || expiresTick <= startedTick || now < startedTick || now >= expiresTick
                || !string.Equals(
                    Clean(startQuestRootDefName),
                    Clean(policy.royalAscentQuestDefName),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string normalizedCorrelation = NormalizeCorrelationId(
                correlationId, policy.maximumRoyalAscentCorrelationCharacters);
            string expectedArc = BuildArcKey(normalizedCorrelation, policy);
            return normalizedCorrelation.Length > 0 && expectedArc.Length > 0
                && string.Equals(expectedArc, Clean(arcKey), StringComparison.Ordinal);
        }

        /// <summary>Returns a safe quest-instance token, or empty when malformed/oversized.</summary>
        public static string NormalizeCorrelationId(string value, int maximumCharacters)
        {
            string cleaned = Clean(value);
            if (maximumCharacters < 1 || cleaned.Length == 0 || cleaned.Length > maximumCharacters
                || cleaned.IndexOf('|') >= 0 || cleaned.IndexOf(';') >= 0
                || cleaned.IndexOf('\r') >= 0 || cleaned.IndexOf('\n') >= 0)
            {
                return string.Empty;
            }

            return cleaned;
        }

        /// <summary>Builds the XML-owned Royal Ascent arc grammar, or empty when unsafe.</summary>
        public static string BuildArcKey(string normalizedCorrelationId, RoyaltyPolicySnapshot policy)
        {
            if (policy == null)
            {
                return string.Empty;
            }

            string prefix = Clean(policy.royalAscentArcPrefix);
            string correlation = NormalizeCorrelationId(
                normalizedCorrelationId, policy.maximumRoyalAscentCorrelationCharacters);
            if (prefix.Length == 0 || prefix.IndexOf('|') >= 0 || prefix.IndexOf(';') >= 0
                || prefix.IndexOf('\r') >= 0 || prefix.IndexOf('\n') >= 0
                || correlation.Length == 0)
            {
                return string.Empty;
            }

            string result = prefix + "|" + correlation;
            return policy.maximumRoyalAscentArcCharacters < 1
                || result.Length > policy.maximumRoyalAscentArcCharacters
                ? string.Empty
                : result;
        }

        /// <summary>
        /// Builds one prose-free journey-chapter evidence row for a proven lifecycle edge. Invalid
        /// identity produces no row, so a canonical page can survive without invented continuity.
        /// </summary>
        public static List<NarrativeEvidence> JourneyEvidence(
            string eventId,
            int tick,
            string povPawnId,
            string povRole,
            RoyalAscentLifecycleDecision decision,
            string sourceDomain,
            string sourceDefName)
        {
            List<NarrativeEvidence> result = new List<NarrativeEvidence>();
            if (decision == null || !decision.recognized
                || !RoyalAscentPhaseTokens.IsKnown(decision.phase)
                || string.IsNullOrWhiteSpace(decision.arcKey)
                || string.IsNullOrWhiteSpace(povPawnId))
            {
                return result;
            }

            result.Add(new NarrativeEvidence
            {
                eventId = eventId ?? string.Empty,
                tick = tick,
                povPawnId = povPawnId.Trim(),
                povRole = povRole ?? string.Empty,
                facet = NarrativeFacetTokens.JourneyChapter,
                phase = decision.phase,
                subjectKind = NarrativeSubjectKindTokens.Colony,
                subjectId = "royal_ascent",
                arcKey = decision.arcKey,
                beliefTopics = new List<string> { "authority", "status", "duty", "hospitality" },
                salience = decision.emitsTerminalPage
                    ? NarrativeSalienceTokens.Terminal
                    : NarrativeSalienceTokens.Major,
                pawnCanKnow = true,
                sourceDomain = sourceDomain ?? string.Empty,
                sourceDefName = sourceDefName ?? string.Empty
            });
            return result;
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}

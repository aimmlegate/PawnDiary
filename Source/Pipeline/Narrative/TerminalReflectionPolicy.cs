// Pure Wave-12 qualification for source-owned terminal reflection connections. Runtime adapters
// supply only the canonical page identity, its saved NarrativeEvidence/NarrativeReference rows, and
// the exact source contract already proven by the owning callback. This file deliberately has no
// RimWorld, Verse, Unity, settings, Def, save, or transport dependency.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>The exact source-owned facts a terminal page must preserve before it may queue N4.</summary>
    internal sealed class TerminalReflectionContract
    {
        public bool ownershipCorrelated;
        public string phase = string.Empty;
        public string arcKey = string.Empty;
        public string sourceDomain = string.Empty;
        public string sourceDefName = string.Empty;
    }

    /// <summary>Detached canonical page plus its already-saved continuity rows.</summary>
    internal sealed class TerminalReflectionRequest
    {
        public string canonicalEventId = string.Empty;
        public int canonicalEventTick = -1;
        public string povPawnId = string.Empty;
        public string povRole = string.Empty;
        public TerminalReflectionContract contract;
        public List<NarrativeEvidence> evidence = new List<NarrativeEvidence>();
        public List<NarrativeReference> references = new List<NarrativeReference>();
    }

    /// <summary>Pure result: either one deferred non-recap owner ID or no opportunity.</summary>
    internal sealed class TerminalReflectionDecision
    {
        public bool queueMajorArc;
        public string avoidRelatedEventId = string.Empty;
    }

    /// <summary>
    /// Requires one exact terminal journey row and its matching saved reference. Generic DLC identity,
    /// current state, keywords, unrelated memories, and malformed partial evidence therefore cannot
    /// manufacture a reflection request.
    /// </summary>
    internal static class TerminalReflectionPolicy
    {
        public static TerminalReflectionDecision Evaluate(TerminalReflectionRequest request)
        {
            TerminalReflectionDecision result = new TerminalReflectionDecision();
            TerminalReflectionContract contract = request?.contract;
            if (request == null || contract == null || !contract.ownershipCorrelated
                || request.canonicalEventTick < 0
                || Empty(request.canonicalEventId) || Empty(request.povPawnId)
                || Empty(request.povRole) || Empty(contract.phase) || Empty(contract.arcKey)
                || Empty(contract.sourceDomain) || Empty(contract.sourceDefName)
                || request.evidence == null || request.references == null)
            {
                return result;
            }

            NarrativeEvidence matched = null;
            for (int i = 0; i < request.evidence.Count; i++)
            {
                NarrativeEvidence row = request.evidence[i];
                if (row != null
                    && row.eventId == request.canonicalEventId
                    && row.tick == request.canonicalEventTick
                    && row.povPawnId == request.povPawnId
                    && row.povRole == request.povRole
                    && row.facet == NarrativeFacetTokens.JourneyChapter
                    && row.phase == contract.phase
                    && row.arcKey == contract.arcKey
                    && row.salience == NarrativeSalienceTokens.Terminal
                    && row.pawnCanKnow == true
                    && row.sourceDomain == contract.sourceDomain
                    && row.sourceDefName == contract.sourceDefName)
                {
                    matched = row;
                    break;
                }
            }
            if (matched == null) return result;

            for (int i = 0; i < request.references.Count; i++)
            {
                NarrativeReference row = request.references[i];
                if (row != null
                    && row.sourceEventId == request.canonicalEventId
                    && row.sourceTick == request.canonicalEventTick
                    && row.facet == matched.facet
                    && row.phase == matched.phase
                    && row.subjectKind == matched.subjectKind
                    && row.subjectId == matched.subjectId
                    && row.arcKey == matched.arcKey)
                {
                    result.queueMajorArc = true;
                    result.avoidRelatedEventId = request.canonicalEventId;
                    return result;
                }
            }
            return result;
        }

        private static bool Empty(string value)
        {
            return string.IsNullOrWhiteSpace(value);
        }
    }

    /// <summary>
    /// Decides whether one terminal-owned major-arc request may survive an ineligible rest check.
    /// Ordinary major events have no canonical owner ID and retain the original one-check behavior.
    /// </summary>
    internal static class TerminalReflectionRetryPolicy
    {
        public static bool CanRetry(
            bool majorEventTrigger,
            string canonicalOwnerEventId,
            int requestedTick,
            int currentTick,
            int maximumAgeTicks)
        {
            if (!majorEventTrigger
                || string.IsNullOrWhiteSpace(canonicalOwnerEventId)
                || requestedTick < 0
                || currentTick < 0
                || maximumAgeTicks <= 0)
            {
                return false;
            }

            return !IsExpired(
                canonicalOwnerEventId,
                requestedTick,
                currentTick,
                maximumAgeTicks);
        }

        public static bool IsExpired(
            string canonicalOwnerEventId,
            int requestedTick,
            int currentTick,
            int maximumAgeTicks)
        {
            if (string.IsNullOrWhiteSpace(canonicalOwnerEventId)
                || requestedTick < 0
                || currentTick < 0
                || maximumAgeTicks <= 0)
            {
                return false;
            }

            // A defensive clock rewind must not discard valid saved debt. Treat it as age zero; the
            // normal game clock will catch up and the same configured ceiling remains authoritative.
            long age = Math.Max(0L, (long)currentTick - requestedTick);
            return age > maximumAgeTicks;
        }
    }
}

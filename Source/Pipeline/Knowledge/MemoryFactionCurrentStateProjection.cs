// MemoryFactionCurrentStateProjection.cs — joins an owner's known faction to exact saved diplomacy.
//
// Faction awareness stores the owner's connection, while diplomacy is shared once per faction
// instance/generation. This pure projection requires both; labels never authorize or identify a
// lookup. All emitted words are stable structured schema tokens, not localized prompt prose.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PawnDiary
{
    /// <summary>Builds bounded current truth without exposing an unknown faction or partial facts.</summary>
    internal static class MemoryFactionCurrentStateProjection
    {
        /// <summary>
        /// Resolves exactly one tracked faction under the owner's awareness gate. Missing, duplicate,
        /// stale-generation, corrupt, and over-budget state returns no text or provenance.
        /// </summary>
        public static bool TryProject(
            KnowledgeAwarenessState awareness,
            IEnumerable<KnowledgeFactionState> factions,
            int maximumFacts,
            int maximumCharacters,
            out string text,
            out string sourceId)
        {
            text = string.Empty;
            sourceId = string.Empty;
            string instanceId;
            long generation;
            if (awareness == null
                || awareness.scopeKindToken != KnowledgeObservationTokens.ScopeFaction
                || awareness.subjectKind != KnowledgeObservationTokens.SubjectFaction
                || awareness.factStreamToken != KnowledgeObservationTokens.StreamFactionConnection
                || awareness.trackingStateToken != KnowledgeObservationTokens.TrackingTracked
                || !MemoryIdentityCodec.TryParseFactionSubjectId(
                    awareness.subjectId, out instanceId, out generation)
                || awareness.stateFacts == null || awareness.stateFacts.Count != 1
                || awareness.stateFacts[0]?.key != KnowledgeObservationTokens.FactConnectionKind)
                return false;
            string connection = awareness.stateFacts[0].value;
            if (connection != KnowledgeObservationTokens.ConnectionCurrent
                && connection != KnowledgeObservationTokens.ConnectionRecentFormer
                && connection != KnowledgeObservationTokens.ConnectionFamily) return false;

            KnowledgeFactionState match = null;
            foreach (KnowledgeFactionState row in factions ?? new KnowledgeFactionState[0])
            {
                if (row == null || row.factionInstanceId != instanceId
                    || row.allocatorGeneration != generation) continue;
                if (match != null) return false;
                match = row;
            }
            if (match == null
                || match.trackingStateToken != KnowledgeObservationTokens.TrackingTracked
                || match.snapshotRevision <= 0 || match.snapshotRevision == long.MaxValue
                || match.observedTick < 0 || match.goodwill < -100 || match.goodwill > 100
                || !KnowledgeRelationPolicy.IsKnownFactionRelationKind(match.relationKindToken))
                return false;

            string leader = match.leaderPawnId ?? string.Empty;
            if (leader.Length > MemoryIdentityCodec.MaximumRawIdentityCharacters
                || !MemoryIdentityCodec.IsWellFormedUtf16(leader)
                || !string.Equals(leader, PromptTextSanitizer.OneLine(leader), StringComparison.Ordinal))
                return false;
            // Six schema fields travel together: omitting a terminal state or leader could leave
            // contradictory historical wording authoritative. XML limits may decline the whole line.
            if (maximumFacts < 6) return false;
            string projection = "current_state: faction_state relation_kind=" + match.relationKindToken
                + "; goodwill=" + match.goodwill.ToString(CultureInfo.InvariantCulture)
                + "; defeated=" + (match.defeated ? "true" : "false")
                + "; removed=" + (match.removed ? "true" : "false")
                + "; leader=" + (leader.Length == 0 ? "none" : leader)
                + "; connection_kind=" + connection;
            if (projection.Length > maximumCharacters) return false;
            text = projection;
            // The global saved row is keyed by this exact tuple, independently of its display name.
            sourceId = awareness.subjectId;
            return true;
        }
    }
}

// MemoryFactionRecallTests.cs — standalone regressions for known-faction current-truth projection.
// Uses plain snapshots only: no game, provider, Scribe, Verse, or Unity runtime is needed.
using System;
using System.Collections.Generic;
using PawnDiary;

namespace PawnMemoryTests
{
    /// <summary>Checks exact identity, privacy, terminal state, provenance, and complete text budgets.</summary>
    internal static class MemoryFactionRecallTests
    {
        public static int Run()
        {
            int count = 0;
            Action<bool> check = passed =>
            {
                count++;
                if (!passed) throw new InvalidOperationException("Faction Recall assertion " + count);
            };
            string subject;
            check(MemoryIdentityCodec.TryCreateFactionSubjectId("Faction_7", 2, out subject));
            var known = new KnowledgeAwarenessState
            {
                subjectKind = KnowledgeObservationTokens.SubjectFaction,
                subjectId = subject,
                scopeKindToken = KnowledgeObservationTokens.ScopeFaction,
                factStreamToken = KnowledgeObservationTokens.StreamFactionConnection,
                trackingStateToken = KnowledgeObservationTokens.TrackingTracked,
                stateFacts = new List<KnowledgeStateFact>
                {
                    new KnowledgeStateFact { key = KnowledgeObservationTokens.FactConnectionKind,
                        value = KnowledgeObservationTokens.ConnectionFamily }
                }
            };
            var current = new KnowledgeFactionState
            {
                factionInstanceId = "Faction_7", allocatorGeneration = 2,
                relationKindToken = "Hostile", goodwill = -90, leaderPawnId = "Pawn_Leader",
                trackingStateToken = KnowledgeObservationTokens.TrackingTracked,
                snapshotRevision = 2, observedTick = 100
            };
            var unrelated = new KnowledgeFactionState
            {
                factionInstanceId = "Faction_8", allocatorGeneration = 2,
                factionDefName = current.factionDefName,
                frozenDisplayLabel = current.frozenDisplayLabel,
                relationKindToken = "Ally", goodwill = 100
            };
            var rows = new List<KnowledgeFactionState> { unrelated, current };
            string text;
            string source;
            check(MemoryFactionCurrentStateProjection.TryProject(known, rows, 8, 240, out text, out source));
            check(text.Contains("relation_kind=Hostile") && text.Contains("goodwill=-90"));
            check(text.Contains("leader=Pawn_Leader") && text.Contains("connection_kind=family"));
            check(source == subject && !text.Contains("Ally"));
            int exactLength = text.Length;
            check(MemoryFactionCurrentStateProjection.TryProject(known, rows, 6, exactLength, out text, out source));
            check(!MemoryFactionCurrentStateProjection.TryProject(known, rows, 6, exactLength - 1, out text, out source));
            check(text.Length == 0 && source.Length == 0);
            check(!MemoryFactionCurrentStateProjection.TryProject(known, rows, 5, 240, out text, out source));

            current.defeated = true;
            current.removed = true;
            current.leaderPawnId = string.Empty;
            check(MemoryFactionCurrentStateProjection.TryProject(known, rows, 8, 240, out text, out source));
            check(text.Contains("defeated=true") && text.Contains("removed=true") && text.Contains("leader=none"));
            current.allocatorGeneration = 3;
            check(!MemoryFactionCurrentStateProjection.TryProject(known, rows, 8, 240, out text, out source));
            current.allocatorGeneration = 2;
            rows.Add(current);
            check(!MemoryFactionCurrentStateProjection.TryProject(known, rows, 8, 240, out text, out source));
            rows.RemoveAt(rows.Count - 1);
            current.trackingStateToken = KnowledgeObservationTokens.TrackingCapacityUntracked;
            check(!MemoryFactionCurrentStateProjection.TryProject(known, rows, 8, 240, out text, out source));
            current.trackingStateToken = KnowledgeObservationTokens.TrackingTracked;
            known.trackingStateToken = KnowledgeObservationTokens.TrackingCapacityUntracked;
            check(!MemoryFactionCurrentStateProjection.TryProject(known, rows, 8, 240, out text, out source));
            known.trackingStateToken = KnowledgeObservationTokens.TrackingTracked;
            check(!MemoryFactionCurrentStateProjection.TryProject(null, rows, 8, 240, out text, out source));
            check(!MemoryFactionCurrentStateProjection.TryProject(known, null, 8, 240, out text, out source));
            current.goodwill = 101;
            check(!MemoryFactionCurrentStateProjection.TryProject(known, rows, 8, 240, out text, out source));
            current.goodwill = -90;
            current.relationKindToken = "unknown";
            check(!MemoryFactionCurrentStateProjection.TryProject(known, rows, 8, 240, out text, out source));
            return count;
        }
    }
}

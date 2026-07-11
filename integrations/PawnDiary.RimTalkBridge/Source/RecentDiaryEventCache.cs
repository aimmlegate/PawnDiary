// Thread-safe recent native/other-adapter diary-event cache for RimTalk conversation assessment.
// Pawn Diary status notifications seed pending/completed facts, and the main-thread coordinator
// enriches them from existing GetContextSnapshot reads before scoring/submission. Bridge-authored
// conversation entries are excluded so the selector cannot recursively cite its own output.
//
// The cache stores only plain strings/ints. Its status callback and reads still share a lock because
// integration listeners must not assume they always run on the same thread.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Generic;
using PawnDiary.Integration;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Collects bounded recent diary-event facts by pawn id and event id.</summary>
    internal static class RecentDiaryEventCache
    {
        // Defensive emergency bound independent of XML. Normal reads prune much more tightly using
        // recentEventCount/window; this protects long Level-2 stretches with no nominated candidate.
        private const int HardEventsPerPawn = 64;

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Dictionary<string, CachedEvent>> ByPawn =
            new Dictionary<string, Dictionary<string, CachedEvent>>(StringComparer.Ordinal);

        /// <summary>Registers the bridge's third frozen status-listener id.</summary>
        public static void Register()
        {
            PawnDiaryApi.RegisterEntryStatusListener(BridgeIds.AssessmentStatusListenerId, OnEntryStatus);
        }

        /// <summary>
        /// MAIN THREAD: merges completed prose snapshots for a pawn into the status-seeded cache.
        /// Existing completed title/summary values win over sparse pending notifications.
        /// </summary>
        public static void EnrichForPawn(Pawn pawn, int maxEntries)
        {
            if (pawn == null || maxEntries <= 0)
            {
                return;
            }

            DiaryContextSnapshot snapshot = PawnDiaryApi.GetContextSnapshot(pawn, maxEntries);
            if (snapshot == null || snapshot.entries == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            for (int i = 0; i < snapshot.entries.Count; i++)
            {
                DiaryEntryProseSnapshot entry = snapshot.entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.eventId)
                    || IsBridgeSource(entry.externalSourceId))
                {
                    continue;
                }

                Merge(pawnId, new RecentDiaryEvent
                {
                    EventId = entry.eventId,
                    Tick = entry.tick,
                    PawnId = pawnId,
                    GroupLabel = entry.groupLabel,
                    Domain = entry.domain,
                    Title = entry.title,
                    Summary = entry.summary,
                    ExternalSourceId = entry.externalSourceId
                }, true);
            }
        }

        /// <summary>Returns merged recent events for either pawn, newest first and XML-bounded.</summary>
        public static List<RecentDiaryEvent> ForPair(
            string firstPawnId,
            string secondPawnId,
            int nowTick,
            int maxEvents,
            int windowTicks)
        {
            List<RecentDiaryEvent> result = new List<RecentDiaryEvent>();
            if (maxEvents <= 0 || windowTicks <= 0)
            {
                return result;
            }

            lock (Gate)
            {
                PruneAll(nowTick, windowTicks);
                PruneAllToCount(maxEvents);
                Dictionary<string, RecentDiaryEvent> byEvent =
                    new Dictionary<string, RecentDiaryEvent>(StringComparer.Ordinal);
                MergePawnInto(firstPawnId, byEvent);
                if (secondPawnId != firstPawnId)
                {
                    MergePawnInto(secondPawnId, byEvent);
                }

                result.AddRange(byEvent.Values);
            }

            result.Sort(delegate(RecentDiaryEvent left, RecentDiaryEvent right)
            {
                int tick = right.Tick.CompareTo(left.Tick);
                return tick != 0 ? tick : string.CompareOrdinal(left.EventId, right.EventId);
            });
            if (result.Count > maxEvents)
            {
                result.RemoveRange(maxEvents, result.Count - maxEvents);
            }

            return result;
        }

        /// <summary>Clears process-global cache state on FinalizeInit.</summary>
        public static void ResetForNewGame()
        {
            Clear();
        }

        /// <summary>Clears cached assessment facts when Level 2 becomes a no-flow boundary.</summary>
        public static void Clear()
        {
            lock (Gate)
            {
                ByPawn.Clear();
            }
        }

        private static void OnEntryStatus(DiaryEntryStatusSnapshot snapshot)
        {
            if (!PawnDiaryRimTalkBridgeMod.LevelAtLeast(2)
                || snapshot == null || snapshot.handle == null || (!snapshot.pending && !snapshot.complete))
            {
                return;
            }

            string pawnId = snapshot.handle.pawnId ?? string.Empty;
            string eventId = snapshot.handle.eventId ?? string.Empty;
            if (pawnId.Length == 0 || eventId.Length == 0)
            {
                return;
            }

            if (IsBridgeSource(snapshot.externalSourceId))
            {
                Remove(pawnId, eventId);
                return;
            }

            Merge(pawnId, new RecentDiaryEvent
            {
                EventId = eventId,
                Tick = snapshot.tick,
                PawnId = pawnId,
                GroupLabel = snapshot.groupLabel,
                Domain = snapshot.domain,
                Title = snapshot.title,
                Summary = snapshot.summary,
                ExternalSourceId = snapshot.externalSourceId
            }, snapshot.complete);
        }

        private static void Merge(string pawnId, RecentDiaryEvent incoming, bool complete)
        {
            if (string.IsNullOrEmpty(pawnId) || incoming == null || string.IsNullOrEmpty(incoming.EventId))
            {
                return;
            }

            lock (Gate)
            {
                Dictionary<string, CachedEvent> byEvent;
                if (!ByPawn.TryGetValue(pawnId, out byEvent))
                {
                    byEvent = new Dictionary<string, CachedEvent>(StringComparer.Ordinal);
                    ByPawn[pawnId] = byEvent;
                }

                CachedEvent existing;
                if (!byEvent.TryGetValue(incoming.EventId, out existing))
                {
                    byEvent[incoming.EventId] = new CachedEvent
                    {
                        Value = Clone(incoming),
                        Complete = complete
                    };
                }
                else
                {
                    // A completed prose snapshot is richer than pending group-only status. Once rich
                    // title/summary exists, a later sparse pending-like update cannot erase it.
                    existing.Value.Tick = incoming.Tick != 0 ? incoming.Tick : existing.Value.Tick;
                    existing.Value.GroupLabel = Prefer(incoming.GroupLabel, existing.Value.GroupLabel);
                    existing.Value.Domain = Prefer(incoming.Domain, existing.Value.Domain);
                    existing.Value.ExternalSourceId = Prefer(incoming.ExternalSourceId, existing.Value.ExternalSourceId);
                    if (complete || !existing.Complete)
                    {
                        existing.Value.Title = Prefer(incoming.Title, existing.Value.Title);
                        existing.Value.Summary = Prefer(incoming.Summary, existing.Value.Summary);
                    }

                    existing.Complete = existing.Complete || complete;
                }

                TrimHardBound(byEvent);
            }
        }

        private static void MergePawnInto(string pawnId, Dictionary<string, RecentDiaryEvent> destination)
        {
            if (string.IsNullOrEmpty(pawnId))
            {
                return;
            }

            Dictionary<string, CachedEvent> source;
            if (!ByPawn.TryGetValue(pawnId, out source))
            {
                return;
            }

            foreach (KeyValuePair<string, CachedEvent> pair in source)
            {
                RecentDiaryEvent existing;
                if (!destination.TryGetValue(pair.Key, out existing)
                    || Richness(pair.Value.Value) > Richness(existing))
                {
                    destination[pair.Key] = Clone(pair.Value.Value);
                }
            }
        }

        private static void PruneAll(int nowTick, int windowTicks)
        {
            List<string> emptyPawns = null;
            foreach (KeyValuePair<string, Dictionary<string, CachedEvent>> pawnPair in ByPawn)
            {
                List<string> expired = null;
                foreach (KeyValuePair<string, CachedEvent> eventPair in pawnPair.Value)
                {
                    if (nowTick - eventPair.Value.Value.Tick > windowTicks)
                    {
                        if (expired == null)
                        {
                            expired = new List<string>();
                        }

                        expired.Add(eventPair.Key);
                    }
                }

                if (expired != null)
                {
                    for (int i = 0; i < expired.Count; i++)
                    {
                        pawnPair.Value.Remove(expired[i]);
                    }
                }

                if (pawnPair.Value.Count == 0)
                {
                    if (emptyPawns == null)
                    {
                        emptyPawns = new List<string>();
                    }

                    emptyPawns.Add(pawnPair.Key);
                }
            }

            if (emptyPawns != null)
            {
                for (int i = 0; i < emptyPawns.Count; i++)
                {
                    ByPawn.Remove(emptyPawns[i]);
                }
            }
        }

        private static void TrimHardBound(Dictionary<string, CachedEvent> byEvent)
        {
            TrimToCount(byEvent, HardEventsPerPawn);
        }

        private static void PruneAllToCount(int maxEvents)
        {
            foreach (KeyValuePair<string, Dictionary<string, CachedEvent>> pair in ByPawn)
            {
                TrimToCount(pair.Value, maxEvents);
            }
        }

        private static void TrimToCount(Dictionary<string, CachedEvent> byEvent, int maxEvents)
        {
            while (byEvent.Count > maxEvents)
            {
                string oldestId = null;
                int oldestTick = int.MaxValue;
                foreach (KeyValuePair<string, CachedEvent> pair in byEvent)
                {
                    if (oldestId == null
                        || pair.Value.Value.Tick < oldestTick
                        || (pair.Value.Value.Tick == oldestTick
                            && string.CompareOrdinal(pair.Key, oldestId) < 0))
                    {
                        oldestId = pair.Key;
                        oldestTick = pair.Value.Value.Tick;
                    }
                }

                if (oldestId == null)
                {
                    break;
                }

                byEvent.Remove(oldestId);
            }
        }

        private static void Remove(string pawnId, string eventId)
        {
            lock (Gate)
            {
                Dictionary<string, CachedEvent> byEvent;
                if (ByPawn.TryGetValue(pawnId, out byEvent))
                {
                    byEvent.Remove(eventId);
                    if (byEvent.Count == 0)
                    {
                        ByPawn.Remove(pawnId);
                    }
                }
            }
        }

        private static bool IsBridgeSource(string sourceId)
        {
            return !string.IsNullOrEmpty(sourceId)
                && string.Equals(sourceId, BridgeIds.ModId, StringComparison.OrdinalIgnoreCase);
        }

        private static string Prefer(string incoming, string existing)
        {
            return string.IsNullOrWhiteSpace(incoming) ? existing ?? string.Empty : incoming;
        }

        private static int Richness(RecentDiaryEvent value)
        {
            if (value == null)
            {
                return 0;
            }

            return (string.IsNullOrWhiteSpace(value.Title) ? 0 : 1)
                + (string.IsNullOrWhiteSpace(value.Summary) ? 0 : 1)
                + (string.IsNullOrWhiteSpace(value.GroupLabel) ? 0 : 1);
        }

        private static RecentDiaryEvent Clone(RecentDiaryEvent value)
        {
            return new RecentDiaryEvent
            {
                EventId = value.EventId,
                Tick = value.Tick,
                PawnId = value.PawnId,
                GroupLabel = value.GroupLabel,
                Domain = value.Domain,
                Title = value.Title,
                Summary = value.Summary,
                ExternalSourceId = value.ExternalSourceId
            };
        }

        private sealed class CachedEvent
        {
            public RecentDiaryEvent Value;
            public bool Complete;
        }
    }
}

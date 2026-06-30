// The event store for the diary system. Owns the saved master list of DiaryEvents and the O(1)
// id->event lookup index that mirrors it. Every event-creation path funnels through Register and
// every id lookup through FindEvent, so reads stay constant-time instead of linear-scanning the
// whole history (the diary tab redraws every frame and the arrival/death boundary checks loop a
// pawn's events, so a linear scan made those hot paths grow quadratically with colony history).
//
// This is plain persisted state extracted out of DiaryGameComponent (Run Card 10,
// ARCHITECTURE_IMPROVEMENT_PLAN.md) so the event store has one clear owner. DiaryGameComponent
// stays the only RimWorld lifecycle/save owner: it constructs this repository, delegates the saved
// list to ExposeEvents, and rebuilds the index from its own PostLoadInit. The lookup index itself
// is never serialized — it is rebuilt from the master list after load (RebuildIndex) and kept in
// sync on add/remove.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable" / Scribe). Scribe_Collections.Look is RimWorld's
// save/load helper for collections; LookMode.Deep means "each element saves/loads itself via its own
// ExposeData". A `ref` parameter is required by Look so it can substitute a loaded list for the
// field — that is why the list lives here (only its declaring type can pass it by ref).
using System;
using System.Collections;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// The saved store of every <see cref="DiaryEvent"/> across all pawns, plus the O(1) id->event
    /// lookup index that mirrors it. Constructed and owned by <see cref="DiaryGameComponent"/>, which
    /// remains the sole RimWorld lifecycle/save owner and drives serialization via
    /// <see cref="ExposeEvents"/> and index rebuild via <see cref="RebuildIndex"/>.
    /// </summary>
    internal sealed class DiaryEventRepository
    {
        // All diary events across every pawn, in tick/insertion order. Persisted via ExposeEvents.
        private List<DiaryEvent> diaryEvents = new List<DiaryEvent>();

        // O(1) lookup index (eventId -> DiaryEvent) mirroring diaryEvents. NOT saved: rebuilt from
        // diaryEvents after load (RebuildIndex) and kept in sync as events are created (Register) or
        // removed (RemoveEvent / RemoveEvents). FindEvent is called inside per-event loops, so the
        // index keeps those lookups constant-time instead of growing with colony history.
        // Ordinal-ignore-case so this index agrees with the retention sweep's referenced-id set
        // (CollectHotReferencedEventIds uses OrdinalIgnoreCase). Event ids are lowercase GUIDs today, so
        // this only matters defensively, but the two halves of the same lookup must use one comparer.
        private readonly Dictionary<string, DiaryEvent> eventsById = new Dictionary<string, DiaryEvent>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The number of stored events.</summary>
        public int Count
        {
            get { return diaryEvents.Count; }
        }

        /// <summary>
        /// Read-only view of the master list in tick/insertion order. Use this for scans that need
        /// the full history (continuity summaries, day-signal collection, generated-speech pruning).
        /// It is never null: the store guarantees a non-null list across new game, load, and
        /// PostLoadInit (see <see cref="ExposeEvents"/>), so callers can drop the old null guards.
        /// </summary>
        public IReadOnlyList<DiaryEvent> AllEvents
        {
            get { return diaryEvents; }
        }

        /// <summary>
        /// Read-only view over the newest <paramref name="maxEvents"/> events. This is the "hot"
        /// maintenance window: old archived entries stay saved and visible through <see cref="AllEvents"/>,
        /// but background catch-up scans can stay bounded.
        /// </summary>
        public IReadOnlyList<DiaryEvent> MostRecentEvents(int maxEvents)
        {
            if (maxEvents <= 0)
            {
                return EmptyRecentEvents.List;
            }

            if (diaryEvents.Count <= maxEvents)
            {
                return diaryEvents;
            }

            return new RecentEventList(diaryEvents, diaryEvents.Count - maxEvents, maxEvents);
        }

        /// <summary>
        /// Locates a <see cref="DiaryEvent"/> by its unique ID via the O(1) lookup index, or null if
        /// the id is blank or unknown.
        /// </summary>
        /// <param name="eventId">The <see cref="DiaryEvent.eventId"/> to look up.</param>
        public DiaryEvent FindEvent(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return null;
            }

            DiaryEvent diaryEvent;
            return eventsById.TryGetValue(eventId, out diaryEvent) ? diaryEvent : null;
        }

        /// <summary>True when an event with the given id is currently in the store.</summary>
        public bool ContainsEvent(string eventId)
        {
            return !string.IsNullOrWhiteSpace(eventId) && eventsById.ContainsKey(eventId);
        }

        /// <summary>
        /// Adds a freshly created event to both the master list and the lookup index, keeping the two
        /// in sync. Null events are ignored. First registration of an id wins (matching the old
        /// load behavior); ids are GUIDs, so duplicate ids are not expected, but keeping the first
        /// preserves the previous semantics if any ever occur.
        /// </summary>
        public void Register(DiaryEvent diaryEvent)
        {
            if (diaryEvent == null)
            {
                return;
            }

            diaryEvents.Add(diaryEvent);
            if (!string.IsNullOrWhiteSpace(diaryEvent.eventId) && !eventsById.ContainsKey(diaryEvent.eventId))
            {
                eventsById[diaryEvent.eventId] = diaryEvent;
            }
        }

        /// <summary>
        /// Removes a single event by id from the list and the index. No-op if the id is blank or not
        /// present. The caller is responsible for scrubbing per-pawn diary references afterwards.
        /// Uses the O(1) id index to resolve the event (so an absent id short-circuits without
        /// scanning history and without allocating a predicate closure); the bulk path that may touch
        /// several ids stays in <see cref="RemoveEvents"/>.
        /// </summary>
        public void RemoveEvent(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return;
            }

            if (!eventsById.TryGetValue(eventId, out DiaryEvent diaryEvent) || diaryEvent == null)
            {
                return;
            }

            diaryEvents.Remove(diaryEvent);
            eventsById.Remove(eventId);
        }

        /// <summary>
        /// Removes every event whose id is in <paramref name="eventIds"/> from the list and the index.
        /// Used by the dev prompt-suite reset to drop synthetic test events in one pass. The caller is
        /// responsible for scrubbing per-pawn diary references afterwards.
        /// </summary>
        public void RemoveEvents(HashSet<string> eventIds)
        {
            if (eventIds == null || eventIds.Count == 0)
            {
                return;
            }

            diaryEvents.RemoveAll(e => e != null && eventIds.Contains(e.eventId));
            foreach (string id in eventIds)
            {
                eventsById.Remove(id);
            }
        }

        /// <summary>
        /// Keeps only events whose id is in <paramref name="referencedIds"/> and drops the rest —
        /// master-list events that no pawn references anymore — then rebuilds the id lookup index to
        /// match. Used by the per-pawn retention sweep, which caps each pawn's refs first and passes
        /// the union of surviving refs here. Null/blank-id entries are dropped as cleanup.
        /// </summary>
        /// <param name="referencedIds">Ids still referenced by at least one pawn's diary.</param>
        /// <returns>The number of unreferenced events removed.</returns>
        public int RetainOnly(HashSet<string> referencedIds)
        {
            if (referencedIds == null)
            {
                return 0;
            }

            int before = diaryEvents.Count;
            diaryEvents.RemoveAll(e => e == null
                || string.IsNullOrWhiteSpace(e.eventId)
                || !referencedIds.Contains(e.eventId));
            int removed = before - diaryEvents.Count;
            if (removed > 0)
            {
                RebuildIndex();
            }

            return removed;
        }

        /// <summary>
        /// Rebuilds the id->event index from the master list. Called once after a save loads (the
        /// index is never serialized). First occurrence wins, matching the old linear FindEvent scan —
        /// duplicate ids are not expected (eventIds are GUIDs) but this keeps behavior identical if
        /// any occur, and it tolerates null entries from odd saves.
        /// </summary>
        public void RebuildIndex()
        {
            eventsById.Clear();
            for (int i = 0; i < diaryEvents.Count; i++)
            {
                DiaryEvent diaryEvent = diaryEvents[i];
                if (diaryEvent == null || string.IsNullOrWhiteSpace(diaryEvent.eventId))
                {
                    continue;
                }

                if (!eventsById.ContainsKey(diaryEvent.eventId))
                {
                    eventsById[diaryEvent.eventId] = diaryEvent;
                }
            }
        }

        /// <summary>
        /// Defensively rebuilds the index only if a caller needs it (e.g. a prune) before the normal
        /// PostLoadInit rebuild has run. In the usual load path the index is rebuilt unconditionally,
        /// so this is a no-op there; it only does work when the index is empty but the master list is
        /// not.
        /// </summary>
        public void EnsureIndexReady()
        {
            if (eventsById.Count == 0 && diaryEvents.Count > 0)
            {
                RebuildIndex();
            }
        }

        /// <summary>
        /// Serializes the master event list under the given Scribe key. The lookup index is never
        /// saved. On PostLoadInit a missing/null list (odd/corrupt save) is restored to empty so the
        /// rest of the load path — and every caller of <see cref="AllEvents"/> — sees a non-null store.
        /// </summary>
        /// <param name="label">The Scribe key; must stay "diaryEvents" to read existing saves.</param>
        public void ExposeEvents(string label)
        {
            Scribe_Collections.Look(ref diaryEvents, label, LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && diaryEvents == null)
            {
                diaryEvents = new List<DiaryEvent>();
            }
        }

        private static class EmptyRecentEvents
        {
            public static readonly IReadOnlyList<DiaryEvent> List = new DiaryEvent[0];
        }

        private sealed class RecentEventList : IReadOnlyList<DiaryEvent>
        {
            private readonly List<DiaryEvent> source;
            private readonly int start;
            private readonly int count;

            public RecentEventList(List<DiaryEvent> source, int start, int count)
            {
                this.source = source;
                this.start = start;
                this.count = count;
            }

            public int Count
            {
                get { return count; }
            }

            public DiaryEvent this[int index]
            {
                get { return source[start + index]; }
            }

            public IEnumerator<DiaryEvent> GetEnumerator()
            {
                for (int i = 0; i < count; i++)
                {
                    yield return source[start + i];
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}

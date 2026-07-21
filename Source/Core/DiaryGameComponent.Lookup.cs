// Lookups and bookkeeping over the saved data: cross-referencing events onto pawn diary records,
// finding events/records by id, the "arrival first / death last" rules that make a colonist's
// arrival and death entries hard diary boundaries, per-pawn eligibility checks, the dedup gate
// shared by every Record* hook, writing-style seeding/defaulting, and the shared empty-list singleton.
// These are the small, mostly-pure helpers the other partial files lean on.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // One dedup entry: the tick the key was recorded AND the source's own window at that moment.
        // Storing the window per-key (rather than borrowing the current caller's window) is what lets
        // the shared store hold short- and long-window sources side by side: a prune sweep driven by a
        // 300-tick source can no longer evict a still-live 60000-tick hediff key. See RecentEventExpiry
        // for the pure policy these helpers defer to.
        private struct RecentEventEntry
        {
            public int tick;
            public int windowTicks;
        }

        /// <summary>
        /// Dedup gate: returns true if the same key was recorded within the given tick window, preventing
        /// duplicate events (e.g. mirrored SocialFighting calls). Combines check-then-mark for callers
        /// that decide and emit in one step; callers that need to draw impure state (e.g. an ability's
        /// RNG roll) between the check and the mark call <see cref="IsRecentlyRecorded"/> and
        /// <see cref="MarkRecentlyRecorded"/> separately.
        /// </summary>
        private bool RecentlyRecorded(Dictionary<string, RecentEventEntry> recentEvents, string key, int windowTicks)
        {
            if (IsRecentlyRecorded(recentEvents, key, windowTicks))
            {
                return true;
            }

            MarkRecentlyRecorded(recentEvents, key, windowTicks);
            return false;
        }

        private bool IsRecentlyRecorded(Dictionary<string, RecentEventEntry> recentEvents, string key, int windowTicks)
        {
            // A zero/negative window means "this source opted out of dedup": do not check, do not mark,
            // do not prune. Without this guard a zero-window source would have wiped the whole shared
            // store on its first prune (see RecentEventExpiry.IsWithinWindow).
            if (recentEvents == null || string.IsNullOrEmpty(key) || windowTicks <= 0)
            {
                return false;
            }

            int now = Find.TickManager.TicksGame;
            PruneRecentEvents(recentEvents, now);

            RecentEventEntry entry;
            return recentEvents.TryGetValue(key, out entry)
                && RecentEventExpiry.IsWithinWindow(entry.tick, windowTicks, now);
        }

        private void MarkRecentlyRecorded(Dictionary<string, RecentEventEntry> recentEvents, string key, int windowTicks)
        {
            if (recentEvents == null || string.IsNullOrEmpty(key) || windowTicks <= 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            PruneRecentEvents(recentEvents, now);
            recentEvents[key] = new RecentEventEntry { tick = now, windowTicks = windowTicks };
        }

        /// <summary>
        /// Removes dedup keys that are older than their OWN useful window. The stores are transient and
        /// only answer "did this just happen?", so old keys should not survive a long play session.
        /// Each entry carries the window it was recorded with, so a prune triggered by one source only
        /// ever evicts entries that have actually expired by their own source's window — never a
        /// longer-window key swept out early by a shorter-window caller.
        /// </summary>
        private static void PruneRecentEvents(Dictionary<string, RecentEventEntry> recentEvents, int now)
        {
            if (recentEvents == null || recentEvents.Count < RecentEventPruneThreshold)
            {
                return;
            }

            List<string> expiredKeys = null;
            foreach (KeyValuePair<string, RecentEventEntry> pair in recentEvents)
            {
                if (RecentEventExpiry.IsExpired(pair.Value.tick, pair.Value.windowTicks, now))
                {
                    if (expiredKeys == null)
                    {
                        expiredKeys = new List<string>();
                    }

                    expiredKeys.Add(pair.Key);
                }
            }

            if (expiredKeys == null)
            {
                return;
            }

            for (int i = 0; i < expiredKeys.Count; i++)
            {
                recentEvents.Remove(expiredKeys[i]);
            }
        }

        /// <summary>
        /// Checks whether a pawn is a humanlike (as opposed to an animal or mechanoid).
        /// </summary>
        private static bool IsHumanlike(Pawn pawn)
        {
            return pawn != null && pawn.RaceProps != null && pawn.RaceProps.Humanlike;
        }

        /// <summary>
        /// A pawn qualifies for diary tracking if it is a humanlike colonist old enough to write
        /// first-person entries. Pre-teen colonists can still appear as "other pawn" context.
        /// </summary>
        // internal: part of the DiarySignal capture surface (signals snapshot eligibility into CaptureContext).
        internal static bool IsDiaryEligible(Pawn pawn)
        {
            return IsHumanlike(pawn) && pawn.IsColonist && IsFirstPersonDiaryAgeEligible(pawn);
        }

        /// <summary>
        /// A pawn can receive a neutral death-description page if it is a humanlike colonist — even
        /// post-mortem, when IsDiaryEligible no longer holds. Shared by the Tale death-description path
        /// and the Death fallback path. internal so the Tale signal in PawnDiary.Ingestion can read it.
        /// Moved verbatim from the old DiaryGameComponent.Tales.cs.
        /// </summary>
        internal static bool IsDeathDescriptionEligible(Pawn pawn)
        {
            return pawn != null && IsHumanlike(pawn) && pawn.IsColonist;
        }

        /// <summary>
        /// True when the pawn already has a neutral death-description page, so the Tale path and the
        /// Pawn.Kill fallback do not both write one. internal so the Death fallback signal can read it
        /// via DiaryGameComponent.Instance. Moved verbatim from the old DiaryGameComponent.Tales.cs.
        /// </summary>
        internal bool HasDeathDescriptionFor(Pawn pawn)
        {
            string pawnId = pawn?.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary?.eventIds == null)
            {
                return false;
            }

            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                if (diaryEvent != null && diaryEvent.IsDeathDescriptionFor(pawnId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns whether a pawn is old enough for first-person diary ownership/generation.
        /// </summary>
        private static bool IsFirstPersonDiaryAgeEligible(Pawn pawn)
        {
            int minimumAge = Math.Max(0, DiaryTuning.Current.minimumFirstPersonAgeYears);
            return minimumAge <= 0
                || (pawn?.ageTracker != null && pawn.ageTracker.AgeBiologicalYears >= minimumAge);
        }

        /// <summary>
        /// Harmony hooks can fire during pawn/world generation, before TicksAbs is valid. Event
        /// recorders that create DiaryEvents call this before stamping the current absolute date.
        /// </summary>
        private static bool CanRecordGameplayEventNow()
        {
            return GamePlaying;
        }

        /// <summary>
        /// Builds a CaptureContext from the impure eligibility/enable facts the caller already
        /// computed. Centralized so every source — the partial-class Record methods and the
        /// DiarySignal capture classes alike — reads the same way and the pure Decider sees a
        /// consistent snapshot. internal so signals in PawnDiary.Ingestion can reuse it.
        /// </summary>
        internal static CaptureContext BuildCaptureContext(
            bool eligible, bool userEnabled, bool signalEnabled, bool ambientSignalEnabled)
        {
            return new CaptureContext
            {
                Eligible = eligible,
                UserEnabled = userEnabled,
                SignalEnabled = signalEnabled,
                AmbientSignalEnabled = ambientSignalEnabled,
                Now = Find.TickManager.TicksGame,
            };
        }

        // One shared free-colonist snapshot per game tick. Several scans (hediff, work, thought
        // progression, ambient flush, day summary) fire on independent timers and used to each copy the
        // live list; when two land on the same tick they now share one copy. Reset across game loads
        // (constructor) because TicksGame can repeat between different games.
        private static int cachedFreeColonistsTick = -1;
        private static List<Pawn> cachedFreeColonists;

        /// <summary>Drops the per-tick free-colonist snapshot. Called on each Game construction so a
        /// loaded game never reuses the previous game's list at a coincidentally equal tick.</summary>
        private static void ResetFreeColonistSnapshot()
        {
            cachedFreeColonistsTick = -1;
            cachedFreeColonists = null;
        }

        /// <summary>
        /// Returns a one-tick snapshot of the live free-colonist list. RimWorld may change that list
        /// while diary tick work records entries, so scheduled scans should loop this copy instead of
        /// enumerating <see cref="PawnsFinder.AllMaps_FreeColonists"/> directly. The copy is cached for
        /// the current tick and shared across scans (read-only by every caller), so co-firing scans pay
        /// one allocation instead of one each.
        /// </summary>
        private static List<Pawn> SnapshotFreeColonists()
        {
            int tick = Find.TickManager != null ? Find.TickManager.TicksGame : -1;
            if (tick >= 0 && tick == cachedFreeColonistsTick && cachedFreeColonists != null)
            {
                return cachedFreeColonists;
            }

            cachedFreeColonists = new List<Pawn>(PawnsFinder.AllMaps_FreeColonists);
            cachedFreeColonistsTick = tick;
            return cachedFreeColonists;
        }

        /// <summary>
        /// Reads <paramref name="from"/>'s opinion of <paramref name="to"/> through the guard vanilla's
        /// opinion math needs. <c>Pawn_RelationsTracker.OpinionOf</c> walks the other pawn's social
        /// thoughts, and that walk can throw: an <see cref="ArgumentOutOfRangeException"/> from
        /// <c>ThoughtHandler.OpinionOffsetOfGroup</c> when a pawn's memory list is momentarily
        /// inconsistent, or an NRE from a stale relation thought / another mod's OpinionOf patch. Since
        /// callers loop colonist pairs or build pairwise prompt continuity, one fragile pawn must cost
        /// only its own read, not abort the whole GameComponent tick, day-start snapshot, or interaction
        /// batch. Returns false (and leaves <paramref name="opinion"/> at 0) when the opinion cannot be
        /// read. Internal so the separate impure context builder uses the same guard as this component.
        /// </summary>
        internal static bool TryReadOpinion(Pawn from, Pawn to, out int opinion)
        {
            opinion = 0;
            if (from == null || to == null || from.relations == null)
            {
                return false;
            }

            try
            {
                opinion = from.relations.OpinionOf(to);
                return true;
            }
            catch (Exception e)
            {
                // Routine pawn churn (a relation ending the same tick we read it) stays quiet on the
                // common path; a genuinely broken getter — vanilla's own list race, or another mod's
                // OpinionOf/thought patch — surfaces at most once per session, keyed by the thrown
                // exception type so distinct failure modes each get one line instead of spamming the log.
                Log.WarningOnce(
                    "[Pawn Diary] Skipped an opinion read that threw (stale relation, vanilla thought-list "
                    + "race, or another mod's OpinionOf patch?): " + e,
                    ("PawnDiary.OpinionReadSkip." + e.GetType().Name).GetHashCode());
                return false;
            }
        }

        /// <summary>
        /// Adds an event ID to a pawn's diary, creating the diary record if necessary.
        /// </summary>
        private void AddEventRef(Pawn pawn, string eventId, bool insertChronologically = false)
        {
            AddEventRef(pawn, eventId, insertChronologically, eligibilityAlreadyVerified: false);
        }

        /// <summary>
        /// Adds a delayed event for a writer whose diary eligibility was frozen before an irreversible
        /// vanilla state change. The caller must supply only an exact writer selected from that capture.
        /// </summary>
        internal void AddPreverifiedEventRef(
            Pawn pawn,
            string eventId,
            bool insertChronologically = false)
        {
            // Event retention runs inside the factory before this delayed repair. Never create a
            // dangling diary ID if an extreme retention boundary discarded the event first.
            if (events.FindEvent(eventId) == null)
            {
                return;
            }

            AddEventRef(pawn, eventId, insertChronologically, eligibilityAlreadyVerified: true);
        }

        private void AddEventRef(
            Pawn pawn,
            string eventId,
            bool insertChronologically,
            bool eligibilityAlreadyVerified)
        {
            if (pawn == null || string.IsNullOrWhiteSpace(eventId)
                || (!eligibilityAlreadyVerified && !IsDiaryEligible(pawn)))
            {
                return;
            }

            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            DiaryEvent diaryEvent = events.FindEvent(eventId);
            if (EventFallsOutsideDiaryBounds(diaryEvent, pawnId, diary, PawnAliveForDiaryBounds(pawn)))
            {
                return;
            }

            diary.pawnName = pawn.LabelShortCap;
            if (diary.eventIds == null)
            {
                diary.eventIds = new List<string>();
            }

            if (!diary.eventIds.Contains(eventId))
            {
                // Arrival is the diary's boundary and first page. If a startup hook recorded another
                // event before the bootstrap scan ran, keep the arrival at the front of this pawn's
                // index so UI/order-sensitive scans still treat it as the beginning of the arc.
                if (diaryEvent != null && diaryEvent.IsArrivalDescriptionFor(pawnId))
                {
                    diary.eventIds.Insert(0, eventId);
                }
                else if (insertChronologically && diaryEvent != null)
                {
                    int insertionIndex = HistoricalEventInsertionIndex(diary, diaryEvent, pawnId);
                    if (insertionIndex >= 0)
                    {
                        diary.eventIds.Insert(insertionIndex, eventId);
                    }
                    else
                    {
                        diary.eventIds.Add(eventId);
                    }
                }
                else
                {
                    diary.eventIds.Add(eventId);
                }
            }
        }

        /// <summary>
        /// Finds where a delayed historical event belongs. A same-tick final-death page sorts after
        /// the historical event so childbirth remains visible when the birther died in vanilla's call.
        /// </summary>
        private int HistoricalEventInsertionIndex(
            PawnDiaryRecord diary,
            DiaryEvent historicalEvent,
            string pawnId)
        {
            if (diary?.eventIds == null || historicalEvent == null)
            {
                return -1;
            }

            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent existing = events.FindEvent(diary.eventIds[i]);
                if (existing == null || existing.IsArrivalDescriptionFor(pawnId))
                {
                    continue;
                }

                if (existing.tick > historicalEvent.tick
                    || (existing.tick == historicalEvent.tick
                        && existing.IsDeathDescriptionFor(pawnId)))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Adds the death-description event to the deceased colonist's diary even after death state
        /// makes the usual live-colonist eligibility checks unreliable.
        /// </summary>
        internal void AddDeathEventRef(Pawn pawn, string eventId)
        {
            if (!IsDeathDescriptionEligible(pawn) || string.IsNullOrWhiteSpace(eventId))
            {
                return;
            }

            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            DiaryEvent diaryEvent = events.FindEvent(eventId);
            if (EventFallsOutsideDiaryBounds(diaryEvent, pawnId, diary, PawnAliveForDiaryBounds(pawn)))
            {
                return;
            }

            diary.pawnName = pawn.LabelShortCap;
            if (diary.eventIds == null)
            {
                diary.eventIds = new List<string>();
            }

            if (!diary.eventIds.Contains(eventId))
            {
                diary.eventIds.Add(eventId);
            }
        }

        /// <summary>
        /// Returns the tick of the latest neutral death-description event for a pawn. Callers that need
        /// the active diary boundary must also consider whether the pawn has since been resurrected.
        /// </summary>
        private int? FinalDeathTickFor(string pawnId, PawnDiaryRecord diary)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            int? finalDeathTick = null;
            if (diary?.eventIds != null)
            {
                for (int i = 0; i < diary.eventIds.Count; i++)
                {
                    DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                    if (diaryEvent == null || !diaryEvent.IsDeathDescriptionFor(pawnId))
                    {
                        continue;
                    }

                    if (!finalDeathTick.HasValue || diaryEvent.tick > finalDeathTick.Value)
                    {
                        finalDeathTick = diaryEvent.tick;
                    }
                }
            }

            int? archivedDeathTick = archive.FinalDeathTickForPawn(pawnId);
            if (archivedDeathTick.HasValue && (!finalDeathTick.HasValue || archivedDeathTick.Value > finalDeathTick.Value))
            {
                finalDeathTick = archivedDeathTick;
            }

            return finalDeathTick;
        }

        /// <summary>
        /// Returns the tick of the first neutral arrival-description event for a pawn. That event is
        /// the first diary page: anything earlier is suppressed from display and generation.
        /// </summary>
        private int? FirstArrivalTickFor(string pawnId, PawnDiaryRecord diary)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            int? firstArrivalTick = null;
            if (diary?.eventIds != null)
            {
                for (int i = 0; i < diary.eventIds.Count; i++)
                {
                    DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                    if (diaryEvent == null || !diaryEvent.IsArrivalDescriptionFor(pawnId))
                    {
                        continue;
                    }

                    if (!firstArrivalTick.HasValue || diaryEvent.tick < firstArrivalTick.Value)
                    {
                        firstArrivalTick = diaryEvent.tick;
                    }
                }
            }

            // Be forgiving of old/odd saves where an arrival event exists but was not present in
            // the pawn's event-id list: the boundary is still known from the event itself.
            if (!firstArrivalTick.HasValue)
            {
                IReadOnlyList<DiaryEvent> allEvents = events.AllEvents;
                for (int i = 0; i < allEvents.Count; i++)
                {
                    DiaryEvent diaryEvent = allEvents[i];
                    if (diaryEvent == null || !diaryEvent.IsArrivalDescriptionFor(pawnId))
                    {
                        continue;
                    }

                    if (!firstArrivalTick.HasValue || diaryEvent.tick < firstArrivalTick.Value)
                    {
                        firstArrivalTick = diaryEvent.tick;
                    }
                }
            }

            int? archivedArrivalTick = archive.FirstArrivalTickForPawn(pawnId);
            if (archivedArrivalTick.HasValue && (!firstArrivalTick.HasValue || archivedArrivalTick.Value < firstArrivalTick.Value))
            {
                firstArrivalTick = archivedArrivalTick;
            }

            return firstArrivalTick;
        }

        private bool EventFallsOutsideDiaryBoundsForPawn(DiaryEvent diaryEvent, string pawnId)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            return EventFallsOutsideDiaryBounds(diaryEvent, pawnId, diary, PawnAliveForDiaryBounds(pawnId, null));
        }

        /// <summary>
        /// True when an event would sit outside this pawn's diary lifespan. Arrival and death entries
        /// themselves are kept; entries before the arrival are hidden, and entries after death are
        /// hidden only while the pawn has not been resurrected.
        /// </summary>
        private bool EventFallsOutsideDiaryBounds(DiaryEvent diaryEvent, string pawnId, PawnDiaryRecord diary,
            bool pawnAliveNow)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            DiaryBounds bounds = ComputeDiaryBounds(pawnId, diary, pawnAliveNow);
            int eventIndex = EventIndexInDiary(diary, diaryEvent.eventId);
            return EventFallsOutsideDiaryBounds(diaryEvent, eventIndex, bounds);
        }

        /// <summary>
        /// A pawn's arrival/death boundary, as both list index and tick. The single-event
        /// <see cref="EventFallsOutsideDiaryBounds(DiaryEvent,string,PawnDiaryRecord)"/> overload
        /// re-derives this for every event; computing it once with <see cref="ComputeDiaryBounds"/>
        /// and reusing it across a pawn's events (see <see cref="GeneratedEntryForPlayLogEntry"/>)
        /// keeps those bounded scans O(events) instead of O(events^2).
        /// </summary>
        private struct DiaryBounds
        {
            public int firstArrivalIndex;
            public int finalDeathIndex;
            public int? firstArrivalTick;
            public int? finalDeathTick;
        }

        private sealed class DiaryBoundsCacheEntry
        {
            public DiaryBounds bounds;
            public Dictionary<string, int> eventIndexesById;
        }

        /// <summary>
        /// Computes a pawn's arrival/death boundary in a single pass over its saved event-id list,
        /// instead of the four separate scans the per-event helpers run. Mirrors the semantics of
        /// FirstArrivalIndexFor / FinalDeathIndexFor / FirstArrivalTickFor / FinalDeathTickFor
        /// (first arrival in list order, last death in list order; min arrival tick, max death tick).
        /// </summary>
        private DiaryBounds ComputeDiaryBounds(string pawnId, PawnDiaryRecord diary, bool pawnAliveNow)
        {
            DiaryBounds bounds = new DiaryBounds { firstArrivalIndex = -1, finalDeathIndex = -1 };
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return bounds;
            }

            if (diary?.eventIds != null)
            {
                for (int i = 0; i < diary.eventIds.Count; i++)
                {
                    DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                    if (diaryEvent == null)
                    {
                        continue;
                    }

                    if (diaryEvent.IsArrivalDescriptionFor(pawnId))
                    {
                        if (bounds.firstArrivalIndex < 0)
                        {
                            bounds.firstArrivalIndex = i; // first in list order
                        }

                        if (!bounds.firstArrivalTick.HasValue || diaryEvent.tick < bounds.firstArrivalTick.Value)
                        {
                            bounds.firstArrivalTick = diaryEvent.tick;
                        }
                    }

                    if (diaryEvent.IsDeathDescriptionFor(pawnId))
                    {
                        bounds.finalDeathIndex = i; // last in list order
                        if (!bounds.finalDeathTick.HasValue || diaryEvent.tick > bounds.finalDeathTick.Value)
                        {
                            bounds.finalDeathTick = diaryEvent.tick;
                        }
                    }
                }
            }

            // Forgiving fallback (mirrors FirstArrivalTickFor): an arrival event that exists but was
            // never added to this pawn's event-id list still defines the tick boundary.
            if (!bounds.firstArrivalTick.HasValue)
            {
                IReadOnlyList<DiaryEvent> allEvents = events.AllEvents;
                for (int i = 0; i < allEvents.Count; i++)
                {
                    DiaryEvent diaryEvent = allEvents[i];
                    if (diaryEvent != null
                        && diaryEvent.IsArrivalDescriptionFor(pawnId)
                        && (!bounds.firstArrivalTick.HasValue || diaryEvent.tick < bounds.firstArrivalTick.Value))
                    {
                        bounds.firstArrivalTick = diaryEvent.tick;
                    }
                }
            }

            int? archivedArrivalTick = archive.FirstArrivalTickForPawn(pawnId);
            if (archivedArrivalTick.HasValue && (!bounds.firstArrivalTick.HasValue || archivedArrivalTick.Value < bounds.firstArrivalTick.Value))
            {
                bounds.firstArrivalTick = archivedArrivalTick;
            }

            int? archivedDeathTick = archive.FinalDeathTickForPawn(pawnId);
            if (archivedDeathTick.HasValue && (!bounds.finalDeathTick.HasValue || archivedDeathTick.Value > bounds.finalDeathTick.Value))
            {
                bounds.finalDeathTick = archivedDeathTick;
            }

            SuppressFinalDeathBoundaryIfResurrected(ref bounds, pawnAliveNow);
            return bounds;
        }

        /// <summary>
        /// Clears the terminal death boundary while the same saved pawn is alive again. The death page
        /// remains in the diary; it just no longer cuts off later resurrected-life entries.
        /// </summary>
        private static void SuppressFinalDeathBoundaryIfResurrected(ref DiaryBounds bounds, bool pawnAliveNow)
        {
            bool hasFinalDeathBoundary = bounds.finalDeathTick.HasValue || bounds.finalDeathIndex >= 0;
            if (DiaryLifeBoundaryPolicy.FinalDeathBoundaryApplies(hasFinalDeathBoundary, pawnAliveNow))
            {
                return;
            }

            bounds.finalDeathIndex = -1;
            bounds.finalDeathTick = null;
        }

        /// <summary>
        /// Boundary check against a precomputed <see cref="DiaryBounds"/> and the event's already-known
        /// index in the pawn's list. Same rules as the per-event
        /// <see cref="EventFallsOutsideDiaryBounds(DiaryEvent,string,PawnDiaryRecord)"/> overload, but
        /// without re-deriving the boundary for every event.
        /// </summary>
        private static bool EventFallsOutsideDiaryBounds(DiaryEvent diaryEvent, int eventIndex, DiaryBounds bounds)
        {
            if (diaryEvent == null)
            {
                return false;
            }

            if (eventIndex >= 0)
            {
                if (bounds.firstArrivalIndex >= 0 && eventIndex < bounds.firstArrivalIndex)
                {
                    return true;
                }

                if (bounds.finalDeathIndex >= 0 && eventIndex > bounds.finalDeathIndex)
                {
                    return true;
                }
            }

            return EventFallsBeforeFirstArrival(diaryEvent, bounds.firstArrivalTick)
                || EventFallsAfterFinalDeath(diaryEvent, bounds.finalDeathTick);
        }

        private static bool ArchivedEntryFallsOutsideDiaryBounds(ArchivedDiaryEntry archivedEntry, DiaryBounds bounds)
        {
            if (archivedEntry == null)
            {
                return false;
            }

            if (bounds.firstArrivalTick.HasValue
                && !archivedEntry.arrivalDescription
                && archivedEntry.tick < bounds.firstArrivalTick.Value)
            {
                return true;
            }

            return bounds.finalDeathTick.HasValue
                && !archivedEntry.deathDescription
                && archivedEntry.tick > bounds.finalDeathTick.Value;
        }

        private bool EventFallsOutsideDiaryBoundsForPawn(DiaryEvent diaryEvent, string pawnId,
            Dictionary<string, DiaryBoundsCacheEntry> boundsCache, Dictionary<string, Pawn> livePawnsById = null)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            DiaryBoundsCacheEntry cacheEntry = DiaryBoundsForPawn(pawnId, boundsCache, livePawnsById);
            int eventIndex = EventIndexInDiary(cacheEntry, diaryEvent.eventId);
            return EventFallsOutsideDiaryBounds(diaryEvent, eventIndex, cacheEntry.bounds);
        }

        private DiaryBoundsCacheEntry DiaryBoundsForPawn(string pawnId,
            Dictionary<string, DiaryBoundsCacheEntry> boundsCache, Dictionary<string, Pawn> livePawnsById = null)
        {
            if (boundsCache == null)
            {
                return BuildDiaryBoundsCacheEntry(pawnId, FindDiaryByPawnId(pawnId), livePawnsById);
            }

            DiaryBoundsCacheEntry cacheEntry;
            if (!boundsCache.TryGetValue(pawnId, out cacheEntry))
            {
                cacheEntry = BuildDiaryBoundsCacheEntry(pawnId, FindDiaryByPawnId(pawnId), livePawnsById);
                boundsCache[pawnId] = cacheEntry;
            }

            return cacheEntry;
        }

        private DiaryBoundsCacheEntry BuildDiaryBoundsCacheEntry(string pawnId, PawnDiaryRecord diary,
            Dictionary<string, Pawn> livePawnsById)
        {
            return new DiaryBoundsCacheEntry
            {
                bounds = ComputeDiaryBounds(pawnId, diary, PawnAliveForDiaryBounds(pawnId, livePawnsById)),
                eventIndexesById = EventIndexesById(diary)
            };
        }

        private static bool PawnAliveForDiaryBounds(Pawn pawn)
        {
            return pawn != null && !pawn.Dead;
        }

        private static bool PawnAliveForDiaryBounds(string pawnId, Dictionary<string, Pawn> livePawnsById)
        {
            return FindLivePawnByLoadId(pawnId, livePawnsById) != null;
        }

        private static Dictionary<string, int> EventIndexesById(PawnDiaryRecord diary)
        {
            Dictionary<string, int> indexes = new Dictionary<string, int>();
            if (diary?.eventIds == null)
            {
                return indexes;
            }

            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                string eventId = diary.eventIds[i];
                if (!string.IsNullOrWhiteSpace(eventId) && !indexes.ContainsKey(eventId))
                {
                    indexes[eventId] = i;
                }
            }

            return indexes;
        }

        private static int EventIndexInDiary(PawnDiaryRecord diary, string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId) || diary?.eventIds == null)
            {
                return -1;
            }

            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                if (diary.eventIds[i] == eventId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int EventIndexInDiary(DiaryBoundsCacheEntry cacheEntry, string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId) || cacheEntry?.eventIndexesById == null)
            {
                return -1;
            }

            int index;
            return cacheEntry.eventIndexesById.TryGetValue(eventId, out index) ? index : -1;
        }

        private static bool EventFallsBeforeFirstArrival(DiaryEvent diaryEvent, int? firstArrivalTick)
        {
            return diaryEvent != null
                && firstArrivalTick.HasValue
                && !diaryEvent.HasArrivalDescription()
                && diaryEvent.tick < firstArrivalTick.Value;
        }

        private static bool EventFallsAfterFinalDeath(DiaryEvent diaryEvent, int? finalDeathTick)
        {
            return diaryEvent != null
                && finalDeathTick.HasValue
                && !diaryEvent.HasDeathDescription()
                && diaryEvent.tick > finalDeathTick.Value;
        }

        /// <summary>
        /// Removes blank, duplicate, and dangling event references from per-pawn diary indexes. The
        /// saved event list is the source of truth; records should not keep pointing at events that no
        /// longer exist or were corrupted out of a save.
        /// </summary>
        private void PruneDiaryEventRefs()
        {
            if (diaries == null)
            {
                return;
            }

            events.EnsureIndexReady();

            for (int i = 0; i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                if (diary == null)
                {
                    continue;
                }

                if (diary.eventIds == null)
                {
                    diary.eventIds = new List<string>();
                    continue;
                }

                HashSet<string> seen = null;
                for (int j = diary.eventIds.Count - 1; j >= 0; j--)
                {
                    string eventId = diary.eventIds[j];
                    if (string.IsNullOrWhiteSpace(eventId) || !events.ContainsEvent(eventId))
                    {
                        diary.eventIds.RemoveAt(j);
                        continue;
                    }

                    if (seen == null)
                    {
                        seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    }

                    if (!seen.Add(eventId))
                    {
                        diary.eventIds.RemoveAt(j);
                    }
                }
            }
        }

        /// <summary>
        /// Public accessor for finding a DiaryEvent by ID (used by the diary tab to build
        /// linked-entry previews and navigate to the other pawn's entry).
        /// </summary>
        internal DiaryEvent FindEventById(string eventId)
        {
            return events.FindEvent(eventId);
        }

        /// <summary>
        /// Newest diary events that remain "hot" for maintenance scans. Older entries are archive
        /// history: still saved and rendered by the UI, but no longer revisited by retry/title/orphan
        /// catch-up work.
        /// </summary>
        private IReadOnlyList<DiaryEvent> ActiveScanEvents()
        {
            return events.MostRecentEvents(DiaryTuning.ActiveScanEventWindow);
        }

        /// <summary>
        /// Returns the pawn's saved favorite entry keys ("eventId|povRole"), or null when the pawn has
        /// no diary record yet. The Diary tab syncs its per-session lookup set from this list; callers
        /// must treat it as read-only and go through <see cref="SetEntryFavorite"/> to change it.
        /// </summary>
        internal IReadOnlyList<string> FavoriteEntryKeysFor(Pawn pawn)
        {
            PawnDiaryRecord diary = FindDiary(pawn, false);
            return diary?.favoriteEntryKeys;
        }

        /// <summary>
        /// Stars/un-stars one diary page, persisting the choice on the pawn's diary record. A pawn
        /// without a record has no pages to favorite, so nothing is created here. Past the defensive
        /// bound the add is refused (the page simply cannot be starred) rather than evicting older
        /// favorites the player chose.
        /// </summary>
        internal void SetEntryFavorite(Pawn pawn, string entryKey, bool favorite)
        {
            if (string.IsNullOrEmpty(entryKey))
            {
                return;
            }

            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary == null)
            {
                return;
            }

            if (diary.favoriteEntryKeys == null)
            {
                diary.favoriteEntryKeys = new List<string>();
            }

            if (favorite)
            {
                if (diary.favoriteEntryKeys.Count >= DiaryEntryFilterPolicy.MaximumFavoriteEntryKeys
                    || diary.favoriteEntryKeys.Contains(entryKey))
                {
                    return;
                }

                diary.favoriteEntryKeys.Add(entryKey);
            }
            else
            {
                diary.favoriteEntryKeys.Remove(entryKey);
            }
        }

        /// <summary>
        /// Looks up a pawn's diary record by Pawn instance; optionally creates one if missing.
        /// </summary>
        private PawnDiaryRecord FindDiary(Pawn pawn, bool createIfMissing)
        {
            if (pawn == null)
            {
                return null;
            }

            string pawnId = pawn.GetUniqueLoadID();
            PawnDiaryRecord existingDiary = LookupDiaryByPawnId(pawnId);
            if (existingDiary != null)
            {
                EnsurePawnDiaryDefaults(existingDiary);
                return existingDiary;
            }

            if (!createIfMissing)
            {
                return null;
            }

            // Both voice layers roll for the pawn's current age band so a child gets the child catalogs
            // and an adult the adult catalogs. The player can override either from the diary tab; a
            // pinned/player-chosen pick is never auto-re-rolled.
            string band = BandForPawn(pawn);
            PawnDiaryRecord diary = new PawnDiaryRecord
            {
                pawnId = pawnId,
                pawnName = pawn.LabelShortCap,
                // Initial writing style is rolled once, biased toward the pawn's traits/backstory and
                // softly steered away from styles other colonists already use.
                personaDefName = DiaryPersonas.WeightedStartingPersona(pawn, BuildUsedPersonaCounts(pawnId, band), band).defName,
                // Psychotype rolls independently from skill passions (only when the layer is enabled; a
                // disabled layer defers the roll and leaves this empty until re-enabled).
                psychotypeDefName = PsychotypesEnabled ? RollPsychotypeFor(pawn, band, null, pawnId) : string.Empty,
                voiceStageBand = band,
                diaryGenerationEnabled = true
            };
            diaries.Add(diary);
            IndexDiaryRecord(diary);
            return diary;
        }

        /// <summary>
        /// Counts how many current, living colonists in the same age band already use each writing style,
        /// so a new pawn's initial roll can softly avoid duplicates (see
        /// <see cref="DiaryPersonas.WeightedStartingPersona"/>). Keyed by style defName. Band-aware so
        /// child styles are not penalized by adult use and vice versa.
        /// </summary>
        private Dictionary<string, int> BuildUsedPersonaCounts(string excludePawnId, string band = null)
        {
            return BuildUsedVoiceCounts(excludePawnId, band, diary => diary.personaDefName);
        }

        /// <summary>
        /// Band-aware count of how many living colonists already hold each psychotype defName, mirroring
        /// <see cref="BuildUsedPersonaCounts"/> for the psychotype layer's soft duplicate penalty.
        /// </summary>
        private Dictionary<string, int> BuildUsedPsychotypeCounts(string excludePawnId, string band = null)
        {
            return BuildUsedVoiceCounts(excludePawnId, band, diary => diary.psychotypeDefName);
        }

        // Shared living-colony counter for both voice layers. Ignores dead/lost pawns and non-colonists
        // (so retired voices free up) and, when a band is given, only counts colonists in that band.
        private Dictionary<string, int> BuildUsedVoiceCounts(string excludePawnId, string band,
            Func<PawnDiaryRecord, string> selector)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            if (diaries == null)
            {
                return counts;
            }

            // Live colonist id -> current band, so "use" reflects the living colony rather than every
            // record ever created, and stays within the band we are rolling for.
            Dictionary<string, string> colonistBands = new Dictionary<string, string>();
            List<Pawn> colonists = SnapshotFreeColonists();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn colonist = colonists[i];
                if (colonist == null)
                {
                    continue;
                }

                string id = colonist.GetUniqueLoadID();
                if (!string.IsNullOrWhiteSpace(id) && !colonistBands.ContainsKey(id))
                {
                    colonistBands[id] = BandForPawn(colonist);
                }
            }

            string wantedBand = band == null ? null : DiaryPersonas.NormalizeStage(band);
            for (int i = 0; i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                if (diary == null || diary.pawnId == excludePawnId)
                {
                    continue;
                }

                string value = selector(diary);
                if (string.IsNullOrWhiteSpace(value)
                    || !colonistBands.TryGetValue(diary.pawnId, out string colonistBand)
                    || (wantedBand != null && colonistBand != wantedBand))
                {
                    continue;
                }

                counts.TryGetValue(value, out int current);
                counts[value] = current + 1;
            }

            return counts;
        }

        /// <summary>
        /// Looks up a diary record by pawn ID string (no creation).
        /// </summary>
        private PawnDiaryRecord FindDiaryByPawnId(string pawnId)
        {
            PawnDiaryRecord diary = LookupDiaryByPawnId(pawnId);
            if (diary != null)
            {
                EnsurePawnDiaryDefaults(diary);
            }

            return diary;
        }

        /// <summary>
        /// O(1) record lookup through <see cref="diariesById"/>, with a defensive rebuild if a caller
        /// reaches here before the PostLoadInit rebuild (mirrors DiaryEventRepository.EnsureIndexReady).
        /// Does not apply persona defaults — the public finders above do that on the resolved record.
        /// </summary>
        private PawnDiaryRecord LookupDiaryByPawnId(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || diaries == null)
            {
                return null;
            }

            if (diariesById.Count == 0 && diaries.Count > 0)
            {
                RebuildDiaryIndex();
            }

            PawnDiaryRecord diary;
            return diariesById.TryGetValue(pawnId, out diary) ? diary : null;
        }

        /// <summary>
        /// Rebuilds the pawnId->record index from the loaded <see cref="diaries"/> list. The index is
        /// never serialized; this runs in PostLoadInit. First occurrence of an id wins, matching the
        /// old linear scan, and null / blank-id records are skipped.
        /// </summary>
        private void RebuildDiaryIndex()
        {
            diariesById.Clear();
            if (diaries == null)
            {
                return;
            }

            for (int i = 0; i < diaries.Count; i++)
            {
                IndexDiaryRecord(diaries[i]);
            }
        }

        /// <summary>Adds one record to the lookup index, keeping the first id seen (no overwrite).</summary>
        private void IndexDiaryRecord(PawnDiaryRecord diary)
        {
            if (diary == null || string.IsNullOrWhiteSpace(diary.pawnId))
            {
                return;
            }

            if (!diariesById.ContainsKey(diary.pawnId))
            {
                diariesById[diary.pawnId] = diary;
            }
        }

        /// <summary>
        /// Ensures a usable writing style is assigned even if the Def was removed or never set.
        /// </summary>
        private static void EnsurePawnDiaryDefaults(PawnDiaryRecord diary)
        {
            if (diary == null)
            {
                return;
            }

            // XML edits or mod patches may remove a style Def that a pawn record names.
            if (string.IsNullOrWhiteSpace(diary.personaDefName) || DiaryPersonas.ForDefName(diary.personaDefName) == null)
            {
                diary.personaDefName = DiaryPersonas.Default.defName;
            }

            diary.EnsureProgressionState();
            diary.EnsureArcSchedule();
        }

        /// <summary>
        /// Collects the POV pawn's most recently saved Narrative Continuity selection keys — newest
        /// hot pages first, then newest archive rows — so the pure selector can apply its XML
        /// repetition penalty instead of immediately reselecting the same lens for that pawn. The
        /// result stays inside the XML recent-key cap and the builder re-normalizes it, so a request
        /// can never grow unbounded. Runs only when a narrative-capable source records a page (growth
        /// moments, births, event windows), and both scans stop as soon as the cap is filled.
        /// </summary>
        internal List<string> RecentNarrativeSelectedCandidateKeys(string pawnId)
        {
            List<string> keys = new List<string>();
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return keys;
            }

            int cap = Math.Max(1, DiaryNarrativeContinuityPolicy.Snapshot().maxRecentSelectedCandidateKeys);
            IReadOnlyList<DiaryEvent> live = events.AllEvents;
            for (int i = live.Count - 1; i >= 0 && keys.Count < cap; i--)
            {
                DiaryEvent diaryEvent = live[i];
                if (diaryEvent == null)
                {
                    continue;
                }

                AppendRecentNarrativeKeys(keys, cap, diaryEvent, pawnId, DiaryEvent.InitiatorRole);
                AppendRecentNarrativeKeys(keys, cap, diaryEvent, pawnId, DiaryEvent.RecipientRole);
            }

            IReadOnlyList<ArchivedDiaryEntry> archived = archive.EntriesForPawn(pawnId);
            for (int i = archived.Count - 1; i >= 0 && keys.Count < cap; i--)
            {
                AppendDistinctNarrativeKeys(keys, cap, archived[i]?.narrativeSelectedCandidateKeys);
            }

            return keys;
        }

        /// <summary>Appends one hot event's saved keys when the pawn wrote that POV slot.</summary>
        private static void AppendRecentNarrativeKeys(
            List<string> keys,
            int cap,
            DiaryEvent diaryEvent,
            string pawnId,
            string povRole)
        {
            if (keys.Count >= cap || diaryEvent.PawnIdForRole(povRole) != pawnId)
            {
                return;
            }

            AppendDistinctNarrativeKeys(keys, cap, diaryEvent.NarrativeSelectedCandidateKeysForRole(povRole));
        }

        /// <summary>Appends keys without duplicates until the cap; lists here are at most a dozen rows.</summary>
        private static void AppendDistinctNarrativeKeys(List<string> keys, int cap, List<string> source)
        {
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count && keys.Count < cap; i++)
            {
                string key = source[i];
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                bool exists = false;
                for (int j = 0; j < keys.Count; j++)
                {
                    if (string.Equals(keys[j], key, StringComparison.Ordinal))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    keys.Add(key);
                }
            }
        }

    }
}

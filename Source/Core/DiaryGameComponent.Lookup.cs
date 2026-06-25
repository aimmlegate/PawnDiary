// Lookups and bookkeeping over the saved data: cross-referencing events onto pawn diary records,
// finding events/records by id, the "arrival first / death last" rules that make a colonist's
// arrival and death entries hard diary boundaries, per-pawn eligibility checks, the dedup gate
// shared by every Record* hook, writing-style seeding/defaulting, and the shared empty-list singleton.
// These are the small, mostly-pure helpers the other partial files lean on.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Dedup gate: returns true if the same key was recorded within the given tick window, preventing
        /// duplicate events (e.g. mirrored SocialFighting calls).
        /// </summary>
        private bool RecentlyRecorded(Dictionary<string, int> recentEvents, string key, int windowTicks)
        {
            if (IsRecentlyRecorded(recentEvents, key, windowTicks))
            {
                return true;
            }

            MarkRecentlyRecorded(recentEvents, key, windowTicks);
            return false;
        }

        private bool IsRecentlyRecorded(Dictionary<string, int> recentEvents, string key, int windowTicks)
        {
            if (recentEvents == null || string.IsNullOrEmpty(key))
            {
                return false;
            }

            int now = Find.TickManager.TicksGame;
            PruneRecentEvents(recentEvents, now, windowTicks);

            int last;
            return recentEvents.TryGetValue(key, out last) && now - last < windowTicks;
        }

        private void MarkRecentlyRecorded(Dictionary<string, int> recentEvents, string key, int windowTicks)
        {
            if (recentEvents == null || string.IsNullOrEmpty(key))
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            PruneRecentEvents(recentEvents, now, windowTicks);
            recentEvents[key] = now;
        }

        /// <summary>
        /// Removes dedup keys that are older than their useful window. The dictionaries are transient
        /// and only answer "did this just happen?", so old keys should not survive a long play session.
        /// </summary>
        private static void PruneRecentEvents(Dictionary<string, int> recentEvents, int now, int windowTicks)
        {
            if (recentEvents == null || recentEvents.Count < RecentEventPruneThreshold)
            {
                return;
            }

            List<string> expiredKeys = null;
            foreach (KeyValuePair<string, int> pair in recentEvents)
            {
                if (windowTicks <= 0 || now - pair.Value >= windowTicks)
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
        private static bool IsDiaryEligible(Pawn pawn)
        {
            return IsHumanlike(pawn) && pawn.IsColonist && IsFirstPersonDiaryAgeEligible(pawn);
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
        /// Returns a one-tick snapshot of the live free-colonist list. RimWorld may change that list
        /// while diary tick work records entries, so scheduled scans should loop this copy instead of
        /// enumerating <see cref="PawnsFinder.AllMaps_FreeColonists"/> directly.
        /// </summary>
        private static List<Pawn> SnapshotFreeColonists()
        {
            return new List<Pawn>(PawnsFinder.AllMaps_FreeColonists);
        }

        /// <summary>
        /// Adds an event ID to a pawn's diary, creating the diary record if necessary.
        /// </summary>
        private void AddEventRef(Pawn pawn, string eventId)
        {
            if (!IsDiaryEligible(pawn) || string.IsNullOrWhiteSpace(eventId))
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
            if (EventFallsOutsideDiaryBounds(diaryEvent, pawnId, diary))
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
        /// Adds the death-description event to the deceased colonist's diary even after death state
        /// makes the usual live-colonist eligibility checks unreliable.
        /// </summary>
        private void AddDeathEventRef(Pawn pawn, string eventId)
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
            if (EventFallsOutsideDiaryBounds(diaryEvent, pawnId, diary))
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
        /// Returns the tick of the latest neutral death-description event for a pawn. That event is
        /// terminal: anything later is suppressed from display and generation for that pawn.
        /// </summary>
        private int? FinalDeathTickFor(string pawnId, PawnDiaryRecord diary)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || diary?.eventIds == null)
            {
                return null;
            }

            int? finalDeathTick = null;
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

            return firstArrivalTick;
        }

        private bool EventFallsOutsideDiaryBoundsForPawn(DiaryEvent diaryEvent, string pawnId)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            return EventFallsOutsideDiaryBounds(diaryEvent, pawnId, diary);
        }

        /// <summary>
        /// True when an event would sit outside this pawn's diary lifespan. Arrival and death entries
        /// themselves are kept; entries before the arrival or after the death are hidden and skipped.
        /// </summary>
        private bool EventFallsOutsideDiaryBounds(DiaryEvent diaryEvent, string pawnId, PawnDiaryRecord diary)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            DiaryBounds bounds = ComputeDiaryBounds(pawnId, diary);
            int eventIndex = EventIndexInDiary(diary, diaryEvent.eventId);
            return EventFallsOutsideDiaryBounds(diaryEvent, eventIndex, bounds);
        }

        /// <summary>
        /// A pawn's arrival/death boundary, as both list index and tick. The single-event
        /// <see cref="EventFallsOutsideDiaryBounds(DiaryEvent,string,PawnDiaryRecord)"/> overload
        /// re-derives this for every event; computing it once with <see cref="ComputeDiaryBounds"/>
        /// and reusing it (see <see cref="EntriesFor"/>) turns the per-frame diary redraw from
        /// O(events^2) into O(events).
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
        private DiaryBounds ComputeDiaryBounds(string pawnId, PawnDiaryRecord diary)
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

            return bounds;
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

        private bool EventFallsOutsideDiaryBoundsForPawn(DiaryEvent diaryEvent, string pawnId,
            Dictionary<string, DiaryBoundsCacheEntry> boundsCache)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            DiaryBoundsCacheEntry cacheEntry = DiaryBoundsForPawn(pawnId, boundsCache);
            int eventIndex = EventIndexInDiary(cacheEntry, diaryEvent.eventId);
            return EventFallsOutsideDiaryBounds(diaryEvent, eventIndex, cacheEntry.bounds);
        }

        private DiaryBoundsCacheEntry DiaryBoundsForPawn(string pawnId,
            Dictionary<string, DiaryBoundsCacheEntry> boundsCache)
        {
            if (boundsCache == null)
            {
                return BuildDiaryBoundsCacheEntry(pawnId, FindDiaryByPawnId(pawnId));
            }

            DiaryBoundsCacheEntry cacheEntry;
            if (!boundsCache.TryGetValue(pawnId, out cacheEntry))
            {
                cacheEntry = BuildDiaryBoundsCacheEntry(pawnId, FindDiaryByPawnId(pawnId));
                boundsCache[pawnId] = cacheEntry;
            }

            return cacheEntry;
        }

        private DiaryBoundsCacheEntry BuildDiaryBoundsCacheEntry(string pawnId, PawnDiaryRecord diary)
        {
            return new DiaryBoundsCacheEntry
            {
                bounds = ComputeDiaryBounds(pawnId, diary),
                eventIndexesById = EventIndexesById(diary)
            };
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

        /// <summary>
        /// Uses the pawn's saved event-id order as the hard boundary. This catches same-tick cases
        /// where a startup thought was recorded before the arrival page, or a hook fires after death.
        /// </summary>
        private bool EventFallsOutsideDiaryBoundsByIndex(DiaryEvent diaryEvent, string pawnId, PawnDiaryRecord diary)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(pawnId) || diary?.eventIds == null)
            {
                return false;
            }

            int eventIndex = EventIndexInDiary(diary, diaryEvent.eventId);
            if (eventIndex < 0)
            {
                return false;
            }

            int firstArrivalIndex = FirstArrivalIndexFor(pawnId, diary);
            if (firstArrivalIndex >= 0 && eventIndex < firstArrivalIndex)
            {
                return true;
            }

            int finalDeathIndex = FinalDeathIndexFor(pawnId, diary);
            return finalDeathIndex >= 0 && eventIndex > finalDeathIndex;
        }

        private int FirstArrivalIndexFor(string pawnId, PawnDiaryRecord diary)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || diary?.eventIds == null)
            {
                return -1;
            }

            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                if (diaryEvent != null && diaryEvent.IsArrivalDescriptionFor(pawnId))
                {
                    return i;
                }
            }

            return -1;
        }

        private int FinalDeathIndexFor(string pawnId, PawnDiaryRecord diary)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || diary?.eventIds == null)
            {
                return -1;
            }

            int finalDeathIndex = -1;
            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                if (diaryEvent != null && diaryEvent.IsDeathDescriptionFor(pawnId))
                {
                    finalDeathIndex = i;
                }
            }

            return finalDeathIndex;
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
        public DiaryEvent FindEventById(string eventId)
        {
            return events.FindEvent(eventId);
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
            for (int i = 0; i < diaries.Count; i++)
            {
                PawnDiaryRecord existingDiary = diaries[i];
                if (existingDiary == null)
                {
                    continue;
                }

                if (existingDiary.pawnId == pawnId)
                {
                    EnsurePawnDiaryDefaults(existingDiary);
                    return existingDiary;
                }
            }

            if (!createIfMissing)
            {
                return null;
            }

            PawnDiaryRecord diary = new PawnDiaryRecord
            {
                pawnId = pawnId,
                pawnName = pawn.LabelShortCap,
                // Initial writing style is rolled once, biased toward the pawn's traits/backstory and
                // softly steered away from styles other colonists already use. The player can
                // still override it from the diary tab; that saved choice is never re-rolled.
                personaDefName = DiaryPersonas.WeightedStartingPersona(pawn, BuildUsedPersonaCounts(pawnId)).defName,
                diaryGenerationEnabled = true
            };
            diaries.Add(diary);
            return diary;
        }

        /// <summary>
        /// Counts how many current, living colonists already use each writing style, so a new pawn's
        /// initial roll can softly avoid duplicates (see <see cref="DiaryPersonas.WeightedStartingPersona"/>).
        /// Keyed by style defName. The pawn being created is excluded via <paramref name="excludePawnId"/>;
        /// dead/lost pawns and non-colonists are ignored so retired styles free up again.
        /// </summary>
        private Dictionary<string, int> BuildUsedPersonaCounts(string excludePawnId)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            if (diaries == null)
            {
                return counts;
            }

            // The set of pawn IDs that are colonists right now, so style "use" reflects the
            // living colony rather than every record ever created.
            HashSet<string> colonistIds = new HashSet<string>();
            List<Pawn> colonists = SnapshotFreeColonists();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn colonist = colonists[i];
                if (colonist != null)
                {
                    colonistIds.Add(colonist.GetUniqueLoadID());
                }
            }

            for (int i = 0; i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                if (diary == null || diary.pawnId == excludePawnId || string.IsNullOrWhiteSpace(diary.personaDefName))
                {
                    continue;
                }

                if (!colonistIds.Contains(diary.pawnId))
                {
                    continue;
                }

                counts.TryGetValue(diary.personaDefName, out int current);
                counts[diary.personaDefName] = current + 1;
            }

            return counts;
        }

        /// <summary>
        /// Looks up a diary record by pawn ID string (no creation).
        /// </summary>
        private PawnDiaryRecord FindDiaryByPawnId(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId) || diaries == null)
            {
                return null;
            }

            for (int i = 0; i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                if (diary == null)
                {
                    continue;
                }

                if (diary.pawnId == pawnId)
                {
                    EnsurePawnDiaryDefaults(diary);
                    return diary;
                }
            }

            return null;
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
        }

        /// <summary>
        /// Singleton empty list returned when a pawn has no diary entries, avoiding per-call allocations.
        /// </summary>
        private static class EmptyEntries
        {
            public static readonly IReadOnlyList<DiaryEntryView> List = new List<DiaryEntryView>();
        }
    }
}

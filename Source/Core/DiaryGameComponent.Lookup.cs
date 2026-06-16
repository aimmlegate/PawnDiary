// Lookups and bookkeeping over the saved data: cross-referencing events onto pawn diary records,
// finding events/records by id, the "arrival first / death last" rules that make a colonist's
// arrival and death entries hard diary boundaries, per-pawn eligibility checks, the dedup gate
// shared by every Record* hook, persona seeding/defaulting, and the shared empty-list singleton.
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
            int now = Find.TickManager.TicksGame;
            int last;
            if (recentEvents.TryGetValue(key, out last) && now - last < windowTicks)
            {
                return true;
            }

            recentEvents[key] = now;
            return false;
        }

        /// <summary>
        /// Checks whether a pawn is a humanlike (as opposed to an animal or mechanoid).
        /// </summary>
        private static bool IsHumanlike(Pawn pawn)
        {
            return pawn != null && pawn.RaceProps != null && pawn.RaceProps.Humanlike;
        }

        /// <summary>
        /// A pawn qualifies for diary tracking if it is a humanlike colonist.
        /// </summary>
        private static bool IsDiaryEligible(Pawn pawn)
        {
            return IsHumanlike(pawn) && pawn.IsColonist;
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
            DiaryEvent diaryEvent = FindEvent(eventId);
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
            DiaryEvent diaryEvent = FindEvent(eventId);
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
                DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
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
                    DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
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
            if (!firstArrivalTick.HasValue && diaryEvents != null)
            {
                for (int i = 0; i < diaryEvents.Count; i++)
                {
                    DiaryEvent diaryEvent = diaryEvents[i];
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

            if (EventFallsOutsideDiaryBoundsByIndex(diaryEvent, pawnId, diary))
            {
                return true;
            }

            return EventFallsBeforeFirstArrival(diaryEvent, FirstArrivalTickFor(pawnId, diary))
                || EventFallsAfterFinalDeath(diaryEvent, FinalDeathTickFor(pawnId, diary));
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
                    DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
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
            if (!bounds.firstArrivalTick.HasValue && diaryEvents != null)
            {
                for (int i = 0; i < diaryEvents.Count; i++)
                {
                    DiaryEvent diaryEvent = diaryEvents[i];
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
                DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
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
                DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
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
        /// Locates a DiaryEvent by its unique ID via the O(1) lookup index (see
        /// <see cref="eventsById"/>). The index is kept in sync by <see cref="RegisterDiaryEvent"/>
        /// and rebuilt on load by <see cref="RebuildEventIndex"/>.
        /// </summary>
        private DiaryEvent FindEvent(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return null;
            }

            DiaryEvent diaryEvent;
            return eventsById.TryGetValue(eventId, out diaryEvent) ? diaryEvent : null;
        }

        /// <summary>
        /// Adds a freshly created event to both the master list and the O(1) lookup index, keeping the
        /// two in sync. Every event-creation path funnels through here (see AddPairwiseEvent /
        /// AddSoloEvent) so <see cref="FindEvent"/> can stay a constant-time dictionary lookup.
        /// </summary>
        private void RegisterDiaryEvent(DiaryEvent diaryEvent)
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
        /// Rebuilds the eventId -> event index from the loaded master list. Called once after a save
        /// loads (the index itself is never serialized). First occurrence wins, matching the old
        /// linear FindEvent scan — duplicate ids are not expected (eventIds are GUIDs) but this keeps
        /// behavior identical if any occur, and it tolerates null entries from odd saves.
        /// </summary>
        private void RebuildEventIndex()
        {
            eventsById.Clear();
            if (diaryEvents == null)
            {
                return;
            }

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
        /// Public accessor for finding a DiaryEvent by ID (used by the diary tab to build
        /// linked-entry previews and navigate to the other pawn's entry).
        /// </summary>
        public DiaryEvent FindEventById(string eventId)
        {
            return FindEvent(eventId);
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
                if (diaries[i].pawnId == pawnId)
                {
                    EnsurePawnDiaryDefaults(diaries[i]);
                    return diaries[i];
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
                // Initial persona is rolled once, biased toward the pawn's traits/backstory and
                // softly steered away from personas other colonists already use. The player can
                // still override it from the diary tab; that saved choice is never re-rolled.
                personaDefName = DiaryPersonas.WeightedStartingPersona(pawn, BuildUsedPersonaCounts(pawnId)).defName,
                diaryGenerationEnabled = true
            };
            diaries.Add(diary);
            return diary;
        }

        /// <summary>
        /// Counts how many current, living colonists already use each persona, so a new pawn's
        /// initial roll can softly avoid duplicates (see <see cref="DiaryPersonas.WeightedStartingPersona"/>).
        /// Keyed by persona defName. The pawn being created is excluded via <paramref name="excludePawnId"/>;
        /// dead/lost pawns and non-colonists are ignored so retired voices free up again.
        /// </summary>
        private Dictionary<string, int> BuildUsedPersonaCounts(string excludePawnId)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            if (diaries == null)
            {
                return counts;
            }

            // The set of pawn IDs that are colonists right now, so persona "use" reflects the
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
                if (diaries[i].pawnId == pawnId)
                {
                    EnsurePawnDiaryDefaults(diaries[i]);
                    return diaries[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Ensures a usable persona is assigned even if the Def was removed or never set.
        /// </summary>
        private static void EnsurePawnDiaryDefaults(PawnDiaryRecord diary)
        {
            if (diary == null)
            {
                return;
            }

            // XML edits or mod patches may remove a persona Def that a pawn record names.
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

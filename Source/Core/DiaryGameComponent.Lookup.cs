// Lookups and bookkeeping over the saved data: cross-referencing events onto pawn diary records,
// finding events/records by id, the "final death" rules that make a colonist's death entry terminal
// (anything later is hidden and never generated), per-pawn eligibility checks, the dedup gate shared
// by every Record* hook, persona migration/seeding, and the shared empty-list singleton. These are
// the small, mostly-pure helpers the other partial files lean on.
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

            DiaryEvent diaryEvent = FindEvent(eventId);
            if (EventFallsAfterFinalDeath(diaryEvent, FinalDeathTickFor(pawn.GetUniqueLoadID(), diary)))
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

            DiaryEvent diaryEvent = FindEvent(eventId);
            if (EventFallsAfterFinalDeath(diaryEvent, FinalDeathTickFor(pawn.GetUniqueLoadID(), diary)))
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

        private bool EventFallsAfterFinalDeathForPawn(DiaryEvent diaryEvent, string pawnId)
        {
            if (diaryEvent == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            return EventFallsAfterFinalDeath(diaryEvent, FinalDeathTickFor(pawnId, diary));
        }

        private static bool EventFallsAfterFinalDeath(DiaryEvent diaryEvent, int? finalDeathTick)
        {
            return diaryEvent != null && finalDeathTick.HasValue && diaryEvent.tick > finalDeathTick.Value;
        }

        /// <summary>
        /// Locates a DiaryEvent by its unique ID.
        /// </summary>
        private DiaryEvent FindEvent(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId) || diaryEvents == null)
            {
                return null;
            }

            for (int i = 0; i < diaryEvents.Count; i++)
            {
                if (diaryEvents[i].eventId == eventId)
                {
                    return diaryEvents[i];
                }
            }

            return null;
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
            foreach (Pawn colonist in PawnsFinder.AllMaps_FreeColonists)
            {
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
        /// Migrates old saves: ensures a persona is assigned even if the Def was removed or never set.
        /// </summary>
        private static void EnsurePawnDiaryDefaults(PawnDiaryRecord diary)
        {
            if (diary == null)
            {
                return;
            }

            // Existing saves may have no persona, and XML mods may remove an old persona Def.
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

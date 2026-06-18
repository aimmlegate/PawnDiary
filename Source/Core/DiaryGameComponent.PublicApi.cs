// Read/write entry points the UI (the Diary tab and social-log integration) calls on the live
// DiaryGameComponent: list a pawn's entries, jump from a social-log row to its generated entry,
// and toggle/read the per-pawn generation flag and persona. These are pure-ish accessors over the
// saved data; all the heavy lifting (recording, generation) lives in the sibling partial files.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Returns the diary entries to render for a pawn, bounded by that pawn's arrival and death pages.
        /// Pure read — no side effects. Generation is driven entirely by the background tick scan.
        /// </summary>
        public IReadOnlyList<DiaryEntryView> EntriesFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return EmptyEntries.List;
            }

            string pawnId = pawn.GetUniqueLoadID();

            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary == null)
            {
                return EmptyEntries.List;
            }

            List<DiaryEntryView> views = new List<DiaryEntryView>();

            if (diary.eventIds != null)
            {
                // This runs every frame the tab is open. Compute the arrival/death boundary once for
                // the pawn, then reuse it for every event below — re-deriving it per event made this
                // call grow with the square of the pawn's entry count. i is the event's own index in
                // the pawn's list, so we can pass it straight to the bounds check.
                DiaryBounds bounds = ComputeDiaryBounds(pawnId, diary);
                for (int i = 0; i < diary.eventIds.Count; i++)
                {
                    DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
                    if (EventFallsOutsideDiaryBounds(diaryEvent, i, bounds))
                    {
                        continue;
                    }

                    DiaryEntryView view = diaryEvent?.ToViewFor(pawnId);
                    if (view != null)
                    {
                        views.Add(view);
                    }
                }
            }

            return views;
        }

        /// <summary>
        /// Cheap per-frame cache key for a pawn's rendered diary. It changes whenever the pawn gains an
        /// event (events are append-only, so the count only rises) or any event's rendered state
        /// changes (<see cref="DiaryStateVersion"/>). The diary tab compares this between frames and
        /// rebuilds its <see cref="DiaryEntryView"/> list only when it differs, so it does not
        /// re-classify and re-parse every entry on every frame. See ITab_Pawn_Diary.FillTab.
        /// </summary>
        public DiaryRenderToken RenderTokenFor(Pawn pawn)
        {
            PawnDiaryRecord diary = pawn == null ? null : FindDiary(pawn, false);
            return new DiaryRenderToken(diary?.eventIds?.Count ?? 0, DiaryStateVersion.Current);
        }

        /// <summary>
        /// Finds the generated diary entry that belongs to a clicked RimWorld social-log row.
        /// Returns null when the event was not recorded, belongs to another pawn, or has not
        /// produced visible LLM text yet; callers should keep vanilla click behavior in that case.
        /// </summary>
        public DiaryEntryView GeneratedEntryForPlayLogEntry(Pawn pawn, int playLogEntryId)
        {
            if (!IsDiaryEligible(pawn) || playLogEntryId < 0)
            {
                return null;
            }

            string pawnId = pawn.GetUniqueLoadID();
            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary == null || diary.eventIds == null)
            {
                return null;
            }

            // Compute the arrival/death boundary once and reuse the index-based check, mirroring
            // EntriesFor. The per-event (pawnId, diary) overload re-derives the boundary on every
            // call, which made this loop grow with the square of the pawn's entry count.
            DiaryBounds bounds = ComputeDiaryBounds(pawnId, diary);
            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = FindEvent(diary.eventIds[i]);
                if (diaryEvent == null || !diaryEvent.MatchesPlayLogEntry(playLogEntryId))
                {
                    continue;
                }

                if (EventFallsOutsideDiaryBounds(diaryEvent, i, bounds))
                {
                    continue;
                }

                DiaryEntryView view = diaryEvent.ToViewFor(pawnId);
                if (view != null && !string.IsNullOrWhiteSpace(view.GeneratedText))
                {
                    return view;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns whether a pawn has diary generation enabled (defaults to true if no record exists yet).
        /// </summary>
        public bool DiaryGenerationEnabledFor(Pawn pawn)
        {
            if (!IsDiaryEligible(pawn))
            {
                return false;
            }

            return FindDiary(pawn, true)?.diaryGenerationEnabled ?? true;
        }

        public void SetDiaryGenerationEnabled(Pawn pawn, bool enabled)
        {
            if (!IsDiaryEligible(pawn))
            {
                return;
            }

            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary != null)
            {
                diary.diaryGenerationEnabled = enabled;
                if (enabled)
                {
                    QueuePendingGenerationsForPawn(pawn.GetUniqueLoadID());
                }
            }
        }

        public DiaryPersonaDef PersonaFor(Pawn pawn)
        {
            if (!IsDiaryEligible(pawn))
            {
                return DiaryPersonas.Default;
            }

            PawnDiaryRecord diary = FindDiary(pawn, true);
            return DiaryPersonas.Resolve(diary?.personaDefName);
        }

        public void SetPersona(Pawn pawn, string personaDefName)
        {
            if (!IsDiaryEligible(pawn))
            {
                return;
            }

            DiaryPersonaDef persona = DiaryPersonas.Resolve(personaDefName);
            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary != null)
            {
                diary.personaDefName = persona.defName;
            }
        }
    }
}

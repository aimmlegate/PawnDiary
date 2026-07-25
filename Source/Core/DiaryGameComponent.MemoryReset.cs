// Pawn-memory reset boundary. Vanilla Brainwipe erases a pawn's remembered experiences several hours
// after its psychic ritual finishes; this partial keeps the corresponding saved Pawn Diary mutation
// inside the component that owns those records. Voice/settings survive because they describe how the
// pawn writes after the wipe, while pages and narrative-memory bookkeeping do not.
using System;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Forgets every personal diary page and narrative memory owned by one pawn while preserving
        /// their player-configured voice and generation settings. Shared events remain available to
        /// other pawns; only events no diary references anymore are removed from the hot repository.
        /// </summary>
        internal void ForgetDiaryHistory(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            if (diary == null)
            {
                return;
            }

            diary.eventIds?.Clear();
            diary.favoriteEntryKeys?.Clear();
            diary.hasUnreadGeneratedEntry = false;
            diary.acknowledgedGeneratedEntryCount = 0;

            // Culture provenance describes identity rather than an episodic memory, so retain it while
            // dropping the durable important-event records used as factual prompt memories.
            PawnKnowledgeState knowledge = diary.KnowledgeStateOrNull();
            knowledge?.records?.Clear();

            // Reset every scheduler/cache whose values refer to now-forgotten pages. Fresh reflection
            // and belief state request silent baselines before they can write about post-wipe changes.
            diary.arcSchedule = new PawnArcScheduleState();
            diary.beliefState = new PawnBeliefState();
            diary.reflectionState = new PawnReflectionState();

            archive.RemoveForPawn(pawnId);
            RemovePendingDayDigestForPawn(pawnId);
            events.RetainOnly(CollectHotReferencedEventIds());
            SetCachedCommandStatus(pawnId, 0, 0, false);
            DiaryStateVersion.Bump();
        }

        /// <summary>Removes saved and indexed daily digest facts collected before the brainwipe.</summary>
        private void RemovePendingDayDigestForPawn(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            if (dayDigestStates != null)
            {
                dayDigestStates.RemoveAll(state =>
                    state != null && string.Equals(state.pawnId, pawnId, StringComparison.Ordinal));
            }

            // The dictionary is a transient index over dayDigestStates. Rebuilding it is less fragile
            // than duplicating the composite pawn/day key rules here.
            RebuildDayDigestIndex();
        }
    }
}

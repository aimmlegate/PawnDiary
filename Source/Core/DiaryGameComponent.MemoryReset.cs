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
        /// Returns whether the reset removed the pawn's exact player-authored background singleton, so
        /// the post-Brainwipe adapter can show a truthful non-blocking notice only when relevant.
        /// </summary>
        internal bool ForgetDiaryHistory(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            string pawnId = pawn.GetUniqueLoadID();
            // A row may be accepted before its source page creates a diary record, so cancel the wiped
            // pawn's future autobiographical reflection before the missing-diary early return below.
            // Rows where this pawn is only the subject belong to another pawn and intentionally survive.
            CancelPendingSocialReflectionsForWriter(pawnId);
            PawnDiaryRecord diary = FindDiaryByPawnId(pawnId);
            if (diary == null)
            {
                return false;
            }

            diary.eventIds?.Clear();
            diary.favoriteEntryKeys?.Clear();
            diary.hasUnreadGeneratedEntry = false;
            diary.unreadGeneratedEntryCount = 0;
            diary.acknowledgedGeneratedEntryCount = 0;

            // Culture provenance describes identity rather than an episodic memory, so retain it while
            // dropping the durable important-event records and player background used as factual prompts.
            PawnKnowledgeState knowledge = diary.KnowledgeStateOrNull();
            bool removedPlayerBackground = HasCanonicalBackgroundMemory(knowledge, pawnId);
            knowledge?.records?.Clear();

            // Reset every scheduler/cache whose values refer to now-forgotten pages. Fresh reflection
            // and belief state request silent baselines before they can write about post-wipe changes.
            diary.arcSchedule = new PawnArcScheduleState();
            diary.beliefState = new PawnBeliefState();
            diary.reflectionState = new PawnReflectionState();

            archive.RemoveForPawn(pawnId);
            RemovePendingDayDigestForPawn(pawnId);
            events.RetainOnly(CollectHotReferencedEventIds());
            SetCachedCommandStatus(pawnId, 0, 0, 0);
            DiaryStateVersion.Bump();
            return removedPlayerBackground;
        }

        private static bool HasCanonicalBackgroundMemory(
            PawnKnowledgeState state,
            string ownerPawnId)
        {
            if (state?.records == null)
            {
                return false;
            }

            for (int i = 0; i < state.records.Count; i++)
            {
                ImportantMemoryRecord record = state.records[i];
                if (record != null && PlayerMemoryPolicy.IsCanonicalBackstory(
                    ownerPawnId,
                    record.recordId,
                    record.dedupKey,
                    record.eventKind,
                    record.sourceKind,
                    record.recallScope))
                {
                    return true;
                }
            }

            return false;
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

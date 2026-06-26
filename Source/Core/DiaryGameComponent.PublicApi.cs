// Read/write entry points the UI (the Diary tab and social-log integration) calls on the live
// DiaryGameComponent: list a pawn's entries, jump from a social-log row to its generated entry,
// and toggle/read the per-pawn generation flag and writing style. These are pure-ish accessors over the
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
        /// Compact status snapshot for the selected-pawn Diary command. It keeps the gizmo out of the
        /// saved event internals while still showing whether pages are being written or newly ready.
        /// </summary>
        public struct DiaryCommandStatus
        {
            public int completedCount;
            public int unacknowledgedCount;
            public int pendingCount;

            public bool HasNewPages
            {
                get { return unacknowledgedCount > 0; }
            }

            public bool IsWriting
            {
                get { return pendingCount > 0; }
            }
        }

        /// <summary>
        /// Returns the diary entries to render for a pawn, bounded by that pawn's arrival and death pages.
        /// Pure read — no side effects. Generation is driven by capture hooks plus demand-driven
        /// catch-up scans, never by opening the UI.
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
                    DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
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

        // Memoized completed/pending counts for CommandStatusFor, keyed by pawn id + render token.
        // The Diary tab asks for this status every frame via AcknowledgeGeneratedEntriesFor, and the
        // count scan builds a DiaryEntryView per in-bounds entry (ToViewFor does group classification
        // + linked-entry preview building), so without this cache the tab's cost grew with the pawn's
        // whole history every frame. Invalidated whenever the pawn or its render token changes (new or
        // changed entry text/status). unacknowledgedCount is NOT cached: it depends on the acknowledged
        // counter, which AcknowledgeGeneratedEntriesFor can move without a token change.
        private string cachedStatusPawnId;
        private DiaryRenderToken cachedStatusToken;
        private int cachedStatusCompleted;
        private int cachedStatusPending;

        /// <summary>
        /// Returns the current completed/pending counts for the selected-pawn Diary command badge.
        /// Opening the Diary tab calls <see cref="AcknowledgeGeneratedEntriesFor"/>; this reader only
        /// mutates old saves once, baselining pre-existing pages so they do not all appear "new."
        /// </summary>
        /// <remarks>
        /// Per-frame caller: the Diary tab calls this via <see cref="AcknowledgeGeneratedEntriesFor"/>
        /// every draw frame. The expensive part (the per-event <see cref="DiaryEvent.ToViewFor"/> scan)
        /// is memoized by the pawn's render token and only reruns when an entry's text or status
        /// changes. <c>unacknowledgedCount</c> is derived fresh each call.
        /// </remarks>
        public DiaryCommandStatus CommandStatusFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return default(DiaryCommandStatus);
            }

            string pawnId = pawn.GetUniqueLoadID();
            DiaryRenderToken token = RenderTokenFor(pawn);
            if (cachedStatusPawnId != pawnId || !token.Equals(cachedStatusToken))
            {
                CountCompletedAndPending(pawn, pawnId, out int completed, out int pending);
                cachedStatusPawnId = pawnId;
                cachedStatusToken = token;
                cachedStatusCompleted = completed;
                cachedStatusPending = pending;
            }

            DiaryCommandStatus status = default(DiaryCommandStatus);
            status.completedCount = cachedStatusCompleted;
            status.pendingCount = cachedStatusPending;

            PawnDiaryRecord diary = FindDiary(pawn, false);
            int acknowledged = diary == null ? 0 : diary.acknowledgedGeneratedEntryCount;
            if (acknowledged < 0)
            {
                // Old save / first read: treat the already-completed backlog as acknowledged so the
                // badge does not flag every existing page as "new".
                acknowledged = status.completedCount;
                if (diary != null)
                {
                    diary.acknowledgedGeneratedEntryCount = acknowledged;
                }
            }
            status.unacknowledgedCount = Math.Max(0, status.completedCount - acknowledged);
            return status;
        }

        /// <summary>
        /// Counts in-bounds completed and pending entries for a pawn. Extracted from
        /// <see cref="CommandStatusFor"/> so the result can be memoized by render token; the counting
        /// logic is unchanged (it still mirrors <see cref="DiaryEvent.ToViewFor"/> so the generated-
        /// text and pending flags stay in sync with what the tab renders).
        /// </summary>
        private void CountCompletedAndPending(Pawn pawn, string pawnId, out int completed, out int pending)
        {
            completed = 0;
            pending = 0;
            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary == null || diary.eventIds == null)
            {
                return;
            }

            DiaryBounds bounds = ComputeDiaryBounds(pawnId, diary);
            for (int i = 0; i < diary.eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
                if (EventFallsOutsideDiaryBounds(diaryEvent, i, bounds))
                {
                    continue;
                }

                DiaryEntryView view = diaryEvent?.ToViewFor(pawnId);
                if (view == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(view.GeneratedText))
                {
                    completed++;
                }

                if (view.LlmStatus == DiaryEvent.PendingStatus || view.TitlePending)
                {
                    pending++;
                }
            }
        }

        /// <summary>
        /// Marks the currently completed pages for a pawn as seen. Called when the player opens that
        /// pawn's Diary tab, clearing the command's "new page" count while preserving in-flight status.
        /// </summary>
        public void AcknowledgeGeneratedEntriesFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary == null)
            {
                return;
            }

            DiaryCommandStatus status = CommandStatusFor(pawn);
            if (diary.acknowledgedGeneratedEntryCount != status.completedCount)
            {
                diary.acknowledgedGeneratedEntryCount = status.completedCount;
            }
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
        /// Dev UI action: rewrite an existing diary page using the current API routing/model settings.
        /// Pairwise entries reset both POVs when both are eligible so the linked preview stays in sync.
        /// Returns false when the event cannot be found, is already being written, or generation is off.
        /// </summary>
        public bool RegenerateEntry(Pawn pawn, DiaryEntryView entry)
        {
            if (pawn == null || entry == null || string.IsNullOrWhiteSpace(entry.EventId))
            {
                return false;
            }

            DiaryEvent diaryEvent = events.FindEvent(entry.EventId);
            if (diaryEvent == null || string.IsNullOrWhiteSpace(entry.PovRole))
            {
                return false;
            }

            Dictionary<string, DiaryBoundsCacheEntry> boundsCache = new Dictionary<string, DiaryBoundsCacheEntry>();
            Dictionary<string, Pawn> livePawnsById = SnapshotLivePawnsByLoadId();

            if (diaryEvent.HasDeathDescription() || diaryEvent.HasArrivalDescription())
            {
                return RegenerateRole(diaryEvent, DiaryEvent.NeutralRole, boundsCache, livePawnsById);
            }

            if (!diaryEvent.solo && DiaryEvent.RoleIsInitiatorOrRecipient(entry.PovRole))
            {
                return RegeneratePairwiseEntry(diaryEvent, boundsCache, livePawnsById);
            }

            return RegenerateRole(diaryEvent, entry.PovRole, boundsCache, livePawnsById);
        }

        private bool RegeneratePairwiseEntry(
            DiaryEvent diaryEvent,
            Dictionary<string, DiaryBoundsCacheEntry> boundsCache,
            Dictionary<string, Pawn> livePawnsById)
        {
            if (diaryEvent == null || diaryEvent.IsPending(DiaryEvent.InitiatorRole) || diaryEvent.IsPending(DiaryEvent.RecipientRole))
            {
                return false;
            }

            bool initiatorEnabled = DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.InitiatorRole, boundsCache);
            bool recipientEnabled = DiaryGenerationEnabledFor(diaryEvent, DiaryEvent.RecipientRole, boundsCache);
            if (!initiatorEnabled && !recipientEnabled)
            {
                return false;
            }

            if (initiatorEnabled)
            {
                diaryEvent.PrepareForRegeneration(DiaryEvent.InitiatorRole);
            }

            if (recipientEnabled)
            {
                diaryEvent.PrepareForRegeneration(DiaryEvent.RecipientRole);
            }

            QueueSequentialPairwiseRewrite(diaryEvent, null, boundsCache, livePawnsById);
            return true;
        }

        private bool RegenerateRole(
            DiaryEvent diaryEvent,
            string povRole,
            Dictionary<string, DiaryBoundsCacheEntry> boundsCache,
            Dictionary<string, Pawn> livePawnsById)
        {
            if (diaryEvent == null
                || string.IsNullOrWhiteSpace(povRole)
                || diaryEvent.IsPending(povRole)
                || !DiaryGenerationEnabledFor(diaryEvent, povRole, boundsCache))
            {
                return false;
            }

            diaryEvent.PrepareForRegeneration(povRole);
            EnsureGenerationQueued(diaryEvent, povRole, boundsCache, livePawnsById);
            return true;
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
                DiaryEvent diaryEvent = events.FindEvent(diary.eventIds[i]);
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

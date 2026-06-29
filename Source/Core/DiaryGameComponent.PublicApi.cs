// Read/write entry points the UI (the Diary tab and social-log integration) calls on the live
// DiaryGameComponent: list a pawn's entries, jump from a social-log row to its generated entry,
// and toggle/read the per-pawn generation flag and writing style. These are pure-ish accessors over the
// saved data; all the heavy lifting (recording, generation) lives in the sibling partial files.
// This is one piece of the partial DiaryGameComponent class — see DiaryGameComponent.cs for the map.
using System;
using System.Collections.Generic;
using UnityEngine;
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
        /// Lightweight Diary-tab index for one pawn. It stores only year/count metadata and backing
        /// event references; real <see cref="DiaryEntryView" /> objects are built later for the
        /// selected year only.
        /// </summary>
        public sealed class DiaryTabYearIndex
        {
            private struct Candidate
            {
                public DiaryEvent diaryEvent;
                public int year;
                public bool archivedForScans;
            }

            private readonly List<Candidate> candidates = new List<Candidate>();
            private readonly Dictionary<int, int> countsByYear = new Dictionary<int, int>();
            private readonly Dictionary<string, int> yearByEventId = new Dictionary<string, int>();
            private readonly Dictionary<int, List<int>> candidateIndexesByYear = new Dictionary<int, List<int>>();

            public readonly List<int> years = new List<int>();
            public int generatingCount;
            public int completedCount;
            public int pendingCount;

            public int CountForYear(int year)
            {
                int count;
                return countsByYear.TryGetValue(year, out count) ? count : 0;
            }

            public bool TryGetYearForEvent(string eventId, out int year)
            {
                if (string.IsNullOrWhiteSpace(eventId))
                {
                    year = 0;
                    return false;
                }

                return yearByEventId.TryGetValue(eventId, out year);
            }

            public void AppendEntriesForYear(List<DiaryEntryView> target, string pawnId, int year)
            {
                if (target == null)
                {
                    return;
                }

                for (int i = 0; i < candidates.Count; i++)
                {
                    Candidate candidate = candidates[i];
                    if (candidate.year != year)
                    {
                        continue;
                    }

                    DiaryEntryView view = candidate.diaryEvent?.ToViewFor(pawnId, candidate.archivedForScans);
                    if (view != null)
                    {
                        target.Add(view);
                    }
                }
            }

            public bool AppendEntriesForYearSlice(
                List<DiaryEntryView> target,
                string pawnId,
                int year,
                ref int yearCandidateIndex,
                int maxEvents,
                float maxSeconds)
            {
                if (target == null || string.IsNullOrWhiteSpace(pawnId))
                {
                    return true;
                }

                List<int> indexes;
                if (!candidateIndexesByYear.TryGetValue(year, out indexes) || indexes.Count == 0)
                {
                    return true;
                }

                int processedThisSlice = 0;
                int max = Math.Max(1, maxEvents);
                float endTime = Time.realtimeSinceStartup + Math.Max(0.0001f, maxSeconds);

                while (yearCandidateIndex < indexes.Count && processedThisSlice < max)
                {
                    Candidate candidate = candidates[indexes[yearCandidateIndex]];
                    yearCandidateIndex++;
                    processedThisSlice++;

                    DiaryEntryView view = candidate.diaryEvent?.ToViewFor(pawnId, candidate.archivedForScans);
                    if (view != null)
                    {
                        target.Add(view);
                    }

                    if (processedThisSlice > 0 && Time.realtimeSinceStartup >= endTime)
                    {
                        break;
                    }
                }

                return yearCandidateIndex >= indexes.Count;
            }

            internal void Add(DiaryEvent diaryEvent, int year, bool archivedForScans, bool hasGeneratedText, bool generating, bool titlePending)
            {
                int candidateIndex = candidates.Count;
                candidates.Add(new Candidate
                {
                    diaryEvent = diaryEvent,
                    year = year,
                    archivedForScans = archivedForScans
                });

                if (!countsByYear.ContainsKey(year))
                {
                    countsByYear[year] = 0;
                    years.Add(year);
                    candidateIndexesByYear[year] = new List<int>();
                }

                countsByYear[year]++;
                candidateIndexesByYear[year].Add(candidateIndex);

                string eventId = diaryEvent?.eventId;
                if (!string.IsNullOrWhiteSpace(eventId) && !yearByEventId.ContainsKey(eventId))
                {
                    yearByEventId[eventId] = year;
                }

                if (hasGeneratedText)
                {
                    completedCount++;
                }

                if (generating)
                {
                    generatingCount++;
                }

                if (generating || titlePending)
                {
                    pendingCount++;
                }
            }

            internal void SortYearsDescending()
            {
                years.Sort((left, right) => right.CompareTo(left));
            }
        }

        /// <summary>
        /// Incremental, main-thread builder for a pawn's Diary tab year index. It deliberately avoids
        /// background threads because the scan can touch RimWorld/Verse state and saved mod objects.
        /// </summary>
        public sealed class DiaryTabYearIndexBuild
        {
            private enum BuildPhase
            {
                DiaryEvents,
                Complete
            }

            private readonly DiaryGameComponent owner;
            private readonly PawnDiaryRecord diary;
            private readonly string pawnId;
            private readonly bool showLlmDebugInfo;
            private readonly bool showGeneratingEntries;
            private readonly bool showPromptOnlyEntries;
            private readonly HashSet<string> activeEventIds;

            private DiaryBounds bounds = new DiaryBounds { firstArrivalIndex = -1, finalDeathIndex = -1 };
            private BuildPhase phase = BuildPhase.DiaryEvents;
            private int diaryIndex;

            public readonly DiaryRenderToken token;
            public readonly DiaryTabYearIndex index = new DiaryTabYearIndex();
            public readonly int totalWork;
            public int processedWork;

            internal DiaryTabYearIndexBuild(
                DiaryGameComponent owner,
                Pawn pawn,
                bool showLlmDebugInfo,
                bool showGeneratingEntries,
                bool showPromptOnlyEntries)
            {
                this.owner = owner;
                this.showLlmDebugInfo = showLlmDebugInfo;
                this.showGeneratingEntries = showGeneratingEntries;
                this.showPromptOnlyEntries = showPromptOnlyEntries;

                pawnId = pawn?.GetUniqueLoadID();
                diary = pawn == null ? null : owner.FindDiary(pawn, false);
                token = owner.RenderTokenFor(pawn);
                activeEventIds = owner.ActiveScanEventIds();
                totalWork = diary?.eventIds?.Count ?? 0;

                if (string.IsNullOrWhiteSpace(pawnId) || diary?.eventIds == null)
                {
                    phase = BuildPhase.Complete;
                }
            }

            public bool IsComplete
            {
                get { return phase == BuildPhase.Complete; }
            }

            public bool Matches(Pawn pawn, DiaryRenderToken currentToken, bool showDebug, bool showGenerating, bool showPromptOnly)
            {
                // Only a STRUCTURAL change invalidates an in-progress build: a different pawn, a
                // different event count (events were added/removed), or a filter toggle. The render
                // token also carries the process-wide DiaryStateVersion, which ticks whenever ANY
                // pawn's entry status/text/title changes anywhere in the colony. Testing the full
                // token here used to discard the in-progress build on every such tick, so during
                // ordinary generation a large history could never finish loading — each tick reset
                // the frame-sliced scan back to event zero. The build reads live per-event state as
                // it goes; a state-version tick mid-build only means a few already-indexed entries
                // have slightly stale visibility, which the completed-index quiet-refresh path
                // (RebuildIndexIfNeeded) corrects within a few frames. So the volatile stateVersion
                // is deliberately ignored here, and only the structural eventCount is compared.
                string currentPawnId = pawn?.GetUniqueLoadID();
                return string.Equals(currentPawnId, pawnId, StringComparison.Ordinal)
                    && currentToken.eventCount == token.eventCount
                    && showDebug == showLlmDebugInfo
                    && showGenerating == showGeneratingEntries
                    && showPromptOnly == showPromptOnlyEntries;
            }

            public void ProcessSlice(int maxEvents, float maxSeconds)
            {
                if (IsComplete)
                {
                    return;
                }

                int processedThisSlice = 0;
                int max = Math.Max(1, maxEvents);
                float endTime = Time.realtimeSinceStartup + Math.Max(0.0001f, maxSeconds);

                while (!IsComplete && processedThisSlice < max)
                {
                    bool didWork = ProcessOne();
                    if (didWork)
                    {
                        processedThisSlice++;
                        processedWork++;
                    }

                    if (processedThisSlice > 0 && Time.realtimeSinceStartup >= endTime)
                    {
                        break;
                    }
                }
            }

            private bool ProcessOne()
            {
                switch (phase)
                {
                    case BuildPhase.DiaryEvents:
                        return ProcessDiaryEvent();
                    default:
                        return false;
                }
            }

            private bool ProcessDiaryEvent()
            {
                if (diary?.eventIds == null || diaryIndex >= diary.eventIds.Count)
                {
                    index.SortYearsDescending();
                    phase = BuildPhase.Complete;
                    return false;
                }

                int eventIndex = diaryIndex;
                DiaryEvent diaryEvent = owner.events.FindEvent(diary.eventIds[diaryIndex]);
                diaryIndex++;
                if (diaryEvent == null)
                {
                    return true;
                }

                try
                {
                    IndexDiaryEvent(diaryEvent, eventIndex);
                }
                catch (Exception e)
                {
                    Log.ErrorOnce("[Pawn Diary] Skipped one diary tab entry while building the visible index: " + e,
                        ("DiaryTabYearIndexBuild:" + (diaryEvent.eventId ?? eventIndex.ToString())).GetHashCode());
                }

                return true;
            }

            private void IndexDiaryEvent(DiaryEvent diaryEvent, int eventIndex)
            {
                TrackBounds(diaryEvent, eventIndex);

                if (EventFallsOutsideKnownSequentialBounds(diaryEvent, eventIndex))
                {
                    return;
                }

                bool archivedForScans = EventIsArchivedForScans(diaryEvent, activeEventIds);
                string povRole;
                bool hasGeneratedText;
                bool archivedGenerationStale;
                bool generating;
                bool promptOnly;
                bool titlePending;
                if (diaryEvent.TryGetTabStateForPawn(
                    pawnId,
                    archivedForScans,
                    out povRole,
                    out hasGeneratedText,
                    out archivedGenerationStale,
                    out generating,
                    out promptOnly,
                    out titlePending))
                {
                    bool visible = showLlmDebugInfo
                        || hasGeneratedText
                        || archivedGenerationStale
                        || (showGeneratingEntries && generating)
                        || (showPromptOnlyEntries && promptOnly);
                    if (visible)
                    {
                        index.Add(
                            diaryEvent,
                            ExtractYear(diaryEvent.date),
                            archivedForScans,
                            hasGeneratedText,
                            generating,
                            titlePending);
                    }
                    else
                    {
                        if (hasGeneratedText)
                        {
                            index.completedCount++;
                        }

                        if (generating)
                        {
                            index.generatingCount++;
                        }

                        if (generating || titlePending)
                        {
                            index.pendingCount++;
                        }
                    }
                }
            }

            private void TrackBounds(DiaryEvent diaryEvent, int eventIndex)
            {
                if (diaryEvent.IsArrivalDescriptionFor(pawnId))
                {
                    if (bounds.firstArrivalIndex < 0)
                    {
                        bounds.firstArrivalIndex = eventIndex;
                    }

                    if (!bounds.firstArrivalTick.HasValue || diaryEvent.tick < bounds.firstArrivalTick.Value)
                    {
                        bounds.firstArrivalTick = diaryEvent.tick;
                    }
                }

                if (diaryEvent.IsDeathDescriptionFor(pawnId))
                {
                    bounds.finalDeathIndex = eventIndex;
                    if (!bounds.finalDeathTick.HasValue || diaryEvent.tick > bounds.finalDeathTick.Value)
                    {
                        bounds.finalDeathTick = diaryEvent.tick;
                    }
                }
            }

            private bool EventFallsOutsideKnownSequentialBounds(DiaryEvent diaryEvent, int eventIndex)
            {
                if (diaryEvent == null)
                {
                    return true;
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
        }

        /// <summary>
        /// Starts a frame-sliced Diary tab index build. Call <see cref="DiaryTabYearIndexBuild.ProcessSlice" />
        /// from the UI until <see cref="DiaryTabYearIndexBuild.IsComplete" /> is true.
        /// </summary>
        public DiaryTabYearIndexBuild BeginTabYearIndexBuild(
            Pawn pawn,
            bool showLlmDebugInfo,
            bool showGeneratingEntries,
            bool showPromptOnlyEntries)
        {
            return new DiaryTabYearIndexBuild(this, pawn, showLlmDebugInfo, showGeneratingEntries, showPromptOnlyEntries);
        }

        private HashSet<string> ActiveScanEventIds()
        {
            IReadOnlyList<DiaryEvent> activeEvents = ActiveScanEvents();
            HashSet<string> ids = new HashSet<string>();
            for (int i = 0; i < activeEvents.Count; i++)
            {
                string id = activeEvents[i]?.eventId;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        private static bool EventIsArchivedForScans(DiaryEvent diaryEvent, HashSet<string> activeEventIds)
        {
            return diaryEvent != null
                && !string.IsNullOrWhiteSpace(diaryEvent.eventId)
                && activeEventIds != null
                && !activeEventIds.Contains(diaryEvent.eventId);
        }

        private const int UnknownDiaryYear = int.MinValue;

        private static int ExtractYear(string date)
        {
            if (string.IsNullOrWhiteSpace(date))
            {
                return UnknownDiaryYear;
            }

            int end = -1;
            for (int i = date.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(date[i]))
                {
                    end = i;
                    break;
                }
            }

            if (end < 0)
            {
                return UnknownDiaryYear;
            }

            int start = end;
            while (start > 0 && char.IsDigit(date[start - 1]))
            {
                start--;
            }

            int year;
            return int.TryParse(date.Substring(start, end - start + 1), out year) ? year : UnknownDiaryYear;
        }

        /// <summary>
        /// Returns the current completed/pending counts for the selected-pawn Diary command badge.
        /// Opening the Diary tab calls <see cref="AcknowledgeGeneratedEntriesFor"/>; this reader only
        /// checks an in-memory per-pawn status cache, never saved diary records or event history.
        /// </summary>
        /// <remarks>
        /// Per-frame caller: the inspect tab marker and command overlay call this from GUI draw. A
        /// cache miss returns an empty status rather than touching saved diary state during selection.
        /// </remarks>
        public DiaryCommandStatus CommandStatusFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return default(DiaryCommandStatus);
            }

            DiaryCommandStatus status = default(DiaryCommandStatus);
            string pawnId = pawn.GetUniqueLoadID();
            return commandStatusByPawnId.TryGetValue(pawnId, out status) ? status : default(DiaryCommandStatus);
        }

        /// <summary>
        /// Updates the command-status cache from a completed sliced tab index. The Diary tab calls this
        /// when it finishes loading so acknowledgement does not kick off a second history scan.
        /// </summary>
        public void AcknowledgeGeneratedEntriesFor(Pawn pawn, int completedCount, int pendingCount, DiaryRenderToken token)
        {
            if (pawn == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            SetCachedCommandStatus(pawnId, completedCount, pendingCount, false);

            PawnDiaryRecord diary = FindDiary(pawn, false);
            if (diary != null)
            {
                diary.hasUnreadGeneratedEntry = false;
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

            diary.hasUnreadGeneratedEntry = false;
            SetCachedCommandUnreadFlag(diary.pawnId, false);
        }

        /// <summary>
        /// Rebuilds the closed-window badge cache after save load. This is the only place old saved
        /// unread flags are copied into the selection-time dictionary.
        /// </summary>
        private void RebuildCommandStatusCache()
        {
            commandStatusByPawnId.Clear();
            if (diaries == null)
            {
                return;
            }

            for (int i = 0; i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                if (diary == null || string.IsNullOrWhiteSpace(diary.pawnId) || !diary.hasUnreadGeneratedEntry)
                {
                    continue;
                }

                SetCachedCommandUnreadFlag(diary.pawnId, true);
            }
        }

        private void SetCachedCommandStatus(string pawnId, int completedCount, int pendingCount, bool hasUnread)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            DiaryCommandStatus status = default(DiaryCommandStatus);
            status.completedCount = Math.Max(0, completedCount);
            status.pendingCount = Math.Max(0, pendingCount);
            status.unacknowledgedCount = hasUnread ? 1 : 0;

            if (status.completedCount <= 0 && status.pendingCount <= 0 && status.unacknowledgedCount <= 0)
            {
                commandStatusByPawnId.Remove(pawnId);
                return;
            }

            commandStatusByPawnId[pawnId] = status;
        }

        private void SetCachedCommandUnreadFlag(string pawnId, bool hasUnread)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            DiaryCommandStatus status;
            commandStatusByPawnId.TryGetValue(pawnId, out status);
            status.unacknowledgedCount = hasUnread ? 1 : 0;

            if (status.completedCount <= 0 && status.pendingCount <= 0 && status.unacknowledgedCount <= 0)
            {
                commandStatusByPawnId.Remove(pawnId);
                return;
            }

            commandStatusByPawnId[pawnId] = status;
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
                return RegenerateRole(diaryEvent, DiaryEvent.NeutralRole, boundsCache, livePawnsById, entry.ArchivedGenerationStale);
            }

            if (!diaryEvent.solo && DiaryEvent.RoleIsInitiatorOrRecipient(entry.PovRole))
            {
                return RegeneratePairwiseEntry(diaryEvent, boundsCache, livePawnsById,
                    entry.ArchivedGenerationStale ? entry.PovRole : null);
            }

            return RegenerateRole(diaryEvent, entry.PovRole, boundsCache, livePawnsById, entry.ArchivedGenerationStale);
        }

        private bool RegeneratePairwiseEntry(
            DiaryEvent diaryEvent,
            Dictionary<string, DiaryBoundsCacheEntry> boundsCache,
            Dictionary<string, Pawn> livePawnsById,
            string archivedStalePovRole)
        {
            if (diaryEvent == null)
            {
                return false;
            }

            bool initiatorPending = diaryEvent.IsPending(DiaryEvent.InitiatorRole);
            bool recipientPending = diaryEvent.IsPending(DiaryEvent.RecipientRole);
            bool allowInitiatorReset = DiaryEvent.RoleEquals(archivedStalePovRole, DiaryEvent.InitiatorRole);
            bool allowRecipientReset = DiaryEvent.RoleEquals(archivedStalePovRole, DiaryEvent.RecipientRole);
            if ((initiatorPending && !allowInitiatorReset) || (recipientPending && !allowRecipientReset))
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
            Dictionary<string, Pawn> livePawnsById,
            bool allowArchivedPendingReset)
        {
            bool pending = diaryEvent != null && diaryEvent.IsPending(povRole);
            if (diaryEvent == null
                || string.IsNullOrWhiteSpace(povRole)
                || (pending && !allowArchivedPendingReset)
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

            // Compute the arrival/death boundary once and reuse the index-based check. The per-event
            // (pawnId, diary) overload re-derives the boundary on every call, which made this loop
            // grow with the square of the pawn's entry count.
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

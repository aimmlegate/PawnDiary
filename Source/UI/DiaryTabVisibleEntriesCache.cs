// Visible-entry cache for the Diary tab. RimWorld redraws inspector tabs every frame, so this
// helper owns the impure pawn/token-backed lists that can be reused between draw passes.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Partial implementation of the pawn Diary inspector tab.
    /// </summary>
    public partial class ITab_Pawn_Diary
    {
        /// <summary>
        /// Caches the selected pawn's lightweight year index and materializes full card views only for
        /// the currently selected year. This keeps pawn switching cheap even when dev fixtures seed
        /// thousands of archived pages.
        /// </summary>
        private sealed class DiaryTabVisibleEntriesCache
        {
            private Pawn cachedIndexPawn;
            private DiaryRenderToken cachedIndexToken;
            private bool cachedIndexShowDebug;
            private bool cachedIndexShowGenerating;
            private bool cachedIndexShowPromptOnly;
            private DevDiaryPreviewKind cachedIndexPreviewKind = DevDiaryPreviewKind.None;
            private DiaryGameComponent.DiaryTabYearIndex cachedIndex;
            private DiaryGameComponent.DiaryTabYearIndexBuild cachedIndexBuild;
            private DiaryEntryView cachedPreviewEntry;

            private readonly List<int> cachedVisibleYears = new List<int>();
            private readonly Dictionary<int, int> cachedYearCounts = new Dictionary<int, int>();
            private readonly List<DiaryEntryView> cachedOrderedEntries = new List<DiaryEntryView>();
            private int cachedVisibleRevision;
            private int cachedGeneratingCount;
            private int cachedCompletedCount;
            private int cachedPendingCount;
            private bool loading;
            private int loadingProcessed;
            private int loadingTotal;
            private bool loadingSelectedYear;
            private int loadingYear;
            private int loadingYearCandidateIndex;
            private string loadingYearPawnId;
            private int cachedOrderedVisibleRevision = -1;
            private int cachedOrderedYear = int.MinValue;

            /// <summary>
            /// Number of hidden pending entries that are currently being written.
            /// </summary>
            public int GeneratingCount
            {
                get { return cachedGeneratingCount; }
            }

            public int CompletedCount
            {
                get { return cachedCompletedCount; }
            }

            public int PendingCount
            {
                get { return cachedPendingCount; }
            }

            public bool IsLoading
            {
                get { return loading; }
            }

            public int LoadingProcessed
            {
                get { return loadingProcessed; }
            }

            public int LoadingTotal
            {
                get { return loadingTotal; }
            }

            /// <summary>
            /// Distinct years represented by entries visible under the current dev filters, newest first.
            /// </summary>
            public List<int> VisibleYears
            {
                get { return cachedVisibleYears; }
            }

            /// <summary>
            /// Monotonic version for the current visible year index. The scroll-layout cache uses it
            /// to know when selected-year contents may have changed even though lists are reused.
            /// </summary>
            public int VisibleRevision
            {
                get { return cachedVisibleRevision; }
            }

            /// <summary>
            /// Rebuilds the lightweight year index only when the pawn, render token, or tab filters
            /// change. No full card views are created here for saved entries.
            /// </summary>
            public void RebuildIndexIfNeeded(
                Pawn pawn,
                DiaryGameComponent component,
                bool showLlmDebugInfo,
                bool showGeneratingEntries,
                bool showPromptOnlyEntries,
                DevDiaryPreviewKind devPreviewKind,
                out DiaryRenderToken token)
            {
                token = default(DiaryRenderToken);
                if (component == null)
                {
                    ClearIfNeeded();
                    return;
                }

                token = component.RenderTokenFor(pawn);
                if (pawn == cachedIndexPawn
                    && token.Equals(cachedIndexToken)
                    && cachedIndexShowDebug == showLlmDebugInfo
                    && cachedIndexShowGenerating == showGeneratingEntries
                    && cachedIndexShowPromptOnly == showPromptOnlyEntries
                    && cachedIndexPreviewKind == devPreviewKind
                    && cachedIndex != null)
                {
                    return;
                }

                if (cachedIndexBuild == null
                    || !cachedIndexBuild.Matches(pawn, token, showLlmDebugInfo, showGeneratingEntries, showPromptOnlyEntries)
                    || cachedIndexPreviewKind != devPreviewKind)
                {
                    cachedIndexPawn = pawn;
                    cachedIndexToken = token;
                    cachedIndexShowDebug = showLlmDebugInfo;
                    cachedIndexShowGenerating = showGeneratingEntries;
                    cachedIndexShowPromptOnly = showPromptOnlyEntries;
                    cachedIndexPreviewKind = devPreviewKind;
                    cachedIndexBuild = component.BeginTabYearIndexBuild(pawn, showLlmDebugInfo, showGeneratingEntries, showPromptOnlyEntries);
                    cachedIndex = null;
                    cachedPreviewEntry = null;
                    cachedGeneratingCount = 0;
                    cachedCompletedCount = 0;
                    cachedPendingCount = 0;
                    cachedVisibleYears.Clear();
                    cachedYearCounts.Clear();
                    cachedOrderedEntries.Clear();
                    cachedOrderedVisibleRevision = -1;
                    loadingSelectedYear = false;
                    loadingYearCandidateIndex = 0;
                    loading = true;
                }

                cachedIndexBuild.ProcessSlice(DiaryTuning.UiHistoryScanMaxEventsPerFrame, DiaryTuning.UiHistoryScanFrameBudgetSeconds);
                loadingProcessed = cachedIndexBuild.processedWork;
                loadingTotal = cachedIndexBuild.totalWork;
                if (!cachedIndexBuild.IsComplete)
                {
                    return;
                }

                cachedIndexPawn = pawn;
                cachedIndexToken = token;
                cachedIndexShowDebug = showLlmDebugInfo;
                cachedIndexShowGenerating = showGeneratingEntries;
                cachedIndexShowPromptOnly = showPromptOnlyEntries;
                cachedIndexPreviewKind = devPreviewKind;
                cachedIndex = cachedIndexBuild.index;
                cachedIndexBuild = null;
                cachedPreviewEntry = null;
                cachedGeneratingCount = cachedIndex == null ? 0 : cachedIndex.generatingCount;
                cachedCompletedCount = cachedIndex == null ? 0 : cachedIndex.completedCount;
                cachedPendingCount = cachedIndex == null ? 0 : cachedIndex.pendingCount;
                loading = false;
                loadingProcessed = loadingTotal;

                cachedVisibleYears.Clear();
                cachedYearCounts.Clear();

                if (cachedIndex != null)
                {
                    for (int i = 0; i < cachedIndex.years.Count; i++)
                    {
                        int year = cachedIndex.years[i];
                        AddYearCount(year, cachedIndex.CountForYear(year));
                    }
                }

                AddDevPreviewEntryIfNeeded(pawn, devPreviewKind);
                cachedVisibleYears.Sort((left, right) => right.CompareTo(left));
                cachedVisibleRevision++;
                cachedOrderedVisibleRevision = -1;
            }

            /// <summary>
            /// Returns how many visible entries belong to a year, used by the year pager label/menu.
            /// </summary>
            public int CountForYear(int year)
            {
                int count;
                return cachedYearCounts.TryGetValue(year, out count) ? count : 0;
            }

            /// <summary>
            /// Finds the year for a visible event id without building every card view.
            /// </summary>
            public bool TryGetYearForEvent(string eventId, out int year)
            {
                if (cachedPreviewEntry != null && cachedPreviewEntry.EventId == eventId)
                {
                    year = EntryYear(cachedPreviewEntry);
                    return true;
                }

                if (cachedIndex != null && cachedIndex.TryGetYearForEvent(eventId, out year))
                {
                    return true;
                }

                year = UnknownYear;
                return false;
            }

            /// <summary>
            /// Builds and caches full card views for one selected year in small slices. Long archived
            /// years still use viewport virtualization after this; older years are not materialized
            /// until selected.
            /// </summary>
            public bool TryGetOrderedEntriesForSelectedYear(Pawn pawn, int year, out List<DiaryEntryView> ordered)
            {
                ordered = cachedOrderedEntries;
                if (cachedOrderedVisibleRevision == cachedVisibleRevision && cachedOrderedYear == year)
                {
                    return true;
                }

                string pawnId = pawn?.GetUniqueLoadID();
                if (!loadingSelectedYear
                    || loadingYear != year
                    || loadingYearPawnId != pawnId)
                {
                    cachedOrderedEntries.Clear();
                    if (cachedPreviewEntry != null && EntryYear(cachedPreviewEntry) == year)
                    {
                        cachedOrderedEntries.Add(cachedPreviewEntry);
                    }

                    loadingSelectedYear = true;
                    loadingYear = year;
                    loadingYearPawnId = pawnId;
                    loadingYearCandidateIndex = 0;
                }

                loading = true;
                loadingProcessed = loadingYearCandidateIndex;
                loadingTotal = CountForYear(year);
                bool complete = cachedIndex == null
                    || cachedIndex.AppendEntriesForYearSlice(
                        cachedOrderedEntries,
                        pawnId,
                        year,
                        ref loadingYearCandidateIndex,
                        DiaryTuning.UiHistoryScanMaxEventsPerFrame,
                        DiaryTuning.UiHistoryScanFrameBudgetSeconds);
                loadingProcessed = loadingYearCandidateIndex;
                if (!complete)
                {
                    return false;
                }

                cachedOrderedEntries.Sort((left, right) => right.Tick.CompareTo(left.Tick));
                cachedOrderedVisibleRevision = cachedVisibleRevision;
                cachedOrderedYear = year;
                loadingSelectedYear = false;
                loading = false;
                loadingProcessed = loadingTotal;
                return true;
            }

            /// <summary>
            /// Forces the next selected-year ordering request to rebuild from the cached year index.
            /// </summary>
            public void InvalidateOrdering()
            {
                cachedOrderedVisibleRevision = -1;
            }

            private void ClearIfNeeded()
            {
                if (cachedIndex == null
                    && cachedIndexBuild == null
                    && cachedVisibleYears.Count == 0
                    && cachedYearCounts.Count == 0
                    && cachedPreviewEntry == null
                    && cachedOrderedEntries.Count == 0)
                {
                    return;
                }

                cachedIndexPawn = null;
                cachedIndexToken = default(DiaryRenderToken);
                cachedIndex = null;
                cachedIndexBuild = null;
                cachedPreviewEntry = null;
                cachedGeneratingCount = 0;
                cachedCompletedCount = 0;
                cachedPendingCount = 0;
                loading = false;
                loadingProcessed = 0;
                loadingTotal = 0;
                cachedVisibleYears.Clear();
                cachedYearCounts.Clear();
                cachedOrderedEntries.Clear();
                loadingSelectedYear = false;
                loadingYearCandidateIndex = 0;
                cachedVisibleRevision++;
                cachedOrderedVisibleRevision = -1;
            }

            private void AddDevPreviewEntryIfNeeded(Pawn pawn, DevDiaryPreviewKind devPreviewKind)
            {
                if (devPreviewKind == DevDiaryPreviewKind.None)
                {
                    return;
                }

                cachedPreviewEntry = BuildDevPreviewEntry(pawn, devPreviewKind);
                AddYearCount(EntryYear(cachedPreviewEntry), 1);
            }

            private void AddYearCount(int year, int count)
            {
                if (count <= 0)
                {
                    return;
                }

                if (!cachedYearCounts.ContainsKey(year))
                {
                    cachedYearCounts[year] = 0;
                    cachedVisibleYears.Add(year);
                }

                cachedYearCounts[year] += count;
            }
        }
    }
}

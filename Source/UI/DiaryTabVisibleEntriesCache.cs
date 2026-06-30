// Visible-entry cache for the Diary tab. RimWorld redraws inspector tabs every frame, so this
// helper owns the impure pawn/token-backed lists that can be reused between draw passes.
using System;
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
            private const int MaxCachedPawnStates = 6;

            private sealed class CachedPawnState
            {
                public Pawn cachedIndexPawn;
                public DiaryRenderToken cachedIndexToken;
                public bool cachedIndexShowDebug;
                public bool cachedIndexShowGenerating;
                public bool cachedIndexShowPromptOnly;
                public DevDiaryPreviewKind cachedIndexPreviewKind;
                public DiaryGameComponent.DiaryTabYearIndex cachedIndex;
                public DiaryGameComponent.DiaryTabYearIndexBuild cachedIndexBuild;
                public DiaryEntryView cachedPreviewEntry;
                public readonly List<int> visibleYears = new List<int>();
                public readonly Dictionary<int, int> yearCounts = new Dictionary<int, int>();
                public readonly List<DiaryEntryView> orderedEntries = new List<DiaryEntryView>();
                public readonly List<DiaryEntryView> quietOrderedEntries = new List<DiaryEntryView>();
                public int visibleRevision;
                public int generatingCount;
                public int completedCount;
                public int pendingCount;
                public bool loading;
                public int loadingProcessed;
                public int loadingTotal;
                public bool loadingSelectedYear;
                public int loadingYear;
                public int loadingYearCandidateIndex;
                public string loadingYearPawnId;
                public bool quietSelectedYearRefresh;
                public int orderedVisibleRevision;
                public int orderedYear;
            }

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
            private readonly List<DiaryEntryView> quietOrderedEntries = new List<DiaryEntryView>();
            private readonly Dictionary<string, CachedPawnState> cachedPawnStates = new Dictionary<string, CachedPawnState>();
            private readonly List<string> cachedPawnStateLru = new List<string>();
            private string currentCacheKey;
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
            private bool quietSelectedYearRefresh;
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

            public bool IsIndexLoading
            {
                get { return cachedIndexBuild != null && loading; }
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

                string cacheKey = CacheKeyFor(pawn, showLlmDebugInfo, showGeneratingEntries, showPromptOnlyEntries, devPreviewKind);
                if (!string.Equals(cacheKey, currentCacheKey, StringComparison.Ordinal))
                {
                    StoreCurrentState();
                    currentCacheKey = cacheKey;
                    if (!RestoreCachedState(cacheKey))
                    {
                        ClearCurrentState();
                    }
                }

                token = component.RenderTokenFor(pawn);
                bool sameVisibleCache = cachedIndexPawn != null
                    && string.Equals(cacheKey, currentCacheKey, StringComparison.Ordinal)
                    && cachedIndexShowDebug == showLlmDebugInfo
                    && cachedIndexShowGenerating == showGeneratingEntries
                    && cachedIndexShowPromptOnly == showPromptOnlyEntries
                    && cachedIndexPreviewKind == devPreviewKind;
                if (sameVisibleCache
                    && token.Equals(cachedIndexToken)
                    && cachedIndex != null
                    && cachedIndexBuild == null)
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
                    if (!(sameVisibleCache && cachedIndex != null))
                    {
                        cachedIndex = null;
                        cachedPreviewEntry = null;
                        cachedGeneratingCount = 0;
                        cachedCompletedCount = 0;
                        cachedPendingCount = 0;
                        cachedVisibleYears.Clear();
                        cachedYearCounts.Clear();
                        cachedOrderedEntries.Clear();
                        quietOrderedEntries.Clear();
                        cachedOrderedVisibleRevision = -1;
                        loadingSelectedYear = false;
                        quietSelectedYearRefresh = false;
                        loadingYearCandidateIndex = 0;
                        loading = true;
                    }
                    else
                    {
                        loading = false;
                    }
                }

                cachedIndexBuild.ProcessSlice(DiaryTuning.UiHistoryScanMaxEventsPerFrame, DiaryTuning.UiHistoryScanFrameBudgetSeconds);
                loadingTotal = Math.Max(0, cachedIndexBuild.totalWork);
                loadingProcessed = Math.Min(Math.Max(0, cachedIndexBuild.processedWork), loadingTotal);
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
                bool canRefreshQuietly = cachedOrderedYear == year && cachedOrderedEntries.Count > 0;
                if (!loadingSelectedYear
                    || loadingYear != year
                    || loadingYearPawnId != pawnId
                    || quietSelectedYearRefresh != canRefreshQuietly)
                {
                    quietSelectedYearRefresh = canRefreshQuietly;
                    List<DiaryEntryView> buildTarget = quietSelectedYearRefresh ? quietOrderedEntries : cachedOrderedEntries;
                    buildTarget.Clear();
                    if (!quietSelectedYearRefresh)
                    {
                        cachedOrderedEntries.Clear();
                    }

                    if (cachedPreviewEntry != null && EntryYear(cachedPreviewEntry) == year)
                    {
                        buildTarget.Add(cachedPreviewEntry);
                    }

                    loadingSelectedYear = true;
                    loadingYear = year;
                    loadingYearPawnId = pawnId;
                    loadingYearCandidateIndex = 0;
                }

                List<DiaryEntryView> targetEntries = quietSelectedYearRefresh ? quietOrderedEntries : cachedOrderedEntries;
                loading = !quietSelectedYearRefresh;
                int yearTotal = CountForYear(year);
                loadingTotal = yearTotal;
                loadingProcessed = Math.Min(targetEntries.Count, yearTotal);
                bool complete = cachedIndex == null
                    || cachedIndex.AppendEntriesForYearSlice(
                        targetEntries,
                        pawnId,
                        year,
                        ref loadingYearCandidateIndex,
                        DiaryTuning.UiHistoryScanMaxEventsPerFrame,
                        DiaryTuning.UiHistoryScanFrameBudgetSeconds);
                loadingProcessed = Math.Min(targetEntries.Count, yearTotal);
                if (!complete)
                {
                    return quietSelectedYearRefresh;
                }

                // Newest first by tick; on a tick tie, BoundaryRank keeps the death page (newest) on top
                // and the arrival page (oldest) at the bottom, so a game-start thought sharing the
                // arrival tick can never render above the arrival page (List.Sort is not stable).
                targetEntries.Sort((left, right) =>
                {
                    int byTick = right.Tick.CompareTo(left.Tick);
                    return byTick != 0 ? byTick : right.BoundaryRank.CompareTo(left.BoundaryRank);
                });
                if (quietSelectedYearRefresh)
                {
                    cachedOrderedEntries.Clear();
                    cachedOrderedEntries.AddRange(quietOrderedEntries);
                    quietOrderedEntries.Clear();
                }

                cachedVisibleRevision++;
                cachedOrderedVisibleRevision = cachedVisibleRevision;
                cachedOrderedYear = year;
                loadingSelectedYear = false;
                quietSelectedYearRefresh = false;
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

            private static string CacheKeyFor(
                Pawn pawn,
                bool showLlmDebugInfo,
                bool showGeneratingEntries,
                bool showPromptOnlyEntries,
                DevDiaryPreviewKind devPreviewKind)
            {
                string pawnId = pawn?.GetUniqueLoadID() ?? string.Empty;
                return pawnId
                    + "|debug=" + showLlmDebugInfo
                    + "|generating=" + showGeneratingEntries
                    + "|promptOnly=" + showPromptOnlyEntries
                    + "|preview=" + devPreviewKind;
            }

            private bool HasCurrentState()
            {
                return cachedIndexPawn != null
                    || cachedIndex != null
                    || cachedIndexBuild != null
                    || cachedVisibleYears.Count > 0
                    || cachedYearCounts.Count > 0
                    || cachedOrderedEntries.Count > 0
                    || quietOrderedEntries.Count > 0;
            }

            private void StoreCurrentState()
            {
                if (string.IsNullOrWhiteSpace(currentCacheKey) || !HasCurrentState())
                {
                    return;
                }

                CachedPawnState state = new CachedPawnState
                {
                    cachedIndexPawn = cachedIndexPawn,
                    cachedIndexToken = cachedIndexToken,
                    cachedIndexShowDebug = cachedIndexShowDebug,
                    cachedIndexShowGenerating = cachedIndexShowGenerating,
                    cachedIndexShowPromptOnly = cachedIndexShowPromptOnly,
                    cachedIndexPreviewKind = cachedIndexPreviewKind,
                    cachedIndex = cachedIndex,
                    cachedIndexBuild = cachedIndexBuild,
                    cachedPreviewEntry = cachedPreviewEntry,
                    visibleRevision = cachedVisibleRevision,
                    generatingCount = cachedGeneratingCount,
                    completedCount = cachedCompletedCount,
                    pendingCount = cachedPendingCount,
                    loading = loading,
                    loadingProcessed = loadingProcessed,
                    loadingTotal = loadingTotal,
                    loadingSelectedYear = loadingSelectedYear,
                    loadingYear = loadingYear,
                    loadingYearCandidateIndex = loadingYearCandidateIndex,
                    loadingYearPawnId = loadingYearPawnId,
                    quietSelectedYearRefresh = quietSelectedYearRefresh,
                    orderedVisibleRevision = cachedOrderedVisibleRevision,
                    orderedYear = cachedOrderedYear
                };
                state.visibleYears.AddRange(cachedVisibleYears);
                foreach (KeyValuePair<int, int> pair in cachedYearCounts)
                {
                    state.yearCounts[pair.Key] = pair.Value;
                }

                state.orderedEntries.AddRange(cachedOrderedEntries);
                state.quietOrderedEntries.AddRange(quietOrderedEntries);
                cachedPawnStates[currentCacheKey] = state;
                TouchCachedState(currentCacheKey);
                TrimCachedStates();
            }

            private bool RestoreCachedState(string cacheKey)
            {
                CachedPawnState state;
                if (string.IsNullOrWhiteSpace(cacheKey) || !cachedPawnStates.TryGetValue(cacheKey, out state))
                {
                    return false;
                }

                cachedIndexPawn = state.cachedIndexPawn;
                cachedIndexToken = state.cachedIndexToken;
                cachedIndexShowDebug = state.cachedIndexShowDebug;
                cachedIndexShowGenerating = state.cachedIndexShowGenerating;
                cachedIndexShowPromptOnly = state.cachedIndexShowPromptOnly;
                cachedIndexPreviewKind = state.cachedIndexPreviewKind;
                cachedIndex = state.cachedIndex;
                cachedIndexBuild = state.cachedIndexBuild;
                cachedPreviewEntry = state.cachedPreviewEntry;
                cachedVisibleRevision = state.visibleRevision;
                cachedGeneratingCount = state.generatingCount;
                cachedCompletedCount = state.completedCount;
                cachedPendingCount = state.pendingCount;
                loading = state.loading;
                loadingProcessed = state.loadingProcessed;
                loadingTotal = state.loadingTotal;
                loadingSelectedYear = state.loadingSelectedYear;
                loadingYear = state.loadingYear;
                loadingYearCandidateIndex = state.loadingYearCandidateIndex;
                loadingYearPawnId = state.loadingYearPawnId;
                quietSelectedYearRefresh = state.quietSelectedYearRefresh;
                cachedOrderedVisibleRevision = state.orderedVisibleRevision;
                cachedOrderedYear = state.orderedYear;

                cachedVisibleYears.Clear();
                cachedVisibleYears.AddRange(state.visibleYears);
                cachedYearCounts.Clear();
                foreach (KeyValuePair<int, int> pair in state.yearCounts)
                {
                    cachedYearCounts[pair.Key] = pair.Value;
                }

                cachedOrderedEntries.Clear();
                cachedOrderedEntries.AddRange(state.orderedEntries);
                quietOrderedEntries.Clear();
                quietOrderedEntries.AddRange(state.quietOrderedEntries);
                TouchCachedState(cacheKey);
                return true;
            }

            private void TouchCachedState(string cacheKey)
            {
                if (string.IsNullOrWhiteSpace(cacheKey))
                {
                    return;
                }

                cachedPawnStateLru.Remove(cacheKey);
                cachedPawnStateLru.Add(cacheKey);
            }

            private void TrimCachedStates()
            {
                while (cachedPawnStateLru.Count > MaxCachedPawnStates)
                {
                    string evictedKey = cachedPawnStateLru[0];
                    cachedPawnStateLru.RemoveAt(0);
                    cachedPawnStates.Remove(evictedKey);
                }
            }

            private void ClearCurrentState()
            {
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
                quietOrderedEntries.Clear();
                loadingSelectedYear = false;
                quietSelectedYearRefresh = false;
                loadingYearCandidateIndex = 0;
                cachedVisibleRevision++;
                cachedOrderedVisibleRevision = -1;
            }

            private void ClearIfNeeded()
            {
                if (cachedIndex == null
                    && cachedIndexBuild == null
                    && cachedVisibleYears.Count == 0
                    && cachedYearCounts.Count == 0
                    && cachedPreviewEntry == null
                    && cachedOrderedEntries.Count == 0
                    && cachedPawnStates.Count == 0)
                {
                    return;
                }

                ClearCurrentState();
                currentCacheKey = null;
                cachedPawnStates.Clear();
                cachedPawnStateLru.Clear();
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

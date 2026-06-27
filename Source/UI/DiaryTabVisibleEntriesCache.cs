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
        /// Caches raw diary views, the currently visible subset, year pages, and per-year ordering.
        /// </summary>
        private sealed class DiaryTabVisibleEntriesCache
        {
            // Sentinel for no entries; avoids allocating a new empty list on every frame when the component is null.
            private static readonly IReadOnlyList<DiaryEntryView> EmptyList = new List<DiaryEntryView>();

            // Per-frame cache for the heaviest part of drawing the tab: rebuilding a DiaryEntryView for
            // every one of the pawn's events (each runs group classification + gameContext parsing). The
            // built list is reused until the pawn's render token changes: a new event, or an entry's
            // status/text/title changing. A tab that is merely being read does no rebuild work.
            private Pawn cachedEntriesPawn;
            private DiaryRenderToken cachedEntriesToken;
            private IReadOnlyList<DiaryEntryView> cachedEntries = EmptyList;

            // The visible subset, year list, and per-year ordering are derived from cachedEntries.
            // FillTab runs every draw frame, so these buffers avoid per-frame LINQ/List churn while the
            // selected pawn, render token, dev toggles, and transient preview stay unchanged.
            private IReadOnlyList<DiaryEntryView> cachedVisibleSource = EmptyList;
            private Pawn cachedVisiblePawn;
            private DiaryRenderToken cachedVisibleToken;
            private bool cachedVisibleShowDebug;
            private bool cachedVisibleShowGenerating;
            private bool cachedVisibleShowPromptOnly;
            private DevDiaryPreviewKind cachedVisiblePreviewKind = DevDiaryPreviewKind.None;
            private int cachedVisibleRevision;
            private int cachedGeneratingCount;
            private readonly List<DiaryEntryView> cachedVisibleEntries = new List<DiaryEntryView>();
            private readonly List<int> cachedVisibleYears = new List<int>();
            private readonly List<DiaryEntryView> cachedOrderedEntries = new List<DiaryEntryView>();
            private int cachedOrderedVisibleRevision = -1;
            private int cachedOrderedYear = int.MinValue;

            /// <summary>
            /// Number of hidden pending entries that are currently being written.
            /// </summary>
            public int GeneratingCount
            {
                get { return cachedGeneratingCount; }
            }

            /// <summary>
            /// Current visible entries, including any transient dev preview entry.
            /// </summary>
            public List<DiaryEntryView> VisibleEntries
            {
                get { return cachedVisibleEntries; }
            }

            /// <summary>
            /// Distinct years represented by <see cref="VisibleEntries" />, newest first.
            /// </summary>
            public List<int> VisibleYears
            {
                get { return cachedVisibleYears; }
            }

            /// <summary>
            /// Monotonic version for the currently visible entry set. The scroll-layout cache uses it
            /// to know when the ordered list contents changed even though the same List instance is
            /// reused to avoid allocations.
            /// </summary>
            public int VisibleRevision
            {
                get { return cachedVisibleRevision; }
            }

            /// <summary>
            /// Returns the pawn's raw entry views, rebuilding only when the component render token changes.
            /// </summary>
            public IReadOnlyList<DiaryEntryView> EntriesFor(Pawn pawn, DiaryGameComponent component, out DiaryRenderToken token)
            {
                token = default(DiaryRenderToken);
                if (component == null)
                {
                    return EmptyList;
                }

                token = component.RenderTokenFor(pawn);
                if (pawn != cachedEntriesPawn || !token.Equals(cachedEntriesToken) || cachedEntries == null)
                {
                    cachedEntries = component.EntriesFor(pawn);
                    cachedEntriesPawn = pawn;
                    cachedEntriesToken = token;
                }

                return cachedEntries;
            }

            /// <summary>
            /// Refreshes the filtered entries and year list only when the backing render token or view
            /// toggles change. This is the allocation-sensitive part of the immediate-mode tab draw.
            /// </summary>
            public void RebuildVisibleEntriesIfNeeded(
                IReadOnlyList<DiaryEntryView> entries,
                Pawn pawn,
                DiaryRenderToken token,
                bool showLlmDebugInfo,
                bool showGeneratingEntries,
                bool showPromptOnlyEntries,
                DevDiaryPreviewKind devPreviewKind)
            {
                if (cachedVisibleSource == entries
                    && cachedVisiblePawn == pawn
                    && token.Equals(cachedVisibleToken)
                    && cachedVisibleShowDebug == showLlmDebugInfo
                    && cachedVisibleShowGenerating == showGeneratingEntries
                    && cachedVisibleShowPromptOnly == showPromptOnlyEntries
                    && cachedVisiblePreviewKind == devPreviewKind)
                {
                    return;
                }

                cachedVisibleSource = entries;
                cachedVisiblePawn = pawn;
                cachedVisibleToken = token;
                cachedVisibleShowDebug = showLlmDebugInfo;
                cachedVisibleShowGenerating = showGeneratingEntries;
                cachedVisibleShowPromptOnly = showPromptOnlyEntries;
                cachedVisiblePreviewKind = devPreviewKind;
                cachedGeneratingCount = 0;
                cachedVisibleEntries.Clear();
                cachedVisibleYears.Clear();

                AddDevPreviewEntryIfNeeded(pawn, devPreviewKind);

                if (entries != null)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        DiaryEntryView entry = entries[i];
                        bool generating = IsGenerating(entry);
                        if (generating)
                        {
                            cachedGeneratingCount++;
                        }

                        if (entry != null
                            && (showLlmDebugInfo
                                || IsGenerated(entry)
                                || (showGeneratingEntries && generating)
                                || (showPromptOnlyEntries && IsPromptOnly(entry))))
                        {
                            cachedVisibleEntries.Add(entry);
                            AddYearIfMissing(cachedVisibleYears, EntryYear(entry));
                        }
                    }
                }

                cachedVisibleYears.Sort((left, right) => right.CompareTo(left));
                cachedVisibleRevision++;
            }

            /// <summary>
            /// Reuses the selected-year ordering until the visible-entry cache or selected year changes.
            /// </summary>
            public List<DiaryEntryView> OrderedEntriesForSelectedYear(int year)
            {
                if (cachedOrderedVisibleRevision == cachedVisibleRevision && cachedOrderedYear == year)
                {
                    return cachedOrderedEntries;
                }

                cachedOrderedEntries.Clear();
                for (int i = 0; i < cachedVisibleEntries.Count; i++)
                {
                    DiaryEntryView entry = cachedVisibleEntries[i];
                    if (EntryYear(entry) == year)
                    {
                        cachedOrderedEntries.Add(entry);
                    }
                }

                cachedOrderedEntries.Sort((left, right) => right.Tick.CompareTo(left.Tick));
                cachedOrderedVisibleRevision = cachedVisibleRevision;
                cachedOrderedYear = year;
                return cachedOrderedEntries;
            }

            /// <summary>
            /// Forces the next selected-year ordering request to rebuild from the visible list.
            /// </summary>
            public void InvalidateOrdering()
            {
                cachedOrderedVisibleRevision = -1;
            }

            /// <summary>
            /// Inserts the transient preview into the visible entry cache before real saved entries.
            /// </summary>
            private void AddDevPreviewEntryIfNeeded(Pawn pawn, DevDiaryPreviewKind devPreviewKind)
            {
                if (devPreviewKind == DevDiaryPreviewKind.None)
                {
                    return;
                }

                DiaryEntryView preview = BuildDevPreviewEntry(pawn, devPreviewKind);
                cachedVisibleEntries.Add(preview);
                AddYearIfMissing(cachedVisibleYears, EntryYear(preview));
            }

            /// <summary>
            /// Appends a year once. The list is tiny in practice, so a linear scan avoids a per-frame set.
            /// </summary>
            private static void AddYearIfMissing(List<int> years, int year)
            {
                if (!years.Contains(year))
                {
                    years.Add(year);
                }
            }
        }
    }
}

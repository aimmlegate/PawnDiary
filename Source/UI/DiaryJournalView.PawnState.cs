// Per-pawn, session-only reader view state.
// The journal renderer is shared while the player moves between pawns, so this file snapshots the
// scroll/year/filter controls before a switch and restores them when that pawn is selected again.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PawnDiary
{
    /// <summary>
    /// Per-pawn view-state helpers for the reusable diary journal renderer.
    /// </summary>
    internal sealed partial class DiaryJournalView
    {
        // A colony normally has far fewer diary subjects. The cap only prevents an unusually long
        // archive-browsing session from retaining unbounded UI-only state.
        private const int MaxPawnUiStates = 64;

        private sealed class PawnUiState
        {
            public Vector2 journalScroll;
            public int selectedYear;
            public Vector2 filterPanelScroll;
            public float filterPanelContentHeight;
            public bool favoritesOnly;
            public readonly HashSet<string> activeTags = new HashSet<string>(StringComparer.Ordinal);
            public string searchQuery = string.Empty;
        }

        private readonly Dictionary<string, PawnUiState> pawnUiStates =
            new Dictionary<string, PawnUiState>(StringComparer.Ordinal);
        private readonly List<string> pawnUiStateLru = new List<string>();
        private string activePawnUiStateId;

        /// <summary>
        /// Saves the outgoing pawn's controls and restores the incoming pawn's last controls.
        /// </summary>
        private void ActivatePawnUiState(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId)
                || string.Equals(activePawnUiStateId, pawnId, StringComparison.Ordinal))
            {
                return;
            }

            StoreActivePawnUiState();
            activePawnUiStateId = pawnId;

            PawnUiState state;
            if (pawnUiStates.TryGetValue(pawnId, out state))
            {
                selectedYear = state.selectedYear;
                yearFilterPawnId = pawnId;
                scrollPosition = state.journalScroll;
                filterPanelScrollPosition = state.filterPanelScroll;
                filterPanelContentHeight = state.filterPanelContentHeight;
                filterFavoritesOnly = state.favoritesOnly;
                filterActiveTags.Clear();
                filterActiveTags.UnionWith(state.activeTags);
                filterSearchQuery = state.searchQuery ?? string.Empty;
            }
            else
            {
                // A first visit resolves to the newest available year in EnsureSelectedYear.
                selectedYear = UnknownYear;
                yearFilterPawnId = null;
                scrollPosition = Vector2.zero;
                filterPanelScrollPosition = Vector2.zero;
                filterPanelContentHeight = 0f;
                filterFavoritesOnly = false;
                filterActiveTags.Clear();
                filterSearchQuery = string.Empty;
            }

            InvalidatePawnScopedFilterCaches();
            TouchPawnUiState(pawnId);
            TrimPawnUiStates();
        }

        private void StoreActivePawnUiState()
        {
            if (string.IsNullOrWhiteSpace(activePawnUiStateId))
            {
                return;
            }

            PawnUiState state = new PawnUiState
            {
                journalScroll = scrollPosition,
                selectedYear = selectedYear,
                filterPanelScroll = filterPanelScrollPosition,
                filterPanelContentHeight = filterPanelContentHeight,
                favoritesOnly = filterFavoritesOnly,
                searchQuery = filterSearchQuery ?? string.Empty
            };
            state.activeTags.UnionWith(filterActiveTags);
            pawnUiStates[activePawnUiStateId] = state;
            TouchPawnUiState(activePawnUiStateId);
        }

        private void InvalidatePawnScopedFilterCaches()
        {
            filterTagInfoBuffer.Clear();
            filterTagInfoSource = null;
            filterTagInfoSourceRevision = -1;
            filterTagInfoYear = int.MinValue;
            journalFilterBuffer.Clear();
            journalFilterSource = null;
            journalFilterSourceRevision = -1;
            journalFilterTags.Clear();
            journalFilterSearchQuery = string.Empty;
            journalFilterVersion++;
        }

        private void TouchPawnUiState(string pawnId)
        {
            pawnUiStateLru.Remove(pawnId);
            pawnUiStateLru.Add(pawnId);
        }

        private void TrimPawnUiStates()
        {
            while (pawnUiStateLru.Count > MaxPawnUiStates)
            {
                string evicted = pawnUiStateLru[0];
                pawnUiStateLru.RemoveAt(0);
                if (string.Equals(evicted, activePawnUiStateId, StringComparison.Ordinal))
                {
                    pawnUiStateLru.Add(evicted);
                    continue;
                }

                pawnUiStates.Remove(evicted);
            }
        }

        /// <summary>Clears references and controls owned by the previous loaded game.</summary>
        private void ClearPawnUiStatesForSession()
        {
            pawnUiStates.Clear();
            pawnUiStateLru.Clear();
            activePawnUiStateId = null;
        }
    }
}

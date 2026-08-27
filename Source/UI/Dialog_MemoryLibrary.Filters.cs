// Dialog_MemoryLibrary.Filters.cs — view, search, filter, and sort controls for the M9 Library.
//
// These controls update detached query state and request a later refresh. They never modify
// persistent memories during RimWorld's immediate-mode drawing pass.
using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    internal sealed partial class Dialog_MemoryLibrary
    {
        // The Library panel is session-local, just like the dialog's other navigation state. Its
        // scroll never leaks into the subject rail, list, or timeline scroll positions.
        private const int MemoryLibraryImportanceFilterMask =
            MemoryLibraryPolicy.ImportanceMinor
            | MemoryLibraryPolicy.ImportanceRegular
            | MemoryLibraryPolicy.ImportanceImportant;
        private const int MemoryLibraryFilterPanelErrorKey = 0x0D1A0F12;
        private bool memoryLibraryFilterPanelOpen;
        private Vector2 memoryLibraryFilterPanelScroll;
        private float memoryLibraryFilterPanelContentHeight;

        /// <summary>
        /// Draws the subject-first shell's single memory search field and Filters toggle. Imported
        /// evidence keeps search and sort, but hides filters that its read-only query cannot apply.
        /// </summary>
        private float DrawMemoryLibrarySearchRow(Rect rect)
        {
            EnsureMemoryLibraryFiltersMatchView();
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            float gap = Mathf.Max(2f, style.memoryLibraryFilterPanelGap);
            bool filtersAvailable = session.selectedView != MemoryLibraryViews.Imported
                && session.selectedView != MemoryLibraryUiViews.Culture;
            int activeCount = MemoryLibraryUiPolicy.ActiveFilterCount(session.filters);
            string filterLabel = activeCount > 0
                ? T("PawnDiary.Memory.Library.Filters.ButtonActive", activeCount)
                : T("PawnDiary.Memory.Library.Filters.Button");
            float filterWidth = 0f;
            if (filtersAvailable)
            {
                float desired = Text.CalcSize(filterLabel).x + rect.height;
                filterWidth = Mathf.Min(desired, Mathf.Max(0f, rect.width - gap - 80f));
            }

            Rect searchRect = new Rect(rect.x, rect.y,
                Mathf.Max(0f, rect.width - (filterWidth > 0f ? filterWidth + gap : 0f)),
                rect.height);
            string before = session.memorySearch ?? string.Empty;
            string after = MemoryLibraryPolicy.ClampScalars(
                MemoryLibraryPolicy.RepairMalformedUtf16(
                    Widgets.TextField(searchRect, before) ?? string.Empty),
                detachedSearchScalarCap, detachedSearchUtf16Cap);
            if (string.IsNullOrEmpty(after))
            {
                Color prior = GUI.color;
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(searchRect.x + 7f, searchRect.y,
                    Mathf.Max(0f, searchRect.width - 14f), searchRect.height),
                    T("PawnDiary.Memory.Library.Search"));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = prior;
            }
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                session.memorySearch = after;
                ResetContentListQuery();
            }
            TooltipHandler.TipRegion(searchRect, T("PawnDiary.Memory.Library.SearchTip"));

            if (filterWidth > 0f)
            {
                Rect filterRect = new Rect(searchRect.xMax + gap, rect.y, filterWidth, rect.height);
                if (Widgets.ButtonText(filterRect, filterLabel))
                    memoryLibraryFilterPanelOpen = !memoryLibraryFilterPanelOpen;
            }
            return rect.yMax;
        }

        /// <summary>
        /// Returns the width the main shell must reserve at the right edge. Closed, Culture, and
        /// Imported views deliberately return zero so their content pane uses the remaining width.
        /// </summary>
        private float MemoryLibraryFilterPanelWidth(float availableWidth)
        {
            if (!memoryLibraryFilterPanelOpen
                || session.selectedView == MemoryLibraryViews.Imported
                || session.selectedView == MemoryLibraryUiViews.Culture)
                return 0f;
            return Mathf.Min(Mathf.Max(0f,
                DiaryJournalView.UiStyle.memoryLibraryFilterPanelWidth),
                Mathf.Max(0f, availableWidth));
        }

        /// <summary>
        /// Draws the independently scrolling, truly reserved filter column. The main shell owns the
        /// column's rectangle; this helper owns only its background, controls, and scroll position.
        /// </summary>
        private void DrawMemoryLibraryFilterPanel(Rect panelRect)
        {
            EnsureMemoryLibraryFiltersMatchView();
            if (!memoryLibraryFilterPanelOpen
                || session.selectedView == MemoryLibraryViews.Imported
                || session.selectedView == MemoryLibraryUiViews.Culture
                || panelRect.width <= 1f || panelRect.height <= 1f)
                return;

            Widgets.DrawMenuSection(panelRect);
            float padding = Mathf.Max(2f,
                DiaryJournalView.UiStyle.memoryLibraryFilterPanelGap);
            Rect inner = panelRect.ContractedBy(padding);
            if (inner.width <= 1f || inner.height <= 1f) return;

            Rect view = new Rect(0f, 0f, Mathf.Max(0f, inner.width - 16f),
                Mathf.Max(inner.height, memoryLibraryFilterPanelContentHeight));
            Widgets.BeginScrollView(inner, ref memoryLibraryFilterPanelScroll, view);
            Listing_Standard listing = new Listing_Standard();
            bool listingBegun = false;
            try
            {
                listing.Begin(view);
                listingBegun = true;
                DrawMemoryLibraryFilterSectionHeader(
                    listing, "PawnDiary.Memory.Library.Filters.WhatHappened");
                DrawMemoryLibraryCategoryFilter(listing,
                    "PawnDiary.Memory.Library.CategoryPersonal", MemoryCategoryBits.Personal);
                DrawMemoryLibraryCategoryFilter(listing,
                    "PawnDiary.Memory.Library.CategoryRelationships", MemoryCategoryBits.Relationships);
                DrawMemoryLibraryCategoryFilter(listing,
                    "PawnDiary.Memory.Library.CategoryFamily", MemoryCategoryBits.Family);
                DrawMemoryLibraryCategoryFilter(listing,
                    "PawnDiary.Memory.Library.CategoryFactions", MemoryCategoryBits.Factions);

                listing.Gap(padding);
                DrawMemoryLibraryFilterSectionHeader(
                    listing, "PawnDiary.Memory.Library.Filters.Importance");
                DrawMemoryLibraryImportanceFilter(listing,
                    "PawnDiary.Memory.Library.Importance.Minor",
                    MemoryLibraryPolicy.ImportanceMinor);
                DrawMemoryLibraryImportanceFilter(listing,
                    "PawnDiary.Memory.Library.Importance.Regular",
                    MemoryLibraryPolicy.ImportanceRegular);
                DrawMemoryLibraryImportanceFilter(listing,
                    "PawnDiary.Memory.Library.Importance.Important",
                    MemoryLibraryPolicy.ImportanceImportant);

                listing.Gap(padding);
                DrawMemoryLibraryFilterSectionHeader(
                    listing, "PawnDiary.Memory.Library.Filters.Show");
                DrawMemoryLibraryStateFilter(listing,
                    "PawnDiary.Memory.Library.Filters.Everything", "all");
                DrawMemoryLibraryStateFilter(listing,
                    "PawnDiary.Memory.Library.Filters.Edited", "edited");
                DrawMemoryLibraryStateFilter(listing,
                    "PawnDiary.Memory.Library.Filters.Hidden", "suppressed");
                DrawMemoryLibraryStateFilter(listing,
                    "PawnDiary.Memory.Library.Filters.Available", "unsuppressed");

                listing.Gap(padding);
                bool filtersActive = MemoryLibraryUiPolicy.ActiveFilterCount(session.filters) > 0;
                bool oldEnabled = GUI.enabled;
                GUI.enabled = oldEnabled && filtersActive;
                if (listing.ButtonText(T("PawnDiary.Memory.Library.Filters.Clear")))
                    ClearMemoryLibraryFilters();
                GUI.enabled = oldEnabled;

                int showing;
                int total;
                if (TryMemoryLibraryFilterResultCount(out showing, out total))
                {
                    Color oldColor = GUI.color;
                    GUI.color = DiaryJournalView.UiStyle.EntryDateColor;
                    listing.Label(T("PawnDiary.Memory.Library.Filters.Showing", showing, total));
                    GUI.color = oldColor;
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[PawnDiary] Memory Library filter panel draw failed: " + e,
                    MemoryLibraryFilterPanelErrorKey);
            }
            finally
            {
                if (listingBegun)
                {
                    memoryLibraryFilterPanelContentHeight = listing.CurHeight;
                    listing.End();
                }
                Widgets.EndScrollView();
            }
        }

        private void DrawMemoryLibraryCategoryFilter(
            Listing_Standard listing, string labelKey, int categoryBit)
        {
            DrawMemoryLibraryMaskFilter(listing, labelKey, categoryBit,
                MemoryCategoryBits.KnownMask, true);
        }

        private void DrawMemoryLibraryImportanceFilter(
            Listing_Standard listing, string labelKey, int importanceBit)
        {
            DrawMemoryLibraryMaskFilter(listing, labelKey, importanceBit,
                MemoryLibraryImportanceFilterMask, false);
        }

        /// <summary>
        /// Draws one multi-select checkbox. A zero mask is the backend's exact "all" sentinel, so all
        /// known boxes read checked and the pure policy expands that sentinel before toggling one bit.
        /// </summary>
        private void DrawMemoryLibraryMaskFilter(
            Listing_Standard listing,
            string labelKey,
            int toggledBit,
            int knownMask,
            bool category)
        {
            int current = category
                ? session.filters.categoryMask : session.filters.importanceMask;
            bool selected = current == 0 || (current & toggledBit) != 0;
            bool before = selected;
            listing.CheckboxLabeled(T(labelKey), ref selected);
            if (selected == before) return;

            int changed = MemoryLibraryUiPolicy.ToggleFilterBit(
                current, toggledBit, knownMask);
            if (changed == current) return;
            if (category) session.filters.categoryMask = changed;
            else session.filters.importanceMask = changed;
            ResetContentListQuery();
        }

        private void DrawMemoryLibraryStateFilter(
            Listing_Standard listing, string labelKey, string stateToken)
        {
            string current = session.filters.stateToken ?? "all";
            bool selected = string.Equals(current, stateToken, StringComparison.Ordinal);
            if (listing.RadioButton(T(labelKey), selected) && !selected)
            {
                session.filters.stateToken = stateToken;
                ResetContentListQuery();
            }
        }

        private static void DrawMemoryLibraryFilterSectionHeader(
            Listing_Standard listing, string labelKey)
        {
            Color oldColor = GUI.color;
            GUI.color = DiaryJournalView.UiStyle.EntryDateColor;
            listing.Label(T(labelKey));
            GUI.color = oldColor;
        }

        private void ClearMemoryLibraryFilters()
        {
            if (MemoryLibraryUiPolicy.ActiveFilterCount(session.filters) == 0) return;
            MemoryLibraryUiPolicy.ClearFilters(session.filters);
            ResetContentListQuery();
        }

        /// <summary>
        /// Imported evidence has no category, importance, or suppression contract. Clear stale masks
        /// defensively if a view transition bypassed MemoryLibraryUiSession.SelectView.
        /// </summary>
        private void EnsureMemoryLibraryFiltersMatchView()
        {
            if (session.selectedView == MemoryLibraryUiViews.Culture)
            {
                memoryLibraryFilterPanelOpen = false;
                return;
            }
            if (session.selectedView != MemoryLibraryViews.Imported) return;
            memoryLibraryFilterPanelOpen = false;
            if (MemoryLibraryUiPolicy.ActiveFilterCount(session.filters) == 0) return;
            MemoryLibraryUiPolicy.ClearImportedIncompatibleFilters(session.filters);
            ResetContentListQuery();
        }

        private bool TryMemoryLibraryFilterResultCount(out int showing, out int total)
        {
            showing = 0;
            total = 0;
            if (session.selectedView == MemoryLibraryViews.Threads
                && threadDetail != null
                && threadDetail.status == MemoryLibraryStatuses.Ready)
            {
                showing = Math.Max(0, threadDetail.shownManageableCount);
                total = Math.Max(showing, threadDetail.totalManageableCount);
                return true;
            }
            if (session.selectedView == MemoryLibraryViews.Standalone
                && list != null
                && list.status == MemoryLibraryStatuses.Ready)
            {
                showing = Math.Max(0, list.totalMatchedRows);
                total = Math.Max(showing, list.totalEligibleRows);
                return true;
            }
            return false;
        }

        private float DrawImportedSort(Rect rect)
        {
            if (Widgets.ButtonText(rect, T("PawnDiary.Memory.Library.Sort",
                    session.sortToken == "oldest"
                        ? T("PawnDiary.Memory.Library.Oldest")
                        : T("PawnDiary.Memory.Library.Newest"))))
            {
                session.sortToken = session.sortToken == "oldest" ? "newest" : "oldest";
                ResetContentListQuery();
            }
            return rect.yMax;
        }

    }
}

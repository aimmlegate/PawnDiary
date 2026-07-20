// Right-hand filter/controls panel for the Diary tab. Split from ITab_Pawn_Diary.cs.
//
// This is an independent, NON-virtualized scroll column drawn beside the journal. It owns the year
// selector, a set of stub filter controls (favorites + per-tag toggles that are not wired to the
// journal yet), and — in dev mode — the diary dev tools that used to sit above the journal. It is
// built entirely from existing RimWorld widgets (Listing_Standard, Widgets.CheckboxLabeled/ButtonText,
// the year FloatMenu pager, DrawMenuSection) rather than any bespoke control.
//
// New to C#/RimWorld? (JS/TS analogy) A `Listing_Standard` is a vertical layout helper like a flexbox
// column: you `Begin(rect)`, append rows (labels, checkboxes, buttons), and `End()`. `BeginScrollView`
// clips the column to the panel and adds a scrollbar — its own scroll offset, separate from the
// journal's.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Partial implementation of the pawn Diary inspector tab.
    /// </summary>
    public partial class ITab_Pawn_Diary
    {
        // Scroll offset for the filter panel, kept independent from the journal's scrollPosition so the
        // two columns scroll on their own.
        private Vector2 filterPanelScrollPosition;
        // Last measured content height, used to size the scroll view's virtual rect next frame. The
        // panel is short and static, so this settles in one frame.
        private float filterPanelContentHeight;

        // ---- Stub filter state ----
        // These are placeholders: the controls render and toggle, but nothing filters the journal yet.
        // The pawn id lets the panel reset its state when the tab (a shared singleton) switches pawns,
        // so one pawn's toggles/scroll don't bleed onto the next.
        private string filterPanelPawnId;
        private bool filterFavoritesOnly;
        private readonly HashSet<string> filterActiveTags = new HashSet<string>();
        // Reusable buffer for the per-year distinct tag labels, rebuilt in place each draw.
        private readonly List<string> filterTagLabelsBuffer = new List<string>();

        private static float FilterPanelWidth => UiStyle.filterPanelWidth;
        private static float FilterPanelGap => UiStyle.filterPanelGap;
        // The journal column never shrinks below this; if the tab is too narrow to fit both, the panel
        // hides so the journal stays usable.
        private const float FilterPanelMinJournalWidth = 360f;
        // Below this the panel would be a useless sliver, so it hides entirely (and FillTab falls back
        // to the in-column year pager + dev tools) instead of drawing a cramped strip.
        private const float FilterPanelMinUsableWidth = 150f;
        private const float FilterPanelPadding = 8f;
        private const float FilterPanelButtonGap = 6f;
        private const int FilterMaxTagRows = 24;
        private const int FilterPanelErrorKey = 0x0D1A0F11;

        /// <summary>
        /// Resolves the panel width for the current tab width, clamping so the journal keeps at least
        /// <see cref="FilterPanelMinJournalWidth"/>. Returns 0 (panel hidden) when the tab is too narrow.
        /// </summary>
        private static float ResolveFilterPanelWidth(float totalWidth)
        {
            float requested = Mathf.Max(0f, FilterPanelWidth);
            if (requested <= 0f)
            {
                return 0f;
            }

            float maxPanel = totalWidth - FilterPanelMinJournalWidth - Mathf.Max(0f, FilterPanelGap);
            if (maxPanel < FilterPanelMinUsableWidth)
            {
                return 0f;
            }

            return Mathf.Min(requested, maxPanel);
        }

        /// <summary>
        /// Draws the filter/controls panel and its independent scroll view. Safe to call in every tab
        /// state: <paramref name="years"/> may be null while the index is still building, and
        /// <paramref name="orderedForTags"/> may be null while the selected year's cards are loading.
        /// </summary>
        private void DrawFilterPanel(
            Rect panelRect,
            Pawn pawn,
            DiaryGameComponent component,
            List<int> years,
            DiaryTabVisibleEntriesCache entriesCache,
            List<DiaryEntryView> orderedForTags)
        {
            if (panelRect.width <= 1f || panelRect.height <= 1f)
            {
                return;
            }

            ResetFilterStateOnPawnChange(pawn);
            Widgets.DrawMenuSection(panelRect);
            Rect inner = panelRect.ContractedBy(FilterPanelPadding);
            if (inner.width <= 0f || inner.height <= 0f)
            {
                return;
            }

            // The scroll view's virtual height is at least the panel height (so short content does not
            // scroll) and grows to the measured content height.
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, inner.width - 16f), Mathf.Max(inner.height, filterPanelContentHeight));

            Widgets.BeginScrollView(inner, ref filterPanelScrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            bool listingBegun = false;
            try
            {
                listing.Begin(viewRect);
                listingBegun = true;
                DrawFilterYearSection(listing, years, entriesCache);
                DrawFilterStubSection(listing, orderedForTags);
                if (Prefs.DevMode)
                {
                    listing.GapLine();
                    DrawFilterDevSection(listing, pawn, component);
                }
            }
            catch (Exception e)
            {
                // Never let a panel draw break the whole GUI frame — log once and keep the clip stack
                // balanced via the finally below.
                Log.ErrorOnce("[PawnDiary] Diary filter panel draw failed: " + e, FilterPanelErrorKey);
            }
            finally
            {
                // Only close the Listing group if Begin actually ran, then always close the scroll view
                // so the GUI clip stack stays balanced no matter where a throw happened.
                if (listingBegun)
                {
                    filterPanelContentHeight = listing.CurHeight;
                    listing.End();
                }

                Widgets.EndScrollView();
            }
        }

        /// <summary>
        /// Year selector section: reuses the existing FloatMenu pager when several years exist, or a
        /// plain label for a single year. Drawn only once the year index is ready.
        /// </summary>
        private void DrawFilterYearSection(Listing_Standard listing, List<int> years, DiaryTabVisibleEntriesCache entriesCache)
        {
            if (years == null || years.Count == 0)
            {
                return;
            }

            DrawFilterSectionHeader(listing, "PawnDiary.Tab.FilterYearHeader".Translate());
            if (years.Count > 1)
            {
                Rect yearRect = listing.GetRect(YearFilterHeight);
                DrawYearFilter(yearRect, years, entriesCache);
            }
            else
            {
                listing.Label(YearLabel(selectedYear));
            }
        }

        /// <summary>
        /// Stub filter controls: a favorites toggle, per-tag toggles for the tags present in the
        /// current year, and Clear/Apply buttons. None of these filter the journal yet.
        /// </summary>
        private void DrawFilterStubSection(Listing_Standard listing, List<DiaryEntryView> orderedForTags)
        {
            listing.Gap(6f);
            DrawFilterSectionHeader(listing, "PawnDiary.Tab.FilterHeader".Translate());
            bool favorites = filterFavoritesOnly;
            listing.CheckboxLabeled("PawnDiary.Tab.FilterFavoritesOnly".Translate(), ref favorites);
            filterFavoritesOnly = favorites;

            listing.Gap(6f);
            DrawFilterSectionHeader(listing, "PawnDiary.Tab.FilterTagsHeader".Translate());
            CollectTagLabels(orderedForTags, filterTagLabelsBuffer);
            if (filterTagLabelsBuffer.Count == 0)
            {
                Color oldColor = GUI.color;
                GUI.color = UiStyle.ModelNameColor;
                listing.Label("PawnDiary.Tab.FilterNoTags".Translate());
                GUI.color = oldColor;
            }
            else
            {
                for (int i = 0; i < filterTagLabelsBuffer.Count; i++)
                {
                    string tag = filterTagLabelsBuffer[i];
                    bool active = filterActiveTags.Contains(tag);
                    bool before = active;
                    listing.CheckboxLabeled(tag, ref active);
                    if (active != before)
                    {
                        if (active)
                        {
                            filterActiveTags.Add(tag);
                        }
                        else
                        {
                            filterActiveTags.Remove(tag);
                        }
                    }
                }
            }

            listing.Gap(8f);
            Rect buttonRow = listing.GetRect(ControlLineHeight);
            float half = Mathf.Max(0f, (buttonRow.width - FilterPanelButtonGap) * 0.5f);
            Rect clearRect = new Rect(buttonRow.x, buttonRow.y, half, buttonRow.height);
            Rect applyRect = new Rect(buttonRow.xMax - half, buttonRow.y, half, buttonRow.height);
            if (Widgets.ButtonText(clearRect, "PawnDiary.Tab.FilterClear".Translate()))
            {
                filterFavoritesOnly = false;
                filterActiveTags.Clear();
            }

            // Count only tags that are actually shown for the current year, so the Apply badge matches
            // the visible checked boxes rather than tags left active from a different year.
            int visibleActiveTags = 0;
            for (int i = 0; i < filterTagLabelsBuffer.Count; i++)
            {
                if (filterActiveTags.Contains(filterTagLabelsBuffer[i]))
                {
                    visibleActiveTags++;
                }
            }

            int activeCount = visibleActiveTags + (filterFavoritesOnly ? 1 : 0);
            // Stub: the Apply button is present but filtering is not wired to the journal yet.
            Widgets.ButtonText(applyRect, "PawnDiary.Tab.FilterApply".Translate(activeCount));
            TooltipHandler.TipRegion(applyRect, "PawnDiary.Tab.FilterStubTip".Translate());
        }

        /// <summary>
        /// Dev-mode section: the diary dev tools that used to sit above the journal, now hosted in the
        /// panel. DrawPawnControls owns its own Listing on the reserved rect.
        /// </summary>
        private void DrawFilterDevSection(Listing_Standard listing, Pawn pawn, DiaryGameComponent component)
        {
            DrawFilterSectionHeader(listing, "PawnDiary.Tab.FilterDevHeader".Translate());
            float devHeight = PawnControlsHeight();
            if (devHeight <= 0f)
            {
                return;
            }

            Rect devRect = listing.GetRect(devHeight);
            DrawPawnControls(pawn, component, devRect);
        }

        /// <summary>
        /// Draws a quiet, muted section header label using only the existing Listing label widget.
        /// </summary>
        private static void DrawFilterSectionHeader(Listing_Standard listing, string label)
        {
            Color oldColor = GUI.color;
            GUI.color = UiStyle.EntryDateColor;
            listing.Label(label);
            GUI.color = oldColor;
        }

        /// <summary>
        /// Clears the stub filter toggles and panel scroll when the shown pawn changes, so state does
        /// not carry across pawns on this shared tab instance.
        /// </summary>
        private void ResetFilterStateOnPawnChange(Pawn pawn)
        {
            string pawnId = pawn?.GetUniqueLoadID();
            if (string.Equals(pawnId, filterPanelPawnId, StringComparison.Ordinal))
            {
                return;
            }

            filterPanelPawnId = pawnId;
            filterFavoritesOnly = false;
            filterActiveTags.Clear();
            filterPanelScrollPosition = Vector2.zero;
        }

        /// <summary>
        /// Rebuilds <paramref name="target"/> with the distinct, non-empty entry group labels present in
        /// the current year's ordered cards (stable sorted, capped). This is the stub tag vocabulary.
        /// </summary>
        private static void CollectTagLabels(List<DiaryEntryView> ordered, List<string> target)
        {
            target.Clear();
            if (ordered == null)
            {
                return;
            }

            for (int i = 0; i < ordered.Count && target.Count < FilterMaxTagRows; i++)
            {
                string label = ordered[i]?.GroupLabel;
                if (!string.IsNullOrWhiteSpace(label) && !target.Contains(label))
                {
                    target.Add(label);
                }
            }

            target.Sort(StringComparer.OrdinalIgnoreCase);
        }
    }
}

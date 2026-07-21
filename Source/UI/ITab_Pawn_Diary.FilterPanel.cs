// Right-hand filter/controls panel for the Diary tab. Split from ITab_Pawn_Diary.cs.
//
// This is an independent, NON-virtualized scroll column drawn beside the journal. It owns the year
// selector, the journal filter controls (a favorites-only star toggle + per-tag chips that narrow
// the journal's cards), and — in dev mode — the diary dev tools that used to sit above the journal.
// It is built entirely from existing RimWorld widgets (Listing_Standard, Widgets.CheckboxLabeled/
// ButtonText, the year FloatMenu pager, DrawMenuSection) rather than any bespoke control.
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

        // ---- Journal filter state ----
        // The player's current filter selections. The pawn id lets the panel reset its state when the
        // tab (a shared singleton) switches pawns, so one pawn's toggles/scroll don't bleed onto the
        // next. The selections feed the journal through EnsureFilteredJournalEntries (FillTab).
        private string filterPanelPawnId;
        private bool filterFavoritesOnly;
        private readonly HashSet<string> filterActiveTags = new HashSet<string>();
        // Reusable buffer for the per-year distinct tags. The source/revision/year keys let Layout,
        // Repaint, and other IMGUI passes reuse it instead of rescanning a long diary every draw.
        // Each row carries the group label plus its color cue/importance so a filter chip can be tinted
        // exactly like the matching group chip on the entry cards.
        private readonly List<FilterTagInfo> filterTagInfoBuffer = new List<FilterTagInfo>();
        private List<DiaryEntryView> filterTagInfoSource;
        private int filterTagInfoSourceRevision = -1;
        private int filterTagInfoYear = int.MinValue;
        // Reusable scratch for the tag-chip flow layout (relative rects), cleared and refilled each
        // draw so the per-frame panel render allocates nothing once its capacity settles.
        private readonly List<Rect> filterTagChipLayoutBuffer = new List<Rect>();

        // ---- Filtered journal list ----
        // The narrowed card list FillTab draws while any filter is engaged, plus the inputs it was
        // built from. Rebuilt only when one of those inputs changes (the year's cards, the filter
        // selections, or the favorite set), so idle frames reuse the same list reference and the row
        // layout cache stays valid. journalFilterVersion bumps on every rebuild and stands in for the
        // visible-entries revision in the layout dirty check — it covers BOTH content changes and
        // filter changes, which a cache revision alone would miss.
        private readonly List<DiaryEntryView> journalFilterBuffer = new List<DiaryEntryView>();
        private List<DiaryEntryView> journalFilterSource;
        private int journalFilterSourceRevision = -1;
        private bool journalFilterFavoritesOnly;
        private readonly HashSet<string> journalFilterTags = new HashSet<string>();
        private int journalFilterFavoritesVersion = -1;
        private int journalFilterVersion;

        /// <summary>
        /// True while any journal filter selection is engaged (favorites-only or at least one tag
        /// chip). Drives both the header funnel's amber "filtering" state and whether FillTab narrows
        /// the journal through <see cref="EnsureFilteredJournalEntries"/>.
        /// </summary>
        private bool JournalFiltersActive
        {
            get { return filterFavoritesOnly || filterActiveTags.Count > 0; }
        }

        /// <summary>
        /// Returns the selected year's cards narrowed by the current filter selections, reusing one
        /// stable buffer between frames. The buffer is refilled only when an input changed: the source
        /// list, its revision, the favorites-only toggle, the active tag set, or the favorite set
        /// itself (a star click with "Favorites only" on must re-filter the same frame). The match
        /// rule itself is the pure <see cref="DiaryEntryFilterPolicy.Passes"/>; the tag chips and year
        /// counts intentionally read the UNFILTERED year list so they cannot shrink away while
        /// filtering narrows the journal.
        /// </summary>
        private List<DiaryEntryView> EnsureFilteredJournalEntries(List<DiaryEntryView> ordered, int visibleRevision)
        {
            bool dirty = journalFilterSource != ordered
                || journalFilterSourceRevision != visibleRevision
                || journalFilterFavoritesOnly != filterFavoritesOnly
                || journalFilterFavoritesVersion != favoritesVersion
                || journalFilterTags.Count != filterActiveTags.Count
                || !journalFilterTags.SetEquals(filterActiveTags);
            if (!dirty)
            {
                return journalFilterBuffer;
            }

            journalFilterBuffer.Clear();
            if (ordered != null)
            {
                for (int i = 0; i < ordered.Count; i++)
                {
                    DiaryEntryView entry = ordered[i];
                    if (entry != null
                        && DiaryEntryFilterPolicy.Passes(
                            filterFavoritesOnly,
                            IsFavoriteEntry(entry.EntryKey),
                            entry.GroupLabel,
                            filterActiveTags))
                    {
                        journalFilterBuffer.Add(entry);
                    }
                }
            }

            journalFilterSource = ordered;
            journalFilterSourceRevision = visibleRevision;
            journalFilterFavoritesOnly = filterFavoritesOnly;
            journalFilterFavoritesVersion = favoritesVersion;
            journalFilterTags.Clear();
            journalFilterTags.UnionWith(filterActiveTags);
            journalFilterVersion++;
            return journalFilterBuffer;
        }

        // One distinct tag shown as a filter chip: its display label and the accent inputs (color cue +
        // importance) taken from the first entry that used it this year.
        private struct FilterTagInfo
        {
            public string label;
            public string colorCue;
            public bool important;
        }

        // Filter tag chip geometry (mirrors the entry-card group chip; flows/wraps to the panel width).
        private const float FilterTagChipHeight = 22f;
        private const float FilterTagChipHPadding = 9f;
        private const float FilterTagChipGap = 6f;
        private const float FilterTagChipRowGap = 6f;

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
            // This lifecycle reset must run even when the panel is hidden. FillTab still applies active
            // filters in that state, so returning first would leak the previous pawn's selections.
            ResetFilterStateOnPawnChange(pawn);
            if (panelRect.width <= 1f || panelRect.height <= 1f)
            {
                return;
            }

            // The seasonal wash is painted once across the whole tab in FillTab, so it already sits
            // behind this panel; nothing extra to draw here.
            // No DrawMenuSection here on purpose: the inspect tab already paints a window background
            // behind the whole tab, so an extra inset box only boxed the controls in and its border
            // crowded RimWorld's inspect-pane close button at the top-right. The panel now floats on
            // the shared background; the journal cards still supply the visual structure on the left.
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
                DrawJournalFilterSection(
                    listing,
                    orderedForTags,
                    entriesCache == null ? -1 : entriesCache.VisibleRevision);
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
        /// Journal filter controls: a favorites-only toggle, per-tag chips for the tags present in the
        /// current year, and a Clear button. The selections narrow the journal via
        /// <see cref="EnsureFilteredJournalEntries"/> on the next frame.
        /// </summary>
        private void DrawJournalFilterSection(
            Listing_Standard listing,
            List<DiaryEntryView> orderedForTags,
            int visibleRevision)
        {
            // No "Filters" section header: the favorites star and the tag chips read as filter controls
            // on their own, and the extra title only added clutter above them.
            listing.Gap(6f);
            DrawFavoritesOnlyToggle(listing);

            listing.Gap(6f);
            DrawFilterSectionHeader(listing, "PawnDiary.Tab.FilterTagsHeader".Translate());
            CollectTagInfo(orderedForTags, visibleRevision);
            if (filterTagInfoBuffer.Count == 0)
            {
                Color oldColor = GUI.color;
                GUI.color = UiStyle.ModelNameColor;
                listing.Label("PawnDiary.Tab.FilterNoTags".Translate());
                GUI.color = oldColor;
            }
            else
            {
                // Tags render as color-coded chips matching the group chip on the entry cards. Clicking
                // a chip toggles it; active chips read filled at full strength, inactive ones as a quiet
                // outline, so the selected/unselected state is obvious.
                DrawFilterTagChips(listing);
            }

            listing.Gap(8f);
            // Clear resets the filter selections; the journal re-filters on the next frame. A separate
            // "Apply" button was removed — filtering is toggle-driven and applies immediately.
            Rect clearRect = listing.GetRect(ControlLineHeight);
            if (Widgets.ButtonText(clearRect, "PawnDiary.Tab.FilterClear".Translate()))
            {
                filterFavoritesOnly = false;
                filterActiveTags.Clear();
            }
        }

        /// <summary>
        /// Draws the "Favorites only" filter as a star toggle (replacing the old checkbox): the label on
        /// the left and a star icon on the right that reads warm gold when active and a quiet outline
        /// when not. The whole row toggles <see cref="filterFavoritesOnly"/> on click, narrowing the
        /// journal to the pawn's starred pages.
        /// </summary>
        private void DrawFavoritesOnlyToggle(Listing_Standard listing)
        {
            Rect row = listing.GetRect(ControlLineHeight);
            Widgets.DrawHighlightIfMouseover(row);

            bool on = filterFavoritesOnly;
            float iconSize = Mathf.Min(row.height - 4f, 22f);
            Rect starRect = new Rect(
                row.xMax - iconSize,
                row.y + (row.height - iconSize) * 0.5f,
                iconSize,
                iconSize);

            Color oldColor = GUI.color;
            GUI.color = on ? UiStyle.FavoriteStarColor : new Color(1f, 1f, 1f, 0.72f);
            GUI.DrawTexture(starRect, DiaryButtonTextures.Favorite);
            GUI.color = oldColor;

            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(
                new Rect(row.x, row.y, Mathf.Max(0f, row.width - iconSize - 6f), row.height),
                "PawnDiary.Tab.FilterFavoritesOnly".Translate());
            Text.Anchor = oldAnchor;

            if (Widgets.ButtonInvisible(row, false))
            {
                filterFavoritesOnly = !filterFavoritesOnly;
            }
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
        /// Clears the journal filter selections and panel scroll when the shown pawn changes, so state
        /// does not carry across pawns on this shared tab instance.
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
            filterTagInfoSource = null;
            filterTagInfoSourceRevision = -1;
            filterTagInfoYear = int.MinValue;
        }

        /// <summary>
        /// Rebuilds <see cref="filterTagInfoBuffer"/> with the distinct, non-empty entry group tags in
        /// the current year's ordered cards (stable sorted by label, capped). Each row keeps the color
        /// cue/importance of the first entry that used the tag so its chip matches the card group chip.
        /// </summary>
        private void CollectTagInfo(List<DiaryEntryView> ordered, int visibleRevision)
        {
            if (ReferenceEquals(filterTagInfoSource, ordered)
                && filterTagInfoSourceRevision == visibleRevision
                && filterTagInfoYear == selectedYear)
            {
                return;
            }

            filterTagInfoBuffer.Clear();
            filterTagInfoSource = ordered;
            filterTagInfoSourceRevision = visibleRevision;
            filterTagInfoYear = selectedYear;
            if (ordered == null)
            {
                return;
            }

            for (int i = 0; i < ordered.Count && filterTagInfoBuffer.Count < FilterMaxTagRows; i++)
            {
                DiaryEntryView entry = ordered[i];
                string label = entry?.GroupLabel;
                if (string.IsNullOrWhiteSpace(label) || ContainsTagLabel(label))
                {
                    continue;
                }

                filterTagInfoBuffer.Add(new FilterTagInfo
                {
                    label = label,
                    colorCue = entry.ColorCue,
                    important = entry.Important,
                });
            }

            filterTagInfoBuffer.Sort((left, right) => string.Compare(left.label, right.label, StringComparison.OrdinalIgnoreCase));
        }

        private bool ContainsTagLabel(string label)
        {
            for (int i = 0; i < filterTagInfoBuffer.Count; i++)
            {
                if (string.Equals(filterTagInfoBuffer[i].label, label, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Flows the collected tag chips across the panel width, wrapping to new rows, and reserves the
        /// matching height in <paramref name="listing"/>. Clicking a chip toggles its active state.
        /// </summary>
        private void DrawFilterTagChips(Listing_Standard listing)
        {
            int count = filterTagInfoBuffer.Count;
            float maxWidth = listing.ColumnWidth;
            if (count == 0 || maxWidth <= 0f)
            {
                return;
            }

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;

            // Single layout pass computes each chip's position relative to (0,0). Reserving the summed
            // height afterwards keeps the drawn chips and the Listing's advance perfectly in sync.
            filterTagChipLayoutBuffer.Clear();
            float x = 0f;
            float y = 0f;
            for (int i = 0; i < count; i++)
            {
                float chipWidth = Mathf.Min(maxWidth, FilterTagChipWidth(filterTagInfoBuffer[i].label));
                if (x > 0f && x + FilterTagChipGap + chipWidth > maxWidth)
                {
                    x = 0f;
                    y += FilterTagChipHeight + FilterTagChipRowGap;
                }
                else if (x > 0f)
                {
                    x += FilterTagChipGap;
                }

                filterTagChipLayoutBuffer.Add(new Rect(x, y, chipWidth, FilterTagChipHeight));
                x += chipWidth;
            }

            Rect block = listing.GetRect(y + FilterTagChipHeight);
            for (int i = 0; i < count; i++)
            {
                FilterTagInfo info = filterTagInfoBuffer[i];
                Rect rel = filterTagChipLayoutBuffer[i];
                Rect chipRect = new Rect(block.x + rel.x, block.y + rel.y, rel.width, rel.height);
                Color accent = UiStyle.ColorForCue(info.colorCue, info.important);
                bool active = filterActiveTags.Contains(info.label);
                if (DrawFilterTagChip(chipRect, info.label, accent, active))
                {
                    if (active)
                    {
                        filterActiveTags.Remove(info.label);
                    }
                    else
                    {
                        filterActiveTags.Add(info.label);
                    }
                }
            }

            Text.Font = oldFont;
        }

        /// <summary>
        /// Measures a chip's width for the current (Tiny) font: the label plus symmetric padding.
        /// </summary>
        private static float FilterTagChipWidth(string label)
        {
            return Text.CalcSize(label ?? string.Empty).x + FilterTagChipHPadding * 2f;
        }

        /// <summary>
        /// Draws one tag chip in the same visual language as the entry-card group chip
        /// (<see cref="DrawGroupLabel"/>). Selected chips are boldly filled with a 2px accent outline and
        /// near-white text; unselected chips read as a quiet, mostly-empty thin outline — so the
        /// select/deselect state is obvious at a glance. Returns true when clicked.
        /// </summary>
        private static bool DrawFilterTagChip(Rect rect, string label, Color accent, bool active)
        {
            float fillMul = active ? 0.42f : 0.08f;
            float fillAlpha = active ? 0.95f : 0.30f;
            float outlineAlpha = active ? 1f : 0.55f;
            int outlineThickness = active ? 2 : 1;
            Widgets.DrawBoxSolidWithOutline(
                rect,
                new Color(accent.r * fillMul, accent.g * fillMul, accent.b * fillMul, fillAlpha),
                new Color(accent.r, accent.g, accent.b, outlineAlpha),
                outlineThickness);
            Widgets.DrawHighlightIfMouseover(rect);

            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Anchor = TextAnchor.MiddleCenter;
            Color textColor = Color.Lerp(accent, Color.white, active ? 0.75f : 0.25f);
            textColor.a = active ? 1f : 0.70f;
            GUI.color = textColor;
            Widgets.Label(new Rect(rect.x + 4f, rect.y, Mathf.Max(0f, rect.width - 8f), rect.height), label);
            GUI.color = oldColor;
            Text.Anchor = oldAnchor;

            return Widgets.ButtonInvisible(rect, false);
        }

        /// <summary>
        /// Flips the persisted show/hide state of the right-hand filter panel and saves settings, so the
        /// choice sticks across pawns and sessions like the other global UI preferences.
        /// </summary>
        private void ToggleFilterPanelVisible()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings == null)
            {
                return;
            }

            settings.showDiaryFilterPanel = !settings.showDiaryFilterPanel;
            WriteGlobalSettings();
        }

        /// <summary>
        /// Draws the header funnel icon that shows/hides the filter panel and returns true when clicked.
        /// Three states are shown by tint alone (the CoreUI solid/duotone funnels are Pro, so there is
        /// no separate filled glyph): closed with no filters = dim, open with no filters = brighter,
        /// and any active filter = amber even while closed. Hover lifts each toward full strength.
        /// </summary>
        private static bool DrawFilterPanelToggleIcon(Rect rect, bool panelOpen, bool filtersActive)
        {
            bool hover = Mouse.IsOver(rect);
            Color iconColor;
            if (filtersActive)
            {
                Color accent = UiStyle.FilterActiveIconColor;
                iconColor = hover ? new Color(accent.r, accent.g, accent.b, 1f) : accent;
            }
            else
            {
                float alpha = hover
                    ? WritingStyleIconHoverAlpha
                    : (panelOpen ? Mathf.Min(1f, WritingStyleIconAlpha + 0.22f) : WritingStyleIconAlpha);
                iconColor = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            }

            // Pass iconColor as both base and mouseover (hover is already folded in above); this
            // overload, unlike the 2-arg ButtonImage, does not force GUI.color to white. No mouseover
            // sound, matching the old primitive toggle.
            bool clicked = Widgets.ButtonImage(rect, DiaryButtonTextures.Filter, iconColor, iconColor, false);

            TooltipHandler.TipRegion(
                rect,
                (panelOpen ? "PawnDiary.Tab.HideFilterPanel" : "PawnDiary.Tab.ShowFilterPanel").Translate());
            return clicked;
        }
    }
}

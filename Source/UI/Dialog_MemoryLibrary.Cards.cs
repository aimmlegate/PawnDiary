// Dialog_MemoryLibrary.Cards.cs — mutation-free list and card rendering for the M9 Library.
//
// Cards consume detached rows and cached labels only. Selection changes detached navigation
// state; persistent memory mutations remain staged for WindowUpdate after the IMGUI pass.
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

        private void DrawListPane(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(6f);
            bool hasPaging = list?.status == MemoryLibraryStatuses.Ready
                && (list.hasPrevious || list.hasMore);
            float footerHeight = hasPaging ? 30f : 0f;
            Rect rowsRect = new Rect(inner.x, inner.y, inner.width,
                Mathf.Max(0f, inner.height - footerHeight - (hasPaging ? 4f : 0f)));

            if (selectedOwner?.primaryHandle == null
                && session.selectedCompatibilityHandle != null)
            {
                DrawCompatibilityOnly(rowsRect);
                return;
            }
            if (list == null || list.status != MemoryLibraryStatuses.Ready)
            {
                DrawCenteredState(rowsRect, QueryStateText(list?.status));
                return;
            }
            if (list.rows == null || list.rows.Count == 0)
            {
                DrawMemoryQueryEmptyState(rowsRect, ListEmptyText(), true);
                // A recovery button can reset list to null during this same IMGUI event. Do not
                // dereference the invalidated publication in the paging footer below.
                return;
            }
            DrawVirtualizedList(rowsRect);
            if (hasPaging)
                DrawPageFooter(new Rect(inner.x, rowsRect.yMax + 4f, inner.width, footerHeight),
                    list.returnedStart, list.returnedCount, list.totalMatchedRows,
                    list.hasPrevious, list.hasMore,
                    delegate
                    {
                        listStart = Math.Max(0, list.returnedStart - PageSize);
                        listExpectedSnapshotRevision = list.listSnapshotRevision;
                        listScroll = Vector2.zero;
                        repositoryNavigationDirty = true;
                    },
                    delegate
                    {
                        listStart = list.nextStart;
                        listExpectedSnapshotRevision = list.listSnapshotRevision;
                        listScroll = Vector2.zero;
                        repositoryNavigationDirty = true;
                    });
        }

        private void DrawVirtualizedList(Rect outRect)
        {
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            float rowHeight = Mathf.Max(64f, style.memoryLibraryCardHeight);
            MemoryLibraryUiVirtualWindow window = MemoryLibraryUiPolicy.Virtualize(
                list.rows.Count, listScroll.y, outRect.height, rowHeight,
                style.memoryLibraryOverscanRows, style.memoryLibraryMaximumMaterializedRows);
            Rect view = new Rect(0f, 0f, Mathf.Max(0f, outRect.width - 16f),
                Mathf.Max(outRect.height, window.contentHeight));
            Widgets.BeginScrollView(outRect, ref listScroll, view);
            try
            {
                for (int index = window.firstIndex; index < window.endExclusive; index++)
                    DrawListCard(new Rect(0f, index * rowHeight, view.width,
                        Mathf.Max(40f, rowHeight - 4f)), list.rows[index]);
            }
            finally { Widgets.EndScrollView(); }
        }

        /// <summary>
        /// Draws the shared Memory card chrome - selection/hover background, the category accent
        /// spine, and a low-alpha wash of that accent across the card - mirroring the Diary's entry
        /// cards so both windows read as one product. Returns the padded text rect inside the spine.
        /// </summary>
        /// <summary>
        /// Draws one Memory card using the Diary's exact entry-card recipe so the two windows are
        /// visually the same product: bordered menu section, page wash inside the spine, title bar,
        /// 1px-inset accent spine with its highlight, and the warm hairline under the header. The
        /// title bar carries the date on the left and right-aligned chips, matching how a Diary card
        /// carries its date and group chip. Returns the body rect below the header rule.
        /// </summary>
        private static Rect DrawCardChrome(Rect rect, Color accent, bool selected,
            string dateLabel, List<MemoryLibraryUiChip> chips)
        {
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            float spine = Mathf.Max(2f, style.memoryLibraryAccentWidth);
            float titleHeight = Mathf.Max(18f, style.memoryLibraryCardTitleHeight);
            float innerX = rect.x + spine + 2f;
            float innerWidth = Mathf.Max(0f, rect.width - spine - 4f);

            Widgets.DrawMenuSection(rect);
            if (selected)
                Widgets.DrawBoxSolid(
                    new Rect(innerX, rect.y + 1f, innerWidth, Mathf.Max(0f, rect.height - 2f)),
                    style.MemoryLibrarySelectedCardBackground);
            // Page wash behind the body only, drawn under the hover highlight so mouseover reads.
            Widgets.DrawBoxSolid(
                new Rect(innerX, rect.y + titleHeight, innerWidth,
                    Mathf.Max(0f, rect.height - titleHeight - 2f)),
                new Color(accent.r, accent.g, accent.b,
                    Mathf.Clamp(style.memoryLibraryCardTintAlpha, 0f, 0.4f)));
            Widgets.DrawHighlightIfMouseover(rect);

            Rect titleRect = new Rect(rect.x, rect.y, rect.width, titleHeight);
            Widgets.DrawTitleBG(titleRect);

            Rect accentRect = new Rect(rect.x + 1f, rect.y + 1f, spine,
                Mathf.Max(0f, rect.height - 2f));
            Widgets.DrawBoxSolid(accentRect, accent);
            Widgets.DrawBoxSolid(
                new Rect(accentRect.xMax, accentRect.y, 1f, accentRect.height),
                style.AccentHighlightColor);
            Widgets.DrawBoxSolid(
                new Rect(rect.x + spine + 8f, rect.y + titleHeight,
                    Mathf.Max(0f, rect.width - spine - 20f), 1f),
                style.HeaderRuleColor);

            // Date left, chips right — the Diary's header split.
            Rect headerRect = new Rect(rect.x + spine + 8f, rect.y + 2f,
                Mathf.Max(0f, rect.width - spine - 18f), titleHeight - 4f);
            GameFont priorFont = Text.Font;
            Color priorColor = GUI.color;
            Text.Font = GameFont.Tiny;
            float chipWidth = MeasureChipRow(chips, headerRect.width);
            GUI.color = style.EntryDateColor;
            Widgets.Label(
                new Rect(headerRect.x, headerRect.y,
                    Mathf.Max(0f, headerRect.width - chipWidth - 6f), headerRect.height),
                dateLabel ?? string.Empty);
            GUI.color = priorColor;
            Text.Font = priorFont;
            if (chipWidth > 0f)
                DrawChipRow(new Rect(headerRect.xMax - chipWidth, headerRect.y + 1f,
                    chipWidth, headerRect.height - 2f), chips, true);

            return new Rect(rect.x + spine + 8f, rect.y + titleHeight + 4f,
                Mathf.Max(10f, rect.width - spine - 18f),
                Mathf.Max(10f, rect.height - titleHeight - 8f));
        }

        /// <summary>
        /// Width one chip row needs, capped to the space available. Whole trailing chips are dropped
        /// rather than clipping one mid-word; the first chip is the most identifying, so it survives.
        /// Callers must already be in GameFont.Tiny.
        /// </summary>
        private static float MeasureChipRow(List<MemoryLibraryUiChip> chips, float available)
        {
            if (chips == null || chips.Count == 0) return 0f;
            float total = 0f;
            bool any = false;
            for (int index = 0; index < chips.Count; index++)
            {
                MemoryLibraryUiChip chip = chips[index];
                if (chip == null || string.IsNullOrEmpty(chip.label)) continue;
                float step = Text.CalcSize(chip.label).x + 12f + (any ? 4f : 0f);
                if (total + step > available) break;
                total += step;
                any = true;
            }
            return total;
        }

        /// <summary>
        /// Lays out pre-translated chips left to right on one line, dropping any that no longer fit
        /// rather than clipping one mid-word. Mirrors the Diary's group-label chip treatment.
        /// </summary>
        private static void DrawChipRow(Rect rect, List<MemoryLibraryUiChip> chips,
            bool rightAlign = false)
        {
            if (chips == null || chips.Count == 0) return;
            GameFont priorFont = Text.Font;
            TextAnchor priorAnchor = Text.Anchor;
            Color priorColor = GUI.color;
            Text.Font = GameFont.Tiny;
            float x = rightAlign
                ? rect.xMax - MeasureChipRow(chips, rect.width)
                : rect.x;
            for (int index = 0; index < chips.Count; index++)
            {
                MemoryLibraryUiChip chip = chips[index];
                if (chip == null || string.IsNullOrEmpty(chip.label)) continue;
                float width = Text.CalcSize(chip.label).x + 12f;
                if (x + width > rect.xMax + 0.5f) break;
                Rect box = new Rect(x, rect.y, width, rect.height);
                Color accent = chip.color;
                Widgets.DrawBoxSolidWithOutline(box,
                    new Color(accent.r * 0.23f, accent.g * 0.23f, accent.b * 0.23f, 0.72f),
                    new Color(accent.r, accent.g, accent.b, 0.92f), 1);
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.Lerp(accent, Color.white, 0.55f);
                Widgets.LabelFit(
                    new Rect(box.x + 4f, box.y, Mathf.Max(4f, box.width - 8f), box.height),
                    chip.label);
                x = box.xMax + 4f;
            }
            GUI.color = priorColor;
            Text.Anchor = priorAnchor;
            Text.Font = priorFont;
        }

        /// <summary>Accent for a thread row: its subject type, which is what the row is about.</summary>
        private static Color ThreadAccent(string subjectTypeToken)
        {
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            if (string.Equals(subjectTypeToken, "person", StringComparison.OrdinalIgnoreCase))
                return style.MemoryLibraryCategoryRelationshipsColor;
            if (string.Equals(subjectTypeToken, "faction", StringComparison.OrdinalIgnoreCase))
                return style.MemoryLibraryCategoryFactionsColor;
            return style.MemoryLibraryCategoryMixedColor;
        }

        /// <summary>Accent for one memory block: its category, or the summary hue when folded.</summary>
        private static Color BlockAccent(MemoryBlockRow row)
        {
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            if (row == null) return style.MemoryLibraryCategoryMixedColor;
            if (row.rollingSummary || row.closedSummary)
                return style.MemoryLibrarySummaryAccentColor;
            return style.MemoryLibraryCategoryColor(row.projectedCategoryMask);
        }

        private static Color ListRowAccent(MemoryLibraryListRow row)
        {
            if (row?.thread != null) return ThreadAccent(row.thread.subjectTypeToken);
            if (row?.standalone != null) return BlockAccent(row.standalone);
            return DiaryJournalView.UiStyle.MemoryLibraryCategoryMixedColor;
        }

        private void DrawListCard(Rect rect, MemoryLibraryListRow row)
        {
            if (row == null) return;
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            bool selected = row.thread != null && Same(row.thread.rootHandle, session.selectedRootHandle)
                || row.standalone != null && MemoryLibraryUiPolicy.Same(
                    row.standalone.recordHandle, session.selectedRecordHandle)
                || row.imported != null && Same(row.imported.archiveHandle,
                    session.selectedArchiveHandle);
            string cardKey = MemoryLibraryUiPolicy.ListRowCacheKey(row);
            cachedListCardTitles.TryGetValue(cardKey, out string title);
            cachedListCardDetails.TryGetValue(cardKey, out string details);
            cachedListCardDates.TryGetValue(cardKey, out string date);
            cachedListCardChips.TryGetValue(cardKey, out List<MemoryLibraryUiChip> chips);
            Rect text = DrawCardChrome(rect, ListRowAccent(row), selected, date, chips);
            // A hidden row keeps its spine at full strength (so it stays findable in the list) but
            // fades its text, which is what "not used in writing" should look like at a glance.
            Color priorColor = GUI.color;
            if (row.standalone?.suppressed == true)
                GUI.color = new Color(1f, 1f, 1f,
                    Mathf.Clamp(style.memoryLibrarySuppressedAlpha, 0.2f, 1f));
            Text.Font = GameFont.Tiny;
            float detailsHeight = Text.LineHeight;
            Text.Font = GameFont.Small;
            Widgets.Label(
                new Rect(text.x, text.y, text.width,
                    Mathf.Max(Text.LineHeight, text.height - detailsHeight - 2f)),
                title ?? string.Empty);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, GUI.color.a * 0.7f);
            Widgets.Label(new Rect(text.x, text.yMax - detailsHeight, text.width, detailsHeight),
                details ?? string.Empty);
            GUI.color = priorColor;
            Text.Font = GameFont.Small;
            if (Widgets.ButtonInvisible(rect))
            {
                session.selectedRootHandle = MemoryLibraryUiPolicy.Copy(row.thread?.rootHandle);
                session.selectedRecordHandle = MemoryLibraryUiPolicy.Copy(row.standalone?.recordHandle);
                session.selectedArchiveHandle = MemoryLibraryUiPolicy.Copy(row.imported?.archiveHandle);
                session.selectedPlacementToken = row.standalone != null ? "standalone" : string.Empty;
                session.narrowDetailOpen = true;
                session.editDraft = null;
                session.feedbackStatus = string.Empty;
                selectedBlockRow = row.standalone;
                selectedImportedRow = row.imported;
                diagnosticsExpanded = false;
                ResetDetailQuery();
            }
        }

        private string ListEmptyText()
        {
            string state = MemoryLibraryUiPolicy.ListEmptyState(
                list?.emptyStateToken, session.memorySearch, session.filters);
            if (state == "no_matches")
                return T("PawnDiary.Memory.Library.NoMatches");
            if (state == "no_filter_matches")
                return T("PawnDiary.Memory.Library.NoFilterMatches");
            if (state == "no_memories")
            {
                if (session.selectedView == MemoryLibraryViews.Threads)
                    return T("PawnDiary.Memory.Library.NoThreads");
                if (session.selectedView == MemoryLibraryViews.Standalone)
                    return T("PawnDiary.Memory.Library.NoStandalone");
                return T("PawnDiary.Memory.Library.NoImported");
            }
            if (session.selectedView == MemoryLibraryViews.Threads)
                return T("PawnDiary.Memory.Library.NoThreads");
            if (session.selectedView == MemoryLibraryViews.Standalone)
                return T("PawnDiary.Memory.Library.NoStandalone");
            return T("PawnDiary.Memory.Library.NoImported");
        }

        /// <summary>
        /// Draws the most useful empty-result recovery: clear search, then filters, then change
        /// owner for a genuinely empty section when another directory row is available.
        /// </summary>
        private void DrawMemoryQueryEmptyState(Rect rect, string message, bool offerOwnerChange)
        {
            if (!string.IsNullOrWhiteSpace(session.memorySearch))
            {
                DrawCenteredStateWithAction(rect, message,
                    T("PawnDiary.Memory.Library.ClearSearch"), delegate
                    {
                        session.memorySearch = string.Empty;
                        ResetContentListQuery();
                    });
                return;
            }
            if (MemoryLibraryUiPolicy.ActiveFilterCount(session.filters) > 0)
            {
                DrawCenteredStateWithAction(rect, message,
                    T("PawnDiary.Memory.Library.ClearFilters"), ClearMemoryLibraryFilters);
                return;
            }
            if (offerOwnerChange && (owners?.directoryRowCount ?? 0) > 1)
            {
                DrawCenteredStateWithAction(rect, message,
                    T("PawnDiary.Memory.Library.ChangeOwner"), OpenOwnerMenu);
                return;
            }
            DrawCenteredState(rect, message);
        }

        private string BlockMeta(MemoryBlockRow row)
        {
            string key = RecordKey(row?.recordHandle);
            if (key.Length > 0 && cachedBlockMeta.TryGetValue(key, out string cached))
                return cached;
            return BuildBlockMeta(row);
        }

        /// <summary>
        /// The quiet line under a memory card. Category, importance, edit and hidden state moved to
        /// chips, so this keeps only what a chip cannot carry: when it happened and how long it has.
        /// </summary>
        private string BuildBlockMeta(MemoryBlockRow row)
        {
            if (row == null) return string.Empty;
            return LifetimeLabel(row);
        }

        private void CacheBlockDisplay(MemoryBlockRow row)
        {
            string key = RecordKey(row?.recordHandle);
            if (key.Length == 0 || cachedBlockMeta.ContainsKey(key)) return;
            CacheDate(row.originalTick);
            MemoryLibraryUiLifetime life = MemoryLibraryUiPolicy.Lifetime(row, detachedNowTick,
                detachedMinorLifetimeTicks, detachedRegularLifetimeTicks);
            if (life.expiryTick != long.MaxValue) CacheDate(life.expiryTick);
            cachedLifetimeLabels[key] = BuildLifetimeLabel(row);
            cachedBlockMeta[key] = BuildBlockMeta(row);
            cachedBlockChips[key] = BuildBlockChips(row);
            cachedBlockCardWording[key] = EmptyFallback(row.displayWording,
                T("PawnDiary.Memory.Library.EmptyWording"));
            if (row.lastAutomaticIncludedTick >= 0) CacheDate(row.lastAutomaticIncludedTick);
            cachedUsageLabels[key] = BuildUsageFacts(row);
        }

        private void CacheListCardDisplay(MemoryLibraryListRow row)
        {
            string key = MemoryLibraryUiPolicy.ListRowCacheKey(row);
            if (key.Length == 0 || cachedListCardTitles.ContainsKey(key)) return;
            string title;
            string details;
            List<MemoryLibraryUiChip> chips;
            // The state a card used to spell out in one grey "a · b · c" run now reads as chips, so
            // the meta line keeps only what a chip cannot carry: when it happened, and how much of
            // it there is.
            string date;
            if (row.thread != null)
            {
                title = EmptyFallback(row.thread.subjectLabel,
                    T("PawnDiary.Memory.Library.UnknownSubject"));
                date = DateLabel(row.thread.latestActivityTick);
                details = T("PawnDiary.Memory.Library.ThreadCardMeta",
                    row.thread.chapterCount, row.thread.manageableMemoryCount);
                chips = BuildThreadChips(row.thread);
            }
            else if (row.standalone != null)
            {
                title = EmptyFallback(row.standalone.displayWording,
                    T("PawnDiary.Memory.Library.EmptyWording"));
                date = DateLabel(row.standalone.originalTick);
                details = LifetimeLabel(row.standalone);
                chips = BuildBlockChips(row.standalone);
            }
            else
            {
                title = EmptyFallback(row.imported?.preview,
                    T("PawnDiary.Memory.Library.ImportedPreviewUnavailable"));
                date = DateLabel(row.imported?.originalTick ?? -1);
                details = MigrationReasonLabel(row.imported?.migrationReasonToken);
                chips = new List<MemoryLibraryUiChip>
                {
                    Chip(ArchiveSourceLabel(row.imported?.archiveHandle),
                        DiaryJournalView.UiStyle.MemoryLibraryCategoryMixedColor)
                };
            }
            cachedListCardTitles[key] = title;
            cachedListCardDetails[key] = details;
            cachedListCardDates[key] = date;
            cachedListCardChips[key] = chips;
        }

        private static MemoryLibraryUiChip Chip(string label, Color color)
        {
            return new MemoryLibraryUiChip { label = label ?? string.Empty, color = color };
        }

        /// <summary>
        /// Chips for a thread row: what kind of subject it is, how important its heaviest memory is,
        /// and whether the player has touched any of it. Counts only appear when non-zero.
        /// </summary>
        private List<MemoryLibraryUiChip> BuildThreadChips(MemoryThreadHeaderRow thread)
        {
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            List<MemoryLibraryUiChip> chips = new List<MemoryLibraryUiChip>();
            if (thread == null) return chips;
            chips.Add(Chip(RootTypeLabel(thread.subjectTypeToken),
                ThreadAccent(thread.subjectTypeToken)));
            if (thread.highestImportanceMask != 0)
                chips.Add(Chip(ImportanceLabel(thread.highestImportanceMask),
                    style.QuietCueColor));
            if (thread.editedCount > 0)
                chips.Add(Chip(T("PawnDiary.Memory.Library.EditedCount", thread.editedCount),
                    style.FilterActiveIconColor));
            if (thread.suppressedCount > 0)
                chips.Add(Chip(
                    T("PawnDiary.Memory.Library.SuppressedCount", thread.suppressedCount),
                    style.MemoryLibraryCategoryMixedColor));
            return chips;
        }

        /// <summary>
        /// Chips for one memory block. A folded Summary shows its role instead of a category,
        /// because it carries several and would otherwise claim one it does not own.
        /// </summary>
        private List<MemoryLibraryUiChip> BuildBlockChips(MemoryBlockRow row)
        {
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            List<MemoryLibraryUiChip> chips = new List<MemoryLibraryUiChip>();
            if (row == null) return chips;
            if (row.rollingSummary || row.closedSummary)
                chips.Add(Chip(SummaryRoleLabel(row), style.MemoryLibrarySummaryAccentColor));
            else
                chips.Add(Chip(ContentCategoryLabel(row.projectedCategoryMask),
                    style.MemoryLibraryCategoryColor(row.projectedCategoryMask)));
            if (row.projectedHighestImportanceMask != 0)
                chips.Add(Chip(ImportanceLabel(row.projectedHighestImportanceMask),
                    style.QuietCueColor));
            if (row.playerEdited)
                chips.Add(Chip(T("PawnDiary.Memory.Library.Edited"),
                    style.FilterActiveIconColor));
            if (row.suppressed)
                chips.Add(Chip(T("PawnDiary.Memory.Library.Suppressed"),
                    style.MemoryLibraryCategoryMixedColor));
            if ((row.projectedCategoryMask & ~detachedCategoryMask) != 0)
                chips.Add(Chip(T("PawnDiary.Memory.Library.CategoryDisabled"),
                    style.MemoryLibraryCategoryMixedColor));
            return chips;
        }

        private string BlockCardWording(MemoryBlockRow row)
        {
            string key = RecordKey(row?.recordHandle);
            return key.Length > 0 && cachedBlockCardWording.TryGetValue(key, out string cached)
                ? cached : string.Empty;
        }

    }
}

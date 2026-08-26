// Dialog_MemoryLibrary.Layout.cs — mutation-free immediate-mode rendering for the M9 Library.
//
// Every button below either navigates detached state or stages a MemoryLibraryCommand. Persistent
// mutations are deliberately impossible from this file; WindowUpdate transfers staged commands to
// DiaryGameComponent after the IMGUI pass has ended.
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
        public override void DoWindowContents(Rect inRect)
        {
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            float control = Mathf.Max(24f, style.memoryLibraryControlHeight);
            float gap = Mathf.Max(2f, style.memoryLibraryCardGap);
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 32f),
                "PawnDiary.Memory.Library.Title".Translate());
            Text.Font = GameFont.Small;
            y += 36f;

            y = DrawOwnerBar(new Rect(inRect.x, y, inRect.width, control), gap);
            if (session.selectedOwnerHandle == null && session.selectedCompatibilityHandle == null)
            {
                DrawCenteredState(new Rect(inRect.x, y + gap, inRect.width,
                    Mathf.Max(40f, inRect.yMax - y - gap)), OwnerEmptyText());
                return;
            }
            if (owners?.status == MemoryLibraryStatuses.Ready
                && owners.ownerEmptyStateToken == "no_matches")
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(inRect.x, y + gap, inRect.width, 22f),
                    T("PawnDiary.Memory.Library.NoOwnerMatches"));
                GUI.color = Color.white;
                y += 24f;
            }

            if (HasExactSelectedOwner())
            {
                float cultureHeight = Mathf.Max(58f, style.memoryLibraryCulturePanelHeight);
                DrawCulturePanel(new Rect(inRect.x, y + gap, inRect.width, cultureHeight));
                y += gap + cultureHeight + gap;
            }
            else y += gap;

            y = DrawViewAndSearch(new Rect(inRect.x, y, inRect.width, control), gap);
            if (session.selectedView == MemoryLibraryViews.Imported)
                y = DrawImportedSort(new Rect(inRect.x, y + gap, inRect.width, control));
            else
                y = DrawFilters(new Rect(inRect.x, y + gap, inRect.width, control), gap);
            y += gap;

            Rect body = new Rect(inRect.x, y, inRect.width, Mathf.Max(0f, inRect.yMax - y));
            bool narrow = body.width < Mathf.Max(560f, style.memoryLibraryNarrowThreshold);
            if (narrow)
            {
                if (session.narrowDetailOpen)
                    DrawDetailPane(body, true);
                else
                    DrawListPane(body);
                return;
            }

            float paneGap = Mathf.Max(4f, style.memoryLibraryPaneGap);
            float fraction = Mathf.Clamp(style.memoryLibraryListFraction, 0.30f, 0.62f);
            float listWidth = Mathf.Max(220f, (body.width - paneGap) * fraction);
            Rect listRect = new Rect(body.x, body.y, listWidth, body.height);
            Rect detailRect = new Rect(listRect.xMax + paneGap, body.y,
                Mathf.Max(0f, body.xMax - listRect.xMax - paneGap), body.height);
            DrawListPane(listRect);
            DrawDetailPane(detailRect, false);
        }

        private float DrawOwnerBar(Rect rect, float gap)
        {
            float searchWidth = Mathf.Clamp(rect.width * 0.34f, 150f, 320f);
            float pagingWidth = Mathf.Min(72f, rect.width * 0.10f);
            float selectorWidth = Mathf.Max(120f,
                rect.width - searchWidth - pagingWidth * 2f - gap * 3f);
            Rect selector = new Rect(rect.x, rect.y, selectorWidth, rect.height);
            if (Widgets.ButtonText(selector,
                    T("PawnDiary.Memory.Library.OwnerButton",
                        EmptyFallback(session.selectedOwnerDisplayName,
                            T("PawnDiary.Memory.Library.SelectOwner")))))
                OpenOwnerMenu();

            Rect previous = new Rect(selector.xMax + gap, rect.y, pagingWidth, rect.height);
            if (Widgets.ButtonText(previous, T("PawnDiary.Memory.Library.Previous"),
                    true, true, owners?.hasPrevious == true))
            {
                ownerStart = Math.Max(0, owners.returnedStart - PageSize);
                ownerExpectedDirectoryRevision = owners.directoryRevision;
            }
            Rect next = new Rect(previous.xMax + gap, rect.y, pagingWidth, rect.height);
            if (Widgets.ButtonText(next, T("PawnDiary.Memory.Library.Next"),
                    true, true, owners?.hasMore == true))
            {
                ownerStart = owners.nextStart;
                ownerExpectedDirectoryRevision = owners.directoryRevision;
            }
            Rect search = new Rect(next.xMax + gap, rect.y, searchWidth, rect.height);
            string before = session.ownerSearch ?? string.Empty;
            string after = MemoryLibraryPolicy.ClampScalars(
                MemoryLibraryPolicy.RepairMalformedUtf16(
                    Widgets.TextField(search, before) ?? string.Empty),
                detachedSearchScalarCap, detachedSearchUtf16Cap);
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                session.ownerSearch = after;
                preferredOwnerId = string.Empty;
                ownerSearchDirty = true;
            }
            TooltipHandler.TipRegion(search, T("PawnDiary.Memory.Library.OwnerSearchTip"));
            return rect.yMax;
        }

        private bool HasExactSelectedOwner()
        {
            return !string.IsNullOrWhiteSpace(
                selectedOwner?.primaryHandle?.exactOwnerPawnIdOrEmpty)
                || !string.IsNullOrWhiteSpace(
                    selectedOwner?.compatibilityHandle?.exactOwnerPawnIdOrEmpty);
        }

        private void OpenOwnerMenu()
        {
            if (owners?.rows == null || owners.rows.Count == 0) return;
            long capturedDirectoryRevision = owners.directoryRevision;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int index = 0; index < owners.rows.Count; index++)
            {
                MemoryLibraryOwnerRow captured = owners.rows[index];
                if (captured == null) continue;
                options.Add(new FloatMenuOption(
                    T("PawnDiary.Memory.Library.OwnerMenuRow",
                        EmptyFallback(captured.displayName,
                            T("PawnDiary.Memory.Library.UnknownOwner")),
                        LifecycleLabel(captured.lifecycleToken), captured.threadCount,
                        captured.standaloneCount, captured.importedCount),
                    delegate
                    {
                        if (!MemoryLibraryUiPolicy.CanApplyOwnerMenuOption(
                                capturedDirectoryRevision, owners))
                        {
                            // WindowUpdate may republish the directory while this menu is open.
                            // Never bless its captured row with the newer revision.
                            ownerStart = 0;
                            ownerExpectedDirectoryRevision = 0;
                            selectedOwnerValidatedDirectoryRevision = 0;
                            ResetSelectedOwnerValidation();
                            return;
                        }
                        string before = OwnerKey(session.selectedOwnerHandle);
                        session.SelectOwner(captured);
                        selectedOwner = captured;
                        preferredOwnerId = string.Empty;
                        ownerWalkHandle = null;
                        preferredFallback = null;
                        ownerStart = 0;
                        ownerExpectedDirectoryRevision = capturedDirectoryRevision;
                        ResetSelectedOwnerValidation();
                        if (!string.Equals(before, OwnerKey(session.selectedOwnerHandle),
                                StringComparison.Ordinal)) ResetOwnerQueries(captured);
                        selectedOwnerValidatedDirectoryRevision = capturedDirectoryRevision;
                    }));
            }
            if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawCulturePanel(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            float devWidth = Prefs.DevMode ? Mathf.Min(180f, inner.width * 0.34f) : 0f;
            float textWidth = Mathf.Max(40f, inner.width - (devWidth > 0f ? devWidth + 6f : 0f));
            Widgets.Label(new Rect(inner.x, inner.y, textWidth, 22f),
                cachedCultureTitle);
            Widgets.Label(new Rect(inner.x, inner.y + 22f, textWidth, 22f), cachedCultureOrigin);
            if (cachedCultureHasAdopted)
                Widgets.Label(new Rect(inner.x, inner.y + 43f, textWidth, 22f),
                    cachedCultureAdopted);
            float explanationY = cachedCultureHasAdopted ? inner.y + 66f : inner.y + 44f;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inner.x, explanationY, textWidth,
                Mathf.Max(22f, inner.yMax - explanationY)),
                cachedCultureExplanation);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            if (Prefs.DevMode && Widgets.ButtonText(
                    new Rect(inner.xMax - devWidth, inner.y, devWidth, 28f),
                    loreExpanded
                        ? T("PawnDiary.Memory.Library.HideCultureDiagnostics")
                        : T("PawnDiary.Memory.Library.ShowCultureDiagnostics")))
            {
                loreExpanded = !loreExpanded;
                if (loreExpanded)
                {
                    session.narrowDetailOpen = true;
                    loreScroll = Vector2.zero;
                }
            }
            TooltipHandler.TipRegion(rect, cachedCultureExplanation);
        }

        private float DrawViewAndSearch(Rect rect, float gap)
        {
            bool activeVisible = MemoryLibraryUiPolicy.HasActiveViews(selectedOwner);
            bool importedVisible = MemoryLibraryUiPolicy.HasImportedViewContent(selectedOwner);
            int tabCount = (activeVisible ? 2 : 0) + (importedVisible ? 1 : 0);
            float tabArea = Mathf.Min(rect.width * 0.62f,
                tabCount * 160f + Math.Max(0, tabCount - 1) * gap);
            float tabWidth = tabCount <= 0 ? 0f : Mathf.Clamp(
                (tabArea - Math.Max(0, tabCount - 1) * gap) / tabCount, 88f, 160f);
            float nextX = rect.x;
            if (activeVisible)
            {
                DrawViewButton(new Rect(nextX, rect.y, tabWidth, rect.height),
                    MemoryLibraryViews.Threads, T("PawnDiary.Memory.Library.Threads"), true);
                nextX += tabWidth + gap;
                DrawViewButton(new Rect(nextX, rect.y, tabWidth, rect.height),
                    MemoryLibraryViews.Standalone,
                    T("PawnDiary.Memory.Library.Standalone"), true);
                nextX += tabWidth + gap;
            }
            if (importedVisible)
            {
                DrawViewButton(new Rect(nextX, rect.y, tabWidth, rect.height),
                    MemoryLibraryViews.Imported, T("PawnDiary.Memory.Library.Imported"), true);
                nextX += tabWidth + gap;
            }

            Rect search = new Rect(nextX, rect.y,
                Mathf.Max(80f, rect.xMax - nextX), rect.height);
            string before = session.memorySearch ?? string.Empty;
            string after = MemoryLibraryPolicy.ClampScalars(
                MemoryLibraryPolicy.RepairMalformedUtf16(
                    Widgets.TextField(search, before) ?? string.Empty),
                detachedSearchScalarCap, detachedSearchUtf16Cap);
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                session.memorySearch = after;
                ResetListQuery();
            }
            TooltipHandler.TipRegion(search, T("PawnDiary.Memory.Library.SearchTip"));
            return rect.yMax;
        }

        private void DrawViewButton(Rect rect, string view, string label, bool available)
        {
            bool prior = GUI.enabled;
            GUI.enabled = prior && available;
            if (Widgets.ButtonText(rect, session.selectedView == view ? "• " + label : label)
                && session.selectedView != view)
            {
                session.SelectView(view);
                selectedBlockRow = null;
                selectedImportedRow = null;
                session.feedbackStatus = string.Empty;
                ResetListQuery();
            }
            GUI.enabled = prior;
        }

        private float DrawFilters(Rect rect, float gap)
        {
            float width = Mathf.Max(96f, (rect.width - gap * 3f) / 4f);
            Rect importance = new Rect(rect.x, rect.y, width, rect.height);
            if (Widgets.ButtonText(importance, T("PawnDiary.Memory.Library.FilterImportance",
                    ImportanceFilterLabel(session.filters.importanceMask))))
            {
                session.filters.importanceMask = NextImportance(session.filters.importanceMask);
                ResetListQuery();
            }
            Rect category = new Rect(importance.xMax + gap, rect.y, width, rect.height);
            if (Widgets.ButtonText(category, T("PawnDiary.Memory.Library.FilterCategory",
                    CategoryLabel(session.filters.categoryMask))))
            {
                session.filters.categoryMask = NextCategory(session.filters.categoryMask);
                ResetListQuery();
            }
            Rect state = new Rect(category.xMax + gap, rect.y, width, rect.height);
            if (Widgets.ButtonText(state, T("PawnDiary.Memory.Library.FilterState",
                    StateFilterLabel(session.filters.stateToken))))
            {
                session.filters.stateToken = NextState(session.filters.stateToken);
                ResetListQuery();
            }
            Rect sort = new Rect(state.xMax + gap, rect.y,
                Mathf.Max(70f, rect.xMax - state.xMax - gap), rect.height);
            if (Widgets.ButtonText(sort, T("PawnDiary.Memory.Library.Sort",
                    session.sortToken == "oldest"
                        ? T("PawnDiary.Memory.Library.Oldest")
                        : T("PawnDiary.Memory.Library.Newest"))))
            {
                session.sortToken = session.sortToken == "oldest" ? "newest" : "oldest";
                ResetListQuery();
            }
            return rect.yMax;
        }

        private float DrawImportedSort(Rect rect)
        {
            if (Widgets.ButtonText(rect, T("PawnDiary.Memory.Library.Sort",
                    session.sortToken == "oldest"
                        ? T("PawnDiary.Memory.Library.Oldest")
                        : T("PawnDiary.Memory.Library.Newest"))))
            {
                session.sortToken = session.sortToken == "oldest" ? "newest" : "oldest";
                ResetListQuery();
            }
            return rect.yMax;
        }

        private void DrawListPane(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(6f);
            float footerHeight = 30f;
            Rect rowsRect = new Rect(inner.x, inner.y, inner.width,
                Mathf.Max(0f, inner.height - footerHeight - 4f));

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
                DrawCenteredState(rowsRect, ListEmptyText());
            }
            else
            {
                DrawVirtualizedList(rowsRect);
            }
            DrawPageFooter(new Rect(inner.x, rowsRect.yMax + 4f, inner.width, footerHeight),
                list.returnedStart, list.returnedCount, list.totalMatchedRows,
                list.hasPrevious, list.hasMore,
                delegate
                {
                    listStart = Math.Max(0, list.returnedStart - PageSize);
                    listExpectedSnapshotRevision = list.listSnapshotRevision;
                    listScroll = Vector2.zero;
                },
                delegate
                {
                    listStart = list.nextStart;
                    listExpectedSnapshotRevision = list.listSnapshotRevision;
                    listScroll = Vector2.zero;
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

        private void DrawListCard(Rect rect, MemoryLibraryListRow row)
        {
            if (row == null) return;
            bool selected = row.thread != null && Same(row.thread.rootHandle, session.selectedRootHandle)
                || row.standalone != null && MemoryLibraryUiPolicy.Same(
                    row.standalone.recordHandle, session.selectedRecordHandle)
                || row.imported != null && Same(row.imported.archiveHandle,
                    session.selectedArchiveHandle);
            Widgets.DrawBoxSolid(rect, selected
                ? new Color(0.24f, 0.30f, 0.36f, 0.95f)
                : new Color(0.12f, 0.14f, 0.17f, 0.90f));
            Widgets.DrawHighlightIfMouseover(rect);
            Rect text = rect.ContractedBy(8f);
            string title;
            string details;
            if (row.thread != null)
            {
                title = EmptyFallback(row.thread.subjectLabel,
                    T("PawnDiary.Memory.Library.UnknownSubject"));
                details = T("PawnDiary.Memory.Library.ThreadCard",
                    DateLabel(row.thread.latestActivityTick),
                    RootTypeLabel(row.thread.subjectTypeToken), row.thread.chapterCount,
                    row.thread.manageableMemoryCount,
                    ImportanceLabel(row.thread.highestImportanceMask));
                string states = ThreadStateCounts(row.thread);
                if (states.Length > 0) details += " · " + states;
            }
            else if (row.standalone != null)
            {
                title = EmptyFallback(row.standalone.displayWording,
                    T("PawnDiary.Memory.Library.EmptyWording"));
                details = BlockBadges(row.standalone);
            }
            else
            {
                title = EmptyFallback(row.imported?.preview,
                    T("PawnDiary.Memory.Library.ImportedPreviewUnavailable"));
                details = T("PawnDiary.Memory.Library.ImportedCard",
                    DateLabel(row.imported?.originalTick ?? -1),
                    ArchiveSourceLabel(row.imported?.archiveHandle),
                    MigrationReasonLabel(row.imported?.migrationReasonToken));
            }
            Widgets.Label(new Rect(text.x, text.y, text.width, 40f), title);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(text.x, text.yMax - 22f, text.width, 20f), details);
            GUI.color = Color.white;
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

        private void DrawDetailPane(Rect rect, bool showBack)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            if (showBack)
            {
                if (Widgets.ButtonText(new Rect(inner.x, inner.y, 120f, 28f),
                    T("PawnDiary.Memory.Library.Back")))
                {
                    session.narrowDetailOpen = false;
                    return;
                }
                inner.yMin += 34f;
            }
            if (Prefs.DevMode && loreExpanded)
            {
                DrawLoreSection(inner);
                return;
            }
            if (session.selectedView == MemoryLibraryViews.Imported)
                DrawImportedDetail(inner);
            else if (session.selectedRecordHandle != null)
                DrawBlockDetail(inner, !showBack);
            else if (session.selectedRootHandle != null)
                DrawThreadDetail(inner);
            else
                DrawCenteredState(inner, T("PawnDiary.Memory.Library.SelectMemory"));
        }

        private void DrawThreadDetail(Rect rect)
        {
            if (threadDetail == null || threadDetail.status != MemoryLibraryStatuses.Ready)
            {
                DrawCenteredState(rect, QueryStateText(threadDetail?.status));
                return;
            }
            float header = 78f;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 28f),
                EmptyFallback(threadDetail.header?.subjectLabel,
                    T("PawnDiary.Memory.Library.UnknownSubject")));
            Text.Font = GameFont.Tiny;
            Rect statusOut = new Rect(rect.x, rect.y + 28f, rect.width, 42f);
            float statusHeight = Mathf.Max(statusOut.height,
                Text.CalcHeight(cachedThreadHeaderText,
                    Mathf.Max(40f, statusOut.width - 16f)) + 2f);
            Rect statusView = new Rect(0f, 0f, Mathf.Max(0f, statusOut.width - 16f), statusHeight);
            Widgets.BeginScrollView(statusOut, ref currentStatusScroll, statusView);
            try { Widgets.Label(statusView, cachedThreadHeaderText); }
            finally { Widgets.EndScrollView(); }
            Text.Font = GameFont.Small;
            Rect rowsRect = new Rect(rect.x, rect.y + header, rect.width,
                Mathf.Max(0f, rect.height - header - 32f));
            DrawVirtualizedBlocks(rowsRect, threadDetail.blocks);
            DrawDetailPaging(new Rect(rect.x, rowsRect.yMax + 3f, rect.width, 29f));
        }

        private void DrawVirtualizedBlocks(Rect outRect, List<MemoryBlockRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                DrawCenteredState(outRect, threadDetail?.allBlocksSuppressedForWriting == true
                    ? T("PawnDiary.Memory.Library.AllSuppressed")
                    : T("PawnDiary.Memory.Library.NoBlocks"));
                return;
            }
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            float rowHeight = Mathf.Max(76f, style.memoryLibraryBlockCardHeight);
            MemoryLibraryUiVirtualWindow window = MemoryLibraryUiPolicy.Virtualize(
                rows.Count, detailScroll.y, outRect.height, rowHeight,
                style.memoryLibraryOverscanRows, style.memoryLibraryMaximumMaterializedRows);
            Rect view = new Rect(0f, 0f, Mathf.Max(0f, outRect.width - 16f),
                Mathf.Max(outRect.height, window.contentHeight));
            Widgets.BeginScrollView(outRect, ref detailScroll, view);
            try
            {
                for (int index = window.firstIndex; index < window.endExclusive; index++)
                {
                    MemoryBlockRow row = rows[index];
                    Rect card = new Rect(0f, index * rowHeight, view.width, rowHeight - 4f);
                    bool selected = MemoryLibraryUiPolicy.Same(row?.recordHandle,
                        session.selectedRecordHandle);
                    Widgets.DrawBoxSolid(card, selected
                        ? new Color(0.24f, 0.30f, 0.36f, 0.95f)
                        : new Color(0.12f, 0.14f, 0.17f, 0.90f));
                    Widgets.DrawHighlightIfMouseover(card);
                    Rect text = card.ContractedBy(7f);
                    bool chapterStart = row?.rollingSummary == true || index == 0
                        || !string.Equals(rows[index - 1]?.chapterId,
                            row?.chapterId, StringComparison.Ordinal);
                    float wordingY = text.y;
                    if (chapterStart)
                    {
                        Text.Font = GameFont.Tiny;
                        GUI.color = Color.gray;
                        Widgets.Label(new Rect(text.x, text.y, text.width, 18f),
                            row?.rollingSummary == true
                                ? SummaryRoleLabel(row) : ChapterLabel(row?.chapterId));
                        GUI.color = Color.white;
                        Text.Font = GameFont.Small;
                        wordingY += 18f;
                    }
                    Widgets.Label(new Rect(text.x, wordingY, text.width,
                            Mathf.Max(24f, text.yMax - wordingY - 22f)),
                        EmptyFallback(row?.displayWording,
                            T("PawnDiary.Memory.Library.EmptyWording")));
                    Text.Font = GameFont.Tiny;
                    GUI.color = Color.gray;
                    Widgets.Label(new Rect(text.x, text.yMax - 22f, text.width, 20f),
                        BlockBadges(row));
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    if (Widgets.ButtonInvisible(card))
                    {
                        session.selectedRecordHandle = MemoryLibraryUiPolicy.Copy(row?.recordHandle);
                        session.selectedPlacementToken = string.Empty;
                        session.editDraft = null;
                        session.feedbackStatus = string.Empty;
                        selectedBlockRow = row;
                        selectedImportedRow = null;
                        diagnosticsExpanded = false;
                        ResetDetailQuery();
                    }
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawBlockDetail(Rect rect, bool showInternalBack)
        {
            if (blockDetail == null || blockDetail.status != MemoryLibraryStatuses.Ready
                || blockDetail.row == null)
            {
                DrawCenteredState(rect, QueryStateText(blockDetail?.status));
                return;
            }
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            MemoryBlockRow row = blockDetail.row;
            float contentWidth = Mathf.Max(40f, rect.width - 16f);
            string wording = EmptyFallback(
                row.displayWording, T("PawnDiary.Memory.Library.EmptyWording"));
            string role = SummaryRoleLabel(row);
            string meta = T("PawnDiary.Memory.Library.BlockDetailMeta",
                DateLabel(row.originalTick), ContentCategoryLabel(row.projectedCategoryMask),
                ImportanceLabel(row.projectedHighestImportanceMask), LifetimeLabel(row), role);
            Text.Font = GameFont.Small;
            float wordingHeight = Mathf.Max(50f, Text.CalcHeight(wording, contentWidth) + 4f);
            float factsHeight = Mathf.Max(52f,
                Text.CalcHeight(cachedBlockFactsText, contentWidth) + 4f);
            string recordKey = RecordKey(row.recordHandle);
            string usage = recordKey.Length > 0
                && cachedUsageLabels.TryGetValue(recordKey, out string cachedUsage)
                    ? cachedUsage : BuildUsageFacts(row);
            float usageHeight = Mathf.Max(58f, Text.CalcHeight(usage, contentWidth) + 4f);
            Text.Font = GameFont.Tiny;
            float metaHeight = Mathf.Max(42f, Text.CalcHeight(meta, contentWidth) + 4f);
            Text.Font = GameFont.Small;
            float tailHeight;
            if (session.editDraft != null)
            {
                tailHeight = 180f;
                if (!string.IsNullOrEmpty(session.feedbackStatus)) tailHeight += 26f;
                if (session.editDraft.structuralConflict) tailHeight += 44f;
            }
            else
            {
                float diagnosticsHeight = 0f;
                if (Prefs.DevMode)
                    diagnosticsHeight = diagnosticsExpanded
                        ? Mathf.Max(100f,
                            Text.CalcHeight(cachedDiagnosticText, contentWidth) + 68f)
                        : 32f;
                tailHeight = 34f + 26f + usageHeight + 4f + diagnosticsHeight;
            }
            float contentHeight = (showInternalBack ? 34f : 0f)
                + wordingHeight + 2f + metaHeight + 4f + factsHeight + 4f + tailHeight + 8f;
            Rect view = new Rect(0f, 0f, contentWidth,
                Mathf.Max(rect.height,
                    Mathf.Max(style.memoryLibraryBlockDetailMinimumContentHeight, contentHeight)));
            Widgets.BeginScrollView(rect, ref blockDetailScroll, view);
            try
            {
                float y = view.y;
                if (showInternalBack)
                {
                    if (Widgets.ButtonText(new Rect(view.x, y, 120f, 28f),
                        T("PawnDiary.Memory.Library.Back")))
                    {
                        session.selectedRecordHandle = null;
                        session.editDraft = null;
                        session.feedbackStatus = string.Empty;
                        blockDetail = null;
                        selectedBlockRow = null;
                        diagnosticsExpanded = false;
                        return;
                    }
                    y += 34f;
                }
                Widgets.Label(new Rect(view.x, y, view.width, wordingHeight), wording);
                y += wordingHeight + 2f;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(view.x, y, view.width, metaHeight), meta);
                Text.Font = GameFont.Small;
                y += metaHeight + 4f;

                DrawNormalFacts(new Rect(view.x, y, view.width, factsHeight), row);
                y += factsHeight + 4f;

                if (session.editDraft != null)
                {
                    DrawEditDraft(new Rect(view.x, y, view.width,
                        Mathf.Max(180f, view.yMax - y)), row);
                    return;
                }

                float buttonGap = 5f;
                float buttonWidth = Mathf.Max(82f, (view.width - buttonGap * 2f) / 3f);
                bool available = MutationsAvailable()
                    && pendingCommandId == 0 && session.pendingCommand == null;
                Rect suppress = new Rect(view.x, y, buttonWidth, 28f);
                if (Widgets.ButtonText(suppress, row.suppressed
                        ? T("PawnDiary.Memory.Library.UseAgain")
                        : T("PawnDiary.Memory.Library.Suppress"), true, true,
                        available && row.canSuppress))
                    StageBlockAction(MemoryLibraryActions.SetSuppressed, !row.suppressed, false);
                Rect edit = new Rect(suppress.xMax + buttonGap, y, buttonWidth, 28f);
                if (Widgets.ButtonText(edit, T("PawnDiary.Memory.Library.Edit"), true, true,
                        available && row.canSaveWording))
                    session.editDraft = MemoryLibraryUiPolicy.BeginEdit(blockDetail,
                        detachedTextCap);
                if (row.rollingSummary)
                    TooltipHandler.TipRegion(edit,
                        T("PawnDiary.Memory.Library.SummaryChanging"));
                Rect original = new Rect(edit.xMax + buttonGap, y,
                    Mathf.Max(70f, view.xMax - edit.xMax - buttonGap), 28f);
                if (Widgets.ButtonText(original, T("PawnDiary.Memory.Library.UseOriginal"),
                        true, true, available && row.canUseOriginal))
                    StageUseOriginal(row);
                y += 34f;

                DrawFeedback(new Rect(view.x, y, view.width, 24f));
                y += 26f;
                DrawUsageFacts(new Rect(view.x, y, view.width, usageHeight), row);
                y += usageHeight + 4f;
                if (Prefs.DevMode)
                    DrawDevDiagnostics(new Rect(view.x, y, view.width,
                        Mathf.Max(32f, view.yMax - y)), row);
            }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawEditDraft(Rect rect, MemoryBlockRow row)
        {
            MemoryLibraryUiEditDraft draft = session.editDraft;
            if (draft == null) return;
            float y = rect.y;
            if (!string.IsNullOrEmpty(session.feedbackStatus))
            {
                DrawFeedback(new Rect(rect.x, y, rect.width, 24f));
                y += 26f;
            }
            if (draft.structuralConflict)
            {
                GUI.color = Color.yellow;
                Widgets.Label(new Rect(rect.x, y, rect.width, 42f),
                    T("PawnDiary.Memory.Library.EditConflict"));
                GUI.color = Color.white;
                y += 44f;
            }
            float buttons = 34f;
            Rect area = new Rect(rect.x, y, rect.width,
                Mathf.Max(70f, rect.yMax - y - buttons));
            string edited = Widgets.TextArea(area, draft.text ?? string.Empty) ?? string.Empty;
            draft.text = MemoryLibraryPolicy.ClampUtf16CompleteScalar(edited, detachedTextCap);
            y = area.yMax + 5f;
            float width = Mathf.Max(84f, (rect.width - 10f) / 3f);
            bool canSave = MutationsAvailable()
                && pendingCommandId == 0 && session.pendingCommand == null
                && !string.IsNullOrWhiteSpace(draft.text);
            if (Widgets.ButtonText(new Rect(rect.x, y, width, 28f),
                    T("PawnDiary.Memory.Library.Save"), true, true, canSave))
            {
                long commandId = AllocateCommandId();
                if (commandId > 0) session.StageCommand(MemoryLibraryUiPolicy.BuildBlockCommand(
                    clientToken, commandId, MemoryLibraryActions.SaveWording,
                    row, false, draft));
            }
            if (Widgets.ButtonText(new Rect(rect.x + width + 5f, y, width, 28f),
                T("PawnDiary.Memory.Library.Cancel"))) session.editDraft = null;
        }

        private void DrawUsageFacts(Rect rect, MemoryBlockRow row)
        {
            string key = RecordKey(row?.recordHandle);
            Widgets.Label(rect, key.Length > 0
                && cachedUsageLabels.TryGetValue(key, out string cached)
                    ? cached : BuildUsageFacts(row));
        }

        private void DrawNormalFacts(Rect rect, MemoryBlockRow row)
        {
            Widgets.Label(rect, cachedBlockFactsText);
        }

        private string BuildNormalFactsText()
        {
            string source = string.IsNullOrWhiteSpace(blockDetail?.detail?.sourcePageLinkToken)
                ? T("PawnDiary.Memory.Library.SourceUnavailable")
                : T("PawnDiary.Memory.Library.SourceDiaryPage");
            return T("PawnDiary.Memory.Library.ReadOnlyFacts",
                Join(blockDetail?.detail?.factDescriptors), source);
        }

        private void DrawDevDiagnostics(Rect rect, MemoryBlockRow row)
        {
            Rect toggle = new Rect(rect.x, rect.y, rect.width, 28f);
            if (Widgets.ButtonText(toggle, diagnosticsExpanded
                    ? T("PawnDiary.Memory.Library.HideDiagnostics")
                    : T("PawnDiary.Memory.Library.ShowDiagnostics")))
                diagnosticsExpanded = !diagnosticsExpanded;
            if (!diagnosticsExpanded) return;
            string facts = cachedDiagnosticText;
            Rect textRect = new Rect(rect.x, toggle.yMax + 4f, rect.width,
                Mathf.Max(34f, rect.height - 68f));
            Widgets.Label(textRect, facts);
            Rect copy = new Rect(rect.x, rect.yMax - 30f, 120f, 28f);
            if (Widgets.ButtonText(copy, T("PawnDiary.Memory.Library.CopyDiagnostics")))
                GUIUtility.systemCopyBuffer = facts;
            Rect forget = new Rect(copy.xMax + 6f, copy.y, 120f, 28f);
            if (Widgets.ButtonText(forget, T("PawnDiary.Memory.Library.Forget"),
                    true, true, MutationsAvailable()
                        && pendingCommandId == 0 && row.canDevForget))
                ConfirmForget(row);
        }

        private void DrawImportedDetail(Rect rect)
        {
            if (session.selectedArchiveHandle == null)
            {
                if (compatibility?.status == MemoryLibraryStatuses.Ready
                    && compatibility.pending != null)
                {
                    Widgets.Label(rect, T("PawnDiary.Memory.Library.CompatibilityPreview",
                        compatibility.pending.safePreview,
                        compatibility.pending.rowCount,
                        compatibility.pending.logicalByteCount));
                }
                else DrawCenteredState(rect, T("PawnDiary.Memory.Library.SelectMemory"));
                return;
            }
            if (importedDetail == null || importedDetail.status != MemoryLibraryStatuses.Ready)
            {
                DrawCenteredState(rect, QueryStateText(importedDetail?.status));
                return;
            }
            MemoryImportedRow selectedImported = FindSelectedImportedRow();
            float footer = Prefs.DevMode ? 68f : 34f;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 42f),
                T("PawnDiary.Memory.Library.ImportedDetailMeta",
                    ArchiveSourceLabel(selectedImported?.archiveHandle),
                    MigrationReasonLabel(selectedImported?.migrationReasonToken)));
            Rect textOut = new Rect(rect.x, rect.y + 44f, rect.width,
                Mathf.Max(40f, rect.height - footer - 44f));
            string text = importedDetail.textChunk ?? string.Empty;
            float height = Mathf.Max(textOut.height,
                Text.CalcHeight(text, Mathf.Max(40f, textOut.width - 16f)) + 8f);
            Rect view = new Rect(0f, 0f, Mathf.Max(0f, textOut.width - 16f), height);
            Widgets.BeginScrollView(textOut, ref detailScroll, view);
            try { Widgets.Label(view, text); }
            finally { Widgets.EndScrollView(); }
            Rect page = new Rect(rect.x, textOut.yMax + 4f, rect.width, 28f);
            DrawTextPaging(page);
            if (Prefs.DevMode)
            {
                MemoryImportedRow row = selectedImported;
                if (Widgets.ButtonText(new Rect(rect.x, page.yMax + 4f, 140f, 28f),
                    T("PawnDiary.Memory.Library.Forget"), true, true,
                    MutationsAvailable()
                        && pendingCommandId == 0 && row != null)) ConfirmForgetImported(row);
            }
        }

        private void DrawCompatibilityOnly(Rect rect)
        {
            if (compatibility?.status != MemoryLibraryStatuses.Ready
                || compatibility.pending == null)
            {
                DrawCenteredState(rect, QueryStateText(compatibility?.status));
                return;
            }
            Widgets.Label(rect, T("PawnDiary.Memory.Library.CompatibilityPreview",
                compatibility.pending.safePreview, compatibility.pending.rowCount,
                compatibility.pending.logicalByteCount));
        }

        private void DrawLoreSection(Rect rect)
        {
            if (!Prefs.DevMode) return;
            if (Widgets.ButtonText(new Rect(rect.x, rect.y, rect.width, 28f), loreExpanded
                    ? T("PawnDiary.Memory.Library.HideCultureDiagnostics")
                    : T("PawnDiary.Memory.Library.ShowCultureDiagnostics")))
                loreExpanded = !loreExpanded;
            if (!loreExpanded || lore == null) return;
            string details = Dialog_PawnWritingStyle.LoreStateText(lore);
            if (lore.topics != null && lore.topics.Count > 0)
            {
                selectedLoreTopicIndex = Mathf.Clamp(
                    selectedLoreTopicIndex, 0, lore.topics.Count - 1);
                LoreMemoryTopicForDev topic = lore.topics[selectedLoreTopicIndex];
                Rect previous = new Rect(rect.x, rect.y + 32f, 90f, 28f);
                Rect next = new Rect(rect.xMax - 90f, previous.y, 90f, 28f);
                if (Widgets.ButtonText(previous, T("PawnDiary.Memory.Library.Previous"),
                        true, true, selectedLoreTopicIndex > 0))
                {
                    selectedLoreTopicIndex--;
                    loreScroll = Vector2.zero;
                    topic = lore.topics[selectedLoreTopicIndex];
                }
                if (Widgets.ButtonText(next, T("PawnDiary.Memory.Library.Next"),
                        true, true, selectedLoreTopicIndex + 1 < lore.topics.Count))
                {
                    selectedLoreTopicIndex++;
                    loreScroll = Vector2.zero;
                    topic = lore.topics[selectedLoreTopicIndex];
                }
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(previous.xMax + 4f, previous.y,
                    Mathf.Max(0f, next.x - previous.xMax - 8f), 28f),
                    T("PawnDiary.Memory.Library.Position",
                        selectedLoreTopicIndex + 1, lore.topics.Count));
                Text.Anchor = TextAnchor.UpperLeft;
                details += "\n\n" + Dialog_PawnWritingStyle.LoreTopicLabel(topic)
                    + "\n" + Dialog_PawnWritingStyle.LoreTopicDetails(lore, topic);
            }
            details += "\n\n" + Dialog_PawnWritingStyle.LoreLastMatchText(lore);
            float navigation = lore.topics != null && lore.topics.Count > 0 ? 32f : 0f;
            Rect output = new Rect(rect.x, rect.y + 32f + navigation, rect.width,
                Mathf.Max(40f, rect.height - 32f - navigation));
            float height = Mathf.Max(output.height,
                Text.CalcHeight(details, Mathf.Max(40f, output.width - 16f)) + 4f);
            Widgets.BeginScrollView(output, ref loreScroll,
                new Rect(0f, 0f, Mathf.Max(0f, output.width - 16f), height));
            try { Widgets.Label(new Rect(0f, 0f, output.width - 16f, height), details); }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawDetailPaging(Rect rect)
        {
            if (threadDetail == null) return;
            DrawPageFooter(rect, threadDetail.returnedStart, threadDetail.returnedCount,
                threadDetail.shownManageableCount, threadDetail.hasPrevious, threadDetail.hasMore,
                delegate
                {
                    detailStart = Math.Max(0, threadDetail.returnedStart - PageSize);
                    detailExpectedSnapshotRevision = threadDetail.detailSnapshotRevision;
                    detailScroll = Vector2.zero;
                },
                delegate
                {
                    detailStart = threadDetail.nextStart;
                    detailExpectedSnapshotRevision = threadDetail.detailSnapshotRevision;
                    detailScroll = Vector2.zero;
                });
        }

        private void DrawTextPaging(Rect rect)
        {
            if (importedDetail == null) return;
            DrawPageFooter(rect, importedDetail.returnedTextStart,
                (importedDetail.textChunk ?? string.Empty).Length, importedDetail.totalTextLength,
                importedDetail.hasPrevious, importedDetail.hasMore,
                delegate
                {
                    importedTextStart = importedDetail.previousTextStart;
                    importedTextExpectedSnapshotRevision = importedDetail.archiveTextSnapshotRevision;
                    detailScroll = Vector2.zero;
                },
                delegate
                {
                    importedTextStart = importedDetail.nextTextStart;
                    importedTextExpectedSnapshotRevision = importedDetail.archiveTextSnapshotRevision;
                    detailScroll = Vector2.zero;
                });
        }

        private void DrawPageFooter(Rect rect, int start, int count, int total,
            bool hasPrevious, bool hasMore, Action previous, Action next)
        {
            float width = Mathf.Min(90f, rect.width * 0.23f);
            if (Widgets.ButtonText(new Rect(rect.x, rect.y, width, rect.height),
                    T("PawnDiary.Memory.Library.Previous"), true, true, hasPrevious)) previous();
            if (Widgets.ButtonText(new Rect(rect.xMax - width, rect.y, width, rect.height),
                    T("PawnDiary.Memory.Library.Next"), true, true, hasMore)) next();
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.x + width + 4f, rect.y,
                Mathf.Max(0f, rect.width - width * 2f - 8f), rect.height),
                T("PawnDiary.Memory.Library.Showing", Math.Min(total, start + 1),
                    Math.Min(total, start + count), total));
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void StageBlockAction(string action, bool desiredSuppressed, bool force)
        {
            MemoryBlockRow row = blockDetail?.row;
            long commandId = AllocateCommandId();
            if (row == null || commandId <= 0) return;
            session.StageCommand(MemoryLibraryUiPolicy.BuildBlockCommand(
                clientToken, commandId, action, row, desiredSuppressed, null));
        }

        private void StageUseOriginal(MemoryBlockRow row)
        {
            Action stage = delegate
            {
                long id = AllocateCommandId();
                if (id > 0) session.StageCommand(MemoryLibraryUiPolicy.BuildBlockCommand(
                    clientToken, id, MemoryLibraryActions.UseOriginalWording,
                    row, false, null));
            };
            if (MemoryLibraryUiPolicy.PastNormalLifetime(row, detachedNowTick,
                    detachedMinorLifetimeTicks, detachedRegularLifetimeTicks))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    T("PawnDiary.Memory.Library.UseOriginalWarning"), stage, true,
                    T("PawnDiary.Memory.Library.UseOriginal")));
            }
            else stage();
        }

        private void ConfirmForget(MemoryBlockRow row)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                T("PawnDiary.Memory.Library.ForgetWarning"), delegate
                {
                    long id = AllocateCommandId();
                    if (id > 0) session.StageCommand(MemoryLibraryUiPolicy.BuildBlockCommand(
                        clientToken, id, MemoryLibraryActions.ForgetPermanent,
                        row, false, null));
                }, true, T("PawnDiary.Memory.Library.Forget")));
        }

        private void ConfirmForgetImported(MemoryImportedRow row)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                T("PawnDiary.Memory.Library.ForgetWarning"), delegate
                {
                    long id = AllocateCommandId();
                    if (id > 0) session.StageCommand(
                        MemoryLibraryUiPolicy.BuildImportedForgetCommand(clientToken, id, row));
                }, true, T("PawnDiary.Memory.Library.Forget")));
        }

        private void DrawFeedback(Rect rect)
        {
            if (string.IsNullOrEmpty(session.feedbackStatus)) return;
            GUI.color = session.feedbackStatus == MemoryLibraryCommandStatuses.Success
                ? Color.green : Color.yellow;
            Widgets.Label(rect, CommandStatusText(session.feedbackStatus));
            GUI.color = Color.white;
        }

        private string OwnerEmptyText()
        {
            if (!string.IsNullOrWhiteSpace(preferredOwnerId)
                || owners?.status == MemoryLibraryStatuses.Preparing)
                return T("PawnDiary.Memory.Library.Preparing");
            return owners?.ownerEmptyStateToken == "no_matches"
                ? T("PawnDiary.Memory.Library.NoOwnerMatches")
                : T("PawnDiary.Memory.Library.NoOwners");
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

        private static string QueryStateText(string status)
        {
            if (status == MemoryLibraryStatuses.Missing)
                return T("PawnDiary.Memory.Library.Missing");
            if (status == MemoryLibraryStatuses.Invalid)
                return T("PawnDiary.Memory.Library.Invalid");
            if (status == MemoryLibraryStatuses.Stale)
                return T("PawnDiary.Memory.Library.Refreshing");
            return T("PawnDiary.Memory.Library.Preparing");
        }

        private static void DrawCenteredState(Rect rect, string value)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            Widgets.Label(rect, value ?? string.Empty);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static string CultureLine(string state, string label, string provenance)
        {
            string display = CultureStateLabel(state, label, true);
            return provenance == "none" || string.IsNullOrWhiteSpace(provenance)
                ? T("PawnDiary.Memory.Library.CultureOriginNoProvenance", display)
                : T("PawnDiary.Memory.Library.CultureOrigin", display,
                    CultureProvenanceLabel(provenance));
        }

        private static string LifecycleLabel(string token)
        {
            if (token == "active") return T("PawnDiary.Memory.Library.OwnerActive");
            if (token == "archive" || token == "saved")
                return T("PawnDiary.Memory.Library.OwnerDeparted");
            if (token == "migration_pending")
                return T("PawnDiary.Memory.Library.OwnerMigrationPending");
            return T("PawnDiary.Memory.Library.OwnerUnknown");
        }

        private static string CultureStateLabel(string state, string label, bool origin)
        {
            if (!string.IsNullOrWhiteSpace(label)) return label;
            if (state == "none") return origin
                ? T("PawnDiary.Memory.Library.CultureUnknown")
                : T("PawnDiary.Memory.Library.CultureNone");
            if (state == "recorded" || state == "resolved")
                return T("PawnDiary.Memory.Library.CultureRecorded");
            if (state == "inferred") return T("PawnDiary.Memory.Library.CultureInferred");
            if (state == "unavailable") return T("PawnDiary.Memory.Library.CultureUnavailable");
            return origin ? T("PawnDiary.Memory.Library.CultureUnknown")
                : T("PawnDiary.Memory.Library.CultureNone");
        }

        private static string CultureProvenanceLabel(string token)
        {
            if (token == "recorded" || token == "captured")
                return T("PawnDiary.Memory.Library.CultureRecorded");
            if (token == "inferred") return T("PawnDiary.Memory.Library.CultureInferred");
            if (token == "unknown") return T("PawnDiary.Memory.Library.CultureUnknown");
            return token == "none"
                ? T("PawnDiary.Memory.Library.CultureNone")
                : T("PawnDiary.Memory.Library.CultureUnavailable");
        }

        private string LifetimeLabel(MemoryBlockRow row)
        {
            string key = RecordKey(row?.recordHandle);
            if (key.Length > 0 && cachedLifetimeLabels.TryGetValue(key, out string cached))
                return cached;
            return BuildLifetimeLabel(row);
        }

        private string BuildLifetimeLabel(MemoryBlockRow row)
        {
            MemoryLibraryUiLifetime life = MemoryLibraryUiPolicy.Lifetime(row, detachedNowTick,
                detachedMinorLifetimeTicks, detachedRegularLifetimeTicks);
            if (life.stateToken == MemoryLibraryUiLifetimeTokens.Protected)
                return T("PawnDiary.Memory.Library.LifetimeProtected");
            if (life.stateToken == MemoryLibraryUiLifetimeTokens.Important)
                return T("PawnDiary.Memory.Library.LifetimeImportant");
            if (life.stateToken == MemoryLibraryUiLifetimeTokens.Due)
                return T("PawnDiary.Memory.Library.LifetimeDue");
            if (life.stateToken == MemoryLibraryUiLifetimeTokens.Mixed)
                return T("PawnDiary.Memory.Library.LifetimeMixed",
                    life.expiryTick == long.MaxValue
                        ? T("PawnDiary.Memory.Library.DateUnknown")
                        : DateLabel(life.expiryTick));
            if (life.stateToken == MemoryLibraryUiLifetimeTokens.Unknown)
                return T("PawnDiary.Memory.Library.LifetimeUnknown");
            long days = Math.Max(1, (life.remainingTicks + 59999L) / 60000L);
            return life.stateToken == MemoryLibraryUiLifetimeTokens.Regular
                ? T("PawnDiary.Memory.Library.LifetimeRegular", days)
                : T("PawnDiary.Memory.Library.LifetimeMinor", days);
        }

        private string BlockBadges(MemoryBlockRow row)
        {
            string key = RecordKey(row?.recordHandle);
            if (key.Length > 0 && cachedBlockBadges.TryGetValue(key, out string cached))
                return cached;
            return BuildBlockBadges(row);
        }

        private string BuildBlockBadges(MemoryBlockRow row)
        {
            if (row == null) return string.Empty;
            List<string> badges = new List<string>
            {
                DateLabel(row.originalTick),
                ContentCategoryLabel(row.projectedCategoryMask),
                ImportanceLabel(row.projectedHighestImportanceMask),
                LifetimeLabel(row)
            };
            if (row.playerEdited) badges.Add(T("PawnDiary.Memory.Library.Edited"));
            if (row.suppressed) badges.Add(T("PawnDiary.Memory.Library.Suppressed"));
            if (row.rollingSummary || row.closedSummary) badges.Add(SummaryRoleLabel(row));
            if ((row.projectedCategoryMask & ~detachedCategoryMask) != 0)
                badges.Add(T("PawnDiary.Memory.Library.CategoryDisabled"));
            return string.Join(" · ", badges.ToArray());
        }

        private static string ImportanceLabel(int mask)
        {
            if ((mask & MemoryLibraryPolicy.ImportanceImportant) != 0)
                return T("PawnDiary.Memory.Library.ImportanceImportant");
            if ((mask & MemoryLibraryPolicy.ImportanceRegular) != 0)
                return T("PawnDiary.Memory.Library.ImportanceRegular");
            if ((mask & MemoryLibraryPolicy.ImportanceMinor) != 0)
                return T("PawnDiary.Memory.Library.ImportanceMinor");
            return T("PawnDiary.Memory.Library.All");
        }

        private static string ImportanceFilterLabel(int mask)
        {
            return mask == 0 ? T("PawnDiary.Memory.Library.All") : ImportanceLabel(mask);
        }

        private static int NextImportance(int value)
        {
            if (value == 0) return MemoryLibraryPolicy.ImportanceMinor;
            if (value == MemoryLibraryPolicy.ImportanceMinor) return MemoryLibraryPolicy.ImportanceRegular;
            if (value == MemoryLibraryPolicy.ImportanceRegular) return MemoryLibraryPolicy.ImportanceImportant;
            return 0;
        }

        private static string CategoryLabel(int mask)
        {
            if (mask == MemoryCategoryBits.Personal) return T("PawnDiary.Memory.Library.CategoryPersonal");
            if (mask == MemoryCategoryBits.Relationships) return T("PawnDiary.Memory.Library.CategoryRelationships");
            if (mask == MemoryCategoryBits.Family) return T("PawnDiary.Memory.Library.CategoryFamily");
            if (mask == MemoryCategoryBits.Factions) return T("PawnDiary.Memory.Library.CategoryFactions");
            return T("PawnDiary.Memory.Library.All");
        }

        private static string ContentCategoryLabel(int mask)
        {
            List<string> labels = new List<string>();
            if ((mask & MemoryCategoryBits.Personal) != 0)
                labels.Add(T("PawnDiary.Memory.Library.CategoryPersonal"));
            if ((mask & MemoryCategoryBits.Relationships) != 0)
                labels.Add(T("PawnDiary.Memory.Library.CategoryRelationships"));
            if ((mask & MemoryCategoryBits.Family) != 0)
                labels.Add(T("PawnDiary.Memory.Library.CategoryFamily"));
            if ((mask & MemoryCategoryBits.Factions) != 0)
                labels.Add(T("PawnDiary.Memory.Library.CategoryFactions"));
            return labels.Count == 0
                ? T("PawnDiary.Memory.Library.All") : string.Join(", ", labels.ToArray());
        }

        private static int NextCategory(int value)
        {
            if (value == 0) return MemoryCategoryBits.Personal;
            if (value == MemoryCategoryBits.Personal) return MemoryCategoryBits.Relationships;
            if (value == MemoryCategoryBits.Relationships) return MemoryCategoryBits.Family;
            if (value == MemoryCategoryBits.Family) return MemoryCategoryBits.Factions;
            return 0;
        }

        private static string StateFilterLabel(string token)
        {
            if (token == "edited") return T("PawnDiary.Memory.Library.Edited");
            if (token == "suppressed") return T("PawnDiary.Memory.Library.Suppressed");
            if (token == "unsuppressed") return T("PawnDiary.Memory.Library.Unsuppressed");
            return T("PawnDiary.Memory.Library.All");
        }

        private static string NextState(string token)
        {
            if (token == "all") return "suppressed";
            if (token == "suppressed") return "edited";
            return "all";
        }

        private static string RootTypeLabel(string token)
        {
            if (string.Equals(token, "person", StringComparison.OrdinalIgnoreCase))
                return T("PawnDiary.Memory.Library.RootPerson");
            if (string.Equals(token, "faction", StringComparison.OrdinalIgnoreCase))
                return T("PawnDiary.Memory.Library.RootFaction");
            return T("PawnDiary.Memory.Library.RootOngoingStory");
        }

        private static string ThreadStateCounts(MemoryThreadHeaderRow row)
        {
            List<string> values = new List<string>();
            if (row != null && row.editedCount > 0)
                values.Add(T("PawnDiary.Memory.Library.EditedCount", row.editedCount));
            if (row != null && row.suppressedCount > 0)
                values.Add(T("PawnDiary.Memory.Library.SuppressedCount", row.suppressedCount));
            return values.Count == 0 ? string.Empty : string.Join(", ", values.ToArray());
        }

        private string ChapterLabel(string chapterId)
        {
            if (threadDetail?.chapters == null) return T("PawnDiary.Memory.Library.ChapterUnknown");
            for (int index = 0; index < threadDetail.chapters.Count; index++)
                if (string.Equals(threadDetail.chapters[index]?.chapterId, chapterId,
                        StringComparison.Ordinal))
                {
                    MemoryChapterRow chapter = threadDetail.chapters[index];
                    string label = T("PawnDiary.Memory.Library.Chapter",
                        Math.Max(1L, chapter.ordinal));
                    string phase = ChapterPhaseLabel(chapter.phaseToken);
                    if (phase.Length > 0) label += " · " + phase;
                    if (chapter.continuedFromPrevious)
                        label += " · " + T("PawnDiary.Memory.Library.ChapterContinued");
                    return label;
                }
            return T("PawnDiary.Memory.Library.ChapterUnknown");
        }

        private static string ChapterPhaseLabel(string token)
        {
            switch (token)
            {
                case "relationship_phase": return T("PawnDiary.Memory.Library.PhaseRelationship");
                case "family_lifecycle": return T("PawnDiary.Memory.Library.PhaseFamily");
                case "body_state": return T("PawnDiary.Memory.Library.PhaseBody");
                case "membership_state": return T("PawnDiary.Memory.Library.PhaseMembership");
                case "growth_stage": return T("PawnDiary.Memory.Library.PhaseGrowth");
                case "belief_state": return T("PawnDiary.Memory.Library.PhaseBelief");
                case "role_state": return T("PawnDiary.Memory.Library.PhaseRole");
                case "title_state": return T("PawnDiary.Memory.Library.PhaseTitle");
                case "psylink_state": return T("PawnDiary.Memory.Library.PhasePsylink");
                case "genetic_state": return T("PawnDiary.Memory.Library.PhaseGenetic");
                case "mechlink_state": return T("PawnDiary.Memory.Library.PhaseMechlink");
                case "persona_bond_state": return T("PawnDiary.Memory.Library.PhasePersonaBond");
                case "opinion_episode": return T("PawnDiary.Memory.Library.PhaseOpinion");
                case "formal_relationship": return T("PawnDiary.Memory.Library.PhaseFormalRelationship");
                case "faction_diplomacy": return T("PawnDiary.Memory.Library.PhaseFactionDiplomacy");
                case "faction_lifecycle": return T("PawnDiary.Memory.Library.PhaseFactionLifecycle");
                default: return string.Empty;
            }
        }

        private static string SummaryRoleLabel(MemoryBlockRow row)
        {
            if (row?.rollingSummary == true) return T("PawnDiary.Memory.Library.RollingSummary");
            if (row?.closedSummary == true) return T("PawnDiary.Memory.Library.ClosedSummary");
            return T("PawnDiary.Memory.Library.EventMemory");
        }

        private static string ProviderExposureLabel(string state)
        {
            if (state == "not_sent") return T("PawnDiary.Memory.Library.ProviderNotSent");
            if (state == "potentially_sent")
                return T("PawnDiary.Memory.Library.ProviderPotential");
            if (state == "confirmed_sent")
                return T("PawnDiary.Memory.Library.ProviderConfirmed");
            return T("PawnDiary.Memory.Library.ProviderUnknown");
        }

        private static string ArchiveSourceLabel(MemoryArchiveHandle handle)
        {
            return handle?.archiveScopeToken == MemoryLibraryScopes.UnresolvedImported
                ? T("PawnDiary.Memory.Library.ImportedSourceLegacy")
                : T("PawnDiary.Memory.Library.ImportedSourceArchive");
        }

        private static string MigrationReasonLabel(string reason)
        {
            return string.IsNullOrWhiteSpace(reason)
                ? T("PawnDiary.Memory.Library.ImportedReasonUnavailable")
                : T("PawnDiary.Memory.Library.ImportedReasonMigration");
        }

        private string DiagnosticText(MemoryBlockDetail detail)
        {
            if (detail == null) return T("PawnDiary.Memory.Library.DiagnosticsUnavailable");
            MemoryBlockRow row = blockDetail?.row;
            StringBuilder builder = new StringBuilder(768);
            builder.AppendLine(T("PawnDiary.Memory.Library.DiagnosticsIdentity",
                row?.recordHandle?.ownerPawnId ?? string.Empty,
                row?.recordHandle?.epochToken ?? string.Empty,
                row?.recordHandle?.recordId ?? string.Empty,
                row?.rootHandle?.rootId ?? T("PawnDiary.Memory.Library.None"),
                row?.chapterId ?? T("PawnDiary.Memory.Library.None")));
            builder.AppendLine(T("PawnDiary.Memory.Library.DiagnosticsState",
                row?.kind ?? string.Empty, row?.targetStructuralRevision ?? 0,
                row?.originalTick ?? -1, row?.suppressed ?? false,
                row?.playerEdited ?? false, LifetimeLabel(row), detachedTextCap));
            builder.Append(T("PawnDiary.Memory.Library.DiagnosticsBody",
                Join(detail.factDescriptors), Join(detail.subjectDescriptors),
                Join(detail.provenanceDescriptors),
                EmptyFallback(detail.sourcePageLinkToken,
                    T("PawnDiary.Memory.Library.None")),
                EmptyFallback(detail.automaticWording,
                    T("PawnDiary.Memory.Library.None")),
                Join(detail.devIdentifiersAndReasons)));
            return MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                builder.ToString(), detachedDiagnosticTextCap);
        }

        private static string Join(List<string> values)
        {
            return values == null || values.Count == 0
                ? T("PawnDiary.Memory.Library.None") : string.Join(", ", values.ToArray());
        }

        private static string CommandStatusText(string status)
        {
            if (status == MemoryLibraryCommandStatuses.Success)
                return T("PawnDiary.Memory.Library.CommandSuccess");
            if (status == MemoryLibraryCommandStatuses.Stale)
                return T("PawnDiary.Memory.Library.CommandStale");
            if (status == MemoryLibraryCommandStatuses.CapFull)
                return T("PawnDiary.Memory.Library.EditCapFull");
            if (status == "QueueFull")
                return T("PawnDiary.Memory.Library.CommandBusy");
            if (status == MemoryLibraryCommandStatuses.Missing)
                return T("PawnDiary.Memory.Library.CommandMissing");
            return T("PawnDiary.Memory.Library.CommandRejected");
        }

        private string DateLabel(long tick)
        {
            if (tick < 0) return T("PawnDiary.Memory.Library.DateUnknown");
            if (cachedDateLabels.TryGetValue(tick, out string cached)) return cached;
            return T("PawnDiary.Memory.Library.Day", tick / 60000L + 1L);
        }

        /// <summary>
        /// Formats bounded row display state once per detached publication/day/language tuple. Draw
        /// passes perform dictionary lookups and never translate or recalculate TTL for every row.
        /// </summary>
        private void RefreshDisplayCaches()
        {
            string signature = string.Join("|",
                owners?.directoryRevision ?? 0,
                OwnerKey(session.selectedOwnerHandle),
                list?.listSnapshotRevision ?? 0,
                list?.returnedStart ?? 0,
                threadDetail?.detailSnapshotRevision ?? 0,
                threadDetail?.returnedStart ?? 0,
                blockDetail?.targetStructuralRevision ?? 0,
                blockDetail?.targetStatusRevision ?? 0,
                blockDetail?.status ?? string.Empty,
                importedDetail?.archiveTextSnapshotRevision ?? 0,
                importedDetail?.status ?? string.Empty,
                importedDetail?.returnedTextStart ?? 0,
                RecordKey(session.selectedRecordHandle),
                session.selectedView ?? string.Empty,
                MemoryEffectivePolicyProvider.PublicationRevision,
                detachedNowTick / 60000L,
                Prefs.DevMode ? 1 : 0,
                LanguageDatabase.activeLanguage?.GetHashCode() ?? 0);
            if (string.Equals(displayCacheSignature, signature, StringComparison.Ordinal)) return;
            displayCacheSignature = signature;
            cachedBlockBadges.Clear();
            cachedLifetimeLabels.Clear();
            cachedDateLabels.Clear();
            cachedUsageLabels.Clear();
            cachedThreadHeaderText = string.Empty;
            cachedBlockFactsText = string.Empty;
            cachedDiagnosticText = string.Empty;
            if (list?.rows != null)
            {
                for (int index = 0; index < list.rows.Count; index++)
                {
                    MemoryLibraryListRow row = list.rows[index];
                    CacheBlockDisplay(row?.standalone);
                    CacheDate(row?.thread?.latestActivityTick ?? -1);
                    CacheDate(row?.imported?.originalTick ?? -1);
                }
            }
            if (threadDetail?.blocks != null)
                for (int index = 0; index < threadDetail.blocks.Count; index++)
                    CacheBlockDisplay(threadDetail.blocks[index]);
            CacheBlockDisplay(blockDetail?.row);
            CacheDate(threadDetail?.currentStatus?.capturedTick ?? -1);
            cachedThreadHeaderText = BuildThreadHeaderText();
            cachedBlockFactsText = BuildNormalFactsText();
            cachedDiagnosticText = Prefs.DevMode
                ? DiagnosticText(blockDetail?.detail) : string.Empty;
            MemoryOwnerCultureDto culture = selectedOwner?.culture;
            cachedCultureTitle = T("PawnDiary.Memory.Library.CulturalContext");
            cachedCultureOrigin = CultureLine(culture?.originStateToken,
                culture?.originDisplayLabel, culture?.originProvenanceToken);
            cachedCultureHasAdopted = culture != null
                && (culture.adoptedStateToken != "none"
                    || !string.IsNullOrWhiteSpace(culture.adoptedDisplayLabel));
            cachedCultureAdopted = cachedCultureHasAdopted
                ? T("PawnDiary.Memory.Library.CultureAdopted",
                    CultureStateLabel(culture.adoptedStateToken,
                        culture.adoptedDisplayLabel, false))
                : string.Empty;
            cachedCultureExplanation = T("PawnDiary.Memory.Library.CultureExplanation");
        }

        private void CacheBlockDisplay(MemoryBlockRow row)
        {
            string key = RecordKey(row?.recordHandle);
            if (key.Length == 0 || cachedBlockBadges.ContainsKey(key)) return;
            CacheDate(row.originalTick);
            MemoryLibraryUiLifetime life = MemoryLibraryUiPolicy.Lifetime(row, detachedNowTick,
                detachedMinorLifetimeTicks, detachedRegularLifetimeTicks);
            if (life.expiryTick != long.MaxValue) CacheDate(life.expiryTick);
            cachedLifetimeLabels[key] = BuildLifetimeLabel(row);
            cachedBlockBadges[key] = BuildBlockBadges(row);
            if (row.lastAutomaticIncludedTick >= 0) CacheDate(row.lastAutomaticIncludedTick);
            cachedUsageLabels[key] = BuildUsageFacts(row);
        }

        private string BuildUsageFacts(MemoryBlockRow row)
        {
            if (row == null) return string.Empty;
            string last = row.lastAutomaticIncludedTick < 0
                ? T("PawnDiary.Memory.Library.Never") : DateLabel(row.lastAutomaticIncludedTick);
            return T("PawnDiary.Memory.Library.Usage", last, row.automaticInclusionCount,
                ProviderExposureLabel(row.providerExposureState));
        }

        private string BuildThreadHeaderText()
        {
            if (threadDetail == null || threadDetail.status != MemoryLibraryStatuses.Ready)
                return string.Empty;
            MemoryCurrentStatusDto current = threadDetail.currentStatus;
            string status = string.Equals(current?.statusToken, "tracked",
                    StringComparison.Ordinal)
                ? T("PawnDiary.Memory.Library.CurrentKnown")
                : T("PawnDiary.Memory.Library.CurrentUnknown");
            string saved = MemoryLibraryUiPolicy.HasCapturedCurrentStatus(current)
                ? T("PawnDiary.Memory.Library.CurrentStatusSaved", status,
                    DateLabel(current.capturedTick), Join(current.frozenDisplayFields))
                : status;
            return T("PawnDiary.Memory.Library.ThreadDetailHeader", saved,
                threadDetail.shownManageableCount, threadDetail.totalManageableCount);
        }

        private void CacheDate(long tick)
        {
            if (tick < 0 || cachedDateLabels.ContainsKey(tick)) return;
            int gameTick = (int)Math.Min(int.MaxValue, tick);
            cachedDateLabels[tick] = GenDate.DateFullStringAt(
                GenDate.TickGameToAbs(gameTick), Vector2.zero);
        }

        private static string RecordKey(MemoryRecordHandle handle)
        {
            return handle == null ? string.Empty : (handle.ownerPawnId ?? string.Empty) + "\n"
                + (handle.epochToken ?? string.Empty) + "\n" + (handle.recordId ?? string.Empty);
        }

        private static string EmptyFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string T(string key, params object[] values)
        {
            string frame = key.Translate().Resolve();
            if (values == null || values.Length == 0) return frame;
            try { return string.Format(frame, values); }
            catch (FormatException) { return frame; }
        }
    }
}

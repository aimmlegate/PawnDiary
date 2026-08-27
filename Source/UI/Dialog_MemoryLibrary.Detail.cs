// Dialog_MemoryLibrary.Detail.cs — detail panes and staged actions for the M9 Library.
//
// Detail buttons only edit detached drafts or stage explicit commands. WindowUpdate transfers
// those commands to DiaryGameComponent after the IMGUI pass has ended.
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
        /// <summary>
        /// Session-only measurement for one bounded thread-detail publication. Chapter dividers make
        /// row slots variable-height, so drawing consumes this prefix array instead of rebuilding it
        /// during every Layout/Repaint pass.
        /// </summary>
        private sealed class MemoryThreadTimelineLayout
        {
            public long detailSnapshotRevision;
            public int returnedStart;
            public int returnedCount;
            public int blockCount;
            public int chapterCount;
            public string rootId = string.Empty;
            public float cardHeight;
            public float cardGap;
            public float chapterDividerHeight;
            public float chapterDividerTopGap;
            public float chapterDividerLineGap;
            public float[] cumulativeOffsets = new float[0];
            public MemoryChapterRow[] chapterStarts = new MemoryChapterRow[0];
        }

        private MemoryThreadTimelineLayout threadTimelineLayout;

        private void DrawDetailPane(Rect rect, bool showBack)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            if (showBack)
            {
                if (Widgets.ButtonText(new Rect(inner.x, inner.y, 120f, 28f),
                    T("PawnDiary.Memory.Library.Back")))
                {
                    NavigateBackFromDetail();
                    return;
                }
                inner.yMin += 34f;
            }
            if (Prefs.DevMode && loreExpanded)
            {
                DrawLoreSection(inner);
                return;
            }
            if (session.selectedView == MemoryLibraryUiViews.Culture)
                DrawCultureContextDetail(inner);
            else if (session.selectedView == MemoryLibraryViews.Imported)
                DrawImportedDetail(inner);
            else if (session.selectedRecordHandle != null)
                DrawBlockDetail(inner, !showBack);
            else if (session.selectedRootHandle != null)
                DrawThreadDetail(inner);
            else
                DrawCenteredState(inner, T("PawnDiary.Memory.Library.SelectMemory"));
        }

        /// <summary>
        /// Moves back exactly one subject-first navigation level. A selected memory returns to its
        /// thread or pinned list; only the content root returns to the subject rail.
        /// </summary>
        private void NavigateBackFromDetail()
        {
            if (loreExpanded)
            {
                loreExpanded = false;
                return;
            }
            if (session.selectedRecordHandle != null)
            {
                session.selectedRecordHandle = null;
                session.selectedPlacementToken = string.Empty;
                session.editDraft = null;
                session.feedbackStatus = string.Empty;
                selectedBlockRow = null;
                blockDetail = null;
                blockDetailScroll = Vector2.zero;
                diagnosticsExpanded = false;
                return;
            }
            if (session.selectedArchiveHandle != null)
            {
                session.selectedArchiveHandle = null;
                selectedImportedRow = null;
                importedDetail = null;
                importedTextStart = 0;
                importedTextExpectedSnapshotRevision = 0;
                detailScroll = Vector2.zero;
                session.feedbackStatus = string.Empty;
                return;
            }
            session.narrowDetailOpen = false;
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
            bool hasPaging = threadDetail.hasPrevious || threadDetail.hasMore;
            Rect rowsRect = new Rect(rect.x, rect.y + header, rect.width,
                Mathf.Max(0f, rect.height - header - (hasPaging ? 32f : 0f)));
            DrawThreadTimeline(rowsRect, threadDetail.blocks);
            if (hasPaging)
                DrawDetailPaging(new Rect(rect.x, rowsRect.yMax + 3f, rect.width, 29f));
        }

        /// <summary>
        /// Draws one bounded detail page as a continuous timeline. Chapter headers occupy layout
        /// space but never become synthetic block rows, so X/Y counts and page cursors stay exact.
        /// </summary>
        private void DrawThreadTimeline(Rect outRect, List<MemoryBlockRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                DrawMemoryQueryEmptyState(outRect,
                    threadDetail?.allBlocksSuppressedForWriting == true
                        ? T("PawnDiary.Memory.Library.AllSuppressed")
                        : T("PawnDiary.Memory.Library.NoBlocks"),
                    false);
                return;
            }

            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            MemoryThreadTimelineLayout layout = GetThreadTimelineLayout(rows, style);
            MemoryLibraryUiVirtualWindow window = MemoryLibraryUiPolicy.Virtualize(
                layout.cumulativeOffsets, detailScroll.y, outRect.height,
                style.memoryLibraryOverscanRows, style.memoryLibraryMaximumMaterializedRows);
            Rect view = new Rect(0f, 0f, Mathf.Max(0f, outRect.width - 16f),
                Mathf.Max(outRect.height, window.contentHeight));
            Widgets.BeginScrollView(outRect, ref detailScroll, view);
            try
            {
                for (int index = window.firstIndex; index < window.endExclusive; index++)
                {
                    float y = layout.cumulativeOffsets[index];
                    MemoryChapterRow chapter = layout.chapterStarts[index];
                    if (chapter != null)
                    {
                        y += layout.chapterDividerTopGap;
                        DrawTimelineChapterDivider(
                            new Rect(0f, y, view.width, layout.chapterDividerHeight),
                            chapter,
                            layout.chapterDividerLineGap);
                        y += layout.chapterDividerHeight;
                    }
                    DrawTimelineBlockCard(
                        new Rect(0f, y, view.width, layout.cardHeight),
                        rows[index],
                        style);
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        /// <summary>Returns cached prefix offsets and chapter starts for the current detail page.</summary>
        private MemoryThreadTimelineLayout GetThreadTimelineLayout(
            List<MemoryBlockRow> rows,
            DiaryUiStyleDef style)
        {
            GameFont priorFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float tinyLineHeight = Text.LineHeight;
            Text.Font = priorFont;

            float cardHeight = ValidAtLeast(style.memoryLibraryBlockCardHeight, 76f);
            float cardGap = ValidNonnegative(style.memoryLibraryCardGap);
            float dividerHeight = ValidAtLeast(
                style.memoryLibraryChapterDividerHeight, tinyLineHeight);
            float dividerTopGap = ValidNonnegative(
                style.memoryLibraryChapterDividerTopGap);
            float dividerLineGap = ValidNonnegative(
                style.memoryLibraryChapterDividerLineGap);
            int chapterCount = threadDetail?.chapters?.Count ?? 0;
            string rootId = threadDetail?.header?.rootHandle?.rootId ?? string.Empty;
            if (threadTimelineLayout != null
                && threadTimelineLayout.detailSnapshotRevision
                    == threadDetail.detailSnapshotRevision
                && threadTimelineLayout.returnedStart == threadDetail.returnedStart
                && threadTimelineLayout.returnedCount == threadDetail.returnedCount
                && threadTimelineLayout.blockCount == rows.Count
                && threadTimelineLayout.chapterCount == chapterCount
                && string.Equals(threadTimelineLayout.rootId, rootId, StringComparison.Ordinal)
                && threadTimelineLayout.cardHeight == cardHeight
                && threadTimelineLayout.cardGap == cardGap
                && threadTimelineLayout.chapterDividerHeight == dividerHeight
                && threadTimelineLayout.chapterDividerTopGap == dividerTopGap
                && threadTimelineLayout.chapterDividerLineGap == dividerLineGap)
                return threadTimelineLayout;

            MemoryThreadTimelineLayout rebuilt = new MemoryThreadTimelineLayout
            {
                detailSnapshotRevision = threadDetail.detailSnapshotRevision,
                returnedStart = threadDetail.returnedStart,
                returnedCount = threadDetail.returnedCount,
                blockCount = rows.Count,
                chapterCount = chapterCount,
                rootId = rootId,
                cardHeight = cardHeight,
                cardGap = cardGap,
                chapterDividerHeight = dividerHeight,
                chapterDividerTopGap = dividerTopGap,
                chapterDividerLineGap = dividerLineGap,
                cumulativeOffsets = new float[rows.Count + 1],
                chapterStarts = new MemoryChapterRow[rows.Count]
            };
            for (int index = 0; threadDetail.chapters != null
                && index < threadDetail.chapters.Count; index++)
            {
                MemoryChapterRow chapter = threadDetail.chapters[index];
                int start = chapter?.returnedChildStart ?? -1;
                if (start >= 0 && start < rebuilt.chapterStarts.Length
                    && rebuilt.chapterStarts[start] == null)
                    rebuilt.chapterStarts[start] = chapter;
            }
            for (int index = 0; index < rows.Count; index++)
            {
                float height = cardHeight + cardGap;
                if (rebuilt.chapterStarts[index] != null)
                    height += dividerTopGap + dividerHeight;
                rebuilt.cumulativeOffsets[index + 1] =
                    rebuilt.cumulativeOffsets[index] + height;
            }
            threadTimelineLayout = rebuilt;
            return rebuilt;
        }

        /// <summary>Draws a localized chapter label followed by the shared Diary divider rule.</summary>
        private void DrawTimelineChapterDivider(
            Rect rect,
            MemoryChapterRow chapter,
            float lineGap)
        {
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            GameFont priorFont = Text.Font;
            TextAnchor priorAnchor = Text.Anchor;
            Color priorColor = GUI.color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            string label = ChapterLabel(chapter?.chapterId);
            float labelWidth = Mathf.Min(rect.width, Text.CalcSize(label).x);
            GUI.color = style.QuadrumDividerLabelColor;
            Widgets.LabelFit(new Rect(rect.x, rect.y, labelWidth, rect.height), label);
            float lineX = rect.x + labelWidth + lineGap;
            if (lineX < rect.xMax)
                Widgets.DrawBoxSolid(
                    new Rect(lineX, rect.y + rect.height * 0.5f,
                        rect.xMax - lineX, 1f),
                    style.QuadrumDividerLineColor);
            GUI.color = priorColor;
            Text.Anchor = priorAnchor;
            Text.Font = priorFont;
        }

        /// <summary>Draws one existing A-prime block card at its measured timeline position.</summary>
        private void DrawTimelineBlockCard(
            Rect card,
            MemoryBlockRow row,
            DiaryUiStyleDef style)
        {
            bool selected = MemoryLibraryUiPolicy.Same(row?.recordHandle,
                session.selectedRecordHandle);
            string blockKey = RecordKey(row?.recordHandle);
            cachedBlockChips.TryGetValue(blockKey, out List<MemoryLibraryUiChip> blockChips);
            Rect text = DrawCardChrome(card, BlockAccent(row), selected,
                DateLabel(row?.originalTick ?? -1), blockChips);
            // A hidden memory fades its text but keeps its spine, so it stays scannable in the
            // timeline without reading as available to the writer.
            Color priorBlockColor = GUI.color;
            if (row?.suppressed == true)
                GUI.color = new Color(1f, 1f, 1f,
                    Mathf.Clamp(style.memoryLibrarySuppressedAlpha, 0.2f, 1f));
            Text.Font = GameFont.Tiny;
            float badgeHeight = Text.LineHeight;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(text.x, text.y, text.width,
                    Mathf.Max(Text.LineHeight, text.height - badgeHeight - 2f)),
                BlockCardWording(row));
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b,
                GUI.color.a * 0.7f);
            Widgets.Label(new Rect(text.x, text.yMax - badgeHeight,
                    text.width, badgeHeight),
                BlockMeta(row));
            GUI.color = priorBlockColor;
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

        private static float ValidAtLeast(float value, float minimum)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < minimum
                ? minimum : value;
        }

        private static float ValidNonnegative(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f
                ? 0f : value;
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
            string actionConsequence = T(row.suppressed
                ? "PawnDiary.Memory.Library.Action.AllowConsequence"
                : "PawnDiary.Memory.Library.Action.HideConsequence");
            float consequenceHeight = Mathf.Max(Text.LineHeight,
                Text.CalcHeight(actionConsequence, contentWidth));
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
                if (Prefs.DevMode && diagnosticsExpanded)
                    diagnosticsHeight = Mathf.Max(68f,
                        Text.CalcHeight(cachedDiagnosticText, contentWidth) + 34f);
                tailHeight = 34f + consequenceHeight + 4f
                    + 26f + usageHeight + 4f + diagnosticsHeight;
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

                Text.Font = GameFont.Tiny;
                Color priorColor = GUI.color;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(view.x, y, view.width, consequenceHeight),
                    actionConsequence);
                GUI.color = priorColor;
                Text.Font = GameFont.Small;
                y += consequenceHeight + 4f;

                DrawFeedback(new Rect(view.x, y, view.width, 24f));
                y += 26f;
                DrawUsageFacts(new Rect(view.x, y, view.width, usageHeight), row);
                y += usageHeight + 4f;
                if (Prefs.DevMode && diagnosticsExpanded)
                    DrawDevDiagnostics(new Rect(view.x, y, view.width,
                        Mathf.Max(68f, view.yMax - y)), row);
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
            if (!diagnosticsExpanded) return;
            string facts = cachedDiagnosticText;
            Rect textRect = new Rect(rect.x, rect.y, rect.width,
                Mathf.Max(34f, rect.height - 34f));
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
            if (!Prefs.DevMode || !loreExpanded || lore == null) return;
            string details = Dialog_PawnWritingStyle.LoreStateText(lore);
            if (lore.topics != null && lore.topics.Count > 0)
            {
                selectedLoreTopicIndex = Mathf.Clamp(
                    selectedLoreTopicIndex, 0, lore.topics.Count - 1);
                LoreMemoryTopicForDev topic = lore.topics[selectedLoreTopicIndex];
                Rect previous = new Rect(rect.x, rect.y, 90f, 28f);
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
            Rect output = new Rect(rect.x, rect.y + navigation, rect.width,
                Mathf.Max(40f, rect.height - navigation));
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
                    repositoryNavigationDirty = true;
                },
                delegate
                {
                    detailStart = threadDetail.nextStart;
                    detailExpectedSnapshotRevision = threadDetail.detailSnapshotRevision;
                    detailScroll = Vector2.zero;
                    repositoryNavigationDirty = true;
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
                    repositoryNavigationDirty = true;
                },
                delegate
                {
                    importedTextStart = importedDetail.nextTextStart;
                    importedTextExpectedSnapshotRevision = importedDetail.archiveTextSnapshotRevision;
                    detailScroll = Vector2.zero;
                    repositoryNavigationDirty = true;
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
    }
}

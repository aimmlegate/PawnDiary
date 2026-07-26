// Fixed-size, non-pausing three-pane diary reader window.
// The left pane resolves current and historical pawns, while the middle/right area delegates to the
// same DiaryJournalView used by the inspect tab, preserving virtualization, filters, and favorites.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Standalone reader for all pawn diaries in the current game.
    /// </summary>
    // The placeholder texture remains lazy-loaded from DrawPortrait on the UI thread. This marker
    // tells RimWorld the Texture2D-holding type participates in its main-thread startup contract.
    [StaticConstructorOnStartup]
    internal sealed class Dialog_DiaryReader : Window
    {
        private const float DirectorySectionHeight = 24f;
        private const float DirectoryPawnRowMinimumHeight = 64f;
        private const float ReaderChromePadding = 24f;
        private const string PlaceholderTexturePath = "UI/Commands/PawnDiaryOpen";

        private static Texture2D placeholderTexture;

        private readonly DiaryJournalView journalView = new DiaryJournalView();
        private readonly DiaryReaderPawnDirectory directory = new DiaryReaderPawnDirectory();
        private readonly List<DiaryReaderPawnRow> visibleRows = new List<DiaryReaderPawnRow>();
        private DiaryReaderSubject selectedSubject;
        private Vector2 pawnListScroll;
        private bool showDeadPawns;
        private DiaryReaderSortMode sortMode = DiaryReaderSortMode.NewestPage;
        private string searchQuery = string.Empty;
        private bool forceDirectoryRefresh = true;
        // Remember the map selection independently from the directory selection. This lets a NEW
        // colonist selection follow the player into the open reader without a manual directory click,
        // while an intentional directory click remains stable until the map selection changes again.
        private string lastObservedMapSelectionPawnId;

        private Dialog_DiaryReader(DiaryReaderSubject subject)
        {
            selectedSubject = subject;
            lastObservedMapSelectionPawnId = SelectedPawnOrCorpse()?.GetUniqueLoadID();
            forcePause = false;
            draggable = true;
            resizeable = false;
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            onlyOneOfTypeAllowed = true;
        }

        public override Vector2 InitialSize
        {
            get
            {
                DiaryUiStyleDef style = DiaryJournalView.UiStyle;
                DiaryReaderWindowSize size = DiaryReaderLayoutPolicy.WindowSize(
                    UI.screenWidth,
                    UI.screenHeight,
                    style.readerMaxWidth,
                    style.readerMaxHeight,
                    style.readerMinWidth,
                    style.readerMinHeight,
                    style.readerScreenMargin);
                return new Vector2(size.width, size.height);
            }
        }

        /// <summary>
        /// Opens or focuses the singleton reader on the requested live pawn.
        /// </summary>
        internal static void Open(Pawn pawn)
        {
            Open(DiaryReaderSubject.FromPawn(pawn));
        }

        /// <summary>
        /// Opens or focuses the singleton reader on a live or archive-only subject.
        /// </summary>
        internal static void Open(DiaryReaderSubject subject)
        {
            if (Find.WindowStack == null)
            {
                return;
            }

            Dialog_DiaryReader existing = Find.WindowStack.Windows
                .OfType<Dialog_DiaryReader>()
                .FirstOrDefault();
            if (existing != null)
            {
                existing.SelectSubject(subject);
                Find.WindowStack.Notify_ManuallySetFocus(existing);
                return;
            }

            Find.WindowStack.Add(new Dialog_DiaryReader(subject));
        }

        /// <summary>
        /// Toggles the singleton reader, seeding a new window from the current pawn/corpse selection.
        /// </summary>
        internal static void Toggle()
        {
            Dialog_DiaryReader existing = Find.WindowStack?.Windows
                .OfType<Dialog_DiaryReader>()
                .FirstOrDefault();
            if (existing != null)
            {
                existing.Close();
                return;
            }

            Open(DiaryReaderSubject.FromPawn(SelectedPawnOrCorpse()));
        }

        /// <summary>
        /// Closes every open reader window. Used when alternative mode is disabled.
        /// </summary>
        internal static void CloseAll()
        {
            Find.WindowStack?.TryRemove(typeof(Dialog_DiaryReader), true);
        }

        /// <summary>
        /// Follows newly selected map colonists while the standalone reader remains open.
        /// </summary>
        public override void WindowUpdate()
        {
            base.WindowUpdate();

            Pawn pawn = SelectedPawnOrCorpse();
            string pawnId = pawn?.GetUniqueLoadID();
            if (string.Equals(pawnId, lastObservedMapSelectionPawnId, StringComparison.Ordinal))
            {
                return;
            }

            lastObservedMapSelectionPawnId = pawnId;
            if (IsCurrentLivingColonist(pawn))
            {
                SelectSubject(DiaryReaderSubject.FromPawn(pawn));
            }
        }

        private static Pawn SelectedPawnOrCorpse()
        {
            Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
            if (pawn != null)
            {
                return pawn;
            }

            return (Find.Selector?.SingleSelectedThing as Corpse)?.InnerPawn;
        }

        /// <summary>
        /// Uses RimWorld's current free-colonist roster rather than Pawn.IsColonist, because departed
        /// world pawns can retain the player faction and still report themselves as colonists.
        /// </summary>
        private static bool IsCurrentLivingColonist(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
            {
                return false;
            }

            IEnumerable<Pawn> colonists =
                PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists;
            if (colonists == null)
            {
                return false;
            }

            string pawnId = pawn.GetUniqueLoadID();
            foreach (Pawn colonist in colonists)
            {
                if (colonist != null
                    && string.Equals(colonist.GetUniqueLoadID(), pawnId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void SelectSubject(DiaryReaderSubject subject)
        {
            if (subject.IsValid)
            {
                selectedSubject = subject;
            }

            forceDirectoryRefresh = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            DiaryGameComponent component = DiaryGameComponent.Instance;
            if (component == null)
            {
                Widgets.Label(inRect, "PawnDiary.Reader.NoDiaries".Translate());
                return;
            }

            directory.RefreshIfNeeded(
                component,
                "PawnDiary.Reader.UnknownPawn".Translate().ToString(),
                sortMode,
                searchQuery,
                forceDirectoryRefresh);
            forceDirectoryRefresh = false;
            EnsureSelectionVisible();

            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            float gap = Mathf.Max(0f, style.readerPaneGap);
            float pawnListWidth = DiaryReaderLayoutPolicy.PawnListWidth(
                inRect.width,
                style.readerCompactThreshold,
                style.readerPawnListWidth,
                style.readerPawnListWidthCompact);
            pawnListWidth = Mathf.Clamp(pawnListWidth, 120f, Mathf.Max(120f, inRect.width * 0.4f));

            Rect directoryRect = new Rect(inRect.x, inRect.y, pawnListWidth, inRect.height);
            float remainingX = directoryRect.xMax + gap;
            float remainingWidth = Mathf.Max(0f, inRect.xMax - remainingX);
            float readerWidth = DiaryReaderLayoutPolicy.ReaderWidth(
                remainingWidth,
                style.readerBookWidth,
                style.filterPanelWidth,
                style.filterPanelGap,
                ReaderChromePadding);
            Rect readerRect = new Rect(
                remainingX + Mathf.Max(0f, (remainingWidth - readerWidth) * 0.5f),
                inRect.y,
                readerWidth,
                inRect.height);

            DrawDirectory(directoryRect, style, component);
            DiaryReaderDirectoryEmptyReason emptyReason = CurrentDirectoryEmptyReason();
            if (emptyReason != DiaryReaderDirectoryEmptyReason.None)
            {
                DrawReaderEmptyState(readerRect, emptyReason);
            }
            else if (!selectedSubject.IsValid)
            {
                Widgets.Label(readerRect, "PawnDiary.Reader.SelectPawnHint".Translate());
            }
            else
            {
                journalView.Draw(readerRect, selectedSubject, component);
            }
        }

        private void EnsureSelectionVisible()
        {
            IReadOnlyList<DiaryReaderPawnRow> rows = directory.Rows;
            if (selectedSubject.IsValid)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (string.Equals(
                        rows[i].Subject.PawnId,
                        selectedSubject.PawnId,
                        StringComparison.Ordinal))
                    {
                        selectedSubject = rows[i].Subject;
                        if (rows[i].Departed)
                        {
                            showDeadPawns = true;
                        }

                        return;
                    }
                }
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (showDeadPawns || !rows[i].Departed)
                {
                    selectedSubject = rows[i].Subject;
                    return;
                }
            }

            selectedSubject = default(DiaryReaderSubject);
        }

        private void DrawDirectory(Rect rect, DiaryUiStyleDef style, DiaryGameComponent component)
        {
            float controlHeight = Mathf.Max(24f, style.readerDirectoryControlHeight);
            float controlGap = Mathf.Max(0f, style.readerDirectoryControlGap);
            float searchLabelHeight = Mathf.Max(20f, style.readerDirectorySearchLabelHeight);
            float y = rect.y;

            Rect searchLabelRect = new Rect(rect.x, y, rect.width, searchLabelHeight);
            Widgets.Label(searchLabelRect, "PawnDiary.Reader.SearchLabel".Translate());
            y = searchLabelRect.yMax + controlGap;

            Rect searchRowRect = new Rect(rect.x, y, rect.width, controlHeight);
            float clearWidth = controlHeight;
            Rect searchRect = new Rect(
                searchRowRect.x,
                searchRowRect.y,
                Mathf.Max(0f, searchRowRect.width - clearWidth - controlGap),
                searchRowRect.height);
            Rect clearRect = new Rect(searchRect.xMax + controlGap, searchRowRect.y, clearWidth, searchRowRect.height);
            string previousSearch = searchQuery ?? string.Empty;
            string editedSearch = Widgets.TextField(searchRect, previousSearch);
            TooltipHandler.TipRegion(searchRect, "PawnDiary.Reader.SearchTip".Translate());
            if (!string.Equals(previousSearch, editedSearch, StringComparison.Ordinal))
            {
                SetDirectorySearch(editedSearch);
            }

            bool oldEnabled = GUI.enabled;
            GUI.enabled = !string.IsNullOrEmpty(searchQuery);
            if (Widgets.ButtonText(clearRect, "×"))
            {
                SetDirectorySearch(string.Empty);
            }
            GUI.enabled = oldEnabled;
            TooltipHandler.TipRegion(clearRect, "PawnDiary.Reader.ClearSearchTip".Translate());
            y = searchRowRect.yMax + controlGap;

            Rect sortRect = new Rect(rect.x, y, rect.width, controlHeight);
            if (Widgets.ButtonText(
                sortRect,
                "PawnDiary.Reader.SortButton".Translate(SortModeLabel(sortMode))))
            {
                ShowSortMenu();
            }
            TooltipHandler.TipRegion(sortRect, "PawnDiary.Reader.SortTip".Translate());
            y = sortRect.yMax + controlGap;

            bool oldShowDead = showDeadPawns;
            Rect toggleRect = new Rect(rect.x, y, rect.width, controlHeight);
            Widgets.CheckboxLabeled(
                toggleRect,
                "PawnDiary.Reader.ShowDeadPawns".Translate(),
                ref showDeadPawns);
            TooltipHandler.TipRegion(toggleRect, "PawnDiary.Reader.ShowDeadPawnsTip".Translate());
            if (oldShowDead && !showDeadPawns && SelectedSubjectIsDeparted())
            {
                selectedSubject = default(DiaryReaderSubject);
                EnsureSelectionVisible();
            }
            if (oldShowDead != showDeadPawns)
            {
                pawnListScroll.y = 0f;
            }

            Rect outRect = new Rect(
                rect.x,
                toggleRect.yMax + controlGap,
                rect.width,
                Mathf.Max(0f, rect.yMax - toggleRect.yMax - controlGap));
            RebuildVisibleRows();
            if (visibleRows.Count == 0)
            {
                Widgets.Label(outRect, DirectoryEmptyText(CurrentDirectoryEmptyReason()));
                return;
            }

            float rowHeight = Mathf.Max(DirectoryPawnRowMinimumHeight, style.readerPawnRowHeight);
            int sectionCount = 1;
            if (directory.GroupedByDeparture)
            {
                bool hasLiving = false;
                bool hasDeparted = false;
                for (int i = 0; i < visibleRows.Count; i++)
                {
                    hasDeparted |= visibleRows[i].Departed;
                    hasLiving |= !visibleRows[i].Departed;
                }

                sectionCount = (hasLiving ? 1 : 0) + (hasDeparted ? 1 : 0);
            }

            float viewHeight = sectionCount * DirectorySectionHeight + visibleRows.Count * rowHeight;
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, outRect.width - 16f), Mathf.Max(outRect.height, viewHeight));
            Widgets.BeginScrollView(outRect, ref pawnListScroll, viewRect);
            try
            {
                float listY = 0f;
                bool departedHeaderDrawn = false;
                if (!directory.GroupedByDeparture)
                {
                    DrawSectionHeader(
                        new Rect(0f, listY, viewRect.width, DirectorySectionHeight),
                        "PawnDiary.Reader.AllPawnsHeader".Translate());
                    listY += DirectorySectionHeight;
                }

                for (int i = 0; i < visibleRows.Count; i++)
                {
                    DiaryReaderPawnRow row = visibleRows[i];
                    if (directory.GroupedByDeparture)
                    {
                        if (row.Departed && !departedHeaderDrawn)
                        {
                            DrawSectionHeader(
                                new Rect(0f, listY, viewRect.width, DirectorySectionHeight),
                                "PawnDiary.Reader.DepartedPawnsHeader".Translate());
                            listY += DirectorySectionHeight;
                            departedHeaderDrawn = true;
                        }
                        else if (!row.Departed && i == 0)
                        {
                            DrawSectionHeader(
                                new Rect(0f, listY, viewRect.width, DirectorySectionHeight),
                                "PawnDiary.Reader.LivingPawnsHeader".Translate());
                            listY += DirectorySectionHeight;
                        }
                    }

                    DrawPawnRow(
                        new Rect(0f, listY, viewRect.width, rowHeight),
                        row,
                        style,
                        component);
                    listY += rowHeight;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void SetDirectorySearch(string value)
        {
            searchQuery = value ?? string.Empty;
            pawnListScroll.y = 0f;
            forceDirectoryRefresh = true;
        }

        private void ShowSortMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            AddSortOption(options, DiaryReaderSortMode.NewestPage);
            AddSortOption(options, DiaryReaderSortMode.UnreadCount);
            AddSortOption(options, DiaryReaderSortMode.Name);
            AddSortOption(options, DiaryReaderSortMode.LivingDeparted);
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void AddSortOption(List<FloatMenuOption> options, DiaryReaderSortMode mode)
        {
            options.Add(new FloatMenuOption(SortModeLabel(mode), delegate
            {
                sortMode = mode;
                pawnListScroll.y = 0f;
                forceDirectoryRefresh = true;
            }));
        }

        private static string SortModeLabel(DiaryReaderSortMode mode)
        {
            switch (mode)
            {
                case DiaryReaderSortMode.UnreadCount:
                    return "PawnDiary.Reader.SortUnread".Translate();
                case DiaryReaderSortMode.Name:
                    return "PawnDiary.Reader.SortName".Translate();
                case DiaryReaderSortMode.LivingDeparted:
                    return "PawnDiary.Reader.SortLivingDeparted".Translate();
                default:
                    return "PawnDiary.Reader.SortNewest".Translate();
            }
        }

        private void RebuildVisibleRows()
        {
            visibleRows.Clear();
            IReadOnlyList<DiaryReaderPawnRow> rows = directory.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                if (showDeadPawns || !rows[i].Departed)
                {
                    visibleRows.Add(rows[i]);
                }
            }
        }

        private DiaryReaderDirectoryEmptyReason CurrentDirectoryEmptyReason()
        {
            RebuildVisibleRows();
            return DiaryReaderEmptyStatePolicy.DirectoryReason(
                directory.EligibleRowCount,
                directory.Rows.Count,
                visibleRows.Count,
                DiaryReaderListPolicy.IsSearchActive(searchQuery),
                showDeadPawns);
        }

        private TaggedString DirectoryEmptyText(DiaryReaderDirectoryEmptyReason reason)
        {
            switch (reason)
            {
                case DiaryReaderDirectoryEmptyReason.SearchNoMatch:
                    return "PawnDiary.Reader.NoSearchMatches".Translate((searchQuery ?? string.Empty).Trim());
                case DiaryReaderDirectoryEmptyReason.DepartedHidden:
                    return "PawnDiary.Reader.DepartedHidden".Translate();
                default:
                    return "PawnDiary.Reader.NoPawns".Translate();
            }
        }

        private void DrawReaderEmptyState(Rect rect, DiaryReaderDirectoryEmptyReason reason)
        {
            Widgets.Label(rect, DirectoryEmptyText(reason));
        }

        /// <summary>
        /// Returns whether the selected row belongs to the hidden historical partition. Aliveness
        /// is not enough: a former colonist can still be alive while correctly appearing under
        /// "Dead and departed".
        /// </summary>
        private bool SelectedSubjectIsDeparted()
        {
            if (!selectedSubject.IsValid)
            {
                return false;
            }

            IReadOnlyList<DiaryReaderPawnRow> rows = directory.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                if (string.Equals(
                    rows[i].Subject.PawnId,
                    selectedSubject.PawnId,
                    StringComparison.Ordinal))
                {
                    return rows[i].Departed;
                }
            }

            return false;
        }

        private static void DrawSectionHeader(Rect rect, string label)
        {
            Color oldColor = GUI.color;
            GUI.color = DiaryJournalView.UiStyle.EntryDateColor;
            Widgets.Label(rect, label);
            GUI.color = oldColor;
        }

        private void DrawPawnRow(
            Rect rect,
            DiaryReaderPawnRow row,
            DiaryUiStyleDef style,
            DiaryGameComponent component)
        {
            bool selected = string.Equals(
                selectedSubject.PawnId,
                row.Subject.PawnId,
                StringComparison.Ordinal);
            if (selected)
            {
                Widgets.DrawHighlightSelected(rect);
            }
            else
            {
                Widgets.DrawHighlightIfMouseover(rect);
            }

            Color oldColor = GUI.color;
            if (row.Departed)
            {
                GUI.color = style.ReaderDeadPawnTint;
            }

            float portraitSize = Mathf.Min(
                Mathf.Max(16f, style.readerPortraitSize),
                Mathf.Max(16f, rect.height - 4f));
            Rect portraitRect = new Rect(
                rect.x + 2f,
                rect.y + (rect.height - portraitSize) * 0.5f,
                portraitSize,
                portraitSize);
            DrawPortrait(portraitRect, row.Subject.Pawn);

            float textX = portraitRect.xMax + 6f;
            DiaryGameComponent.DiaryCommandStatus status = component == null
                ? default(DiaryGameComponent.DiaryCommandStatus)
                : component.ReaderStatusForId(row.Subject.PawnId);
            bool hasStatus = status.HasNewPages || status.IsWriting || status.HasFailures;
            float statusColumnWidth = hasStatus
                ? Mathf.Min(
                    Mathf.Max(24f, style.statusBadgeWidth),
                    Mathf.Max(0f, rect.xMax - textX - 2f))
                : 0f;
            float textRight = rect.xMax - 2f - statusColumnWidth;
            float textWidth = Mathf.Max(0f, textRight - textX);
            float nameHeight = 24f;
            float metadataHeight = Mathf.Max(20f, (rect.height - nameHeight - 2f) * 0.5f);
            Rect nameRect = new Rect(textX, rect.y, textWidth, nameHeight);
            Rect countRect = new Rect(textX, nameRect.yMax, textWidth, metadataHeight);
            Rect dateRect = new Rect(textX, countRect.yMax, textWidth, metadataHeight);
            Widgets.Label(nameRect, row.Subject.DisplayName);

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            string countLabel = "PawnDiary.Reader.PawnRowPages".Translate(row.EntryCount);
            string dateLabel = row.HasLatestEntry && !string.IsNullOrWhiteSpace(row.LatestEntryDate)
                ? "PawnDiary.Reader.PawnRowLastEntry".Translate(row.LatestEntryDate)
                : "PawnDiary.Reader.PawnRowNoFinishedEntry".Translate();
            Color countColor = GUI.color;
            GUI.color = new Color(countColor.r, countColor.g, countColor.b, countColor.a * 0.72f);
            Widgets.LabelFit(countRect, countLabel);
            Widgets.LabelFit(dateRect, dateLabel);
            GUI.color = oldColor;
            Text.Font = oldFont;

            if (hasStatus && statusColumnWidth > 0f)
            {
                float badgeWidth = Mathf.Max(20f, statusColumnWidth - 4f);
                float badgeHeight = Mathf.Min(18f, Mathf.Max(14f, rect.height / 3f - 3f));
                float badgeX = rect.xMax - badgeWidth - 2f;
                if (status.HasNewPages)
                {
                    DiaryStatusOverlay.DrawUnreadCountBadge(
                        new Rect(badgeX, rect.y + 2f, badgeWidth, badgeHeight),
                        status.unacknowledgedCount);
                }
                if (status.IsWriting)
                {
                    DiaryStatusOverlay.DrawWritingBadge(
                        new Rect(
                            badgeX,
                            rect.y + (rect.height - badgeHeight) * 0.5f,
                            badgeWidth,
                            badgeHeight));
                }
                if (status.HasFailures)
                {
                    DiaryStatusOverlay.DrawFailureBadge(
                        new Rect(badgeX, rect.yMax - badgeHeight - 2f, badgeWidth, badgeHeight),
                        status.failedCount);
                }
            }

            if (Text.CalcSize(row.Subject.DisplayName).x > nameRect.width)
            {
                TooltipHandler.TipRegion(nameRect, row.Subject.DisplayName);
            }
            GameFont tooltipMeasureFont = Text.Font;
            Text.Font = GameFont.Tiny;
            if (Text.CalcSize(countLabel).x > countRect.width)
            {
                TooltipHandler.TipRegion(countRect, countLabel);
            }
            if (Text.CalcSize(dateLabel).x > dateRect.width)
            {
                TooltipHandler.TipRegion(dateRect, dateLabel);
            }
            Text.Font = tooltipMeasureFont;

            if (Widgets.ButtonInvisible(rect, false))
            {
                selectedSubject = row.Subject;
            }
        }

        private static void DrawPortrait(Rect rect, Pawn pawn)
        {
            Texture texture = null;
            if (pawn != null)
            {
                try
                {
                    texture = PortraitsCache.Get(
                        pawn,
                        new Vector2(rect.width, rect.height),
                        Rot4.South);
                }
                catch
                {
                    texture = null;
                }
            }

            Color oldColor = GUI.color;
            if (texture == null)
            {
                if (placeholderTexture == null)
                {
                    placeholderTexture = ContentFinder<Texture2D>.Get(
                        PlaceholderTexturePath,
                        false) ?? TexButton.IconBook;
                }

                texture = placeholderTexture;
                GUI.color = new Color(oldColor.r, oldColor.g, oldColor.b, oldColor.a * 0.55f);
            }

            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
            GUI.color = oldColor;
        }
    }
}

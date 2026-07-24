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
    [StaticConstructorOnStartup]
    internal sealed class Dialog_DiaryReader : Window
    {
        private const float DirectoryHeaderHeight = 30f;
        private const float DirectorySectionHeight = 24f;
        private const float ReaderChromePadding = 24f;
        private const string PlaceholderTexturePath = "UI/Commands/PawnDiaryOpen";

        private static Texture2D placeholderTexture;

        private readonly DiaryJournalView journalView = new DiaryJournalView();
        private readonly DiaryReaderPawnDirectory directory = new DiaryReaderPawnDirectory();
        private DiaryReaderSubject selectedSubject;
        private Vector2 pawnListScroll;
        private bool showDeadPawns;
        private bool forceDirectoryRefresh = true;

        private Dialog_DiaryReader(DiaryReaderSubject subject)
        {
            selectedSubject = subject;
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

        private static Pawn SelectedPawnOrCorpse()
        {
            Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
            if (pawn != null)
            {
                return pawn;
            }

            return (Find.Selector?.SingleSelectedThing as Corpse)?.InnerPawn;
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

            DrawDirectory(directoryRect, style);
            if (directory.Rows.Count == 0)
            {
                Widgets.Label(readerRect, "PawnDiary.Reader.NoDiaries".Translate());
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

            int visibleCount = showDeadPawns ? rows.Count : directory.DepartedDividerIndex;
            selectedSubject = visibleCount > 0 ? rows[0].Subject : default(DiaryReaderSubject);
        }

        private void DrawDirectory(Rect rect, DiaryUiStyleDef style)
        {
            bool oldShowDead = showDeadPawns;
            Rect toggleRect = new Rect(rect.x, rect.y, rect.width, DirectoryHeaderHeight);
            Widgets.CheckboxLabeled(
                toggleRect,
                "PawnDiary.Reader.ShowDeadPawns".Translate(),
                ref showDeadPawns);
            TooltipHandler.TipRegion(toggleRect, "PawnDiary.Reader.ShowDeadPawnsTip".Translate());
            if (oldShowDead && !showDeadPawns && !selectedSubject.Alive)
            {
                selectedSubject = default(DiaryReaderSubject);
                EnsureSelectionVisible();
            }

            Rect outRect = new Rect(
                rect.x,
                toggleRect.yMax + 4f,
                rect.width,
                Mathf.Max(0f, rect.yMax - toggleRect.yMax - 4f));
            int visibleCount = showDeadPawns ? directory.Rows.Count : directory.DepartedDividerIndex;
            float rowHeight = Mathf.Max(28f, style.readerPawnRowHeight);
            float viewHeight = DirectorySectionHeight + directory.DepartedDividerIndex * rowHeight;
            if (showDeadPawns && directory.Rows.Count > directory.DepartedDividerIndex)
            {
                viewHeight += DirectorySectionHeight
                    + (directory.Rows.Count - directory.DepartedDividerIndex) * rowHeight;
            }

            Rect viewRect = new Rect(0f, 0f, Mathf.Max(0f, outRect.width - 16f), Mathf.Max(outRect.height, viewHeight));
            Widgets.BeginScrollView(outRect, ref pawnListScroll, viewRect);
            try
            {
                float y = 0f;
                DrawSectionHeader(
                    new Rect(0f, y, viewRect.width, DirectorySectionHeight),
                    "PawnDiary.Reader.LivingPawnsHeader".Translate());
                y += DirectorySectionHeight;

                for (int i = 0; i < visibleCount; i++)
                {
                    if (showDeadPawns && i == directory.DepartedDividerIndex)
                    {
                        DrawSectionHeader(
                            new Rect(0f, y, viewRect.width, DirectorySectionHeight),
                            "PawnDiary.Reader.DepartedPawnsHeader".Translate());
                        y += DirectorySectionHeight;
                    }

                    DrawPawnRow(
                        new Rect(0f, y, viewRect.width, rowHeight),
                        directory.Rows[i],
                        style);
                    y += rowHeight;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private static void DrawSectionHeader(Rect rect, string label)
        {
            Color oldColor = GUI.color;
            GUI.color = DiaryJournalView.UiStyle.EntryDateColor;
            Widgets.Label(rect, label);
            GUI.color = oldColor;
        }

        private void DrawPawnRow(Rect rect, DiaryReaderPawnRow row, DiaryUiStyleDef style)
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
            Rect nameRect = new Rect(textX, rect.y + 2f, Mathf.Max(0f, rect.xMax - textX - 2f), 22f);
            Rect countRect = new Rect(textX, rect.y + 23f, nameRect.width, 20f);
            Widgets.Label(nameRect, row.Subject.DisplayName);
            Color countColor = GUI.color;
            GUI.color = new Color(countColor.r, countColor.g, countColor.b, countColor.a * 0.72f);
            Widgets.Label(countRect, "PawnDiary.Reader.PawnRowPages".Translate(row.EntryCount));
            GUI.color = oldColor;

            if (Text.CalcSize(row.Subject.DisplayName).x > nameRect.width)
            {
                TooltipHandler.TipRegion(nameRect, row.Subject.DisplayName);
            }

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

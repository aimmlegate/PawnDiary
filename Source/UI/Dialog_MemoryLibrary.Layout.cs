// Dialog_MemoryLibrary.Layout.cs — subject-first shell for the detached Memory Library.
//
// The shell only arranges partial views and changes detached navigation state. Repository reads and
// staged mutations remain in WindowUpdate so RimWorld's repeated IMGUI passes stay side-effect free.
using System;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    internal sealed partial class Dialog_MemoryLibrary
    {
        /// <summary>Draws the owner strip, persistent subject rail, content, filters, and dev strip.</summary>
        public override void DoWindowContents(Rect inRect)
        {
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            float control = Mathf.Max(24f, style.memoryLibraryControlHeight);
            float gap = Mathf.Max(2f, style.memoryLibraryCardGap);
            float y = inRect.y;

            y = DrawOwnerBar(new Rect(inRect.x, y, inRect.width, control), gap);
            if (session.selectedOwnerHandle == null && session.selectedCompatibilityHandle == null)
            {
                DrawOwnerEmptyState(new Rect(inRect.x, y + gap, inRect.width,
                    Mathf.Max(40f, inRect.yMax - y - gap)));
                return;
            }

            if (owners?.ownerEmptyStateToken == "no_matches"
                && !string.IsNullOrWhiteSpace(session.ownerSearch))
            {
                DrawOwnerSearchNoMatches(new Rect(inRect.x, y + gap, inRect.width, control));
                y += gap + control;
            }

            if (session.selectedView == MemoryLibraryViews.Imported)
                y = DrawImportedSort(new Rect(inRect.x, y + gap, inRect.width, control));
            y += gap;

            float devHeight = Prefs.DevMode
                ? Mathf.Max(control, style.memoryLibraryDevDiagnosticsStripHeight)
                : 0f;
            Rect devStrip = devHeight > 0f
                ? new Rect(inRect.x, inRect.yMax - devHeight, inRect.width, devHeight)
                : Rect.zero;
            float bodyBottom = devHeight > 0f ? devStrip.y - gap : inRect.yMax;
            Rect body = new Rect(inRect.x, y, inRect.width, Mathf.Max(0f, bodyBottom - y));
            DrawMemoryLibraryBody(body, style, gap);
            if (devHeight > 0f) DrawMemoryLibraryDeveloperStrip(devStrip);
        }

        private void DrawMemoryLibraryBody(Rect body, DiaryUiStyleDef style, float gap)
        {
            if (body.width <= 1f || body.height <= 1f) return;
            float panelGap = Mathf.Max(2f, style.memoryLibraryFilterPanelGap);
            float requestedPanelWidth = MemoryLibraryFilterPanelWidth(body.width);
            float panelWidth = Mathf.Min(requestedPanelWidth,
                Mathf.Max(0f, body.width - panelGap - 220f));
            Rect main = new Rect(body.x, body.y,
                Mathf.Max(0f, body.width - (panelWidth > 0f ? panelWidth + panelGap : 0f)),
                body.height);
            Rect filter = panelWidth > 0f
                ? new Rect(main.xMax + panelGap, body.y, panelWidth, body.height)
                : Rect.zero;

            bool narrow = main.width < Mathf.Max(560f, style.memoryLibraryNarrowThreshold);
            if (narrow)
            {
                if (!DrawNarrowSubjectRail(main)) DrawNarrowMemoryLibraryContent(main);
            }
            else
            {
                float paneGap = Mathf.Max(4f, style.memoryLibraryPaneGap);
                float railWidth = Mathf.Min(
                    Mathf.Max(220f, style.memoryLibraryRailWidth),
                    Mathf.Max(220f, main.width - paneGap - 260f));
                Rect rail = new Rect(main.x, main.y, railWidth, main.height);
                Rect content = new Rect(rail.xMax + paneGap, main.y,
                    Mathf.Max(0f, main.xMax - rail.xMax - paneGap), main.height);
                DrawSubjectRail(rail);
                if (RightPaneShowsContentList())
                    DrawListPane(content);
                else
                    DrawDetailPane(content,
                        session.selectedView == MemoryLibraryViews.Imported
                            && session.selectedArchiveHandle != null);
            }

            if (panelWidth > 0f) DrawMemoryLibraryFilterPanel(filter);
        }

        /// <summary>Provides the middle Back step in narrow Rail -> Content -> Memory navigation.</summary>
        private void DrawNarrowMemoryLibraryContent(Rect rect)
        {
            if (!RightPaneShowsContentList())
            {
                DrawDetailPane(rect, true);
                return;
            }

            float backHeight = Mathf.Max(24f, DiaryJournalView.UiStyle.memoryLibraryControlHeight);
            if (Widgets.ButtonText(new Rect(rect.x, rect.y, Mathf.Min(120f, rect.width), backHeight),
                    T("PawnDiary.Memory.Library.Back")))
            {
                session.narrowDetailOpen = false;
                return;
            }
            DrawListPane(new Rect(rect.x, rect.y + backHeight + 4f, rect.width,
                Mathf.Max(0f, rect.height - backHeight - 4f)));
        }

        /// <summary>Draws the single bottom diagnostics switchboard used only in developer mode.</summary>
        private void DrawMemoryLibraryDeveloperStrip(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(3f);
            float gap = 4f;
            float width = Mathf.Max(40f, (inner.width - gap * 2f) / 3f);
            bool cultureAvailable = HasExactSelectedOwner();
            bool memoryAvailable = session.selectedRecordHandle != null
                && blockDetail?.status == MemoryLibraryStatuses.Ready
                && blockDetail.row != null;
            bool priorEnabled = GUI.enabled;

            GUI.enabled = priorEnabled && cultureAvailable;
            if (Widgets.ButtonText(new Rect(inner.x, inner.y, width, inner.height),
                    (loreExpanded ? "• " : string.Empty)
                        + T("PawnDiary.Memory.Library.Dev.Culture")))
            {
                loreExpanded = !loreExpanded;
                diagnosticsExpanded = false;
                if (loreExpanded)
                {
                    session.narrowDetailOpen = true;
                    loreScroll = Vector2.zero;
                }
            }

            GUI.enabled = priorEnabled && memoryAvailable;
            if (Widgets.ButtonText(new Rect(inner.x + width + gap, inner.y, width, inner.height),
                    (diagnosticsExpanded ? "• " : string.Empty)
                        + T("PawnDiary.Memory.Library.Dev.Memory")))
            {
                diagnosticsExpanded = !diagnosticsExpanded;
                loreExpanded = false;
                if (diagnosticsExpanded) session.narrowDetailOpen = true;
            }

            GUI.enabled = priorEnabled && (loreExpanded || diagnosticsExpanded);
            if (Widgets.ButtonText(new Rect(inner.x + (width + gap) * 2f, inner.y,
                    Mathf.Max(40f, inner.xMax - inner.x - (width + gap) * 2f), inner.height),
                    T("PawnDiary.Memory.Library.Dev.Close")))
            {
                loreExpanded = false;
                diagnosticsExpanded = false;
            }
            GUI.enabled = priorEnabled;
        }

        private void DrawOwnerEmptyState(Rect rect)
        {
            bool canClear = owners?.ownerEmptyStateToken == "no_matches"
                && !string.IsNullOrWhiteSpace(session.ownerSearch);
            if (!canClear)
            {
                DrawCenteredState(rect, OwnerEmptyText());
                return;
            }
            DrawCenteredStateWithAction(rect, OwnerEmptyText(),
                T("PawnDiary.Memory.Library.ClearOwnerSearch"), ClearOwnerSearch);
        }

        private void DrawOwnerSearchNoMatches(Rect rect)
        {
            float buttonWidth = Mathf.Min(220f, Mathf.Max(120f, rect.width * 0.28f));
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x, rect.y,
                Mathf.Max(20f, rect.width - buttonWidth - 6f), rect.height),
                T("PawnDiary.Memory.Library.NoOwnerMatches"));
            GUI.color = Color.white;
            if (Widgets.ButtonText(new Rect(rect.xMax - buttonWidth, rect.y,
                    buttonWidth, rect.height),
                T("PawnDiary.Memory.Library.ClearOwnerSearch"))) ClearOwnerSearch();
        }

        private void ClearOwnerSearch()
        {
            session.ownerSearch = string.Empty;
            preferredOwnerId = string.Empty;
            ownerSearchDirty = true;
            repositoryNavigationDirty = true;
        }

        /// <summary>Draws a compact empty-state message plus one recovery action.</summary>
        private static void DrawCenteredStateWithAction(
            Rect rect, string message, string actionLabel, Action action)
        {
            float buttonHeight = 28f;
            float buttonWidth = Mathf.Min(220f, Mathf.Max(100f, rect.width * 0.44f));
            float totalHeight = Mathf.Min(rect.height, 58f);
            float top = rect.y + Mathf.Max(0f, (rect.height - totalHeight) * 0.5f);
            DrawCenteredState(new Rect(rect.x, top, rect.width,
                Mathf.Max(24f, totalHeight - buttonHeight - 6f)), message);
            Rect button = new Rect(rect.x + (rect.width - buttonWidth) * 0.5f,
                top + totalHeight - buttonHeight, buttonWidth, buttonHeight);
            if (Widgets.ButtonText(button, actionLabel ?? string.Empty)) action?.Invoke();
        }
    }
}

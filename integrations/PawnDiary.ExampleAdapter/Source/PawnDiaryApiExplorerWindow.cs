// The API Explorer window: a three-pane IMGUI layout (method tree | form | running result log)
// that lets a developer exercise every public PawnDiaryApi method without writing code. Opened from
// the [DebugAction] entry in RimWorld's Debug Actions menu (Dev mode → "Pawn Diary Example Adapter"
// → "Open API explorer…").
//
// LAYOUT
//   ┌──────────────────────────────────────────────────────────────┐
//   │ Title bar:  Subject ▾   Partner ▾    ApiVersion  Ready ●      │
//   ├────────────┬──────────────────────────────┬───────────────────┤
//   │ [search…]  │ method name + wrapped        │ Result log        │
//   │ ▼ category │ summary                      │ (append)          │
//   │   method   │ target: subject / partner    │  · entry lines    │
//   │ ▶ category │ ── form fields ──            │  · detail view    │
//   │ (scroll)   │ [Invoke]                     │ [Copy][Clear]     │
//   └────────────┴──────────────────────────────┴───────────────────┘
//
// The left tree has a live search box (filters method/summary/category, force-expands matches) and
// per-category collapse; leaf rows are left-aligned with a full-label+summary tooltip on hover.
//
// STATE: shared session state lives in ExplorerState (selected pawns, log, remembered handles,
// listener/provider ring buffers) so it survives closing and reopening the window. The window
// itself owns only the per-window transient UI state (scroll positions, current selection).
//
// New to C#/RimWorld? See AGENTS.md. For the broader "why a debug menu mod", see
// integrations/README.md.
using System.Collections.Generic;
using System.Text;
using PawnDiary.Integration;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiaryExampleAdapter
{
    /// <summary>
    /// Three-pane IMGUI window for ad-hoc testing of the entire PawnDiaryApi surface. Opened from a
    /// dev action; never appears in normal play.
    /// </summary>
    internal sealed class PawnDiaryApiExplorerWindow : Window
    {
        // Layout constants. Tiny/Small font line heights match the values the rest of Pawn Diary's
        // IMGUI uses (AGENTS.md "tiny text accessibility toggle" caveat — these are fixed for the
        // explorer since it is a dev tool, not player-facing).
        private const float DragHandleHeight = 22f;
        private const float HeaderHeight = 36f;
        private const float Gap = 6f;
        private const float TreePreferredWidth = 310f;
        private const float TreeMinWidth = 250f;
        private const float FormMinWidth = 390f;
        private const float LogPreferredWidth = 360f;
        private const float LogMinWidth = 300f;
        private const float RowHeight = 28f;
        private const float CategoryRowHeight = 30f;
        private const float SearchHeight = 26f;

        // Per-window transient UI state. Selection state and scroll positions are NOT shared —
        // closing the window resets them, which is the right behavior for a scratch testing tool.
        private readonly FormState form = new FormState();
        private Vector2 treeScroll;
        private Vector2 formScroll;
        private Vector2 logListScroll;
        private Vector2 logDetailScroll;
        private string selectedNodeId;     // category|label key into the catalog
        // Categories collapse independently; a category is open unless its name is in this set. A
        // fresh window shows everything expanded (empty set), which is the friendliest first view.
        private readonly HashSet<string> collapsedCategories = new HashSet<string>();
        // Live tree filter text. When non-empty, only matching leaves show and their categories are
        // force-expanded regardless of collapsedCategories.
        private string treeFilter = string.Empty;
        // Manual drag state. Verse's draggable flag is not reliable for this borderless developer
        // overlay, so the top strip moves windowRect directly while the mouse is held.
        private bool draggingWindow;
        private Vector2 dragOffset;

        public PawnDiaryApiExplorerWindow()
        {
            // Treat the explorer as a moveable debug overlay: it should not pause the map, dim the
            // screen, close when the tester clicks elsewhere, or block normal game UI/camera input
            // outside its own rectangle.
            doCloseX = true;
            forcePause = false;
            preventCameraMotion = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            resizeable = true;
            draggable = true;
            drawShadow = false;
            // Default to the first leaf so the form area isn't blank on first open.
            selectedNodeId = FirstLeafNodeId();
            form.ResetToDefaults();
        }

        public override Vector2 InitialSize
        {
            get
            {
                // Big enough for all three panes on a 1080p display; resizeable for smaller screens.
                return new Vector2(1180f, 760f);
            }
        }

        public override void WindowOnGUI()
        {
            base.WindowOnGUI();
            ContinueManualWindowDrag();
        }

        // --------------------------------------------------------------------------------------------
        // Main layout
        // --------------------------------------------------------------------------------------------

        public override void DoWindowContents(Rect inRect)
        {
            // Readiness guard: if the core mod isn't ready (no game, master toggle off), show why
            // instead of letting every invoke return null/false silently.
            Text.Font = GameFont.Small;

            float y = 0f;
            DrawDragHandle(new Rect(inRect.x, y, inRect.width, DragHandleHeight));
            y += DragHandleHeight + Gap;

            DrawHeader(new Rect(inRect.x, y, inRect.width, HeaderHeight));
            y += HeaderHeight + Gap;

            Rect body = new Rect(inRect.x, y, inRect.width, inRect.height - y);
            DrawThreePanes(body);
        }

        private void DrawDragHandle(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.28f));
            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.82f, 0.82f, 0.82f);
            Widgets.Label(rect, "PawnDiaryExampleAdapter.Window.DragHint".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            HandleManualWindowDrag(rect);
        }

        private void HandleManualWindowDrag(Rect dragRect)
        {
            Event ev = Event.current;
            if (ev == null)
            {
                return;
            }

            if (ev.type == EventType.MouseDown && ev.button == 0 && Mouse.IsOver(dragRect))
            {
                draggingWindow = true;
                dragOffset = UI.MousePositionOnUIInverted - new Vector2(windowRect.x, windowRect.y);
                ev.Use();
            }
        }

        private void ContinueManualWindowDrag()
        {
            if (!draggingWindow)
            {
                return;
            }

            Event ev = Event.current;
            if (ev == null)
            {
                return;
            }

            if (ev.rawType == EventType.MouseUp || ev.type == EventType.MouseUp)
            {
                draggingWindow = false;
                return;
            }

            Vector2 mouse = UI.MousePositionOnUIInverted;
            float x = Mathf.Clamp(mouse.x - dragOffset.x, -windowRect.width + 120f, UI.screenWidth - 120f);
            float y = Mathf.Clamp(mouse.y - dragOffset.y, 0f, UI.screenHeight - 48f);
            windowRect = new Rect(x, y, windowRect.width, windowRect.height);

            if (ev.type == EventType.MouseDrag)
            {
                ev.Use();
            }
        }

        private void DrawHeader(Rect rect)
        {
            // Pawn pickers on the left, readiness badge on the right.
            List<Pawn> pool = ExplorerPawns.EligiblePawns();
            Pawn subject = ExplorerState.ResolveSubjectPawn();
            Pawn partner = ExplorerState.ResolvePartnerPawn(subject);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;

            float badgeWidth = Mathf.Min(300f, Mathf.Max(220f, rect.width * 0.24f));
            float pickerAreaWidth = Mathf.Max(0f, rect.width - badgeWidth - Gap);
            float pickerWidth = Mathf.Max(180f, (pickerAreaWidth - Gap) * 0.5f);
            DrawPawnPicker(new Rect(rect.x, rect.y + 4f, pickerWidth, RowHeight), pool, subject,
                "PawnDiaryExampleAdapter.Header.Subject", isPartner: false);
            DrawPawnPicker(new Rect(rect.x + pickerWidth + Gap, rect.y + 4f, pickerWidth, RowHeight),
                pool, partner, "PawnDiaryExampleAdapter.Header.Partner", isPartner: true, exclude: subject);

            // Readiness badge.
            Rect badge = new Rect(rect.xMax - badgeWidth, rect.y + 4f, badgeWidth, RowHeight);
            DrawReadinessBadge(badge);

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawThreePanes(Rect rect)
        {
            float treeWidth = Mathf.Clamp(rect.width * 0.25f, TreeMinWidth, TreePreferredWidth);
            float logWidth = Mathf.Clamp(rect.width * 0.28f, LogMinWidth, LogPreferredWidth);
            float formWidth = rect.width - treeWidth - logWidth - Gap * 2f;

            if (formWidth < FormMinWidth)
            {
                float shortage = FormMinWidth - formWidth;
                float logReduction = Mathf.Min(shortage, Mathf.Max(0f, logWidth - LogMinWidth));
                logWidth -= logReduction;
                shortage -= logReduction;

                float treeReduction = Mathf.Min(shortage, Mathf.Max(0f, treeWidth - TreeMinWidth));
                treeWidth -= treeReduction;
                formWidth = rect.width - treeWidth - logWidth - Gap * 2f;
            }

            formWidth = Mathf.Max(240f, formWidth);
            float formX = rect.x + treeWidth + Gap;
            float logX = formX + formWidth + Gap;

            Rect treeRect = new Rect(rect.x, rect.y, treeWidth, rect.height);
            Rect formRect = new Rect(formX, rect.y, formWidth, rect.height);
            Rect logRect = new Rect(logX, rect.y, Mathf.Max(0f, rect.xMax - logX), rect.height);

            DrawMethodTree(treeRect);
            DrawFormPane(formRect);
            DrawResultLog(logRect);
        }

        // --------------------------------------------------------------------------------------------
        // Left pane: method tree
        // --------------------------------------------------------------------------------------------

        private void DrawMethodTree(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(4f);

            // Search box pinned to the top; the scrollable category list fills the rest.
            DrawTreeSearch(new Rect(inner.x, inner.y, inner.width, SearchHeight));
            Rect listRect = new Rect(inner.x, inner.y + SearchHeight + 4f, inner.width, inner.height - SearchHeight - 4f);

            string filter = (treeFilter ?? string.Empty).Trim();
            bool filtering = filter.Length > 0;

            // Group nodes by category preserving catalog order, dropping leaves that fail the filter.
            List<string> categories = new List<string>();
            Dictionary<string, List<ExplorerMethodNode>> byCategory = new Dictionary<string, List<ExplorerMethodNode>>();
            foreach (ExplorerMethodNode node in ExplorerMethodCatalog.Nodes)
            {
                if (filtering && !MatchesFilter(node, filter))
                {
                    continue;
                }

                if (!byCategory.TryGetValue(node.category, out List<ExplorerMethodNode> bucket))
                {
                    bucket = new List<ExplorerMethodNode>();
                    byCategory[node.category] = bucket;
                    categories.Add(node.category);
                }

                bucket.Add(node);
            }

            if (categories.Count == 0)
            {
                // Filter matched nothing — say so instead of showing a blank column.
                Widgets.Label(listRect, "PawnDiaryExampleAdapter.Tree.NoMatches".Translate(filter));
                return;
            }

            float viewWidth = Mathf.Max(120f, inner.width - 16f);
            float contentHeight = 0f;
            foreach (string cat in categories)
            {
                contentHeight += CategoryRowHeight; // category header
                if (IsCategoryOpen(cat, filtering))
                {
                    foreach (ExplorerMethodNode node in byCategory[cat])
                    {
                        contentHeight += TreeLeafHeight(node, viewWidth);
                    }
                }
            }

            Rect view = new Rect(0f, 0f, viewWidth, contentHeight);
            Widgets.BeginScrollView(listRect, ref treeScroll, view);

            float y = 0f;
            foreach (string cat in categories)
            {
                List<ExplorerMethodNode> leaves = byCategory[cat];
                bool open = IsCategoryOpen(cat, filtering);

                // Category header row — collapses just this category (disabled while filtering, since
                // a filter force-expands everything so results stay visible).
                Rect headerRow = new Rect(0f, y, view.width, CategoryRowHeight);
                if (Widgets.ButtonText(headerRow, (open ? "▼ " : "▶ ") + cat + "  (" + leaves.Count + ")") && !filtering)
                {
                    if (!collapsedCategories.Remove(cat))
                    {
                        collapsedCategories.Add(cat);
                    }
                }

                y += CategoryRowHeight;

                if (!open)
                {
                    continue;
                }

                foreach (ExplorerMethodNode node in leaves)
                {
                    float leafHeight = TreeLeafHeight(node, view.width);
                    Rect leafRow = new Rect(0f, y, view.width, leafHeight);
                    string id = NodeId(node);
                    bool selected = id == selectedNodeId;
                    if (selected)
                    {
                        Widgets.DrawHighlightSelected(leafRow);
                    }
                    else if (Mouse.IsOver(leafRow))
                    {
                        Widgets.DrawHighlight(leafRow);
                    }

                    DrawTreeLeafText(leafRow, node);

                    TooltipHandler.TipRegion(leafRow, node.label + "\n\n" + node.summary);
                    if (Widgets.ButtonInvisible(leafRow))
                    {
                        selectedNodeId = id;
                    }

                    y += leafHeight;
                }
            }

            Widgets.EndScrollView();
        }

        private static float TreeLeafHeight(ExplorerMethodNode node, float width)
        {
            float textWidth = Mathf.Max(80f, width - 18f);
            GameFont oldFont = Text.Font;

            Text.Font = GameFont.Small;
            float labelHeight = Mathf.Min(44f, Mathf.Max(22f, Text.CalcHeight(node.label, textWidth)));

            Text.Font = GameFont.Tiny;
            float summaryHeight = Mathf.Min(36f, Mathf.Max(18f, Text.CalcHeight(node.summary, textWidth)));

            Text.Font = oldFont;
            return labelHeight + summaryHeight + 10f;
        }

        private static void DrawTreeLeafText(Rect row, ExplorerMethodNode node)
        {
            float x = row.x + 14f;
            float w = row.width - 18f;

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            float labelHeight = Mathf.Min(44f, Mathf.Max(22f, Text.CalcHeight(node.label, w)));
            Rect labelRect = new Rect(x, row.y + 4f, w, labelHeight);
            Widgets.Label(labelRect, node.label);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.72f, 0.72f);
            Rect summaryRect = new Rect(x, labelRect.yMax, w, Mathf.Max(18f, row.yMax - labelRect.yMax - 3f));
            Widgets.Label(summaryRect, node.summary);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        // A category is open unless the user collapsed it; an active filter forces every surviving
        // category open so matches are never hidden behind a collapsed header.
        private bool IsCategoryOpen(string category, bool filtering)
        {
            return filtering || !collapsedCategories.Contains(category);
        }

        private void DrawTreeSearch(Rect rect)
        {
            bool hasText = !string.IsNullOrEmpty(treeFilter);
            float clearWidth = hasText ? 22f : 0f;
            Rect fieldRect = new Rect(rect.x, rect.y, rect.width - clearWidth - (hasText ? 2f : 0f), rect.height);
            treeFilter = Widgets.TextField(fieldRect, treeFilter ?? string.Empty);

            if (!hasText)
            {
                // Grey placeholder while empty; it vanishes as soon as the tester types.
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(fieldRect.x + 6f, fieldRect.y, fieldRect.width - 6f, fieldRect.height),
                    "PawnDiaryExampleAdapter.Tree.SearchPlaceholder".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
            else
            {
                Rect clearBtn = new Rect(fieldRect.xMax + 2f, rect.y, clearWidth, rect.height);
                TooltipHandler.TipRegion(clearBtn, "PawnDiaryExampleAdapter.Tree.ClearFilter".Translate());
                if (Widgets.ButtonText(clearBtn, "×"))
                {
                    treeFilter = string.Empty;
                    UI.UnfocusCurrentControl(); // drop the caret so the cleared field isn't left focused
                }
            }
        }

        // Case-insensitive substring over label, summary, and category so a tester can search by
        // method name ("bundle"), by concept ("style"), or by group ("read").
        private static bool MatchesFilter(ExplorerMethodNode node, string filter)
        {
            string f = filter.ToLowerInvariant();
            return (node.label != null && node.label.ToLowerInvariant().Contains(f))
                || (node.summary != null && node.summary.ToLowerInvariant().Contains(f))
                || (node.category != null && node.category.ToLowerInvariant().Contains(f));
        }

        // --------------------------------------------------------------------------------------------
        // Middle pane: form for the selected method + Invoke button
        // --------------------------------------------------------------------------------------------

        private void DrawFormPane(Rect rect)
        {
            ExplorerMethodNode node = FindNode(selectedNodeId);
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f, 1f));
            Rect inner = rect.ContractedBy(8f);

            if (node == null)
            {
                Widgets.Label(inner, "PawnDiaryExampleAdapter.Form.PickMethod".Translate());
                return;
            }

            // Title + wrapped summary at the top (measured, not fixed — the SubmitEvent summary wraps
            // to several lines and used to clip into the Invoke button). Scrollable form below.
            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(inner.x, inner.y, inner.width, Text.CalcHeight(node.label, inner.width));
            float titleHeight = titleRect.height;
            Widgets.Label(titleRect, node.label);
            Text.Font = GameFont.Small;

            float cursorY = inner.y + titleHeight + 2f;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            float summaryHeight = Text.CalcHeight(node.summary, inner.width);
            Rect summaryRect = new Rect(inner.x, cursorY, inner.width, summaryHeight);
            Widgets.Label(summaryRect, node.summary);
            GUI.color = Color.white;
            DrawMethodHelpPopover(new Rect(inner.x, inner.y, inner.width, titleHeight + 2f + summaryHeight), node);
            cursorY += summaryHeight + 4f;

            // Which pawns this call targets. Most methods take no visible argument, so without this
            // line the header pickers are the only clue about who "Invoke" acts on.
            Pawn subj = ExplorerState.ResolveSubjectPawn();
            Pawn part = ExplorerState.ResolvePartnerPawn(subj);
            string partnerLabel = part == null
                ? (string)"PawnDiaryExampleAdapter.Header.None".Translate()
                : ExplorerPawns.LabelOrEmpty(part);
            GUI.color = new Color(0.55f, 0.72f, 0.95f);
            Widgets.Label(new Rect(inner.x, cursorY, inner.width, 22f),
                "PawnDiaryExampleAdapter.Form.Target".Translate(ExplorerPawns.LabelOrEmpty(subj), partnerLabel));
            GUI.color = Color.white;
            cursorY += 26f;

            Rect buttonRow = new Rect(inner.x, cursorY, inner.width, 32f);
            Rect formArea = new Rect(inner.x, cursorY + 40f, inner.width, Mathf.Max(0f, inner.yMax - (cursorY + 40f)));

            float resetWidth = buttonRow.width >= 220f ? Mathf.Min(150f, buttonRow.width * 0.28f) : 0f;
            Rect invokeButton = resetWidth > 0f
                ? new Rect(buttonRow.x, buttonRow.y, buttonRow.width - resetWidth - Gap, buttonRow.height)
                : buttonRow;
            Rect resetButton = new Rect(invokeButton.xMax + Gap, buttonRow.y, resetWidth, buttonRow.height);

            if (Widgets.ButtonText(invokeButton, "PawnDiaryExampleAdapter.Form.Invoke".Translate()))
            {
                InvokeSelected(node);
            }

            if (resetWidth > 0f && Widgets.ButtonText(resetButton, "PawnDiaryExampleAdapter.Form.Reset".Translate()))
            {
                form.ResetToDefaults();
                formScroll = Vector2.zero;
            }

            // Form content (scrollable when the form is long, e.g. the event-request form).
            float formHeight = ExplorerMethodCatalog.EstimateFormHeight(node, form, formArea.width - 16f);
            Rect formView = new Rect(0f, 0f, formArea.width - 16f, formHeight);
            Widgets.BeginScrollView(formArea, ref formScroll, formView);
            try
            {
                node.drawForm?.Invoke(form, formView);
            }
            catch (System.Exception e)
            {
                Widgets.Label(formView, "Form draw failed: " + e.Message);
            }
            Widgets.EndScrollView();
        }

        // --------------------------------------------------------------------------------------------
        // Right pane: append-only result log + selected detail view
        // --------------------------------------------------------------------------------------------

        private void DrawResultLog(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(4f);

            // The list grows with history but stays compact for a short log, leaving the detail panel
            // useful after the first few invocations instead of parking it halfway down the pane.
            float desiredListHeight = ExplorerState.Log.Count == 0 ? 80f : ExplorerState.Log.Count * 24f + 12f;
            float listHeight = Mathf.Min(inner.height * 0.35f, Mathf.Max(80f, desiredListHeight));
            Rect listArea = new Rect(inner.x, inner.y, inner.width, listHeight);
            Rect detailArea = new Rect(inner.x, inner.y + listHeight + Gap, inner.width, inner.height - listHeight - Gap - 28f);
            Rect buttonArea = new Rect(inner.x, inner.yMax - 24f, inner.width, 24f);

            DrawLogList(listArea);
            DrawLogDetail(detailArea);

            if (Widgets.ButtonText(new Rect(buttonArea.x, buttonArea.y, buttonArea.width * 0.5f - 2f, buttonArea.height),
                "PawnDiaryExampleAdapter.Log.Copy".Translate()))
            {
                CopySelectedOrLatest();
            }

            if (Widgets.ButtonText(new Rect(buttonArea.x + buttonArea.width * 0.5f + 2f, buttonArea.y, buttonArea.width * 0.5f - 2f, buttonArea.height),
                "PawnDiaryExampleAdapter.Log.Clear".Translate()))
            {
                ExplorerState.ClearLog();
            }
        }

        private void DrawLogList(Rect area)
        {
            if (ExplorerState.Log.Count == 0)
            {
                Widgets.Label(area, "PawnDiaryExampleAdapter.Log.Empty".Translate());
                return;
            }

            float contentH = ExplorerState.Log.Count * 22f;
            Rect view = new Rect(0f, 0f, area.width - 16f, contentH);
            Widgets.BeginScrollView(area, ref logListScroll, view);

            float y = 0f;
            // Walk oldest→newest so newest lands at the bottom (matches a chat-style log).
            for (int i = 0; i < ExplorerState.Log.Count; i++)
            {
                ExplorerLogEntry entry = ExplorerState.Log[i];
                Rect row = new Rect(0f, y, view.width, 22f);
                bool selected = i == ExplorerState.selectedLogIndex;
                if (selected)
                {
                    Widgets.DrawHighlightSelected(row);
                }
                else if (Mouse.IsOver(row))
                {
                    Widgets.DrawHighlight(row);
                }

                // The row already shows the method name in blue, so strip a duplicate leading copy
                // from the one-line result (e.g. "IsReady → True" → "→ True"). Colour the remainder by
                // outcome (green success / orange failure-or-nothing / grey neutral).
                string result = StripMethodPrefix(entry.oneLineResult, entry.methodName);
                string line = "<color=#88ccff>[" + i + "] " + entry.methodName + "</color>  "
                    + LogResultColorTag(entry.oneLineResult) + result + "</color>";
                Widgets.Label(row, line);
                if (Widgets.ButtonInvisible(row))
                {
                    ExplorerState.selectedLogIndex = i;
                }

                y += 22f;
            }

            Widgets.EndScrollView();

            // Auto-scroll to bottom when a new entry is appended.
            if (ExplorerState.selectedLogIndex == ExplorerState.Log.Count - 1)
            {
                logListScroll.y = Mathf.Max(0f, contentH - area.height + 16f);
            }
        }

        private void DrawLogDetail(Rect area)
        {
            ExplorerLogEntry entry = (ExplorerState.selectedLogIndex >= 0 && ExplorerState.selectedLogIndex < ExplorerState.Log.Count)
                ? ExplorerState.Log[ExplorerState.selectedLogIndex]
                : null;

            if (entry == null)
            {
                Widgets.Label(area, "PawnDiaryExampleAdapter.Log.NoSelection".Translate());
                return;
            }

            // Calc the rendered height of the multi-line detail so the scroll view can scroll.
            float detailHeight = Text.CalcHeight(entry.detail, area.width - 16f);
            Rect view = new Rect(0f, 0f, area.width - 16f, Mathf.Max(detailHeight, area.height));
            Widgets.BeginScrollView(area, ref logDetailScroll, view);

            // Use a fixed-width box so long lines wrap rather than running off the panel.
            Widgets.Label(view, entry.detail);

            Widgets.EndScrollView();
        }

        // --------------------------------------------------------------------------------------------
        // Helpers: pawn pickers, readiness badge, node lookup
        // --------------------------------------------------------------------------------------------

        private void DrawPawnPicker(Rect rect, List<Pawn> pool, Pawn current, string labelKey, bool isPartner, Pawn exclude = null)
        {
            // Label + button. The button opens a FloatMenu of eligible pawns (and "(none)" for partner).
            string label = labelKey.Translate();
            float labelWidth = Mathf.Min(96f, Mathf.Max(64f, Text.CalcSize(label).x + 8f));
            Widgets.Label(new Rect(rect.x, rect.y, labelWidth, rect.height), label);
            Rect btn = new Rect(rect.x + labelWidth + 6f, rect.y, Mathf.Max(80f, rect.width - labelWidth - 6f), rect.height);
            // Translate() returns TaggedString; coerce to string so the ternary has matching types.
            string noneLabel = "PawnDiaryExampleAdapter.Header.None".Translate();
            string currentLabel = current == null ? noneLabel : current.LabelShortCap;
            if (Widgets.ButtonText(btn, currentLabel))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                if (isPartner)
                {
                    opts.Add(new FloatMenuOption("PawnDiaryExampleAdapter.Header.None".Translate(), () =>
                    {
                        ExplorerState.selectedPartnerPawnId = null;
                    }));
                }

                for (int i = 0; i < pool.Count; i++)
                {
                    Pawn p = pool[i];
                    if (p == null || p == exclude)
                    {
                        continue;
                    }

                    Pawn captured = p;
                    opts.Add(new FloatMenuOption(captured.LabelShortCap, () =>
                    {
                        if (isPartner)
                        {
                            ExplorerState.selectedPartnerPawnId = captured.GetUniqueLoadID();
                        }
                        else
                        {
                            ExplorerState.selectedSubjectPawnId = captured.GetUniqueLoadID();
                        }
                    }));
                }

                Find.WindowStack.Add(new FloatMenu(opts));
            }
        }

        private void DrawReadinessBadge(Rect rect)
        {
            bool ready = PawnDiaryExampleApi.IsReady;
            bool enabled = PawnDiaryExampleApi.IsExternalApiEnabled;
            string statusKey = !ready
                ? "PawnDiaryExampleAdapter.Status.NotReady"
                : (enabled ? "PawnDiaryExampleAdapter.Status.Ready" : "PawnDiaryExampleAdapter.Status.Disabled");
            string label = "API v" + PawnDiaryExampleApi.ApiVersion + "  " + statusKey.Translate();
            GUI.color = ready && enabled ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.6f, 0.4f);
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.4f));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rect, "PawnDiaryExampleAdapter.Status.Tooltip".Translate(
                ready ? "true" : "false",
                enabled ? "true" : "false"));
        }

        private void InvokeSelected(ExplorerMethodNode node)
        {
            if (node?.invoke == null)
            {
                return;
            }

            if (!PawnDiaryExampleApi.IsExternalApiEnabled && node.category != "Readiness")
            {
                string message = "PawnDiaryExampleAdapter.Log.ApiDisabled".Translate();
                ExplorerState.AppendLog(node.label, message, message);
                return;
            }

            try
            {
                node.invoke(form);
            }
            catch (System.Exception e)
            {
                ExplorerState.AppendLog(node.label, "EXCEPTION", "Invoke threw:\n" + e);
            }
        }

        private static void DrawMethodHelpPopover(Rect rect, ExplorerMethodNode node)
        {
            if (node == null)
            {
                return;
            }

            TooltipHandler.TipRegion(rect, "PawnDiaryExampleAdapter.Help.MethodBody".Translate(node.category, node.summary));
        }

        // --------------------------------------------------------------------------------------------
        // Node identity / lookup
        // --------------------------------------------------------------------------------------------

        private static string NodeId(ExplorerMethodNode node)
        {
            return node.category + "|" + node.label;
        }

        private ExplorerMethodNode FindNode(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (ExplorerMethodNode node in ExplorerMethodCatalog.Nodes)
            {
                if (NodeId(node) == id)
                {
                    return node;
                }
            }

            return null;
        }

        private static string FirstLeafNodeId()
        {
            // The first node in the catalog (IsReady) — selected by default so the form area isn't empty.
            return ExplorerMethodCatalog.Nodes.Count > 0 ? NodeId(ExplorerMethodCatalog.Nodes[0]) : null;
        }

        /// <summary>
        /// Truncates long method labels for the narrow tree column; the full label shows in the form
        /// header. Keeps the parens so the parameter shape is still recognizable.
        /// </summary>
        private static string ShortLabel(string label)
        {
            if (string.IsNullOrEmpty(label) || label.Length <= 28)
            {
                return label;
            }

            return label.Substring(0, 27) + "…";
        }

        /// <summary>
        /// Drops a leading copy of the method name from the one-line result, since the log row already
        /// prints the method name in blue right before it. Many formatters echo the method
        /// ("IsReady → True", "SubmitEvent → recorded=…"); this removes the redundancy. Leaves results
        /// that don't start with the method name untouched.
        /// </summary>
        private static string StripMethodPrefix(string oneLine, string methodName)
        {
            if (string.IsNullOrEmpty(oneLine))
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(methodName) && oneLine.StartsWith(methodName))
            {
                string rest = oneLine.Substring(methodName.Length).TrimStart();
                return rest.Length > 0 ? rest : oneLine;
            }

            return oneLine;
        }

        /// <summary>
        /// Picks a rich-text colour tag for a one-line result: green for a clear success, orange for a
        /// failure or an empty/absent result, grey otherwise. A lightweight cue, not authoritative —
        /// the detail panel always has the full picture.
        /// </summary>
        private static string LogResultColorTag(string oneLine)
        {
            if (string.IsNullOrEmpty(oneLine))
            {
                return "<color=#cccccc>";
            }

            string l = oneLine.ToLowerInvariant();
            if (l.Contains("exception") || l.Contains("recorded=false") || l.Contains("no pawn")
                || l.Contains("no handle") || l.Contains("null"))
            {
                return "<color=#ff9a6b>"; // orange: failed, declined, or nothing to show
            }

            if (l.Contains("recorded=true") || l.Contains("→ true") || l.Contains("ok=true") || l.Contains("= true"))
            {
                return "<color=#7dff7d>"; // green: recorded / true / ok
            }

            return "<color=#cccccc>"; // neutral (counts, statuses, etc.)
        }

        private void CopySelectedOrLatest()
        {
            string text;
            if (ExplorerState.selectedLogIndex >= 0 && ExplorerState.selectedLogIndex < ExplorerState.Log.Count)
            {
                ExplorerLogEntry e = ExplorerState.Log[ExplorerState.selectedLogIndex];
                text = "[" + e.methodName + "]\n" + e.oneLineResult + "\n\n" + e.detail;
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                foreach (ExplorerLogEntry e in ExplorerState.Log)
                {
                    sb.Append('[').Append(e.methodName).Append("] ").Append(e.oneLineResult).Append('\n');
                }
                text = sb.ToString();
            }

            // GUIUtility.systemCopyBuffer is the Unity IMGUI clipboard; works without a focused text field.
            GUIUtility.systemCopyBuffer = text ?? string.Empty;
        }
    }

    /// <summary>
    /// Extension on FormState that resets every field to a sensible default so the explorer works
    /// with zero typing on first open. Lives here (not on FormState itself) so the field bag in
    /// ExplorerMethodCatalog stays a plain data holder without inter-file dependencies.
    /// </summary>
    internal static class FormStateDefaultExtensions
    {
        /// <summary>
        /// Restores all FormState fields to the documented defaults. Called once on window creation.
        /// </summary>
        public static void ResetToDefaults(this FormState f)
        {
            FormState defaults = new FormState();

            f.sourceId = defaults.sourceId;
            f.povRole = defaults.povRole;
            f.maxCount = defaults.maxCount;
            f.eventKey = defaults.eventKey;
            f.summaryText = defaults.summaryText;
            f.eventLabel = defaults.eventLabel;
            f.extraContext = defaults.extraContext;
            f.promptFragment = defaults.promptFragment;
            f.enchantmentCandidates = defaults.enchantmentCandidates;
            f.enchantmentMode = defaults.enchantmentMode;
            f.forceRecord = defaults.forceRecord;
            f.dedupKey = defaults.dedupKey;
            f.dedupTicks = defaults.dedupTicks;
            f.promptInstruction = defaults.promptInstruction;
            f.directText = defaults.directText;
            f.directPartnerText = defaults.directPartnerText;
            f.directTitle = defaults.directTitle;
            f.directPartnerTitle = defaults.directPartnerTitle;
            f.directGenerateTitle = defaults.directGenerateTitle;
            f.styleSourceId = defaults.styleSourceId;
            f.styleRule = defaults.styleRule;
            f.manualEventId = defaults.manualEventId;
            f.manualPovRole = defaults.manualPovRole;
            f.qDomain = defaults.qDomain;
            f.qAtmosphereCue = defaults.qAtmosphereCue;
            f.qPovRole = defaults.qPovRole;
            f.qSourceId = defaults.qSourceId;
            f.qEventKey = defaults.qEventKey;
            f.qDateContains = defaults.qDateContains;
            f.qIncludeActive = defaults.qIncludeActive;
            f.qIncludeArchived = defaults.qIncludeArchived;
            f.qImportant = defaults.qImportant;
            f.qHasTitle = defaults.qHasTitle;
            f.qHasGeneratedText = defaults.qHasGeneratedText;
            f.bundleIncludeImportant = defaults.bundleIncludeImportant;
            f.bundleUseQuery = defaults.bundleUseQuery;
            f.bundleUseImportant = defaults.bundleUseImportant;
            f.setGenEnabled = defaults.setGenEnabled;
            f.enchantIncludeImportant = defaults.enchantIncludeImportant;
            f.useRememberedHandle = defaults.useRememberedHandle;
            f.rememberedHandleIndex = defaults.rememberedHandleIndex;
            f.applyPawnQuery = defaults.applyPawnQuery;
        }
    }
}

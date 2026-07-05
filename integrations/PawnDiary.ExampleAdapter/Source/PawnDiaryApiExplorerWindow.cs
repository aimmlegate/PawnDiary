// The API Explorer window: a three-pane IMGUI layout (method tree | form | running result log)
// that lets a developer exercise every public PawnDiaryApi method without writing code. Opened from
// the [DebugAction] entry in RimWorld's Debug Actions menu (Dev mode → "Pawn Diary Example Adapter"
// → "Open API explorer…").
//
// LAYOUT
//   ┌──────────────────────────────────────────────────────────────┐
//   │ Title bar:  Subject ▾   Partner ▾    ApiVersion  Ready ●      │
//   ├────────────┬──────────────────────────────┬───────────────────┤
//   │ method     │ method name + one-line       │ Result log        │
//   │ tree       │ summary                      │ (append)          │
//   │ (scroll)   │ ── form fields ──            │  · entry lines    │
//   │            │ [Invoke]   [Preview]         │  · detail view    │
//   │            │                              │ [Copy][Clear]     │
//   └────────────┴──────────────────────────────┴───────────────────┘
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
        private const float HeaderHeight = 36f;
        private const float Gap = 6f;
        private const float TreeWidth = 220f;
        private const float LogWidth = 360f;
        private const float RowHeight = 28f;

        // Per-window transient UI state. Selection state and scroll positions are NOT shared —
        // closing the window resets them, which is the right behavior for a scratch testing tool.
        private readonly FormState form = new FormState();
        private Vector2 treeScroll;
        private Vector2 formScroll;
        private Vector2 logListScroll;
        private Vector2 logDetailScroll;
        private string selectedNodeId;     // category|label key into the catalog
        private bool treeExpanded = true;  // single expand/collapse for all categories (simple)

        public PawnDiaryApiExplorerWindow()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            resizeable = true;
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

        // --------------------------------------------------------------------------------------------
        // Main layout
        // --------------------------------------------------------------------------------------------

        public override void DoWindowContents(Rect inRect)
        {
            // Readiness guard: if the core mod isn't ready (no game, master toggle off), show why
            // instead of letting every invoke return null/false silently.
            Text.Font = GameFont.Small;

            float y = 0f;
            DrawHeader(new Rect(inRect.x, y, inRect.width, HeaderHeight));
            y += HeaderHeight + Gap;

            Rect body = new Rect(inRect.x, y, inRect.width, inRect.height - y);
            DrawThreePanes(body);
        }

        private void DrawHeader(Rect rect)
        {
            // Pawn pickers on the left, readiness badge on the right.
            List<Pawn> pool = ExplorerPawns.EligiblePawns();
            Pawn subject = ExplorerState.ResolveSubjectPawn();
            Pawn partner = ExplorerState.ResolvePartnerPawn(subject);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;

            float pickerWidth = (rect.width - 320f) * 0.5f;
            DrawPawnPicker(new Rect(rect.x, rect.y + 4f, pickerWidth, RowHeight), pool, subject,
                "PawnDiaryExampleAdapter.Header.Subject", isPartner: false);
            DrawPawnPicker(new Rect(rect.x + pickerWidth + Gap, rect.y + 4f, pickerWidth, RowHeight),
                pool, partner, "PawnDiaryExampleAdapter.Header.Partner", isPartner: true, exclude: subject);

            // Readiness badge.
            Rect badge = new Rect(rect.xMax - 300f, rect.y + 4f, 300f, RowHeight);
            DrawReadinessBadge(badge);

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawThreePanes(Rect rect)
        {
            float formX = rect.x + TreeWidth + Gap;
            float formWidth = rect.width - TreeWidth - LogWidth - Gap * 2f;
            float logX = formX + formWidth + Gap;

            Rect treeRect = new Rect(rect.x, rect.y, TreeWidth, rect.height);
            Rect formRect = new Rect(formX, rect.y, formWidth, rect.height);
            Rect logRect = new Rect(logX, rect.y, LogWidth, rect.height);

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

            // Group nodes by category preserving catalog order.
            List<string> categories = new List<string>();
            Dictionary<string, List<ExplorerMethodNode>> byCategory = new Dictionary<string, List<ExplorerMethodNode>>();
            foreach (ExplorerMethodNode node in ExplorerMethodCatalog.Nodes)
            {
                if (!byCategory.TryGetValue(node.category, out List<ExplorerMethodNode> bucket))
                {
                    bucket = new List<ExplorerMethodNode>();
                    byCategory[node.category] = bucket;
                    categories.Add(node.category);
                }

                bucket.Add(node);
            }

            float contentHeight = 0f;
            foreach (string cat in categories)
            {
                contentHeight += RowHeight; // category header
                if (treeExpanded)
                {
                    contentHeight += RowHeight * byCategory[cat].Count;
                }
            }

            Rect view = new Rect(0f, 0f, inner.width - 16f, contentHeight);
            Widgets.BeginScrollView(inner, ref treeScroll, view);

            float y = 0f;
            foreach (string cat in categories)
            {
                // Category header row — clickable to expand/collapse all.
                Rect headerRow = new Rect(0f, y, view.width, RowHeight);
                if (Widgets.ButtonText(headerRow, (treeExpanded ? "▼ " : "▶ ") + cat))
                {
                    treeExpanded = !treeExpanded;
                }

                y += RowHeight;

                if (!treeExpanded)
                {
                    continue;
                }

                foreach (ExplorerMethodNode node in byCategory[cat])
                {
                    Rect leafRow = new Rect(8f, y, view.width - 8f, RowHeight);
                    string id = NodeId(node);
                    bool selected = id == selectedNodeId;
                    if (selected)
                    {
                        Widgets.DrawHighlight(leafRow);
                    }

                    string display = ShortLabel(node.label);
                    if (Widgets.ButtonText(leafRow, display, !selected, true, true))
                    {
                        selectedNodeId = id;
                    }

                    y += RowHeight;
                }
            }

            Widgets.EndScrollView();
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

            // Title + summary at the top (fixed), scrollable form below.
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 30f), node.label);
            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inner.x, inner.y + 30f, inner.width, 36f), node.summary);
            GUI.color = Color.white;

            Rect buttonRow = new Rect(inner.x, inner.y + 70f, inner.width, 32f);
            Rect formArea = new Rect(inner.x, inner.y + 110f, inner.width, inner.height - 110f);

            if (Widgets.ButtonText(new Rect(buttonRow.x, buttonRow.y, buttonRow.width, buttonRow.height),
                "PawnDiaryExampleAdapter.Form.Invoke".Translate()))
            {
                InvokeSelected(node);
            }

            // Form content (scrollable when the form is long, e.g. the event-request form).
            float formHeight = EstimateFormHeight(node);
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

            // Top half: list of log entries (clickable to select for the detail view).
            float listHeight = inner.height * 0.35f;
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
                    Widgets.DrawHighlight(row);
                }

                string colorPrefix = entry.oneLineResult != null && entry.oneLineResult.Contains("recorded=True") ? "<color=#7dff7d>" : "<color=#cccccc>";
                string line = "<color=#88ccff>[" + i + "] " + entry.methodName + "</color>  " + colorPrefix + entry.oneLineResult + "</color>";
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
            Widgets.Label(new Rect(rect.x, rect.y, 70f, rect.height), labelKey.Translate());
            Rect btn = new Rect(rect.x + 76f, rect.y, rect.width - 76f, rect.height);
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
            bool ready = PawnDiaryApi.IsReady;
            string label = "API v" + PawnDiaryApi.ApiVersion + "  " + (ready ? "● ready" : "○ not ready");
            GUI.color = ready ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.6f, 0.4f);
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.4f));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void InvokeSelected(ExplorerMethodNode node)
        {
            if (node?.invoke == null)
            {
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
        /// Rough height estimate so the form scroll view knows its content size without measuring
        /// every widget. Methods with the long event-request form get extra room; short forms less.
        /// </summary>
        private static float EstimateFormHeight(ExplorerMethodNode node)
        {
            if (node == null) return 0f;
            // Each TextFieldEntryLabeled / Checkbox row is ~30px; the event-request form has ~10 rows.
            switch (node.category)
            {
                case "Submit":       return 360f;
                case "Prompt Entry": return 220f;
                case "Direct Entry": return 280f;
                case "Read Pawn":    return node.label.Contains("query") ? 520f : 80f;
                case "Hooks":        return 80f;
                case "Style":        return 100f;
                default:             return 80f;
            }
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
            // The fields are already initialized to their defaults at construction; this method is a
            // hook for a future "reset form" button. Kept minimal so the defaults stay in one place
            // (the field initializers).
        }
    }
}

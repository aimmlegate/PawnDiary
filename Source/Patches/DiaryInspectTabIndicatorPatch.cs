// Draws the Diary tab's unread marker in RimWorld's normal pawn inspect-tab row.
// The bottom gizmo has its own command overlay; this patch is only for tab mode, where players
// asked for a quieter signal: a small white underline for newly finished pages and no writing dots.
// New to this? See AGENTS.md ("Harmony patches").
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Adds a small unread underline under the Diary inspect-tab button.
    /// </summary>
    [HarmonyPatch(typeof(InspectPaneUtility), "DoTabs")]
    public static class DiaryInspectTabIndicatorPatch
    {
        // Vanilla draws inspect tabs as 75x30 buttons, right-to-left from PaneWidthFor(pane) - 75.
        // Recomputing that rectangle in a DoTabs postfix avoids patching RimWorld's compiler-generated
        // per-tab local helper, whose name is fragile across game builds.
        private const float VanillaInspectTabWidth = 75f;
        private const float VanillaInspectTabHeight = 30f;

        /// <summary>
        /// After vanilla draws the tab strip, overlays the Diary unread underline if this pawn has
        /// completed pages that have not yet been acknowledged by opening the Diary tab.
        /// </summary>
        public static void Postfix(IInspectPane pane)
        {
            if (pane == null
                || PawnDiaryMod.Settings == null
                || !PawnDiaryMod.Settings.showDiaryInspectTab)
            {
                return;
            }

            Pawn pawn = SelectedDiaryPawn();
            if (!ITab_Pawn_Diary.CanShowDiaryFor(pawn))
            {
                return;
            }

            DiaryGameComponent component = DiaryGameComponent.Current;
            if (component == null || !component.CommandStatusFor(pawn).HasNewPages)
            {
                return;
            }

            Rect tabRect;
            if (TryDiaryTabRect(pane, out tabRect))
            {
                DrawUnreadUnderline(tabRect);
            }
        }

        /// <summary>
        /// Resolves the currently selected pawn or colonist corpse using the same single-selection
        /// rule as the bottom Diary command.
        /// </summary>
        private static Pawn SelectedDiaryPawn()
        {
            if (Find.Selector == null || Find.Selector.NumSelected != 1)
            {
                return null;
            }

            Thing selected = Find.Selector.SingleSelectedThing;
            Pawn pawn = selected as Pawn;
            if (pawn != null)
            {
                return pawn;
            }

            Corpse corpse = selected as Corpse;
            return corpse?.InnerPawn;
        }

        /// <summary>
        /// Mirrors RimWorld's inspect-tab row layout to find the Diary button rectangle after vanilla
        /// has already drawn it.
        /// </summary>
        private static bool TryDiaryTabRect(IInspectPane pane, out Rect rect)
        {
            rect = Rect.zero;

            IEnumerable<InspectTabBase> tabs = pane.CurTabs;
            if (tabs == null)
            {
                return false;
            }

            float curTabX = InspectPaneUtility.PaneWidthFor(pane) - VanillaInspectTabWidth;
            float tabsTopY = pane.PaneTopY - VanillaInspectTabHeight;
            foreach (InspectTabBase tab in tabs)
            {
                if (tab == null || !tab.IsVisible)
                {
                    continue;
                }

                if (tab.Hidden)
                {
                    continue;
                }

                Rect candidate = new Rect(curTabX, tabsTopY, VanillaInspectTabWidth, VanillaInspectTabHeight);
                if (tab is ITab_Pawn_Diary)
                {
                    rect = candidate;
                    return true;
                }

                curTabX -= VanillaInspectTabWidth;
            }

            return false;
        }

        /// <summary>
        /// Draws the tab-mode unread marker. No writing/loading indicator is drawn in tab mode.
        /// </summary>
        private static void DrawUnreadUnderline(Rect tabRect)
        {
            DiaryUiStyleDef style = DiaryUiStyles.Current;
            float width = Mathf.Clamp(
                style.inspectTabUnreadUnderlineWidth,
                4f,
                Mathf.Max(4f, tabRect.width - 12f));
            float height = Mathf.Max(1f, style.inspectTabUnreadUnderlineHeight);
            float bottomPadding = Mathf.Max(0f, style.inspectTabUnreadUnderlineBottomPadding);
            Rect underlineRect = new Rect(
                tabRect.x + (tabRect.width - width) * 0.5f,
                tabRect.yMax - bottomPadding - height,
                width,
                height);

            Color oldColor = GUI.color;
            Widgets.DrawBoxSolid(underlineRect, style.InspectTabUnreadUnderlineColor);
            GUI.color = oldColor;

            TooltipHandler.TipRegion(
                underlineRect.ExpandedBy(4f),
                "PawnDiary.Command.NewPagesTip".Translate());
        }
    }
}

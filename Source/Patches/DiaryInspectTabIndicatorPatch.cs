// Draws the Diary tab's unread marker in RimWorld's normal pawn inspect-tab row.
// The bottom gizmo has its own command overlay; this patch is only for tab mode, where players
// asked for a quieter signal: a small Unicode mark near the tab's right edge and no writing dots.
// New to this? See AGENTS.md ("Harmony patches").
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Adds a small unread marker to the Diary inspect-tab button.
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
        /// After vanilla draws the tab strip, overlays the Diary unread marker if this pawn has
        /// completed pages that have not yet been acknowledged by opening the Diary tab.
        /// </summary>
        public static void Postfix(IInspectPane pane)
        {
            // This postfix runs every frame on vanilla's inspect-tab strip for every selection, so an
            // unguarded throw would break that strip game-wide, not just the Diary tab. Wrap the whole
            // body: a failure here drops the unread marker for one frame, nothing more.
            try
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
                if (!TryDiaryTabRect(pane, out tabRect))
                {
                    return;
                }

                DrawUnreadMarker(tabRect);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Pawn Diary] Diary inspect-tab marker draw failed: " + e, 0x7D1A0001);
            }
        }

        /// <summary>
        /// Resolves the currently selected pawn or colonist corpse using the same single-selection
        /// rule as the bottom Diary command.
        /// </summary>
        private static Pawn SelectedDiaryPawn()
        {
            Selector selector;
            try
            {
                selector = Find.Selector;
            }
            catch (System.InvalidCastException)
            {
                // World inspect panes can draw before a map UI exists; in that state Find.Selector
                // throws while resolving Find.MapUI. The marker only belongs to map pawn tabs.
                return null;
            }

            if (selector == null || selector.NumSelected != 1)
            {
                return null;
            }

            Thing selected = selector.SingleSelectedThing;
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
        private static void DrawUnreadMarker(Rect tabRect)
        {
            DiaryUiStyleDef style = DiaryUiStyles.Current;
            string icon = string.IsNullOrEmpty(style.inspectTabUnreadIcon)
                ? "•"
                : style.inspectTabUnreadIcon;
            float width = Mathf.Clamp(style.inspectTabUnreadIconWidth, 8f, 18f);
            float height = Mathf.Clamp(style.inspectTabUnreadIconHeight, 10f, 26f);
            float rightPadding = Mathf.Max(0f, style.inspectTabUnreadIconRightPadding);
            float xOffset = style.inspectTabUnreadIconXOffset;
            float yOffset = style.inspectTabUnreadIconYOffset;
            Rect iconRect = new Rect(
                tabRect.xMax - rightPadding - width + xOffset,
                tabRect.y + (tabRect.height - height) * 0.5f + yOffset,
                width,
                height);

            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;

            GUI.color = style.InspectTabUnreadIconColor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(iconRect, icon);

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;

            TooltipHandler.TipRegion(
                iconRect.ExpandedBy(5f),
                "PawnDiary.Command.NewPagesTip".Translate());
        }
    }
}

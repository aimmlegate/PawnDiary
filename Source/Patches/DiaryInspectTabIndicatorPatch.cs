// Draws the Diary tab's unread marker in RimWorld's normal pawn inspect-tab row.
// The bottom gizmo has its own command overlay; this patch is only for tab mode, where players
// asked for a quieter signal: a small white dot for newly finished pages and no writing dots.
// New to this? See AGENTS.md ("Harmony patches").
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Adds a small unread dot to the Diary inspect-tab button.
    /// </summary>
    [HarmonyPatch(typeof(InspectPaneUtility), "DoTabs")]
    public static class DiaryInspectTabIndicatorPatch
    {
        // Vanilla draws inspect tabs as 75x30 buttons, right-to-left from PaneWidthFor(pane) - 75.
        // Recomputing that rectangle in a DoTabs postfix avoids patching RimWorld's compiler-generated
        // per-tab local helper, whose name is fragile across game builds.
        private const float VanillaInspectTabWidth = 75f;
        private const float VanillaInspectTabHeight = 30f;
        private const int DotTextureSize = 16;
        private static Texture2D unreadDotTexture;

        /// <summary>
        /// After vanilla draws the tab strip, overlays the Diary unread dot if this pawn has
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
                DrawUnreadDot(tabRect);
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
        private static void DrawUnreadDot(Rect tabRect)
        {
            DiaryUiStyleDef style = DiaryUiStyles.Current;
            float size = Mathf.Clamp(style.inspectTabUnreadDotSize, 3f, 9f);
            float topPadding = Mathf.Max(0f, style.inspectTabUnreadDotTopPadding);
            Rect dotRect = new Rect(
                tabRect.center.x - size * 0.5f,
                tabRect.y + topPadding,
                size,
                size);

            Color oldColor = GUI.color;
            GUI.color = style.InspectTabUnreadDotColor;
            GUI.DrawTexture(dotRect, UnreadDotTexture);
            GUI.color = oldColor;

            TooltipHandler.TipRegion(
                dotRect.ExpandedBy(5f),
                "PawnDiary.Command.NewPagesTip".Translate());
        }

        /// <summary>
        /// Tiny antialiased white circle used as the unread marker. The final tint comes from GUI.color.
        /// </summary>
        private static Texture2D UnreadDotTexture
        {
            get
            {
                if (unreadDotTexture == null)
                {
                    unreadDotTexture = BuildUnreadDotTexture();
                }

                return unreadDotTexture;
            }
        }

        /// <summary>
        /// Builds a small circle texture once so the tab marker reads as a dot instead of a square.
        /// </summary>
        private static Texture2D BuildUnreadDotTexture()
        {
            Texture2D texture = new Texture2D(DotTextureSize, DotTextureSize);
            texture.name = "PawnDiaryUnreadDot";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            float center = (DotTextureSize - 1) * 0.5f;
            float radius = center;
            Color[] pixels = new Color[DotTextureSize * DotTextureSize];
            for (int y = 0; y < DotTextureSize; y++)
            {
                for (int x = 0; x < DotTextureSize; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius + 0.5f - distance);
                    pixels[y * DotTextureSize + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}

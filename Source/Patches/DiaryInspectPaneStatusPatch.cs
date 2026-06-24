// Adds a tiny Diary status underline under the visible Diary inspect tab button.
// RimWorld owns inspect-tab layout, so this patch waits until the inspect window has drawn, reads the
// Diary tab's final button rectangle, then draws a short underline in the same GUI space.
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    [HarmonyPatch(typeof(MainTabWindow_Inspect), nameof(MainTabWindow_Inspect.DoWindowContents))]
    public static class DiaryInspectPaneStatusPatch
    {
        private const float UnderlineWidth = 34f;
        private const float UnderlineHeight = 2f;

        /// <summary>
        /// Draws a steady underline for finished diary pages, or a soft pulse on that same underline
        /// while a diary page/title is still being written.
        /// </summary>
        public static void Postfix(MainTabWindow_Inspect __instance)
        {
            ITab_Pawn_Diary diaryTab = VisibleDiaryTab(__instance);
            if (diaryTab == null)
            {
                return;
            }

            Pawn pawn = SelectedDiaryPawn();
            DiaryGameComponent component = DiaryGameComponent.Current;
            if (pawn == null || component == null)
            {
                return;
            }

            DiaryGameComponent.DiaryCommandStatus status = component.CommandStatusFor(pawn);
            if (!status.HasNewPages && !status.IsWriting)
            {
                return;
            }

            Rect tabRect = diaryTab.DiaryTabButtonRect();
            if (tabRect.width <= 0f || tabRect.height <= 0f)
            {
                return;
            }

            DrawUnderline(tabRect, status.IsWriting);
        }

        private static ITab_Pawn_Diary VisibleDiaryTab(MainTabWindow_Inspect inspectWindow)
        {
            if (inspectWindow == null || inspectWindow.CurTabs == null)
            {
                return null;
            }

            foreach (InspectTabBase tab in inspectWindow.CurTabs)
            {
                ITab_Pawn_Diary diaryTab = tab as ITab_Pawn_Diary;
                if (diaryTab != null && !diaryTab.Hidden)
                {
                    return diaryTab;
                }
            }

            return null;
        }

        private static Pawn SelectedDiaryPawn()
        {
            if (Find.Selector == null || Find.Selector.NumSelected != 1)
            {
                return null;
            }

            Thing selected = Find.Selector.SingleSelectedThing;
            Pawn pawn = selected as Pawn;
            if (pawn == null)
            {
                Corpse corpse = selected as Corpse;
                pawn = corpse?.InnerPawn;
            }

            return ITab_Pawn_Diary.CanShowDiaryFor(pawn) ? pawn : null;
        }

        private static void DrawUnderline(Rect tabRect, bool writing)
        {
            Rect underlineRect = new Rect(
                tabRect.center.x - UnderlineWidth * 0.5f,
                tabRect.yMax - 4f,
                UnderlineWidth,
                UnderlineHeight);

            Color oldColor = GUI.color;
            float alpha = 0.42f;
            if (writing)
            {
                float pulse = (Mathf.Sin(Time.realtimeSinceStartup * 4f) + 1f) * 0.5f;
                alpha = Mathf.Lerp(0.20f, 0.44f, pulse);
            }

            GUI.color = writing
                ? new Color(0.60f, 0.68f, 0.54f, alpha)
                : new Color(0.62f, 0.56f, 0.43f, alpha);

            Widgets.DrawBoxSolid(underlineRect, GUI.color);
            TooltipHandler.TipRegion(
                new Rect(underlineRect.x - 4f, underlineRect.y - 4f, underlineRect.width + 8f, 12f),
                writing ? "PawnDiary.Command.WritingTip".Translate() : "PawnDiary.Command.NewPagesTip".Translate());

            GUI.color = oldColor;
        }
    }
}

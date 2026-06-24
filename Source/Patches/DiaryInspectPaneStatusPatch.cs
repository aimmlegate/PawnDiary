// Adds a tiny Diary status underline to the visible Diary inspect tab button.
// RimWorld owns inspect-tab layout, so this patch draws during the tab button's own GUI pass and
// keeps the underline inside the tab rectangle, avoiding coordinate-space mismatches and clipping.
using HarmonyLib;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    [HarmonyPatch(typeof(InspectTabBase), nameof(InspectTabBase.DoTabGUI))]
    public static class DiaryInspectPaneStatusPatch
    {
        private const float UnderlineInset = 8f;
        private const float UnderlineHeight = 3f;

        /// <summary>
        /// Draws a steady underline for finished diary pages, or a soft pulse on that same underline
        /// while a diary page/title is still being written.
        /// </summary>
        public static void Postfix(InspectTabBase __instance)
        {
            ITab_Pawn_Diary diaryTab = __instance as ITab_Pawn_Diary;
            if (diaryTab == null || diaryTab.Hidden)
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
                tabRect.x + UnderlineInset,
                tabRect.yMax - 7f,
                Mathf.Max(0f, tabRect.width - UnderlineInset * 2f),
                UnderlineHeight);

            Color oldColor = GUI.color;
            float alpha = 0.56f;
            if (writing)
            {
                float pulse = (Mathf.Sin(Time.realtimeSinceStartup * 4f) + 1f) * 0.5f;
                alpha = Mathf.Lerp(0.28f, 0.62f, pulse);
            }

            GUI.color = writing
                ? new Color(0.67f, 0.75f, 0.58f, alpha)
                : new Color(0.68f, 0.61f, 0.44f, alpha);

            Widgets.DrawBoxSolid(underlineRect, GUI.color);
            TooltipHandler.TipRegion(
                new Rect(underlineRect.x - 4f, underlineRect.y - 4f, underlineRect.width + 8f, 12f),
                writing ? "PawnDiary.Command.WritingTip".Translate() : "PawnDiary.Command.NewPagesTip".Translate());

            GUI.color = oldColor;
        }
    }
}

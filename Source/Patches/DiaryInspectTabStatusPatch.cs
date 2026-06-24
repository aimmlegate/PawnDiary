// Dynamic label update for the optional visible Diary inspect tab.
// RimWorld owns the tab-strip button drawing. Instead of drawing an overlay, this prefix swaps the
// tab's localization key before RimWorld renders it. The keys reserve equal left/right indicator
// slots, so the title remains centered whether the new-page dot is visible or hidden.
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnDiary
{
    [HarmonyPatch(typeof(InspectTabBase), nameof(InspectTabBase.DoTabGUI))]
    public static class DiaryInspectTabStatusPatch
    {
        /// <summary>
        /// Refreshes only the Diary tab's label. Loading is intentionally ignored here; the tab label
        /// only communicates that a completed page is waiting to be read.
        /// </summary>
        public static void Prefix(InspectTabBase __instance)
        {
            ITab_Pawn_Diary diaryTab = __instance as ITab_Pawn_Diary;
            if (diaryTab != null)
            {
                diaryTab.RefreshTabLabelStatus();
            }
        }
    }
}

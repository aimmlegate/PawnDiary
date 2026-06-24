// Adds a tiny Diary status mark to the selected pawn's inspect-pane title.
// This is intentionally independent of whether the Diary opens from the bottom command gizmo or the
// normal pawn inspect tab, because RimWorld reliably redraws the inspect title in both modes.
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    [HarmonyPatch(typeof(MainTabWindow_Inspect), nameof(MainTabWindow_Inspect.GetLabel))]
    public static class DiaryInspectPaneStatusPatch
    {
        /// <summary>
        /// Appends a steady sparkle when finished diary pages are waiting, or a soft two-frame sparkle
        /// animation while a diary page/title is still being written.
        /// </summary>
        public static void Postfix(ref string __result)
        {
            if (string.IsNullOrEmpty(__result))
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
            string marker = StatusMarker(status);
            if (string.IsNullOrEmpty(marker))
            {
                return;
            }

            __result += " " + marker;
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

        private static string StatusMarker(DiaryGameComponent.DiaryCommandStatus status)
        {
            if (status.IsWriting)
            {
                return WritingMarker();
            }

            return status.HasNewPages ? "PawnDiary.InspectPane.NewMarker".Translate().Resolve() : string.Empty;
        }

        private static string WritingMarker()
        {
            int frame = Mathf.FloorToInt(Time.realtimeSinceStartup * 2.5f) % 2;
            return (frame == 0
                    ? "PawnDiary.InspectPane.WritingMarkerA"
                    : "PawnDiary.InspectPane.WritingMarkerB")
                .Translate()
                .Resolve();
        }
    }
}

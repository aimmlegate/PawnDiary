// Pawn/corpse command entry point for the Diary UI.
// RimWorld asks selected things for Gizmos (bottom action buttons). We append one command that opens
// the same hidden ITab_Pawn_Diary hosted by the inspect pane, so the diary UI behavior stays shared.
// New to this? See AGENTS.md ("Harmony patches").
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class PawnDiaryPawnGizmoPatch
    {
        /// <summary>
        /// Adds the Diary command when exactly one eligible colonist pawn is selected.
        /// </summary>
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            __result = DiaryInspectCommand.AppendDiaryCommand(__result, __instance, __instance);
        }
    }

    [HarmonyPatch(typeof(Corpse), nameof(Corpse.GetGizmos))]
    public static class PawnDiaryCorpseGizmoPatch
    {
        /// <summary>
        /// Adds the Diary command for a selected colonist corpse, preserving the old corpse-tab access.
        /// </summary>
        public static void Postfix(Corpse __instance, ref IEnumerable<Gizmo> __result)
        {
            __result = DiaryInspectCommand.AppendDiaryCommand(__result, __instance, __instance?.InnerPawn);
        }
    }

    /// <summary>
    /// Shared command construction for opening the hidden diary inspect tab.
    /// </summary>
    internal static class DiaryInspectCommand
    {
        // Stable command merge key for RimWorld's gizmo grouping. The command is single-select only,
        // but a fixed key keeps the command identity deterministic if vanilla compares commands.
        private const int DiaryCommandGroupKey = 9162301;
        private const string DiaryCommandIconPath = "UI/Commands/PawnDiaryOpen";
        private static Texture2D diaryCommandIcon;

        /// <summary>
        /// Loads the mod's command texture lazily, with a vanilla fallback if the asset is missing.
        /// </summary>
        private static Texture2D DiaryCommandIcon
        {
            get
            {
                if (diaryCommandIcon == null)
                {
                    diaryCommandIcon = ContentFinder<Texture2D>.Get(DiaryCommandIconPath, false) ?? TexButton.IconBook;
                }

                return diaryCommandIcon;
            }
        }

        /// <summary>
        /// Yields vanilla gizmos first, then a Diary command when the selected thing can show a diary.
        /// </summary>
        public static IEnumerable<Gizmo> AppendDiaryCommand(
            IEnumerable<Gizmo> original,
            Thing selectedThing,
            Pawn diaryPawn)
        {
            if (original != null)
            {
                foreach (Gizmo gizmo in original)
                {
                    yield return gizmo;
                }
            }

            if (ShouldShowDiaryCommand(selectedThing, diaryPawn))
            {
                yield return CreateDiaryCommand();
            }
        }

        /// <summary>
        /// Matches the hidden Diary tab's pawn eligibility and limits the command to one selection.
        /// </summary>
        private static bool ShouldShowDiaryCommand(Thing selectedThing, Pawn diaryPawn)
        {
            return selectedThing != null
                && ITab_Pawn_Diary.CanShowDiaryFor(diaryPawn)
                && Find.Selector != null
                && Find.Selector.NumSelected == 1
                && Find.Selector.SingleSelectedThing == selectedThing;
        }

        /// <summary>
        /// Builds the RimWorld command button that opens or closes the diary inspect tab.
        /// </summary>
        private static Command_Action CreateDiaryCommand()
        {
            return new Command_Action
            {
                defaultLabel = "PawnDiaryTabLabel".Translate(),
                defaultDesc = "PawnDiary.Command.OpenDiaryDesc".Translate(),
                icon = DiaryCommandIcon,
                groupKey = DiaryCommandGroupKey,
                action = ToggleDiaryTab
            };
        }

        /// <summary>
        /// Opens the hidden Diary tab, or closes it when the same tab is already open in Inspect.
        /// </summary>
        private static void ToggleDiaryTab()
        {
            MainTabWindow_Inspect inspectWindow = MainButtonDefOf.Inspect?.TabWindow as MainTabWindow_Inspect;
            if (inspectWindow != null
                && Find.MainTabsRoot != null
                && Find.MainTabsRoot.OpenTab == MainButtonDefOf.Inspect
                && inspectWindow.OpenTabType == typeof(ITab_Pawn_Diary))
            {
                inspectWindow.CloseOpenTab();
                return;
            }

            InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Diary));
        }
    }
}

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
                yield return CreateDiaryCommand(diaryPawn);
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
        private static Command_Action CreateDiaryCommand(Pawn diaryPawn)
        {
            return new DiaryCommand_Action(diaryPawn)
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

        /// <summary>
        /// Diary command with small overlays: a count for newly finished pages and pulsing dots while
        /// any page or title is still being generated. Vanilla still draws the base button.
        /// </summary>
        private sealed class DiaryCommand_Action : Command_Action
        {
            private const float BadgeSize = 22f;
            private const float WritingDotSize = 4f;
            private const float WritingDotGap = 3f;

            private readonly Pawn diaryPawn;

            public DiaryCommand_Action(Pawn diaryPawn)
            {
                this.diaryPawn = diaryPawn;
            }

            public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
            {
                GizmoResult result = base.GizmoOnGUI(topLeft, maxWidth, parms);
                DrawDiaryStatus(topLeft, maxWidth);
                return result;
            }

            private void DrawDiaryStatus(Vector2 topLeft, float maxWidth)
            {
                DiaryGameComponent component = DiaryGameComponent.Current;
                if (component == null || diaryPawn == null)
                {
                    return;
                }

                DiaryGameComponent.DiaryCommandStatus status = component.CommandStatusFor(diaryPawn);
                if (!status.HasNewPages && !status.IsWriting)
                {
                    return;
                }

                Rect buttonRect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
                if (status.HasNewPages)
                {
                    DrawNewPageBadge(new Rect(buttonRect.xMax - BadgeSize - 4f, buttonRect.y + 4f, BadgeSize, BadgeSize),
                        status.unacknowledgedCount);
                }

                if (status.IsWriting)
                {
                    DrawWritingBadge(new Rect(buttonRect.x + 7f, buttonRect.y + 7f, 28f, 14f));
                }
            }

            private static void DrawNewPageBadge(Rect rect, int count)
            {
                Color oldColor = GUI.color;
                Widgets.DrawBoxSolidWithOutline(rect, new Color(0.2f, 0.52f, 0.24f, 0.94f), new Color(0.74f, 1f, 0.72f), 1);

                GameFont oldFont = Text.Font;
                TextAnchor oldAnchor = Text.Anchor;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.white;
                Widgets.Label(rect.ContractedBy(1f), count > 9 ? "9+" : count.ToString());
                Text.Anchor = oldAnchor;
                Text.Font = oldFont;
                GUI.color = oldColor;

                TooltipHandler.TipRegion(rect, "PawnDiary.Command.NewPagesTip".Translate(count));
            }

            private static void DrawWritingBadge(Rect rect)
            {
                Color oldColor = GUI.color;
                Widgets.DrawBoxSolid(rect, new Color(0.06f, 0.08f, 0.06f, 0.72f));
                GUI.color = Color.white;

                float pulse = (Mathf.Sin(Time.realtimeSinceStartup * 5.2f) + 1f) * 0.5f;
                Color dotColor = Color.Lerp(new Color(0.46f, 0.78f, 0.46f), new Color(0.84f, 1f, 0.76f), pulse);
                for (int i = 0; i < 3; i++)
                {
                    float x = rect.x + 5f + i * (WritingDotSize + WritingDotGap);
                    float y = rect.y + rect.height * 0.5f - WritingDotSize * 0.5f;
                    Widgets.DrawBoxSolid(new Rect(x, y, WritingDotSize, WritingDotSize), dotColor);
                }

                GUI.color = oldColor;
                TooltipHandler.TipRegion(rect, "PawnDiary.Command.WritingTip".Translate());
            }
        }
    }
}

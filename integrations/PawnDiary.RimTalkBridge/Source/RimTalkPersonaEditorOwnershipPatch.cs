// Adds a compact ownership notice to RimTalk's persona editor. The bridge does not replace RimTalk's
// editor: it only explains that Pawn Diary will periodically overwrite this field and, when the
// optional transform is enabled, offers a regenerate action beside that explanation.
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.UI;
using UnityEngine;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Reserves space below RimTalk's fixed-height persona editor for the bridge notice.</summary>
    [HarmonyPatch(typeof(PersonaEditorWindow), "get_InitialSize")]
    internal static class RimTalkPersonaEditorInitialSizePatch
    {
        private const float ExtraHeight = 58f;

        private static void Postfix(PersonaEditorWindow __instance, ref Vector2 __result)
        {
            if (RimTalkPersonaEditorOwnershipPatch.PawnDiaryControls(__instance))
            {
                __result.y += ExtraHeight;
            }
        }
    }

    /// <summary>Draws ownership/regeneration UI and refreshes RimTalk's editor after async completion.</summary>
    [HarmonyPatch(typeof(PersonaEditorWindow), nameof(PersonaEditorWindow.DoWindowContents))]
    internal static class RimTalkPersonaEditorOwnershipPatch
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(PersonaEditorWindow), "_pawn");
        private static readonly FieldInfo PersonalityField =
            AccessTools.Field(typeof(PersonaEditorWindow), "_editingPersonality");
        private static readonly HashSet<string> AwaitingRefresh = new HashSet<string>();

        internal static bool PawnDiaryControls(PersonaEditorWindow window)
        {
            Pawn pawn = PawnFor(window);
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            return pawn != null && PawnDiaryRimTalkBridgeMod.LevelAtLeast(1) && settings != null
                && settings.personaSyncDirection == PersonaSyncDirection.PawnDiaryToRimTalk;
        }

        private static void Postfix(PersonaEditorWindow __instance, Rect inRect)
        {
            if (!PawnDiaryControls(__instance)) return;
            Pawn pawn = PawnFor(__instance);
            if (pawn == null) return;

            string id = pawn.GetUniqueLoadID();
            bool busy = PersonaSync.IsTransformBusy(pawn);
            if (AwaitingRefresh.Contains(id) && !busy)
            {
                AwaitingRefresh.Remove(id);
                try { PersonalityField?.SetValue(__instance, PersonaService.GetPersonality(pawn) ?? string.Empty); }
                catch { }
            }

            Rect row = new Rect(inRect.x, inRect.y + 402f, inRect.width, 44f);
            bool canRegenerate = PersonaSync.CanRegenerateExport(pawn);
            float buttonWidth = canRegenerate ? 112f : 0f;
            Rect labelRect = new Rect(row.x, row.y, row.width - buttonWidth - (canRegenerate ? 8f : 0f), row.height);
            GUI.color = new Color(0.75f, 0.85f, 1f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(labelRect, "PawnDiaryRimTalkBridge.Persona.RimTalkControlledNotice".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            if (!canRegenerate) return;
            Rect buttonRect = new Rect(row.xMax - buttonWidth, row.y + 8f, buttonWidth, 28f);
            string label = busy
                ? "PawnDiaryRimTalkBridge.Persona.Generating".Translate()
                : "PawnDiaryRimTalkBridge.Persona.Regenerate".Translate();
            if (Widgets.ButtonText(buttonRect, label, true, true, !busy) && !busy)
            {
                PersonaSync.RegenerateExport(pawn);
                AwaitingRefresh.Add(id);
            }
        }

        private static Pawn PawnFor(PersonaEditorWindow window)
        {
            try { return PawnField?.GetValue(window) as Pawn; }
            catch { return null; }
        }
    }
}

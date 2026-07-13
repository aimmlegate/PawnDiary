// Adds a compact ownership notice to RimTalk's persona editor. RimTalk is an optional external
// assembly, so every target type/member is resolved dynamically after the active-mod guard; no
// RimTalk type token appears in Harmony attributes or patch signatures.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Dynamically registers and implements the two RimTalk persona-editor postfixes.</summary>
    internal static class RimTalkPersonaEditorOwnershipPatch
    {
        private const string EditorTypeName = "RimTalk.UI.PersonaEditorWindow";
        private const float ExtraHeight = 58f;

        private static FieldInfo pawnField;
        private static FieldInfo personalityField;
        // Pawn id -> editor text that existed when Regenerate started. Async completion refreshes the
        // editor only if the player has not typed something newer meanwhile.
        private static readonly Dictionary<string, string> AwaitingRefresh =
            new Dictionary<string, string>();
        private static bool registered;

        /// <summary>Resolves the current RimTalk UI surface and installs each patch independently.</summary>
        internal static bool TryRegister(Harmony harmony)
        {
            if (registered) return true;
            if (harmony == null || !PawnDiaryRimTalkBridgeMod.RimTalkActive) return false;

            Type editorType = AccessTools.TypeByName(EditorTypeName);
            if (editorType == null) return WarnUnavailable("persona editor type not found");

            MethodInfo initialSize = AccessTools.PropertyGetter(editorType, "InitialSize");
            MethodInfo draw = AccessTools.Method(editorType, "DoWindowContents", new[] { typeof(Rect) });
            pawnField = AccessTools.Field(editorType, "_pawn");
            personalityField = AccessTools.Field(editorType, "_editingPersonality");
            if (initialSize == null || draw == null || pawnField == null || personalityField == null)
            {
                return WarnUnavailable("persona editor API changed");
            }

            try
            {
                harmony.Patch(initialSize, postfix: new HarmonyMethod(
                    typeof(RimTalkPersonaEditorOwnershipPatch), nameof(InitialSizePostfix)));
                harmony.Patch(draw, postfix: new HarmonyMethod(
                    typeof(RimTalkPersonaEditorOwnershipPatch), nameof(DoWindowContentsPostfix)));
                registered = true;
                return true;
            }
            catch (Exception e)
            {
                Log.Warning(PawnDiaryRimTalkBridgeMod.LogPrefix
                    + " persona-editor ownership UI is disabled: " + e.Message);
                return false;
            }
        }

        internal static void ResetForNewGame()
        {
            AwaitingRefresh.Clear();
        }

        internal static bool PawnDiaryControls(object window)
        {
            Pawn pawn = PawnFor(window);
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            return pawn != null && PawnDiaryRimTalkBridgeMod.LevelAtLeast(1) && settings != null
                && settings.personaSyncDirection == PersonaSyncDirection.PawnDiaryToRimTalk;
        }

        public static void InitialSizePostfix(object __instance, ref Vector2 __result)
        {
            if (PawnDiaryControls(__instance)) __result.y += ExtraHeight;
        }

        public static void DoWindowContentsPostfix(object __instance, Rect inRect)
        {
            if (!PawnDiaryControls(__instance)) return;
            Pawn pawn = PawnFor(__instance);
            if (pawn == null) return;

            string id = pawn.GetUniqueLoadID();
            bool busy = PersonaSync.IsTransformBusy(pawn);
            string textAtStart;
            if (AwaitingRefresh.TryGetValue(id, out textAtStart) && !busy)
            {
                AwaitingRefresh.Remove(id);
                string currentEditorText = EditingPersonalityFor(__instance);
                string refreshed;
                // Do not clobber text typed while the async transform was running.
                if (string.Equals(currentEditorText, textAtStart, StringComparison.Ordinal)
                    && PersonaSync.TryGetPersonality(pawn, out refreshed))
                {
                    try { personalityField.SetValue(__instance, refreshed ?? string.Empty); }
                    catch { }
                }
            }

            Rect row = new Rect(inRect.x, inRect.y + 402f, inRect.width, 44f);
            bool canRegenerate = PersonaSync.CanRegenerateExport(pawn);
            float buttonWidth = canRegenerate ? 112f : 0f;
            Rect labelRect = new Rect(row.x, row.y,
                row.width - buttonWidth - (canRegenerate ? 8f : 0f), row.height);
            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;
            GUI.color = new Color(0.75f, 0.85f, 1f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(labelRect, "PawnDiaryRimTalkBridge.Persona.RimTalkControlledNotice".Translate());
            GUI.color = previousColor;
            Text.Font = previousFont;

            if (!canRegenerate) return;
            Rect buttonRect = new Rect(row.xMax - buttonWidth, row.y + 8f, buttonWidth, 28f);
            string label = busy
                ? "PawnDiaryRimTalkBridge.Persona.Generating".Translate()
                : "PawnDiaryRimTalkBridge.Persona.Regenerate".Translate();
            if (Widgets.ButtonText(buttonRect, label, true, true, !busy) && !busy)
            {
                string editorText = EditingPersonalityFor(__instance);
                PersonaSync.RegenerateExport(pawn);
                AwaitingRefresh[id] = editorText;
            }
        }

        private static Pawn PawnFor(object window)
        {
            try { return window == null ? null : pawnField?.GetValue(window) as Pawn; }
            catch { return null; }
        }

        private static string EditingPersonalityFor(object window)
        {
            try { return personalityField?.GetValue(window) as string ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static bool WarnUnavailable(string reason)
        {
            Log.Warning(PawnDiaryRimTalkBridgeMod.LogPrefix
                + " persona-editor ownership UI is disabled (" + reason + "). Persona synchronization remains active.");
            return false;
        }
    }
}

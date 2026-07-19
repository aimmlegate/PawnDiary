// Quest-related Harmony patches. Quest.Accept and Quest.End are the canonical lifecycle hooks;
// the UI closure patch is defensive fallback registration for RimWorld's generated accept button.
// New to this? See AGENTS.md ("Harmony patches").
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnDiary
{
    // Fires when the player accepts a quest (Quest.Accept). Ordinary acceptances remain bookkeeping
    // and generic event-window signals; an exact XML window such as Royal Ascent may truthfully own a
    // start page at this transition. Quest completion/failure still owns the terminal quest page.
    /// <summary>
    /// Captures accepted quests through the canonical Quest.Accept lifecycle hook.
    /// </summary>
    [HarmonyPatch(typeof(Quest), nameof(Quest.Accept), new[] { typeof(Pawn) })]
    internal static class QuestAcceptPatch
    {
        /// <summary>Remembers whether vanilla can perform an acceptance transition in this call.</summary>
        public static void Prefix(Quest __instance, out bool __state)
        {
            __state = __instance != null && __instance.State == QuestState.NotYetAccepted;
        }

        /// <summary>
        /// Harmony Postfix for Quest.Accept. Forwards the freshly accepted quest to
        /// DiaryGameComponent.RecordQuestAccepted, which deduplicates the canonical acceptance and
        /// lets root-first event-window policy decide whether that proven transition owns a page.
        /// </summary>
        public static void Postfix(Quest __instance, bool __state)
        {
            DiaryPatchSafety.Run("QuestAcceptPatch", () =>
            {
                if (!__state || __instance == null || !__instance.EverAccepted)
                {
                    return;
                }

                DiaryGameComponent.Instance?.RecordQuestAccepted(__instance);
            });
        }
    }

    // UI fallback for quest acceptance. Vanilla accepts quests from MainTabWindow_Quests through a
    // compiler-generated local function; Quest.Accept should still be the canonical hook, but this
    // covers the exact in-game button path if a RimWorld/Harmony edge case skips the direct patch.
    // New to C#/RimWorld? Compiler-generated names are fragile, so this is registered manually with
    // null checks instead of a bare [HarmonyPatch] attribute.
    /// <summary>
    /// Defensively patches the generated quest-accept UI closure as a fallback to Quest.Accept.
    /// </summary>
    internal static class QuestUiAcceptPatch
    {
        private const string AcceptClosureTypeName = "RimWorld.MainTabWindow_Quests+<>c__DisplayClass83_1";
        private const string AcceptActionMethodName = "<AcceptQuestByInterface>g__AcceptAction|1";
        private const string ParentClosureFieldName = "CS$<>8__locals1";
        private const string QuestWindowFieldName = "<>4__this";
        private const string SelectedQuestFieldName = "selected";

        private static FieldInfo ParentClosureField;
        private static FieldInfo QuestWindowField;
        private static FieldInfo SelectedQuestField;

        /// <summary>
        /// Patches the generated UI accept action when RimWorld still exposes it under the known
        /// compiler name. Safe to skip: the canonical Quest.Accept patch remains registered above.
        /// </summary>
        public static void TryRegister(Harmony harmony)
        {
            Type closureType = AccessTools.TypeByName(AcceptClosureTypeName);
            MethodBase target = AccessTools.Method(closureType, AcceptActionMethodName);
            ParentClosureField = AccessTools.Field(closureType, ParentClosureFieldName);
            Type parentClosureType = ParentClosureField?.FieldType;
            QuestWindowField = AccessTools.Field(parentClosureType, QuestWindowFieldName);
            SelectedQuestField = AccessTools.Field(typeof(MainTabWindow_Quests), SelectedQuestFieldName);

            if (target == null
                || ParentClosureField == null
                || QuestWindowField == null
                || SelectedQuestField == null)
            {
                // The compiler-generated closure name is version-specific and commonly fails to
                // resolve; that is expected, not a fault — the canonical Quest.Accept patch above is
                // the real hook. Stay quiet on a clean boot and only surface the miss under dev mode.
                if (Prefs.DevMode)
                {
                    Log.Message("[Pawn Diary] MainTabWindow_Quests quest-accept UI action not found; "
                        + "quest accepted bookkeeping will rely on Quest.Accept only.");
                }
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(QuestUiAcceptPatch), nameof(Postfix)));
        }

        /// <summary>
        /// Harmony Postfix for the UI accept action. Runs after vanilla acceptance, then forwards the
        /// selected quest. If Quest.Accept already marked it, RecordQuestAccepted's seen-set update
        /// keeps the fallback scanner quiet.
        /// </summary>
        public static void Postfix(object __instance)
        {
            DiaryPatchSafety.Run("QuestUiAcceptPatch", () =>
            {
                object parentClosure = ParentClosureField?.GetValue(__instance);
                object questWindow = QuestWindowField?.GetValue(parentClosure);
                Quest quest = SelectedQuestField?.GetValue(questWindow) as Quest;
                if (quest == null || !quest.EverAccepted)
                {
                    return;
                }

                DiaryGameComponent.Instance?.RecordQuestAccepted(quest);
            });
        }
    }

    // Fires when a quest ends (Quest.End). Only Success ("completed") and Fail ("failed") outcomes
    // are recorded; Unknown and InvalidPreAcceptance are skipped. The root-first XML group chooses
    // the audience (all eligible colonists by default, or one stable witness for Royal Ascent).
    /// <summary>
    /// Captures completed and failed quest endings from Quest.End.
    /// </summary>
    [HarmonyPatch(typeof(Quest), nameof(Quest.End), new[]
    {
        typeof(QuestEndOutcome), typeof(bool), typeof(bool)
    })]
    internal static class QuestEndPatch
    {
        /// <summary>Remembers whether vanilla can perform a terminal transition in this call.</summary>
        public static void Prefix(Quest __instance, out bool __state)
        {
            __state = __instance != null && !__instance.Historical;
        }

        /// <summary>
        /// Harmony Postfix for Quest.End. Forwards Success/Fail outcomes to
        /// DiaryGameComponent.RecordQuestEnded, which maps them to the completed/failed signals.
        /// </summary>
        public static void Postfix(Quest __instance, QuestEndOutcome outcome, bool __state)
        {
            DiaryPatchSafety.Run("QuestEndPatch", () =>
            {
                if (!__state || __instance == null || !__instance.Historical)
                {
                    return;
                }

                if (outcome != QuestEndOutcome.Success && outcome != QuestEndOutcome.Fail)
                {
                    return;
                }

                DiaryGameComponent.Instance?.RecordQuestEnded(__instance, outcome);
            });
        }
    }
}

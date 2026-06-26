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
    // Fires when the player accepts a quest (Quest.Accept). This is the diary's entry point for the
    // whole quest lifecycle: offered-but-not-accepted quests are intentionally ignored (per the
    // requirement "only quest that accepted"). Accept fans out to every eligible colonist.
    /// <summary>
    /// Captures accepted quests through the canonical Quest.Accept lifecycle hook.
    /// </summary>
    [HarmonyPatch(typeof(Quest), nameof(Quest.Accept), new[] { typeof(Pawn) })]
    public static class QuestAcceptPatch
    {
        /// <summary>
        /// Harmony Postfix for Quest.Accept. Forwards the freshly accepted quest to
        /// DiaryGameComponent.RecordQuestAccepted, which records a solo entry per eligible colonist.
        /// </summary>
        public static void Postfix(Quest __instance)
        {
            DiaryPatchSafety.Run("QuestAcceptPatch", () =>
            {
                if (__instance == null)
                {
                    return;
                }

                DiaryGameComponent.Current?.RecordQuestAccepted(__instance);
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
    public static class QuestUiAcceptPatch
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
                Log.Warning("[Pawn Diary] Could not find MainTabWindow_Quests quest-accept UI action; "
                    + "quest accepted diary entries will rely on Quest.Accept only.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(QuestUiAcceptPatch), nameof(Postfix)));
        }

        /// <summary>
        /// Harmony Postfix for the UI accept action. Runs after vanilla acceptance, then forwards the
        /// selected quest. If Quest.Accept already recorded it, RecordQuestAccepted's dedup key drops
        /// this duplicate in the same tick.
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

                DiaryGameComponent.Current?.RecordQuestAccepted(quest);
            });
        }
    }

    // Fires when a quest ends (Quest.End). Only Success ("completed") and Fail ("failed") outcomes
    // are recorded; Unknown and InvalidPreAcceptance are skipped. Each outcome fans out to every
    // eligible colonist with its own prompt group and emotional register.
    /// <summary>
    /// Captures completed and failed quest endings from Quest.End.
    /// </summary>
    [HarmonyPatch(typeof(Quest), nameof(Quest.End))]
    public static class QuestEndPatch
    {
        /// <summary>
        /// Harmony Postfix for Quest.End. Forwards Success/Fail outcomes to
        /// DiaryGameComponent.RecordQuestEnded, which maps them to the completed/failed signals.
        /// </summary>
        public static void Postfix(Quest __instance, QuestEndOutcome outcome)
        {
            DiaryPatchSafety.Run("QuestEndPatch", () =>
            {
                if (__instance == null)
                {
                    return;
                }

                if (outcome != QuestEndOutcome.Success && outcome != QuestEndOutcome.Fail)
                {
                    return;
                }

                DiaryGameComponent.Current?.RecordQuestEnded(__instance, outcome);
            });
        }
    }
}

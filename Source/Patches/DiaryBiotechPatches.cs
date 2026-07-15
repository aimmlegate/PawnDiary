// Stable Biotech growth-letter lifecycle patches. They register dynamically as one readiness unit:
// if either ConfigureGrowthLetter or MakeChoices is unavailable, HooksReady remains false and the
// existing birthday patch never suppresses the mature ordinary Birthday path.
//
// New to C#/RimWorld? See AGENTS.md ("Harmony patches" and "DLC-safety").
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Captures exact pregnancy/labor parent assignment at the shared HediffWithParents boundary.
    /// Registration is defensive so a changed RimWorld signature disables only family correlation.
    /// </summary>
    internal static class BiotechFamilyHediffPatch
    {
        /// <summary>Resolves and patches HediffWithParents.SetParents(Pawn, Pawn, GeneSet).</summary>
        public static void TryRegister(Harmony harmony)
        {
            if (harmony == null) return;
            MethodBase target = AccessTools.Method(
                typeof(HediffWithParents),
                "SetParents",
                new[] { typeof(Pawn), typeof(Pawn), typeof(GeneSet) });
            if (target == null)
            {
                Log.Warning("[Pawn Diary] HediffWithParents.SetParents changed; Biotech family "
                    + "pregnancy/labor correlation is disabled.");
                return;
            }

            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(
                    typeof(BiotechFamilyHediffPatch),
                    nameof(Postfix)));
            }
            catch (Exception exception)
            {
                Log.Warning("[Pawn Diary] Could not register Biotech family Hediff capture: "
                    + exception);
            }
        }

        private static void Postfix(HediffWithParents __instance)
        {
            if (!ModsConfig.BiotechActive || __instance == null) return;
            DiaryPatchSafety.Run("BiotechFamilyHediffPatch.SetParents", () =>
            {
                DiaryGameComponent.Instance?.ObserveBiotechFamilyHediff(__instance);
            });
        }
    }

    /// <summary>Captures configured and committed Biotech growth choices without requiring the DLC.</summary>
    internal static class BiotechGrowthLetterPatch
    {
        private static FieldInfo PawnField;
        private static FieldInfo GrowthTierField;
        private static FieldInfo ChoiceMadeField;

        /// <summary>
        /// True only after both stable lifecycle methods and their required fields were patched.
        /// Birthday ownership checks this first, so a partial hook failure always fails open.
        /// </summary>
        public static bool HooksReady { get; private set; }

        /// <summary>Resolves and patches the two verified RimWorld 1.6 growth-letter methods.</summary>
        public static void TryRegister(Harmony harmony)
        {
            HooksReady = false;
            if (harmony == null)
            {
                return;
            }

            Type type = typeof(ChoiceLetter_GrowthMoment);
            MethodBase configure = AccessTools.DeclaredMethod(type, "ConfigureGrowthLetter", new[]
            {
                typeof(Pawn),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(List<string>),
                typeof(Name)
            });
            MethodBase makeChoices = AccessTools.DeclaredMethod(type, "MakeChoices", new[]
            {
                typeof(List<SkillDef>),
                typeof(Trait)
            });
            PawnField = AccessTools.Field(type, "pawn");
            GrowthTierField = AccessTools.Field(type, "growthTier");
            ChoiceMadeField = AccessTools.Field(type, "choiceMade");
            if (configure == null || makeChoices == null
                || PawnField == null || GrowthTierField == null || ChoiceMadeField == null)
            {
                Log.Warning(
                    "[Pawn Diary] Biotech growth-letter methods changed; canonical growth capture is disabled "
                    + "and ordinary birthdays remain active.");
                return;
            }

            try
            {
                harmony.Patch(
                    configure,
                    postfix: new HarmonyMethod(
                        typeof(BiotechGrowthLetterPatch),
                        nameof(ConfigureGrowthLetterPostfix)));
                harmony.Patch(
                    makeChoices,
                    prefix: new HarmonyMethod(
                        typeof(BiotechGrowthLetterPatch),
                        nameof(MakeChoicesPrefix)),
                    postfix: new HarmonyMethod(
                        typeof(BiotechGrowthLetterPatch),
                        nameof(MakeChoicesPostfix)));
                HooksReady = true;
            }
            catch (Exception exception)
            {
                HooksReady = false;
                Log.Warning(
                    "[Pawn Diary] Could not register complete Biotech growth capture; ordinary birthdays "
                    + "remain active. " + exception);
            }
        }

        private static void ConfigureGrowthLetterPostfix(
            ChoiceLetter_GrowthMoment __instance,
            Pawn pawn,
            List<string> enabledWorkTypes)
        {
            if (!HooksReady || !ModsConfig.BiotechActive || pawn == null)
            {
                return;
            }

            DiaryPatchSafety.Run("BiotechGrowthLetterPatch.ConfigureGrowthLetter", () =>
            {
                int growthTier = FieldInt(GrowthTierField, __instance);
                DiaryGameComponent.Instance?.ObserveBiotechGrowthLetterConfigured(
                    pawn,
                    growthTier,
                    enabledWorkTypes != null && enabledWorkTypes.Count > 0);
            });
        }

        private static void MakeChoicesPrefix(
            ChoiceLetter_GrowthMoment __instance,
            List<SkillDef> skills,
            Trait trait,
            ref BiotechGrowthChoiceState __state)
        {
            BiotechGrowthChoiceState state = null;
            if (HooksReady && ModsConfig.BiotechActive && __instance != null)
            {
                DiaryPatchSafety.Run("BiotechGrowthLetterPatch.MakeChoices.Prefix", () =>
                {
                    Pawn pawn = PawnField.GetValue(__instance) as Pawn;
                    bool choiceMade = FieldBool(ChoiceMadeField, __instance);
                    int growthTier = FieldInt(GrowthTierField, __instance);
                    state = DiaryGameComponent.Instance?.BeginBiotechGrowthChoice(
                        pawn,
                        growthTier,
                        choiceMade,
                        skills,
                        trait);
                });
            }

            __state = state;
        }

        private static void MakeChoicesPostfix(
            ChoiceLetter_GrowthMoment __instance,
            BiotechGrowthChoiceState __state)
        {
            if (__state == null || __instance == null)
            {
                return;
            }

            DiaryPatchSafety.Run("BiotechGrowthLetterPatch.MakeChoices.Postfix", () =>
            {
                DiaryGameComponent.Instance?.FinishBiotechGrowthChoice(
                    __state,
                    FieldBool(ChoiceMadeField, __instance));
            });
        }

        private static int FieldInt(FieldInfo field, object instance)
        {
            object value = field?.GetValue(instance);
            return value is int ? (int)value : 0;
        }

        private static bool FieldBool(FieldInfo field, object instance)
        {
            object value = field?.GetValue(instance);
            return value is bool && (bool)value;
        }
    }
}

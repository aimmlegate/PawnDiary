// Stable Biotech family/birth and growth-letter lifecycle patches. Every fragile target registers
// dynamically and fails open: a missing canonical birth target leaves Tale/Thought/Ritual routes
// active, while an incomplete growth hook set leaves the mature ordinary Birthday path active.
//
// New to C#/RimWorld? See AGENTS.md ("Harmony patches" and "DLC-safety").
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Owns the exact ApplyBirthOutcome boundary. Registration is all-or-nothing so a changed
    /// signature leaves mature Tale/Thought/ritual routes untouched.
    /// </summary>
    internal static class BiotechBirthOutcomePatch
    {
        /// <summary>Resolves the verified RimWorld 1.6 birth signature and installs prefix/postfix/finalizer.</summary>
        public static void TryRegister(Harmony harmony)
        {
            if (harmony == null) return;
            MethodBase target = AccessTools.DeclaredMethod(
                typeof(PregnancyUtility),
                "ApplyBirthOutcome",
                new[]
                {
                    typeof(RitualOutcomePossibility),
                    typeof(float),
                    typeof(Precept_Ritual),
                    typeof(List<GeneDef>),
                    typeof(Pawn),
                    typeof(Thing),
                    typeof(Pawn),
                    typeof(Pawn),
                    typeof(LordJob_Ritual),
                    typeof(RitualRoleAssignments),
                    typeof(bool)
                });
            if (target == null)
            {
                Log.Warning("[Pawn Diary] PregnancyUtility.ApplyBirthOutcome changed; canonical "
                    + "Biotech births are disabled and mature routes remain active.");
                return;
            }

            try
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(BiotechBirthOutcomePatch), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(BiotechBirthOutcomePatch), nameof(Postfix)),
                    finalizer: new HarmonyMethod(typeof(BiotechBirthOutcomePatch), nameof(Finalizer)));
            }
            catch (Exception exception)
            {
                Log.Warning("[Pawn Diary] Could not register canonical Biotech birth capture; "
                    + "mature routes remain active. " + exception);
            }
        }

        private static void Prefix(
            Pawn geneticMother,
            Thing birtherThing,
            Pawn father,
            Pawn doctor,
            LordJob_Ritual lordJobRitual,
            ref BiotechBirthCallState __state)
        {
            if (!ModsConfig.BiotechActive || birtherThing == null)
            {
                return;
            }

            try
            {
                __state = DiaryGameComponent.Instance?.BeginBiotechBirth(
                    geneticMother,
                    birtherThing,
                    father,
                    doctor,
                    lordJobRitual != null,
                    lordJobRitual);
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Could not begin canonical Biotech birth capture; mature routes "
                    + "remain active: " + exception,
                    "PawnDiary.BiotechBirth.Begin".GetHashCode());
            }
        }

        private static void Postfix(
            Thing __result,
            RitualOutcomePossibility outcome,
            Thing birtherThing,
            BiotechBirthCallState __state)
        {
            if (__state == null)
            {
                return;
            }

            try
            {
                __state.canonicalClaimed = DiaryGameComponent.Instance?.CompleteBiotechBirth(
                    __state,
                    __result,
                    birtherThing,
                    outcome?.positivityIndex ?? int.MinValue) == true;
            }
            catch (Exception exception)
            {
                __state.canonicalClaimed = false;
                Log.ErrorOnce(
                    "[Pawn Diary] Canonical Biotech birth completion failed; mature routes will be "
                    + "released: " + exception,
                    "PawnDiary.BiotechBirth.Complete".GetHashCode());
            }
        }

        private static Exception Finalizer(Exception __exception, BiotechBirthCallState __state)
        {
            if (__state?.correlationScope != null)
            {
                int expiryTicks = BiotechPolicySnapshot.CreateDefault().birthCorrelationExpiryTicks;
                try
                {
                    expiryTicks = DiaryBiotechPolicy.Snapshot().birthCorrelationExpiryTicks;
                }
                catch (Exception exception)
                {
                    Log.ErrorOnce(
                        "[Pawn Diary] Biotech birth correlation policy was unavailable; the safe "
                        + "fallback expiry will be used: " + exception,
                        "PawnDiary.BiotechBirth.FinalizerPolicy".GetHashCode());
                }

                try
                {
                    // canonicalClaimed records whether our own Postfix committed ownership. Harmony
                    // also passes exceptions thrown by later third-party postfixes to this Finalizer;
                    // those must not release signals that Pawn Diary already consumed successfully.
                    BiotechBirthCorrelation.CloseBirth(
                        __state.correlationScope,
                        __state.canonicalClaimed,
                        Find.TickManager?.TicksGame ?? 0,
                        expiryTicks);
                }
                catch (Exception exception)
                {
                    Log.ErrorOnce(
                        "[Pawn Diary] Biotech birth correlation cleanup failed and was skipped: "
                        + exception,
                        "PawnDiary.BiotechBirth.Finalizer".GetHashCode());
                }
            }

            return __exception;
        }
    }

    /// <summary>Attaches exact miscarriage memories to their family arc without creating a birth page.</summary>
    internal static class BiotechMiscarriagePatch
    {
        /// <summary>Defensively patches the stable public Hediff_Pregnant.Miscarry method.</summary>
        public static void TryRegister(Harmony harmony)
        {
            if (harmony == null) return;
            MethodBase target = AccessTools.DeclaredMethod(typeof(Hediff_Pregnant), "Miscarry", Type.EmptyTypes);
            if (target == null)
            {
                Log.Warning("[Pawn Diary] Hediff_Pregnant.Miscarry changed; exact Biotech family-loss "
                    + "enrichment is disabled.");
                return;
            }

            try
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(BiotechMiscarriagePatch), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(BiotechMiscarriagePatch), nameof(Postfix)),
                    finalizer: new HarmonyMethod(typeof(BiotechMiscarriagePatch), nameof(Finalizer)));
            }
            catch (Exception exception)
            {
                Log.Warning("[Pawn Diary] Could not register Biotech miscarriage enrichment: " + exception);
            }
        }

        private static void Prefix(Hediff_Pregnant __instance, ref BiotechMiscarriageCallState __state)
        {
            if (!ModsConfig.BiotechActive || __instance == null) return;
            try
            {
                __state = DiaryGameComponent.Instance?.BeginBiotechMiscarriage(__instance);
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Could not begin exact Biotech miscarriage context: " + exception,
                    "PawnDiary.BiotechMiscarriage.Begin".GetHashCode());
            }
        }

        private static void Postfix(BiotechMiscarriageCallState __state)
        {
            if (__state == null) return;
            try
            {
                DiaryGameComponent.Instance?.CompleteBiotechMiscarriage(__state);
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Could not close exact Biotech miscarriage family state: " + exception,
                    "PawnDiary.BiotechMiscarriage.Complete".GetHashCode());
            }
        }

        private static Exception Finalizer(Exception __exception, BiotechMiscarriageCallState __state)
        {
            BiotechBirthCorrelation.CloseMiscarriage(__state?.correlationScope);
            return __exception;
        }
    }

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

        /// <summary>
        /// Returns the pawn a live growth-moment letter still targets, or null for any other letter.
        /// Pending-growth maintenance uses this to keep claimed ownership while the player's choice
        /// letter is still open (vanilla growth letters never time out); the caller checks liveness so
        /// a letter lingering for a dead pawn cannot hold its row forever. Degrades to null when the
        /// field hook failed, so expiry stays fail-open.
        /// </summary>
        public static Pawn LetterPawn(Letter letter)
        {
            ChoiceLetter_GrowthMoment growthLetter = letter as ChoiceLetter_GrowthMoment;
            if (growthLetter == null || PawnField == null)
            {
                return null;
            }

            return PawnField.GetValue(growthLetter) as Pawn;
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

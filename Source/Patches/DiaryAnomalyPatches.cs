// Defensive registration for the exact Anomaly study and containment seams. The paid DLC's C# types
// exist in every RimWorld installation, but these hooks register only when Anomaly is active; live
// DLC reads are forwarded as objects to DlcContext and vanilla always continues.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using PawnDiary.Capture;
using PawnDiary.Ingestion;

namespace PawnDiary
{
    /// <summary>Registers and owns the optional <c>CompStudyUnlocks.OnStudied</c> before/after hook.</summary>
    internal static class DiaryAnomalyPatches
    {
        private const string StudyUnlocksTypeName = "RimWorld.CompStudyUnlocks";
        private const string KnowledgeCategoryTypeName = "RimWorld.KnowledgeCategoryDef";
        private const string HoldingPlatformTargetTypeName =
            "RimWorld.CompHoldingPlatformTarget";

        /// <summary>True only when the exact A1.2 study method was found and patched.</summary>
        public static bool StudyHookReady { get; private set; }

        /// <summary>True only when the exact A1.3 Escape(bool initiator) method was patched.</summary>
        public static bool ContainmentHookReady { get; private set; }

        /// <summary>
        /// Registers the exact signature defensively. A changed/missing target disables only study
        /// milestones and leaves vanilla study and the generic StudiedEntity Tale untouched.
        /// </summary>
        public static void TryRegister(Harmony harmony)
        {
            StudyHookReady = false;
            ContainmentHookReady = false;
            if (harmony == null || !ModsConfig.AnomalyActive) return;

            TryRegisterStudy(harmony);
            TryRegisterContainment(harmony);
        }

        private static void TryRegisterStudy(Harmony harmony)
        {
            try
            {
                Type studyType = AccessTools.TypeByName(StudyUnlocksTypeName);
                Type categoryType = AccessTools.TypeByName(KnowledgeCategoryTypeName);
                MethodBase target = studyType == null || categoryType == null
                    ? null
                    : AccessTools.DeclaredMethod(
                        studyType,
                        "OnStudied",
                        new[] { typeof(Pawn), typeof(float), categoryType });
                if (target == null)
                {
                    WarnMissing("the exact CompStudyUnlocks.OnStudied(Pawn, float, KnowledgeCategoryDef) target was not found");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(DiaryAnomalyPatches), nameof(StudyPrefix)),
                    postfix: new HarmonyMethod(typeof(DiaryAnomalyPatches), nameof(StudyPostfix)));
                StudyHookReady = true;
            }
            catch (Exception exception)
            {
                WarnMissing("registration failed: " + exception.GetType().Name + ": "
                    + Limit(exception.Message));
            }
        }

        private static void TryRegisterContainment(Harmony harmony)
        {
            try
            {
                Type targetType = AccessTools.TypeByName(HoldingPlatformTargetTypeName);
                MethodInfo target = targetType == null
                    ? null
                    : AccessTools.DeclaredMethod(
                        targetType, "Escape", new[] { typeof(bool) }) as MethodInfo;
                ParameterInfo[] parameters = target?.GetParameters();
                if (target == null || target.ReturnType != typeof(void)
                    || parameters == null || parameters.Length != 1
                    || !string.Equals(parameters[0].Name, "initiator", StringComparison.Ordinal))
                {
                    WarnMissingContainment(
                        "the exact CompHoldingPlatformTarget.Escape(bool initiator) target was not found");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(
                        typeof(DiaryAnomalyPatches), nameof(ContainmentPrefix)),
                    postfix: new HarmonyMethod(
                        typeof(DiaryAnomalyPatches), nameof(ContainmentPostfix)),
                    finalizer: new HarmonyMethod(
                        typeof(DiaryAnomalyPatches), nameof(ContainmentFinalizer)));
                ContainmentHookReady = true;
            }
            catch (Exception exception)
            {
                WarnMissingContainment("registration failed: " + exception.GetType().Name + ": "
                    + Limit(exception.Message));
            }
        }

        /// <summary>Captures exact progress, completion, identity, and activation state before vanilla.</summary>
        private static void StudyPrefix(
            object __instance,
            Pawn studier,
            ref AnomalyStudyFacts __state)
        {
            __state = null;
            if (!RuntimeReady()) return;

            AnomalyStudyFacts captured = null;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.StudyPrefix", () =>
            {
                DlcContext.TryCaptureAnomalyStudyBefore(
                    __instance, studier, out captured);
            });
            __state = captured;
        }

        /// <summary>Observes only committed progress after vanilla and never changes vanilla's result.</summary>
        private static void StudyPostfix(
            object __instance,
            Pawn studier,
            object category,
            AnomalyStudyFacts __state)
        {
            if (!RuntimeReady() || __state == null) return;
            DiaryPatchSafety.Run(
                "DiaryAnomalyPatches.StudyPostfix",
                (instance: __instance, pawn: studier, categoryObject: category, before: __state),
                state => DiaryGameComponent.Instance?.CompleteAnomalyStudy(
                    state.instance, state.pawn, state.categoryObject, state.before));
        }

        /// <summary>Opens one exact synchronous frame before vanilla ejects the held entity.</summary>
        private static void ContainmentPrefix(
            object __instance,
            bool initiator,
            ref ContainmentEscapeCallState __state)
        {
            __state = null;
            if (!ContainmentHookReady || !RuntimeReady()) return;
            ContainmentEscapeCallState captured = null;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.ContainmentPrefix", () =>
            {
                captured = ContainmentEscapeScopeStack.Begin(__instance, initiator);
            });
            __state = captured;
        }

        /// <summary>Verifies the post-ejection state and lets only the outer frame submit one signal.</summary>
        private static void ContainmentPostfix(ContainmentEscapeCallState __state)
        {
            if (__state == null) return;
            ContainmentEscapeCompletion completion = null;
            DiaryPatchSafety.Run("DiaryAnomalyPatches.ContainmentPostfix.Close", () =>
            {
                completion = ContainmentEscapeScopeStack.Complete(__state);
            });
            if (completion == null) return;

            DiaryPatchSafety.Run("DiaryAnomalyPatches.ContainmentPostfix.Emit", () =>
            {
                ContainmentBreachPlan plan = ContainmentBreachPolicy.Plan(
                    completion.facts, completion.policy);
                if (!plan.writePage) return;
                List<Pawn> writers = completion.ResolveWriters(plan);
                if (writers.Count != plan.selectedWriters.Count)
                {
                    Log.WarningOnce(
                        "[Pawn Diary] A verified containment breach was skipped because its captured "
                            + "writer identities could not be resolved consistently.",
                        "PawnDiary.Anomaly.Containment.WriterResolution".GetHashCode());
                    return;
                }
                DiaryInteractionGroupDef group = InteractionGroups.ClassifyAnomalyEvent(
                    AnomalyEventDefNames.ContainmentBreach);
                DiaryEvents.Submit(new AnomalyContainmentBreachSignal(
                    completion.facts,
                    plan,
                    completion.policy,
                    group,
                    writers,
                    completion.FirstEscapedPawn(plan)));
            });
        }

        /// <summary>Always clears an unfinished call/scope while preserving vanilla's exception.</summary>
        private static Exception ContainmentFinalizer(
            Exception __exception,
            ContainmentEscapeCallState __state)
        {
            if (__state == null || __state.completed) return __exception;
            try
            {
                ContainmentEscapeScopeStack.Abort(__state);
            }
            catch (Exception cleanupException)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Anomaly containment scope cleanup failed and was skipped: "
                        + cleanupException,
                    "PawnDiary.Anomaly.Containment.Finalizer".GetHashCode());
            }
            return __exception;
        }

        private static bool RuntimeReady()
        {
            return ModsConfig.AnomalyActive
                && DiaryGameComponent.GamePlaying
                && Scribe.mode == LoadSaveMode.Inactive;
        }

        private static string Limit(string value)
        {
            string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length <= 240 ? clean : clean.Substring(0, 240);
        }

        private static void WarnMissing(string detail)
        {
            Log.WarningOnce(
                "[Pawn Diary] Anomaly study milestones are disabled because " + detail
                    + "; vanilla study and generic Tales remain unchanged.",
                "PawnDiary.Anomaly.MissingHook.Study".GetHashCode());
        }

        private static void WarnMissingContainment(string detail)
        {
            Log.WarningOnce(
                "[Pawn Diary] Anomaly containment breach capture is disabled because " + detail
                    + "; vanilla escape letters, entity behavior, and other diary routes remain unchanged.",
                "PawnDiary.Anomaly.MissingHook.Containment".GetHashCode());
        }
    }
}

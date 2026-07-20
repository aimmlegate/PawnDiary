// Defensive registration for the exact Anomaly study-commit seam. The paid DLC's C# type exists in
// every RimWorld installation, but this hook is registered only when Anomaly is active; all live
// comp/category/codex reads are forwarded as objects to DlcContext and vanilla always continues.
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using PawnDiary.Capture;

namespace PawnDiary
{
    /// <summary>Registers and owns the optional <c>CompStudyUnlocks.OnStudied</c> before/after hook.</summary>
    internal static class DiaryAnomalyPatches
    {
        private const string StudyUnlocksTypeName = "RimWorld.CompStudyUnlocks";
        private const string KnowledgeCategoryTypeName = "RimWorld.KnowledgeCategoryDef";

        /// <summary>True only when the exact A1.2 study method was found and patched.</summary>
        public static bool StudyHookReady { get; private set; }

        /// <summary>
        /// Registers the exact signature defensively. A changed/missing target disables only study
        /// milestones and leaves vanilla study and the generic StudiedEntity Tale untouched.
        /// </summary>
        public static void TryRegister(Harmony harmony)
        {
            StudyHookReady = false;
            if (harmony == null || !ModsConfig.AnomalyActive) return;

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
    }
}

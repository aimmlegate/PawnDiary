// Exact, optional Ideology mutation boundaries. Registration is manual and signature-verified so a
// RimWorld update disables only the changed route. Prefix/postfix state is detached immediately by
// DlcContext, then pure policy/coalescing owns it; no patch can emit a diary page.
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>Stack-local detached state shared by one mutation prefix and its postfix.</summary>
    internal sealed class BeliefMutationCallState
    {
        public BeliefMutationState before;
        public BeliefMutationState attempted;
        public string causeToken = string.Empty;
        public long startedSequence;
    }

    /// <summary>Registers and handles exact Pawn_IdeoTracker mutation methods.</summary>
    internal static class DiaryIdeologyMutationPatches
    {
        public static bool ConversionAttemptHookReady { get; private set; }
        public static bool OffsetCertaintyHookReady { get; private set; }
        public static bool SetIdeologyHookReady { get; private set; }

        /// <summary>Registers only the exact verified methods while Ideology is active.</summary>
        public static void TryRegister(Harmony harmony)
        {
            ConversionAttemptHookReady = false;
            OffsetCertaintyHookReady = false;
            SetIdeologyHookReady = false;
            if (harmony == null || !ModsConfig.IdeologyActive)
            {
                if (harmony != null)
                {
                    DiaryPatchManifest.Report(
                        "Ideology",
                        "all Ideology mutation hooks",
                        DiaryPatchManifest.HookStatus.Skipped,
                        "Ideology inactive");
                }
                return;
            }
            if (!DlcContext.BeliefMutationProjectionReady)
            {
                Log.Warning("[Pawn Diary] Pawn_IdeoTracker.pawn changed; exact Ideology mutation "
                    + "capture is disabled until the projection adapter is updated.");
                DiaryPatchManifest.Report(
                    "Ideology",
                    "Pawn_IdeoTracker mutation projection",
                    DiaryPatchManifest.HookStatus.Degraded,
                    "Pawn_IdeoTracker.pawn changed; exact mutation capture disabled");
                return;
            }

            ConversionAttemptHookReady = TryPatch(
                harmony,
                AccessTools.DeclaredMethod(typeof(Pawn_IdeoTracker),
                    nameof(Pawn_IdeoTracker.IdeoConversionAttempt),
                    new[] { typeof(float), typeof(Ideo), typeof(bool) }),
                nameof(ConversionAttemptPrefix),
                nameof(ConversionAttemptPostfix),
                "Pawn_IdeoTracker.IdeoConversionAttempt");
            OffsetCertaintyHookReady = TryPatch(
                harmony,
                AccessTools.DeclaredMethod(typeof(Pawn_IdeoTracker),
                    nameof(Pawn_IdeoTracker.OffsetCertainty), new[] { typeof(float) }),
                nameof(OffsetCertaintyPrefix),
                nameof(VoidMutationPostfix),
                "Pawn_IdeoTracker.OffsetCertainty");
            SetIdeologyHookReady = TryPatch(
                harmony,
                AccessTools.DeclaredMethod(typeof(Pawn_IdeoTracker),
                    nameof(Pawn_IdeoTracker.SetIdeo), new[] { typeof(Ideo) }),
                nameof(SetIdeologyPrefix),
                nameof(VoidMutationPostfix),
                "Pawn_IdeoTracker.SetIdeo");
        }

        private static bool TryPatch(
            Harmony harmony,
            MethodBase target,
            string prefixName,
            string postfixName,
            string targetName)
        {
            if (target == null)
            {
                Log.Warning("[Pawn Diary] " + targetName + " changed; exact Ideology mutation "
                    + "capture is disabled for that route.");
                DiaryPatchManifest.Report(
                    "Ideology",
                    targetName,
                    DiaryPatchManifest.HookStatus.Degraded,
                    "target changed; exact mutation capture disabled for this route");
                return false;
            }
            try
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(DiaryIdeologyMutationPatches), prefixName),
                    postfix: new HarmonyMethod(typeof(DiaryIdeologyMutationPatches), postfixName),
                    finalizer: new HarmonyMethod(typeof(DiaryIdeologyMutationPatches), nameof(Finalizer)));
                DiaryPatchManifest.Report(
                    "Ideology", targetName, DiaryPatchManifest.HookStatus.Applied);
                return true;
            }
            catch (Exception exception)
            {
                Log.Warning("[Pawn Diary] Could not register " + targetName
                    + "; exact Ideology mutation capture is disabled for that route. " + exception);
                DiaryPatchManifest.Report(
                    "Ideology",
                    targetName,
                    DiaryPatchManifest.HookStatus.Failed,
                    exception.GetType().Name + ": " + exception.Message);
                return false;
            }
        }

        private static void ConversionAttemptPrefix(
            Pawn_IdeoTracker __instance,
            Ideo initiatorIdeo,
            ref BeliefMutationCallState __state)
        {
            Begin(__instance, initiatorIdeo, BeliefMutationCauseTokens.ConversionAttempt, ref __state);
        }

        private static void OffsetCertaintyPrefix(
            Pawn_IdeoTracker __instance,
            ref BeliefMutationCallState __state)
        {
            Begin(__instance, null, BeliefMutationCauseTokens.CertaintyOffset, ref __state);
        }

        private static void SetIdeologyPrefix(
            Pawn_IdeoTracker __instance,
            ref BeliefMutationCallState __state)
        {
            Begin(__instance, null, BeliefMutationCauseTokens.SetIdeology, ref __state);
        }

        private static void Begin(
            Pawn_IdeoTracker tracker,
            Ideo attemptedIdeology,
            string causeToken,
            ref BeliefMutationCallState state)
        {
            if (!ModsConfig.IdeologyActive || !DiaryGameComponent.GamePlaying || tracker == null) return;
            BeliefMutationCallState captured = DiaryPatchSafety.Run(
                "DiaryIdeologyMutationPatches.Prefix",
                (tracker, attemptedIdeology, causeToken),
                input =>
                {
                    if (!DiaryBeliefPolicy.Enabled) return null;
                    BeliefMutationState before;
                    if (!DlcContext.TryCaptureBeliefMutationState(input.tracker, out before)) return null;
                    return new BeliefMutationCallState
                    {
                        before = before,
                        attempted = DlcContext.CaptureAttemptedBeliefMutationState(input.attemptedIdeology),
                        causeToken = input.causeToken,
                        startedSequence = BeliefMutationCache.NextSequence()
                    };
                },
                (BeliefMutationCallState)null);
            state = captured;
        }

        private static void ConversionAttemptPostfix(
            Pawn_IdeoTracker __instance,
            bool __result,
            BeliefMutationCallState __state)
        {
            Complete(__instance, __state, __result);
        }

        private static void VoidMutationPostfix(
            Pawn_IdeoTracker __instance,
            BeliefMutationCallState __state)
        {
            Complete(__instance, __state, null);
        }

        private static void Complete(
            Pawn_IdeoTracker tracker,
            BeliefMutationCallState state,
            bool? conversionSucceeded)
        {
            if (!ModsConfig.IdeologyActive || state == null || tracker == null) return;
            DiaryPatchSafety.Run(
                "DiaryIdeologyMutationPatches.Postfix",
                (tracker, state, conversionSucceeded),
                input =>
                {
                    BeliefMutationState after;
                    if (!DlcContext.TryCaptureBeliefMutationState(input.tracker, out after)) return;
                    BeliefMutationSnapshot mutation = BeliefMutationPolicy.Create(
                        input.state.before,
                        after,
                        input.state.attempted,
                        input.state.causeToken,
                        input.conversionSucceeded,
                        input.state.startedSequence,
                        BeliefMutationCache.NextSequence());
                    BeliefMutationCache.RecordOrMerge(mutation, DiaryBeliefPolicy.Snapshot());
                });
        }

        private static Exception Finalizer(Exception __exception)
        {
            // __state is stack-local, so a throwing vanilla method leaves no half-open cache entry.
            // Its allocated monotonic sequence number may be skipped; sequence gaps carry no meaning.
            // Return the original exception unchanged; Pawn Diary must not alter vanilla behavior.
            return __exception;
        }
    }
}

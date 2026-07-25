// Central registry for Harmony patches that cannot use bare [HarmonyPatch] discovery safely. Keep
// fragile/generated-name/manual registrations here so startup has one defensive patching choke point.
// New to this? See AGENTS.md ("Harmony patches").
using System;
using HarmonyLib;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Registers manual Harmony patches whose target methods are fragile enough that PatchAll should
    /// not discover them directly. Registrations are isolated so one unexpected patch failure cannot
    /// prevent later optional hooks from being attempted.
    /// </summary>
    internal static class DiaryPatchRegistrar
    {
        /// <summary>
        /// Registers optional reflection-based patches after the attribute-discovered patches finish.
        /// </summary>
        public static void RegisterFragilePatches(Harmony harmony)
        {
            string[] labels =
            {
                nameof(ThoughtGainPatch),
                nameof(FoodIngestionEvidencePatch),
                nameof(DiaryRoyaltyPatches),
                nameof(DiaryAnomalyPatches),
                nameof(QuestUiAcceptPatch),
                nameof(ProximityLetterEventWindowPatch),
                nameof(VoidMonolithActivationEventWindowPatch),
                nameof(PrisonBreakEventWindowPatch),
                nameof(BiotechFamilyHediffPatch),
                nameof(BiotechGrowthLetterPatch),
                nameof(BiotechBirthOutcomePatch),
                nameof(BiotechMiscarriagePatch),
                nameof(BiotechXenogermMutationPatch),
                nameof(DiaryIdeologyMutationPatches),
                nameof(OdysseyLandingEndedPatch),
                nameof(OdysseyLandingOutcomePatch),
                nameof(OdysseyMechhiveOutcomePatch),
                nameof(SpeakUpReplySchedulingGuardPatch),
                nameof(DiaryLogReportPatch),
            };
            Action[] registrations =
            {
                () => ThoughtGainPatch.TryRegister(harmony),
                () => FoodIngestionEvidencePatch.TryRegister(harmony),
                () => DiaryRoyaltyPatches.TryRegister(harmony),
                () => DiaryAnomalyPatches.TryRegister(harmony),
                () => QuestUiAcceptPatch.TryRegister(harmony),
                () => ProximityLetterEventWindowPatch.TryRegister(harmony),
                () => VoidMonolithActivationEventWindowPatch.TryRegister(harmony),
                () => PrisonBreakEventWindowPatch.TryRegister(harmony),
                () => BiotechFamilyHediffPatch.TryRegister(harmony),
                () => BiotechGrowthLetterPatch.TryRegister(harmony),
                () => BiotechBirthOutcomePatch.TryRegister(harmony),
                () => BiotechMiscarriagePatch.TryRegister(harmony),
                () => BiotechXenogermMutationPatch.TryRegister(harmony),
                () => DiaryIdeologyMutationPatches.TryRegister(harmony),
                () => OdysseyLandingEndedPatch.TryRegister(harmony),
                () => OdysseyLandingOutcomePatch.TryRegister(harmony),
                () => OdysseyMechhiveOutcomePatch.TryRegister(harmony),
                () => SpeakUpReplySchedulingGuardPatch.TryRegister(harmony),
                () => DiaryLogReportPatch.TryRegister(harmony),
            };

            IndependentActionRunner.RunAll(registrations, (index, exception) =>
            {
                string label = index >= 0 && index < labels.Length ? labels[index] : "unknown";
                Log.Warning("[Pawn Diary] Fragile patch registration failed for " + label
                    + "; later hooks will still be attempted. " + exception);
                DiaryPatchManifest.Report(
                    "Startup",
                    label,
                    DiaryPatchManifest.HookStatus.Failed,
                    exception.GetType().Name + ": " + exception.Message);
            });
        }
    }
}

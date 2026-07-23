// Central registry for Harmony patches that cannot use bare [HarmonyPatch] discovery safely. Keep
// fragile/generated-name/manual registrations here so startup has one defensive patching choke point.
// New to this? See AGENTS.md ("Harmony patches").
using HarmonyLib;

namespace PawnDiary
{
    /// <summary>
    /// Registers manual Harmony patches whose target methods are fragile enough that PatchAll should
    /// not discover them directly. Each patch's TryRegister method owns its own warning/no-op path.
    /// </summary>
    internal static class DiaryPatchRegistrar
    {
        /// <summary>
        /// Registers optional reflection-based patches after the attribute-discovered patches finish.
        /// </summary>
        public static void RegisterFragilePatches(Harmony harmony)
        {
            ThoughtGainPatch.TryRegister(harmony);
            FoodIngestionEvidencePatch.TryRegister(harmony);
            DiaryRoyaltyPatches.TryRegister(harmony);
            DiaryAnomalyPatches.TryRegister(harmony);
            QuestUiAcceptPatch.TryRegister(harmony);
            ProximityLetterEventWindowPatch.TryRegister(harmony);
            VoidMonolithActivationEventWindowPatch.TryRegister(harmony);
            PrisonBreakEventWindowPatch.TryRegister(harmony);
            BiotechFamilyHediffPatch.TryRegister(harmony);
            BiotechGrowthLetterPatch.TryRegister(harmony);
            BiotechBirthOutcomePatch.TryRegister(harmony);
            BiotechMiscarriagePatch.TryRegister(harmony);
            BiotechXenogermMutationPatch.TryRegister(harmony);
            DiaryIdeologyMutationPatches.TryRegister(harmony);
            OdysseyLandingEndedPatch.TryRegister(harmony);
            OdysseyLandingOutcomePatch.TryRegister(harmony);
            OdysseyMechhiveOutcomePatch.TryRegister(harmony);
            SpeakUpReplySchedulingGuardPatch.TryRegister(harmony);
            DiaryLogReportPatch.TryRegister(harmony);
        }
    }
}

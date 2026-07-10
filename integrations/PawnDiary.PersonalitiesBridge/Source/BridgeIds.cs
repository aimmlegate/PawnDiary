// Frozen identifier constants for the 1-2-3 Personalities bridge.
//
// These strings are REGISTRY / SAVE tokens: the sourceId ends up in Pawn Diary's per-pawn saved
// psychotype-override state, and the provider id lives in Pawn Diary's process-global context-provider
// registry. Renaming any of them after release silently orphans a saved override or double-registers a
// provider, so choose once and never change (mirrors the RimTalk bridge's BridgeIds contract).
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
namespace PawnDiaryPersonalities123
{
    /// <summary>
    /// Stable ids shared across the bridge. Everything here is a frozen token, not tunable policy.
    /// </summary>
    internal static class BridgeIds
    {
        /// <summary>The bridge's packageId. Doubles as the Pawn Diary <c>sourceId</c> that owns this
        /// mod's psychotype (outlook) overrides.</summary>
        public const string ModId = "aimmlegate.pawndiary.adapter.personalities123";

        /// <summary>PackageId of 1-2-3 Personalities Module 1 (the assembly this bridge reads). Every
        /// code path that touches SP_Module1 types checks that this mod is active first.</summary>
        public const string SimplePersonalitiesPackageId = "hahkethomemah.simplepersonalities";

        /// <summary>Registry id for the Tier A "personality=" pawn-context provider.</summary>
        public const string PersonalityProviderId = ModId + ".personality";
    }
}

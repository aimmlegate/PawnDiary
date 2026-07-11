// Frozen identifier constants for the 1-2-3 Personalities bridge.
//
// These strings are REGISTRY / SAVE tokens: ModId ends up in Pawn Diary's per-pawn saved state (as the
// sourceId that owned this mod's old psychotype overrides, and as the sourceId attributed to the bridge's
// LLM transform requests). Renaming it after release silently orphans that saved state, so choose once
// and never change (mirrors the RimTalk bridge's BridgeIds contract).
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
namespace PawnDiaryPersonalities123
{
    /// <summary>
    /// Stable ids shared across the bridge. Everything here is a frozen token, not tunable policy.
    /// </summary>
    internal static class BridgeIds
    {
        /// <summary>The bridge's packageId. Doubles as the Pawn Diary <c>sourceId</c> attributed to this
        /// mod's LLM completion requests, and the sourceId that owned its now-retired psychotype
        /// overrides (still swept once on load so those stale overrides are released).</summary>
        public const string ModId = "aimmlegate.pawndiary.adapter.personalities123";

        /// <summary>PackageId of 1-2-3 Personalities Module 1 (the assembly this bridge reads). Every
        /// code path that touches SP_Module1 types checks that this mod is active first.</summary>
        public const string SimplePersonalitiesPackageId = "hahkethomemah.simplepersonalities";
    }
}

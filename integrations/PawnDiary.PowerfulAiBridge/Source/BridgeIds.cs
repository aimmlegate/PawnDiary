// Stable identifiers shared by the Powerful AI Integration bridge. These strings are save/API
// schema, so they must not be renamed after release.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repository.
namespace PawnDiaryPowerfulAiBridge
{
    /// <summary>Frozen package, source, and Def identifiers for the bridge.</summary>
    internal static class BridgeIds
    {
        public const string ModId = "aimmlegate.pawndiary.adapter.powerfulai";
        public const string PowerfulAiPackageId = "codex.dynamicrolesstoryteller";
        public const string TuningDefName = "PawnDiaryPowerfulAiBridge_Tuning";
    }
}

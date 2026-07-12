// Frozen registry and save identifiers shared by the Rimpsyche bridge.
//
// These values are contracts, not tuning: Pawn Diary stores sourceId/eventKey alongside saved
// diary state. Renaming one after release would orphan an override or make an old external event
// impossible to classify. Keep all such strings in this one deliberately boring file.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repository.
namespace PawnDiaryRimpsyche
{
    /// <summary>Stable identifiers used by every bridge layer.</summary>
    internal static class BridgeIds
    {
        /// <summary>The adapter package id and the owner id for source-owned Pawn Diary state.</summary>
        public const string ModId = "aimmlegate.pawndiary.adapter.rimpsyche";

        /// <summary>The target mod's package id, verified against installed Rimpsyche v1.0.41.</summary>
        public const string RimpsychePackageId = "Maux36.Rimpsyche";

        /// <summary>Process-global id for the Tier-A pawn-context provider.</summary>
        public const string ContextProviderId = ModId + ".psyche";

        /// <summary>
        /// Frozen External-domain eventKey for a charged Rimpsyche conversation. This is save data:
        /// never rename it after release.
        /// </summary>
        public const string ConversationEventKey = "rimpsyche_conversation";

        /// <summary>DefName of the adapter's XML-backed thresholds and caps.</summary>
        public const string TuningDefName = "PawnDiaryRimpsycheBridge_Tuning";
    }
}

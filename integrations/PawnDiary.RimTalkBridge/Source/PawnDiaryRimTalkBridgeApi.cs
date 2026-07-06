// Frozen identifier constants for the RimTalk bridge.
//
// These strings are SAVE/REGISTRY TOKENS: they end up in the player's save (eventKey), in Pawn
// Diary's process-global registries (provider/listener ids, style-override owner id), and in
// RimTalk's prompt registry (modId). Renaming any of them after release silently orphans old data
// or double-registers hooks, so choose once and never change (see design/RIMTALK_BRIDGE_PLAN.md
// Step 0, "Save-data tokens are frozen once shipped").
//
// New to C#/RimWorld? See AGENTS.md. For the public contract, see INTEGRATIONS.md / EXTERNAL_API.md.
namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Stable ids shared across the bridge. Everything here is a frozen token, not tunable policy.
    /// </summary>
    internal static class BridgeIds
    {
        /// <summary>The bridge's packageId. Doubles as the Pawn Diary <c>sourceId</c>, the RimTalk
        /// <c>modId</c> for prompt registrations, and the owner id for writing-style overrides.</summary>
        public const string ModId = "aimmlegate.pawndiary.rimtalkbridge";

        /// <summary>Frozen event key for important-conversation submissions. Must match the
        /// <c>matchDefNames</c> entry in the bridge's External-domain group XML.</summary>
        public const string ConversationEventKey = "rimtalkbridge_conversation";

        /// <summary>Registry id for the Tier A "chat_persona=" pawn-context provider.</summary>
        public const string PersonaProviderId = ModId + ".persona";

        /// <summary>Registry id for the entry-status listener that invalidates the context cache.</summary>
        public const string StatusListenerId = ModId + ".status";

        /// <summary>RimTalk injected-section name for the diary-memories block (also the Scriban
        /// pawn variable name, usable as <c>{{pawn1.diary}}</c> by template editors).</summary>
        public const string DiarySectionName = "diary";
    }
}

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

        /// <summary>Second entry-status listener id, for the pair shared-memory cache (Feature 3).
        /// A separate id so it coexists with <see cref="StatusListenerId"/> (one id = one listener).</summary>
        public const string SharedStatusListenerId = ModId + ".sharedstatus";

        /// <summary>RimTalk injected-section name for the diary-memories block (also the Scriban
        /// pawn variable name, usable as <c>{{pawn1.diary}}</c> by template editors).</summary>
        public const string DiarySectionName = "diary";

        /// <summary>RimTalk environment variable + injected-section name for the colony-situation
        /// block, usable as <c>{{colony_events}}</c> (Feature 1). Frozen token — matches the section
        /// name so both registrations share one identifier.</summary>
        public const string ColonyEventsVariableName = "colony_events";

        /// <summary>RimTalk context variable name for the pair shared-memory block, usable as
        /// <c>{{diary_shared}}</c> (Feature 3). Frozen token.</summary>
        public const string SharedMemoryVariableName = "diary_shared";

        /// <summary>Name of the optional auto-injected RimTalk prompt entry that embeds
        /// <c>{{diary_shared}}</c>. Frozen token — used to find/remove the entry idempotently. This is
        /// an internal registry name, not player-facing prose, so it stays English (not localized).</summary>
        public const string SharedMemoryPromptEntryName = "PawnDiary shared memory";
    }
}

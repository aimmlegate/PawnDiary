// Frozen identifier constants for the RimTalk bridge.
//
// These strings are SAVE/REGISTRY TOKENS: they end up in the player's save (eventKey), in Pawn
// Diary's process-global registries (provider/listener ids, style-override owner id), and in
// RimTalk's prompt registry (modId). Renaming any of them after release silently orphans old data
// or double-registers hooks, so choose once and never change (see design/RIMTALK_BRIDGE_PLAN.md
// Step 0, "Save-data tokens are frozen once shipped").
//
// New to C#/RimWorld? See AGENTS.md. For the public contract, see the Adapter Contract wiki page / EXTERNAL_API.md.
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

        /// <summary>Frozen event key for editorially accepted conversation submissions. Must match the
        /// <c>matchDefNames</c> entry in the bridge's External-domain group XML.</summary>
        public const string ConversationEventKey = "rimtalkbridge_conversation";

        /// <summary>Capability reported ready only after the exact displayed-chat Harmony hook is
        /// installed. The core ambient XML fallback suppresses itself while this id is ready.</summary>
        public const string DisplayedConversationCaptureCapability =
            ModId + ".displayed-conversation";

        /// <summary>Intentional fallback-suppression capability for bridge Levels 0/1, where chat
        /// capture is disabled by player policy even if RimTalk's displayed-chat hook has drifted.</summary>
        public const string ConversationCaptureNotRequestedCapability =
            ModId + ".conversation-capture-not-requested";

        /// <summary>Registry id for the Tier A "chat_persona=" pawn-context provider.</summary>
        public const string PersonaProviderId = ModId + ".persona";

        /// <summary>Registry id for the entry-status listener that invalidates the context cache.</summary>
        public const string StatusListenerId = ModId + ".status";

        /// <summary>Third entry-status listener id, for recent native-event assessment context.</summary>
        public const string AssessmentStatusListenerId = ModId + ".assessmentstatus";

        /// <summary>RimTalk injected-section name for the diary-memories block (also the Scriban
        /// pawn variable name, usable as <c>{{ pawn.diary }}</c> by template editors).</summary>
        public const string DiarySectionName = "diary";

        /// <summary>Opt-in RimTalk pawn variable containing Pawn Diary's psychotype only,
        /// usable as <c>{{ pawn.diary_persona }}</c>. It is registered but never auto-injected.</summary>
        public const string DiaryPersonaVariableName = "diary_persona";

        /// <summary>RimTalk environment variable + injected-section name for the colony-situation
        /// block, usable as <c>{{colony_events}}</c> (Feature 1). Frozen token — matches the section
        /// name so both registrations share one identifier.</summary>
        public const string ColonyEventsVariableName = "colony_events";

    }
}

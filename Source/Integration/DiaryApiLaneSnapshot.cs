// Public read-only DTO for one configured LLM API "lane" (one endpoint/model row in Pawn Diary's
// settings). Returned as part of DiaryApiSetupSnapshot so an adapter can see which providers/models
// the player configured and, if it wants, reuse the same endpoint+key for its own calls.
//
// Keep this class plain: fields only, strings only, no live RimWorld objects. Enum-like values are
// exposed as stable STRING TOKENS (see ApiLaneImport) so the contract does not break if an internal
// enum is renamed.
//
// SECURITY: apiKey is the player's real, plaintext API key. Pawn Diary returns it in full on purpose
// (so an adapter can reuse the player's provider), but treat it as a secret — never log it, never
// send it anywhere the player did not intend. Use hasApiKey when you only need to know whether a key
// is set.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// One configured LLM API lane (endpoint + model + auth) exposed through the public integration API.
    /// </summary>
    public sealed class DiaryApiLaneSnapshot
    {
        /// <summary>Zero-based position of this lane in the player's list. This is also the failover
        /// order (row order controls failover), so treat it as stable only within one read.</summary>
        public int index;

        /// <summary>Base endpoint URL the request is sent to. May be empty on a half-configured row.</summary>
        public string url = string.Empty;

        /// <summary>Model id sent in the request payload. Empty means the row has no model yet.</summary>
        public string model = string.Empty;

        /// <summary>Whether the player enabled this lane (a disabled lane is kept but excluded from use).</summary>
        public bool enabled;

        /// <summary>True when this lane actually participates in generation/failover right now: enabled
        /// AND has both a URL and a model (mirrors PawnDiarySettings.ActiveEndpoints).</summary>
        public bool active;

        /// <summary>Stable auth-mode token: "bearer", "none", "customHeader", or "queryParam".</summary>
        public string authMode = string.Empty;

        /// <summary>Header name used when authMode is "customHeader"; empty otherwise.</summary>
        public string customAuthHeaderName = string.Empty;

        /// <summary>Stable compatibility-mode token: "chatCompletions" or "responses".</summary>
        public string apiMode = string.Empty;

        /// <summary>Reasoning-effort token for the responses mode ("default", "low", "high", ...).</summary>
        public string reasoningEffort = string.Empty;

        /// <summary>Reasoning-tag override for response stripping ("auto", "think", ...).</summary>
        public string reasoningTag = string.Empty;

        /// <summary>Per-lane prompt-context detail override token: "inherit", "full", "balanced", "compact".</summary>
        public string contextDetailOverride = string.Empty;

        /// <summary>True when this lane has a non-empty API key. Prefer this over reading apiKey when
        /// you only need presence, so a secret never has to leave your call site.</summary>
        public bool hasApiKey;

        /// <summary>
        /// The player's real, plaintext API key for this lane (empty when none). SENSITIVE: never log
        /// or forward this. It is exposed so an adapter can reuse the player's provider; that is the
        /// only intended use.
        /// </summary>
        public string apiKey = string.Empty;
    }
}

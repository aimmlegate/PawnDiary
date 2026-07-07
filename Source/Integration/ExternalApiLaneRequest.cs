// Public request DTO an adapter fills in to add a new LLM API "lane" to Pawn Diary's settings through
// PawnDiaryApi.AddApiLane. A new lane defaults to enabled ("active") and, once added, participates in
// generation/failover immediately and is persisted like a lane the player added by hand.
//
// Keep this class plain: fields only, strings only. Enum-like fields are stable STRING TOKENS (see
// ApiLaneImport) so the request shape does not depend on internal enums. Only url and model are
// required; everything else has a safe default.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// Describes a new LLM API lane to add to Pawn Diary's connection settings.
    /// </summary>
    public sealed class ExternalApiLaneRequest
    {
        /// <summary>Your mod's packageId (or any stable id) for log attribution. Optional.</summary>
        public string sourceId = string.Empty;

        /// <summary>Required. Base endpoint URL (Pawn Diary appends the mode-specific path itself).</summary>
        public string url = string.Empty;

        /// <summary>Required. Model id to send in the request payload. A lane with no model is unusable.</summary>
        public string model = string.Empty;

        /// <summary>Optional API key. May be empty for local models that need no auth.</summary>
        public string apiKey = string.Empty;

        /// <summary>Optional auth-mode token: "bearer" (default), "none", "customHeader", "queryParam".</summary>
        public string authMode = string.Empty;

        /// <summary>Optional header name used only when authMode is "customHeader".</summary>
        public string customAuthHeaderName = string.Empty;

        /// <summary>Optional compatibility-mode token: "chatCompletions" (default) or "responses".</summary>
        public string apiMode = string.Empty;

        /// <summary>Optional reasoning-effort token for the responses mode ("default", "low", "high", ...).</summary>
        public string reasoningEffort = string.Empty;

        /// <summary>Optional reasoning-tag override token ("auto", "think", ...).</summary>
        public string reasoningTag = string.Empty;

        /// <summary>Optional per-lane prompt-context detail override token ("inherit", "full", "balanced",
        /// "compact"). Defaults to inheriting the global setting.</summary>
        public string contextDetailOverride = string.Empty;

        /// <summary>When true (default), the added lane is enabled and participates in generation at
        /// once. Set false to add the row but keep it excluded until the player enables it.</summary>
        public bool enabled = true;

        /// <summary>When true (default), a request whose endpoint identity (url + key + auth + mode)
        /// matches an existing lane is treated as a no-op that reports the existing lane instead of
        /// adding a duplicate row. Set false to always append a new row.</summary>
        public bool avoidDuplicate = true;
    }
}

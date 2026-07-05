// Public DTO for adapter-side prompt diagnostics. Unlike the older read APIs, this intentionally
// exposes assembled prompt text; it is for development/configuration previews and never represents a
// saved diary entry or an LLM response.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// Side-effect-free preview of the prompt Pawn Diary would send for one external-event POV.
    /// </summary>
    public sealed class DiaryPromptPreviewSnapshot
    {
        /// <summary>Submitting mod id copied from the request for adapter-side correlation.</summary>
        public string sourceId = string.Empty;

        /// <summary>External event key copied from the request.</summary>
        public string eventKey = string.Empty;

        /// <summary>Prompt POV role: "initiator" for the subject or "recipient" for the partner.</summary>
        public string povRole = string.Empty;

        /// <summary>Subject pawn load id, or empty if unavailable.</summary>
        public string subjectPawnId = string.Empty;

        /// <summary>Partner pawn load id for pairwise previews, or empty for solo previews.</summary>
        public string partnerPawnId = string.Empty;

        /// <summary>True when the request would be treated as a pairwise external event.</summary>
        public bool pairwise;

        /// <summary>The External-domain group that claimed the request key.</summary>
        public string groupDefName = string.Empty;

        /// <summary>The prompt-template key selected by the pure planner.</summary>
        public string templateKey = string.Empty;

        /// <summary>The event-prompt key selected by policy, when one exists.</summary>
        public string eventPromptKey = string.Empty;

        /// <summary>Forced model name selected by event prompt policy, when one exists.</summary>
        public string forcedModelName = string.Empty;

        /// <summary>Response token cap selected by the template/request rules.</summary>
        public int maxTokens;

        /// <summary>
        /// True when this recipient preview cannot include the real hidden initiator entry because no
        /// LLM generation has happened. The live recipient prompt may gain that extra context later.
        /// </summary>
        public bool requiresPriorPovText;

        /// <summary>The assembled system prompt.</summary>
        public string systemPrompt = string.Empty;

        /// <summary>The assembled user prompt.</summary>
        public string userPrompt = string.Empty;

        /// <summary>Human-readable combined prompt using the same format as prompt-test mode.</summary>
        public string combinedPrompt = string.Empty;
    }
}

// Public read-only DTO for integration adapters that want the structured pawn-summary context
// (API v6, capability C-CTX-2). Keep this class plain: fields only, strings/bools/lists only, no
// live RimWorld objects.
//
// This is the structured breakout of the composite `health=` line the diary builds for its own
// prompts. Exporting named fields (instead of the formatted string) lets the assembly keep evolving
// the prompt text without breaking the additive-only integration contract — see
// design/EXTERNAL_API_CAPABILITIES.md §3.8 rule 1.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// Structured health context for one pawn, as the diary would feed it to a prompt. Every field
    /// is empty/false when the pawn is healthy, so the snapshot is always safe to enumerate.
    /// </summary>
    public sealed class DiaryHealthSummarySnapshot
    {
        /// <summary>True when the pawn is currently downed.</summary>
        public bool downed;

        /// <summary>True when the pawn is in pain shock.</summary>
        public bool painShock;

        /// <summary>Localized pain bucket label (e.g. "moderate pain"), or empty when not visible.</summary>
        public string pain = string.Empty;

        /// <summary>Localized bleeding bucket label (e.g. "bleeding heavily"), or empty when not visible.</summary>
        public string bleeding = string.Empty;

        /// <summary>Up to two notable, visible condition labels. Empty when the pawn has none worth noting.</summary>
        public System.Collections.Generic.List<string> conditions = new System.Collections.Generic.List<string>();
    }
}

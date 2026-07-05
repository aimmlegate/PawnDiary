// Public read-only DTO for integration adapters that want the pawn summary Pawn Diary builds for
// its own prompts. This is the "machinery as a service" export: a chat
// or context mod can read the SAME understanding of the pawn the diary feeds the LLM, so its output
// can reflect the diary without Pawn Diary driving another model directly.
//
// Keep this class plain: fields only, strings/lists only, no live RimWorld objects.
//
// Why named fields instead of the `key=value` blob? The internal summary is built as a
// semicolon-delimited string (see DiaryContextBuilder.BuildPawnSummary). Exporting that string
// verbatim would freeze its format under the additive-only contract (renaming `mood=` to
// `emotional_state=` would be a breaking change). Named DTO fields let the assembly keep evolving
// the prompt text while the public snapshot shape stays stable. Same reason GetWritingStyle and
// GetRecentEntryTitles return DTOs. See design/EXTERNAL_API_CAPABILITIES.md §3.8 rule 1.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// Structured snapshot of the pawn-summary context Pawn Diary would feed to one of its own
    /// prompts. This is a side-effect-free read: it never creates a diary record, queues
    /// generation, or spends tokens. Every field is empty for state the diary would omit.
    /// </summary>
    public sealed class DiaryPawnSummarySnapshot
    {
        /// <summary>
        /// Pawn sex as the diary records it (lowercase: "male", "female", or "none"). Never empty.
        /// </summary>
        public string sex = string.Empty;

        /// <summary>
        /// Localized life-stage band (e.g. "adult", "teen"). Empty when age data is unavailable.
        /// </summary>
        public string lifeStage = string.Empty;

        /// <summary>
        /// Biotech xenotype label (e.g. "Sanguophage"). Empty without Biotech or for a plain
        /// Baseliner human, exactly as the prompt omits that line.
        /// </summary>
        public string xenotype = string.Empty;

        /// <summary>
        /// Royalty highest royal title (e.g. "Knight"). Empty without Royalty or a titleless pawn.
        /// </summary>
        public string royalTitle = string.Empty;

        /// <summary>
        /// Ideology faith and role (e.g. "Hidden Truth (Acolyte)"). Empty without Ideology or a
        /// pawn with no ideoligion.
        /// </summary>
        public string faith = string.Empty;

        /// <summary>
        /// Localized mood bucket label (e.g. "content"). Empty when mood is unavailable.
        /// </summary>
        public string mood = string.Empty;

        /// <summary>Structured health context. All-empty when healthy.</summary>
        public DiaryHealthSummarySnapshot health = new DiaryHealthSummarySnapshot();

        /// <summary>
        /// Localized low-capacity keywords (e.g. "limping", "trouble seeing"). Empty when the pawn
        /// has no notably reduced capacities.
        /// </summary>
        public System.Collections.Generic.List<string> lowCapacities = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Up to two localized top-thought labels the diary would surface (one positive and/or one
        /// negative, with their effect bucket). Empty when no notable thoughts are visible.
        /// </summary>
        public System.Collections.Generic.List<string> topThoughts = new System.Collections.Generic.List<string>();

        /// <summary>
        /// External context-provider lines — the contributions other mods made to
        /// this pawn's summary, kept verbatim (each provider owns its own `key=`). Empty when no
        /// provider returned a line. These are the same cleaned strings that join the prompt summary.
        /// </summary>
        public System.Collections.Generic.List<string> providerLines = new System.Collections.Generic.List<string>();
    }
}

// Public read-only DTO for integration adapters that want a pawn's diary writing style as context
// (e.g. a chat mod that lets the player align its own voice with how the pawn writes their diary).
// Keep this class plain: fields only, strings only, no live RimWorld objects.
//
// This is the pawn's BASE saved writing style. It intentionally does NOT reflect temporary
// hediff-driven style overrides (a pawn in agony or on a high writes differently only at prompt
// time); those are prompt-time only and never change the saved style, so they stay out of this
// snapshot.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// One pawn's base diary writing style exposed through the public integration API.
    /// </summary>
    public sealed class DiaryWritingStyleSnapshot
    {
        /// <summary>Stable writing-style defName. Treat it like opaque save data.</summary>
        public string styleDefName = string.Empty;

        /// <summary>Short writing-style label (e.g. "spare-iceberg"). May be empty.</summary>
        public string label = string.Empty;

        /// <summary>
        /// Plain-language description of how this pawn tends to write — the same voice instruction
        /// the diary feeds its own prompts. This is the useful field for adapters that want the
        /// pawn's voice as context. May be empty if the style carries no rule.
        /// </summary>
        public string rule = string.Empty;
    }
}

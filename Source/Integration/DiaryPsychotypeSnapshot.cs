// Public read-only DTO for integration adapters that want a pawn's diary PSYCHOTYPE (outlook) as
// context — the sibling of DiaryWritingStyleSnapshot. Where the writing style is HOW a pawn writes,
// the psychotype is the lens they see through: what they notice, value, and fear. Keep this class
// plain: fields only, strings only, no live RimWorld objects.
//
// This is the EFFECTIVE psychotype (external override > pawn custom > base type). It is empty when the
// psychotype layer is turned off in settings, since nothing then reaches the prompt.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// One pawn's effective diary psychotype exposed through the public integration API.
    /// </summary>
    public sealed class DiaryPsychotypeSnapshot
    {
        /// <summary>Stable base psychotype defName. Treat it like opaque save data. May be Neutral.</summary>
        public string psychotypeDefName = string.Empty;

        /// <summary>Short psychotype label (e.g. "Paranoid"). Picker text only; may be empty.</summary>
        public string label = string.Empty;

        /// <summary>
        /// The effective outlook rule the diary folds into its own prompts — the useful field for
        /// adapters that want the pawn's outlook as context. Empty when the layer is disabled or the
        /// resolved psychotype is Neutral.
        /// </summary>
        public string rule = string.Empty;
    }
}

// The request object for wrapped prompt-entry submissions. It extends the ordinary external
// event request with a required prompt instruction while keeping Pawn Diary in charge of persona,
// safety, context, style, budget, queueing, parsing, and persistence.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// One prompt-bearing diary entry request from another mod. Required: sourceId, eventKey,
    /// subject, and promptInstruction. Optional fields inherited from ExternalEventRequest still
    /// control partner POVs, labels, summaries, context, prompt enchantments, and dedup.
    /// </summary>
    public sealed class ExternalPromptEntryRequest : ExternalEventRequest
    {
        /// <summary>
        /// Caller-authored guidance for what this diary entry should cover. Pawn Diary sanitizes and
        /// caps it, then presents it as a protected prompt field inside the normal first-person diary
        /// wrapper. It is not a raw system prompt and cannot replace Pawn Diary's safety/persona text.
        /// </summary>
        public string promptInstruction;
    }
}

// Public read-filter DTO for integration adapters. Keep this class plain: fields only,
// strings/ints/bools only, no live RimWorld objects. See INTEGRATIONS.md.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// Optional filters for title/context snapshot reads. API v19 adds the richer metadata filters
    /// while keeping blank/default fields equivalent to the original v5 query.
    /// </summary>
    public sealed class DiaryEntryTitleQuery
    {
        /// <summary>
        /// Semantic event domain, e.g. "External", "Thought", "Interaction". Empty means any domain.
        /// </summary>
        public string domain = string.Empty;

        /// <summary>
        /// Rare atmosphere cue such as "fractured", "unsettled", or "memorial". Empty means any cue.
        /// </summary>
        public string atmosphereCue = string.Empty;

        /// <summary>Point-of-view role such as "initiator", "recipient", or "neutral". Empty means any POV.</summary>
        public string povRole = string.Empty;

        /// <summary>
        /// Cleaned external adapter source id. Empty means any source, including native diary entries.
        /// </summary>
        public string sourceId = string.Empty;

        /// <summary>
        /// Saved event classifier key: an external eventKey for External entries, or the source defName
        /// for native entries. Empty means any event key.
        /// </summary>
        public string eventKey = string.Empty;

        /// <summary>
        /// Other pawn id in a paired entry. Empty means any partner/no-partner state.
        /// </summary>
        public string partnerPawnId = string.Empty;

        /// <summary>Case-insensitive date text fragment. Empty means any date.</summary>
        public string dateContains = string.Empty;

        /// <summary>Minimum game tick, inclusive. Negative means no lower tick bound.</summary>
        public int minTick = -1;

        /// <summary>Maximum game tick, inclusive. Negative means no upper tick bound.</summary>
        public int maxTick = -1;

        /// <summary>When false, hot/non-archived entries are excluded.</summary>
        public bool includeActive = true;

        /// <summary>When false, compact archived entries are excluded.</summary>
        public bool includeArchived = true;

        /// <summary>Tri-state importance filter: negative = any, 0 = not important, positive = important.</summary>
        public int important = -1;

        /// <summary>Tri-state title filter: negative = any, 0 = no title, positive = has title.</summary>
        public int hasTitle = -1;

        /// <summary>Tri-state generated-prose filter: negative = any, 0 = no prose, positive = has prose.</summary>
        public int hasGeneratedText = -1;
    }
}

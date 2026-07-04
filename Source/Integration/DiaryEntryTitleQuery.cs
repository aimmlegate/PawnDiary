// Public read-filter DTO for integration adapters. Keep this class plain: fields only,
// strings/ints/bools only, no live RimWorld objects. See INTEGRATIONS.md.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// Optional filters for the v5 title-snapshot read API.
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
    }
}

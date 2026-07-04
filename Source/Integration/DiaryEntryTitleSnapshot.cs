// Public read-only DTO for integration adapters that need a tiny amount of diary context.
// Keep this class plain: fields only, strings/ints/bools only, no live RimWorld objects.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// One recent diary-page title exposed through the public integration API.
    /// </summary>
    public sealed class DiaryEntryTitleSnapshot
    {
        /// <summary>Game tick of the diary event, useful for newest-first sorting.</summary>
        public int tick;

        /// <summary>Human-readable in-game date shown by the Diary tab.</summary>
        public string date = string.Empty;

        /// <summary>Stable event id backing this page. Treat it like opaque save data.</summary>
        public string eventId = string.Empty;

        /// <summary>Point-of-view role used by this pawn's page.</summary>
        public string povRole = string.Empty;

        /// <summary>Stored LLM-generated title. Empty when title generation is off or not yet complete.</summary>
        public string title = string.Empty;

        /// <summary>Event group label that can be used as a fallback display label.</summary>
        public string groupLabel = string.Empty;

        /// <summary>True when this title came from a compact archived diary row.</summary>
        public bool archived;
    }
}

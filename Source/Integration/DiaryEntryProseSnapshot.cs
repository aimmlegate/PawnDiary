// Public read-only DTO for integration adapters that need compact diary prose as memory/context.
// Keep this class plain: fields only, strings/ints/bools only, no live RimWorld objects.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// One recent completed diary page exposed as compact prose through the public integration API.
    /// </summary>
    public sealed class DiaryEntryProseSnapshot
    {
        /// <summary>Game tick of the diary event, useful for newest-first sorting.</summary>
        public int tick;

        /// <summary>Human-readable in-game date shown by the Diary tab.</summary>
        public string date = string.Empty;

        /// <summary>Stable event id backing this page. Treat it like opaque save data.</summary>
        public string eventId = string.Empty;

        /// <summary>Point-of-view role used by this pawn's page.</summary>
        public string povRole = string.Empty;

        /// <summary>Stored LLM-generated title. Empty when title generation is off or not complete.</summary>
        public string title = string.Empty;

        /// <summary>Event group label that can be used as a fallback display label.</summary>
        public string groupLabel = string.Empty;

        /// <summary>True when this entry was authored or influenced through the external API.</summary>
        public bool externallyAuthored;

        /// <summary>Cleaned adapter source id for external entries. Empty for native diary entries.</summary>
        public string externalSourceId = string.Empty;

        /// <summary>Semantic event domain such as External, Thought, Interaction, or Reflection.</summary>
        public string domain = string.Empty;

        /// <summary>Rare atmosphere cue such as fractured, unsettled, or memorial.</summary>
        public string atmosphereCue = string.Empty;

        /// <summary>
        /// First sentence of the completed diary prose, cleaned to one line and length-capped.
        /// </summary>
        public string summary = string.Empty;

        /// <summary>True when this entry came from a compact archived diary row.</summary>
        public bool archived;
    }
}

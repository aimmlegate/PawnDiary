// Public read-only DTO for checking one diary entry's generation lifecycle.
// It exposes compact state only: no prompts, raw provider responses, or live RimWorld objects.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// Current status for one handled diary entry POV.
    /// </summary>
    public sealed class DiaryEntryStatusSnapshot
    {
        /// <summary>Stable handle for this event+POV pair.</summary>
        public DiaryEntryHandle handle;

        /// <summary>Game tick of the diary event.</summary>
        public int tick;

        /// <summary>Human-readable in-game date shown by the Diary tab.</summary>
        public string date = string.Empty;

        /// <summary>Main generation status token: not_generated, pending, complete, failed, skipped, or prompt_only.</summary>
        public string status = string.Empty;

        /// <summary>True while the main diary prose is queued or generating.</summary>
        public bool pending;

        /// <summary>True once generated diary prose exists for this POV.</summary>
        public bool complete;

        /// <summary>True when generation failed or an archived pending entry is stale.</summary>
        public bool failed;

        /// <summary>True when this POV was intentionally skipped.</summary>
        public bool skipped;

        /// <summary>True when prompt-test mode captured a prompt without sending it to an LLM.</summary>
        public bool promptOnly;

        /// <summary>True when this entry is outside the active generation scan window.</summary>
        public bool archived;

        /// <summary>True when an archived entry still has stale pending state.</summary>
        public bool archivedGenerationStale;

        /// <summary>True when generated diary prose exists for this POV.</summary>
        public bool hasGeneratedText;

        /// <summary>Stored LLM-generated title. Empty when title generation is off or not complete.</summary>
        public string title = string.Empty;

        /// <summary>Title-generation status token for this POV.</summary>
        public string titleStatus = string.Empty;

        /// <summary>True while the separate title follow-up is queued or generating.</summary>
        public bool titlePending;

        /// <summary>True once a nonblank title has been stored for this POV.</summary>
        public bool titleComplete;

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

        /// <summary>First sentence of completed diary prose, cleaned to one line and length-capped.</summary>
        public string summary = string.Empty;
    }
}

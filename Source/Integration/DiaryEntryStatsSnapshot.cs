// Public read-only DTO for counting diary entries that match an integration query.
// Fields are primitive-only so adapter mods can consume the snapshot directly or by reflection.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary.Integration
{
    /// <summary>
    /// Aggregate counts for diary entries matching a <see cref="DiaryEntryTitleQuery"/>.
    /// </summary>
    public sealed class DiaryEntryStatsSnapshot
    {
        /// <summary>Total matched entries after query filtering and active/archive de-duplication.</summary>
        public int total;

        /// <summary>Matched hot/non-archived entries.</summary>
        public int active;

        /// <summary>Matched compact archived entries.</summary>
        public int archived;

        /// <summary>Matched entries whose normalized main status is complete.</summary>
        public int complete;

        /// <summary>Matched entries whose normalized main status is pending.</summary>
        public int pending;

        /// <summary>Matched entries whose normalized main status is failed.</summary>
        public int failed;

        /// <summary>Matched entries whose normalized main status is skipped.</summary>
        public int skipped;

        /// <summary>Matched entries whose normalized main status is prompt_only.</summary>
        public int promptOnly;

        /// <summary>Matched entries whose normalized main status is not_generated or unknown.</summary>
        public int notGenerated;

        /// <summary>Matched entries with a stored title.</summary>
        public int withTitle;

        /// <summary>Matched entries with stored generated diary prose.</summary>
        public int withGeneratedText;

        /// <summary>Newest matched event tick, or 0 when no entries match.</summary>
        public int newestTick;

        /// <summary>Oldest matched event tick, or 0 when no entries match.</summary>
        public int oldestTick;

        /// <summary>Human-readable date for the newest matched event.</summary>
        public string newestDate = string.Empty;

        /// <summary>Human-readable date for the oldest matched event.</summary>
        public string oldestDate = string.Empty;
    }
}

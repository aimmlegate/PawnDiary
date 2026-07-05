// Public read-only DTO for integration adapters that want Pawn Diary as a compact memory source.
// The snapshot exposes only player-visible completed diary prose summaries, never prompts, raw
// provider responses, errors, or live RimWorld objects.
//
// New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;

namespace PawnDiary.Integration
{
    /// <summary>
    /// Recent diary prose context for one pawn, newest first.
    /// </summary>
    public sealed class DiaryContextSnapshot
    {
        /// <summary>Returned entries, newest first.</summary>
        public List<DiaryEntryProseSnapshot> entries = new List<DiaryEntryProseSnapshot>();

        /// <summary>Number of entries returned in this snapshot.</summary>
        public int entryCount;

        /// <summary>Newest returned entry tick, or zero when the snapshot is empty.</summary>
        public int newestTick;

        /// <summary>Oldest returned entry tick, or zero when the snapshot is empty.</summary>
        public int oldestTick;

        /// <summary>Newest returned entry date, or empty when the snapshot is empty.</summary>
        public string newestDate = string.Empty;

        /// <summary>Oldest returned entry date, or empty when the snapshot is empty.</summary>
        public string oldestDate = string.Empty;
    }
}

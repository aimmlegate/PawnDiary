// Pure accumulator for integration entry stats. Runtime code snapshots each diary entry into plain
// DiaryEntryTitleFilterFacts; this helper turns matched facts into count/range totals without
// touching live RimWorld objects.
//
// New to C#/RimWorld? See AGENTS.md.
using System;
using PawnDiary.Integration;

namespace PawnDiary
{
    /// <summary>
    /// Counts diary entry facts for the public stats read API.
    /// </summary>
    internal static class DiaryEntryStatsAccumulator
    {
        public static void Add(DiaryEntryStatsSnapshot stats, DiaryEntryTitleFilterFacts facts)
        {
            if (stats == null)
            {
                return;
            }

            bool first = stats.total == 0;
            stats.total++;
            if (facts.archived)
            {
                stats.archived++;
            }
            else
            {
                stats.active++;
            }

            if (facts.hasTitle)
            {
                stats.withTitle++;
            }

            if (facts.hasGeneratedText)
            {
                stats.withGeneratedText++;
            }

            CountStatus(stats, facts);
            UpdateRange(stats, facts, first);
        }

        private static void CountStatus(DiaryEntryStatsSnapshot stats, DiaryEntryTitleFilterFacts facts)
        {
            string status = facts.status ?? string.Empty;
            if (facts.archivedGenerationStale
                || string.Equals(status, DiaryGenerationStatus.Failed, StringComparison.OrdinalIgnoreCase))
            {
                stats.failed++;
            }
            else if (string.Equals(status, DiaryGenerationStatus.Pending, StringComparison.OrdinalIgnoreCase))
            {
                stats.pending++;
            }
            else if (string.Equals(status, DiaryGenerationStatus.Skipped, StringComparison.OrdinalIgnoreCase))
            {
                stats.skipped++;
            }
            else if (string.Equals(status, DiaryGenerationStatus.PromptOnly, StringComparison.OrdinalIgnoreCase))
            {
                stats.promptOnly++;
            }
            else if (string.Equals(status, DiaryGenerationStatus.Complete, StringComparison.OrdinalIgnoreCase))
            {
                stats.complete++;
            }
            else
            {
                stats.notGenerated++;
            }
        }

        private static void UpdateRange(
            DiaryEntryStatsSnapshot stats,
            DiaryEntryTitleFilterFacts facts,
            bool first)
        {
            if (first || facts.tick > stats.newestTick)
            {
                stats.newestTick = facts.tick;
                stats.newestDate = facts.date ?? string.Empty;
            }

            if (first || facts.tick < stats.oldestTick)
            {
                stats.oldestTick = facts.tick;
                stats.oldestDate = facts.date ?? string.Empty;
            }
        }
    }
}

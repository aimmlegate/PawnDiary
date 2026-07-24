// Pure ordering and partition policy for the standalone diary reader's pawn list.
// It deliberately depends only on System collections so the rules can be exercised without loading
// RimWorld, Verse, or Unity assemblies.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Plain input/output row used by <see cref="DiaryReaderListPolicy"/>.
    /// </summary>
    internal sealed class DiaryReaderListRow
    {
        public string pawnId;
        public string name;
        public bool alive;
        public bool isCurrentColonist;
        public int entryCount;
    }

    /// <summary>
    /// Ordered reader rows plus the index where dead/departed subjects begin.
    /// </summary>
    internal sealed class DiaryReaderListResult
    {
        public readonly List<DiaryReaderListRow> rows = new List<DiaryReaderListRow>();
        public int departedDividerIndex;
    }

    /// <summary>
    /// Partitions living colonists from historical subjects and sorts each group predictably.
    /// </summary>
    internal static class DiaryReaderListPolicy
    {
        /// <summary>
        /// Living colonists are always included, even with zero pages. Dead, unresolved, and living
        /// non-colonists are treated as departed and included only when they have pages.
        /// </summary>
        public static DiaryReaderListResult Order(
            IEnumerable<DiaryReaderListRow> source,
            string unknownName)
        {
            List<DiaryReaderListRow> living = new List<DiaryReaderListRow>();
            List<DiaryReaderListRow> departed = new List<DiaryReaderListRow>();
            string fallbackName = unknownName ?? string.Empty;

            if (source != null)
            {
                foreach (DiaryReaderListRow input in source)
                {
                    if (input == null || string.IsNullOrWhiteSpace(input.pawnId))
                    {
                        continue;
                    }

                    DiaryReaderListRow row = new DiaryReaderListRow
                    {
                        pawnId = input.pawnId,
                        name = string.IsNullOrWhiteSpace(input.name) ? fallbackName : input.name.Trim(),
                        alive = input.alive,
                        isCurrentColonist = input.isCurrentColonist,
                        entryCount = Math.Max(0, input.entryCount)
                    };

                    if (row.alive && row.isCurrentColonist)
                    {
                        living.Add(row);
                    }
                    else if (row.entryCount > 0)
                    {
                        departed.Add(row);
                    }
                }
            }

            Comparison<DiaryReaderListRow> comparison = CompareRows;
            living.Sort(comparison);
            departed.Sort(comparison);

            DiaryReaderListResult result = new DiaryReaderListResult();
            result.rows.AddRange(living);
            result.departedDividerIndex = living.Count;
            result.rows.AddRange(departed);
            return result;
        }

        private static int CompareRows(DiaryReaderListRow left, DiaryReaderListRow right)
        {
            int byName = StringComparer.OrdinalIgnoreCase.Compare(left?.name, right?.name);
            return byName != 0
                ? byName
                : StringComparer.Ordinal.Compare(left?.pawnId, right?.pawnId);
        }
    }

    /// <summary>
    /// Pure fixed-window dimensions used by the reader host.
    /// </summary>
    internal struct DiaryReaderWindowSize
    {
        public float width;
        public float height;
    }

    /// <summary>
    /// Responsive reader geometry policy, kept free of Unity types for standalone tests.
    /// </summary>
    internal static class DiaryReaderLayoutPolicy
    {
        public static DiaryReaderWindowSize WindowSize(
            float screenWidth,
            float screenHeight,
            float maxWidth,
            float maxHeight,
            float minWidth,
            float minHeight,
            float screenMargin)
        {
            float availableWidth = Math.Max(1f, screenWidth - Math.Max(0f, screenMargin));
            float availableHeight = Math.Max(1f, screenHeight - Math.Max(0f, screenMargin));
            return new DiaryReaderWindowSize
            {
                width = Math.Max(minWidth, Math.Min(maxWidth, availableWidth)),
                height = Math.Max(minHeight, Math.Min(maxHeight, availableHeight))
            };
        }

        public static float PawnListWidth(
            float innerWidth,
            float compactThreshold,
            float normalWidth,
            float compactWidth)
        {
            return innerWidth < compactThreshold ? compactWidth : normalWidth;
        }

        public static float ReaderWidth(
            float remainingWidth,
            float bookWidth,
            float filterPanelWidth,
            float filterPanelGap,
            float chromePadding)
        {
            float preferred = Math.Max(0f, bookWidth)
                + Math.Max(0f, filterPanelWidth)
                + Math.Max(0f, filterPanelGap)
                + Math.Max(0f, chromePadding);
            return Math.Max(0f, Math.Min(remainingWidth, preferred));
        }
    }
}

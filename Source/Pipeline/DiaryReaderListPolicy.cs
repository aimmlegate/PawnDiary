// Pure ordering and partition policy for the standalone diary reader's pawn list.
// It deliberately depends only on System collections so the rules can be exercised without loading
// RimWorld, Verse, or Unity assemblies.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Player-selectable ordering modes for the standalone reader's pawn directory.
    /// </summary>
    internal enum DiaryReaderSortMode
    {
        NewestPage,
        UnreadCount,
        Name,
        LivingDeparted
    }

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
        public int unreadCount;
        public bool hasLatestEntry;
        public int latestEntryTick;
        public string latestEntryDate;
    }

    /// <summary>
    /// Ordered reader rows plus eligibility and optional living/departed grouping metadata.
    /// </summary>
    internal sealed class DiaryReaderListResult
    {
        public readonly List<DiaryReaderListRow> rows = new List<DiaryReaderListRow>();
        public int departedDividerIndex;
        public int eligibleRowCount;
        public bool groupedByDeparture;
    }

    /// <summary>
    /// Includes current colonists plus historical subjects with pages, then searches and sorts them.
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
            return Order(source, unknownName, DiaryReaderSortMode.LivingDeparted, string.Empty);
        }

        /// <summary>
        /// Includes eligible subjects, applies the case-insensitive name search, then orders the
        /// filtered rows according to the player's selected mode.
        /// </summary>
        public static DiaryReaderListResult Order(
            IEnumerable<DiaryReaderListRow> source,
            string unknownName,
            DiaryReaderSortMode sortMode,
            string searchQuery)
        {
            List<DiaryReaderListRow> eligible = new List<DiaryReaderListRow>();
            string fallbackName = unknownName ?? string.Empty;
            string query = NormalizeSearchQuery(searchQuery);

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
                        entryCount = Math.Max(0, input.entryCount),
                        unreadCount = Math.Max(0, input.unreadCount),
                        hasLatestEntry = input.hasLatestEntry,
                        latestEntryTick = input.latestEntryTick,
                        latestEntryDate = input.latestEntryDate ?? string.Empty
                    };

                    if (row.alive && row.isCurrentColonist)
                    {
                        eligible.Add(row);
                    }
                    else if (row.entryCount > 0)
                    {
                        eligible.Add(row);
                    }
                }
            }

            DiaryReaderListResult result = new DiaryReaderListResult();
            result.eligibleRowCount = eligible.Count;
            result.groupedByDeparture = sortMode == DiaryReaderSortMode.LivingDeparted;
            for (int i = 0; i < eligible.Count; i++)
            {
                DiaryReaderListRow row = eligible[i];
                if (query.Length == 0
                    || (row.name ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.rows.Add(row);
                }
            }

            result.rows.Sort(ComparisonFor(sortMode));
            for (int i = 0; i < result.rows.Count; i++)
            {
                if (!IsDeparted(result.rows[i]))
                {
                    result.departedDividerIndex++;
                }
            }

            return result;
        }

        /// <summary>True when a nonblank directory search is active.</summary>
        public static bool IsSearchActive(string searchQuery)
        {
            return NormalizeSearchQuery(searchQuery).Length > 0;
        }

        /// <summary>True when this row belongs to the historical/departed directory population.</summary>
        public static bool IsDeparted(DiaryReaderListRow row)
        {
            return row == null || !row.alive || !row.isCurrentColonist;
        }

        private static string NormalizeSearchQuery(string searchQuery)
        {
            return string.IsNullOrWhiteSpace(searchQuery) ? string.Empty : searchQuery.Trim();
        }

        private static Comparison<DiaryReaderListRow> ComparisonFor(DiaryReaderSortMode sortMode)
        {
            switch (sortMode)
            {
                case DiaryReaderSortMode.NewestPage:
                    return CompareNewest;
                case DiaryReaderSortMode.UnreadCount:
                    return CompareUnread;
                case DiaryReaderSortMode.Name:
                    return CompareNames;
                default:
                    return CompareLivingDeparted;
            }
        }

        private static int CompareNewest(DiaryReaderListRow left, DiaryReaderListRow right)
        {
            bool leftHasPage = left != null && left.hasLatestEntry;
            bool rightHasPage = right != null && right.hasLatestEntry;
            if (leftHasPage != rightHasPage)
            {
                return leftHasPage ? -1 : 1;
            }

            if (leftHasPage)
            {
                int byTick = right.latestEntryTick.CompareTo(left.latestEntryTick);
                if (byTick != 0)
                {
                    return byTick;
                }
            }

            return CompareNames(left, right);
        }

        private static int CompareUnread(DiaryReaderListRow left, DiaryReaderListRow right)
        {
            int leftUnread = left == null ? 0 : left.unreadCount;
            int rightUnread = right == null ? 0 : right.unreadCount;
            int byUnread = rightUnread.CompareTo(leftUnread);
            return byUnread != 0 ? byUnread : CompareNewest(left, right);
        }

        private static int CompareLivingDeparted(DiaryReaderListRow left, DiaryReaderListRow right)
        {
            bool leftDeparted = IsDeparted(left);
            bool rightDeparted = IsDeparted(right);
            if (leftDeparted != rightDeparted)
            {
                return leftDeparted ? 1 : -1;
            }

            return CompareNames(left, right);
        }

        private static int CompareNames(DiaryReaderListRow left, DiaryReaderListRow right)
        {
            int byName = StringComparer.OrdinalIgnoreCase.Compare(left?.name, right?.name);
            return byName != 0
                ? byName
                : StringComparer.Ordinal.Compare(left?.pawnId, right?.pawnId);
        }
    }

    /// <summary>
    /// Actionable reason shown when the directory has no row it can currently display.
    /// </summary>
    internal enum DiaryReaderDirectoryEmptyReason
    {
        None,
        NoPawns,
        SearchNoMatch,
        DepartedHidden
    }

    /// <summary>
    /// Pure empty-state selection for the reader directory.
    /// </summary>
    internal static class DiaryReaderEmptyStatePolicy
    {
        public static DiaryReaderDirectoryEmptyReason DirectoryReason(
            int eligibleRowCount,
            int filteredRowCount,
            int visibleRowCount,
            bool searchActive,
            bool showDeparted)
        {
            if (visibleRowCount > 0)
            {
                return DiaryReaderDirectoryEmptyReason.None;
            }

            if (eligibleRowCount <= 0)
            {
                return DiaryReaderDirectoryEmptyReason.NoPawns;
            }

            if (!showDeparted && filteredRowCount > 0)
            {
                return DiaryReaderDirectoryEmptyReason.DepartedHidden;
            }

            if (searchActive)
            {
                return DiaryReaderDirectoryEmptyReason.SearchNoMatch;
            }

            return DiaryReaderDirectoryEmptyReason.NoPawns;
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

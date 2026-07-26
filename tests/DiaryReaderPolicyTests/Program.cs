// Standalone tests for the pure diary reader pawn-list policy.
using System;
using System.Collections.Generic;
using PawnDiary;

namespace DiaryReaderPolicyTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestPartitionsAndOrdersRows();
            TestLivingColonistWithZeroPagesIsIncluded();
            TestUnknownNameFallback();
            TestLivingNonColonistIsDeparted();
            TestOffRosterPlayerPawnIsExcludedWithoutPages();
            TestStablePawnIdTiebreak();
            TestSearchMatchesPawnNames();
            TestSortsByNewestPage();
            TestSortsByUnreadCount();
            TestSortsByNameWithoutStatusPartition();
            TestDirectoryEmptyReasons();
            TestResponsiveWindowGeometry();

            Console.WriteLine("DiaryReaderPolicyTests passed: " + assertions + " assertions.");
            return 0;
        }

        private static void TestPartitionsAndOrdersRows()
        {
            DiaryReaderListResult result = DiaryReaderListPolicy.Order(
                new[]
                {
                    Row("live-b", "Zoe", true, true, 2),
                    Row("dead-b", "Yuri", false, true, 3),
                    Row("live-a", "Ada", true, true, 0),
                    Row("dead-a", "Bea", false, true, 1),
                    Row("dead-empty", "Gone", false, true, 0)
                },
                "Unknown pawn");

            Equal(2, result.departedDividerIndex, "divider follows living rows");
            Equal(4, result.rows.Count, "dead rows without pages are excluded");
            Equal("live-a", result.rows[0].pawnId, "living rows sort by name");
            Equal("live-b", result.rows[1].pawnId, "second living row");
            Equal("dead-a", result.rows[2].pawnId, "departed rows sort independently");
            Equal("dead-b", result.rows[3].pawnId, "second departed row");
        }

        private static void TestLivingColonistWithZeroPagesIsIncluded()
        {
            DiaryReaderListResult result = DiaryReaderListPolicy.Order(
                new[] { Row("new-colonist", "New", true, true, 0) },
                "Unknown pawn");

            Equal(1, result.rows.Count, "zero-page living colonist remains selectable");
            Equal(1, result.departedDividerIndex, "zero-page living colonist stays in living group");
        }

        private static void TestUnknownNameFallback()
        {
            DiaryReaderListResult result = DiaryReaderListPolicy.Order(
                new[] { Row("archive-only", "   ", false, false, 4) },
                "Unknown pawn");

            Equal("Unknown pawn", result.rows[0].name, "blank archive name uses caller fallback");
        }

        private static void TestLivingNonColonistIsDeparted()
        {
            DiaryReaderListResult result = DiaryReaderListPolicy.Order(
                new[]
                {
                    Row("visitor-pages", "Visitor", true, false, 2),
                    Row("visitor-empty", "Empty visitor", true, false, 0)
                },
                "Unknown pawn");

            Equal(0, result.departedDividerIndex, "living non-colonist belongs after divider");
            Equal(1, result.rows.Count, "living non-colonist needs pages");
            Equal("visitor-pages", result.rows[0].pawnId, "paged visitor remains historical");
        }

        private static void TestOffRosterPlayerPawnIsExcludedWithoutPages()
        {
            DiaryReaderListResult result = DiaryReaderListPolicy.Order(
                new[] { Row("former-player-pawn", "Former", true, false, 0) },
                "Unknown pawn");

            Equal(0, result.rows.Count,
                "alive player-faction pawn outside the current colonist roster is excluded");
        }

        private static void TestStablePawnIdTiebreak()
        {
            DiaryReaderListResult result = DiaryReaderListPolicy.Order(
                new[]
                {
                    Row("pawn-z", "Same", true, true, 1),
                    Row("pawn-a", "same", true, true, 1)
                },
                "Unknown pawn");

            Equal("pawn-a", result.rows[0].pawnId, "pawn ID breaks case-insensitive name tie");
            Equal("pawn-z", result.rows[1].pawnId, "pawn ID tiebreak is stable");
        }

        private static void TestSearchMatchesPawnNames()
        {
            DiaryReaderListResult result = DiaryReaderListPolicy.Order(
                new[]
                {
                    Row("ada", "Ada Lovelace", true, true, 2),
                    Row("bea", "Beatrice", true, true, 3)
                },
                "Unknown pawn",
                DiaryReaderSortMode.Name,
                "  LOVE ");

            Equal(2, result.eligibleRowCount, "search keeps the pre-filter eligible count");
            Equal(1, result.rows.Count, "search narrows rows by pawn name");
            Equal("ada", result.rows[0].pawnId, "search is trimmed and case-insensitive");
        }

        private static void TestSortsByNewestPage()
        {
            DiaryReaderListResult result = DiaryReaderListPolicy.Order(
                new[]
                {
                    Row("none", "No page", true, true, 0),
                    Row("older", "Older", true, true, 1, latestEntryTick: 100, hasLatestEntry: true),
                    Row("newer", "Newer", false, false, 2, latestEntryTick: 300, hasLatestEntry: true)
                },
                "Unknown pawn",
                DiaryReaderSortMode.NewestPage,
                string.Empty);

            Equal("newer", result.rows[0].pawnId, "newest page sorts first across status groups");
            Equal("older", result.rows[1].pawnId, "older page follows newest");
            Equal("none", result.rows[2].pawnId, "pawn without a finished page sorts last");
        }

        private static void TestSortsByUnreadCount()
        {
            DiaryReaderListResult result = DiaryReaderListPolicy.Order(
                new[]
                {
                    Row("read", "Read", true, true, 4, unreadCount: 0, latestEntryTick: 500, hasLatestEntry: true),
                    Row("one", "One", true, true, 3, unreadCount: 1, latestEntryTick: 300, hasLatestEntry: true),
                    Row("three", "Three", true, true, 2, unreadCount: 3, latestEntryTick: 100, hasLatestEntry: true)
                },
                "Unknown pawn",
                DiaryReaderSortMode.UnreadCount,
                string.Empty);

            Equal("three", result.rows[0].pawnId, "largest unread count sorts first");
            Equal("one", result.rows[1].pawnId, "smaller unread count follows");
            Equal("read", result.rows[2].pawnId, "read pawn sorts after unread pawns");
        }

        private static void TestSortsByNameWithoutStatusPartition()
        {
            DiaryReaderListResult result = DiaryReaderListPolicy.Order(
                new[]
                {
                    Row("living", "Zoe", true, true, 1),
                    Row("departed", "Ada", false, false, 1)
                },
                "Unknown pawn",
                DiaryReaderSortMode.Name,
                string.Empty);

            Equal(false, result.groupedByDeparture, "name mode does not claim grouped sections");
            Equal("departed", result.rows[0].pawnId, "name mode may place departed pawn before living pawn");
            Equal("living", result.rows[1].pawnId, "name mode orders the whole visible directory");
        }

        private static void TestDirectoryEmptyReasons()
        {
            Equal(
                DiaryReaderDirectoryEmptyReason.None,
                DiaryReaderEmptyStatePolicy.DirectoryReason(3, 2, 1, true, false),
                "a visible result needs no empty state");
            Equal(
                DiaryReaderDirectoryEmptyReason.DepartedHidden,
                DiaryReaderEmptyStatePolicy.DirectoryReason(3, 1, 0, true, false),
                "a matching departed row explains the hidden partition");
            Equal(
                DiaryReaderDirectoryEmptyReason.SearchNoMatch,
                DiaryReaderEmptyStatePolicy.DirectoryReason(3, 0, 0, true, true),
                "an empty search result explains how to recover");
            Equal(
                DiaryReaderDirectoryEmptyReason.NoPawns,
                DiaryReaderEmptyStatePolicy.DirectoryReason(0, 0, 0, false, false),
                "an actually empty directory explains the onboarding state");
            Equal(
                DiaryReaderDirectoryEmptyReason.NoPawns,
                DiaryReaderEmptyStatePolicy.DirectoryReason(0, 0, 0, true, false),
                "a stale search cannot disguise an actually empty directory");
        }

        private static void TestResponsiveWindowGeometry()
        {
            DiaryReaderWindowSize fullHd = DiaryReaderLayoutPolicy.WindowSize(
                1920f, 1080f, 1460f, 940f, 760f, 520f, 48f);
            Equal(1460f, fullHd.width, "Full HD width reaches preferred cap");
            Equal(940f, fullHd.height, "Full HD height reaches preferred cap");

            DiaryReaderWindowSize hd = DiaryReaderLayoutPolicy.WindowSize(
                1366f, 768f, 1460f, 940f, 760f, 520f, 48f);
            Equal(1318f, hd.width, "HD width preserves screen margin");
            Equal(720f, hd.height, "HD height preserves screen margin");

            DiaryReaderWindowSize narrow = DiaryReaderLayoutPolicy.WindowSize(
                1280f, 720f, 1460f, 940f, 760f, 520f, 48f);
            Equal(1232f, narrow.width, "1280 width preserves screen margin");
            Equal(672f, narrow.height, "720 height preserves screen margin");
            Equal(170f, DiaryReaderLayoutPolicy.PawnListWidth(1232f, 1360f, 220f, 170f),
                "narrow screen uses compact pawn list");
            Equal(220f, DiaryReaderLayoutPolicy.PawnListWidth(1424f, 1360f, 220f, 170f),
                "wide screen uses normal pawn list");
            Equal(1122f, DiaryReaderLayoutPolicy.ReaderWidth(1200f, 850f, 260f, 12f, 0f),
                "reader width caps at book plus filter columns");
        }

        private static DiaryReaderListRow Row(
            string pawnId,
            string name,
            bool alive,
            bool isCurrentColonist,
            int entryCount,
            int unreadCount = 0,
            int latestEntryTick = 0,
            bool hasLatestEntry = false)
        {
            return new DiaryReaderListRow
            {
                pawnId = pawnId,
                name = name,
                alive = alive,
                isCurrentColonist = isCurrentColonist,
                entryCount = entryCount,
                unreadCount = unreadCount,
                latestEntryTick = latestEntryTick,
                hasLatestEntry = hasLatestEntry
            };
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    message + ": expected " + expected + ", actual " + actual);
            }
        }
    }
}

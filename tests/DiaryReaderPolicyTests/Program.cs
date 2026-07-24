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
            int entryCount)
        {
            return new DiaryReaderListRow
            {
                pawnId = pawnId,
                name = name,
                alive = alive,
                isCurrentColonist = isCurrentColonist,
                entryCount = entryCount
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

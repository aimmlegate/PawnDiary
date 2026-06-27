// Pure unit tests for the per-pawn diary retention planner (DiaryRetentionPlan.Plan). These exercise
// the trim-and-keep decision without RimWorld assemblies. Run via: build DiaryRetentionTests.csproj,
// then execute the resulting exe (exit code 0 = pass), or `dotnet run --project
// tests/DiaryRetentionTests/DiaryRetentionTests.csproj`.
using System;
using System.Collections.Generic;
using PawnDiary;

namespace DiaryRetentionTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestNullInputPlansNothing();
            TestNegativeLimitPlansNothing();
            TestAllUnderCapNoTrim();
            TestExactlyAtCapNoTrim();
            TestSinglePawnOverCapDropsOldestFront();
            TestSurvivorsAreNewestIds();
            TestSharedEventSurvivesUntilAllDrop();
            TestZeroLimitDropsEverything();
            TestNullInnerListSkipped();
            TestBlankIdsIgnored();
            TestReferencedIsCaseInsensitive();
            TestDropCountsAlignByIndex();

            Console.WriteLine("DiaryRetentionTests passed " + assertions + " assertions.");
            return 0;
        }

        // A null pawn list plans nothing: no trim, no survivors, empty drop-count array.
        private static void TestNullInputPlansNothing()
        {
            DiaryRetentionResult plan = DiaryRetentionPlan.Plan(null, 100);
            AssertEqual("null input not trimmed", false, plan.TrimmedAny);
            AssertEqual("null input no survivors", 0, plan.Referenced.Count);
            AssertEqual("null input empty drop counts", 0, plan.DropCounts.Length);
        }

        // A negative limit is a guard sentinel — plan nothing even if pawns have pages.
        private static void TestNegativeLimitPlansNothing()
        {
            DiaryRetentionResult plan = DiaryRetentionPlan.Plan(View(L("a", "b", "c")), -1);
            AssertEqual("negative limit not trimmed", false, plan.TrimmedAny);
            AssertEqual("negative limit no survivors", 0, plan.Referenced.Count);
            AssertEqual("negative limit drop count zero", 0, plan.DropCounts[0]);
        }

        // Every pawn under the cap: nothing to do, and we do not pay to build the survivor set.
        private static void TestAllUnderCapNoTrim()
        {
            DiaryRetentionResult plan = DiaryRetentionPlan.Plan(View(L("a", "b"), L("c")), 5);
            AssertEqual("under cap not trimmed", false, plan.TrimmedAny);
            AssertEqual("under cap no survivors collected", 0, plan.Referenced.Count);
            AssertEqual("under cap pawn0 keeps all", 0, plan.DropCounts[0]);
            AssertEqual("under cap pawn1 keeps all", 0, plan.DropCounts[1]);
        }

        // A list exactly at the cap has zero overflow — the boundary keeps everything.
        private static void TestExactlyAtCapNoTrim()
        {
            DiaryRetentionResult plan = DiaryRetentionPlan.Plan(View(L("a", "b", "c")), 3);
            AssertEqual("at-cap not trimmed", false, plan.TrimmedAny);
            AssertEqual("at-cap drop count zero", 0, plan.DropCounts[0]);
        }

        // Over the cap: drop count is the overflow, and the survivors are the NEWEST ids (tail), proving
        // the oldest sit at the front and are the ones removed.
        private static void TestSinglePawnOverCapDropsOldestFront()
        {
            // 5 ids, cap 2 -> drop the 3 oldest at the front, keep "d","e".
            DiaryRetentionResult plan = DiaryRetentionPlan.Plan(View(L("a", "b", "c", "d", "e")), 2);
            AssertEqual("over cap trimmed", true, plan.TrimmedAny);
            AssertEqual("over cap drop count is overflow", 3, plan.DropCounts[0]);
            AssertEqual("over cap survivor count", 2, plan.Referenced.Count);
            AssertTrue("newest 'd' survives", plan.Referenced.Contains("d"));
            AssertTrue("newest 'e' survives", plan.Referenced.Contains("e"));
            AssertTrue("oldest 'a' dropped", !plan.Referenced.Contains("a"));
            AssertTrue("oldest 'c' dropped", !plan.Referenced.Contains("c"));
        }

        // The survivor set unions across pawns; ids unique to a kept tail all appear once.
        private static void TestSurvivorsAreNewestIds()
        {
            DiaryRetentionResult plan = DiaryRetentionPlan.Plan(
                View(L("p0old", "p0new"), L("p1a", "p1b", "p1c")), 1);
            AssertEqual("two pawns trimmed", true, plan.TrimmedAny);
            AssertEqual("pawn0 drops 1", 1, plan.DropCounts[0]);
            AssertEqual("pawn1 drops 2", 2, plan.DropCounts[1]);
            AssertEqual("survivors are the two newest", 2, plan.Referenced.Count);
            AssertTrue("pawn0 newest survives", plan.Referenced.Contains("p0new"));
            AssertTrue("pawn1 newest survives", plan.Referenced.Contains("p1c"));
        }

        // A page shared by two pawns survives until BOTH drop it: if one still references it, it stays
        // in the survivor union; once neither references it, it is gone.
        private static void TestSharedEventSurvivesUntilAllDrop()
        {
            // "S" is old for pawn0 (front, will be dropped) but recent for pawn1 (kept) -> survives.
            DiaryRetentionResult stillHeld = DiaryRetentionPlan.Plan(
                View(L("S", "p0a", "p0b"), L("p1a", "S")), 2);
            AssertTrue("shared id survives while one pawn keeps it", stillHeld.Referenced.Contains("S"));

            // "S" is the oldest for both pawns and both are over the cap -> dropped by both -> gone.
            DiaryRetentionResult droppedByBoth = DiaryRetentionPlan.Plan(
                View(L("S", "p0a", "p0b"), L("S", "p1a", "p1b")), 2);
            AssertTrue("shared id gone once all pawns drop it", !droppedByBoth.Referenced.Contains("S"));
            AssertTrue("droppedByBoth still trims", droppedByBoth.TrimmedAny);
        }

        // Limit 0 drops every page (overflow == count): trimmed, but no survivors remain.
        private static void TestZeroLimitDropsEverything()
        {
            DiaryRetentionResult plan = DiaryRetentionPlan.Plan(View(L("a", "b")), 0);
            AssertEqual("zero limit trims", true, plan.TrimmedAny);
            AssertEqual("zero limit drops all", 2, plan.DropCounts[0]);
            AssertEqual("zero limit no survivors", 0, plan.Referenced.Count);
        }

        // A null entry among real lists is skipped (its drop count stays 0) and never crashes.
        private static void TestNullInnerListSkipped()
        {
            IReadOnlyList<string>[] view = new IReadOnlyList<string>[]
            {
                L("a", "b", "c"),
                null,
                L("d", "e"),
            };
            DiaryRetentionResult plan = DiaryRetentionPlan.Plan(view, 1);
            AssertEqual("null inner does not break trimming", true, plan.TrimmedAny);
            AssertEqual("pawn0 drops 2", 2, plan.DropCounts[0]);
            AssertEqual("null pawn drops 0", 0, plan.DropCounts[1]);
            AssertEqual("pawn2 drops 1", 1, plan.DropCounts[2]);
            AssertTrue("pawn0 newest survives", plan.Referenced.Contains("c"));
            AssertTrue("pawn2 newest survives", plan.Referenced.Contains("e"));
        }

        // Blank / whitespace ids never enter the survivor set (defensive against odd saves).
        private static void TestBlankIdsIgnored()
        {
            DiaryRetentionResult plan = DiaryRetentionPlan.Plan(View(L("", "  ", "real")), 2);
            AssertEqual("blank ids trim", true, plan.TrimmedAny);
            AssertEqual("only real id survives", 1, plan.Referenced.Count);
            AssertTrue("real id survives", plan.Referenced.Contains("real"));
        }

        // The survivor set is case-insensitive, so the same id surviving in two cases counts once.
        // Both pawns are over the cap (2 > 1) and each keeps its newest id — "ABC" and "abc" — which
        // collapse to a single survivor.
        private static void TestReferencedIsCaseInsensitive()
        {
            DiaryRetentionResult plan = DiaryRetentionPlan.Plan(
                View(L("old0", "ABC"), L("old1", "abc")), 1);
            AssertEqual("case-insensitive dedup collapses ABC/abc", 1, plan.Referenced.Count);
            AssertTrue("kept regardless of case", plan.Referenced.Contains("aBc"));
        }

        // Drop counts are positional: pawn i's drop count is at DropCounts[i], so the caller can apply
        // by index even with null entries interleaved.
        private static void TestDropCountsAlignByIndex()
        {
            IReadOnlyList<string>[] view = new IReadOnlyList<string>[]
            {
                L("only"),                 // under cap -> 0
                L("a", "b", "c", "d"),     // over cap (2) -> 2
            };
            DiaryRetentionResult plan = DiaryRetentionPlan.Plan(view, 2);
            AssertEqual("drop counts length matches input", 2, plan.DropCounts.Length);
            AssertEqual("index 0 aligned", 0, plan.DropCounts[0]);
            AssertEqual("index 1 aligned", 2, plan.DropCounts[1]);
        }

        // ── Builders ──

        private static List<string> L(params string[] ids)
        {
            return new List<string>(ids);
        }

        private static IReadOnlyList<IReadOnlyList<string>> View(params IReadOnlyList<string>[] pawns)
        {
            return pawns;
        }

        // ── Assertions ──

        private static void AssertTrue(string name, bool condition)
        {
            assertions++;
            if (!condition)
            {
                throw new Exception("FAIL: " + name);
            }
        }

        private static void AssertEqual(string name, int expected, int actual)
        {
            assertions++;
            if (expected != actual)
            {
                throw new Exception("FAIL: " + name + " — expected " + expected + ", got " + actual);
            }
        }

        private static void AssertEqual(string name, bool expected, bool actual)
        {
            assertions++;
            if (expected != actual)
            {
                throw new Exception("FAIL: " + name + " — expected " + expected + ", got " + actual);
            }
        }
    }
}

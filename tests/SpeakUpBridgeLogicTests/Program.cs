// Pure unit tests for the SpeakUp whole-conversation decision and sample formatter. Like the other
// tests/* console harnesses, Main runs focused assertions and exits non-zero on any failure. No
// RimWorld/Verse/Unity/Harmony/SpeakUp assembly is loaded here.
using System;
using System.Collections.Generic;
using PawnDiarySpeakUp.Pure;

namespace SpeakUpBridgeLogicTests
{
    internal static class Program
    {
        private static int passed;
        private static int failed;

        private static int Main()
        {
            TestFrozenEventKeyAndDefaults();
            TestBelowThresholdStaysAmbient();
            TestExactThresholdProducesPlan();
            TestConfiguredThresholdIsClamped();
            TestWhitespaceCleanupAndBlankRemoval();
            TestDuplicateLinesAreDroppedInOrder();
            TestSampleLimitAndHardCap();
            TestNoSamplesStillProducesPlan();
            TestLongLinesAreBounded();

            Console.WriteLine();
            Console.WriteLine("SpeakUpBridgeLogicTests: " + passed + " passed, " + failed + " failed.");
            return failed > 0 ? 1 : 0;
        }

        private static void TestFrozenEventKeyAndDefaults()
        {
            Check("eventKey keeps its frozen value",
                TalkSummaryFormat.ConversationEventKey == "speakupbridge_conversation");
            Check("default threshold matches normal SpeakUp length",
                TalkSummaryFormat.DefaultMinimumReplies == 3);
            Check("default sample limit remains compact",
                TalkSummaryFormat.DefaultSampleLineLimit == 3);
        }

        private static void TestBelowThresholdStaysAmbient()
        {
            TalkSummaryPlan plan = TalkSummaryFormat.Plan(2, 3, 3, new[] { "first", "second" });
            Check("two replies below threshold -> null", plan == null);
        }

        private static void TestExactThresholdProducesPlan()
        {
            TalkSummaryPlan plan = TalkSummaryFormat.Plan(3, 3, 3, new[] { "one", "two", "three" });
            Check("exact threshold -> plan", plan != null);
            Check("plan preserves reply count", plan != null && plan.ExchangeCount == 3);
            Check("plan keeps all bounded samples", plan != null && plan.SampledLines.Count == 3);
            Check("joined sample punctuation is stable",
                plan != null && plan.JoinedSamples() == "one | two | three");
        }

        private static void TestConfiguredThresholdIsClamped()
        {
            Check("zero threshold is treated as one",
                TalkSummaryFormat.Plan(1, 0, 1, new[] { "one" }) != null);
            Check("negative reply count cannot pass clamped threshold",
                TalkSummaryFormat.Plan(-1, -4, 1, new[] { "one" }) == null);
        }

        private static void TestWhitespaceCleanupAndBlankRemoval()
        {
            TalkSummaryPlan plan = TalkSummaryFormat.Plan(
                3, 3, 3, new[] { "  first\r\n  line  ", "\t", null, "second\tline" });
            Check("blank samples are dropped", plan != null && plan.SampledLines.Count == 2);
            Check("newlines and repeated spaces fold to one",
                plan != null && plan.SampledLines[0] == "first line");
            Check("tabs fold to one space",
                plan != null && plan.SampledLines[1] == "second line");
        }

        private static void TestDuplicateLinesAreDroppedInOrder()
        {
            TalkSummaryPlan plan = TalkSummaryFormat.Plan(
                4, 3, 4, new[] { "alpha", "beta", "alpha", "gamma", "beta" });
            Check("duplicates are dropped", plan != null && plan.SampledLines.Count == 3);
            Check("first-seen order is preserved",
                plan != null && plan.JoinedSamples() == "alpha | beta | gamma");
        }

        private static void TestSampleLimitAndHardCap()
        {
            string[] lines = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
            TalkSummaryPlan requestedTwo = TalkSummaryFormat.Plan(3, 3, 2, lines);
            TalkSummaryPlan requestedTooMany = TalkSummaryFormat.Plan(3, 3, 99, lines);
            Check("requested sample limit is honored",
                requestedTwo != null && requestedTwo.SampledLines.Count == 2);
            Check("defensive hard sample cap is eight",
                requestedTooMany != null && requestedTooMany.SampledLines.Count == 8);
        }

        private static void TestNoSamplesStillProducesPlan()
        {
            TalkSummaryPlan nullInput = TalkSummaryFormat.Plan(3, 3, 3, null);
            TalkSummaryPlan zeroLimit = TalkSummaryFormat.Plan(3, 3, -1, new[] { "ignored" });
            Check("null sample input still yields threshold plan",
                nullInput != null && nullInput.SampledLines.Count == 0);
            Check("negative sample limit becomes zero",
                zeroLimit != null && zeroLimit.SampledLines.Count == 0);
        }

        private static void TestLongLinesAreBounded()
        {
            string longLine = new string('x', 500);
            TalkSummaryPlan plan = TalkSummaryFormat.Plan(3, 3, 1, new[] { longLine });
            Check("long line is bounded to 300 characters",
                plan != null && plan.SampledLines[0].Length == 300);
            Check("truncated line ends in an ellipsis",
                plan != null && plan.SampledLines[0].EndsWith("…", StringComparison.Ordinal));
        }

        private static void Check(string name, bool condition)
        {
            if (condition)
            {
                passed++;
                Console.WriteLine("  PASS  " + name);
            }
            else
            {
                failed++;
                Console.WriteLine("  FAIL  " + name);
            }
        }
    }
}

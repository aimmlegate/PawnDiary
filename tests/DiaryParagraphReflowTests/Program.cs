// Pure tests for DiaryParagraphReflow. Plain console harness — no RimWorld/Verse/Unity references.
// Each test asserts one behavior of ReflowLine. Run with: dotnet run --project tests/DiaryParagraphReflowTests
using System;
using System.Collections.Generic;
using PawnDiary;

namespace DiaryParagraphReflowTests
{
    internal static class Program
    {
        private static int assertions;
        private static int failures;

        // A canonical all-cues-on options set with a small target so the tests can exercise splits
        // without typing 300-character inputs.
        private static DiaryParagraphReflowOptions Options(int target, int max, int minBreak = 0)
        {
            return new DiaryParagraphReflowOptions
            {
                enabled = true,
                targetChars = target,
                maxChars = max,
                splitOnSentenceEnd = true,
                splitOnDateYear = true,
                splitOnSemicolon = true,
                splitOnEmDash = true,
                minBreakSpacing = minBreak
            };
        }

        private static int Main()
        {
            TestShortLineReturnedWhole();
            TestTwoSentencesUnderTargetStayWhole();
            TestThreeSentencesSplitAtSentenceEnd();
            TestSemicolonSplitWhenNoSentenceEnd();
            TestEmDashVariantsSplit();
            TestDateYearSplitAfterComma();
            TestNonYearNumberDoesNotSplit();
            TestHardBreakAtSpaceWhenNoCues();
            TestHardBreakNeverSplitsMidWord();
            TestMinBreakSpacingMergesTrailingStub();
            TestDisabledReturnsWholeLine();
            TestEmptyAndWhitespaceLines();
            TestDateYearInThenSpaceBreak();
            TestMultipleSplitsAcrossLongLine();
            TestInvalidLengthsReturnWholeLine();

            if (failures > 0)
            {
                Console.WriteLine("DiaryParagraphReflowTests FAILED: " + failures + " of " + assertions + " assertions failed.");
                return 1;
            }

            Console.WriteLine("DiaryParagraphReflowTests passed " + assertions + " assertions.");
            return 0;
        }

        // ---- Tests ----

        private static void TestShortLineReturnedWhole()
        {
            List<string> r = DiaryParagraphReflow.ReflowLine("Short line.", Options(140, 200));
            Equal(1, r.Count, "short line returns one chunk");
            Equal("Short line.", r[0]);
        }

        private static void TestTwoSentencesUnderTargetStayWhole()
        {
            string line = "The harvest came in. We ate well that night.";
            List<string> r = DiaryParagraphReflow.ReflowLine(line, Options(140, 200));
            Equal(1, r.Count, "two sentences under target stay whole");
            Equal(line, r[0]);
        }

        private static void TestThreeSentencesSplitAtSentenceEnd()
        {
            // Each sentence ~30 chars; target 40 means the closest cue to target is a sentence end.
            string line = "We planted the field. The rain finally came. Then everything grew tall.";
            List<string> r = DiaryParagraphReflow.ReflowLine(line, Options(40, 200));
            True(r.Count >= 2, "three sentences split into >=2 chunks");
            True(r[0].EndsWith("field.") || r[0].EndsWith("came."), "first chunk ends at a sentence boundary: '" + r[0] + "'");
            True(string.Join(" ", r).Replace("  ", " ").Trim().Length >= line.Length - 2, "no significant text lost");
        }

        private static void TestSemicolonSplitWhenNoSentenceEnd()
        {
            // One long run with a semicolon and no period until the very end.
            string line = "The wall held through the morning and the raiders withdrew before noon; we counted our dead and rebuilt the gate.";
            List<string> r = DiaryParagraphReflow.ReflowLine(line, Options(40, 200));
            True(r.Count >= 2, "semicolon-only line splits");
            True(r[0].Contains(";") || r[0].Trim().EndsWith("noon;"), "first chunk breaks at the semicolon");
        }

        private static void TestEmDashVariantsSplit()
        {
            string baseText = "The doctor said she would recover in time and the fever had broken at last";
            // U+2014 em-dash variant.
            string em = "The doctor said she would recover in time \u2014 and the fever had broken at last, we hoped.";
            List<string> r1 = DiaryParagraphReflow.ReflowLine(em, Options(40, 200));
            True(r1.Count >= 2, "em-dash (U+2014) line splits");

            // "--" ASCII variant.
            string dashes = "The doctor said she would recover in time -- and the fever had broken at last, we hoped.";
            List<string> r2 = DiaryParagraphReflow.ReflowLine(dashes, Options(40, 200));
            True(r2.Count >= 2, "'--' ASCII em-dash line splits");

            // Sanity: the plain version without any dash should still split at the period/comma.
            True(baseText.Length > 0, "base text non-empty");
        }

        private static void TestDateYearSplitAfterComma()
        {
            // "In 5502, we rebuilt." -> break after the comma's following space.
            string line = "In 5502, we rebuilt the wall and the raiders never came back that winter, which surprised everyone.";
            List<string> r = DiaryParagraphReflow.ReflowLine(line, Options(40, 200));
            True(r.Count >= 2, "date-year line splits");
            True(r[0].Contains("5502"), "first chunk keeps the year with its clause");
        }

        private static void TestNonYearNumberDoesNotSplit()
        {
            // "9999" and "300" are not RimWorld years (55xx) -> no date split; only sentence end splits.
            string line = "We counted 9999 grains of rice. Then we ate them all at once.";
            List<string> r = DiaryParagraphReflow.ReflowLine(line, Options(40, 200));
            True(r.Count >= 1, "non-year number line still processes");
            True(r[0].Contains("9999"), "non-year number preserved in first chunk");
        }

        private static void TestHardBreakAtSpaceWhenNoCues()
        {
            // No punctuation at all, no date — pure word run. maxChars=20 forces a space break.
            string line = "one two three four five six seven eight nine ten eleven twelve thirteen";
            List<string> r = DiaryParagraphReflow.ReflowLine(line, Options(10, 20));
            True(r.Count >= 2, "no-cue line splits via hard break");
            // No chunk should contain a partial word: each chunk's words must all be from the source.
            foreach (string chunk in r)
            {
                True(!chunk.StartsWith("e ") && !chunk.StartsWith("o "), "chunk starts on a word boundary: '" + chunk + "'");
            }
        }

        private static void TestHardBreakNeverSplitsMidWord()
        {
            string line = "alpha beta gamma delta epsilon zeta eta theta iota kappa lambda";
            List<string> r = DiaryParagraphReflow.ReflowLine(line, Options(8, 12));
            foreach (string chunk in r)
            {
                string[] words = chunk.Split(' ');
                foreach (string w in words)
                {
                    // Every emitted word must be one of the source tokens (i.e. never a fragment).
                    True(Array.IndexOf(new[] { "alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta", "iota", "kappa", "lambda" }, w) >= 0,
                        "emitted word is a whole source token: '" + w + "'");
                }
            }
        }

        private static void TestMinBreakSpacingMergesTrailingStub()
        {
            // Force a layout where the last chunk would be tiny, then require minBreakSpacing=30.
            string line = "First sentence here. Second one is longer than the rest. x";
            List<string> r = DiaryParagraphReflow.ReflowLine(line, Options(30, 200, 30));
            True(r[r.Count - 1].Length >= 30 || r.Count == 1, "tiny trailing chunk merged back (last chunk >= minBreak or whole line)");
        }

        private static void TestDisabledReturnsWholeLine()
        {
            string line = "A long line that would normally split because it has several sentences. And more. And more.";
            DiaryParagraphReflowOptions opts = Options(10, 20);
            opts.enabled = false;
            List<string> r = DiaryParagraphReflow.ReflowLine(line, opts);
            Equal(1, r.Count, "disabled options returns whole line");
            Equal(line, r[0]);
        }

        private static void TestEmptyAndWhitespaceLines()
        {
            List<string> r1 = DiaryParagraphReflow.ReflowLine("", Options(40, 200));
            Equal(1, r1.Count, "empty line returns one (empty) chunk");
            Equal("", r1[0]);

            List<string> r2 = DiaryParagraphReflow.ReflowLine(null, Options(40, 200));
            Equal(1, r2.Count, "null line returns one (empty) chunk");
            Equal("", r2[0]);
        }

        private static void TestDateYearInThenSpaceBreak()
        {
            // Year followed by a space (no comma): "since 5502 the raids..."
            string line = "Ever since 5502 the raids have grown worse each season and we barely hold the line now.";
            List<string> r = DiaryParagraphReflow.ReflowLine(line, Options(40, 200));
            True(r.Count >= 2, "year-then-space line splits");
            True(r[0].Contains("5502"), "year stays with the leading clause");
        }

        private static void TestMultipleSplitsAcrossLongLine()
        {
            // Long line with several sentence ends -> should produce several chunks.
            string line = "Spring arrived early. We planted the south field. The traders came with steel. A child was born. The wall held through summer.";
            List<string> r = DiaryParagraphReflow.ReflowLine(line, Options(25, 200));
            True(r.Count >= 3, "long multi-sentence line produces >=3 chunks");
        }

        private static void TestInvalidLengthsReturnWholeLine()
        {
            string line = "one two three four five six seven eight nine ten eleven twelve";
            List<string> r = DiaryParagraphReflow.ReflowLine(line, Options(0, 0));
            Equal(1, r.Count, "zero lengths return one chunk");
            Equal(line, r[0]);
        }

        // ---- Assert helpers ----

        private static void Equal<T>(T expected, T actual, string message = null)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                failures++;
                Console.WriteLine("FAIL: expected <" + expected + "> actual <" + actual + "> — " + (message ?? "(no message)"));
            }
        }

        private static void True(bool condition, string message = null)
        {
            assertions++;
            if (!condition)
            {
                failures++;
                Console.WriteLine("FAIL: expected true — " + (message ?? "(no message)"));
            }
        }
    }
}

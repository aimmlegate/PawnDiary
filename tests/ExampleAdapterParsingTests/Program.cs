// Pure unit tests for ExplorerParsing. Mirrors the shape of the other tests/* console projects: a
// static Main that runs focused assertions and returns non-zero on the first failure.
//
// These run without RimWorld/Verse/Unity — the helpers under test are deliberately pure so the
// explorer's parsing edge cases (multiline paste, comment lines, tri-state round-trips, tick
// bounds) are covered without booting the game.
using System;
using System.Collections.Generic;
using PawnDiaryExampleAdapter;

namespace ExampleAdapterParsingTests
{
    internal static class Program
    {
        private static int passed;
        private static int failed;

        private static int Main()
        {
            TestLinesFromMultiline_BasicSplit();
            TestLinesFromMultiline_DropsBlanksAndComments();
            TestLinesFromMultiline_CapsAtMaxLines();
            TestLinesFromMultiline_NullAndEmpty();
            TestLinesFromMultiline_MixedLineEndings();
            TestMultilineFromLines_RoundTrip();
            TestMultilineFromLines_NullAndEmpty();
            TestLooksLikeEventKey_ValidAndInvalid();
            TestNormalizePovRole();
            TestParseTick();
            TestParsePositiveInt();
            TestTriStateRoundTrip();
            TestTriStateOutOfRange();

            Console.WriteLine("==================================================");
            Console.WriteLine("ExplorerParsing tests: " + passed + " passed, " + failed + " failed.");
            return failed == 0 ? 0 : 1;
        }

        // ---- LinesFromMultiline --------------------------------------------------

        private static void TestLinesFromMultiline_BasicSplit()
        {
            List<string> r = ExplorerParsing.LinesFromMultiline("a=1\nb=2\nc=3");
            Assert(r.Count == 3, "3 non-blank lines → count 3; got " + r.Count);
            Assert(r[0] == "a=1", "first line preserved; got '" + r[0] + "'");
            Assert(r[2] == "c=3", "last line preserved; got '" + r[2] + "'");
        }

        private static void TestLinesFromMultiline_DropsBlanksAndComments()
        {
            List<string> r = ExplorerParsing.LinesFromMultiline("a=1\n\n  \n# a note\nb=2");
            Assert(r.Count == 2, "blank + comment lines dropped; got " + r.Count);
            Assert(r[0] == "a=1" && r[1] == "b=2", "only the two real lines remain");
        }

        private static void TestLinesFromMultiline_CapsAtMaxLines()
        {
            // Build a blob with more lines than the cap.
            string blob = "key0=0";
            for (int i = 1; i < ExplorerParsing.MaxMultilineLines + 20; i++)
            {
                blob += "\nkey" + i + "=" + i;
            }

            List<string> r = ExplorerParsing.LinesFromMultiline(blob);
            Assert(r.Count == ExplorerParsing.MaxMultilineLines,
                "capped at MaxMultilineLines (" + ExplorerParsing.MaxMultilineLines + "); got " + r.Count);
        }

        private static void TestLinesFromMultiline_NullAndEmpty()
        {
            Assert(ExplorerParsing.LinesFromMultiline(null).Count == 0, "null → empty list");
            Assert(ExplorerParsing.LinesFromMultiline("").Count == 0, "empty → empty list");
            Assert(ExplorerParsing.LinesFromMultiline("   \n  \t \n").Count == 0, "only blanks → empty list");
        }

        private static void TestLinesFromMultiline_MixedLineEndings()
        {
            List<string> r = ExplorerParsing.LinesFromMultiline("a=1\r\nb=2\rc=3");
            Assert(r.Count == 3, "CR, LF, CRLF all split; got " + r.Count);
        }

        // ---- MultilineFromLines -------------------------------------------------

        private static void TestMultilineFromLines_RoundTrip()
        {
            List<string> input = new List<string> { "a=1", "b=2", "c=3" };
            string blob = ExplorerParsing.MultilineFromLines(input);
            // Round-trip through LinesFromMultiline and expect the same list back.
            List<string> back = ExplorerParsing.LinesFromMultiline(blob);
            Assert(back.Count == 3, "round-trip preserves count");
            Assert(back[0] == "a=1" && back[2] == "c=3", "round-trip preserves order");
        }

        private static void TestMultilineFromLines_NullAndEmpty()
        {
            Assert(ExplorerParsing.MultilineFromLines(null) == string.Empty, "null → empty string");
            Assert(ExplorerParsing.MultilineFromLines(new List<string>()) == string.Empty, "empty list → empty string");
        }

        // ---- LooksLikeEventKey --------------------------------------------------

        private static void TestLooksLikeEventKey_ValidAndInvalid()
        {
            Assert(ExplorerParsing.LooksLikeEventKey("exampleadapter_quiet_moment"), "snake_case with prefix is valid");
            Assert(!ExplorerParsing.LooksLikeEventKey(""), "empty invalid");
            Assert(!ExplorerParsing.LooksLikeEventKey("   "), "whitespace invalid");
            Assert(!ExplorerParsing.LooksLikeEventKey("nounderscore"), "no underscore invalid");
            Assert(!ExplorerParsing.LooksLikeEventKey("has space_inside"), "space invalid");
            Assert(!ExplorerParsing.LooksLikeEventKey("a".PadRight(200, 'a') + "_x"), "too long invalid");
            Assert(ExplorerParsing.LooksLikeEventKey("a_b"), "minimal two-segment valid");
        }

        // ---- NormalizePovRole ---------------------------------------------------

        private static void TestNormalizePovRole()
        {
            Assert(ExplorerParsing.NormalizePovRole("") == null, "blank → null");
            Assert(ExplorerParsing.NormalizePovRole("   ") == null, "whitespace → null");
            Assert(ExplorerParsing.NormalizePovRole(null) == null, "null → null");
            Assert(ExplorerParsing.NormalizePovRole("initiator") == "initiator", "value preserved");
            Assert(ExplorerParsing.NormalizePovRole("  recipient  ") == "recipient", "value trimmed");
        }

        // ---- ParseTick / ParsePositiveInt --------------------------------------

        private static void TestParseTick()
        {
            Assert(ExplorerParsing.ParseTick("", -1) == -1, "blank → negative default");
            Assert(ExplorerParsing.ParseTick("  ", -1) == -1, "whitespace → negative default");
            Assert(ExplorerParsing.ParseTick("12345", -1) == 12345, "valid positive → value");
            Assert(ExplorerParsing.ParseTick("-5", -1) == -1, "negative → negative default");
            Assert(ExplorerParsing.ParseTick("abc", -1) == -1, "non-numeric → negative default");
        }

        private static void TestParsePositiveInt()
        {
            Assert(ExplorerParsing.ParsePositiveInt("", 5) == 5, "blank → fallback");
            Assert(ExplorerParsing.ParsePositiveInt("0", 5) == 5, "zero → fallback");
            Assert(ExplorerParsing.ParsePositiveInt("-3", 5) == 5, "negative → fallback");
            Assert(ExplorerParsing.ParsePositiveInt("12", 5) == 12, "valid positive → value");
        }

        // ---- TriState round-trip -----------------------------------------------

        private static void TestTriStateRoundTrip()
        {
            for (int ui = 0; ui < 3; ui++)
            {
                int api = ExplorerParsing.TriStateFromIndex(ui);
                int back = ExplorerParsing.IndexFromTriState(api);
                Assert(back == ui, "UI index " + ui + " round-trips through API value " + api);
            }
        }

        private static void TestTriStateOutOfRange()
        {
            Assert(ExplorerParsing.TriStateFromIndex(99) == -1, "out-of-range UI index → any (-1)");
            Assert(ExplorerParsing.TriStateFromIndex(-7) == -1, "negative UI index → any (-1)");
            Assert(ExplorerParsing.IndexFromTriState(99) == 0, "out-of-range API value → UI any (0)");
        }

        // ---- helpers ------------------------------------------------------------

        private static void Assert(bool condition, string message)
        {
            if (condition)
            {
                passed++;
                return;
            }

            failed++;
            Console.WriteLine("FAIL: " + message);
        }
    }
}

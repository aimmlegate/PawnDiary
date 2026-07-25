// Daily digest buffer and low-salience soft cap (Quality Wave §9, B6).
//
// Everything that decides WHETHER an everyday page may be written now, which folded-away moments a
// pawn still remembers at bedtime, and how many highlight slots the important evidence claims first
// is pure and lives in Source/Pipeline/DigestPacingPolicy.cs, so it is verified here without
// RimWorld: cap on/off, under/at/over cap, asymmetric and both-capped pairs, the important bypass,
// saturating counts, four-line newest-wins deduplicating buffers, and the important-first split.
//
// The shipped XML defaults are pinned too, because they are the numbers players actually get.
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private static void TestDigestPacingPolicy()
        {
            TestSoftCapDecisions();
            TestDigestLineBuffer();
            TestImportantFirstHighlightSlots();
            TestShippedDigestPacingXml();
        }

        private static DigestLineCandidate DigestLine(string line, string kind = "thought", int tick = 0)
        {
            return new DigestLineCandidate { line = line, sourceKind = kind, tick = tick };
        }

        private static void TestSoftCapDecisions()
        {
            AssertTrue("a positive cap enables pacing", DigestPacingPolicy.IsSoftCapEnabled(2));
            AssertTrue("cap 0 disables pacing", !DigestPacingPolicy.IsSoftCapEnabled(0));
            AssertTrue("a negative cap disables pacing", !DigestPacingPolicy.IsSoftCapEnabled(-3));

            AssertTrue("under cap is not at cap", !DigestPacingPolicy.IsAtCap(1, 2));
            AssertTrue("exactly at cap is at cap", DigestPacingPolicy.IsAtCap(2, 2));
            AssertTrue("over cap is at cap", DigestPacingPolicy.IsAtCap(9, 2));
            AssertTrue("no count is at cap while pacing is off", !DigestPacingPolicy.IsAtCap(99, 0));

            // A single-writer (solo) page.
            AssertTrue("a solo page under cap still emits",
                !DigestPacingPolicy.ShouldSuppressPage(true, new List<int> { 1 }, 2));
            AssertTrue("a solo page at cap folds into the digest",
                DigestPacingPolicy.ShouldSuppressPage(true, new List<int> { 2 }, 2));

            // Important/combat moments never pace, whatever the counts say.
            AssertTrue("an important page is never suppressed",
                !DigestPacingPolicy.ShouldSuppressPage(false, new List<int> { 9, 9 }, 2));
            AssertTrue("pacing off never suppresses",
                !DigestPacingPolicy.ShouldSuppressPage(true, new List<int> { 9, 9 }, 0));

            // A shared pair page is all-or-nothing: it must never become half-visible.
            AssertTrue("an asymmetric pair still emits for both",
                !DigestPacingPolicy.ShouldSuppressPage(true, new List<int> { 2, 0 }, 2));
            AssertTrue("an asymmetric pair emits whichever side has room",
                !DigestPacingPolicy.ShouldSuppressPage(true, new List<int> { 0, 2 }, 2));
            AssertTrue("only a both-capped pair is suppressed",
                DigestPacingPolicy.ShouldSuppressPage(true, new List<int> { 2, 3 }, 2));

            // Degenerate inputs must not suppress a page (fail open, never silently lose one).
            AssertTrue("no writers means no pacing",
                !DigestPacingPolicy.ShouldSuppressPage(true, new List<int>(), 2));
            AssertTrue("a null writer list means no pacing",
                !DigestPacingPolicy.ShouldSuppressPage(true, null, 2));

            AssertEqual("a fresh day advances to one", 1, DigestPacingPolicy.NextEmittedCount(0));
            AssertEqual("a negative saved count heals to one", 1, DigestPacingPolicy.NextEmittedCount(-4));
            AssertEqual("the count saturates instead of wrapping negative",
                int.MaxValue, DigestPacingPolicy.NextEmittedCount(int.MaxValue));
        }

        private static void TestDigestLineBuffer()
        {
            AssertEqual("a disabled buffer clamps to zero", 0, DigestPacingPolicy.ClampMaxLines(0));
            AssertEqual("a negative buffer clamps to zero", 0, DigestPacingPolicy.ClampMaxLines(-2));
            AssertEqual("the shipped buffer size passes through", 4, DigestPacingPolicy.ClampMaxLines(4));
            AssertEqual("an absurd buffer size is bounded", 32, DigestPacingPolicy.ClampMaxLines(9999));

            List<DigestLineCandidate> buffer = new List<DigestLineCandidate>();
            AssertTrue("the first line is buffered",
                DigestPacingPolicy.AddLine(buffer, DigestLine("chatted about the weather"), 4));
            AssertTrue("a blank line is not buffered",
                !DigestPacingPolicy.AddLine(buffer, DigestLine("   "), 4));
            AssertTrue("a null candidate is not buffered",
                !DigestPacingPolicy.AddLine(buffer, null, 4));
            AssertTrue("a null buffer is a no-op",
                !DigestPacingPolicy.AddLine(null, DigestLine("anything"), 4));
            AssertTrue("a zero-size buffer refuses every line",
                !DigestPacingPolicy.AddLine(buffer, DigestLine("dropped"), 0));
            AssertEqual("only the one usable line was kept", 1, buffer.Count);

            // Exact duplicates are one memory, not two — including a whitespace-padded repeat.
            AssertTrue("an exact duplicate is rejected",
                !DigestPacingPolicy.AddLine(buffer, DigestLine("chatted about the weather"), 4));
            AssertTrue("a padded duplicate is rejected after trimming",
                !DigestPacingPolicy.AddLine(buffer, DigestLine("  chatted about the weather  "), 4));
            AssertEqual("the duplicate did not grow the buffer", 1, buffer.Count);

            // Fill to the cap, then prove the OLDEST row is the one evicted.
            DigestPacingPolicy.AddLine(buffer, DigestLine("hauled steel", "work", 1), 4);
            DigestPacingPolicy.AddLine(buffer, DigestLine("ate a fine meal", "thought", 2), 4);
            DigestPacingPolicy.AddLine(buffer, DigestLine("deep talked", "interaction", 3), 4);
            AssertEqual("the buffer filled to its cap", 4, buffer.Count);
            AssertEqual("the buffer stays chronological", "chatted about the weather", buffer[0].line);

            DigestPacingPolicy.AddLine(buffer, DigestLine("slept badly", "thought", 4), 4);
            AssertEqual("the buffer never exceeds its cap", 4, buffer.Count);
            AssertEqual("the oldest line was evicted", "hauled steel", buffer[0].line);
            AssertEqual("the newest line is last", "slept badly", buffer[3].line);

            // Shrinking the cap mid-day must converge immediately, not one line per call.
            DigestPacingPolicy.AddLine(buffer, DigestLine("chatted again", "interaction", 5), 2);
            AssertEqual("a smaller cap trims the buffer on the next add", 2, buffer.Count);
            AssertEqual("trimming keeps the newest lines", "chatted again", buffer[1].line);

            // The source token is normalized so the saved gameContext tag stays clean.
            List<DigestLineCandidate> tokens = new List<DigestLineCandidate>();
            DigestPacingPolicy.AddLine(tokens, DigestLine("a moment", "  work  "), 4);
            AssertEqual("the source token is trimmed", "work", tokens[0].sourceKind);
            AssertEqual("the line is trimmed", "a moment", tokens[0].line);
        }

        private static void TestImportantFirstHighlightSlots()
        {
            // Important evidence selects first; the background pool only gets what is left.
            AssertEqual("important evidence claims the slots it can fill",
                2, DigestPacingPolicy.ImportantSelectionSlots(2, 3));
            AssertEqual("important evidence never claims more than the cap",
                3, DigestPacingPolicy.ImportantSelectionSlots(9, 3));
            AssertEqual("no important evidence claims no slots",
                0, DigestPacingPolicy.ImportantSelectionSlots(0, 3));
            AssertEqual("a zero highlight cap claims nothing",
                0, DigestPacingPolicy.ImportantSelectionSlots(5, 0));

            AssertEqual("background gets the leftover slots",
                1, DigestPacingPolicy.RemainingSelectionSlots(2, 3));
            AssertEqual("a full important selection leaves nothing",
                0, DigestPacingPolicy.RemainingSelectionSlots(3, 3));
            AssertEqual("an over-full selection cannot go negative",
                0, DigestPacingPolicy.RemainingSelectionSlots(5, 3));
            AssertEqual("an empty important selection leaves every slot",
                3, DigestPacingPolicy.RemainingSelectionSlots(0, 3));
            AssertEqual("a negative selected count is treated as none",
                3, DigestPacingPolicy.RemainingSelectionSlots(-2, 3));
        }

        private static void TestShippedDigestPacingXml()
        {
            XDocument tuning = XDocument.Load(RepoPath("1.6", "Defs", "DiaryTuningDef.xml"));
            XElement def = tuning.Descendants("PawnDiary.DiaryTuningDef").Single();

            AssertEqual("shipped soft cap", "2", ChildValue(def, "lowSalienceDailySoftCap"));
            AssertEqual("shipped digest buffer size", "4", ChildValue(def, "dayDigestMaxLines"));
            AssertEqual("shipped digest weight", "0.25", ChildValue(def, "daySummaryWeightDigest"));

            // The whole no-empty-reflection guarantee: "digest" must never be an important kind, or a
            // day of nothing but small talk would start generating pages.
            List<string> importantKinds = def.Descendants("daySummaryImportantSignalKinds")
                .Elements("li")
                .Select(row => (row.Value ?? string.Empty).Trim())
                .ToList();
            AssertTrue("digest is never an important signal kind",
                !importantKinds.Contains("digest"));
            AssertTrue("the shipped weight keeps digest below plain filler-free evidence",
                float.Parse(ChildValue(def, "daySummaryWeightDigest"),
                    System.Globalization.CultureInfo.InvariantCulture)
                < float.Parse(ChildValue(def, "daySummaryWeightMajorEvent"),
                    System.Globalization.CultureInfo.InvariantCulture));

            // EN + RU parity for the three new Advanced tooltips.
            string[] keys =
            {
                "PawnDiary.Settings.Adv.Help.daySummaryWeightDigest",
                "PawnDiary.Settings.Adv.Help.lowSalienceDailySoftCap",
                "PawnDiary.Settings.Adv.Help.dayDigestMaxLines"
            };
            XDocument english = XDocument.Load(
                RepoPath("Languages", "English", "Keyed", "PawnDiary.xml"));
            XDocument russian = XDocument.Load(
                RepoPath("Languages", "Russian (Русский)", "Keyed", "PawnDiary.xml"));
            for (int i = 0; i < keys.Length; i++)
            {
                AssertTrue("B6 English keyed string " + keys[i],
                    english.Descendants(keys[i]).Count() == 1);
                AssertTrue("B6 Russian keyed string " + keys[i],
                    russian.Descendants(keys[i]).Count() == 1);
            }
        }
    }
}

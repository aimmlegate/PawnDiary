using System;
using PawnDiary;

namespace DiaryMarkdownExportTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            FormatsLocalizedMetadataAndNewestFirstOrdering();
            UsesLifeBoundaryRankAndStableSourceOrderForTickTies();
            UsesUntitledFallbackAndPreservesBodyMarkdown();
            OmitsBlankPagesAndFormatsAnEmptyDiary();
            NullDocumentReturnsEmptyText();

            Console.WriteLine("DiaryMarkdownExportTests passed " + assertions + " assertions.");
            return 0;
        }

        private static void FormatsLocalizedMetadataAndNewestFirstOrdering()
        {
            DiaryMarkdownDocument document = Document();
            document.entries.Add(new DiaryMarkdownEntry
            {
                tick = 100,
                date = "1 Aprimay, 5500",
                title = "The first page",
                category = "Arrival",
                body = "I arrived.\r\n\r\nThe walls looked temporary."
            });
            document.entries.Add(new DiaryMarkdownEntry
            {
                tick = 200,
                date = "2\nAprimay, 5500",
                title = "A\nnew beginning",
                category = "Colony\tlife",
                body = "We planted rice."
            });

            string markdown = Normalize(DiaryMarkdownFormatter.Format(document));

            AssertContains("localized document title", markdown, "# Mira's Diary\n\n");
            AssertContains("title is kept on one line", markdown, "## A new beginning");
            AssertContains("date metadata is kept on one line", markdown, "**Date:** 2 Aprimay, 5500");
            AssertContains("category metadata is kept on one line", markdown, "**Category:** Colony life");
            AssertContains("body paragraphs are preserved", markdown,
                "I arrived.\n\nThe walls looked temporary.");
            AssertTrue(
                "newest page appears first",
                markdown.IndexOf("## A new beginning", StringComparison.Ordinal)
                    < markdown.IndexOf("## The first page", StringComparison.Ordinal));
        }

        private static void UsesLifeBoundaryRankAndStableSourceOrderForTickTies()
        {
            DiaryMarkdownDocument document = Document();
            document.entries.Add(Entry(300, 0, "Ordinary first"));
            document.entries.Add(Entry(300, 0, "Ordinary second"));
            document.entries.Add(Entry(300, 1, "Final death page"));
            document.entries.Add(Entry(300, -1, "Arrival page"));

            string markdown = Normalize(DiaryMarkdownFormatter.Format(document));
            int death = markdown.IndexOf("## Final death page", StringComparison.Ordinal);
            int first = markdown.IndexOf("## Ordinary first", StringComparison.Ordinal);
            int second = markdown.IndexOf("## Ordinary second", StringComparison.Ordinal);
            int arrival = markdown.IndexOf("## Arrival page", StringComparison.Ordinal);

            AssertTrue("final boundary sorts first", death < first);
            AssertTrue("equal-rank pages retain source order", first < second);
            AssertTrue("arrival boundary sorts last", second < arrival);
        }

        private static void UsesUntitledFallbackAndPreservesBodyMarkdown()
        {
            DiaryMarkdownDocument document = Document();
            document.entries.Add(new DiaryMarkdownEntry
            {
                tick = 10,
                date = string.Empty,
                title = " \r\n ",
                category = string.Empty,
                body = "A plain paragraph.\r\n\r\n- one\r\n- two"
            });

            string markdown = Normalize(DiaryMarkdownFormatter.Format(document));

            AssertContains("untitled fallback", markdown, "## Untitled entry");
            AssertContains("body Markdown is preserved", markdown,
                "A plain paragraph.\n\n- one\n- two");
            AssertFalse("blank metadata is omitted", markdown.Contains("**Date:**", StringComparison.Ordinal));
            AssertFalse("blank category is omitted", markdown.Contains("**Category:**", StringComparison.Ordinal));
        }

        private static void OmitsBlankPagesAndFormatsAnEmptyDiary()
        {
            DiaryMarkdownDocument document = Document();
            document.entries.Add(new DiaryMarkdownEntry
            {
                tick = 10,
                title = "Should not appear",
                body = " \r\n "
            });

            string markdown = Normalize(DiaryMarkdownFormatter.Format(document));

            AssertEqual(
                "empty diary output",
                "# Mira's Diary\n\nNo completed pages yet.\n",
                markdown);
        }

        private static void NullDocumentReturnsEmptyText()
        {
            AssertEqual("null document", string.Empty, DiaryMarkdownFormatter.Format(null));
        }

        private static DiaryMarkdownDocument Document()
        {
            return new DiaryMarkdownDocument
            {
                title = "Mira's Diary",
                dateLabel = "Date",
                categoryLabel = "Category",
                untitledEntryLabel = "Untitled entry",
                emptyDiaryText = "No completed pages yet."
            };
        }

        private static DiaryMarkdownEntry Entry(int tick, int boundaryRank, string title)
        {
            return new DiaryMarkdownEntry
            {
                tick = tick,
                boundaryRank = boundaryRank,
                title = title,
                date = "A date",
                category = "A category",
                body = title + " body."
            };
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n");
        }

        private static void AssertContains(string label, string actual, string expected)
        {
            assertions++;
            if (actual == null || !actual.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    label + ": expected to find <" + expected + "> in <" + actual + ">.");
            }
        }

        private static void AssertTrue(string label, bool condition)
        {
            assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(label + ": expected true.");
            }
        }

        private static void AssertFalse(string label, bool condition)
        {
            assertions++;
            if (condition)
            {
                throw new InvalidOperationException(label + ": expected false.");
            }
        }

        private static void AssertEqual(string label, string expected, string actual)
        {
            assertions++;
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    label + ": expected <" + expected + "> but got <" + actual + ">.");
            }
        }
    }
}

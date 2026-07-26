// Pure Markdown formatting for player-facing diary exports.
//
// The RimWorld adapter snapshots one pawn's completed pages into these plain contracts, then this
// helper orders and formats them without touching Verse, Unity, localization, or the filesystem.
// Keeping that split makes the output rules testable in a standalone console project.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary
{
    /// <summary>
    /// Localized document text plus the completed pages that belong to one pawn.
    /// </summary>
    internal sealed class DiaryMarkdownDocument
    {
        public string title;
        public string dateLabel;
        public string categoryLabel;
        public string untitledEntryLabel;
        public string emptyDiaryText;
        public readonly List<DiaryMarkdownMetadata> metadata = new List<DiaryMarkdownMetadata>();
        public readonly List<DiaryMarkdownEntry> entries = new List<DiaryMarkdownEntry>();
    }

    /// <summary>
    /// One localized document-level fact shown below the export title, such as game time or filters.
    /// </summary>
    internal sealed class DiaryMarkdownMetadata
    {
        public string label;
        public string value;
    }

    /// <summary>
    /// Plain player-visible fields for one exported diary page.
    /// </summary>
    internal sealed class DiaryMarkdownEntry
    {
        public int tick;
        public int boundaryRank;
        public string date;
        public string title;
        public string category;
        public string body;
    }

    /// <summary>
    /// Produces a readable Markdown document, newest page first to match the in-game diary.
    /// </summary>
    internal static class DiaryMarkdownFormatter
    {
        private sealed class OrderedEntry
        {
            public DiaryMarkdownEntry entry;
            public int sourceIndex;
        }

        /// <summary>
        /// Formats one pawn's diary. Blank page bodies are omitted, and equal-tick pages keep their
        /// source order after the life-boundary tie-break used by the in-game journal.
        /// </summary>
        public static string Format(DiaryMarkdownDocument document)
        {
            if (document == null)
            {
                return string.Empty;
            }

            StringBuilder markdown = new StringBuilder();
            markdown.Append("# ").AppendLine(SingleLine(document.title));
            markdown.AppendLine();

            bool wroteDocumentMetadata = false;
            for (int i = 0; i < document.metadata.Count; i++)
            {
                DiaryMarkdownMetadata item = document.metadata[i];
                if (item != null && AppendMetadata(markdown, item.label, item.value))
                {
                    wroteDocumentMetadata = true;
                }
            }

            if (wroteDocumentMetadata)
            {
                markdown.AppendLine();
            }

            List<OrderedEntry> ordered = OrderedUsableEntries(document.entries);
            if (ordered.Count == 0)
            {
                string emptyText = NormalizeBody(document.emptyDiaryText);
                if (!string.IsNullOrEmpty(emptyText))
                {
                    markdown.AppendLine(emptyText);
                }

                return markdown.ToString();
            }

            for (int i = 0; i < ordered.Count; i++)
            {
                DiaryMarkdownEntry entry = ordered[i].entry;
                string entryTitle = SingleLine(entry.title);
                if (string.IsNullOrEmpty(entryTitle))
                {
                    entryTitle = SingleLine(document.untitledEntryLabel);
                }

                markdown.Append("## ").AppendLine(entryTitle);
                AppendMetadata(markdown, document.dateLabel, entry.date);
                AppendMetadata(markdown, document.categoryLabel, entry.category);
                markdown.AppendLine();
                markdown.AppendLine(NormalizeBody(entry.body));

                if (i + 1 < ordered.Count)
                {
                    markdown.AppendLine();
                    markdown.AppendLine("---");
                    markdown.AppendLine();
                }
            }

            return markdown.ToString();
        }

        private static List<OrderedEntry> OrderedUsableEntries(IList<DiaryMarkdownEntry> source)
        {
            List<OrderedEntry> ordered = new List<OrderedEntry>();
            if (source == null)
            {
                return ordered;
            }

            for (int i = 0; i < source.Count; i++)
            {
                DiaryMarkdownEntry entry = source[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.body))
                {
                    continue;
                }

                ordered.Add(new OrderedEntry
                {
                    entry = entry,
                    sourceIndex = i
                });
            }

            ordered.Sort(CompareEntries);
            return ordered;
        }

        private static int CompareEntries(OrderedEntry left, OrderedEntry right)
        {
            int byTick = right.entry.tick.CompareTo(left.entry.tick);
            if (byTick != 0)
            {
                return byTick;
            }

            int byBoundary = right.entry.boundaryRank.CompareTo(left.entry.boundaryRank);
            return byBoundary != 0 ? byBoundary : left.sourceIndex.CompareTo(right.sourceIndex);
        }

        private static bool AppendMetadata(StringBuilder markdown, string label, string value)
        {
            string cleanLabel = SingleLine(label);
            string cleanValue = SingleLine(value);
            if (string.IsNullOrEmpty(cleanLabel) || string.IsNullOrEmpty(cleanValue))
            {
                return false;
            }

            markdown.Append("**")
                .Append(cleanLabel)
                .Append(":** ")
                .AppendLine(cleanValue);
            return true;
        }

        /// <summary>
        /// Headings and metadata must stay on one Markdown line even when a mod supplies unusual text.
        /// </summary>
        private static string SingleLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder result = new StringBuilder(value.Length);
            bool previousWasWhitespace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (char.IsWhiteSpace(current))
                {
                    previousWasWhitespace = result.Length > 0;
                    continue;
                }

                if (previousWasWhitespace)
                {
                    result.Append(' ');
                    previousWasWhitespace = false;
                }

                result.Append(current);
            }

            return result.ToString().Trim();
        }

        /// <summary>
        /// Normalizes platform line endings while preserving intentional Markdown paragraphs.
        /// </summary>
        private static string NormalizeBody(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            return normalized.Replace("\n", Environment.NewLine);
        }
    }
}

// Pure sentence excerpt helpers for prompt continuity.
//
// The runtime event factory uses these helpers to copy a short ending from the previous diary page
// into the next event's plain payload. Keeping sentence slicing here (instead of inside the
// RimWorld-facing context builder) makes the text rules testable without Verse/Unity assemblies.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PawnDiary
{
    /// <summary>
    /// Extracts compact sentence snippets from diary text for prompt context. Pure: no game state,
    /// no Defs, no localization, and no IO.
    /// </summary>
    public static class DiarySentenceExcerpt
    {
        private static readonly Regex RichTextTagRegex = new Regex("<.*?>");
        private static readonly Regex WhitespaceRegex = new Regex("\\s+");

        /// <summary>
        /// Returns the first sentence from <paramref name="text"/>, collapsed onto one line and capped
        /// from the right. Used by the public integration context snapshot so adapters get a small
        /// memory excerpt without the full diary prose.
        /// </summary>
        public static string FirstSentence(string text, int maxChars)
        {
            string cleaned = CleanLine(text);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            List<int> ends = SentenceEndIndexes(cleaned);
            string first = ends.Count == 0
                ? cleaned
                : cleaned.Substring(0, ends[0] + 1).Trim();

            return CapFromRight(first, maxChars);
        }

        /// <summary>
        /// Returns the final <paramref name="sentenceCount"/> sentences from <paramref name="text"/>,
        /// collapsed onto one line and capped from the left so the latest words are preserved.
        /// </summary>
        public static string LastSentences(string text, int sentenceCount, int maxChars)
        {
            string cleaned = CleanLine(text);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            int count = Math.Max(1, sentenceCount);
            List<int> ends = SentenceEndIndexes(cleaned);
            if (ends.Count == 0)
            {
                return CapFromLeft(cleaned, maxChars);
            }

            int start = 0;
            if (ends.Count > count)
            {
                start = ends[ends.Count - count - 1] + 1;
                while (start < cleaned.Length && char.IsWhiteSpace(cleaned[start]))
                {
                    start++;
                }
            }

            return CapFromLeft(cleaned.Substring(start).Trim(), maxChars);
        }

        private static string CapFromRight(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            int cap = Math.Max(0, maxChars);
            if (cap <= 0 || value.Length <= cap)
            {
                return value;
            }

            if (cap <= 3)
            {
                return TextTruncation.SafePrefix(value, cap);
            }

            return TextTruncation.SafePrefix(value, cap - 3).TrimEnd() + "...";
        }

        private static List<int> SentenceEndIndexes(string text)
        {
            List<int> ends = new List<int>();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '.' && c != '!' && c != '?')
                {
                    continue;
                }

                int end = IncludeTrailingClosers(text, i);
                ends.Add(end);
                i = end;
            }

            return ends;
        }

        private static int IncludeTrailingClosers(string text, int punctuationIndex)
        {
            int end = punctuationIndex;
            for (int i = punctuationIndex + 1; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\'' || c == ')' || c == ']' || c == '}')
                {
                    end = i;
                    continue;
                }

                break;
            }

            return end;
        }

        private static string CapFromLeft(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            int cap = Math.Max(0, maxChars);
            if (cap <= 0 || value.Length <= cap)
            {
                return value;
            }

            if (cap <= 3)
            {
                return TextTruncation.SafeSuffix(value, cap);
            }

            string suffix = TextTruncation.SafeSuffix(value, cap - 3).TrimStart();
            return "..." + suffix;
        }

        private static string CleanLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string cleaned = value.Replace("\r", " ").Replace("\n", " ");
            cleaned = RichTextTagRegex.Replace(cleaned, string.Empty);
            return WhitespaceRegex.Replace(cleaned, " ").Trim();
        }
    }
}

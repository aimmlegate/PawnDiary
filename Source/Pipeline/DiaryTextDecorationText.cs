// Shared pure text primitives for the diary decoration + name-highlight pipeline.
//
// The rule matcher (DiaryTextDecorationMatcher), the rich-text decorators (DiaryRichTextDecorators),
// and the name highlighter (DiaryNameHighlighter) all walk Unity rich text the same way: trim a
// token, compare a decoration kind case-insensitively, and copy an angle-bracket tag through
// untouched. Those primitives used to be copy-pasted into each file, so a fix to tag handling (an
// unclosed '<', a nested tag) had to be made in two or three places or the files would silently
// disagree. Keeping them here once means there is a single source of truth. Stays Verse-free so the
// pure console tests can compile it alongside the files that use it.
using System;
using System.Text;

namespace PawnDiary
{
    /// <summary>
    /// Deterministic, allocation-light helpers for scanning and comparing Unity rich text.
    /// </summary>
    internal static class DiaryTextDecorationText
    {
        /// <summary>
        /// Null-safe trim: returns <see cref="string.Empty"/> for null so callers can compare without
        /// repeating the null guard.
        /// </summary>
        internal static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        /// <summary>
        /// Case-insensitive comparison of a (possibly untrimmed) decoration-kind id against a known
        /// kind constant.
        /// </summary>
        internal static bool KindEquals(string actual, string expected)
        {
            return string.Equals(Trim(actual), expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// When the cursor sits on a Unity rich-text tag ('&lt;...&gt;'), copies the whole tag through
        /// to <paramref name="result"/> unchanged and advances <paramref name="index"/> past it. Returns
        /// false (leaving the cursor untouched) when the current char is not '&lt;' or the tag is
        /// unterminated, so the caller can treat that char as ordinary visible text.
        /// </summary>
        internal static bool TryCopyRichTextTag(string rich, ref int index, StringBuilder result)
        {
            if (index >= rich.Length || rich[index] != '<')
            {
                return false;
            }

            int tagEnd = rich.IndexOf('>', index);
            if (tagEnd < 0)
            {
                return false;
            }

            result.Append(rich, index, tagEnd - index + 1);
            index = tagEnd + 1;
            return true;
        }
    }
}

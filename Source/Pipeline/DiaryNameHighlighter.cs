// Pure rich-text name highlighting for diary pages.
//
// The UI layer resolves live RimWorld pawns into these plain name/color facts. This helper then
// rewrites only visible text, preserving Unity rich-text tags already produced by DiaryTextFormat
// and the XML decoration pipeline.
using System;
using System.Collections.Generic;
using System.Text;
using static PawnDiary.DiaryTextDecorationText;

namespace PawnDiary
{
    /// <summary>
    /// Plain display instruction for one known pawn name. Empty color means "bold only".
    /// </summary>
    public class DiaryNameHighlight
    {
        public string name;
        public string colorHex;
    }

    /// <summary>
    /// Applies deterministic, display-only pawn-name highlights to Unity rich text.
    /// </summary>
    public static class DiaryNameHighlighter
    {
        public static string ApplyToRichText(string rich, IEnumerable<DiaryNameHighlight> highlights)
        {
            string text = rich ?? string.Empty;
            List<DiaryNameHighlight> normalized = Normalize(highlights);
            if (text.Length == 0 || normalized.Count == 0)
            {
                return text;
            }

            StringBuilder result = new StringBuilder(text.Length + 32);
            for (int i = 0; i < text.Length;)
            {
                if (TryCopyRichTextTag(text, ref i, result))
                {
                    continue;
                }

                int segmentStart = i;
                while (i < text.Length && text[i] != '<')
                {
                    i++;
                }

                HighlightVisibleSegment(text, segmentStart, i, normalized, result);
            }

            return result.ToString();
        }

        private static List<DiaryNameHighlight> Normalize(IEnumerable<DiaryNameHighlight> highlights)
        {
            List<DiaryNameHighlight> result = new List<DiaryNameHighlight>();
            if (highlights == null)
            {
                return result;
            }

            foreach (DiaryNameHighlight highlight in highlights)
            {
                string name = highlight?.name == null ? string.Empty : highlight.name.Trim();
                if (name.Length < 2)
                {
                    continue;
                }

                if (ContainsName(result, name))
                {
                    continue;
                }

                result.Add(new DiaryNameHighlight
                {
                    name = name,
                    colorHex = CleanColorHex(highlight.colorHex)
                });
            }

            result.Sort((left, right) => right.name.Length.CompareTo(left.name.Length));
            return result;
        }

        private static void HighlightVisibleSegment(
            string text,
            int start,
            int end,
            List<DiaryNameHighlight> highlights,
            StringBuilder result)
        {
            int i = start;
            while (i < end)
            {
                DiaryNameHighlight match = FindMatch(text, i, end, highlights);
                if (match == null)
                {
                    result.Append(text[i]);
                    i++;
                    continue;
                }

                string visible = text.Substring(i, match.name.Length);
                AppendHighlight(result, visible, match.colorHex);
                i += match.name.Length;
            }
        }

        private static DiaryNameHighlight FindMatch(
            string text,
            int index,
            int end,
            List<DiaryNameHighlight> highlights)
        {
            for (int i = 0; i < highlights.Count; i++)
            {
                DiaryNameHighlight highlight = highlights[i];
                int length = highlight.name.Length;
                if (index + length > end)
                {
                    continue;
                }

                if (!CharsEqualIgnoreCase(text[index], highlight.name[0]))
                {
                    continue;
                }

                if (!BoundaryBefore(text, index) || !BoundaryAfter(text, index + length, end))
                {
                    continue;
                }

                if (string.Compare(text, index, highlight.name, 0, length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return highlight;
                }
            }

            return null;
        }

        private static void AppendHighlight(StringBuilder result, string visible, string colorHex)
        {
            if (string.IsNullOrEmpty(colorHex))
            {
                result.Append("<b>");
                result.Append(visible);
                result.Append("</b>");
                return;
            }

            result.Append("<color=#");
            result.Append(colorHex);
            result.Append("><b>");
            result.Append(visible);
            result.Append("</b></color>");
        }

        // TryCopyRichTextTag now lives in DiaryTextDecorationText (shared with the decoration
        // pipeline); see the `using static` at the top of this file.

        private static bool BoundaryBefore(string text, int index)
        {
            return index <= 0 || !IsNameWordChar(text[index - 1]);
        }

        private static bool BoundaryAfter(string text, int index, int end)
        {
            return index >= end || !IsNameWordChar(text[index]);
        }

        private static bool IsNameWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        private static bool CharsEqualIgnoreCase(char left, char right)
        {
            return char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
        }

        private static bool ContainsName(List<DiaryNameHighlight> highlights, string name)
        {
            for (int i = 0; i < highlights.Count; i++)
            {
                if (string.Equals(highlights[i].name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string CleanColorHex(string colorHex)
        {
            string value = colorHex == null ? string.Empty : colorHex.Trim().TrimStart('#');
            if (value.Length != 6)
            {
                return string.Empty;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return string.Empty;
                }
            }

            return value.ToUpperInvariant();
        }
    }
}

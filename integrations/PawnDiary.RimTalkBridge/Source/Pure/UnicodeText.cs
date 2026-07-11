// Shared Unicode-safe text primitives for the RimTalk conversation-selection pipeline. This file
// is pure (no RimWorld, RimTalk, settings, Defs, or translation) and is linked into the standalone
// bridge logic tests.
//
// The assessor deals with player text in every supported language. These helpers deliberately use
// Unicode categories instead of an English stop-word list, and every length cap preserves UTF-16
// surrogate pairs so an emoji or non-BMP letter is never cut in half.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Language-neutral normalization, tokenization, and safe text-capping helpers.</summary>
    public static class UnicodeText
    {
        /// <summary>
        /// Compatibility-normalizes, lowercases invariantly, keeps Unicode letters/numbers/marks,
        /// and collapses every other run to one ASCII space.
        /// </summary>
        public static string NormalizeForMatching(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized;
            try
            {
                normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                // Malformed unpaired surrogates are not valid Unicode, but chat supplied by another
                // mod should still fail soft. They are treated as separators by the category scan.
                normalized = value.ToLowerInvariant();
            }
            StringBuilder result = new StringBuilder(normalized.Length);
            bool pendingSpace = false;

            for (int i = 0; i < normalized.Length;)
            {
                int codeUnitCount = char.IsHighSurrogate(normalized[i])
                    && i + 1 < normalized.Length
                    && char.IsLowSurrogate(normalized[i + 1]) ? 2 : 1;
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(normalized, i);

                if (IsWordCategory(category))
                {
                    if (pendingSpace && result.Length > 0)
                    {
                        result.Append(' ');
                    }

                    result.Append(normalized, i, codeUnitCount);
                    pendingSpace = false;
                }
                else
                {
                    pendingSpace = result.Length > 0;
                }

                i += codeUnitCount;
            }

            return result.ToString();
        }

        /// <summary>Returns distinct normalized tokens, omitting tokens shorter than the supplied cap.</summary>
        public static HashSet<string> TokenSet(string value, int minimumTokenChars)
        {
            HashSet<string> tokens = new HashSet<string>(StringComparer.Ordinal);
            string normalized = NormalizeForMatching(value);
            if (normalized.Length == 0)
            {
                return tokens;
            }

            int minimum = minimumTokenChars < 1 ? 1 : minimumTokenChars;
            string[] parts = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (TextElementCount(parts[i]) >= minimum)
                {
                    tokens.Add(parts[i]);
                }
            }

            return tokens;
        }

        /// <summary>
        /// True when a normalized term occurs as one or more complete normalized tokens. A category
        /// can therefore contain phrases without matching a fragment inside an unrelated word.
        /// </summary>
        public static bool ContainsWholeTerm(string normalizedText, string term)
        {
            if (string.IsNullOrEmpty(normalizedText))
            {
                return false;
            }

            string normalizedTerm = NormalizeForMatching(term);
            if (normalizedTerm.Length == 0)
            {
                return false;
            }

            string paddedText = " " + normalizedText + " ";
            string paddedTerm = " " + normalizedTerm + " ";
            return paddedText.IndexOf(paddedTerm, StringComparison.Ordinal) >= 0;
        }

        /// <summary>Collapses line breaks/tabs/whitespace to a single-line, single-spaced value.</summary>
        public static string CleanOneLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder result = new StringBuilder(value.Length);
            bool pendingSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = result.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    result.Append(' ');
                    pendingSpace = false;
                }

                result.Append(c);
            }

            return result.ToString().Trim();
        }

        /// <summary>Caps UTF-16 text without leaving a dangling high surrogate at the end.</summary>
        public static string CapUtf16(string value, int maxChars)
        {
            string cleaned = value ?? string.Empty;
            if (maxChars <= 0 || cleaned.Length <= maxChars)
            {
                return cleaned;
            }

            int end = maxChars;
            if (end > 0 && end < cleaned.Length && char.IsHighSurrogate(cleaned[end - 1]))
            {
                end--;
            }

            return cleaned.Substring(0, end).TrimEnd();
        }

        /// <summary>Counts user-perceived Unicode text elements rather than UTF-16 code units.</summary>
        public static int TextElementCount(string value)
        {
            return string.IsNullOrEmpty(value) ? 0 : new StringInfo(value).LengthInTextElements;
        }

        /// <summary>Returns normalized non-space text elements for character n-gram comparison.</summary>
        public static List<string> NormalizedTextElements(string value)
        {
            string normalized = NormalizeForMatching(value).Replace(" ", string.Empty);
            List<string> elements = new List<string>();
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(normalized);
            while (enumerator.MoveNext())
            {
                elements.Add(enumerator.GetTextElement());
            }

            return elements;
        }

        private static bool IsWordCategory(UnicodeCategory category)
        {
            return category == UnicodeCategory.UppercaseLetter
                || category == UnicodeCategory.LowercaseLetter
                || category == UnicodeCategory.TitlecaseLetter
                || category == UnicodeCategory.ModifierLetter
                || category == UnicodeCategory.OtherLetter
                || category == UnicodeCategory.DecimalDigitNumber
                || category == UnicodeCategory.LetterNumber
                || category == UnicodeCategory.OtherNumber
                || category == UnicodeCategory.NonSpacingMark
                || category == UnicodeCategory.SpacingCombiningMark
                || category == UnicodeCategory.EnclosingMark;
        }
    }
}

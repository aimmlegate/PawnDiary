// Pure one-line text cleaner: collapses newlines and strips RimWorld rich-text tags so a label,
// thought, or generated line can be embedded into a compact prompt string or context field. This is
// the one helper from the old DiaryContextBuilder that is genuinely free of RimWorld/Verse state —
// it only touches System.String — so it lives in its own file where a pure test project can link it
// without pulling in the game assemblies. Split out of DiaryContextBuilder.cs (Run Card 6). See
// DOCUMENTATION.md. New to C#/RimWorld? See AGENTS.md.
using System;
using System.Text.RegularExpressions;

namespace PawnDiary
{
    /// <summary>
    /// Stateless text cleaning for diary context strings. Takes raw label/thought text that may
    /// contain rich-text tags and line breaks and returns a single trimmed line. Has no dependency
    /// on RimWorld/Verse/Unity, so it is safe to unit-test in a plain console harness.
    /// </summary>
    public static class DiaryLineCleaner
    {
        // Matches any RimWorld rich-text tag fragment such as <b>, </b>, <size=20>, <color=#FF00FF>.
        // Non-greedy so it removes one tag at a time rather than swallowing text between two tags.
        private static readonly Regex RichTextTagRegex = new Regex("<.*?>");

        /// <summary>
        /// Returns the input as a single trimmed line with rich-text tags removed and newlines
        /// turned into spaces. Whitespace-only or null input returns an empty string so callers can
        /// append the result unconditionally.
        /// </summary>
        public static string CleanLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string cleaned = value.Replace("\r", " ").Replace("\n", " ");
            cleaned = RichTextTagRegex.Replace(cleaned, string.Empty);
            return cleaned.Trim();
        }
    }
}

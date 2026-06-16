// Turns one line of generated diary text into Unity rich text for the diary tab.
// LLMs emit light markdown (*italic*, **bold**, "# " headings, "- " bullets) and quoted speech; the
// vanilla label renderer would print those markers literally. This helper rewrites the common cases
// into rich-text tags (<b>, <i>, <color>) so the page reads cleanly. New to C#/RimWorld? Unity's
// IMGUI labels understand a small HTML-like markup when the GUIStyle has richText enabled (see
// ITab_Pawn_Diary.BodyStyle). Kept deliberately conservative so ordinary prose is never mangled.
using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PawnDiary
{
    /// <summary>
    /// Stateless formatter that rewrites one already-trimmed diary line (plain text + light markdown)
    /// into the Unity rich-text string drawn by <see cref="ITab_Pawn_Diary"/>. Both the height-measure
    /// pass and the draw pass call <see cref="ToRichText"/> with the same input, so wrapped heights
    /// stay in sync: text color does not affect glyph width, and the bold spans that do affect it are
    /// applied identically in both passes.
    /// </summary>
    public static class DiaryTextFormat
    {
        // Typographic characters built from code points so the source stays pure ASCII (the .cs files
        // carry no BOM, so a raw "smart quote" byte would depend on the compiler's codepage guess).
        // 0x201C/0x201D are the left/right double quotation marks; 0x2022 is the bullet.
        private static readonly string LeftQuote = ((char)0x201C).ToString();
        private static readonly string RightQuote = ((char)0x201D).ToString();
        private static readonly string Bullet = ((char)0x2022).ToString();

        // **bold** and *italic*. Each marker must hug non-space text, so arithmetic like "a * b" or a
        // lone stray asterisk is left alone instead of being read as emphasis.
        private static readonly Regex BoldPattern = new Regex(@"\*\*(?=\S)(.+?)(?<=\S)\*\*");
        private static readonly Regex ItalicPattern = new Regex(@"\*(?=\S)(.+?)(?<=\S)\*");
        // Leading line markers: "# ".."###### " headings and "- "/"* " bullets.
        private static readonly Regex HeadingPattern = new Regex(@"^#{1,6}\s+");
        private static readonly Regex BulletPattern = new Regex(@"^[-*]\s+");
        // Double-quoted speech, smart first then straight. Single quotes are left alone so apostrophes
        // are never swallowed. (The quote characters are not regex metacharacters, so embedding them
        // directly is safe.)
        private static readonly Regex SmartQuotePattern = new Regex(LeftQuote + "([^" + RightQuote + "]+)" + RightQuote);
        private static readonly Regex StraightQuotePattern = new Regex("\"([^\"]+)\"");

        /// <summary>
        /// Rewrites one already-trimmed line into rich text: light markdown becomes &lt;b&gt;/&lt;i&gt;
        /// tags and quoted speech is wrapped in a bold, colored span using <paramref name="quoteColor"/>.
        /// Returns the line unchanged when there is nothing to format.
        /// </summary>
        public static string ToRichText(string line, Color quoteColor)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line ?? string.Empty;
            }

            string s = line;

            // Line-level markers first, so inline emphasis runs on the visible remainder only.
            bool heading = false;
            Match h = HeadingPattern.Match(s);
            if (h.Success)
            {
                s = s.Substring(h.Length);
                heading = true;
            }
            else if (s.StartsWith("> ", StringComparison.Ordinal))
            {
                // Block quote: drop the marker, keep the words.
                s = s.Substring(2);
            }
            else
            {
                Match b = BulletPattern.Match(s);
                if (b.Success)
                {
                    s = Bullet + " " + s.Substring(b.Length);
                }
            }

            // Inline emphasis: bold before italic so "**x**" is not split by the italic rule.
            s = BoldPattern.Replace(s, "<b>$1</b>");
            s = ItalicPattern.Replace(s, "<i>$1</i>");

            if (heading)
            {
                s = "<b>" + s + "</b>";
            }

            // Color quoted speech inline (straight quotes normalized to typographic quotes) so the
            // spoken words stand out without lighting up the surrounding narration. Smart quotes are
            // handled first; the straight pass then cannot re-wrap the smart quotes this inserts.
            string open = "<color=#" + ColorUtility.ToHtmlStringRGB(quoteColor) + "><b>" + LeftQuote;
            string close = RightQuote + "</b></color>";
            s = SmartQuotePattern.Replace(s, open + "$1" + close);
            s = StraightQuotePattern.Replace(s, open + "$1" + close);

            return s;
        }
    }
}

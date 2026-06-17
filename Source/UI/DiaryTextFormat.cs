// Turns one line of generated diary text into Unity rich text for the diary tab.
// LLMs emit light markdown (*italic*, **bold**, "# " headings, "- " bullets) and quoted speech; the
// vanilla label renderer would print those markers literally. This helper rewrites the common cases
// into rich-text tags (<b>, <i>, <color>) so the page reads cleanly. New to C#/RimWorld? Unity's
// IMGUI labels understand a small HTML-like markup when the GUIStyle has richText enabled (see
// ITab_Pawn_Diary.BodyStyle). Kept deliberately conservative so ordinary prose is never mangled.
using System;
using System.Text;
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
        private static readonly char[] LightZalgoMarks =
        {
            (char)0x0307, // dot above
            (char)0x0301, // acute accent
            (char)0x0300, // grave accent
            (char)0x0302, // circumflex
            (char)0x0303, // tilde
            (char)0x0323, // dot below
            (char)0x0324, // diaeresis below
            (char)0x0331, // macron below
            (char)0x0315, // comma above right
            (char)0x0336  // long stroke overlay
        };

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
            return ToRichText(line, quoteColor, false, 0);
        }

        /// <summary>
        /// Rewrites one already-trimmed line into rich text, optionally adding a dramatic
        /// deterministic distortion to quoted direct speech for strange-chat anomaly pages.
        /// </summary>
        public static string ToRichText(string line, Color quoteColor, bool distortQuotedSpeech, int seed)
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
            int quoteIndex = 0;
            s = SmartQuotePattern.Replace(s, match =>
            {
                string quote = FormatQuoteText(match.Groups[1].Value, distortQuotedSpeech, seed, quoteIndex++);
                return open + quote + close;
            });
            s = StraightQuotePattern.Replace(s, match =>
            {
                string quote = FormatQuoteText(match.Groups[1].Value, distortQuotedSpeech, seed, quoteIndex++);
                return open + quote + close;
            });

            return s;
        }

        private static string FormatQuoteText(string text, bool distort, int seed, int quoteIndex)
        {
            return distort ? ApplyLightZalgo(text, seed ^ (quoteIndex * 1103515245)) : text;
        }

        private static string ApplyLightZalgo(string text, int seed)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            StringBuilder result = new StringBuilder(text.Length + text.Length / 2);
            int visibleIndex = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                result.Append(c);
                if (char.IsLetter(c))
                {
                    int hash = PositiveHash(MixHash(seed, visibleIndex, c));
                    if (hash % 3 != 1)
                    {
                        result.Append(LightZalgoMarks[hash % LightZalgoMarks.Length]);
                        if ((hash / 11) % 4 == 0)
                        {
                            result.Append(LightZalgoMarks[(hash / 97) % LightZalgoMarks.Length]);
                        }
                    }

                    visibleIndex++;
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Adds deterministic variable-size words for staggered low-Consciousness/intoxication entries.
        /// The input is already rich text, so this walks past existing tags and only wraps visible
        /// words. The seed makes the result random-looking but stable across the measure and draw pass.
        /// </summary>
        public static string ApplyStaggeredWordSizes(string rich, int intensity, int seed, int baseFontSize)
        {
            intensity = intensity < 0 ? 0 : (intensity > 4 ? 4 : intensity);
            if (string.IsNullOrEmpty(rich) || intensity <= 0)
            {
                return rich ?? string.Empty;
            }

            if (baseFontSize <= 0)
            {
                baseFontSize = 13;
            }

            int selectionModulo = intensity >= 4 ? 3 : (intensity == 3 ? 4 : (intensity == 2 ? 6 : 9));
            int maxDelta = Math.Max(1, intensity + 1);
            StringBuilder result = new StringBuilder(rich.Length + 32);
            int wordIndex = 0;

            for (int i = 0; i < rich.Length;)
            {
                char c = rich[i];
                if (c == '<')
                {
                    int tagEnd = rich.IndexOf('>', i);
                    if (tagEnd >= 0)
                    {
                        result.Append(rich, i, tagEnd - i + 1);
                        i = tagEnd + 1;
                        continue;
                    }
                }

                if (char.IsLetterOrDigit(c))
                {
                    int start = i;
                    i++;
                    while (i < rich.Length && char.IsLetterOrDigit(rich[i]))
                    {
                        i++;
                    }

                    int length = i - start;
                    string word = rich.Substring(start, length);
                    int hash = PositiveHash(MixHash(seed, wordIndex, length));
                    if (length > 2 && hash % selectionModulo == 0)
                    {
                        int magnitude = 1 + (PositiveHash(hash / 17) % maxDelta);
                        int direction = (hash & 2) == 0 ? -1 : 1;
                        int size = Math.Max(8, baseFontSize + direction * magnitude);
                        result.Append("<size=");
                        result.Append(size);
                        result.Append(">");
                        result.Append(word);
                        result.Append("</size>");
                    }
                    else
                    {
                        result.Append(word);
                    }

                    wordIndex++;
                    continue;
                }

                result.Append(c);
                i++;
            }

            return result.ToString();
        }

        private static int MixHash(int seed, int wordIndex, int length)
        {
            unchecked
            {
                int hash = seed;
                hash = (hash * 397) ^ wordIndex;
                hash = (hash * 397) ^ length;
                hash ^= hash >> 13;
                hash *= 1274126177;
                return hash;
            }
        }

        private static int PositiveHash(int value)
        {
            return value & 0x7fffffff;
        }
    }
}

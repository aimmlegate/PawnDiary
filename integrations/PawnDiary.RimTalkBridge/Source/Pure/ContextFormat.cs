// Pure text formatting for the bridge. NO RimWorld / RimTalk / Verse usings live here: this file is
// file-linked into the pure test project (tests/RimTalkBridgeLogicTests) and must compile without
// the game. All localized label/format strings are passed in ALREADY TRANSLATED by the caller —
// pure code never calls .Translate() (see AGENTS.md and DOCUMENTATION.md §12).
//
// New to C#/RimWorld? See AGENTS.md. Analogy for JS/TS: this is a plain module of string helpers
// that take primitives in and return a string out — no I/O, no globals, trivially testable.
using System.Collections.Generic;
using System.Text;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>One recent diary memory reduced to the three strings the RimTalk section needs.</summary>
    public sealed class DiaryMemoryLine
    {
        /// <summary>Entry title (or group label fallback), already resolved by the caller.</summary>
        public string Title;

        /// <summary>One-line prose summary of the entry.</summary>
        public string Summary;

        /// <summary>Human-readable in-game date string.</summary>
        public string Date;
    }

    /// <summary>
    /// Builds the plain-text blocks the bridge hands to RimTalk and to Pawn Diary. Everything here is
    /// deterministic and side-effect free.
    /// </summary>
    public static class ContextFormat
    {
        /// <summary>
        /// Builds the "recent diary memories" section RimTalk sees for one pawn. Returns "" when there
        /// is nothing worth injecting (no usable memories, or the budget cannot fit even one line).
        /// </summary>
        /// <param name="entries">Recent memories, newest first. Null/blank rows are skipped.</param>
        /// <param name="header">Translated section header, e.g. "recent diary memories:".</param>
        /// <param name="memoryLineFormat">Translated line template with {0}=title, {1}=date, {2}=summary.</param>
        /// <param name="voiceLineFormat">Translated voice-line template with {0}=style rule.</param>
        /// <param name="styleRule">The pawn's diary writing-voice rule, or null/blank for none.</param>
        /// <param name="includeStyle">When true and styleRule is present, append the voice line.</param>
        /// <param name="maxChars">Hard cap on the whole section. Whole lines are dropped, never split.</param>
        public static string BuildDiarySection(
            List<DiaryMemoryLine> entries,
            string header,
            string memoryLineFormat,
            string voiceLineFormat,
            string styleRule,
            bool includeStyle,
            int maxChars)
        {
            List<string> memoryLines = new List<string>();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    DiaryMemoryLine entry = entries[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    string title = Clean(entry.Title);
                    string summary = Clean(entry.Summary);
                    // Title or summary is enough to be worth a line; a bare date is not.
                    if (title.Length == 0 && summary.Length == 0)
                    {
                        continue;
                    }

                    string date = Clean(entry.Date);
                    memoryLines.Add(SafeFormat(memoryLineFormat, title, date, summary));
                }
            }

            if (memoryLines.Count == 0)
            {
                // No memories means no section at all — never emit a lone header or a lone voice line.
                return string.Empty;
            }

            string cleanHeader = Clean(header);
            StringBuilder builder = new StringBuilder();
            builder.Append(cleanHeader);
            int runningLength = cleanHeader.Length;
            int kept = 0;

            for (int i = 0; i < memoryLines.Count; i++)
            {
                string line = memoryLines[i];
                int addedLength = 1 + line.Length; // newline + the line itself
                if (runningLength + addedLength > maxChars)
                {
                    break;
                }

                builder.Append('\n').Append(line);
                runningLength += addedLength;
                kept++;
            }

            if (kept == 0)
            {
                // Even one memory line does not fit under the budget: skip the section entirely.
                return string.Empty;
            }

            if (includeStyle)
            {
                string rule = Clean(styleRule);
                if (rule.Length > 0)
                {
                    string voiceLine = SafeFormat(voiceLineFormat, rule);
                    int addedLength = 1 + voiceLine.Length;
                    if (runningLength + addedLength <= maxChars)
                    {
                        builder.Append('\n').Append(voiceLine);
                    }
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Returns the first sentence of <paramref name="text"/>, cleaned to one line and capped to
        /// <paramref name="maxChars"/> at a word boundary. Used to reduce a multi-sentence RimTalk
        /// persona to a single compact identity line. Returns "" for null/blank input.
        /// </summary>
        public static string FirstSentenceCap(string text, int maxChars)
        {
            string cleaned = Clean(text);
            if (cleaned.Length == 0)
            {
                return string.Empty;
            }

            int end = -1;
            for (int i = 0; i < cleaned.Length; i++)
            {
                char c = cleaned[i];
                if (c == '.' || c == '!' || c == '?')
                {
                    end = i + 1; // include the terminator
                    break;
                }
            }

            string sentence = end > 0 ? cleaned.Substring(0, end) : cleaned;
            return CapAtWord(sentence.Trim(), maxChars);
        }

        /// <summary>
        /// Caps <paramref name="text"/> to <paramref name="maxChars"/>, cutting at the last whitespace
        /// so words are never split. No ellipsis is added (callers want a clean fragment). Returns the
        /// input unchanged when it already fits or when maxChars is non-positive.
        /// </summary>
        public static string CapAtWord(string text, int maxChars)
        {
            if (text == null)
            {
                return string.Empty;
            }

            if (maxChars <= 0 || text.Length <= maxChars)
            {
                return text;
            }

            int cut = maxChars;
            for (int i = maxChars; i > 0; i--)
            {
                if (char.IsWhiteSpace(text[i - 1]))
                {
                    cut = i - 1;
                    break;
                }
            }

            // No whitespace found in range: fall back to a hard cut so we still respect the budget.
            if (cut <= 0)
            {
                cut = maxChars;
            }

            return text.Substring(0, cut).TrimEnd();
        }

        /// <summary>
        /// Collapses whitespace/newlines to single spaces and trims. Keeps section lines single-line
        /// so the newline layout above stays exact.
        /// </summary>
        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            bool lastWasSpace = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r' || c == '\n' || c == '\t')
                {
                    c = ' ';
                }

                if (c == ' ')
                {
                    if (lastWasSpace)
                    {
                        continue;
                    }

                    lastWasSpace = true;
                }
                else
                {
                    lastWasSpace = false;
                }

                builder.Append(c);
            }

            return builder.ToString().Trim();
        }

        /// <summary>
        /// string.Format that never throws on a malformed template: LLM/prose values could in theory
        /// contain stray braces, and a broken template must degrade to something readable rather than
        /// crash RimTalk's prompt build. Falls back to joining the args with spaces.
        /// </summary>
        private static string SafeFormat(string format, params object[] args)
        {
            if (string.IsNullOrEmpty(format))
            {
                return string.Join(" ", ToStrings(args));
            }

            try
            {
                return string.Format(format, args);
            }
            catch (System.FormatException)
            {
                return string.Join(" ", ToStrings(args));
            }
        }

        private static string[] ToStrings(object[] args)
        {
            if (args == null)
            {
                return new string[0];
            }

            string[] result = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                result[i] = args[i] == null ? string.Empty : args[i].ToString();
            }

            return result;
        }
    }
}

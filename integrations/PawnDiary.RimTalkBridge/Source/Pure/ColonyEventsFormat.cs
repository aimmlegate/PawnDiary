// Pure text formatting for the colony-situation block ({{colony_events}}). NO RimWorld / RimTalk /
// Verse usings live here: this file is file-linked into the pure test project
// (tests/RimTalkBridgeLogicTests) and must compile without the game. The caller collects the raw
// situation lines from live map state (already translated) on the main thread and hands them here as
// plain strings + weights; this file only orders, caps, and joins them.
//
// New to C#/RimWorld? See AGENTS.md. Analogy for JS/TS: a plain module that takes an array of
// {text, weight} objects and returns one string — no I/O, no globals, trivially testable.
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>One line of the colony situation plus a weight used to order and trim the block.</summary>
    public sealed class ColonyEventLine
    {
        /// <summary>Already-translated one-line description, e.g. "the colony is under attack".</summary>
        public string Text;

        /// <summary>Higher = more situationally important; highest-weight lines are kept first.</summary>
        public int Weight;
    }

    /// <summary>
    /// Builds the compact "colony situation" block RimTalk sees as the {{colony_events}} environment
    /// variable. Deterministic and side-effect free.
    /// </summary>
    public static class ColonyEventsFormat
    {
        /// <summary>
        /// Builds "&lt;header&gt;\n- &lt;line&gt;\n- &lt;line&gt;", highest-weight first, keeping at most
        /// <paramref name="maxLines"/> whole lines and never exceeding <paramref name="maxChars"/>.
        /// Returns "" when there is nothing worth injecting (no usable lines, maxLines &lt;= 0, or the
        /// budget cannot fit even one line). Whole lines are dropped, never split.
        /// </summary>
        /// <param name="lines">Situation lines with weights. Null/blank rows are skipped.</param>
        /// <param name="header">Translated block header, e.g. "colony situation:".</param>
        /// <param name="maxLines">Cap on how many lines to keep (the colonyEventCount setting).</param>
        /// <param name="maxChars">Hard cap on the whole block. Whole lines are dropped, never split.</param>
        public static string BuildColonySituation(
            List<ColonyEventLine> lines,
            string header,
            int maxLines,
            int maxChars)
        {
            if (lines == null || maxLines <= 0)
            {
                return string.Empty;
            }

            // Clean, drop blanks, and order by weight (descending). OrderByDescending is a stable sort,
            // so equal-weight lines keep the caller's insertion order (deterministic under test).
            List<string> ordered = lines
                .Where(l => l != null)
                .Select(l => new { Text = ContextFormat.Clean(l.Text), l.Weight })
                .Where(l => l.Text.Length > 0)
                .OrderByDescending(l => l.Weight)
                .Select(l => "- " + l.Text)
                .ToList();

            if (ordered.Count == 0)
            {
                return string.Empty;
            }

            string cleanHeader = ContextFormat.Clean(header);
            StringBuilder builder = new StringBuilder();
            builder.Append(cleanHeader);
            int runningLength = cleanHeader.Length;
            int kept = 0;

            for (int i = 0; i < ordered.Count && kept < maxLines; i++)
            {
                string line = ordered[i];
                int addedLength = 1 + line.Length; // newline + the line itself
                if (runningLength + addedLength > maxChars)
                {
                    break;
                }

                builder.Append('\n').Append(line);
                runningLength += addedLength;
                kept++;
            }

            // A lone header with no lines under budget is not worth injecting.
            return kept == 0 ? string.Empty : builder.ToString();
        }
    }
}

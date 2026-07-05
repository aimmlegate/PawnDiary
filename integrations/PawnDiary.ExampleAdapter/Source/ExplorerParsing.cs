// PURE helpers for the API Explorer window. No RimWorld/Verse/Unity types appear in any signature —
// only strings, ints, bools, IList<string> — so this file is unit-tested by the
// tests/ExampleAdapterParsingTests console project with no game assemblies referenced.
//
// What lives here: the small piece of the explorer's request-building UI that is genuinely pure —
// turning the text the developer types into multiline text fields into the structured inputs the
// PawnDiaryApi DTOs want. Everything else in the explorer is impure IMGUI glue over stable DTOs
// (DTO field labels stay English by design, see AGENTS.md §12 schema carve-out).
//
// New to C#? See AGENTS.md.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiaryExampleAdapter
{
    /// <summary>
    /// Stateless text helpers for the explorer's request/query builders. Every method is pure: same
    /// inputs in, same outputs out, no field or static state, no exceptions thrown into the caller.
    /// </summary>
    internal static class ExplorerParsing
    {
        /// <summary>
        /// Maximum extra-context / enchantment-candidate lines the explorer will accept from one
        /// multiline paste. Generous on purpose (PawnDiaryApi caps and cleans afterwards), but still
        /// bounded so a runaway paste cannot stall the UI.
        /// </summary>
        public const int MaxMultilineLines = 64;

        /// <summary>
        /// Splits one text-area blob into trimmed, non-blank lines. Blank lines and lines whose first
        /// non-whitespace character is '#' (a developer comment) are dropped. The result is capped at
        /// <see cref="MaxMultilineLines"/> entries. A null or whitespace-only input returns an empty
        /// list (never null) — the caller can pass it straight to a DTO's List&lt;string&gt; field,
        /// and PawnDiaryApi treats an empty list the same as a null one.
        /// </summary>
        public static List<string> LinesFromMultiline(string text)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            // Split on any of the three common line endings so a paste from Windows/notepad/VSCode
            // all behave the same. StringSplitOptions.None keeps the engine simple; blanks are
            // filtered below.
            string[] raw = text.Split(NewLineChars, StringSplitOptions.None);
            for (int i = 0; i < raw.Length; i++)
            {
                if (result.Count >= MaxMultilineLines)
                {
                    break;
                }

                string line = raw[i];
                if (line == null)
                {
                    continue;
                }

                string trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                // Treat '#' as a developer comment so a tester can paste notes into the same box.
                if (trimmed[0] == '#')
                {
                    continue;
                }

                result.Add(trimmed);
            }

            return result;
        }

        /// <summary>
        /// Joins a list back into a single text-area blob, one line per entry. Used when restoring
        /// the form from a saved request or rehydrating the example default. Null/empty input → "".
        /// </summary>
        public static string MultilineFromLines(IList<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }

                string line = lines[i];
                if (line != null)
                {
                    sb.Append(line);
                }
            }

            return sb.ToString();
        }

        private static readonly string[] NewLineChars = { "\r\n", "\n", "\r" };

        /// <summary>
        /// Lightweight validity check for an eventKey the developer typed. This mirrors the
        /// PawnDiaryApi contract (lowercase, prefix-with-mod, snake_case) well enough to color the
        /// form field red before submit; PawnDiaryApi still does the authoritative cleaning. A valid
        /// key is non-empty, has no whitespace, and contains at least one underscore (the mod-prefix
        /// convention).
        /// </summary>
        public static bool LooksLikeEventKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            string trimmed = key.Trim();
            if (trimmed.Length == 0 || trimmed.Length > 128)
            {
                return false;
            }

            bool sawUnderscore = false;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (char.IsWhiteSpace(c))
                {
                    return false;
                }

                if (c == '_')
                {
                    sawUnderscore = true;
                }
            }

            return sawUnderscore;
        }

        /// <summary>
        /// Normalizes a POV role string. Empty/whitespace → null (let PawnDiaryApi pick its default).
        /// Otherwise returns the trimmed value. The API accepts "initiator"/"recipient"/"neutral" and
        /// any unknown value is treated as the planner default — the explorer does not enforce the
        /// exact vocabulary so a tester can probe behavior.
        /// </summary>
        public static string NormalizePovRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return null;
            }

            return role.Trim();
        }

        /// <summary>
        /// Parses a free-form tick field. Returns the parsed value when it is a non-negative integer,
        /// otherwise <c>negativeDefault</c> (the "unbounded" sentinel PawnDiaryApi uses for tick
        /// bounds). Empty/whitespace → <c>negativeDefault</c>.
        /// </summary>
        public static int ParseTick(string text, int negativeDefault)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return negativeDefault;
            }

            string trimmed = text.Trim();
            if (int.TryParse(trimmed, out int value) && value >= 0)
            {
                return value;
            }

            return negativeDefault;
        }

        /// <summary>
        /// Parses a non-negative integer count (e.g. the "max entries" field). Returns
        /// <paramref name="fallback"/> when the text is blank or not a positive integer.
        /// </summary>
        public static int ParsePositiveInt(string text, int fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            string trimmed = text.Trim();
            if (int.TryParse(trimmed, out int value) && value > 0)
            {
                return value;
            }

            return fallback;
        }

        /// <summary>
        /// Converts a UI tri-state selector index into the integer PawnDiaryApi's DiaryEntryTitleQuery
        /// expects: index 0 → -1 (any), 1 → 0 (no/false), 2 → 1 (yes/true). Anything out of range
        /// collapses to "any" (-1).
        /// </summary>
        public static int TriStateFromIndex(int index)
        {
            switch (index)
            {
                case 1: return 0;
                case 2: return 1;
                default: return -1;
            }
        }

        /// <summary>
        /// Inverse of <see cref="TriStateFromIndex"/>: restores the UI selector index from a stored
        /// tri-state value. Used when rehydrating the query form.
        /// </summary>
        public static int IndexFromTriState(int value)
        {
            switch (value)
            {
                case 0: return 1;
                case 1: return 2;
                default: return 0;
            }
        }
    }
}

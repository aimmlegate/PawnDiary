// ImportantMemoryLineRenderer.cs — pure rendering of one important-memory record into one
// localized canonical fact line (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §3.2). The template comes
// from the record's DiaryImportantEventDef (DefInjected-localized), so re-rendering after a
// language switch produces current-language lines; the capture-time fallbackSummary only covers a
// removed Def. A player/editor-authored manualTextOverride deliberately wins for future recall.
//
// New to C#/RimWorld? Placeholders are simple "{token}" substitutions: "{other}" is the first
// participant's saved name, "{<factKey>}" is that fact row's display value. Unresolved
// placeholders are stripped so a missing fact can never leak "{part_label}" into an LLM prompt.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary
{
    /// <summary>Renders record fact lines from localized templates with saved-value fallbacks.</summary>
    internal static class ImportantMemoryLineRenderer
    {
        /// <summary>
        /// Uses a nonblank player/editor-authored text override first, otherwise renders the record
        /// through <paramref name="template"/>; a blank template (or a render that collapses to
        /// nothing) falls back to the record's capture-time summary. The result is trimmed to
        /// <paramref name="maxChars"/> whole characters (defensive bound, §2.2).
        /// </summary>
        public static string Render(ImportantMemoryRecordSnapshot record, string template, int maxChars)
        {
            if (record == null)
            {
                return string.Empty;
            }

            string rendered = CleanManualOverride(record.manualTextOverride, maxChars);
            if (string.IsNullOrWhiteSpace(rendered))
            {
                rendered = string.IsNullOrWhiteSpace(template)
                    ? string.Empty
                    : Substitute(record, template);
            }

            if (string.IsNullOrWhiteSpace(rendered))
            {
                rendered = record.fallbackSummary ?? string.Empty;
            }

            rendered = rendered.Trim();
            int bound = Math.Max(0, maxChars);
            if (bound > 0 && rendered.Length > bound)
            {
                rendered = TextTruncation.SafePrefix(rendered, bound).TrimEnd();
            }

            return rendered;
        }

        /// <summary>
        /// Turns editor-authored memory prose into one prompt-safe line and applies the same
        /// XML-owned character bound as captured fallback summaries. A non-positive bound means
        /// "unbounded," matching <see cref="Render"/>.
        /// </summary>
        public static string CleanManualOverride(string value, int maxChars)
        {
            string line = PromptTextSanitizer.OneLine(value);
            if (maxChars > 0 && line.Length > maxChars)
            {
                return TextTruncation.SafePrefix(line, maxChars).TrimEnd();
            }

            return line;
        }

        /// <summary>
        /// Frames one already-sanitized player background as factual canon using the localized
        /// XML/DefInjected format. Malformed or blank formats fail safely to the authored text, and
        /// ordinary <see cref="string.Format(string, object)"/> preserves its case and non-ASCII data.
        /// </summary>
        public static string FormatBackground(string fact, string format)
        {
            string usableFact = (fact ?? string.Empty).Trim();
            if (usableFact.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                string framed = string.Format(
                    string.IsNullOrWhiteSpace(format) ? "{0}" : format,
                    usableFact);
                // A translator can accidentally remove or escape the placeholder without causing
                // string.Format to throw. Treat that as malformed too: prompt framing may decorate
                // player canon, but it must never replace or silently discard the authored fact.
                return string.IsNullOrWhiteSpace(framed)
                        || framed.IndexOf(usableFact, StringComparison.Ordinal) < 0
                    ? usableFact
                    : framed.Trim();
            }
            catch (FormatException)
            {
                return usableFact;
            }
        }

        /// <summary>
        /// Joins already-rendered "relevant past" lines into the prompt block, enforcing the line
        /// cap and the character budget by DROPPING whole lines from the end — a fact line is
        /// never truncated mid-text (§3.2).
        /// </summary>
        public static string ComposeBlock(List<string> lines, int maxLines, int maxChars)
        {
            if (lines == null || lines.Count == 0)
            {
                return string.Empty;
            }

            List<string> usable = new List<string>();
            for (int i = 0; i < lines.Count && usable.Count < Math.Max(0, maxLines); i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    usable.Add(lines[i].Trim());
                }
            }

            int budget = Math.Max(0, maxChars);
            string joined = string.Join("\n", usable.ToArray());
            while (budget > 0 && joined.Length > budget && usable.Count > 0)
            {
                usable.RemoveAt(usable.Count - 1);
                joined = string.Join("\n", usable.ToArray());
            }

            return joined;
        }

        private static string Substitute(ImportantMemoryRecordSnapshot record, string template)
        {
            StringBuilder builder = new StringBuilder(template.Length + 32);
            int index = 0;
            while (index < template.Length)
            {
                char current = template[index];
                if (current != '{')
                {
                    builder.Append(current);
                    index++;
                    continue;
                }

                int close = template.IndexOf('}', index + 1);
                if (close < 0)
                {
                    // Unbalanced brace: keep the rest literally rather than guessing.
                    builder.Append(template, index, template.Length - index);
                    break;
                }

                string token = template.Substring(index + 1, close - index - 1).Trim();
                builder.Append(ResolveToken(record, token));
                index = close + 1;
            }

            // Placeholder stripping can leave doubled spaces ("lost  suddenly"); collapse them so
            // the prompt line stays clean.
            return CollapseSpaces(builder.ToString());
        }

        private static string ResolveToken(ImportantMemoryRecordSnapshot record, string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            if (string.Equals("{" + token + "}", KnowledgeTokens.PlaceholderOther,
                StringComparison.OrdinalIgnoreCase))
            {
                return record.participants != null && record.participants.Count > 0
                    ? (record.participants[0].name ?? string.Empty)
                    : string.Empty;
            }

            List<KnowledgeFact> facts = record.facts;
            if (facts != null)
            {
                for (int i = 0; i < facts.Count; i++)
                {
                    KnowledgeFact fact = facts[i];
                    if (fact != null
                        && string.Equals(fact.key, token, StringComparison.OrdinalIgnoreCase))
                    {
                        return fact.value ?? string.Empty;
                    }
                }
            }

            return string.Empty;
        }

        private static string CollapseSpaces(string text)
        {
            StringBuilder builder = new StringBuilder(text.Length);
            bool lastWasSpace = false;
            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                bool isSpace = current == ' ';
                if (isSpace && lastWasSpace)
                {
                    continue;
                }

                builder.Append(current);
                lastWasSpace = isSpace;
            }

            return builder.ToString().Trim();
        }
    }
}

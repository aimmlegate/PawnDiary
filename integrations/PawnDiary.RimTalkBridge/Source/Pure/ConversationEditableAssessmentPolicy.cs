// Pure helpers for the two player-editable parts of the RimTalk conversation funnel: the
// comma-separated reaction-term lexicon and the semantic assessor's system prompt. The settings
// window owns text buffers and localization; this file owns deterministic validation, normalization,
// category preservation, and prompt cleanup without referencing RimWorld, Verse, or Defs.
//
// Existing XML terms keep their original semantic categories when a player retains them. Newly
// added terms share one bounded custom category, so adding many spelling variants cannot inflate a
// candidate's score without limit.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Stable validation result for one comma-separated reaction-term edit.</summary>
    public sealed class ConversationReactionTermsValidationResult
    {
        public bool IsValid;
        public string Error = string.Empty;
        public int ErrorValue;
        public string NormalizedCsv = string.Empty;
        public List<string> Terms = new List<string>();
    }

    /// <summary>Validates edits and maps them back onto the category-bounded scoring lexicon.</summary>
    public static class ConversationReactionTermsEditor
    {
        public const string ErrorEmpty = "empty";
        public const string ErrorNewline = "newline";
        public const string ErrorTooMany = "too_many";
        public const string ErrorTooLong = "too_long";
        public const string ErrorInvalidTerm = "invalid_term";

        /// <summary>
        /// Validates a comma-separated edit, trims entries, removes case-insensitive Unicode
        /// duplicates, and returns a canonical comma+space representation suitable for saving.
        /// </summary>
        public static ConversationReactionTermsValidationResult Validate(
            string input,
            int maxTerms,
            int maxTermChars)
        {
            ConversationReactionTermsValidationResult result =
                new ConversationReactionTermsValidationResult();
            if (string.IsNullOrWhiteSpace(input))
            {
                result.Error = ErrorEmpty;
                return result;
            }

            if (input.IndexOf('\r') >= 0 || input.IndexOf('\n') >= 0)
            {
                result.Error = ErrorNewline;
                return result;
            }

            int termLimit = Math.Max(1, maxTerms);
            int charLimit = Math.Max(1, maxTermChars);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            string[] pieces = input.Split(new[] { ',' }, StringSplitOptions.None);
            for (int i = 0; i < pieces.Length; i++)
            {
                string term = UnicodeText.CleanOneLine(pieces[i]);
                // Empty pieces are harmless while typing a trailing comma and do not become terms.
                if (term.Length == 0)
                {
                    continue;
                }

                string normalized = UnicodeText.NormalizeForMatching(term);
                if (normalized.Length == 0)
                {
                    result.Error = ErrorInvalidTerm;
                    result.ErrorValue = i + 1;
                    return result;
                }

                if (UnicodeText.TextElementCount(term) > charLimit)
                {
                    result.Error = ErrorTooLong;
                    result.ErrorValue = charLimit;
                    return result;
                }

                if (!seen.Add(normalized))
                {
                    continue;
                }

                result.Terms.Add(term);
                if (result.Terms.Count > termLimit)
                {
                    result.Error = ErrorTooMany;
                    result.ErrorValue = termLimit;
                    return result;
                }
            }

            if (result.Terms.Count == 0)
            {
                result.Error = ErrorEmpty;
                return result;
            }

            result.IsValid = true;
            result.NormalizedCsv = string.Join(", ", result.Terms.ToArray());
            return result;
        }

        /// <summary>Flattens every category into a stable, de-duplicated list for the text editor.</summary>
        public static List<string> Flatten(ConversationKeywordLexicon lexicon)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (lexicon == null)
            {
                return result;
            }

            AppendUnique(result, seen, lexicon.DisclosureTerms);
            AppendUnique(result, seen, lexicon.CommitmentTerms);
            AppendUnique(result, seen, lexicon.ConflictTerms);
            AppendUnique(result, seen, lexicon.ReconciliationTerms);
            AppendUnique(result, seen, lexicon.CustomTerms);
            return result;
        }

        /// <summary>Returns a canonical CSV representation of a lexicon for the settings editor.</summary>
        public static string ToCsv(ConversationKeywordLexicon lexicon)
        {
            return string.Join(", ", Flatten(lexicon).ToArray());
        }

        /// <summary>
        /// Applies an edited flat list to the XML categories. Retained XML terms keep their category;
        /// terms not present in XML are collected into one custom category.
        /// </summary>
        public static ConversationKeywordLexicon ApplyOverride(
            ConversationKeywordLexicon defaults,
            IList<string> selectedTerms)
        {
            ConversationKeywordLexicon safeDefaults = defaults ?? new ConversationKeywordLexicon();
            HashSet<string> selected = NormalizedSet(selectedTerms);
            HashSet<string> knownDefaults = new HashSet<string>(StringComparer.Ordinal);
            AddNormalized(knownDefaults, safeDefaults.DisclosureTerms);
            AddNormalized(knownDefaults, safeDefaults.CommitmentTerms);
            AddNormalized(knownDefaults, safeDefaults.ConflictTerms);
            AddNormalized(knownDefaults, safeDefaults.ReconciliationTerms);

            ConversationKeywordLexicon result = new ConversationKeywordLexicon
            {
                DisclosureTerms = FilterSelected(safeDefaults.DisclosureTerms, selected),
                CommitmentTerms = FilterSelected(safeDefaults.CommitmentTerms, selected),
                ConflictTerms = FilterSelected(safeDefaults.ConflictTerms, selected),
                ReconciliationTerms = FilterSelected(safeDefaults.ReconciliationTerms, selected),
                CustomTerms = new List<string>()
            };

            if (selectedTerms != null)
            {
                HashSet<string> customSeen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < selectedTerms.Count; i++)
                {
                    string term = UnicodeText.CleanOneLine(selectedTerms[i]);
                    string normalized = UnicodeText.NormalizeForMatching(term);
                    if (normalized.Length > 0
                        && !knownDefaults.Contains(normalized)
                        && customSeen.Add(normalized))
                    {
                        result.CustomTerms.Add(term);
                    }
                }
            }

            return result;
        }

        /// <summary>Compares two term lists as case-insensitive Unicode-normalized sets.</summary>
        public static bool SameTerms(IList<string> left, IList<string> right)
        {
            return NormalizedSet(left).SetEquals(NormalizedSet(right));
        }

        private static void AppendUnique(List<string> result, HashSet<string> seen, IList<string> terms)
        {
            if (terms == null)
            {
                return;
            }

            for (int i = 0; i < terms.Count; i++)
            {
                string term = UnicodeText.CleanOneLine(terms[i]);
                string normalized = UnicodeText.NormalizeForMatching(term);
                if (normalized.Length > 0 && seen.Add(normalized))
                {
                    result.Add(term);
                }
            }
        }

        private static HashSet<string> NormalizedSet(IList<string> terms)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            AddNormalized(result, terms);
            return result;
        }

        private static void AddNormalized(HashSet<string> result, IList<string> terms)
        {
            if (terms == null)
            {
                return;
            }

            for (int i = 0; i < terms.Count; i++)
            {
                string normalized = UnicodeText.NormalizeForMatching(terms[i]);
                if (normalized.Length > 0)
                {
                    result.Add(normalized);
                }
            }
        }

        private static List<string> FilterSelected(IList<string> terms, HashSet<string> selected)
        {
            List<string> result = new List<string>();
            if (terms == null)
            {
                return result;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < terms.Count; i++)
            {
                string term = UnicodeText.CleanOneLine(terms[i]);
                string normalized = UnicodeText.NormalizeForMatching(term);
                if (selected.Contains(normalized) && seen.Add(normalized))
                {
                    result.Add(term);
                }
            }

            return result;
        }
    }

    /// <summary>Resolves and sanitizes the optional player-authored semantic assessment prompt.</summary>
    public static class ConversationAssessmentPromptEditor
    {
        /// <summary>Blank override means XML default; otherwise the cleaned override replaces it.</summary>
        public static string Resolve(string defaultPrompt, string overridePrompt, int maxChars)
        {
            int limit = Math.Max(1, maxChars);
            string cleanedOverride = Clean(overridePrompt, limit);
            return cleanedOverride.Length > 0 ? cleanedOverride : Clean(defaultPrompt, limit);
        }

        /// <summary>Normalizes line endings, strips unsafe controls, trims, and caps without surrogate splits.</summary>
        public static string Clean(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
            StringBuilder result = new StringBuilder(normalized.Length);
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (c == '\n')
                {
                    result.Append(c);
                }
                else if (c == '\t')
                {
                    result.Append(' ');
                }
                else if (!char.IsControl(c))
                {
                    result.Append(c);
                }
            }

            return UnicodeText.CapUtf16(result.ToString().Trim(), Math.Max(1, maxChars));
        }
    }
}

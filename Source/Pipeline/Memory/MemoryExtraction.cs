// Pure extraction for the pawn memory subsystem (design/MEMORY_SYSTEM_DESIGN.md §7.3). ONE
// function turns an event's frozen strings into a fragment's tags, keywords, importance, and
// excerpt text — and the recall seam reuses the SAME function to build the query's tags and
// keywords. One mechanism everywhere: no second vocabulary, no query language, no LLM, no
// embeddings. Everything here is deterministic string/table work over policy-owned mappings.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers"). This file must stay free of
// Verse/Unity/settings/Def references so the pure test project can link it directly.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Extraction input (design §7.3). Every field is an already-frozen event string copied by the
    /// impure caller from the just-constructed DiaryEvent — no Pawn or Def access happens here.
    /// </summary>
    internal sealed class MemoryExtractionInput
    {
        public string povName = string.Empty;         // writer's short name (EXCLUDED from keywords — see Extract)
        public string otherName = string.Empty;       // other participant's short name (may be empty)
        public string interactionLabel = string.Empty;
        public string colorCue = string.Empty;
        public string moodImpact = string.Empty;      // "positive" / "negative" / "neutral" / ""
        public bool importantGroup;                   // DiaryInteractionGroupDef.important
        public bool solo;                             // solo events never gain the social tag
        public string gameContext = string.Empty;     // DiaryEvent.gameContext key=value blob
        public string rawText = string.Empty;         // POV raw event text (already localized)
    }

    /// <summary>Extraction output: the four things a fragment (or a recall query) is made of.</summary>
    internal sealed class MemoryExtractionResult
    {
        public List<string> tags = new List<string>();       // deduped, closed vocabulary
        public List<string> keywords = new List<string>();   // <= policy.maxKeywordsPerFragment
        public float importance;                             // clamped to [0.05, 1.0]; 0 for null input
        public string fragmentText = string.Empty;           // excerpt, <= policy.fragmentTextMaxChars
    }

    /// <summary>The one tag/keyword/importance extraction mechanism shared by deposit and recall.</summary>
    internal static class MemoryExtraction
    {
        // Saved gameContext fields use these words to mean "nothing here" (e.g. royal_title=none).
        // They are never meaningful association keys and never trigger presence markers.
        private static readonly string[] SentinelValues = { "none", "n/a", "unknown" };

        // Small embedded English stopword list (~60 words) — enough to keep a shared person or
        // place name signal-clean without NLP. Keywords are schema tokens, not player-facing
        // prose, so the list intentionally stays English across locales (DOCUMENTATION.md §12).
        private static readonly string[] Stopwords =
        {
            "the", "and", "that", "this", "with", "from", "for", "was", "were", "are",
            "has", "had", "have", "not", "but", "all", "can", "will", "would", "there",
            "their", "what", "about", "which", "when", "where", "who", "whom", "how", "why",
            "its", "his", "her", "him", "she", "you", "your", "our", "out", "they", "them",
            "then", "than", "into", "over", "under", "after", "before", "between", "because",
            "while", "during", "against", "through", "off", "too", "very", "just", "now",
            "also", "got", "get"
        };

        /// <summary>
        /// Builds tags, keywords, importance, and the capped excerpt for one event POV. Null input
        /// yields an empty result (importance 0) so a defensive caller can never crash capture.
        /// </summary>
        public static MemoryExtractionResult Extract(MemoryExtractionInput input, MemoryPolicySnapshot policy)
        {
            MemoryExtractionResult result = new MemoryExtractionResult();
            if (input == null)
            {
                return result;
            }

            MemoryPolicySnapshot safePolicy = policy ?? MemoryPolicySnapshot.CreateDefault();
            result.tags = ExtractTags(input, safePolicy);
            result.keywords = ExtractKeywords(input, safePolicy);
            result.importance = ExtractImportance(input, safePolicy);
            result.fragmentText = DiarySentenceExcerpt.FirstSentence(
                input.rawText, Math.Max(0, safePolicy.fragmentTextMaxChars));
            return result;
        }

        /// <summary>True when a saved gameContext value means "absent" rather than naming something.</summary>
        public static bool IsSentinelValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            string trimmed = value.Trim();
            for (int i = 0; i < SentinelValues.Length; i++)
            {
                if (string.Equals(trimmed, SentinelValues[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> ExtractTags(MemoryExtractionInput input, MemoryPolicySnapshot policy)
        {
            List<string> tags = new List<string>();

            // 1. colorCue -> tags (policy table).
            AddKnownTags(tags, CueTagsFor(policy.cueTags, input.colorCue));

            // 2. Mood direction -> joy/sorrow.
            if (string.Equals((input.moodImpact ?? string.Empty).Trim(), "negative", StringComparison.OrdinalIgnoreCase))
            {
                AddKnownTag(tags, MemoryTagTokens.Sorrow);
            }
            else if (string.Equals((input.moodImpact ?? string.Empty).Trim(), "positive", StringComparison.OrdinalIgnoreCase))
            {
                AddKnownTag(tags, MemoryTagTokens.Joy);
            }

            // 3. gameContext markers -> tags (policy table, parsed with the shared field reader).
            if (policy.contextMarkerTags != null)
            {
                for (int i = 0; i < policy.contextMarkerTags.Count; i++)
                {
                    MemoryContextMarkerTags row = policy.contextMarkerTags[i];
                    if (row != null && MarkerMatches(input.gameContext, row.marker))
                    {
                        AddKnownTags(tags, row.tags);
                    }
                }
            }

            // 4. Every pairwise (non-solo) event is a social memory.
            if (!input.solo)
            {
                AddKnownTag(tags, MemoryTagTokens.Social);
            }

            return tags;
        }

        private static List<string> ExtractKeywords(MemoryExtractionInput input, MemoryPolicySnapshot policy)
        {
            int cap = Math.Max(0, policy.maxKeywordsPerFragment);
            List<string> keywords = new List<string>();
            if (cap == 0)
            {
                return keywords;
            }

            // The writer's own name would appear on every one of their fragments and match
            // everything, so it is excluded from the keyword stream entirely.
            List<string> povTokens = Tokenize(input.povName);

            // Priority order (design §7.3): the other participant, whitelisted context values,
            // the interaction label, then the first raw-text tokens for any remaining slots.
            AddKeywordTokens(keywords, Tokenize(input.otherName), povTokens, cap);
            if (policy.contextKeywordKeys != null)
            {
                for (int i = 0; i < policy.contextKeywordKeys.Count && keywords.Count < cap; i++)
                {
                    string key = policy.contextKeywordKeys[i];
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    string value = DiaryContextFields.Value(input.gameContext, key.Trim());
                    if (IsSentinelValue(value))
                    {
                        continue;
                    }

                    AddKeywordTokens(keywords, Tokenize(value), povTokens, cap);
                }
            }

            AddKeywordTokens(keywords, Tokenize(input.interactionLabel), povTokens, cap);
            AddKeywordTokens(keywords, Tokenize(input.rawText), povTokens, cap);
            return keywords;
        }

        private static float ExtractImportance(MemoryExtractionInput input, MemoryPolicySnapshot policy)
        {
            float importance = policy.fallbackCueImportance;
            if (policy.cueImportance != null)
            {
                for (int i = 0; i < policy.cueImportance.Count; i++)
                {
                    MemoryCueImportance row = policy.cueImportance[i];
                    if (row != null && string.Equals(row.cue, (input.colorCue ?? string.Empty).Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        importance = row.importance;
                        break;
                    }
                }
            }

            if (input.importantGroup)
            {
                importance += Math.Max(0f, policy.importantGroupBonus);
            }

            // Negativity bias is deliberate: bad memories stick more than good ones (design §7.3).
            string mood = (input.moodImpact ?? string.Empty).Trim();
            if (string.Equals(mood, "negative", StringComparison.OrdinalIgnoreCase))
            {
                importance += Math.Max(0f, policy.negativeMoodBonus);
            }
            else if (string.Equals(mood, "positive", StringComparison.OrdinalIgnoreCase))
            {
                importance += Math.Max(0f, policy.positiveMoodBonus);
            }

            return Clamp(importance, 0.05f, 1f);
        }

        /// <summary>
        /// Marker matching on the saved key=value blob. "key=" means "field present with a
        /// meaningful (non-sentinel) value"; "key=value" means an exact value match; a bare "key"
        /// is treated as a presence check. Malformed/blank markers never match.
        /// </summary>
        private static bool MarkerMatches(string gameContext, string marker)
        {
            if (string.IsNullOrWhiteSpace(gameContext) || string.IsNullOrWhiteSpace(marker))
            {
                return false;
            }

            string trimmed = marker.Trim();
            int equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex == trimmed.Length - 1)
            {
                string key = trimmed.Substring(0, equalsIndex);
                return !IsSentinelValue(DiaryContextFields.Value(gameContext, key));
            }

            if (equalsIndex > 0)
            {
                return DiaryContextFields.FieldEquals(
                    gameContext,
                    trimmed.Substring(0, equalsIndex),
                    trimmed.Substring(equalsIndex + 1));
            }

            return !IsSentinelValue(DiaryContextFields.Value(gameContext, trimmed));
        }

        private static List<string> CueTagsFor(List<MemoryCueTags> rows, string colorCue)
        {
            if (rows != null)
            {
                string cue = (colorCue ?? string.Empty).Trim();
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] != null && string.Equals(rows[i].cue, cue, StringComparison.OrdinalIgnoreCase))
                    {
                        return rows[i].tags;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Normalizes free text into keyword tokens: lowercase-invariant, alphanumerics only,
        /// length >= 3, stopwords dropped, first occurrence order preserved.
        /// </summary>
        private static List<string> Tokenize(string text)
        {
            List<string> tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return tokens;
            }

            string lowered = text.ToLowerInvariant();
            int start = -1;
            for (int i = 0; i <= lowered.Length; i++)
            {
                bool isTokenChar = i < lowered.Length && char.IsLetterOrDigit(lowered[i]);
                if (isTokenChar && start < 0)
                {
                    start = i;
                }
                else if (!isTokenChar && start >= 0)
                {
                    AddToken(tokens, lowered.Substring(start, i - start));
                    start = -1;
                }
            }

            return tokens;
        }

        private static void AddToken(List<string> tokens, string token)
        {
            if (token.Length < 3 || IsStopword(token) || ContainsOrdinal(tokens, token))
            {
                return;
            }

            tokens.Add(token);
        }

        private static void AddKeywordTokens(List<string> keywords, List<string> tokens,
            List<string> povTokens, int cap)
        {
            for (int i = 0; i < tokens.Count && keywords.Count < cap; i++)
            {
                string token = tokens[i];
                if (ContainsOrdinal(povTokens, token) || ContainsOrdinal(keywords, token))
                {
                    continue;
                }

                keywords.Add(token);
            }
        }

        private static void AddKnownTags(List<string> target, List<string> tags)
        {
            if (tags == null)
            {
                return;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                AddKnownTag(target, tags[i]);
            }
        }

        private static void AddKnownTag(List<string> target, string tag)
        {
            // The vocabulary is closed: unknown tags (typos, or tokens from a newer mod version)
            // are dropped here instead of being deposited and then never matching anything.
            if (string.IsNullOrWhiteSpace(tag) || !MemoryTagTokens.IsKnown(tag.Trim())
                || ContainsOrdinal(target, tag.Trim()))
            {
                return;
            }

            target.Add(tag.Trim());
        }

        private static bool IsStopword(string token)
        {
            for (int i = 0; i < Stopwords.Length; i++)
            {
                if (token == Stopwords[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsOrdinal(List<string> values, string target)
        {
            if (values == null || target == null)
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], target, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static float Clamp(float value, float min, float max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}

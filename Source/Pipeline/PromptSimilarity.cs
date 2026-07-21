// Pure prompt-similarity measurement for the anti-repetition guard.
//
// The generation queue builds a trial prompt and asks here whether it looks too much like the
// pawn's recent prompts. The metric is deliberately simple and language-neutral: a word-token
// Jaccard score (shared tokens / union of tokens) over lowercased letter/digit runs. It needs no
// RimWorld types, no localization, no RNG, and no IO, so the guard's decision is unit-testable
// without the game (see tests/DiaryPipelineTests).
//
// Interpretation: prompts assembled from the same template, group instruction, tone, and persona
// summary share most of their tokens, so repeated same-group events (the 50th raid) score high.
// The score is only ever compared against an XML-tuned threshold; it is a heuristic gate, never a
// hard block — the guard rerolls prompt "enhancements" a bounded number of times and then gives up.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary
{
    /// <summary>
    /// Word-token Jaccard similarity for prompt text. Pure: no game state, no RNG, no IO.
    /// </summary>
    internal static class PromptSimilarity
    {
        /// <summary>
        /// Returns the Jaccard similarity in [0,1] between the word tokens of two texts. Two empty
        /// (or tokenless) texts are defined as dissimilar (0) so an empty candidate never triggers
        /// the anti-repetition guard.
        /// </summary>
        public static float Similarity(string left, string right)
        {
            HashSet<string> leftTokens = TokenSet(left);
            HashSet<string> rightTokens = TokenSet(right);
            if (leftTokens.Count == 0 || rightTokens.Count == 0)
            {
                return 0f;
            }

            int intersection = 0;
            foreach (string token in leftTokens)
            {
                if (rightTokens.Contains(token))
                {
                    intersection++;
                }
            }

            int union = leftTokens.Count + rightTokens.Count - intersection;
            return union <= 0 ? 0f : (float)intersection / union;
        }

        /// <summary>
        /// Returns the strongest similarity between one candidate prompt and any recent prompt.
        /// </summary>
        public static float StrongestSimilarity(string candidate, IList<string> recentPrompts)
        {
            float strongest = 0f;
            if (recentPrompts == null)
            {
                return 0f;
            }

            for (int i = 0; i < recentPrompts.Count; i++)
            {
                float score = Similarity(candidate, recentPrompts[i]);
                if (score > strongest)
                {
                    strongest = score;
                }
            }

            return strongest;
        }

        /// <summary>
        /// Returns true when the candidate's strongest similarity to the recent prompts reaches
        /// <paramref name="threshold"/>. The strongest score is reported via
        /// <paramref name="strongest"/> so callers can log how close the decision was. An invalid
        /// threshold (NaN / outside 0..1) fails open: the guard stays off rather than rerolling
        /// every prompt forever.
        /// </summary>
        public static bool TooSimilar(string candidate, IList<string> recentPrompts,
            float threshold, out float strongest)
        {
            strongest = StrongestSimilarity(candidate, recentPrompts);
            if (float.IsNaN(threshold) || threshold < 0f || threshold > 1f)
            {
                return false;
            }

            return strongest >= threshold;
        }

        // Lowercased runs of letters/digits; everything else (punctuation, the "label: value"
        // schema colons, newlines) is a separator. Ordinal lowercase keeps this culture-invariant
        // and thread-agnostic.
        private static HashSet<string> TokenSet(string text)
        {
            HashSet<string> tokens = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(text))
            {
                return tokens;
            }

            StringBuilder current = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c))
                {
                    current.Append(char.ToLowerInvariant(c));
                    continue;
                }

                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Length = 0;
                }
            }

            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
            }

            return tokens;
        }
    }
}

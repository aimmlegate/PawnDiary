// Pure, language-neutral transcript/event overlap heuristic. It is intentionally only a negative
// signal: literal repetition can suppress an echo, while low overlap never proves that a paraphrase
// is unrelated. The semantic assessor still receives the recent events for the final judgment.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Generic;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Compares two texts with token-set Jaccard and an optional character-trigram fallback.</summary>
    public static class ConversationTextOverlap
    {
        /// <summary>Returns a similarity in [0,1], or zero when either side has no usable text.</summary>
        public static float Similarity(string left, string right, int minimumTokenChars, bool useTrigramFallback)
        {
            HashSet<string> leftTokens = UnicodeText.TokenSet(left, minimumTokenChars);
            HashSet<string> rightTokens = UnicodeText.TokenSet(right, minimumTokenChars);
            float tokenScore = Jaccard(leftTokens, rightTokens);

            if (!useTrigramFallback)
            {
                return tokenScore;
            }

            HashSet<string> leftTrigrams = Trigrams(left);
            HashSet<string> rightTrigrams = Trigrams(right);
            float trigramScore = Jaccard(leftTrigrams, rightTrigrams);
            return tokenScore > trigramScore ? tokenScore : trigramScore;
        }

        /// <summary>Returns the strongest overlap between one transcript and any supplied event text.</summary>
        public static float StrongestSimilarity(
            string transcript,
            IList<string> eventTexts,
            int minimumTokenChars,
            bool useTrigramFallback)
        {
            if (eventTexts == null || eventTexts.Count == 0)
            {
                return 0f;
            }

            float strongest = 0f;
            for (int i = 0; i < eventTexts.Count; i++)
            {
                float score = Similarity(transcript, eventTexts[i], minimumTokenChars, useTrigramFallback);
                if (score > strongest)
                {
                    strongest = score;
                }
            }

            return strongest;
        }

        private static HashSet<string> Trigrams(string value)
        {
            List<string> elements = UnicodeText.NormalizedTextElements(value);
            HashSet<string> grams = new HashSet<string>(StringComparer.Ordinal);
            if (elements.Count < 3)
            {
                return grams;
            }

            for (int i = 0; i <= elements.Count - 3; i++)
            {
                grams.Add(elements[i] + elements[i + 1] + elements[i + 2]);
            }

            return grams;
        }

        private static float Jaccard(HashSet<string> left, HashSet<string> right)
        {
            if (left == null || right == null || left.Count == 0 || right.Count == 0)
            {
                return 0f;
            }

            int intersection = 0;
            foreach (string item in left)
            {
                if (right.Contains(item))
                {
                    intersection++;
                }
            }

            int union = left.Count + right.Count - intersection;
            return union <= 0 ? 0f : (float)intersection / union;
        }
    }
}

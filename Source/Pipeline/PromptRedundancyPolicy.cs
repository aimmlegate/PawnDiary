// Pure exact-duplicate detection for prompt context fields (PROMPT_FOOTPRINT plan §9.1).
//
// The diary keeps both "my last opening line" and "how my previous entry ended" as continuity
// context. For one-sentence entries both snapshots are often the SAME sentence, and sending it
// twice costs tokens while teaching small models that repetition is normal. This helper answers
// exactly one question: are two context strings the same text once trivial whitespace/case noise
// is ignored? Nothing fuzzy — no substring, stemming, edit-distance, or semantic matching — so a
// genuinely different ending can never be silently dropped.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers"): this file is pure on purpose (no
// Verse/RimWorld/Unity, no settings, no IO) so the standalone pipeline tests pin its behavior.
using System;

namespace PawnDiary
{
    /// <summary>Normalized exact-equality checks used when projecting per-request prompt values.</summary>
    internal static class PromptRedundancyPolicy
    {
        /// <summary>
        /// True when both strings carry the same non-empty text after collapsing CR/LF and
        /// whitespace runs to single spaces, trimming, and comparing case-insensitively
        /// (ordinal). Punctuation is preserved, so "It held." and "It held!" stay different.
        /// Two blank strings are NOT a meaningful duplicate and return false.
        /// </summary>
        public static bool SameNormalizedText(string left, string right)
        {
            // PromptTextSanitizer.OneLine already does the exact normalization this policy needs
            // (null -> empty, control chars -> space, whitespace runs -> one space, trim).
            string normalizedLeft = PromptTextSanitizer.OneLine(left);
            string normalizedRight = PromptTextSanitizer.OneLine(right);
            if (normalizedLeft.Length == 0 || normalizedRight.Length == 0)
            {
                return false;
            }

            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
    }
}

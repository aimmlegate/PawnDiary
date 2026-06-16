// One home for the "which way did this affect the pawn's mood?" direction shared by mood-event and
// thought diary entries. These three tokens are load-bearing values, not just display text: they
// are saved on DiaryEvent.moodImpact and embedded in the gameContext "mood_impact=" field, and they
// pick which localized sentence describes the event. Before this existed the literal strings and the
// ±0.5 classification threshold were copy-pasted across DiaryGameComponent and DiaryContextBuilder,
// which is easy to typo and hard to grep. Centralizing them keeps the values consistent everywhere.
// New to C#/RimWorld? See AGENTS.md.
using System;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// The three mood-impact direction tokens plus helpers to classify a numeric mood offset and to
    /// pick the matching localized text. The string values are persisted and re-parsed, so they must
    /// stay exactly "positive" / "negative" / "neutral".
    /// </summary>
    public static class MoodImpact
    {
        /// <summary>The event lifted the pawn's mood.</summary>
        public const string Positive = "positive";
        /// <summary>The event hurt the pawn's mood.</summary>
        public const string Negative = "negative";
        /// <summary>No clear mood direction (also the safe default for old saves).</summary>
        public const string Neutral = "neutral";

        /// <summary>
        /// A mood offset must exceed ±this magnitude to count as positive/negative; anything closer
        /// to zero is treated as neutral. Shared so every classifier uses the same cut-off.
        /// </summary>
        public const float MeaningfulThreshold = 0.5f;

        /// <summary>
        /// Classifies a numeric mood offset into a direction token. Offsets within ±<see cref="MeaningfulThreshold"/> are neutral.
        /// </summary>
        public static string Classify(float moodOffset)
        {
            if (moodOffset > MeaningfulThreshold)
            {
                return Positive;
            }

            if (moodOffset < -MeaningfulThreshold)
            {
                return Negative;
            }

            return Neutral;
        }

        /// <summary>
        /// Resolves the localized event text for a mood direction: chooses the positive/negative/neutral
        /// translation key and applies the same arguments to whichever key wins. Mirrors the old inline
        /// "if positive … else if negative … else …" blocks, so callers shrink to one line.
        /// </summary>
        public static string PickText(string moodImpact, string positiveKey, string negativeKey, string neutralKey, params NamedArgument[] args)
        {
            string key;
            if (string.Equals(moodImpact, Positive, StringComparison.OrdinalIgnoreCase))
            {
                key = positiveKey;
            }
            else if (string.Equals(moodImpact, Negative, StringComparison.OrdinalIgnoreCase))
            {
                key = negativeKey;
            }
            else
            {
                key = neutralKey;
            }

            return key.Translate(args).Resolve();
        }
    }
}

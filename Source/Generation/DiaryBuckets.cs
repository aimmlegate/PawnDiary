// Compact "bucket" formatting for the LLM context: turns a raw mood percent, pain level, opinion
// score, age, beauty, bleed rate, or thought strength into one short localized token (e.g. "happy",
// "severe", "devoted"). The band thresholds come from DiaryTuningDef (XML-tunable); the labels are
// localized via ".Translate()", so this file is intentionally free of live Pawn/Map reads but is
// NOT free of Verse — it produces player-language strings. The truly pure text cleaner
// (DiaryLineCleaner) is kept in its own file so it can be unit-tested without the game.
// Split out of DiaryContextBuilder.cs (Run Card 6). See repowiki/README.md.
// New to C#/RimWorld? See AGENTS.md.
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Stateless formatters that classify a numeric value into a localized band token for prompt
    /// context. Each method takes a primitive and returns a string; none read live game state. The
    /// thresholds are read from <see cref="DiaryTuningDef"/> via <see cref="DiaryTuning.Current"/>.
    /// </summary>
    internal static class DiaryBuckets
    {
        /// <summary>
        /// Formats an opinion score (-100..100) as a localized relation-band token. Kept as a
        /// thin alias over <see cref="OpinionBucket"/> for readable call sites.
        /// </summary>
        public static string FormatOpinion(int opinion)
        {
            return OpinionBucket(opinion);
        }

        /// <summary>
        /// Classifies a map beauty value into beautiful / pleasant / neutral / ugly / hideous.
        /// </summary>
        public static string BeautyBucket(float beauty)
        {
            DiaryTuningDef t = DiaryTuning.Current;
            if (beauty >= t.beautyBeautiful)
            {
                return "PawnDiary.Bucket.Beauty.Beautiful".Translate();
            }

            if (beauty >= t.beautyPleasant)
            {
                return "PawnDiary.Bucket.Beauty.Pleasant".Translate();
            }

            if (beauty > -t.beautyPleasant)
            {
                return "PawnDiary.Bucket.Beauty.Neutral".Translate();
            }

            if (beauty > t.beautyUgly)
            {
                return "PawnDiary.Bucket.Beauty.Ugly".Translate();
            }

            return "PawnDiary.Bucket.Beauty.Hideous".Translate();
        }

        /// <summary>
        /// Classifies a mood percentage (0..100) into happy / stable / stressed / miserable.
        /// </summary>
        public static string MoodBucket(int moodPercent)
        {
            DiaryTuningDef t = DiaryTuning.Current;
            if (moodPercent >= t.moodHappy)
            {
                return "PawnDiary.Bucket.Mood.Happy".Translate();
            }

            if (moodPercent >= t.moodStable)
            {
                return "PawnDiary.Bucket.Mood.Stable".Translate();
            }

            if (moodPercent >= t.moodStressed)
            {
                return "PawnDiary.Bucket.Mood.Stressed".Translate();
            }

            return "PawnDiary.Bucket.Mood.Miserable".Translate();
        }

        /// <summary>
        /// Classifies a total pain fraction (0..1+) into minor / moderate / severe.
        /// </summary>
        public static string PainBucket(float pain)
        {
            DiaryTuningDef t = DiaryTuning.Current;
            if (pain >= t.painSevere)
            {
                return "PawnDiary.Bucket.Pain.Severe".Translate();
            }

            if (pain >= t.painModerate)
            {
                return "PawnDiary.Bucket.Pain.Moderate".Translate();
            }

            return "PawnDiary.Bucket.Pain.Minor".Translate();
        }

        /// <summary>
        /// Classifies a bleed rate into minor / moderate / severe. Thresholds are stable clinical
        /// bands rather than XML tuning, so they stay as code constants.
        /// </summary>
        public static string BleedingBucket(float bleedRate)
        {
            if (bleedRate >= 2f)
            {
                return "PawnDiary.Bucket.Bleeding.Severe".Translate();
            }

            if (bleedRate >= 1f)
            {
                return "PawnDiary.Bucket.Bleeding.Moderate".Translate();
            }

            return "PawnDiary.Bucket.Bleeding.Minor".Translate();
        }

        /// <summary>
        /// Classifies an opinion score (-100..100) into devoted / friendly / neutral / strained /
        /// hostile.
        /// </summary>
        public static string OpinionBucket(int opinion)
        {
            DiaryTuningDef t = DiaryTuning.Current;
            if (opinion >= t.opinionDevoted)
            {
                return "PawnDiary.Bucket.Opinion.Devoted".Translate();
            }

            if (opinion >= t.opinionFriendly)
            {
                return "PawnDiary.Bucket.Opinion.Friendly".Translate();
            }

            if (opinion > t.opinionNeutralAbove)
            {
                return "PawnDiary.Bucket.Opinion.Neutral".Translate();
            }

            if (opinion > t.opinionStrainedAbove)
            {
                return "PawnDiary.Bucket.Opinion.Strained".Translate();
            }

            return "PawnDiary.Bucket.Opinion.Hostile".Translate();
        }

        /// <summary>
        /// Classifies a biological age in years into child / teen / adult / older adult / elder.
        /// Takes the age as a plain int so the band logic is independent of the live Pawn read; the
        /// caller snapshots <c>Pawn_AgeTracker.AgeBiologicalYears</c> and guards a null tracker.
        /// </summary>
        public static string AgeBucket(int years)
        {
            if (years < 13)
            {
                return "PawnDiary.Bucket.Age.Child".Translate();
            }

            if (years < 20)
            {
                return "PawnDiary.Bucket.Age.Teen".Translate();
            }

            if (years < 45)
            {
                return "PawnDiary.Bucket.Age.Adult".Translate();
            }

            if (years < 65)
            {
                return "PawnDiary.Bucket.Age.OlderAdult".Translate();
            }

            return "PawnDiary.Bucket.Age.Elder".Translate();
        }

        /// <summary>
        /// Classifies a mood/thought offset into strong-positive / positive / strong-negative /
        /// negative for the "(effect)" annotation next to a thought in the prompt.
        /// </summary>
        public static string EffectBucket(float effect)
        {
            if (effect >= 8f)
            {
                return "PawnDiary.Bucket.Effect.StrongPositive".Translate();
            }

            if (effect > 0f)
            {
                return "PawnDiary.Bucket.Effect.Positive".Translate();
            }

            if (effect <= -8f)
            {
                return "PawnDiary.Bucket.Effect.StrongNegative".Translate();
            }

            return "PawnDiary.Bucket.Effect.Negative".Translate();
        }
    }
}

// XML-facing tuning and localized prose for H7 delayed social reflections. The Def is read only on
// RimWorld's main thread and projected immediately into plain policy data, preserving the boundary
// between live game state and deterministic/testable decisions.
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace PawnDiary
{
    /// <summary>Mutable XML row describing one absolute-opinion probability band.</summary>
    public sealed class DiarySocialReflectionChanceBandDef
    {
        public int minimumAbsoluteOpinion;
        public int maximumAbsoluteOpinion;
        public float chance;
    }

    /// <summary>Mutable XML row describing prompt tone and reader color for one polarity.</summary>
    public sealed class DiarySocialReflectionStyleDef
    {
        public string polarity = string.Empty;
        public string tone = string.Empty;
        public string colorCue = string.Empty;
    }

    /// <summary>Singleton XML policy for delayed initiator-side reflections about another pawn.</summary>
    public sealed class DiarySocialReflectionDef : Def
    {
        public bool enabled = true;
        public int pairCooldownTicks = 900000;
        public int writerCooldownTicks = 180000;
        public int minimumDelayTicks = 15000;
        public int maximumDelayTicks = 60000;
        public int retryIntervalTicks = 2500;
        public int proseGraceTicks = 60000;
        public int maximumPendingRows = 128;
        public int maximumPairCooldownRows = 512;
        public int maximumWriterCooldownRows = 512;
        public int maximumHandledSourceRows = 2048;
        public int maximumProcessedPerTick = 4;
        public int maximumSourceLookupRows = 256;
        public int maximumSourceLabelCharacters = 120;
        public int sourceExcerptSentenceCount = 2;
        public int sourceExcerptMaximumCharacters = 400;
        public int maximumOpinionReasons = 3;
        public int maximumMemoryLines = 2;
        public float veryUnattractiveMaximum = -1.5f;
        public float unattractiveMaximum = -0.5f;
        public float attractiveMinimum = 0.5f;
        public float veryAttractiveMinimum = 1.5f;
        public int negativeOpinionMaximum = -1;
        public int positiveOpinionMinimum = 1;
        public string veryUnattractiveLabel = string.Empty;
        public string unattractiveLabel = string.Empty;
        public string ordinaryAppearanceLabel = string.Empty;
        public string attractiveLabel = string.Empty;
        public string veryAttractiveLabel = string.Empty;
        public List<DiarySocialReflectionChanceBandDef> chanceBands =
            new List<DiarySocialReflectionChanceBandDef>();
        public List<DiarySocialReflectionStyleDef> styles =
            new List<DiarySocialReflectionStyleDef>();

        /// <summary>Reports unsafe timing, caps, band coverage, or missing localized policy text.</summary>
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (pairCooldownTicks <= 0 || writerCooldownTicks <= 0)
                yield return "Social-reflection cooldowns must be positive.";
            if (minimumDelayTicks < 0 || maximumDelayTicks < minimumDelayTicks)
                yield return "Social-reflection delay bounds are invalid.";
            if (retryIntervalTicks <= 0 || proseGraceTicks < retryIntervalTicks)
                yield return "Social-reflection retry/grace timing is invalid.";
            if (maximumPendingRows < 1 || maximumPendingRows > 512
                || maximumPairCooldownRows < 1 || maximumPairCooldownRows > 4096
                || maximumWriterCooldownRows < 1 || maximumWriterCooldownRows > 4096
                || maximumHandledSourceRows < 1 || maximumHandledSourceRows > 8192)
                yield return "Social-reflection saved-row caps are outside their safe ranges.";
            if (maximumHandledSourceRows < maximumPendingRows)
                yield return "maximumHandledSourceRows must cover every maximumPendingRows owner.";
            if (maximumProcessedPerTick < 1 || maximumProcessedPerTick > 32
                || maximumSourceLookupRows < 1 || maximumSourceLookupRows > 2048)
                yield return "Social-reflection work caps are outside their safe ranges.";
            if (maximumSourceLabelCharacters < 16 || maximumSourceLabelCharacters > 512
                || sourceExcerptSentenceCount < 1 || sourceExcerptSentenceCount > 8
                || sourceExcerptMaximumCharacters < 40 || sourceExcerptMaximumCharacters > 1200)
                yield return "Social-reflection source-prose caps are outside their safe ranges.";
            if (maximumOpinionReasons < 0 || maximumOpinionReasons > 8
                || maximumMemoryLines < 0 || maximumMemoryLines > 8)
                yield return "Social-reflection context caps are outside their safe ranges.";
            if (!(veryUnattractiveMaximum < unattractiveMaximum
                && unattractiveMaximum < attractiveMinimum
                && attractiveMinimum < veryAttractiveMinimum))
                yield return "Social-reflection beauty thresholds must be strictly increasing.";
            if (negativeOpinionMaximum >= positiveOpinionMinimum)
                yield return "Social-reflection polarity thresholds must leave an ordered neutral band.";
            if (string.IsNullOrWhiteSpace(veryUnattractiveLabel)
                || string.IsNullOrWhiteSpace(unattractiveLabel)
                || string.IsNullOrWhiteSpace(ordinaryAppearanceLabel)
                || string.IsNullOrWhiteSpace(attractiveLabel)
                || string.IsNullOrWhiteSpace(veryAttractiveLabel))
                yield return "Social-reflection beauty labels require localized DefInjected text.";

            foreach (string error in ChanceBandErrors()) yield return error;
            foreach (string error in StyleErrors()) yield return error;
        }

        private IEnumerable<string> ChanceBandErrors()
        {
            if (chanceBands == null || chanceBands.Count == 0)
            {
                yield return "Social-reflection chanceBands must cover absolute opinion 0..100.";
                yield break;
            }

            int expectedMinimum = 0;
            foreach (DiarySocialReflectionChanceBandDef row in chanceBands
                .Where(value => value != null)
                .OrderBy(value => value.minimumAbsoluteOpinion))
            {
                if (row.minimumAbsoluteOpinion != expectedMinimum
                    || row.maximumAbsoluteOpinion < row.minimumAbsoluteOpinion
                    || row.maximumAbsoluteOpinion > 100)
                {
                    yield return "Social-reflection chanceBands must be contiguous and ordered over 0..100.";
                    yield break;
                }

                if (float.IsNaN(row.chance) || float.IsInfinity(row.chance)
                    || row.chance < 0f || row.chance > 1f)
                {
                    yield return "Social-reflection chanceBands require finite probabilities from 0 to 1.";
                }

                expectedMinimum = row.maximumAbsoluteOpinion + 1;
            }

            if (expectedMinimum != 101)
                yield return "Social-reflection chanceBands must end at absolute opinion 100.";
        }

        private IEnumerable<string> StyleErrors()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (styles != null)
            {
                foreach (DiarySocialReflectionStyleDef row in styles)
                {
                    string polarity = Clean(row == null ? null : row.polarity);
                    if (!IsPolarity(polarity) || !seen.Add(polarity))
                        yield return "Social-reflection styles need one distinct known polarity per row.";
                    if (row == null || string.IsNullOrWhiteSpace(row.tone)
                        || string.IsNullOrWhiteSpace(row.colorCue))
                        yield return "Social-reflection styles require localized tone and a color cue.";
                }
            }

            if (!seen.Contains(SocialReflectionPolicy.PositivePolarity)
                || !seen.Contains(SocialReflectionPolicy.NeutralPolarity)
                || !seen.Contains(SocialReflectionPolicy.NegativePolarity))
                yield return "Social-reflection styles must define positive, neutral, and negative rows.";
        }

        private static bool IsPolarity(string value)
        {
            return string.Equals(value, SocialReflectionPolicy.PositivePolarity, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, SocialReflectionPolicy.NeutralPolarity, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, SocialReflectionPolicy.NegativePolarity, StringComparison.OrdinalIgnoreCase);
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    /// <summary>Projects the loaded mutable Def into detached pure H7 policy data.</summary>
    internal static class DiarySocialReflectionPolicy
    {
        internal const string DefName = "Diary_SocialReflection";

        /// <summary>Returns a safe detached snapshot; call only from RimWorld's main thread.</summary>
        public static SocialReflectionPolicySnapshot Snapshot()
        {
            SocialReflectionPolicySnapshot result = SocialReflectionPolicySnapshot.CreateDefault();
            DiarySocialReflectionDef source =
                DefDatabase<DiarySocialReflectionDef>.GetNamedSilentFail(DefName);
            if (source == null)
            {
                return result;
            }

            result.enabled = source.enabled;
            result.pairCooldownTicks = Positive(source.pairCooldownTicks, result.pairCooldownTicks);
            result.writerCooldownTicks = Positive(source.writerCooldownTicks, result.writerCooldownTicks);
            result.minimumDelayTicks = NonNegative(source.minimumDelayTicks, result.minimumDelayTicks);
            result.maximumDelayTicks = Math.Max(
                result.minimumDelayTicks,
                NonNegative(source.maximumDelayTicks, result.maximumDelayTicks));
            result.retryIntervalTicks = Positive(source.retryIntervalTicks, result.retryIntervalTicks);
            result.proseGraceTicks = Positive(source.proseGraceTicks, result.proseGraceTicks);
            result.maximumPendingRows = Between(source.maximumPendingRows, 1, 512, result.maximumPendingRows);
            result.maximumPairCooldownRows = Between(
                source.maximumPairCooldownRows, 1, 4096, result.maximumPairCooldownRows);
            result.maximumWriterCooldownRows = Between(
                source.maximumWriterCooldownRows, 1, 4096, result.maximumWriterCooldownRows);
            result.maximumHandledSourceRows = Math.Max(
                result.maximumPendingRows,
                Between(
                    source.maximumHandledSourceRows,
                    1,
                    8192,
                    result.maximumHandledSourceRows));
            result.maximumProcessedPerTick = Between(
                source.maximumProcessedPerTick, 1, 32, result.maximumProcessedPerTick);
            result.maximumSourceLookupRows = Between(
                source.maximumSourceLookupRows, 1, 2048, result.maximumSourceLookupRows);
            result.maximumSourceLabelCharacters = Between(
                source.maximumSourceLabelCharacters, 16, 512, result.maximumSourceLabelCharacters);
            result.sourceExcerptSentenceCount = Between(
                source.sourceExcerptSentenceCount, 1, 8, result.sourceExcerptSentenceCount);
            result.sourceExcerptMaximumCharacters = Between(
                source.sourceExcerptMaximumCharacters, 40, 1200, result.sourceExcerptMaximumCharacters);
            result.maximumOpinionReasons = Between(
                source.maximumOpinionReasons, 0, 8, result.maximumOpinionReasons);
            result.maximumMemoryLines = Between(
                source.maximumMemoryLines, 0, 8, result.maximumMemoryLines);
            if (source.veryUnattractiveMaximum < source.unattractiveMaximum
                && source.unattractiveMaximum < source.attractiveMinimum
                && source.attractiveMinimum < source.veryAttractiveMinimum)
            {
                result.veryUnattractiveMaximum = source.veryUnattractiveMaximum;
                result.unattractiveMaximum = source.unattractiveMaximum;
                result.attractiveMinimum = source.attractiveMinimum;
                result.veryAttractiveMinimum = source.veryAttractiveMinimum;
            }
            if (source.negativeOpinionMaximum < source.positiveOpinionMinimum)
            {
                result.negativeOpinionMaximum = source.negativeOpinionMaximum;
                result.positiveOpinionMinimum = source.positiveOpinionMinimum;
            }

            result.veryUnattractiveLabel = Clean(source.veryUnattractiveLabel);
            result.unattractiveLabel = Clean(source.unattractiveLabel);
            result.ordinaryAppearanceLabel = Clean(source.ordinaryAppearanceLabel);
            result.attractiveLabel = Clean(source.attractiveLabel);
            result.veryAttractiveLabel = Clean(source.veryAttractiveLabel);

            List<SocialReflectionChanceBand> bands = CopyBands(source.chanceBands);
            if (bands.Count > 0)
            {
                result.chanceBands = bands;
            }

            List<SocialReflectionStyle> styles = CopyStyles(source.styles);
            if (styles.Count == 3)
            {
                result.styles = styles;
            }

            return result;
        }

        private static List<SocialReflectionChanceBand> CopyBands(
            IEnumerable<DiarySocialReflectionChanceBandDef> source)
        {
            List<SocialReflectionChanceBand> result = new List<SocialReflectionChanceBand>();
            if (source == null) return result;
            foreach (DiarySocialReflectionChanceBandDef row in source)
            {
                if (row == null)
                {
                    return new List<SocialReflectionChanceBand>();
                }

                result.Add(new SocialReflectionChanceBand
                {
                    minimumAbsoluteOpinion = row.minimumAbsoluteOpinion,
                    maximumAbsoluteOpinion = row.maximumAbsoluteOpinion,
                    chance = row.chance
                });
            }

            List<SocialReflectionChanceBand> ordered =
                result.OrderBy(row => row.minimumAbsoluteOpinion).ToList();
            return SocialReflectionPolicy.HasCompleteChanceCoverage(ordered)
                ? ordered
                : new List<SocialReflectionChanceBand>();
        }

        private static List<SocialReflectionStyle> CopyStyles(
            IEnumerable<DiarySocialReflectionStyleDef> source)
        {
            List<SocialReflectionStyle> result = new List<SocialReflectionStyle>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (source == null) return result;
            foreach (DiarySocialReflectionStyleDef row in source)
            {
                string polarity = Clean(row == null ? null : row.polarity).ToLowerInvariant();
                bool known = string.Equals(
                        polarity, SocialReflectionPolicy.PositivePolarity, StringComparison.Ordinal)
                    || string.Equals(
                        polarity, SocialReflectionPolicy.NeutralPolarity, StringComparison.Ordinal)
                    || string.Equals(
                        polarity, SocialReflectionPolicy.NegativePolarity, StringComparison.Ordinal);
                if (!known || !seen.Add(polarity)
                    || string.IsNullOrWhiteSpace(row == null ? null : row.tone)
                    || string.IsNullOrWhiteSpace(row == null ? null : row.colorCue))
                {
                    continue;
                }

                result.Add(new SocialReflectionStyle
                {
                    polarity = polarity,
                    tone = Clean(row.tone),
                    colorCue = Clean(row.colorCue)
                });
            }

            return result;
        }

        private static int Positive(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }

        private static int NonNegative(int value, int fallback)
        {
            return value >= 0 ? value : fallback;
        }

        private static int Between(int value, int minimum, int maximum, int fallback)
        {
            return value >= minimum && value <= maximum ? value : fallback;
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}

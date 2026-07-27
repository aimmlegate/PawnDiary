// PURE formatter for the delayed subject snapshot used by social-reflection prompts. The impure
// adapter reads live Pawn state once at emission; this helper turns those values into qualitative,
// frozen text without leaking numeric opinion or beauty scores.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PawnDiary
{
    /// <summary>Plain live facts captured about the social-reflection subject at delayed emission.</summary>
    public sealed class SocialReflectionSubjectFacts
    {
        public string displayName = string.Empty;
        public int? biologicalAgeYears;
        public string lifeStageLabel = string.Empty;
        public float? beauty;
        public string primaryRelationLabel = string.Empty;
        public int opinion;
        public string opinionBandLabel = string.Empty;
        public List<string> socialReasons = new List<string>();
    }

    /// <summary>Sanitized qualitative fields frozen into one H7 diary event.</summary>
    public sealed class SocialReflectionFormattedContext
    {
        public string subjectName = string.Empty;
        public string subjectAge = string.Empty;
        public string subjectLifeStage = string.Empty;
        public string subjectAppearance = string.Empty;
        public string subjectRelation = string.Empty;
        public string subjectSentiment = string.Empty;
        public string subjectReasons = string.Empty;
        public string polarity = string.Empty;
        public string tone = string.Empty;
        public string colorCue = string.Empty;
    }

    /// <summary>Maps delayed pawn facts to localized, score-free prompt fields.</summary>
    public static class SocialReflectionContextFormatter
    {
        /// <summary>
        /// Formats one frozen subject snapshot. Missing optional facts become empty fields so the prompt
        /// never invents a relationship, motive, appearance judgment, or social memory.
        /// </summary>
        public static SocialReflectionFormattedContext Format(
            SocialReflectionSubjectFacts facts,
            SocialReflectionPolicySnapshot policy)
        {
            SocialReflectionPolicySnapshot safe = policy ?? SocialReflectionPolicySnapshot.CreateDefault();
            SocialReflectionSubjectFacts source = facts ?? new SocialReflectionSubjectFacts();
            SocialReflectionStyle style = SocialReflectionPolicy.StyleForOpinion(source.opinion, safe);
            SocialReflectionFormattedContext result = new SocialReflectionFormattedContext
            {
                subjectName = Clean(source.displayName),
                subjectAge = source.biologicalAgeYears.HasValue
                    ? Math.Max(0, source.biologicalAgeYears.Value)
                        .ToString(CultureInfo.InvariantCulture)
                    : string.Empty,
                subjectLifeStage = Clean(source.lifeStageLabel),
                subjectRelation = Clean(source.primaryRelationLabel),
                subjectSentiment = Clean(source.opinionBandLabel),
                polarity = style.polarity,
                tone = style.tone,
                colorCue = style.colorCue
            };

            string[] beautyLabels =
            {
                Clean(safe.veryUnattractiveLabel),
                Clean(safe.unattractiveLabel),
                Clean(safe.ordinaryAppearanceLabel),
                Clean(safe.attractiveLabel),
                Clean(safe.veryAttractiveLabel)
            };
            result.subjectAppearance = source.beauty.HasValue
                ? beautyLabels[
                    SocialReflectionPolicy.BeautyBandIndex(source.beauty.Value, safe)]
                : string.Empty;

            int reasonCap = Math.Max(0, safe.maximumOpinionReasons);
            if (source.socialReasons != null && reasonCap > 0)
            {
                result.subjectReasons = string.Join(
                    ", ",
                    source.socialReasons
                        .Select(Clean)
                        .Where(reason => reason.Length > 0)
                        .Distinct(StringComparer.Ordinal)
                        .Take(reasonCap));
            }

            return result;
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}

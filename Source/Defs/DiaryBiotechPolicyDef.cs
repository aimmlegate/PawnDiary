// XML schema and main-thread snapshot adapter for Biotech B1 growth/family policy. The Def owns all
// tunable thresholds, exact activity matcher strings, and localized qualitative descriptions. Pure
// policies receive a detached BiotechPolicySnapshot and never touch DefDatabase themselves.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary
{
    /// <summary>One inclusive internal growth-tier range and localized qualitative description.</summary>
    public class DiaryBiotechOpportunityBandDef
    {
        public int minimumTier;
        public int maximumTier;
        public string token;
        public string description;
    }

    /// <summary>One minimum exact-observation count and localized upbringing description.</summary>
    public class DiaryBiotechObservationBandDef
    {
        public int minimumEvidence;
        public string token;
        public string description;
    }

    /// <summary>Singleton XML-owned policy for Biotech B1; it contains no live DLC Def reference.</summary>
    public class DiaryBiotechPolicyDef : Def
    {
        public int growthPendingExpiryTicks = 180000;
        public int growthFallbackGraceTicks = 60000;
        public List<DiaryBiotechOpportunityBandDef> opportunityBands =
            new List<DiaryBiotechOpportunityBandDef>();
        // Localized prompt prose for passion transitions. Stable passion tokens stay in the pure
        // contract, while these DefInjected descriptions are what the LLM actually sees.
        public string newInterestDescription;
        public string deepenedInterestDescription;
        // N2-B provider prose is DefInjected. {0} is the child's visible short name; the identity
        // format additionally uses {1} for the visible current xenotype label.
        public string familySinceBirthNarrativeFormat;
        public string familyObservedNarrativeFormat;
        public string familyBaselineNarrativeFormat;
        public string identityNarrativeFormat;
        public List<string> familyActivityExactDefNames = new List<string>();
        public List<string> familyActivityPrefixes = new List<string>();
        public List<string> familyPregnancyHediffDefNames = new List<string>();
        public List<string> familyLaborHediffDefNames = new List<string>();
        public List<string> familyLessonAdultThoughtDefNames = new List<string>();
        public List<string> familyLessonChildThoughtDefNames = new List<string>();
        public List<string> matureBirthDefNames = new List<string>();
        public List<string> miscarriageBirtherThoughtDefNames = new List<string>();
        public List<string> miscarriagePartnerThoughtDefNames = new List<string>();
        public int familyActivityPairDedupTicks = 2500;
        public List<DiaryBiotechObservationBandDef> observationBands =
            new List<DiaryBiotechObservationBandDef>();
        public int supporterMinimumEvidence = 2;
        public int maximumSupporterRows = 12;
        public int familyArcRetentionTicks = 3600000;
        public int birthNamingPollTicks = 2500;
        public int birthNamingGraceTicks = 60000;
        public int birthCorrelationExpiryTicks = 2500;
        public int maximumBirthWriters = 2;

        /// <summary>Reports malformed XML policy at Def load instead of failing inside a later hook.</summary>
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (growthPendingExpiryTicks <= 0) yield return "growthPendingExpiryTicks must be positive.";
            if (growthFallbackGraceTicks < 0) yield return "growthFallbackGraceTicks cannot be negative.";
            if (string.IsNullOrWhiteSpace(newInterestDescription))
                yield return "newInterestDescription must contain DefInjected prompt prose.";
            if (string.IsNullOrWhiteSpace(deepenedInterestDescription))
                yield return "deepenedInterestDescription must contain DefInjected prompt prose.";
            if (string.IsNullOrWhiteSpace(familySinceBirthNarrativeFormat))
                yield return "familySinceBirthNarrativeFormat must contain DefInjected prompt prose.";
            if (string.IsNullOrWhiteSpace(familyObservedNarrativeFormat))
                yield return "familyObservedNarrativeFormat must contain DefInjected prompt prose.";
            if (string.IsNullOrWhiteSpace(familyBaselineNarrativeFormat))
                yield return "familyBaselineNarrativeFormat must contain DefInjected prompt prose.";
            if (string.IsNullOrWhiteSpace(identityNarrativeFormat))
                yield return "identityNarrativeFormat must contain DefInjected prompt prose.";
            if (familyActivityPairDedupTicks < 0) yield return "familyActivityPairDedupTicks cannot be negative.";
            if (supporterMinimumEvidence <= 0) yield return "supporterMinimumEvidence must be positive.";
            if (maximumSupporterRows <= 0) yield return "maximumSupporterRows must be positive.";
            if (familyArcRetentionTicks <= 0) yield return "familyArcRetentionTicks must be positive.";
            if (birthNamingPollTicks <= 0) yield return "birthNamingPollTicks must be positive.";
            if (birthNamingGraceTicks < 0) yield return "birthNamingGraceTicks cannot be negative.";
            if (birthCorrelationExpiryTicks <= 0) yield return "birthCorrelationExpiryTicks must be positive.";
            if (maximumBirthWriters < 1 || maximumBirthWriters > 2)
            {
                yield return "maximumBirthWriters must stay between one and the hard two-writer cap.";
            }
            if (!HasNonBlankMatcher(matureBirthDefNames))
                yield return "matureBirthDefNames must contain at least one exact Def name.";
            if (!HasNonBlankMatcher(miscarriageBirtherThoughtDefNames))
                yield return "miscarriageBirtherThoughtDefNames must contain at least one exact Def name.";
            if (!HasNonBlankMatcher(miscarriagePartnerThoughtDefNames))
                yield return "miscarriagePartnerThoughtDefNames must contain at least one exact Def name.";

            foreach (string error in OpportunityBandErrors(opportunityBands)) yield return error;
            foreach (string error in ObservationBandErrors(observationBands)) yield return error;
        }

        internal static bool HasNonBlankMatcher(List<string> values)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i])) return true;
            }
            return false;
        }

        private static IEnumerable<string> OpportunityBandErrors(List<DiaryBiotechOpportunityBandDef> bands)
        {
            bool[] covered = new bool[9];
            if (bands == null || bands.Count == 0)
            {
                yield return "opportunityBands must cover internal tiers zero through eight.";
                yield break;
            }

            HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < bands.Count; i++)
            {
                DiaryBiotechOpportunityBandDef band = bands[i];
                if (band == null || string.IsNullOrWhiteSpace(band.token)
                    || band.minimumTier < 0 || band.maximumTier > 8 || band.minimumTier > band.maximumTier)
                {
                    yield return "opportunityBands rows require a token and an inclusive range within zero through eight.";
                    continue;
                }

                if (!tokens.Add(band.token.Trim()))
                {
                    yield return "opportunityBands tokens must be unique.";
                }

                if (string.IsNullOrWhiteSpace(band.description))
                {
                    yield return "opportunityBands descriptions must be non-blank DefInjected prompt prose.";
                }

                for (int tier = band.minimumTier; tier <= band.maximumTier; tier++)
                {
                    if (covered[tier]) yield return "opportunityBands ranges must not overlap.";
                    covered[tier] = true;
                }
            }

            for (int tier = 0; tier < covered.Length; tier++)
            {
                if (!covered[tier]) yield return "opportunityBands must cover every tier from zero through eight.";
            }
        }

        private static IEnumerable<string> ObservationBandErrors(List<DiaryBiotechObservationBandDef> bands)
        {
            if (bands == null || bands.Count == 0)
            {
                yield return "observationBands must contain at least one qualitative band.";
                yield break;
            }

            HashSet<int> thresholds = new HashSet<int>();
            HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < bands.Count; i++)
            {
                DiaryBiotechObservationBandDef band = bands[i];
                if (band == null || band.minimumEvidence <= 0 || string.IsNullOrWhiteSpace(band.token))
                {
                    yield return "observationBands rows require a positive threshold and token.";
                    continue;
                }

                if (!thresholds.Add(band.minimumEvidence) || !tokens.Add(band.token.Trim()))
                {
                    yield return "observationBands thresholds and tokens must be unique.";
                }

                if (string.IsNullOrWhiteSpace(band.description))
                {
                    yield return "observationBands descriptions must be non-blank DefInjected prompt prose.";
                }
            }
        }
    }

    /// <summary>Copies the singleton live Def into a detached plain snapshot with safe fallbacks.</summary>
    internal static class DiaryBiotechPolicy
    {
        private const string DefName = "Diary_BiotechPolicy";

        /// <summary>Returns a detached policy snapshot, safely falling back when the Def is unavailable.</summary>
        public static BiotechPolicySnapshot Snapshot()
        {
            BiotechPolicySnapshot result = BiotechPolicySnapshot.CreateDefault();
            DiaryBiotechPolicyDef source = DefDatabase<DiaryBiotechPolicyDef>.GetNamedSilentFail(DefName);
            if (source == null)
            {
                return result;
            }

            result.growthPendingExpiryTicks = Positive(source.growthPendingExpiryTicks, result.growthPendingExpiryTicks);
            result.growthFallbackGraceTicks = Math.Max(0, source.growthFallbackGraceTicks);
            result.familyActivityPairDedupTicks = Math.Max(0, source.familyActivityPairDedupTicks);
            result.supporterMinimumEvidence = Positive(source.supporterMinimumEvidence, result.supporterMinimumEvidence);
            result.maximumSupporterRows = Positive(source.maximumSupporterRows, result.maximumSupporterRows);
            result.familyArcRetentionTicks = Positive(source.familyArcRetentionTicks, result.familyArcRetentionTicks);
            result.birthNamingPollTicks = Positive(source.birthNamingPollTicks, result.birthNamingPollTicks);
            result.birthNamingGraceTicks = Math.Max(0, source.birthNamingGraceTicks);
            result.birthCorrelationExpiryTicks = Positive(
                source.birthCorrelationExpiryTicks,
                result.birthCorrelationExpiryTicks);
            result.maximumBirthWriters = source.maximumBirthWriters < 1 || source.maximumBirthWriters > 2
                ? result.maximumBirthWriters
                : source.maximumBirthWriters;
            result.newInterestDescription = source.newInterestDescription ?? string.Empty;
            result.deepenedInterestDescription = source.deepenedInterestDescription ?? string.Empty;
            result.familySinceBirthNarrativeFormat = source.familySinceBirthNarrativeFormat ?? string.Empty;
            result.familyObservedNarrativeFormat = source.familyObservedNarrativeFormat ?? string.Empty;
            result.familyBaselineNarrativeFormat = source.familyBaselineNarrativeFormat ?? string.Empty;
            result.identityNarrativeFormat = source.identityNarrativeFormat ?? string.Empty;
            CopyOpportunityBands(source.opportunityBands, result);
            CopyObservationBands(source.observationBands, result);
            CopyStrings(source.familyActivityExactDefNames, result.familyActivityExactDefNames);
            CopyStrings(source.familyActivityPrefixes, result.familyActivityPrefixes);
            CopyStrings(source.familyPregnancyHediffDefNames, result.familyPregnancyHediffDefNames);
            CopyStrings(source.familyLaborHediffDefNames, result.familyLaborHediffDefNames);
            CopyStrings(source.familyLessonAdultThoughtDefNames, result.familyLessonAdultThoughtDefNames);
            CopyStrings(source.familyLessonChildThoughtDefNames, result.familyLessonChildThoughtDefNames);
            ReplaceStrings(source.matureBirthDefNames, result.matureBirthDefNames);
            ReplaceStrings(
                source.miscarriageBirtherThoughtDefNames,
                result.miscarriageBirtherThoughtDefNames);
            ReplaceStrings(
                source.miscarriagePartnerThoughtDefNames,
                result.miscarriagePartnerThoughtDefNames);
            return result;
        }

        private static void CopyOpportunityBands(
            List<DiaryBiotechOpportunityBandDef> source,
            BiotechPolicySnapshot destination)
        {
            if (source == null || source.Count == 0) return;
            List<BiotechOpportunityBandRule> copied = new List<BiotechOpportunityBandRule>();
            for (int i = 0; i < source.Count; i++)
            {
                DiaryBiotechOpportunityBandDef band = source[i];
                if (band != null && !string.IsNullOrWhiteSpace(band.token)
                    && band.minimumTier >= 0 && band.maximumTier <= 8 && band.minimumTier <= band.maximumTier)
                {
                    copied.Add(new BiotechOpportunityBandRule
                    {
                        minimumTier = band.minimumTier,
                        maximumTier = band.maximumTier,
                        token = band.token.Trim(),
                        description = band.description ?? string.Empty
                    });
                }
            }

            if (copied.Count > 0) destination.opportunityBands = copied;
        }

        private static void CopyObservationBands(
            List<DiaryBiotechObservationBandDef> source,
            BiotechPolicySnapshot destination)
        {
            if (source == null || source.Count == 0) return;
            List<BiotechObservationBandRule> copied = new List<BiotechObservationBandRule>();
            for (int i = 0; i < source.Count; i++)
            {
                DiaryBiotechObservationBandDef band = source[i];
                if (band != null && band.minimumEvidence > 0 && !string.IsNullOrWhiteSpace(band.token))
                {
                    copied.Add(new BiotechObservationBandRule
                    {
                        minimumEvidence = band.minimumEvidence,
                        token = band.token.Trim(),
                        description = band.description ?? string.Empty
                    });
                }
            }

            if (copied.Count > 0) destination.observationBands = copied;
        }

        private static void CopyStrings(List<string> source, List<string> destination)
        {
            if (source == null) return;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; i++)
            {
                string value = (source[i] ?? string.Empty).Trim();
                if (value.Length > 0 && seen.Add(value)) destination.Add(value);
            }
        }

        private static void ReplaceStrings(List<string> source, List<string> destination)
        {
            if (!DiaryBiotechPolicyDef.HasNonBlankMatcher(source)) return;
            destination.Clear();
            CopyStrings(source, destination);
        }

        private static int Positive(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }
    }
}

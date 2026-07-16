// XML-facing policy for Odyssey gravship journeys. The Def stores only primitive values and plain
// Def-name strings, so loading Pawn Diary without Odyssey never resolves or requires DLC content.
// Runtime code immediately copies this mutable Def into the detached OdysseyPolicySnapshot used by
// pure classification, history, writer, and formatting policy.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>One exact biome/site Def-name mapping authored in Odyssey policy XML.</summary>
    public sealed class DiaryOdysseyLocationCategoryDef
    {
        public string defName = string.Empty;
        public string categoryToken = string.Empty;
        public bool majorDestination;
    }

    /// <summary>One ordered landing reason and its page-policy flags.</summary>
    public sealed class DiaryOdysseyReasonRuleDef
    {
        public string reasonToken = string.Empty;
        public int priority;
        public bool bypassCooldown;
        public bool important;
    }

    /// <summary>
    /// Singleton XML-owned Odyssey policy. Every DLC reference is a plain string; live access is
    /// separately guarded by ModsConfig.OdysseyActive in DlcContext.
    /// </summary>
    public sealed class DiaryOdysseyPolicyDef : Def
    {
        public bool enabled = true;
        public string packageId = "Ludeon.RimWorld.Odyssey";
        public string launchGroupKey = "ritualGravship";
        public string landingGroupKey = "odysseyGravshipLanding";
        public int takeoffCorrelationTicks = 2500;
        public int landingCorrelationTicks = 2500;
        public int staleJourneyRetentionTicks = 3600000;
        public int launchCooldownTicks = 60000;
        public int landingCooldownTicks = 60000;
        public int shortJourneyMaximumTicks = 15000;
        public int longJourneyMinimumTicks = 60000;
        public int longHeldHomeMinimumTicks = 900000;
        public int maximumLaunchWriters = 2;
        public int maximumLandingWriters = 2;
        public int maximumVisitedLocations = 128;
        public int maximumVisitedCategories = 64;
        public int maximumHomeKeys = 32;
        public int maximumEmittedJourneyIds = 128;
        public int maximumContextCharacters = 900;
        public int maximumContextValueCharacters = 120;
        public float poorLaunchQualityMaximum = 0.35f;
        public float excellentLaunchQualityMinimum = 0.75f;
        public List<DiaryOdysseyLocationCategoryDef> biomeCategories =
            new List<DiaryOdysseyLocationCategoryDef>();
        public List<DiaryOdysseyLocationCategoryDef> siteCategories =
            new List<DiaryOdysseyLocationCategoryDef>();
        public List<DiaryOdysseyReasonRuleDef> reasonRules =
            new List<DiaryOdysseyReasonRuleDef>();

        /// <summary>Reports malformed policy during Def loading instead of failing later in play.</summary>
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (string.IsNullOrWhiteSpace(packageId)) yield return "packageId must be non-blank.";
            if (string.IsNullOrWhiteSpace(launchGroupKey)) yield return "launchGroupKey must be non-blank.";
            if (string.IsNullOrWhiteSpace(landingGroupKey)) yield return "landingGroupKey must be non-blank.";
            if (takeoffCorrelationTicks <= 0) yield return "takeoffCorrelationTicks must be positive.";
            if (landingCorrelationTicks <= 0) yield return "landingCorrelationTicks must be positive.";
            if (staleJourneyRetentionTicks <= 0) yield return "staleJourneyRetentionTicks must be positive.";
            if (launchCooldownTicks < 0 || landingCooldownTicks < 0)
                yield return "launch/landing cooldown ticks cannot be negative.";
            if (shortJourneyMaximumTicks < 0 || longJourneyMinimumTicks <= shortJourneyMaximumTicks)
                yield return "journey duration thresholds must satisfy 0 <= short < long.";
            if (longHeldHomeMinimumTicks <= 0) yield return "longHeldHomeMinimumTicks must be positive.";
            if (maximumLaunchWriters < 1 || maximumLaunchWriters > 2
                || maximumLandingWriters < 1 || maximumLandingWriters > 2)
                yield return "Odyssey writer caps must be 1 or 2.";
            if (maximumVisitedLocations <= 0 || maximumVisitedCategories <= 0
                || maximumHomeKeys <= 0 || maximumEmittedJourneyIds <= 0)
                yield return "Odyssey history caps must be positive.";
            if (maximumContextCharacters <= 0 || maximumContextValueCharacters <= 0)
                yield return "Odyssey context caps must be positive.";
            if (float.IsNaN(poorLaunchQualityMaximum) || float.IsNaN(excellentLaunchQualityMinimum)
                || poorLaunchQualityMaximum < 0f || poorLaunchQualityMaximum > 1f
                || excellentLaunchQualityMinimum < poorLaunchQualityMaximum
                || excellentLaunchQualityMinimum > 1f)
                yield return "launch quality thresholds must satisfy 0 <= poor <= excellent <= 1.";

            foreach (string error in MappingErrors(biomeCategories, "biomeCategories")) yield return error;
            foreach (string error in MappingErrors(siteCategories, "siteCategories")) yield return error;

            HashSet<string> reasons = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> priorities = new HashSet<int>();
            if (reasonRules == null || reasonRules.Count == 0)
            {
                yield return "reasonRules must list the frozen Odyssey landing reasons.";
            }
            else
            {
                for (int i = 0; i < reasonRules.Count; i++)
                {
                    DiaryOdysseyReasonRuleDef rule = reasonRules[i];
                    string token = rule == null ? string.Empty : (rule.reasonToken ?? string.Empty).Trim();
                    if (!OdysseyLandingReasonTokens.IsKnown(token))
                        yield return "reasonRules contains unknown token '" + token + "'.";
                    else if (!reasons.Add(token))
                        yield return "reasonRules repeats token '" + token + "'.";
                    if (rule != null && !priorities.Add(rule.priority))
                        yield return "reasonRules repeats priority " + rule.priority + ".";
                }

                string[] requiredReasons =
                {
                    OdysseyLandingReasonTokens.FirstOrbit,
                    OdysseyLandingReasonTokens.NewBiomeCategory,
                    OdysseyLandingReasonTokens.MajorDestination,
                    OdysseyLandingReasonTokens.Homecoming,
                    OdysseyLandingReasonTokens.LongJourney,
                    OdysseyLandingReasonTokens.RoughLanding
                };
                for (int i = 0; i < requiredReasons.Length; i++)
                {
                    if (!reasons.Contains(requiredReasons[i]))
                    {
                        yield return "reasonRules is missing frozen token '" + requiredReasons[i] + "'.";
                    }
                }
            }
        }

        private static IEnumerable<string> MappingErrors(
            List<DiaryOdysseyLocationCategoryDef> mappings,
            string fieldName)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (mappings == null) yield break;
            for (int i = 0; i < mappings.Count; i++)
            {
                DiaryOdysseyLocationCategoryDef row = mappings[i];
                string defName = row == null ? string.Empty : (row.defName ?? string.Empty).Trim();
                string token = row == null ? string.Empty : (row.categoryToken ?? string.Empty).Trim();
                if (defName.Length == 0 || token.Length == 0 || token.IndexOfAny(new[] { '|', ';', '=' }) >= 0)
                    yield return fieldName + " row " + i + " needs a non-blank Def name and safe category token.";
                else if (!seen.Add(defName))
                    yield return fieldName + " repeats Def name '" + defName + "'.";
            }
        }
    }

    /// <summary>Copies the singleton live Def into detached pure policy with safe code fallbacks.</summary>
    internal static class DiaryOdysseyPolicy
    {
        internal const string DefName = "Diary_Odyssey";
        private static DiaryOdysseyPolicyDef cachedSource;
        private static OdysseyPolicySnapshot cachedSnapshot;

        public static OdysseyPolicySnapshot Snapshot()
        {
            DiaryOdysseyPolicyDef source = DefDatabase<DiaryOdysseyPolicyDef>.GetNamedSilentFail(DefName);
            if (source != null && ReferenceEquals(source, cachedSource) && cachedSnapshot != null)
            {
                return cachedSnapshot;
            }

            OdysseyPolicySnapshot result = OdysseyPolicySnapshot.CreateDefault();
            if (source == null) return result;

            result.enabled = source.enabled;
            result.packageId = Clean(source.packageId, result.packageId);
            result.launchGroupKey = Clean(source.launchGroupKey, result.launchGroupKey);
            result.landingGroupKey = Clean(source.landingGroupKey, result.landingGroupKey);
            result.takeoffCorrelationTicks = Positive(source.takeoffCorrelationTicks, result.takeoffCorrelationTicks);
            result.landingCorrelationTicks = Positive(source.landingCorrelationTicks, result.landingCorrelationTicks);
            result.staleJourneyRetentionTicks = Positive(source.staleJourneyRetentionTicks, result.staleJourneyRetentionTicks);
            result.launchCooldownTicks = Math.Max(0, source.launchCooldownTicks);
            result.landingCooldownTicks = Math.Max(0, source.landingCooldownTicks);
            result.shortJourneyMaximumTicks = Math.Max(0, source.shortJourneyMaximumTicks);
            result.longJourneyMinimumTicks = Math.Max(
                result.shortJourneyMaximumTicks + 1,
                source.longJourneyMinimumTicks);
            result.longHeldHomeMinimumTicks = Positive(source.longHeldHomeMinimumTicks, result.longHeldHomeMinimumTicks);
            result.maximumLaunchWriters = WriterCap(source.maximumLaunchWriters, result.maximumLaunchWriters);
            result.maximumLandingWriters = WriterCap(source.maximumLandingWriters, result.maximumLandingWriters);
            result.maximumVisitedLocations = Positive(source.maximumVisitedLocations, result.maximumVisitedLocations);
            result.maximumVisitedCategories = Positive(source.maximumVisitedCategories, result.maximumVisitedCategories);
            result.maximumHomeKeys = Positive(source.maximumHomeKeys, result.maximumHomeKeys);
            result.maximumEmittedJourneyIds = Positive(source.maximumEmittedJourneyIds, result.maximumEmittedJourneyIds);
            result.maximumContextCharacters = Positive(source.maximumContextCharacters, result.maximumContextCharacters);
            result.maximumContextValueCharacters = Positive(
                source.maximumContextValueCharacters,
                result.maximumContextValueCharacters);
            if (!float.IsNaN(source.poorLaunchQualityMaximum)
                && !float.IsNaN(source.excellentLaunchQualityMinimum)
                && source.poorLaunchQualityMaximum >= 0f
                && source.poorLaunchQualityMaximum <= source.excellentLaunchQualityMinimum
                && source.excellentLaunchQualityMinimum <= 1f)
            {
                result.poorLaunchQualityMaximum = source.poorLaunchQualityMaximum;
                result.excellentLaunchQualityMinimum = source.excellentLaunchQualityMinimum;
            }

            CopyMappings(source.biomeCategories, result.biomeCategories);
            CopyMappings(source.siteCategories, result.siteCategories);
            List<OdysseyReasonRule> reasons = CopyReasons(source.reasonRules);
            if (reasons.Count > 0) result.reasonRules = reasons;
            cachedSource = source;
            cachedSnapshot = result;
            return result;
        }

        private static void CopyMappings(
            List<DiaryOdysseyLocationCategoryDef> source,
            List<OdysseyLocationCategoryRule> destination)
        {
            if (source == null || source.Count == 0) return;
            destination.Clear();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; i++)
            {
                DiaryOdysseyLocationCategoryDef row = source[i];
                string defName = row == null ? string.Empty : (row.defName ?? string.Empty).Trim();
                string token = row == null ? string.Empty : (row.categoryToken ?? string.Empty).Trim();
                if (defName.Length > 0 && token.Length > 0 && seen.Add(defName))
                {
                    destination.Add(new OdysseyLocationCategoryRule
                    {
                        defName = defName,
                        categoryToken = token,
                        majorDestination = row.majorDestination
                    });
                }
            }
        }

        private static List<OdysseyReasonRule> CopyReasons(List<DiaryOdysseyReasonRuleDef> source)
        {
            List<OdysseyReasonRule> result = new List<OdysseyReasonRule>();
            if (source == null) return result;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.Count; i++)
            {
                DiaryOdysseyReasonRuleDef row = source[i];
                string token = row == null ? string.Empty : (row.reasonToken ?? string.Empty).Trim();
                if (OdysseyLandingReasonTokens.IsKnown(token) && seen.Add(token))
                {
                    result.Add(new OdysseyReasonRule
                    {
                        reasonToken = token,
                        priority = row.priority,
                        bypassCooldown = row.bypassCooldown,
                        important = row.important
                    });
                }
            }

            return result;
        }

        private static string Clean(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static int Positive(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }

        private static int WriterCap(int value, int fallback)
        {
            return value >= 1 && value <= 2 ? value : fallback;
        }
    }
}

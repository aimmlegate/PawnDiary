// Generic XML policy for event trackers. C# owns observation (thought gained, work sampled, etc.);
// XML owns the thresholds, cooldowns, token lists, and weights that decide whether a tracked signal
// becomes prompt material. This keeps future trackers extensible without adding settings fields.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One signal policy keyed by signalKey, for example "Thought", "ThoughtProgression", or "Work".
    /// Fields are intentionally broad: trackers read only the fields relevant to their source.
    /// </summary>
    public class DiarySignalPolicyDef : Def
    {
        public string signalKey;
        public bool enabled = true;

        // Shared scanner/dedup knobs.
        public int scanIntervalTicks = -1;
        public int dedupTicks = -1;

        // Temporary ThoughtDef recording.
        public float minMoodOffset = -1f;
        public float eatingMinMoodOffset = -1f;
        public List<string> ignoreTokens;
        public List<string> bypassThresholdTokens;
        public List<string> eatingTokens;
        public List<string> ambientTokens;

        // Ambient thought batching.
        public int ambientWindowTicks = -1;
        public int ambientMinEventsToWrite = -1;
        public int ambientMaxSampleLines = -1;

        // Situational thought progression.
        public List<ThoughtProgressionRule> thoughtProgressionRules;

        // Work sampling.
        public float baseChance = -1f;
        public int sameTypeCooldownTicks = -1;
        public float recentDifferentTypeMultiplier = -1f;
        public float passionChanceMultiplier = -1f;
        public float negativeChanceMultiplier = -1f;
        public float darkStudyChanceMultiplier = -1f;
        public int lowSkillThreshold = -1;
    }

    /// <summary>
    /// Runtime accessor for signal policies. Every helper falls back to DiaryTuningDef so partial XML
    /// patches are safe and older tuning files keep working.
    /// </summary>
    internal static class DiarySignalPolicies
    {
        public const string Thought = "Thought";
        public const string AmbientThought = "AmbientThought";
        public const string ThoughtProgression = "ThoughtProgression";
        public const string Work = "Work";
        public const string Progression = "Progression";

        public static bool Enabled(string signalKey)
        {
            return ForKey(signalKey).enabled;
        }

        public static List<string> ThoughtIgnoreTokens
        {
            get { return ListOrFallback(ForKey(Thought).ignoreTokens, DiaryTuning.Current.thoughtIgnoreTokens); }
        }

        public static List<string> ThoughtBypassThresholdTokens
        {
            get { return ListOrFallback(ForKey(Thought).bypassThresholdTokens, DiaryTuning.Current.thoughtBypassThresholdTokens); }
        }

        public static List<string> ThoughtEatingTokens
        {
            get { return ListOrFallback(ForKey(Thought).eatingTokens, DiaryTuning.Current.thoughtEatingTokens); }
        }

        public static List<string> ThoughtAmbientTokens
        {
            get { return ListOrFallback(ForKey(Thought).ambientTokens, DiaryTuning.Current.thoughtAmbientTokens); }
        }

        public static float ThoughtMinMoodOffset
        {
            get { return FloatOrFallback(ForKey(Thought).minMoodOffset, DiaryTuning.Current.thoughtMinMoodOffset); }
        }

        public static float ThoughtEatingMinMoodOffset
        {
            get { return FloatOrFallback(ForKey(Thought).eatingMinMoodOffset, DiaryTuning.Current.thoughtEatingMinMoodOffset); }
        }

        public static int ThoughtDedupTicks
        {
            get { return IntOrFallback(ForKey(Thought).dedupTicks, DiaryTuning.Current.thoughtDedupTicks); }
        }

        public static int AmbientThoughtWindowTicks
        {
            get { return IntOrFallback(ForKey(AmbientThought).ambientWindowTicks, DiaryTuning.Current.thoughtAmbientWindowTicks); }
        }

        public static int AmbientThoughtMinEventsToWrite
        {
            get { return IntOrFallback(ForKey(AmbientThought).ambientMinEventsToWrite, DiaryTuning.Current.thoughtAmbientMinEventsToWrite); }
        }

        public static int AmbientThoughtMaxSampleLines
        {
            get { return IntOrFallback(ForKey(AmbientThought).ambientMaxSampleLines, DiaryTuning.Current.thoughtAmbientMaxSampleLines); }
        }

        public static int ThoughtProgressionScanIntervalTicks
        {
            get { return IntOrFallback(ForKey(ThoughtProgression).scanIntervalTicks, DiaryTuning.Current.thoughtProgressionScanIntervalTicks); }
        }

        public static int ThoughtProgressionDedupTicks
        {
            get { return IntOrFallback(ForKey(ThoughtProgression).dedupTicks, DiaryTuning.Current.thoughtProgressionDedupTicks); }
        }

        public static List<ThoughtProgressionRule> ThoughtProgressionRules
        {
            get
            {
                List<ThoughtProgressionRule> rules = ForKey(ThoughtProgression).thoughtProgressionRules;
                return rules ?? DiaryTuning.Current.thoughtProgressionRules;
            }
        }

        public static int WorkScanIntervalTicks
        {
            get { return IntOrFallback(ForKey(Work).scanIntervalTicks, DiaryTuning.Current.workScanIntervalTicks); }
        }

        public static float WorkBaseChance
        {
            get { return FloatOrFallback(ForKey(Work).baseChance, DiaryTuning.Current.workBaseChance); }
        }

        public static int WorkSameTypeCooldownTicks
        {
            get { return IntOrFallback(ForKey(Work).sameTypeCooldownTicks, DiaryTuning.Current.workSameTypeCooldownTicks); }
        }

        public static float WorkRecentDifferentTypeMultiplier
        {
            get { return FloatOrFallback(ForKey(Work).recentDifferentTypeMultiplier, DiaryTuning.Current.workRecentDifferentTypeMultiplier); }
        }

        public static float WorkPassionChanceMultiplier
        {
            get { return FloatOrFallback(ForKey(Work).passionChanceMultiplier, DiaryTuning.Current.workPassionChanceMultiplier); }
        }

        public static float WorkNegativeChanceMultiplier
        {
            get { return FloatOrFallback(ForKey(Work).negativeChanceMultiplier, DiaryTuning.Current.workNegativeChanceMultiplier); }
        }

        public static float WorkDarkStudyChanceMultiplier
        {
            get { return FloatOrFallback(ForKey(Work).darkStudyChanceMultiplier, DiaryTuning.Current.workDarkStudyChanceMultiplier); }
        }

        public static int WorkLowSkillThreshold
        {
            get { return IntOrFallback(ForKey(Work).lowSkillThreshold, DiaryTuning.Current.workLowSkillThreshold); }
        }

        public static int ProgressionScanIntervalTicks
        {
            get { return IntOrFallback(ForKey(Progression).scanIntervalTicks, DiaryTuning.Current.progressionScanIntervalTicks); }
        }

        // Fallback policies are cached per key so a missing/renamed Def does not allocate a new
        // object on every getter call. The real Def always wins because ForKey scans the
        // DefDatabase first and only reaches the cache on a miss.
        private static readonly Dictionary<string, DiarySignalPolicyDef> FallbackCache =
            new Dictionary<string, DiarySignalPolicyDef>(StringComparer.OrdinalIgnoreCase);

        public static DiarySignalPolicyDef ForKey(string signalKey)
        {
            List<DiarySignalPolicyDef> defs = DefDatabase<DiarySignalPolicyDef>.AllDefsListForReading;
            if (defs != null)
            {
                for (int i = 0; i < defs.Count; i++)
                {
                    DiarySignalPolicyDef def = defs[i];
                    if (def == null)
                    {
                        continue;
                    }

                    if (string.Equals(def.signalKey, signalKey, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(def.defName, signalKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return def;
                    }
                }
            }

            string key = signalKey ?? "Unknown";
            DiarySignalPolicyDef fallback;
            if (FallbackCache.TryGetValue(key, out fallback))
            {
                return fallback;
            }

            fallback = new DiarySignalPolicyDef
            {
                defName = "DiarySignalPolicy_Fallback_" + key,
                signalKey = signalKey
            };
            FallbackCache[key] = fallback;
            return fallback;
        }

        private static int IntOrFallback(int configured, int fallback)
        {
            return configured >= 0 ? configured : fallback;
        }

        private static float FloatOrFallback(float configured, float fallback)
        {
            return configured >= 0f ? configured : fallback;
        }

        private static List<string> ListOrFallback(List<string> configured, List<string> fallback)
        {
            return configured ?? fallback;
        }
    }
}

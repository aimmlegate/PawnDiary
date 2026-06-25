// Prompt enchantment planning: pure weighted selection and text assembly for the optional
// "important context" prompt line. Runtime code must collect live RimWorld facts first, then pass
// only these plain DTOs here so selection can be tested without Verse/RimWorld assemblies.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// XML-backed prompt-enchantment tuning after fallback/safety normalization. New to C#/RimWorld?
    /// See AGENTS.md ("Defs") for why game policy lives in XML with code defaults.
    /// </summary>
    public sealed class PromptEnchantmentTuning
    {
        public const float DefaultMinorHediffSeverity = 0.05f;
        public const float DefaultModerateHediffSeverity = 0.25f;
        public const float DefaultMajorHediffSeverity = 0.50f;
        public const float DefaultCriticalHediffSeverity = 0.75f;
        public const float DefaultCloudedConsciousnessBelow = 0.55f;
        public const float DefaultFadingConsciousnessBelow = 0.35f;
        public const float DefaultBarelyConsciousBelow = 0.20f;
        public const int DefaultMaxImpactCues = 3;

        public float minorHediffSeverity = DefaultMinorHediffSeverity;
        public float moderateHediffSeverity = DefaultModerateHediffSeverity;
        public float majorHediffSeverity = DefaultMajorHediffSeverity;
        public float criticalHediffSeverity = DefaultCriticalHediffSeverity;
        public float cloudedConsciousnessBelow = DefaultCloudedConsciousnessBelow;
        public float fadingConsciousnessBelow = DefaultFadingConsciousnessBelow;
        public float barelyConsciousBelow = DefaultBarelyConsciousBelow;
        public int maxImpactCues = DefaultMaxImpactCues;

        /// <summary>
        /// Returns the cue cap after treating negative XML values as "use the safe default". Zero is
        /// valid and intentionally suppresses cue text.
        /// </summary>
        public int EffectiveMaxImpactCues()
        {
            return maxImpactCues < 0 ? DefaultMaxImpactCues : maxImpactCues;
        }
    }

    /// <summary>
    /// One collected prompt-enchantment option. It intentionally stores only primitive/localized text
    /// snapshots, never live <c>Pawn</c>, <c>Hediff</c>, or XML Def objects.
    /// </summary>
    public sealed class PromptEnchantmentCandidate
    {
        public float weight;
        public string priorityText;
        public string conditionText;
        public List<string> impactCues = new List<string>();
        public List<string> configuredCues = new List<string>();
    }

    /// <summary>
    /// Pure selector/formatter for prompt enchantments. The caller supplies the random roll so tests
    /// can cover exact weighted-pick boundaries without touching RimWorld's global RNG.
    /// </summary>
    public static class PromptEnchantmentPlanner
    {
        public static string Build(IList<PromptEnchantmentCandidate> candidates,
            PromptEnchantmentTuning tuning, float roll01)
        {
            float totalWeight = TotalWeight(candidates);
            if (candidates == null || candidates.Count == 0 || totalWeight <= 0f)
            {
                return string.Empty;
            }

            PromptEnchantmentCandidate candidate = PickWeighted(candidates, totalWeight, roll01);
            return FormatCandidate(candidate, tuning);
        }

        private static float TotalWeight(IList<PromptEnchantmentCandidate> candidates)
        {
            if (candidates == null)
            {
                return 0f;
            }

            float total = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                PromptEnchantmentCandidate candidate = candidates[i];
                if (candidate != null && candidate.weight > 0f && !float.IsNaN(candidate.weight))
                {
                    total += candidate.weight;
                }
            }

            return total;
        }

        private static PromptEnchantmentCandidate PickWeighted(IList<PromptEnchantmentCandidate> candidates,
            float totalWeight, float roll01)
        {
            float roll = Clamp01(roll01) * totalWeight;
            float cumulative = 0f;
            PromptEnchantmentCandidate lastPositive = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                PromptEnchantmentCandidate candidate = candidates[i];
                if (candidate == null || candidate.weight <= 0f || float.IsNaN(candidate.weight))
                {
                    continue;
                }

                cumulative += candidate.weight;
                lastPositive = candidate;
                if (roll <= cumulative)
                {
                    return candidate;
                }
            }

            return lastPositive;
        }

        private static string FormatCandidate(PromptEnchantmentCandidate candidate,
            PromptEnchantmentTuning tuning)
        {
            if (candidate == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            AddPart(parts, candidate.priorityText);
            AddPart(parts, candidate.conditionText);
            AddCuePart(parts, candidate.impactCues, tuning);
            AddCuePart(parts, candidate.configuredCues, tuning);
            return string.Join("; ", parts.ToArray());
        }

        private static void AddPart(List<string> parts, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(value);
            }
        }

        private static void AddCuePart(List<string> parts, IList<string> cues, PromptEnchantmentTuning tuning)
        {
            int maxCues = tuning == null
                ? PromptEnchantmentTuning.DefaultMaxImpactCues
                : tuning.EffectiveMaxImpactCues();
            if (cues == null || cues.Count == 0 || maxCues <= 0)
            {
                return;
            }

            List<string> kept = new List<string>();
            for (int i = 0; i < cues.Count && kept.Count < maxCues; i++)
            {
                string cue = cues[i];
                if (!string.IsNullOrWhiteSpace(cue))
                {
                    kept.Add(cue);
                }
            }

            if (kept.Count > 0)
            {
                parts.Add(string.Join(", ", kept.ToArray()));
            }
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value < 0f)
            {
                return 0f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }
    }
}

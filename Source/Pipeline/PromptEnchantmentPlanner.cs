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
    internal sealed class PromptEnchantmentTuning
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
    /// Pure age-based fade for lasting prompt enhancers. A factor of 1 means "full XML weight"; lower
    /// factors make the lasting context less likely to be selected and relax normal-context suppression
    /// back toward 1.0 without needing RimWorld state in the calculation.
    /// </summary>
    internal static class PromptEnchantmentDecayPolicy
    {
        public static float AgeFactor(int nowTick, int startedTick, int decayTicks, float minMultiplier)
        {
            if (decayTicks <= 0 || nowTick <= startedTick)
            {
                return 1f;
            }

            float floor = Clamp01(minMultiplier);
            float progress = Clamp01((float)(nowTick - startedTick) / decayTicks);
            return 1f + (floor - 1f) * progress;
        }

        public static float DecayedWeight(float baseWeight, float ageFactor)
        {
            if (float.IsNaN(baseWeight) || baseWeight <= 0f)
            {
                return 0f;
            }

            return baseWeight * Clamp01(ageFactor);
        }

        public static float RelaxedNormalMultiplier(float baseMultiplier, float ageFactor)
        {
            if (float.IsNaN(baseMultiplier) || baseMultiplier < 0f)
            {
                baseMultiplier = 0f;
            }

            return 1f + (baseMultiplier - 1f) * Clamp01(ageFactor);
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

    /// <summary>
    /// One collected prompt-enchantment option. It intentionally stores only primitive/localized text
    /// snapshots, never live <c>Pawn</c>, <c>Hediff</c>, or XML Def objects.
    /// </summary>
    internal sealed class PromptEnchantmentCandidate
    {
        public float weight;
        public string sourceHediffDefName;
        public string priorityText;
        public string conditionText;
        public List<string> impactCues = new List<string>();
        public List<string> configuredCues = new List<string>();
    }

    /// <summary>
    /// Pure selector/formatter for prompt enchantments. The caller supplies the random roll so tests
    /// can cover exact weighted-pick boundaries without touching RimWorld's global RNG.
    /// </summary>
    internal static class PromptEnchantmentPlanner
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

        public static List<PromptEnchantmentCandidate> WithoutSuppressedHediffSources(
            IList<PromptEnchantmentCandidate> candidates, IList<string> suppressedHediffDefNames)
        {
            List<PromptEnchantmentCandidate> kept = new List<PromptEnchantmentCandidate>();
            if (candidates == null)
            {
                return kept;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                PromptEnchantmentCandidate candidate = candidates[i];
                if (candidate == null
                    || !StringInList(candidate.sourceHediffDefName, suppressedHediffDefNames))
                {
                    kept.Add(candidate);
                }
            }

            return kept;
        }

        /// <summary>
        /// Prepares the same candidate pool <see cref="Build"/> should pick from: normal XML/health
        /// candidates first have hediff-style suppression and the live normal-weight multiplier applied;
        /// already-biased extra candidates are appended unchanged.
        /// </summary>
        public static List<PromptEnchantmentCandidate> PrepareCandidatesForBuild(
            IList<PromptEnchantmentCandidate> normalCandidates,
            IList<PromptEnchantmentCandidate> extraCandidates,
            float normalCandidateWeightMultiplier,
            IList<string> suppressedHediffDefNames)
        {
            List<PromptEnchantmentCandidate> prepared =
                WithoutSuppressedHediffSources(normalCandidates, suppressedHediffDefNames);
            ApplyNormalCandidateWeightMultiplier(prepared, normalCandidateWeightMultiplier);
            AddExtraCandidates(prepared, extraCandidates);
            return prepared;
        }

        private static void ApplyNormalCandidateWeightMultiplier(List<PromptEnchantmentCandidate> candidates,
            float multiplier)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return;
            }

            float safeMultiplier = float.IsNaN(multiplier) || multiplier < 0f ? 0f : multiplier;
            if (safeMultiplier == 1f)
            {
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                PromptEnchantmentCandidate candidate = candidates[i];
                if (candidate != null)
                {
                    candidate.weight *= safeMultiplier;
                }
            }
        }

        private static void AddExtraCandidates(List<PromptEnchantmentCandidate> candidates,
            IList<PromptEnchantmentCandidate> extraCandidates)
        {
            if (candidates == null || extraCandidates == null || extraCandidates.Count == 0)
            {
                return;
            }

            for (int i = 0; i < extraCandidates.Count; i++)
            {
                PromptEnchantmentCandidate candidate = extraCandidates[i];
                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            }
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

        private static bool StringInList(string value, IList<string> values)
        {
            if (string.IsNullOrWhiteSpace(value) || values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(value, values[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

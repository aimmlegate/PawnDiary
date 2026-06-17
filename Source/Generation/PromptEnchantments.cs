// XML-driven prompt enchantments: optional, one-shot writing directives chosen from live pawn
// state right before a first-person prompt is queued. These do not replace personas; they add one
// extra "prompt_enchantment:" line when a matching Def wins its chance and weight rolls.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One stat-threshold matcher used by <see cref="DiaryPromptEnchantmentDef"/>. XML stores the
    /// stat by defName string so bad or mod-removed stats can fail silently instead of crashing.
    /// </summary>
    public class PromptEnchantmentStatThreshold
    {
        public string statDefName;
        public float value;
    }

    /// <summary>
    /// One pawn-capacity threshold matcher used by <see cref="DiaryPromptEnchantmentDef"/>. Capacities
    /// are things like Consciousness, Moving, Sight, and Manipulation, expressed as 0..1-ish levels.
    /// </summary>
    public class PromptEnchantmentCapacityThreshold
    {
        public string capacityDefName;
        public float minValue = -1f;
        public float value;
    }

    /// <summary>
    /// Optional hediff-severity override for a prompt enchantment. XML authors name one of the fixed
    /// levels understood by <see cref="PromptEnchantments"/>; the code owns the numeric thresholds so
    /// tuning stays consistent across Defs.
    /// </summary>
    public class PromptEnchantmentSeverityTier
    {
        public string level;
        public string rule;

        // Defaults below zero mean "inherit the parent Def's value".
        public float chance = -1f;
        public float frequency = -1f;
        public float weight = -1f;
        public float severity = -1f;
    }

    /// <summary>
    /// XML-configured prompt modifier. A matching Def may append its <see cref="rule"/> to the LLM
    /// prompt as "prompt_enchantment:" for a single request.
    /// </summary>
    public class DiaryPromptEnchantmentDef : Def
    {
        // Natural-language directive for the model. This is Def text, so translators can override it
        // via DefInjected rather than Keyed strings.
        public string rule;

        // Chance/frequency controls whether a matching Def appears on this prompt at all.
        // "chance" is the documented XML name. "frequency" is accepted as an alias for authors who
        // think about this as appearance frequency; when set to 0 or greater it overrides chance.
        public float chance = 1f;
        public float frequency = -1f;

        // Selection controls once several matching Defs pass their chance roll. Severity multiplies
        // weight so XML can make urgent states dominate without exposing every match to the model.
        public float weight = 1f;
        public float severity = 1f;

        // Base-game health conditions. Matches visible hediffs by defName string.
        public List<string> hediffDefNames = new List<string>();
        public float minHediffSeverity = 0f;
        public List<PromptEnchantmentSeverityTier> hediffSeverityTiers = new List<PromptEnchantmentSeverityTier>();

        // DLC-safe string matchers. The actual pawn tracker reads live in DlcContext.
        public List<string> geneDefNames = new List<string>();
        public List<string> royalTitleDefNames = new List<string>();

        // Pawn StatDefs whose current value must be below the configured threshold.
        public List<PromptEnchantmentStatThreshold> statBelow = new List<PromptEnchantmentStatThreshold>();

        // PawnCapacityDefs whose current health capacity must be below the configured threshold.
        public List<PromptEnchantmentCapacityThreshold> capacityBelow = new List<PromptEnchantmentCapacityThreshold>();
    }

    /// <summary>
    /// Runtime resolver for prompt enchantment Defs. It deliberately returns only one rule per
    /// prompt so pawn state adds flavor without flooding small local models with competing orders.
    /// </summary>
    public static class PromptEnchantments
    {
        private sealed class Candidate
        {
            public readonly string rule;
            public readonly float weight;

            public Candidate(string rule, float weight)
            {
                this.rule = rule;
                this.weight = weight;
            }
        }

        private sealed class PromptEnchantmentMatch
        {
            public readonly string rule;
            public readonly float chance;
            public readonly float weight;
            public readonly float severity;

            public PromptEnchantmentMatch(string rule, float chance, float weight, float severity)
            {
                this.rule = rule;
                this.chance = chance;
                this.weight = weight;
                this.severity = severity;
            }
        }

        // Fixed hediff severity bands. These are intentionally code-owned so XML Defs pick named
        // levels instead of each inventing different numeric thresholds.
        private const float MinorHediffSeverity = 0.05f;
        private const float ModerateHediffSeverity = 0.25f;
        private const float MajorHediffSeverity = 0.50f;
        private const float CriticalHediffSeverity = 0.75f;

        /// <summary>
        /// Returns one matching enchantment rule for this pawn, or empty when disabled/no match.
        /// </summary>
        public static string RuleFor(Pawn pawn)
        {
            if (pawn == null || PawnDiaryMod.Settings == null || !PawnDiaryMod.Settings.enablePromptEnchantments)
            {
                return string.Empty;
            }

            List<DiaryPromptEnchantmentDef> defs = DefDatabase<DiaryPromptEnchantmentDef>.AllDefsListForReading;
            if (defs == null || defs.Count == 0)
            {
                return string.Empty;
            }

            float totalWeight = 0f;
            List<Candidate> candidates = new List<Candidate>();
            for (int i = 0; i < defs.Count; i++)
            {
                DiaryPromptEnchantmentDef def = defs[i];
                PromptEnchantmentMatch match = MatchFor(def, pawn);
                if (match == null || string.IsNullOrWhiteSpace(match.rule))
                {
                    continue;
                }

                float chance = ChanceFor(match.chance);
                if (chance <= 0f || Rand.Range(0f, 1f) > chance)
                {
                    continue;
                }

                float effectiveWeight = Mathf.Max(0f, match.weight) * Mathf.Max(0f, match.severity);
                if (effectiveWeight <= 0f)
                {
                    continue;
                }

                candidates.Add(new Candidate(match.rule, effectiveWeight));
                totalWeight += effectiveWeight;
            }

            if (candidates.Count == 0 || totalWeight <= 0f)
            {
                return string.Empty;
            }

            float roll = Rand.Range(0f, totalWeight);
            float cumulative = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                cumulative += candidates[i].weight;
                if (roll <= cumulative)
                {
                    return candidates[i].rule ?? string.Empty;
                }
            }

            return candidates[candidates.Count - 1].rule ?? string.Empty;
        }

        private static float ChanceFor(float configured)
        {
            return Mathf.Clamp01(configured);
        }

        private static PromptEnchantmentMatch MatchFor(DiaryPromptEnchantmentDef def, Pawn pawn)
        {
            if (def == null)
            {
                return null;
            }

            if (HasItems(def.hediffDefNames))
            {
                float hediffSeverity;
                if (TryMatchedHediffSeverity(def, pawn, out hediffSeverity))
                {
                    return MatchFrom(def, TierForHediffSeverity(def.hediffSeverityTiers, hediffSeverity));
                }
            }

            if (HasItems(def.geneDefNames))
            {
                if (DlcContext.HasActiveGene(pawn, def.geneDefNames))
                {
                    return MatchFrom(def, null);
                }
            }

            if (HasItems(def.royalTitleDefNames))
            {
                if (DlcContext.HasRoyalTitleDef(pawn, def.royalTitleDefNames))
                {
                    return MatchFrom(def, null);
                }
            }

            if (def.statBelow != null && def.statBelow.Count > 0)
            {
                if (MatchesStatBelow(def.statBelow, pawn))
                {
                    return MatchFrom(def, null);
                }
            }

            if (def.capacityBelow != null && def.capacityBelow.Count > 0)
            {
                if (MatchesCapacityBelow(def.capacityBelow, pawn))
                {
                    return MatchFrom(def, null);
                }
            }

            return null;
        }

        private static PromptEnchantmentMatch MatchFrom(DiaryPromptEnchantmentDef def, PromptEnchantmentSeverityTier tier)
        {
            string rule = tier != null && !string.IsNullOrWhiteSpace(tier.rule)
                ? tier.rule
                : def.rule;

            float chance = ResolvedChance(def, tier);
            float weight = tier != null && tier.weight >= 0f ? tier.weight : def.weight;
            float severity = tier != null && tier.severity >= 0f ? tier.severity : def.severity;
            return new PromptEnchantmentMatch(rule, chance, weight, severity);
        }

        private static float ResolvedChance(DiaryPromptEnchantmentDef def, PromptEnchantmentSeverityTier tier)
        {
            if (tier != null)
            {
                if (tier.frequency >= 0f)
                {
                    return tier.frequency;
                }

                if (tier.chance >= 0f)
                {
                    return tier.chance;
                }
            }

            return def.frequency >= 0f ? def.frequency : def.chance;
        }

        private static bool TryMatchedHediffSeverity(DiaryPromptEnchantmentDef def, Pawn pawn, out float matchedSeverity)
        {
            matchedSeverity = 0f;
            if (pawn?.health?.hediffSet?.hediffs == null)
            {
                return false;
            }

            bool matched = false;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff == null || hediff.def == null || !hediff.Visible)
                {
                    continue;
                }

                if (DefNameInList(hediff.def.defName, def.hediffDefNames)
                    && hediff.Severity >= Mathf.Max(0f, def.minHediffSeverity))
                {
                    matched = true;
                    matchedSeverity = Mathf.Max(matchedSeverity, hediff.Severity);
                }
            }

            return matched;
        }

        private static PromptEnchantmentSeverityTier TierForHediffSeverity(List<PromptEnchantmentSeverityTier> tiers, float hediffSeverity)
        {
            if (tiers == null || tiers.Count == 0)
            {
                return null;
            }

            PromptEnchantmentSeverityTier bestTier = null;
            float bestThreshold = -1f;
            for (int i = 0; i < tiers.Count; i++)
            {
                PromptEnchantmentSeverityTier tier = tiers[i];
                if (tier == null)
                {
                    continue;
                }

                float threshold;
                if (!TrySeverityLevelThreshold(tier.level, out threshold))
                {
                    continue;
                }

                if (hediffSeverity >= threshold && threshold >= bestThreshold)
                {
                    bestTier = tier;
                    bestThreshold = threshold;
                }
            }

            return bestTier;
        }

        private static bool TrySeverityLevelThreshold(string level, out float threshold)
        {
            threshold = 0f;
            if (string.IsNullOrWhiteSpace(level))
            {
                return false;
            }

            if (string.Equals(level, "minor", StringComparison.OrdinalIgnoreCase))
            {
                threshold = MinorHediffSeverity;
                return true;
            }

            if (string.Equals(level, "moderate", StringComparison.OrdinalIgnoreCase))
            {
                threshold = ModerateHediffSeverity;
                return true;
            }

            if (string.Equals(level, "major", StringComparison.OrdinalIgnoreCase)
                || string.Equals(level, "severe", StringComparison.OrdinalIgnoreCase))
            {
                threshold = MajorHediffSeverity;
                return true;
            }

            if (string.Equals(level, "critical", StringComparison.OrdinalIgnoreCase)
                || string.Equals(level, "extreme", StringComparison.OrdinalIgnoreCase))
            {
                threshold = CriticalHediffSeverity;
                return true;
            }

            return false;
        }

        private static bool MatchesStatBelow(List<PromptEnchantmentStatThreshold> thresholds, Pawn pawn)
        {
            if (thresholds == null || pawn == null)
            {
                return false;
            }

            for (int i = 0; i < thresholds.Count; i++)
            {
                PromptEnchantmentStatThreshold threshold = thresholds[i];
                if (threshold == null || string.IsNullOrWhiteSpace(threshold.statDefName))
                {
                    continue;
                }

                StatDef statDef = DefDatabase<StatDef>.GetNamedSilentFail(threshold.statDefName);
                if (statDef == null)
                {
                    continue;
                }

                try
                {
                    if (pawn.GetStatValue(statDef) < threshold.value)
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // Some StatDefs are not meaningful for pawns. Treat XML experiments that pick
                    // those stats as a non-match rather than logging errors during generation.
                }
            }

            return false;
        }

        private static bool MatchesCapacityBelow(List<PromptEnchantmentCapacityThreshold> thresholds, Pawn pawn)
        {
            if (thresholds == null || pawn?.health?.capacities == null)
            {
                return false;
            }

            for (int i = 0; i < thresholds.Count; i++)
            {
                PromptEnchantmentCapacityThreshold threshold = thresholds[i];
                if (threshold == null || string.IsNullOrWhiteSpace(threshold.capacityDefName))
                {
                    continue;
                }

                PawnCapacityDef capacityDef = DefDatabase<PawnCapacityDef>.GetNamedSilentFail(threshold.capacityDefName);
                if (capacityDef == null)
                {
                    continue;
                }

                try
                {
                    float level = pawn.health.capacities.GetLevel(capacityDef);
                    if (level < threshold.value && (threshold.minValue < 0f || level >= threshold.minValue))
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // Some capacity Defs can be unsuitable for unusual pawns. Bad XML should simply
                    // fail to match instead of interrupting diary generation.
                }
            }

            return false;
        }

        private static bool HasItems(List<string> values)
        {
            if (values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DefNameInList(string defName, List<string> defNames)
        {
            if (string.IsNullOrWhiteSpace(defName) || defNames == null)
            {
                return false;
            }

            for (int i = 0; i < defNames.Count; i++)
            {
                if (string.Equals(defName, defNames[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

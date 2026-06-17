// Hediff-based prompt enchantments: optional, one-shot health context chosen from live pawn
// state right before a first-person prompt is queued. XML no longer writes prompt prose here; it
// only controls which hediffs are eligible and how strongly each one is weighted.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Optional hediff-severity override for a prompt enchantment. XML authors name one of the
    /// fixed levels understood by <see cref="PromptEnchantments"/> and may retune chance/weight.
    /// </summary>
    public class PromptEnchantmentSeverityTier
    {
        public string level;

        // Defaults below zero mean "inherit the parent Def's value".
        public float chance = -1f;
        public float frequency = -1f;
        public float weight = -1f;
        public float severity = -1f;
    }

    /// <summary>
    /// XML-configured weighting rule for live hediff prompt context. The prompt text itself comes
    /// from RimWorld's hediff label, body part, urgency, and strongest live impact cues.
    /// </summary>
    public class DiaryPromptEnchantmentDef : Def
    {
        // Chance/frequency controls whether a matching hediff appears on this prompt at all.
        // "frequency" is accepted as an alias; when set to 0 or greater it overrides chance.
        public float chance = 1f;
        public float frequency = -1f;

        // Selection controls once several matching hediffs pass their chance roll.
        public float weight = 1f;
        public float severity = 1f;

        // Visible hediffs matched by defName string. String matching keeps DLC/modded names safe:
        // absent defs simply never appear on a pawn and therefore never match.
        public List<string> hediffDefNames = new List<string>();
        public float minHediffSeverity = 0f;
        public List<PromptEnchantmentSeverityTier> hediffSeverityTiers = new List<PromptEnchantmentSeverityTier>();
    }

    /// <summary>
    /// Runtime resolver for hediff prompt context. It deliberately returns only one health
    /// condition per prompt so small local models get a clear signal instead of a medical dump.
    /// </summary>
    public static class PromptEnchantments
    {
        private sealed class Candidate
        {
            public readonly Hediff hediff;
            public readonly float weight;

            public Candidate(Hediff hediff, float weight)
            {
                this.hediff = hediff;
                this.weight = weight;
            }
        }

        private sealed class MatchTuning
        {
            public readonly float chance;
            public readonly float weight;
            public readonly float severity;

            public MatchTuning(float chance, float weight, float severity)
            {
                this.chance = chance;
                this.weight = weight;
                this.severity = severity;
            }
        }

        // Fixed hediff severity bands. XML selects names; code owns the thresholds.
        private const float MinorHediffSeverity = 0.05f;
        private const float ModerateHediffSeverity = 0.25f;
        private const float MajorHediffSeverity = 0.50f;
        private const float CriticalHediffSeverity = 0.75f;
        private const int MaxImpactCues = 3;

        /// <summary>
        /// Returns one live hediff prompt for this pawn, or empty when disabled/no match.
        /// </summary>
        public static string RuleFor(Pawn pawn)
        {
            if (pawn == null || PawnDiaryMod.Settings == null || !PawnDiaryMod.Settings.enablePromptEnchantments)
            {
                return string.Empty;
            }

            if (pawn.health?.hediffSet?.hediffs == null)
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
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int hediffIndex = 0; hediffIndex < hediffs.Count; hediffIndex++)
            {
                Hediff hediff = hediffs[hediffIndex];
                if (!VisibleHediff(hediff))
                {
                    continue;
                }

                for (int defIndex = 0; defIndex < defs.Count; defIndex++)
                {
                    DiaryPromptEnchantmentDef def = defs[defIndex];
                    if (!MatchesHediff(def, hediff))
                    {
                        continue;
                    }

                    MatchTuning tuning = TuningFor(def, hediff.Severity);
                    float chance = ChanceFor(tuning.chance);
                    if (chance <= 0f || Rand.Range(0f, 1f) > chance)
                    {
                        continue;
                    }

                    float effectiveWeight = Mathf.Max(0f, tuning.weight)
                        * Mathf.Max(0f, tuning.severity)
                        * LiveSeverityWeight(hediff);
                    if (effectiveWeight <= 0f)
                    {
                        continue;
                    }

                    candidates.Add(new Candidate(hediff, effectiveWeight));
                    totalWeight += effectiveWeight;
                }
            }

            if (candidates.Count == 0 || totalWeight <= 0f)
            {
                return string.Empty;
            }

            return BuildPromptText(PickWeighted(candidates, totalWeight));
        }

        private static Candidate PickWeighted(List<Candidate> candidates, float totalWeight)
        {
            float roll = Rand.Range(0f, totalWeight);
            float cumulative = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                cumulative += candidates[i].weight;
                if (roll <= cumulative)
                {
                    return candidates[i];
                }
            }

            return candidates[candidates.Count - 1];
        }

        private static string BuildPromptText(Candidate candidate)
        {
            Hediff hediff = candidate?.hediff;
            if (hediff == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>
            {
                PromptText("PawnDiary.Prompt.Health.HighPriority"),
                CompactCondition(hediff)
            };

            AddImpactCues(parts, hediff);
            return string.Join("; ", parts.ToArray());
        }

        private static string CompactCondition(Hediff hediff)
        {
            string condition = DiaryContextBuilder.CleanLine(hediff.LabelCap);
            if (string.IsNullOrWhiteSpace(condition))
            {
                condition = DiaryContextBuilder.CleanLine(hediff.def?.label);
            }

            if (string.IsNullOrWhiteSpace(condition))
            {
                condition = PromptText("PawnDiary.Prompt.Health.ConditionFallback");
            }

            string part = hediff.Part == null ? string.Empty : DiaryContextBuilder.CleanLine(hediff.Part.LabelCap);
            if (!string.IsNullOrWhiteSpace(part))
            {
                condition = PromptText("PawnDiary.Prompt.Health.ConditionInPart", condition, part);
            }

            string intensity = HediffIntensity(hediff);
            return string.IsNullOrWhiteSpace(intensity)
                ? condition
                : PromptText("PawnDiary.Prompt.Health.IntensityCondition", intensity, condition);
        }

        private static void AddImpactCues(List<string> parts, Hediff hediff)
        {
            List<string> cues = new List<string>();
            AppendCue(cues, hediff.IsCurrentlyLifeThreatening, PromptText("PawnDiary.Prompt.Health.Cue.LifeThreatening"));
            AppendCue(cues, hediff.Bleeding, BleedingCue(hediff));
            AppendCue(cues, hediff.PainOffset > 0.05f, PainCue(hediff));
            AppendCue(cues, hediff.SummaryHealthPercentImpact < -0.05f, PromptText("PawnDiary.Prompt.Health.Cue.WeakensBody"));
            AppendCue(cues, hediff is Hediff_Addiction, PromptText("PawnDiary.Prompt.Health.Cue.AddictionPressure"));
            AppendCue(cues, hediff.def != null && hediff.def.makesSickThought, PromptText("PawnDiary.Prompt.Health.Cue.HurtsMood"));

            if (cues.Count > 0)
            {
                while (cues.Count > MaxImpactCues)
                {
                    cues.RemoveAt(cues.Count - 1);
                }

                parts.Add(string.Join(", ", cues.ToArray()));
            }
        }

        private static void AppendCue(List<string> cues, bool include, string value)
        {
            if (include && !string.IsNullOrWhiteSpace(value))
            {
                cues.Add(value);
            }
        }

        private static string BleedingCue(Hediff hediff)
        {
            return hediff.BleedRate >= 0.45f
                ? PromptText("PawnDiary.Prompt.Health.Cue.HeavyBleeding")
                : PromptText("PawnDiary.Prompt.Health.Cue.Bleeding");
        }

        private static string PainCue(Hediff hediff)
        {
            return hediff.PainOffset >= 0.20f
                ? PromptText("PawnDiary.Prompt.Health.Cue.SeverePain")
                : PromptText("PawnDiary.Prompt.Health.Cue.Pain");
        }

        private static bool VisibleHediff(Hediff hediff)
        {
            return hediff != null && hediff.def != null && hediff.Visible;
        }

        private static bool MatchesHediff(DiaryPromptEnchantmentDef def, Hediff hediff)
        {
            return def != null
                && hediff != null
                && DefNameInList(hediff.def?.defName, def.hediffDefNames)
                && hediff.Severity >= Mathf.Max(0f, def.minHediffSeverity);
        }

        private static MatchTuning TuningFor(DiaryPromptEnchantmentDef def, float hediffSeverity)
        {
            PromptEnchantmentSeverityTier tier = TierForHediffSeverity(def.hediffSeverityTiers, hediffSeverity);
            float chance = ResolvedChance(def, tier);
            float weight = tier != null && tier.weight >= 0f ? tier.weight : def.weight;
            float severity = tier != null && tier.severity >= 0f ? tier.severity : def.severity;
            return new MatchTuning(chance, weight, severity);
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

        private static float ChanceFor(float configured)
        {
            return Mathf.Clamp01(configured);
        }

        private static float LiveSeverityWeight(Hediff hediff)
        {
            if (hediff == null)
            {
                return 1f;
            }

            float weight = 1f + Mathf.Clamp(hediff.Severity, 0f, 2f) * 0.5f;
            if (hediff.IsCurrentlyLifeThreatening)
            {
                weight += 1.5f;
            }

            if (hediff.Bleeding)
            {
                weight += Mathf.Clamp(hediff.BleedRate, 0f, 2f) * 0.5f;
            }

            weight += Mathf.Clamp(hediff.PainOffset, 0f, 1f);
            weight += Mathf.Clamp(-hediff.SummaryHealthPercentImpact, 0f, 1f);
            return Mathf.Max(0.1f, weight);
        }

        private static string HediffIntensity(Hediff hediff)
        {
            if (hediff == null)
            {
                return string.Empty;
            }

            if (hediff.IsCurrentlyLifeThreatening || hediff.Severity >= CriticalHediffSeverity)
            {
                return PromptText("PawnDiary.Prompt.Health.Intensity.Critical");
            }

            if (hediff.Severity >= MajorHediffSeverity)
            {
                return PromptText("PawnDiary.Prompt.Health.Intensity.Major");
            }

            if (hediff.Severity >= ModerateHediffSeverity)
            {
                return PromptText("PawnDiary.Prompt.Health.Intensity.Moderate");
            }

            return PromptText("PawnDiary.Prompt.Health.Intensity.Minor");
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

            // These are XML tuning tokens, not prompt text. Keep them stable so Def authors do not
            // have to rewrite configs when translating the model-facing words above.
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

        private static string PromptText(string key)
        {
            return key.Translate().Resolve();
        }

        private static string PromptText(string key, string arg0, string arg1)
        {
            return key.Translate(arg0, arg1).Resolve();
        }
    }
}

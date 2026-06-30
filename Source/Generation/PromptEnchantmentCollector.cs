// Prompt enchantment collector: the impure edge for live pawn context. It reads RimWorld objects,
// XML Defs, translations, DLC-safe context helpers, and global RNG, then returns plain DTOs for the
// pure PromptEnchantmentPlanner to select and format.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Collects prompt-enchantment candidates from live pawn state. New to C#/RimWorld? See AGENTS.md
    /// ("Defs" and "DLC-safety") for why Def reads, translations, and DLC pawn data stay here.
    /// </summary>
    internal static class PromptEnchantmentCollector
    {
        // Defensive prompt budget cap for localized HediffDef.description text. Descriptions can be
        // several sentences; the prompt only needs enough game-authored context to ground the model.
        private const int HediffDescriptionMaxChars = 220;

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

        /// <summary>
        /// Returns all candidates that match the pawn and pass their XML-configured chance roll.
        /// </summary>
        public static List<PromptEnchantmentCandidate> Collect(Pawn pawn,
            List<DiaryPromptEnchantmentDef> defs, bool includeImportantEventContext,
            PromptEnchantmentTuning tuning)
        {
            List<PromptEnchantmentCandidate> candidates = new List<PromptEnchantmentCandidate>();
            if (pawn == null || defs == null || defs.Count == 0)
            {
                return candidates;
            }

            PromptEnchantmentTuning safeTuning = SafeTuning(tuning);
            List<Hediff> hediffs = pawn.health?.hediffSet?.hediffs;
            for (int defIndex = 0; defIndex < defs.Count; defIndex++)
            {
                DiaryPromptEnchantmentDef def = defs[defIndex];
                if (def == null)
                {
                    continue;
                }

                if (IsCapacitySource(def))
                {
                    AddCapacityCandidate(pawn, def, candidates, safeTuning);
                    continue;
                }

                if (IsImportantEventContextSource(def))
                {
                    AddImportantEventContextCandidate(pawn, def, includeImportantEventContext,
                        candidates, safeTuning);
                    continue;
                }

                if (hediffs == null)
                {
                    continue;
                }

                for (int hediffIndex = 0; hediffIndex < hediffs.Count; hediffIndex++)
                {
                    Hediff hediff = hediffs[hediffIndex];
                    if (!VisibleHediff(def, hediff) || !MatchesHediff(def, hediff))
                    {
                        continue;
                    }

                    AddHediffCandidate(def, hediff, candidates, safeTuning);
                }
            }

            return candidates;
        }

        private static void AddHediffCandidate(DiaryPromptEnchantmentDef def, Hediff hediff,
            List<PromptEnchantmentCandidate> candidates, PromptEnchantmentTuning tuning)
        {
            MatchTuning match = TuningFor(def, hediff.Severity, tuning);
            float chance = ChanceFor(match.chance);
            if (chance <= 0f || Rand.Range(0f, 1f) > chance)
            {
                return;
            }

            float effectiveWeight = Mathf.Max(0f, match.weight)
                * Mathf.Max(0f, match.severity)
                * LiveSeverityWeight(hediff);
            if (effectiveWeight <= 0f)
            {
                return;
            }

            List<string> configuredCues = ConfiguredCues(def, tuning);
            AppendHediffDescriptionCue(configuredCues, def, hediff);

            candidates.Add(new PromptEnchantmentCandidate
            {
                weight = effectiveWeight,
                sourceHediffDefName = hediff.def?.defName,
                priorityText = PriorityText(def),
                conditionText = HediffConditionText(def, hediff, tuning),
                impactCues = ImpactCues(hediff),
                configuredCues = configuredCues
            });
        }

        private static void AddImportantEventContextCandidate(Pawn pawn, DiaryPromptEnchantmentDef def,
            bool includeImportantEventContext, List<PromptEnchantmentCandidate> candidates,
            PromptEnchantmentTuning tuning)
        {
            if (!includeImportantEventContext)
            {
                return;
            }

            string value = ImportantEventContextValue(pawn, def);
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            float chance = ChanceFor(def.frequency >= 0f ? def.frequency : def.chance);
            if (chance <= 0f || Rand.Range(0f, 1f) > chance)
            {
                return;
            }

            float effectiveWeight = Mathf.Max(0f, def.weight) * Mathf.Max(0f, def.severity);
            if (effectiveWeight <= 0f)
            {
                return;
            }

            candidates.Add(new PromptEnchantmentCandidate
            {
                weight = effectiveWeight,
                priorityText = PriorityText(def),
                conditionText = PromptText(
                    "PawnDiary.Prompt.Context.Value",
                    ImportantEventContextLabel(def),
                    value),
                configuredCues = ConfiguredCues(def, tuning)
            });
        }

        private static void AddCapacityCandidate(Pawn pawn, DiaryPromptEnchantmentDef def,
            List<PromptEnchantmentCandidate> candidates, PromptEnchantmentTuning tuning)
        {
            PawnCapacityDef capacityDef = CapacityDefFor(def);
            if (pawn?.health?.capacities == null || capacityDef == null)
            {
                return;
            }

            float level = pawn.health.capacities.GetLevel(capacityDef);
            if (def.minCapacity >= 0f && level < def.minCapacity)
            {
                return;
            }

            if (def.maxCapacity >= 0f && level >= def.maxCapacity)
            {
                return;
            }

            float chance = ChanceFor(def.frequency >= 0f ? def.frequency : def.chance);
            if (chance <= 0f || Rand.Range(0f, 1f) > chance)
            {
                return;
            }

            float effectiveWeight = Mathf.Max(0f, def.weight)
                * Mathf.Max(0f, def.severity)
                * CapacitySeverityWeight(level);
            if (effectiveWeight <= 0f)
            {
                return;
            }

            string label = PromptTextSanitizer.OneLine(capacityDef.LabelCap.Resolve());
            candidates.Add(new PromptEnchantmentCandidate
            {
                weight = effectiveWeight,
                priorityText = PriorityText(def),
                conditionText = CapacityConditionText(def, level, label, tuning),
                configuredCues = ConfiguredCues(def, tuning)
            });
        }

        private static string PriorityText(DiaryPromptEnchantmentDef def)
        {
            return !string.IsNullOrWhiteSpace(def?.priorityKey)
                ? PromptText(def.priorityKey)
                : PromptText("PawnDiary.Prompt.Health.HighPriority");
        }

        private static string CapacityConditionText(DiaryPromptEnchantmentDef def, float level,
            string capacityLabel, PromptEnchantmentTuning tuning)
        {
            string condition = CapacityConditionLabel(def, capacityLabel);
            string intensity = CapacityIntensity(def, level, tuning);
            return string.IsNullOrWhiteSpace(intensity)
                ? condition
                : PromptText("PawnDiary.Prompt.Health.IntensityCondition", intensity, condition);
        }

        private static string CapacityConditionLabel(DiaryPromptEnchantmentDef def, string capacityLabel)
        {
            if (!string.IsNullOrWhiteSpace(def?.conditionLabel))
            {
                return def.conditionLabel;
            }

            if (!string.IsNullOrWhiteSpace(def?.conditionKey))
            {
                return PromptText(def.conditionKey);
            }

            if (!string.IsNullOrWhiteSpace(capacityLabel))
            {
                return capacityLabel;
            }

            return PromptText("PawnDiary.Prompt.Health.ConditionFallback");
        }

        private static string CapacityIntensity(DiaryPromptEnchantmentDef def, float level,
            PromptEnchantmentTuning tuning)
        {
            if (!string.IsNullOrWhiteSpace(def?.intensityKey))
            {
                return PromptText(def.intensityKey);
            }

            if (level < tuning.barelyConsciousBelow)
            {
                return PromptText("PawnDiary.Prompt.Health.Intensity.Critical");
            }

            if (level < tuning.fadingConsciousnessBelow)
            {
                return PromptText("PawnDiary.Prompt.Health.Intensity.Major");
            }

            if (level < tuning.cloudedConsciousnessBelow)
            {
                return PromptText("PawnDiary.Prompt.Health.Intensity.Moderate");
            }

            return string.Empty;
        }

        private static List<string> ConfiguredCues(DiaryPromptEnchantmentDef def,
            PromptEnchantmentTuning tuning)
        {
            List<string> cues = new List<string>();
            if (def?.cueKeys == null || def.cueKeys.Count == 0)
            {
                return cues;
            }

            int maxCues = tuning.EffectiveMaxImpactCues();
            for (int i = 0; i < def.cueKeys.Count && cues.Count < maxCues; i++)
            {
                string key = def.cueKeys[i];
                if (!string.IsNullOrWhiteSpace(key))
                {
                    string cue = PromptText(key);
                    if (!string.IsNullOrWhiteSpace(cue))
                    {
                        cues.Add(cue);
                    }
                }
            }

            return cues;
        }

        private static string HediffConditionText(DiaryPromptEnchantmentDef def, Hediff hediff,
            PromptEnchantmentTuning tuning)
        {
            bool configuredCondition = !string.IsNullOrWhiteSpace(def?.conditionLabel)
                || !string.IsNullOrWhiteSpace(def?.conditionKey);
            string condition = HediffConditionLabel(def, hediff);
            if (!configuredCondition)
            {
                string part = hediff.Part == null
                    ? string.Empty
                    : PromptTextSanitizer.LocalizedPromptText(hediff.Part.LabelCap);
                if (!string.IsNullOrWhiteSpace(part))
                {
                    condition = PromptText("PawnDiary.Prompt.Health.ConditionInPart", condition, part);
                }
            }

            string intensity = HediffIntensity(def, hediff, tuning);
            return string.IsNullOrWhiteSpace(intensity)
                ? condition
                : PromptText("PawnDiary.Prompt.Health.IntensityCondition", intensity, condition);
        }

        private static string HediffConditionLabel(DiaryPromptEnchantmentDef def, Hediff hediff)
        {
            if (!string.IsNullOrWhiteSpace(def?.conditionLabel))
            {
                return def.conditionLabel;
            }

            if (!string.IsNullOrWhiteSpace(def?.conditionKey))
            {
                return PromptText(def.conditionKey);
            }

            string condition = PromptTextSanitizer.LocalizedPromptText(hediff.LabelCap);
            if (string.IsNullOrWhiteSpace(condition))
            {
                condition = PromptTextSanitizer.LocalizedPromptText(hediff.def?.label);
            }

            if (string.IsNullOrWhiteSpace(condition))
            {
                condition = PromptText("PawnDiary.Prompt.Health.ConditionFallback");
            }

            return condition;
        }

        private static List<string> ImpactCues(Hediff hediff)
        {
            List<string> cues = new List<string>();
            AppendCue(cues, hediff.IsCurrentlyLifeThreatening,
                PromptText("PawnDiary.Prompt.Health.Cue.LifeThreatening"));
            AppendCue(cues, hediff.Bleeding, BleedingCue(hediff));
            AppendCue(cues, hediff.PainOffset > 0.05f, PainCue(hediff));
            AppendCue(cues, hediff.SummaryHealthPercentImpact < -0.05f,
                PromptText("PawnDiary.Prompt.Health.Cue.WeakensBody"));
            AppendCue(cues, hediff is Hediff_Addiction,
                PromptText("PawnDiary.Prompt.Health.Cue.AddictionPressure"));
            AppendCue(cues, hediff.def != null && hediff.def.makesSickThought,
                PromptText("PawnDiary.Prompt.Health.Cue.HurtsMood"));
            return cues;
        }

        private static void AppendHediffDescriptionCue(List<string> cues, DiaryPromptEnchantmentDef def,
            Hediff hediff)
        {
            if (cues == null || hediff?.def == null)
            {
                return;
            }

            string description = HediffDescription(def, hediff);
            if (string.IsNullOrWhiteSpace(description))
            {
                return;
            }

            if (description.Length > HediffDescriptionMaxChars)
            {
                description = TextTruncation.SafePrefix(description, HediffDescriptionMaxChars) + "...";
            }

            cues.Add(PromptText("PawnDiary.Prompt.Health.ConditionDescription", description));
        }

        private static string HediffDescription(DiaryPromptEnchantmentDef def, Hediff hediff)
        {
            if (!string.IsNullOrWhiteSpace(def?.descriptionOverrideKey))
            {
                return DiaryLineCleaner.CleanLine(PromptText(def.descriptionOverrideKey));
            }

            if (!string.IsNullOrWhiteSpace(def?.descriptionOverrideText))
            {
                return DiaryLineCleaner.CleanLine(def.descriptionOverrideText);
            }

            return PromptTextSanitizer.LocalizedPromptText(hediff.def.description);
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

        private static bool VisibleHediff(DiaryPromptEnchantmentDef def, Hediff hediff)
        {
            return hediff != null && hediff.def != null && (def == null || !def.visibleOnly || hediff.Visible);
        }

        private static bool MatchesHediff(DiaryPromptEnchantmentDef def, Hediff hediff)
        {
            return def != null
                && hediff != null
                && !IsCapacitySource(def)
                && DefNameInList(hediff.def?.defName, def.hediffDefNames)
                && hediff.Severity >= Mathf.Max(0f, def.minHediffSeverity);
        }

        private static bool IsCapacitySource(DiaryPromptEnchantmentDef def)
        {
            return def != null
                && (!string.IsNullOrWhiteSpace(def.capacityDefName)
                    || string.Equals(def.source, "Capacity", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsImportantEventContextSource(DiaryPromptEnchantmentDef def)
        {
            return IsRoyalTitleSource(def) || IsIdeologyRoleSource(def);
        }

        private static bool IsRoyalTitleSource(DiaryPromptEnchantmentDef def)
        {
            return def != null && string.Equals(def.source, "RoyalTitle", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIdeologyRoleSource(DiaryPromptEnchantmentDef def)
        {
            return def != null && string.Equals(def.source, "IdeologyRole", StringComparison.OrdinalIgnoreCase);
        }

        private static string ImportantEventContextValue(Pawn pawn, DiaryPromptEnchantmentDef def)
        {
            if (IsRoyalTitleSource(def))
            {
                return DlcContext.RoyalTitle(pawn);
            }

            if (IsIdeologyRoleSource(def))
            {
                return DlcContext.IdeologicalRole(pawn);
            }

            return string.Empty;
        }

        private static string ImportantEventContextLabel(DiaryPromptEnchantmentDef def)
        {
            if (!string.IsNullOrWhiteSpace(def?.conditionLabel))
            {
                return def.conditionLabel;
            }

            if (!string.IsNullOrWhiteSpace(def?.conditionKey))
            {
                return PromptText(def.conditionKey);
            }

            return IsRoyalTitleSource(def)
                ? PromptText("PawnDiary.Prompt.Context.RoyalTitle")
                : PromptText("PawnDiary.Prompt.Context.IdeologyRole");
        }

        private static PawnCapacityDef CapacityDefFor(DiaryPromptEnchantmentDef def)
        {
            if (def == null)
            {
                return null;
            }

            string defName = string.IsNullOrWhiteSpace(def.capacityDefName)
                ? "Consciousness"
                : def.capacityDefName;
            if (string.Equals(defName, "Consciousness", StringComparison.OrdinalIgnoreCase)
                && PawnCapacityDefOf.Consciousness != null)
            {
                return PawnCapacityDefOf.Consciousness;
            }

            return DefDatabase<PawnCapacityDef>.GetNamedSilentFail(defName);
        }

        private static float CapacitySeverityWeight(float level)
        {
            return 1f + Mathf.Clamp01(1f - level) * 2f;
        }

        private static MatchTuning TuningFor(DiaryPromptEnchantmentDef def, float hediffSeverity,
            PromptEnchantmentTuning tuning)
        {
            PromptEnchantmentSeverityTier tier = TierForHediffSeverity(
                def.hediffSeverityTiers,
                hediffSeverity,
                tuning);
            float chance = ResolvedChance(def, tier);
            float weight = tier != null && tier.weight >= 0f ? tier.weight : def.weight;
            float severity = tier != null && tier.severity >= 0f ? tier.severity : def.severity;
            return new MatchTuning(chance, weight, severity);
        }

        private static float ResolvedChance(DiaryPromptEnchantmentDef def,
            PromptEnchantmentSeverityTier tier)
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
            if (float.IsNaN(configured))
            {
                return 0f;
            }

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

        private static string HediffIntensity(DiaryPromptEnchantmentDef def, Hediff hediff,
            PromptEnchantmentTuning tuning)
        {
            if (!string.IsNullOrWhiteSpace(def?.intensityKey))
            {
                return PromptText(def.intensityKey);
            }

            if (hediff == null)
            {
                return string.Empty;
            }

            if (hediff.IsCurrentlyLifeThreatening || hediff.Severity >= tuning.criticalHediffSeverity)
            {
                return PromptText("PawnDiary.Prompt.Health.Intensity.Critical");
            }

            if (hediff.Severity >= tuning.majorHediffSeverity)
            {
                return PromptText("PawnDiary.Prompt.Health.Intensity.Major");
            }

            if (hediff.Severity >= tuning.moderateHediffSeverity)
            {
                return PromptText("PawnDiary.Prompt.Health.Intensity.Moderate");
            }

            return PromptText("PawnDiary.Prompt.Health.Intensity.Minor");
        }

        private static PromptEnchantmentSeverityTier TierForHediffSeverity(
            List<PromptEnchantmentSeverityTier> tiers, float hediffSeverity, PromptEnchantmentTuning tuning)
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
                if (!TrySeverityLevelThreshold(tier.level, tuning, out threshold))
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

        private static bool TrySeverityLevelThreshold(string level, PromptEnchantmentTuning tuning,
            out float threshold)
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
                threshold = tuning.minorHediffSeverity;
                return true;
            }

            if (string.Equals(level, "moderate", StringComparison.OrdinalIgnoreCase))
            {
                threshold = tuning.moderateHediffSeverity;
                return true;
            }

            if (string.Equals(level, "major", StringComparison.OrdinalIgnoreCase)
                || string.Equals(level, "severe", StringComparison.OrdinalIgnoreCase))
            {
                threshold = tuning.majorHediffSeverity;
                return true;
            }

            if (string.Equals(level, "critical", StringComparison.OrdinalIgnoreCase)
                || string.Equals(level, "extreme", StringComparison.OrdinalIgnoreCase))
            {
                threshold = tuning.criticalHediffSeverity;
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

        private static PromptEnchantmentTuning SafeTuning(PromptEnchantmentTuning tuning)
        {
            return tuning ?? new PromptEnchantmentTuning();
        }

        private static string PromptText(string key)
        {
            return key.Translate().Resolve();
        }

        private static string PromptText(string key, string arg0)
        {
            return key.Translate(arg0).Resolve();
        }

        private static string PromptText(string key, string arg0, string arg1)
        {
            return key.Translate(arg0, arg1).Resolve();
        }
    }
}

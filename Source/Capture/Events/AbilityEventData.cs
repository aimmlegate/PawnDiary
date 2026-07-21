// Payload + pure decision for a pawn using a RimWorld Ability. The live hook supplies primitive
// facts from Ability.Activate; this class owns the cooldown-weighted sampling decision and context
// string format so ability spam and exact downstream-owned routes can be filtered without touching
// RimWorld state in tests.
using System;
using System.Globalization;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one successful ability activation.
    /// </summary>
    internal class AbilityEventData : DiaryEventData
    {
        public override DiaryEventType EventType => DiaryEventType.Ability;

        public string DefName;
        public string Label;
        public string Category;
        public string TargetLabel;
        public int CooldownTicks;
        public float RecordChance;
        public float Roll;
        public bool DownstreamCovered;

        /// <summary>
        /// Pure decision for one ability use. Callers precompute the cooldown-weighted chance and a
        /// random roll because randomness and Def access belong at the game edge.
        /// </summary>
        public static CaptureDecision Decide(AbilityEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.DefName))
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled || !ctx.SignalEnabled)
            {
                return CaptureDecision.Drop;
            }

            // Exact XML policy proves that a later visible interaction/ritual already owns this
            // event. Drop before sampling so a redundant route does not advance the global RNG.
            if (data.DownstreamCovered)
            {
                return CaptureDecision.Drop;
            }

            if (data.Roll > Clamp01(data.RecordChance))
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSolo;
        }

        /// <summary>
        /// Converts cooldown length into a sample chance. Faster cooldowns get lower probability;
        /// longer cooldowns asymptotically approach <paramref name="maxChance"/>.
        /// </summary>
        public static float CooldownWeightedChance(
            int cooldownTicks,
            float minChance,
            float maxChance,
            int referenceCooldownTicks)
        {
            minChance = Clamp01(minChance);
            maxChance = Clamp01(maxChance);
            if (maxChance < minChance)
            {
                float swap = maxChance;
                maxChance = minChance;
                minChance = swap;
            }

            if (referenceCooldownTicks <= 0)
            {
                referenceCooldownTicks = 1;
            }

            int safeCooldown = Math.Max(0, cooldownTicks);
            float ratio = safeCooldown / (float)(safeCooldown + referenceCooldownTicks);
            return minChance + ((maxChance - minChance) * ratio);
        }

        /// <summary>
        /// Pure assembly of the ability-use context marker. The leading "ability=" marker is
        /// load-bearing for domain classification.
        /// </summary>
        public static string BuildGameContext(
            string defName,
            string label,
            string category,
            string targetLabel,
            int cooldownTicks,
            float recordChance)
        {
            string context = "ability=" + Clean(defName)
                + "; ability_label=" + Fallback(label, defName)
                + "; ability_category=" + Fallback(category, "unknown")
                + "; ability_cooldown_ticks=" + Math.Max(0, cooldownTicks).ToString(CultureInfo.InvariantCulture)
                + "; ability_record_chance=" + Clamp01(recordChance).ToString("0.###", CultureInfo.InvariantCulture);

            string cleanTarget = Clean(targetLabel);
            if (!string.IsNullOrWhiteSpace(cleanTarget))
            {
                context += "; ability_target=" + cleanTarget;
            }

            return context;
        }

        private static string Fallback(string value, string fallback)
        {
            string clean = Clean(value);
            return string.IsNullOrWhiteSpace(clean) ? Clean(fallback) : clean;
        }

        private static string Clean(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}

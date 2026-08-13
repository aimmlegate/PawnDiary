// Payload + pure decision for a pawn using a RimWorld Ability. The live hook supplies primitive
// facts from Ability.Activate; this class owns cooldown-weighted native-chance math, semantic
// downstream ownership, and context formatting without touching RimWorld state in tests. The shared
// dispatcher owns the final frequency sample after this reducer accepts the event.
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
        public bool DownstreamCovered;

        /// <summary>
        /// Pure semantic decision for one ability use. Callers precompute the cooldown-weighted native
        /// chance for the shared frequency gate because randomness and Def access belong at the game edge.
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

            // Exact XML policy proves that a later visible downstream event already owns this event.
            // Drop here so the dispatcher never reaches its isolated shared frequency draw.
            if (data.DownstreamCovered)
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
            // These values come from Def labels and target display text, including content supplied by
            // other mods. Keep each value inside its own `; key=value` field: semicolons/newlines cannot
            // open another field, and equals signs cannot imitate another assignment inside the value.
            return GameContextValue.Sanitize(value);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}

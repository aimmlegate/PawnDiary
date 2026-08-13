// Pure frequency resolution for diary-page candidates. The game edge freezes a group key, its XML
// tier, the source's native capture chance, and one isolated random roll; this file combines those
// plain values without touching Verse, settings, DefDatabase, or RimWorld's global RNG. Keeping the
// decision here makes preset migration, UI previews, and later source integration use one contract.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Stable XML tokens understood by every shipped frequency preset.</summary>
    internal static class DiaryFrequencyTiers
    {
        public const string Essential = "essential";
        public const string Significant = "significant";
        public const string Routine = "routine";
        public const string Ambient = "ambient";

        /// <summary>Returns one canonical tier token, or an empty string for unknown input.</summary>
        public static string Normalize(string value)
        {
            string token = (value ?? string.Empty).Trim();
            if (string.Equals(token, Essential, StringComparison.OrdinalIgnoreCase))
            {
                return Essential;
            }

            if (string.Equals(token, Significant, StringComparison.OrdinalIgnoreCase))
            {
                return Significant;
            }

            if (string.Equals(token, Routine, StringComparison.OrdinalIgnoreCase))
            {
                return Routine;
            }

            if (string.Equals(token, Ambient, StringComparison.OrdinalIgnoreCase))
            {
                return Ambient;
            }

            return string.Empty;
        }

        /// <summary>True only for the four stable tier tokens above.</summary>
        public static bool IsKnown(string value)
        {
            return Normalize(value).Length > 0;
        }
    }

    /// <summary>
    /// Detached copy of one XML preset. Dictionaries are keyed by stable tier/group tokens; callers
    /// may leave either dictionary null and the resolver will reproduce Standard's 1x behavior.
    /// </summary>
    internal sealed class DiaryFrequencyPresetSnapshot
    {
        public string presetKey = string.Empty;
        public IDictionary<string, float> tierMultipliers =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public IDictionary<string, float> groupOverrides =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Plain group identity used when deciding whether sparse player overrides are Custom.</summary>
    internal sealed class DiaryFrequencyGroupSnapshot
    {
        public string groupKey = string.Empty;
        public string frequencyTier = string.Empty;
    }

    /// <summary>Everything needed to decide one candidate after semantic eligibility has been frozen.</summary>
    internal sealed class DiaryFrequencyRequest
    {
        public string groupKey = string.Empty;
        public string frequencyTier = string.Empty;
        public float nativeCaptureChance = 1f;
        public DiaryFrequencyPresetSnapshot preset;
        public bool hasPlayerOverride;
        public float playerOverride = 1f;
        public bool enabled = true;
        public bool bypassFrequency;
        public float roll;
        // Most legacy chance gates accept equality. Deterministic upstream samplers such as Social
        // Reflection historically used a strict comparison and can preserve it explicitly.
        public bool strictRollBoundary;
    }

    /// <summary>Typed reason returned by <see cref="DiaryFrequencyPolicy.Decide"/>.</summary>
    internal enum DiaryFrequencyDecisionReason
    {
        Invalid = 0,
        Disabled = 1,
        AcceptedBypass = 2,
        Accepted = 3,
        RejectedByFrequency = 4
    }

    /// <summary>Frozen result of one frequency decision, including values useful for diagnostics.</summary>
    internal sealed class DiaryFrequencyDecision
    {
        public DiaryFrequencyDecisionReason reason;
        public float multiplier;
        public float effectiveChance;

        public bool Accepted => reason == DiaryFrequencyDecisionReason.Accepted
            || reason == DiaryFrequencyDecisionReason.AcceptedBypass;
    }

    /// <summary>
    /// Resolves XML preset policy, optional player overrides, and an injected random roll. All
    /// tunable values arrive in snapshots; the only hard cap is a defensive corruption boundary.
    /// </summary>
    internal static class DiaryFrequencyPolicy
    {
        public const float StandardMultiplier = 1f;
        public const float MaximumMultiplier = 5f;

        /// <summary>
        /// Computes the final bounded probability without drawing randomness. Runtime adapters use
        /// this to avoid consuming even an isolated RNG value for deterministic 0x/1x outcomes.
        /// </summary>
        public static bool TryCalculateEffectiveChance(
            float nativeCaptureChance,
            float multiplier,
            out float effectiveChance)
        {
            effectiveChance = 0f;
            if (!IsFinite(nativeCaptureChance) || !IsFinite(multiplier))
            {
                return false;
            }

            effectiveChance = Clamp(nativeCaptureChance * multiplier, 0f, 1f);
            return true;
        }

        /// <summary>
        /// Resolves one preset multiplier. Precedence is exact group override, known tier multiplier,
        /// then the safe Standard fallback. An exact group override remains useful for a future or
        /// third-party tier token; an unknown tier must never make Lite thin that group accidentally.
        /// </summary>
        public static float ResolvePresetMultiplier(
            DiaryFrequencyPresetSnapshot preset,
            string groupKey,
            string frequencyTier)
        {
            float value;
            if (preset != null
                && TryGet(preset.groupOverrides, groupKey, out value)
                && TryNormalizeMultiplier(value, out value))
            {
                return value;
            }

            string tier = DiaryFrequencyTiers.Normalize(frequencyTier);
            if (tier.Length > 0
                && preset != null
                && TryGet(preset.tierMultipliers, tier, out value)
                && TryNormalizeMultiplier(value, out value))
            {
                return value;
            }

            return StandardMultiplier;
        }

        /// <summary>
        /// Applies a sparse player override to the inherited preset value. Corrupt non-finite values
        /// are ignored; finite out-of-range values are clamped to the supported defensive range.
        /// </summary>
        public static float ResolveEffectiveMultiplier(
            DiaryFrequencyPresetSnapshot preset,
            string groupKey,
            string frequencyTier,
            bool hasPlayerOverride,
            float playerOverride)
        {
            float inherited = ResolvePresetMultiplier(preset, groupKey, frequencyTier);
            float normalized;
            return hasPlayerOverride && TryNormalizeMultiplier(playerOverride, out normalized)
                ? normalized
                : inherited;
        }

        /// <summary>
        /// Returns true when at least one known group's valid saved override differs from its selected
        /// preset. Unknown future group keys and corrupt values stay preserved by persistence later,
        /// but do not make today's UI claim that a known row is Custom.
        /// </summary>
        public static bool HasCustomOverrides(
            IDictionary<string, float> playerOverrides,
            IEnumerable<DiaryFrequencyGroupSnapshot> groups,
            DiaryFrequencyPresetSnapshot preset)
        {
            if (playerOverrides == null || playerOverrides.Count == 0 || groups == null)
            {
                return false;
            }

            foreach (DiaryFrequencyGroupSnapshot group in groups)
            {
                if (group == null || string.IsNullOrWhiteSpace(group.groupKey))
                {
                    continue;
                }

                float raw;
                float normalized;
                if (!TryGet(playerOverrides, group.groupKey, out raw)
                    || !TryNormalizeMultiplier(raw, out normalized))
                {
                    continue;
                }

                float inherited = ResolvePresetMultiplier(
                    preset,
                    group.groupKey,
                    group.frequencyTier);
                if (Math.Abs(normalized - inherited) > 0.0001f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Decides one page candidate. The hard enable switch wins over a frequency bypass. Forced,
        /// manual, and External callers therefore bypass only this probability gate, never a semantic
        /// rejection that has already disabled the group.
        /// </summary>
        public static DiaryFrequencyDecision Decide(DiaryFrequencyRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.groupKey))
            {
                return Result(DiaryFrequencyDecisionReason.Invalid, StandardMultiplier, 0f);
            }

            float multiplier = ResolveEffectiveMultiplier(
                request.preset,
                request.groupKey,
                request.frequencyTier,
                request.hasPlayerOverride,
                request.playerOverride);

            if (!request.enabled)
            {
                return Result(DiaryFrequencyDecisionReason.Disabled, multiplier, 0f);
            }

            if (request.bypassFrequency)
            {
                return Result(DiaryFrequencyDecisionReason.AcceptedBypass, multiplier, 1f);
            }

            if (!IsFinite(request.nativeCaptureChance) || !IsFinite(request.roll))
            {
                return Result(DiaryFrequencyDecisionReason.Invalid, multiplier, 0f);
            }

            // Multiply first, then clamp the final probability. A source may deliberately expose a
            // native value above 1 before a reducing preset is applied; pre-clamping it would turn
            // native=2 with Lite=0.3 into 0.3 instead of the contract's correct 0.6.
            float effectiveChance;
            if (!TryCalculateEffectiveChance(
                request.nativeCaptureChance,
                multiplier,
                out effectiveChance))
            {
                return Result(DiaryFrequencyDecisionReason.Invalid, multiplier, 0f);
            }
            float roll = Clamp(request.roll, 0f, 1f);
            // A zero probability is always closed. Without this guard, the legacy inclusive
            // comparison would accept the singular roll==0 boundary even though the caller
            // explicitly requested that this source never be admitted.
            if (effectiveChance <= 0f)
            {
                return Result(
                    DiaryFrequencyDecisionReason.RejectedByFrequency,
                    multiplier,
                    effectiveChance);
            }

            return Result(
                (request.strictRollBoundary ? roll < effectiveChance : roll <= effectiveChance)
                    ? DiaryFrequencyDecisionReason.Accepted
                    : DiaryFrequencyDecisionReason.RejectedByFrequency,
                multiplier,
                effectiveChance);
        }

        private static DiaryFrequencyDecision Result(
            DiaryFrequencyDecisionReason reason,
            float multiplier,
            float chance)
        {
            return new DiaryFrequencyDecision
            {
                reason = reason,
                multiplier = multiplier,
                effectiveChance = chance
            };
        }

        private static bool TryNormalizeMultiplier(float value, out float normalized)
        {
            if (!IsFinite(value))
            {
                normalized = StandardMultiplier;
                return false;
            }

            normalized = Clamp(value, 0f, MaximumMultiplier);
            return true;
        }

        private static bool TryGet(
            IDictionary<string, float> values,
            string key,
            out float value)
        {
            value = 0f;
            if (values == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (values.TryGetValue(key, out value))
            {
                return true;
            }

            // A caller can supply an Ordinal dictionary even though XML snapshots use an
            // OrdinalIgnoreCase one. Scan defensively so the pure contract has stable semantics.
            foreach (KeyValuePair<string, float> pair in values)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            return false;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}

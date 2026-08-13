// Pure normalization and one-time migration for global diary-frequency settings. RimWorld's
// settings adapter supplies only detached group facts and raw legacy-key presence; this file maps
// that old intent into sparse per-group multipliers without reading Defs, Scribe, Unity, or Verse.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Plain description of one known non-External group for legacy migration. The adapter includes
    /// dormant package-gated rows so intent survives a later add-on install; the three affected flags
    /// mirror the only paths the retired global/Work/Social sliders ever changed.
    /// </summary>
    internal sealed class DiaryFrequencyMigrationGroupSnapshot
    {
        public string groupKey = string.Empty;
        public string frequencyTier = string.Empty;
        public bool affectedByWorkWeight;
        public bool affectedByAbilityWeight;
        public bool affectedByInteractionPromotionWeight;
    }

    /// <summary>
    /// Raw old-setting values plus explicit presence bits. Presence cannot be inferred from NaN:
    /// hand-edited settings may contain NaN and still need defensive normalization.
    /// </summary>
    internal sealed class DiaryFrequencyLegacySettingsSnapshot
    {
        public bool hasGenerationChanceWeight;
        public float generationChanceWeight;
        public bool hasWorkGenerationWeight;
        public float workGenerationWeight;
        public bool hasSocialGenerationWeight;
        public float socialGenerationWeight;
    }

    /// <summary>Detached migration result applied by <c>PawnDiarySettings</c>.</summary>
    internal sealed class DiaryFrequencyMigrationResult
    {
        public readonly Dictionary<string, float> groupOverrides =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public bool hasMigratedCustomIntent;
    }

    /// <summary>
    /// Owns settings-only frequency migration and sparse-map cleanup. Runtime admission remains in
    /// <see cref="DiaryFrequencyPolicy"/>; keeping this separate prevents persistence concerns from
    /// leaking into capture decisions.
    /// </summary>
    internal static class DiaryFrequencySettingsPolicy
    {
        private const float EqualityTolerance = 0.0001f;

        /// <summary>
        /// Maps the retired settings to only the group families they historically affected. The newer
        /// unified key wins when present. For the older split keys, Ability inherits their compatibility
        /// average while Work and interaction promotion retain their own side of the split.
        /// </summary>
        public static DiaryFrequencyMigrationResult MigrateLegacy(
            DiaryFrequencyLegacySettingsSnapshot legacy,
            IEnumerable<DiaryFrequencyMigrationGroupSnapshot> groups)
        {
            DiaryFrequencyMigrationResult result = new DiaryFrequencyMigrationResult();
            if (legacy == null || groups == null)
            {
                return result;
            }

            float unified = NormalizeMultiplier(
                legacy.generationChanceWeight,
                DiaryFrequencyPolicy.StandardMultiplier);
            float work = legacy.hasWorkGenerationWeight
                ? NormalizeMultiplier(
                    legacy.workGenerationWeight,
                    DiaryFrequencyPolicy.StandardMultiplier)
                : DiaryFrequencyPolicy.StandardMultiplier;
            float social = legacy.hasSocialGenerationWeight
                ? NormalizeMultiplier(
                    legacy.socialGenerationWeight,
                    DiaryFrequencyPolicy.StandardMultiplier)
                : DiaryFrequencyPolicy.StandardMultiplier;
            float ability = NormalizeMultiplier(
                (work + social) * 0.5f,
                DiaryFrequencyPolicy.StandardMultiplier);

            foreach (DiaryFrequencyMigrationGroupSnapshot group in groups)
            {
                if (group == null || string.IsNullOrWhiteSpace(group.groupKey))
                {
                    continue;
                }

                bool affected = group.affectedByWorkWeight
                    || group.affectedByAbilityWeight
                    || group.affectedByInteractionPromotionWeight;
                if (!affected)
                {
                    continue;
                }

                float migratedValue;
                if (legacy.hasGenerationChanceWeight)
                {
                    migratedValue = unified;
                }
                else if (group.affectedByWorkWeight)
                {
                    migratedValue = work;
                }
                else if (group.affectedByAbilityWeight)
                {
                    migratedValue = ability;
                }
                else
                {
                    migratedValue = social;
                }

                if (NearlyEqual(migratedValue, DiaryFrequencyPolicy.StandardMultiplier))
                {
                    continue;
                }

                string key = group.groupKey.Trim();
                if (!result.groupOverrides.ContainsKey(key))
                {
                    result.groupOverrides[key] = migratedValue;
                }
            }

            result.hasMigratedCustomIntent = result.groupOverrides.Count > 0;
            return result;
        }

        /// <summary>
        /// Returns a detached, case-insensitive sparse map. Known rows equal to their selected preset
        /// are removed; unknown nonblank keys are retained for forward compatibility but are never
        /// considered by the known-row Custom detector.
        /// </summary>
        public static Dictionary<string, float> NormalizeOverrides(
            IDictionary<string, float> source,
            IEnumerable<DiaryFrequencyGroupSnapshot> knownGroups,
            DiaryFrequencyPresetSnapshot preset)
        {
            Dictionary<string, DiaryFrequencyGroupSnapshot> known =
                new Dictionary<string, DiaryFrequencyGroupSnapshot>(StringComparer.OrdinalIgnoreCase);
            if (knownGroups != null)
            {
                foreach (DiaryFrequencyGroupSnapshot group in knownGroups)
                {
                    string key = (group?.groupKey ?? string.Empty).Trim();
                    if (key.Length > 0 && !known.ContainsKey(key))
                    {
                        known[key] = group;
                    }
                }
            }

            Dictionary<string, float> normalized =
                new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (source == null)
            {
                return normalized;
            }

            foreach (KeyValuePair<string, float> entry in source)
            {
                string rawKey = (entry.Key ?? string.Empty).Trim();
                if (rawKey.Length == 0)
                {
                    continue;
                }

                DiaryFrequencyGroupSnapshot group;
                bool isKnown = known.TryGetValue(rawKey, out group);
                string storedKey = isKnown ? group.groupKey.Trim() : rawKey;
                if (normalized.ContainsKey(storedKey))
                {
                    // A corrupted XML dictionary can contain case variants. First wins, matching
                    // the preset Def adapter and avoiding load-order-dependent replacement.
                    continue;
                }

                float inherited = isKnown
                    ? DiaryFrequencyPolicy.ResolvePresetMultiplier(
                        preset,
                        group.groupKey,
                        group.frequencyTier)
                    : DiaryFrequencyPolicy.StandardMultiplier;
                float value = NormalizeMultiplier(entry.Value, inherited);
                if (isKnown && NearlyEqual(value, inherited))
                {
                    continue;
                }

                normalized[storedKey] = value;
            }

            return normalized;
        }

        /// <summary>
        /// Converts corrupt/out-of-range persisted values to the supported 0x..5x interval. NaN uses
        /// the supplied inherited value; infinities clamp to the matching boundary.
        /// </summary>
        public static float NormalizeMultiplier(float value, float inheritedValue)
        {
            float fallback = IsFinite(inheritedValue)
                ? Clamp(inheritedValue)
                : DiaryFrequencyPolicy.StandardMultiplier;
            if (float.IsNaN(value))
            {
                return fallback;
            }

            if (float.IsPositiveInfinity(value))
            {
                return DiaryFrequencyPolicy.MaximumMultiplier;
            }

            if (float.IsNegativeInfinity(value))
            {
                return 0f;
            }

            return Clamp(value);
        }

        /// <summary>True when two normalized settings values have no meaningful UI difference.</summary>
        public static bool NearlyEqual(float left, float right)
        {
            return Math.Abs(left - right) <= EqualityTolerance;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float Clamp(float value)
        {
            return Math.Max(0f, Math.Min(DiaryFrequencyPolicy.MaximumMultiplier, value));
        }
    }
}

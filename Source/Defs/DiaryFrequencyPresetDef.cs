// XML boundary for diary-frequency presets. RimWorld owns these Def objects; this adapter copies one
// selected preset into the plain DiaryFrequencyPresetSnapshot consumed by the pure policy. No live
// Def or settings object crosses that boundary, and a missing/unknown preset safely becomes Standard.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>One XML tier-to-multiplier row inside a frequency preset.</summary>
    public class DiaryFrequencyTierMultiplier
    {
        public string tier;
        public float multiplier = DiaryFrequencyPolicy.StandardMultiplier;
    }

    /// <summary>One exact group-key override inside a frequency preset.</summary>
    public class DiaryFrequencyGroupMultiplier
    {
        public string groupKey;
        public float multiplier = DiaryFrequencyPolicy.StandardMultiplier;
    }

    /// <summary>
    /// XML-owned frequency policy. <see cref="Def.label"/> and <see cref="Def.description"/> provide
    /// the localized player-facing name and explanation inherited by every ordinary RimWorld Def.
    /// </summary>
    public class DiaryFrequencyPresetDef : Def
    {
        public List<DiaryFrequencyTierMultiplier> tierMultipliers;
        public List<DiaryFrequencyGroupMultiplier> groupOverrides;
    }

    /// <summary>Finds preset Defs and freezes them into the pure frequency contract.</summary>
    internal static class DiaryFrequencyPresets
    {
        public const string LiteDefName = "PawnDiary_Frequency_Lite";
        public const string StandardDefName = "PawnDiary_Frequency_Standard";
        public const string FrequentDefName = "PawnDiary_Frequency_Frequent";

        /// <summary>
        /// Resolves a selected Def name. Unknown tokens use the shipped Standard row; if XML failed
        /// to load altogether, a built-in all-1x snapshot keeps existing behavior intact.
        /// </summary>
        public static DiaryFrequencyPresetSnapshot Snapshot(string presetDefName)
        {
            DiaryFrequencyPresetDef source = null;
            if (!string.IsNullOrWhiteSpace(presetDefName))
            {
                source = DefDatabase<DiaryFrequencyPresetDef>.GetNamedSilentFail(presetDefName.Trim());
            }

            if (source == null)
            {
                source = DefDatabase<DiaryFrequencyPresetDef>.GetNamedSilentFail(StandardDefName);
            }

            return source == null ? StandardFallback() : Snapshot(source);
        }

        /// <summary>Copies one loaded Def without retaining mutable list-row references.</summary>
        public static DiaryFrequencyPresetSnapshot Snapshot(DiaryFrequencyPresetDef source)
        {
            if (source == null)
            {
                return StandardFallback();
            }

            DiaryFrequencyPresetSnapshot snapshot = new DiaryFrequencyPresetSnapshot
            {
                presetKey = source.defName ?? string.Empty
            };

            CopyTiers(source.tierMultipliers, snapshot.tierMultipliers);
            CopyGroups(source.groupOverrides, snapshot.groupOverrides);
            return snapshot;
        }

        private static void CopyTiers(
            List<DiaryFrequencyTierMultiplier> rows,
            IDictionary<string, float> destination)
        {
            if (rows == null)
            {
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                DiaryFrequencyTierMultiplier row = rows[i];
                string tier = DiaryFrequencyTiers.Normalize(row?.tier);
                if (tier.Length > 0 && !destination.ContainsKey(tier))
                {
                    destination[tier] = row.multiplier;
                }
            }
        }

        private static void CopyGroups(
            List<DiaryFrequencyGroupMultiplier> rows,
            IDictionary<string, float> destination)
        {
            if (rows == null)
            {
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                DiaryFrequencyGroupMultiplier row = rows[i];
                string key = (row?.groupKey ?? string.Empty).Trim();
                if (key.Length > 0 && !destination.ContainsKey(key))
                {
                    destination[key] = row.multiplier;
                }
            }
        }

        private static DiaryFrequencyPresetSnapshot StandardFallback()
        {
            DiaryFrequencyPresetSnapshot snapshot = new DiaryFrequencyPresetSnapshot
            {
                presetKey = StandardDefName
            };
            snapshot.tierMultipliers[DiaryFrequencyTiers.Essential] = 1f;
            snapshot.tierMultipliers[DiaryFrequencyTiers.Significant] = 1f;
            snapshot.tierMultipliers[DiaryFrequencyTiers.Routine] = 1f;
            snapshot.tierMultipliers[DiaryFrequencyTiers.Ambient] = 1f;
            return snapshot;
        }
    }
}

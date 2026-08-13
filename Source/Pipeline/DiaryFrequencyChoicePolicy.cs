// Pure display policy for the compact named frequency choices shown on the Events settings tab.
// XML-backed Defs are copied into these plain snapshots at the RimWorld boundary; this file owns
// ordering and display-band selection without reading DefDatabase, translated text, or GUI state.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Detached numeric policy for one player-facing frequency choice.</summary>
    internal sealed class DiaryFrequencyChoiceSnapshot
    {
        public string choiceKey = string.Empty;
        public float multiplier = DiaryFrequencyPolicy.StandardMultiplier;
        public float displayMaxMultiplier = DiaryFrequencyPolicy.StandardMultiplier;
        public int order;
    }

    /// <summary>Normalizes XML-owned frequency choices and maps exact values to named display bands.</summary>
    internal static class DiaryFrequencyChoicePolicy
    {
        /// <summary>
        /// Returns valid detached rows in player-facing order. Duplicate keys use the first row, which
        /// matches the other frequency XML adapters and avoids load-order-dependent replacement.
        /// </summary>
        public static List<DiaryFrequencyChoiceSnapshot> NormalizeForMenu(
            IEnumerable<DiaryFrequencyChoiceSnapshot> source)
        {
            List<DiaryFrequencyChoiceSnapshot> result = new List<DiaryFrequencyChoiceSnapshot>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (source == null)
            {
                return result;
            }

            foreach (DiaryFrequencyChoiceSnapshot row in source)
            {
                string key = (row?.choiceKey ?? string.Empty).Trim();
                if (key.Length == 0 || seen.Contains(key)
                    || !IsFinite(row.multiplier) || !IsFinite(row.displayMaxMultiplier)
                    || row.multiplier < 0f || row.multiplier > DiaryFrequencyPolicy.MaximumMultiplier
                    || row.displayMaxMultiplier < 0f
                    || row.displayMaxMultiplier > DiaryFrequencyPolicy.MaximumMultiplier)
                {
                    continue;
                }

                seen.Add(key);
                result.Add(new DiaryFrequencyChoiceSnapshot
                {
                    choiceKey = key,
                    multiplier = row.multiplier,
                    displayMaxMultiplier = row.displayMaxMultiplier,
                    order = row.order
                });
            }

            result.Sort(CompareMenuOrder);
            return result;
        }

        /// <summary>
        /// Finds the named display band containing an effective multiplier. Bands use their XML-owned
        /// inclusive maximums, independent of menu order; values above every band use the highest band.
        /// </summary>
        public static DiaryFrequencyChoiceSnapshot ChoiceForMultiplier(
            IEnumerable<DiaryFrequencyChoiceSnapshot> choices,
            float effectiveMultiplier)
        {
            List<DiaryFrequencyChoiceSnapshot> valid = NormalizeForMenu(choices);
            if (valid.Count == 0)
            {
                return null;
            }

            float value = NormalizeMultiplier(effectiveMultiplier);
            DiaryFrequencyChoiceSnapshot best = null;
            DiaryFrequencyChoiceSnapshot highest = null;
            for (int i = 0; i < valid.Count; i++)
            {
                DiaryFrequencyChoiceSnapshot candidate = valid[i];
                if (highest == null
                    || candidate.displayMaxMultiplier > highest.displayMaxMultiplier
                    || (Math.Abs(candidate.displayMaxMultiplier - highest.displayMaxMultiplier) <= 0.0001f
                        && CompareMenuOrder(candidate, highest) < 0))
                {
                    highest = candidate;
                }

                if (value <= candidate.displayMaxMultiplier
                    && (best == null
                        || candidate.displayMaxMultiplier < best.displayMaxMultiplier
                        || (Math.Abs(candidate.displayMaxMultiplier - best.displayMaxMultiplier) <= 0.0001f
                            && CompareMenuOrder(candidate, best) < 0)))
                {
                    best = candidate;
                }
            }

            return best ?? highest;
        }

        private static int CompareMenuOrder(
            DiaryFrequencyChoiceSnapshot left,
            DiaryFrequencyChoiceSnapshot right)
        {
            int order = left.order.CompareTo(right.order);
            return order != 0
                ? order
                : string.Compare(left.choiceKey, right.choiceKey, StringComparison.OrdinalIgnoreCase);
        }

        private static float NormalizeMultiplier(float value)
        {
            if (float.IsNaN(value))
            {
                return DiaryFrequencyPolicy.StandardMultiplier;
            }

            if (float.IsPositiveInfinity(value))
            {
                return DiaryFrequencyPolicy.MaximumMultiplier;
            }

            if (float.IsNegativeInfinity(value))
            {
                return 0f;
            }

            return Math.Max(0f, Math.Min(DiaryFrequencyPolicy.MaximumMultiplier, value));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}

// Pure selection helper for API lanes. The game-facing code owns live endpoint objects, cooldown
// state, and logging; this file only turns "how many lanes exist, which are ready, and what routing
// mode did the player choose?" into a stable primary-lane index that tests can cover without
// RimWorld assemblies.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Player-facing strategy for choosing the primary API lane before failover.
    /// </summary>
    public enum ApiLaneRoutingMode
    {
        Balanced,
        PreferTopRows,
        FailoverOnly
    }

    /// <summary>
    /// Deterministic, testable lane-selection rules for API routing.
    /// </summary>
    internal static class ApiLaneSelector
    {
        /// <summary>
        /// Returns the primary lane index. When any ready lanes exist, cooling/unready lanes are
        /// skipped; when none are ready, selection falls back across all lanes so requests can still
        /// make progress after every provider has backed off.
        /// </summary>
        public static int SelectPrimaryIndex(int laneCount, ApiLaneRoutingMode mode, int selectorCounter, IList<bool> readyLanes)
        {
            if (laneCount <= 0)
            {
                return -1;
            }

            int safeCounter = selectorCounter & int.MaxValue;
            bool anyReady = AnyReady(laneCount, readyLanes);

            switch (Normalize(mode))
            {
                case ApiLaneRoutingMode.FailoverOnly:
                    return FirstAllowed(laneCount, readyLanes, anyReady);
                case ApiLaneRoutingMode.PreferTopRows:
                    return WeightedByOrder(laneCount, safeCounter, readyLanes, anyReady);
                default:
                    return Balanced(laneCount, safeCounter, readyLanes, anyReady);
            }
        }

        /// <summary>
        /// Finds the first configured model matching an event prompt's optional forced-model text.
        /// Blank, unknown, or whitespace-only values return -1 so callers can use normal routing.
        /// </summary>
        public static int SelectForcedModelIndex(IList<string> modelNames, string forcedModelName)
        {
            string requested = (forcedModelName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(requested) || modelNames == null)
            {
                return -1;
            }

            for (int i = 0; i < modelNames.Count; i++)
            {
                string modelName = modelNames[i];
                if (!string.IsNullOrWhiteSpace(modelName)
                    && string.Equals(modelName.Trim(), requested, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Normalizes invalid enum values loaded from old or hand-edited settings.</summary>
        public static ApiLaneRoutingMode Normalize(ApiLaneRoutingMode mode)
        {
            switch (mode)
            {
                case ApiLaneRoutingMode.Balanced:
                case ApiLaneRoutingMode.PreferTopRows:
                case ApiLaneRoutingMode.FailoverOnly:
                    return mode;
                default:
                    return ApiLaneRoutingMode.Balanced;
            }
        }

        private static int Balanced(int laneCount, int selectorCounter, IList<bool> readyLanes, bool anyReady)
        {
            int allowedCount = AllowedCount(laneCount, readyLanes, anyReady);
            int slot = selectorCounter % allowedCount;
            for (int i = 0; i < laneCount; i++)
            {
                if (!Allowed(i, readyLanes, anyReady))
                {
                    continue;
                }

                if (slot == 0)
                {
                    return i;
                }

                slot--;
            }

            return 0;
        }

        private static int WeightedByOrder(int laneCount, int selectorCounter, IList<bool> readyLanes, bool anyReady)
        {
            int totalWeight = 0;
            for (int i = 0; i < laneCount; i++)
            {
                if (Allowed(i, readyLanes, anyReady))
                {
                    totalWeight += laneCount - i;
                }
            }

            if (totalWeight <= 0)
            {
                return 0;
            }

            int slot = selectorCounter % totalWeight;
            for (int i = 0; i < laneCount; i++)
            {
                if (!Allowed(i, readyLanes, anyReady))
                {
                    continue;
                }

                int weight = laneCount - i;
                if (slot < weight)
                {
                    return i;
                }

                slot -= weight;
            }

            return 0;
        }

        private static int FirstAllowed(int laneCount, IList<bool> readyLanes, bool anyReady)
        {
            for (int i = 0; i < laneCount; i++)
            {
                if (Allowed(i, readyLanes, anyReady))
                {
                    return i;
                }
            }

            return 0;
        }

        private static int AllowedCount(int laneCount, IList<bool> readyLanes, bool anyReady)
        {
            int count = 0;
            for (int i = 0; i < laneCount; i++)
            {
                if (Allowed(i, readyLanes, anyReady))
                {
                    count++;
                }
            }

            return count > 0 ? count : laneCount;
        }

        private static bool AnyReady(int laneCount, IList<bool> readyLanes)
        {
            if (readyLanes == null)
            {
                return true;
            }

            int count = laneCount < readyLanes.Count ? laneCount : readyLanes.Count;
            for (int i = 0; i < count; i++)
            {
                if (readyLanes[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Allowed(int index, IList<bool> readyLanes, bool anyReady)
        {
            if (!anyReady || readyLanes == null)
            {
                return true;
            }

            return index >= 0 && index < readyLanes.Count && readyLanes[index];
        }
    }
}

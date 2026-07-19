// Pure eviction planning for the pawn memory subsystem (design/MEMORY_SYSTEM_DESIGN.md §10.1).
// The store stays small over a colony's whole lifetime by forgetting what stopped being relevant:
// ordinary memories that were never recalled decay out after two in-game years, high-importance
// "core" memories are exempt from that decay but have their own cap, and a per-pawn cap plus a
// colony-wide safety cap bound total save weight. The planner only RETURNS ids to evict — the
// impure applier performs the removals, and nothing here mutates its input.
//
// Being recalled keeps a memory alive: retention decays from max(createdTick, lastRecalledTick),
// so a fragment that keeps surfacing in diaries defends its place in the store.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers"). This file must stay free of
// Verse/Unity/settings/Def references so the pure test project can link it directly.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Decides which fragments a pawn (or the colony-wide store) forgets.</summary>
    internal static class MemoryEvictionPlanner
    {
        /// <summary>
        /// One pawn's snapshots -> memoryIds to evict, in a deterministic order (design §10.1):
        /// 1. stale rule, 2. core cap overflow, 3. per-pawn cap overflow. The input list is never
        /// mutated; null or blank-id rows are skipped (they cannot be evicted by id).
        /// </summary>
        public static List<string> Plan(
            List<MemoryFragmentSnapshot> fragments,
            int currentTick,
            MemoryPolicySnapshot policy)
        {
            List<string> evict = new List<string>();
            MemoryPolicySnapshot safePolicy = policy ?? MemoryPolicySnapshot.CreateDefault();
            List<MemoryFragmentSnapshot> usable = Usable(fragments);
            float coreThreshold = Clamp01(safePolicy.coreImportanceThreshold);

            // 1. Stale rule: an ordinary (non-core) memory that has neither been created nor
            // recalled within staleEvictTicks fades out even when the pawn is under cap.
            List<MemoryFragmentSnapshot> survivors = new List<MemoryFragmentSnapshot>();
            for (int i = 0; i < usable.Count; i++)
            {
                MemoryFragmentSnapshot fragment = usable[i];
                bool core = Clamp01(fragment.importance) >= coreThreshold;
                int ageSinceFresh = currentTick - Freshness(fragment);
                if (!core && ageSinceFresh > Math.Max(0, safePolicy.staleEvictTicks))
                {
                    AddDistinct(evict, fragment.memoryId);
                }
                else
                {
                    survivors.Add(fragment);
                }
            }

            // 2. Core exemption + core cap: core memories ignore score eviction, but their own
            // cap evicts the one with the OLDEST freshness first.
            List<MemoryFragmentSnapshot> coreSurvivors = new List<MemoryFragmentSnapshot>();
            List<MemoryFragmentSnapshot> nonCoreSurvivors = new List<MemoryFragmentSnapshot>();
            for (int i = 0; i < survivors.Count; i++)
            {
                if (Clamp01(survivors[i].importance) >= coreThreshold)
                {
                    coreSurvivors.Add(survivors[i]);
                }
                else
                {
                    nonCoreSurvivors.Add(survivors[i]);
                }
            }

            int maxCore = Math.Max(0, safePolicy.maxCoreFragmentsPerPawn);
            if (coreSurvivors.Count > maxCore)
            {
                coreSurvivors.Sort(CompareByFreshnessAsc);
                int overflow = coreSurvivors.Count - maxCore;
                for (int i = 0; i < overflow; i++)
                {
                    AddDistinct(evict, coreSurvivors[i].memoryId);
                }

                coreSurvivors.RemoveRange(0, overflow);
            }

            // 3. Per-pawn cap: survivors above maxFragmentsPerPawn evict NON-CORE fragments
            // lowest-retention-first. (Core is bounded by its own cap; if XML ever sets the core
            // cap above the per-pawn cap, core fragments can fill the store — by design.)
            int maxPerPawn = Math.Max(0, safePolicy.maxFragmentsPerPawn);
            int remaining = coreSurvivors.Count + nonCoreSurvivors.Count;
            if (remaining > maxPerPawn)
            {
                nonCoreSurvivors.Sort((left, right) => CompareByRetentionAsc(left, right, currentTick, safePolicy));
                int overflow = remaining - maxPerPawn;
                for (int i = 0; i < nonCoreSurvivors.Count && overflow > 0; i++, overflow--)
                {
                    AddDistinct(evict, nonCoreSurvivors[i].memoryId);
                }
            }

            return evict;
        }

        /// <summary>
        /// Colony-wide safety pass (design §10.1): when the WHOLE store exceeds maxTotalFragments,
        /// evict the lowest-retention fragments regardless of owner or core status until under.
        /// Importance already dominates the retention score, so core memories are the last to go.
        /// </summary>
        public static List<string> PlanGlobalCap(
            List<MemoryFragmentSnapshot> all,
            int currentTick,
            MemoryPolicySnapshot policy)
        {
            List<string> evict = new List<string>();
            MemoryPolicySnapshot safePolicy = policy ?? MemoryPolicySnapshot.CreateDefault();
            List<MemoryFragmentSnapshot> usable = Usable(all);
            int maxTotal = Math.Max(0, safePolicy.maxTotalFragments);
            if (usable.Count <= maxTotal)
            {
                return evict;
            }

            usable.Sort((left, right) => CompareByRetentionAsc(left, right, currentTick, safePolicy));
            int overflow = usable.Count - maxTotal;
            for (int i = 0; i < overflow; i++)
            {
                AddDistinct(evict, usable[i].memoryId);
            }

            return evict;
        }

        /// <summary>
        /// The relevance a fragment must defend (design §10.1): importance scaled by exponential
        /// decay from the last time the memory was created OR recalled. Future-dated fragments
        /// (a clock regression) clamp their age to zero rather than inflating past importance.
        /// </summary>
        private static float Retention(MemoryFragmentSnapshot fragment, int currentTick, MemoryPolicySnapshot policy)
        {
            int age = Math.Max(0, currentTick - Freshness(fragment));
            double halfLives = age / (double)Math.Max(1, policy.retentionHalfLifeTicks);
            return Clamp01(fragment.importance) * (float)Math.Pow(0.5d, halfLives);
        }

        private static int Freshness(MemoryFragmentSnapshot fragment)
        {
            return Math.Max(fragment.createdTick, fragment.lastRecalledTick);
        }

        /// <summary>Eviction order: lowest retention, then oldest, then least recalled, then id.</summary>
        private static int CompareByRetentionAsc(
            MemoryFragmentSnapshot left,
            MemoryFragmentSnapshot right,
            int currentTick,
            MemoryPolicySnapshot policy)
        {
            int retention = Retention(left, currentTick, policy).CompareTo(Retention(right, currentTick, policy));
            return retention != 0 ? retention : CompareTieBreak(left, right);
        }

        /// <summary>Core-cap order: oldest freshness first, then the shared tie-breaks.</summary>
        private static int CompareByFreshnessAsc(MemoryFragmentSnapshot left, MemoryFragmentSnapshot right)
        {
            int freshness = Freshness(left).CompareTo(Freshness(right));
            return freshness != 0 ? freshness : CompareTieBreak(left, right);
        }

        private static int CompareTieBreak(MemoryFragmentSnapshot left, MemoryFragmentSnapshot right)
        {
            int created = left.createdTick.CompareTo(right.createdTick);
            if (created != 0)
            {
                return created;
            }

            int recalls = left.recallCount.CompareTo(right.recallCount);
            return recalls != 0 ? recalls : string.Compare(left.memoryId, right.memoryId,
                StringComparison.Ordinal);
        }

        private static List<MemoryFragmentSnapshot> Usable(List<MemoryFragmentSnapshot> fragments)
        {
            List<MemoryFragmentSnapshot> usable = new List<MemoryFragmentSnapshot>();
            if (fragments == null)
            {
                return usable;
            }

            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i] != null && !string.IsNullOrWhiteSpace(fragments[i].memoryId))
                {
                    usable.Add(fragments[i]);
                }
            }

            return usable;
        }

        private static void AddDistinct(List<string> ids, string memoryId)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], memoryId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            ids.Add(memoryId);
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}

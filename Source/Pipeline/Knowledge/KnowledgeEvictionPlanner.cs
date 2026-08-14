// KnowledgeEvictionPlanner.cs — pure defensive-cap planning for the important-memory store
// (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §2.3). There is NO age-based eviction and no recall
// metadata: records live forever unless a hard cap is hit. Dead owners keep their records for
// resurrection; only owners gone from the game entirely count as absent.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers"). No Verse/Unity/Def/settings here.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Plans which record ids to drop when the per-pawn or global caps are exceeded.</summary>
    internal static class KnowledgeEvictionPlanner
    {
        private sealed class GlobalStub
        {
            public string recordId;
            public int tick;
            public bool ownerAbsent;
            public bool protectedFromAutomaticEviction;
        }

        /// <summary>
        /// Per-pawn cap first (oldest of that owner drop), then the global cap: the oldest records
        /// of absent owners first, then the oldest records globally. globalCapHit asks the caller
        /// to emit its ONE bounded warning (§2.3).
        /// </summary>
        public static KnowledgeEvictionPlan Plan(List<KnowledgeOwnerLoad> owners, KnowledgePolicySnapshot policy)
        {
            KnowledgeEvictionPlan plan = new KnowledgeEvictionPlan();
            if (owners == null || owners.Count == 0)
            {
                return plan;
            }

            KnowledgePolicySnapshot safePolicy = policy ?? KnowledgePolicySnapshot.CreateDefault();
            int perPawnCap = Math.Max(0, safePolicy.maxRecordsPerPawn);
            int globalCap = Math.Max(0, safePolicy.maxRecordsGlobal);

            List<GlobalStub> survivors = new List<GlobalStub>();
            for (int i = 0; i < owners.Count; i++)
            {
                KnowledgeOwnerLoad owner = owners[i];
                if (owner == null || owner.records == null)
                {
                    continue;
                }

                List<KnowledgeRecordStub> stubs = UsableStubs(owner.records);
                stubs.Sort(CompareOldestFirst);
                int dropCount = Math.Max(0, stubs.Count - perPawnCap);
                for (int j = 0; j < stubs.Count; j++)
                {
                    KnowledgeRecordStub stub = stubs[j];
                    // Protected rows still count toward the cap. Skip them and keep scanning oldest-
                    // first for an evictable captured row; if every row is protected, dropCount simply
                    // remains unmet and this bounded loop terminates without deleting canon.
                    if (dropCount > 0 && !stub.protectedFromAutomaticEviction)
                    {
                        plan.dropRecordIds.Add(stub.recordId);
                        dropCount--;
                    }
                    else
                    {
                        survivors.Add(new GlobalStub
                        {
                            recordId = stub.recordId,
                            tick = stub.tick,
                            ownerAbsent = owner.ownerAbsent,
                            protectedFromAutomaticEviction = stub.protectedFromAutomaticEviction
                        });
                    }
                }
            }

            int overflow = survivors.Count - globalCap;
            if (overflow <= 0)
            {
                return plan;
            }

            // Absent owners first (§2.3), each pool oldest-first, ties by record id so replays of
            // the same save always evict the same rows. Protected rows count toward overflow but are
            // excluded from the candidate pool, so an all-protected store terminates with no plan.
            List<GlobalStub> candidates = new List<GlobalStub>();
            for (int i = 0; i < survivors.Count; i++)
            {
                if (!survivors[i].protectedFromAutomaticEviction)
                {
                    candidates.Add(survivors[i]);
                }
            }

            candidates.Sort(CompareGlobalEvictionOrder);
            int globalDrops = Math.Min(overflow, candidates.Count);
            for (int i = 0; i < globalDrops; i++)
            {
                plan.dropRecordIds.Add(candidates[i].recordId);
            }

            plan.globalCapHit = globalDrops > 0;

            return plan;
        }

        private static List<KnowledgeRecordStub> UsableStubs(List<KnowledgeRecordStub> records)
        {
            List<KnowledgeRecordStub> usable = new List<KnowledgeRecordStub>();
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] != null && !string.IsNullOrWhiteSpace(records[i].recordId))
                {
                    usable.Add(records[i]);
                }
            }

            return usable;
        }

        private static int CompareOldestFirst(KnowledgeRecordStub left, KnowledgeRecordStub right)
        {
            int tick = left.tick.CompareTo(right.tick);
            return tick != 0
                ? tick
                : string.Compare(left.recordId, right.recordId, StringComparison.Ordinal);
        }

        private static int CompareGlobalEvictionOrder(GlobalStub left, GlobalStub right)
        {
            int absent = right.ownerAbsent.CompareTo(left.ownerAbsent);
            if (absent != 0)
            {
                return absent;
            }

            int tick = left.tick.CompareTo(right.tick);
            return tick != 0
                ? tick
                : string.Compare(left.recordId, right.recordId, StringComparison.Ordinal);
        }
    }
}

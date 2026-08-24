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
    /// <summary>One owner-qualified legacy deletion handle; record IDs are owner-private.</summary>
    internal sealed class KnowledgeEvictionHandle
    {
        public string ownerPawnId = string.Empty;
        public string recordId = string.Empty;
        public int sourceIndex = -1;
    }

    /// <summary>Qualified legacy plan used by the runtime adapter.</summary>
    internal sealed class QualifiedKnowledgeEvictionPlan
    {
        public List<KnowledgeEvictionHandle> drops = new List<KnowledgeEvictionHandle>();
        public bool globalCapHit;
    }

    /// <summary>One independently removable M4 hard-pressure atom.</summary>
    internal sealed class MemoryPressureAtom
    {
        public string ownerPawnId = string.Empty;
        public string rootId = string.Empty;
        public string recordId = string.Empty;
        /// <summary>Empty means the whole unedited block; otherwise one Summary contribution.</summary>
        public string contributionId = string.Empty;
        public string importance = string.Empty;
        public bool playerEdited;
        public bool ageUnknown;
        public long originalEventTick;
        public long logicalBytes;
        public int blockUnits;
    }

    /// <summary>Detached pressure request. Both deficits must be satisfied by one complete plan.</summary>
    internal sealed class MemoryPressurePlanRequest
    {
        public long bytesToRelease;
        public int blocksToRelease;
        public List<MemoryPressureAtom> atoms = new List<MemoryPressureAtom>();
    }

    /// <summary>Atomic hard-pressure result; refusal never exposes a partial deletion list.</summary>
    internal sealed class MemoryPressurePlan
    {
        public bool canApply;
        public bool protectedSaturation;
        public long releasedBytes;
        public int releasedBlocks;
        public List<MemoryPressureAtom> removals = new List<MemoryPressureAtom>();
    }

    /// <summary>Plans legacy and M4 eviction without touching saved state.</summary>
    internal static class KnowledgeEvictionPlanner
    {
        private sealed class GlobalStub
        {
            public string ownerPawnId;
            public string recordId;
            public int tick;
            public bool ownerAbsent;
            public bool protectedFromAutomaticEviction;
            public int sourceIndex;
        }

        /// <summary>
        /// Per-pawn cap first (oldest of that owner drop), then the global cap: the oldest records
        /// of absent owners first, then the oldest records globally. globalCapHit asks the caller
        /// to emit its ONE bounded warning (§2.3).
        /// </summary>
        public static KnowledgeEvictionPlan Plan(List<KnowledgeOwnerLoad> owners, KnowledgePolicySnapshot policy)
        {
            QualifiedKnowledgeEvictionPlan qualified = PlanQualified(owners, policy);
            KnowledgeEvictionPlan plan = new KnowledgeEvictionPlan();
            for (int i = 0; i < qualified.drops.Count; i++)
                plan.dropRecordIds.Add(qualified.drops[i].recordId);
            plan.globalCapHit = qualified.globalCapHit;
            return plan;
        }

        /// <summary>
        /// Owner-qualified form of the legacy policy. This prevents equal owner-private record IDs
        /// from deleting rows belonging to a different pawn during the M4 adapter consolidation.
        /// </summary>
        public static QualifiedKnowledgeEvictionPlan PlanQualified(
            List<KnowledgeOwnerLoad> owners,
            KnowledgePolicySnapshot policy)
        {
            QualifiedKnowledgeEvictionPlan plan = new QualifiedKnowledgeEvictionPlan();
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
                        plan.drops.Add(new KnowledgeEvictionHandle
                        {
                            ownerPawnId = owner.ownerPawnId ?? string.Empty,
                            recordId = stub.recordId,
                            sourceIndex = stub.sourceIndex
                        });
                        dropCount--;
                    }
                    else
                    {
                        survivors.Add(new GlobalStub
                        {
                            ownerPawnId = owner.ownerPawnId ?? string.Empty,
                            recordId = stub.recordId,
                            tick = stub.tick,
                            ownerAbsent = owner.ownerAbsent,
                            protectedFromAutomaticEviction = stub.protectedFromAutomaticEviction,
                            sourceIndex = stub.sourceIndex
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
                plan.drops.Add(new KnowledgeEvictionHandle
                {
                    ownerPawnId = candidates[i].ownerPawnId,
                    recordId = candidates[i].recordId,
                    sourceIndex = candidates[i].sourceIndex
                });
            }

            plan.globalCapHit = globalDrops > 0;

            return plan;
        }

        /// <summary>
        /// Plans emergency deletion in the frozen order low → medium → unedited high, then oldest
        /// known tick and full ordinal identity. Player-edited atoms have no deletion candidate.
        /// If the complete deficits cannot be met, the plan refuses atomically.
        /// </summary>
        public static MemoryPressurePlan PlanMemoryPressure(MemoryPressurePlanRequest request)
        {
            MemoryPressurePlan plan = new MemoryPressurePlan();
            if (request == null || request.bytesToRelease < 0 || request.blocksToRelease < 0
                || request.atoms == null) return plan;
            long requiredBytes = request.bytesToRelease;
            int requiredBlocks = request.blocksToRelease;
            if (requiredBytes == 0 && requiredBlocks == 0)
            {
                plan.canApply = true;
                return plan;
            }

            List<MemoryPressureAtom> candidates = new List<MemoryPressureAtom>();
            for (int i = 0; i < request.atoms.Count; i++)
            {
                MemoryPressureAtom atom = request.atoms[i];
                if (atom != null && !atom.playerEdited && atom.logicalBytes >= 0
                    && atom.blockUnits >= 0 && MemoryContractTokens.IsKnownImportance(atom.importance)
                    && !string.IsNullOrEmpty(atom.ownerPawnId)
                    && !string.IsNullOrEmpty(atom.recordId)) candidates.Add(atom);
            }
            candidates.Sort(ComparePressureAtom);
            long releasedBytes = 0;
            int releasedBlocks = 0;
            List<MemoryPressureAtom> chosen = new List<MemoryPressureAtom>();
            for (int i = 0; i < candidates.Count
                && (releasedBytes < requiredBytes || releasedBlocks < requiredBlocks); i++)
            {
                MemoryPressureAtom atom = candidates[i];
                chosen.Add(atom);
                releasedBytes = SaturatingAdd(releasedBytes, atom.logicalBytes);
                releasedBlocks = SaturatingAdd(releasedBlocks, atom.blockUnits);
            }
            if (releasedBytes < requiredBytes || releasedBlocks < requiredBlocks)
            {
                plan.protectedSaturation = true;
                return plan;
            }
            plan.canApply = true;
            plan.releasedBytes = releasedBytes;
            plan.releasedBlocks = releasedBlocks;
            plan.removals = chosen;
            return plan;
        }

        private static List<KnowledgeRecordStub> UsableStubs(List<KnowledgeRecordStub> records)
        {
            List<KnowledgeRecordStub> usable = new List<KnowledgeRecordStub>();
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] != null && !string.IsNullOrWhiteSpace(records[i].recordId))
                {
                    usable.Add(new KnowledgeRecordStub
                    {
                        recordId = records[i].recordId,
                        tick = records[i].tick,
                        sourceIndex = records[i].sourceIndex >= 0 ? records[i].sourceIndex : i,
                        protectedFromAutomaticEviction = records[i].protectedFromAutomaticEviction
                    });
                }
            }

            return usable;
        }

        private static int CompareOldestFirst(KnowledgeRecordStub left, KnowledgeRecordStub right)
        {
            int tick = left.tick.CompareTo(right.tick);
            if (tick != 0) return tick;
            int record = string.Compare(left.recordId, right.recordId, StringComparison.Ordinal);
            return record != 0 ? record : left.sourceIndex.CompareTo(right.sourceIndex);
        }

        private static int CompareGlobalEvictionOrder(GlobalStub left, GlobalStub right)
        {
            int absent = right.ownerAbsent.CompareTo(left.ownerAbsent);
            if (absent != 0)
            {
                return absent;
            }

            int tick = left.tick.CompareTo(right.tick);
            if (tick != 0) return tick;
            int owner = string.Compare(left.ownerPawnId, right.ownerPawnId, StringComparison.Ordinal);
            if (owner != 0) return owner;
            int record = string.Compare(left.recordId, right.recordId, StringComparison.Ordinal);
            return record != 0 ? record : left.sourceIndex.CompareTo(right.sourceIndex);
        }

        private static int ComparePressureAtom(MemoryPressureAtom left, MemoryPressureAtom right)
        {
            int importance = ImportanceRank(left.importance).CompareTo(ImportanceRank(right.importance));
            if (importance != 0) return importance;
            long leftTick = left.ageUnknown ? long.MaxValue : left.originalEventTick;
            long rightTick = right.ageUnknown ? long.MaxValue : right.originalEventTick;
            int tick = leftTick.CompareTo(rightTick);
            if (tick != 0) return tick;
            int owner = string.Compare(left.ownerPawnId, right.ownerPawnId, StringComparison.Ordinal);
            if (owner != 0) return owner;
            int root = string.Compare(left.rootId, right.rootId, StringComparison.Ordinal);
            if (root != 0) return root;
            int record = string.Compare(left.recordId, right.recordId, StringComparison.Ordinal);
            return record != 0 ? record : string.Compare(
                left.contributionId, right.contributionId, StringComparison.Ordinal);
        }

        private static int ImportanceRank(string value)
        {
            if (value == MemoryContractTokens.ImportanceImportant) return 2;
            if (value == MemoryContractTokens.ImportanceRegular) return 1;
            return 0;
        }

        private static long SaturatingAdd(long left, long right)
        {
            return right > 0 && left > long.MaxValue - right ? long.MaxValue : left + right;
        }

        private static int SaturatingAdd(int left, int right)
        {
            return right > 0 && left > int.MaxValue - right ? int.MaxValue : left + right;
        }
    }
}

// DiaryGameComponent.MemoryMaintenance.cs — one bounded elapsed-time main-thread M4 cursor.
//
// Maintenance is deliberately absent from Scribe's pre-save path. Load/settings/new-game marks
// derived work dirty; ordinary ticks process a deterministic owner/root window, observing both the
// XML work-item cap and microsecond escape hatch. The legacy record cap is redirected through this
// same cadence and its one pure planner, so there is no competing live eviction loop.
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private sealed class MemoryMaintenanceHandle
        {
            public string ownerPawnId = string.Empty;
            public string rootId = string.Empty;
            public bool repairRoots;
        }

        private readonly List<MemoryMaintenanceHandle> memoryMaintenanceHandles =
            new List<MemoryMaintenanceHandle>();
        private long memoryMaintenanceLastRunTick;
        private int memoryMaintenanceNextItemIndex;
        private bool memoryMaintenanceDirty = true;
        private bool memoryMaintenanceLegacyDoneForCycle;

        /// <summary>Resets all transient cursor state at a game/component identity boundary.</summary>
        private void ResetMemoryMaintenanceTransient(bool dirty)
        {
            memoryMaintenanceHandles.Clear();
            memoryMaintenanceLastRunTick = 0;
            memoryMaintenanceNextItemIndex = 0;
            memoryMaintenanceDirty = dirty;
            memoryMaintenanceLegacyDoneForCycle = false;
            memoryM4IndexesDirty = true;
        }

        /// <summary>Marks bounded work dirty after a shorter TTL/lower target settings commit.</summary>
        internal void MarkMemoryMaintenanceDirtyForSettingsChange(
            long priorMinorTicks,
            long priorRegularTicks,
            int priorTarget,
            long nextMinorTicks,
            long nextRegularTicks,
            int nextTarget)
        {
            if (!MemoryMaintenancePolicy.SettingsChangeMakesDirty(
                priorMinorTicks, priorRegularTicks, priorTarget,
                nextMinorTicks, nextRegularTicks, nextTarget)) return;
            memoryMaintenanceDirty = true;
            memoryMaintenanceNextItemIndex = 0;
            memoryMaintenanceHandles.Clear();
            memoryMaintenanceLegacyDoneForCycle = false;
        }

        /// <summary>Runs at most one XML-bounded maintenance slice on the main game tick.</summary>
        private void RunMemoryMaintenanceSlice(int nowTick)
        {
            if (nowTick < 0) return;
            try
            {
                EnsureMemoryM4Indexes();
                int intervalTicks = DiaryKnowledgePolicy.EvictionScanIntervalTicks();
                // Check elapsed/dirty state before reading the remaining XML capacities. A completed
                // cycle deliberately clears its snapshot; when the next interval becomes due, rebuild
                // from saved truth even though no intervening mutation marked the component dirty.
                MemoryMaintenanceSlicePlan dueCheck = MemoryMaintenancePolicy.Plan(
                    new MemoryMaintenanceSliceRequest
                    {
                        nowTick = nowTick,
                        lastRunTick = memoryMaintenanceLastRunTick,
                        dirty = memoryMaintenanceDirty,
                        itemCount = memoryMaintenanceHandles.Count,
                        nextItemIndex = memoryMaintenanceNextItemIndex,
                        maximumWorkItems = 1,
                        intervalTicks = intervalTicks
                    });
                if (!dueCheck.due) return;
                if (MemoryMaintenancePolicy.ShouldRebuildSnapshot(
                    dueCheck, memoryMaintenanceHandles.Count))
                    RebuildMemoryMaintenanceHandles();
                int maximumItems = (int)ReadCapacityLong("sliceWorkItems", 30, 240);
                int targetMicroseconds = (int)ReadCapacityLong(
                    "sliceTargetMicroseconds", 375, 1000);
                MemoryMaintenanceSlicePlan plan = MemoryMaintenancePolicy.Plan(
                    new MemoryMaintenanceSliceRequest
                    {
                        nowTick = nowTick,
                        lastRunTick = memoryMaintenanceLastRunTick,
                        dirty = memoryMaintenanceDirty,
                        itemCount = memoryMaintenanceHandles.Count,
                        nextItemIndex = memoryMaintenanceNextItemIndex,
                        maximumWorkItems = maximumItems,
                        intervalTicks = intervalTicks
                    });

                if (!memoryMaintenanceLegacyDoneForCycle)
                {
                    ApplyKnowledgeEviction();
                    memoryMaintenanceLegacyDoneForCycle = true;
                }
                if (plan.workItems == 0)
                {
                    CompleteMemoryMaintenanceCycle(nowTick);
                    return;
                }

                Stopwatch timer = Stopwatch.StartNew();
                int processed = 0;
                bool changed = false;
                MemoryReducerPolicy policy = BuildMemoryReducerPolicy(nowTick);
                for (int offset = 0; offset < plan.workItems; offset++)
                {
                    // The first indivisible item always runs; before every later item, the elapsed
                    // escape hatch wins. One reducer item is never interrupted halfway through.
                    if (offset > 0 && ElapsedMicroseconds(timer) >= targetMicroseconds) break;
                    int handleIndex = plan.startIndex + offset;
                    MemoryMaintenanceHandle handle = memoryMaintenanceHandles[handleIndex];
                    PawnKnowledgeState owner;
                    if (memoryM4OwnerById.TryGetValue(handle.ownerPawnId, out owner))
                    {
                        if (handle.repairRoots)
                        {
                            changed |= TryRepairSavedMemoryRoots(owner, policy);
                        }
                        else if (string.IsNullOrEmpty(handle.rootId))
                        {
                            changed |= TryReduceSavedStandaloneBlocks(owner, policy);
                        }
                        else
                        {
                            int rootIndex = FindSavedRootIndex(owner.threadRoots, handle.rootId);
                            if (rootIndex >= 0)
                                changed |= TryReduceSavedMemoryRoot(owner, rootIndex, policy);
                        }
                    }
                    processed++;
                }

                int next = plan.startIndex + processed;
                if (next >= memoryMaintenanceHandles.Count)
                {
                    CompleteMemoryMaintenanceCycle(nowTick);
                }
                else
                {
                    memoryMaintenanceNextItemIndex = next;
                    memoryMaintenanceDirty = true;
                }
                if (changed)
                {
                    RebuildMemoryM4Indexes();
                    RebuildMemorySizeIndexes();
                }
            }
            catch (Exception exception)
            {
                // Back off for one full elapsed interval. Keeping dirty=true would make the same
                // failing item throw again on every game tick while ErrorOnce hid the retry storm.
                memoryMaintenanceLastRunTick = nowTick;
                memoryMaintenanceNextItemIndex = 0;
                memoryMaintenanceDirty = false;
                memoryMaintenanceLegacyDoneForCycle = false;
                memoryMaintenanceHandles.Clear();
                memoryM4IndexesDirty = true;
                RecordMemoryDiagnostic("other", "maintenance");
                Verse.Log.ErrorOnce("[Pawn Diary] Memory maintenance failed: " + exception,
                    "PawnDiary.Memory.Maintenance".GetHashCode());
            }
        }

        private void RebuildMemoryMaintenanceHandles()
        {
            memoryMaintenanceHandles.Clear();
            foreach (KeyValuePair<string, PawnKnowledgeState> pair in memoryM4OwnerById)
            {
                PawnKnowledgeState owner = pair.Value;
                if (owner?.threadRoots != null
                    && memoryM4OwnersWithDuplicateCanonicalRoots.Contains(pair.Key))
                {
                    memoryMaintenanceHandles.Add(new MemoryMaintenanceHandle
                    {
                        ownerPawnId = pair.Key,
                        repairRoots = true
                    });
                }
                if (owner?.standaloneBlocks != null && owner.standaloneBlocks.Count > 0)
                {
                    memoryMaintenanceHandles.Add(new MemoryMaintenanceHandle
                    {
                        ownerPawnId = pair.Key,
                        rootId = string.Empty
                    });
                }
                for (int i = 0; owner?.threadRoots != null && i < owner.threadRoots.Count; i++)
                {
                    SavedMemoryThreadRoot root = owner.threadRoots[i];
                    if (root == null || string.IsNullOrEmpty(root.rootId)) continue;
                    memoryMaintenanceHandles.Add(new MemoryMaintenanceHandle
                    {
                        ownerPawnId = pair.Key,
                        rootId = root.rootId
                    });
                }
            }
            memoryMaintenanceHandles.Sort(delegate(
                MemoryMaintenanceHandle left, MemoryMaintenanceHandle right)
            {
                int owner = string.Compare(
                    left.ownerPawnId, right.ownerPawnId, StringComparison.Ordinal);
                if (owner != 0) return owner;
                int repair = right.repairRoots.CompareTo(left.repairRoots);
                return repair != 0 ? repair : string.Compare(
                    left.rootId, right.rootId, StringComparison.Ordinal);
            });
            if (memoryMaintenanceNextItemIndex < 0
                || memoryMaintenanceNextItemIndex >= memoryMaintenanceHandles.Count)
                memoryMaintenanceNextItemIndex = 0;
        }

        private void CompleteMemoryMaintenanceCycle(long nowTick)
        {
            MemoryPressureCommitResult pressure = TryApplyMemoryPressureCaps(nowTick);
            if (pressure.protectedSaturation)
                RecordMemoryDiagnostic("capacity_refused", "maintenance");
            memoryMaintenanceLastRunTick = nowTick;
            memoryMaintenanceNextItemIndex = 0;
            memoryMaintenanceDirty = false;
            memoryMaintenanceLegacyDoneForCycle = false;
            memoryMaintenanceHandles.Clear();
        }

        private static long ElapsedMicroseconds(Stopwatch timer)
        {
            if (timer == null || Stopwatch.Frequency <= 0) return long.MaxValue;
            long ticks = timer.ElapsedTicks;
            if (ticks > long.MaxValue / 1000000L) return long.MaxValue;
            return ticks * 1000000L / Stopwatch.Frequency;
        }
    }
}

// MemoryMaintenancePolicy.cs — pure elapsed-time maintenance slicing for M4.
//
// The runtime owns a transient stopwatch for the microsecond escape hatch. This helper owns the
// deterministic item window and elapsed scheduling, so missed intervals catch up once without a
// tick-modulo burst and load/settings dirtiness can request immediate bounded work.
using System;

namespace PawnDiary
{
    /// <summary>Detached maintenance cursor input.</summary>
    internal sealed class MemoryMaintenanceSliceRequest
    {
        public long nowTick;
        public long lastRunTick;
        public bool dirty;
        public int itemCount;
        public int nextItemIndex;
        public int maximumWorkItems = 30;
        public long intervalTicks = 2500;
    }

    /// <summary>Bounded deterministic maintenance window.</summary>
    internal sealed class MemoryMaintenanceSlicePlan
    {
        public bool due;
        public int startIndex;
        public int workItems;
        public int nextItemIndex;
        public bool completedCycle;
        public long nextLastRunTick;
    }

    /// <summary>Plans one bounded elapsed-time slice.</summary>
    internal static class MemoryMaintenancePolicy
    {
        /// <summary>
        /// A due cycle with no retained snapshot must enumerate saved truth again. Dirtiness is not
        /// required: completed cycles intentionally discard their handles between elapsed intervals.
        /// </summary>
        public static bool ShouldRebuildSnapshot(MemoryMaintenanceSlicePlan dueCheck, int handleCount)
        {
            return dueCheck != null && dueCheck.due && handleCount == 0;
        }

        public static MemoryMaintenanceSlicePlan Plan(MemoryMaintenanceSliceRequest request)
        {
            MemoryMaintenanceSlicePlan plan = new MemoryMaintenanceSlicePlan();
            if (request == null || request.nowTick < 0 || request.lastRunTick < 0
                || request.itemCount < 0 || request.maximumWorkItems <= 0
                || request.intervalTicks < 0) return plan;
            bool elapsed = MemoryChapterPolicy.Elapsed(request.nowTick, request.lastRunTick)
                >= request.intervalTicks;
            if (!request.dirty && !elapsed) return plan;
            plan.due = true;
            if (request.itemCount == 0)
            {
                plan.completedCycle = true;
                plan.nextLastRunTick = request.nowTick;
                return plan;
            }
            int start = request.nextItemIndex;
            if (start < 0 || start >= request.itemCount) start = 0;
            plan.startIndex = start;
            plan.workItems = Math.Min(request.maximumWorkItems, request.itemCount - start);
            int next = start + plan.workItems;
            plan.completedCycle = next >= request.itemCount;
            plan.nextItemIndex = plan.completedCycle ? 0 : next;
            plan.nextLastRunTick = plan.completedCycle ? request.nowTick : request.lastRunTick;
            return plan;
        }

        /// <summary>Shorter TTL or a lower target requires maintenance; increases do not rebuild detail.</summary>
        public static bool SettingsChangeMakesDirty(
            long priorMinorTicks,
            long priorRegularTicks,
            int priorTarget,
            long nextMinorTicks,
            long nextRegularTicks,
            int nextTarget)
        {
            return nextMinorTicks < priorMinorTicks || nextRegularTicks < priorRegularTicks
                || nextTarget < priorTarget;
        }
    }
}

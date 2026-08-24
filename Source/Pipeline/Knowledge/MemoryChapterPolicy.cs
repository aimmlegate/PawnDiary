// MemoryChapterPolicy.cs — pure chapter lifecycle and elapsed-time policy for M4.
//
// Chapters are flat metadata rows beneath one exact thread root. This helper deliberately knows
// nothing about Pawn, Scribe, settings objects, or the game clock: callers provide detached values
// and receive a deterministic plan. Elapsed comparisons use subtraction, never tick modulo, so a
// long pause or a skipped maintenance slice cannot strand overdue work.
using System;

namespace PawnDiary
{
    /// <summary>Stable, non-localized chapter closure tokens saved by the M4 backend.</summary>
    internal static class MemoryChapterTokens
    {
        public const string FormalEnd = "formal_end";
        public const string Reversal = "reversal";
        public const string Lifecycle = "lifecycle";
        public const string Inactivity = "inactivity";
        public const string Repair = "repair";

        /// <summary>True only for one frozen closure-reason token.</summary>
        public static bool IsKnownClosureReason(string value)
        {
            return value == FormalEnd || value == Reversal || value == Lifecycle
                || value == Inactivity || value == Repair;
        }
    }

    /// <summary>Detached input for deciding whether one open chapter closes now.</summary>
    internal sealed class MemoryChapterClosureRequest
    {
        public bool alreadyClosed;
        public long nowTick;
        public long lastActivityTick;
        public long inactivityTicks;
        public bool formalEnd;
        public bool reversal;
        public bool lifecycleBoundary;
    }

    /// <summary>Pure closure result. A false shouldClose result never carries a reason.</summary>
    internal sealed class MemoryChapterClosurePlan
    {
        public bool shouldClose;
        public long closedTick;
        public string reasonToken = string.Empty;
    }

    /// <summary>Plans explicit or elapsed-inactivity chapter closure with overflow-safe arithmetic.</summary>
    internal static class MemoryChapterPolicy
    {
        /// <summary>
        /// Explicit evidence wins in a stable order. Otherwise, inactivity closes at the exact
        /// boundary <c>age &gt;= lifetime</c>. A clock moving backwards is treated as zero elapsed.
        /// </summary>
        public static MemoryChapterClosurePlan PlanClosure(MemoryChapterClosureRequest request)
        {
            MemoryChapterClosurePlan plan = new MemoryChapterClosurePlan();
            if (request == null || request.alreadyClosed || request.nowTick < 0
                || request.lastActivityTick < 0)
            {
                return plan;
            }

            if (request.formalEnd)
                return Close(request.nowTick, MemoryChapterTokens.FormalEnd);
            if (request.reversal)
                return Close(request.nowTick, MemoryChapterTokens.Reversal);
            if (request.lifecycleBoundary)
                return Close(request.nowTick, MemoryChapterTokens.Lifecycle);

            long elapsed = Elapsed(request.nowTick, request.lastActivityTick);
            if (request.inactivityTicks > 0 && elapsed >= request.inactivityTicks)
                return Close(SaturatingAdd(request.lastActivityTick, request.inactivityTicks),
                    MemoryChapterTokens.Inactivity);
            return plan;
        }

        /// <summary>Returns saturated nonnegative elapsed ticks, robust at Int64 boundaries.</summary>
        public static long Elapsed(long nowTick, long earlierTick)
        {
            if (nowTick <= earlierTick) return 0;
            if (earlierTick < 0 && nowTick > long.MaxValue + earlierTick) return long.MaxValue;
            return nowTick - earlierTick;
        }

        /// <summary>Adds nonnegative ticks without wrapping past the saved Int64 boundary.</summary>
        private static long SaturatingAdd(long left, long right)
        {
            if (left < 0 || right < 0) return 0;
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }

        private static MemoryChapterClosurePlan Close(long tick, string reason)
        {
            return new MemoryChapterClosurePlan
            {
                shouldClose = true,
                closedTick = tick,
                reasonToken = reason
            };
        }
    }
}

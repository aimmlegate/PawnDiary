// Bounded adapter-side staging for exact Thought_MemoryRoyalTitle signals. Vanilla adds these
// memories before its post-title callback, so a successful title/ritual page may claim the same
// action. Unmatched and expired signals are submitted unchanged to the ordinary Thought pipeline.
using System;
using System.Collections.Generic;
using PawnDiary.Ingestion;

namespace PawnDiary
{
    /// <summary>Stages, claims, releases, and resets exact title-memory signals.</summary>
    internal static class RoyalTitleThoughtCorrelation
    {
        private sealed class PendingThought
        {
            public RoyalTitleThoughtSnapshot fact;
            public ThoughtSignal signal;
        }

        private sealed class RecentOwner
        {
            public string pawnId = string.Empty;
            public string previousTitleDefName = string.Empty;
            public string newTitleDefName = string.Empty;
            public int tick;
        }

        private static readonly List<PendingThought> Pending = new List<PendingThought>();
        private static readonly List<RecentOwner> Recent = new List<RecentOwner>();

        public static bool TryStage(
            RoyalTitleThoughtSnapshot fact,
            ThoughtSignal signal,
            int nowTick,
            int correlationTicks,
            int maximumPending)
        {
            Maintain(nowTick, correlationTicks);
            if (fact == null || signal == null) return false;
            for (int i = Recent.Count - 1; i >= 0; i--)
            {
                RecentOwner owner = Recent[i];
                if (RoyalTitleThoughtOwnershipPolicy.Matches(
                    fact, owner.pawnId, owner.previousTitleDefName, owner.newTitleDefName)) return true;
            }

            int cap = maximumPending < 1 || maximumPending > 512 ? 128 : maximumPending;
            if (Pending.Count >= cap)
            {
                // Admission overflow fails open: the new signal remains on the ordinary pipeline.
                return false;
            }
            Pending.Add(new PendingThought { fact = fact, signal = signal });
            return true;
        }

        /// <summary>Claims matching staged memories and records a short inverse-order owner token.</summary>
        public static int Claim(
            string pawnId,
            string previousTitleDefName,
            string newTitleDefName,
            int tick,
            int correlationTicks)
        {
            Maintain(tick, correlationTicks);
            int claimed = 0;
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                PendingThought row = Pending[i];
                if (!RoyalTitleThoughtOwnershipPolicy.Matches(
                    row?.fact, pawnId, previousTitleDefName, newTitleDefName)) continue;
                Pending.RemoveAt(i);
                claimed++;
            }
            Recent.Add(new RecentOwner
            {
                pawnId = pawnId ?? string.Empty,
                previousTitleDefName = previousTitleDefName ?? string.Empty,
                newTitleDefName = newTitleDefName ?? string.Empty,
                tick = Math.Max(0, tick)
            });
            while (Recent.Count > 64) Recent.RemoveAt(0);
            return claimed;
        }

        public static void Maintain(int nowTick, int correlationTicks)
        {
            List<ThoughtSignal> released = new List<ThoughtSignal>();
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                PendingThought row = Pending[i];
                if (row?.fact != null && !RoyalTitleThoughtOwnershipPolicy.IsExpired(
                    row.fact.tick, nowTick, correlationTicks)) continue;
                Pending.RemoveAt(i);
                if (row?.signal != null) released.Add(row.signal);
            }
            for (int i = Recent.Count - 1; i >= 0; i--)
            {
                RecentOwner row = Recent[i];
                if (row == null || RoyalTitleThoughtOwnershipPolicy.IsExpired(
                    row.tick, nowTick, correlationTicks)) Recent.RemoveAt(i);
            }
            released.Reverse();
            for (int i = 0; i < released.Count; i++)
            {
                ThoughtSignal captured = released[i];
                DiaryPatchSafety.Run("RoyalTitleThoughtCorrelation.Release",
                    () => DiaryEvents.Submit(captured));
            }
        }

        public static int PendingCountForTests => Pending.Count;

        public static void Clear()
        {
            Pending.Clear();
            Recent.Clear();
        }
    }
}

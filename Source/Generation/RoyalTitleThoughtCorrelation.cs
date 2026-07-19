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
            // Capture paths never release other pawns' rows. The component's ordered maintenance pass
            // owns publication after mutation/title observers have all had a chance to claim.
            PruneRecentOwners(nowTick, correlationTicks);
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
            // Claim the exact row without releasing unrelated expired rows. Title-mutation and title-
            // thought windows intentionally share the same XML duration, so several richer owners can
            // arrive on the expiry tick. The component performs one global release only after every
            // pawn observer has had its chance to claim.
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
            PruneRecentOwners(tick, correlationTicks);
            return claimed;
        }

        public static void Maintain(int nowTick, int correlationTicks)
        {
            if (Pending.Count == 0 && Recent.Count == 0) return;
            List<ThoughtSignal> released = null;
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                PendingThought row = Pending[i];
                if (row?.fact != null && !RoyalTitleThoughtOwnershipPolicy.IsExpired(
                    row.fact.tick, nowTick, correlationTicks)) continue;
                Pending.RemoveAt(i);
                if (row?.signal != null)
                {
                    if (released == null) released = new List<ThoughtSignal>();
                    released.Add(row.signal);
                }
            }
            PruneRecentOwners(nowTick, correlationTicks);
            if (released == null) return;
            released.Reverse();
            for (int i = 0; i < released.Count; i++)
            {
                ThoughtSignal captured = released[i];
                DiaryPatchSafety.Run("RoyalTitleThoughtCorrelation.Release",
                    () => DiaryEvents.Submit(captured));
            }
        }

        /// <summary>
        /// Releases every unmatched live signal before a save. The ownership cache stays transient,
        /// but an ordinary Thought page must not disappear merely because the player saved during its
        /// short correlation window.
        /// </summary>
        public static void FlushPending()
        {
            if (Pending.Count == 0) return;
            List<ThoughtSignal> released = new List<ThoughtSignal>();
            for (int i = 0; i < Pending.Count; i++)
                if (Pending[i]?.signal != null) released.Add(Pending[i].signal);
            Pending.Clear();
            for (int i = 0; i < released.Count; i++)
            {
                ThoughtSignal captured = released[i];
                DiaryPatchSafety.Run("RoyalTitleThoughtCorrelation.FlushPending",
                    () => DiaryEvents.Submit(captured));
            }
        }

        /// <summary>True when at least one title memory still awaits an exact richer owner.</summary>
        public static bool HasPending => Pending.Count > 0;

        /// <summary>Checks whether one pawn has a title memory that should be reconciled before save.</summary>
        public static bool HasPendingForPawn(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId)) return false;
            for (int i = 0; i < Pending.Count; i++)
                if (string.Equals(Pending[i]?.fact?.pawnId, pawnId, StringComparison.Ordinal)) return true;
            return false;
        }

        public static int PendingCountForTests => Pending.Count;

        public static void Clear()
        {
            Pending.Clear();
            Recent.Clear();
        }

        private static void PruneRecentOwners(int nowTick, int correlationTicks)
        {
            for (int i = Recent.Count - 1; i >= 0; i--)
            {
                RecentOwner row = Recent[i];
                if (row == null || RoyalTitleThoughtOwnershipPolicy.IsExpired(
                    row.tick, nowTick, correlationTicks)) Recent.RemoveAt(i);
            }
        }
    }
}

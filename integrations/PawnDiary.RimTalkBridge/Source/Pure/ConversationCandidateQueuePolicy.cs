// Pure bounded ranking and request-cadence state for RimTalk conversation assessment. The runtime
// coordinator owns live pawn resolution and LLM handles; this file owns only deterministic queue
// decisions over plain DTOs, so chatter volume cannot grow memory or token spend without bound.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Generic;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>One plain queued candidate; live Pawn references never enter this DTO.</summary>
    public sealed class QueuedConversationCandidate
    {
        public string ConversationId;
        public string PairKey;
        public string SubjectId;
        public string PartnerId;
        public int FirstTick;
        public int LastTick;
        public int Score;
        public string ScoreReason;
        public float RecentEventTextOverlap;
        public Conversation Conversation;
        public List<RecentDiaryEvent> RecentEvents = new List<RecentDiaryEvent>();
    }

    /// <summary>Why a queue offer was accepted or conservatively ignored.</summary>
    public enum ConversationQueueOfferResult
    {
        Added,
        ReplacedWeakest,
        Duplicate,
        Invalid,
        CapacityDisabled,
        TooWeak,
        PairLimit
    }

    /// <summary>Bounded, de-duplicated, rank-ordered set of conversation candidates.</summary>
    public sealed class ConversationCandidateQueue
    {
        private readonly List<QueuedConversationCandidate> items = new List<QueuedConversationCandidate>();
        private readonly HashSet<string> seenConversationIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Current queued count.</summary>
        public int Count
        {
            get { return items.Count; }
        }

        /// <summary>True after an id has been offered in this game, whether queued, assessed, or ignored.</summary>
        public bool HasSeen(string conversationId)
        {
            return !string.IsNullOrEmpty(conversationId) && seenConversationIds.Contains(conversationId);
        }

        /// <summary>True when at least one currently queued candidate belongs to this pair.</summary>
        public bool ContainsPair(string pairKey)
        {
            if (string.IsNullOrEmpty(pairKey))
            {
                return false;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].PairKey == pairKey)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Offers one candidate. Stronger candidates replace the weakest globally or within an
        /// already-full pair; every id receives one terminal queue outcome per game session.
        /// </summary>
        public ConversationQueueOfferResult TryAdd(
            QueuedConversationCandidate candidate,
            int maxCapacity,
            int maxPerPair,
            out QueuedConversationCandidate evicted)
        {
            evicted = null;
            if (candidate == null || string.IsNullOrEmpty(candidate.ConversationId))
            {
                return ConversationQueueOfferResult.Invalid;
            }

            if (!seenConversationIds.Add(candidate.ConversationId))
            {
                return ConversationQueueOfferResult.Duplicate;
            }

            if (maxCapacity <= 0)
            {
                return ConversationQueueOfferResult.CapacityDisabled;
            }

            if (maxPerPair > 0 && CountForPair(candidate.PairKey) >= maxPerPair)
            {
                int weakestPairIndex = WeakestIndexForPair(candidate.PairKey);
                if (weakestPairIndex < 0 || CompareRank(candidate, items[weakestPairIndex]) <= 0)
                {
                    return ConversationQueueOfferResult.PairLimit;
                }

                evicted = items[weakestPairIndex];
                items.RemoveAt(weakestPairIndex);
            }

            if (items.Count >= maxCapacity)
            {
                int weakestIndex = WeakestIndex();
                if (weakestIndex < 0 || CompareRank(candidate, items[weakestIndex]) <= 0)
                {
                    // If a same-pair replacement already occurred, restore it: a rejected offer must
                    // not accidentally shrink the queue. In practice this branch only occurs when a
                    // caller supplies inconsistent pair/capacity limits, but keeping it atomic is cheap.
                    if (evicted != null)
                    {
                        items.Add(evicted);
                        evicted = null;
                    }

                    return ConversationQueueOfferResult.TooWeak;
                }

                evicted = items[weakestIndex];
                items.RemoveAt(weakestIndex);
            }

            items.Add(candidate);
            return evicted == null
                ? ConversationQueueOfferResult.Added
                : ConversationQueueOfferResult.ReplacedWeakest;
        }

        /// <summary>Returns up to max candidates in deterministic best-first rank order.</summary>
        public List<QueuedConversationCandidate> PeekRanked(int max)
        {
            List<QueuedConversationCandidate> ranked = new List<QueuedConversationCandidate>(items);
            ranked.Sort(delegate(QueuedConversationCandidate left, QueuedConversationCandidate right)
            {
                return -CompareRank(left, right);
            });

            if (max >= 0 && ranked.Count > max)
            {
                ranked.RemoveRange(max, ranked.Count - max);
            }

            return ranked;
        }

        /// <summary>Removes candidates whose quiet-finish tick is older than the configured window.</summary>
        public List<QueuedConversationCandidate> RemoveExpired(int nowTick, int expiryTicks)
        {
            List<QueuedConversationCandidate> removed = new List<QueuedConversationCandidate>();
            if (expiryTicks <= 0)
            {
                removed.AddRange(items);
                items.Clear();
                return removed;
            }

            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (nowTick - items[i].LastTick >= expiryTicks)
                {
                    removed.Add(items[i]);
                    items.RemoveAt(i);
                }
            }

            return removed;
        }

        /// <summary>Removes one queued id without forgetting that it already received an outcome.</summary>
        public bool Remove(string conversationId)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].ConversationId == conversationId)
                {
                    items.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Clears queued and seen state on a new game.</summary>
        public void Reset()
        {
            items.Clear();
            seenConversationIds.Clear();
        }

        /// <summary>Drops queued work while preserving seen ids, so toggling Level 2 cannot replay it.</summary>
        public void ClearQueued()
        {
            items.Clear();
        }

        /// <summary>
        /// Positive means left ranks higher: score descending, older first, then ordinal id ascending.
        /// </summary>
        public static int CompareRank(QueuedConversationCandidate left, QueuedConversationCandidate right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return -1;
            }

            if (right == null)
            {
                return 1;
            }

            int score = left.Score.CompareTo(right.Score);
            if (score != 0)
            {
                return score;
            }

            // Smaller first tick is older and therefore stronger on a tie.
            int age = right.FirstTick.CompareTo(left.FirstTick);
            if (age != 0)
            {
                return age;
            }

            // Lexicographically smaller stable id wins the final tie.
            return -string.CompareOrdinal(left.ConversationId ?? string.Empty, right.ConversationId ?? string.Empty);
        }

        private int CountForPair(string pairKey)
        {
            int count = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].PairKey == (pairKey ?? string.Empty))
                {
                    count++;
                }
            }

            return count;
        }

        private int WeakestIndexForPair(string pairKey)
        {
            int weakest = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].PairKey != (pairKey ?? string.Empty))
                {
                    continue;
                }

                if (weakest < 0 || CompareRank(items[i], items[weakest]) < 0)
                {
                    weakest = i;
                }
            }

            return weakest;
        }

        private int WeakestIndex()
        {
            int weakest = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (weakest < 0 || CompareRank(items[i], items[weakest]) < 0)
                {
                    weakest = i;
                }
            }

            return weakest;
        }
    }

    /// <summary>Pure one-in-flight/day/gap gate used by the runtime completion coordinator.</summary>
    public sealed class ConversationAssessmentBatchGate
    {
        private bool haveDay;
        private int dayIndex;
        private int batchesStartedToday;
        private bool haveAttempt;
        private int lastAttemptTick;
        private bool inFlight;

        public int BatchesStartedToday { get { return batchesStartedToday; } }
        public bool InFlight { get { return inFlight; } }

        /// <summary>True when a new request attempt is within its daily, gap, and in-flight bounds.</summary>
        public bool CanAttempt(int nowTick, int currentDayIndex, int maxBatchesPerDay, int minGapTicks)
        {
            RollDay(currentDayIndex);
            if (inFlight || maxBatchesPerDay <= 0 || batchesStartedToday >= maxBatchesPerDay)
            {
                return false;
            }

            return !haveAttempt || minGapTicks <= 0 || nowTick - lastAttemptTick >= minGapTicks;
        }

        /// <summary>Records a rejected API/no-budget attempt without spending a daily batch slot.</summary>
        public void MarkRejectedAttempt(int nowTick)
        {
            haveAttempt = true;
            lastAttemptTick = nowTick;
        }

        /// <summary>Records a successfully started handle and occupies the one in-flight slot.</summary>
        public void MarkStarted(int nowTick, int currentDayIndex)
        {
            RollDay(currentDayIndex);
            batchesStartedToday++;
            haveAttempt = true;
            lastAttemptTick = nowTick;
            inFlight = true;
        }

        /// <summary>Releases the one in-flight slot after any terminal completion status.</summary>
        public void MarkFinished()
        {
            inFlight = false;
        }

        /// <summary>Clears all transient cadence state on a new game.</summary>
        public void Reset()
        {
            haveDay = false;
            dayIndex = 0;
            batchesStartedToday = 0;
            haveAttempt = false;
            lastAttemptTick = 0;
            inFlight = false;
        }

        private void RollDay(int currentDayIndex)
        {
            if (haveDay && dayIndex == currentDayIndex)
            {
                return;
            }

            haveDay = true;
            dayIndex = currentDayIndex;
            batchesStartedToday = 0;
        }
    }
}

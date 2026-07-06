// Pure throttle policy: caps how many important-conversation entries the bridge submits, so a chatty
// colony does not flood every diary. NO RimWorld / RimTalk usings — file-linked into the pure tests.
//
// Three independent limits, all enforced in TryReserve:
//   • per-pawn daily cap  — each pawn only stars in so many conversation entries per in-game day.
//   • colony daily cap    — a whole-colony ceiling per day.
//   • pair minimum gap    — the same two pawns cannot generate entries back-to-back.
//
// Counters are in-memory only and reset on day rollover; they are NOT saved (a decided v1
// simplification — worst case a reload re-allows a few entries that day). See RIMTALK_BRIDGE_PLAN U9.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System.Collections.Generic;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>The three tunable throttle limits, passed in from settings.</summary>
    public sealed class ThrottleLimits
    {
        /// <summary>Max entries per pawn per day. 0 disables the per-pawn cap.</summary>
        public int perPawnDailyCap;

        /// <summary>Max entries colony-wide per day. 0 disables the colony cap.</summary>
        public int colonyDailyCap;

        /// <summary>Minimum ticks between entries for the same pawn pair. 0 disables the gap.</summary>
        public int pairMinGapTicks;
    }

    /// <summary>
    /// Tracks per-day and per-pair usage and decides whether a new conversation entry may be recorded.
    /// Stateful but pure: no RimWorld types, fully unit-testable.
    /// </summary>
    public sealed class ThrottlePolicy
    {
        private readonly Dictionary<string, int> perPawnToday = new Dictionary<string, int>();
        private readonly Dictionary<string, int> lastTickByPair = new Dictionary<string, int>();
        private int colonyToday;
        private bool haveDay;
        private int currentDay;

        /// <summary>
        /// Atomically checks all limits and, when they all pass, records the reservation and returns
        /// true. On any failure nothing is mutated and it returns false. Pass the partner id as "" for a
        /// solo entry (only <paramref name="idA"/> is charged).
        /// </summary>
        public bool TryReserve(string idA, string idB, int nowTick, int dayIndex, ThrottleLimits limits)
        {
            if (limits == null || string.IsNullOrEmpty(idA))
            {
                return false;
            }

            RollDayIfNeeded(dayIndex);

            string partner = idB ?? string.Empty;
            string pairKey = PairKey(idA, partner);

            // Pair minimum gap.
            int lastTick;
            if (limits.pairMinGapTicks > 0
                && lastTickByPair.TryGetValue(pairKey, out lastTick)
                && (nowTick - lastTick) < limits.pairMinGapTicks)
            {
                return false;
            }

            // Colony daily cap.
            if (limits.colonyDailyCap > 0 && colonyToday >= limits.colonyDailyCap)
            {
                return false;
            }

            // Per-pawn daily cap (both pawns must have headroom).
            if (limits.perPawnDailyCap > 0)
            {
                if (CountFor(idA) >= limits.perPawnDailyCap)
                {
                    return false;
                }

                if (partner.Length > 0 && CountFor(partner) >= limits.perPawnDailyCap)
                {
                    return false;
                }
            }

            // All checks passed: commit the reservation.
            colonyToday++;
            Increment(idA);
            if (partner.Length > 0)
            {
                Increment(partner);
            }

            lastTickByPair[pairKey] = nowTick;
            return true;
        }

        /// <summary>Clears all counters. Used on new-game reset.</summary>
        public void Reset()
        {
            perPawnToday.Clear();
            lastTickByPair.Clear();
            colonyToday = 0;
            haveDay = false;
            currentDay = 0;
        }

        // Resets the daily counters when the day changes. The pair-gap map is intentionally kept across
        // days: the gap is a rate limit measured in ticks, independent of the calendar day, and the map
        // is bounded by the number of distinct pawn pairs (small).
        private void RollDayIfNeeded(int dayIndex)
        {
            if (haveDay && dayIndex == currentDay)
            {
                return;
            }

            haveDay = true;
            currentDay = dayIndex;
            perPawnToday.Clear();
            colonyToday = 0;
        }

        private int CountFor(string id)
        {
            int count;
            return perPawnToday.TryGetValue(id, out count) ? count : 0;
        }

        private void Increment(string id)
        {
            int count;
            perPawnToday[id] = (perPawnToday.TryGetValue(id, out count) ? count : 0) + 1;
        }

        // Orders the two ids so (A,B) and (B,A) share one key. A solo entry keys on idA alone.
        private static string PairKey(string a, string b)
        {
            if (b.Length == 0)
            {
                return a;
            }

            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }
    }
}

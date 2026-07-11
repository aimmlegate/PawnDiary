// Pure throttle policy: caps how many accepted-conversation entries the bridge submits, so a chatty
// colony does not flood every diary. NO RimWorld / RimTalk usings — file-linked into the pure tests.
//
// Four independent limits, all enforced in TryReserve:
//   • per-pawn daily cap  — each pawn only stars in so many conversation entries per in-game day.
//   • colony daily cap    — a whole-colony ceiling per day.
//   • pair minimum gap    — the same two pawns cannot generate entries back-to-back.
//   • per-pawn cooldown   — either participant blocks new chat events for one rolling game day.
//
// Daily/colony counters remain transient, but the rolling per-pawn timestamps can be snapshotted by
// the GameComponent so save/load cannot bypass the anti-spam rule.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System.Collections.Generic;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>The tunable throttle limits, passed in from settings/XML policy.</summary>
    public sealed class ThrottleLimits
    {
        /// <summary>Max entries per pawn per day. 0 disables conversation recording.</summary>
        public int perPawnDailyCap;

        /// <summary>Max entries colony-wide per day. 0 disables conversation recording.</summary>
        public int colonyDailyCap;

        /// <summary>Minimum ticks between entries for the same pawn pair. 0 disables the gap.</summary>
        public int pairMinGapTicks;

        /// <summary>Rolling ticks after an accepted chat event during which either pawn is blocked.</summary>
        public int perPawnCooldownTicks;
    }

    /// <summary>
    /// Tracks per-day, per-pair, and rolling per-pawn usage and decides whether a new conversation
    /// entry may be recorded.
    /// Stateful but pure: no RimWorld types, fully unit-testable.
    /// </summary>
    public sealed class ThrottlePolicy
    {
        private readonly Dictionary<string, int> perPawnToday = new Dictionary<string, int>();
        private readonly Dictionary<string, int> lastTickByPair = new Dictionary<string, int>();
        private readonly Dictionary<string, int> lastTickByPawn = new Dictionary<string, int>();
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
            if (limits == null || string.IsNullOrEmpty(idA)
                || limits.perPawnDailyCap <= 0 || limits.colonyDailyCap <= 0)
            {
                return false;
            }

            RollDayIfNeeded(dayIndex);

            string partner = idB ?? string.Empty;
            string pairKey = PairKey(idA, partner);

            // The anti-spam gate is rolling rather than calendar-bound: an event at the end of one
            // day still blocks both pawns until a full configured game-day worth of ticks has passed.
            if (IsPawnOnCooldown(idA, nowTick, limits.perPawnCooldownTicks)
                || (partner.Length > 0
                    && IsPawnOnCooldown(partner, nowTick, limits.perPawnCooldownTicks)))
            {
                return false;
            }

            // Pair minimum gap.
            int lastTick;
            if (limits.pairMinGapTicks > 0
                && lastTickByPair.TryGetValue(pairKey, out lastTick)
                && (nowTick - lastTick) < limits.pairMinGapTicks)
            {
                return false;
            }

            // Colony daily cap.
            if (colonyToday >= limits.colonyDailyCap)
            {
                return false;
            }

            // Per-pawn daily cap (both pawns must have headroom).
            if (CountFor(idA) >= limits.perPawnDailyCap)
            {
                return false;
            }

            if (partner.Length > 0 && CountFor(partner) >= limits.perPawnDailyCap)
            {
                return false;
            }

            // All checks passed: commit the reservation.
            colonyToday++;
            Increment(idA);
            if (partner.Length > 0)
            {
                Increment(partner);
            }

            lastTickByPair[pairKey] = nowTick;
            lastTickByPawn[idA] = nowTick;
            if (partner.Length > 0)
            {
                lastTickByPawn[partner] = nowTick;
            }
            return true;
        }

        /// <summary>True while one pawn is inside the rolling chat-event cooldown.</summary>
        public bool IsPawnOnCooldown(string pawnId, int nowTick, int cooldownTicks)
        {
            if (string.IsNullOrEmpty(pawnId) || cooldownTicks <= 0)
            {
                return false;
            }

            int lastTick;
            if (!lastTickByPawn.TryGetValue(pawnId, out lastTick))
            {
                return false;
            }

            long elapsed = (long)nowTick - lastTick;
            // A hand-edited/debug-rewound tick is treated conservatively as still cooling down.
            return elapsed < 0L || elapsed < cooldownTicks;
        }

        /// <summary>True when either participant is currently blocked by the rolling cooldown.</summary>
        public bool IsEitherPawnOnCooldown(string idA, string idB, int nowTick, int cooldownTicks)
        {
            return IsPawnOnCooldown(idA, nowTick, cooldownTicks)
                || IsPawnOnCooldown(idB, nowTick, cooldownTicks);
        }

        /// <summary>
        /// Reverses a prior successful <see cref="TryReserve"/> for the same ids on the same day, for when
        /// the downstream submission was rejected and the entry never materialized. Decrements the colony
        /// and per-pawn day counts (never below zero), then clears the pair-gap and both pawn-cooldown
        /// markers so nobody is blocked by an entry that did not happen. A no-op if the day has since
        /// rolled (those counters were already cleared). Pass the partner id as "" for a solo reservation.
        /// </summary>
        public void Release(string idA, string idB, int dayIndex, ThrottleLimits limits)
        {
            if (limits == null || string.IsNullOrEmpty(idA))
            {
                return;
            }

            // If the day already rolled since the reservation, RollDayIfNeeded already cleared the day
            // counters, so there is nothing to refund. Do NOT roll the day here — release must not mutate
            // the current-day marker.
            if (!haveDay || dayIndex != currentDay)
            {
                return;
            }

            string partner = idB ?? string.Empty;

            if (colonyToday > 0)
            {
                colonyToday--;
            }

            Decrement(idA);
            if (partner.Length > 0)
            {
                Decrement(partner);
            }

            // Remove rather than restore the previous tick: the entry did not happen, so there is no
            // reason to enforce a pair gap measured from it.
            lastTickByPair.Remove(PairKey(idA, partner));
            lastTickByPawn.Remove(idA);
            if (partner.Length > 0)
            {
                lastTickByPawn.Remove(partner);
            }
        }

        /// <summary>Returns a detached copy suitable for GameComponent value/value Scribing.</summary>
        public Dictionary<string, int> PawnCooldownSnapshot()
        {
            return new Dictionary<string, int>(lastTickByPawn);
        }

        /// <summary>Drops timestamps whose configured rolling window has fully elapsed.</summary>
        public void PruneExpiredPawnCooldowns(int nowTick, int cooldownTicks)
        {
            if (cooldownTicks <= 0)
            {
                lastTickByPawn.Clear();
                return;
            }

            List<string> expired = new List<string>();
            foreach (KeyValuePair<string, int> pair in lastTickByPawn)
            {
                long elapsed = (long)nowTick - pair.Value;
                if (elapsed >= cooldownTicks)
                {
                    expired.Add(pair.Key);
                }
            }

            for (int i = 0; i < expired.Count; i++)
            {
                lastTickByPawn.Remove(expired[i]);
            }
        }

        /// <summary>Restores saved per-pawn timestamps after the static policy is reset on load.</summary>
        public void RestorePawnCooldowns(IDictionary<string, int> saved)
        {
            lastTickByPawn.Clear();
            if (saved == null)
            {
                return;
            }

            foreach (KeyValuePair<string, int> pair in saved)
            {
                if (!string.IsNullOrEmpty(pair.Key) && pair.Value >= 0)
                {
                    lastTickByPawn[pair.Key] = pair.Value;
                }
            }
        }

        /// <summary>Clears all counters. Used on new-game reset.</summary>
        public void Reset()
        {
            perPawnToday.Clear();
            lastTickByPair.Clear();
            lastTickByPawn.Clear();
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

        // Reverses one Increment, dropping the key entirely at zero so the map stays bounded.
        private void Decrement(string id)
        {
            int count;
            if (!perPawnToday.TryGetValue(id, out count))
            {
                return;
            }

            if (count <= 1)
            {
                perPawnToday.Remove(id);
            }
            else
            {
                perPawnToday[id] = count - 1;
            }
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

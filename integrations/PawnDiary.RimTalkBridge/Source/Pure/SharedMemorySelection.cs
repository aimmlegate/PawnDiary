// Pure selection logic for the pair shared-memory block ({{diary_shared}}). NO RimWorld / RimTalk /
// Verse usings: file-linked into the pure test project, must compile without the game.
//
// The impure shell (SharedMemoryInjector) reads a pair's shared diary entries on the MAIN thread,
// maps each to a plain SharedMemoryCandidate, and calls Select(...) to pick a few weighted-random.
// Randomness here is a System.Random seeded from a stable per-pair value the caller passes in —
// NEVER Verse.Rand, which is main-thread global state and is banned from anything a background
// prompt provider can reach (AGENTS.md / SKILL.md). Seeding also makes the pick deterministic under
// test.
//
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>One shared diary memory reduced to the fields selection + formatting need.</summary>
    public sealed class SharedMemoryCandidate
    {
        /// <summary>Resolved entry title (or group label fallback).</summary>
        public string Title;

        /// <summary>One-line prose summary of the entry.</summary>
        public string Summary;

        /// <summary>Human-readable in-game date string.</summary>
        public string Date;

        /// <summary>Game tick of the event; higher = more recent. Drives the recency weight.</summary>
        public int Tick;

        /// <summary>A rare atmosphere cue was present → the moment was more notable.</summary>
        public bool HasAtmosphereCue;

        /// <summary>The entry is a bridge conversation ("a previous interaction") → weighted up.</summary>
        public bool IsConversationEntry;
    }

    /// <summary>
    /// Picks which of a pair's shared memories to surface, weighted by recency and importance.
    /// Deterministic given the same inputs and seed; no side effects.
    /// </summary>
    public static class SharedMemorySelection
    {
        // Importance multiplier bonuses. Parser-style tuning constants (not player settings): they
        // shape how much a rare cue or a "previous interaction" outweighs a plain memory of equal age.
        // Kept in code as named constants per the plan; revisit only with playtest data.
        private const double CueBonus = 1.0;
        private const double ConversationBonus = 0.5;

        /// <summary>
        /// Stable, order-independent key for a pawn pair: the two unique load ids sorted and joined
        /// with '|'. Identical on the read and write sides so a pair maps to one cache slot regardless
        /// of who is speaking.
        /// </summary>
        public static string PairKey(string idA, string idB)
        {
            string a = idA ?? string.Empty;
            string b = idB ?? string.Empty;
            // Ordinal compare so the key is culture-invariant and stable across sessions.
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }

        /// <summary>
        /// Weighted-random selection WITHOUT replacement of up to <paramref name="maxPick"/> candidates.
        /// Weight = recency (newest-first rank) × importance multiplier (cue / conversation bonuses).
        /// Uses a <see cref="System.Random"/> seeded with <paramref name="seed"/> so a pair's shown
        /// memories are stable within a refresh window and reproducible under test. The returned list is
        /// ordered newest-first for display stability. Empty input, <paramref name="maxPick"/> &lt;= 0
        /// returns empty; <paramref name="maxPick"/> &gt;= count returns all (still newest-first).
        /// </summary>
        public static List<SharedMemoryCandidate> Select(
            List<SharedMemoryCandidate> candidates,
            int maxPick,
            int seed)
        {
            List<SharedMemoryCandidate> result = new List<SharedMemoryCandidate>();
            if (candidates == null || candidates.Count == 0 || maxPick <= 0)
            {
                return result;
            }

            // Build a working set tagged with the original index (stable tiebreak) and a fixed weight.
            // ageRank is assigned newest-first over the WHOLE set, so recencyWeight does not shift as we
            // remove picks.
            List<Weighted> pool = new List<Weighted>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] != null)
                {
                    pool.Add(new Weighted { Candidate = candidates[i], Index = i });
                }
            }

            if (pool.Count == 0)
            {
                return result;
            }

            // Newest first; ties broken by original index so ranks are deterministic.
            pool.Sort((x, y) =>
            {
                int byTick = y.Candidate.Tick.CompareTo(x.Candidate.Tick);
                return byTick != 0 ? byTick : x.Index.CompareTo(y.Index);
            });

            for (int rank = 0; rank < pool.Count; rank++)
            {
                double recencyWeight = 1.0 / (1 + rank);
                double importance = 1.0
                    + (pool[rank].Candidate.HasAtmosphereCue ? CueBonus : 0.0)
                    + (pool[rank].Candidate.IsConversationEntry ? ConversationBonus : 0.0);
                pool[rank].Weight = recencyWeight * importance;
            }

            Random rng = new Random(seed);
            int take = maxPick < pool.Count ? maxPick : pool.Count;
            for (int n = 0; n < take; n++)
            {
                int chosen = PickWeighted(pool, rng);
                if (chosen < 0)
                {
                    break;
                }

                result.Add(pool[chosen].Candidate);
                pool.RemoveAt(chosen);
            }

            // Present newest-first regardless of the random draw order, for a stable-looking block.
            result.Sort((x, y) => y.Tick.CompareTo(x.Tick));
            return result;
        }

        // Roulette-wheel pick over the remaining pool. Returns the index of the chosen element, or -1
        // when the pool has no positive weight left (defensive; weights are always positive here).
        private static int PickWeighted(List<Weighted> pool, Random rng)
        {
            double total = 0.0;
            for (int i = 0; i < pool.Count; i++)
            {
                total += pool[i].Weight;
            }

            if (total <= 0.0)
            {
                return pool.Count > 0 ? 0 : -1;
            }

            double roll = rng.NextDouble() * total;
            double cumulative = 0.0;
            for (int i = 0; i < pool.Count; i++)
            {
                cumulative += pool[i].Weight;
                if (roll < cumulative)
                {
                    return i;
                }
            }

            // Floating-point drift guard: return the last element if the roll landed at the very end.
            return pool.Count - 1;
        }

        private sealed class Weighted
        {
            public SharedMemoryCandidate Candidate;
            public int Index;
            public double Weight;
        }
    }
}

// Pure lore-seed planning (design/LORE_MEMORY_SEED_PLAN.md §7). PlanInitial builds a pawn's
// one-time initial target roster: eligibility matrices over stable Def-name/category facts,
// lifetime capacity enforcement, one reserved pawn-specific slot (core-specific preferred),
// deterministic weighted sampling without replacement, and mutexGroup exclusion that lives ONLY
// inside this single construction. The impure adapter persists the returned roster BEFORE any
// deposit attempt; retries never call the sampling path again (§7.1).
//
// PlanProgression is deferred L5; its facts/history contracts already exist so nothing here is
// designed into a corner.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers"). This file must stay free of
// Verse/Unity/settings/Def references so the pure test project can link it directly. RNG is
// stable System.Random from a caller-supplied seed — never Verse.Rand (§16 G12).
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Deterministic initial lore-seed roster construction.</summary>
    internal static class LoreSeedPlanner
    {
        /// <summary>
        /// Plans the pawn's complete initial target roster (§7.1). Returns an empty list when the
        /// policy is disabled, capacity is exhausted, or nothing is eligible. Never mutates its
        /// inputs. The same candidates/facts/policy/seed always return the same roster.
        /// </summary>
        public static List<LoreSeedPick> PlanInitial(
            List<LoreSeedCandidate> candidates,
            LoreSeedPawnFacts pawnFacts,
            LoreSeedPolicy policy,
            int deterministicSeed)
        {
            List<LoreSeedPick> picks = new List<LoreSeedPick>();
            LoreSeedPolicy safePolicy = policy ?? new LoreSeedPolicy();
            if (!safePolicy.enabled || pawnFacts == null || candidates == null)
            {
                return picks;
            }

            // Lifetime capacities (§6): the roster is planned once, but exclusions stay defensive
            // so a partial legacy state can never over-allocate.
            int remainingSlots = Math.Max(0,
                safePolicy.maxInitialSeeds - Count(pawnFacts.initialTargetDefNames));
            int coreCapacity = Math.Max(0,
                safePolicy.maxCoreSeedsLifetime - Count(pawnFacts.coreDefNamesEverDeposited));
            if (remainingSlots == 0)
            {
                return picks;
            }

            List<LoreSeedCandidate> pool = EligiblePool(candidates, pawnFacts);
            if (pool.Count == 0)
            {
                return picks;
            }

            Random rng = new Random(deterministicSeed);

            // Reserved pawn-specific slot (§7.1 step 3): prefer a core-specific candidate while
            // core capacity remains, otherwise any specific candidate. When no specific candidate
            // is eligible the quota is simply unfilled — generic seeds fill the roster instead and
            // the impure adapter owns the bounded diagnostic (§7.1).
            int reserved = Math.Min(Math.Max(0, safePolicy.minSpecificInitialSeeds), remainingSlots);
            for (int i = 0; i < reserved; i++)
            {
                LoreSeedCandidate specificPick = SampleSpecific(pool, coreCapacity > 0, rng);
                if (specificPick == null)
                {
                    break;
                }

                bool core = IsCore(specificPick);
                picks.Add(new LoreSeedPick
                {
                    seedDefName = specificPick.seedDefName,
                    core = core,
                    specific = true
                });
                if (core)
                {
                    coreCapacity--;
                }

                remainingSlots--;
                RemoveSelectedAndMutexSiblings(pool, specificPick);
            }

            // Fill remaining slots by weighted sampling without replacement (§7.1 step 4). A core
            // candidate drawn past the core lifetime capacity is discarded from the pool without
            // consuming a slot — capacity is allocation policy, not luck.
            while (remainingSlots > 0 && pool.Count > 0)
            {
                LoreSeedCandidate candidate = SampleWeighted(pool, rng);
                if (candidate == null)
                {
                    break;
                }

                bool core = IsCore(candidate);
                if (core && coreCapacity <= 0)
                {
                    pool.Remove(candidate);
                    continue;
                }

                picks.Add(new LoreSeedPick
                {
                    seedDefName = candidate.seedDefName,
                    core = core,
                    specific = IsSpecific(candidate)
                });
                if (core)
                {
                    coreCapacity--;
                }

                remainingSlots--;
                RemoveSelectedAndMutexSiblings(pool, candidate);
            }

            return picks;
        }

        /// <summary>
        /// Plans AT MOST ONE progression lore seed for one successfully registered
        /// identity-changing event (§7.2, deferred L5). Filters to progression/both candidates
        /// whose progressionEventDefNames contain the EXACT registered event Def token and whose
        /// pawn constraints match, excludes every name the pawn has ever targeted or received,
        /// enforces the remaining progression and core lifetime capacities, then draws one
        /// deterministic weighted pick. mutexGroup deliberately has no cross-event semantics —
        /// one event yields one seed and the lifetime history blocks exact-Def reuse only.
        /// </summary>
        public static LoreSeedPick PlanProgression(
            List<LoreSeedCandidate> candidates,
            LoreSeedPawnFacts pawnFacts,
            LoreSeedProgressionFacts progressionFacts,
            LoreSeedPolicy policy,
            int deterministicSeed)
        {
            LoreSeedPolicy safePolicy = policy ?? new LoreSeedPolicy();
            if (!safePolicy.enabled || pawnFacts == null || candidates == null
                || progressionFacts == null
                || string.IsNullOrWhiteSpace(progressionFacts.eventDefName))
            {
                return null;
            }

            int remainingProgression = Math.Max(0,
                safePolicy.maxProgressionSeedsLifetime - Count(pawnFacts.progressionDefNamesEverDeposited));
            if (remainingProgression == 0)
            {
                return null;
            }

            int coreCapacity = Math.Max(0,
                safePolicy.maxCoreSeedsLifetime - Count(pawnFacts.coreDefNamesEverDeposited));
            List<LoreSeedCandidate> pool = new List<LoreSeedCandidate>();
            for (int i = 0; i < candidates.Count; i++)
            {
                LoreSeedCandidate candidate = candidates[i];
                if (!IsEligibleForProgression(candidate, pawnFacts, progressionFacts.eventDefName))
                {
                    continue;
                }

                if (IsCore(candidate) && coreCapacity <= 0)
                {
                    continue;
                }

                if (!ContainsCandidate(pool, candidate.seedDefName))
                {
                    pool.Add(candidate);
                }
            }

            if (pool.Count == 0)
            {
                return null;
            }

            LoreSeedCandidate picked = SampleWeighted(pool, new Random(deterministicSeed));
            return picked == null ? null : new LoreSeedPick
            {
                seedDefName = picked.seedDefName,
                core = IsCore(picked),
                specific = IsSpecific(picked)
            };
        }

        /// <summary>
        /// Progression eligibility (§7.2): progression/both usage, the exact registered event Def
        /// token, the shared pawn-constraint matrix, and one-Def-per-pawn-lifetime exclusion.
        /// </summary>
        public static bool IsEligibleForProgression(
            LoreSeedCandidate candidate, LoreSeedPawnFacts facts, string eventDefName)
        {
            if (candidate == null || facts == null
                || string.IsNullOrWhiteSpace(candidate.seedDefName))
            {
                return false;
            }

            if (candidate.usage != LoreSeedTokens.UsageProgression
                && candidate.usage != LoreSeedTokens.UsageBoth)
            {
                return false;
            }

            if (!(candidate.weight > 0f) || float.IsInfinity(candidate.weight))
            {
                return false;
            }

            if (!ContainsName(candidate.progressionEventDefNames, eventDefName))
            {
                return false;
            }

            return !IsUsedName(candidate.seedDefName, facts)
                && MatchesPawnConstraints(candidate, facts);
        }

        /// <summary>
        /// Clamps the authored narrative-age offset to the pawn's biological age (§3.1, §16 G2):
        /// no configuration can make a pawn remember a time before their own life.
        /// </summary>
        public static int ClampNarrativeOffset(int policyOffsetTicks, long biologicalAgeTicks)
        {
            long offset = Math.Max(0, policyOffsetTicks);
            long age = Math.Max(0L, biologicalAgeTicks);
            return (int)Math.Min(offset, Math.Min(age, int.MaxValue));
        }

        /// <summary>
        /// True when the candidate carries any positive pawn constraint (§7.1 step 3): a backstory
        /// category or exact Def, a xenotype, or a hediff requirement.
        /// </summary>
        public static bool IsSpecific(LoreSeedCandidate candidate)
        {
            return candidate != null
                && (Count(candidate.backstoryCategories) > 0
                    || Count(candidate.backstoryDefNames) > 0
                    || Count(candidate.xenotypeDefNames) > 0
                    || Count(candidate.hediffDefNames) > 0);
        }

        /// <summary>
        /// Initial-plan eligibility (§7.1 step 1): initial/both usage, the shared pawn-constraint
        /// matrix (any-of within a populated positive list, all populated positive list TYPES
        /// required, exclusion lists always honored), and every name the pawn has ever targeted
        /// or received excluded (§6: one Def at most once per pawn).
        /// </summary>
        public static bool IsEligible(LoreSeedCandidate candidate, LoreSeedPawnFacts facts)
        {
            if (candidate == null || facts == null
                || string.IsNullOrWhiteSpace(candidate.seedDefName))
            {
                return false;
            }

            if (candidate.usage != LoreSeedTokens.UsageInitial
                && candidate.usage != LoreSeedTokens.UsageBoth)
            {
                return false;
            }

            if (!(candidate.weight > 0f) || float.IsInfinity(candidate.weight))
            {
                return false;
            }

            return !IsUsedName(candidate.seedDefName, facts)
                && MatchesPawnConstraints(candidate, facts);
        }

        /// <summary>One Def at most once per pawn across roster and both lifetime histories (§6).</summary>
        private static bool IsUsedName(string seedDefName, LoreSeedPawnFacts facts)
        {
            return ContainsName(facts.initialTargetDefNames, seedDefName)
                || ContainsName(facts.progressionDefNamesEverDeposited, seedDefName)
                || ContainsName(facts.coreDefNamesEverDeposited, seedDefName);
        }

        /// <summary>
        /// The shared pawn-constraint matrix: any-of within a populated positive list, all
        /// populated positive list TYPES required together, exclusion lists always honored.
        /// </summary>
        private static bool MatchesPawnConstraints(LoreSeedCandidate candidate, LoreSeedPawnFacts facts)
        {
            if (Count(candidate.backstoryCategories) > 0
                && !AnyOverlap(candidate.backstoryCategories, facts.backstoryCategories))
            {
                return false;
            }

            if (AnyOverlap(candidate.excludeBackstoryCategories, facts.backstoryCategories))
            {
                return false;
            }

            if (Count(candidate.backstoryDefNames) > 0
                && !AnyOverlap(candidate.backstoryDefNames, facts.backstoryDefNames))
            {
                return false;
            }

            if (AnyOverlap(candidate.excludeBackstoryDefNames, facts.backstoryDefNames))
            {
                return false;
            }

            if (Count(candidate.xenotypeDefNames) > 0
                && !ContainsName(candidate.xenotypeDefNames, facts.xenotypeDefName))
            {
                return false;
            }

            if (Count(candidate.hediffDefNames) > 0
                && !AnyOverlap(candidate.hediffDefNames, facts.hediffDefNames))
            {
                return false;
            }

            return true;
        }

        private static bool IsCore(LoreSeedCandidate candidate)
        {
            return candidate != null && candidate.retentionTier == LoreSeedTokens.TierCore;
        }

        private static List<LoreSeedCandidate> EligiblePool(
            List<LoreSeedCandidate> candidates, LoreSeedPawnFacts facts)
        {
            List<LoreSeedCandidate> pool = new List<LoreSeedCandidate>();
            for (int i = 0; i < candidates.Count; i++)
            {
                LoreSeedCandidate candidate = candidates[i];
                if (IsEligible(candidate, facts) && !ContainsCandidate(pool, candidate.seedDefName))
                {
                    pool.Add(candidate);
                }
            }

            return pool;
        }

        /// <summary>
        /// Draws the reserved specific pick: core-specific subset first while core capacity
        /// remains, then any specific candidate. Weighted like every other draw.
        /// </summary>
        private static LoreSeedCandidate SampleSpecific(
            List<LoreSeedCandidate> pool, bool coreCapacityRemains, Random rng)
        {
            if (coreCapacityRemains)
            {
                List<LoreSeedCandidate> coreSpecific = new List<LoreSeedCandidate>();
                for (int i = 0; i < pool.Count; i++)
                {
                    if (IsSpecific(pool[i]) && IsCore(pool[i]))
                    {
                        coreSpecific.Add(pool[i]);
                    }
                }

                if (coreSpecific.Count > 0)
                {
                    return SampleWeighted(coreSpecific, rng);
                }
            }

            List<LoreSeedCandidate> specific = new List<LoreSeedCandidate>();
            for (int i = 0; i < pool.Count; i++)
            {
                if (IsSpecific(pool[i]) && (coreCapacityRemains || !IsCore(pool[i])))
                {
                    specific.Add(pool[i]);
                }
            }

            return specific.Count > 0 ? SampleWeighted(specific, rng) : null;
        }

        /// <summary>One deterministic weighted draw. The list is never mutated here.</summary>
        private static LoreSeedCandidate SampleWeighted(List<LoreSeedCandidate> pool, Random rng)
        {
            double total = 0d;
            for (int i = 0; i < pool.Count; i++)
            {
                total += Math.Max(0f, pool[i].weight);
            }

            if (total <= 0d)
            {
                return null;
            }

            double roll = rng.NextDouble() * total;
            double cursor = 0d;
            for (int i = 0; i < pool.Count; i++)
            {
                cursor += Math.Max(0f, pool[i].weight);
                if (roll < cursor)
                {
                    return pool[i];
                }
            }

            return pool[pool.Count - 1];
        }

        /// <summary>
        /// Mutex semantics (§7.1 step 5): selecting a Def removes its mutexGroup siblings from
        /// THIS construction's pool only. There is no cross-retry or lifetime mutex state.
        /// </summary>
        private static void RemoveSelectedAndMutexSiblings(
            List<LoreSeedCandidate> pool, LoreSeedCandidate selected)
        {
            string group = (selected.mutexGroup ?? string.Empty).Trim();
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                LoreSeedCandidate candidate = pool[i];
                bool sameGroup = group.Length > 0
                    && string.Equals((candidate.mutexGroup ?? string.Empty).Trim(), group,
                        StringComparison.OrdinalIgnoreCase);
                if (candidate == selected || sameGroup)
                {
                    pool.RemoveAt(i);
                }
            }
        }

        private static bool AnyOverlap(List<string> left, List<string> right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (ContainsName(right, left[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsName(List<string> values, string name)
        {
            if (values == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i])
                    && string.Equals(values[i].Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsCandidate(List<LoreSeedCandidate> pool, string seedDefName)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (string.Equals(pool[i].seedDefName, seedDefName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int Count(List<string> values)
        {
            return values == null ? 0 : values.Count;
        }
    }
}

// Pure associative recall for the pawn memory subsystem (design/MEMORY_SYSTEM_DESIGN.md §8).
// Given one pawn's fragment snapshots and a query built by the SAME extraction used at deposit
// time, this selector surfaces 0..2 memories: the single best direct match, plus — behind a
// seeded coin flip — one 1-hop spreading-activation pick that shares a person/place/theme with
// the direct pick yet is NOT itself a direct match. It also renders the frozen memoryContext
// prompt field. All randomness flows from the query's stable seed, so recall is reproducible.
//
// The selector NEVER mutates its inputs: bumping lastRecalledTick/recallCount and freezing the
// result onto the event are the impure applier's job (design §8.5, step 6).
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers"). This file must stay free of
// Verse/Unity/settings/Def references so the pure test project can link it directly.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Similarity scoring, 1-hop spreading activation, and memoryContext rendering.</summary>
    internal static class MemoryRecallSelector
    {
        private sealed class ScoredFragment
        {
            public MemoryFragmentSnapshot fragment;
            public float score;
        }

        /// <summary>
        /// Runs both gates, direct scoring, the optional 1-hop spread, and rendering. Returns an
        /// empty result (empty memoryContext, diagnostic reason) whenever any gate fails, so
        /// nothing downstream is special-cased for young colonies or unlucky rolls.
        /// </summary>
        public static MemoryRecallResult Recall(
            MemoryRecallQuery query,
            List<MemoryFragmentSnapshot> fragments,
            MemoryPolicySnapshot policy)
        {
            MemoryRecallResult result = new MemoryRecallResult();
            MemoryPolicySnapshot safePolicy = policy ?? MemoryPolicySnapshot.CreateDefault();
            if (!safePolicy.enabled)
            {
                result.diagnostics.Add(MemoryDiagnosticTokens.PolicyDisabled);
                return result;
            }

            if (query == null)
            {
                result.diagnostics.Add(MemoryDiagnosticTokens.NoQuery);
                return result;
            }

            List<MemoryFragmentSnapshot> usable = UsableFragments(fragments);
            if (usable.Count < Math.Max(0, safePolicy.minFragmentsForRecall))
            {
                // First-entry / young-colony behavior: nothing to associate with yet.
                result.diagnostics.Add(MemoryDiagnosticTokens.StoreTooSmall);
                return result;
            }

            // One seeded Random per recall: the FIRST draw is the recall gate, the SECOND the
            // spread gate (consumed only when a direct pick exists). Same seed, same RimWorld-runtime
            // outcome. Tests discover gate seeds at runtime instead of pinning a framework-specific
            // sequence. If exact sequences must ever survive a runtime change, replace this with a
            // tiny pinned PURE PRNG; do not introduce Verse.Rand into this pure selector.
            Random rng = new Random(query.seed);
            if (rng.NextDouble() >= Clamp01(safePolicy.recallGateChance))
            {
                // Memories must not flavor literally every page.
                result.diagnostics.Add(MemoryDiagnosticTokens.RecallGateMiss);
                return result;
            }

            string currentEventId = query.currentEventId ?? string.Empty;
            List<ScoredFragment> directScores = new List<ScoredFragment>();
            for (int i = 0; i < usable.Count; i++)
            {
                MemoryFragmentSnapshot candidate = usable[i];
                // Self-echo guard: an event can never recall the fragment it deposited (recall
                // runs BEFORE deposit, but staged replays make this belt-and-braces).
                if (EqualsIgnoreCase(candidate.sourceEventId, currentEventId))
                {
                    continue;
                }

                float score = DirectScoreFormula(candidate, query.tags, query.keywords,
                    query.currentTick, safePolicy);
                directScores.Add(new ScoredFragment { fragment = candidate, score = score });
            }

            ScoredFragment direct = BestAtOrAbove(directScores, Math.Max(0f, safePolicy.minDirectScore));
            if (direct == null)
            {
                result.diagnostics.Add(MemoryDiagnosticTokens.NoDirectPick);
                return result;
            }

            result.picks.Add(new MemoryRecallPick
            {
                memoryId = direct.fragment.memoryId,
                kind = MemoryRecallPick.Direct,
                score = direct.score
            });
            result.diagnostics.Add(MemoryDiagnosticTokens.SelectedDirect + ":" + direct.fragment.memoryId);

            TrySpread(query, safePolicy, rng, usable, directScores, direct, result);
            RenderAndFitBudget(result, usable, query.currentTick, safePolicy);
            return result;
        }

        /// <summary>
        /// The 1-hop spread (design §8.3). Reuses the direct scores twice: fragments that matched
        /// the ORIGINAL query directly are excluded (the hop exists to surface the unrelated), and
        /// the direct pick's tags/keywords EXPAND the query so a shared person can bridge across.
        /// </summary>
        private static void TrySpread(
            MemoryRecallQuery query,
            MemoryPolicySnapshot policy,
            Random rng,
            List<MemoryFragmentSnapshot> usable,
            List<ScoredFragment> directScores,
            ScoredFragment direct,
            MemoryRecallResult result)
        {
            if (rng.NextDouble() >= Clamp01(policy.spreadGateChance))
            {
                result.diagnostics.Add(MemoryDiagnosticTokens.SpreadGateMiss);
                return;
            }

            List<string> expandedTags = Union(direct.fragment.tags, query.tags);
            List<string> expandedKeywords = Union(direct.fragment.keywords, query.keywords);
            float minDirect = Math.Max(0f, policy.minDirectScore);
            float damping = Math.Max(0f, policy.spreadDamping);
            float minSpread = Math.Max(0f, policy.minSpreadScore);
            string currentEventId = query.currentEventId ?? string.Empty;

            List<ScoredFragment> hopScores = new List<ScoredFragment>();
            for (int i = 0; i < directScores.Count; i++)
            {
                ScoredFragment scored = directScores[i];
                MemoryFragmentSnapshot candidate = scored.fragment;
                if (candidate == direct.fragment
                    || EqualsIgnoreCase(candidate.sourceEventId, currentEventId)
                    || (!string.IsNullOrEmpty(direct.fragment.sourceEventId)
                        && EqualsIgnoreCase(candidate.sourceEventId, direct.fragment.sourceEventId)))
                {
                    continue;
                }

                // The associative filter: anything that already matched the original query
                // directly is just another direct match, not an association.
                if (scored.score >= minDirect)
                {
                    continue;
                }

                float hopScore = DirectScoreFormula(candidate, expandedTags, expandedKeywords,
                    query.currentTick, policy) * damping;
                hopScores.Add(new ScoredFragment { fragment = candidate, score = hopScore });
            }

            ScoredFragment hop = BestAtOrAbove(hopScores, minSpread);
            if (hop == null)
            {
                result.diagnostics.Add(MemoryDiagnosticTokens.NoSpreadPick);
                return;
            }

            result.picks.Add(new MemoryRecallPick
            {
                memoryId = hop.fragment.memoryId,
                kind = MemoryRecallPick.Associative,
                score = hop.score
            });
            result.diagnostics.Add(MemoryDiagnosticTokens.SelectedAssociative + ":" + hop.fragment.memoryId);
        }

        /// <summary>
        /// The direct score (design §8.2): saturated tag/keyword overlap, blended by policy
        /// weights, boosted by importance salience, faded by recency decay (half-life with a
        /// floor — old memories fade but never vanish), zeroed for same-day fragments, and
        /// quartered inside the anti-repetition cooldown.
        /// </summary>
        private static float DirectScoreFormula(
            MemoryFragmentSnapshot fragment,
            List<string> queryTags,
            List<string> queryKeywords,
            int currentTick,
            MemoryPolicySnapshot policy)
        {
            int tagOverlap = CountOverlap(fragment.tags, queryTags, StringComparison.Ordinal);
            int keywordOverlap = CountOverlap(fragment.keywords, queryKeywords, StringComparison.OrdinalIgnoreCase);
            if (tagOverlap == 0 && keywordOverlap == 0)
            {
                return 0f;
            }

            float tagScore = Math.Min(1f, tagOverlap / (float)Math.Max(1, policy.tagSaturationCount));
            float keywordScore = Math.Min(1f, keywordOverlap / (float)Math.Max(1, policy.keywordSaturationCount));
            float baseScore = Math.Max(0f, policy.tagWeight) * tagScore
                + Math.Max(0f, policy.keywordWeight) * keywordScore;

            float salience = 0.5f + Clamp01(fragment.importance);
            int age = Math.Max(0, currentTick - fragment.createdTick);
            double halfLives = age / (double)Math.Max(1, policy.recencyHalfLifeTicks);
            float decay = Math.Max(Clamp01(policy.recencyFloor), (float)Math.Pow(0.5d, halfLives));
            float score = baseScore * salience * decay;

            // Never echo this morning: same-day fragments score zero regardless of overlap.
            // The minimum-age guard alone uses the EFFECTIVE narrative age (real age + authored
            // lore offset, LORE_MEMORY_SEED_PLAN §3.1) so an initial lore seed can surface on the
            // pawn's very first prompt. Recency decay above and the cooldown below deliberately
            // keep using real ticks: a freshly deposited row must not look old to scoring.
            if (EffectiveNarrativeAge(age, fragment) < Math.Max(0, policy.minRecallAgeTicks))
            {
                return 0f;
            }

            // Core lore hard cooldown (LORE_MEMORY_SEED_PLAN §4): after a core identity seed has
            // actually surfaced in a prompt, it stays INELIGIBLE for the long XML-tunable window
            // (default 20 days) rather than merely penalized. A freshly deposited core seed
            // (lastRecalledTick == createdTick) has never surfaced and is immediately eligible.
            int sinceRecall = currentTick - fragment.lastRecalledTick;
            bool recalledBefore = fragment.lastRecalledTick > fragment.createdTick && sinceRecall >= 0;
            if (recalledBefore
                && !string.IsNullOrEmpty(fragment.loreSeedDefName)
                && Clamp01(fragment.importance) >= Clamp01(policy.coreImportanceThreshold)
                && sinceRecall < Math.Max(0, policy.coreLoreRecallCooldownTicks))
            {
                return 0f;
            }

            // Anti-repetition without any extra recently-used list: a fragment recalled inside
            // the cooldown window simply scores at quarter strength.
            if (recalledBefore && sinceRecall < Math.Max(0, policy.recallCooldownTicks))
            {
                score *= Clamp01(policy.repetitionPenaltyFactor);
            }

            return score;
        }

        /// <summary>
        /// Renders picks as "- (age label) text" lines and enforces the character budget by
        /// DROPPING whole picks — associative first, then direct — never truncating a fragment
        /// mid-text (the FitsCharacterBudget convention). UsableFragments already excludes blank
        /// text before scoring; the defensive check here protects against future lookup changes.
        /// </summary>
        private static void RenderAndFitBudget(
            MemoryRecallResult result,
            List<MemoryFragmentSnapshot> usable,
            int currentTick,
            MemoryPolicySnapshot policy)
        {
            for (int i = result.picks.Count - 1; i >= 0; i--)
            {
                MemoryFragmentSnapshot fragment = FindByMemoryId(usable, result.picks[i].memoryId);
                if (fragment == null || string.IsNullOrWhiteSpace(fragment.text))
                {
                    result.picks.RemoveAt(i);
                }
            }

            // Whole-line ceiling (LORE_MEMORY_SEED_PLAN §9): never deliver more lines than the
            // universal cap, dropping the associative pick first. Applies before the character
            // budget so the surviving-pick list always equals the delivered lines exactly.
            int maxLines = Math.Max(0, policy.memoryContextMaxLines);
            while (result.picks.Count > maxLines)
            {
                result.picks.RemoveAt(result.picks.Count - 1);
                result.diagnostics.Add(MemoryDiagnosticTokens.LineCapDroppedPick);
            }

            int budget = Math.Max(0, policy.memoryContextMaxChars);
            string rendered = Render(result.picks, usable, currentTick, policy);
            if (rendered.Length > budget && result.picks.Count > 1)
            {
                result.picks.RemoveAt(result.picks.Count - 1); // the associative pick is always last
                result.diagnostics.Add(MemoryDiagnosticTokens.BudgetDroppedAssociative);
                rendered = Render(result.picks, usable, currentTick, policy);
            }

            if (rendered.Length > budget && result.picks.Count > 0)
            {
                result.picks.RemoveAt(result.picks.Count - 1);
                result.diagnostics.Add(MemoryDiagnosticTokens.BudgetDroppedDirect);
                rendered = string.Empty;
            }

            result.memoryContext = rendered;
        }

        private static string Render(
            List<MemoryRecallPick> picks,
            List<MemoryFragmentSnapshot> usable,
            int currentTick,
            MemoryPolicySnapshot policy)
        {
            List<string> lines = new List<string>();
            for (int i = 0; i < picks.Count; i++)
            {
                MemoryFragmentSnapshot fragment = FindByMemoryId(usable, picks[i].memoryId);
                if (fragment == null)
                {
                    continue;
                }

                // The rendered age band reflects the pawn's NARRATIVE sense of time (§3.1): an
                // initial lore seed reads as an old memory even though its row is brand new.
                int age = Math.Max(0, currentTick - fragment.createdTick);
                string label = AgeLabel(policy, EffectiveNarrativeAge(age, fragment));
                string text = fragment.text.Trim();
                lines.Add(label.Length == 0 ? "- " + text : "- (" + label + ") " + text);
            }

            return string.Join("\n", lines.ToArray());
        }

        /// <summary>
        /// Real age plus the authored lore narrative offset (LORE_MEMORY_SEED_PLAN §3.1). Long
        /// arithmetic so a large authored offset near an old real age can never wrap negative.
        /// Zero offset — every lived memory — makes this identical to the real age.
        /// </summary>
        private static long EffectiveNarrativeAge(int realAge, MemoryFragmentSnapshot fragment)
        {
            return (long)realAge + Math.Max(0, fragment.narrativeAgeOffsetTicks);
        }

        /// <summary>First band whose maxAgeTicks covers the age wins; the last band is the else.</summary>
        private static string AgeLabel(MemoryPolicySnapshot policy, long age)
        {
            string fallback = string.Empty;
            if (policy.ageBands != null)
            {
                for (int i = 0; i < policy.ageBands.Count; i++)
                {
                    MemoryAgeBand band = policy.ageBands[i];
                    if (band == null || string.IsNullOrWhiteSpace(band.label))
                    {
                        continue;
                    }

                    fallback = band.label.Trim();
                    if (age <= band.maxAgeTicks)
                    {
                        return fallback;
                    }
                }
            }

            return fallback;
        }

        /// <summary>
        /// The single highest scorer at or above the threshold, or null. Ordering is fully
        /// deterministic: score desc, then newer createdTick, then memoryId ordinal (mirrors
        /// NarrativeContextSelector.CompareScoredCandidates).
        /// </summary>
        private static ScoredFragment BestAtOrAbove(List<ScoredFragment> scored, float threshold)
        {
            if (scored.Count == 0)
            {
                return null;
            }

            scored.Sort(CompareScoredFragments);
            return scored[0].score >= threshold ? scored[0] : null;
        }

        private static int CompareScoredFragments(ScoredFragment left, ScoredFragment right)
        {
            int score = right.score.CompareTo(left.score);
            if (score != 0)
            {
                return score;
            }

            int tick = right.fragment.createdTick.CompareTo(left.fragment.createdTick);
            return tick != 0 ? tick : string.Compare(left.fragment.memoryId, right.fragment.memoryId,
                StringComparison.Ordinal);
        }

        private static List<MemoryFragmentSnapshot> UsableFragments(List<MemoryFragmentSnapshot> fragments)
        {
            List<MemoryFragmentSnapshot> usable = new List<MemoryFragmentSnapshot>();
            if (fragments == null)
            {
                return usable;
            }

            for (int i = 0; i < fragments.Count; i++)
            {
                // A memory that cannot render must never compete for the one direct slot. If a
                // blank-text row won scoring and were dropped only later, it could hide a valid
                // runner-up or leave an associative pick with no direct anchor.
                if (fragments[i] != null
                    && !string.IsNullOrWhiteSpace(fragments[i].memoryId)
                    && !string.IsNullOrWhiteSpace(fragments[i].text))
                {
                    usable.Add(fragments[i]);
                }
            }

            return usable;
        }

        private static List<string> Union(List<string> first, List<string> second)
        {
            List<string> union = new List<string>();
            AddDistinct(union, first);
            AddDistinct(union, second);
            return union;
        }

        private static void AddDistinct(List<string> target, List<string> values)
        {
            if (values == null)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]) && !ContainsOrdinal(target, values[i].Trim()))
                {
                    target.Add(values[i].Trim());
                }
            }
        }

        private static MemoryFragmentSnapshot FindByMemoryId(List<MemoryFragmentSnapshot> fragments, string memoryId)
        {
            for (int i = 0; i < fragments.Count; i++)
            {
                if (EqualsIgnoreCase(fragments[i].memoryId, memoryId))
                {
                    return fragments[i];
                }
            }

            return null;
        }

        private static int CountOverlap(List<string> left, List<string> right, StringComparison comparison)
        {
            if (left == null || right == null)
            {
                return 0;
            }

            int overlap = 0;
            for (int i = 0; i < left.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(left[i]))
                {
                    continue;
                }

                for (int j = 0; j < right.Count; j++)
                {
                    if (!string.IsNullOrWhiteSpace(right[j])
                        && string.Equals(left[i].Trim(), right[j].Trim(), comparison))
                    {
                        overlap++;
                        break;
                    }
                }
            }

            return overlap;
        }

        private static bool ContainsOrdinal(List<string> values, string target)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], target, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EqualsIgnoreCase(string left, string right)
        {
            return !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right)
                && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}

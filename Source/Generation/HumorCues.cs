// Humor cue selector: the hidden, always-on engine that occasionally injects one structural
// writing-license sentence into a first-person diary prompt. This is IMPURE (RNG + DefDatabase +
// tuning read), so it lives here in Source/Generation next to PromptEnchantments — the pure
// pipeline never touches it. The chosen cue's rule text is folded into the persona voice block at
// the adapter, so no planner or assembler change is needed.
//
// Why "structural cue" and not "be funny": small local models latch onto any mention of humor and
// produce stale standup. Each cue instead names a concrete sentence shape (a flat understatement, a
// dry inventory, a clerical tally) that yields a subtly droll or deadpan entry without ever naming
// comedy. Flavor matches stakes: Light (dry/absurdist) for mundane events, Gallows (dark/deadpan)
// for high-stakes events. At most one cue per entry, and only on ~humorChance of eligible entries.
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Picks zero or one <see cref="DiaryHumorCueDef"/> rule for a diary event. Impure: rolls RNG,
    /// reads <see cref="DiaryTuning"/>, and walks the loaded cue defs. Returns the winner's
    /// <c>rule</c> text, or <c>string.Empty</c> when humor is not selected this entry.
    /// </summary>
    internal static class HumorCues
    {
        /// <summary>
        /// Returns the chosen cue's rule text for this event, or <c>string.Empty</c> when no cue is
        /// selected. At most one cue per entry.
        /// </summary>
        public static string CueFor(DiaryEvent diaryEvent)
        {
            if (diaryEvent == null)
            {
                return string.Empty;
            }

            // Base rate is XML-tuned (DiaryTuningDef.humorChance); DiaryTuning.HumorChance owns the
            // safe 0..1 clamp and fallback when the def is absent/invalid.
            float chance = DiaryTuning.HumorChance;
            if (!Rand.Chance(chance))
            {
                return string.Empty;
            }

            bool isGallows = IsHighStakes(diaryEvent);

            List<DiaryHumorCueDef> candidates = null;
            float totalWeight = 0f;
            IReadOnlyList<DiaryHumorCueDef> all = DiaryHumorCues.All;
            for (int i = 0; i < all.Count; i++)
            {
                DiaryHumorCueDef def = all[i];
                if (def == null
                    || DiaryHumorCues.IsGallows(def) != isGallows
                    || string.IsNullOrWhiteSpace(def.rule)
                    || def.weight <= 0f)
                {
                    continue;
                }

                if (candidates == null)
                {
                    candidates = new List<DiaryHumorCueDef>();
                }

                candidates.Add(def);
                totalWeight += def.weight;
            }

            if (candidates == null || candidates.Count == 0 || totalWeight <= 0f)
            {
                return string.Empty;
            }

            return PickWeighted(candidates, totalWeight).rule;
        }

        /// <summary>
        /// Derives the stakes tier from the event. Gallows (high-stakes) when the event is marked
        /// important, belongs to the Raid domain, or its display color cue is combat/social-fight/
        /// mental-break; otherwise Light. Both tiers remain eligible — this only picks the flavor.
        /// </summary>
        private static bool IsHighStakes(DiaryEvent diaryEvent)
        {
            if (diaryEvent.IsImportant())
            {
                return true;
            }

            string domain = DiaryEventDomainClassifier.DomainForContext(diaryEvent.gameContext);
            if (string.Equals(domain, DiaryEventDomainClassifier.Raid, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string colorCue = DiaryEvent.ResolveColorCue(diaryEvent.interactionDefName, diaryEvent.gameContext);
            if (string.Equals(colorCue, DiaryEvent.CombatColorCue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(colorCue, DiaryEvent.SocialFightColorCue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(colorCue, DiaryEvent.MentalBreakColorCue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        // Same weighted-pick algorithm as PromptEnchantments.PickWeighted: roll in [0, total), walk
        // the cumulative weights until the roll is passed. Falls back to the last candidate if float
        // rounding leaves the roll above the final cumulative bucket.
        private static DiaryHumorCueDef PickWeighted(List<DiaryHumorCueDef> candidates, float totalWeight)
        {
            float roll = Rand.Range(0f, totalWeight);
            float cumulative = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                cumulative += candidates[i].weight;
                if (roll <= cumulative)
                {
                    return candidates[i];
                }
            }

            return candidates[candidates.Count - 1];
        }
    }
}

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
//
// Hidden temperament bias: who the writer is nudges how often humor fires. An upbeat temperament
// (Optimist/Sanguine — both degrees of NaturalMood — or Anomaly's Joyous) or a passion (minor or
// burning) in Social pulls the chance UP; a dour, anxious, or unfeeling temperament (Pessimist,
// Depressive, Nervous, Neurotic, Very neurotic, Psychopath, or Anomaly's Disturbing) pulls it DOWN.
// This is deliberately NOT cumulative: within a direction several qualifiers still count once, and a
// writer who somehow qualifies for both directions (say a Sanguine psychopath) offsets back to the
// plain base rate. There is no settings field for this, matching the rest of the humor-cue feature.
using System;
using System.Collections.Generic;
using RimWorld;
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
        // Trait keys that shift the humor chance. Spectrum-trait degrees are keyed "defName:degree"
        // (NaturalMood's sanguine/optimist/pessimist/depressive are degrees 2/1/-1/-2); single-degree
        // traits (Joyous, Psychopath, Disturbing) are keyed by bare defName. This mirrors the key
        // convention in PersonaAffinity; HasAnyTraitKey tests both forms of each of the pawn's traits.
        private static readonly HashSet<string> UpbeatTraitKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "NaturalMood:2", // sanguine
            "NaturalMood:1", // optimist
            "Joyous",        // Anomaly DLC
        };

        private static readonly HashSet<string> DourTraitKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "NaturalMood:-1", // pessimist
            "NaturalMood:-2", // depressive
            "Nerves:-1",      // nervous
            "Neurotic:1",     // neurotic
            "Neurotic:2",     // very neurotic
            "Psychopath",     // unfeeling (singular)
            "Disturbing",     // Anomaly DLC (singular)
        };

        /// <summary>
        /// Returns the chosen cue's rule text for this event, or <c>string.Empty</c> when no cue is
        /// selected. At most one cue per entry. <paramref name="writerPawn"/> is the live pawn writing
        /// this POV (null is fine — the offline/unresolvable case simply skips the temperament check
        /// and uses the plain base rate).
        /// </summary>
        public static string CueFor(DiaryEvent diaryEvent, Pawn writerPawn)
        {
            if (diaryEvent == null)
            {
                return string.Empty;
            }

            // Base rate is XML-tuned (DiaryTuningDef.humorChance); DiaryTuning.HumorChance owns the
            // safe 0..1 clamp and fallback when the def is absent/invalid. The writer's temperament
            // then applies one non-cumulative multiplier on top -- see HumorChanceMultiplierFor.
            float chance = DiaryTuning.HumorChance * HumorChanceMultiplierFor(writerPawn);
            if (!Rand.Chance(Mathf.Clamp01(chance)))
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
        /// Resolves the single, non-cumulative multiplier the writer's temperament applies to the
        /// base humor chance. An upbeat temperament (Optimist/Sanguine/Joyous) or a Social passion
        /// pulls it UP; a dour/anxious/unfeeling temperament pulls it DOWN. The two directions are
        /// mutually exclusive rather than stacked: within a direction several matches still count
        /// once, and a writer who qualifies for both (say a Sanguine psychopath) offsets back to 1.
        /// </summary>
        private static float HumorChanceMultiplierFor(Pawn pawn)
        {
            if (pawn == null)
            {
                return 1f;
            }

            bool up = HasAnyTraitKey(pawn, UpbeatTraitKeys) || HasSocialPassion(pawn);
            bool down = HasAnyTraitKey(pawn, DourTraitKeys);

            // Neither, or both (which offset) -> no adjustment.
            if (up == down)
            {
                return 1f;
            }

            return up
                ? DiaryTuning.HumorElevatedChanceMultiplier
                : DiaryTuning.HumorReducedChanceMultiplier;
        }

        private static bool HasSocialPassion(Pawn pawn)
        {
            SkillRecord social = pawn.skills?.GetSkill(SkillDefOf.Social);
            return social != null && social.passion != Passion.None;
        }

        // Iterates the pawn's traits once, testing both the bare defName and the "defName:degree"
        // form of each against the key set (see the convention on UpbeatTraitKeys/DourTraitKeys).
        private static bool HasAnyTraitKey(Pawn pawn, HashSet<string> keys)
        {
            List<Trait> traits = pawn.story?.traits?.allTraits;
            if (traits == null)
            {
                return false;
            }

            for (int i = 0; i < traits.Count; i++)
            {
                Trait trait = traits[i];
                string defName = trait?.def?.defName;
                if (defName == null)
                {
                    continue;
                }

                if (keys.Contains(defName) || keys.Contains(defName + ":" + trait.Degree))
                {
                    return true;
                }
            }

            return false;
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

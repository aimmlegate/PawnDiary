// Optional voice-cue selector (historical "HumorCues" name retained for compatibility). It
// occasionally injects one structural sentence device into a first-person prompt. This is IMPURE
// (RNG + DefDatabase + tuning read), so it lives in Source/Generation; stable repertoire ownership
// stays in the pure HumorChancePolicy. The selected rule is folded into the existing persona voice
// block, so no planner, saved state, or prompt-field change is needed.
//
// Each identified writer owns a small stable repertoire in each stakes tier. Light covers mundane
// events and Gallows covers high-stakes events; the existing event/writer/salt seed chooses one
// weighted rule from the owned tier after the existing chance gate. At most one cue reaches an entry.
//
// Hidden temperament bias: who the writer is nudges how often humor fires. An upbeat temperament
// (Optimist/Sanguine — both degrees of NaturalMood — or Anomaly's Joyous) or a passion (minor or
// burning) in Social pulls the chance UP; a dour, anxious, or unfeeling temperament (Pessimist,
// Depressive, Nervous, Neurotic, Very neurotic, Psychopath, or Anomaly's Disturbing) pulls it DOWN.
// This is deliberately NOT cumulative: within a direction several qualifiers still count once, and a
// writer who somehow qualifies for both directions (say a Sanguine psychopath) offsets back to the
// plain base rate. Rates and trait-key lists are XML-authored and available in Advanced tuning; the
// cue itself stays out of ordinary settings rather than becoming a gameplay toggle.
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
        /// <summary>
        /// Returns the chosen cue's rule text for this event, or <c>string.Empty</c> when no cue is
        /// selected. At most one cue per entry. <paramref name="writerPawn"/> is the live pawn writing
        /// this POV (null is fine — the offline/unresolvable case simply skips the temperament check
        /// and uses the plain base rate). <paramref name="seedSalt"/> is the anti-repetition guard's
        /// persisted reroll counter: 0 reproduces the entry's original humor decision exactly, while
        /// a positive value re-rolls onto a different stable seed.
        /// </summary>
        public static string CueFor(
            DiaryEvent diaryEvent,
            Pawn writerPawn,
            string writerStableId,
            int seedSalt = 0,
            string povRole = null)
        {
            if (diaryEvent == null)
            {
                return string.Empty;
            }

            // Isolate this cosmetic prompt choice from Verse's process-global gameplay RNG. The stable
            // event+POV seed also means Regenerate produces the same humor decision and cue.
            Rand.PushState(HumorChancePolicy.StableSeed(diaryEvent.eventId, writerStableId, seedSalt));
            try
            {
                // Base rate is XML-tuned (DiaryTuningDef.humorChance); DiaryTuning.HumorChance owns the
                // safe 0..1 clamp and fallback when the def is absent/invalid. The writer's temperament
                // then applies one non-cumulative multiplier on top -- see HumorChanceMultiplierFor.
                float chance = DiaryTuning.HumorChance * HumorChanceMultiplierFor(writerPawn);
                if (!Rand.Chance(Mathf.Clamp01(chance)))
                {
                    return string.Empty;
                }

                bool isGallows = IsHighStakes(diaryEvent, povRole);

                List<DiaryHumorCueDef> candidates = null;
                List<string> candidateKeys = null;
                IReadOnlyList<DiaryHumorCueDef> all = DiaryHumorCues.All;
                for (int i = 0; i < all.Count; i++)
                {
                    DiaryHumorCueDef def = all[i];
                    if (def == null
                        || string.IsNullOrWhiteSpace(def.defName)
                        || !DiaryHumorCues.HasRecognizedTier(def)
                        || DiaryHumorCues.IsGallows(def) != isGallows
                        || string.IsNullOrWhiteSpace(def.rule)
                        || def.weight <= 0f
                        || float.IsNaN(def.weight)
                        || float.IsInfinity(def.weight))
                    {
                        continue;
                    }

                    if (candidates == null)
                    {
                        candidates = new List<DiaryHumorCueDef>();
                        candidateKeys = new List<string>();
                    }

                    candidates.Add(def);
                    candidateKeys.Add(def.defName);
                }

                if (candidates == null || candidates.Count == 0)
                {
                    return string.Empty;
                }

                List<string> repertoireKeys = HumorChancePolicy.StableRepertoire(
                    candidateKeys,
                    writerStableId,
                    isGallows ? DiaryHumorCues.TierGallows : DiaryHumorCues.TierLight,
                    DiaryTuning.HumorCueRepertoireSize);
                if (repertoireKeys.Count == 0)
                {
                    return string.Empty;
                }

                // StableRepertoire returns ordinal key order. Map in that order rather than filtering
                // the DefDatabase list, whose XML order must not change the same event's weighted pick.
                List<DiaryHumorCueDef> repertoire = new List<DiaryHumorCueDef>(repertoireKeys.Count);
                float totalWeight = 0f;
                for (int keyIndex = 0; keyIndex < repertoireKeys.Count; keyIndex++)
                {
                    string key = repertoireKeys[keyIndex];
                    for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                    {
                        DiaryHumorCueDef candidate = candidates[candidateIndex];
                        if (!string.Equals(candidate.defName, key, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        repertoire.Add(candidate);
                        totalWeight += candidate.weight;
                        break;
                    }
                }

                if (repertoire.Count == 0
                    || totalWeight <= 0f
                    || float.IsNaN(totalWeight)
                    || float.IsInfinity(totalWeight))
                {
                    return string.Empty;
                }

                return PickWeighted(repertoire, totalWeight).rule;
            }
            finally
            {
                Rand.PopState();
            }
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

            bool upbeatTrait = HasAnyTraitKey(pawn, DiaryTuning.HumorElevatedTraitKeys);
            bool socialPassion = HasSocialPassion(pawn);
            bool dourTrait = HasAnyTraitKey(pawn, DiaryTuning.HumorReducedTraitKeys);
            return HumorChancePolicy.Multiplier(upbeatTrait, socialPassion, dourTrait,
                DiaryTuning.HumorElevatedChanceMultiplier,
                DiaryTuning.HumorReducedChanceMultiplier);
        }

        private static bool HasSocialPassion(Pawn pawn)
        {
            SkillRecord social = pawn.skills?.GetSkill(SkillDefOf.Social);
            return social != null && social.passion != Passion.None;
        }

        // Iterates the pawn's traits once, testing both the bare defName and the "defName:degree"
        // form of each against the XML-authored key list.
        private static bool HasAnyTraitKey(Pawn pawn, IList<string> keys)
        {
            List<Trait> traits = pawn.story?.traits?.allTraits;
            if (traits == null || keys == null)
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

                string degreeKey = defName + ":" + trait.Degree;
                for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
                {
                    string key = (keys[keyIndex] ?? string.Empty).Trim();
                    if (string.Equals(key, defName, StringComparison.Ordinal)
                        || string.Equals(key, degreeKey, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Derives the stakes tier from the event's central per-POV semantics. Gallows applies to an
        /// important or combat-related POV; otherwise Light. Both tiers remain eligible — this only
        /// picks the flavor.
        /// </summary>
        private static bool IsHighStakes(DiaryEvent diaryEvent, string povRole)
        {
            PlayerEntrySemanticProjection semantics = diaryEvent.SemanticProjectionForRole(povRole);
            return semantics.important || semantics.combat;
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

// Impure adapter for the psychotype roll. It snapshots live Pawn skill passions, the creepjoiner flag,
// the two veto traits, and the canonical trait keys (PsychotypeTraitAffinities: trait weight pull +
// trait-gated psychotype unlocks) on the main thread into a plain PsychotypeRollInput, projects the
// psychotype catalog into pure candidates, then delegates to the pure PsychotypeRollPolicy with the
// component-owned private random stream. Keep Verse/DefDatabase access here; the policy stays pure
// and unit-tested, while repeated UI rerolls advance without touching RimWorld's gameplay RNG.
//
// New to C#/RimWorld? See AGENTS.md. Passion is RimWorld's per-skill interest level (None/Minor/Major);
// "burning" is Passion.Major.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    internal static class PsychotypeRolls
    {
        // Trait defNames that veto a psychotype: a Psychopath never rolls Dependent; a Kind pawn never
        // rolls Ruthless. The broader trait channel (weight pull + gated unlocks) goes through
        // PsychotypeTraitAffinities.CanonicalTraitKey below instead of ad-hoc constants.
        private const string PsychopathTraitDefName = "Psychopath";
        private const string KindTraitDefName = "Kind";

        /// <summary>
        /// Rolls a psychotype defName for the pawn in the given band ("adult"/"child"). Returns Neutral
        /// when the roll finds no usable candidate, so callers always get a valid defName. Main thread
        /// only (reads live Pawn/Def state; the caller owns the private advancing random stream).
        /// </summary>
        public static string Roll(Pawn pawn, string stageBand, IDictionary<string, int> usedCounts,
            string childPsychotypeDefName, Func<float> nextUnitFloat)
        {
            if (nextUnitFloat == null)
            {
                return DiaryPsychotypes.NeutralDefName;
            }

            PsychotypeRollInput input = BuildInput(pawn, stageBand, usedCounts, childPsychotypeDefName);
            List<PsychotypeCandidate> candidates = DiaryPsychotypes.RollCandidates();
            string defName = PsychotypeRollPolicy.Roll(input, candidates, nextUnitFloat);
            return string.IsNullOrWhiteSpace(defName) ? DiaryPsychotypes.NeutralDefName : defName;
        }

        private static PsychotypeRollInput BuildInput(Pawn pawn, string stageBand,
            IDictionary<string, int> usedCounts, string childPsychotypeDefName)
        {
            PsychotypeTraitAffinityPolicy traitPolicy = DiaryPsychotypeTraitPolicy.Snapshot();
            PsychotypeRollInput input = new PsychotypeRollInput
            {
                stageBand = string.Equals(stageBand, PsychotypeRollPolicy.StageChild, System.StringComparison.OrdinalIgnoreCase)
                    ? PsychotypeRollPolicy.StageChild
                    : PsychotypeRollPolicy.StageAdult,
                isCreepJoiner = DlcContext.IsCreepJoiner(pawn),
                childPsychotypeDefName = childPsychotypeDefName ?? string.Empty,
                usedCounts = usedCounts != null ? new Dictionary<string, int>(usedCounts) : new Dictionary<string, int>(),
                passions = PassionsFor(pawn),
                // XML-owned odds/weights/thresholds snapshotted once per roll (DiaryPsychotypeRollPolicyDef).
                weights = DiaryPsychotypeRollPolicy.Snapshot(),
                traitPolicy = traitPolicy
            };

            // Trait vetoes + canonical trait keys (weight pull and gated-psychotype unlocks; see
            // PsychotypeTraitAffinities). story/traits is null for some pawn kinds, so guard.
            List<Trait> traits = pawn?.story?.traits?.allTraits;
            if (traits != null)
            {
                for (int i = 0; i < traits.Count; i++)
                {
                    Trait trait = traits[i];
                    string defName = trait?.def?.defName;
                    if (string.IsNullOrEmpty(defName))
                    {
                        continue;
                    }

                    if (defName == PsychopathTraitDefName)
                    {
                        input.blockDependent = true;
                    }
                    else if (defName == KindTraitDefName)
                    {
                        input.blockRuthless = true;
                    }

                    string key = PsychotypeTraitAffinities.CanonicalTraitKey(
                        defName, trait.Degree, traitPolicy);
                    if (!string.IsNullOrEmpty(key) && !input.traitKeys.Contains(key))
                    {
                        input.traitKeys.Add(key);
                    }
                }
            }

            return input;
        }

        // Snapshots the pawn's passionate skills as (skillDefName, level) where minor = 1, burning = 2.
        private static List<PsychotypeSkillPassion> PassionsFor(Pawn pawn)
        {
            List<PsychotypeSkillPassion> passions = new List<PsychotypeSkillPassion>();
            List<SkillRecord> skills = pawn?.skills?.skills;
            if (skills == null)
            {
                return passions;
            }

            for (int i = 0; i < skills.Count; i++)
            {
                SkillRecord skill = skills[i];
                if (skill == null || skill.def == null)
                {
                    continue;
                }

                int level = LevelFor(skill.passion);
                if (level > 0)
                {
                    passions.Add(new PsychotypeSkillPassion { skillDefName = skill.def.defName, level = level });
                }
            }

            return passions;
        }

        // Passion.Major (burning) = 2 points, Passion.Minor = 1, Passion.None (and any modded higher
        // value) fold to the nearest of these so an unusual passion never crashes the roll.
        private static int LevelFor(Passion passion)
        {
            if (passion == Passion.None)
            {
                return 0;
            }

            return passion == Passion.Minor ? 1 : 2;
        }
    }
}

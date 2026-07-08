// Impure adapter for the psychotype roll. It snapshots live Pawn skill passions, the creepjoiner flag,
// and the two veto traits on the main thread into a plain PsychotypeRollInput, projects the psychotype
// catalog into pure candidates, then delegates to the pure PsychotypeRollPolicy with Rand.Value. Keep
// all Verse/DefDatabase/Rand access here; the policy stays pure and unit-tested.
//
// New to C#/RimWorld? See AGENTS.md. Passion is RimWorld's per-skill interest level (None/Minor/Major);
// "burning" is Passion.Major.
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    internal static class PsychotypeRolls
    {
        // Trait defNames that veto a psychotype (independence is the point, so only these two feed in):
        // a Psychopath never rolls Dependent; a Kind pawn never rolls Ruthless.
        private const string PsychopathTraitDefName = "Psychopath";
        private const string KindTraitDefName = "Kind";

        /// <summary>
        /// Rolls a psychotype defName for the pawn in the given band ("adult"/"child"). Returns Neutral
        /// when the roll finds no usable candidate, so callers always get a valid defName. Main thread
        /// only (reads live Pawn/Def state and Rand).
        /// </summary>
        public static string Roll(Pawn pawn, string stageBand, IDictionary<string, int> usedCounts,
            string childPsychotypeDefName)
        {
            PsychotypeRollInput input = BuildInput(pawn, stageBand, usedCounts, childPsychotypeDefName);
            List<PsychotypeCandidate> candidates = DiaryPsychotypes.RollCandidates();

            // Isolate the roll from the global gameplay RNG stream. This is a cosmetic per-pawn pick, not a
            // simulation event, so it must not shift the seeded Rand sequence the rest of the game draws
            // from. PushState/PopState checkpoints that stream and restores it exactly around the roll.
            Rand.PushState();
            try
            {
                string defName = PsychotypeRollPolicy.Roll(input, candidates, () => Rand.Value);
                return string.IsNullOrWhiteSpace(defName) ? DiaryPsychotypes.NeutralDefName : defName;
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static PsychotypeRollInput BuildInput(Pawn pawn, string stageBand,
            IDictionary<string, int> usedCounts, string childPsychotypeDefName)
        {
            PsychotypeRollInput input = new PsychotypeRollInput
            {
                stageBand = string.Equals(stageBand, PsychotypeRollPolicy.StageChild, System.StringComparison.OrdinalIgnoreCase)
                    ? PsychotypeRollPolicy.StageChild
                    : PsychotypeRollPolicy.StageAdult,
                isCreepJoiner = DlcContext.IsCreepJoiner(pawn),
                childPsychotypeDefName = childPsychotypeDefName ?? string.Empty,
                usedCounts = usedCounts != null ? new Dictionary<string, int>(usedCounts) : new Dictionary<string, int>(),
                passions = PassionsFor(pawn)
            };

            // Trait vetoes. story/traits is null for some pawn kinds, so guard.
            List<Trait> traits = pawn?.story?.traits?.allTraits;
            if (traits != null)
            {
                for (int i = 0; i < traits.Count; i++)
                {
                    string defName = traits[i]?.def?.defName;
                    if (defName == PsychopathTraitDefName)
                    {
                        input.blockDependent = true;
                    }
                    else if (defName == KindTraitDefName)
                    {
                        input.blockRuthless = true;
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

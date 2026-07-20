// DLC-safe body-modification stance reader. This is the impure edge for body-part diary events:
// it reads live Pawn state (traits, ideoligion precepts, hediffs, Anomaly ghoul status) and returns
// a plain BodyModStanceFacts DTO for the pure BodyPartEventPolicy. New to C#/RimWorld? See AGENTS.md
// ("DLC-safety") for why all optional-DLC reads are guarded.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Captures a pawn's body-modification stance without leaking live Pawn objects into pure policy.
    /// </summary>
    internal static class BodyModContext
    {
        /// <summary>
        /// Returns plain stance facts for body-part event policy. Missing DLC/content simply produces
        /// false/none values, so a base-game-only player keeps the same safe runtime path.
        /// </summary>
        public static BodyModStanceFacts FactsFor(Pawn pawn)
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            return new BodyModStanceFacts
            {
                HasCravesTrait = HasTrait(pawn, tuning.bodyPartCravesTraitDefNames),
                HasDespisesTrait = HasTrait(pawn, tuning.bodyPartDespisesTraitDefNames),
                IdeologyStance = IdeologyStance(pawn, tuning.bodyPartApprovePreceptDefNames, tuning.bodyPartDespisePreceptDefNames),
                IsInhumanized = HasHediff(pawn, tuning.bodyPartInhumanizedHediffDefNames),
                // All ghoul-state reads deliberately route through the guarded DLC adapter.
                IsGhoul = DlcContext.IsGhoul(pawn)
            };
        }

        private static bool HasTrait(Pawn pawn, List<string> traitDefNames)
        {
            if (pawn?.story?.traits?.allTraits == null || traitDefNames == null || traitDefNames.Count == 0)
            {
                return false;
            }

            List<Trait> traits = pawn.story.traits.allTraits;
            for (int i = 0; i < traits.Count; i++)
            {
                string defName = traits[i]?.def?.defName;
                if (ContainsDefName(traitDefNames, defName))
                {
                    return true;
                }
            }

            return false;
        }

        private static string IdeologyStance(Pawn pawn, List<string> approvePrecepts, List<string> despisePrecepts)
        {
            List<string> precepts = DlcContext.IdeologyPreceptDefNames(pawn);
            if (precepts == null || precepts.Count == 0)
            {
                return BodyPartEventPolicy.IdeologyNone;
            }

            bool approves = false;
            for (int i = 0; i < precepts.Count; i++)
            {
                string defName = precepts[i];
                if (ContainsDefName(despisePrecepts, defName))
                {
                    return BodyPartEventPolicy.IdeologyDespises;
                }

                if (ContainsDefName(approvePrecepts, defName))
                {
                    approves = true;
                }
            }

            return approves ? BodyPartEventPolicy.IdeologyApproves : BodyPartEventPolicy.IdeologyNone;
        }

        private static bool HasHediff(Pawn pawn, List<string> hediffDefNames)
        {
            if (pawn?.health?.hediffSet?.hediffs == null || hediffDefNames == null || hediffDefNames.Count == 0)
            {
                return false;
            }

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                string defName = hediffs[i]?.def?.defName;
                if (ContainsDefName(hediffDefNames, defName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsDefName(List<string> defNames, string defName)
        {
            if (string.IsNullOrEmpty(defName) || defNames == null)
            {
                return false;
            }

            for (int i = 0; i < defNames.Count; i++)
            {
                if (string.Equals(defNames[i], defName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

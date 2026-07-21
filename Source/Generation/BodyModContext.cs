// DLC-safe body-modification stance reader. This is the impure edge for body-part diary events:
// it reads live Pawn state (traits, hediffs, Anomaly ghoul status) and returns
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
        public static BodyModStanceFacts FactsFor(
            Pawn pawn,
            string ideologyStance = BodyPartEventPolicy.IdeologyNone)
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            return new BodyModStanceFacts
            {
                HasCravesTrait = HasTrait(pawn, tuning.bodyPartCravesTraitDefNames),
                HasDespisesTrait = HasTrait(pawn, tuning.bodyPartDespisesTraitDefNames),
                // Ideology Phase 1 supplies this token from the one shared resolver pass. This adapter
                // no longer owns or consults a vanilla/DLC precept-ID allowlist.
                IdeologyStance = ideologyStance,
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

        /// <summary>
        /// Maps only reliable matched correlation valence back to the legacy body-attitude token.
        /// Negative wins when two independent selected stances disagree, preserving prior precedence.
        /// </summary>
        public static string IdeologyStance(BeliefStanceResolution resolution)
        {
            List<string> valences = new List<string>();
            if (resolution?.stances != null)
            {
                for (int i = 0; i < resolution.stances.Count; i++)
                    if (resolution.stances[i] != null)
                        valences.Add(resolution.stances[i].correlationValence);
            }
            return BodyPartEventPolicy.IdeologyStanceForCorrelationValences(valences);
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

// DLC-safe body-modification stance reader. This is the impure edge for body-part diary events:
// it reads live Pawn state (traits, ideoligion precepts, hediffs, Anomaly ghoul status) and returns
// a plain BodyModStanceFacts DTO for the pure BodyPartEventPolicy. New to C#/RimWorld? See AGENTS.md
// ("DLC-safety") for why all optional-DLC reads are guarded.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Captures a pawn's body-modification stance without leaking live Pawn objects into pure policy.
    /// </summary>
    public static class BodyModContext
    {
        private static readonly PropertyInfo IsGhoulProperty = typeof(Pawn).GetProperty("IsGhoul");
        private static readonly FieldInfo MutantField = typeof(Pawn).GetField("mutant");

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
                IsGhoul = IsGhoul(pawn)
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

        private static bool IsGhoul(Pawn pawn)
        {
            if (!ModsConfig.AnomalyActive || pawn == null)
            {
                return false;
            }

            if (IsGhoulProperty != null && IsGhoulProperty.PropertyType == typeof(bool))
            {
                object value = IsGhoulProperty.GetValue(pawn, null);
                return value is bool && (bool)value;
            }

            // RimWorld versions can move this convenience property. Reflection keeps the code
            // compiling against 1.6 while still supporting the older mutant tracker shape if present.
            object mutant = MutantField != null ? MutantField.GetValue(pawn) : null;
            object def = ReadPropertyOrField(mutant, "Def");
            string defName = ReadPropertyOrField(def, "defName") as string;
            return string.Equals(defName, "Ghoul", StringComparison.OrdinalIgnoreCase);
        }

        private static object ReadPropertyOrField(object target, string name)
        {
            if (target == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name);
            if (property != null)
            {
                return property.GetValue(target, null);
            }

            FieldInfo field = type.GetField(name);
            return field != null ? field.GetValue(target) : null;
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

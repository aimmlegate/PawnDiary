// Runtime adapter for hediff-driven writing-style overrides. It reads live Pawn health state and XML
// Defs on the main thread, then delegates matching to HediffPersonaOverridePolicy's pure DTO logic.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Resolves temporary writing-style overrides caused by active pawn hediffs.
    /// </summary>
    public static class HediffPersonaOverrides
    {
        /// <summary>
        /// Returns the prompt-ready writing-style rule, using a matching hediff override when present
        /// and the pawn's saved style otherwise.
        /// </summary>
        public static string RuleFor(Pawn pawn, string fallbackPersonaDefName)
        {
            string overridePersonaDefName = PersonaDefNameFor(pawn);
            return DiaryPersonas.RuleFor(string.IsNullOrWhiteSpace(overridePersonaDefName)
                ? fallbackPersonaDefName
                : overridePersonaDefName);
        }

        /// <summary>
        /// Returns the DiaryPersonaDef name forced by this pawn's active hediffs, or empty.
        /// </summary>
        public static string PersonaDefNameFor(Pawn pawn)
        {
            List<DiaryHediffPersonaOverrideDef> defs =
                DefDatabase<DiaryHediffPersonaOverrideDef>.AllDefsListForReading;
            if (pawn == null || defs == null || defs.Count == 0)
            {
                return string.Empty;
            }

            string selected = HediffPersonaOverridePolicy.SelectPersonaDefName(
                RulesFor(defs),
                FactsFor(pawn));
            return DiaryPersonas.ForDefName(selected) == null ? string.Empty : selected;
        }

        private static List<HediffPersonaOverrideRule> RulesFor(
            List<DiaryHediffPersonaOverrideDef> defs)
        {
            List<HediffPersonaOverrideRule> rules = new List<HediffPersonaOverrideRule>();
            if (defs == null)
            {
                return rules;
            }

            for (int i = 0; i < defs.Count; i++)
            {
                DiaryHediffPersonaOverrideDef def = defs[i];
                if (def != null && DiaryPersonas.ForDefName(def.personaDefName) != null)
                {
                    rules.Add(def.ToPolicyRule());
                }
            }

            return rules;
        }

        private static List<HediffPersonaOverrideFact> FactsFor(Pawn pawn)
        {
            List<HediffPersonaOverrideFact> facts = new List<HediffPersonaOverrideFact>();
            List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null)
            {
                return facts;
            }

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff == null)
                {
                    continue;
                }

                facts.Add(new HediffPersonaOverrideFact
                {
                    defName = hediff.def?.defName ?? string.Empty,
                    label = DiaryLineCleaner.CleanLine(hediff.Label),
                    severity = hediff.Severity,
                    visible = hediff.Visible
                });
            }

            return facts;
        }
    }
}

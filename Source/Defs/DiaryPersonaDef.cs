using System.Collections.Generic;
using System.Linq;
using Verse;

namespace PawnDiary
{
    // XML-backed writing style for a pawn. The rule is sent in the user prompt as
    // "persona:" so the model has a stable voice target separate from traits/mood.
    public class DiaryPersonaDef : Def
    {
        public string rule;
    }

    // Central lookup/fallback helper for the persona catalog. RimWorld loads Defs from
    // 1.6/Defs/DiaryPersonaDefs.xml; the hardcoded fallback keeps saves usable if XML is missing.
    public static class DiaryPersonas
    {
        private static readonly DiaryPersonaDef Fallback = new DiaryPersonaDef
        {
            defName = "DiaryPersona_StoicSurvivor",
            label = "stoic-survivor",
            rule = "writes in terse, matter-of-fact sentences; avoids self-pity and focuses on what needs doing next"
        };

        private static readonly List<DiaryPersonaDef> FallbackList = new List<DiaryPersonaDef> { Fallback };

        public static IReadOnlyList<DiaryPersonaDef> All
        {
            get
            {
                List<DiaryPersonaDef> defs = DefDatabase<DiaryPersonaDef>.AllDefsListForReading;
                return defs != null && defs.Count > 0 ? defs : FallbackList;
            }
        }

        public static DiaryPersonaDef Default
        {
            get
            {
                // The default persona is itself configurable in DiaryPromptDef.xml.
                return ForDefName(DiaryPrompts.Current.defaultPersonaDefName) ?? All.FirstOrDefault() ?? Fallback;
            }
        }

        public static DiaryPersonaDef ForDefName(string defName)
        {
            if (string.IsNullOrWhiteSpace(defName))
            {
                return null;
            }

            return DefDatabase<DiaryPersonaDef>.GetNamedSilentFail(defName)
                ?? All.FirstOrDefault(persona => persona.defName == defName);
        }

        public static DiaryPersonaDef Resolve(string defName)
        {
            return ForDefName(defName) ?? Default;
        }

        // Include the label in the prompt so debug output clearly shows which preset was used.
        public static string RuleFor(string defName)
        {
            DiaryPersonaDef persona = Resolve(defName);
            if (persona == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(persona.label))
            {
                return persona.rule ?? string.Empty;
            }

            return persona.label + ": " + (persona.rule ?? string.Empty);
        }
    }
}

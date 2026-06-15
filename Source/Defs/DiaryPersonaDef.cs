using System.Collections.Generic;
using System.Linq;
using Verse;

namespace PawnDiary
{
    // XML-backed writing style for a pawn. The rule is sent in the user prompt as
    // "persona:" so the model has a stable voice target separate from traits/mood.
    public class DiaryPersonaDef : Def
    {
        // The writing rule injected into the LLM prompt as "persona:" so the model adopts a consistent voice.
        public string rule;
    }

    // Central lookup/fallback helper for the persona catalog. RimWorld loads Defs from
    // 1.6/Defs/DiaryPersonaDefs.xml; the hardcoded fallback keeps saves usable if XML is missing.
    public static class DiaryPersonas
    {
        // Hardcoded fallback used when no XML Defs are loaded at all (e.g. during early startup or missing mod files).
        private static readonly DiaryPersonaDef Fallback = new DiaryPersonaDef
        {
            defName = "DiaryPersona_StoicSurvivor",
            label = "stoic-survivor",
            rule = "writes in terse, matter-of-fact sentences; avoids self-pity and focuses on what needs doing next"
        };

        // Wrapped in a list so All can return a non-null IReadOnlyList even with zero XML defs.
        private static readonly List<DiaryPersonaDef> FallbackList = new List<DiaryPersonaDef> { Fallback };

        /// <summary>
        /// All loaded persona defs, or the hardcoded fallback list if none exist in XML.
        /// </summary>
        public static IReadOnlyList<DiaryPersonaDef> All
        {
            get
            {
                List<DiaryPersonaDef> defs = DefDatabase<DiaryPersonaDef>.AllDefsListForReading;
                return defs != null && defs.Count > 0 ? defs : FallbackList;
            }
        }

        /// <summary>
        /// The default persona, sourced from DiaryPromptDef.xml's defaultPersonaDefName,
        /// with cascading fallbacks to the first available def then the hardcoded Fallback.
        /// </summary>
        public static DiaryPersonaDef Default
        {
            get
            {
                // The default persona is itself configurable in DiaryPromptDef.xml.
                return ForDefName(DiaryPrompts.Current.defaultPersonaDefName) ?? All.FirstOrDefault() ?? Fallback;
            }
        }

        /// <summary>
        /// Picks the initial persona for a brand-new pawn diary record. Existing records keep
        /// their saved persona; this is only used the first time a pawn enters the diary system.
        /// </summary>
        public static DiaryPersonaDef RandomStartingPersona()
        {
            IReadOnlyList<DiaryPersonaDef> personas = All;
            if (personas == null || personas.Count == 0)
            {
                return Default ?? Fallback;
            }

            return personas[Rand.Range(0, personas.Count)] ?? Default ?? Fallback;
        }

        /// <summary>
        /// Looks up a persona by defName, returning null if not found or the name is blank.
        /// </summary>
        public static DiaryPersonaDef ForDefName(string defName)
        {
            if (string.IsNullOrWhiteSpace(defName))
            {
                return null;
            }

            return DefDatabase<DiaryPersonaDef>.GetNamedSilentFail(defName)
                ?? All.FirstOrDefault(persona => persona.defName == defName);
        }

        /// <summary>
        /// Resolves a defName to its persona, falling back to Default if the name is missing or unknown.
        /// </summary>
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

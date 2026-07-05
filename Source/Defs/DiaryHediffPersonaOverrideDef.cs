// XML Def contract for hediff-driven writing-style overrides. These rows let DLC or modded hediffs
// temporarily force a DiaryPersonaDef without referencing DLC types or defs from C#.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Maps active pawn hediffs to a temporary writing style for first-person diary prompts.
    /// </summary>
    public class DiaryHediffPersonaOverrideDef : Def
    {
        // Higher priority wins when multiple active hediffs match. Equal priority keeps load order.
        public int priority;

        // DiaryPersonaDef.defName to use while the rule matches.
        public string personaDefName;

        // By default only visible health conditions affect writing style. Rules can opt out for
        // hidden story-state hediffs such as Anomaly conditions.
        public bool visibleOnly = true;
        public float minSeverity = -1f;

        // String matchers keep optional DLC and modded content safe: absent hediffs never match.
        public List<string> hediffDefNames = new List<string>();
        public List<string> hediffDefNameContains = new List<string>();
        public List<string> hediffLabelContains = new List<string>();

        /// <summary>
        /// Converts the XML Def into the pure matching DTO used by prompt generation.
        /// </summary>
        internal HediffPersonaOverrideRule ToPolicyRule()
        {
            return new HediffPersonaOverrideRule
            {
                key = defName,
                priority = priority,
                personaDefName = personaDefName,
                visibleOnly = visibleOnly,
                minSeverity = minSeverity,
                hediffDefNames = Copy(hediffDefNames),
                hediffDefNameContains = Copy(hediffDefNameContains),
                hediffLabelContains = Copy(hediffLabelContains)
            };
        }

        private static List<string> Copy(List<string> values)
        {
            return values == null ? new List<string>() : new List<string>(values);
        }
    }
}

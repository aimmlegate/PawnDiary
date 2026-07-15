// Pure formatting for the optional Narrative Continuity prompt field. Event-time candidate prose is
// already selected and frozen before it reaches this class; the policy-owned instruction is simply
// joined without inventing or truncating any fact.
//
// New to C#/RimWorld? See AGENTS.md ("localization" and "architecture barriers").
using System;

namespace PawnDiary
{
    /// <summary>Stable prompt source token and pure formatter for the optional narrative-context field.</summary>
    internal static class NarrativeContextPrompt
    {
        // This is a structured prompt-schema token, intentionally English and stable across locales.
        public const string Source = "NarrativeContext";

        /// <summary>
        /// Returns no field value for empty selected context; otherwise prefixes the XML/DefInjected
        /// usage instruction when supplied. The selected factual units remain complete and in order.
        /// </summary>
        public static string Compose(string narrativeContext, string instruction)
        {
            string facts = Trim(narrativeContext);
            if (facts.Length == 0)
            {
                return string.Empty;
            }

            string guidance = Trim(instruction);
            return guidance.Length == 0 ? facts : guidance + "\n" + facts;
        }

        private static string Trim(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}

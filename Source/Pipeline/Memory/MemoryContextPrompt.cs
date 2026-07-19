// Pure formatting for the optional Memory prompt field (design/MEMORY_SYSTEM_DESIGN.md §9.6),
// mirroring NarrativeContextPrompt step for step. The recalled memory lines are already selected
// and frozen before they reach this class; the policy-owned instruction is simply joined without
// inventing or truncating any memory. Empty input yields an empty field value, which the prompt
// assembler drops — an unused memory field costs zero tokens.
//
// New to C#/RimWorld? See AGENTS.md ("localization" and "architecture barriers").
using System;

namespace PawnDiary
{
    /// <summary>Stable prompt source token and pure formatter for the optional memory-context field.</summary>
    internal static class MemoryContextPrompt
    {
        // This is a structured prompt-schema token, intentionally English and stable across locales.
        public const string Source = "MemoryContext";

        /// <summary>
        /// Returns no field value for an empty recalled context; otherwise prefixes the
        /// XML/DefInjected usage instruction when supplied. The recalled memory lines remain
        /// complete and in order (direct first, associative second).
        /// </summary>
        public static string Compose(string memoryContext, string instruction)
        {
            string memories = Trim(memoryContext);
            if (memories.Length == 0)
            {
                return string.Empty;
            }

            string guidance = Trim(instruction);
            return guidance.Length == 0 ? memories : guidance + "\n" + memories;
        }

        private static string Trim(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}

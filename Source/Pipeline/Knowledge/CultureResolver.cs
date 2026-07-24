// CultureResolver.cs — pure decisions about a pawn's persisted culture state
// (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §4.1). The impure side gathers the inputs (ideology
// culture, faction allowed cultures, DLC flags) and persists the returned state; this file owns
// the rules: resolve origin ONCE, mark legacy inference, replace (never stack) adopted culture on
// conversion, and never invent a fallback for an unknown culture.
//
// New to C#/RimWorld? See AGENTS.md ("architecture barriers"). No Verse/Unity/Def/settings here.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Origin/adopted culture resolution rules.</summary>
    internal static class CultureResolver
    {
        /// <summary>
        /// Resolves the origin culture for a pawn that does not have one yet. With Ideology
        /// active the pawn's current ideology culture wins; otherwise the origin faction's first
        /// allowed culture (XML order — the deterministic choice). Returns an EMPTY state when
        /// nothing is resolvable: no invented fallback (§4.1).
        /// </summary>
        public static CultureStateSnapshot ResolveOrigin(CultureResolutionInput input)
        {
            CultureStateSnapshot state = new CultureStateSnapshot();
            if (input == null)
            {
                return state;
            }

            string source = input.legacyInference
                ? KnowledgeTokens.CultureSourceInferred
                : KnowledgeTokens.CultureSourceCaptured;

            if (input.ideologyActive && !string.IsNullOrWhiteSpace(input.ideoCultureDefName))
            {
                state.originCultureDefName = input.ideoCultureDefName.Trim();
                state.originSource = source;
                return state;
            }

            string factionCulture = FirstNonBlank(input.factionCultureDefNames);
            if (!string.IsNullOrWhiteSpace(factionCulture))
            {
                state.originCultureDefName = factionCulture;
                state.originSource = source;
            }

            return state;
        }

        /// <summary>
        /// True when the existing state may be (re)initialized: origin is resolved once and never
        /// silently rewritten afterwards — not even when a later resolution disagrees (§4.1).
        /// </summary>
        public static bool NeedsOriginResolution(CultureStateSnapshot existing)
        {
            return existing == null || string.IsNullOrWhiteSpace(existing.originCultureDefName);
        }

        /// <summary>
        /// Applies an Ideology conversion: the latest adopted CultureDef REPLACES any earlier
        /// adopted value (earlier adopted cultures are not retained, §4.1). A conversion into the
        /// origin culture still records it — "went back" is itself knowledge. Blank new cultures
        /// leave the state untouched.
        /// </summary>
        public static CultureStateSnapshot ApplyConversion(
            CultureStateSnapshot existing, string newCultureDefName)
        {
            CultureStateSnapshot state = existing ?? new CultureStateSnapshot();
            if (!string.IsNullOrWhiteSpace(newCultureDefName))
            {
                state.adoptedCultureDefName = newCultureDefName.Trim();
            }

            return state;
        }

        /// <summary>
        /// The culture whose profile should VOICE the pawn right now: adopted when present,
        /// otherwise origin. Blank when the pawn has no resolved culture at all.
        /// </summary>
        public static string EffectiveCulture(CultureStateSnapshot state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(state.adoptedCultureDefName)
                ? (state.originCultureDefName ?? string.Empty).Trim()
                : state.adoptedCultureDefName.Trim();
        }

        private static string FirstNonBlank(List<string> values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i].Trim();
                }
            }

            return string.Empty;
        }
    }
}

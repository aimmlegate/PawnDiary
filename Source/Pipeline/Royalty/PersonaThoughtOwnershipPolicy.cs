// Pure exact-identity policy for thought side effects produced during persona bonding. The Harmony
// adapter stages only a thought whose owning pawn and ThoughtDef name both match the active action.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Matches an accepted thought to an exact persona lifecycle owner.</summary>
    internal static class PersonaThoughtOwnershipPolicy
    {
        public static bool Matches(
            string expectedPawnId,
            IList<string> expectedThoughtDefNames,
            string actualPawnId,
            string actualThoughtDefName)
        {
            string pawn = Clean(expectedPawnId);
            string actualPawn = Clean(actualPawnId);
            string thought = Clean(actualThoughtDefName);
            if (pawn.Length == 0 || actualPawn.Length == 0 || thought.Length == 0
                || !string.Equals(pawn, actualPawn, StringComparison.Ordinal)
                || expectedThoughtDefNames == null)
            {
                return false;
            }

            for (int i = 0; i < expectedThoughtDefNames.Count; i++)
            {
                if (string.Equals(Clean(expectedThoughtDefNames[i]), thought,
                    StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}

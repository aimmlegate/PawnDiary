// Pure helpers for the trait-gain progression scanner. The scanner (impure, in
// DiaryGameComponent.Progression.cs) reads a pawn's live traits, but the two decisions that are worth
// testing without the game live here: how a trait becomes a stable key, and which of the pawn's
// current keys are newly gained versus the last saved snapshot. No RimWorld/Verse/Unity types, so the
// standalone DiaryCapturePolicyTests harness links and exercises this file directly.
//
// New to C#/RimWorld? See AGENTS.md. (TS analogy: `NewlyGainedTraitKeys` is just a case-insensitive
// set difference `current \ previous` that keeps `current` order.)
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Stateless key-building and diffing for the trait-gain progression source.
    /// </summary>
    internal static class TraitProgressionPolicy
    {
        /// <summary>
        /// Builds the stable identity of one trait from its spectrum defName and active degree. A RimWorld
        /// trait is a spectrum Def plus a degree (e.g. the <c>Nerves</c> spectrum at degree <c>-1</c> shows
        /// as "Nervous"), so the degree is part of the key: if a pawn's trait shifts to a different degree,
        /// that reads as a new personality change worth its own page. A blank defName yields an empty key
        /// the caller skips.
        /// </summary>
        public static string BuildTraitKey(string traitDefName, int degree)
        {
            if (string.IsNullOrWhiteSpace(traitDefName))
            {
                return string.Empty;
            }

            return traitDefName.Trim() + "|" + degree.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns the keys present in <paramref name="current"/> but not in <paramref name="previous"/>,
        /// preserving <paramref name="current"/> order and dropping blanks and duplicates. Comparison is
        /// case-insensitive so a save round-trip cannot re-fire a trait. A null/empty
        /// <paramref name="previous"/> means "no baseline recorded yet", in which case every current key
        /// counts as new — the scanner avoids an old-save catch-up burst by baselining silently on the
        /// first scan instead of relying on this method to suppress it.
        /// </summary>
        public static List<string> NewlyGainedTraitKeys(IEnumerable<string> previous, IEnumerable<string> current)
        {
            List<string> result = new List<string>();
            if (current == null)
            {
                return result;
            }

            HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (previous != null)
            {
                foreach (string key in previous)
                {
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        known.Add(key.Trim());
                    }
                }
            }

            HashSet<string> alreadyEmitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in current)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                string trimmed = key.Trim();
                if (known.Contains(trimmed) || !alreadyEmitted.Add(trimmed))
                {
                    continue;
                }

                result.Add(trimmed);
            }

            return result;
        }
    }
}

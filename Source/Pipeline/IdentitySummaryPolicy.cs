// Selects and formats the small relationship roster attached to a pairwise diary POV.
// This file is deliberately pure: callers provide plain rows gathered from RimWorld, and the policy
// performs only deterministic ranking and text assembly. That keeps live Pawn state on the impure
// side of the event-time capture barrier and makes the selection contract standalone-testable.
using System;
using System.Collections.Generic;
using System.Linq;

namespace PawnDiary
{
    /// <summary>
    /// Plain event-time facts about one other colonist as seen by the diary's first-person writer.
    /// Labels are already localized and sanitized before an impure adapter creates this row.
    /// </summary>
    internal sealed class IdentityRow
    {
        public string name;
        public string relationLabel;
        public string sentimentLabel;
        public int opinion;
        public bool hasRelation;
    }

    /// <summary>
    /// Deterministically reserves one named relation, then fills the remaining bounded roster by
    /// absolute opinion strength. The saved output never exposes RimWorld's numeric opinion score.
    /// </summary>
    internal static class IdentitySummaryPolicy
    {
        private const string Prefix = "relationships=";

        /// <summary>
        /// Selects at most <paramref name="maximum"/> rows. If any named relation exists, its
        /// strongest row is reserved before the remaining slots are filled from the global ranking.
        /// Ties use the colonist name with ordinal, case-insensitive comparison.
        /// </summary>
        public static List<IdentityRow> Select(IReadOnlyList<IdentityRow> rows, int maximum)
        {
            List<IdentityRow> selected = new List<IdentityRow>();
            if (rows == null || maximum <= 0)
            {
                return selected;
            }

            List<IdentityRow> ranked = rows
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.name))
                .OrderByDescending(row => OpinionMagnitude(row.opinion))
                .ThenBy(row => row.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (ranked.Count == 0)
            {
                return selected;
            }

            IdentityRow reservedRelation = ranked.FirstOrDefault(row => row.hasRelation);
            if (reservedRelation != null)
            {
                selected.Add(reservedRelation);
            }

            for (int i = 0; i < ranked.Count && selected.Count < maximum; i++)
            {
                IdentityRow row = ranked[i];
                if (!ReferenceEquals(row, reservedRelation))
                {
                    selected.Add(row);
                }
            }

            return selected;
        }

        /// <summary>
        /// Formats the selected rows as one compact structured prompt fact. Relation rows use
        /// <c>name (relation, sentiment)</c>; ordinary rows use <c>name (sentiment)</c>.
        /// </summary>
        public static string Format(IReadOnlyList<IdentityRow> rows, int maximum)
        {
            List<IdentityRow> selected = Select(rows, maximum);
            if (selected.Count == 0)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>(selected.Count);
            for (int i = 0; i < selected.Count; i++)
            {
                IdentityRow row = selected[i];
                string name = row.name.Trim();
                string sentiment = (row.sentimentLabel ?? string.Empty).Trim();
                string relation = (row.relationLabel ?? string.Empty).Trim();
                if (row.hasRelation && relation.Length > 0)
                {
                    parts.Add(name + " (" + relation + ", " + sentiment + ")");
                }
                else
                {
                    parts.Add(name + " (" + sentiment + ")");
                }
            }

            return Prefix + string.Join("; ", parts.ToArray());
        }

        private static long OpinionMagnitude(int opinion)
        {
            // Widen before Math.Abs so even an adversarial int.MinValue fixture cannot overflow.
            return Math.Abs((long)opinion);
        }
    }
}

// Pure Brainwipe projection for saved CreepJoiner continuity. Non-terminal rows describe what the
// pawn visibly learned about their still-active arc and may be rebuilt after the reset; terminal rows
// are durable world outcomes and replay barriers, so they remain even for the wiped pawn.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>Removes one exact pawn's non-terminal CreepJoiner autobiography.</summary>
    internal static class CreepJoinerMemoryResetPolicy
    {
        /// <summary>
        /// Returns all terminal, unrelated, and prefix-collision rows while removing only the exact
        /// pawn's non-terminal arc. A later visible inspection can then open fresh continuity normally.
        /// </summary>
        public static List<CreepJoinerArcSnapshot> RemoveNonterminalForPawn(
            IList<CreepJoinerArcSnapshot> source,
            string pawnId)
        {
            string id = CleanPawnId(pawnId);
            List<CreepJoinerArcSnapshot> result = new List<CreepJoinerArcSnapshot>();
            if (source == null) return result;

            for (int i = 0; i < source.Count; i++)
            {
                CreepJoinerArcSnapshot row = source[i];
                if (row == null) continue;
                bool exactTarget = id.Length > 0 && string.Equals(
                    CleanPawnId(row.pawnId),
                    id,
                    StringComparison.Ordinal);
                if (!exactTarget || row.terminal) result.Add(row);
            }

            return result;
        }

        private static string CleanPawnId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.Length > 0 && cleaned.Length <= 200
                && cleaned.IndexOf('|') < 0 && cleaned.IndexOf(';') < 0
                && cleaned.IndexOf('=') < 0 && cleaned.IndexOf('\r') < 0
                && cleaned.IndexOf('\n') < 0
                ? cleaned
                : string.Empty;
        }
    }
}

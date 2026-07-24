// DlcContext.Knowledge.cs — the ONE home for the knowledge system's DLC-gated pawn reads
// (AGENTS.md "DLC-safety"): the pawn's ideology culture and the origin faction's allowed
// cultures. Every accessor double-guards (ModsConfig flag + null-check) and returns empty
// strings/lists when absent, so a no-DLC game cleanly omits the data.
//
// CultureDefs themselves are CORE content (Astropolitan/Corunan/Rustican/Kriminul; Royalty adds
// Sophian) — only the IDEOLOGY-side read (pawn.Ideo) is DLC-gated. Faction allowedCultures exist
// in every game.
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    internal static partial class DlcContext
    {
        /// <summary>
        /// The pawn's current ideology culture defName, or empty. Ideology-gated: without the DLC
        /// the ideo tracker is not authoritative for culture, so callers fall back to the faction
        /// path (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §4.1).
        /// </summary>
        public static string PawnIdeoCultureDefName(Pawn pawn)
        {
            if (!ModsConfig.IdeologyActive || pawn?.ideo == null)
            {
                return string.Empty;
            }

            Ideo ideo = pawn.ideo.Ideo;
            return ideo?.culture?.defName ?? string.Empty;
        }

        /// <summary>
        /// The pawn's faction's allowedCultures defNames in XML order (deterministic choice =
        /// first entry). Empty for factionless pawns or factions that declare none.
        /// </summary>
        public static List<string> PawnFactionAllowedCultureDefNames(Pawn pawn)
        {
            List<string> cultures = new List<string>();
            List<CultureDef> allowed = pawn?.Faction?.def?.allowedCultures;
            if (allowed == null)
            {
                return cultures;
            }

            for (int i = 0; i < allowed.Count; i++)
            {
                if (allowed[i] != null && !string.IsNullOrWhiteSpace(allowed[i].defName))
                {
                    cultures.Add(allowed[i].defName);
                }
            }

            return cultures;
        }
    }
}

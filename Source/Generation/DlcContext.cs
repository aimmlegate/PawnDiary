// DLC-safe pawn-context accessors. This is the ONE place the mod reads pawn data that only
// exists when the player owns a paid DLC (Biotech genes, Royalty titles, Ideology faith).
//
// Why a dedicated helper? RimWorld ships ALL its C# — base game and every DLC — in one assembly,
// so a DLC type like Pawn_GeneTracker compiles even for a player who doesn't own the DLC. The
// build passing tells you nothing. What's actually missing at runtime is the *content* and the
// per-pawn trackers: pawn.genes / pawn.royalty / pawn.ideo are null without their DLC. Reading
// them unguarded would NullReference and break a base-game player's game.
//
// So every accessor here is double-guarded — ModsConfig.<Dlc>Active AND a null-check — and
// returns string.Empty when the DLC (or the trait) is absent. Callers can therefore append the
// result unconditionally: a no-DLC game simply omits the line. New DLC-gated pawn reads belong
// here, in the same shape. See AGENTS.md ("DLC-safety").
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Guarded accessors for pawn state owned by optional paid DLCs.
    /// </summary>
    public static class DlcContext
    {
        /// <summary>
        /// Biotech: the pawn's xenotype label (e.g. "Sanguophage", "Hussar"). Empty without
        /// Biotech, and empty for a plain Baseliner human because that default carries no signal.
        /// A custom-named xenotype (UniqueXenotype) is always shown.
        /// </summary>
        public static string Xenotype(Pawn pawn)
        {
            if (!ModsConfig.BiotechActive || pawn?.genes == null)
            {
                return string.Empty;
            }

            if (pawn.genes.Xenotype == XenotypeDefOf.Baseliner && !pawn.genes.UniqueXenotype)
            {
                return string.Empty;
            }

            return DiaryContextBuilder.CleanLine(pawn.genes.XenotypeLabelCap);
        }

        /// <summary>
        /// Royalty: the pawn's highest royal title, gender-specific (e.g. "Knight", "Countess").
        /// Empty without Royalty or for a titleless pawn.
        /// </summary>
        public static string RoyalTitle(Pawn pawn)
        {
            if (!ModsConfig.RoyaltyActive || pawn?.royalty == null)
            {
                return string.Empty;
            }

            // MostSeniorTitle is null when the pawn holds no title; its .def is the RoyalTitleDef.
            RoyalTitleDef titleDef = pawn.royalty.MostSeniorTitle?.def;
            if (titleDef == null)
            {
                return string.Empty;
            }

            return DiaryContextBuilder.CleanLine(titleDef.GetLabelCapFor(pawn));
        }

        /// <summary>
        /// Biotech: true when the pawn has one of the requested active gene defNames. Used by
        /// prompt enchantment XML. The matcher accepts strings so XML can mention DLC genes without
        /// creating hard def references that break no-DLC games.
        /// </summary>
        public static bool HasActiveGene(Pawn pawn, IEnumerable<string> geneDefNames)
        {
            if (!ModsConfig.BiotechActive || pawn?.genes?.GenesListForReading == null || geneDefNames == null)
            {
                return false;
            }

            List<Gene> genes = pawn.genes.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                Gene gene = genes[i];
                if (gene != null && gene.Active && DefNameMatches(gene.def?.defName, geneDefNames))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Royalty: true when the pawn's highest title defName appears in the requested string list.
        /// Empty without Royalty or when the pawn has no title.
        /// </summary>
        public static bool HasRoyalTitleDef(Pawn pawn, IEnumerable<string> royalTitleDefNames)
        {
            if (!ModsConfig.RoyaltyActive || pawn?.royalty == null || royalTitleDefNames == null)
            {
                return false;
            }

            RoyalTitleDef titleDef = pawn.royalty.MostSeniorTitle?.def;
            return DefNameMatches(titleDef?.defName, royalTitleDefNames);
        }

        /// <summary>
        /// Ideology: the pawn's ideoligion name plus their role if they have one (e.g.
        /// "Hidden Truth (Acolyte)"). Empty without Ideology or for a pawn with no ideo.
        /// </summary>
        public static string Ideoligion(Pawn pawn)
        {
            if (!ModsConfig.IdeologyActive || pawn?.ideo?.Ideo == null)
            {
                return string.Empty;
            }

            Ideo ideo = pawn.ideo.Ideo;
            string faith = DiaryContextBuilder.CleanLine(ideo.name);
            if (string.IsNullOrWhiteSpace(faith))
            {
                return string.Empty;
            }

            // GetRole returns null when the pawn holds no ideoligion role; LabelForPawn is the
            // gender/age-specific role title (e.g. "Acolyte", "Moral guide").
            Precept_Role role = ideo.GetRole(pawn);
            string roleLabel = role != null ? DiaryContextBuilder.CleanLine(role.LabelForPawn(pawn)) : string.Empty;
            return string.IsNullOrWhiteSpace(roleLabel) ? faith : faith + " (" + roleLabel + ")";
        }

        private static bool DefNameMatches(string defName, IEnumerable<string> requestedDefNames)
        {
            if (string.IsNullOrWhiteSpace(defName) || requestedDefNames == null)
            {
                return false;
            }

            foreach (string requested in requestedDefNames)
            {
                if (string.Equals(defName, requested, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

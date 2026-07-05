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

            return PromptTextSanitizer.LocalizedPromptText(pawn.genes.XenotypeLabelCap);
        }

        /// <summary>
        /// Biotech: the current xenotype label, including ordinary Baseliner when Biotech is present.
        /// Used by progression bookkeeping where a later change from Baseliner is meaningful.
        /// </summary>
        public static string XenotypeLabel(Pawn pawn)
        {
            if (!ModsConfig.BiotechActive || pawn?.genes == null)
            {
                return string.Empty;
            }

            return PromptTextSanitizer.LocalizedPromptText(pawn.genes.XenotypeLabelCap);
        }

        /// <summary>
        /// Biotech: stable xenotype defName for progression dedup. Empty without Biotech or tracker.
        /// </summary>
        public static string XenotypeDefName(Pawn pawn)
        {
            if (!ModsConfig.BiotechActive || pawn?.genes?.Xenotype == null)
            {
                return string.Empty;
            }

            return pawn.genes.Xenotype.defName ?? string.Empty;
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

            return PromptTextSanitizer.LocalizedPromptText(titleDef.GetLabelCapFor(pawn));
        }

        /// <summary>
        /// Royalty: the pawn's highest royal title label. Kept separate from RoyalTitle for callers
        /// that need the matching defName too.
        /// </summary>
        public static string RoyalTitleLabel(Pawn pawn)
        {
            return RoyalTitle(pawn);
        }

        /// <summary>
        /// Royalty: stable defName for the pawn's highest royal title. Empty without title/Royalty.
        /// </summary>
        public static string RoyalTitleDefName(Pawn pawn)
        {
            if (!ModsConfig.RoyaltyActive || pawn?.royalty == null)
            {
                return string.Empty;
            }

            RoyalTitleDef titleDef = pawn.royalty.MostSeniorTitle?.def;
            return titleDef?.defName ?? string.Empty;
        }

        /// <summary>
        /// Anomaly: whether this pawn joined as a creepjoiner. False without Anomaly or for normal
        /// pawns, so callers do not need to touch the DLC-backed pawn property directly.
        /// </summary>
        public static bool IsCreepJoiner(Pawn pawn)
        {
            return ModsConfig.AnomalyActive && pawn != null && pawn.IsCreepJoiner;
        }

        /// <summary>
        /// Anomaly: whether an unnatural corpse is currently haunting/imitating this pawn (i.e. the
        /// corpse is this pawn's "double"). False without Anomaly or when no corpse targets the pawn,
        /// so callers never touch the DLC's <see cref="GameComponent_Anomaly"/> directly.
        ///
        /// Why read it this way: an unnatural corpse's ThingDef (UnnaturalCorpse_Human) is generated
        /// per race and tells you nothing about WHO it imitates. The DLC tracks that link on
        /// <see cref="GameComponent_Anomaly"/> via <c>PawnHasUnnaturalCorpse</c>. Crucially, vanilla
        /// removes that link when the corpse is destroyed/dissolves, so this is also the reliable
        /// "is it really still present?" truth — the same metalhorror-style end trigger we rely on for
        /// the UnnaturalCorpsePresence observed condition (the observation stops when the corpse is
        /// gone, so the pure policy ends the state via its normal missing/end-debounce path).
        /// </summary>
        public static bool IsHauntedByUnnaturalCorpse(Pawn pawn)
        {
            if (!ModsConfig.AnomalyActive || pawn == null)
            {
                return false;
            }

            // GameComponent_Anomaly is registered by the Anomaly DLC, so it is null in a no-Anomaly
            // game even though ModsConfig already gates above — double-guarding is the contract here.
            GameComponent_Anomaly anomaly = Verse.Current.Game?.GetComponent<GameComponent_Anomaly>();
            return anomaly != null && anomaly.PawnHasUnnaturalCorpse(pawn);
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
            string faith = PromptTextSanitizer.LocalizedPromptText(ideo.name);
            if (string.IsNullOrWhiteSpace(faith))
            {
                return string.Empty;
            }

            // GetRole returns null when the pawn holds no ideoligion role; LabelForPawn is the
            // gender/age-specific role title (e.g. "Acolyte", "Moral guide").
            Precept_Role role = ideo.GetRole(pawn);
            string roleLabel = role != null
                ? PromptTextSanitizer.LocalizedPromptText(role.LabelForPawn(pawn))
                : string.Empty;
            return string.IsNullOrWhiteSpace(roleLabel) ? faith : faith + " (" + roleLabel + ")";
        }

        /// <summary>
        /// Ideology: stable precept defNames for pure stance/policy checks. Empty without Ideology,
        /// without an ideoligion, or when the ideoligion has no precepts.
        /// </summary>
        public static List<string> IdeologyPreceptDefNames(Pawn pawn)
        {
            List<string> names = new List<string>();
            if (!ModsConfig.IdeologyActive || pawn?.ideo?.Ideo == null)
            {
                return names;
            }

            List<Precept> precepts = pawn.ideo.Ideo.PreceptsListForReading;
            if (precepts == null)
            {
                return names;
            }

            for (int i = 0; i < precepts.Count; i++)
            {
                string defName = precepts[i]?.def?.defName;
                if (!string.IsNullOrWhiteSpace(defName))
                {
                    names.Add(defName);
                }
            }

            return names;
        }

        /// <summary>
        /// Ideology: the pawn's formal ideoligion role only (for example "Moral guide"). Empty
        /// without Ideology, without an ideoligion, or when the pawn holds no role.
        /// </summary>
        public static string IdeologicalRole(Pawn pawn)
        {
            if (!ModsConfig.IdeologyActive || pawn?.ideo?.Ideo == null)
            {
                return string.Empty;
            }

            Precept_Role role = pawn.ideo.Ideo.GetRole(pawn);
            return role == null
                ? string.Empty
                : PromptTextSanitizer.LocalizedPromptText(role.LabelForPawn(pawn));
        }
    }
}

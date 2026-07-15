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
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Guarded accessors for pawn state owned by optional paid DLCs.
    /// </summary>
    internal static class DlcContext
    {
        /// <summary>
        /// Biotech: copies the live child facts needed for an age-7/10/13 growth diff. The caller
        /// supplies the exact birthday age/tier captured by the owning hook; no live object escapes.
        /// </summary>
        public static bool TryCaptureGrowthPawn(
            Pawn pawn,
            int birthdayAge,
            int growthTier,
            bool hasNewResponsibilities,
            out GrowthPawnSnapshot snapshot)
        {
            snapshot = null;
            if (!ModsConfig.BiotechActive
                || pawn?.ageTracker == null
                || pawn.RaceProps == null
                || !pawn.RaceProps.Humanlike
                || BiotechGrowthStageTokens.ForAge(birthdayAge).Length == 0)
            {
                return false;
            }

            GrowthPawnSnapshot captured = new GrowthPawnSnapshot
            {
                pawnId = pawn.GetUniqueLoadID(),
                displayName = DiaryLineCleaner.CleanLine(pawn.LabelShortCap),
                biologicalAge = birthdayAge,
                growthTier = Math.Max(0, Math.Min(8,
                    growthTier >= 0 ? growthTier : pawn.ageTracker.GrowthTier)),
                shortName = DiaryLineCleaner.CleanLine(pawn.LabelShort),
                hasNewResponsibilities = hasNewResponsibilities,
                capturedTick = Find.TickManager?.TicksGame ?? 0
            };

            CaptureGrowthTraits(pawn, captured.traits);
            CaptureGrowthSkills(pawn, captured.skills);
            snapshot = captured;
            return !string.IsNullOrWhiteSpace(captured.pawnId);
        }

        /// <summary>
        /// Biotech growth adapter: stable trait key for a committed choice parameter. The NoTrait
        /// sentinel is deliberately returned as <c>NoTrait</c> so the pure policy can reject it.
        /// </summary>
        public static string GrowthTraitKey(Trait trait)
        {
            if (trait == null)
            {
                return string.Empty;
            }

            if (ReferenceEquals(trait, ChoiceLetter_GrowthMoment.NoTrait))
            {
                return "NoTrait";
            }

            string defName = trait.def?.defName;
            return string.Equals(defName, "NoTrait", StringComparison.OrdinalIgnoreCase)
                ? "NoTrait"
                : TraitProgressionPolicy.BuildTraitKey(defName, trait.Degree);
        }

        /// <summary>
        /// Captures the pawn's currently disabled work-type defNames so the birthday owner can prove
        /// that at least one responsibility actually opened without sending work lists to the prompt.
        /// </summary>
        public static HashSet<string> GrowthDisabledWorkTypeDefNames(Pawn pawn)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!ModsConfig.BiotechActive || pawn == null)
            {
                return result;
            }

            List<WorkTypeDef> disabled = pawn.GetDisabledWorkTypes(permanentOnly: false);
            if (disabled == null)
            {
                return result;
            }

            for (int i = 0; i < disabled.Count; i++)
            {
                string defName = disabled[i]?.defName;
                if (!string.IsNullOrWhiteSpace(defName))
                {
                    result.Add(defName);
                }
            }

            return result;
        }

        /// <summary>
        /// Biotech: copies exact pregnancy/labor participants after HediffWithParents.SetParents has
        /// committed them. The kind token is classified from XML before this adapter is called.
        /// </summary>
        public static bool TryCaptureFamilyHediff(
            HediffWithParents hediff,
            string kindToken,
            out FamilyHediffSnapshot snapshot)
        {
            snapshot = null;
            Pawn birther = hediff?.pawn;
            if (!ModsConfig.BiotechActive
                || birther?.RaceProps == null
                || !birther.RaceProps.Humanlike
                || (kindToken != BiotechFamilyHediffKindTokens.Pregnancy
                    && kindToken != BiotechFamilyHediffKindTokens.Labor))
            {
                return false;
            }

            string hediffId = hediff.GetUniqueLoadID();
            string birtherId = birther.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(hediffId) || string.IsNullOrWhiteSpace(birtherId))
            {
                return false;
            }

            Pawn mother = hediff.Mother;
            Pawn father = hediff.Father;
            snapshot = new FamilyHediffSnapshot
            {
                kindToken = kindToken,
                hediffId = hediffId,
                birtherId = birtherId,
                birtherName = FamilyPawnName(birther),
                geneticMotherId = FamilyPawnId(mother),
                geneticMotherName = FamilyPawnName(mother),
                fatherId = FamilyPawnId(father),
                fatherName = FamilyPawnName(father),
                observedTick = Find.TickManager?.TicksGame ?? 0
            };
            return true;
        }

        /// <summary>
        /// Biotech: copies one living player baby/child and exact Parent/ParentBirth relations. This
        /// is used both when growth occurs and for the silent one-time baseline of older saves.
        /// </summary>
        public static bool TryCaptureFamilyChild(Pawn child, out FamilyChildSnapshot snapshot)
        {
            snapshot = null;
            if (!ModsConfig.BiotechActive
                || child?.relations == null
                || child.RaceProps == null
                || !child.RaceProps.Humanlike
                || child.Faction != Faction.OfPlayer
                || child.Dead
                || (child.DevelopmentalStage != DevelopmentalStage.Baby
                    && child.DevelopmentalStage != DevelopmentalStage.Child))
            {
                return false;
            }

            string childId = child.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(childId))
            {
                return false;
            }

            FamilyChildSnapshot captured = new FamilyChildSnapshot
            {
                childId = childId,
                childName = FamilyPawnName(child),
                observedTick = Find.TickManager?.TicksGame ?? 0
            };
            List<DirectPawnRelation> relations = child.relations.DirectRelations;
            if (relations != null)
            {
                for (int i = 0; i < relations.Count; i++)
                {
                    DirectPawnRelation relation = relations[i];
                    Pawn adult = relation?.otherPawn;
                    string role = string.Empty;
                    if (relation?.def == PawnRelationDefOf.ParentBirth)
                    {
                        role = BiotechFamilyRoleTokens.BirthParent;
                    }
                    else if (relation?.def == PawnRelationDefOf.Parent)
                    {
                        role = BiotechFamilyRoleTokens.Parent;
                    }

                    string adultId = FamilyPawnId(adult);
                    if (role.Length > 0 && adultId.Length > 0)
                    {
                        captured.parents.Add(new FamilyParticipantFact
                        {
                            pawnId = adultId,
                            displayName = FamilyPawnName(adult),
                            roleToken = role
                        });
                    }
                }
            }

            snapshot = captured;
            return true;
        }

        /// <summary>
        /// Biotech: verifies adult/child developmental roles and copies one exact observed activity.
        /// Proximity and diary eligibility are intentionally not converted into relationship facts.
        /// </summary>
        public static bool TryCaptureFamilyActivity(
            Pawn adult,
            Pawn child,
            string kindToken,
            out FamilyActivityObservation observation)
        {
            observation = null;
            if (!ModsConfig.BiotechActive
                || adult?.relations == null
                || child?.relations == null
                || adult.RaceProps == null
                || child.RaceProps == null
                || !adult.RaceProps.Humanlike
                || !child.RaceProps.Humanlike
                || adult.DevelopmentalStage != DevelopmentalStage.Adult
                || (child.DevelopmentalStage != DevelopmentalStage.Baby
                    && child.DevelopmentalStage != DevelopmentalStage.Child)
                || child.Faction != Faction.OfPlayer
                || adult == child
                || (kindToken != BiotechFamilyActivityKindTokens.BabyPlay
                    && kindToken != BiotechFamilyActivityKindTokens.Lesson))
            {
                return false;
            }

            string adultId = FamilyPawnId(adult);
            string childId = FamilyPawnId(child);
            if (adultId.Length == 0 || childId.Length == 0)
            {
                return false;
            }

            string relationToken = string.Empty;
            if (PawnRelationDefOf.ParentBirth != null
                && child.relations.DirectRelationExists(PawnRelationDefOf.ParentBirth, adult))
            {
                relationToken = BiotechFamilyRoleTokens.BirthParent;
            }
            else if (PawnRelationDefOf.Parent != null
                && child.relations.DirectRelationExists(PawnRelationDefOf.Parent, adult))
            {
                relationToken = BiotechFamilyRoleTokens.Parent;
            }

            observation = new FamilyActivityObservation
            {
                kindToken = kindToken,
                adultId = adultId,
                adultName = FamilyPawnName(adult),
                childId = childId,
                childName = FamilyPawnName(child),
                relationToken = relationToken,
                observedTick = Find.TickManager?.TicksGame ?? 0
            };
            return true;
        }

        private static string FamilyPawnId(Pawn pawn)
        {
            return pawn == null ? string.Empty : pawn.GetUniqueLoadID() ?? string.Empty;
        }

        private static string FamilyPawnName(Pawn pawn)
        {
            return pawn == null ? string.Empty : DiaryLineCleaner.CleanLine(pawn.LabelShortCap);
        }

        private static void CaptureGrowthTraits(Pawn pawn, List<GrowthTraitFact> destination)
        {
            List<Trait> traits = pawn?.story?.traits?.allTraits;
            if (traits == null)
            {
                return;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < traits.Count; i++)
            {
                Trait trait = traits[i];
                string key = GrowthTraitKey(trait);
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                {
                    continue;
                }

                destination.Add(new GrowthTraitFact
                {
                    traitKey = key,
                    label = GrowthTraitLabel(pawn, trait),
                    description = GrowthTraitDescription(pawn, trait)
                });
            }
        }

        private static void CaptureGrowthSkills(Pawn pawn, List<GrowthSkillFact> destination)
        {
            if (pawn?.skills == null)
            {
                return;
            }

            List<SkillDef> definitions = DefDatabase<SkillDef>.AllDefsListForReading;
            for (int i = 0; i < definitions.Count; i++)
            {
                SkillDef definition = definitions[i];
                SkillRecord skill = definition == null ? null : pawn.skills.GetSkill(definition);
                if (skill == null || string.IsNullOrWhiteSpace(definition.defName))
                {
                    continue;
                }

                destination.Add(new GrowthSkillFact
                {
                    skillDefName = definition.defName,
                    label = DiaryLineCleaner.CleanLine(definition.LabelCap),
                    passion = GrowthPassionToken(skill.passion),
                    level = skill.Level
                });
            }
        }

        private static string GrowthPassionToken(Passion passion)
        {
            if (passion == Passion.Major) return BiotechPassionTokens.Major;
            if (passion == Passion.Minor) return BiotechPassionTokens.Minor;
            return BiotechPassionTokens.None;
        }

        private static string GrowthTraitLabel(Pawn pawn, Trait trait)
        {
            string label = null;
            try
            {
                label = trait?.CurrentData?.GetLabelCapFor(pawn);
            }
            catch
            {
                // Malformed modded trait data should lose only its localized label.
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                try
                {
                    label = trait?.LabelCap;
                }
                catch
                {
                    // Fall through to the stable defName below.
                }
            }

            string cleaned = DiaryLineCleaner.CleanLine(label);
            return string.IsNullOrWhiteSpace(cleaned) ? (trait?.def?.defName ?? string.Empty) : cleaned;
        }

        private static string GrowthTraitDescription(Pawn pawn, Trait trait)
        {
            string resolved = null;
            try
            {
                string description = trait?.CurrentData?.description;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    resolved = description.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn).Resolve();
                }
            }
            catch
            {
                // A label-only fact is still truthful and useful.
            }

            string cleaned = DiaryLineCleaner.CleanLine(resolved);
            const int MaximumDescriptionCharacters = 400;
            return cleaned.Length <= MaximumDescriptionCharacters
                ? cleaned
                : cleaned.Substring(0, MaximumDescriptionCharacters).TrimEnd() + "…";
        }

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

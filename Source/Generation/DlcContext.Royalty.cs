// Royalty-specific guarded collectors. Every live weapon, trait, title, faction, pawn, and hediff is
// reduced to a detached contract here; callers and saved state never retain a DLC-backed object.
using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace PawnDiary
{
    internal static partial class DlcContext
    {
        /// <summary>Copies one possible persona weapon into a plain snapshot.</summary>
        public static bool TryCapturePersonaWeapon(
            ThingWithComps weapon,
            Pawn observingPawn,
            out PersonaWeaponSnapshot snapshot)
        {
            snapshot = null;
            if (!ModsConfig.RoyaltyActive || weapon == null) return false;
            CompBladelinkWeapon comp = weapon.TryGetComp<CompBladelinkWeapon>();
            if (comp == null) return false;

            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            Pawn codedPawn = comp.CodedPawn;
            PersonaWeaponSnapshot captured = new PersonaWeaponSnapshot
            {
                weaponThingId = weapon.GetUniqueLoadID() ?? string.Empty,
                weaponDefName = weapon.def?.defName ?? string.Empty,
                displayName = CleanRoyaltyText(weapon.LabelCap, policy.maximumTraitLabelCharacters),
                codedPawnId = codedPawn?.GetUniqueLoadID() ?? string.Empty,
                codedPawnName = CleanRoyaltyText(
                    codedPawn?.LabelShortCap ?? comp.CodedPawnLabel,
                    policy.maximumTraitLabelCharacters),
                isCurrentlyPrimary = observingPawn?.equipment != null
                    && ReferenceEquals(observingPawn.equipment.Primary, weapon),
                isDestroyed = weapon.Destroyed,
                traits = CapturePersonaTraits(comp, policy)
            };
            snapshot = captured;
            return !string.IsNullOrWhiteSpace(captured.weaponThingId);
        }

        /// <summary>
        /// Returns coded persona weapons currently owned by this pawn's equipment/inventory graph.
        /// Unavailable/off-map weapons are intentionally not inferred as separation evidence.
        /// </summary>
        public static List<PersonaWeaponSnapshot> CapturePersonaWeapons(Pawn pawn)
        {
            List<PersonaWeaponSnapshot> result = new List<PersonaWeaponSnapshot>();
            if (!ModsConfig.RoyaltyActive || pawn == null) return result;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            CapturePersonaCandidate(pawn.equipment?.bondedWeapon as ThingWithComps, pawn, seen, result);
            List<ThingWithComps> equipment = pawn.equipment?.AllEquipmentListForReading;
            if (equipment != null)
                for (int i = 0; i < equipment.Count; i++)
                    CapturePersonaCandidate(equipment[i], pawn, seen, result);

            ThingOwner<Thing> inventory = pawn.inventory?.innerContainer;
            if (inventory != null)
                for (int i = 0; i < inventory.Count; i++)
                    CapturePersonaCandidate(inventory[i] as ThingWithComps, pawn, seen, result);

            result.Sort((left, right) => string.CompareOrdinal(left.weaponThingId, right.weaponThingId));
            return result;
        }

        /// <summary>Projects the pawn's highest current Royalty title in every faction.</summary>
        public static List<RoyalTitleSnapshot> CaptureRoyalTitles(Pawn pawn)
        {
            List<RoyalTitleSnapshot> result = new List<RoyalTitleSnapshot>();
            if (!ModsConfig.RoyaltyActive || pawn?.royalty == null) return result;
            List<RoyalTitle> titles = pawn.royalty.AllTitlesForReading;
            Dictionary<string, RoyalTitleSnapshot> byFaction =
                new Dictionary<string, RoyalTitleSnapshot>(StringComparer.Ordinal);
            for (int i = 0; i < (titles?.Count ?? 0); i++)
            {
                RoyalTitle title = titles[i];
                RoyalTitleDef def = title?.def;
                Faction faction = title?.faction;
                if (def == null || faction == null) continue;
                string factionId = faction.GetUniqueLoadID() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(factionId)) continue;
                RoyalTitleSnapshot captured = new RoyalTitleSnapshot
                {
                    pawnId = pawn.GetUniqueLoadID(),
                    factionId = factionId,
                    factionName = CleanRoyaltyText(faction.Name, 120),
                    titleDefName = def.defName ?? string.Empty,
                    titleLabel = CleanRoyaltyText(def.GetLabelCapFor(pawn), 120),
                    seniority = Math.Max(0, def.seniority),
                    receivedTick = Math.Max(-1, title.receivedTick),
                    wasInherited = title.wasInherited,
                    dutyCategoryTokens = CaptureRoyalDutyCategories(pawn, def)
                };
                RoyalTitleSnapshot previous;
                if (!byFaction.TryGetValue(factionId, out previous)
                    || captured.seniority > previous.seniority)
                    byFaction[factionId] = captured;
            }
            result.AddRange(byFaction.Values);
            result.Sort((left, right) => string.CompareOrdinal(left.factionId, right.factionId));
            return result;
        }

        /// <summary>Returns the guarded current psylink level, clamped to vanilla's 0-6 range.</summary>
        public static int CurrentPsylinkLevel(Pawn pawn)
        {
            if (!ModsConfig.RoyaltyActive || pawn?.health?.hediffSet?.hediffs == null) return 0;
            int highest = 0;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (!RoyaltyMatchesDefName(DiaryTuning.Current.psylinkHediffDefNames, hediff?.def?.defName))
                    continue;
                int level;
                if (TryReadRoyaltyIntMember(hediff, "level", out level)
                    || TryReadRoyaltyIntMember(hediff, "Level", out level))
                {
                    highest = Math.Max(highest, ClampRoyaltyPsylink(level));
                    continue;
                }
                try
                {
                    if (hediff.def?.stages != null && hediff.def.stages.Count > 0)
                        highest = Math.Max(highest, ClampRoyaltyPsylink(hediff.CurStageIndex + 1));
                }
                catch
                {
                    // A malformed modded hediff contributes no level; other hediffs remain readable.
                }
            }
            return highest;
        }

        private static void CapturePersonaCandidate(
            ThingWithComps weapon,
            Pawn pawn,
            HashSet<string> seen,
            List<PersonaWeaponSnapshot> result)
        {
            PersonaWeaponSnapshot captured;
            if (!TryCapturePersonaWeapon(weapon, pawn, out captured)
                || !string.Equals(captured.codedPawnId, pawn.GetUniqueLoadID(), StringComparison.Ordinal)
                || !seen.Add(captured.weaponThingId)) return;
            result.Add(captured);
        }

        private static List<PersonaTraitFact> CapturePersonaTraits(
            CompBladelinkWeapon comp,
            RoyaltyPolicySnapshot policy)
        {
            List<PersonaTraitFact> result = new List<PersonaTraitFact>();
            List<WeaponTraitDef> traits = comp?.TraitsListForReading;
            int cap = Math.Max(1, Math.Min(128, policy.maximumTraitCandidates));
            for (int i = 0; i < (traits?.Count ?? 0) && result.Count < cap; i++)
            {
                WeaponTraitDef trait = traits[i];
                if (trait == null || string.IsNullOrWhiteSpace(trait.defName)) continue;
                result.Add(new PersonaTraitFact
                {
                    traitDefName = trait.defName,
                    label = CleanRoyaltyText(trait.LabelCap, policy.maximumTraitLabelCharacters),
                    description = CleanRoyaltyText(
                        trait.description,
                        policy.maximumTraitDescriptionCharacters),
                    workerTypeToken = trait.workerClass?.FullName ?? string.Empty,
                    hasKillThought = trait.killThought != null,
                    hasBondedThought = trait.bondedThought != null,
                    hasBondedHediff = trait.bondedHediffs != null && trait.bondedHediffs.Count > 0,
                    hasEquippedHediff = trait.equippedHediffs != null && trait.equippedHediffs.Count > 0
                });
            }
            result.Sort((left, right) => string.CompareOrdinal(left.traitDefName, right.traitDefName));
            return result;
        }

        private static List<string> CaptureRoyalDutyCategories(Pawn pawn, RoyalTitleDef def)
        {
            List<string> result = new List<string>();
            Pawn_RoyaltyTracker royalty = pawn?.royalty;
            if (def == null || royalty == null) return result;
            if (royalty.allowRoomRequirements && def.throneRoomRequirements?.Count > 0) result.Add("throne_room");
            if (royalty.allowRoomRequirements && def.bedroomRequirements?.Count > 0) result.Add("bedroom");
            if (royalty.allowApparelRequirements && def.requiredApparel?.Count > 0) result.Add("apparel");
            if (def.foodRequirement.Defined) result.Add("food");
            if (def.disabledWorkTags != WorkTags.None) result.Add("work_restriction");
            if (def.disabledJoyKinds?.Count > 0) result.Add("joy_restriction");
            if (def.decreeTags?.Count > 0 || def.decreeMtbDays > 0f) result.Add("decree");
            if (def.speechCooldown.max > 0) result.Add("speech");
            return result;
        }

        private static string CleanRoyaltyText(string value, int maximumCharacters)
        {
            string cleaned = PromptTextSanitizer.LocalizedPromptText(value).Replace(";", ",");
            int cap = Math.Max(1, maximumCharacters);
            return cleaned.Length <= cap ? cleaned : cleaned.Substring(0, cap).TrimEnd();
        }

        private static bool TryReadRoyaltyIntMember(object instance, string memberName, out int value)
        {
            value = 0;
            if (instance == null) return false;
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = instance.GetType();
            FieldInfo field = type.GetField(memberName, flags);
            if (field != null && TryCoerceRoyaltyInt(field.GetValue(instance), out value)) return true;
            PropertyInfo property = type.GetProperty(memberName, flags);
            return property != null && property.GetIndexParameters().Length == 0
                && TryCoerceRoyaltyInt(property.GetValue(instance, null), out value);
        }

        private static bool TryCoerceRoyaltyInt(object raw, out int value)
        {
            value = 0;
            if (raw is int) { value = (int)raw; return true; }
            if (raw is float) { value = (int)(float)raw; return true; }
            if (raw is double) { value = (int)(double)raw; return true; }
            return raw != null && int.TryParse(raw.ToString(), out value);
        }

        private static int ClampRoyaltyPsylink(int level)
        {
            return level < 1 ? 0 : Math.Min(6, level);
        }

        private static bool RoyaltyMatchesDefName(List<string> names, string defName)
        {
            if (names == null || string.IsNullOrWhiteSpace(defName)) return false;
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], defName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}

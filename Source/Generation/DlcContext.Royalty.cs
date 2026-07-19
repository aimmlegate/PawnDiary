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
        /// Returns the exact persona weapon currently bonded to this pawn. Vanilla keeps
        /// equipment.bondedWeapon pointing at a merely dropped/swapped weapon and clears it only on
        /// UnCode, so this one reference distinguishes observable separation from unavailable cleanup
        /// without scanning every equipment and inventory item on each reconciliation pass.
        /// </summary>
        public static List<PersonaWeaponSnapshot> CapturePersonaWeapons(Pawn pawn)
        {
            List<PersonaWeaponSnapshot> result = new List<PersonaWeaponSnapshot>();
            if (!ModsConfig.RoyaltyActive || pawn == null) return result;
            PersonaWeaponSnapshot captured;
            if (TryCapturePersonaWeapon(pawn.equipment?.bondedWeapon as ThingWithComps, pawn, out captured)
                && string.Equals(captured.codedPawnId, pawn.GetUniqueLoadID(), StringComparison.Ordinal))
            {
                result.Add(captured);
            }
            return result;
        }

        /// <summary>
        /// Proves that the killer is currently wielding their exact bonded persona weapon and copies
        /// only that weapon's configured kill-memory Def names. An empty list is still a valid persona
        /// kill scope: it means the milestone can qualify but there is no thought side effect to own.
        /// </summary>
        public static bool TryCapturePersonaKillThoughtDefNames(Pawn pawn, out List<string> thoughtDefNames)
        {
            // Keep the no-Royalty/no-equipment hot path allocation-free. Pawn.Kill calls this
            // unconditionally so the DLC gate must precede construction of the result list.
            thoughtDefNames = null;
            if (!ModsConfig.RoyaltyActive || pawn?.equipment == null) return false;
            ThingWithComps weapon = pawn.equipment.bondedWeapon as ThingWithComps;
            PersonaWeaponSnapshot captured;
            if (!TryCapturePersonaWeapon(weapon, pawn, out captured)
                || !captured.isCurrentlyPrimary
                || !string.Equals(captured.codedPawnId, pawn.GetUniqueLoadID(), StringComparison.Ordinal))
                return false;

            thoughtDefNames = new List<string>();
            CompBladelinkWeapon comp = weapon.TryGetComp<CompBladelinkWeapon>();
            List<WeaponTraitDef> traits = comp?.TraitsListForReading;
            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < (traits?.Count ?? 0); i++)
            {
                string defName = traits[i]?.killThought?.defName;
                if (!string.IsNullOrWhiteSpace(defName) && unique.Add(defName))
                    thoughtDefNames.Add(defName.Trim());
            }
            thoughtDefNames.Sort(StringComparer.Ordinal);
            return true;
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
                RoyalTitleSnapshot captured = CaptureRoyalTitleSnapshot(
                    pawn, faction, def, title.receivedTick, title.wasInherited);
                RoyalTitleSnapshot previous;
                if (!byFaction.TryGetValue(factionId, out previous)
                    || captured.seniority > previous.seniority)
                    byFaction[factionId] = captured;
            }
            result.AddRange(byFaction.Values);
            result.Sort((left, right) => string.CompareOrdinal(left.factionId, right.factionId));
            return result;
        }

        /// <summary>Copies the pawn's current highest title in one exact faction, or null when absent.</summary>
        public static RoyalTitleSnapshot CaptureRoyalTitleForFaction(Pawn pawn, Faction faction)
        {
            if (!ModsConfig.RoyaltyActive || pawn?.royalty == null || faction == null) return null;
            string factionId = faction.GetUniqueLoadID() ?? string.Empty;
            List<RoyalTitleSnapshot> titles = CaptureRoyalTitles(pawn);
            for (int i = 0; i < titles.Count; i++)
                if (string.Equals(titles[i]?.factionId, factionId, StringComparison.Ordinal)) return titles[i];
            return null;
        }

        /// <summary>
        /// Projects the private post-title callback's live tracker/faction/Defs into exact detached
        /// before/after facts. The previous Def may no longer exist in the tracker's live list.
        /// </summary>
        public static bool TryCaptureRoyalTitleTransition(
            Pawn_RoyaltyTracker tracker,
            Faction faction,
            RoyalTitleDef previousDef,
            RoyalTitleDef newDef,
            out Pawn pawn,
            out RoyalTitleSnapshot previous,
            out RoyalTitleSnapshot current)
        {
            pawn = null;
            previous = null;
            current = null;
            if (!ModsConfig.RoyaltyActive || tracker?.pawn == null || faction == null) return false;
            pawn = tracker.pawn;
            RoyalTitleSnapshot liveCurrent = CaptureRoyalTitleForFaction(pawn, faction);
            if (previousDef != null)
                previous = CaptureRoyalTitleSnapshot(pawn, faction, previousDef, -1, false);
            if (newDef != null)
            {
                current = liveCurrent != null
                    && string.Equals(liveCurrent.titleDefName, newDef.defName, StringComparison.Ordinal)
                    ? liveCurrent
                    : CaptureRoyalTitleSnapshot(pawn, faction, newDef, -1, false);
            }
            return previous != null || current != null;
        }

        /// <summary>
        /// Copies vanilla's inheritance-worker result immediately. This is candidate evidence only;
        /// callers must still observe the enclosing death tracker's wasInherited commit.
        /// </summary>
        public static bool TryCaptureSuccessionCandidate(
            RoyalTitleDef title,
            Pawn deceased,
            Faction faction,
            RoyalTitleInheritanceOutcome outcome,
            int tick,
            out RoyalSuccessionCandidateSnapshot snapshot)
        {
            snapshot = null;
            Pawn heir = outcome.heir;
            if (!ModsConfig.RoyaltyActive || title == null || deceased == null || faction == null
                || heir == null || deceased.royalty == null || heir.royalty == null) return false;
            RoyalTitleSnapshot previous = CaptureRoyalTitleForFaction(heir, faction);
            RoyalTitleSnapshot inherited = CaptureRoyalTitleSnapshot(heir, faction, title, -1, false);
            if (inherited == null) return false;
            snapshot = new RoyalSuccessionCandidateSnapshot
            {
                deceasedPawnId = deceased.GetUniqueLoadID() ?? string.Empty,
                deceasedPawnName = CleanRoyaltyText(deceased.LabelShortCap, 120),
                heirPawnId = heir.GetUniqueLoadID() ?? string.Empty,
                heirPawnName = CleanRoyaltyText(heir.LabelShortCap, 120),
                factionId = faction.GetUniqueLoadID() ?? string.Empty,
                factionName = CleanRoyaltyText(faction.Name, 120),
                inheritedTitleDefName = title.defName ?? string.Empty,
                inheritedTitleLabel = inherited.titleLabel,
                inheritedTitleSeniority = Math.Max(0, title.seniority),
                previousHeirTitleDefName = previous?.titleDefName ?? string.Empty,
                previousHeirTitleLabel = previous?.titleLabel ?? string.Empty,
                previousHeirTitleSeniority = previous?.seniority ?? -1,
                heirAlreadyHeldEqualOrHigherTitle = outcome.heirTitleHigher,
                candidateTick = Math.Max(0, tick)
            };
            return !string.IsNullOrWhiteSpace(snapshot.deceasedPawnId)
                && !string.IsNullOrWhiteSpace(snapshot.heirPawnId)
                && !string.IsNullOrWhiteSpace(snapshot.factionId)
                && !string.IsNullOrWhiteSpace(snapshot.inheritedTitleDefName);
        }

        /// <summary>Proves the exact deceased title row was marked inherited by vanilla.</summary>
        public static RoyalSuccessionCommitObservation CaptureSuccessionCommit(
            Pawn deceased,
            RoyalSuccessionCandidateSnapshot candidate,
            int tick)
        {
            if (!ModsConfig.RoyaltyActive || deceased?.royalty == null || candidate == null) return null;
            List<RoyalTitle> titles = deceased.royalty.AllTitlesForReading;
            for (int i = 0; i < (titles?.Count ?? 0); i++)
            {
                RoyalTitle row = titles[i];
                if (!string.Equals(row?.def?.defName, candidate.inheritedTitleDefName,
                        StringComparison.Ordinal)
                    || !string.Equals(row?.faction?.GetUniqueLoadID(), candidate.factionId,
                        StringComparison.Ordinal)) continue;
                return new RoyalSuccessionCommitObservation
                {
                    correlationId = candidate.correlationId,
                    deceasedPawnId = deceased.GetUniqueLoadID() ?? string.Empty,
                    factionId = candidate.factionId,
                    inheritedTitleDefName = candidate.inheritedTitleDefName,
                    wasInherited = row.wasInherited,
                    commitTick = Math.Max(0, tick)
                };
            }
            return null;
        }

        /// <summary>Copies an explicit ChangeRoyalHeir quest edge before vanilla changes the heir.</summary>
        public static bool TryCaptureHeirAppointment(
            QuestPart_ChangeHeir questPart,
            int tick,
            out RoyalHeirAppointmentSnapshot snapshot)
        {
            snapshot = null;
            Pawn holder = questPart?.holder;
            Pawn heir = questPart?.heir;
            Faction faction = questPart?.faction;
            if (!ModsConfig.RoyaltyActive || questPart == null || questPart.done
                || holder?.royalty == null || heir == null || faction == null) return false;
            Pawn previousHeir = holder.royalty.GetHeir(faction);
            RoyalTitleSnapshot title = CaptureRoyalTitleForFaction(holder, faction);
            if (title == null) return false;
            snapshot = new RoyalHeirAppointmentSnapshot
            {
                correlationId = "heir-appointment|" + (holder.GetUniqueLoadID() ?? string.Empty)
                    + "|" + (faction.GetUniqueLoadID() ?? string.Empty) + "|" + Math.Max(0, tick),
                sourceToken = "change_royal_heir_quest",
                titleHolderPawnId = holder.GetUniqueLoadID() ?? string.Empty,
                titleHolderPawnName = CleanRoyaltyText(holder.LabelShortCap, 120),
                previousHeirPawnId = previousHeir?.GetUniqueLoadID() ?? string.Empty,
                previousHeirPawnName = CleanRoyaltyText(previousHeir?.LabelShortCap, 120),
                heirPawnId = heir.GetUniqueLoadID() ?? string.Empty,
                heirPawnName = CleanRoyaltyText(heir.LabelShortCap, 120),
                factionId = faction.GetUniqueLoadID() ?? string.Empty,
                factionName = CleanRoyaltyText(faction.Name, 120),
                titleDefName = title.titleDefName,
                titleLabel = title.titleLabel,
                observedTick = Math.Max(0, tick)
            };
            return RoyalSuccessionPolicy.ValidAppointment(snapshot);
        }

        /// <summary>Verifies the explicit quest actually committed its promised heir.</summary>
        public static bool HeirAppointmentCommitted(
            QuestPart_ChangeHeir questPart,
            RoyalHeirAppointmentSnapshot snapshot)
        {
            if (!ModsConfig.RoyaltyActive || questPart == null || snapshot == null || !questPart.done
                || questPart.holder?.royalty == null || questPart.faction == null) return false;
            Pawn current = questPart.holder.royalty.GetHeir(questPart.faction);
            return string.Equals(current?.GetUniqueLoadID(), snapshot.heirPawnId, StringComparison.Ordinal);
        }

        /// <summary>Reads the exact bestowing target/bestower/faction only inside the guarded adapter.</summary>
        public static bool TryCaptureBestowingActors(
            LordJob_Ritual ritualJob,
            out Pawn target,
            out Pawn bestower,
            out Faction faction)
        {
            target = null;
            bestower = null;
            faction = null;
            if (!ModsConfig.RoyaltyActive || ritualJob == null) return false;
            LordJob_BestowingCeremony bestowing = ritualJob as LordJob_BestowingCeremony;
            if (bestowing?.target == null || bestowing.bestower == null) return false;
            target = bestowing.target;
            bestower = bestowing.bestower;
            faction = bestowing.bestower.Faction;
            return faction != null;
        }

        /// <summary>Matches the DLC-safe XML-owned parent item defName for a neuroformer use.</summary>
        public static bool IsNeuroformerUse(CompUseEffect_InstallImplant comp)
        {
            if (!ModsConfig.RoyaltyActive || comp?.parent?.def == null) return false;
            return RoyalMutationRoutePolicy.IsNeuroformer(
                comp.parent.def.defName,
                DiaryRoyaltyPolicy.Snapshot());
        }

        /// <summary>
        /// Projects only exact Thought_MemoryRoyalTitle award/loss memories. Ordinary thoughts and
        /// other memories using a similarly named Def remain untouched.
        /// </summary>
        public static bool TryCaptureRoyalTitleThought(
            Pawn pawn,
            Thought_Memory memory,
            int tick,
            out RoyalTitleThoughtSnapshot snapshot)
        {
            snapshot = null;
            if (!ModsConfig.RoyaltyActive || pawn == null || memory?.def == null) return false;
            Thought_MemoryRoyalTitle royalMemory = memory as Thought_MemoryRoyalTitle;
            RoyalTitleDef titleDef = royalMemory?.titleDef;
            if (titleDef == null || string.IsNullOrWhiteSpace(titleDef.defName)) return false;
            string relationship = ReferenceEquals(memory.def, titleDef.awardThought)
                ? RoyalTitleThoughtRelationshipTokens.Award
                : ReferenceEquals(memory.def, titleDef.lostThought)
                    ? RoyalTitleThoughtRelationshipTokens.Loss
                    : string.Empty;
            if (!RoyalTitleThoughtRelationshipTokens.IsKnown(relationship)) return false;
            snapshot = new RoyalTitleThoughtSnapshot
            {
                pawnId = pawn.GetUniqueLoadID() ?? string.Empty,
                titleDefName = titleDef.defName,
                relationshipToken = relationship,
                tick = Math.Max(0, tick)
            };
            return !string.IsNullOrWhiteSpace(snapshot.pawnId);
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

        private static RoyalTitleSnapshot CaptureRoyalTitleSnapshot(
            Pawn pawn,
            Faction faction,
            RoyalTitleDef def,
            int receivedTick,
            bool wasInherited)
        {
            if (!ModsConfig.RoyaltyActive || pawn == null || faction == null || def == null) return null;
            return new RoyalTitleSnapshot
            {
                pawnId = pawn.GetUniqueLoadID() ?? string.Empty,
                factionId = faction.GetUniqueLoadID() ?? string.Empty,
                factionName = CleanRoyaltyText(faction.Name, 120),
                titleDefName = def.defName ?? string.Empty,
                titleLabel = CleanRoyaltyText(def.GetLabelCapFor(pawn), 120),
                seniority = Math.Max(0, def.seniority),
                receivedTick = Math.Max(-1, receivedTick),
                wasInherited = wasInherited,
                dutyCategoryTokens = CaptureRoyalDutyCategories(pawn, def)
            };
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
            try
            {
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Type type = instance.GetType();
                FieldInfo field = type.GetField(memberName, flags);
                if (field != null && TryCoerceRoyaltyInt(field.GetValue(instance), out value)) return true;
                PropertyInfo property = type.GetProperty(memberName, flags);
                return property != null && property.GetIndexParameters().Length == 0
                    && TryCoerceRoyaltyInt(property.GetValue(instance, null), out value);
            }
            catch
            {
                // A malformed modded getter contributes no level; the remaining matching hediffs
                // still deserve a chance to provide a valid psylink observation.
                value = 0;
                return false;
            }
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

// Loaded-game acceptance for Royalty Phases 2 and 3. The suite drives vanilla
// CompBladelinkWeapon/Pawn.Kill/Tale methods, proves late-visible historical bonds are adopted
// silently, verifies current narrative context against live coded-weapon truth, and audits every
// exact Harmony seam used by the lifecycle and its first-kill/death enrichment.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Loaded exact-hook and reconciliation acceptance for persona weapons.</summary>
    [TestSuite]
    public static class PawnDiaryRoyaltyFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly FieldInfo PersonaBondsField =
            typeof(DiaryGameComponent).GetField("royaltyPersonaBonds", PrivateInstance);
        private static readonly FieldInfo EventsField =
            typeof(DiaryGameComponent).GetField("events", PrivateInstance);
        private static readonly MethodInfo ReconcileMethod =
            typeof(DiaryGameComponent).GetMethod("ReconcileRoyaltyPersonaBonds", PrivateInstance);
        private static readonly MethodInfo ResetFreeColonistSnapshotMethod =
            typeof(DiaryGameComponent).GetMethod("ResetFreeColonistSnapshot", PrivateStatic);

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(
                "personaWeaponLifecycle", "personaWeaponMilestone", "talecombat");
            pawn = scope.CreateAdultColonist();
            scope.SpawnAsLiveColonist(pawn);
            scope.RegisterCleanup(() => RemovePersonaRows(string.Empty, pawn?.GetUniqueLoadID()));
        }

        [AfterEach]
        public static void TearDown()
        {
            try { scope?.TearDown(); }
            finally
            {
                scope = null;
                pawn = null;
            }
        }

        /// <summary>
        /// Codes a real vanilla persona weapon, removes its saved row to model an old caravan/map that
        /// was absent at the global baseline, then proves reconciliation silently adopts it without
        /// inferring separation from the same first sight. A later observation may start that timer;
        /// UnCode must immediately remove the stale saved relationship from current narrative context.
        /// </summary>
        [Test]
        public static void RealCodingLateVisibilityAndUncodeFollowLiveTruth()
        {
            if (!RequireRoyaltyOrSkip(nameof(RealCodingLateVisibilityAndUncodeFollowLiveTruth))) return;
            ThingWithComps weapon;
            CompBladelinkWeapon comp;
            CreatePersonaWeapon(out weapon, out comp);

            DiaryEvent formed = scope.FireAndRequireEvent(
                () => comp.CodeFor(pawn),
                PersonaWeaponEventData.BondFormedDefName,
                pawn,
                null);
            scope.RequireSoloRef(formed, pawn);
            List<PersonaWeaponSnapshot> visible = DlcContext.CapturePersonaWeapons(pawn);
            PawnDiaryRimTestScope.Require(visible.Count == 1
                    && visible[0].weaponThingId == weapon.GetUniqueLoadID(),
                "Vanilla coding did not expose the exact bonded weapon through the pawn tracker.");
            PawnDiaryRimTestScope.Require(
                scope.Component.RoyaltyNarrativeSnapshotFor(pawn, formed.tick).personaBonds.Count == 1,
                "A newly coded live persona bond was missing from current narrative context.");

            string weaponId = weapon.GetUniqueLoadID();
            RemovePersonaRows(weaponId, pawn.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(ReconcileMethod != null
                    && ResetFreeColonistSnapshotMethod != null,
                "Could not resolve the exact reconciliation/cache-reset fixture seams.");
            scope.RequireNoNewEvent(() =>
            {
                // The test runner is paused, so spawning the pawn and reconciling happen in one game
                // tick. Production intentionally caches free colonists for that tick; clear the
                // pre-spawn snapshot to model the next scheduled observation after a caravan/map load.
                ResetFreeColonistSnapshotMethod.Invoke(null, null);
                ReconcileMethod.Invoke(scope.Component, null);
            });
            PersonaBondState adopted = PersonaRows().SingleOrDefault(row =>
                row != null && row.weaponThingId == weaponId);
            PawnDiaryRimTestScope.Require(adopted != null
                    && adopted.phaseToken == PersonaBondPhaseTokens.Active
                    && adopted.firstConsequentialKillObserved,
                "Late-visible historical persona bond was not silently adopted as a consumed baseline; "
                    + Describe(adopted) + ".");

            scope.RequireNoNewEvent(() => ReconcileMethod?.Invoke(scope.Component, null));
            adopted = PersonaRows().SingleOrDefault(row =>
                row != null && row.weaponThingId == weaponId);
            PawnDiaryRimTestScope.Require(adopted != null
                    && adopted.phaseToken == PersonaBondPhaseTokens.SeparationPending,
                "A later not-primary observation did not begin normal separation inference.");

            scope.RequireNoNewEvent(() => comp.UnCode());
            PawnDiaryRimTestScope.Require(
                scope.Component.RoyaltyNarrativeSnapshotFor(pawn, formed.tick).personaBonds.Count == 0,
                "UnCode left a saved-only persona bond in current narrative context.");
        }

        /// <summary>Audits all six exact RimWorld 1.6 methods and the fail-safe owner ID.</summary>
        [Test]
        public static void ExactPersonaWeaponHooksAreRegistered()
        {
            if (!RequireRoyaltyOrSkip(nameof(ExactPersonaWeaponHooksAreRegistered))) return;
            PawnDiaryRimTestScope.Require(DiaryRoyaltyPatches.HooksReady,
                "Pawn Diary reported an incomplete Royalty persona hook set.");
            Type type = typeof(CompBladelinkWeapon);
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.CodeFor),
                    new[] { typeof(Pawn) }),
                "CodeForPrefix", "CodeForPostfix");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.Notify_Equipped),
                    new[] { typeof(Pawn) }),
                null, "EquippedPostfix");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.Notify_EquipmentLost),
                    new[] { typeof(Pawn) }),
                null, "EquipmentLostPostfix");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.PostDestroy),
                    new[] { typeof(DestroyMode), typeof(Map) }),
                "DestroyPrefix", null);
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.Notify_MapRemoved),
                    Type.EmptyTypes),
                "MapRemovedPrefix", null);
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(type, nameof(CompBladelinkWeapon.UnCode), Type.EmptyTypes),
                "UncodePrefix", null);
        }

        /// <summary>
        /// Codes and equips a real persona weapon, kills a guaranteed vanilla major threat through
        /// Pawn.Kill, and proves the qualifying KilledMajorThreat Tale becomes one canonical killer
        /// page. A second real major-threat kill cannot become another first-kill milestone.
        /// </summary>
        [Test]
        public static void RealMajorThreatKillCreatesOneCanonicalMilestone()
        {
            if (!RequireRoyaltyOrSkip(nameof(RealMajorThreatKillCreatesOneCanonicalMilestone))) return;
            ThingWithComps weapon;
            CompBladelinkWeapon comp;
            CreatePersonaWeapon(out weapon, out comp);
            comp.CodeFor(pawn);
            pawn.equipment.AddEquipment(weapon);
            PawnDiaryRimTestScope.Require(ReferenceEquals(pawn.equipment.Primary, weapon),
                "The real persona weapon did not become the killer's current primary equipment.");

            Pawn firstVictim = CreateMajorThreatVictim();
            RegisterDeadPawnCleanup(firstVictim);
            DamageInfo firstDamage = new DamageInfo(
                DamageDefOf.Crush, 10000f, instigator: pawn, weapon: weapon.def);
            int before = CountEvents(PersonaMilestoneContextFormatter.FirstKillDefName);
            DiaryEvent milestone = scope.FireAndRequireEvent(
                () => firstVictim.Kill(firstDamage),
                PersonaMilestoneContextFormatter.FirstKillDefName,
                pawn,
                null);
            scope.RequireSoloRef(milestone, pawn);
            PawnDiaryRimTestScope.Require(
                CountEvents(PersonaMilestoneContextFormatter.FirstKillDefName) == before + 1,
                "The first real major-threat kill did not create exactly one canonical milestone.");
            PawnDiaryRimTestScope.Require(
                milestone.gameContext != null
                    && milestone.gameContext.Contains("tale=PersonaWeaponFirstConsequentialKill")
                    && milestone.gameContext.Contains("tale_source_def=KilledMajorThreat")
                    && milestone.gameContext.Contains("persona_milestone=first_consequential_kill")
                    && !milestone.gameContext.Contains("persona_weapon="),
                "The canonical milestone lost its Tale domain, source Tale, or non-standalone marker contract.");
            PersonaBondState state = PersonaRows().SingleOrDefault(row => row != null
                && row.weaponThingId == weapon.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(state != null
                    && state.firstConsequentialKillObserved
                    && state.firstConsequentialKillEventRecorded,
                "The accepted real kill did not persist both observed truth and durable page ownership.");

            Pawn secondVictim = CreateMajorThreatVictim();
            RegisterDeadPawnCleanup(secondVictim);
            DamageInfo secondDamage = new DamageInfo(
                DamageDefOf.Crush, 10000f, instigator: pawn, weapon: weapon.def);
            secondVictim.Kill(secondDamage);
            PawnDiaryRimTestScope.Require(
                CountEvents(PersonaMilestoneContextFormatter.FirstKillDefName) == before + 1,
                "A later real major-threat kill was mislabeled as another first persona milestone.");
        }

        /// <summary>
        /// Kills a real bonded wielder through the neutral fallback path and proves the existing death
        /// page retained the pre-UnCode weapon relationship instead of creating a standalone ending.
        /// </summary>
        [Test]
        public static void BondedWielderDeathEnrichesExistingDeathPageBeforeUncode()
        {
            if (!RequireRoyaltyOrSkip(nameof(BondedWielderDeathEnrichesExistingDeathPageBeforeUncode))) return;
            RegisterDeadPawnCleanup(pawn);
            ThingWithComps weapon;
            CompBladelinkWeapon comp;
            CreatePersonaWeapon(out weapon, out comp);
            comp.CodeFor(pawn);
            pawn.equipment.AddEquipment(weapon);
            string weaponName = DlcContext.CapturePersonaWeapons(pawn).Single().displayName;

            DiaryEvent death = scope.FireAndRequireEvent(
                () => pawn.Kill(null),
                DeathFallbackSignal.DeathFallbackDefName,
                pawn,
                null);
            PawnDiaryRimTestScope.Require(death.HasDeathDescription(),
                "The bonded wielder's existing page was not a neutral death description.");
            PawnDiaryRimTestScope.Require(death.gameContext != null
                    && death.gameContext.Contains("persona_milestone=wielder_death")
                    && death.gameContext.Contains("persona_weapon_name=" + weaponName)
                    && death.gameContext.Contains("bond_end_cause=pawn_death")
                    && !death.gameContext.Contains("persona_weapon="),
                "The death page did not retain the exact pre-UnCode persona relationship context.");
            PawnDiaryRimTestScope.Require(
                CountEvents(PersonaWeaponEventData.BondEndedDefName) == 0,
                "Pawn death created a duplicate standalone persona-bond ending page.");
        }

        private static void CreatePersonaWeapon(
            out ThingWithComps weapon,
            out CompBladelinkWeapon comp)
        {
            weapon = null;
            comp = null;
            List<ThingDef> candidates = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def?.comps != null
                    && def.comps.Any(properties =>
                        properties?.compClass == typeof(CompBladelinkWeapon)))
                .OrderBy(def => def.defName, StringComparer.Ordinal)
                .ToList();
            for (int i = 0; i < candidates.Count && weapon == null; i++)
            {
                ThingDef def = candidates[i];
                ThingWithComps created = ThingMaker.MakeThing(
                    def,
                    def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null) as ThingWithComps;
                CompBladelinkWeapon createdComp = created?.TryGetComp<CompBladelinkWeapon>();
                if (createdComp != null)
                {
                    weapon = created;
                    comp = createdComp;
                }
                else if (created != null && !created.Destroyed)
                {
                    created.Destroy(DestroyMode.Vanish);
                }
            }

            PawnDiaryRimTestScope.Require(weapon != null && comp != null,
                "Royalty is active but no constructible CompBladelinkWeapon Def was available.");
            ThingWithComps cleanupWeapon = weapon;
            CompBladelinkWeapon cleanupComp = comp;
            scope.RegisterCleanup(() =>
            {
                Pawn owner = cleanupComp.CodedPawn;
                if (owner?.equipment != null && ReferenceEquals(owner.equipment.Primary, cleanupWeapon))
                    owner.equipment.Remove(cleanupWeapon);
                if (cleanupComp.CodedPawn != null) cleanupComp.UnCode();
                if (!cleanupWeapon.Destroyed) cleanupWeapon.Destroy(DestroyMode.Vanish);
            });
        }

        private static Pawn CreateMajorThreatVictim()
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Mech_CentipedeBlaster");
            PawnDiaryRimTestScope.Require(kind != null && kind.combatPower >= 400f,
                "The base-game centipede major-threat fixture Def was unavailable or retuned below certainty.");
            PawnDiaryRimTestScope.Require(Faction.OfMechanoids != null
                    && Faction.OfMechanoids.HostileTo(scope.PlayerFaction),
                "The loaded game had no hostile base-game mechanoid faction for the kill fixture.");
            Pawn victim = scope.CreateTrackedPawn(kind, Faction.OfMechanoids);
            scope.SpawnAsLiveColonist(victim);
            return victim;
        }

        private static int CountEvents(string defName)
        {
            DiaryEventRepository repository = EventsField?.GetValue(scope.Component)
                as DiaryEventRepository;
            PawnDiaryRimTestScope.Require(repository != null,
                "Could not read the event repository for Royalty milestone assertions.");
            return repository.AllEvents.Count(row => row != null
                && string.Equals(row.interactionDefName, defName, StringComparison.Ordinal));
        }

        private static void RegisterDeadPawnCleanup(Pawn deadPawn)
        {
            scope.RegisterCleanup(() =>
            {
                if (deadPawn != null && !deadPawn.Destroyed && Find.WorldPawns != null
                    && Find.WorldPawns.Contains(deadPawn)) Find.WorldPawns.RemovePawn(deadPawn);
            });
            scope.RegisterCleanup(() =>
            {
                Corpse corpse = deadPawn?.ParentHolder as Corpse;
                if (corpse != null && !corpse.Destroyed) corpse.Destroy(DestroyMode.Vanish);
            });
        }

        private static List<PersonaBondState> PersonaRows()
        {
            List<PersonaBondState> rows = PersonaBondsField?.GetValue(scope.Component)
                as List<PersonaBondState>;
            PawnDiaryRimTestScope.Require(rows != null,
                "Could not read DiaryGameComponent.royaltyPersonaBonds for fixture cleanup/assertion.");
            return rows;
        }

        private static string Describe(PersonaBondState state)
        {
            return state == null
                ? "row=missing"
                : "phase=" + (state.phaseToken ?? "<null>")
                    + ", firstKillConsumed=" + state.firstConsequentialKillObserved
                    + ", pawnId=" + (state.currentPawnId ?? "<null>");
        }

        private static void RemovePersonaRows(string weaponId, string pawnId)
        {
            if (scope?.Component == null || PersonaBondsField == null) return;
            List<PersonaBondState> rows = PersonaBondsField.GetValue(scope.Component)
                as List<PersonaBondState>;
            rows?.RemoveAll(row => row != null
                && ((!string.IsNullOrEmpty(weaponId) && row.weaponThingId == weaponId)
                    || (!string.IsNullOrEmpty(pawnId) && row.currentPawnId == pawnId)));
        }

        private static void RequireOwnedPatch(
            MethodBase target,
            string prefixName,
            string postfixName)
        {
            Patches patches = target == null ? null : Harmony.GetPatchInfo(target);
            PawnDiaryRimTestScope.Require(target != null && patches != null,
                "Expected a patched Royalty persona target, but the exact method was unavailable: "
                    + target + ".");
            if (prefixName != null)
            {
                PawnDiaryRimTestScope.Require(patches.Prefixes.Any(row =>
                        row.owner == "aimml.pawndiary"
                        && row.PatchMethod?.DeclaringType == typeof(DiaryRoyaltyPatches)
                        && row.PatchMethod.Name == prefixName),
                    "Expected Pawn Diary's " + prefixName + " on " + target + ".");
            }
            if (postfixName != null)
            {
                PawnDiaryRimTestScope.Require(patches.Postfixes.Any(row =>
                        row.owner == "aimml.pawndiary"
                        && row.PatchMethod?.DeclaringType == typeof(DiaryRoyaltyPatches)
                        && row.PatchMethod.Name == postfixName),
                    "Expected Pawn Diary's " + postfixName + " on " + target + ".");
            }
        }

        private static bool RequireRoyaltyOrSkip(string fixtureName)
        {
            if (ModsConfig.RoyaltyActive) return true;
            Log.Message("[Pawn Diary RimTest] SKIP " + fixtureName
                + ": Royalty is not active in this test profile.");
            return false;
        }
    }
}

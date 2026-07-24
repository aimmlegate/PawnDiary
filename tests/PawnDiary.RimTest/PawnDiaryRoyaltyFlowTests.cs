// Loaded-game acceptance for Royalty Phases 2, 3, and 8. The suite drives vanilla
// CompBladelinkWeapon/Pawn.Kill/Tale methods, proves late-visible historical bonds are adopted
// silently, verifies structural modded-trait compatibility and bounded elapsed reconciliation,
// checks game-boundary cache reset, and audits every exact lifecycle/first-kill Harmony seam.
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
        private const string KillThoughtTraitDefName = "OnKill_ThoughtGood";
        private const string KillThoughtDefName = "OnKill_GoodThought";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly FieldInfo PersonaBondsField =
            typeof(DiaryGameComponent).GetField("royaltyPersonaBonds", PrivateInstance);
        private static readonly FieldInfo EventsField =
            typeof(DiaryGameComponent).GetField("events", PrivateInstance);
        private static readonly MethodInfo ReconcileMethod =
            typeof(DiaryGameComponent).GetMethod("ReconcileRoyaltyPersonaBonds", PrivateInstance);
        private static readonly MethodInfo RunReconciliationIfDueMethod =
            typeof(DiaryGameComponent).GetMethod(
                "RunRoyaltyPersonaReconciliationIfDue", PrivateInstance);
        private static readonly FieldInfo NextReconciliationTickField =
            typeof(DiaryGameComponent).GetField(
                "nextRoyaltyPersonaReconciliationTick", PrivateInstance);
        private static readonly FieldInfo InitialArrivalScanPendingField =
            typeof(DiaryGameComponent).GetField("initialArrivalScanPending", PrivateInstance);
        private static readonly MethodInfo ResetFreeColonistSnapshotMethod =
            typeof(DiaryGameComponent).GetMethod("ResetFreeColonistSnapshot", PrivateStatic);
        private static readonly MethodInfo FlushAllTaleBatchesMethod =
            typeof(DiaryGameComponent).GetMethod("FlushAllTaleBatches", PrivateInstance);
        private static readonly FieldInfo PersonaWeaponTraitsField =
            typeof(CompBladelinkWeapon).GetField("traits", PrivateInstance);
        private static readonly MethodInfo DebugSetTicksGameMethod =
            typeof(TickManager).GetMethod(
                "DebugSetTicksGame",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(int) },
                null);

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(
                "personaWeaponLifecycle", "personaWeaponMilestone", "talecombat", "thoughtPositive");
            PersonaKillThoughtCorrelation.Clear();
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
                PersonaKillThoughtCorrelation.Clear();
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

        /// <summary>
        /// Sends synthetic modded WeaponTraitDefs through the real main-thread adapter. Structural
        /// kill-thought evidence is accepted regardless of localized wording, while a persuasive
        /// label and an unsafe Def identity remain unable to opt themselves into a prompt.
        /// </summary>
        [Test]
        public static void SyntheticModdedPersonaTraitsUseOnlyStructuralFacts()
        {
            if (!RequireRoyaltyOrSkip(nameof(SyntheticModdedPersonaTraitsUseOnlyStructuralFacts))) return;
            ThingWithComps weapon;
            CompBladelinkWeapon comp;
            CreatePersonaWeapon(out weapon, out comp);
            ThoughtDef killThought = DefDatabase<ThoughtDef>.GetNamedSilentFail(KillThoughtDefName);
            PawnDiaryRimTestScope.Require(PersonaWeaponTraitsField != null && killThought != null,
                "Could not resolve the persona trait adapter fixture seams.");

            List<WeaponTraitDef> original = PersonaWeaponTraitsField.GetValue(comp)
                as List<WeaponTraitDef>;
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            bool originalEnabled = policy.enabled;
            int originalCandidateCap = policy.maximumTraitCandidates;
            try
            {
                // Cached XML policy is mutable process state. Pin only the two values this fixture
                // needs, then restore them so a local compatibility profile cannot make the test
                // order-dependent or leak into the next loaded fixture.
                policy.enabled = true;
                policy.maximumTraitCandidates = Math.Max(3, originalCandidateCap);
                WeaponTraitDef structural = new WeaponTraitDef
                {
                    defName = "Phase8_ModPersonaKillSignal",
                    label = "quietly reflective",
                    description = "Localized display text deliberately unrelated to killing.",
                    killThought = killThought
                };
                WeaponTraitDef wordingOnly = new WeaponTraitDef
                {
                    defName = "Phase8_ModPersonaWordingOnly",
                    label = "bloodthirsty killer bond",
                    description = "English-looking words are display data, not policy evidence."
                };
                WeaponTraitDef malformed = new WeaponTraitDef
                {
                    defName = "Phase8|MalformedPersonaTrait",
                    label = "structural but unsafe",
                    killThought = killThought
                };
                PersonaWeaponTraitsField.SetValue(comp, new List<WeaponTraitDef>
                {
                    wordingOnly, malformed, structural
                });

                PersonaWeaponSnapshot captured;
                PawnDiaryRimTestScope.Require(
                    DlcContext.TryCapturePersonaWeapon(weapon, pawn, out captured)
                        && captured?.traits?.Count == 3,
                    "The live Royalty adapter did not copy all three synthetic trait candidates.");
                List<PersonaTraitFact> selected = PersonaTraitPolicy.Select(
                    captured.traits,
                    PersonaTraitEventTokens.Kill,
                    "phase8-loaded-modded-traits",
                    policy);
                PawnDiaryRimTestScope.Require(selected.Count == 1
                        && selected[0].traitDefName == structural.defName,
                    "Persona trait selection used localized wording, retained an unsafe identity, "
                        + "or lost structural kill-thought evidence.");
            }
            finally
            {
                PersonaWeaponTraitsField.SetValue(comp, original);
                policy.maximumTraitCandidates = originalCandidateCap;
                policy.enabled = originalEnabled;
            }
        }

        /// <summary>
        /// Models an arbitrarily overdue reconciliation deadline with a real pending persona bond.
        /// One current-state pass consumes the elapsed separation, rebases from now, and a second
        /// call at the same game time cannot poll or duplicate the page.
        /// </summary>
        [Test]
        public static void LongTimeSkipRunsOneBoundedPersonaReconciliation()
        {
            if (!RequireRoyaltyOrSkip(nameof(LongTimeSkipRunsOneBoundedPersonaReconciliation))) return;
            PawnDiaryRimTestScope.Require(RunReconciliationIfDueMethod != null
                    && NextReconciliationTickField != null
                    && InitialArrivalScanPendingField != null
                    && DebugSetTicksGameMethod != null
                    && Find.TickManager != null,
                "Could not resolve the elapsed-time reconciliation fixture seams.");
            int originalTick = Find.TickManager.TicksGame;
            long originalDeadline = (long)NextReconciliationTickField.GetValue(scope.Component);
            bool originalArrivalPending =
                (bool)InitialArrivalScanPendingField.GetValue(scope.Component);
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            bool originalEnabled = policy.enabled;
            int originalThreshold = policy.separationThresholdTicks;
            int originalCadence = policy.reconciliationCadenceTicks;
            try
            {
                const int testSeparationTicks = 1000;
                const int testCadenceTicks = 2500;
                policy.enabled = true;
                policy.separationThresholdTicks = testSeparationTicks;
                policy.reconciliationCadenceTicks = testCadenceTicks;
                InitialArrivalScanPendingField.SetValue(scope.Component, false);

                ThingWithComps weapon;
                CompBladelinkWeapon comp;
                CreatePersonaWeapon(out weapon, out comp);
                scope.FireAndRequireEvent(
                    () => comp.CodeFor(pawn),
                    PersonaWeaponEventData.BondFormedDefName,
                    pawn,
                    null);
                scope.RequireNoNewEvent(() =>
                    scope.Component.ObserveRoyaltyPersonaEquipment(weapon, pawn));
                PersonaBondState pending = PersonaRows().SingleOrDefault(row => row != null
                    && row.weaponThingId == weapon.GetUniqueLoadID());
                PawnDiaryRimTestScope.Require(pending != null
                        && pending.phaseToken == PersonaBondPhaseTokens.SeparationPending,
                    "The time-skip fixture did not establish a pending non-primary persona bond.");

                long futureLong = (long)originalTick
                    + testSeparationTicks
                    + ((long)testCadenceTicks * 20L);
                PawnDiaryRimTestScope.Require(futureLong <= int.MaxValue,
                    "The loaded game's tick counter is too near Int32.MaxValue for this reversible fixture.");
                int future = (int)futureLong;
                // Backdating the deadline models every skipped cadence without asking the game loop
                // to run thousands of synthetic ticks. DebugSetTicksGame is synchronous, so the
                // global tick cannot advance a storyteller/gameplay tick before finally restores it.
                NextReconciliationTickField.SetValue(
                    scope.Component, Math.Max(0L, (long)originalTick - testCadenceTicks));
                DebugSetTicksGameMethod.Invoke(Find.TickManager, new object[] { future });
                scope.FireAndRequireEvent(
                    () => RunReconciliationIfDueMethod.Invoke(
                        scope.Component, new object[] { future }),
                    PersonaWeaponEventData.BondSeparatedDefName,
                    pawn,
                    null);
                PawnDiaryRimTestScope.Require(
                    (long)NextReconciliationTickField.GetValue(scope.Component)
                        == RoyaltyReconciliationSchedule.NextDeadline(future, testCadenceTicks),
                    "The overdue reconciliation deadline was not rebased from current game time.");
                scope.RequireNoNewEvent(() => RunReconciliationIfDueMethod.Invoke(
                    scope.Component, new object[] { future }));
            }
            finally
            {
                DebugSetTicksGameMethod.Invoke(Find.TickManager, new object[] { originalTick });
                NextReconciliationTickField.SetValue(scope.Component, originalDeadline);
                InitialArrivalScanPendingField.SetValue(scope.Component, originalArrivalPending);
                policy.reconciliationCadenceTicks = originalCadence;
                policy.separationThresholdTicks = originalThreshold;
                policy.enabled = originalEnabled;
            }
            PawnDiaryRimTestScope.Require(Find.TickManager.TicksGame == originalTick,
                "The elapsed-time fixture did not restore the global game tick.");
        }

        /// <summary>
        /// Audits the six persona lifecycle seams plus the seven exact title/cause/succession seams and their
        /// fail-safe owner ID. Private title registration is verified against its exact signature.
        /// </summary>
        [Test]
        public static void ExactPersonaWeaponHooksAreRegistered()
        {
            if (!RequireRoyaltyOrSkip(nameof(ExactPersonaWeaponHooksAreRegistered))) return;
            PawnDiaryRimTestScope.Require(DiaryRoyaltyPatches.HooksReady,
                "Pawn Diary reported an incomplete Royalty persona hook set.");
            Type type = typeof(CompBladelinkWeapon);
            MethodBase codeForTarget = AccessTools.DeclaredMethod(
                type, nameof(CompBladelinkWeapon.CodeFor), new[] { typeof(Pawn) });
            RequireOwnedPatch(
                codeForTarget,
                "CodeForPrefix", "CodeForPostfix");
            string caughtFailureDetail;
            PawnDiaryRimTestScope.Require(
                !DiaryRoyaltyPatches.ProbePatchFailureForTests(
                    codeForTarget, out caughtFailureDetail)
                    && caughtFailureDetail.Contains("NullReferenceException"),
                "The caught Royalty patch-failure path discarded its exception type/message detail.");
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

            PawnDiaryRimTestScope.Require(DiaryRoyaltyPatches.TitleHookReady
                    && DiaryRoyaltyPatches.BestowingHookReady
                    && DiaryRoyaltyPatches.AnimaHookReady
                    && DiaryRoyaltyPatches.NeuroformerHookReady,
                "Pawn Diary reported an incomplete Royalty title/cause hook set.");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(typeof(Pawn_RoyaltyTracker), "OnPostTitleChanged",
                    new[] { typeof(Faction), typeof(RoyalTitleDef), typeof(RoyalTitleDef) }),
                null, "TitleChangedPostfix");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(typeof(RitualOutcomeEffectWorker_Bestowing), "Apply",
                    new[] { typeof(float), typeof(Dictionary<Pawn, int>), typeof(LordJob_Ritual) }),
                "BestowingPrefix", "BestowingPostfix", "BestowingFinalizer");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(typeof(CompPsylinkable), "FinishLinkingRitual",
                    new[] { typeof(Pawn), typeof(int) }),
                "AnimaPrefix", "AnimaPostfix", "AnimaFinalizer");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(typeof(CompUseEffect_InstallImplant), "DoEffect",
                    new[] { typeof(Pawn) }),
                "NeuroformerPrefix", "NeuroformerPostfix", "NeuroformerFinalizer");

            PawnDiaryRimTestScope.Require(DiaryRoyaltyPatches.SuccessionCandidateHookReady
                    && DiaryRoyaltyPatches.SuccessionCommitHookReady
                    && DiaryRoyaltyPatches.HeirAppointmentHookReady,
                "Pawn Diary reported an incomplete Royalty succession/appointment hook set.");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(typeof(RoyalTitleDefExt), "TryInherit", new[]
                {
                    typeof(RoyalTitleDef), typeof(Pawn), typeof(Faction),
                    typeof(RoyalTitleInheritanceOutcome).MakeByRefType()
                }),
                null, "SuccessionCandidatePostfix");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(typeof(Pawn_RoyaltyTracker), "Notify_PawnKilled",
                    Type.EmptyTypes),
                "SuccessionDeathPrefix", "SuccessionDeathPostfix", "SuccessionDeathFinalizer");
            RequireOwnedPatch(
                AccessTools.DeclaredMethod(typeof(QuestPart_ChangeHeir),
                    "Notify_QuestSignalReceived", new[] { typeof(Signal) }),
                "HeirAppointmentPrefix", "HeirAppointmentPostfix", "HeirAppointmentFinalizer");
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
            ForceKillThoughtTrait(comp);
            comp.CodeFor(pawn);
            pawn.equipment.AddEquipment(weapon);
            PawnDiaryRimTestScope.Require(ReferenceEquals(pawn.equipment.Primary, weapon),
                "The real persona weapon did not become the killer's current primary equipment.");

            Pawn firstVictim = CreateMajorThreatVictim();
            RegisterDeadPawnCleanup(firstVictim);
            DamageInfo firstDamage = new DamageInfo(
                DamageDefOf.Crush, 10000f, instigator: pawn, weapon: weapon.def);
            int before = CountEvents(PersonaMilestoneContextFormatter.FirstKillDefName);
            int thoughtBefore = CountEventsForPawn(KillThoughtDefName, pawn);
            int combatBefore = CountTaleBatchesForPawn("talecombat", pawn);
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
            PawnDiaryRimTestScope.Require(
                CountEventsForPawn(KillThoughtDefName, pawn) == thoughtBefore,
                "The canonical milestone left a duplicate persona kill-thought page.");
            FlushAllTaleBatches();
            PawnDiaryRimTestScope.Require(
                CountTaleBatchesForPawn("talecombat", pawn) == combatBefore,
                "Flushing delayed Tale batches exposed a companion combat page for the canonical kill.");

            Pawn secondVictim = CreateMajorThreatVictim();
            RegisterDeadPawnCleanup(secondVictim);
            DamageInfo secondDamage = new DamageInfo(
                DamageDefOf.Crush, 10000f, instigator: pawn, weapon: weapon.def);
            secondVictim.Kill(secondDamage);
            PawnDiaryRimTestScope.Require(
                CountEvents(PersonaMilestoneContextFormatter.FirstKillDefName) == before + 1,
                "A later real major-threat kill was mislabeled as another first persona milestone.");
            PawnDiaryRimTestScope.Require(
                CountEventsForPawn(KillThoughtDefName, pawn) == thoughtBefore + 1,
                "A later ordinary kill-thought was over-suppressed by the earlier milestone.");
            FlushAllTaleBatches();
            PawnDiaryRimTestScope.Require(
                CountTaleBatchesForPawn("talecombat", pawn) == combatBefore + 1,
                "A later ordinary major-threat kill did not retain one batched combat page.");
        }

        /// <summary>
        /// A memory callback has killer/ThoughtDef identity but no victim. Once the exact
        /// Pawn.Kill scope closes, it must therefore fail open; a recent-owner cache would
        /// incorrectly suppress a later kill's otherwise ordinary thought.
        /// </summary>
        [Test]
        public static void LateKillThoughtAfterClosedScopeFailsOpen()
        {
            if (!RequireRoyaltyOrSkip(nameof(LateKillThoughtAfterClosedScopeFailsOpen))) return;
            Pawn victim = scope.CreateAdultColonist();
            int tick = Find.TickManager?.TicksGame ?? 0;
            PersonaKillCorrelationScope killScope = PersonaKillThoughtCorrelation.Begin(
                victim,
                pawn,
                new List<string> { KillThoughtDefName },
                tick,
                correlationTicks: 60);
            PawnDiaryRimTestScope.Require(killScope != null,
                "The exact persona kill scope could not be opened for the late-thought regression.");
            PersonaKillThoughtCorrelation.Claim(killScope, tick);
            PersonaKillThoughtCorrelation.End(killScope);

            bool suppressed = PersonaKillThoughtCorrelation.TryStageOrSuppress(
                pawn, KillThoughtDefName, signal: null, tick: tick + 1);
            PawnDiaryRimTestScope.Require(!suppressed,
                "A post-scope kill-thought was suppressed without victim identity; it must fail open.");
        }

        /// <summary>FinalizeInit clears the real unsaved persona-kill scope before another game runs.</summary>
        [Test]
        public static void FinalizeInitClearsPersonaKillScopeAcrossGameBoundary()
        {
            if (!RequireRoyaltyOrSkip(nameof(FinalizeInitClearsPersonaKillScopeAcrossGameBoundary))) return;
            Pawn victim = scope.CreateAdultColonist();
            PersonaKillCorrelationScope killScope = PersonaKillThoughtCorrelation.Begin(
                victim,
                pawn,
                new List<string> { KillThoughtDefName },
                Find.TickManager?.TicksGame ?? 0,
                correlationTicks: 60);
            PawnDiaryRimTestScope.Require(killScope != null
                    && PersonaKillThoughtCorrelation.ActiveCountForTests == 1,
                "The game-boundary fixture did not populate the real persona-kill cache.");

            scope.Component.FinalizeInit();

            PawnDiaryRimTestScope.Require(PersonaKillThoughtCorrelation.ActiveCountForTests == 0,
                "FinalizeInit left a persona-kill ownership scope available to the next game.");
        }

        /// <summary>
        /// Disabling the milestone consumes gameplay truth but releases the exact Thought and Tales
        /// back to their ordinary pipelines instead of silently dropping them.
        /// </summary>
        [Test]
        public static void DisabledMilestoneReleasesOrdinaryKillSignals()
        {
            if (!RequireRoyaltyOrSkip(nameof(DisabledMilestoneReleasesOrdinaryKillSignals))) return;
            PawnDiaryMod.Settings.SetGroupEnabled("personaWeaponMilestone", false);
            ThingWithComps weapon;
            CompBladelinkWeapon comp;
            CreatePersonaWeapon(out weapon, out comp);
            ForceKillThoughtTrait(comp);
            comp.CodeFor(pawn);
            pawn.equipment.AddEquipment(weapon);

            Pawn victim = CreateMajorThreatVictim();
            RegisterDeadPawnCleanup(victim);
            DamageInfo damage = new DamageInfo(
                DamageDefOf.Crush, 10000f, instigator: pawn, weapon: weapon.def);
            int milestoneBefore = CountEvents(PersonaMilestoneContextFormatter.FirstKillDefName);
            int thoughtBefore = CountEventsForPawn(KillThoughtDefName, pawn);
            int combatBefore = CountTaleBatchesForPawn("talecombat", pawn);
            scope.FireAndRequireEvent(
                () => victim.Kill(damage),
                KillThoughtDefName,
                pawn,
                null);

            PersonaBondState state = PersonaRows().SingleOrDefault(row => row != null
                && row.weaponThingId == weapon.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(state != null
                    && state.firstConsequentialKillObserved
                    && !state.firstConsequentialKillEventRecorded,
                "Disabled milestone did not preserve observed-versus-recorded save semantics.");
            PawnDiaryRimTestScope.Require(
                CountEvents(PersonaMilestoneContextFormatter.FirstKillDefName) == milestoneBefore,
                "Disabled milestone unexpectedly created a canonical page.");
            PawnDiaryRimTestScope.Require(
                CountEventsForPawn(KillThoughtDefName, pawn) == thoughtBefore + 1,
                "Disabled milestone did not release the ordinary kill-thought page.");
            FlushAllTaleBatches();
            PawnDiaryRimTestScope.Require(
                CountTaleBatchesForPawn("talecombat", pawn) == combatBefore + 1,
                "Disabled milestone did not release the ordinary combat Tales as one batch.");
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
            RoyaltyPolicySnapshot policy = DiaryRoyaltyPolicy.Snapshot();
            int weaponNameCap = Math.Max(1, policy?.maximumTraitLabelCharacters ?? weaponName.Length);
            string expectedWeaponName = weaponName.Length <= weaponNameCap
                ? weaponName
                : weaponName.Substring(0, weaponNameCap).TrimEnd();
            PawnDiaryRimTestScope.Require(death.HasDeathDescription(),
                "The bonded wielder's existing page was not a neutral death description.");
            PawnDiaryRimTestScope.Require(death.gameContext != null
                    && death.gameContext.Contains("persona_milestone=wielder_death")
                    && death.gameContext.Contains("persona_weapon_name=" + expectedWeaponName)
                    && death.gameContext.Contains("bond_end_cause=pawn_death")
                    && !death.gameContext.Contains("persona_weapon="),
                "The death page did not retain the exact pre-UnCode persona relationship context.");
            PawnDiaryRimTestScope.Require(
                CountEvents(PersonaWeaponEventData.BondEndedDefName) == 0,
                "Pawn death created a duplicate standalone persona-bond ending page.");
        }

        /// <summary>
        /// A still-coded weapon need not be primary when its wielder dies. The pending/separated
        /// relationship must still enrich the one existing death page before vanilla UnCode.
        /// </summary>
        [Test]
        public static void NonPrimaryCodedWielderDeathRetainsPersonaContext()
        {
            if (!RequireRoyaltyOrSkip(nameof(NonPrimaryCodedWielderDeathRetainsPersonaContext))) return;
            RegisterDeadPawnCleanup(pawn);
            ThingWithComps weapon;
            CompBladelinkWeapon comp;
            CreatePersonaWeapon(out weapon, out comp);
            comp.CodeFor(pawn);
            PawnDiaryRimTestScope.Require(
                ReferenceEquals(comp.CodedPawn, pawn),
                "Vanilla refused to code the non-primary persona fixture weapon.");
            // This fixture tests Pawn Diary's pre-UnCode death enrichment, not another equipment mod's
            // tracker normalization. Re-establish the exact relation written by vanilla OnCodedFor
            // without equipping the weapon, so it remains deliberately non-primary.
            pawn.equipment.bondedWeapon = weapon;
            scope.RequireNoNewEvent(() =>
            {
                // The exact coded relation above does not require the weapon to be primary. Drive Pawn
                // Diary's equipment observer directly so this death-enrichment fixture does not also
                // depend on other loaded mods' ThingOwner.Remove patches.
                scope.Component.ObserveRoyaltyPersonaEquipment(weapon, pawn);
            });

            List<PersonaWeaponSnapshot> visible = DlcContext.CapturePersonaWeapons(pawn);
            PersonaBondState beforeDeath = PersonaRows().SingleOrDefault(row => row != null
                && row.weaponThingId == weapon.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(visible.Count == 1
                    && !visible[0].isCurrentlyPrimary
                    && string.Equals(visible[0].codedPawnId, pawn.GetUniqueLoadID(), StringComparison.Ordinal)
                    && beforeDeath != null
                    && beforeDeath.phaseToken == PersonaBondPhaseTokens.SeparationPending,
                "Fixture did not establish one non-primary, still-coded pending persona bond: visible="
                    + visible.Count
                    + ", trackerMatches=" + ReferenceEquals(pawn.equipment?.bondedWeapon, weapon)
                    + ", " + Describe(beforeDeath) + ".");

            DiaryEvent death = scope.FireAndRequireEvent(
                () => pawn.Kill(null),
                DeathFallbackSignal.DeathFallbackDefName,
                pawn,
                null);
            PawnDiaryRimTestScope.Require(death.gameContext != null
                    && death.gameContext.Contains("persona_milestone=wielder_death")
                    && death.gameContext.Contains("bond_end_cause=pawn_death"),
                "Non-primary coded wielder death lost its exact persona context.");
            PawnDiaryRimTestScope.Require(
                CountEvents(PersonaWeaponEventData.BondEndedDefName) == 0,
                "Non-primary coded wielder death created a duplicate standalone ending page.");
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

        private static void ForceKillThoughtTrait(CompBladelinkWeapon comp)
        {
            WeaponTraitDef trait = DefDatabase<WeaponTraitDef>.GetNamedSilentFail(KillThoughtTraitDefName);
            PawnDiaryRimTestScope.Require(PersonaWeaponTraitsField != null
                    && comp != null && trait?.killThought != null
                    && string.Equals(trait.killThought.defName, KillThoughtDefName, StringComparison.Ordinal),
                "Could not resolve the real persona kill-thought trait fixture.");
            PersonaWeaponTraitsField.SetValue(comp, new List<WeaponTraitDef> { trait });
        }

        private static void FlushAllTaleBatches()
        {
            PawnDiaryRimTestScope.Require(FlushAllTaleBatchesMethod != null,
                "Could not resolve DiaryGameComponent.FlushAllTaleBatches for delayed-Tale assertions.");
            FlushAllTaleBatchesMethod.Invoke(scope.Component, null);
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

        private static int CountEventsForPawn(string defName, Pawn owner)
        {
            DiaryEventRepository repository = EventsField?.GetValue(scope.Component)
                as DiaryEventRepository;
            PawnDiaryRimTestScope.Require(repository != null && owner != null,
                "Could not read the event repository/pawn for Royalty ownership assertions.");
            string pawnId = owner.GetUniqueLoadID();
            return repository.AllEvents.Count(row => row != null
                && string.Equals(row.interactionDefName, defName, StringComparison.Ordinal)
                && (string.Equals(row.initiatorPawnId, pawnId, StringComparison.Ordinal)
                    || string.Equals(row.recipientPawnId, pawnId, StringComparison.Ordinal)));
        }

        /// <summary>
        /// Counts flushed Tale batches by their saved group/batch context contract. The batch event's
        /// interactionDefName is the source Tale for one item or the XML syntheticDefName for several;
        /// it is never the interaction-group defName itself.
        /// </summary>
        private static int CountTaleBatchesForPawn(string groupDefName, Pawn owner)
        {
            DiaryEventRepository repository = EventsField?.GetValue(scope.Component)
                as DiaryEventRepository;
            PawnDiaryRimTestScope.Require(repository != null && owner != null,
                "Could not read the event repository/pawn for Royalty Tale-batch assertions.");
            string pawnId = owner.GetUniqueLoadID();
            return repository.AllEvents.Count(row => row != null
                && DiaryContextFields.FieldEquals(row.gameContext, "group", groupDefName)
                && DiaryContextFields.FieldEquals(row.gameContext, "batch", "tale")
                && (string.Equals(row.initiatorPawnId, pawnId, StringComparison.Ordinal)
                    || string.Equals(row.recipientPawnId, pawnId, StringComparison.Ordinal)));
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
            string postfixName,
            string finalizerName = null)
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
            if (finalizerName != null)
            {
                PawnDiaryRimTestScope.Require(patches.Finalizers.Any(row =>
                        row.owner == "aimml.pawndiary"
                        && row.PatchMethod?.DeclaringType == typeof(DiaryRoyaltyPatches)
                        && row.PatchMethod.Name == finalizerName),
                    "Expected Pawn Diary's " + finalizerName + " on " + target + ".");
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

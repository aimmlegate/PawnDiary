// Standalone no-game-assembly tests for Royalty policy and N3-R evidence contracts. These tests
// exercise only detached DTOs and pure deterministic policy; no live game object is available here.
using System;
using System.Collections.Generic;
using PawnDiary;

namespace RoyaltyContextTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestFrozenTokensArcAndContinuityMapping();
            TestPolicyFallbacksAndMalformedValues();
            TestFormationAndBaselineMatrix();
            TestSeparationRecoveryMatrix();
            TestEndingTransferAndDisabledMatrix();
            TestLifecycleContextVisibilityAndReservedThoughtOwnership();
            TestPersonaKillCorrelationBufferPolicy();
            TestTraitStructuralRankingOrderingAndCaps();
            TestTraitSanitizationOverridesAndMalformedRows();
            TestMilestoneQualificationAndOwnership();
            TestTitleTransitionMatrixAndDutyCaps();
            TestPhase4FactionObservationEdges();
            TestMutationExactOwnersAndDedup();
            TestMutationExpiryMismatchAndFallback();
            TestMutationOutputSelectionAndMasterGate();
            TestMutationRuntimeCorrelationStore();
            TestPhase4RoutesThoughtsAndContext();
            TestSuccessionCommitMatchingAndContext();
            TestSuccessionCorrelationNormalizationAndAppointment();
            TestPersonaPersistenceBaselinesAndNormalization();
            TestTitleObservationNormalizationAndOrdering();
            Console.WriteLine("RoyaltyContextTests passed " + assertions + " assertions.");
            return 0;
        }

        private static void TestFrozenTokensArcAndContinuityMapping()
        {
            AssertEqual("persona arc grammar", "royalty-persona|Weapon_9|2", RoyaltyArcKeys.Persona(" Weapon_9 ", 2));
            AssertEqual("persona arc rejects delimiter", string.Empty, RoyaltyArcKeys.Persona("bad|weapon", 2));
            AssertEqual("persona arc rejects zero epoch", string.Empty, RoyaltyArcKeys.Persona("Weapon_9", 0));
            AssertTrue("formed phase known", PersonaNarrativePhaseTokens.IsKnown("bond_formed"));
            AssertTrue("invented phase rejected", !PersonaNarrativePhaseTokens.IsKnown("became_best_friends"));

            PersonaWeaponSnapshot weapon = Weapon("Weapon_9", "Pawn_A", true);
            NarrativeEvidence evidence = RoyaltyNarrativeEvidenceFactory.Persona(
                "event-1", 50, "Pawn_A", "initiator", weapon, 2,
                PersonaNarrativePhaseTokens.BondFormed, string.Empty, "PersonaWeaponBondFormed", true);
            AssertNotNull("valid persona evidence", evidence);
            AssertEqual("shared facet", NarrativeFacetTokens.BondLifecycle, evidence.facet);
            AssertEqual("shared subject", NarrativeSubjectKindTokens.Weapon, evidence.subjectKind);
            AssertEqual("shared arc", "royalty-persona|Weapon_9|2", evidence.arcKey);
            AssertEqual("source domain", "royalty_persona", evidence.sourceDomain);
            AssertEqual("ordinary lifecycle salience", NarrativeSalienceTokens.Meaningful, evidence.salience);
            AssertEqual("lifecycle topic count", 3, evidence.beliefTopics.Count);

            evidence = RoyaltyNarrativeEvidenceFactory.Persona(
                "event-2", 51, "Pawn_A", "initiator", weapon, 2,
                PersonaNarrativePhaseTokens.FirstConsequentialKill, "event-1", "PersonaWeaponFirstConsequentialKill", true);
            AssertEqual("kill adds violence/death topics", 5, evidence.beliefTopics.Count);
            AssertEqual("kill keeps same exact arc", "royalty-persona|Weapon_9|2", evidence.arcKey);
            AssertTrue("bad evidence tick rejected", RoyaltyNarrativeEvidenceFactory.Persona(
                "event", -1, "Pawn_A", "", weapon, 2, PersonaNarrativePhaseTokens.BondFormed, "", "", true) == null);
            AssertTrue("bad evidence phase rejected", RoyaltyNarrativeEvidenceFactory.Persona(
                "event", 1, "Pawn_A", "", weapon, 2, "unknown", "", "", true) == null);

            NarrativeEvidence title = RoyaltyNarrativeEvidenceFactory.TitleTransition(
                "event-3", 52, "Pawn_A", "initiator", "Pawn_A", "Ari",
                RoyalTitleTransitionTokens.Promotion, "event-2", "Baron", false, true);
            AssertNotNull("valid title transition evidence", title);
            AssertEqual("title transition uses shared identity facet",
                NarrativeFacetTokens.IdentityTransition, title.facet);
            AssertEqual("title transition identifies exact pawn",
                NarrativeSubjectKindTokens.Pawn, title.subjectKind);
            AssertEqual("title/faction prose never becomes a Royalty-only arc", string.Empty, title.arcKey);
            AssertEqual("title evidence domain is stable", "royalty_title", title.sourceDomain);
            AssertEqual("title evidence carries bounded authority/status/duty topics", 3,
                title.beliefTopics.Count);
            title = RoyaltyNarrativeEvidenceFactory.TitleTransition(
                "event-4", 53, "Pawn_A", "initiator", "Pawn_A", "Ari",
                RoyalTitleTransitionTokens.Loss, "event-3", "Baron", true, true);
            AssertEqual("succession-related title transition includes death topic", 4,
                title.beliefTopics.Count);
            AssertEqual("title loss is major continuity evidence",
                NarrativeSalienceTokens.Major, title.salience);
            AssertTrue("no-change title is not narrative evidence",
                RoyaltyNarrativeEvidenceFactory.TitleTransition(
                    "event", 1, "Pawn_A", "", "Pawn_A", "", RoyalTitleTransitionTokens.NoChange,
                    "", "", false, true) == null);
        }

        private static void TestPolicyFallbacksAndMalformedValues()
        {
            RoyaltyPolicySnapshot policy = RoyaltyPolicySnapshot.CreateDefault();
            AssertEqual("fallback separation", 60000, policy.separationThresholdTicks);
            AssertEqual("fallback reconciliation", 2500, policy.reconciliationCadenceTicks);
            AssertEqual("fallback trait cap", 2, policy.maximumSelectedTraits);
            AssertEqual("fallback Tale rows", 1, policy.qualifyingTales.Count);
            AssertEqual("fallback first Tale", "KilledMajorThreat", policy.qualifyingTales[0].taleDefName);
            AssertEqual("fallback killer role", RoyaltyTaleRoleTokens.Initiator,
                policy.qualifyingTales[0].killerRoleToken);
            AssertEqual("fallback victim role", RoyaltyTaleRoleTokens.Recipient,
                policy.qualifyingTales[0].victimRoleToken);
            AssertEqual("fallback kill-thought correlation", 60, policy.killThoughtCorrelationTicks);
            AssertEqual("fallback companion Tale rows", 8, policy.personaKillCompanionTales.Count);
            AssertEqual("fallback companion Tale", "KilledMelee",
                policy.personaKillCompanionTales[5].taleDefName);
            AssertEqual("fallback companion killer role", RoyaltyTaleRoleTokens.Initiator,
                policy.personaKillCompanionTales[5].killerRoleToken);
            AssertEqual("fallback companion victim role", RoyaltyTaleRoleTokens.Recipient,
                policy.personaKillCompanionTales[5].victimRoleToken);

            policy.separationThresholdTicks = -10;
            PersonaBondStateSnapshot state = ActiveState();
            PersonaLifecycleDecision decision = PersonaLifecyclePolicy.Evaluate(
                state, Observation(PersonaObservationTokens.NotPrimary, 10, true), policy);
            decision = PersonaLifecyclePolicy.Evaluate(
                decision.nextState, Observation(PersonaObservationTokens.NotPrimary, 11, true), policy);
            AssertEqual("malformed threshold falls back to defensive one-tick minimum",
                PersonaBondPhaseTokens.Separated, decision.nextState.phaseToken);

            AssertEqual("null lifecycle leaves safe state", PersonaBondPhaseTokens.Untracked,
                PersonaLifecyclePolicy.Evaluate(null, null, null).nextState.phaseToken);
            AssertEqual("null trait list empty", 0,
                PersonaTraitPolicy.Select(null, PersonaTraitEventTokens.Kill, "event", null).Count);
            AssertEqual("null mutation batch empty", 0,
                RoyalMutationOwnershipPolicy.Plan(null, null, 0, false, true, false, null).mutations.Count);
        }

        private static void TestLifecycleContextVisibilityAndReservedThoughtOwnership()
        {
            RoyaltyPolicySnapshot policy = Policy(1000);
            PersonaBondStateSnapshot previous = ActiveState();
            previous.currentPawnName = "Ari";
            PersonaLifecycleDecision decision = new PersonaLifecycleDecision
            {
                narrativePhase = PersonaNarrativePhaseTokens.BondEnded,
                shouldEmit = true,
                nextState = ActiveState()
            };
            decision.nextState.phaseToken = PersonaBondPhaseTokens.Ended;
            decision.nextState.endCauseToken = PersonaEndCauseTokens.WeaponDestroyed;
            List<PersonaTraitFact> selected = new List<PersonaTraitFact>
            {
                Trait("Jealous", bondedThought: true),
                Trait("KillHappy", kill: true)
            };
            string context = PersonaWeaponContextFormatter.Format(
                Weapon("Weapon_1", "Pawn_A", true), previous, decision, selected, "three days", policy);
            AssertTrue("persona marker formatted", context.Contains("persona_weapon=bond_ended"));
            AssertTrue("persona exact identity formatted", context.Contains("persona_weapon_id=Weapon_1"));
            AssertTrue("persona end cause formatted", context.Contains("bond_end_cause=weapon_destroyed"));
            AssertTrue("persona bounded trait formatted", context.Contains("persona_trait_1=Jealous localized"));
            AssertTrue("persona formatter rejects unknown phase", PersonaWeaponContextFormatter.Format(
                Weapon("Weapon_1", "Pawn_A", true), previous,
                new PersonaLifecycleDecision { narrativePhase = "invented" }, selected, "", policy).Length == 0);

            // Phase 2 does not correlate vanilla bonded situational thoughts through the memory
            // callback. Keep this exact pure owner contract for Phase 3 killThought memories.
            List<string> thoughts = new List<string> { "PersonaWeaponKillMemory" };
            AssertTrue("exact persona thought owner matches", PersonaThoughtOwnershipPolicy.Matches(
                "Pawn_A", thoughts, "Pawn_A", "personaweaponkillmemory"));
            AssertTrue("wrong pawn cannot be claimed", !PersonaThoughtOwnershipPolicy.Matches(
                "Pawn_A", thoughts, "Pawn_B", "PersonaWeaponKillMemory"));
            AssertTrue("wrong thought cannot be claimed", !PersonaThoughtOwnershipPolicy.Matches(
                "Pawn_A", thoughts, "Pawn_A", "OtherThought"));

            string milestoneContext = PersonaMilestoneContextFormatter.FormatFirstKill(
                "tale=PersonaWeaponFirstConsequentialKill; label=First kill; taleClass=Tale_DoublePawn",
                Weapon("Weapon_1", "Pawn_A", true),
                previous,
                selected,
                "KilledMajorThreat",
                "killed a major threat",
                RoyaltyTaleRoleTokens.Initiator,
                RoyaltyTaleRoleTokens.Recipient,
                policy);
            AssertTrue("milestone remains Tale-domain", milestoneContext.StartsWith(
                "tale=PersonaWeaponFirstConsequentialKill", StringComparison.Ordinal));
            AssertTrue("milestone omits standalone persona marker",
                !milestoneContext.Contains("persona_weapon="));
            AssertTrue("milestone source Tale preserved",
                milestoneContext.Contains("tale_source_def=KilledMajorThreat"));
            AssertTrue("milestone exact weapon projected",
                milestoneContext.Contains("persona_weapon_name=North Wind"));

            string deathContext = PersonaMilestoneContextFormatter.FormatWielderDeath(
                Weapon("Weapon_1", "Pawn_A", true), previous, selected, policy);
            AssertTrue("wielder death milestone projected",
                deathContext.Contains("persona_milestone=wielder_death"));
            AssertTrue("wielder death exact ending projected",
                deathContext.Contains("bond_end_cause=pawn_death"));
            AssertTrue("wielder death omits standalone persona marker",
                !deathContext.Contains("persona_weapon="));
        }

        private static void TestPersonaKillCorrelationBufferPolicy()
        {
            AssertEqual("new exact signal stages", PersonaKillSignalAction.Stage,
                PersonaKillCorrelationPolicy.Decide(false, false, 0, 1));
            AssertEqual("duplicate exact signal suppresses", PersonaKillSignalAction.Suppress,
                PersonaKillCorrelationPolicy.Decide(false, true, 0, 1));
            AssertEqual("claimed exact signal suppresses", PersonaKillSignalAction.Suppress,
                PersonaKillCorrelationPolicy.Decide(true, false, 0, 1));
            AssertEqual("full bounded buffer fails open", PersonaKillSignalAction.PassThrough,
                PersonaKillCorrelationPolicy.Decide(false, false, 1, 1));
            AssertTrue("kill scope remains live at its opening tick",
                !PersonaKillCorrelationPolicy.IsExpired(100, 100, 60));
            AssertTrue("kill scope remains live at its inclusive expiry boundary",
                !PersonaKillCorrelationPolicy.IsExpired(100, 160, 60));
            AssertTrue("kill scope expires after its elapsed window",
                PersonaKillCorrelationPolicy.IsExpired(100, 161, 60));
            AssertTrue("backwards tick invalidates kill scope",
                PersonaKillCorrelationPolicy.IsExpired(100, 99, 60));
            AssertTrue("non-positive correlation uses one-tick minimum",
                PersonaKillCorrelationPolicy.IsExpired(100, 102, 0));
        }

        private static void TestFormationAndBaselineMatrix()
        {
            RoyaltyPolicySnapshot policy = Policy(1000);
            PersonaLifecycleDecision formed = PersonaLifecyclePolicy.Evaluate(
                null, Observation(PersonaObservationTokens.Coding, 100, true), policy);
            AssertEqual("new coding active", PersonaBondPhaseTokens.Active, formed.nextState.phaseToken);
            AssertEqual("new coding epoch", 1, formed.nextState.bondEpoch);
            AssertEqual("new coding phase", PersonaNarrativePhaseTokens.BondFormed, formed.narrativePhase);
            AssertTrue("new coding emits", formed.shouldEmit);

            PersonaLifecycleDecision repeated = PersonaLifecyclePolicy.Evaluate(
                formed.nextState, Observation(PersonaObservationTokens.Coding, 101, true), policy);
            AssertTrue("repeated coding no state change", !repeated.stateChanged);
            AssertTrue("repeated coding no formation", !repeated.shouldEmit);

            PersonaLifecycleObservation loading = Observation(PersonaObservationTokens.Coding, 200, true);
            loading.normalPlay = false;
            PersonaLifecycleDecision baselineViaLoad = PersonaLifecyclePolicy.Evaluate(null, loading, policy);
            AssertEqual("load coding baselines", PersonaBondPhaseTokens.Active, baselineViaLoad.nextState.phaseToken);
            AssertTrue("load consumes old first kill", baselineViaLoad.nextState.firstConsequentialKillObserved);
            AssertTrue("load emits no formation", !baselineViaLoad.shouldEmit);

            PersonaLifecycleDecision baseline = PersonaLifecyclePolicy.Evaluate(
                null, Observation(PersonaObservationTokens.Baseline, 300, true), policy);
            AssertTrue("explicit baseline consumes old first kill", baseline.nextState.firstConsequentialKillObserved);
            AssertTrue("explicit baseline silent", !baseline.shouldEmit);

            PersonaLifecycleObservation malformed = Observation(PersonaObservationTokens.Coding, 10, true);
            malformed.weapon.weaponThingId = "bad|id";
            AssertTrue("unsafe weapon identity rejected",
                !PersonaLifecyclePolicy.Evaluate(null, malformed, policy).stateChanged);
            malformed.weapon = null;
            AssertTrue("null weapon rejected", !PersonaLifecyclePolicy.Evaluate(null, malformed, policy).stateChanged);
        }

        private static void TestPersonaPersistenceBaselinesAndNormalization()
        {
            PersonaWeaponSnapshot weapon = Weapon("Weapon_Save", "Pawn_Save", true);
            weapon.displayName = "  Voice; of Dawn  ";
            weapon.traits = new List<PersonaTraitFact>
            {
                Trait("Trait_B", bondedThought: true, worker: "Worker.B"),
                Trait("Trait_B", kill: true, worker: "Worker.Duplicate"),
                Trait("Trait_A", bondedHediff: true, worker: "Worker.A")
            };
            PersonaBondStateSnapshot baseline = RoyaltyStatePersistence.BaselinePersona(weapon, -5, 2);
            AssertNotNull("valid persona baseline", baseline);
            AssertEqual("baseline phase active", PersonaBondPhaseTokens.Active, baseline.phaseToken);
            AssertEqual("baseline epoch one", 1, baseline.bondEpoch);
            AssertTrue("baseline consumes historical first kill", baseline.firstConsequentialKillObserved);
            AssertEqual("baseline clamps tick", 0, baseline.bondStartedTick);
            AssertEqual("baseline sanitizes semicolon", "Voice, of Dawn", baseline.lastDisplayName);
            AssertEqual("baseline trait cap and duplicate", 2, baseline.traits.Count);
            AssertTrue("baseline invalid weapon rejected",
                RoyaltyStatePersistence.BaselinePersona(Weapon("bad|id", "Pawn_Save", true), 1, 2) == null);

            List<PersonaWeaponSnapshot> visible = new List<PersonaWeaponSnapshot> { weapon };
            AssertTrue("exact live visible bond is current context",
                RoyaltyStatePersistence.IsCurrentVisiblePersonaBond(baseline, "Pawn_Save", visible));
            AssertTrue("missing live weapon excludes stale saved bond", !RoyaltyStatePersistence
                .IsCurrentVisiblePersonaBond(baseline, "Pawn_Save", new List<PersonaWeaponSnapshot>()));
            PersonaWeaponSnapshot wrongOwner = Weapon("Weapon_Save", "Pawn_Other", true);
            AssertTrue("wrong live coded pawn excludes saved bond", !RoyaltyStatePersistence
                .IsCurrentVisiblePersonaBond(baseline, "Pawn_Save",
                    new List<PersonaWeaponSnapshot> { wrongOwner }));
            PersonaWeaponSnapshot destroyed = Weapon("Weapon_Save", "Pawn_Save", true);
            destroyed.isDestroyed = true;
            AssertTrue("destroyed live weapon excludes saved bond", !RoyaltyStatePersistence
                .IsCurrentVisiblePersonaBond(baseline, "Pawn_Save",
                    new List<PersonaWeaponSnapshot> { destroyed }));
            PersonaBondStateSnapshot ended = RoyaltyStatePersistence.NormalizePersona(baseline, 2);
            ended.phaseToken = PersonaBondPhaseTokens.Ended;
            AssertTrue("ended saved bond is never current context", !RoyaltyStatePersistence
                .IsCurrentVisiblePersonaBond(ended, "Pawn_Save", visible));

            PersonaBondStateSnapshot malformed = ActiveState();
            malformed.weaponThingId = "Weapon_Duplicate";
            malformed.phaseToken = "invented";
            malformed.bondEpoch = -9;
            malformed.pendingSeparationTick = 50;
            malformed.endedTick = 40;
            malformed.endCauseToken = "invented";
            PersonaBondStateSnapshot repaired = RoyaltyStatePersistence.NormalizePersona(malformed, 2);
            AssertEqual("unknown phase repairs", PersonaBondPhaseTokens.Untracked, repaired.phaseToken);
            AssertEqual("untracked epoch clamps zero", 0, repaired.bondEpoch);
            AssertEqual("nonpending clears pending tick", -1, repaired.pendingSeparationTick);
            AssertEqual("nonended clears ending tick", -1, repaired.endedTick);
            AssertEqual("nonended clears cause", PersonaEndCauseTokens.None, repaired.endCauseToken);
            malformed.firstConsequentialKillObserved = false;
            malformed.firstConsequentialKillEventRecorded = true;
            repaired = RoyaltyStatePersistence.NormalizePersona(malformed, 2);
            AssertTrue("recorded milestone repairs observed invariant",
                repaired.firstConsequentialKillObserved);

            PersonaBondStateSnapshot older = ActiveState();
            older.weaponThingId = "Weapon_Duplicate";
            older.lastDisplayName = "Older";
            PersonaBondStateSnapshot newer = ActiveState();
            newer.weaponThingId = "Weapon_Duplicate";
            newer.lastDisplayName = "Newer";
            List<PersonaBondStateSnapshot> rows = RoyaltyStatePersistence.NormalizePersonas(
                new List<PersonaBondStateSnapshot> { older, null, newer, ActiveState() }, 2, 2);
            AssertEqual("persona row cap", 2, rows.Count);
            AssertEqual("duplicate keeps newest", "Newer", rows[0].lastDisplayName);
        }

        private static void TestTitleObservationNormalizationAndOrdering()
        {
            List<RoyalTitleSnapshot> titles = new List<RoyalTitleSnapshot>
            {
                Title("Pawn_A", "Faction_Z", "Baron", 5, "speech"),
                Title("Pawn_A", "Faction_A", "Freeholder", 1),
                Title("Pawn_A", "Faction_A", "Count", 7, "bedroom")
            };
            List<RoyalTitleObservationSnapshot> baseline = RoyaltyStatePersistence.BaselineTitles(titles, -10);
            AssertEqual("title baseline faction dedup", 2, baseline.Count);
            AssertEqual("title baseline deterministic first faction", "Faction_A", baseline[0].factionId);
            AssertEqual("title baseline keeps highest seniority", "Count", baseline[0].titleDefName);
            AssertEqual("title baseline clamps tick", 0, baseline[0].lastObservedTick);

            List<RoyalTitleObservationSnapshot> normalized = RoyaltyStatePersistence.NormalizeTitleObservations(
                new List<RoyalTitleObservationSnapshot>
                {
                    null,
                    new RoyalTitleObservationSnapshot { factionId = "bad|id", titleDefName = "Count" },
                    new RoyalTitleObservationSnapshot
                    {
                        factionId = "Faction_B", factionName = " Empire; West ", titleDefName = "Knight",
                        titleLabel = " Knight ", seniority = -2, lastObservedTick = -8
                    },
                    new RoyalTitleObservationSnapshot
                    {
                        factionId = "Faction_B", titleDefName = "Baron", seniority = 4, lastObservedTick = 10
                    },
                    new RoyalTitleObservationSnapshot
                    {
                        factionId = "Faction_C", titleDefName = "Yeoman", seniority = 1, lastObservedTick = 5
                    }
                }, 1);
            AssertEqual("title observation cap", 1, normalized.Count);
            AssertEqual("title cap follows stable faction ordering", "Faction_B", normalized[0].factionId);
            AssertEqual("duplicate faction keeps higher title", "Baron", normalized[0].titleDefName);
            AssertEqual("title seniority normalized", 4, normalized[0].seniority);
        }

        private static void TestSeparationRecoveryMatrix()
        {
            RoyaltyPolicySnapshot policy = Policy(1000);
            PersonaLifecycleDecision steady = PersonaLifecyclePolicy.Evaluate(
                ActiveState(), Observation(PersonaObservationTokens.Primary, 50, true), policy);
            AssertTrue("new primary timestamp is persisted", steady.stateChanged);
            AssertEqual("new primary timestamp value", 50, steady.nextState.lastPrimaryObservedTick);

            PersonaBondStateSnapshot state = steady.nextState;
            PersonaLifecycleDecision pending = PersonaLifecyclePolicy.Evaluate(
                state, Observation(PersonaObservationTokens.NotPrimary, 100, true), policy);
            AssertEqual("loss starts pending", PersonaBondPhaseTokens.SeparationPending, pending.nextState.phaseToken);
            AssertEqual("pending start tick", 100, pending.nextState.pendingSeparationTick);
            AssertTrue("initial loss silent", !pending.shouldEmit);

            PersonaLifecycleDecision early = PersonaLifecyclePolicy.Evaluate(
                pending.nextState, Observation(PersonaObservationTokens.NotPrimary, 1099, true), policy);
            AssertEqual("one tick before threshold pending", PersonaBondPhaseTokens.SeparationPending, early.nextState.phaseToken);
            AssertTrue("one tick before threshold silent", !early.shouldEmit);

            PersonaLifecycleDecision threshold = PersonaLifecyclePolicy.Evaluate(
                early.nextState, Observation(PersonaObservationTokens.NotPrimary, 1100, true), policy);
            AssertEqual("threshold separates", PersonaBondPhaseTokens.Separated, threshold.nextState.phaseToken);
            AssertEqual("separation phase", PersonaNarrativePhaseTokens.BondSeparated, threshold.narrativePhase);
            AssertTrue("separation recorded", threshold.nextState.separationEmitted);
            AssertTrue("threshold emits once", threshold.shouldEmit);

            PersonaLifecycleDecision repeated = PersonaLifecyclePolicy.Evaluate(
                threshold.nextState, Observation(PersonaObservationTokens.NotPrimary, 2100, true), policy);
            AssertTrue("repeated separated observation silent", !repeated.shouldEmit);
            AssertTrue("repeated separated unchanged", !repeated.stateChanged);

            PersonaLifecycleDecision recovered = PersonaLifecyclePolicy.Evaluate(
                threshold.nextState, Observation(PersonaObservationTokens.Primary, 2200, true), policy);
            AssertEqual("recovery active", PersonaBondPhaseTokens.Active, recovered.nextState.phaseToken);
            AssertEqual("recovery phase", PersonaNarrativePhaseTokens.BondRecovered, recovered.narrativePhase);
            AssertTrue("recorded separation recovers", recovered.shouldEmit);

            pending = PersonaLifecyclePolicy.Evaluate(
                ActiveState(), Observation(PersonaObservationTokens.NotPrimary, 100, true), policy);
            PersonaLifecycleDecision shortSwap = PersonaLifecyclePolicy.Evaluate(
                pending.nextState, Observation(PersonaObservationTokens.Primary, 500, true), policy);
            AssertEqual("short swap returns active", PersonaBondPhaseTokens.Active, shortSwap.nextState.phaseToken);
            AssertTrue("short swap has no recovery", !shortSwap.shouldEmit);

            PersonaLifecycleDecision unavailable = PersonaLifecyclePolicy.Evaluate(
                pending.nextState, Observation(PersonaObservationTokens.Unavailable, 5000, true), policy);
            AssertEqual("unobservable pending cancels", PersonaBondPhaseTokens.Active, unavailable.nextState.phaseToken);
            AssertTrue("off-map elapsed time silent", !unavailable.shouldEmit);

            PersonaBondStateSnapshot unrecorded = ActiveState();
            unrecorded.phaseToken = PersonaBondPhaseTokens.Separated;
            unrecorded.separationEmitted = false;
            recovered = PersonaLifecyclePolicy.Evaluate(
                unrecorded, Observation(PersonaObservationTokens.Primary, 5000, true), policy);
            AssertTrue("recovery without emitted separation silent", !recovered.shouldEmit);
        }

        private static void TestEndingTransferAndDisabledMatrix()
        {
            RoyaltyPolicySnapshot policy = Policy(1000);
            AssertEqual("death wins end-cause precedence", PersonaEndCauseTokens.PawnDeath,
                PersonaLifecyclePolicy.ClassifyEndCause(true, true, true, true, true));
            AssertEqual("destruction wins below death", PersonaEndCauseTokens.WeaponDestroyed,
                PersonaLifecyclePolicy.ClassifyEndCause(false, true, true, true, true));
            AssertEqual("transfer wins below destruction", PersonaEndCauseTokens.Transfer,
                PersonaLifecyclePolicy.ClassifyEndCause(false, false, true, true, true));
            AssertEqual("unknown uncode wins below transfer", PersonaEndCauseTokens.UnknownUncode,
                PersonaLifecyclePolicy.ClassifyEndCause(false, false, false, true, true));
            AssertEqual("map removal is lowest cleanup cause", PersonaEndCauseTokens.MapRemoval,
                PersonaLifecyclePolicy.ClassifyEndCause(false, false, false, false, true));
            AssertEqual("no ending evidence remains none", PersonaEndCauseTokens.None,
                PersonaLifecyclePolicy.ClassifyEndCause(false, false, false, false, false));

            PersonaLifecycleDecision destroyed = PersonaLifecyclePolicy.Evaluate(
                ActiveState(), Observation(PersonaObservationTokens.Destroyed, 100, true), policy);
            AssertEqual("destruction ends bond", PersonaBondPhaseTokens.Ended, destroyed.nextState.phaseToken);
            AssertEqual("destruction cause", PersonaEndCauseTokens.WeaponDestroyed, destroyed.nextState.endCauseToken);
            AssertTrue("destruction owns persona ending", destroyed.shouldEmit && !destroyed.deathOwnsEnding);

            PersonaLifecycleDecision death = PersonaLifecyclePolicy.Evaluate(
                ActiveState(), Observation(PersonaObservationTokens.PawnDeath, 100, true), policy);
            AssertEqual("death ends bond", PersonaBondPhaseTokens.Ended, death.nextState.phaseToken);
            AssertTrue("death page owns ending", death.deathOwnsEnding && !death.shouldEmit);

            PersonaLifecycleDecision mapRemoved = PersonaLifecyclePolicy.Evaluate(
                ActiveState(), Observation(PersonaObservationTokens.MapRemoved, 100, true), policy);
            AssertTrue("map removal silent", !mapRemoved.shouldEmit);
            AssertEqual("map removal preserves active bond", PersonaBondPhaseTokens.Active, mapRemoved.nextState.phaseToken);

            PersonaLifecycleDecision unknown = PersonaLifecyclePolicy.Evaluate(
                ActiveState(), Observation(PersonaObservationTokens.UnknownUncode, 100, true), policy);
            AssertTrue("unknown uncode fails closed", !unknown.stateChanged && !unknown.shouldEmit);

            PersonaLifecycleObservation transferObservation = Observation(PersonaObservationTokens.Transfer, 200, true);
            transferObservation.weapon.codedPawnId = "Pawn_B";
            transferObservation.weapon.codedPawnName = "B";
            PersonaLifecycleDecision transfer = PersonaLifecyclePolicy.Evaluate(
                ActiveState(), transferObservation, policy);
            AssertEqual("transfer increments epoch", 2, transfer.nextState.bondEpoch);
            AssertEqual("transfer records prior pawn", "Pawn_A", transfer.nextState.previousPawnId);
            AssertTrue("transfer is one new formation", transfer.shouldEmit && transfer.includesExactPreviousBond);

            PersonaLifecycleObservation invalidTransfer = Observation(PersonaObservationTokens.Transfer, 200, true);
            AssertTrue("same-pawn transfer rejected",
                !PersonaLifecyclePolicy.Evaluate(ActiveState(), invalidTransfer, policy).stateChanged);

            PersonaLifecycleObservation disabledCoding = Observation(PersonaObservationTokens.Coding, 1, false);
            PersonaLifecycleDecision disabled = PersonaLifecyclePolicy.Evaluate(null, disabledCoding, policy);
            AssertEqual("disabled group still records bond", PersonaBondPhaseTokens.Active, disabled.nextState.phaseToken);
            AssertTrue("disabled group emits no formation", !disabled.shouldEmit);

            PersonaLifecycleDecision pending = PersonaLifecyclePolicy.Evaluate(
                disabled.nextState, Observation(PersonaObservationTokens.NotPrimary, 10, false), policy);
            PersonaLifecycleDecision separated = PersonaLifecyclePolicy.Evaluate(
                pending.nextState, Observation(PersonaObservationTokens.NotPrimary, 1010, false), policy);
            AssertEqual("disabled group still reaches separated truth", PersonaBondPhaseTokens.Separated, separated.nextState.phaseToken);
            AssertTrue("disabled separation not recorded as page", !separated.nextState.separationEmitted);
            PersonaLifecycleDecision recovery = PersonaLifecyclePolicy.Evaluate(
                separated.nextState, Observation(PersonaObservationTokens.Primary, 1100, true), policy);
            AssertTrue("reenable does not release recovery backlog", !recovery.shouldEmit);
        }

        private static void TestTraitStructuralRankingOrderingAndCaps()
        {
            RoyaltyPolicySnapshot policy = Policy(1000);
            policy.traitWorkerRules.Add(new RoyaltyTraitWorkerRule
            {
                workerTypeToken = "Worker_Relevant",
                eventToken = PersonaTraitEventTokens.Kill,
                weight = 90
            });
            List<PersonaTraitFact> traits = new List<PersonaTraitFact>
            {
                Trait("Equipped", equipped: true),
                Trait("Worker", worker: "Worker_Relevant"),
                Trait("KillThought", kill: true),
                Trait("Bonded", bondedThought: true),
                Trait("Irrelevant")
            };
            List<PersonaTraitFact> selected = PersonaTraitPolicy.Select(
                traits, PersonaTraitEventTokens.Kill, "event-a", policy);
            AssertEqual("trait output cap", 2, selected.Count);
            AssertEqual("kill thought wins", "KillThought", selected[0].traitDefName);
            AssertEqual("worker mapping second", "Worker", selected[1].traitDefName);

            List<PersonaTraitFact> reversed = new List<PersonaTraitFact>(traits);
            reversed.Reverse();
            List<PersonaTraitFact> again = PersonaTraitPolicy.Select(
                reversed, PersonaTraitEventTokens.Kill, "event-a", policy);
            AssertEqual("input order does not change first", selected[0].traitDefName, again[0].traitDefName);
            AssertEqual("input order does not change second", selected[1].traitDefName, again[1].traitDefName);

            policy.maximumSelectedTraits = 1;
            AssertEqual("authored one-trait cap", 1,
                PersonaTraitPolicy.Select(traits, PersonaTraitEventTokens.Kill, "event-a", policy).Count);
            policy.maximumSelectedTraits = 99;
            AssertEqual("malformed cap falls back to hard two", 2,
                PersonaTraitPolicy.Select(traits, PersonaTraitEventTokens.Kill, "event-a", policy).Count);

            selected = PersonaTraitPolicy.Select(
                traits, PersonaTraitEventTokens.Formation, "event-b", policy);
            AssertEqual("bond thought selected for formation", "Bonded", selected[0].traitDefName);
            AssertTrue("unrelated kill-only trait omitted from formation",
                Find(selected, "KillThought") == null);

            traits.Add(Trait("KillThought", kill: true));
            selected = PersonaTraitPolicy.Select(traits, PersonaTraitEventTokens.Kill, "event-a", policy);
            AssertEqual("duplicate trait Def ignored", 1, Count(selected, "KillThought"));
        }

        private static void TestTraitSanitizationOverridesAndMalformedRows()
        {
            RoyaltyPolicySnapshot policy = Policy(1000);
            policy.maximumTraitLabelCharacters = 5;
            policy.maximumTraitDescriptionCharacters = 8;
            policy.traitOverrides.Add(new RoyaltyTraitOverrideRule
            {
                traitDefName = "OpaqueModTrait",
                eventToken = PersonaTraitEventTokens.Kill,
                weight = 999
            });
            policy.traitOverrides.Add(new RoyaltyTraitOverrideRule
            {
                traitDefName = "ExcludedTrait",
                eventToken = PersonaTraitEventTokens.Kill,
                weight = 999,
                excluded = true
            });
            List<PersonaTraitFact> traits = new List<PersonaTraitFact>
            {
                new PersonaTraitFact
                {
                    traitDefName = "OpaqueModTrait",
                    label = "Localized adjective that policy must ignore",
                    description = "line one;\nline two"
                },
                Trait("Structural", kill: true),
                Trait("ExcludedTrait", kill: true),
                Trait("bad|id", kill: true),
                null
            };
            List<PersonaTraitFact> selected = PersonaTraitPolicy.Select(
                traits, PersonaTraitEventTokens.Kill, "event", policy);
            AssertEqual("structural outranks capped override", "Structural", selected[0].traitDefName);
            PersonaTraitFact opaque = Find(selected, "OpaqueModTrait");
            AssertNotNull("exact opaque override selected", opaque);
            AssertEqual("label capped", 5, opaque.label.Length);
            AssertTrue("description flattened", opaque.description.IndexOf(';') < 0 && opaque.description.IndexOf('\n') < 0);
            AssertTrue("description capped", opaque.description.Length <= 8);
            AssertTrue("exact exclusion wins over structure", Find(selected, "ExcludedTrait") == null);
            AssertTrue("unsafe identity omitted", Find(selected, "bad|id") == null);

            AssertEqual("unknown event returns empty", 0,
                PersonaTraitPolicy.Select(traits, "localized kill", "event", policy).Count);
            policy.enabled = false;
            AssertEqual("disabled policy returns empty", 0,
                PersonaTraitPolicy.Select(traits, PersonaTraitEventTokens.Kill, "event", policy).Count);
        }

        private static void TestMilestoneQualificationAndOwnership()
        {
            RoyaltyPolicySnapshot policy = Policy(1000);
            PersonaMilestoneObservation input = Milestone();
            PersonaMilestoneDecision result = PersonaMilestonePolicy.Evaluate(input, policy);
            AssertTrue("qualifying milestone", result.qualifies);
            AssertTrue("qualifying milestone consumes gameplay truth", result.markObserved);
            AssertTrue("accepted milestone records and enriches", result.markEventRecorded && result.enrichTale);
            AssertTrue("accepted milestone forces one killer POV", result.forceSoloKillerPov);
            AssertTrue("victim death route preserved", result.preserveVictimDeathRoute);
            AssertEqual("kill trait attached", "KillThought", result.selectedTraits[0].traitDefName);

            input.personaGroupEnabled = false;
            result = PersonaMilestonePolicy.Evaluate(input, policy);
            AssertTrue("disabled group still consumes first", result.qualifies && result.markObserved);
            AssertTrue("disabled group does not record page", !result.markEventRecorded && !result.enrichTale);

            input = Milestone();
            input.pageAccepted = false;
            result = PersonaMilestonePolicy.Evaluate(input, policy);
            AssertTrue("rejected page still consumes first", result.qualifies && result.markObserved);
            AssertTrue("rejected page not recorded", !result.markEventRecorded);

            input = Milestone();
            input.bond.firstConsequentialKillObserved = true;
            AssertTrue("second qualifying tale ignored", !PersonaMilestonePolicy.Evaluate(input, policy).qualifies);
            input = Milestone(); input.currentWeapon.isCurrentlyPrimary = false;
            AssertTrue("bonded weapon not primary does not consume", !PersonaMilestonePolicy.Evaluate(input, policy).markObserved);
            input = Milestone(); input.currentWeapon.weaponThingId = "Other";
            AssertTrue("different current weapon does not consume", !PersonaMilestonePolicy.Evaluate(input, policy).markObserved);
            input = Milestone(); input.taleDefName = "BuiltTable";
            AssertTrue("nonqualifying tale unchanged", !PersonaMilestonePolicy.Evaluate(input, policy).qualifies);
            input = Milestone(); input.significance = 0;
            AssertTrue("minimum significance boundary", !PersonaMilestonePolicy.Evaluate(input, policy).qualifies);
            input = Milestone(); input.victimPresent = false;
            AssertTrue("missing victim fails closed", !PersonaMilestonePolicy.Evaluate(input, policy).qualifies);
            input = Milestone(); input.victimDied = false;
            AssertTrue("surviving victim fails closed", !PersonaMilestonePolicy.Evaluate(input, policy).qualifies);
            input = Milestone(); input.resolvedKillerRoleToken = string.Empty;
            AssertTrue("ambiguous killer role fails closed", !PersonaMilestonePolicy.Evaluate(input, policy).qualifies);
            input = Milestone(); input.deathVictimRoleToken = input.resolvedKillerRoleToken;
            AssertTrue("killer cannot also be death victim", !PersonaMilestonePolicy.Evaluate(input, policy).qualifies);
            input = Milestone(); input.hasDeathContext = true; input.deathContextMatchesKiller = false;
            AssertTrue("death-context mismatch fails closed", !PersonaMilestonePolicy.Evaluate(input, policy).qualifies);
            input = Milestone(); input.hasDeathContext = false;
            AssertTrue("missing exact kill scope fails closed", !PersonaMilestonePolicy.Evaluate(input, policy).qualifies);

            string resolvedKiller;
            string resolvedVictim;
            AssertTrue("exact Tale roles resolve", PersonaMilestonePolicy.TryResolveRoles(
                "KilledMajorThreat", policy, out resolvedKiller, out resolvedVictim));
            AssertEqual("resolved killer role", RoyaltyTaleRoleTokens.Initiator, resolvedKiller);
            AssertEqual("resolved victim role", RoyaltyTaleRoleTokens.Recipient, resolvedVictim);
            AssertTrue("unknown Tale roles do not resolve", !PersonaMilestonePolicy.TryResolveRoles(
                "BuiltTable", policy, out resolvedKiller, out resolvedVictim));
            AssertTrue("companion Tale roles resolve", PersonaMilestonePolicy.TryResolveCompanionRoles(
                "KilledMelee", policy, out resolvedKiller, out resolvedVictim));
            AssertEqual("companion killer role", RoyaltyTaleRoleTokens.Initiator, resolvedKiller);
            AssertEqual("companion victim role", RoyaltyTaleRoleTokens.Recipient, resolvedVictim);
            AssertTrue("qualifier is not its own companion", !PersonaMilestonePolicy.TryResolveCompanionRoles(
                "KilledMajorThreat", policy, out resolvedKiller, out resolvedVictim));

            policy.qualifyingTales[0].killerRoleToken = RoyaltyTaleRoleTokens.Recipient;
            policy.qualifyingTales[0].victimRoleToken = RoyaltyTaleRoleTokens.Initiator;
            input = Milestone(); input.resolvedKillerRoleToken = RoyaltyTaleRoleTokens.Initiator;
            AssertTrue("exact role correction enforced", !PersonaMilestonePolicy.Evaluate(input, policy).qualifies);
            input.resolvedKillerRoleToken = RoyaltyTaleRoleTokens.Recipient;
            input.deathVictimRoleToken = RoyaltyTaleRoleTokens.Initiator;
            AssertTrue("recipient-killer correction supported", PersonaMilestonePolicy.Evaluate(input, policy).qualifies);
        }

        private static void TestTitleTransitionMatrixAndDutyCaps()
        {
            RoyaltyPolicySnapshot policy = Policy(1000);
            RoyalTitleSnapshot yeoman = Title("Pawn_A", "Empire", "Yeoman", 1, "throne_room");
            RoyalTitleSnapshot acolyte = Title("Pawn_A", "Empire", "Acolyte", 2,
                "speech", "apparel", "bedroom");
            RoyalTitleTransitionDecision first = RoyalTitleTransitionPolicy.Classify(null, yeoman, false, true, policy);
            AssertEqual("first title", RoyalTitleTransitionTokens.FirstTitle, first.transitionToken);
            AssertTrue("first title advances and emits", first.shouldAdvanceObservation && first.shouldEmit);

            RoyalTitleTransitionDecision promotion = RoyalTitleTransitionPolicy.Classify(
                yeoman, acolyte, false, true, policy);
            AssertEqual("promotion", RoyalTitleTransitionTokens.Promotion, promotion.transitionToken);
            AssertEqual("duty cap", 2, promotion.introducedDutyCategoryTokens.Count);
            AssertEqual("duty stable ordering", "apparel", promotion.introducedDutyCategoryTokens[0]);
            AssertEqual("second duty stable ordering", "bedroom", promotion.introducedDutyCategoryTokens[1]);

            AssertEqual("demotion", RoyalTitleTransitionTokens.Demotion,
                RoyalTitleTransitionPolicy.Classify(acolyte, yeoman, false, true, policy).transitionToken);
            AssertEqual("loss", RoyalTitleTransitionTokens.Loss,
                RoyalTitleTransitionPolicy.Classify(yeoman, null, false, true, policy).transitionToken);
            string promotionKey = RoyalTitleTransitionPolicy.BuildEventDedupKey(
                yeoman, acolyte, RoyalTitleTransitionTokens.Promotion, 700);
            AssertTrue("valid title edge has stable dedup key", promotionKey.Length > 0);
            AssertEqual("identical title edge keeps identical dedup key", promotionKey,
                RoyalTitleTransitionPolicy.BuildEventDedupKey(
                    yeoman, acolyte, RoyalTitleTransitionTokens.Promotion, 700));
            AssertTrue("different same-tick title edge keeps distinct dedup key",
                promotionKey != RoyalTitleTransitionPolicy.BuildEventDedupKey(
                    acolyte, null, RoyalTitleTransitionTokens.Loss, 700));
            AssertEqual("malformed title edge has no dedup key", string.Empty,
                RoyalTitleTransitionPolicy.BuildEventDedupKey(
                    null, null, RoyalTitleTransitionTokens.Invalid, -1));
            RoyalTitleSnapshot renamed = Title("Pawn_A", "Empire", "Yeoman", 1);
            renamed.titleLabel = "Localized title changed";
            AssertEqual("same stable title no-op", RoyalTitleTransitionTokens.NoChange,
                RoyalTitleTransitionPolicy.Classify(yeoman, renamed, false, true, policy).transitionToken);
            AssertEqual("equal seniority replacement no meaningful rank edge", RoyalTitleTransitionTokens.NoChange,
                RoyalTitleTransitionPolicy.Classify(yeoman, Title("Pawn_A", "Empire", "Other", 1), false, true, policy).transitionToken);
            AssertEqual("two factions with same label remain distinct", RoyalTitleTransitionTokens.Invalid,
                RoyalTitleTransitionPolicy.Classify(yeoman, Title("Pawn_A", "OtherFaction", "Yeoman", 2), false, true, policy).transitionToken);

            RoyalTitleTransitionDecision claimed = RoyalTitleTransitionPolicy.Classify(
                yeoman, acolyte, true, true, policy);
            AssertTrue("richer owner suppresses progression", claimed.claimedByRicherOwner && !claimed.shouldEmit);
            AssertTrue("claimed transition still advances baseline", claimed.shouldAdvanceObservation);
            RoyalTitleTransitionDecision disabled = RoyalTitleTransitionPolicy.Classify(
                yeoman, acolyte, false, false, policy);
            AssertTrue("disabled progression still advances", disabled.shouldAdvanceObservation && !disabled.shouldEmit);
            AssertEqual("null/null invalid", RoyalTitleTransitionTokens.Invalid,
                RoyalTitleTransitionPolicy.Classify(null, null, false, true, policy).transitionToken);
        }

        private static void TestMutationExactOwnersAndDedup()
        {
            RoyaltyPolicySnapshot policy = Policy(1000);
            List<RoyalMutationFact> mutations = new List<RoyalMutationFact>
            {
                TitleMutation("Pawn_A", "Empire", "Yeoman", "Acolyte", 105, "corr"),
                PsylinkMutation("Pawn_A", 1, 2, 106, "corr")
            };
            RoyalMutationCauseScope bestowing = Scope(
                RoyalMutationCauseTokens.ImperialBestowing, "Pawn_A", 100, "corr");
            bestowing.factionId = "Empire";
            bestowing.previousTitleDefName = "Yeoman";
            bestowing.newTitleDefName = "Acolyte";
            bestowing.previousPsylinkLevel = 1;
            bestowing.newPsylinkLevel = 2;
            RoyalMutationBatchPlan plan = RoyalMutationOwnershipPolicy.Plan(
                mutations, bestowing, 110, true, true, false, policy);
            AssertEqual("bestowing claims both mutations", 2, plan.mutations.Count);
            AssertEqual("bestowing owner ritual", RoyalMutationOwnerTokens.Ritual, plan.ownerToken);
            AssertTrue("bestowing emits one owner", plan.shouldEmitOwnerPage && !plan.shouldEmitFallbackPage);
            AssertTrue("title duplicate suppressed", plan.mutations[0].suppressProgressionDuplicate);
            AssertTrue("psylink duplicate suppressed", plan.mutations[1].suppressProgressionDuplicate);

            RoyalMutationCauseScope anima = Scope(RoyalMutationCauseTokens.AnimaLinking, "Pawn_A", 100, "corr");
            anima.previousPsylinkLevel = 1;
            anima.newPsylinkLevel = 2;
            plan = RoyalMutationOwnershipPolicy.Plan(mutations, anima, 110, true, true, false, policy);
            AssertTrue("anima does not claim title", !plan.mutations[0].exactCauseMatch);
            AssertTrue("anima claims psylink", plan.mutations[1].exactCauseMatch);
            AssertEqual("anima psylink owner ritual", RoyalMutationOwnerTokens.Ritual, plan.mutations[1].ownerToken);

            RoyalMutationCauseScope neuroformer = Scope(
                RoyalMutationCauseTokens.Neuroformer, "Pawn_A", 100, "corr");
            neuroformer.previousPsylinkLevel = 1;
            neuroformer.newPsylinkLevel = 2;
            plan = RoyalMutationOwnershipPolicy.Plan(
                new List<RoyalMutationFact> { mutations[1] }, neuroformer, 110, false, true, false, policy);
            AssertEqual("neuroformer owns progression", RoyalMutationOwnerTokens.Progression, plan.ownerToken);
            AssertTrue("neuroformer emits one progression", plan.shouldEmitOwnerPage);

            RoyalMutationCauseScope succession = Scope(
                RoyalMutationCauseTokens.SuccessionRelated, "Pawn_A", 100, "corr");
            succession.factionId = "Empire";
            succession.previousTitleDefName = "Yeoman";
            succession.newTitleDefName = "Acolyte";
            plan = RoyalMutationOwnershipPolicy.Plan(
                new List<RoyalMutationFact> { mutations[0] }, succession, 110, true, true, false, policy);
            AssertEqual("succession owns exact title", RoyalMutationOwnerTokens.Succession, plan.ownerToken);
            AssertTrue("succession suppresses title progression", plan.mutations[0].suppressProgressionDuplicate);

            plan = RoyalMutationOwnershipPolicy.Plan(mutations, bestowing, 110, true, false, false, policy);
            AssertTrue("disabled owner emits nothing", !plan.shouldEmitOwnerPage);
            AssertTrue("disabled owner still advances both observations",
                plan.mutations[0].advanceObservation && plan.mutations[1].advanceObservation);
        }

        private static void TestPhase4FactionObservationEdges()
        {
            RoyalTitleSnapshot yeoman = Title("Pawn_A", "Empire", "Yeoman", 1);
            RoyalTitleSnapshot acolyte = Title("Pawn_A", "Empire", "Acolyte", 2);
            List<RoyalTitleObservationSnapshot> baseline =
                RoyaltyStatePersistence.BaselineTitles(
                    new List<RoyalTitleSnapshot> { yeoman }, 100);

            List<RoyalTitleObservationSnapshot> afterHook =
                RoyalTitleObservationPolicy.Advance(baseline, yeoman, acolyte, 101);
            AssertEqual("exact hook replaces one faction row", 1, afterHook.Count);
            AssertEqual("exact hook stores current title", "Acolyte", afterHook[0].titleDefName);
            AssertEqual("exact hook records immediate tick", 101, afterHook[0].lastObservedTick);
            AssertEqual("hook update prevents scanner duplicate", 0,
                RoyalTitleObservationPolicy.Diff(
                    "Pawn_A", afterHook, new List<RoyalTitleSnapshot> { acolyte }, 102).Count);

            List<RoyalTitleMutationSnapshot> loss = RoyalTitleObservationPolicy.Diff(
                "Pawn_A", baseline, new List<RoyalTitleSnapshot>(), 102);
            AssertEqual("scanner fallback includes disappeared faction", 1, loss.Count);
            AssertEqual("scanner loss preserves faction", "Empire", loss[0].factionId);
            AssertNotNull("scanner loss preserves previous title", loss[0].previousTitle);
            AssertTrue("scanner loss has absent current title", loss[0].newTitle == null);
            AssertEqual("scanner loss classifies exactly", RoyalTitleTransitionTokens.Loss,
                RoyalTitleTransitionPolicy.Classify(
                    loss[0].previousTitle, loss[0].newTitle, false, true, Policy(1000)).transitionToken);

            // Observation advance is deliberately independent of a page-output decision. This is
            // the pure disabled->enabled contract used by the runtime component.
            RoyalTitleTransitionDecision disabled = RoyalTitleTransitionPolicy.Classify(
                yeoman, acolyte, false, false, Policy(1000));
            List<RoyalTitleObservationSnapshot> disabledAdvance =
                RoyalTitleObservationPolicy.Advance(baseline, yeoman, acolyte, 103);
            AssertTrue("disabled title edge advances without page",
                disabled.shouldAdvanceObservation && !disabled.shouldEmit);
            AssertEqual("re-enable does not catch up disabled edge", 0,
                RoyalTitleObservationPolicy.Diff(
                    "Pawn_A", disabledAdvance, new List<RoyalTitleSnapshot> { acolyte }, 104).Count);

            RoyalTitleSnapshot sameLabelOtherFaction = Title(
                "Pawn_A", "Deserters", "RebelKnight", 2);
            List<RoyalTitleObservationSnapshot> twoFactionBaseline =
                RoyaltyStatePersistence.BaselineTitles(
                    new List<RoyalTitleSnapshot> { yeoman, sameLabelOtherFaction }, 110);
            List<RoyalTitleMutationSnapshot> oneFactionChange = RoyalTitleObservationPolicy.Diff(
                "Pawn_A", twoFactionBaseline,
                new List<RoyalTitleSnapshot> { acolyte, sameLabelOtherFaction }, 111);
            AssertEqual("identical localized labels stay faction-distinct", 1, oneFactionChange.Count);
            AssertEqual("only exact changed faction emitted", "Empire", oneFactionChange[0].factionId);
            AssertEqual("malformed pawn cannot create scanner edges", 0,
                RoyalTitleObservationPolicy.Diff(
                    "bad|pawn", baseline, new List<RoyalTitleSnapshot> { acolyte }, 120).Count);
        }

        private static void TestMutationExpiryMismatchAndFallback()
        {
            RoyaltyPolicySnapshot policy = Policy(1000);
            List<RoyalMutationFact> mutations = new List<RoyalMutationFact>
            {
                TitleMutation("Pawn_A", "Empire", "Yeoman", "Acolyte", 105, "corr"),
                PsylinkMutation("Pawn_A", 1, 2, 106, "corr")
            };
            RoyalMutationCauseScope bestowing = Scope(
                RoyalMutationCauseTokens.ImperialBestowing, "Pawn_A", 100, "corr");
            bestowing.factionId = "Empire";
            bestowing.previousTitleDefName = "Yeoman";
            bestowing.newTitleDefName = "Acolyte";
            bestowing.previousPsylinkLevel = 1;
            bestowing.newPsylinkLevel = 2;

            RoyalMutationBatchPlan boundary = RoyalMutationOwnershipPolicy.Plan(
                mutations, bestowing, 1105, false, true, false, policy);
            AssertEqual("earliest mutation at inclusive expiry boundary pending", RoyalMutationOwnerTokens.Pending,
                boundary.mutations[0].ownerToken);
            AssertTrue("pending emits no page", !boundary.shouldEmitOwnerPage && !boundary.shouldEmitFallbackPage);

            RoyalMutationBatchPlan expired = RoyalMutationOwnershipPolicy.Plan(
                mutations, bestowing, 1107, false, true, false, policy);
            AssertTrue("expired batch emits one fallback", expired.shouldEmitFallbackPage);
            AssertTrue("expired batch consumes fallback", expired.fallbackConsumed);
            AssertEqual("only first mutation owns fallback", RoyalMutationOwnerTokens.FallbackProgression,
                expired.mutations[0].ownerToken);
            AssertEqual("second mutation cannot double-write fallback", RoyalMutationOwnerTokens.None,
                expired.mutations[1].ownerToken);

            RoyalMutationBatchPlan consumed = RoyalMutationOwnershipPolicy.Plan(
                mutations, bestowing, 1107, false, true, true, policy);
            AssertTrue("previous fallback consumption prevents replay", !consumed.shouldEmitFallbackPage);

            RoyalMutationCauseScope wrongPawn = Scope(
                RoyalMutationCauseTokens.ImperialBestowing, "Pawn_B", 100, "corr");
            wrongPawn.factionId = "Empire";
            wrongPawn.previousTitleDefName = "Yeoman";
            wrongPawn.newTitleDefName = "Acolyte";
            RoyalMutationBatchPlan mismatch = RoyalMutationOwnershipPolicy.Plan(
                new List<RoyalMutationFact> { mutations[0] }, wrongPawn, 110, true, true, false, policy);
            AssertTrue("wrong pawn cannot claim", !mismatch.mutations[0].exactCauseMatch);
            AssertEqual("wrong pawn uses honest generic progression", RoyalMutationOwnerTokens.Progression,
                mismatch.mutations[0].ownerToken);

            RoyalMutationCauseScope wrongLevel = Scope(
                RoyalMutationCauseTokens.AnimaLinking, "Pawn_A", 100, "corr");
            wrongLevel.previousPsylinkLevel = 0;
            wrongLevel.newPsylinkLevel = 3;
            mismatch = RoyalMutationOwnershipPolicy.Plan(
                new List<RoyalMutationFact> { mutations[1] }, wrongLevel, 110, true, true, false, policy);
            AssertTrue("wrong levels cannot claim", !mismatch.mutations[0].exactCauseMatch);

            RoyalMutationCauseScope wrongFaction = Scope(
                RoyalMutationCauseTokens.ImperialBestowing, "Pawn_A", 100, "corr");
            wrongFaction.factionId = "Deserters";
            wrongFaction.previousTitleDefName = "Yeoman";
            wrongFaction.newTitleDefName = "Acolyte";
            mismatch = RoyalMutationOwnershipPolicy.Plan(
                new List<RoyalMutationFact> { mutations[0] }, wrongFaction,
                110, true, true, false, policy);
            AssertTrue("wrong faction cannot claim", !mismatch.mutations[0].exactCauseMatch);

            List<RoyalMutationFact> malformed = new List<RoyalMutationFact>
            {
                null,
                PsylinkMutation("Pawn_A", 2, 2, 100, "same"),
                TitleMutation("Pawn_A", "Empire", "Yeoman", "Yeoman", 100, "same"),
                new RoyalMutationFact { kindToken = "invented", pawnId = "Pawn_A", previousValue = "a", newValue = "b", tick = 1 }
            };
            AssertEqual("malformed/no-op mutations rejected", 0,
                RoyalMutationOwnershipPolicy.Plan(malformed, null, 100, false, true, false, policy).mutations.Count);

            RoyalMutationBatchPlan unknown = RoyalMutationOwnershipPolicy.Plan(
                new List<RoyalMutationFact> { mutations[1] }, null, 110, false, true, false, policy);
            AssertEqual("unknown source keeps scanner progression", RoyalMutationOwnerTokens.Progression, unknown.ownerToken);
            AssertTrue("unknown source emits once", unknown.shouldEmitOwnerPage);

            RoyalMutationBatchPlan disabledFallback = RoyalMutationOwnershipPolicy.Plan(
                mutations, bestowing, 1107, false, false, false, policy);
            AssertTrue("disabled fallback consumed without page",
                disabledFallback.fallbackConsumed && !disabledFallback.shouldEmitFallbackPage);

            RoyalMutationBatchPlan future = RoyalMutationOwnershipPolicy.Plan(
                new List<RoyalMutationFact> { PsylinkMutation("Pawn_A", 1, 2, 500, "future") },
                null, 499, false, true, false, policy);
            AssertTrue("future mutation timing fails closed",
                !future.shouldEmitOwnerPage && !future.mutations[0].advanceObservation);
        }

        private static void TestMutationOutputSelectionAndMasterGate()
        {
            AssertEqual("title is richer when both mutation routes are enabled",
                RoyalMutationKindTokens.Title,
                RoyalMutationPageSelectionPolicy.Select(true, true, true, true, true));
            AssertEqual("disabled title route preserves enabled psylink route",
                RoyalMutationKindTokens.Psylink,
                RoyalMutationPageSelectionPolicy.Select(true, true, false, true, true));
            AssertEqual("unchanged title preserves enabled psylink route",
                RoyalMutationKindTokens.Psylink,
                RoyalMutationPageSelectionPolicy.Select(true, false, true, true, true));
            AssertEqual("all filtered routes produce no page", string.Empty,
                RoyalMutationPageSelectionPolicy.Select(true, true, false, true, false));
            AssertEqual("Royalty master switch suppresses every mutation route", string.Empty,
                RoyalMutationPageSelectionPolicy.Select(false, true, true, true, true));

            RoyaltyPolicySnapshot policy = Policy(1000);
            policy.enabled = false;
            List<RoyalMutationFact> mutations = new List<RoyalMutationFact>
            {
                TitleMutation("Pawn_A", "Empire", "Yeoman", "Acolyte", 105, "disabled"),
                PsylinkMutation("Pawn_A", 1, 2, 106, "disabled")
            };
            RoyalMutationCauseScope scope = Scope(
                RoyalMutationCauseTokens.ImperialBestowing, "Pawn_A", 100, "disabled");
            scope.factionId = "Empire";
            scope.previousTitleDefName = "Yeoman";
            scope.newTitleDefName = "Acolyte";
            scope.previousPsylinkLevel = 1;
            scope.newPsylinkLevel = 2;
            RoyalMutationBatchPlan expired = RoyalMutationOwnershipPolicy.Plan(
                mutations, scope, 1107, false, true, false, policy);
            AssertTrue("disabled master still consumes expired truth",
                expired.fallbackConsumed && expired.mutations.Count == 2);
            AssertTrue("disabled master never creates an expiry page",
                !expired.shouldEmitOwnerPage && !expired.shouldEmitFallbackPage);
        }

        private static void TestMutationRuntimeCorrelationStore()
        {
            RoyalMutationCorrelation.Clear();
            RoyaltyPolicySnapshot policy = Policy(1000);
            RoyalTitleSnapshot before = Title("Pawn_A", "Empire", "Yeoman", 1);
            RoyalTitleSnapshot after = Title("Pawn_A", "Empire", "Acolyte", 2);

            RoyalMutationBatchSnapshot batch = RoyalMutationCorrelation.Open(
                "Pawn_A", "Ari", "Empire", RoyalMutationCauseTokens.ImperialBestowing,
                100, before, 1, 1);
            AssertNotNull("exact correlation scope opens", batch);
            AssertTrue("active cap rejects overflow", RoyalMutationCorrelation.Open(
                "Pawn_B", "Bea", "Empire", RoyalMutationCauseTokens.ImperialBestowing,
                100, before, 1, 1) == null);
            AssertTrue("bestowing title owner matches exact pawn/faction",
                RoyalMutationCorrelation.HasRicherTitleOwner("Pawn_A", "Empire", 100, 1000));
            AssertTrue("bestowing title owner includes the expiry boundary",
                RoyalMutationCorrelation.HasRicherTitleOwner("Pawn_A", "Empire", 1100, 1000));
            AssertTrue("bestowing title owner rejects wrong pawn and faction",
                !RoyalMutationCorrelation.HasRicherTitleOwner("Pawn_B", "Empire", 100, 1000)
                && !RoyalMutationCorrelation.HasRicherTitleOwner("Pawn_A", "Deserters", 100, 1000));
            AssertTrue("bestowing title owner expires after its window",
                !RoyalMutationCorrelation.HasRicherTitleOwner("Pawn_A", "Empire", 1101, 1000));

            AssertTrue("completed ritual mutation enters pending ownership",
                RoyalMutationCorrelation.Complete(
                    batch,
                    TitleMutationSnapshot(batch, before, after, 105),
                    PsylinkMutationSnapshot(batch, 1, 2, 106),
                    64));
            AssertTrue("completion moves active to pending",
                RoyalMutationCorrelation.ActiveCountForTests == 0
                && RoyalMutationCorrelation.PendingCountForTests == 1
                && RoyalMutationCorrelation.HasPending);
            AssertTrue("completed boundary cannot be closed a second time",
                !RoyalMutationCorrelation.IsActive(batch)
                && !RoyalMutationCorrelation.Complete(
                    batch,
                    TitleMutationSnapshot(batch, before, after, 105),
                    PsylinkMutationSnapshot(batch, 1, 2, 106),
                    64));
            AssertTrue("wrong ritual cause cannot prepare owner",
                RoyalMutationCorrelation.PrepareRitualOwner(
                    RoyalMutationCauseTokens.AnimaLinking,
                    new List<string> { "Pawn_A" }, 110, policy) == null);
            AssertTrue("wrong ritual candidate cannot prepare owner",
                RoyalMutationCorrelation.PrepareRitualOwner(
                    RoyalMutationCauseTokens.ImperialBestowing,
                    new List<string> { "Pawn_B" }, 110, policy) == null);
            RoyalMutationBatchSnapshot prepared = RoyalMutationCorrelation.PrepareRitualOwner(
                RoyalMutationCauseTokens.ImperialBestowing,
                new List<string> { "Pawn_A" }, 110, policy);
            AssertTrue("exact ritual owner prepares and claims once",
                prepared == batch && RoyalMutationCorrelation.ClaimRitual(prepared)
                && !RoyalMutationCorrelation.ClaimRitual(prepared));

            batch = RoyalMutationCorrelation.Open(
                "Pawn_A", "Ari", "Empire", RoyalMutationCauseTokens.ImperialBestowing,
                200, before, 1, 16);
            AssertTrue("disabled canonical ritual consumes without staging",
                RoyalMutationCorrelation.Complete(
                    batch, TitleMutationSnapshot(batch, before, after, 200), null, 64, false)
                && RoyalMutationCorrelation.PendingCountForTests == 0);

            RoyalTitleSnapshot capBeforeA = Title("Pawn_CapA", "Empire", "Yeoman", 1);
            RoyalTitleSnapshot capAfterA = Title("Pawn_CapA", "Empire", "Acolyte", 2);
            RoyalMutationBatchSnapshot capFirst = OpenCompletedTitleBatch(
                "Pawn_CapA", "Empire", 250, capBeforeA, capAfterA, 1);
            RoyalTitleSnapshot capBeforeB = Title("Pawn_CapB", "Empire", "Yeoman", 1);
            RoyalTitleSnapshot capAfterB = Title("Pawn_CapB", "Empire", "Acolyte", 2);
            RoyalMutationBatchSnapshot capSecond = RoyalMutationCorrelation.Open(
                "Pawn_CapB", "Cap B", "Empire", RoyalMutationCauseTokens.ImperialBestowing,
                251, capBeforeB, 0, 16);
            AssertTrue("pending cap fixture fills its one allowed slot",
                capFirst != null && capSecond != null
                && RoyalMutationCorrelation.PendingCountForTests == 1);
            AssertTrue("pending cap fails closed without retaining overflow",
                !RoyalMutationCorrelation.Complete(
                    capSecond, TitleMutationSnapshot(capSecond, capBeforeB, capAfterB, 251), null, 1)
                && RoyalMutationCorrelation.ActiveCountForTests == 0
                && RoyalMutationCorrelation.PendingCountForTests == 1);
            AssertTrue("pending cap fixture claims cleanly",
                RoyalMutationCorrelation.ClaimRitual(capFirst)
                && RoyalMutationCorrelation.PendingCountForTests == 0);

            RoyalMutationBatchSnapshot saveBatch = OpenCompletedTitleBatch(
                "Pawn_Save", "Empire", 275,
                Title("Pawn_Save", "Empire", "Yeoman", 1),
                Title("Pawn_Save", "Empire", "Acolyte", 2), 64);
            AssertTrue("pre-save fallback consumes only the exact live pawn batch",
                saveBatch != null
                && RoyalMutationCorrelation.TakePendingForSave("Pawn_Other") == null
                && RoyalMutationCorrelation.TakePendingForSave("Pawn_Save") == saveBatch
                && saveBatch.fallbackConsumed
                && RoyalMutationCorrelation.PendingCountForTests == 0);

            batch = RoyalMutationCorrelation.Open(
                "Pawn_A", "Ari", "Empire", RoyalMutationCauseTokens.ImperialBestowing,
                300, before, 1, 16);
            AssertTrue("enabled ritual stages before master switch",
                RoyalMutationCorrelation.Complete(
                    batch, TitleMutationSnapshot(batch, before, after, 300), null, 64));
            policy.enabled = false;
            AssertTrue("disabled master refuses ritual attachment",
                RoyalMutationCorrelation.PrepareRitualOwner(
                    RoyalMutationCauseTokens.ImperialBestowing,
                    new List<string> { "Pawn_A" }, 301, policy) == null);
            AssertTrue("disabled master consumes expired pending mutation without replay",
                RoyalMutationCorrelation.TakeExpiredFallback("Pawn_A", 1301, true, policy) == null
                && RoyalMutationCorrelation.PendingCountForTests == 0);

            policy.enabled = true;
            RoyalMutationBatchSnapshot missing = OpenCompletedTitleBatch(
                "Pawn_A", "Empire", 400, before, after, 64);
            RoyalMutationBatchSnapshot eligible = OpenCompletedTitleBatch(
                "Pawn_B", "Empire", 400,
                Title("Pawn_B", "Empire", "Yeoman", 1),
                Title("Pawn_B", "Empire", "Acolyte", 2), 64);
            RoyalMutationBatchSnapshot youngMissing = OpenCompletedTitleBatch(
                "Pawn_C", "Empire", 1000,
                Title("Pawn_C", "Empire", "Yeoman", 1),
                Title("Pawn_C", "Empire", "Acolyte", 2), 64);
            AssertTrue("global prune fixtures staged", missing != null && eligible != null
                && youngMissing != null && RoyalMutationCorrelation.PendingCountForTests == 3);
            int pruned = RoyalMutationCorrelation.PruneExpiredMissingOwners(
                new HashSet<string>(StringComparer.Ordinal) { "Pawn_B" }, 1401, policy);
            AssertTrue("global prune removes only expired missing pawn",
                pruned == 1 && RoyalMutationCorrelation.PendingCountForTests == 2);
            AssertTrue("eligible pawn still receives its one expired fallback",
                RoyalMutationCorrelation.TakeExpiredFallback("Pawn_B", 1401, true, policy) == eligible
                && RoyalMutationCorrelation.PendingCountForTests == 1);
            AssertTrue("later global pass removes newly expired missing pawn",
                RoyalMutationCorrelation.PruneExpiredMissingOwners(
                    new HashSet<string>(StringComparer.Ordinal), 2001, policy) == 1
                && RoyalMutationCorrelation.PendingCountForTests == 0);
            RoyalMutationCorrelation.Clear();
        }

        private static void TestPhase4RoutesThoughtsAndContext()
        {
            RoyaltyPolicySnapshot policy = RoyaltyPolicySnapshot.CreateDefault();
            AssertEqual("bestowing exact string route", RoyalMutationCauseTokens.ImperialBestowing,
                RoyalMutationRoutePolicy.RitualCause("BestowingCeremony", policy));
            AssertEqual("anima exact string route", RoyalMutationCauseTokens.AnimaLinking,
                RoyalMutationRoutePolicy.RitualCause("AnimaTreeLinking", policy));
            AssertEqual("unknown ritual stays unknown", RoyalMutationCauseTokens.Unknown,
                RoyalMutationRoutePolicy.RitualCause("ModdedBestowingLabel", policy));
            AssertTrue("neuroformer exact parent string", RoyalMutationRoutePolicy.IsNeuroformer(
                "PsychicAmplifier", policy));
            AssertTrue("localized item label cannot route", !RoyalMutationRoutePolicy.IsNeuroformer(
                "psychic neuroformer", policy));

            RoyalTitleThoughtSnapshot award = new RoyalTitleThoughtSnapshot
            {
                pawnId = "Pawn_A",
                titleDefName = "Acolyte",
                relationshipToken = RoyalTitleThoughtRelationshipTokens.Award,
                tick = 100
            };
            AssertTrue("exact award thought claimed", RoyalTitleThoughtOwnershipPolicy.Matches(
                award, "Pawn_A", "Yeoman", "Acolyte"));
            AssertTrue("award wrong pawn releases", !RoyalTitleThoughtOwnershipPolicy.Matches(
                award, "Pawn_B", "Yeoman", "Acolyte"));
            AssertTrue("award wrong new title releases", !RoyalTitleThoughtOwnershipPolicy.Matches(
                award, "Pawn_A", "Yeoman", "Knight"));
            RoyalTitleThoughtSnapshot loss = new RoyalTitleThoughtSnapshot
            {
                pawnId = "Pawn_A",
                titleDefName = "Yeoman",
                relationshipToken = RoyalTitleThoughtRelationshipTokens.Loss,
                tick = 100
            };
            AssertTrue("exact loss thought claimed", RoyalTitleThoughtOwnershipPolicy.Matches(
                loss, "Pawn_A", "Yeoman", "Acolyte"));
            AssertTrue("title thought window inclusive",
                !RoyalTitleThoughtOwnershipPolicy.IsExpired(100, 2600, 2500));
            AssertTrue("title thought expires after window",
                RoyalTitleThoughtOwnershipPolicy.IsExpired(100, 2601, 2500));
            loss.relationshipToken = "localized loss";
            AssertTrue("malformed thought relationship releases",
                !RoyalTitleThoughtOwnershipPolicy.Matches(
                    loss, "Pawn_A", "Yeoman", "Acolyte"));

            RoyalTitleSnapshot before = Title("Pawn_A", "Empire", "Yeoman", 1, "throne_room");
            RoyalTitleSnapshot after = Title(
                "Pawn_A", "Empire", "Acolyte", 2, "speech", "apparel", "bedroom");
            before.titleLabel = "Yeoman";
            after.titleLabel = "Acolyte";
            RoyalMutationBatchSnapshot batch = new RoyalMutationBatchSnapshot
            {
                pawnId = "Pawn_A",
                pawnName = "Ari; the First",
                causeToken = RoyalMutationCauseTokens.ImperialBestowing,
                titleMutation = new RoyalTitleMutationSnapshot
                {
                    pawnId = "Pawn_A",
                    factionId = "Empire",
                    previousTitle = before,
                    newTitle = after,
                    tick = 100
                },
                psylinkMutation = new RoyalPsychicMutationSnapshot
                {
                    pawnId = "Pawn_A",
                    previousPsylinkLevel = 1,
                    newPsylinkLevel = 2,
                    tick = 100
                }
            };
            string full = RoyalMutationContextFormatter.Format(
                batch, RoyalTitleTransitionTokens.Promotion, 120, 2, true);
            AssertTrue("mutation context sanitizes pawn prose",
                full.Contains("royal_mutation_pawn=Ari, the First"));
            AssertTrue("mutation context preserves cause",
                full.Contains("royal_cause=imperial_bestowing"));
            AssertTrue("mutation context preserves exact faction",
                full.Contains("royal_faction_id=Empire") && full.Contains("royal_faction=Empire Name"));
            AssertTrue("mutation context preserves title before/after",
                full.Contains("previous_title=Yeoman") && full.Contains("title=Acolyte"));
            AssertTrue("mutation context preserves psylink before/after",
                full.Contains("previous_psylink_level=1") && full.Contains("psylink_level=2"));
            AssertTrue("duty additions are bounded and stable",
                full.Contains("royal_duty_changes=apparel, bedroom") && !full.Contains("speech"));
            string compact = RoyalMutationContextFormatter.Format(
                batch, RoyalTitleTransitionTokens.Promotion, 120, 2, false);
            AssertTrue("compact removes optional duty prose first",
                !compact.Contains("royal_duty_changes="));
            AssertTrue("compact keeps exact title and psylink edges",
                compact.Contains("previous_title=Yeoman") && compact.Contains("title=Acolyte")
                && compact.Contains("previous_psylink_level=1") && compact.Contains("psylink_level=2"));

            batch.pawnName = new string('x', 800) + ";tail";
            string bounded = RoyalMutationContextFormatter.Format(
                batch, RoyalTitleTransitionTokens.Promotion, 20, 2, false);
            string pawnField = bounded.Split(';')[0];
            AssertTrue("oversized mutation prose is capped", pawnField.Length <= 20 + 20);
            batch.pawnId = "bad|pawn";
            AssertEqual("malformed mutation identity fails safely", string.Empty,
                RoyalMutationContextFormatter.Format(
                    batch, RoyalTitleTransitionTokens.Promotion, 120, 2, true));
            AssertEqual("null mutation context fails safely", string.Empty,
                RoyalMutationContextFormatter.Format(
                    null, RoyalTitleTransitionTokens.Promotion, 120, 2, true));
        }

        private static void TestSuccessionCommitMatchingAndContext()
        {
            RoyalSuccessionCandidateSnapshot candidate = SuccessionCandidate("Pawn_Dead", "Pawn_Heir", 100);
            AssertTrue("candidate alone never authorizes succession",
                RoyalSuccessionPolicy.Commit(candidate, null, 2500) == null);

            RoyalSuccessionCommitObservation commit = new RoyalSuccessionCommitObservation
            {
                correlationId = candidate.correlationId,
                deceasedPawnId = candidate.deceasedPawnId,
                factionId = candidate.factionId,
                inheritedTitleDefName = candidate.inheritedTitleDefName,
                wasInherited = true,
                commitTick = 110
            };
            RoyalSuccessionFact fact = RoyalSuccessionPolicy.Commit(candidate, commit, 2500);
            AssertNotNull("exact candidate plus outer wasInherited commit authorizes", fact);
            AssertEqual("succession expiry is XML-window bounded", 2610, fact.expiresTick);
            AssertEqual("succession keeps exact deceased identity", "Pawn_Dead", fact.deceasedPawnId);
            AssertEqual("succession keeps exact heir identity", "Pawn_Heir", fact.heirPawnId);

            RoyalTitleMutationSnapshot mutation = new RoyalTitleMutationSnapshot
            {
                pawnId = "Pawn_Heir",
                factionId = "Faction_Empire",
                previousTitle = new RoyalTitleSnapshot
                {
                    pawnId = "Pawn_Heir", factionId = "Faction_Empire", titleDefName = "Yeoman",
                    titleLabel = "Yeoman", seniority = 1
                },
                newTitle = new RoyalTitleSnapshot
                {
                    pawnId = "Pawn_Heir", factionId = "Faction_Empire", titleDefName = "Acolyte",
                    titleLabel = "Acolyte", seniority = 2
                },
                tick = 110
            };
            AssertTrue("immediate exact title callback belongs to succession",
                RoyalSuccessionPolicy.MatchesMutation(fact, mutation, fact.correlationId, 110));
            AssertTrue("delayed callback matches through inclusive expiry",
                RoyalSuccessionPolicy.MatchesMutation(fact, mutation, fact.correlationId, 2610));
            AssertTrue("callback after expiry is released",
                !RoyalSuccessionPolicy.MatchesMutation(fact, mutation, fact.correlationId, 2611));
            mutation.factionId = "Faction_Other";
            AssertTrue("wrong faction cannot be claimed",
                !RoyalSuccessionPolicy.MatchesMutation(fact, mutation, fact.correlationId, 110));
            mutation.factionId = "Faction_Empire";
            AssertTrue("wrong correlation cannot be claimed",
                !RoyalSuccessionPolicy.MatchesMutation(fact, mutation, "other", 110));
            mutation.newTitle.titleDefName = "Knight";
            AssertTrue("wrong inherited rank cannot be claimed",
                !RoyalSuccessionPolicy.MatchesMutation(fact, mutation, fact.correlationId, 110));

            candidate.heirAlreadyHeldEqualOrHigherTitle = true;
            AssertTrue("equal-or-higher heir is not a succession page",
                RoyalSuccessionPolicy.Commit(candidate, commit, 2500) == null);
            candidate.heirAlreadyHeldEqualOrHigherTitle = false;
            commit.wasInherited = false;
            AssertTrue("uncommitted outcome is rejected",
                RoyalSuccessionPolicy.Commit(candidate, commit, 2500) == null);
            commit.wasInherited = true;
            commit.factionId = "Faction_Other";
            AssertTrue("commit faction mismatch is rejected",
                RoyalSuccessionPolicy.Commit(candidate, commit, 2500) == null);

            string context = RoyalSuccessionContextFormatter.Format(fact, 120);
            AssertTrue("succession context exposes four truthful bounded facts",
                context.Contains("succession_deceased=Former Count")
                && context.Contains("succession_heir=New Heir")
                && context.Contains("succession_title=Acolyte")
                && context.Contains("succession_faction=Shattered Empire"));
            AssertTrue("succession context omits internal proof metadata",
                !context.Contains("Pawn_Dead") && !context.Contains("correlation")
                && !context.Contains("commitTick") && !context.Contains("wasInherited"));
        }

        private static void TestSuccessionCorrelationNormalizationAndAppointment()
        {
            RoyalSuccessionCorrelation.Clear();
            RoyalSuccessionDeathScope scope = RoyalSuccessionCorrelation.Open("Pawn_Dead", 100, 4);
            RoyalSuccessionCandidateSnapshot candidate = SuccessionCandidate("Pawn_Dead", "Pawn_Heir", 100);
            AssertTrue("candidate enters exact active death scope",
                RoyalSuccessionCorrelation.AddCandidate(candidate, 4));
            AssertTrue("candidate receives distinct edge correlation", candidate.correlationId.Contains("|edge|0"));
            RoyalTitleMutationSnapshot mutation = new RoyalTitleMutationSnapshot
            {
                pawnId = "Pawn_Heir", factionId = "Faction_Empire",
                previousTitle = new RoyalTitleSnapshot { titleDefName = "Yeoman" },
                newTitle = new RoyalTitleSnapshot { titleDefName = "Acolyte" }, tick = 101
            };
            AssertTrue("title callback can precede outer commit",
                RoyalSuccessionCorrelation.StageTitle(mutation, 4));
            RoyalSuccessionDeathScope closed = RoyalSuccessionCorrelation.Close(scope);
            AssertEqual("closed scope keeps one candidate", 1, closed.candidates.Count);
            AssertEqual("closed scope keeps one staged callback", 1, closed.stagedTitleMutations.Count);
            AssertEqual("staged callback carries candidate correlation", candidate.correlationId,
                closed.stagedTitleMutations[0].correlationId);
            AssertTrue("closed scope cannot close twice", RoyalSuccessionCorrelation.Close(scope) == null);

            RoyalSuccessionFact first = RoyalSuccessionPolicy.Commit(candidate,
                new RoyalSuccessionCommitObservation
                {
                    correlationId = candidate.correlationId, deceasedPawnId = "Pawn_Dead",
                    factionId = "Faction_Empire", inheritedTitleDefName = "Acolyte",
                    wasInherited = true, commitTick = 110
                }, 2500);
            RoyalSuccessionCandidateSnapshot secondCandidate =
                SuccessionCandidate("Pawn_Dead_2", "Pawn_Heir_2", 100);
            RoyalSuccessionCommitObservation secondCommit = new RoyalSuccessionCommitObservation
            {
                correlationId = secondCandidate.correlationId, deceasedPawnId = secondCandidate.deceasedPawnId,
                factionId = secondCandidate.factionId,
                inheritedTitleDefName = secondCandidate.inheritedTitleDefName,
                wasInherited = true, commitTick = 110
            };
            RoyalSuccessionFact distinctSameTick =
                RoyalSuccessionPolicy.Commit(secondCandidate, secondCommit, 2500);
            List<RoyalSuccessionFact> normalized = RoyalSuccessionPolicy.Normalize(
                new List<RoyalSuccessionFact> { first, first, distinctSameTick }, 110, 4);
            AssertEqual("exact duplicate removed but distinct same-tick edges preserved", 2, normalized.Count);
            normalized = RoyalSuccessionPolicy.Normalize(normalized, 3000, 4);
            AssertEqual("expired succession facts are pruned", 0, normalized.Count);

            RoyalHeirAppointmentSnapshot appointment = new RoyalHeirAppointmentSnapshot
            {
                sourceToken = "change_royal_heir_quest", titleHolderPawnId = "Pawn_Holder",
                previousHeirPawnId = "Pawn_Old", heirPawnId = "Pawn_New",
                heirPawnName = "Named Heir", factionId = "Faction_Empire",
                factionName = "Shattered Empire", titleDefName = "Count",
                titleLabel = "Count", observedTick = 500
            };
            AssertTrue("explicit ChangeRoyalHeir quest appointment is valid",
                RoyalSuccessionPolicy.ValidAppointment(appointment));
            AssertTrue("appointment context omits a nonexistent deceased holder",
                !RoyalSuccessionContextFormatter.FormatAppointment(appointment, 120)
                    .Contains("succession_deceased="));
            appointment.sourceToken = "automatic_set_heir";
            AssertTrue("automatic heir assignment remains silent",
                !RoyalSuccessionPolicy.ValidAppointment(appointment));
            RoyalSuccessionCorrelation.Clear();
        }

        private static RoyalSuccessionCandidateSnapshot SuccessionCandidate(
            string deceasedPawnId,
            string heirPawnId,
            int tick)
        {
            return new RoyalSuccessionCandidateSnapshot
            {
                correlationId = "succession-test", deceasedPawnId = deceasedPawnId,
                deceasedPawnName = "Former Count", heirPawnId = heirPawnId,
                heirPawnName = "New Heir", factionId = "Faction_Empire",
                factionName = "Shattered Empire", inheritedTitleDefName = "Acolyte",
                inheritedTitleLabel = "Acolyte", inheritedTitleSeniority = 2,
                previousHeirTitleDefName = "Yeoman", previousHeirTitleLabel = "Yeoman",
                previousHeirTitleSeniority = 1, candidateTick = tick
            };
        }

        private static RoyaltyPolicySnapshot Policy(int separationTicks)
        {
            RoyaltyPolicySnapshot policy = RoyaltyPolicySnapshot.CreateDefault();
            policy.separationThresholdTicks = separationTicks;
            policy.titleCorrelationTicks = 1000;
            policy.psylinkCorrelationTicks = 1000;
            return policy;
        }

        private static PersonaLifecycleObservation Observation(string token, int tick, bool enabled)
        {
            return new PersonaLifecycleObservation
            {
                observationToken = token,
                tick = tick,
                groupEnabled = enabled,
                weapon = Weapon("Weapon_1", "Pawn_A", token == PersonaObservationTokens.Primary
                    || token == PersonaObservationTokens.Coding || token == PersonaObservationTokens.Baseline
                    || token == PersonaObservationTokens.Transfer)
            };
        }

        private static PersonaWeaponSnapshot Weapon(string weaponId, string pawnId, bool primary)
        {
            return new PersonaWeaponSnapshot
            {
                weaponThingId = weaponId,
                weaponDefName = "PersonaSword",
                displayName = "North Wind",
                codedPawnId = pawnId,
                codedPawnName = pawnId,
                isCurrentlyPrimary = primary,
                traits = new List<PersonaTraitFact> { Trait("KillThought", kill: true) }
            };
        }

        private static PersonaBondStateSnapshot ActiveState()
        {
            return new PersonaBondStateSnapshot
            {
                weaponThingId = "Weapon_1",
                weaponDefName = "PersonaSword",
                lastDisplayName = "North Wind",
                bondEpoch = 1,
                currentPawnId = "Pawn_A",
                currentPawnName = "A",
                phaseToken = PersonaBondPhaseTokens.Active,
                bondStartedTick = 1,
                lastPrimaryObservedTick = 1,
                traits = new List<PersonaTraitFact> { Trait("KillThought", kill: true) }
            };
        }

        private static PersonaTraitFact Trait(
            string defName,
            bool kill = false,
            bool bondedThought = false,
            bool bondedHediff = false,
            bool equipped = false,
            string worker = "")
        {
            return new PersonaTraitFact
            {
                traitDefName = defName,
                label = defName + " localized",
                description = defName + " description",
                workerTypeToken = worker,
                hasKillThought = kill,
                hasBondedThought = bondedThought,
                hasBondedHediff = bondedHediff,
                hasEquippedHediff = equipped
            };
        }

        private static PersonaMilestoneObservation Milestone()
        {
            return new PersonaMilestoneObservation
            {
                bond = ActiveState(),
                currentWeapon = Weapon("Weapon_1", "Pawn_A", true),
                taleDefName = "KilledMajorThreat",
                resolvedKillerRoleToken = RoyaltyTaleRoleTokens.Initiator,
                deathVictimRoleToken = RoyaltyTaleRoleTokens.Recipient,
                significance = 1,
                victimPresent = true,
                victimDied = true,
                hasDeathContext = true,
                deathContextMatchesKiller = true,
                personaGroupEnabled = true,
                pageAccepted = true,
                eventIdentity = "event-kill"
            };
        }

        private static RoyalTitleSnapshot Title(
            string pawnId,
            string factionId,
            string titleDefName,
            int seniority,
            params string[] duties)
        {
            return new RoyalTitleSnapshot
            {
                pawnId = pawnId,
                factionId = factionId,
                factionName = factionId + " Name",
                titleDefName = titleDefName,
                titleLabel = "Same localized title",
                seniority = seniority,
                dutyCategoryTokens = new List<string>(duties ?? new string[0])
            };
        }

        private static RoyalMutationFact TitleMutation(
            string pawnId, string factionId, string before, string after, int tick, string correlation)
        {
            return new RoyalMutationFact
            {
                kindToken = RoyalMutationKindTokens.Title,
                pawnId = pawnId,
                factionId = factionId,
                previousValue = before,
                newValue = after,
                tick = tick,
                correlationId = correlation
            };
        }

        private static RoyalMutationFact PsylinkMutation(
            string pawnId, int before, int after, int tick, string correlation)
        {
            return new RoyalMutationFact
            {
                kindToken = RoyalMutationKindTokens.Psylink,
                pawnId = pawnId,
                previousValue = before.ToString(),
                newValue = after.ToString(),
                tick = tick,
                correlationId = correlation
            };
        }

        private static RoyalTitleMutationSnapshot TitleMutationSnapshot(
            RoyalMutationBatchSnapshot batch,
            RoyalTitleSnapshot before,
            RoyalTitleSnapshot after,
            int tick)
        {
            return new RoyalTitleMutationSnapshot
            {
                pawnId = batch.pawnId,
                factionId = after?.factionId ?? before?.factionId ?? string.Empty,
                previousTitle = before,
                newTitle = after,
                causeToken = batch.causeToken,
                tick = tick,
                correlationId = batch.scope?.correlationId
            };
        }

        private static RoyalPsychicMutationSnapshot PsylinkMutationSnapshot(
            RoyalMutationBatchSnapshot batch,
            int before,
            int after,
            int tick)
        {
            return new RoyalPsychicMutationSnapshot
            {
                pawnId = batch.pawnId,
                previousPsylinkLevel = before,
                newPsylinkLevel = after,
                causeToken = batch.causeToken,
                tick = tick,
                correlationId = batch.scope?.correlationId
            };
        }

        private static RoyalMutationBatchSnapshot OpenCompletedTitleBatch(
            string pawnId,
            string factionId,
            int tick,
            RoyalTitleSnapshot before,
            RoyalTitleSnapshot after,
            int maximumPending)
        {
            RoyalMutationBatchSnapshot batch = RoyalMutationCorrelation.Open(
                pawnId, pawnId, factionId, RoyalMutationCauseTokens.ImperialBestowing,
                tick, before, 0, 16);
            if (batch == null || !RoyalMutationCorrelation.Complete(
                batch, TitleMutationSnapshot(batch, before, after, tick), null, maximumPending))
                return null;
            return batch;
        }

        private static RoyalMutationCauseScope Scope(string cause, string pawnId, int tick, string correlation)
        {
            return new RoyalMutationCauseScope
            {
                causeToken = cause,
                pawnId = pawnId,
                openedTick = tick,
                correlationId = correlation
            };
        }

        private static PersonaTraitFact Find(List<PersonaTraitFact> values, string defName)
        {
            for (int i = 0; i < values.Count; i++)
                if (values[i].traitDefName == defName) return values[i];
            return null;
        }

        private static int Count(List<PersonaTraitFact> values, string defName)
        {
            int count = 0;
            for (int i = 0; i < values.Count; i++) if (values[i].traitDefName == defName) count++;
            return count;
        }

        private static void AssertTrue(string label, bool condition)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("FAILED: " + label);
        }

        private static void AssertNotNull(string label, object value)
        {
            AssertTrue(label, value != null);
        }

        private static void AssertEqual<T>(string label, T expected, T actual)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(
                    "FAILED: " + label + " expected '" + expected + "' but got '" + actual + "'.");
        }
    }
}

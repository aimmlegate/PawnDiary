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
            TestLifecycleContextAndThoughtOwnership();
            TestTraitStructuralRankingOrderingAndCaps();
            TestTraitSanitizationOverridesAndMalformedRows();
            TestMilestoneQualificationAndOwnership();
            TestTitleTransitionMatrixAndDutyCaps();
            TestMutationExactOwnersAndDedup();
            TestMutationExpiryMismatchAndFallback();
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
            AssertEqual("fallback persona thought correlation", 2500, policy.personaThoughtCorrelationTicks);
            AssertEqual("fallback trait cap", 2, policy.maximumSelectedTraits);
            AssertEqual("fallback Tale rows", 2, policy.qualifyingTales.Count);
            AssertEqual("fallback first Tale", "KilledMan", policy.qualifyingTales[0].taleDefName);

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

        private static void TestLifecycleContextAndThoughtOwnership()
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

            List<string> thoughts = new List<string> { "BondedPersonaWeapon" };
            AssertTrue("exact persona thought owner matches", PersonaThoughtOwnershipPolicy.Matches(
                "Pawn_A", thoughts, "Pawn_A", "bondedpersonaweapon"));
            AssertTrue("wrong pawn cannot be claimed", !PersonaThoughtOwnershipPolicy.Matches(
                "Pawn_A", thoughts, "Pawn_B", "BondedPersonaWeapon"));
            AssertTrue("wrong thought cannot be claimed", !PersonaThoughtOwnershipPolicy.Matches(
                "Pawn_A", thoughts, "Pawn_A", "OtherThought"));
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
            PersonaBondStateSnapshot state = ActiveState();
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
            input = Milestone(); input.resolvedKillerRoleToken = string.Empty;
            AssertTrue("ambiguous killer role fails closed", !PersonaMilestonePolicy.Evaluate(input, policy).qualifies);
            input = Milestone(); input.deathVictimRoleToken = input.resolvedKillerRoleToken;
            AssertTrue("killer cannot also be death victim", !PersonaMilestonePolicy.Evaluate(input, policy).qualifies);
            input = Milestone(); input.hasDeathContext = true; input.deathContextMatchesKiller = false;
            AssertTrue("death-context mismatch fails closed", !PersonaMilestonePolicy.Evaluate(input, policy).qualifies);

            policy.qualifyingTales[0].killerRoleToken = RoyaltyTaleRoleTokens.Recipient;
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
                taleDefName = "KilledMan",
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

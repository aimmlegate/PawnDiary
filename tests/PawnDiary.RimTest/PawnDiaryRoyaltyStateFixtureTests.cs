// Royalty Phase-1 loaded-game fixtures. These tests exercise the real Scribe models and guarded live
// collectors only; they create no Royalty event, page, prompt, hook, setting, or player-visible state.
using System;
using System.Collections.Generic;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Proves Royalty save round-trips, old-save markers, and clean no-DLC collection.</summary>
    [TestSuite]
    public static class PawnDiaryRoyaltyStateFixtureTests
    {
        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            pawn = scope.CreateAdultColonist();
        }

        [AfterEach]
        public static void TearDown()
        {
            try { scope?.TearDown(); }
            finally { scope = null; pawn = null; }
        }

        /// <summary>Deep-scribed persona identity, lifecycle flags, and bounded trait facts survive.</summary>
        [Test]
        public static void PersonaBondStateRoundTripsAndNormalizes()
        {
            PersonaBondState original = new PersonaBondState
            {
                weaponThingId = "Weapon_RoyaltyFixture",
                weaponDefName = "FixturePersonaSword",
                lastDisplayName = "Dawn Voice",
                bondEpoch = 2,
                currentPawnId = "Pawn_RoyaltyFixture",
                currentPawnName = "Rue",
                previousPawnId = "Pawn_Previous",
                phaseToken = PersonaBondPhaseTokens.SeparationPending,
                bondStartedTick = 100,
                pendingSeparationTick = 200,
                firstConsequentialKillObserved = true,
                lastPrimaryObservedTick = 150,
                traits = new List<PersonaTraitState>
                {
                    new PersonaTraitState
                    {
                        traitDefName = "FixtureTrait",
                        label = "Measured",
                        description = "It waits.",
                        workerTypeToken = "Fixture.Worker",
                        hasBondedThought = true
                    }
                }
            };

            PersonaBondState loaded = ScribeRoundTrip(original);
            Require(loaded.weaponThingId == original.weaponThingId && loaded.bondEpoch == 2,
                "persona weapon identity/epoch did not survive Scribe.");
            Require(loaded.phaseToken == PersonaBondPhaseTokens.SeparationPending
                    && loaded.pendingSeparationTick == 200,
                "persona pending-separation state did not survive Scribe.");
            Require(loaded.firstConsequentialKillObserved && loaded.traits?.Count == 1
                    && loaded.traits[0].hasBondedThought,
                "persona milestone/trait facts did not survive Scribe normalization.");
        }

        /// <summary>Per-pawn title rows are additive; a missing old-save row remains version zero.</summary>
        [Test]
        public static void PawnRoyaltyObservationRoundTripsAndLegacyStaysUninitialized()
        {
            PawnDiaryRecord loaded = ScribeRoundTrip(new PawnDiaryRecord
            {
                pawnId = "Pawn_RoyaltyState",
                pawnName = "State",
                progressionState = new PawnProgressionState
                {
                    highestPsylinkLevelRecorded = 3,
                    royaltyObservationState = new RoyaltyPawnProgressionState
                    {
                        observationVersion = RoyaltyStatePersistence.CurrentObservationVersion,
                        titleObservations = new List<RoyalTitleObservationState>
                        {
                            new RoyalTitleObservationState
                            {
                                factionId = "Faction_12",
                                factionName = "Empire",
                                titleDefName = "Knight",
                                titleLabel = "Knight",
                                seniority = 4,
                                lastObservedTick = 900
                            }
                        }
                    }
                }
            });
            RoyaltyPawnProgressionState royalty = loaded.progressionState.EnsureRoyaltyState();
            Require(royalty.observationVersion == RoyaltyStatePersistence.CurrentObservationVersion
                    && royalty.titleObservations.Count == 1,
                "current Royalty per-pawn observation did not survive Scribe.");
            Require(loaded.progressionState.highestPsylinkLevelRecorded == 3,
                "Royalty nesting changed the existing psylink scalar.");

            PawnDiaryRecord legacy = ScribeRoundTrip(new PawnDiaryRecord
            {
                pawnId = "Pawn_PreRoyaltyState",
                pawnName = "Legacy",
                progressionState = new PawnProgressionState
                {
                    lastObservedRoyalTitleDefName = "Count",
                    lastObservedRoyalTitleLabel = "Count",
                    royaltyObservationState = null
                }
            });
            RoyaltyPawnProgressionState legacyRoyalty = legacy.progressionState.EnsureRoyaltyState();
            Require(legacyRoyalty.observationVersion == 0 && legacyRoyalty.titleObservations.Count == 0,
                "a missing old-save Royalty row invented a current faction baseline.");
            Require(legacy.progressionState.lastObservedRoyalTitleDefName == "Count",
                "legacy scalar title data was not retained for migration compatibility.");
        }

        /// <summary>Global component save keys distinguish missing from initialized-empty persona state.</summary>
        [Test]
        public static void ComponentPersonaMarkerRoundTripsInitializedEmptyAndLegacyMissing()
        {
            RoyaltyComponentStateMirror initialized = ScribeRoundTrip(new RoyaltyComponentStateMirror
            {
                observationVersion = RoyaltyStatePersistence.CurrentObservationVersion,
                bonds = new List<PersonaBondState>()
            });
            Require(initialized.observationVersion == RoyaltyStatePersistence.CurrentObservationVersion
                    && initialized.bonds != null && initialized.bonds.Count == 0,
                "initialized-empty persona ledger lost its explicit version marker.");

            RoyaltyComponentStateMirror legacy = ScribeRoundTrip(new RoyaltyComponentStateMirror
            {
                observationVersion = 0,
                bonds = null
            });
            Require(legacy.observationVersion == 0 && legacy.bonds != null && legacy.bonds.Count == 0,
                "missing old-save persona keys did not normalize to version-zero empty state.");
        }

        /// <summary>All new live collectors short-circuit cleanly when Royalty is inactive.</summary>
        [Test]
        public static void GuardedCollectorsAreCleanWithoutRoyalty()
        {
            List<PersonaWeaponSnapshot> weapons = DlcContext.CapturePersonaWeapons(pawn);
            List<RoyalTitleSnapshot> titles = DlcContext.CaptureRoyalTitles(pawn);
            int psylink = DlcContext.CurrentPsylinkLevel(pawn);
            Require(weapons != null && titles != null && psylink >= 0 && psylink <= 6,
                "Royalty collectors returned malformed values for a normal pawn.");
            if (!ModsConfig.RoyaltyActive)
            {
                Require(weapons.Count == 0 && titles.Count == 0 && psylink == 0,
                    "Royalty-inactive collectors must return only empty/zero snapshots.");
                Log.Message("[PawnDiary RimTest Royalty P1] Royalty inactive: all guarded collectors returned empty.");
            }
            else
            {
                Log.Message("[PawnDiary RimTest Royalty P1] Royalty active: guarded collectors ran on a disposable pawn.");
            }
        }

        private sealed class RoyaltyComponentStateMirror : IExposable
        {
            public int observationVersion;
            public List<PersonaBondState> bonds;

            public void ExposeData()
            {
                Scribe_Values.Look(ref observationVersion, RoyaltySaveKeys.PersonaObservationVersion, 0);
                Scribe_Collections.Look(ref bonds, RoyaltySaveKeys.PersonaBonds, LookMode.Deep);
                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    observationVersion = Math.Max(0, Math.Min(
                        RoyaltyStatePersistence.CurrentObservationVersion,
                        observationVersion));
                    if (bonds == null) bonds = new List<PersonaBondState>();
                }
            }
        }

        private static T ScribeRoundTrip<T>(T original) where T : class, IExposable
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pawndiary_royalty_" + Guid.NewGuid().ToString("N") + ".xml");
            T loaded = null;
            try
            {
                Scribe.saver.InitSaving(path, "root");
                T saveRef = original;
                Scribe_Deep.Look(ref saveRef, "obj");
                Scribe.saver.FinalizeSaving();
                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "obj");
                Scribe.loader.FinalizeLoading();
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
                try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
                catch { }
            }
            if (loaded == null) throw new AssertionException("Royalty Scribe round-trip returned null.");
            return loaded;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }
    }
}

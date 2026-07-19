// Royalty loaded-game state fixtures. These tests exercise the real Scribe models, Phase-4
// per-faction observation availability, transient ownership reset, and guarded live collectors.
using System;
using System.Collections.Generic;
using System.Reflection;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Proves Royalty save round-trips, old-save markers, and clean no-DLC collection.</summary>
    [TestSuite]
    public static class PawnDiaryRoyaltyStateFixtureTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo FindDiaryMethod =
            typeof(DiaryGameComponent).GetMethod("FindDiary", PrivateInstance);
        private static readonly MethodInfo ExposeRoyaltyDataMethod =
            typeof(DiaryGameComponent).GetMethod("ExposeRoyaltyData", PrivateInstance);
        private static readonly FieldInfo PersonaObservationVersionField =
            typeof(DiaryGameComponent).GetField("royaltyPersonaObservationVersion", PrivateInstance);
        private static readonly FieldInfo PersonaBondsField =
            typeof(DiaryGameComponent).GetField("royaltyPersonaBonds", PrivateInstance);
        private static DiaryGameComponent royaltyScribeTarget;
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
            finally
            {
                RoyaltyTransientState.Reset();
                scope = null;
                pawn = null;
            }
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
                firstConsequentialKillEventRecorded = true,
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
            Require(loaded.firstConsequentialKillObserved
                    && loaded.firstConsequentialKillEventRecorded
                    && loaded.traits?.Count == 1
                    && loaded.traits[0].hasBondedThought,
                "persona milestone/trait facts did not survive Scribe normalization.");

            PersonaBondState consumedWithoutPage = ScribeRoundTrip(new PersonaBondState
            {
                weaponThingId = "Weapon_RoyaltyDisabledFixture",
                bondEpoch = 1,
                currentPawnId = "Pawn_RoyaltyFixture",
                phaseToken = PersonaBondPhaseTokens.Active,
                firstConsequentialKillObserved = true,
                firstConsequentialKillEventRecorded = false
            });
            Require(consumedWithoutPage.firstConsequentialKillObserved
                    && !consumedWithoutPage.firstConsequentialKillEventRecorded,
                "Scribe collapsed the observed-without-page milestone distinction.");
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
                        observationAvailable = true,
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
                    && royalty.observationAvailable
                    && royalty.titleObservations.Count == 1,
                "current Royalty per-pawn observation did not survive Scribe.");
            RoyalTitleObservationState title = royalty.titleObservations[0];
            Require(title.factionId == "Faction_12"
                    && title.factionName == "Empire"
                    && title.titleDefName == "Knight"
                    && title.titleLabel == "Knight"
                    && title.seniority == 4
                    && title.lastObservedTick == 900,
                "the nested faction/title/label/seniority/tick observation fields changed across Scribe.");
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

        /// <summary>
        /// The component's actual ExposeRoyaltyData wiring distinguishes missing from initialized-empty
        /// persona state; this is not a mirror of the production keys.
        /// </summary>
        [Test]
        public static void ComponentPersonaMarkerRoundTripsInitializedEmptyAndLegacyMissing()
        {
            Require(ExposeRoyaltyDataMethod != null
                    && PersonaObservationVersionField != null
                    && PersonaBondsField != null,
                "Could not resolve the component's actual Royalty Scribe wiring.");
            int originalVersion = (int)PersonaObservationVersionField.GetValue(scope.Component);
            List<PersonaBondState> originalBonds =
                PersonaBondsField.GetValue(scope.Component) as List<PersonaBondState>;
            royaltyScribeTarget = scope.Component;
            try
            {
                PersonaObservationVersionField.SetValue(
                    scope.Component, RoyaltyStatePersistence.CurrentPersonaObservationVersion);
                PersonaBondsField.SetValue(scope.Component, new List<PersonaBondState>());
                ScribeRoundTripWithAfterSave(
                    new ActualRoyaltyComponentStateAdapter(),
                    () =>
                    {
                        PersonaObservationVersionField.SetValue(scope.Component, 0);
                        PersonaBondsField.SetValue(scope.Component, null);
                    });
                List<PersonaBondState> initializedBonds =
                    PersonaBondsField.GetValue(scope.Component) as List<PersonaBondState>;
                Require((int)PersonaObservationVersionField.GetValue(scope.Component)
                            == RoyaltyStatePersistence.CurrentPersonaObservationVersion
                        && initializedBonds != null && initializedBonds.Count == 0,
                    "the actual component wiring lost its initialized-empty persona marker.");

                // This adapter deliberately writes no Royalty keys, then invokes the actual component
                // wiring during load/PostLoadInit to model a pre-Phase-2 save.
                ScribeRoundTripWithAfterSave(
                    new LegacyMissingRoyaltyComponentStateAdapter(),
                    () =>
                    {
                        PersonaObservationVersionField.SetValue(scope.Component, -1);
                        PersonaBondsField.SetValue(scope.Component, null);
                    });
                List<PersonaBondState> legacyBonds =
                    PersonaBondsField.GetValue(scope.Component) as List<PersonaBondState>;
                Require((int)PersonaObservationVersionField.GetValue(scope.Component) == 0
                        && legacyBonds != null && legacyBonds.Count == 0,
                    "missing old-save persona keys did not normalize through actual component wiring.");
            }
            finally
            {
                PersonaObservationVersionField.SetValue(scope.Component, originalVersion);
                PersonaBondsField.SetValue(scope.Component, originalBonds);
                royaltyScribeTarget = null;
            }
        }

        /// <summary>
        /// Completed ritual ownership is deliberately transient: it may wait briefly for a ritual in
        /// one live session, but FinalizeInit/load reset must discard it instead of inventing a stale
        /// post-load fallback page. Persisted faction observations are covered by the round-trip above.
        /// </summary>
        [Test]
        public static void PendingRoyalMutationOwnershipResetsSafelyAcrossLoadBoundary()
        {
            RoyalMutationCorrelation.Clear();
            RoyalMutationBatchSnapshot batch = RoyalMutationCorrelation.Open(
                "Pawn_LoadBoundary",
                "Load Boundary",
                string.Empty,
                RoyalMutationCauseTokens.AnimaLinking,
                100,
                null,
                0,
                16);
            bool completed = RoyalMutationCorrelation.Complete(
                batch,
                null,
                new RoyalPsychicMutationSnapshot
                {
                    pawnId = "Pawn_LoadBoundary",
                    previousPsylinkLevel = 0,
                    newPsylinkLevel = 1,
                    causeToken = RoyalMutationCauseTokens.AnimaLinking,
                    tick = 100,
                    correlationId = batch?.scope?.correlationId
                },
                64);
            Require(completed && RoyalMutationCorrelation.PendingCountForTests == 1,
                "the ritual mutation fixture did not enter the bounded pending queue.");

            // Exercise the real GameComponent lifecycle boundary, not only the underlying reset helper.
            // No transient owner is serialized; only normalized per-pawn observation truth survives.
            scope.Component.FinalizeInit();
            Require(RoyalMutationCorrelation.ActiveCountForTests == 0
                    && RoyalMutationCorrelation.PendingCountForTests == 0
                    && RoyalTitleThoughtCorrelation.PendingCountForTests == 0,
                "Royalty transient ownership survived the load-boundary reset.");
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

        /// <summary>
        /// A Royalty-authored save loaded without the DLC uses the narrow LoadedGame lifecycle seam to
        /// invalidate availability before a paused player can immediately resave without advancing a tick.
        /// </summary>
        [Test]
        public static void RoyaltyOffLoadedGameInvalidatesObservationBeforeFirstTick()
        {
            if (ModsConfig.RoyaltyActive)
            {
                Log.Message("[Pawn Diary RimTest] SKIP RoyaltyOffLoadedGameInvalidatesObservationBeforeFirstTick: Royalty is active.");
                return;
            }
            Require(FindDiaryMethod != null, "Could not resolve the private FindDiary test seam.");
            PawnDiaryRecord diary = FindDiaryMethod.Invoke(
                scope.Component, new object[] { pawn, true }) as PawnDiaryRecord;
            Require(diary != null, "Could not seed a Royalty observation on the disposable pawn.");
            RoyaltyPawnProgressionState royalty = diary.EnsureProgressionState().EnsureRoyaltyState();
            royalty.observationVersion = RoyaltyStatePersistence.CurrentObservationVersion;
            royalty.observationAvailable = true;
            royalty.titleObservations = new List<RoyalTitleObservationState>
            {
                new RoyalTitleObservationState
                {
                    factionId = "Faction_FormerEmpire",
                    titleDefName = "FormerTitle",
                    titleLabel = "Former title",
                    lastObservedTick = 100
                }
            };

            // Calling the whole LoadedGame lifecycle on the developer's live component would clear
            // unrelated transient queues. Exercise the exact narrow seam that LoadedGame invokes.
            scope.Component.MarkRoyaltyObservationUnavailable();
            Require(!royalty.observationAvailable && royalty.titleObservations.Count == 1,
                "The load-time seam did not invalidate availability while preserving saved title truth.");
        }

        private sealed class ActualRoyaltyComponentStateAdapter : IExposable
        {
            public void ExposeData()
            {
                InvokeActualRoyaltyComponentExposeData();
            }
        }

        private sealed class LegacyMissingRoyaltyComponentStateAdapter : IExposable
        {
            public void ExposeData()
            {
                if (Scribe.mode != LoadSaveMode.Saving)
                    InvokeActualRoyaltyComponentExposeData();
            }
        }

        private static void InvokeActualRoyaltyComponentExposeData()
        {
            Require(royaltyScribeTarget != null && ExposeRoyaltyDataMethod != null,
                "The actual Royalty component Scribe adapter has no target.");
            ExposeRoyaltyDataMethod.Invoke(royaltyScribeTarget, null);
        }

        private static T ScribeRoundTripWithAfterSave<T>(T original, Action afterSave)
            where T : class, IExposable
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pawndiary_royalty_component_" + Guid.NewGuid().ToString("N") + ".xml");
            T loaded = null;
            try
            {
                Scribe.saver.InitSaving(path, "root");
                T saveRef = original;
                Scribe_Deep.Look(ref saveRef, "obj");
                Scribe.saver.FinalizeSaving();
                afterSave?.Invoke();

                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "obj");
                Scribe.loader.FinalizeLoading();
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
                try
                {
                    if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                }
                catch
                {
                    // Best-effort temp cleanup must not hide the actual Scribe assertion.
                }
            }
            Require(loaded != null, "The component Royalty Scribe adapter loaded null.");
            return loaded;
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

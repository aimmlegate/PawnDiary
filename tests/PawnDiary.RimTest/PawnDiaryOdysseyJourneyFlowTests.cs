// In-game O1.2 fixtures for Odyssey's XML policy, guarded live location/mobile-home adapter, and
// additive journey/history Scribe contract. O1.2 owns no takeoff/landing hooks and emits no pages, so
// this suite intentionally stops at state/context boundaries; O1.3 extends it with lifecycle tests.
using System;
using System.Collections.Generic;
using System.Linq;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Proves O1.2 policy loading, no-DLC safety, context scope, and save normalization.</summary>
    [TestSuite]
    public static class PawnDiaryOdysseyJourneyFlowTests
    {
        private const string LogPrefix = "[PawnDiary RimTest Odyssey O1.2] ";

        /// <summary>The shipped singleton projects exact mappings, frozen names, and bounded policy.</summary>
        [Test]
        public static void LoadedPolicyProjectsFrozenNamesMappingsAndCaps()
        {
            DiaryOdysseyPolicyDef def = DefDatabase<DiaryOdysseyPolicyDef>.GetNamedSilentFail(
                DiaryOdysseyPolicy.DefName);
            Require(def != null, "Diary_Odyssey policy Def was not loaded.");
            List<string> errors = def.ConfigErrors().ToList();
            Require(errors.Count == 0,
                "Diary_Odyssey ConfigErrors: " + string.Join(" | ", errors.ToArray()));

            OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
            AssertStr("Ludeon.RimWorld.Odyssey", policy.packageId, "Odyssey package id");
            AssertStr("ritualGravship", policy.launchGroupKey, "launch group key");
            AssertStr("odysseyGravshipLanding", policy.landingGroupKey, "landing group key");
            Require(policy.maximumLaunchWriters >= 1 && policy.maximumLaunchWriters <= 2
                    && policy.maximumLandingWriters >= 1 && policy.maximumLandingWriters <= 2,
                "Odyssey writer caps escaped the one/two-writer boundary.");
            Require(policy.biomeCategories.Count >= 19 && policy.siteCategories.Count >= 20,
                "The loaded Odyssey policy lost its exact biome/site mapping catalog.");
            Require(policy.reasonRules.Count == 6,
                "The loaded Odyssey policy must project all six frozen landing reasons.");

            OdysseyLocationSnapshot classified = OdysseyLocationPolicy.Classify(
                new OdysseyLocationSnapshot
                {
                    visible = true,
                    layerToken = OdysseyLocationLayerTokens.Orbit,
                    biomeDefName = "Orbit",
                    siteDefName = "OrbitalMechhive"
                },
                policy);
            AssertStr("mechhive", classified.categoryToken, "exact Mechhive category");
            Require(classified.majorDestination,
                "OrbitalMechhive must remain an XML-authored major destination.");
        }

        /// <summary>
        /// The live adapter cleanly no-ops without Odyssey; with Odyssey it copies the disposable
        /// pawn's exact map and reports mobile-home context iff vanilla says that pawn is onboard.
        /// </summary>
        [Test]
        public static void GuardedLiveLocationAndMobileHomeContextMatchVanilla()
        {
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            Pawn pawn = scope.CreateAdultColonist();
            try
            {
                scope.SpawnAsLiveColonist(pawn);
                OdysseyLocationSnapshot location;
                bool captured = DlcContext.TryCaptureOdysseyLocation(pawn, out location);
                OdysseyMobileHomeSnapshot mobileHome;
                bool mobileCaptured = DlcContext.TryCaptureOdysseyMobileHome(pawn, out mobileHome);

                if (!ModsConfig.OdysseyActive)
                {
                    Require(!captured && location == null && !mobileCaptured && mobileHome == null,
                        "Odyssey-inactive live context must return false/null before touching DLC state.");
                    Log.Message(LogPrefix + "Odyssey inactive: guarded live adapters returned no state.");
                    return;
                }

                Require(captured && location != null,
                    "Odyssey-active spawned pawn did not produce a detached map location.");
                AssertStr(pawn.Map.Biome?.defName ?? string.Empty, location.biomeDefName,
                    "captured biome Def name");
                Require(location.layerToken == OdysseyLocationLayerTokens.Surface
                        || location.layerToken == OdysseyLocationLayerTokens.Orbit
                        || location.layerToken == OdysseyLocationLayerTokens.Unknown,
                    "Live location emitted an unknown layer token.");

                Building_GravEngine engine = GravshipUtility.GetPlayerGravEngine_NewTemp(pawn.Map);
                bool vanillaOnboard = engine != null
                    && GravshipUtility.IsOnboardGravship_NewTemp(
                        pawn.Position, engine, pawn, desperate: false, respectAllowedAreas: false);
                Require(mobileCaptured == vanillaOnboard,
                    "Mobile-home capture disagreed with vanilla's exact pawn-on-gravship predicate.");

                string surroundings = DiaryContextBuilder.BuildSurroundingsSummary(pawn);
                if (mobileCaptured)
                {
                    Require(mobileHome != null && !string.IsNullOrWhiteSpace(mobileHome.shipStableId)
                            && !string.IsNullOrWhiteSpace(mobileHome.shipName),
                        "An onboard pawn lost detached ship identity/name.");
                    Require(surroundings.IndexOf(mobileHome.shipName, StringComparison.OrdinalIgnoreCase) >= 0,
                        "An onboard pawn's surroundings omitted the visible gravship name.");
                    Require(surroundings.IndexOf(mobileHome.shipStableId, StringComparison.Ordinal) < 0
                            && surroundings.IndexOf(location.stableKey, StringComparison.Ordinal) < 0,
                        "Mobile-home surroundings leaked a stable ship/location identifier.");
                }
                else
                {
                    string marker = "PawnDiary.Ctx.GravshipHome".Translate("RIMTEST_SHIP").ToString();
                    string prefix = marker.Replace("RIMTEST_SHIP", string.Empty).Trim();
                    Require(prefix.Length == 0
                            || surroundings.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) < 0,
                        "A pawn outside the grav field received mobile-home context.");
                }
            }
            finally
            {
                scope.TearDown();
            }
        }

        /// <summary>Both additive component keys and every detached nested value survive real Scribe.</summary>
        [Test]
        public static void JourneyAndHistoryRoundTripUnderFrozenComponentKeys()
        {
            OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
            OdysseyComponentStateMirror original = new OdysseyComponentStateMirror
            {
                activeJourney = ValidJourney(),
                travelHistory = new OdysseyTravelHistoryState
                {
                    historyInitialized = true,
                    historyTrustworthyForFirstClaims = true,
                    featureStartTick = 100,
                    committedJourneyCount = 2,
                    lastDepartureTick = 500,
                    visitedLayerKeys = new List<string> { "surface", "orbit" },
                    visitedCategoryKeys = new List<string> { "forest" },
                    visitedLocationKeys = new List<string> { "planet-layer-0-tile-10" },
                    homeKeys = new List<string> { "planet-layer-0-tile-10" },
                    emittedJourneyIds = new List<string> { "odyssey-journey|WorldObject_7|500" }
                }
            };

            OdysseyComponentStateMirror loaded = ScribeRoundTrip(original);
            Require(loaded.activeJourney != null && loaded.travelHistory != null,
                "Odyssey component state did not survive real Scribe.");
            AssertStr("odyssey-journey|WorldObject_7|500", loaded.activeJourney.journeyId,
                "active journey id");
            AssertStr("Scarlands", loaded.activeJourney.destination.biomeDefName,
                "destination biome Def name");
            AssertInt(2, loaded.activeJourney.writers.Count, "bounded saved writers");
            AssertInt(2, loaded.travelHistory.visitedLayerKeys.Count, "visited layers");
            Require(loaded.travelHistory.historyTrustworthyForFirstClaims,
                "Trustworthy post-feature history lost its trust flag on load.");
            Require(policy.maximumEmittedJourneyIds > 0,
                "Loaded policy exposed an unusable emitted-journey cap.");
        }

        /// <summary>Missing pre-O1.2 keys normalize safely and baseline without false novelty claims.</summary>
        [Test]
        public static void MissingKeysBecomeSilentUntrustedOldSaveBaseline()
        {
            OdysseyComponentStateMirror loaded = ScribeRoundTrip(new OdysseyComponentStateMirror
            {
                omitOdysseyKeysOnSave = true
            });
            Require(loaded.activeJourney == null && loaded.travelHistory != null,
                "Missing Odyssey component keys did not normalize to null journey/non-null history.");
            Require(!loaded.travelHistory.historyInitialized,
                "PostLoad normalization alone must leave missing history ready for LoadedGame baseline.");

            OdysseyTravelHistorySnapshot baseline = OdysseyHistoryPolicy.BaselineOldSave(
                loaded.travelHistory.ToSnapshot(),
                new OdysseyLocationSnapshot
                {
                    stableKey = "planet-layer-0-tile-12",
                    layerToken = OdysseyLocationLayerTokens.Surface,
                    biomeDefName = "TemperateForest",
                    isPlayerHome = true,
                    visible = true
                },
                900,
                DiaryOdysseyPolicy.Snapshot());
            Require(baseline.historyInitialized && !baseline.historyTrustworthyForFirstClaims,
                "Old-save baseline authorized first/new novelty claims.");
            AssertInt(0, baseline.committedJourneyCount,
                "Old-save baseline invented a committed journey");
            Require(baseline.emittedJourneyIds.Count == 0,
                "Old-save baseline invented an emitted landing page.");
        }

        /// <summary>Corrupt journey identity drops and oversized history retains only newest bounded rows.</summary>
        [Test]
        public static void CorruptJourneyDropsAndOversizedHistoryIsBounded()
        {
            OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
            int overflow = policy.maximumVisitedLocations + 17;
            OdysseyTravelHistoryState history = new OdysseyTravelHistoryState
            {
                historyInitialized = true,
                visitedLocationKeys = new List<string>(overflow),
                visitedCategoryKeys = null,
                emittedJourneyIds = null
            };
            for (int i = 0; i < overflow; i++) history.visitedLocationKeys.Add("location-" + i);

            OdysseyComponentStateMirror loaded = ScribeRoundTrip(new OdysseyComponentStateMirror
            {
                activeJourney = new OdysseyJourneyState
                {
                    journeyId = string.Empty,
                    shipStableId = "WorldObject_Broken",
                    departureTick = -50
                },
                travelHistory = history
            });
            Require(loaded.activeJourney == null,
                "A corrupt blank-id active journey survived load normalization.");
            AssertInt(policy.maximumVisitedLocations, loaded.travelHistory.visitedLocationKeys.Count,
                "visited-location policy cap");
            AssertStr("location-" + (overflow - 1),
                loaded.travelHistory.visitedLocationKeys[loaded.travelHistory.visitedLocationKeys.Count - 1],
                "newest visited location");
            Require(!loaded.travelHistory.visitedLocationKeys.Contains("location-0"),
                "History truncation retained the oldest overflow row instead of the newest rows.");
            Require(loaded.travelHistory.visitedCategoryKeys != null
                    && loaded.travelHistory.emittedJourneyIds != null,
                "Null/corrupt history lists were not repaired.");
        }

        private static OdysseyJourneyState ValidJourney()
        {
            return new OdysseyJourneyState
            {
                journeyId = "odyssey-journey|WorldObject_7|500",
                shipStableId = "WorldObject_7",
                shipName = "Wayfarer",
                departureTick = 500,
                sourceComplete = true,
                origin = new OdysseyLocationState
                {
                    stableKey = "planet-layer-0-tile-10",
                    layerToken = OdysseyLocationLayerTokens.Surface,
                    biomeDefName = "TemperateForest",
                    visibleLabel = "Temperate forest"
                },
                destination = new OdysseyLocationState
                {
                    stableKey = "planet-layer-0-tile-20",
                    layerToken = OdysseyLocationLayerTokens.Surface,
                    biomeDefName = "Scarlands",
                    visibleLabel = "Scarlands"
                },
                writers = new List<OdysseyWriterState>
                {
                    new OdysseyWriterState
                    {
                        pawnId = "Pawn_Pilot",
                        displayName = "Pilot",
                        roleToken = OdysseyJourneyRoleTokens.Pilot,
                        eligible = true,
                        present = true,
                        hasDiary = true
                    },
                    new OdysseyWriterState
                    {
                        pawnId = "Pawn_Copilot",
                        displayName = "Copilot",
                        roleToken = OdysseyJourneyRoleTokens.Copilot,
                        eligible = true,
                        present = true,
                        hasDiary = true
                    }
                }
            };
        }

        /// <summary>Mirrors the component's two exact O1.2 Look calls without constructing another component.</summary>
        private sealed class OdysseyComponentStateMirror : IExposable
        {
            public OdysseyJourneyState activeJourney;
            public OdysseyTravelHistoryState travelHistory;
            public bool omitOdysseyKeysOnSave;

            public void ExposeData()
            {
                if (!(Scribe.mode == LoadSaveMode.Saving && omitOdysseyKeysOnSave))
                {
                    Scribe_Deep.Look(ref activeJourney, OdysseySaveKeys.ActiveJourney);
                    Scribe_Deep.Look(ref travelHistory, OdysseySaveKeys.TravelHistory);
                }
                else
                {
                    return;
                }

                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
                    activeJourney = OdysseyStatePersistence.NormalizeJourney(activeJourney, policy);
                    travelHistory = OdysseyStatePersistence.NormalizeHistory(travelHistory, policy);
                }
            }
        }

        private static T ScribeRoundTrip<T>(T toSave) where T : class, IExposable
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pawndiary_odyssey_rimtest_" + Guid.NewGuid().ToString("N") + ".xml");
            T loaded = null;
            try
            {
                Scribe.saver.InitSaving(path, "root");
                T saveRef = toSave;
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
                try
                {
                    if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                }
                catch
                {
                    // Best-effort cleanup: a locked temporary fixture must not fail the test itself.
                }
            }

            if (loaded == null) throw new AssertionException("Odyssey Scribe round-trip returned null.");
            return loaded;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }

        private static void AssertStr(string expected, string actual, string label)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new AssertionException(label + ": expected '" + expected + "', got '" + actual + "'.");
        }

        private static void AssertInt(int expected, int actual, string label)
        {
            if (expected != actual)
                throw new AssertionException(label + ": expected " + expected + ", got " + actual + ".");
        }
    }
}

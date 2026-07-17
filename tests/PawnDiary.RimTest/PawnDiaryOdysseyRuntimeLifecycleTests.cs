// High-value in-game Odyssey hardening fixtures. These tests enter the real vanilla lifecycle
// methods so Pawn Diary's installed Harmony patches receive actual RimWorld objects. The two visual
// cutscene starters are suppressed only after Pawn Diary's prefixes run; TravelTo and LandingEnded
// execute their real vanilla bodies.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimTestRedux;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Exercises real Odyssey Harmony payloads and provides a deterministic three-phase process-boundary
    /// save fixture. See tests/SAVE_COMPATIBILITY_SMOKETEST.md for the Phase A/B/C continuation.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryOdysseyRuntimeLifecycleTests
    {
        private const string LogPrefix = "[PawnDiary RimTest Odyssey runtime] ";
        private const string SuppressionHarmonyId = "aimml.pawndiary.rimtest.odyssey.suppress-visuals";
        private const string FixtureShipName = "Pawn Diary RimTest Odyssey Wayfarer";
        private const string FixtureHomeKey = "pawndiary-rimtest-odyssey-home";
        private const int FixtureHomeSinceTick = 1234;
        private const int FixtureLaunchPageTick = 2345;
        private const string PhaseASaveName = "PawnDiary_Odyssey_RimTest_PhaseA_Active";
        private const string PhaseBSaveName = "PawnDiary_Odyssey_RimTest_PhaseB_Completed";

        private static readonly FieldInfo ActiveJourneyField = ComponentField("odysseyActiveJourney");
        private static readonly FieldInfo TravelHistoryField = ComponentField("odysseyTravelHistory");
        private static readonly FieldInfo TakeoffIntentField = ComponentField("odysseyTakeoffIntent");
        private static readonly FieldInfo PendingLandingField = ComponentField("odysseyPendingLanding");
        private static readonly FieldInfo EventsField = ComponentField("events");
        private static readonly FieldInfo DiariesField = ComponentField("diaries");
        private static readonly FieldInfo DiariesByIdField = ComponentField("diariesById");
        private static readonly FieldInfo ArchiveField = ComponentField("archive");
        private static readonly FieldInfo ControllerGravshipField = ControllerField("gravship");
        private static readonly FieldInfo ControllerMapField = ControllerField("map");
        private static readonly FieldInfo ControllerLandingMarkerField = ControllerField("landingMarker");
        private static readonly FieldInfo ControllerTerrainCaptureField = ControllerField("terrainCapture");
        private static readonly FieldInfo ControllerIndoorMaskField = ControllerField("terrainCurtainIndoorMask");
        private static readonly FieldInfo ControllerShadowMaskField = ControllerField("terrainCurtainShadowMask");
        private static readonly FieldInfo ControllerMoveDesignatorField = ControllerField("moveDesignator");
        private static readonly FieldInfo ValidSubstructureField = AccessTools.Field(
            typeof(Building_GravEngine), "validSubstructure");
        private static readonly FieldInfo GravshipEngineField = AccessTools.Field(typeof(Gravship), "engine");
        private static readonly FieldInfo GravshipPawnsField = AccessTools.Field(typeof(Gravship), "pawns");
        private static readonly FieldInfo GravshipThingsField = AccessTools.Field(typeof(Gravship), "things");
        private static readonly FieldInfo GravshipStoryStateField = AccessTools.Field(typeof(Gravship), "storyState");
        private static readonly FieldInfo GravshipTravelPctField = AccessTools.Field(typeof(Gravship), "traveledPct");
        private static readonly MethodInfo LandingEndedMethod = AccessTools.DeclaredMethod(
            typeof(WorldComponent_GravshipController), "LandingEnded", Type.EmptyTypes);

        /// <summary>
        /// The real public takeoff entry reaches Pawn Diary's Harmony prefix, but an aborted fixture never
        /// calls TravelTo. Therefore it can create only transient intent—not a journey, history commit, or page.
        /// </summary>
        [Test]
        public static void RealInitiateTakeoffCancellationLeavesNoSavedJourneyOrPage()
        {
            if (!RequireOdysseyOrSkip("takeoff-cancellation")) return;
            Map map;
            PlanetTile destination;
            WorldComponent_GravshipController controller;
            if (!TryRuntimePreconditions(out map, out destination, out controller, "takeoff-cancellation")) return;

            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            OdysseyComponentSnapshot componentSnapshot = new OdysseyComponentSnapshot(scope.Component);
            Harmony suppressor = null;
            Building_GravEngine engine = null;
            try
            {
                ResetOdysseyFixtureState(scope.Component);
                Pawn pilot = scope.CreateAdultColonist();
                scope.SpawnAsLiveColonist(pilot);
                IntVec3 engineCell = FindEmptyEngineCell(map);
                engine = SpawnEngine(map, engineCell, pilot, roughLanding: false);
                suppressor = InstallVisualSuppressor(includeLandingFinish: false);

                int eventsBefore = EventCount(scope.Component);
                controller.InitiateTakeoff(engine, destination);

                OdysseyTakeoffIntent intent = TakeoffIntentField.GetValue(scope.Component)
                    as OdysseyTakeoffIntent;
                Require(intent != null && intent.engineId == engine.GetUniqueLoadID(),
                    "The real InitiateTakeoff call did not deliver its engine to Pawn Diary's prefix.");
                Require(intent.origin != null && intent.origin.stableKey == LocationKey(map.Tile),
                    "Takeoff intent did not preserve the live origin map.");
                Require(ActiveJourneyField.GetValue(scope.Component) == null,
                    "InitiateTakeoff committed a saved journey before TravelTo.");
                AssertInt(0, History(scope.Component).committedJourneyCount,
                    "cancelled takeoff committed-journey count");
                AssertInt(eventsBefore, EventCount(scope.Component),
                    "cancelled takeoff event count");
            }
            finally
            {
                suppressor?.UnpatchAll(SuppressionHarmonyId);
                DestroyThing(engine);
                componentSnapshot.Restore(scope.Component);
                scope.TearDown();
            }
        }

        /// <summary>
        /// Runs real TravelTo and real LandingEnded around the safely suppressed visual starters. It proves
        /// cross-layer origin preservation, one canonical rough-landing event, and callback replay rejection.
        /// </summary>
        [Test]
        public static void RealHarmonyLifecycleTravelsAndLandsExactlyOnce()
        {
            if (!RequireOdysseyOrSkip("full-lifecycle")) return;
            Map map;
            PlanetTile destination;
            WorldComponent_GravshipController controller;
            if (!TryRuntimePreconditions(out map, out destination, out controller, "full-lifecycle")) return;

            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin(OdysseyGroupDefNames.Landing);
            OdysseyComponentSnapshot componentSnapshot = new OdysseyComponentSnapshot(scope.Component);
            ControllerSnapshot controllerSnapshot = new ControllerSnapshot(controller);
            Harmony suppressor = null;
            Building_GravEngine engine = null;
            Gravship gravship = null;
            TimeSpeed oldSpeed = Find.TickManager.CurTimeSpeed;
            bool oldScreenshotMode = Find.ScreenshotModeHandler.Active;
            Building_GravEngine oldMaskEngine = SectionLayer_GravshipMask.Engine;
            try
            {
                ResetOdysseyFixtureState(scope.Component);
                Pawn pilot = scope.CreateAdultColonist();
                scope.SpawnAsLiveColonist(pilot);
                IntVec3 pilotCell = pilot.Position;
                IntVec3 engineCell = FindEmptyEngineCell(map);
                engine = SpawnEngine(map, engineCell, pilot, roughLanding: true);
                suppressor = InstallVisualSuppressor(includeLandingFinish: false);

                PlanetTile origin = map.Tile;
                int eventsBefore = EventCount(scope.Component);
                controller.InitiateTakeoff(engine, destination);
                Require(TakeoffIntentField.GetValue(scope.Component) is OdysseyTakeoffIntent,
                    "The real InitiateTakeoff prefix did not create transient intent.");

                gravship = MakeMinimalGravship(engine, pilot);
                Current.Game.Gravship = gravship;
                pilot.DeSpawn(DestroyMode.WillReplace);
                engine.DeSpawn(DestroyMode.WillReplace);

                // This is the real vanilla body. For a cross-layer trip it rewrites its local oldTile
                // onto the orbit layer before setting Gravship.Tile; Pawn Diary's Harmony __state must
                // still carry the original surface tile to its postfix.
                GravshipUtility.TravelTo(gravship, origin, destination);

                OdysseyJourneyState active = ActiveJourneyField.GetValue(scope.Component)
                    as OdysseyJourneyState;
                Require(active != null && active.sourceComplete,
                    "Real TravelTo did not commit the takeoff-correlated active journey.");
                AssertStr(LocationKey(origin), active.origin?.stableKey,
                    "pre-travel surface origin after vanilla mutation/despawn");
                Require(gravship.Tile.Layer == destination.Layer,
                    "Vanilla TravelTo did not mutate the world object's current layer as expected.");
                AssertInt(eventsBefore, EventCount(scope.Component), "TravelTo event count");
                string journeyId = active.journeyId;

                RemoveWorldObject(gravship);
                GenSpawn.Spawn(engine, engineCell, map, Rot4.North);
                GenSpawn.Spawn(pilot, pilotCell, map);
                ConfigureValidSubstructure(engine, pilot);
                engine.launchInfo.doNegativeOutcome = false; // saved rough truth remains; avoid random damage.

                controller.InitiateLanding(gravship, map, engineCell, Rot4.North);
                Require(PendingLandingField.GetValue(scope.Component) is OdysseyPendingLanding,
                    "The real InitiateLanding prefix did not capture pending destination state.");
                AssertInt(eventsBefore, EventCount(scope.Component), "InitiateLanding event count");

                ControllerGravshipField.SetValue(controller, gravship);
                ControllerMapField.SetValue(controller, map);
                Current.Game.Gravship = gravship;
                DiaryEvent landingEvent = scope.FireAndRequireEvent(
                    () => LandingEndedMethod.Invoke(controller, null),
                    OdysseyEventDefNames.Landing,
                    pilot,
                    null);
                Require(landingEvent.solo
                        && landingEvent.gameContext.Contains("journey_reason=rough_landing"),
                    "Real LandingEnded did not create the one expected rough-landing solo event.");

                OdysseyTravelHistoryState landedHistory = History(scope.Component);
                AssertInt(1, landedHistory.emittedJourneyIds.Count(id => id == journeyId),
                    "canonical landing marker count");
                Require(ActiveJourneyField.GetValue(scope.Component) == null
                        && PendingLandingField.GetValue(scope.Component) == null,
                    "Successful real LandingEnded left active or pending lifecycle state.");

                int markerCount = landedHistory.emittedJourneyIds.Count;
                int eventCount = EventCount(scope.Component);
                ControllerGravshipField.SetValue(controller, gravship);
                ControllerMapField.SetValue(controller, map);
                suppressor.UnpatchAll(SuppressionHarmonyId);
                suppressor = InstallVisualSuppressor(includeLandingFinish: true);

                // A duplicate callback still enters Pawn Diary's installed prefix/postfix, but the test's
                // last-priority prefix suppresses the now-invalid second vanilla body. No active journey
                // means Pawn Diary must produce no Harmony __state and therefore no second output.
                LandingEndedMethod.Invoke(controller, null);
                AssertInt(eventCount, EventCount(scope.Component), "duplicate LandingEnded event count");
                AssertInt(markerCount, History(scope.Component).emittedJourneyIds.Count,
                    "duplicate LandingEnded marker count");
            }
            finally
            {
                suppressor?.UnpatchAll(SuppressionHarmonyId);
                RemoveWorldObject(gravship);
                Current.Game.Gravship = null;
                DestroyThing(engine);
                SectionLayer_GravshipMask.Engine = oldMaskEngine;
                Find.TickManager.CurTimeSpeed = oldSpeed;
                Find.ScreenshotModeHandler.Active = oldScreenshotMode;
                controllerSnapshot.Restore(controller);
                componentSnapshot.Restore(scope.Component);
                scope.TearDown();
            }
        }

        /// <summary>
        /// Phase A creates a disposable real save while an actual TravelTo-committed journey is active.
        /// The current colony is restored after the save is written; only the named fixture file remains.
        /// </summary>
        [Test]
        public static void SaveReloadPhaseAWriteActiveJourneySave()
        {
            if (!RequireOdysseyOrSkip("save Phase A")) return;
            Map map;
            PlanetTile destination;
            WorldComponent_GravshipController controller;
            if (!TryRuntimePreconditions(out map, out destination, out controller, "save Phase A")) return;

            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin(OdysseyGroupDefNames.Landing);
            OdysseyComponentSnapshot componentSnapshot = new OdysseyComponentSnapshot(scope.Component);
            ControllerSnapshot controllerSnapshot = new ControllerSnapshot(controller);
            Harmony suppressor = null;
            Building_GravEngine engine = null;
            Gravship gravship = null;
            try
            {
                ResetOdysseyFixtureState(scope.Component);
                Pawn pilot = scope.CreateAdultColonist();
                scope.SpawnAsLiveColonist(pilot);
                IntVec3 engineCell = FindEmptyEngineCell(map);
                engine = SpawnEngine(map, engineCell, pilot, roughLanding: true);
                suppressor = InstallVisualSuppressor(includeLandingFinish: false);

                controller.InitiateTakeoff(engine, destination);
                gravship = MakeMinimalGravship(engine, pilot);
                Current.Game.Gravship = gravship;
                pilot.DeSpawn(DestroyMode.WillReplace);
                engine.DeSpawn(DestroyMode.WillReplace);
                GravshipUtility.TravelTo(gravship, map.Tile, destination);

                OdysseyJourneyState active = ActiveJourneyField.GetValue(scope.Component)
                    as OdysseyJourneyState;
                Require(active != null && active.shipName == FixtureShipName,
                    "Phase A failed to establish its real TravelTo active journey marker.");
                GravshipTravelPctField.SetValue(gravship, -1000f); // Prevent auto-arrival before Phase B.

                OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
                OdysseyTravelHistoryState history = History(scope.Component);
                history.historyInitialized = true;
                history.historyTrustworthyForFirstClaims = true;
                history.currentHomeKey = FixtureHomeKey;
                history.currentHomeSinceTick = FixtureHomeSinceTick;
                history.lastLaunchPageTick = FixtureLaunchPageTick;
                history.visitedLocationKeys = new List<string>();
                for (int i = 0; i < policy.maximumVisitedLocations + 3; i++)
                    history.visitedLocationKeys.Add("rimtest-save-location-" + i);

                // Pending landing is deliberately transient. Saving after the real prefix proves the
                // two frozen component keys restore the journey/history while this row disappears.
                controller.InitiateLanding(gravship, map, map.Center, Rot4.North);
                Require(PendingLandingField.GetValue(scope.Component) is OdysseyPendingLanding,
                    "Phase A could not establish pre-save transient pending landing state.");

                DeleteFixtureSave(PhaseASaveName);
                DeleteFixtureSave(PhaseBSaveName);
                GameDataSaveLoader.SaveGame(PhaseASaveName);
                Require(File.Exists(GenFilePaths.FilePathForSavedGame(PhaseASaveName)),
                    "RimWorld did not write the Phase A disposable save.");
                Log.Message(LogPrefix + "Phase A PASS: load save '" + PhaseASaveName
                    + "' and run SaveReloadPhaseBVerifyAndCompleteJourney.");
            }
            finally
            {
                suppressor?.UnpatchAll(SuppressionHarmonyId);
                RemoveWorldObject(gravship);
                Current.Game.Gravship = null;
                DestroyThing(engine);
                controllerSnapshot.Restore(controller);
                componentSnapshot.Restore(scope.Component);
                scope.TearDown();
            }
        }

        /// <summary>
        /// Phase B runs only in the Phase A save. It verifies the real process-boundary load, completes
        /// the journey through the Harmony landing seam, rejects replay, and writes the completed save.
        /// </summary>
        [Test]
        public static void SaveReloadPhaseBVerifyAndCompleteJourney()
        {
            if (!RequireOdysseyOrSkip("save Phase B")) return;
            DiaryGameComponent component = DiaryGameComponent.Instance;
            OdysseyJourneyState active = ActiveJourneyField.GetValue(component) as OdysseyJourneyState;
            if (active == null || active.shipName != FixtureShipName)
            {
                Log.Message(LogPrefix + "SKIP save Phase B: load '" + PhaseASaveName + "' first.");
                return;
            }

            Gravship gravship = Current.Game?.Gravship;
            Map map = Find.CurrentMap;
            WorldComponent_GravshipController controller = Find.GravshipController;
            Harmony suppressor = null;
            Building_GravEngine engine = gravship?.Engine;
            Pawn pilot = gravship?.Pawns?.FirstOrDefault();
            List<string> createdEventIds = new List<string>();
            Building_GravEngine oldMaskEngine = SectionLayer_GravshipMask.Engine;
            TimeSpeed oldSpeed = Find.TickManager.CurTimeSpeed;
            bool oldScreenshotMode = Find.ScreenshotModeHandler.Active;
            try
            {
                Require(gravship != null && engine != null && pilot != null && map != null && controller != null,
                    "Phase A save lost its live gravship/engine/pilot/map runtime fixture.");
                OdysseyPolicySnapshot policy = DiaryOdysseyPolicy.Snapshot();
                OdysseyTravelHistoryState history = History(component);
                Require(history.historyTrustworthyForFirstClaims,
                    "Phase B load lost the trustworthy-history flag.");
                AssertStr(FixtureHomeKey, history.currentHomeKey, "Phase B current home key");
                AssertInt(FixtureHomeSinceTick, history.currentHomeSinceTick,
                    "Phase B current home tenure");
                AssertInt(FixtureLaunchPageTick, history.lastLaunchPageTick,
                    "Phase B launch cooldown marker");
                AssertInt(policy.maximumVisitedLocations, history.visitedLocationKeys.Count,
                    "Phase B bounded visited-location history");
                AssertStr("rimtest-save-location-" + (policy.maximumVisitedLocations + 2),
                    history.visitedLocationKeys[history.visitedLocationKeys.Count - 1],
                    "Phase B newest bounded history row");
                Require(TakeoffIntentField.GetValue(component) == null
                        && PendingLandingField.GetValue(component) == null,
                    "Transient takeoff/pending landing state resurrected on the first reload.");

                string journeyId = active.journeyId;
                HashSet<string> before = EventIds(component);
                RemoveWorldObject(gravship);
                IntVec3 engineCell = FindEmptyEngineCell(map);
                IntVec3 pilotCell = FindEmptyPawnCell(map, engineCell);
                GenSpawn.Spawn(engine, engineCell, map, Rot4.North);
                GenSpawn.Spawn(pilot, pilotCell, map);
                ConfigureValidSubstructure(engine, pilot);
                engine.launchInfo.doNegativeOutcome = false;
                suppressor = InstallVisualSuppressor(includeLandingFinish: false);

                controller.InitiateLanding(gravship, map, engineCell, Rot4.North);
                ControllerGravshipField.SetValue(controller, gravship);
                ControllerMapField.SetValue(controller, map);
                Current.Game.Gravship = gravship;
                LandingEndedMethod.Invoke(controller, null);

                List<DiaryEvent> created = NewFixtureLandingEvents(component, before);
                AssertInt(1, created.Count, "Phase B canonical landing events");
                createdEventIds.Add(created[0].eventId);
                history = History(component);
                AssertInt(1, history.emittedJourneyIds.Count(id => id == journeyId),
                    "Phase B emitted journey marker");
                Require(ActiveJourneyField.GetValue(component) == null
                        && PendingLandingField.GetValue(component) == null,
                    "Phase B successful landing did not clear lifecycle state.");

                int eventsAfter = EventCount(component);
                int markersAfter = history.emittedJourneyIds.Count;
                ControllerGravshipField.SetValue(controller, gravship);
                ControllerMapField.SetValue(controller, map);
                suppressor.UnpatchAll(SuppressionHarmonyId);
                suppressor = InstallVisualSuppressor(includeLandingFinish: true);
                LandingEndedMethod.Invoke(controller, null);
                AssertInt(eventsAfter, EventCount(component), "Phase B replay event count");
                AssertInt(markersAfter, History(component).emittedJourneyIds.Count,
                    "Phase B replay marker count");

                DeleteFixtureSave(PhaseBSaveName);
                GameDataSaveLoader.SaveGame(PhaseBSaveName);
                Require(File.Exists(GenFilePaths.FilePathForSavedGame(PhaseBSaveName)),
                    "RimWorld did not write the completed Phase B save.");
                Log.Message(LogPrefix + "Phase B PASS: load save '" + PhaseBSaveName
                    + "' and run SaveReloadPhaseCVerifyNoResurrection.");
            }
            finally
            {
                suppressor?.UnpatchAll(SuppressionHarmonyId);
                RemoveWorldObject(gravship);
                Current.Game.Gravship = null;
                RemoveEventsAndDiaryRefs(component, createdEventIds);
                RemoveFixtureDiaryRecords(component, new[] { pilot?.GetUniqueLoadID() });
                DestroyThing(engine);
                DestroyThing(pilot);
                ActiveJourneyField.SetValue(component, null);
                TravelHistoryField.SetValue(component, new OdysseyTravelHistoryState());
                TakeoffIntentField.SetValue(component, null);
                PendingLandingField.SetValue(component, null);
                SectionLayer_GravshipMask.Engine = oldMaskEngine;
                Find.TickManager.CurTimeSpeed = oldSpeed;
                Find.ScreenshotModeHandler.Active = oldScreenshotMode;
                if (controller != null)
                {
                    ControllerGravshipField.SetValue(controller, null);
                    ControllerMapField.SetValue(controller, null);
                    ControllerLandingMarkerField.SetValue(controller, null);
                }
            }
        }

        /// <summary>
        /// Phase C is the second reload. It proves the completed save owns one durable page/marker and
        /// cannot resurrect transient lifecycle state or replay output, then removes fixture artifacts.
        /// </summary>
        [Test]
        public static void SaveReloadPhaseCVerifyNoResurrection()
        {
            if (!RequireOdysseyOrSkip("save Phase C")) return;
            DiaryGameComponent component = DiaryGameComponent.Instance;
            List<DiaryEvent> fixtureEvents = FixtureLandingEvents(component);
            if (fixtureEvents.Count == 0)
            {
                Log.Message(LogPrefix + "SKIP save Phase C: load '" + PhaseBSaveName + "' first.");
                return;
            }

            List<string> fixtureEventIds = fixtureEvents.Select(row => row.eventId).ToList();
            List<string> fixturePawnIds = fixtureEvents
                .SelectMany(row => new[] { row.initiatorPawnId, row.recipientPawnId })
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            try
            {
                AssertInt(1, fixtureEvents.Count, "Phase C persisted canonical landing events");
                Require(ActiveJourneyField.GetValue(component) == null
                        && TakeoffIntentField.GetValue(component) == null
                        && PendingLandingField.GetValue(component) == null,
                    "Second reload resurrected active/transient Odyssey lifecycle state.");
                OdysseyTravelHistoryState history = History(component);
                string journeyId = DiaryContextFields.Value(
                    fixtureEvents[0].gameContext, "journey_id");
                // journey_id intentionally does not render into gameContext. Use the one durable history
                // row and ensure it remains singular rather than guessing an event-time private ID.
                Require(string.IsNullOrEmpty(journeyId),
                    "Phase C fixture unexpectedly exposed its private journey ID in prompt context.");
                AssertInt(1, history.emittedJourneyIds.Count,
                    "Phase C durable emitted-marker count");
                int eventsBefore = EventCount(component);
                int markersBefore = history.emittedJourneyIds.Count;
                bool replayed = component.CompleteOdysseyLanding(new OdysseyPendingLanding
                {
                    shipStableId = "WorldObject_StaleReplay",
                    captureTick = Find.TickManager?.TicksGame ?? 0
                });
                Require(!replayed, "Second reload accepted a stale landing completion replay.");
                AssertInt(eventsBefore, EventCount(component), "Phase C stale replay event count");
                AssertInt(markersBefore, History(component).emittedJourneyIds.Count,
                    "Phase C stale replay marker count");
                Require(Current.Game?.Gravship == null,
                    "Completed Phase B save resurrected the travelling gravship world object.");
                Log.Message(LogPrefix + "Phase C PASS: exactly-once page/history survived two reloads.");
            }
            finally
            {
                RemoveEventsAndDiaryRefs(component, fixtureEventIds);
                RemoveFixtureDiaryRecords(component, fixturePawnIds);
                DestroyFixtureThings(fixturePawnIds);
                ActiveJourneyField.SetValue(component, null);
                TravelHistoryField.SetValue(component, new OdysseyTravelHistoryState());
                TakeoffIntentField.SetValue(component, null);
                PendingLandingField.SetValue(component, null);
                DeleteFixtureSave(PhaseASaveName);
                DeleteFixtureSave(PhaseBSaveName);
            }
        }

        private static bool RequireOdysseyOrSkip(string label)
        {
            if (ModsConfig.OdysseyActive) return true;
            Log.Message(LogPrefix + "SKIP " + label
                + ": ModsConfig.OdysseyActive is false; no Odyssey runtime object was touched.");
            return false;
        }

        private static bool TryRuntimePreconditions(
            out Map map,
            out PlanetTile destination,
            out WorldComponent_GravshipController controller,
            string label)
        {
            map = Find.CurrentMap;
            controller = Find.GravshipController;
            destination = PlanetTile.Invalid;
            if (map == null || controller == null || Find.WorldGrid?.Orbit == null)
            {
                Log.Message(LogPrefix + "SKIP " + label
                    + ": a loaded map and generated Odyssey orbit layer are required.");
                return false;
            }
            if (Current.Game?.Gravship != null || controller.IsGravshipTravelling
                || controller.LandingAreaConfirmationInProgress
                || WorldComponent_GravshipController.CutsceneInProgress)
            {
                Log.Message(LogPrefix + "SKIP " + label
                    + ": the loaded game already owns an active gravship/cutscene.");
                return false;
            }
            if (GravshipUtility.PlayerHasGravEngine(map))
            {
                Log.Message(LogPrefix + "SKIP " + label
                    + ": the current map already contains the player's grav engine; the fixture will not"
                    + " compete with a real parked ship.");
                return false;
            }

            destination = Find.WorldGrid.Orbit.GetClosestTile_NewTemp(map.Tile);
            if (!destination.Valid || destination.Layer == map.Tile.Layer)
            {
                Log.Message(LogPrefix + "SKIP " + label
                    + ": no valid cross-layer Odyssey destination could be resolved.");
                return false;
            }
            return true;
        }

        private static Harmony InstallVisualSuppressor(bool includeLandingFinish)
        {
            Harmony harmony = new Harmony(SuppressionHarmonyId);
            HarmonyMethod skip = new HarmonyMethod(
                typeof(PawnDiaryOdysseyRuntimeLifecycleTests), nameof(SkipOriginal));
            skip.priority = Priority.Last;
            harmony.Patch(AccessTools.DeclaredMethod(
                    typeof(WorldComponent_GravshipController),
                    nameof(WorldComponent_GravshipController.InitiateTakeoff),
                    new[] { typeof(Building_GravEngine), typeof(PlanetTile) }),
                prefix: skip);
            harmony.Patch(AccessTools.DeclaredMethod(
                    typeof(WorldComponent_GravshipController),
                    nameof(WorldComponent_GravshipController.InitiateLanding),
                    new[] { typeof(Gravship), typeof(Map), typeof(IntVec3), typeof(Rot4) }),
                prefix: skip);
            harmony.Patch(AccessTools.DeclaredMethod(
                    typeof(CameraJumper), nameof(CameraJumper.TryJump),
                    new[] { typeof(GlobalTargetInfo), typeof(CameraJumper.MovementMode) }),
                prefix: skip);
            harmony.Patch(AccessTools.DeclaredMethod(
                    typeof(Scenario), nameof(Scenario.PostGravshipLanded), new[] { typeof(Map) }),
                prefix: skip);
            if (includeLandingFinish) harmony.Patch(LandingEndedMethod, prefix: skip);
            return harmony;
        }

        private static bool SkipOriginal()
        {
            return false;
        }

        private static Building_GravEngine SpawnEngine(
            Map map,
            IntVec3 cell,
            Pawn pilot,
            bool roughLanding)
        {
            Building_GravEngine engine = ThingMaker.MakeThing(ThingDefOf.GravEngine)
                as Building_GravEngine;
            if (engine == null) throw new AssertionException("Could not create the loaded GravEngine Def.");
            engine.SetFactionDirect(Faction.OfPlayer);
            engine.RenamableLabel = FixtureShipName;
            engine.launchInfo = new LaunchInfo
            {
                pilot = pilot,
                quality = 1f,
                doNegativeOutcome = roughLanding
            };
            GenSpawn.Spawn(engine, cell, map, Rot4.North);
            ConfigureValidSubstructure(engine, pilot);
            return engine;
        }

        private static void ConfigureValidSubstructure(Building_GravEngine engine, Pawn pilot)
        {
            HashSet<IntVec3> cells = new HashSet<IntVec3>();
            foreach (IntVec3 cell in engine.OccupiedRect()) cells.Add(cell);
            if (pilot != null && pilot.Spawned) cells.Add(pilot.Position);
            ValidSubstructureField.SetValue(engine, cells);
            engine.substructureDirty = false;
        }

        private static Gravship MakeMinimalGravship(Building_GravEngine engine, Pawn pilot)
        {
            Gravship gravship = Activator.CreateInstance(typeof(Gravship), nonPublic: true) as Gravship;
            if (gravship == null) throw new AssertionException("Could not construct an isolated Gravship.");
            GravshipEngineField.SetValue(gravship, engine);
            GravshipPawnsField.SetValue(gravship, new Dictionary<Pawn, PositionData>
            {
                { pilot, new PositionData(IntVec3.Zero, Rot4.North) }
            });
            GravshipThingsField.SetValue(gravship, new Dictionary<Thing, PositionData>
            {
                { engine, new PositionData(IntVec3.Zero, Rot4.North) }
            });
            GravshipStoryStateField.SetValue(gravship, new StoryState(gravship));
            gravship.autoSlaughterConfigs = new List<AutoSlaughterConfig>();
            WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Gravship, gravship);
            return gravship;
        }

        private static IntVec3 FindEmptyEngineCell(Map map)
        {
            IntVec3 found;
            bool success = CellFinder.TryFindRandomCell(map, root =>
            {
                CellRect rect = GenAdj.OccupiedRect(root, Rot4.North, ThingDefOf.GravEngine.Size);
                if (!rect.InBounds(map)) return false;
                foreach (IntVec3 cell in rect)
                {
                    if (!cell.Standable(map) || cell.Fogged(map)
                        || map.thingGrid.ThingsListAtFast(cell).Count != 0)
                        return false;
                }
                return true;
            }, out found);
            if (!success) throw new AssertionException("No empty cells were available for the test GravEngine.");
            return found;
        }

        private static IntVec3 FindEmptyPawnCell(Map map, IntVec3 avoid)
        {
            IntVec3 found;
            if (!CellFinder.TryFindRandomCellNear(avoid, map, 20,
                    cell => cell.Standable(map) && !cell.Fogged(map)
                        && map.thingGrid.ThingsListAtFast(cell).Count == 0
                        && !GenAdj.OccupiedRect(avoid, Rot4.North, ThingDefOf.GravEngine.Size).Contains(cell),
                    out found))
                throw new AssertionException("No empty landing cell was available for the test pilot.");
            return found;
        }

        private static void ResetOdysseyFixtureState(DiaryGameComponent component)
        {
            ActiveJourneyField.SetValue(component, null);
            TakeoffIntentField.SetValue(component, null);
            PendingLandingField.SetValue(component, null);
            TravelHistoryField.SetValue(component, new OdysseyTravelHistoryState
            {
                historyInitialized = true,
                historyTrustworthyForFirstClaims = true,
                featureStartTick = Math.Max(0, Find.TickManager?.TicksGame ?? 0)
            });
        }

        private static OdysseyTravelHistoryState History(DiaryGameComponent component)
        {
            OdysseyTravelHistoryState history = TravelHistoryField.GetValue(component)
                as OdysseyTravelHistoryState;
            if (history == null) throw new AssertionException("Odyssey travel history was unavailable.");
            return history;
        }

        private static int EventCount(DiaryGameComponent component)
        {
            return Repository(component).Count;
        }

        private static HashSet<string> EventIds(DiaryGameComponent component)
        {
            return new HashSet<string>(Repository(component).AllEvents
                .Where(row => row != null)
                .Select(row => row.eventId), StringComparer.OrdinalIgnoreCase);
        }

        private static List<DiaryEvent> NewFixtureLandingEvents(
            DiaryGameComponent component,
            HashSet<string> before)
        {
            return Repository(component).AllEvents.Where(row => row != null
                && !before.Contains(row.eventId)
                && row.interactionDefName == OdysseyEventDefNames.Landing
                && DiaryContextFields.Value(row.gameContext, "ship_name") == FixtureShipName).ToList();
        }

        private static List<DiaryEvent> FixtureLandingEvents(DiaryGameComponent component)
        {
            return Repository(component).AllEvents.Where(row => row != null
                && row.interactionDefName == OdysseyEventDefNames.Landing
                && DiaryContextFields.Value(row.gameContext, "ship_name") == FixtureShipName).ToList();
        }

        private static DiaryEventRepository Repository(DiaryGameComponent component)
        {
            DiaryEventRepository repository = EventsField.GetValue(component) as DiaryEventRepository;
            if (repository == null) throw new AssertionException("Diary repository was unavailable.");
            return repository;
        }

        private static void RemoveEventsAndDiaryRefs(
            DiaryGameComponent component,
            IEnumerable<string> eventIds)
        {
            HashSet<string> ids = new HashSet<string>(
                eventIds?.Where(id => !string.IsNullOrWhiteSpace(id)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            if (ids.Count == 0) return;
            Repository(component).RemoveEvents(ids);
            (ArchiveField.GetValue(component) as DiaryArchiveRepository)?.RemoveForEventIds(ids);
            List<PawnDiaryRecord> diaries = DiariesField.GetValue(component) as List<PawnDiaryRecord>;
            if (diaries != null)
            {
                for (int i = 0; i < diaries.Count; i++) diaries[i]?.eventIds?.RemoveAll(ids.Contains);
            }
            DiaryStateVersion.Bump();
        }

        private static void RemoveFixtureDiaryRecords(
            DiaryGameComponent component,
            IEnumerable<string> pawnIds)
        {
            HashSet<string> ids = new HashSet<string>(
                pawnIds?.Where(id => !string.IsNullOrWhiteSpace(id)) ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);
            if (ids.Count == 0) return;
            List<PawnDiaryRecord> diaries = DiariesField.GetValue(component) as List<PawnDiaryRecord>;
            diaries?.RemoveAll(row => row != null && ids.Contains(row.pawnId));
            Dictionary<string, PawnDiaryRecord> byId = DiariesByIdField.GetValue(component)
                as Dictionary<string, PawnDiaryRecord>;
            foreach (string id in ids) byId?.Remove(id);
            DiaryStateVersion.Bump();
        }

        private static void DestroyFixtureThings(List<string> pawnIds)
        {
            List<Map> maps = Find.Maps?.ToList() ?? new List<Map>();
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                List<Thing> things = map.listerThings.AllThings.ToList();
                for (int t = 0; t < things.Count; t++)
                {
                    Thing thing = things[t];
                    bool fixtureEngine = thing is Building_GravEngine engine
                        && engine.RenamableLabel == FixtureShipName;
                    bool fixturePawn = thing is Pawn pawn && pawnIds.Contains(pawn.GetUniqueLoadID());
                    if (fixtureEngine || fixturePawn) DestroyThing(thing);
                }
            }
        }

        private static void RemoveWorldObject(Gravship gravship)
        {
            if (gravship == null || Find.WorldObjects == null) return;
            if (Find.WorldObjects.AllWorldObjects.Contains(gravship)) Find.WorldObjects.Remove(gravship);
        }

        private static void DestroyThing(Thing thing)
        {
            if (thing == null || thing.Destroyed) return;
            if (thing is Pawn pawn && Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);
            thing.Destroy(DestroyMode.Vanish);
        }

        private static void DeleteFixtureSave(string saveName)
        {
            string path = GenFilePaths.FilePathForSavedGame(saveName);
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception exception)
            {
                Log.Warning(LogPrefix + "Could not delete disposable save '" + saveName + "': "
                    + exception.Message);
            }
        }

        private static string LocationKey(PlanetTile tile)
        {
            return "planet-layer-" + tile.Layer.LayerID + "-tile-" + tile.tileId;
        }

        private static FieldInfo ComponentField(string name)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Missing component field: " + name);
            return field;
        }

        private static FieldInfo ControllerField(string name)
        {
            FieldInfo field = typeof(WorldComponent_GravshipController).GetField(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Missing controller field: " + name);
            return field;
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

        private sealed class OdysseyComponentSnapshot
        {
            private readonly object active;
            private readonly object history;
            private readonly object intent;
            private readonly object pending;

            public OdysseyComponentSnapshot(DiaryGameComponent component)
            {
                active = ActiveJourneyField.GetValue(component);
                history = TravelHistoryField.GetValue(component);
                intent = TakeoffIntentField.GetValue(component);
                pending = PendingLandingField.GetValue(component);
            }

            public void Restore(DiaryGameComponent component)
            {
                ActiveJourneyField.SetValue(component, active);
                TravelHistoryField.SetValue(component, history);
                TakeoffIntentField.SetValue(component, intent);
                PendingLandingField.SetValue(component, pending);
            }
        }

        private sealed class ControllerSnapshot
        {
            private readonly object gravship;
            private readonly object map;
            private readonly object marker;
            private readonly object terrainCapture;
            private readonly object indoorMask;
            private readonly object shadowMask;
            private readonly object moveDesignator;
            private readonly Map landingMap;
            private readonly Gravship gameGravship;

            public ControllerSnapshot(WorldComponent_GravshipController controller)
            {
                gravship = ControllerGravshipField.GetValue(controller);
                map = ControllerMapField.GetValue(controller);
                marker = ControllerLandingMarkerField.GetValue(controller);
                terrainCapture = ControllerTerrainCaptureField.GetValue(controller);
                indoorMask = ControllerIndoorMaskField.GetValue(controller);
                shadowMask = ControllerShadowMaskField.GetValue(controller);
                moveDesignator = ControllerMoveDesignatorField.GetValue(controller);
                landingMap = controller.landingMap;
                gameGravship = Current.Game?.Gravship;
            }

            public void Restore(WorldComponent_GravshipController controller)
            {
                ControllerGravshipField.SetValue(controller, gravship);
                ControllerMapField.SetValue(controller, map);
                ControllerLandingMarkerField.SetValue(controller, marker);
                ControllerTerrainCaptureField.SetValue(controller, terrainCapture);
                ControllerIndoorMaskField.SetValue(controller, indoorMask);
                ControllerShadowMaskField.SetValue(controller, shadowMask);
                ControllerMoveDesignatorField.SetValue(controller, moveDesignator);
                controller.landingMap = landingMap;
                if (Current.Game != null) Current.Game.Gravship = gameGravship;
            }
        }
    }
}

// In-game O1.2-O2/N2-O fixtures for Odyssey policy, guarded context, persistence, state-only
// takeoff/travel boundaries, canonical successful-landing ownership, narrative evidence, and
// package-safe environmental policy Defs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using RimTestRedux;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Proves Odyssey state, landing ownership, environmental policy, and N2-O boundaries.</summary>
    [TestSuite]
    public static class PawnDiaryOdysseyJourneyFlowTests
    {
        private const string LogPrefix = "[PawnDiary RimTest Odyssey O1.2-N2-O] ";

        /// <summary>All three public seams and the defensive private finish seam are actually patched.</summary>
        [Test]
        public static void LifecycleHooksAreRegisteredOnVerifiedRuntimeSeams()
        {
            MethodBase takeoff = AccessTools.DeclaredMethod(
                typeof(WorldComponent_GravshipController),
                nameof(WorldComponent_GravshipController.InitiateTakeoff),
                new[] { typeof(Building_GravEngine), typeof(PlanetTile) });
            MethodBase travel = AccessTools.DeclaredMethod(
                typeof(GravshipUtility),
                nameof(GravshipUtility.TravelTo),
                new[] { typeof(Gravship), typeof(PlanetTile), typeof(PlanetTile) });
            MethodBase landing = AccessTools.DeclaredMethod(
                typeof(WorldComponent_GravshipController),
                nameof(WorldComponent_GravshipController.InitiateLanding),
                new[] { typeof(Gravship), typeof(Map), typeof(IntVec3), typeof(Rot4) });
            MethodBase finish = AccessTools.DeclaredMethod(
                typeof(WorldComponent_GravshipController), "LandingEnded", Type.EmptyTypes);

            Require(HasOwnedPatch(takeoff, typeof(OdysseyInitiateTakeoffPatch), prefix: true),
                "InitiateTakeoff lacks Pawn Diary's exact O1.3 prefix.");
            Require(HasOwnedPatch(travel, typeof(OdysseyTravelToPatch), prefix: false),
                "GravshipUtility.TravelTo lacks Pawn Diary's exact O1.3 postfix.");
            Require(HasOwnedPatch(landing, typeof(OdysseyInitiateLandingPatch), prefix: true),
                "InitiateLanding lacks Pawn Diary's exact O1.3 prefix.");
            Require(OdysseyLandingEndedPatch.HooksReady,
                "The defensive private LandingEnded hook did not report ready.");
            Require(HasOwnedPatch(finish, typeof(OdysseyLandingEndedPatch), prefix: true)
                    && HasOwnedPatch(finish, typeof(OdysseyLandingEndedPatch), prefix: false),
                "LandingEnded lacks Pawn Diary's paired prefix/postfix.");

            Require(OdysseyLandingOutcomePatch.HooksReady,
                "The defensive landing-outcome override hooks did not report ready.");
            Type[] outcomeWorkers =
            {
                typeof(LandingOutcomeWorker_MinorGravshipCrash),
                typeof(LandingOutcomeWorker_ThrusterBreakdown),
                typeof(LandingOutcomeWorker_OverheatedGravEngine),
                typeof(LandingOutcomeWorker_GravNausea)
            };
            for (int i = 0; i < outcomeWorkers.Length; i++)
            {
                MethodBase apply = AccessTools.DeclaredMethod(
                    outcomeWorkers[i], nameof(LandingOutcomeWorker.ApplyOutcome),
                    new[] { typeof(Gravship) });
                Require(HasOwnedPatch(apply, typeof(OdysseyLandingOutcomePatch), prefix: false),
                    outcomeWorkers[i].Name + " lacks Pawn Diary's exact successful-outcome postfix.");
            }
        }

        /// <summary>
        /// Proves the O2 seasonal-flood settings row is absent without Odyssey and loaded with its
        /// exact Thing matcher when Odyssey is active. Grav nausea is string-matched, so its harmless
        /// enchantment policy may load in either case and simply finds no hediff without the DLC.
        /// </summary>
        [Test]
        public static void EnvironmentalPolicyDefsRespectPackageAndExactMatchers()
        {
            DiaryObservedConditionDef flood = DefDatabase<DiaryObservedConditionDef>.GetNamedSilentFail(
                "SeasonalFloodActive");
            if (!ModsConfig.OdysseyActive)
            {
                Require(flood == null,
                    "SeasonalFloodActive settings must be absent when Odyssey is inactive.");
            }
            else
            {
                Require(flood != null
                        && flood.observerType == ObservedConditionObserverType.ThingPresent
                        && flood.matchDefNames != null
                        && flood.matchDefNames.Count == 1
                        && flood.matchDefNames[0] == "SeasonalFlood",
                    "Loaded SeasonalFloodActive policy did not keep its exact ThingPresent matcher.");
                Require(!flood.recordStartEvent && !flood.recordEndEvent && flood.promptEnabled,
                    "Loaded SeasonalFloodActive policy must remain prompt-only.");
            }

            DiaryPromptEnchantmentDef nausea = DefDatabase<DiaryPromptEnchantmentDef>.GetNamedSilentFail(
                "DiaryEnchant_GravNausea");
            Require(nausea != null
                    && nausea.hediffDefNames != null
                    && nausea.hediffDefNames.Count == 1
                    && nausea.hediffDefNames[0] == "GravNausea",
                "Loaded grav-nausea enchantment did not keep its exact hediff matcher.");
            Require(Math.Abs(nausea.chance - 0.7f) < 0.0001f
                    && Math.Abs(nausea.weight - 1.1f) < 0.0001f
                    && Math.Abs(nausea.severity - 1.2f) < 0.0001f,
                "Loaded grav-nausea chance/weight/severity drifted from XML policy.");
        }

        /// <summary>
        /// Detached intent→commit→landing mutates component state once; without resolvable live writers
        /// O1.4 records routine history but correctly creates no page or page marker.
        /// </summary>
        [Test]
        public static void StateOnlyLifecycleCommitsAndLandsIdempotentlyWithoutPage()
        {
            DiaryGameComponent component = DiaryGameComponent.Instance;
            Require(component != null && Verse.Current.ProgramState == ProgramState.Playing,
                "O1.3 state-flow fixture requires a loaded game component.");
            FieldInfo activeField = ComponentField("odysseyActiveJourney");
            FieldInfo historyField = ComponentField("odysseyTravelHistory");
            FieldInfo intentField = ComponentField("odysseyTakeoffIntent");
            FieldInfo pendingField = ComponentField("odysseyPendingLanding");
            object oldActive = activeField.GetValue(component);
            object oldHistory = historyField.GetValue(component);
            object oldIntent = intentField.GetValue(component);
            object oldPending = pendingField.GetValue(component);
            int eventsBefore = EventCount(component);
            try
            {
                activeField.SetValue(component, null);
                historyField.SetValue(component, new OdysseyTravelHistoryState
                {
                    historyInitialized = true,
                    historyTrustworthyForFirstClaims = false,
                    featureStartTick = 1
                });
                intentField.SetValue(component, null);
                pendingField.SetValue(component, null);

                OdysseyTakeoffIntent intent = new OdysseyTakeoffIntent
                {
                    engineId = "Thing_RimTestEngine",
                    shipName = "RimTest Wayfarer",
                    origin = TestLocation("rimtest-home", "surface", true),
                    selectedTargetKey = "rimtest-orbit",
                    captureTick = 100,
                    launchQualityBand = OdysseyLaunchQualityTokens.Excellent,
                    writers = TestWriters()
                };
                Require(component.CaptureOdysseyTakeoffIntent(intent),
                    "State-only takeoff intent was rejected.");
                Require(activeField.GetValue(component) == null && EventCount(component) == eventsBefore,
                    "Takeoff intent committed a journey or emitted a page.");

                OdysseyTravelCommitObservation commit = new OdysseyTravelCommitObservation
                {
                    shipStableId = "WorldObject_RimTestShip",
                    engineId = "Thing_RimTestEngine",
                    shipName = "RimTest Wayfarer",
                    origin = TestLocation("fallback-origin", "surface", false),
                    destination = TestLocation("rimtest-orbit", "orbit", false),
                    departureTick = 150,
                    writers = TestWriters()
                };
                Require(component.CommitOdysseyTravel(commit), "Travel commit was rejected.");
                OdysseyJourneyState active = activeField.GetValue(component) as OdysseyJourneyState;
                OdysseyTravelHistoryState history = historyField.GetValue(component) as OdysseyTravelHistoryState;
                Require(active != null && active.sourceComplete,
                    "Matched takeoff intent did not produce a complete active journey.");
                AssertStr("rimtest-home", active.origin?.stableKey, "pre-despawn origin");
                AssertInt(1, history?.committedJourneyCount ?? -1, "committed journey count");
                Require(history.historyTrustworthyForFirstClaims,
                    "Post-feature TravelTo did not establish trustworthy future novelty history.");
                Require(EventCount(component) == eventsBefore,
                    "TravelTo state commit emitted a diary page.");

                Require(component.CommitOdysseyTravel(commit), "Idempotent replay was rejected.");
                history = historyField.GetValue(component) as OdysseyTravelHistoryState;
                AssertInt(1, history?.committedJourneyCount ?? -1,
                    "replayed journey must not increment history");

                OdysseyPendingLanding pending = new OdysseyPendingLanding
                {
                    shipStableId = "WorldObject_RimTestShip",
                    destination = TestLocation("rimtest-orbit", "orbit", false),
                    captureTick = 500,
                    writers = TestWriters()
                };
                // Make the destination objectively major so pure policy authorizes a page. Because
                // this detached fixture intentionally supplies no live Pawn references, O1.4 must
                // still apply only observation history and roll back the pre-authorized page marker.
                pending.destination.siteDefName = "OrbitalMechhive";
                pending.destination.siteLabel = "Orbital mechhive";
                Require(component.CaptureOdysseyLandingStart(pending),
                    "Matching landing start was rejected.");
                history = historyField.GetValue(component) as OdysseyTravelHistoryState;
                AssertInt(-1, history?.lastLandingObservationTick ?? -2,
                    "landing start must not mutate novelty history");
                Require(EventCount(component) == eventsBefore,
                    "Landing start emitted a diary page.");

                OdysseyPendingLanding finish = new OdysseyPendingLanding
                {
                    shipStableId = pending.shipStableId,
                    destination = pending.destination,
                    captureTick = 550,
                    writers = TestWriters()
                };
                Require(component.CaptureOdysseyLandingFinish(finish)
                        && component.CompleteOdysseyLanding(finish),
                    "Successful landing finish did not apply state.");
                history = historyField.GetValue(component) as OdysseyTravelHistoryState;
                Require(activeField.GetValue(component) == null && pendingField.GetValue(component) == null,
                    "Successful landing left active/pending lifecycle state behind.");
                AssertInt(550, history?.lastLandingObservationTick ?? -1,
                    "successful landing observation tick");
                AssertInt(-1, history?.lastLandingPageTick ?? -2,
                    "writerless landing must not claim a landing page tick");
                AssertInt(0, history?.emittedJourneyIds?.Count ?? -1,
                    "writerless landing must not consume an emitted journey id");
                Require(EventCount(component) == eventsBefore,
                    "Landing without live selected writers emitted a DiaryEvent.");
            }
            finally
            {
                activeField.SetValue(component, oldActive);
                historyField.SetValue(component, oldHistory);
                intentField.SetValue(component, oldIntent);
                pendingField.SetValue(component, oldPending);
            }
        }

        /// <summary>One major successful landing creates one pair event and commits its page marker once.</summary>
        [Test]
        public static void NovelLandingCreatesOneCanonicalTwoPovEvent()
        {
            if (!ModsConfig.OdysseyActive)
            {
                Log.Message(LogPrefix + "Odyssey inactive: canonical landing fixture skipped cleanly.");
                return;
            }

            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin("odysseyGravshipLanding");
            FieldInfo activeField = ComponentField("odysseyActiveJourney");
            FieldInfo historyField = ComponentField("odysseyTravelHistory");
            FieldInfo pendingField = ComponentField("odysseyPendingLanding");
            object oldActive = activeField.GetValue(scope.Component);
            object oldHistory = historyField.GetValue(scope.Component);
            object oldPending = pendingField.GetValue(scope.Component);
            scope.RegisterCleanup(() =>
            {
                activeField.SetValue(scope.Component, oldActive);
                historyField.SetValue(scope.Component, oldHistory);
                pendingField.SetValue(scope.Component, oldPending);
            });

            try
            {
                Pawn pilot = scope.CreateAdultColonist();
                Pawn copilot = scope.CreateAdultColonist();
                int landingTick = Find.TickManager?.TicksGame ?? 70000;
                int departureTick = Math.Max(0, landingTick - 70000);
                string shipId = "RimTestShip_" + pilot.GetUniqueLoadID();
                string journeyId = OdysseyArcKeys.Journey(shipId, departureTick);
                List<OdysseyWriterCandidate> writers = new List<OdysseyWriterCandidate>
                {
                    new OdysseyWriterCandidate
                    {
                        pawnId = pilot.GetUniqueLoadID(),
                        displayName = pilot.LabelShortCap,
                        roleToken = OdysseyJourneyRoleTokens.Pilot,
                        eligible = true,
                        present = true,
                        hasDiary = true
                    },
                    new OdysseyWriterCandidate
                    {
                        pawnId = copilot.GetUniqueLoadID(),
                        displayName = copilot.LabelShortCap,
                        roleToken = OdysseyJourneyRoleTokens.Copilot,
                        eligible = true,
                        present = true,
                        hasDiary = true
                    }
                };
                OdysseyLocationSnapshot destination = new OdysseyLocationSnapshot
                {
                    stableKey = "rimtest-orbital-mechhive",
                    visibleLabel = "RimTest orbital mechhive",
                    layerToken = OdysseyLocationLayerTokens.Orbit,
                    biomeDefName = "Orbit",
                    siteDefName = "OrbitalMechhive",
                    siteLabel = "Orbital mechhive",
                    visible = true
                };
                OdysseyJourneySnapshot journey = new OdysseyJourneySnapshot
                {
                    journeyId = journeyId,
                    shipStableId = shipId,
                    shipName = "RimTest Wayfarer",
                    origin = TestLocation("rimtest-origin", OdysseyLocationLayerTokens.Surface, true),
                    destination = destination,
                    departureTick = departureTick,
                    launchQualityBand = OdysseyLaunchQualityTokens.Excellent,
                    sourceComplete = true,
                    writers = writers
                };
                activeField.SetValue(scope.Component, OdysseyJourneyState.FromSnapshot(journey));
                historyField.SetValue(scope.Component, new OdysseyTravelHistoryState
                {
                    historyInitialized = true,
                    historyTrustworthyForFirstClaims = true,
                    committedJourneyCount = 1,
                    lastDepartureTick = departureTick
                });
                pendingField.SetValue(scope.Component, null);
                OdysseyPendingLanding finish = new OdysseyPendingLanding
                {
                    shipStableId = shipId,
                    destination = destination,
                    captureTick = landingTick,
                    writers = writers
                };

                DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                    () => scope.Component.CompleteOdysseyLanding(
                        finish, new List<Pawn> { pilot, copilot }),
                    OdysseyEventDefNames.Landing,
                    pilot,
                    copilot);
                Require(!diaryEvent.solo,
                    "Two selected Odyssey writers did not share one pair-shaped DiaryEvent.");
                Require(diaryEvent.gameContext.Contains("odyssey_journey=true")
                        && diaryEvent.gameContext.Contains("journey_phase=landing")
                        && diaryEvent.gameContext.Contains("journey_reason=major_destination"),
                    "Canonical landing event lost its frozen Odyssey context markers.");
                OdysseyTravelHistoryState applied = historyField.GetValue(scope.Component)
                    as OdysseyTravelHistoryState;
                AssertInt(landingTick, applied?.lastLandingPageTick ?? -1,
                    "landing page tick committed after event creation");
                Require(applied != null && applied.emittedJourneyIds.Contains(journeyId),
                    "Created landing event did not commit its durable journey marker.");
                Require(activeField.GetValue(scope.Component) == null,
                    "Canonical landing did not clear the completed active journey.");
                scope.RequireNoNewEvent(() => scope.Component.CompleteOdysseyLanding(
                    finish, new List<Pawn> { pilot, copilot }));
            }
            finally
            {
                scope.TearDown();
            }
        }

        /// <summary>
        /// The localized Odyssey prompt-suite fixture exercises the shipped important-pair template.
        /// Full/Balanced/Compact must all retain the minimum journey truth and both POV roles.
        /// </summary>
        [Test]
        public static void OdysseyPromptFixtureKeepsJourneyTruthAcrossAllDetailPresets()
        {
            if (!ModsConfig.OdysseyActive)
            {
                Log.Message(LogPrefix + "Odyssey inactive: prompt fixture skipped cleanly.");
                return;
            }

            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin("odysseyGravshipLanding");
            try
            {
                scope.EnablePromptCapture();
                Pawn pilot = scope.CreateGeneratingAdultColonist();
                Pawn copilot = scope.CreateGeneratingAdultColonist();
                scope.SpawnAsLiveColonist(pilot);
                scope.SpawnAsLiveColonist(copilot);
                ResetFreeColonistSnapshotForFixture();
                DiaryGameComponent.DevPromptSuiteEntry entry =
                    DiaryGameComponent.AllSuiteEntries.FirstOrDefault(row => row?.id == "OdysseyLanding");
                Require(entry != null, "The Odyssey landing prompt-suite fixture was not registered.");

                PromptContextDetailLevel[] levels =
                {
                    PromptContextDetailLevel.Full,
                    PromptContextDetailLevel.Balanced,
                    PromptContextDetailLevel.Compact
                };
                for (int i = 0; i < levels.Length; i++)
                {
                    PawnDiaryMod.Settings.contextDetailLevel = levels[i];
                    Require(scope.Component.ShowPromptSuiteEntryForDev(pilot, entry),
                        "The Odyssey prompt fixture could not be rendered for " + levels[i] + ".");
                    DiaryEvent diaryEvent = PromptSuiteEvent(scope.Component);
                    string initiatorPrompt = scope.CapturedPrompt(diaryEvent, DiaryEvent.InitiatorRole);
                    string recipientPrompt = scope.CapturedPrompt(diaryEvent, DiaryEvent.RecipientRole);
                    string suffix = " (" + levels[i] + ")";
                    string ship = DiaryContextFields.Value(diaryEvent.gameContext, "ship_name");
                    string origin = DiaryContextFields.Value(diaryEvent.gameContext, "origin");
                    string destination = DiaryContextFields.Value(diaryEvent.gameContext, "destination");

                    RequirePromptContains(initiatorPrompt, "major_destination", "journey reason" + suffix);
                    RequirePromptContains(initiatorPrompt, "long_journey", "secondary reason" + suffix);
                    RequirePromptContains(initiatorPrompt, ship, "ship" + suffix);
                    RequirePromptContains(initiatorPrompt, origin, "origin" + suffix);
                    RequirePromptContains(initiatorPrompt, destination, "destination" + suffix);
                    RequirePromptContains(initiatorPrompt, pilot.LabelShortCap, "pilot name" + suffix);
                    RequirePromptContains(initiatorPrompt, copilot.LabelShortCap, "copilot name" + suffix);
                    RequirePromptContains(recipientPrompt, ship, "recipient ship" + suffix);
                    RequirePromptContains(recipientPrompt, destination, "recipient destination" + suffix);
                    RequirePromptOmits(initiatorPrompt, "odyssey-journey|", "journey ID" + suffix);
                    RequirePromptOmits(initiatorPrompt, "WorldObject_", "ship stable ID" + suffix);
                    RequirePromptOmits(initiatorPrompt, "planet-layer-", "location key" + suffix);
                }
            }
            finally
            {
                scope.TearDown();
            }
        }

        /// <summary>A short already-known surface hop updates history but never creates a landing page.</summary>
        [Test]
        public static void RoutineLandingWithEligibleWriterCreatesNoEvent()
        {
            if (!ModsConfig.OdysseyActive)
            {
                Log.Message(LogPrefix + "Odyssey inactive: routine landing fixture skipped cleanly.");
                return;
            }

            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin("odysseyGravshipLanding");
            FieldInfo activeField = ComponentField("odysseyActiveJourney");
            FieldInfo historyField = ComponentField("odysseyTravelHistory");
            object oldActive = activeField.GetValue(scope.Component);
            object oldHistory = historyField.GetValue(scope.Component);
            scope.RegisterCleanup(() =>
            {
                activeField.SetValue(scope.Component, oldActive);
                historyField.SetValue(scope.Component, oldHistory);
            });

            try
            {
                Pawn pilot = scope.CreateAdultColonist();
                int landingTick = Find.TickManager?.TicksGame ?? 70000;
                int departureTick = Math.Max(0, landingTick - 100);
                OdysseyLocationSnapshot origin = TestLocation(
                    "rimtest-routine-origin", OdysseyLocationLayerTokens.Surface, false);
                OdysseyLocationSnapshot destination = OdysseyLocationPolicy.Classify(
                    TestLocation("rimtest-routine-destination", OdysseyLocationLayerTokens.Surface, false),
                    DiaryOdysseyPolicy.Snapshot());
                OdysseyJourneySnapshot journey = new OdysseyJourneySnapshot
                {
                    journeyId = OdysseyArcKeys.Journey("RimTestRoutineShip", departureTick),
                    shipStableId = "RimTestRoutineShip",
                    shipName = "RimTest Shuttle",
                    origin = origin,
                    destination = destination,
                    departureTick = departureTick,
                    launchQualityBand = OdysseyLaunchQualityTokens.Ordinary,
                    sourceComplete = true,
                    writers = new List<OdysseyWriterCandidate>
                    {
                        new OdysseyWriterCandidate
                        {
                            pawnId = pilot.GetUniqueLoadID(),
                            displayName = pilot.LabelShortCap,
                            roleToken = OdysseyJourneyRoleTokens.Pilot,
                            eligible = true,
                            present = true,
                            hasDiary = true
                        }
                    }
                };
                activeField.SetValue(scope.Component, OdysseyJourneyState.FromSnapshot(journey));
                historyField.SetValue(scope.Component, new OdysseyTravelHistoryState
                {
                    historyInitialized = true,
                    historyTrustworthyForFirstClaims = true,
                    committedJourneyCount = 2,
                    lastDepartureTick = departureTick,
                    visitedLayerKeys = new List<string> { OdysseyLocationLayerTokens.Surface },
                    visitedCategoryKeys = new List<string> { destination.categoryToken },
                    visitedLocationKeys = new List<string> { destination.stableKey }
                });
                OdysseyPendingLanding finish = new OdysseyPendingLanding
                {
                    shipStableId = journey.shipStableId,
                    destination = destination,
                    captureTick = landingTick,
                    writers = journey.writers
                };

                scope.RequireNoNewEvent(() => scope.Component.CompleteOdysseyLanding(
                    finish, new List<Pawn> { pilot }));
                OdysseyTravelHistoryState applied = historyField.GetValue(scope.Component)
                    as OdysseyTravelHistoryState;
                AssertInt(landingTick, applied?.lastLandingObservationTick ?? -1,
                    "routine landing observation tick");
                AssertInt(-1, applied?.lastLandingPageTick ?? -2,
                    "routine landing page tick");
                Require(activeField.GetValue(scope.Component) == null,
                    "Routine successful landing left its active journey behind.");
            }
            finally
            {
                scope.TearDown();
            }
        }

        /// <summary>The vanilla no-pawn TileSettled Tale remains deliberately unowned by Pawn Diary.</summary>
        [Test]
        public static void TileSettledNoPawnTaleIsDropped()
        {
            CaptureDecision decision = TaleEventData.Decide(
                new TaleEventData { DefName = "TileSettled" },
                new CaptureContext
                {
                    Eligible = true,
                    SignalEnabled = true,
                    UserEnabled = true,
                    AmbientSignalEnabled = true
                });
            Require(decision == CaptureDecision.Drop,
                "The no-pawn TileSettled Tale acquired a duplicate Pawn Diary owner.");
        }

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
            Require(!string.IsNullOrWhiteSpace(policy.mobileHomeNarrativeFormat),
                "N2-O mobile-home DefInjected format was not projected.");
            Require(policy.landingPageEnabled,
                "O1.4 must project XML-enabled novelty-gated landing pages.");
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
                OdysseyNarrativeSnapshot narrative = DiaryGameComponent.Instance?
                    .OdysseyNarrativeSnapshotFor(pawn, Find.TickManager?.TicksGame ?? 0);

                if (!ModsConfig.OdysseyActive)
                {
                    Require(!captured && location == null && !mobileCaptured && mobileHome == null
                            && narrative == null,
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
                    Require(narrative != null && narrative.hasVerifiedPovConnection
                            && narrative.shipStableId == mobileHome.shipStableId
                            && narrative.locationKey == mobileHome.location.stableKey
                            && narrative.homeText.IndexOf(mobileHome.shipName,
                                StringComparison.OrdinalIgnoreCase) >= 0,
                        "Exact onboard state did not freeze one matching N2-O provider snapshot.");
                }
                else
                {
                    Require(narrative == null,
                        "A pawn outside vanilla's grav field received an N2-O provider snapshot.");
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

        private static OdysseyLocationSnapshot TestLocation(string key, string layer, bool home)
        {
            return new OdysseyLocationSnapshot
            {
                stableKey = key,
                visibleLabel = key,
                layerToken = layer,
                biomeDefName = layer == OdysseyLocationLayerTokens.Orbit ? "Orbit" : "TemperateForest",
                categoryToken = layer,
                isPlayerHome = home,
                visible = true
            };
        }

        private static List<OdysseyWriterCandidate> TestWriters()
        {
            return new List<OdysseyWriterCandidate>
            {
                new OdysseyWriterCandidate
                {
                    pawnId = "Pawn_RimTestPilot",
                    displayName = "RimTest Pilot",
                    roleToken = OdysseyJourneyRoleTokens.Pilot,
                    eligible = true,
                    present = true,
                    hasDiary = true
                }
            };
        }

        private static FieldInfo ComponentField(string name)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new AssertionException("Missing component field: " + name);
            return field;
        }

        private static int EventCount(DiaryGameComponent component)
        {
            FieldInfo field = ComponentField("events");
            DiaryEventRepository repository = field.GetValue(component) as DiaryEventRepository;
            if (repository == null) throw new AssertionException("Diary event repository was unavailable.");
            return repository.Count;
        }

        private static DiaryEvent PromptSuiteEvent(DiaryGameComponent component)
        {
            FieldInfo field = ComponentField("events");
            DiaryEventRepository repository = field.GetValue(component) as DiaryEventRepository;
            DiaryEvent result = repository?.AllEvents.LastOrDefault(diaryEvent => diaryEvent != null
                && diaryEvent.interactionDefName == OdysseyEventDefNames.Landing
                && DiaryContextFields.IsTrue(diaryEvent.gameContext, "dev_prompt_suite"));
            if (result == null)
            {
                throw new AssertionException("The Odyssey prompt-suite event was not found after rendering.");
            }

            return result;
        }

        private static void ResetFreeColonistSnapshotForFixture()
        {
            MethodInfo reset = typeof(DiaryGameComponent).GetMethod(
                "ResetFreeColonistSnapshot", BindingFlags.Static | BindingFlags.NonPublic);
            if (reset == null)
            {
                throw new AssertionException(
                    "The Odyssey prompt fixture could not reset the per-tick free-colonist snapshot.");
            }

            reset.Invoke(null, null);
        }

        private static void RequirePromptContains(string prompt, string expected, string label)
        {
            Require(!string.IsNullOrWhiteSpace(expected)
                    && prompt.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0,
                "Odyssey prompt omitted " + label + ": '" + expected + "'.");
        }

        private static void RequirePromptOmits(string prompt, string forbidden, string label)
        {
            Require(prompt.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) < 0,
                "Odyssey prompt exposed " + label + ": '" + forbidden + "'.");
        }

        private static bool HasOwnedPatch(MethodBase target, Type patchType, bool prefix)
        {
            Patches patches = target == null ? null : Harmony.GetPatchInfo(target);
            if (patches == null) return false;
            IEnumerable<Patch> rows = prefix ? patches.Prefixes : patches.Postfixes;
            return rows.Any(row => row.owner == "aimml.pawndiary"
                && row.PatchMethod?.DeclaringType == patchType);
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

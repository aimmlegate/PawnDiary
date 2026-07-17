// Standalone no-RimWorld tests for Master Wave 4 / Odyssey O1 policy. The project links only the plain
// contracts and pure policies, so any accidental Verse/Unity/Harmony dependency fails compilation.
using System;
using System.Collections.Generic;
using PawnDiary;

namespace DiaryOdysseyPolicyTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestFrozenSchemaAndArcKeys();
            TestExactLocationClassification();
            TestDeterministicWriterSelection();
            TestQualitativeBands();
            TestLaunchCooldownPolicy();
            TestReasonPriorityAndHistoryMutation();
            TestCooldownAndBypass();
            TestUntrustedHistoryAndDropPaths();
            TestHistoryNormalizationAndOldSaveBaseline();
            TestLifecycleCorrelationCommitAndDepartureHistory();
            TestLifecycleFallbackExpiryAndLandingCorrelation();
            TestBoundedContextFormatting();
            Console.WriteLine("DiaryOdysseyPolicyTests passed " + assertions + " assertions.");
            return 0;
        }

        private static void TestFrozenSchemaAndArcKeys()
        {
            AssertEqual("landing event Def", "OdysseyGravshipLanding", OdysseyEventDefNames.Landing);
            AssertEqual("launch group key", "ritualGravship", OdysseyGroupDefNames.Launch);
            AssertEqual("landing group key", "odysseyGravshipLanding", OdysseyGroupDefNames.Landing);
            AssertEqual("active journey save key", "odysseyActiveJourney", OdysseySaveKeys.ActiveJourney);
            AssertEqual("history save key", "odysseyTravelHistory", OdysseySaveKeys.TravelHistory);
            AssertEqual("journey id", "odyssey-journey|Ship_7|0", OdysseyArcKeys.Journey(" Ship_7 ", 0));
            AssertEqual("landing dedup", "odyssey-landing|Ship_7|42", OdysseyArcKeys.Landing("Ship_7", 42));
            AssertEqual("separator rejected", string.Empty, OdysseyArcKeys.Journey("bad|ship", 42));
            AssertEqual("negative tick rejected", string.Empty, OdysseyArcKeys.Landing("Ship_7", -1));
            AssertTrue("known landing reason", OdysseyLandingReasonTokens.IsKnown("homecoming"));
            AssertTrue("unknown landing reason rejected", !OdysseyLandingReasonTokens.IsKnown("first_fish"));
            AssertEqual("pilot rank", 0, OdysseyJourneyRoleTokens.Rank("pilot"));
            AssertEqual("unknown role rank", int.MaxValue, OdysseyJourneyRoleTokens.Rank("passenger"));
            AssertEqual("layer normalization", "orbit", OdysseyLocationLayerTokens.Normalize("ORBIT"));
            AssertEqual("unknown layer normalization", "unknown", OdysseyLocationLayerTokens.Normalize("deep_space"));
        }

        private static void TestExactLocationClassification()
        {
            OdysseyPolicySnapshot policy = Policy();
            OdysseyLocationSnapshot source = Location("loc-1", "orbit", "Biome_Frozen", "Site_Mechhive");
            OdysseyLocationSnapshot classified = OdysseyLocationPolicy.Classify(source, policy);
            AssertEqual("site mapping wins", "mechhive", classified.categoryToken);
            AssertTrue("major site preserved", classified.majorDestination);
            AssertEqual("source remains unmodified", string.Empty, source.categoryToken);

            source.siteDefName = string.Empty;
            classified = OdysseyLocationPolicy.Classify(source, policy);
            AssertEqual("biome fallback", "frozen", classified.categoryToken);
            AssertTrue("ordinary biome is not major", !classified.majorDestination);

            source.biomeDefName = "Modded_Biome_Frozen_Almost";
            source.visibleLabel = "A frozen mechhive-looking plain";
            classified = OdysseyLocationPolicy.Classify(source, policy);
            AssertEqual("no English or substring guessing", string.Empty, classified.categoryToken);

            source.biomeDefName = "Biome_Frozen";
            source.visible = false;
            classified = OdysseyLocationPolicy.Classify(source, policy);
            AssertEqual("invisible location authorizes nothing", string.Empty, classified.categoryToken);
        }

        private static void TestDeterministicWriterSelection()
        {
            List<OdysseyWriterCandidate> candidates = new List<OdysseyWriterCandidate>
            {
                Writer("Crew_B", "crew", true, true, true),
                Writer("Pilot", "pilot", true, true, true),
                Writer("Crew_A", "crew", true, true, true),
                Writer("Copilot", "copilot", true, true, true),
                Writer("Pilot", "crew", true, true, true),
                Writer("DeadPilot", "pilot", true, false, true),
                Writer("NoDiary", "pilot", true, true, false),
                Writer("Unknown", "passenger", true, true, true)
            };

            List<OdysseyWriterCandidate> selected = OdysseyWriterPolicy.Select(candidates, 2);
            AssertEqual("two writer cap", 2, selected.Count);
            AssertEqual("pilot first", "Pilot", selected[0].pawnId);
            AssertEqual("copilot second", "Copilot", selected[1].pawnId);
            AssertEqual("candidate copied", "pilot", selected[0].roleToken);

            selected = OdysseyWriterPolicy.Select(candidates, 1);
            AssertEqual("authored one writer cap", 1, selected.Count);
            AssertEqual("one writer remains pilot", "Pilot", selected[0].pawnId);

            candidates.RemoveAll(row => row.roleToken == "pilot");
            candidates.RemoveAll(row => row.roleToken == "copilot");
            selected = OdysseyWriterPolicy.Select(candidates, 99);
            AssertEqual("hard maximum remains two", 2, selected.Count);
            AssertEqual("crew stable id tie-break", "Crew_A", selected[0].pawnId);
            AssertEqual("second crew stable id", "Crew_B", selected[1].pawnId);
        }

        private static void TestQualitativeBands()
        {
            OdysseyPolicySnapshot policy = Policy();
            AssertEqual("short duration", "short", OdysseyJourneyPolicy.DurationBand(100, 15100, policy));
            AssertEqual("ordinary duration", "ordinary", OdysseyJourneyPolicy.DurationBand(100, 30100, policy));
            AssertEqual("long duration", "long", OdysseyJourneyPolicy.DurationBand(100, 60100, policy));
            AssertEqual("backward tick clamps short", "short", OdysseyJourneyPolicy.DurationBand(500, 100, policy));
            AssertEqual("poor quality", "poor", OdysseyJourneyPolicy.LaunchQualityBand(0.2f, policy));
            AssertEqual("ordinary quality", "ordinary", OdysseyJourneyPolicy.LaunchQualityBand(0.5f, policy));
            AssertEqual("excellent quality", "excellent", OdysseyJourneyPolicy.LaunchQualityBand(0.9f, policy));
            AssertEqual("NaN quality unknown", "unknown", OdysseyJourneyPolicy.LaunchQualityBand(float.NaN, policy));
        }

        private static void TestLaunchCooldownPolicy()
        {
            OdysseyPolicySnapshot policy = Policy();
            policy.launchCooldownTicks = 60000;
            AssertTrue("first launch ritual allowed", OdysseyLaunchPolicy.AllowsPage(-1, 100, policy));
            AssertTrue("launch inside prior-departure cooldown drops",
                !OdysseyLaunchPolicy.AllowsPage(100, 60099, policy));
            AssertTrue("launch at cooldown boundary allowed",
                OdysseyLaunchPolicy.AllowsPage(100, 60100, policy));
            policy.launchCooldownTicks = 0;
            AssertTrue("disabled launch cooldown allows repeat",
                OdysseyLaunchPolicy.AllowsPage(100, 101, policy));
            policy.enabled = false;
            AssertTrue("disabled Odyssey policy suppresses launch adapter",
                !OdysseyLaunchPolicy.AllowsPage(-1, 100, policy));
        }

        private static void TestReasonPriorityAndHistoryMutation()
        {
            OdysseyPolicySnapshot policy = Policy();
            OdysseyJourneySnapshot journey = Journey(100, true);
            journey.roughLanding = true;
            journey.destination = Location("home-b", "orbit", "Biome_Frozen", "Site_Mechhive");
            OdysseyTravelHistorySnapshot history = History(trustworthy: true);
            history.committedJourneyCount = 2;
            history.homeKeys.Add("home-b");

            OdysseyLandingPlan plan = OdysseyLandingPolicy.Plan(
                Observation(journey, 70000, true),
                history,
                policy);
            AssertTrue("multi-reason landing writes", plan.writePage);
            AssertEqual("major destination priority", "major_destination", plan.primaryReason);
            AssertEqual("homecoming secondary", "homecoming", plan.secondaryReason);
            AssertTrue("major landing important", plan.important);
            AssertEqual("deterministic pilot writer", "Pilot", plan.selectedWriters[0].pawnId);
            AssertEqual("deterministic copilot writer", "Copilot", plan.selectedWriters[1].pawnId);
            AssertTrue("location mutation", plan.historyMutation.visitedLocationKeys.Contains("home-b"));
            AssertTrue("category mutation", plan.historyMutation.visitedCategoryKeys.Contains("mechhive"));
            AssertEqual("emitted journey mutation", journey.journeyId,
                plan.historyMutation.journeyIdToMarkEmitted);

            OdysseyTravelHistorySnapshot applied = OdysseyHistoryPolicy.Apply(history, plan, policy);
            OdysseyTravelHistorySnapshot appliedTwice = OdysseyHistoryPolicy.Apply(applied, plan, policy);
            AssertEqual("emitted journey idempotent", 1, appliedTwice.emittedJourneyIds.Count);
            AssertEqual("visited location idempotent", 1, appliedTwice.visitedLocationKeys.Count);
            AssertEqual("landing page tick applied", 70000, appliedTwice.lastLandingPageTick);
        }

        private static void TestCooldownAndBypass()
        {
            OdysseyPolicySnapshot policy = Policy();
            OdysseyJourneySnapshot journey = Journey(100, true);
            journey.destination = Location("new-frozen", "surface", "Biome_Frozen", string.Empty);
            OdysseyTravelHistorySnapshot history = History(trustworthy: true);
            history.lastLandingPageTick = 69990;

            OdysseyLandingPlan plan = OdysseyLandingPolicy.Plan(
                Observation(journey, 70000, true), history, policy);
            AssertEqual("new category reason", "new_biome_category", plan.primaryReason);
            AssertTrue("ordinary novelty respects cooldown", plan.droppedByCooldown && !plan.writePage);
            AssertEqual("cooldown drop still observes landing", 70000,
                plan.historyMutation.landingObservationTick);
            AssertEqual("cooldown drop does not consume emitted id", string.Empty,
                plan.historyMutation.journeyIdToMarkEmitted);

            journey.roughLanding = true;
            plan = OdysseyLandingPolicy.Plan(Observation(journey, 70000, true), history, policy);
            AssertTrue("qualifying bypass reason defeats cooldown", !plan.droppedByCooldown && plan.writePage);
        }

        private static void TestUntrustedHistoryAndDropPaths()
        {
            OdysseyPolicySnapshot policy = Policy();
            OdysseyJourneySnapshot journey = Journey(100, true);
            journey.destination = Location("routine-orbit", "orbit", "Biome_Frozen", string.Empty);
            OdysseyTravelHistorySnapshot history = History(trustworthy: false);

            OdysseyLandingPlan plan = OdysseyLandingPolicy.Plan(
                Observation(journey, 1000, true), history, policy);
            AssertEqual("untrusted history blocks false first/new", string.Empty, plan.primaryReason);
            AssertTrue("routine landing drops", !plan.writePage);
            AssertTrue("routine landing still mutates layer", plan.historyMutation.visitedLayerKeys.Contains("orbit"));

            journey.destination = Location("major", "surface", string.Empty, "Site_Mechhive");
            OdysseyLandingObservation disabled = Observation(journey, 1000, false);
            plan = OdysseyLandingPolicy.Plan(disabled, history, policy);
            AssertEqual("event-local major survives untrusted history", "major_destination", plan.primaryReason);
            AssertTrue("disabled group suppresses page", !plan.writePage);
            AssertTrue("disabled group still mutates history", plan.historyMutation.visitedLocationKeys.Contains("major"));

            OdysseyLandingObservation noWriters = Observation(journey, 1000, true);
            noWriters.writers.Clear();
            journey.writers.Clear();
            plan = OdysseyLandingPolicy.Plan(noWriters, history, policy);
            AssertTrue("no eligible writer suppresses page", !plan.writePage);
            AssertTrue("no-writer drop still observes", plan.historyMutation.landingObservationTick == 1000);

            journey = Journey(100, true);
            journey.destination = Location("major", "surface", string.Empty, "Site_Mechhive");
            history.emittedJourneyIds.Add(journey.journeyId);
            plan = OdysseyLandingPolicy.Plan(Observation(journey, 1000, true), history, policy);
            AssertTrue("durable emitted id rejects replay", !plan.writePage);

            journey.landingApplied = true;
            plan = OdysseyLandingPolicy.Plan(Observation(journey, 1000, true), history, policy);
            AssertEqual("already-applied journey has no partial mutation", -1,
                plan.historyMutation.landingObservationTick);
        }

        private static void TestHistoryNormalizationAndOldSaveBaseline()
        {
            OdysseyPolicySnapshot policy = Policy();
            policy.maximumVisitedCategories = 2;
            OdysseyTravelHistorySnapshot malformed = new OdysseyTravelHistorySnapshot
            {
                schemaVersion = -2,
                featureStartTick = -5,
                committedJourneyCount = -1,
                visitedCategoryKeys = new List<string> { "old", "OLD", "middle", "new" },
                visitedLocationKeys = null,
                homeKeys = null,
                emittedJourneyIds = null
            };
            OdysseyTravelHistorySnapshot normalized = OdysseyHistoryPolicy.Normalize(malformed, policy);
            AssertEqual("schema repaired", 1, normalized.schemaVersion);
            AssertEqual("negative feature tick repaired", 0, normalized.featureStartTick);
            AssertEqual("negative count repaired", 0, normalized.committedJourneyCount);
            AssertEqual("category cap", 2, normalized.visitedCategoryKeys.Count);
            AssertEqual("newest category retained", "new", normalized.visitedCategoryKeys[1]);
            AssertNotNull("null location list repaired", normalized.visitedLocationKeys);

            OdysseyLocationSnapshot visibleHome = Location("old-home", "surface", "Biome_Frozen", string.Empty);
            visibleHome.isPlayerHome = true;
            OdysseyTravelHistorySnapshot baseline = OdysseyHistoryPolicy.BaselineOldSave(
                null, visibleHome, 1234, policy);
            AssertTrue("old save initialized", baseline.historyInitialized);
            AssertTrue("old save distrusts first claims", !baseline.historyTrustworthyForFirstClaims);
            AssertEqual("feature start stored", 1234, baseline.featureStartTick);
            AssertTrue("visible home seeded", baseline.homeKeys.Contains("old-home"));
            AssertEqual("baseline emits no page id", 0, baseline.emittedJourneyIds.Count);

            baseline.historyTrustworthyForFirstClaims = true;
            OdysseyTravelHistorySnapshot repeated = OdysseyHistoryPolicy.BaselineOldSave(
                baseline, Location("other", "orbit", string.Empty, string.Empty), 9999, policy);
            AssertTrue("established trust not reset", repeated.historyTrustworthyForFirstClaims);
            AssertEqual("established feature tick preserved", 1234, repeated.featureStartTick);
            AssertTrue("repeated baseline does not add location", !repeated.visitedLocationKeys.Contains("other"));
        }

        private static void TestLifecycleCorrelationCommitAndDepartureHistory()
        {
            OdysseyPolicySnapshot policy = Policy();
            OdysseyTakeoffIntent intent = new OdysseyTakeoffIntent
            {
                engineId = "Thing_Engine7",
                shipName = "Wayfarer",
                origin = Location("home-a", "surface", "Biome_Frozen", string.Empty),
                selectedTargetKey = "orbit-b",
                captureTick = 100,
                launchQualityBand = OdysseyLaunchQualityTokens.Excellent,
                roughLanding = true,
                writers = new List<OdysseyWriterCandidate> { Writer("Pilot", "pilot", true, true, true) }
            };
            intent.origin.isPlayerHome = true;
            OdysseyTravelCommitObservation commit = new OdysseyTravelCommitObservation
            {
                shipStableId = "WorldObject_12",
                engineId = "Thing_Engine7",
                shipName = string.Empty,
                origin = Location("fallback-origin", "surface", string.Empty, string.Empty),
                destination = Location("orbit-b", "orbit", "Biome_Frozen", string.Empty),
                departureTick = 200,
                launchQualityBand = OdysseyLaunchQualityTokens.Poor,
                writers = new List<OdysseyWriterCandidate> { Writer("Crew", "crew", true, true, true) }
            };

            bool matched;
            OdysseyJourneySnapshot journey = OdysseyLifecyclePolicy.BuildJourney(
                commit, intent, policy, out matched);
            AssertTrue("exact timely takeoff intent correlates", matched);
            AssertEqual("commit owns stable journey id", "odyssey-journey|WorldObject_12|200", journey.journeyId);
            AssertEqual("matched intent supplies missing ship name", "Wayfarer", journey.shipName);
            AssertEqual("matched intent preserves pre-despawn origin", "home-a", journey.origin.stableKey);
            AssertEqual("matched intent preserves launch quality", "excellent", journey.launchQualityBand);
            AssertTrue("matched intent preserves coarse rough flag", journey.roughLanding);
            AssertTrue("matched intent marks complete source", journey.sourceComplete);
            AssertEqual("matched intent writers win", "Pilot", journey.writers[0].pawnId);

            OdysseyTravelHistorySnapshot history = OdysseyHistoryPolicy.ApplyDeparture(
                new OdysseyTravelHistorySnapshot(), journey, policy);
            AssertTrue("post-feature departure initializes trustworthy history",
                history.historyInitialized && history.historyTrustworthyForFirstClaims);
            AssertEqual("departure increments committed count", 1, history.committedJourneyCount);
            AssertEqual("departure tick recorded", 200, history.lastDepartureTick);
            AssertTrue("departure seeds exact origin", history.visitedLocationKeys.Contains("home-a"));
            AssertTrue("departure seeds former home", history.homeKeys.Contains("home-a"));
            AssertEqual("departure does not fake landing observation", -1, history.lastLandingObservationTick);

            history.committedJourneyCount = int.MaxValue;
            OdysseyTravelHistorySnapshot saturated = OdysseyHistoryPolicy.ApplyDeparture(
                history, journey, policy);
            AssertEqual("departure count saturates instead of overflowing",
                int.MaxValue, saturated.committedJourneyCount);
        }

        private static void TestLifecycleFallbackExpiryAndLandingCorrelation()
        {
            OdysseyPolicySnapshot policy = Policy();
            policy.takeoffCorrelationTicks = 50;
            policy.landingCorrelationTicks = 75;
            OdysseyTakeoffIntent stale = new OdysseyTakeoffIntent
            {
                engineId = "Thing_Engine7",
                selectedTargetKey = "target",
                captureTick = 100,
                origin = Location("old-origin", "surface", string.Empty, string.Empty)
            };
            OdysseyTravelCommitObservation commit = new OdysseyTravelCommitObservation
            {
                shipStableId = "WorldObject_12",
                engineId = "Thing_Engine7",
                shipName = "Wayfarer",
                origin = Location("fallback-origin", "surface", string.Empty, string.Empty),
                destination = Location("target", "orbit", string.Empty, string.Empty),
                departureTick = 151,
                launchQualityBand = OdysseyLaunchQualityTokens.Ordinary,
                writers = new List<OdysseyWriterCandidate> { Writer("Crew", "crew", true, true, true) }
            };

            bool matched;
            OdysseyJourneySnapshot fallback = OdysseyLifecyclePolicy.BuildJourney(
                commit, stale, policy, out matched);
            AssertTrue("expired takeoff intent rejected", !matched);
            AssertEqual("fallback uses commit origin", "fallback-origin", fallback.origin.stableKey);
            AssertTrue("fallback is explicitly incomplete", !fallback.sourceComplete);
            AssertEqual("fallback uses live commit writer", "Crew", fallback.writers[0].pawnId);

            stale.captureTick = 150;
            stale.selectedTargetKey = "other-target";
            fallback = OdysseyLifecyclePolicy.BuildJourney(commit, stale, policy, out matched);
            AssertTrue("wrong target intent rejected", !matched);

            OdysseyPendingLanding pending = new OdysseyPendingLanding
            {
                shipStableId = "WorldObject_12",
                captureTick = 1000,
                destination = Location("target", "orbit", string.Empty, string.Empty)
            };
            AssertTrue("timely exact pending landing correlates",
                OdysseyLifecyclePolicy.PendingLandingMatches(pending, "WorldObject_12", 1075, policy));
            AssertTrue("wrong ship pending landing rejected",
                !OdysseyLifecyclePolicy.PendingLandingMatches(pending, "WorldObject_13", 1075, policy));
            AssertTrue("expired pending landing rejected",
                !OdysseyLifecyclePolicy.PendingLandingMatches(pending, "WorldObject_12", 1076, policy));
            AssertTrue("backward transient time expires safely",
                OdysseyLifecyclePolicy.IsExpired(100, 99, 1000));
        }

        private static void TestBoundedContextFormatting()
        {
            OdysseyPolicySnapshot policy = Policy();
            policy.maximumContextCharacters = 260;
            policy.maximumContextValueCharacters = 40;
            OdysseyJourneySnapshot journey = Journey(100, true);
            journey.shipStableId = "SecretShipId";
            journey.journeyId = "odyssey-journey|SecretShipId|100";
            journey.shipName = "The Ark; raw_key=bad\nSecond line";
            journey.launchQualityBand = "excellent";
            journey.roughLanding = true;
            journey.origin.visibleLabel = "Old Home";
            journey.destination = Location("SecretTile42", "orbit", "Biome_Frozen", "Site_Mechhive");
            journey.destination.visibleLabel = "The Silent Platform";
            journey.destination.biomeLabel = "Frozen orbit";
            journey.destination.siteLabel = "Visible station";
            OdysseyLandingPlan plan = OdysseyLandingPolicy.Plan(
                Observation(journey, 70000, true), History(true), policy);

            string context = OdysseyContextFormatter.FormatLanding(
                journey, journey.destination, plan, "Pilot", policy);
            AssertTrue("context bounded", context.Length <= 260);
            AssertContains("journey marker", context, "odyssey_journey=true");
            AssertContains("landing phase", context, "journey_phase=landing");
            AssertContains("primary reason", context, "journey_reason=major_destination");
            AssertContains("POV role", context, "pov_journey_role=pilot");
            AssertTrue("stable ship id omitted", context.IndexOf("SecretShipId", StringComparison.Ordinal) < 0);
            AssertTrue("tile key omitted", context.IndexOf("SecretTile42", StringComparison.Ordinal) < 0);
            AssertTrue("ticks omitted", context.IndexOf("70000", StringComparison.Ordinal) < 0);
            AssertTrue("context injection sanitized",
                context.IndexOf("raw_key=bad", StringComparison.Ordinal) < 0
                && context.IndexOf('\n') < 0);
            AssertEqual("value cleaner", "a b c", OdysseyContextFormatter.CleanValue("a;b=c", 50));
        }

        private static OdysseyPolicySnapshot Policy()
        {
            OdysseyPolicySnapshot policy = OdysseyPolicySnapshot.CreateDefault();
            policy.biomeCategories.Add(new OdysseyLocationCategoryRule
            {
                defName = "Biome_Frozen",
                categoryToken = "frozen"
            });
            policy.siteCategories.Add(new OdysseyLocationCategoryRule
            {
                defName = "Site_Mechhive",
                categoryToken = "mechhive",
                majorDestination = true
            });
            return policy;
        }

        private static OdysseyTravelHistorySnapshot History(bool trustworthy)
        {
            return new OdysseyTravelHistorySnapshot
            {
                historyInitialized = true,
                historyTrustworthyForFirstClaims = trustworthy,
                featureStartTick = 1,
                committedJourneyCount = 1
            };
        }

        private static OdysseyJourneySnapshot Journey(int departureTick, bool withWriters)
        {
            OdysseyJourneySnapshot journey = new OdysseyJourneySnapshot
            {
                shipStableId = "Ship_7",
                shipName = "The Ark",
                departureTick = departureTick,
                origin = Location("home-a", "surface", "Biome_Temperate", string.Empty),
                destination = Location("destination", "surface", string.Empty, string.Empty),
                sourceComplete = true,
                launchQualityBand = OdysseyLaunchQualityTokens.Ordinary
            };
            journey.journeyId = OdysseyArcKeys.Journey(journey.shipStableId, departureTick);
            if (withWriters)
            {
                journey.writers.Add(Writer("Crew_B", "crew", true, true, true));
                journey.writers.Add(Writer("Copilot", "copilot", true, true, true));
                journey.writers.Add(Writer("Pilot", "pilot", true, true, true));
            }

            return journey;
        }

        private static OdysseyLandingObservation Observation(
            OdysseyJourneySnapshot journey,
            int landingTick,
            bool groupEnabled)
        {
            return new OdysseyLandingObservation
            {
                journey = journey,
                destination = journey.destination,
                landingTick = landingTick,
                groupEnabled = groupEnabled,
                writers = new List<OdysseyWriterCandidate>(journey.writers)
            };
        }

        private static OdysseyLocationSnapshot Location(
            string stableKey,
            string layer,
            string biomeDefName,
            string siteDefName)
        {
            return new OdysseyLocationSnapshot
            {
                stableKey = stableKey,
                visibleLabel = stableKey + " visible",
                layerToken = layer,
                biomeDefName = biomeDefName,
                biomeLabel = biomeDefName,
                siteDefName = siteDefName,
                siteLabel = siteDefName,
                visible = true
            };
        }

        private static OdysseyWriterCandidate Writer(
            string pawnId,
            string role,
            bool eligible,
            bool present,
            bool hasDiary)
        {
            return new OdysseyWriterCandidate
            {
                pawnId = pawnId,
                displayName = pawnId + " Name",
                roleToken = role,
                eligible = eligible,
                present = present,
                hasDiary = hasDiary
            };
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

        private static void AssertContains(string label, string value, string fragment)
        {
            AssertTrue(label, value != null && value.IndexOf(fragment, StringComparison.Ordinal) >= 0);
        }

        private static void AssertEqual<T>(string label, T expected, T actual)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    "FAILED: " + label + " expected '" + expected + "' but got '" + actual + "'.");
            }
        }
    }
}

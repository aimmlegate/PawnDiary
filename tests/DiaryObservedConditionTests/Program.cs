// Pure unit tests for the observed-condition lifecycle diff (ObservedConditionPolicy.Plan), the brain
// of Plan 12. These exercise start/refresh/end debounce, duplicate-start prevention, save/load
// rediscovery, stale-Def dropping, and per-map / per-pawn independence WITHOUT RimWorld assemblies.
// Run via: `dotnet run --project tests/DiaryObservedConditionTests/DiaryObservedConditionTests.csproj`
// (exit code 0 = pass).
using System;
using System.Collections.Generic;
using PawnDiary;

namespace DiaryObservedConditionTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestStartDebounceHoldsThenRecords();
            TestNoDuplicateStartWhileObserved();
            TestRefreshUpdatesEvidence();
            TestEndDebounceHoldsThenRecords();
            TestPendingEndRecoversWhenObservedAgain();
            TestDropStaleWhenDefMissing();
            TestPendingNeverStartedDropsWhenMissing();
            TestReloadWithActiveObservationNoDuplicateStart();
            TestReloadWithStaleStateEndsCleanly();
            TestMultipleMapsDoNotCrossContaminate();
            TestPawnScopedDoNotAffectUnrelatedPawns();
            TestZeroStartDebounceRecordsImmediately();
            TestDuplicateObservationsSameScanIgnored();
            TestObservationForUnknownDefIgnored();
            TestPromptActivityStopsAfterMissingDebounce();
            TestPromptActivitySkipsUnstartedAndEndedRows();
            TestPollutionThresholdEntryEscalationAndReclamation();
            TestExclusiveFamilyKeepsHighestBandPerMap();
            TestPollutionContractDefaultsPreserveExistingDefs();
            TestDeterministicCappedPollutionWitnesses();
            TestZeroWitnessCapPreservesExistingFanOut();
            TestUnavailablePollutionProducesNoBaselineRows();
            TestOldSavePollutionBaselineIsSilent();
            TestPollutionBaselinePreservesSavedLifecycleProgress();

            Console.WriteLine("DiaryObservedConditionTests passed " + assertions + " assertions.");
            return 0;
        }

        // A new observation inside the start debounce stays pending (no page), then records once it has
        // persisted long enough — proving ticks gate the start, never trigger it instantly.
        private static void TestStartDebounceHoldsThenRecords()
        {
            List<ObservedConditionDefSnapshot> defs = Defs(Def("threat", 100, 0));

            // First scan at tick 1000: brand new -> StartPending, no page.
            ObservedConditionPlan first = ObservedConditionPolicy.Plan(
                1000, States(), Observations(Obs("threat")), defs);
            ObservedConditionDecision d1 = Single(first);
            AssertEqual("first scan is StartPending", ObservedConditionDecisionKind.StartPending, d1.kind);
            AssertEqual("StartPending records no page", false, d1.recordPage);
            AssertEqual("StartPending not yet started", false, d1.state.startRecorded);
            AssertEqual("StartPending anchors firstObservedTick", 1000, d1.state.firstObservedTick);

            // Second scan still inside debounce (50 < 100): refresh, still pending.
            ObservedConditionPlan second = ObservedConditionPolicy.Plan(
                1050, States(d1.state), Observations(Obs("threat")), defs);
            ObservedConditionDecision d2 = Single(second);
            AssertEqual("inside debounce stays unstarted", false, d2.state.startRecorded);
            AssertEqual("inside debounce records no page", false, d2.recordPage);

            // Third scan at the debounce boundary (100 >= 100): start is now real.
            ObservedConditionPlan third = ObservedConditionPolicy.Plan(
                1100, States(d2.state), Observations(Obs("threat")), defs);
            ObservedConditionDecision d3 = Single(third);
            AssertEqual("boundary records start", ObservedConditionDecisionKind.StartRecorded, d3.kind);
            AssertEqual("StartRecorded writes a page", true, d3.recordPage);
            AssertEqual("StartRecorded marks started", true, d3.state.startRecorded);
        }

        // Once a start is recorded, continuing to observe never re-records it.
        private static void TestNoDuplicateStartWhileObserved()
        {
            List<ObservedConditionDefSnapshot> defs = Defs(Def("threat", 0, 0));
            ObservedConditionStateSnapshot started = Started("threat", firstObserved: 0);

            ObservedConditionPlan plan = ObservedConditionPolicy.Plan(
                9999, States(started), Observations(Obs("threat")), defs);
            ObservedConditionDecision d = Single(plan);
            AssertEqual("still observed -> Refresh", ObservedConditionDecisionKind.Refresh, d.kind);
            AssertEqual("no duplicate start page", false, d.recordPage);
            AssertEqual("stays started", true, d.state.startRecorded);
        }

        // A refresh copies the latest evidence and clears any pending-missing marker.
        private static void TestRefreshUpdatesEvidence()
        {
            List<ObservedConditionDefSnapshot> defs = Defs(Def("evidence", 0, 500));
            ObservedConditionStateSnapshot started = Started("evidence", firstObserved: 0);
            started.lastSeenEvidenceLabel = "one sample";
            started.lastSeenEvidenceCount = 1;
            started.firstMissingTick = 4000; // had briefly gone missing

            ObservedConditionObservation obs = Obs("evidence");
            obs.evidenceLabel = "three samples";
            obs.evidenceDefName = "GrayFleshSample";
            obs.evidenceCount = 3;

            ObservedConditionDecision d = Single(
                ObservedConditionPolicy.Plan(5000, States(started), Observations(obs), defs));
            AssertEqual("re-observed -> Refresh", ObservedConditionDecisionKind.Refresh, d.kind);
            AssertEqual("evidence label updated", "three samples", d.state.lastSeenEvidenceLabel);
            AssertEqual("evidence count updated", 3, d.state.lastSeenEvidenceCount);
            AssertEqual("lastObservedTick updated", 5000, d.state.lastObservedTick);
            AssertEqual("pending-missing cleared on re-observe", -1, d.state.firstMissingTick);
        }

        // A condition that stops being observed holds for the end debounce, then records its end.
        private static void TestEndDebounceHoldsThenRecords()
        {
            List<ObservedConditionDefSnapshot> defs = Defs(Def("threat", 0, 200));
            ObservedConditionStateSnapshot started = Started("threat", firstObserved: 0);

            // First missing scan: anchor firstMissingTick, no end yet.
            ObservedConditionDecision d1 = Single(
                ObservedConditionPolicy.Plan(2000, States(started), Observations(), defs));
            AssertEqual("first missing -> EndPending", ObservedConditionDecisionKind.EndPending, d1.kind);
            AssertEqual("EndPending anchors firstMissingTick", 2000, d1.state.firstMissingTick);
            AssertEqual("EndPending does not remove", false, d1.removeState);

            // Inside the end debounce (100 < 200): still pending.
            ObservedConditionDecision d2 = Single(
                ObservedConditionPolicy.Plan(2100, States(d1.state), Observations(), defs));
            AssertEqual("inside end debounce stays pending", ObservedConditionDecisionKind.EndPending, d2.kind);

            // At the boundary (200 >= 200): record end and remove.
            ObservedConditionDecision d3 = Single(
                ObservedConditionPolicy.Plan(2200, States(d2.state), Observations(), defs));
            AssertEqual("boundary records end", ObservedConditionDecisionKind.EndRecorded, d3.kind);
            AssertEqual("EndRecorded writes a page", true, d3.recordPage);
            AssertEqual("EndRecorded removes the row", true, d3.removeState);
        }

        // If the condition reappears before the end debounce elapses, the pending end is cancelled.
        private static void TestPendingEndRecoversWhenObservedAgain()
        {
            List<ObservedConditionDefSnapshot> defs = Defs(Def("threat", 0, 200));
            ObservedConditionStateSnapshot pendingEnd = Started("threat", firstObserved: 0);
            pendingEnd.firstMissingTick = 2000;

            ObservedConditionDecision d = Single(
                ObservedConditionPolicy.Plan(2100, States(pendingEnd), Observations(Obs("threat")), defs));
            AssertEqual("reappear -> Refresh", ObservedConditionDecisionKind.Refresh, d.kind);
            AssertEqual("reappear cancels pending end", -1, d.state.firstMissingTick);
            AssertEqual("reappear writes no page", false, d.recordPage);
        }

        // A saved condition whose Def is gone (disabled/removed) is dropped silently, no end page.
        private static void TestDropStaleWhenDefMissing()
        {
            List<ObservedConditionDefSnapshot> noDefs = Defs(); // Def removed/disabled.
            ObservedConditionStateSnapshot started = Started("threat", firstObserved: 0);

            ObservedConditionDecision d = Single(
                ObservedConditionPolicy.Plan(3000, States(started), Observations(), noDefs));
            AssertEqual("missing Def -> DropStale", ObservedConditionDecisionKind.DropStale, d.kind);
            AssertEqual("DropStale writes no page", false, d.recordPage);
            AssertEqual("DropStale removes the row", true, d.removeState);
        }

        // A condition that never passed its start debounce just drops when it goes missing — no end page.
        private static void TestPendingNeverStartedDropsWhenMissing()
        {
            List<ObservedConditionDefSnapshot> defs = Defs(Def("threat", 500, 0));
            ObservedConditionStateSnapshot pending = new ObservedConditionStateSnapshot
            {
                conditionDefName = "threat",
                conditionKey = "threat",
                scope = ObservedConditionScope.Map,
                mapUniqueId = 1,
                subjectPawnId = string.Empty,
                firstObservedTick = 0,
                lastObservedTick = 10,
                firstMissingTick = -1,
                startRecorded = false
            };

            ObservedConditionDecision d = Single(
                ObservedConditionPolicy.Plan(100, States(pending), Observations(), defs));
            AssertEqual("never-started missing -> DropStale", ObservedConditionDecisionKind.DropStale, d.kind);
            AssertEqual("no spurious end page", false, d.recordPage);
        }

        // Reload mid-state: a persisted started condition that is still observed refreshes, it never
        // re-announces its start. This is the core save/load guarantee.
        private static void TestReloadWithActiveObservationNoDuplicateStart()
        {
            List<ObservedConditionDefSnapshot> defs = Defs(Def("threat", 2500, 0));
            // Loaded from disk: start already recorded in a previous session.
            ObservedConditionStateSnapshot loaded = Started("threat", firstObserved: 0);

            ObservedConditionDecision d = Single(
                ObservedConditionPolicy.Plan(123456, States(loaded), Observations(Obs("threat")), defs));
            AssertEqual("reload + still active -> Refresh", ObservedConditionDecisionKind.Refresh, d.kind);
            AssertEqual("reload does not re-announce start", false, d.recordPage);
        }

        // Reload of a condition that actually ended while saved: the next scans see it missing and end
        // it cleanly after the debounce.
        private static void TestReloadWithStaleStateEndsCleanly()
        {
            List<ObservedConditionDefSnapshot> defs = Defs(Def("threat", 0, 100));
            ObservedConditionStateSnapshot loaded = Started("threat", firstObserved: 0);
            loaded.firstMissingTick = -1; // looked active when saved

            ObservedConditionDecision d1 = Single(
                ObservedConditionPolicy.Plan(7000, States(loaded), Observations(), defs));
            AssertEqual("stale reload first scan -> EndPending", ObservedConditionDecisionKind.EndPending, d1.kind);

            ObservedConditionDecision d2 = Single(
                ObservedConditionPolicy.Plan(7100, States(d1.state), Observations(), defs));
            AssertEqual("stale reload ends after debounce", ObservedConditionDecisionKind.EndRecorded, d2.kind);
            AssertEqual("stale reload end removes row", true, d2.removeState);
        }

        // The same Def active on two maps is two independent conditions: ending one leaves the other.
        private static void TestMultipleMapsDoNotCrossContaminate()
        {
            List<ObservedConditionDefSnapshot> defs = Defs(Def("threat", 0, 0));
            ObservedConditionStateSnapshot map1 = Started("threat", firstObserved: 0, mapId: 1);
            ObservedConditionStateSnapshot map2 = Started("threat", firstObserved: 0, mapId: 2);

            // Only map 1 still observes the threat.
            ObservedConditionPlan plan = ObservedConditionPolicy.Plan(
                500, States(map1, map2),
                Observations(ObsMap("threat", 1)), defs);
            AssertEqual("two maps -> two decisions", 2, plan.decisions.Count);

            ObservedConditionDecision forMap1 = ForIdentity(plan, "threat", ObservedConditionScope.Map, 1, "");
            ObservedConditionDecision forMap2 = ForIdentity(plan, "threat", ObservedConditionScope.Map, 2, "");
            AssertEqual("observed map stays (Refresh)", ObservedConditionDecisionKind.Refresh, forMap1.kind);
            AssertEqual("unobserved map ends", ObservedConditionDecisionKind.EndRecorded, forMap2.kind);
        }

        // Pawn-scoped conditions are keyed by subject pawn: one pawn's condition ending never touches
        // another pawn's identical condition.
        private static void TestPawnScopedDoNotAffectUnrelatedPawns()
        {
            List<ObservedConditionDefSnapshot> defs = Defs(Def("hediff", 0, 0));
            ObservedConditionStateSnapshot pawnA = StartedPawn("hediff", "A");
            ObservedConditionStateSnapshot pawnB = StartedPawn("hediff", "B");

            ObservedConditionPlan plan = ObservedConditionPolicy.Plan(
                800, States(pawnA, pawnB),
                Observations(ObsPawn("hediff", "A")), defs);
            AssertEqual("two pawns -> two decisions", 2, plan.decisions.Count);

            ObservedConditionDecision forA = ForIdentity(plan, "hediff", ObservedConditionScope.Pawn, -1, "A");
            ObservedConditionDecision forB = ForIdentity(plan, "hediff", ObservedConditionScope.Pawn, -1, "B");
            AssertEqual("observed pawn stays (Refresh)", ObservedConditionDecisionKind.Refresh, forA.kind);
            AssertEqual("unobserved pawn ends", ObservedConditionDecisionKind.EndRecorded, forB.kind);
        }

        // Zero start debounce means the first observation immediately records the start.
        private static void TestZeroStartDebounceRecordsImmediately()
        {
            List<ObservedConditionDefSnapshot> defs = Defs(Def("threat", 0, 0));
            ObservedConditionDecision d = Single(
                ObservedConditionPolicy.Plan(10, States(), Observations(Obs("threat")), defs));
            AssertEqual("zero debounce -> StartRecorded", ObservedConditionDecisionKind.StartRecorded, d.kind);
            AssertEqual("zero debounce writes a page", true, d.recordPage);
        }

        // Two observations of the same identity in one scan collapse to a single decision.
        private static void TestDuplicateObservationsSameScanIgnored()
        {
            List<ObservedConditionDefSnapshot> defs = Defs(Def("threat", 0, 0));
            ObservedConditionPlan plan = ObservedConditionPolicy.Plan(
                10, States(), Observations(Obs("threat"), Obs("threat")), defs);
            AssertEqual("duplicate observations -> one decision", 1, plan.decisions.Count);
        }

        // An observation whose Def is not enabled/present produces no decision.
        private static void TestObservationForUnknownDefIgnored()
        {
            List<ObservedConditionDefSnapshot> defs = Defs(Def("known", 0, 0));
            ObservedConditionPlan plan = ObservedConditionPolicy.Plan(
                10, States(), Observations(Obs("unknown")), defs);
            AssertEqual("unknown-Def observation ignored", 0, plan.decisions.Count);
        }

        // Prompt bias may stay during the missing debounce, but must stop at the same boundary where
        // the lifecycle policy considers the condition ended. This protects optional end-page retries:
        // a saved row can remain retryable without keeping the LLM in the resolved event's tone forever.
        private static void TestPromptActivityStopsAfterMissingDebounce()
        {
            AssertEqual(
                "still observed condition biases prompts",
                true,
                ObservedConditionPromptActivity.IsPromptActive(
                    startRecorded: true,
                    endRecorded: false,
                    firstMissingTick: -1,
                    now: 1000,
                    endDebounceTicks: 500));

            AssertEqual(
                "missing inside debounce still biases prompts",
                true,
                ObservedConditionPromptActivity.IsPromptActive(
                    startRecorded: true,
                    endRecorded: false,
                    firstMissingTick: 1000,
                    now: 1499,
                    endDebounceTicks: 500));

            AssertEqual(
                "missing at debounce boundary no longer biases prompts",
                false,
                ObservedConditionPromptActivity.IsPromptActive(
                    startRecorded: true,
                    endRecorded: false,
                    firstMissingTick: 1000,
                    now: 1500,
                    endDebounceTicks: 500));

            AssertEqual(
                "zero end debounce stops prompt bias immediately",
                false,
                ObservedConditionPromptActivity.IsPromptActive(
                    startRecorded: true,
                    endRecorded: false,
                    firstMissingTick: 1000,
                    now: 1000,
                    endDebounceTicks: 0));
        }

        private static void TestPromptActivitySkipsUnstartedAndEndedRows()
        {
            AssertEqual(
                "pending start does not bias prompts",
                false,
                ObservedConditionPromptActivity.IsPromptActive(
                    startRecorded: false,
                    endRecorded: false,
                    firstMissingTick: -1,
                    now: 1000,
                    endDebounceTicks: 500));

            AssertEqual(
                "ended row does not bias prompts",
                false,
                ObservedConditionPromptActivity.IsPromptActive(
                    startRecorded: true,
                    endRecorded: true,
                    firstMissingTick: 1000,
                    now: 1100,
                    endDebounceTicks: 500));
        }

        private static void TestPollutionThresholdEntryEscalationAndReclamation()
        {
            ObservedConditionDefSnapshot meaningful = ObservedConditionDefSnapshot.Create(
                "meaningful", 0, 0, 0.10f, -1f, "pollution", 1, 2);
            ObservedConditionDefSnapshot severe = ObservedConditionDefSnapshot.Create(
                "severe", 0, 0, 0.35f, -1f, "pollution", 2, 2);
            ObservedConditionDefSnapshot critical = ObservedConditionDefSnapshot.Create(
                "critical", 0, 0, 0.65f, -1f, "pollution", 3, 2);
            List<ObservedConditionDefSnapshot> defs = Defs(meaningful, severe, critical);

            ObservedConditionPlan entry = ObservedConditionPolicy.Plan(
                100, States(), PollutionObservations(0.20f), defs);
            AssertEqual("meaningful threshold enters once", 1, entry.decisions.Count);
            AssertEqual("meaningful entry records start",
                ObservedConditionDecisionKind.StartRecorded, entry.decisions[0].kind);

            ObservedConditionPlan escalation = ObservedConditionPolicy.Plan(
                200, States(entry.decisions[0].state), PollutionObservations(0.40f), defs);
            AssertEqual("meaningful remains while severe starts", 2, escalation.decisions.Count);
            AssertEqual("severe crossing is a new start",
                ObservedConditionDecisionKind.StartRecorded,
                ForIdentity(escalation, "severe", ObservedConditionScope.Map, 1, "").kind);

            List<ObservedConditionStateSnapshot> active = NextStates(escalation);
            ObservedConditionPlan reclamation = ObservedConditionPolicy.Plan(
                300, active, PollutionObservations(0.05f), defs);
            AssertEqual("both active threshold rows end below meaningful", 2, reclamation.decisions.Count);
            AssertEqual("meaningful end is the reclamation edge",
                ObservedConditionDecisionKind.EndRecorded,
                ForIdentity(reclamation, "meaningful", ObservedConditionScope.Map, 1, "").kind);
        }

        private static void TestExclusiveFamilyKeepsHighestBandPerMap()
        {
            List<ObservedConditionInfluenceRow> rows = new List<ObservedConditionInfluenceRow>
            {
                Influence(0, "meaningful", "pollution", 1, 7),
                Influence(1, "severe", "pollution", 2, 7),
                Influence(2, "critical", "pollution", 3, 7),
                Influence(3, "ordinary", string.Empty, 0, 7),
                Influence(4, "severe-map-two", "pollution", 2, 8)
            };
            List<int> selected = ObservedConditionExclusiveFamilyPolicy.SelectSourceIndexes(rows);
            AssertEqual("highest family row plus ordinary and other map survive", 3, selected.Count);
            AssertEqual("critical wins map one family", true, selected.Contains(2));
            AssertEqual("ordinary condition is unchanged", true, selected.Contains(3));
            AssertEqual("family is independent on map two", true, selected.Contains(4));
        }

        private static void TestPollutionContractDefaultsPreserveExistingDefs()
        {
            ObservedConditionDefSnapshot legacy = ObservedConditionDefSnapshot.Create("legacy", 10, 20);
            AssertEqual("legacy minimum defaults neutral", 0f, legacy.minPollutionFraction);
            AssertEqual("legacy maximum defaults absent", -1f, legacy.maxPollutionFraction);
            AssertEqual("legacy family defaults empty", string.Empty, legacy.exclusiveFamilyKey);
            AssertEqual("legacy severity defaults zero", 0, legacy.severityRank);
            AssertEqual("legacy witness cap defaults unlimited", 0, legacy.maxPagePawns);
        }

        private static void TestDeterministicCappedPollutionWitnesses()
        {
            List<ObservedConditionWitnessCandidate> candidates = new List<ObservedConditionWitnessCandidate>
            {
                Witness("Pawn_A", false),
                Witness("Pawn_B", true),
                Witness("Pawn_C", false),
                Witness("Pawn_D", true)
            };
            List<string> first = ObservedConditionWitnessPolicy.SelectPawnIds(
                "critical", 9, 12345, candidates, 2);
            List<string> second = ObservedConditionWitnessPolicy.SelectPawnIds(
                "critical", 9, 12345, candidates, 2);
            AssertEqual("pollution witnesses are capped", 2, first.Count);
            AssertEqual("same detached facts select deterministically",
                string.Join("|", first.ToArray()), string.Join("|", second.ToArray()));
            AssertEqual("visible health relevance fills the bounded sample first",
                true, first.Contains("Pawn_B") && first.Contains("Pawn_D"));
        }

        private static void TestZeroWitnessCapPreservesExistingFanOut()
        {
            List<ObservedConditionWitnessCandidate> candidates = new List<ObservedConditionWitnessCandidate>
            {
                Witness("Pawn_C", false),
                Witness("Pawn_A", true),
                Witness("Pawn_B", false)
            };
            List<string> all = ObservedConditionWitnessPolicy.SelectPawnIds(
                "legacy", 1, 77, candidates, 0);
            AssertEqual("zero cap keeps every existing recipient", 3, all.Count);
            AssertEqual("zero cap keeps existing input order",
                "Pawn_C|Pawn_A|Pawn_B", string.Join("|", all.ToArray()));
        }

        private static void TestUnavailablePollutionProducesNoBaselineRows()
        {
            AssertEqual("invalid unavailable fraction is inactive", false,
                ObservedConditionPollutionPolicy.IsActive(float.NaN, 0.1f, -1f));
            AssertEqual("no available map observations create no baseline rows", 0,
                ObservedConditionBaselinePolicy.BuildStartedRows(100, Observations()).Count);
        }

        private static void TestOldSavePollutionBaselineIsSilent()
        {
            List<ObservedConditionStateSnapshot> rows =
                ObservedConditionBaselinePolicy.BuildStartedRows(
                    400, Observations(ObsMap("meaningful", 12), ObsMap("severe", 12)));
            AssertEqual("old-save baseline retains all active threshold rows", 2, rows.Count);
            AssertEqual("baseline marks current pollution already started", true,
                rows[0].startRecorded && rows[1].startRecorded);
            AssertEqual("baseline never marks a catch-up end", false,
                rows[0].endRecorded || rows[1].endRecorded);
            AssertEqual("baseline anchors current transition-free tick", 400, rows[0].firstObservedTick);
        }

        private static void TestPollutionBaselinePreservesSavedLifecycleProgress()
        {
            ObservedConditionStateSnapshot pendingStart =
                Started("meaningful", firstObserved: 100, mapId: 12);
            pendingStart.startRecorded = false;
            pendingStart.lastObservedTick = 250;

            ObservedConditionStateSnapshot pendingEnd =
                Started("severe", firstObserved: 20, mapId: 12);
            pendingEnd.lastObservedTick = 300;
            pendingEnd.firstMissingTick = 350;

            List<ObservedConditionStateSnapshot> rows =
                ObservedConditionBaselinePolicy.MergeStartedRows(
                    500,
                    States(pendingStart, pendingEnd),
                    Observations(
                        ObsMap("meaningful", 12),
                        ObsMap("critical", 12)));

            AssertEqual("load migration retains saved rows and adds only missing active bands", 3, rows.Count);
            AssertEqual("load migration preserves pending start flag", false, rows[0].startRecorded);
            AssertEqual("load migration preserves first-observed decay anchor", 100, rows[0].firstObservedTick);
            AssertEqual("load migration preserves pending end debounce anchor", 350, rows[1].firstMissingTick);
            AssertEqual("newly discovered old-save band is silently started", true, rows[2].startRecorded);
            AssertEqual("newly discovered old-save band anchors at migration tick", 500, rows[2].firstObservedTick);
        }

        // ----- builders / helpers -----

        private static ObservedConditionDefSnapshot Def(string key, int startDeb, int endDeb)
        {
            return ObservedConditionDefSnapshot.Create(key, startDeb, endDeb);
        }

        private static List<ObservedConditionDefSnapshot> Defs(params ObservedConditionDefSnapshot[] defs)
        {
            return new List<ObservedConditionDefSnapshot>(defs);
        }

        private static ObservedConditionObservation Obs(string key)
        {
            return ObsMap(key, 1);
        }

        private static ObservedConditionObservation ObsMap(string key, int mapId)
        {
            return new ObservedConditionObservation
            {
                conditionDefName = key,
                conditionKey = key,
                scope = ObservedConditionScope.Map,
                mapUniqueId = mapId,
                subjectPawnId = string.Empty
            };
        }

        private static ObservedConditionObservation ObsPawn(string key, string pawnId)
        {
            return new ObservedConditionObservation
            {
                conditionDefName = key,
                conditionKey = key,
                scope = ObservedConditionScope.Pawn,
                mapUniqueId = -1,
                subjectPawnId = pawnId
            };
        }

        private static List<ObservedConditionObservation> PollutionObservations(float fraction)
        {
            List<ObservedConditionObservation> observations = new List<ObservedConditionObservation>();
            if (ObservedConditionPollutionPolicy.IsActive(fraction, 0.10f, -1f))
            {
                observations.Add(ObsMap("meaningful", 1));
            }
            if (ObservedConditionPollutionPolicy.IsActive(fraction, 0.35f, -1f))
            {
                observations.Add(ObsMap("severe", 1));
            }
            if (ObservedConditionPollutionPolicy.IsActive(fraction, 0.65f, -1f))
            {
                observations.Add(ObsMap("critical", 1));
            }

            return observations;
        }

        private static List<ObservedConditionStateSnapshot> NextStates(ObservedConditionPlan plan)
        {
            List<ObservedConditionStateSnapshot> states = new List<ObservedConditionStateSnapshot>();
            for (int i = 0; i < plan.decisions.Count; i++)
            {
                if (!plan.decisions[i].removeState)
                {
                    states.Add(plan.decisions[i].state);
                }
            }

            return states;
        }

        private static ObservedConditionInfluenceRow Influence(int index, string key, string family,
            int severity, int mapId)
        {
            return new ObservedConditionInfluenceRow
            {
                sourceIndex = index,
                conditionKey = key,
                exclusiveFamilyKey = family,
                severityRank = severity,
                mapUniqueId = mapId
            };
        }

        private static ObservedConditionWitnessCandidate Witness(string pawnId, bool relevant)
        {
            return new ObservedConditionWitnessCandidate
            {
                pawnId = pawnId,
                hasVisibleRelevantHealth = relevant
            };
        }

        private static List<ObservedConditionObservation> Observations(params ObservedConditionObservation[] obs)
        {
            return new List<ObservedConditionObservation>(obs);
        }

        private static List<ObservedConditionStateSnapshot> States(params ObservedConditionStateSnapshot[] states)
        {
            return new List<ObservedConditionStateSnapshot>(states);
        }

        private static ObservedConditionStateSnapshot Started(string key, int firstObserved, int mapId = 1)
        {
            return new ObservedConditionStateSnapshot
            {
                conditionDefName = key,
                conditionKey = key,
                scope = ObservedConditionScope.Map,
                mapUniqueId = mapId,
                subjectPawnId = string.Empty,
                firstObservedTick = firstObserved,
                lastObservedTick = firstObserved,
                firstMissingTick = -1,
                startRecorded = true
            };
        }

        private static ObservedConditionStateSnapshot StartedPawn(string key, string pawnId)
        {
            return new ObservedConditionStateSnapshot
            {
                conditionDefName = key,
                conditionKey = key,
                scope = ObservedConditionScope.Pawn,
                mapUniqueId = -1,
                subjectPawnId = pawnId,
                firstObservedTick = 0,
                lastObservedTick = 0,
                firstMissingTick = -1,
                startRecorded = true
            };
        }

        private static ObservedConditionDecision Single(ObservedConditionPlan plan)
        {
            AssertEqual("exactly one decision", 1, plan.decisions.Count);
            return plan.decisions[0];
        }

        private static ObservedConditionDecision ForIdentity(ObservedConditionPlan plan, string key,
            ObservedConditionScope scope, int mapId, string pawnId)
        {
            string identity = ObservedConditionStateSnapshot.Identity(key, scope, mapId, pawnId);
            for (int i = 0; i < plan.decisions.Count; i++)
            {
                if (plan.decisions[i].state.IdentityKey() == identity)
                {
                    return plan.decisions[i];
                }
            }

            throw new Exception("No decision for identity " + identity);
        }

        // ----- assertions -----

        private static void AssertEqual(string what, object expected, object actual)
        {
            assertions++;
            if (!Equals(expected, actual))
            {
                throw new Exception("FAIL: " + what + " — expected <" + expected + "> but was <" + actual + ">");
            }
        }
    }
}

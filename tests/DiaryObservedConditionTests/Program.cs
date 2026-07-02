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

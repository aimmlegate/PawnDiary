// Standalone coverage for the bounded runtime ledger and detached persistence audit.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PawnDiary;

namespace DiaryTelemetryTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestCountersKeepUsefulDimensions();
            TestRecentAnomaliesAreBounded();
            TestCounterCapacityIsBounded();
            TestConcurrentRecordingIsExact();
            TestStaticSessionReset();
            TestHealthyIntegrityFacts();
            TestIntegrityIssuesAndArchivedOwnership();

            Console.WriteLine("DiaryTelemetryTests passed " + assertions + " assertions.");
            return 0;
        }

        private static void TestCountersKeepUsefulDimensions()
        {
            DiaryTelemetrySessionState state = new DiaryTelemetrySessionState(16, 4);
            state.Record(
                DiaryTelemetryOutcome.EventRecorded,
                "dispatch.emit",
                "ThoughtSignal",
                "Thought",
                100,
                2);
            state.Record(
                DiaryTelemetryOutcome.EventRecorded,
                "dispatch.emit",
                "ThoughtSignal",
                "Thought",
                101,
                3);
            state.Record(
                DiaryTelemetryOutcome.SourceDuplicate,
                "dispatch.source_dedup",
                "ThoughtSignal",
                "Thought",
                102);

            DiaryTelemetrySnapshot snapshot = state.Snapshot();
            AssertEqual("two dimensional counter buckets", 2, snapshot.counters.Count);
            AssertEqual(
                "matching transition increments aggregate count",
                5L,
                FindCounter(snapshot, DiaryTelemetryOutcome.EventRecorded).count);
            AssertEqual(
                "normal duplicate is counted",
                1L,
                FindCounter(snapshot, DiaryTelemetryOutcome.SourceDuplicate).count);
            AssertEqual(
                "normal outcomes do not fill anomaly ring",
                0,
                snapshot.recentAnomalies.Count);
        }

        private static void TestRecentAnomaliesAreBounded()
        {
            DiaryTelemetrySessionState state = new DiaryTelemetrySessionState(16, 2);
            state.Record(
                DiaryTelemetryOutcome.DispatchException,
                "dispatch",
                "FirstSignal",
                null,
                10,
                1,
                "InvalidOperationException",
                "first");
            state.Record(
                DiaryTelemetryOutcome.LlmQueueFull,
                "llm.enqueue",
                "main",
                null,
                20,
                1,
                "full",
                "second");
            state.Record(
                DiaryTelemetryOutcome.IntegrityIssue,
                "pre_save",
                "persistence",
                null,
                30,
                4,
                "issues=4",
                "third");

            DiaryTelemetrySnapshot snapshot = state.Snapshot();
            AssertEqual("anomaly ring respects capacity", 2, snapshot.recentAnomalies.Count);
            AssertEqual(
                "oldest anomaly is evicted",
                DiaryTelemetryOutcome.LlmQueueFull,
                snapshot.recentAnomalies[0].outcome);
            AssertEqual(
                "newest anomaly retained",
                "third",
                snapshot.recentAnomalies[1].fingerprint);
            AssertEqual(
                "anomaly keeps aggregate issue amount",
                4L,
                snapshot.recentAnomalies[1].count);
        }

        private static void TestCounterCapacityIsBounded()
        {
            DiaryTelemetrySessionState state = new DiaryTelemetrySessionState(2, 4);
            state.Record(DiaryTelemetryOutcome.EventRecorded, "one", "a", null, 1);
            state.Record(DiaryTelemetryOutcome.SourceDuplicate, "two", "b", null, 2);
            state.Record(DiaryTelemetryOutcome.PolicyDropped, "three", "c", null, 3);

            DiaryTelemetrySnapshot snapshot = state.Snapshot();
            AssertTrue("counter dictionary never exceeds configured cap", snapshot.counters.Count <= 2);
            AssertEqual(
                "overflowed dimensions are still counted",
                2L,
                FindCounter(snapshot, DiaryTelemetryOutcome.TelemetryCounterCapacityReached).count);
        }

        private static void TestConcurrentRecordingIsExact()
        {
            DiaryTelemetrySessionState state = new DiaryTelemetrySessionState(8, 4);
            Task[] writers = new Task[8];
            for (int i = 0; i < writers.Length; i++)
            {
                writers[i] = Task.Run(() =>
                {
                    for (int j = 0; j < 1000; j++)
                    {
                        state.Record(
                            DiaryTelemetryOutcome.LlmQueueAccepted,
                            "llm.enqueue",
                            "main",
                            null,
                            -1);
                    }
                });
            }
            Task.WaitAll(writers);

            AssertEqual(
                "thread-safe counter has no lost increments",
                8000L,
                FindCounter(state.Snapshot(), DiaryTelemetryOutcome.LlmQueueAccepted).count);
        }

        private static void TestStaticSessionReset()
        {
            DiaryTelemetry.ResetSession();
            DiaryTelemetry.Record(
                DiaryTelemetryOutcome.EventRecorded,
                "dispatch.emit",
                "TestSignal");
            AssertEqual("static facade records in current session", 1, DiaryTelemetry.Snapshot().counters.Count);

            DiaryTelemetry.ResetSession();
            AssertEqual("session reset clears previous game counters", 0, DiaryTelemetry.Snapshot().counters.Count);
        }

        private static void TestHealthyIntegrityFacts()
        {
            DiaryIntegrityEventFact diaryEvent = Event("event-a", "pawn-a");
            DiaryIntegrityDiaryFact diary = Diary("pawn-a", "event-a");
            DiaryIntegrityReport report = DiaryIntegrityPolicy.Audit(
                new List<DiaryIntegrityEventFact> { diaryEvent },
                new List<DiaryIntegrityDiaryFact> { diary });

            AssertTrue("matching repository and owner ref is healthy", report.IsHealthy);
            AssertEqual("healthy audit has zero issues", 0, report.IssueCount);
        }

        private static void TestIntegrityIssuesAndArchivedOwnership()
        {
            DiaryIntegrityEventFact eventOne = Event("event-one", "pawn-one");
            DiaryIntegrityEventFact duplicate = Event("EVENT-ONE", "pawn-one");
            DiaryIntegrityEventFact archivedOwner = Event("event-two", "pawn-two");
            DiaryIntegrityEventFact ownerless = Event("event-three");
            DiaryIntegrityEventFact missingOwner = Event("event-four", "pawn-four");

            DiaryIntegrityDiaryFact blankPawn = Diary(" ", " ");
            DiaryIntegrityDiaryFact pawnOne =
                Diary("pawn-one", "event-one", "EVENT-ONE", "missing-event");
            DiaryIntegrityDiaryFact duplicatePawn = Diary("pawn-one");
            DiaryIntegrityDiaryFact pawnTwo = Diary("pawn-two");
            pawnTwo.archivedEventIds.Add("event-two");

            DiaryIntegrityReport report = DiaryIntegrityPolicy.Audit(
                new List<DiaryIntegrityEventFact>
                {
                    null,
                    Event(" "),
                    eventOne,
                    duplicate,
                    archivedOwner,
                    ownerless,
                    missingOwner
                },
                new List<DiaryIntegrityDiaryFact>
                {
                    null,
                    blankPawn,
                    pawnOne,
                    duplicatePawn,
                    pawnTwo
                });

            AssertEqual("null event rows detected", 1, report.nullEventRows);
            AssertEqual("blank event ids detected", 1, report.blankEventIds);
            AssertEqual("event id comparison matches repository casing", 1, report.duplicateEventIds);
            AssertEqual("null diary rows detected", 1, report.nullDiaryRows);
            AssertEqual("blank pawn ids detected", 1, report.blankPawnIds);
            AssertEqual("duplicate pawn diary ids detected", 1, report.duplicatePawnDiaryIds);
            AssertEqual("blank hot refs detected", 1, report.blankEventRefs);
            AssertEqual("case-insensitive duplicate refs detected", 1, report.duplicateEventRefs);
            AssertEqual("unknown hot refs detected", 1, report.danglingEventRefs);
            AssertEqual("unreferenced hot events detected", 3, report.orphanEvents);
            AssertEqual(
                "archive satisfies historical owner while truly missing owner is detected",
                1,
                report.missingOwnerRefs);
            AssertTrue("issue fixture is unhealthy", !report.IsHealthy);
        }

        private static DiaryIntegrityEventFact Event(string eventId, params string[] owners)
        {
            DiaryIntegrityEventFact fact = new DiaryIntegrityEventFact { eventId = eventId };
            if (owners != null)
            {
                fact.expectedOwnerPawnIds.AddRange(owners);
            }
            return fact;
        }

        private static DiaryIntegrityDiaryFact Diary(string pawnId, params string[] eventIds)
        {
            DiaryIntegrityDiaryFact fact = new DiaryIntegrityDiaryFact { pawnId = pawnId };
            if (eventIds != null)
            {
                fact.eventIds.AddRange(eventIds);
            }
            return fact;
        }

        private static DiaryTelemetryCounter FindCounter(
            DiaryTelemetrySnapshot snapshot,
            DiaryTelemetryOutcome outcome)
        {
            for (int i = 0; i < snapshot.counters.Count; i++)
            {
                if (snapshot.counters[i].outcome == outcome)
                {
                    return snapshot.counters[i];
                }
            }

            throw new InvalidOperationException("Counter not found: " + outcome);
        }

        private static void AssertTrue(string name, bool condition)
        {
            assertions++;
            if (!condition)
            {
                throw new InvalidOperationException("Assertion failed: " + name);
            }
        }

        private static void AssertEqual<T>(string name, T expected, T actual)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    "Assertion failed: " + name + ". Expected " + expected + ", got " + actual + ".");
            }
        }
    }
}

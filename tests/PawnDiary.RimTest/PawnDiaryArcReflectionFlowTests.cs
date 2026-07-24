// In-game flow tests for Pawn Diary's rare pawn life-arc reflection path (design/TEST_COVERAGE_PLAN.md §3, EVT-20).
//
// The live orchestration is DiaryGameComponent.ArbitrateReflectionsForPawn: its arc candidate snapshots
// the pawn's saved PawnArcScheduleState, asks the pure ArcReflectionSchedulePolicy whether cadence allows
// an entry, samples hot/archive diary pages through the pure ArcReflectionMemorySelector, and — when enough
// memories survive filtering and the coordinator selects it — submits an ArcReflectionSignal. That
// orchestrator is private and needs the
// storyteller clock, a colony scan, and the pawn's whole event graph, so (following the Phase 1 pattern) these
// tests drive its exact sub-units directly instead of reproducing the scan:
//   - the memory selector (filtering + dedup of seeded ArcMemoryCandidate rows, the production memory-context
//     builder) feeding the same ArcReflectionSignal the orchestrator submits, to prove one solo
//     PawnArcReflection page is produced with the filtered/deduped memory-count context;
//   - the schedule policy on a too-recent PawnArcScheduleState, to prove the year-cap and second-entry gap
//     gates block an arc;
//   - PawnArcScheduleState.MarkArcEntry / NormalizeForYear / the memory-shortfall backoff guard, to prove
//     recent-memory tracking + forced retry backoff update as the orchestrator relies on.
//
// PawnArcScheduleState is the exact type stored on PawnDiaryRecord.arcSchedule, so the schedule/tracking tests
// exercise the production state object; they mutate only local instances (never the test pawn's saved record),
// so there is no separate arc store to clean. The one test that emits a real page relies on the shared harness
// to remove the resulting DiaryEvent + diary index, and never enables per-pawn generation, so no LLM request
// can leave the game.
//
// Determinism (design/TEST_COVERAGE_PLAN.md §3): the selector is seeded with a fixed RNG seed and given candidates
// whose filtering outcome is unambiguous regardless of sample order; the schedule policy is fed an explicit
// ArcReflectionScheduleTuning built in code (independent of XML); the arc signal is enabled by forcing
// DiaryTuning.Current.arcReflectionEnabled, snapshotted and restored in failure-safe cleanup.
//
// Coverage-matrix ID (design/TEST_COVERAGE_PLAN.md §3): EVT-20 Arc reflection.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that Pawn Diary's arc-reflection memory selection filters and dedups its evidence and that the
    /// assembled arc signal records exactly one solo life-chapter page, that the cadence policy blocks an arc
    /// when the pawn's schedule is already full for the year or the second-entry gap has not elapsed, and that
    /// the per-pawn schedule state tracks recently used memories and a forced-retry backoff. These tests require
    /// a loaded game because the capture pipeline ignores events at the main menu.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryArcReflectionFlowTests
    {
        // A fixed, made-up calendar year for the pure selector/policy. The selector and policy are pure, so this
        // never has to match the loaded game's real year; a constant keeps the filtering outcome deterministic.
        private const int ArcYear = 5500;

        // Fixed RNG seed for the weighted memory sampler so the selected set is reproducible.
        private const int SelectionSeed = 1234;

        private static PawnDiaryRimTestScope scope;
        private static Pawn reflectingPawn;

        // The live tuning def whose arc-reflection master switch we force on, plus the value to restore.
        // DiaryTuning.Current returns the loaded Diary_Tuning def (or a shared fallback); either way it is the
        // exact instance ArcReflectionSignal.BuildContext reads, so forcing it enables the capture decision.
        private static DiaryTuningDef tuningDef;
        private static bool originalArcReflectionEnabled;

        /// <summary>
        /// Opens a scope with the Reflection group enabled (defName <c>reflection</c>, which claims
        /// <c>PawnArcReflection</c> via matchDefNames), creates one isolated generation-disabled colonist, and
        /// forces the arc-reflection signal switch on (restored in cleanup) so the emission gate is not random.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("reflection");
            reflectingPawn = scope.CreateAdultColonist();
            scope.SpawnAsLiveColonist(reflectingPawn);

            tuningDef = DiaryTuning.Current;
            originalArcReflectionEnabled = tuningDef.arcReflectionEnabled;
            tuningDef.arcReflectionEnabled = true;
            scope.RegisterCleanup(() =>
            {
                if (tuningDef != null)
                {
                    tuningDef.arcReflectionEnabled = originalArcReflectionEnabled;
                }
            });
        }

        /// <summary>
        /// Restores every mutation and audits that no test-owned event, diary, or log row survived — even when
        /// the test above threw partway through.
        /// </summary>
        [AfterEach]
        public static void TearDown()
        {
            try
            {
                scope?.TearDown();
            }
            finally
            {
                scope = null;
                reflectingPawn = null;
                tuningDef = null;
            }
        }

        /// <summary>
        /// EVT-20. Seeds a pool of arc memory candidates that includes a duplicate eventId, a prior reflection,
        /// a recently used memory, and a wrong-year memory; runs the production memory selector and asserts it
        /// keeps only the two valid unique pages; then submits the assembled ArcReflectionSignal and verifies the
        /// arc capture path records exactly one solo PawnArcReflection page whose context carries the
        /// filtered/deduped memory counts.
        /// </summary>
        [Test]
        public static void ArcReflectionEmitsSoloPageWithFilteredDedupedMemoryContext()
        {
            List<string> recentlyUsed = new List<string> { "arc-D" };
            List<ArcMemoryCandidate> candidates = new List<ArcMemoryCandidate>
            {
                NewCandidate("arc-A", ArcYear, "work", "work|A", "Raised a granary wall"),
                NewCandidate("arc-B", ArcYear, "combat", "combat|B", "Held the line against a raider"),
                // Duplicate eventId of arc-A: the selector dedups by eventId and must drop this.
                NewCandidate("arc-A", ArcYear, "work", "work|A", "Duplicate of the wall memory"),
                // A prior reflection page: excluded so an arc never quotes another arc.
                Reflection(NewCandidate("arc-C", ArcYear, "reflection", "reflection|C", "A look back over the year")),
                // Already used in a recent arc: excluded by the recently-used guard.
                NewCandidate("arc-D", ArcYear, "social", "social|D", "A conversation long since written"),
                // From a different year: excluded by the same-year filter.
                NewCandidate("arc-E", ArcYear - 1, "work", "work|E", "Something from last year"),
            };

            ArcMemorySelectionResult selection = ArcReflectionMemorySelector.Select(new ArcMemorySelectionRequest
            {
                candidates = candidates,
                recentlyUsedEventIds = recentlyUsed,
                currentYear = ArcYear,
                maxMemories = 8,
                minMemories = 1,
                sameDomainGroupCap = 2,
                seed = SelectionSeed,
            });

            // Filtering + dedup: only arc-A and arc-B survive the pool.
            PawnDiaryRimTestScope.Require(
                selection.candidateCount == 2,
                "Expected exactly two valid arc memory candidates after filtering, got " + selection.candidateCount + ".");
            PawnDiaryRimTestScope.Require(
                selection.hasEnoughMemories, "The filtered arc memory pool should satisfy the minimum-memory gate.");
            HashSet<string> selectedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < selection.selected.Count; i++)
            {
                PawnDiaryRimTestScope.Require(
                    selectedIds.Add(selection.selected[i].eventId),
                    "The arc memory selection contained a duplicate eventId.");
            }

            PawnDiaryRimTestScope.Require(
                selectedIds.Count == 2 && selectedIds.Contains("arc-A") && selectedIds.Contains("arc-B"),
                "The arc memory selection should contain exactly the two valid unique pages (arc-A, arc-B).");
            PawnDiaryRimTestScope.Require(
                !selectedIds.Contains("arc-C") && !selectedIds.Contains("arc-D") && !selectedIds.Contains("arc-E"),
                "The arc memory selection retained a reflection, recently-used, or wrong-year page it should have dropped.");

            // The assembled signal is exactly what the orchestrator submits once memory selection passes.
            string gameContext = ArcReflectionEventData.BuildGameContext(
                ArcYear, forced: false, selectedMemories: selection.selected.Count,
                candidateMemories: selection.candidateCount, entriesThisYear: 0);
            ArcReflectionEventData data = new ArcReflectionEventData
            {
                PawnId = reflectingPawn.GetUniqueLoadID(),
                Tick = Find.TickManager.TicksGame,
                DefName = ArcReflectionEventData.DefNameToken,
                ArcYear = ArcYear,
                CandidateMemoryCount = selection.candidateCount,
                SelectedMemoryCount = selection.selected.Count,
                EntriesThisYear = 0,
                Forced = false,
                AlreadyWritten = false,
            };

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new ArcReflectionSignal(
                    data,
                    reflectingPawn,
                    "the year in review",
                    "Raised a granary wall. Held the line against a raider.",
                    "look back over the year",
                    gameContext)),
                ArcReflectionEventData.DefNameToken,
                reflectingPawn,
                null);

            scope.RequireSoloRef(diaryEvent, reflectingPawn);
            RequireContextContains(diaryEvent, "arc_reflection=true");
            RequireContextContains(diaryEvent, "selected_memories=2");
            RequireContextContains(diaryEvent, "candidate_memories=2");
        }

        /// <summary>
        /// N4. A major canonical page may request a later arc reflection, but the callback itself must not
        /// emit a second page. Same-owner delivery is idempotent, while a distinct newer owner replaces
        /// the one bounded row.
        /// </summary>
        [Test]
        public static void MajorEventQueuesOneDeferredArcWithoutImmediatePage()
        {
            const string firstOwner = "evt_terminal_owner_1";
            const string latestOwner = "evt_terminal_owner_2";

            scope.RequireNoNewEvent(() => InvokeConsiderAfterMajorEvent(firstOwner));
            PawnReflectionState state = DiaryRecord().EnsureReflectionState();
            PawnDiaryRimTestScope.Require(
                state.pendingMajorArc
                    && string.Equals(state.pendingMajorArcAvoidEventId, firstOwner, StringComparison.Ordinal),
                "The major event should queue one deferred arc request with its avoid-related event ID.");

            int firstRequestedTick = state.pendingMajorArcRequestedTick;
            bool duplicateAccepted = state.QueueMajorArc(firstRequestedTick + 100, firstOwner.ToUpperInvariant());
            PawnDiaryRimTestScope.Require(
                !duplicateAccepted
                    && state.pendingMajorArcRequestedTick == firstRequestedTick
                    && string.Equals(
                        state.pendingMajorArcAvoidEventId,
                        firstOwner,
                        StringComparison.Ordinal),
                "Re-delivery of the same canonical terminal owner reset or duplicated its pending row.");

            scope.RequireNoNewEvent(() => InvokeConsiderAfterMajorEvent(latestOwner));
            PawnDiaryRimTestScope.Require(
                state.pendingMajorArc
                    && string.Equals(state.pendingMajorArcAvoidEventId, latestOwner, StringComparison.Ordinal),
                "A second major event should replace the bounded pending row instead of creating a page/list.");
        }

        /// <summary>
        /// A world/despawned pawn cannot reach the map-only natural-rest scanner, so a major callback must
        /// not strand permanent pending debt on that diary record.
        /// </summary>
        [Test]
        public static void UnspawnedMajorOwnerCannotQueueUnreachableRestDebt()
        {
            Pawn unspawned = scope.CreateAdultColonist();
            scope.RequireNoNewEvent(() => InvokeConsiderAfterMajorEvent(
                "evt_unreachable_terminal_owner",
                unspawned));

            PawnReflectionState state = DiaryRecord(unspawned).EnsureReflectionState();
            PawnDiaryRimTestScope.Require(
                !state.pendingMajorArc,
                "An unspawned pawn accumulated major reflection debt that no natural-rest scan can consume.");
        }

        /// <summary>
        /// EVT-20. When the pawn has already written the year's full allowance of arc entries, the cadence policy
        /// blocks a further major-event arc with the year-cap reason — the deterministic year-limit gate.
        /// </summary>
        [Test]
        public static void YearCapBlocksArcWhenScheduleAlreadyFull()
        {
            ArcReflectionScheduleTuning tuning = MakeTuning();
            int nowTick = 5_000_000;

            // Fill the year: two marked entries reach the default max (allowSecondMajorEntry => 2/year).
            PawnArcScheduleState schedule = new PawnArcScheduleState();
            schedule.MarkArcEntry(nowTick, ArcYear, forced: false, usedEventIds: null, recentMemoryCap: 16);
            schedule.MarkArcEntry(nowTick, ArcYear, forced: false, usedEventIds: null, recentMemoryCap: 16);
            PawnDiaryRimTestScope.Require(
                schedule.arcEntriesThisYear == 2, "Two marked arc entries should count as two for the year.");

            ArcReflectionScheduleDecision decision = ArcReflectionSchedulePolicy.Evaluate(
                SnapshotOf(schedule), tuning, nowTick, ArcYear, dayOfYear: 60, majorEventTrigger: true);

            PawnDiaryRimTestScope.Require(
                !decision.allowed, "A year already at its arc cap must not allow another arc entry.");
            PawnDiaryRimTestScope.Require(
                string.Equals(decision.blockReason, "year_cap", StringComparison.Ordinal),
                "Expected the year cap to be the blocking reason, got '" + decision.blockReason + "'.");
        }

        /// <summary>
        /// EVT-20. After a single arc entry this year, a major event within the second-entry minimum gap is
        /// blocked with the gap reason; once the gap has elapsed the same schedule allows the second entry —
        /// proving the gap window (not some other gate) is what suppresses the too-recent arc.
        /// </summary>
        [Test]
        public static void SecondEntryGapBlocksArcUntilGapElapses()
        {
            ArcReflectionScheduleTuning tuning = MakeTuning();
            int firstEntryTick = 5_000_000;
            int gapTicks = Math.Max(0, tuning.secondEntryMinGapDays) * Math.Max(1, tuning.ticksPerDay);

            PawnArcScheduleState schedule = new PawnArcScheduleState();
            schedule.MarkArcEntry(firstEntryTick, ArcYear, forced: false, usedEventIds: null, recentMemoryCap: 16);

            // Still inside the gap: a major-event arc is throttled.
            ArcReflectionScheduleDecision blocked = ArcReflectionSchedulePolicy.Evaluate(
                SnapshotOf(schedule), tuning, firstEntryTick + (gapTicks / 2), ArcYear,
                dayOfYear: 60, majorEventTrigger: true);
            PawnDiaryRimTestScope.Require(
                !blocked.allowed, "A second arc entry inside the minimum gap must be blocked.");
            PawnDiaryRimTestScope.Require(
                string.Equals(blocked.blockReason, "gap", StringComparison.Ordinal),
                "Expected the second-entry gap to be the blocking reason, got '" + blocked.blockReason + "'.");

            // Past the gap: the very same schedule now allows the second entry.
            ArcReflectionScheduleDecision allowed = ArcReflectionSchedulePolicy.Evaluate(
                SnapshotOf(schedule), tuning, firstEntryTick + gapTicks + 1, ArcYear,
                dayOfYear: 60, majorEventTrigger: true);
            PawnDiaryRimTestScope.Require(
                allowed.allowed, "Once the minimum gap has elapsed the second major-event arc should be allowed.");
        }

        /// <summary>
        /// EVT-20. MarkArcEntry appends newly used memory ids, moving a repeated id to the most-recent slot
        /// without duplicating it, and the recent-memory cap trims the oldest ids; the resulting recently-used
        /// list then filters a matching candidate back out of a fresh selection. A year rollover resets the
        /// per-year entry count via NormalizeForYear.
        /// </summary>
        [Test]
        public static void RecentMemoryTrackingUpdatesAndFeedsFilter()
        {
            PawnArcScheduleState schedule = new PawnArcScheduleState();

            schedule.MarkArcEntry(1000, ArcYear, forced: false,
                usedEventIds: new List<string> { "e1", "e2", "e3" }, recentMemoryCap: 16);
            PawnDiaryRimTestScope.Require(
                SequenceEquals(schedule.recentlyUsedEventIds, "e1", "e2", "e3"),
                "The first arc entry should record its three used memory ids in order.");

            // Re-using e3 must move it to the end (dedup by id), and add e4.
            schedule.MarkArcEntry(2000, ArcYear, forced: false,
                usedEventIds: new List<string> { "e3", "e4" }, recentMemoryCap: 16);
            PawnDiaryRimTestScope.Require(
                SequenceEquals(schedule.recentlyUsedEventIds, "e1", "e2", "e3", "e4"),
                "Re-using a memory id should move it to the most-recent slot without duplicating it.");

            // A small cap trims the oldest ids after the next entry.
            schedule.MarkArcEntry(3000, ArcYear, forced: false,
                usedEventIds: new List<string> { "e5" }, recentMemoryCap: 2);
            PawnDiaryRimTestScope.Require(
                SequenceEquals(schedule.recentlyUsedEventIds, "e4", "e5"),
                "The recent-memory cap should keep only the two most-recent ids.");

            // The tracked recently-used ids feed straight back into memory filtering: a candidate whose id is
            // now recently used is dropped, while a fresh one survives.
            ArcMemorySelectionResult selection = ArcReflectionMemorySelector.Select(new ArcMemorySelectionRequest
            {
                candidates = new List<ArcMemoryCandidate>
                {
                    NewCandidate("e5", ArcYear, "work", "work|e5", "Recently used, should be filtered"),
                    NewCandidate("fresh", ArcYear, "work", "work|fresh", "Never used before"),
                },
                recentlyUsedEventIds = schedule.recentlyUsedEventIds,
                currentYear = ArcYear,
                maxMemories = 8,
                minMemories = 1,
                sameDomainGroupCap = 2,
                seed = SelectionSeed,
            });
            PawnDiaryRimTestScope.Require(
                selection.candidateCount == 1
                    && selection.selected.Count == 1
                    && string.Equals(selection.selected[0].eventId, "fresh", StringComparison.Ordinal),
                "A candidate whose id is in the recently-used list should be filtered out of the next selection.");

            // A new year zeroes the per-year entry counter.
            schedule.NormalizeForYear(ArcYear + 1, 16);
            PawnDiaryRimTestScope.Require(
                schedule.arcEntriesThisYear == 0,
                "Rolling into a new year should reset the per-year arc entry count.");
        }

        /// <summary>
        /// EVT-20. After an annual forced arc attempt finds too few memories, the schedule records a shortfall so
        /// the resting-pawn scanner backs off: the backoff guard is active inside the retry window, inactive once
        /// it elapses, inactive in a later year, and disabled when the retry window is non-positive. Marking a
        /// real arc entry clears the guard.
        /// </summary>
        [Test]
        public static void ForcedMemoryShortfallBackoffThrottlesRetry()
        {
            const int retryTicks = 30_000;
            int shortfallTick = 4_000_000;

            PawnArcScheduleState schedule = new PawnArcScheduleState();
            schedule.MarkMemoryShortfall(shortfallTick, ArcYear);

            PawnDiaryRimTestScope.Require(
                schedule.IsMemoryShortfallBackoffActive(shortfallTick + (retryTicks / 2), ArcYear, retryTicks),
                "A recent memory shortfall should keep the retry backoff active inside the retry window.");
            PawnDiaryRimTestScope.Require(
                !schedule.IsMemoryShortfallBackoffActive(shortfallTick + retryTicks, ArcYear, retryTicks),
                "The retry backoff should elapse once the full retry window has passed.");
            PawnDiaryRimTestScope.Require(
                !schedule.IsMemoryShortfallBackoffActive(shortfallTick + 1, ArcYear + 1, retryTicks),
                "A shortfall from a previous year should not throttle the current year.");
            PawnDiaryRimTestScope.Require(
                !schedule.IsMemoryShortfallBackoffActive(shortfallTick + 1, ArcYear, 0),
                "A non-positive retry window disables the shortfall backoff.");

            // A successful arc entry clears the pending shortfall guard.
            schedule.MarkArcEntry(shortfallTick + 1, ArcYear, forced: true, usedEventIds: null, recentMemoryCap: 16);
            PawnDiaryRimTestScope.Require(
                !schedule.IsMemoryShortfallBackoffActive(shortfallTick + 2, ArcYear, retryTicks),
                "Recording an arc entry should clear any pending memory-shortfall backoff.");
        }

        // ----- helpers ----------------------------------------------------------------------------

        private static void InvokeConsiderAfterMajorEvent(
            string avoidRelatedEventId,
            Pawn pawn = null)
        {
            MethodInfo method = typeof(DiaryGameComponent).GetMethod(
                "ConsiderArcReflectionAfterMajorEvent",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new InvalidOperationException(
                    "Could not locate DiaryGameComponent.ConsiderArcReflectionAfterMajorEvent.");
            }

            method.Invoke(scope.Component, new object[] { pawn ?? reflectingPawn, avoidRelatedEventId });
        }

        private static PawnDiaryRecord DiaryRecord(Pawn pawn = null)
        {
            MethodInfo method = typeof(DiaryGameComponent).GetMethod(
                "FindDiary",
                BindingFlags.Instance | BindingFlags.NonPublic);
            PawnDiaryRecord diary = method?.Invoke(
                scope.Component,
                new object[] { pawn ?? reflectingPawn, true })
                as PawnDiaryRecord;
            if (diary == null)
            {
                throw new InvalidOperationException("Could not resolve the test pawn's diary record.");
            }

            return diary;
        }

        /// <summary>
        /// Builds an arc-schedule tuning with the shipped default cadence knobs, independent of the loaded XML so
        /// the policy gates are deterministic (2 entries/year, second entry allowed after a 30-day gap).
        /// </summary>
        private static ArcReflectionScheduleTuning MakeTuning()
        {
            return new ArcReflectionScheduleTuning
            {
                enabled = true,
                maxEntriesPerYear = 2,
                allowSecondMajorEntry = true,
                secondEntryMinGapDays = 30,
                forceAfterYearDay = 45,
                ticksPerDay = 60000,
            };
        }

        /// <summary>Copies a saved schedule state into the plain snapshot the pure policy consumes.</summary>
        private static ArcReflectionScheduleSnapshot SnapshotOf(PawnArcScheduleState schedule)
        {
            return new ArcReflectionScheduleSnapshot
            {
                lastArcEntryTick = schedule.lastArcEntryTick,
                lastArcEntryYear = schedule.lastArcEntryYear,
                arcEntriesThisYear = schedule.arcEntriesThisYear,
                forcedArcYear = schedule.forcedArcYear,
            };
        }

        /// <summary>Builds a minimal, valid arc memory candidate (non-reflection, non-death, with text).</summary>
        private static ArcMemoryCandidate NewCandidate(
            string eventId, int year, string domain, string groupKey, string text)
        {
            return new ArcMemoryCandidate
            {
                eventId = eventId,
                pawnId = "test-pawn",
                povRole = "initiator",
                tick = year * 1000,
                year = year,
                domain = domain,
                groupKey = groupKey,
                defName = "SomeEvent",
                label = "an event",
                text = text,
            };
        }

        /// <summary>Marks a candidate as a prior reflection page (excluded from arc evidence).</summary>
        private static ArcMemoryCandidate Reflection(ArcMemoryCandidate candidate)
        {
            candidate.reflection = true;
            return candidate;
        }

        private static bool SequenceEquals(List<string> actual, params string[] expected)
        {
            if (actual == null || actual.Count != expected.Length)
            {
                return false;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (!string.Equals(actual[i], expected[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static void RequireContextContains(DiaryEvent diaryEvent, string expectedFragment)
        {
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf(expectedFragment, StringComparison.Ordinal) >= 0,
                "The arc reflection event context did not contain the expected fact '" + expectedFragment + "'.");
        }
    }
}

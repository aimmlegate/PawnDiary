// In-game flow tests for Pawn Diary's end-of-day reflection (EVT-19 Day/quadrum reflection).
//
// The real trigger is the sleep-path scan FlushAmbientNotesForSleepingPawns, which iterates every
// resting colonist on every map and calls the private per-pawn flush FlushDaySummaryForPawn(pawn).
// Reproducing the storyteller, a loaded map, and a bedded-down colonist is neither deterministic nor
// mapless, so — following the Phase 1 pattern (drive the per-unit production work directly) — these
// tests seed the pawn/day evidence store the live AddHediff hook fills (pendingDayHediffs) and then
// invoke that same per-pawn flush unit by reflection. The flush gathers the day's candidate signals,
// runs the weighted highlight selection, consumes the pending evidence, and dispatches one
// DayReflection page through the shared bus — exactly the work the sleep scan does per colonist.
//
// Determinism: the flush's highlight selection is weighted-random, so each test seeds exactly ONE
// important candidate — with a single candidate the selection is forced (the only element is always
// drawn), so the emitted page and its context are fully deterministic. The XML-backed reflection
// tuning that gates the flush (day-summary master switch, the important-signal-kind list, and the
// rarer arc/quadrum reflections that would otherwise pre-empt the daily one) is snapshotted and
// forced to known values, then restored in teardown.
//
// Coverage-matrix ID (design/TEST_COVERAGE_PLAN.md §3): EVT-19.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the day-reflection flush turns seeded day evidence into exactly one solo reflection page,
    /// that the once-per-day guard blocks a second flush, and that the flush consumes the pending
    /// day-evidence store. Requires a loaded game because the reflection pipeline (like every capture
    /// path) is inert at the main menu.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryDayReflectionFlowTests
    {
        private const BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // A distinctive synthetic hediff defName so the seeded evidence's signal tag ("hediff:<defName>")
        // is unambiguous in the emitted gameContext and cannot collide with a real affliction.
        private const string SeedHediffDefName = "PawnDiaryDayReflectionTestHediff";

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        // Snapshot of the reflection tuning fields this suite forces, restored in teardown.
        private static DiaryTuningDef tuning;
        private static bool savedDaySummaryEnabled;
        private static bool savedQuadrumEnabled;
        private static bool savedArcEnabled;
        private static List<string> savedImportantKinds;

        /// <summary>
        /// Opens a fresh scope with the Reflection group enabled (the day/quadrum reflection user
        /// toggle), forces the reflection tuning to a deterministic state, and creates one isolated
        /// adult colonist with generation disabled.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            // "reflection" is the Reflection-domain group whose matchDefNames includes DayReflection /
            // QuadrumReflection; DayReflectionSignal.BuildContext gates userEnabled on it.
            scope = PawnDiaryRimTestScope.Begin("reflection");
            pawn = scope.CreateAdultColonist();

            ForceDeterministicReflectionTuning();
            RegisterReflectionGuardCleanup();
        }

        /// <summary>
        /// Restores every mutation (tuning fields, the transient written-reflection guards) and audits
        /// that no test-owned state survived, even when a test threw partway through.
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
                pawn = null;
                tuning = null;
                savedImportantKinds = null;
            }
        }

        /// <summary>
        /// EVT-19. Seeds one important day-evidence signal, invokes the per-pawn flush, and verifies it
        /// emits exactly one solo DayReflection page whose context reflects the seeded highlight
        /// (day_reflection marker, one highlight, and the seeded hediff signal tag).
        /// </summary>
        [Test]
        public static void SeededDayFlushesOneSoloReflectionWithHighlightContext()
        {
            int day = CurrentDayIndex();
            SeedPendingDayHediff(day);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeFlush(pawn),
                "DayReflection",
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);

            string context = diaryEvent.gameContext ?? string.Empty;
            PawnDiaryRimTestScope.Require(
                context.IndexOf("day_reflection=true", StringComparison.OrdinalIgnoreCase) >= 0,
                "The reflection context did not carry the day_reflection marker.");
            PawnDiaryRimTestScope.Require(
                context.IndexOf("highlights=1", StringComparison.Ordinal) >= 0,
                "The reflection context did not report the single selected highlight.");
            PawnDiaryRimTestScope.Require(
                context.IndexOf("hediff:" + SeedHediffDefName, StringComparison.Ordinal) >= 0,
                "The reflection context did not include the seeded hediff highlight tag.");
        }

        /// <summary>
        /// EVT-19. The once-per-day guard: after a pawn writes a reflection, a second flush the same day
        /// produces no new page even when fresh evidence is waiting — the guard short-circuits before it
        /// would consume that evidence.
        /// </summary>
        [Test]
        public static void SecondFlushSameDayIsBlockedByOncePerDayGuard()
        {
            int day = CurrentDayIndex();
            SeedPendingDayHediff(day);

            // First flush emits and marks the pawn/day written.
            scope.FireAndRequireEvent(
                () => InvokeFlush(pawn),
                "DayReflection",
                pawn,
                null);

            // Re-seed fresh evidence, then flush again: the guard must drop it with no new page.
            SeedPendingDayHediff(day);
            scope.RequireNoNewEvent(() => InvokeFlush(pawn));

            // Because the guard returns before evidence consumption, the re-seeded batch is untouched.
            PawnDiaryRimTestScope.Require(
                PendingDayHediffContains(DaySummaryKey(pawn, day)),
                "The blocked second flush should have left the re-seeded day evidence in place.");
        }

        /// <summary>
        /// EVT-19. A successful flush consumes the pending day-evidence batch it summarized, so the same
        /// evidence can never separately re-emit on a later day rollover or save.
        /// </summary>
        [Test]
        public static void FlushConsumesPendingDayEvidence()
        {
            int day = CurrentDayIndex();
            string dayKey = DaySummaryKey(pawn, day);
            SeedPendingDayHediff(day);
            PawnDiaryRimTestScope.Require(
                PendingDayHediffContains(dayKey),
                "Test setup failed to seed the pending day-evidence store.");

            scope.FireAndRequireEvent(
                () => InvokeFlush(pawn),
                "DayReflection",
                pawn,
                null);

            PawnDiaryRimTestScope.Require(
                !PendingDayHediffContains(dayKey),
                "The flush did not consume the pending day-evidence batch for this pawn/day.");
        }

        /// <summary>
        /// N4. A disabled Reflection group advances the current day window without creating a page.
        /// Re-enabling in that same window cannot dump the evidence accumulated while disabled.
        /// </summary>
        [Test]
        public static void DisabledReflectionGroupBoundsDayDebtAcrossReenable()
        {
            int day = CurrentDayIndex();
            string dayKey = DaySummaryKey(pawn, day);
            SeedPendingDayHediff(day);

            PawnDiaryMod.Settings.SetGroupEnabled("reflection", false);
            scope.RequireNoNewEvent(() => InvokeFlush(pawn));
            PawnDiaryRimTestScope.Require(
                !PendingDayHediffContains(dayKey),
                "A disabled day-reflection group should consume/bound the current day evidence debt.");

            PawnDiaryMod.Settings.SetGroupEnabled("reflection", true);
            SeedPendingDayHediff(day);
            scope.RequireNoNewEvent(() => InvokeFlush(pawn));
            PawnDiaryRimTestScope.Require(
                PendingDayHediffContains(dayKey),
                "Re-enabling should not bypass the day guard or consume fresh evidence without a page.");
        }

        /// <summary>
        /// N4. A record that lacks the additive runtime row baselines its current cadence window silently.
        /// Repeated scans in that same rest/day cannot turn the pre-upgrade evidence into a catch-up page.
        /// </summary>
        [Test]
        public static void OldSaveReflectionStateBaselinesSilently()
        {
            int day = CurrentDayIndex();
            string dayKey = DaySummaryKey(pawn, day);
            PawnDiaryRecord diary = DiaryRecord();
            diary.reflectionState = new PawnReflectionState();
            SeedPendingDayHediff(day);

            scope.RequireNoNewEvent(() => InvokeFlush(pawn));
            PawnDiaryRimTestScope.Require(
                !diary.reflectionState.baselineOnNextOpportunity
                    && diary.reflectionState.lastReflectionTick >= 0,
                "The first N4 opportunity should establish a silent cooldown baseline.");
            PawnDiaryRimTestScope.Require(
                !PendingDayHediffContains(dayKey),
                "The silent baseline should bound pre-upgrade day evidence.");

            SeedPendingDayHediff(day);
            scope.RequireNoNewEvent(() => InvokeFlush(pawn));
            PawnDiaryRimTestScope.Require(
                PendingDayHediffContains(dayKey),
                "A repeated scan in the baselined window should remain idempotent.");
        }

        // ----- production seam invocation ---------------------------------------------------------

        // Invokes the private per-pawn sleep-path flush directly. This is the exact per-colonist unit
        // that FlushAmbientNotesForSleepingPawns runs for each resting pawn; calling it here keeps the
        // test mapless and free of the storyteller/rest scheduling the real scan needs.
        private static void InvokeFlush(Pawn target)
        {
            MethodInfo flush = typeof(DiaryGameComponent).GetMethod("FlushDaySummaryForPawn", NonPublicInstance);
            if (flush == null)
            {
                throw new AssertionException(
                    "EVT-19 could not locate DiaryGameComponent.FlushDaySummaryForPawn(Pawn).");
            }

            flush.Invoke(scope.Component, new object[] { target });
        }

        private static PawnDiaryRecord DiaryRecord()
        {
            MethodInfo method = typeof(DiaryGameComponent).GetMethod("FindDiary", NonPublicInstance);
            PawnDiaryRecord diary = method?.Invoke(scope.Component, new object[] { pawn, true })
                as PawnDiaryRecord;
            if (diary == null)
            {
                throw new AssertionException("Could not resolve the test pawn's diary record.");
            }

            return diary;
        }

        // ----- day-evidence seeding ---------------------------------------------------------------

        // Adds one important affliction record to pendingDayHediffs["pawnId|day"] — the same store the
        // live Pawn_HealthTracker.AddHediff hook fills for day-reflection-mode hediffs. One record is a
        // single important candidate, which forces the otherwise weighted-random highlight selection.
        private static void SeedPendingDayHediff(int day)
        {
            IDictionary pending = PendingDayHediffs();
            string dayKey = DaySummaryKey(pawn, day);

            Type recordType = typeof(DiaryGameComponent).GetNestedType("DayHediffRecord", BindingFlags.NonPublic);
            if (recordType == null)
            {
                throw new AssertionException(
                    "EVT-19 could not locate the private DiaryGameComponent.DayHediffRecord struct.");
            }

            object record = Activator.CreateInstance(recordType);
            SetRecordField(recordType, record, "defName", SeedHediffDefName);
            SetRecordField(recordType, record, "label", "test affliction");
            SetRecordField(recordType, record, "weight", 1f);
            SetRecordField(recordType, record, "progressed", false);

            Type listType = typeof(List<>).MakeGenericType(recordType);
            object list = Activator.CreateInstance(listType);
            listType.GetMethod("Add").Invoke(list, new[] { record });

            pending[dayKey] = list;
        }

        private static void SetRecordField(Type recordType, object boxedRecord, string fieldName, object value)
        {
            FieldInfo field = recordType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
            {
                throw new AssertionException(
                    "EVT-19 could not set DayHediffRecord field '" + fieldName + "' (renamed?).");
            }

            field.SetValue(boxedRecord, value);
        }

        private static bool PendingDayHediffContains(string dayKey)
        {
            return PendingDayHediffs().Contains(dayKey);
        }

        private static IDictionary PendingDayHediffs()
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField("pendingDayHediffs", NonPublicInstance);
            IDictionary dictionary = field?.GetValue(scope.Component) as IDictionary;
            if (dictionary == null)
            {
                throw new AssertionException(
                    "EVT-19 could not read DiaryGameComponent.pendingDayHediffs.");
            }

            return dictionary;
        }

        // Matches DiaryGameComponent.DaySummaryKey(pawnId, day): "pawnId|day".
        private static string DaySummaryKey(Pawn target, int day)
        {
            return target.GetUniqueLoadID() + "|" + day;
        }

        // Matches DiaryGameComponent.CurrentDayIndex: absolute in-game day from the world clock.
        private static int CurrentDayIndex()
        {
            return Find.TickManager.TicksAbs / GenDate.TicksPerDay;
        }

        // ----- deterministic tuning + guard cleanup -----------------------------------------------

        // Forces the reflection tuning so the daily flush is the only reflection that can fire and the
        // seeded hediff counts as an important candidate. Restored in teardown via RegisterCleanup.
        private static void ForceDeterministicReflectionTuning()
        {
            tuning = DiaryTuning.Current;
            savedDaySummaryEnabled = tuning.daySummaryEnabled;
            savedQuadrumEnabled = tuning.quadrumReflectionEnabled;
            savedArcEnabled = tuning.arcReflectionEnabled;
            savedImportantKinds = tuning.daySummaryImportantSignalKinds;

            tuning.daySummaryEnabled = true;
            // Disable the rarer arc/quadrum reflections so they never pre-empt the daily one; the flush
            // tries both before the ordinary day path.
            tuning.quadrumReflectionEnabled = false;
            tuning.arcReflectionEnabled = false;
            // Only "hediff" needs to justify a reflection for these tests; a fresh list leaves the
            // shipped default untouched (the original reference is restored on teardown).
            tuning.daySummaryImportantSignalKinds = new List<string> { "hediff" };

            scope.RegisterCleanup(RestoreReflectionTuning);
        }

        private static void RestoreReflectionTuning()
        {
            if (tuning == null)
            {
                return;
            }

            tuning.daySummaryEnabled = savedDaySummaryEnabled;
            tuning.quadrumReflectionEnabled = savedQuadrumEnabled;
            tuning.arcReflectionEnabled = savedArcEnabled;
            tuning.daySummaryImportantSignalKinds = savedImportantKinds;
        }

        // The once-per-day / once-per-quadrum guards (writtenDayReflections / writtenQuadrumReflections)
        // are transient HashSet<string> stores keyed by "pawnId|day" and "pawnId|quadrum". They are not
        // part of the harness's owned/audited set, so scrub the test pawn's keys ourselves to keep the
        // developer's loaded colony clean.
        private static void RegisterReflectionGuardCleanup()
        {
            string pawnId = pawn.GetUniqueLoadID();
            scope.RegisterCleanup(() =>
            {
                ScrubGuardSet("writtenDayReflections", pawnId);
                ScrubGuardSet("writtenQuadrumReflections", pawnId);
            });
        }

        private static void ScrubGuardSet(string fieldName, string pawnId)
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField(fieldName, NonPublicInstance);
            HashSet<string> set = field?.GetValue(scope.Component) as HashSet<string>;
            set?.RemoveWhere(key => key != null && key.IndexOf(pawnId, StringComparison.Ordinal) >= 0);
        }
    }
}

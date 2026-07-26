// Loaded-game tests for Pawn Diary's runtime telemetry and persistence-integrity adapters.
//
// DiaryTelemetrySessionState and DiaryIntegrityPolicy have standalone pure tests. This suite proves
// the live edges actually feed them: the startup Harmony manifest is replayed, a real vanilla trigger
// produces dispatch telemetry, the developer export includes a fresh integrity section, a temporary
// dangling diary reference is detected and then disappears when repaired, and repository invariant
// failures retain only safe fingerprints/type names.
//
// The static telemetry facade is swapped to an independent in-memory session for each test and the
// exact prior session object is restored in AfterEach. Optional error reporting is disabled and
// restored while the isolated diagnostics run, so deliberately exercising an invariant never opens a
// network request or consumes the player's report quota.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Exercises telemetry/integrity wiring against the current loaded Game without persisting or
    /// exporting any test-owned diagnostic state.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryRuntimeDiagnosticsFixtureTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const string InspirationGroupKey = "inspiration";
        private const string InspirationDefName = "Inspired_Creativity";

        private static readonly FieldInfo CurrentTelemetrySessionField =
            typeof(DiaryTelemetry).GetField("currentSession", PrivateStatic);
        private static readonly MethodInfo BuildAllDiariesExportTextMethod =
            typeof(DiaryGameComponent).GetMethod("BuildAllDiariesExportText", PrivateInstance);
        private static readonly MethodInfo RunDiaryIntegrityAuditMethod =
            typeof(DiaryGameComponent).GetMethod("RunDiaryIntegrityAudit", PrivateInstance);
        private static readonly MethodInfo RegisterNewEventOrThrowMethod =
            typeof(DiaryGameComponent).GetMethod("RegisterNewEventOrThrow", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;
        private static object previousTelemetrySession;
        private static bool previousErrorReporting;
        private static bool errorReportingWasCaptured;

        /// <summary>
        /// Creates one isolated pawn, then gives the test an independent telemetry ledger and disables
        /// optional error transport before any diagnostic assertion runs.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            RequireReflectionSurface();
            scope = PawnDiaryRimTestScope.Begin(InspirationGroupKey);
            pawn = scope.CreateAdultColonist();
            PawnDiaryRimTestScope.MakeCreativityInspirationEligible(pawn);

            previousTelemetrySession = CurrentTelemetrySessionField.GetValue(null);
            CurrentTelemetrySessionField.SetValue(
                null,
                new DiaryTelemetrySessionState(
                    DiaryTelemetrySessionState.DefaultMaximumCounterBuckets,
                    DiaryTelemetrySessionState.DefaultMaximumRecentAnomalies));

            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings == null)
            {
                throw new AssertionException("Pawn Diary settings were unavailable for diagnostics isolation.");
            }

            previousErrorReporting = settings.enableErrorReporting;
            errorReportingWasCaptured = true;
            settings.enableErrorReporting = false;
        }

        /// <summary>Restores the exact telemetry object and error-reporting setting after all cleanup.</summary>
        [AfterEach]
        public static void TearDown()
        {
            try
            {
                scope?.TearDown();
            }
            finally
            {
                try
                {
                    if (CurrentTelemetrySessionField != null && previousTelemetrySession != null)
                    {
                        CurrentTelemetrySessionField.SetValue(null, previousTelemetrySession);
                    }
                }
                finally
                {
                    if (errorReportingWasCaptured && PawnDiaryMod.Settings != null)
                    {
                        PawnDiaryMod.Settings.enableErrorReporting = previousErrorReporting;
                    }

                    scope = null;
                    pawn = null;
                    previousTelemetrySession = null;
                    errorReportingWasCaptured = false;
                }
            }
        }

        /// <summary>
        /// Replaying the already-built hook manifest produces one hook outcome per manifest entry in the
        /// current Game's isolated telemetry session.
        /// </summary>
        [Test]
        public static void LoadedHookManifestReplaysIntoSessionTelemetry()
        {
            List<DiaryPatchManifest.Entry> manifest = DiaryPatchManifest.Snapshot();
            PawnDiaryRimTestScope.Require(
                manifest.Count > 0,
                "The loaded hook manifest was empty; startup patch telemetry cannot be validated.");

            DiaryTelemetryReporter.RecordHookManifestForSession();
            DiaryTelemetrySnapshot snapshot = DiaryTelemetry.Snapshot();
            long hookCount =
                CountOutcome(snapshot, DiaryTelemetryOutcome.HookApplied)
                + CountOutcome(snapshot, DiaryTelemetryOutcome.HookSkipped)
                + CountOutcome(snapshot, DiaryTelemetryOutcome.HookDegraded)
                + CountOutcome(snapshot, DiaryTelemetryOutcome.HookFailed);
            PawnDiaryRimTestScope.Require(
                hookCount == manifest.Count,
                "Hook-manifest telemetry recorded " + hookCount
                    + " outcomes for " + manifest.Count + " loaded entries.");
        }

        /// <summary>
        /// A real vanilla inspiration crosses its Harmony patch and common dispatcher, while a null
        /// submission records its explicit drop. The in-memory developer export then exposes both
        /// telemetry and a freshly computed persistence-integrity summary.
        /// </summary>
        [Test]
        public static void RealDispatchAndDeveloperExportExposeRuntimeDiagnostics()
        {
            InspirationDef inspirationDef =
                DefDatabase<InspirationDef>.GetNamedSilentFail(InspirationDefName);
            PawnDiaryRimTestScope.Require(
                inspirationDef != null,
                "Required base InspirationDef '" + InspirationDefName + "' was not loaded.");
            scope.RegisterCleanup(() => EndInspirationSafely(pawn, inspirationDef));

            scope.FireAndRequireEvent(
                () =>
                {
                    bool started = pawn.mindState.inspirationHandler.TryStartInspiration(
                        inspirationDef,
                        "RimTest runtime telemetry",
                        false);
                    PawnDiaryRimTestScope.Require(
                        started,
                        "Vanilla refused the diagnostics fixture inspiration.");
                },
                InspirationDefName,
                pawn,
                null);
            DiaryEvents.Submit((DiarySignal)null);

            DiaryTelemetrySnapshot snapshot = DiaryTelemetry.Snapshot();
            PawnDiaryRimTestScope.Require(
                CountOutcome(snapshot, DiaryTelemetryOutcome.EventRecorded) >= 1,
                "The real inspiration dispatch did not record EventRecorded telemetry.");
            PawnDiaryRimTestScope.Require(
                CountOutcome(snapshot, DiaryTelemetryOutcome.SubmitNull) == 1,
                "A null DiaryEvents submission did not record exactly one SubmitNull transition.");

            string export = (string)BuildAllDiariesExportTextMethod.Invoke(scope.Component, null);
            PawnDiaryRimTestScope.Require(
                export.IndexOf(
                    "== Runtime telemetry (current game; not saved) ==",
                    StringComparison.Ordinal) >= 0
                    && export.IndexOf("EventRecorded", StringComparison.Ordinal) >= 0
                    && export.IndexOf(
                        "== Current persistence integrity ==",
                        StringComparison.Ordinal) >= 0
                    && export.IndexOf("issues=", StringComparison.Ordinal) >= 0,
                "The developer export omitted runtime telemetry or its fresh integrity summary.");
        }

        /// <summary>
        /// The live component audit detects one exact temporary dangling reference relative to the
        /// colony's own baseline, and returns to that baseline after the reference is removed.
        /// </summary>
        [Test]
        public static void LiveIntegrityAuditDetectsAndRecoversTemporaryDanglingReference()
        {
            DiaryIntegrityReport baseline = RunAudit("rimtest_integrity_baseline");
            PawnDiaryRecord record = scope.RequireDiaryRecord(pawn);
            string danglingId = "pawndiary-rimtest-dangling-" + Guid.NewGuid().ToString("N");
            record.eventIds.Add(danglingId);
            scope.RegisterCleanup(() => record.eventIds.Remove(danglingId));

            DiaryIntegrityReport corrupted = RunAudit("rimtest_integrity_corrupted");
            PawnDiaryRimTestScope.Require(
                corrupted.danglingEventRefs == baseline.danglingEventRefs + 1
                    && corrupted.IssueCount == baseline.IssueCount + 1,
                "The live integrity audit did not attribute exactly one temporary dangling reference.");
            PawnDiaryRimTestScope.Require(
                CountOutcome(DiaryTelemetry.Snapshot(), DiaryTelemetryOutcome.IntegrityIssue) >= 1,
                "The corrupted live audit did not record an IntegrityIssue transition.");

            record.eventIds.Remove(danglingId);
            DiaryIntegrityReport repaired = RunAudit("rimtest_integrity_repaired");
            PawnDiaryRimTestScope.Require(
                repaired.danglingEventRefs == baseline.danglingEventRefs
                    && repaired.IssueCount == baseline.IssueCount,
                "Removing the temporary reference did not restore the component's integrity baseline.");
        }

        /// <summary>
        /// Duplicate registration fails before references can be committed and records its stable
        /// repository outcome. Exception telemetry stores the type/fingerprint but never the message.
        /// </summary>
        [Test]
        public static void RepositoryInvariantAndExceptionTelemetryRemainIdentifierSafe()
        {
            DiaryEvent diaryEvent = scope.Component.AddSoloEvent(
                pawn,
                null,
                "PawnDiary_RimTest_Diagnostics",
                "diagnostics",
                "A diagnostics fixture page.",
                string.Empty,
                "rimtest_diagnostics=1");
            PawnDiaryRimTestScope.Require(
                diaryEvent != null,
                "The diagnostics fixture could not create its repository row.");

            bool duplicateRejected = false;
            try
            {
                RegisterNewEventOrThrowMethod.Invoke(
                    scope.Component,
                    new object[] { diaryEvent });
            }
            catch (TargetInvocationException exception)
            {
                duplicateRejected = exception.InnerException is InvalidOperationException;
            }

            PawnDiaryRimTestScope.Require(
                duplicateRejected,
                "RegisterNewEventOrThrow did not reject an already-registered event id.");
            PawnDiaryRimTestScope.Require(
                CountOutcome(
                    DiaryTelemetry.Snapshot(),
                    DiaryTelemetryOutcome.RepositoryDuplicateIdRejected) == 1,
                "Duplicate repository registration did not record its invariant outcome.");

            const string secretMessage = "RIMTEST_PRIVATE_EXCEPTION_MESSAGE";
            string fingerprint = DiaryTelemetryReporter.RecordException(
                DiaryTelemetryOutcome.IntegrityAuditException,
                "rimtest.exception",
                "diagnostics",
                "fixture",
                new InvalidOperationException(secretMessage),
                DiaryTelemetryReporter.CurrentGameTick());
            string formatted = DiaryTelemetryFormatter.Format(DiaryTelemetry.Snapshot());
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(fingerprint)
                    && formatted.IndexOf(typeof(InvalidOperationException).FullName, StringComparison.Ordinal) >= 0
                    && formatted.IndexOf(secretMessage, StringComparison.Ordinal) < 0,
                "Exception telemetry leaked the exception message or omitted its safe type/fingerprint.");
        }

        private static DiaryIntegrityReport RunAudit(string stage)
        {
            DiaryIntegrityReport report = RunDiaryIntegrityAuditMethod.Invoke(
                scope.Component,
                new object[] { stage, false }) as DiaryIntegrityReport;
            if (report == null)
            {
                throw new AssertionException("The loaded persistence-integrity audit returned null.");
            }

            return report;
        }

        private static long CountOutcome(
            DiaryTelemetrySnapshot snapshot,
            DiaryTelemetryOutcome outcome)
        {
            long count = 0;
            if (snapshot == null)
            {
                return count;
            }

            for (int i = 0; i < snapshot.counters.Count; i++)
            {
                DiaryTelemetryCounter counter = snapshot.counters[i];
                if (counter != null && counter.outcome == outcome)
                {
                    count += counter.count;
                }
            }

            return count;
        }

        private static void EndInspirationSafely(Pawn subject, InspirationDef inspirationDef)
        {
            InspirationHandler handler = subject?.mindState?.inspirationHandler;
            if (handler != null && inspirationDef != null && handler.Inspired)
            {
                handler.EndInspiration(inspirationDef);
            }
        }

        private static void RequireReflectionSurface()
        {
            PawnDiaryRimTestScope.Require(
                CurrentTelemetrySessionField != null
                    && BuildAllDiariesExportTextMethod != null
                    && RunDiaryIntegrityAuditMethod != null
                    && RegisterNewEventOrThrowMethod != null,
                "Pawn Diary diagnostics reflection surface changed; update the loaded fixture.");
        }
    }
}

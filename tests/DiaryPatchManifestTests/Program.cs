// Standalone no-RimWorld tests for the startup hook-status manifest. Linking only the manifest
// makes an accidental Verse, RimWorld, Unity, or Harmony dependency a compile-time failure.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PawnDiary;

namespace DiaryPatchManifestTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestEmptyManifest();
            TestCountingAndSummary();
            TestHealthSemantics();
            TestDetailFormatting();
            TestNullSafety();
            TestCaps();
            TestResetAndSnapshotCopy();
            TestPatchCircuitBreaker();
            TestIndependentActionsContinueAfterFailure();
            TestBrainwipeNoticeCannotBlockArrivalOrMainCircuit();
            TestLivePawnSnapshotIncludesTravellingTransporters();
            TestErrorTransportCannotBypassLocalLog();
            Console.WriteLine("DiaryPatchManifestTests passed " + assertions + " assertions.");
            return 0;
        }

        private static void TestEmptyManifest()
        {
            DiaryPatchManifest.Reset();
            AssertEqual(
                "empty summary",
                "Hooks: 0 applied, 0 degraded, 0 failed, 0 skipped.",
                DiaryPatchManifest.BuildSummary());
            AssertEqual("empty detail", string.Empty, DiaryPatchManifest.BuildDetail());
            AssertTrue("empty manifest is healthy", DiaryPatchManifest.AllHealthy());
        }

        private static void TestCountingAndSummary()
        {
            DiaryPatchManifest.Reset();
            DiaryPatchManifest.Report(
                "A", "applied-1", DiaryPatchManifest.HookStatus.Applied);
            DiaryPatchManifest.Report(
                "A", "applied-2", DiaryPatchManifest.HookStatus.Applied);
            DiaryPatchManifest.Report(
                "B", "degraded", DiaryPatchManifest.HookStatus.Degraded);
            DiaryPatchManifest.Report(
                "C", "failed", DiaryPatchManifest.HookStatus.Failed);
            DiaryPatchManifest.Report(
                "D", "skipped", DiaryPatchManifest.HookStatus.Skipped);

            AssertEqual(
                "applied count", 2,
                DiaryPatchManifest.Count(DiaryPatchManifest.HookStatus.Applied));
            AssertEqual(
                "degraded count", 1,
                DiaryPatchManifest.Count(DiaryPatchManifest.HookStatus.Degraded));
            AssertEqual(
                "failed count", 1,
                DiaryPatchManifest.Count(DiaryPatchManifest.HookStatus.Failed));
            AssertEqual(
                "skipped count", 1,
                DiaryPatchManifest.Count(DiaryPatchManifest.HookStatus.Skipped));
            AssertEqual(
                "mixed summary",
                "Hooks: 2 applied, 1 degraded, 1 failed, 1 skipped.",
                DiaryPatchManifest.BuildSummary());
        }

        private static void TestHealthSemantics()
        {
            DiaryPatchManifest.Reset();
            DiaryPatchManifest.Report(
                "A", "applied", DiaryPatchManifest.HookStatus.Applied);
            DiaryPatchManifest.Report(
                "A", "skipped", DiaryPatchManifest.HookStatus.Skipped);
            AssertTrue("applied and skipped are healthy", DiaryPatchManifest.AllHealthy());

            DiaryPatchManifest.Report(
                "B", "degraded", DiaryPatchManifest.HookStatus.Degraded);
            AssertTrue("degraded is unhealthy", !DiaryPatchManifest.AllHealthy());

            DiaryPatchManifest.Reset();
            DiaryPatchManifest.Report(
                "C", "failed", DiaryPatchManifest.HookStatus.Failed);
            AssertTrue("failed is unhealthy", !DiaryPatchManifest.AllHealthy());
        }

        private static void TestDetailFormatting()
        {
            DiaryPatchManifest.Reset();
            DiaryPatchManifest.Report(
                "Attribute", "HealthyPatch", DiaryPatchManifest.HookStatus.Applied, "installed");
            DiaryPatchManifest.Report(
                "Quest", "ExpectedMiss", DiaryPatchManifest.HookStatus.Skipped, "normal");
            DiaryPatchManifest.Report(
                "Anomaly",
                "Study",
                DiaryPatchManifest.HookStatus.Degraded,
                "target changed");
            DiaryPatchManifest.Report(
                "Biotech",
                "Birth",
                DiaryPatchManifest.HookStatus.Failed,
                "HarmonyException");

            string detail = DiaryPatchManifest.BuildDetail();
            AssertContains("detail includes degraded area and target", detail, "Anomaly Study");
            AssertContains("detail includes degraded word", detail, "degraded");
            AssertContains("detail includes parenthesized reason", detail, "(target changed)");
            AssertContains("detail joins failed entry", detail, "; Biotech Birth");
            AssertContains("detail includes failed word", detail, "failed");
            AssertContains("detail includes failed reason", detail, "(HarmonyException)");
            AssertTrue(
                "detail excludes applied entries",
                detail.IndexOf("HealthyPatch", StringComparison.Ordinal) < 0);
            AssertTrue(
                "detail excludes skipped entries",
                detail.IndexOf("ExpectedMiss", StringComparison.Ordinal) < 0);
        }

        private static void TestNullSafety()
        {
            DiaryPatchManifest.Reset();
            DiaryPatchManifest.Report(
                null, null, DiaryPatchManifest.HookStatus.Degraded, null);
            List<DiaryPatchManifest.Entry> snapshot = DiaryPatchManifest.Snapshot();
            AssertEqual("null report creates one entry", 1, snapshot.Count);
            AssertEqual("null area normalizes", string.Empty, snapshot[0].area);
            AssertEqual("null target normalizes", string.Empty, snapshot[0].target);
            AssertEqual("null detail normalizes", string.Empty, snapshot[0].detail);
        }

        private static void TestCaps()
        {
            DiaryPatchManifest.Reset();
            DiaryPatchManifest.Report(
                "Area",
                "Target",
                DiaryPatchManifest.HookStatus.Degraded,
                new string('x', 300));
            string storedDetail = DiaryPatchManifest.Snapshot()[0].detail;
            AssertEqual("per-entry detail cap", 240, storedDetail.Length);
            AssertTrue(
                "per-entry detail cap has ellipsis",
                storedDetail.EndsWith("...", StringComparison.Ordinal));

            DiaryPatchManifest.Reset();
            for (int i = 0; i < 100; i++)
            {
                DiaryPatchManifest.Report(
                    "Area",
                    "Target-" + i + "-" + new string('t', 40),
                    DiaryPatchManifest.HookStatus.Degraded,
                    new string('d', 240));
            }

            string detail = DiaryPatchManifest.BuildDetail();
            AssertTrue("whole detail list cap", detail.Length <= 4000);
            AssertTrue(
                "whole detail list cap has ellipsis",
                detail.EndsWith("...", StringComparison.Ordinal));
        }

        private static void TestResetAndSnapshotCopy()
        {
            DiaryPatchManifest.Reset();
            DiaryPatchManifest.Report(
                "Area", "Target", DiaryPatchManifest.HookStatus.Applied);
            List<DiaryPatchManifest.Entry> snapshot = DiaryPatchManifest.Snapshot();
            snapshot.Clear();
            AssertEqual(
                "snapshot mutation does not affect manifest",
                1,
                DiaryPatchManifest.Count(DiaryPatchManifest.HookStatus.Applied));

            DiaryPatchManifest.Reset();
            AssertEqual(
                "reset clears manifest",
                0,
                DiaryPatchManifest.Count(DiaryPatchManifest.HookStatus.Applied));
        }

        private static void TestPatchCircuitBreaker()
        {
            PatchCircuitBreaker breaker = new PatchCircuitBreaker();
            AssertTrue("new patch context is active", !breaker.IsDisabled("hot-hook"));
            AssertTrue("first fault opens circuit", breaker.Disable("hot-hook"));
            AssertTrue("faulted patch context is disabled", breaker.IsDisabled("hot-hook"));
            AssertTrue("repeat fault does not reopen circuit", !breaker.Disable("hot-hook"));
            AssertTrue("other patch context remains active", !breaker.IsDisabled("other-hook"));

            int firstTransitions = 0;
            Parallel.For(0, 64, delegate(int ignored)
            {
                if (breaker.Disable("parallel-hook"))
                {
                    Interlocked.Increment(ref firstTransitions);
                }
            });
            AssertEqual("parallel fault has one first transition", 1, firstTransitions);

            AssertTrue("null context can be disabled", breaker.Disable(null));
            AssertTrue("null context lookup is safe", breaker.IsDisabled(null));
            breaker.Reset();
            AssertTrue("session reset re-enables named context", !breaker.IsDisabled("hot-hook"));
            AssertTrue("session reset re-enables null context", !breaker.IsDisabled(null));
        }

        private static void TestIndependentActionsContinueAfterFailure()
        {
            List<int> ran = new List<int>();
            int failedIndex = -1;
            int reports = 0;
            IndependentActionRunner.RunAll(
                new Action[]
                {
                    () =>
                    {
                        ran.Add(0);
                        throw new InvalidOperationException("expected");
                    },
                    () => ran.Add(1),
                    () =>
                    {
                        ran.Add(2);
                        throw new InvalidOperationException("also expected");
                    },
                    () => ran.Add(3),
                },
                (index, exception) =>
                {
                    reports++;
                    if (failedIndex < 0)
                    {
                        failedIndex = index;
                        throw new InvalidOperationException("reporter failure");
                    }
                });

            AssertEqual("throwing independent action is reported", 0, failedIndex);
            AssertEqual("all independent actions were attempted", 4, ran.Count);
            AssertEqual("second action ran after first failed", 1, ran[1]);
            AssertEqual("third action ran after first failed", 2, ran[2]);
            AssertEqual("fourth action ran after reporter failed", 3, ran[3]);
            AssertEqual("later action failures are still reported", 2, reports);
        }

        private static void TestBrainwipeNoticeCannotBlockArrivalOrMainCircuit()
        {
            string root = FindRepositoryRoot();
            string source = File.ReadAllText(Path.Combine(
                root,
                "Source",
                "Patches",
                "DiaryEventSignalPatches.cs"));
            int patchStart = source.IndexOf(
                "internal static class PsychicRitualBrainwipeOutcomePatch",
                StringComparison.Ordinal);
            int patchEnd = source.IndexOf(
                "// Fires after a pawn ability",
                patchStart,
                StringComparison.Ordinal);
            AssertTrue(
                "Brainwipe patch source region remains discoverable",
                patchStart >= 0 && patchEnd > patchStart);

            string patch = source.Substring(patchStart, patchEnd - patchStart);
            int mainContext = patch.IndexOf(
                "DiaryPatchSafety.Run(\"PsychicRitualBrainwipeOutcomePatch\"",
                StringComparison.Ordinal);
            int arrival = patch.IndexOf(
                "DiaryEvents.Submit(new BrainwipeArrivalSignal(target));",
                StringComparison.Ordinal);
            int noticeContext = patch.IndexOf(
                "PsychicRitualBrainwipeOutcomePatch.HistoryClearedNotice",
                StringComparison.Ordinal);

            AssertTrue("Brainwipe cleanup keeps its main safety context", mainContext >= 0);
            AssertTrue(
                "Brainwipe arrival is submitted inside the main safety context",
                arrival > mainContext);
            AssertTrue(
                "optional Brainwipe notice runs after the essential arrival submission",
                noticeContext > arrival);
            AssertTrue(
                "optional Brainwipe notice has a distinct safety context",
                noticeContext >= 0
                    && patch.IndexOf("ShowHistoryClearedNotice);", StringComparison.Ordinal)
                        > noticeContext);
        }

        private static void TestLivePawnSnapshotIncludesTravellingTransporters()
        {
            string root = FindRepositoryRoot();
            string source = File.ReadAllText(Path.Combine(
                root,
                "Source",
                "Core",
                "DiaryGameComponent.GenerationEligibility.cs"));
            AssertContains(
                "live pawn snapshot includes caravan and travelling transporter aggregate",
                source,
                "PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive");
        }

        private static void TestErrorTransportCannotBypassLocalLog()
        {
            string root = FindRepositoryRoot();
            string sourceRoot = Path.Combine(root, "Source");
            string[] files = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories);
            List<string> directReporterCallers = new List<string>();
            const string directCall = "DiaryErrorReporter.Report(";

            for (int i = 0; i < files.Length; i++)
            {
                string source = File.ReadAllText(files[i]);
                if (source.IndexOf(directCall, StringComparison.Ordinal) >= 0)
                {
                    directReporterCallers.Add(Path.GetFileName(files[i]));
                }
            }

            AssertEqual(
                "remote error transport has one local-log observer",
                1,
                directReporterCallers.Count);
            AssertEqual(
                "only the Log.Error postfix may call remote transport",
                "DiaryLogReportPatch.cs",
                directReporterCallers.Count == 1
                    ? directReporterCallers[0]
                    : string.Empty);

            string telemetryReporter = File.ReadAllText(Path.Combine(
                sourceRoot,
                "Diagnostics",
                "DiaryTelemetryReporter.cs"));
            AssertContains(
                "diagnostic invariants enter RimWorld's local error log",
                telemetryReporter,
                "Log.ErrorOnce(");

            string logPatch = File.ReadAllText(Path.Combine(
                sourceRoot,
                "Diagnostics",
                "DiaryLogReportPatch.cs"));
            AssertContains(
                "error transport observes the non-suppressing postfix path",
                logPatch,
                "harmony.Patch(error, postfix: postfix);");
            AssertTrue(
                "suppressed ErrorOnce duplicates are not reported remotely",
                logPatch.IndexOf(
                    "harmony.Patch(errorOnce",
                    StringComparison.Ordinal) < 0);
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo current = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (current != null)
            {
                if (File.Exists(Path.Combine(
                    current.FullName,
                    "Source",
                    "PawnDiary.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the Pawn Diary repository root for static regression checks.");
        }

        private static void AssertContains(string name, string actual, string expected)
        {
            AssertTrue(name, actual.IndexOf(expected, StringComparison.Ordinal) >= 0);
        }

        private static void AssertTrue(string name, bool value)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("Assertion failed (" + name + ").");
        }

        private static void AssertEqual<T>(string name, T expected, T actual)
        {
            assertions++;
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    "Assertion failed (" + name + "): expected '" + expected
                        + "', got '" + actual + "'.");
            }
        }
    }
}

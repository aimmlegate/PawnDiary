// Standalone no-RimWorld tests for the startup hook-status manifest. Linking only the manifest
// makes an accidental Verse, RimWorld, Unity, or Harmony dependency a compile-time failure.
using System;
using System.Collections.Generic;
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

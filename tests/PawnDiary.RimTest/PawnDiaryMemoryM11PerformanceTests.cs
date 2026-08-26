// PawnDiaryMemoryM11PerformanceTests.cs — loaded Mono/Scribe smoke measurements for the frozen
// M11 thread-target matrix N=4/12/64 and its friend-only transient policy scope.
//
// This is a regression smoke, not the authenticated release-vector selection harness described in
// the design plan. It asserts deterministic bytes/shape and logs elapsed/allocation observations;
// timing is bounded only by a generous hang guard so ordinary developer hardware is not flaky.
using System;
using System.Diagnostics;
using System.IO;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Loaded Scribe/logical-size smoke at every product-owned thread target.</summary>
    [TestSuite]
    public static class PawnDiaryMemoryM11PerformanceTests
    {
        /// <summary>
        /// Each N profile survives two real Scribe loads with identical logical bytes; size grows
        /// monotonically, measurements remain finite, and the fixture policy always restores.
        /// </summary>
        [Test]
        public static void LoadedScribeSmokeMatrixCoversN4N12N64()
        {
            int[] targets = { 4, 12, 64 };
            long priorLogicalBytes = -1;
            long priorXmlBytes = -1;
            Require(!MemoryPerformanceFixturePolicy.Active
                    && string.IsNullOrEmpty(MemoryPerformanceFixturePolicy.ScopeTag),
                "A prior benchmark leaked its friend-only policy scope.");

            for (int index = 0; index < targets.Length; index++)
            {
                int target = targets[index];
                string tag = "m11-loaded-smoke-n" + target;
                MemoryPerformanceFixturePolicyScope policyScope =
                    new MemoryPerformanceFixturePolicyScope(tag);
                try
                {
                    Require(MemoryPerformanceFixturePolicy.Active
                            && MemoryPerformanceFixturePolicy.ScopeTag == tag,
                        "The friend-only performance scope did not publish its exact tag.");
                    bool nestedRejected = false;
                    try
                    {
                        new MemoryPerformanceFixturePolicyScope("illegal-nested");
                    }
                    catch (InvalidOperationException)
                    {
                        nestedRejected = true;
                    }
                    Require(nestedRejected,
                        "The nonreentrant performance-policy boundary accepted a nested override.");

                    PawnKnowledgeState state =
                        PawnDiaryMemoryM11RuntimeFixture.BuildThreadTargetOwner(
                            "Pawn_Performance_" + target,
                            target);
                    MemoryLogicalSizeResult sourceSize =
                        MemoryLogicalPayloadSizer.Size(state);
                    Require(sourceSize.valid && sourceSize.totalBytes > 0,
                        "Logical sizing failed for N=" + target + ": "
                            + sourceSize.errorPath);

                    long allocationBefore = GC.GetTotalMemory(false);
                    Stopwatch elapsed = Stopwatch.StartNew();
                    ScribeMeasurement first = RoundTrip(state, target, 1);
                    ScribeMeasurement second = RoundTrip(first.loaded, target, 2);
                    elapsed.Stop();
                    long allocationAfter = GC.GetTotalMemory(false);
                    long allocationDelta = Math.Max(0, allocationAfter - allocationBefore);
                    Require(first.logicalBytes == sourceSize.totalBytes
                            && second.logicalBytes == first.logicalBytes
                            && second.ownerEpoch == first.ownerEpoch
                            && first.rootCount == target
                            && second.rootCount == first.rootCount
                            && first.blockCount == target
                            && second.blockCount == first.blockCount
                            && first.xmlBytes > 0
                            && second.xmlBytes == first.xmlBytes,
                        "N=" + target
                            + " changed canonical shape/bytes across the second Scribe load.");
                    Require(sourceSize.totalBytes > priorLogicalBytes
                            && first.xmlBytes > priorXmlBytes,
                        "Increasing N did not increase both logical and exact XML bytes.");
                    Require(elapsed.Elapsed < TimeSpan.FromSeconds(30),
                        "N=" + target + " exceeded the loaded Scribe smoke hang guard.");

                    Log.Message("[Pawn Diary RimTest] Memory M11 performance smoke"
                        + " N=" + target
                        + " logicalBytes=" + sourceSize.totalBytes
                        + " xmlBytes=" + first.xmlBytes
                        + " elapsedMs=" + elapsed.ElapsedMilliseconds
                        + " observedHeapDelta=" + allocationDelta);
                    priorLogicalBytes = sourceSize.totalBytes;
                    priorXmlBytes = first.xmlBytes;
                }
                finally
                {
                    policyScope.Dispose();
                }
                Require(!MemoryPerformanceFixturePolicy.Active
                        && string.IsNullOrEmpty(MemoryPerformanceFixturePolicy.ScopeTag),
                    "The performance-policy scope did not restore after N=" + target + ".");
            }
        }

        private static ScribeMeasurement RoundTrip(
            PawnKnowledgeState source,
            int target,
            int pass)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "pawndiary_m11_perf_n" + target + "_p" + pass + "_"
                    + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                PawnKnowledgeState saved = source;
                Scribe.saver.InitSaving(path, "root");
                Scribe_Deep.Look(ref saved, "memory");
                Scribe.saver.FinalizeSaving();
                Scribe.mode = LoadSaveMode.Inactive;
                long xmlBytes = new FileInfo(path).Length;

                PawnKnowledgeState loaded = null;
                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "memory");
                Scribe.loader.FinalizeLoading();
                Scribe.mode = LoadSaveMode.Inactive;
                Require(loaded != null && loaded.IsCurrentSchema(),
                    "N=" + target + " pass " + pass + " did not load a current envelope.");
                MemoryLogicalSizeResult size = MemoryLogicalPayloadSizer.Size(loaded);
                Require(size.valid,
                    "N=" + target + " pass " + pass
                        + " failed logical registry validation: " + size.errorPath);
                return new ScribeMeasurement
                {
                    loaded = loaded,
                    logicalBytes = size.totalBytes,
                    xmlBytes = xmlBytes,
                    ownerEpoch = loaded.autobiographicalEpochToken,
                    blockCount = loaded.standaloneBlocks.Count
                        + SumVisibleBlocks(loaded),
                    rootCount = loaded.threadRoots.Count
                };
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch
                {
                    // A locked measurement file must not conceal the assertion that came first.
                }
            }
        }

        private static void Require(bool condition, string message)
        {
            PawnDiaryMemoryM11RuntimeFixture.Require(condition, message);
        }

        private static int SumVisibleBlocks(PawnKnowledgeState state)
        {
            int total = 0;
            for (int index = 0; index < state.threadRoots.Count; index++)
            {
                total += state.threadRoots[index]?.visibleBlocks?.Count ?? 0;
            }
            return total;
        }

        private sealed class ScribeMeasurement
        {
            internal PawnKnowledgeState loaded;
            internal long logicalBytes;
            internal long xmlBytes;
            internal string ownerEpoch = string.Empty;
            internal int rootCount;
            internal int blockCount;
        }
    }
}

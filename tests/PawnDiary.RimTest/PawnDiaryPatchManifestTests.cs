// Loaded-game canary for every startup Harmony registration. The manifest is populated before the
// test runner starts, so this suite can name all version-drift failures without launching gameplay
// actions or mutating a save.
using System;
using System.Collections.Generic;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Asserts that startup installed every required hook or intentionally skipped it.</summary>
    [TestSuite]
    public static class PawnDiaryPatchManifestTests
    {
        /// <summary>Fails with the complete degraded/failed hook list after a game update.</summary>
        [Test]
        public static void EveryRequiredHookIsHealthy()
        {
            PawnDiaryRimTestScope.Require(
                DiaryPatchManifest.AllHealthy(),
                "Pawn Diary startup hook manifest is unhealthy: "
                    + DiaryPatchManifest.BuildDetail());
        }

        /// <summary>Guards against an empty manifest being mistaken for a healthy one.</summary>
        [Test]
        public static void ManifestRecordedAppliedHooks()
        {
            PawnDiaryRimTestScope.Require(
                DiaryPatchManifest.Count(DiaryPatchManifest.HookStatus.Applied) > 0,
                "Pawn Diary startup hook manifest recorded no applied hooks: "
                    + DiaryPatchManifest.BuildSummary());
        }

        /// <summary>Checks that the Royalty gate reports the active or inactive setup explicitly.</summary>
        [Test]
        public static void RoyaltyGateMatchesActiveDlcState()
        {
            List<DiaryPatchManifest.Entry> entries = DiaryPatchManifest.Snapshot();
            DiaryPatchManifest.HookStatus expected = ModsConfig.RoyaltyActive
                ? DiaryPatchManifest.HookStatus.Applied
                : DiaryPatchManifest.HookStatus.Skipped;
            bool found = false;
            for (int i = 0; i < entries.Count; i++)
            {
                DiaryPatchManifest.Entry entry = entries[i];
                if (entry != null
                    && string.Equals(entry.area, "Royalty", StringComparison.Ordinal)
                    && entry.status == expected)
                {
                    found = true;
                    break;
                }
            }

            PawnDiaryRimTestScope.Require(
                found,
                "Royalty is " + (ModsConfig.RoyaltyActive ? "active" : "inactive")
                    + " but the startup manifest contains no Royalty "
                    + expected + " entry. " + DiaryPatchManifest.BuildSummary());
        }
    }
}

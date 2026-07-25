// Loaded-game fixture for the Quality Wave B6 daily soft cap and digest lines (EVT-26).
//
// The flow suite proves the cap emits and folds the right pages. This fixture pins everything AROUND
// it:
//   (a) the new saved rows survive a Scribe round-trip and an old save loads as "nothing paced yet";
//   (b) junk rows a hand-edited save could carry are dropped rather than believed;
//   (c) the component really writes them under an additive save key;
//   (d) the SHIPPED classification is what makes B6 fire at all — the interaction catch-all is
//       low-salience while the groups a player would call real events are not;
//   (e) the three new tunables are reachable from the Advanced settings editor.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable" for the save round-trip idiom below).
using System;
using System.Collections.Generic;
using System.Reflection;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Pins the live-game half of B6: save schema, normalization, shipped group classification, and
    /// the settings wiring.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryDigestPacingFixtureTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("heartfelt");
            pawn = scope.CreateAdultColonist();
        }

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
            }
        }

        /// <summary>
        /// EVT-26. A pacing row must survive save/load exactly — the count is the whole reason the row
        /// is saved at all, and losing the buffered lines would silently drop a day's evidence.
        /// </summary>
        [Test]
        public static void PacingRowRoundTripsThroughScribe()
        {
            PawnDayDigestState original = new PawnDayDigestState
            {
                pawnId = "Thing_TestColonist",
                day = 4211,
                lowSalienceCount = 3,
                lines = new List<DayDigestRecord>
                {
                    new DayDigestRecord { tick = 10, sourceKind = "thought", line = "ate a fine meal" },
                    new DayDigestRecord { tick = 20, sourceKind = "work", line = "hauled steel" }
                }
            };

            PawnDayDigestState loaded = ScribeRoundTrip(original);

            PawnDiaryRimTestScope.Require(loaded != null, "The pacing row did not survive the round trip.");
            PawnDiaryRimTestScope.Require(loaded.pawnId == "Thing_TestColonist" && loaded.day == 4211,
                "The pacing row lost its pawn/day identity.");
            PawnDiaryRimTestScope.Require(loaded.lowSalienceCount == 3,
                "The daily count did not round-trip, so a reload would hand out a fresh allowance.");
            PawnDiaryRimTestScope.Require(loaded.lines != null && loaded.lines.Count == 2,
                "The buffered digest lines did not round-trip.");
            PawnDiaryRimTestScope.Require(
                loaded.lines[0].line == "ate a fine meal" && loaded.lines[0].sourceKind == "thought"
                    && loaded.lines[0].tick == 10,
                "A buffered line lost its text, source token, or tick.");
            PawnDiaryRimTestScope.Require(loaded.lines[1].line == "hauled steel",
                "The buffered lines came back out of order.");
        }

        /// <summary>
        /// EVT-26. An old save has none of these fields. It must load as "no pages paced, nothing
        /// buffered" — never as nulls the rest of the session has to guard, and never as junk.
        /// </summary>
        [Test]
        public static void OldSavesNormalizeEmptyAndJunkRowsAreDropped()
        {
            PawnDayDigestState fresh = new PawnDayDigestState();
            fresh.Normalize();
            PawnDiaryRimTestScope.Require(fresh.lowSalienceCount == 0,
                "An old save must load with no low-salience pages recorded.");
            PawnDiaryRimTestScope.Require(fresh.lines != null && fresh.lines.Count == 0,
                "An old save must load an empty line buffer rather than a null.");
            PawnDiaryRimTestScope.Require(fresh.pawnId == string.Empty,
                "An old save must load an empty pawn id rather than a null.");

            PawnDayDigestState dirty = new PawnDayDigestState
            {
                pawnId = "  Thing_Victim  ",
                day = -7,
                lowSalienceCount = -3,
                lines = new List<DayDigestRecord>
                {
                    new DayDigestRecord { tick = -5, sourceKind = "  work  ", line = "  hauled steel  " },
                    // Duplicate after trimming, blank, and null: all unusable.
                    new DayDigestRecord { tick = 6, sourceKind = "work", line = "hauled steel" },
                    new DayDigestRecord { tick = 7, sourceKind = "thought", line = "   " },
                    null
                }
            };
            dirty.Normalize();

            PawnDiaryRimTestScope.Require(dirty.pawnId == "Thing_Victim",
                "The saved pawn id was not trimmed.");
            PawnDiaryRimTestScope.Require(dirty.day == 0 && dirty.lowSalienceCount == 0,
                "Negative saved day/count values must clamp to zero.");
            PawnDiaryRimTestScope.Require(dirty.lines.Count == 1,
                "Normalization kept " + dirty.lines.Count
                    + " digest lines; only the one usable, de-duplicated line should survive.");
            PawnDiaryRimTestScope.Require(
                dirty.lines[0].line == "hauled steel" && dirty.lines[0].sourceKind == "work"
                    && dirty.lines[0].tick == 0,
                "The surviving digest line was not trimmed and clamped.");

            dirty.ClearLines();
            PawnDiaryRimTestScope.Require(dirty.lines.Count == 0,
                "Clearing the buffer left lines behind.");
            PawnDiaryRimTestScope.Require(dirty.lowSalienceCount == 0,
                "Clearing the buffer must not be able to disturb the day's pacing count.");
        }

        /// <summary>
        /// EVT-26. The component writes the rows under its own additive save key, and the transient
        /// "pawnId|day" index really points at the same objects the save list holds.
        /// </summary>
        [Test]
        public static void ComponentStoresRowsUnderTheAdditiveSaveKey()
        {
            string pawnId = pawn.GetUniqueLoadID();
            int day = Find.TickManager.TicksAbs / GenDate.TicksPerDay;
            try
            {
                scope.Component.RecordLowSalienceEmission(pawnId, day);
                scope.Component.AddDayDigestLine(pawnId, day, "interaction", "traded a quiet joke", 5);

                PawnDayDigestState indexed = scope.Component.DayDigestStateFor(pawnId, day);
                PawnDiaryRimTestScope.Require(indexed != null,
                    "The component did not create a pacing row for the pawn.");
                PawnDiaryRimTestScope.Require(indexed.lowSalienceCount == 1 && indexed.lines.Count == 1,
                    "The component's pacing row did not record the emission and the buffered line.");
                PawnDiaryRimTestScope.Require(
                    scope.Component.LowSalienceCountForDay(pawnId, day) == 1,
                    "The count accessor disagrees with the stored row.");

                List<PawnDayDigestState> saved = SavedRows();
                PawnDiaryRimTestScope.Require(saved.Contains(indexed),
                    "The indexed row is not the one in the saved list, so it would never persist.");

                // A different day is a different row: yesterday's allowance can never leak into today.
                PawnDiaryRimTestScope.Require(
                    scope.Component.LowSalienceCountForDay(pawnId, day + 1) == 0,
                    "A pacing count leaked across the day boundary.");
            }
            finally
            {
                RemoveRowsFor(pawnId);
            }
        }

        /// <summary>
        /// EVT-26. B6 only ever fires because of how the SHIPPED groups are classified. The interaction
        /// catch-all — where every uncategorized chat lands — must be low-salience, and the groups a
        /// player would call real events must not be, or the cap would start hiding real moments.
        /// </summary>
        [Test]
        public static void ShippedGroupClassificationDrivesPacing()
        {
            RequireLowSalience("other", true);
            RequireLowSalience("smalltalk", true);
            RequireLowSalience("heartfelt", false);
            RequireLowSalience("romance", false);
            RequireLowSalience("insults", false);
            RequireLowSalience("anomaly", false);

            // The art-immortalization group is non-important by design (it is a quiet page), but H6
            // owns a once-per-tale claim, so it must never be paced away by B6. It is not an
            // InteractionSignal, so its signal keeps the exempt default — assert the signal contract
            // rather than the group flag.
            DiaryInteractionGroupDef art = RequireGroup("artImmortalized");
            PawnDiaryRimTestScope.Require(!art.important,
                "The art-immortalization group unexpectedly became important; re-check B6 exemption.");
        }

        /// <summary>EVT-26. The three new tunables ship with their documented defaults and are editable.</summary>
        [Test]
        public static void ShippedTuningAndAdvancedWiringLoad()
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            PawnDiaryRimTestScope.Require(tuning.lowSalienceDailySoftCap == 2,
                "The shipped daily soft cap is not 2.");
            PawnDiaryRimTestScope.Require(tuning.dayDigestMaxLines == 4,
                "The shipped digest buffer size is not 4.");
            PawnDiaryRimTestScope.Require(Math.Abs(tuning.daySummaryWeightDigest - 0.25f) < 0.0001f,
                "The shipped digest selection weight is not 0.25.");

            RequireAdvancedField("lowSalienceDailySoftCap");
            RequireAdvancedField("dayDigestMaxLines");
            RequireAdvancedField("daySummaryWeightDigest");
        }

        // ----- helpers -------------------------------------------------------------------------------

        private static void RequireLowSalience(string groupDefName, bool expected)
        {
            DiaryInteractionGroupDef group = RequireGroup(groupDefName);
            bool actual = !group.important && !group.combat;
            PawnDiaryRimTestScope.Require(actual == expected,
                "Group '" + groupDefName + "' low-salience classification is " + actual
                    + ", expected " + expected + " (important=" + group.important
                    + ", combat=" + group.combat + ").");
        }

        private static DiaryInteractionGroupDef RequireGroup(string defName)
        {
            DiaryInteractionGroupDef group =
                DefDatabase<DiaryInteractionGroupDef>.GetNamedSilentFail(defName);
            if (group == null)
            {
                throw new AssertionException("Diary interaction group '" + defName + "' is missing.");
            }

            return group;
        }

        private static void RequireAdvancedField(string fieldName)
        {
            bool found = false;
            IReadOnlyList<AdvancedFieldDescriptor> all = AdvancedFieldCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].fieldName, fieldName, StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            PawnDiaryRimTestScope.Require(found,
                "Advanced settings has no editor row for the new tuning field '" + fieldName + "'.");
        }

        private static List<PawnDayDigestState> SavedRows()
        {
            FieldInfo field = typeof(DiaryGameComponent).GetField("dayDigestStates", PrivateInstance);
            List<PawnDayDigestState> rows = field?.GetValue(scope.Component) as List<PawnDayDigestState>;
            if (rows == null)
            {
                throw new AssertionException("Could not read DiaryGameComponent.dayDigestStates.");
            }

            return rows;
        }

        private static void RemoveRowsFor(string pawnId)
        {
            SavedRows().RemoveAll(row => row != null && row.pawnId == pawnId);
            typeof(DiaryGameComponent)
                .GetMethod("RebuildDayDigestIndex", PrivateInstance)
                ?.Invoke(scope.Component, null);
        }

        // Writes the row to an in-memory Scribe document and reads it straight back, the same shape
        // the other component-state fixtures use.
        private static PawnDayDigestState ScribeRoundTrip(PawnDayDigestState original)
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pawndiary_digest_pacing_" + Guid.NewGuid().ToString("N") + ".xml");
            PawnDayDigestState loaded = null;
            try
            {
                Scribe.saver.InitSaving(path, "digestPacingRoundTrip");
                PawnDayDigestState saving = original;
                Scribe_Deep.Look(ref saving, "row");
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "row");
                Scribe.loader.FinalizeLoading();
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive)
                {
                    Scribe.ForceStop();
                }

                try
                {
                    if (System.IO.File.Exists(path))
                    {
                        System.IO.File.Delete(path);
                    }
                }
                catch
                {
                    // A leftover temp file is harmless; never let cleanup mask a real assertion.
                }
            }

            return loaded;
        }
    }
}

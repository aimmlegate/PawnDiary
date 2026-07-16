// In-game save/load fixture for the component-level Biotech state contract (SAVE_COMPATIBILITY_
// SMOKETEST.md rows 8-10). The sibling suite PawnDiaryScribeRoundTripFixtureTests round-trips each
// Biotech model object on its own; this suite round-trips the CONTRACT the component writes — the
// exact BiotechSaveKeys keys, LookMode.Deep list scribing, and PostLoadInit normalization with the
// fixed 2048-row corruption ceiling — through RimWorld's real Scribe.
//
// Why a mirror object instead of a real DiaryGameComponent: the component's constructor owns live
// LLM-session state (it begins a session and cancels the previous one), so instantiating a second
// component inside a running test game would disturb the session that hosts the tests. The mirror
// below repeats the component's ExposeBiotechFamilyData/ExposeBiotechGrowthData contract verbatim;
// if the component's keys, modes, or normalizers change, update the mirror in the same change.
// The live component wiring itself stays covered by the manual smoketest (rows 2, 8-10).
//
// New to C#/RimWorld? See AGENTS.md ("IExposable", "DLC-safety"). These rows persist by value/string
// (no live Pawn references), so a standalone IExposable can host them for a file round-trip.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Round-trips the component-level Biotech lists (family arcs, pending births, pending growth
    /// moments, observation versions) through the real Scribe under their production save keys, and
    /// proves the load-time normalization: missing keys become empty lists, corrupt rows drop, and
    /// oversized lists truncate at the fixed hard ceiling while keeping the newest ownership rows.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryBiotechComponentStateFixtureTests
    {
        /// <summary>
        /// All three lists plus both observation versions survive one save/load under the exact
        /// component save keys, and normalization preserves every valid row.
        /// </summary>
        [Test]
        public static void ComponentContractRoundTripsAllThreeLists()
        {
            BiotechComponentStateMirror original = new BiotechComponentStateMirror
            {
                familyObservationVersion = 1,
                growthObservationVersion = 1,
                familyArcs = new List<BiotechFamilyArcState>
                {
                    new BiotechFamilyArcState
                    {
                        familyArcId = "biotech-family|Thing_CompChild",
                        childId = "Thing_CompChild",
                        birtherId = "Thing_CompBirther",
                        currentChildName = "Rue",
                        openedTick = 100,
                        lastObservedTick = 900,
                        recordedGrowthAges = new List<int> { 7 }
                    }
                },
                pendingBirths = new List<PendingBiotechBirthState>
                {
                    ValidPendingBirth("biotech-family|Thing_CompChild", "Thing_CompChild", 22000)
                },
                pendingGrowthMoments = new List<PendingBiotechGrowthMoment>
                {
                    ValidPendingGrowth("Thing_CompChild", 7, 30000),
                    ValidPendingGrowth("Thing_OtherChild", 10, 31000)
                }
            };

            BiotechComponentStateMirror loaded = ScribeRoundTrip(original);
            Require(loaded.familyArcs != null && loaded.familyArcs.Count == 1,
                "component family arcs should survive under BiotechSaveKeys.FamilyArcs.");
            AssertStr("biotech-family|Thing_CompChild", loaded.familyArcs[0].familyArcId,
                "component family arc id");
            Require(loaded.pendingBirths != null && loaded.pendingBirths.Count == 1,
                "component pending births should survive under BiotechSaveKeys.PendingBirths.");
            AssertStr("Thing_CompChild", loaded.pendingBirths[0].snapshot.childId,
                "component pending birth child");
            Require(loaded.pendingBirths[0].writers?.writers != null
                && loaded.pendingBirths[0].writers.writers.Count == 1,
                "component pending birth writers should survive normalization.");
            Require(loaded.pendingGrowthMoments != null && loaded.pendingGrowthMoments.Count == 2,
                "component pending growth rows should survive under BiotechSaveKeys.PendingGrowthMoments.");
            AssertInt(1, loaded.familyObservationVersion, "family observation version");
            AssertInt(1, loaded.growthObservationVersion, "growth observation version");
        }

        /// <summary>
        /// A save without any Biotech keys (every save written before the Biotech phases, and every
        /// no-DLC save) loads to non-null empty lists and version 0 — the exact old-save baseline the
        /// component's PostLoadInit normalization promises.
        /// </summary>
        [Test]
        public static void MissingBiotechKeysNormalizeToEmptyListsOnLoad()
        {
            BiotechComponentStateMirror original = new BiotechComponentStateMirror
            {
                familyArcs = null,
                pendingBirths = null,
                pendingGrowthMoments = null,
                familyObservationVersion = 0,
                growthObservationVersion = 0
            };

            BiotechComponentStateMirror loaded = ScribeRoundTrip(original);
            Require(loaded.familyArcs != null && loaded.familyArcs.Count == 0,
                "missing family-arc key should load as a non-null empty list.");
            Require(loaded.pendingBirths != null && loaded.pendingBirths.Count == 0,
                "missing pending-birth key should load as a non-null empty list.");
            Require(loaded.pendingGrowthMoments != null && loaded.pendingGrowthMoments.Count == 0,
                "missing pending-growth key should load as a non-null empty list.");
            AssertInt(0, loaded.familyObservationVersion, "pre-Biotech family version");
            AssertInt(0, loaded.growthObservationVersion, "pre-Biotech growth version");
        }

        /// <summary>
        /// Corrupt rows (null snapshot, blank ids) drop on load instead of surviving as dangling
        /// state, while valid neighbors are untouched.
        /// </summary>
        [Test]
        public static void CorruptRowsDropOnLoadWhileValidRowsSurvive()
        {
            BiotechComponentStateMirror original = new BiotechComponentStateMirror
            {
                pendingBirths = new List<PendingBiotechBirthState>
                {
                    new PendingBiotechBirthState { createdTick = 100 }, // null snapshot -> dropped
                    ValidPendingBirth("biotech-family|Thing_Valid", "Thing_Valid", 5000)
                },
                pendingGrowthMoments = new List<PendingBiotechGrowthMoment>
                {
                    new PendingBiotechGrowthMoment { pawnId = string.Empty, birthdayAge = 7 }, // dropped
                    ValidPendingGrowth("Thing_Valid", 7, 6000)
                }
            };

            BiotechComponentStateMirror loaded = ScribeRoundTrip(original);
            Require(loaded.pendingBirths != null && loaded.pendingBirths.Count == 1,
                "a null-snapshot pending birth should drop on load, keeping the valid row.");
            AssertStr("Thing_Valid", loaded.pendingBirths[0].snapshot.childId,
                "surviving pending birth child");
            Require(loaded.pendingGrowthMoments != null && loaded.pendingGrowthMoments.Count == 1,
                "a blank-pawn pending growth row should drop on load, keeping the valid row.");
            AssertStr("Thing_Valid", loaded.pendingGrowthMoments[0].pawnId,
                "surviving pending growth pawn");
        }

        /// <summary>
        /// A corrupt/oversized save (more rows than the fixed 2048 hard ceiling) truncates on load,
        /// evicting only the OLDEST rows — the exact promise in SAVE_COMPATIBILITY_SMOKETEST.md row 8
        /// that no automated test exercised before.
        /// </summary>
        [Test]
        public static void PendingOwnershipHardCeilingTruncatesOldestOnLoad()
        {
            int overflow = BiotechPendingOwnershipLimits.HardMaximumRows + 52;
            BiotechComponentStateMirror original = new BiotechComponentStateMirror
            {
                pendingBirths = new List<PendingBiotechBirthState>(overflow),
                pendingGrowthMoments = new List<PendingBiotechGrowthMoment>(overflow)
            };
            for (int i = 0; i < overflow; i++)
            {
                // Distinct arc/pawn ids defeat the per-arc/per-pawn dedup so only the ceiling binds;
                // ascending ticks make "oldest" deterministic (row 0 is the oldest).
                original.pendingBirths.Add(ValidPendingBirth(
                    "biotech-family|Thing_Cap" + i, "Thing_Cap" + i, 1000 + i));
                original.pendingGrowthMoments.Add(ValidPendingGrowth("Thing_Cap" + i, 7, 1000 + i));
            }

            BiotechComponentStateMirror loaded = ScribeRoundTrip(original);
            AssertInt(BiotechPendingOwnershipLimits.HardMaximumRows, loaded.pendingBirths.Count,
                "pending births at the hard ceiling after load");
            AssertInt(BiotechPendingOwnershipLimits.HardMaximumRows, loaded.pendingGrowthMoments.Count,
                "pending growth rows at the hard ceiling after load");
            Require(FindBirth(loaded.pendingBirths, "Thing_Cap" + (overflow - 1)) != null,
                "the newest pending birth must survive ceiling truncation.");
            Require(FindBirth(loaded.pendingBirths, "Thing_Cap0") == null,
                "the oldest pending birth must be the one evicted by the ceiling.");
            Require(FindGrowth(loaded.pendingGrowthMoments, "Thing_Cap" + (overflow - 1)) != null,
                "the newest pending growth row must survive ceiling truncation.");
            Require(FindGrowth(loaded.pendingGrowthMoments, "Thing_Cap0") == null,
                "the oldest pending growth row must be the one evicted by the ceiling.");
        }

        /// <summary>
        /// Rows established by an older/high-cap version remain owned above today's XML admission limit.
        /// A new owner is rejected without eviction; after enough old rows resolve to move below the current
        /// limit, one new row can enter and every other established identity remains intact. This automates
        /// the pre-cap checkpoint fixture.
        /// </summary>
        [Test]
        public static void PreCapOwnershipSurvivesAndAdmissionResumesAfterRoomReturns()
        {
            int admissionLimit = DiaryBiotechPolicy.Snapshot().maximumPendingGrowthRows;
            int birthAdmissionLimit = DiaryBiotechPolicy.Snapshot().maximumPendingBirthRows;
            Require(admissionLimit > 0 && admissionLimit < BiotechPendingOwnershipLimits.HardMaximumRows,
                "growth admission limit must leave room below the corruption ceiling for this fixture.");
            Require(birthAdmissionLimit > 0 && birthAdmissionLimit < BiotechPendingOwnershipLimits.HardMaximumRows,
                "birth admission limit must leave room below the corruption ceiling for this fixture.");

            int growthEstablished = admissionLimit + 1;
            int birthEstablished = birthAdmissionLimit + 1;
            BiotechComponentStateMirror original = new BiotechComponentStateMirror
            {
                pendingBirths = new List<PendingBiotechBirthState>(birthEstablished),
                pendingGrowthMoments = new List<PendingBiotechGrowthMoment>(growthEstablished)
            };
            for (int i = 0; i < birthEstablished; i++)
            {
                original.pendingBirths.Add(ValidPendingBirth(
                    "biotech-family|Thing_PreCapBirth" + i,
                    "Thing_PreCapBirth" + i,
                    2000 + i));
            }
            for (int i = 0; i < growthEstablished; i++)
            {
                original.pendingGrowthMoments.Add(ValidPendingGrowth(
                    "Thing_PreCapGrowth" + i,
                    10,
                    3000 + i));
            }

            BiotechComponentStateMirror loaded = ScribeRoundTrip(original);
            AssertInt(birthEstablished, loaded.pendingBirths.Count,
                "pre-cap pending births preserved after load");
            AssertInt(growthEstablished, loaded.pendingGrowthMoments.Count,
                "pre-cap pending growth rows preserved after load");
            Require(!BiotechPendingOwnershipLimits.CanAdmit(
                    loaded.pendingBirths.Count, birthAdmissionLimit),
                "an over-limit birth table incorrectly admitted another owner.");
            Require(!BiotechPendingOwnershipLimits.CanAdmit(
                    loaded.pendingGrowthMoments.Count, admissionLimit),
                "an over-limit growth table incorrectly admitted another owner.");

            // Resolve enough established rows to move below the authored limit. Admission resumes only
            // once Count < limit; the remaining identities must not be disturbed to make that room.
            loaded.pendingBirths.RemoveRange(0, loaded.pendingBirths.Count - birthAdmissionLimit + 1);
            loaded.pendingGrowthMoments.RemoveRange(
                0,
                loaded.pendingGrowthMoments.Count - admissionLimit + 1);
            Require(BiotechPendingOwnershipLimits.CanAdmit(
                    loaded.pendingBirths.Count, birthAdmissionLimit),
                "birth admission did not resume below the authored limit.");
            Require(BiotechPendingOwnershipLimits.CanAdmit(
                    loaded.pendingGrowthMoments.Count, admissionLimit),
                "growth admission did not resume below the authored limit.");

            string newestBirthId = "Thing_PreCapBirth" + (birthEstablished - 1);
            string newestGrowthId = "Thing_PreCapGrowth" + (growthEstablished - 1);
            loaded.pendingBirths.Add(ValidPendingBirth(
                "biotech-family|Thing_NewBirth", "Thing_NewBirth", 9000));
            loaded.pendingGrowthMoments.Add(ValidPendingGrowth("Thing_NewGrowth", 13, 9000));
            Require(FindBirth(loaded.pendingBirths, newestBirthId) != null,
                "admitting a new birth owner evicted an established birth row.");
            Require(FindGrowth(loaded.pendingGrowthMoments, newestGrowthId) != null,
                "admitting a new growth owner evicted an established growth row.");
            Require(FindBirth(loaded.pendingBirths, "Thing_NewBirth") != null,
                "birth admission did not add the new owner after room became available.");
            Require(FindGrowth(loaded.pendingGrowthMoments, "Thing_NewGrowth") != null,
                "growth admission did not add the new owner after room became available.");
        }

        // ---- fixture builders ----------------------------------------------------------------------

        private static PendingBiotechBirthState ValidPendingBirth(
            string familyArcId,
            string childId,
            int birthTick)
        {
            return new PendingBiotechBirthState
            {
                createdTick = birthTick,
                snapshot = new BirthMutationSnapshot
                {
                    familyArcId = familyArcId,
                    childId = childId,
                    currentChildName = "Baby",
                    outcomeToken = BiotechBirthOutcomeTokens.Healthy,
                    methodToken = BiotechBirthMethodTokens.Pregnancy,
                    namingDeadline = birthTick + 60000,
                    birthTick = birthTick,
                    correlationId = "birth|" + familyArcId
                },
                writers = new BirthWriterSelection
                {
                    writers = new List<BirthWriterFact>
                    {
                        new BirthWriterFact
                        {
                            pawnId = "Thing_Writer_" + childId,
                            displayName = "Writer",
                            roleToken = BiotechFamilyRoleTokens.Birther
                        }
                    }
                }
            };
        }

        private static PendingBiotechGrowthMoment ValidPendingGrowth(
            string pawnId,
            int birthdayAge,
            int birthdayTick)
        {
            return new PendingBiotechGrowthMoment
            {
                pawnId = pawnId,
                birthdayAge = birthdayAge,
                birthdayTick = birthdayTick,
                configuredTick = birthdayTick,
                growthTier = 4,
                familyArcId = "biotech-family|" + pawnId,
                birthdaySnapshot = new GrowthPawnSnapshot
                {
                    pawnId = pawnId,
                    displayName = "Child",
                    biologicalAge = birthdayAge,
                    growthTier = 4,
                    shortName = "Child",
                    capturedTick = birthdayTick
                }
            };
        }

        private static PendingBiotechBirthState FindBirth(
            List<PendingBiotechBirthState> rows,
            string childId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i]?.snapshot != null && rows[i].snapshot.childId == childId)
                {
                    return rows[i];
                }
            }

            return null;
        }

        private static PendingBiotechGrowthMoment FindGrowth(
            List<PendingBiotechGrowthMoment> rows,
            string pawnId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && rows[i].pawnId == pawnId)
                {
                    return rows[i];
                }
            }

            return null;
        }

        // ---- the component contract mirror ----------------------------------------------------------

        /// <summary>
        /// Repeats DiaryGameComponent.ExposeBiotechFamilyData + ExposeBiotechGrowthData verbatim: the
        /// same BiotechSaveKeys keys, LookMode.Deep, and PostLoadInit normalization with the fixed
        /// hard ceiling. One deliberate delta: the component normalizes at Find.TickManager.TicksGame,
        /// while this mirror uses a fixed large tick so fixture rows can never be dropped as
        /// "future" rows by whatever tick the hosting test game happens to be at.
        /// </summary>
        private sealed class BiotechComponentStateMirror : IExposable
        {
            private const int NormalizeTick = 500000;

            public List<BiotechFamilyArcState> familyArcs;
            public List<PendingBiotechBirthState> pendingBirths;
            public List<PendingBiotechGrowthMoment> pendingGrowthMoments;
            public int familyObservationVersion;
            public int growthObservationVersion;

            public void ExposeData()
            {
                Scribe_Collections.Look(ref familyArcs, BiotechSaveKeys.FamilyArcs, LookMode.Deep);
                Scribe_Collections.Look(ref pendingBirths, BiotechSaveKeys.PendingBirths, LookMode.Deep);
                Scribe_Values.Look(
                    ref familyObservationVersion, BiotechSaveKeys.FamilyObservationVersion, 0);
                Scribe_Collections.Look(
                    ref pendingGrowthMoments, BiotechSaveKeys.PendingGrowthMoments, LookMode.Deep);
                Scribe_Values.Look(
                    ref growthObservationVersion, BiotechSaveKeys.GrowthObservationVersion, 0);
                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
                    familyArcs = FamilyArcPolicy.Normalize(
                        familyArcs, NormalizeTick, policy.maximumSupporterRows);
                    pendingBirths = PendingBiotechBirthPolicy.Normalize(
                        pendingBirths, NormalizeTick, BiotechPendingOwnershipLimits.HardMaximumRows);
                    pendingGrowthMoments = PendingBiotechGrowthMomentPolicy.Normalize(
                        pendingGrowthMoments, NormalizeTick, BiotechPendingOwnershipLimits.HardMaximumRows);
                }
            }
        }

        // ---- Scribe round-trip machinery (same pattern as PawnDiaryScribeRoundTripFixtureTests) ------

        private static T ScribeRoundTrip<T>(T toSave) where T : class, IExposable
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pawndiary_rimtest_" + Guid.NewGuid().ToString("N") + ".xml");

            T loaded = null;
            try
            {
                Scribe.saver.InitSaving(path, "root");
                T saveRef = toSave;
                Scribe_Deep.Look(ref saveRef, "obj");
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "obj");
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
                    // Best-effort cleanup: a locked temp file must never fail the test itself.
                }
            }

            if (loaded == null)
            {
                throw new AssertionException(
                    "Scribe round-trip returned a null " + typeof(T).Name + ".");
            }

            return loaded;
        }

        // ---- assertion helpers -----------------------------------------------------------------------

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new AssertionException(message);
            }
        }

        private static void AssertStr(string expected, string actual, string label)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new AssertionException(
                    label + " mismatch: expected \"" + expected + "\" but got \"" + actual + "\".");
            }
        }

        private static void AssertInt(int expected, int actual, string label)
        {
            if (expected != actual)
            {
                throw new AssertionException(
                    label + " mismatch: expected " + expected + " but got " + actual + ".");
            }
        }
    }
}

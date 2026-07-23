// In-game tale-capture tests for Pawn Diary's TaleRecorder.RecordTale hook. Each test calls the exact
// vanilla API a real event would (TaleRecorder.RecordTale), which the production TaleRecorderPatch
// observes through Harmony, then verifies the persisted DiaryEvent's shape and participant extraction.
// All the fragile scaffolding — isolated non-generating pawns, snapshots, RNG isolation, and
// failure-safe teardown — lives in the shared PawnDiaryRimTestScope harness, so a test body only fires
// a trigger, asserts the outcome, and registers cleanup for the one thing the harness does not own: the
// historical Tale that RecordTale adds to Find.TaleManager.
//
// Coverage-matrix ID (design/TEST_COVERAGE_PLAN.md §3): EVT-09 Tale. This suite covers the non-combat, non-death
// slice — single-pawn shape, two-pawn shape + participant extraction, and the XML group toggle. Combat
// batching and death tales belong to EVT-10 and are intentionally out of scope here.
using System;
using System.Collections;
using System.Reflection;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that vanilla TaleRecorder.RecordTale reaches Pawn Diary's persisted event store with the
    /// correct shape (solo vs pairwise), extracts the right participants, and is suppressed when the
    /// classifying tale group is disabled. These tests require a loaded game because the production
    /// capture pipeline intentionally ignores events at the main menu.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryTaleFlowTests
    {
        private static PawnDiaryRimTestScope scope;
        private static Pawn firstPawn;
        private static Pawn secondPawn;

        /// <summary>
        /// Opens a fresh test scope, enables the two tale groups this suite drives (both ship
        /// default-enabled, so this only documents intent), and creates two isolated adult colonists
        /// with generation disabled.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("talequiet", "talelife");
            firstPawn = scope.CreateAdultColonist();
            secondPawn = scope.CreateAdultColonist();
        }

        /// <summary>
        /// Restores every mutation and audits that no test-owned event, diary, or historical tale
        /// survived — even when the test above threw partway through.
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
                firstPawn = null;
                secondPawn = null;
            }
        }

        /// <summary>
        /// EVT-09. Records a single-pawn vanilla tale (Meditated, a Tale_SinglePawn) and verifies the
        /// tale hook produced one solo diary event owned by that pawn.
        /// </summary>
        [Test]
        public static void SinglePawnTaleCreatesSoloEvent()
        {
            TaleDef taleDef = RequireDef<TaleDef>("Meditated");

            // Register the tale-removal cleanup BEFORE firing so a failing assertion inside
            // FireAndRequireEvent can never strand the historical tale in the developer's colony.
            Tale recordedTale = null;
            scope.RegisterCleanup(() => RemoveRecordedTale(recordedTale));

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => { recordedTale = TaleRecorder.RecordTale(taleDef, firstPawn); },
                "Meditated",
                firstPawn,
                null);

            scope.RequireSoloRef(diaryEvent, firstPawn);
            PawnDiaryRimTestScope.Require(
                recordedTale is Tale_SinglePawn,
                "Vanilla did not record the expected single-pawn tale for Meditated.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("tale=Meditated", StringComparison.OrdinalIgnoreCase) >= 0,
                "The tale event context did not identify the Meditated tale.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("taleClass=Tale_SinglePawn", StringComparison.OrdinalIgnoreCase) >= 0,
                "The tale event context did not record the single-pawn tale class.");
        }

        /// <summary>
        /// EVT-09. Records a two-pawn vanilla tale (Recruited, a Tale_DoublePawn) and verifies the tale
        /// hook produced one pairwise diary event whose initiator/recipient are the two supplied pawns
        /// (the first arg is the recruiter/initiator, the second is the joiner/recipient).
        /// </summary>
        [Test]
        public static void DoublePawnTaleCreatesPairEventWithBothParticipants()
        {
            TaleDef taleDef = RequireDef<TaleDef>("Recruited");

            Tale recordedTale = null;
            scope.RegisterCleanup(() => RemoveRecordedTale(recordedTale));

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => { recordedTale = TaleRecorder.RecordTale(taleDef, firstPawn, secondPawn); },
                "Recruited",
                firstPawn,
                secondPawn);

            scope.RequirePairRefs(diaryEvent, firstPawn, secondPawn);
            PawnDiaryRimTestScope.Require(
                recordedTale is Tale_DoublePawn,
                "Vanilla did not record the expected two-pawn tale for Recruited.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("tale=Recruited", StringComparison.OrdinalIgnoreCase) >= 0,
                "The tale event context did not identify the Recruited tale.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("taleClass=Tale_DoublePawn", StringComparison.OrdinalIgnoreCase) >= 0,
                "The tale event context did not record the two-pawn tale class.");
        }

        /// <summary>
        /// EVT-09. Disables the tale's classifying group (talequiet) and verifies the capture pipeline
        /// drops the same single-pawn tale that would otherwise become a solo event. The group override
        /// is reverted by the harness's settings snapshot in teardown.
        /// </summary>
        [Test]
        public static void DisabledTaleGroupCreatesNoEvent()
        {
            TaleDef taleDef = RequireDef<TaleDef>("Meditated");

            // Force the classifying group off for this test. talequiet ships default-enabled, so this
            // stores a player override that the scope's group-settings snapshot restores in teardown.
            PawnDiaryMod.Settings.SetGroupEnabled("talequiet", false);
            PawnDiaryRimTestScope.Require(
                !PawnDiaryMod.Settings.IsTaleEnabled(taleDef),
                "Disabling the talequiet group should have turned the Meditated tale off.");

            // RecordTale still creates and files the historical tale even though no diary event results,
            // so it must still be cleaned up.
            Tale recordedTale = null;
            scope.RegisterCleanup(() => RemoveRecordedTale(recordedTale));

            scope.RequireNoNewEvent(
                () => { recordedTale = TaleRecorder.RecordTale(taleDef, firstPawn); });
        }

        // ----- helpers ---------------------------------------------------------------------------

        // Verse.TaleManager exposes no public single-tale removal (volatile tales normally expire on
        // their own during RemoveExpiredTales). Test cleanup removes the one tale it filed directly from
        // the manager's private backing list — the same reflection pattern the shared harness uses for
        // private state. Resolved by List<Tale> type, so a field rename does not silently break it.
        private static readonly FieldInfo TalesListField = ResolveTalesListField();

        private static FieldInfo ResolveTalesListField()
        {
            FieldInfo[] fields = typeof(TaleManager).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            for (int i = 0; i < fields.Length; i++)
            {
                Type fieldType = fields[i].FieldType;
                if (typeof(IList).IsAssignableFrom(fieldType)
                    && fieldType.IsGenericType
                    && fieldType.GetGenericArguments()[0] == typeof(Tale))
                {
                    return fields[i];
                }
            }

            return null;
        }

        private static void RemoveRecordedTale(Tale tale)
        {
            if (tale == null || Find.TaleManager == null || TalesListField == null)
            {
                return;
            }

            IList tales = TalesListField.GetValue(Find.TaleManager) as IList;
            tales?.Remove(tale);
        }

        private static TDef RequireDef<TDef>(string defName) where TDef : Def
        {
            TDef def = DefDatabase<TDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                throw new AssertionException(
                    "Required vanilla " + typeof(TDef).Name + " '" + defName + "' was not loaded.");
            }

            return def;
        }
    }
}

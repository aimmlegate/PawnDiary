// In-game reaction tests for Pawn Diary's real RimWorld event hooks. Each test calls a vanilla game
// API that Pawn Diary observes through Harmony, then verifies the resulting saved DiaryEvent. All the
// fragile scaffolding — isolated non-generating pawns, snapshots, and failure-safe teardown — lives in
// the shared PawnDiaryRimTestScope harness, so a test body only fires a trigger and asserts an outcome.
//
// Coverage-matrix IDs (design/TEST_COVERAGE_PLAN.md §3): EVT-01 interaction pair, EVT-07 romance,
// EVT-08 mental state. Later phases add the remaining EVT rows on this same harness.
using System;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that representative vanilla event choke points reach Pawn Diary's persisted event store.
    /// These tests require a loaded game because the production capture pipeline intentionally ignores
    /// events at the main menu.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryEventReactionTests
    {
        private static PawnDiaryRimTestScope scope;
        private static Pawn firstPawn;
        private static Pawn secondPawn;

        /// <summary>
        /// Opens a fresh test scope, enables only the groups this suite drives, and creates two
        /// isolated adult colonists with generation disabled.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("heartfelt", "romance_relation", "mentalbreakViolent");
            firstPawn = scope.CreateAdultColonist();
            secondPawn = scope.CreateAdultColonist();
        }

        /// <summary>
        /// Restores every mutation and audits that no test-owned event, diary, or log row survived —
        /// even when the test above threw partway through.
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
        /// EVT-01. Adds a normal vanilla social-log row and verifies that the PlayLog Harmony listener
        /// records one pairwise diary event linked back to that exact row.
        /// </summary>
        [Test]
        public static void SocialPlayLogEntryCreatesLinkedPairEvent()
        {
            InteractionDef interactionDef = RequireDef<InteractionDef>("DeepTalk");

            PlayLogEntry_Interaction entry = GeneratedSpeechPlayLog.CreateInteractionEntry(
                interactionDef,
                firstPawn,
                secondPawn);
            if (entry == null)
            {
                throw new AssertionException("Could not construct the vanilla DeepTalk PlayLog row.");
            }

            scope.TrackPlayLogEntry(entry);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => Find.PlayLog.Add(entry),
                "DeepTalk",
                firstPawn,
                secondPawn);

            scope.RequirePairRefs(diaryEvent, firstPawn, secondPawn);
            PawnDiaryRimTestScope.Require(
                string.Equals(diaryEvent.playLogInteractionDefName, "DeepTalk", StringComparison.Ordinal),
                "The diary event did not retain DeepTalk as its Social-log interaction Def.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.playLogEntryIds != null && diaryEvent.playLogEntryIds.Contains(entry.LogID),
                "The diary event was not linked to the PlayLog row that triggered it.");
        }

        /// <summary>
        /// EVT-07. Adds a vanilla Lover relation and verifies that the relation Harmony listener records
        /// one pairwise romance milestone for the two pawns.
        /// </summary>
        [Test]
        public static void LoverRelationCreatesPairEvent()
        {
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => firstPawn.relations.AddDirectRelation(PawnRelationDefOf.Lover, secondPawn),
                "Lover",
                firstPawn,
                secondPawn);

            scope.RequirePairRefs(diaryEvent, firstPawn, secondPawn);
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("kind=lover", StringComparison.OrdinalIgnoreCase) >= 0,
                "The romance event context did not identify the new Lover relation.");
        }

        /// <summary>
        /// EVT-08. Starts a real vanilla mental state and verifies that the mental-state Harmony listener
        /// records a solo break for the affected pawn.
        /// </summary>
        [Test]
        public static void BerserkMentalStateCreatesSoloEvent()
        {
            MentalStateDef stateDef = RequireDef<MentalStateDef>("Berserk");

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () =>
                {
                    bool started = firstPawn.mindState.mentalStateHandler.TryStartMentalState(
                        stateDef,
                        "Pawn Diary RimTest event reaction",
                        forced: true);
                    PawnDiaryRimTestScope.Require(
                        started, "Vanilla refused to start the forced Berserk mental state.");
                },
                "Berserk",
                firstPawn,
                null);

            scope.RequireSoloRef(diaryEvent, firstPawn);
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("mental_state=Berserk", StringComparison.OrdinalIgnoreCase) >= 0,
                "The mental-break context did not identify the Berserk state.");
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

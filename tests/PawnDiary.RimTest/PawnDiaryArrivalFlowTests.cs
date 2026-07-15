// In-game arrival-capture tests for Pawn Diary's colonist-arrival first page (EVT-18, TEST_COVERAGE_PLAN.md §3).
//
// A colonist's diary opens with one neutral "how I joined" description. Two production paths build it:
// the Pawn.SetFaction postfix (joins after game start) and the founding-colonist bootstrap scan
// (TryRecordStartingColonistArrivals, which iterates each map's FreeColonists once a new game finishes
// creating maps). Both submit the SAME per-pawn unit — DiaryEvents.Submit(new ArrivalSignal(pawn, context))
// (Source/Ingestion/Sources/ArrivalSignal.cs). Because that unit needs no map, storyteller, or colony
// fan-out, these tests drive it directly for the harness's isolated test pawn instead of reproducing the
// map scan, exactly as the WorkSignal suite drives its scanner's per-pawn unit directly.
//
// Two things about arrival differ from the interaction/tale/mental suites and are handled here:
//   1. The starting-arrival GAME CONTEXT (scenario name/description + childhood/adulthood backstory facts)
//      is built by DiaryGameComponent.BuildStartingArrivalContext(pawn), a private static. To assert those
//      real facts flow onto the page, the test resolves that exact production builder by reflection (a
//      field rename fails the test loudly, mirroring the harness's own private-field discipline).
//   2. ArrivalSignal.Emit SYNCHRONOUSLY queues the neutral arrival-description prompt (unlike the other
//      suites, whose Emit only records the event and leaves generation to a scan the test never ticks).
//      That neutral queue path (QueuePrompt) is gated per-pawn only for the initiator/recipient roles, NOT
//      for the neutral arrival role — so the harness's per-pawn "generation disabled" guarantee does not by
//      itself stop an LLM request here. To honor "no LLM request may leave the game," SetUp replaces the
//      settings' API-endpoint list with a single disabled row (snapshotted and restored in teardown); with
//      no active endpoint QueuePrompt marks the neutral role failed and returns before LlmClient.Enqueue.
//
// Coverage-matrix ID (TEST_COVERAGE_PLAN.md §3): EVT-18 Arrival.
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
    /// Proves that the real arrival-capture unit records exactly one neutral arrival-description page for a
    /// colonist, carrying scenario/backstory facts; that the page is inserted FIRST in the pawn's diary
    /// index even when an earlier event was already recorded; and that a malformed arrival unit is dropped
    /// without wedging a later valid arrival. These tests require a loaded game because the capture pipeline
    /// ignores events at the main menu; they never enable per-pawn generation and force the API-endpoint list
    /// empty, so no LLM request can leave the game.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryArrivalFlowTests
    {
        private const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

        // The founding bootstrap's private starting-arrival context builder (scenario + backstory facts).
        // Resolved once; a null handle means it was renamed, and the tests that need it fail loudly.
        private static readonly MethodInfo BuildStartingArrivalContextMethod = typeof(DiaryGameComponent)
            .GetMethod("BuildStartingArrivalContext", StaticNonPublic, null, new[] { typeof(Pawn) }, null);

        // The component's private pawnId -> record index. Used read-only to assert the ordered diary index.
        private static readonly FieldInfo DiariesByIdField =
            typeof(DiaryGameComponent).GetField("diariesById", InstanceNonPublic);

        private static PawnDiaryRimTestScope scope;
        private static Pawn arrivalPawn;

        /// <summary>
        /// Opens a fresh scope with the arrival group (and, for the ordering test, the violent-mental-break
        /// group) enabled, creates one isolated generation-disabled colonist, and neutralizes LLM dispatch so
        /// the neutral arrival prompt this suite drives can never send a request.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("arrival", "mentalbreakViolent");
            arrivalPawn = scope.CreateAdultColonist();
            NeutralizeLlmDispatch();
        }

        /// <summary>
        /// Restores every mutation (including the swapped-out endpoint list) and audits that no test-owned
        /// event, diary, or log row survived — even when a test above threw partway through.
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
                arrivalPawn = null;
            }
        }

        /// <summary>
        /// EVT-18. Submitting the real arrival unit for a colonist records exactly one page that is a NEUTRAL
        /// arrival description for that pawn (asserted through the arrival-description API), carrying the
        /// scenario and backstory facts the founding-arrival context builder produced. Re-submitting the same
        /// arrival is dropped, so the pawn keeps exactly one arrival page.
        /// </summary>
        [Test]
        public static void ColonistGetsExactlyOneNeutralArrivalDescriptionWithFacts()
        {
            string pawnId = arrivalPawn.GetUniqueLoadID();
            string arrivalContext = BuildStartingArrivalContext(arrivalPawn);

            DiaryEvent arrival = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new ArrivalSignal(arrivalPawn, arrivalContext)),
                ArrivalSignal.ArrivalDefName,
                arrivalPawn,
                null);

            // The neutral arrival-description API, not RequireSoloRef, is the contract under test.
            PawnDiaryRimTestScope.Require(
                arrival.HasArrivalDescription(),
                "The arrival page was not marked as a neutral arrival description.");
            PawnDiaryRimTestScope.Require(
                arrival.IsArrivalDescriptionFor(pawnId),
                "The arrival description did not belong to the colonist who joined.");
            PawnDiaryRimTestScope.Require(
                !arrival.IsArrivalDescriptionFor("PawnDiary_NotARealPawnId"),
                "The arrival description matched a pawn who did not join.");
            PawnDiaryRimTestScope.Require(
                scope.Component.HasArrivalEventFor(pawnId),
                "The component did not report an arrival page for the colonist after recording one.");

            // Scenario + backstory facts flow onto the page. A generated adult always has a titled childhood
            // backstory, so its title fact is guaranteed; the scenario name fact is asserted only when the
            // loaded game's scenario actually exposes a name (all vanilla scenarios do).
            RequireContextContains(arrival, "arrival_description=true");
            RequireContextContains(arrival, "arrival_source=game_start");
            RequireContextContains(arrival, "backstory=");
            Scenario scenario = Verse.Current.Game?.Scenario;
            if (scenario != null && !string.IsNullOrWhiteSpace(scenario.name))
            {
                RequireContextContains(arrival, "scenario_name=");
            }

            // A second identical arrival is dropped (ArrivalEventData.Decide sees HasExistingArrival), so the
            // colonist keeps exactly one arrival page.
            scope.RequireNoNewEvent(() => DiaryEvents.Submit(new ArrivalSignal(arrivalPawn, arrivalContext)));
        }

        /// <summary>
        /// EVT-18. When a colonist already has an earlier (non-arrival) diary event, recording their arrival
        /// inserts it at index 0 of their diary index — the arrival is always the first page of the arc
        /// (AddEventRef's arrival Insert(0) path), never appended after whatever incidental event happened to
        /// be captured first.
        /// </summary>
        [Test]
        public static void ArrivalIsInsertedFirstInDiaryIndex()
        {
            MentalStateDef berserk = RequireDef<MentalStateDef>("Berserk");

            // A real earlier event: a forced mental break recorded through the production mental-state hook.
            DiaryEvent earlierEvent = scope.FireAndRequireEvent(
                () =>
                {
                    bool started = arrivalPawn.mindState.mentalStateHandler.TryStartMentalState(
                        berserk,
                        "Pawn Diary RimTest arrival ordering",
                        forced: true);
                    PawnDiaryRimTestScope.Require(
                        started, "Vanilla refused to start the forced Berserk mental state.");
                },
                "Berserk",
                arrivalPawn,
                null);

            List<string> beforeArrival = ReadDiaryEventIds(arrivalPawn);
            PawnDiaryRimTestScope.Require(
                beforeArrival != null && beforeArrival.Contains(earlierEvent.eventId),
                "The earlier mental-break event was not indexed in the pawn's diary before the arrival.");

            DiaryEvent arrival = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new ArrivalSignal(arrivalPawn, BuildStartingArrivalContext(arrivalPawn))),
                ArrivalSignal.ArrivalDefName,
                arrivalPawn,
                null);

            List<string> afterArrival = ReadDiaryEventIds(arrivalPawn);
            PawnDiaryRimTestScope.Require(
                afterArrival != null && afterArrival.Count >= 2,
                "The pawn's diary index did not hold both the earlier event and the arrival.");
            PawnDiaryRimTestScope.Require(
                string.Equals(afterArrival[0], arrival.eventId, StringComparison.Ordinal),
                "The arrival page was not inserted first in the pawn's diary index.");
            PawnDiaryRimTestScope.Require(
                afterArrival.IndexOf(earlierEvent.eventId) > 0,
                "The earlier event did not remain after the arrival in the diary index.");
        }

        /// <summary>
        /// EVT-18. A malformed arrival unit (a null pawn, which yields a null payload) is dropped by the
        /// capture pipeline without throwing and without creating an event — the per-unit fault isolation the
        /// founding-arrival bootstrap relies on so one bad colonist cannot wedge the whole scan (the
        /// pre-2026-07-08 mid-loop abort). A subsequent valid arrival for a real colonist still records.
        /// </summary>
        [Test]
        public static void BadArrivalInputDoesNotWedgeSubsequentArrival()
        {
            // Bad unit first: dropped cleanly (Dispatch reads a null payload and returns false — no throw).
            scope.RequireNoNewEvent(
                () => DiaryEvents.Submit(new ArrivalSignal(null, "arrival_source=game_start")));

            // The next valid arrival still records exactly one neutral arrival page for the real colonist.
            string pawnId = arrivalPawn.GetUniqueLoadID();
            DiaryEvent arrival = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new ArrivalSignal(arrivalPawn, BuildStartingArrivalContext(arrivalPawn))),
                ArrivalSignal.ArrivalDefName,
                arrivalPawn,
                null);

            PawnDiaryRimTestScope.Require(
                arrival.IsArrivalDescriptionFor(pawnId),
                "A valid arrival submitted after a bad one did not record its arrival description.");
        }

        // ----- helpers -------------------------------------------------------------------------------

        /// <summary>
        /// Forces the API-endpoint list to a single disabled row so <c>ActiveEndpoints()</c> is empty and the
        /// synchronous neutral arrival prompt (QueuePrompt) marks the role failed and returns before enqueuing
        /// any request. The original list is restored in teardown, so the developer's real endpoints are
        /// untouched after the suite. Replacing the list (rather than clearing it) is deliberate: an empty
        /// list self-heals to a default MODEL-bearing endpoint in EnsureEndpointsList, which would be active.
        /// </summary>
        private static void NeutralizeLlmDispatch()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings == null)
            {
                throw new AssertionException("Pawn Diary settings must be loaded to neutralize LLM dispatch.");
            }

            List<ApiEndpointConfig> originalEndpoints = settings.apiEndpoints;
            settings.apiEndpoints = new List<ApiEndpointConfig>
            {
                new ApiEndpointConfig { enabled = false, url = string.Empty, model = string.Empty }
            };
            scope.RegisterCleanup(() => settings.apiEndpoints = originalEndpoints);
        }

        /// <summary>
        /// Invokes the founding bootstrap's private starting-arrival context builder so the arrival page
        /// carries the same scenario/backstory facts the real new-game scan would produce.
        /// </summary>
        private static string BuildStartingArrivalContext(Pawn pawn)
        {
            if (BuildStartingArrivalContextMethod == null)
            {
                throw new AssertionException(
                    "Pawn Diary test harness could not locate DiaryGameComponent.BuildStartingArrivalContext(Pawn).");
            }

            return (string)BuildStartingArrivalContextMethod.Invoke(null, new object[] { pawn });
        }

        /// <summary>Reads a copy of the pawn's ordered diary event-id index, or null if it has no record.</summary>
        private static List<string> ReadDiaryEventIds(Pawn pawn)
        {
            if (DiariesByIdField == null)
            {
                throw new AssertionException(
                    "Pawn Diary test harness could not locate the private 'diariesById' index field.");
            }

            Dictionary<string, PawnDiaryRecord> diariesById =
                DiariesByIdField.GetValue(scope.Component) as Dictionary<string, PawnDiaryRecord>;
            PawnDiaryRecord record = null;
            diariesById?.TryGetValue(pawn.GetUniqueLoadID(), out record);
            return record?.eventIds == null ? null : new List<string>(record.eventIds);
        }

        private static void RequireContextContains(DiaryEvent diaryEvent, string expectedFragment)
        {
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf(expectedFragment, StringComparison.Ordinal) >= 0,
                "The arrival event context did not contain the expected fact '" + expectedFragment + "'.");
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

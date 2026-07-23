// In-game observed-condition lifecycle tests for Pawn Diary's live-scan system (EVT-23,
// design/TEST_COVERAGE_PLAN.md §3). Observed conditions are NOT a one-shot Harmony hook: the component's
// private ScanObservedConditions (DiaryGameComponent.ObservedConditions.cs) periodically reads live
// game state (map danger, active game conditions, spawned evidence, pawn hediffs) for every home-map
// colonist, snapshots what it sees into plain ObservedConditionObservation DTOs, hands them to the pure
// ObservedConditionPolicy to diff against saved state, and then APPLIES the returned plan through
// ApplyObservedConditionPlan (persist/refresh/drop rows, optionally record a diary page, mark restart
// cooldowns). That scan only observes spawned free colonists on a loaded map, so — exactly like the Work
// and Tale suites — these tests do NOT drive the whole scan. They invoke the real per-scan processing
// unit directly for one isolated pawn: build the observations the scanner would have produced, run the
// production ObservedConditionPolicy.Plan, and apply it with the production ApplyObservedConditionPlan.
// This keeps the suite mapless and deterministic (the pure policy takes `now` as a parameter, so start
// and end debounces are stepped by passing synthetic future ticks rather than waiting for the game).
//
// This is the LIVE-SCAN counterpart to the pure tests/DiaryObservedConditionTests project: that project
// covers ObservedConditionPolicy in isolation; this one proves the impure apply step really mutates the
// saved activeObservedConditions store, honours the debounce/refresh transitions with the right scope
// identity, holds an unwritable optional page retryable, clears an ended condition, and that a recorded
// restart cooldown filters an immediate re-observation.
//
// PRIVATE STORE OWNERSHIP: the harness does NOT own the observed-condition stores, so this suite scrubs
// them itself in a registered cleanup (see harnessExtensionsNeeded in the delivery report):
//   - activeObservedConditions : List<ActiveObservedConditionState>, rows keyed by subjectPawnId.
//   - observedConditionCooldownUntilTick : Dictionary<string,int>, keyed by the identity string
//     "conditionKey|(int)scope|mapUniqueId|subjectPawnId" (pawn-scoped identities embed the pawn id).
// Every condition this suite creates is Pawn-scoped with subjectPawnId == the test pawn, so both stores
// are scrubbed by pawn id. No diary page is ever written (pages are either disabled or the mapless pawn
// has no on-map recipient), so the harness's own event/diary leak audit stays green.
//
// Coverage-matrix ID (design/TEST_COVERAGE_PLAN.md §3): EVT-23 Observed conditions.
using System;
using System.Collections.Generic;
using System.Reflection;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that Pawn Diary's observed-condition apply step (ApplyObservedConditionPlan) persists a
    /// started condition only after its start debounce, refreshes evidence/scope identity while observed,
    /// keeps an unwritable optional start page retryable, clears an ended condition after its end
    /// debounce, and that ending a condition records a restart cooldown that filters an immediate
    /// re-observation. Requires a loaded game (the capture pipeline ignores events at the main menu) but
    /// no loaded map, and never enables per-pawn generation, so no LLM request can leave the game.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryObservedConditionFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // Private DiaryGameComponent stores and processing units this suite drives by reflection (the
        // same pattern the shared harness uses for the component's other private state). A null handle
        // means the member was renamed and the suite fails loudly rather than silently skipping.
        private static readonly FieldInfo ActiveConditionsField =
            typeof(DiaryGameComponent).GetField("activeObservedConditions", PrivateInstance);
        private static readonly FieldInfo CooldownField =
            typeof(DiaryGameComponent).GetField("observedConditionCooldownUntilTick", PrivateInstance);
        private static readonly MethodInfo ApplyPlanMethod =
            typeof(DiaryGameComponent).GetMethod("ApplyObservedConditionPlan", PrivateInstance);
        private static readonly MethodInfo FilterCooldownMethod =
            typeof(DiaryGameComponent).GetMethod("FilterObservedConditionCooldownObservations", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn observerPawn;

        /// <summary>
        /// Opens a fresh scope, verifies the reflection handles this suite depends on, creates one
        /// isolated generation-disabled colonist, and registers the observed-condition store scrub the
        /// harness does not own (removed rows/cooldowns keyed by the test pawn).
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            RequireHandle(ActiveConditionsField, "activeObservedConditions");
            RequireHandle(CooldownField, "observedConditionCooldownUntilTick");
            RequireHandle(ApplyPlanMethod, "ApplyObservedConditionPlan");
            RequireHandle(FilterCooldownMethod, "FilterObservedConditionCooldownObservations");

            observerPawn = scope.CreateAdultColonist();

            // Scrub the two observed-condition stores the harness does not audit. Registered before any
            // test mutates them, and captured against the live component so it runs while the pawn ids
            // are still known even if an assertion throws partway through.
            DiaryGameComponent component = scope.Component;
            string pawnId = observerPawn.GetUniqueLoadID();
            scope.RegisterCleanup(() => ScrubObservedConditionState(component, pawnId));
        }

        /// <summary>
        /// Restores every mutation and audits that no test-owned event, diary, or log row survived — even
        /// when a test above threw partway through.
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
                observerPawn = null;
            }
        }

        /// <summary>
        /// EVT-23. A newly observed condition stays saved-but-unstarted while inside its start debounce,
        /// refreshes its last-observed tick and evidence on each subsequent scan, and records its start
        /// only once the debounce elapses — carrying the correct pawn-scoped identity throughout.
        /// </summary>
        [Test]
        public static void StartDebounceRecordsStateWithScopeIdentity()
        {
            DiaryGameComponent component = scope.Component;
            string pawnId = observerPawn.GetUniqueLoadID();
            int baseNow = Find.TickManager.TicksGame;
            DiaryObservedConditionDef def = MakeDef(
                "PawnDiaryTest_ObservedStart", startDebounce: 100, endDebounce: 2500,
                recordStart: false, recordEnd: false, restartCooldown: 0);
            string key = def.EffectiveConditionKey();

            // Scan 1: first observation, still inside the start debounce -> StartPending. The row is saved
            // (so a reload mid-state remembers it) but its start is NOT yet recorded.
            PlanAndApply(component, baseNow,
                NoSavedStates(),
                Observations(MakeObservation(def, pawnId, "gray flesh sample", 1)),
                def);
            ActiveObservedConditionState row = FindRow(component, pawnId, key);
            PawnDiaryRimTestScope.Require(row != null,
                "A start-pending observation should persist a saved observed-condition row.");
            PawnDiaryRimTestScope.Require(!row.startRecorded,
                "A condition still inside its start debounce must not record its start.");
            PawnDiaryRimTestScope.Require(
                row.scope == ObservedConditionScope.Pawn && row.mapUniqueId == -1
                    && string.Equals(row.subjectPawnId, pawnId, StringComparison.Ordinal),
                "The saved row must carry the pawn-scoped identity fields.");
            PawnDiaryRimTestScope.Require(row.firstObservedTick == baseNow,
                "firstObservedTick must anchor the start debounce at the first-seen tick.");

            // Scan 2: still observed and still inside the debounce -> Refresh. New evidence and a later
            // tick are captured, but the start remains unrecorded.
            PlanAndApply(component, baseNow + 50,
                SavedFrom(row),
                Observations(MakeObservation(def, pawnId, "gray flesh sample x2", 2)),
                def);
            row = FindRow(component, pawnId, key);
            PawnDiaryRimTestScope.Require(row != null && !row.startRecorded,
                "A refresh inside the start debounce must not record the start.");
            PawnDiaryRimTestScope.Require(row.lastObservedTick == baseNow + 50,
                "A refresh must advance the last-observed tick.");
            PawnDiaryRimTestScope.Require(
                row.lastSeenEvidenceCount == 2
                    && string.Equals(row.lastSeenEvidenceLabel, "gray flesh sample x2", StringComparison.Ordinal),
                "A refresh must capture the latest observed evidence.");

            // Scan 3: the start debounce has elapsed -> StartRecorded with the right scope identity.
            PlanAndApply(component, baseNow + 100,
                SavedFrom(row),
                Observations(MakeObservation(def, pawnId, "gray flesh sample x2", 2)),
                def);
            row = FindRow(component, pawnId, key);
            PawnDiaryRimTestScope.Require(row != null && row.startRecorded,
                "A condition past its start debounce must record its start.");
            string expectedIdentity = ObservedConditionStateSnapshot.Identity(
                key, ObservedConditionScope.Pawn, -1, pawnId);
            PawnDiaryRimTestScope.Require(
                string.Equals(row.ToSnapshot().IdentityKey(), expectedIdentity, StringComparison.Ordinal),
                "The recorded row must carry the pawn-scoped identity key.");
        }

        /// <summary>
        /// EVT-23. When an optional start page is wanted (recordStartEvent=true, recordScope=SubjectPawn)
        /// but the subject pawn is not on any map, the page recorder finds no eligible recipient and the
        /// apply step keeps the row RETRYABLE — persisted with startRecorded still false — so the next
        /// scan re-enters the same transition instead of permanently losing the page. No diary event is
        /// written (the harness event/diary leak audit confirms this in teardown).
        /// </summary>
        [Test]
        public static void OptionalStartPageHeldRetryableWhenNoRecipient()
        {
            DiaryGameComponent component = scope.Component;
            string pawnId = observerPawn.GetUniqueLoadID();
            int baseNow = Find.TickManager.TicksGame;
            DiaryObservedConditionDef def = MakeDef(
                "PawnDiaryTest_ObservedPage", startDebounce: 0, endDebounce: 2500,
                recordStart: true, recordEnd: false, restartCooldown: 0);
            string key = def.EffectiveConditionKey();

            // Start debounce 0 -> StartRecorded on the first scan. recordStartEvent=true wants a page, but
            // the generation-disabled test pawn is unspawned, so ObservedConditionPawns returns nobody and
            // RecordObservedConditionPage reports "retryable".
            PlanAndApply(component, baseNow,
                NoSavedStates(),
                Observations(MakeObservation(def, pawnId, "gray flesh sample", 1)),
                def);
            ActiveObservedConditionState row = FindRow(component, pawnId, key);
            PawnDiaryRimTestScope.Require(row != null,
                "A start whose page could not be written must still persist a retryable row.");
            PawnDiaryRimTestScope.Require(!row.startRecorded,
                "An unwritten optional start page must leave the start unrecorded so the next scan retries.");
        }

        /// <summary>
        /// EVT-23. A saved condition that live state no longer shows anchors its missing-since tick and is
        /// retained while inside its end debounce, then clears its saved row once the end debounce
        /// elapses. Uses a prompt-only condition (no start/end pages) so the transition is deterministic.
        /// </summary>
        [Test]
        public static void EndDebounceClearsState()
        {
            DiaryGameComponent component = scope.Component;
            string pawnId = observerPawn.GetUniqueLoadID();
            int baseNow = Find.TickManager.TicksGame;
            DiaryObservedConditionDef def = MakeDef(
                "PawnDiaryTest_ObservedEnd", startDebounce: 0, endDebounce: 2500,
                recordStart: false, recordEnd: false, restartCooldown: 0);
            string key = def.EffectiveConditionKey();

            ActiveObservedConditionState seeded = SeedStartedRow(component, def, pawnId, baseNow);

            // Scan with NO observation of the condition: it goes missing but is still inside the end
            // debounce, so its row is retained with the missing-since tick anchored.
            PlanAndApply(component, baseNow, SavedFrom(seeded), Observations(), def);
            ActiveObservedConditionState row = FindRow(component, pawnId, key);
            PawnDiaryRimTestScope.Require(row != null,
                "A missing condition inside its end debounce must be retained.");
            PawnDiaryRimTestScope.Require(row.firstMissingTick == baseNow,
                "The missing-since tick must be anchored when the condition first goes missing.");
            PawnDiaryRimTestScope.Require(!row.endRecorded,
                "The end must not be recorded before the end debounce elapses.");

            // Scan after the end debounce elapses: the condition ends and its saved row clears.
            PlanAndApply(component, baseNow + 2500, SavedFrom(row), Observations(), def);
            row = FindRow(component, pawnId, key);
            PawnDiaryRimTestScope.Require(row == null,
                "A condition missing past its end debounce must clear its saved row.");
        }

        /// <summary>
        /// EVT-23. Ending a condition whose Def sets a restart cooldown records that cooldown for the
        /// condition's identity, and a fresh observation of the same identity is filtered out while the
        /// cooldown is still active — so the same evidence lingering on the next poll cannot immediately
        /// re-record the condition.
        /// </summary>
        [Test]
        public static void RestartCooldownBlocksImmediateReRecord()
        {
            DiaryGameComponent component = scope.Component;
            string pawnId = observerPawn.GetUniqueLoadID();
            int realNow = Find.TickManager.TicksGame;
            DiaryObservedConditionDef def = MakeDef(
                "PawnDiaryTest_ObservedCooldown", startDebounce: 0, endDebounce: 0,
                recordStart: false, recordEnd: false, restartCooldown: 5000);
            string key = def.EffectiveConditionKey();

            ActiveObservedConditionState seeded = SeedStartedRow(component, def, pawnId, realNow);

            // End debounce 0 -> the missing condition ends immediately (EndRecorded), dropping the row and
            // marking a restart cooldown for its identity keyed off the real game tick.
            PlanAndApply(component, realNow, SavedFrom(seeded), Observations(), def);
            PawnDiaryRimTestScope.Require(FindRow(component, pawnId, key) == null,
                "The ended condition's saved row must clear.");

            Dictionary<string, int> cooldowns = CooldownField.GetValue(component) as Dictionary<string, int>;
            string identity = ObservedConditionStateSnapshot.Identity(
                key, ObservedConditionScope.Pawn, -1, pawnId);
            PawnDiaryRimTestScope.Require(
                cooldowns != null && cooldowns.ContainsKey(identity),
                "Ending the condition must record a restart cooldown for its identity.");

            // A fresh observation of the SAME identity is filtered out while the cooldown is active. The
            // real tick has not advanced 5000 ticks between these synchronous calls, so now < cooldownUntil.
            List<ObservedConditionObservation> observations =
                Observations(MakeObservation(def, pawnId, string.Empty, 1));
            Dictionary<string, DiaryObservedConditionDef> dueDefByKey = DueDefs(def);
            FilterCooldownMethod.Invoke(component,
                new object[] { observations, dueDefByKey, Find.TickManager.TicksGame });
            PawnDiaryRimTestScope.Require(observations.Count == 0,
                "An identity still in restart cooldown must be filtered before it can re-record.");
        }

        /// <summary>Old saves silently baseline active pollution into the existing condition store.</summary>
        [Test]
        public static void PollutionOldSaveBaselineMarksObservedRowsStarted()
        {
            List<ObservedConditionStateSnapshot> rows = ObservedConditionBaselinePolicy.BuildStartedRows(
                60000,
                new List<ObservedConditionObservation>
                {
                    new ObservedConditionObservation
                    {
                        conditionDefName = "BiotechPollutionCritical",
                        conditionKey = "biotech_pollution_critical",
                        scope = ObservedConditionScope.Map,
                        mapUniqueId = 3,
                        evidenceCount = 3
                    }
                });

            PawnDiaryRimTestScope.Require(
                rows.Count == 1 && rows[0].startRecorded && !rows[0].endRecorded
                    && rows[0].firstObservedTick == 60000,
                "Loaded pollution must become an already-started observed-condition row with no catch-up page.");
        }

        /// <summary>Pollution fanout is bounded while the old zero cap keeps legacy behavior.</summary>
        [Test]
        public static void PollutionWitnessSelectionIsBoundedAndLegacyDefaultsStayNeutral()
        {
            List<ObservedConditionWitnessCandidate> candidates = new List<ObservedConditionWitnessCandidate>
            {
                new ObservedConditionWitnessCandidate { pawnId = "Pawn_A" },
                new ObservedConditionWitnessCandidate { pawnId = "Pawn_B", hasVisibleRelevantHealth = true },
                new ObservedConditionWitnessCandidate { pawnId = "Pawn_C" }
            };
            List<string> bounded = ObservedConditionWitnessPolicy.SelectPawnIds(
                "biotech_pollution_severe", 7, 70000, candidates, 2);
            List<string> legacy = ObservedConditionWitnessPolicy.SelectPawnIds(
                "ordinary_condition", 7, 70000, candidates, 0);
            DiaryObservedConditionDef defaults = new DiaryObservedConditionDef();

            PawnDiaryRimTestScope.Require(
                bounded.Count == 2 && bounded.Contains("Pawn_B"),
                "The two-pawn cap must hold and prefer a visibly pollution-relevant pawn.");
            PawnDiaryRimTestScope.Require(
                legacy.Count == 3 && legacy[0] == "Pawn_A" && legacy[2] == "Pawn_C",
                "A zero cap must preserve the old all-recipient order.");
            PawnDiaryRimTestScope.Require(
                defaults.maxPagePawns == 0 && defaults.severityRank == 0
                    && defaults.minPollutionFraction == 0f && defaults.maxPollutionFraction < 0f
                    && string.IsNullOrEmpty(defaults.exclusiveFamilyKey),
                "New observed-condition fields must retain neutral defaults for unchanged Defs.");
        }

        // ----- production-unit drivers ------------------------------------------------------------

        // Runs the exact per-scan processing the private ScanObservedConditions does after gathering
        // observations: the pure ObservedConditionPolicy.Plan, then the production ApplyObservedConditionPlan.
        private static void PlanAndApply(DiaryGameComponent component, int now,
            List<ObservedConditionStateSnapshot> savedStates,
            List<ObservedConditionObservation> observations,
            DiaryObservedConditionDef def)
        {
            List<ObservedConditionDefSnapshot> defs =
                new List<ObservedConditionDefSnapshot> { def.ToDefSnapshot() };
            ObservedConditionPlan plan = ObservedConditionPolicy.Plan(now, savedStates, observations, defs);
            ApplyPlanMethod.Invoke(component, new object[] { plan, DueDefs(def) });
        }

        // ----- fixture builders -------------------------------------------------------------------

        // An unregistered pawn-scoped observed-condition Def the test fully controls. It is never added to
        // the DefDatabase — the apply step resolves its Def only from the dueDefByKey map passed in — so it
        // needs no cleanup. NOTE: the prompt-bias path (ActiveObservedConditionPromptCandidates) resolves
        // the Def via DefDatabase instead, so it is intentionally out of scope here (see gotchas).
        private static DiaryObservedConditionDef MakeDef(string defName, int startDebounce, int endDebounce,
            bool recordStart, bool recordEnd, int restartCooldown)
        {
            return new DiaryObservedConditionDef
            {
                defName = defName,
                scope = ObservedConditionScope.Pawn,
                observerType = ObservedConditionObserverType.PawnHediff,
                startDebounceTicks = startDebounce,
                endDebounceTicks = endDebounce,
                dedupTicks = 2500,
                pollIntervalTicks = 1000,
                recordStartEvent = recordStart,
                recordEndEvent = recordEnd,
                recordScope = ObservedConditionRecordScope.SubjectPawn,
                restartCooldownTicks = restartCooldown
            };
        }

        private static ObservedConditionObservation MakeObservation(DiaryObservedConditionDef def,
            string subjectPawnId, string evidenceLabel, int evidenceCount)
        {
            return new ObservedConditionObservation
            {
                conditionDefName = def.defName,
                conditionKey = def.EffectiveConditionKey(),
                scope = ObservedConditionScope.Pawn,
                mapUniqueId = -1,
                subjectPawnId = subjectPawnId,
                evidenceDefName = string.Empty,
                evidenceLabel = evidenceLabel ?? string.Empty,
                evidenceCount = evidenceCount
            };
        }

        // Directly seeds a started saved row (start already recorded, still observed) so the end-lifecycle
        // tests do not have to re-drive the start debounce first.
        private static ActiveObservedConditionState SeedStartedRow(DiaryGameComponent component,
            DiaryObservedConditionDef def, string pawnId, int firstObservedTick)
        {
            ActiveObservedConditionState row = new ActiveObservedConditionState
            {
                conditionDefName = def.defName,
                conditionKey = def.EffectiveConditionKey(),
                scope = ObservedConditionScope.Pawn,
                mapUniqueId = -1,
                subjectPawnId = pawnId,
                firstObservedTick = firstObservedTick,
                lastObservedTick = firstObservedTick,
                firstMissingTick = -1,
                startRecorded = true,
                endRecorded = false
            };

            List<ActiveObservedConditionState> rows =
                ActiveConditionsField.GetValue(component) as List<ActiveObservedConditionState>;
            if (rows == null)
            {
                throw new AssertionException("The component's activeObservedConditions store was null.");
            }

            rows.Add(row);
            return row;
        }

        private static List<ObservedConditionObservation> Observations(
            params ObservedConditionObservation[] items)
        {
            return items == null
                ? new List<ObservedConditionObservation>()
                : new List<ObservedConditionObservation>(items);
        }

        private static List<ObservedConditionStateSnapshot> NoSavedStates()
        {
            return new List<ObservedConditionStateSnapshot>();
        }

        private static List<ObservedConditionStateSnapshot> SavedFrom(ActiveObservedConditionState row)
        {
            return new List<ObservedConditionStateSnapshot> { row.ToSnapshot() };
        }

        private static Dictionary<string, DiaryObservedConditionDef> DueDefs(DiaryObservedConditionDef def)
        {
            return new Dictionary<string, DiaryObservedConditionDef> { { def.EffectiveConditionKey(), def } };
        }

        // ----- lookup + cleanup -------------------------------------------------------------------

        private static ActiveObservedConditionState FindRow(DiaryGameComponent component, string pawnId,
            string conditionKey)
        {
            List<ActiveObservedConditionState> rows =
                ActiveConditionsField.GetValue(component) as List<ActiveObservedConditionState>;
            if (rows == null)
            {
                return null;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                ActiveObservedConditionState row = rows[i];
                if (row != null
                    && string.Equals(row.subjectPawnId, pawnId, StringComparison.Ordinal)
                    && string.Equals(row.conditionKey, conditionKey, StringComparison.Ordinal))
                {
                    return row;
                }
            }

            return null;
        }

        // Removes every observed-condition store entry this suite could have created for the test pawn:
        // saved rows whose subjectPawnId is the pawn, and cooldown keys whose identity embeds the pawn id.
        private static void ScrubObservedConditionState(DiaryGameComponent component, string pawnId)
        {
            if (component == null || string.IsNullOrEmpty(pawnId))
            {
                return;
            }

            List<ActiveObservedConditionState> rows =
                ActiveConditionsField?.GetValue(component) as List<ActiveObservedConditionState>;
            rows?.RemoveAll(row => row != null
                && string.Equals(row.subjectPawnId, pawnId, StringComparison.Ordinal));

            Dictionary<string, int> cooldowns =
                CooldownField?.GetValue(component) as Dictionary<string, int>;
            if (cooldowns != null && cooldowns.Count > 0)
            {
                List<string> stale = null;
                foreach (KeyValuePair<string, int> entry in cooldowns)
                {
                    if (!string.IsNullOrEmpty(entry.Key)
                        && entry.Key.IndexOf(pawnId, StringComparison.Ordinal) >= 0)
                    {
                        (stale ?? (stale = new List<string>())).Add(entry.Key);
                    }
                }

                if (stale != null)
                {
                    for (int i = 0; i < stale.Count; i++)
                    {
                        cooldowns.Remove(stale[i]);
                    }
                }
            }
        }

        private static void RequireHandle(MemberInfo handle, string memberName)
        {
            if (handle == null)
            {
                throw new AssertionException(
                    "Pawn Diary observed-condition test suite could not locate private member '"
                    + memberName + "'.");
            }
        }
    }
}

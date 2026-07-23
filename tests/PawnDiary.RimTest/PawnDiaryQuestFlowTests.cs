// In-game quest-lifecycle tests for Pawn Diary's quest capture path (EVT-16, design/TEST_COVERAGE_PLAN.md §3).
//
// Quests are colony-wide: RimWorld's Quest.Accept / Quest.End Harmony hooks (DiaryQuestPatches.cs) call
// DiaryGameComponent.RecordQuestAccepted / RecordQuestEnded (DiaryGameComponent.Quests.cs), which submit
// a QuestFanoutSignal (Source/Ingestion/Sources/QuestSignal.cs). The fan-out's own PerPawnSignals loop
// walks every map's FreeColonists and yields one QuestPawnSignal per eligible colonist. That map walk is
// exactly the part these tests must NOT reproduce: the shared harness builds isolated, UNSPAWNED colonists
// that never appear on a map. So — mirroring the Work/Tale suites — the quest tests drive the per-unit
// production signal directly: they construct the real QuestFanoutSignal from a controlled Quest, then
// submit its per-pawn child, `DiaryEvents.Submit(new QuestPawnSignal(fanout, pawn, pawnId))`, which is the
// precise work the fan-out does for each colonist. This keeps every test mapless and deterministic.
//
// The pure quest decision (QuestEventData.Decide, Source/Capture/Events/QuestEventData.cs) generates a
// diary page ONLY for the "completed"/"failed" outcome signals; an "accepted" signal is bookkeeping-only
// and always drops. Bookkeeping means the quest's id is added to the component's private
// knownAcceptedQuestIds set (so the defensive accept-state scanner does not re-record it). The display
// label is sanitized by QuestEventData.BuildDisplayLabel: RimWorld can surface placeholder ("QuestName")
// or defName-style ("OpportunitySite_AncientComplex") names, and those are rejected/humanized before they
// reach a prompt. The generic event-type dedup window (DiaryTuning.genericEventTypeDedupTicks, applied in
// DiaryGameComponent.Dispatch via recentEvents) drops a duplicate submit inside the window.
//
// Determinism: the dedup window is forced to a large known value (snapshot/restore) so a second, identical
// submit in the same synchronous frame reliably collapses instead of racing the XML default. No per-pawn
// generation is ever enabled, so no LLM request can leave the game.
//
// Coverage-matrix ID (design/TEST_COVERAGE_PLAN.md §3): EVT-16 Quest.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that the real quest capture path — driven through its per-colonist production signal —
    /// (a) marks an accepted quest as seen without writing any diary page, (b) writes a sanitized,
    /// placeholder-free solo page for a completed or failed quest, and (c) drops a duplicate submit.
    /// These tests require a loaded game because the capture pipeline ignores events at the main menu;
    /// they never enable per-pawn generation, so no LLM request can leave the game.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryQuestFlowTests
    {
        // A long, known generic event-type dedup window so a second identical submit in the same frame is
        // reliably suppressed by the production recentEvents dedup rather than a short XML default.
        private const int ForcedGenericDedupTicks = 600000;

        // Reflection handle for the component's private "quests already seen accepted" bookkeeping set.
        // The store is transient and NOT part of the harness's owned/audited stores, so any id this suite
        // adds to it is removed via RegisterCleanup (see AcceptedQuest... below).
        private static readonly FieldInfo KnownAcceptedQuestIdsField =
            typeof(DiaryGameComponent).GetField(
                "knownAcceptedQuestIds", BindingFlags.Instance | BindingFlags.NonPublic);

        private static PawnDiaryRimTestScope scope;
        private static Pawn questPawn;

        /// <summary>
        /// Opens a fresh scope, enables every Quest-domain group this suite drives (accepted is off by
        /// default, so it must be forced on to exercise the accepted fan-out), creates one isolated
        /// generation-disabled colonist, and forces the generic dedup window to a deterministic value.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("questAccepted", "questCompleted", "questFailed");
            questPawn = scope.CreateAdultColonist();
            ForceDeterministicDedupWindow();
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
                questPawn = null;
            }
        }

        /// <summary>
        /// EVT-16. Accepting a quest updates the accept-scanner bookkeeping (the quest id is now "seen")
        /// but writes no diary page: accepted is a bookkeeping/event-window signal only, never a page.
        /// </summary>
        [Test]
        public static void AcceptedQuestUpdatesBookkeepingButEmitsNoPage()
        {
            Quest quest = BuildQuest(
                name: "Rescue the lost caravan",
                rootDefName: "PawnDiaryTest_AcceptedQuest",
                rootLabel: "rescue the lost caravan",
                description: "A neighbouring settlement begged the colony for help pulling survivors out.");

            HashSet<int> known = KnownAcceptedQuestIds();
            PawnDiaryRimTestScope.Require(
                !known.Contains(quest.id),
                "The freshly built quest should not yet be in the accepted-quest bookkeeping set.");

            // Ensure the bookkeeping id is scrubbed even if an assertion below throws. Captured into locals
            // because scope.Component is nulled at the very end of teardown, after custom cleanups run.
            DiaryGameComponent component = scope.Component;
            int questId = quest.id;
            scope.RegisterCleanup(() =>
            {
                HashSet<int> set = KnownAcceptedQuestIdsField?.GetValue(component) as HashSet<int>;
                set?.Remove(questId);
            });

            // The real accept hook path: marks the quest seen, then submits the accepted fan-out (which the
            // pure Decide drops for every colonist). No diary page must reference any test pawn.
            scope.RequireNoNewEvent(() => component.RecordQuestAccepted(quest));

            PawnDiaryRimTestScope.Require(
                KnownAcceptedQuestIds().Contains(quest.id),
                "Accepting the quest should have recorded its id in the accept-scanner bookkeeping set.");
        }

        /// <summary>
        /// EVT-16. A completed quest fans out to a solo page whose display label is sanitized — a RimWorld
        /// "QuestName" placeholder and a defName-style root are humanized to placeholder/underscore-free
        /// text — and whose game-context carries the completed signal and the cleaned quest label.
        /// </summary>
        [Test]
        public static void CompletedQuestFanoutEmitsSanitizedSoloPage()
        {
            // name is RimWorld's generated placeholder (rejected), root has no label, and its defName is a
            // code-token style name — so the display label must fall through to the humanized defName.
            Quest quest = BuildQuest(
                name: "QuestName",
                rootDefName: "OpportunitySite_AncientComplex",
                rootLabel: null,
                description: "The colony's shared effort finally saw the contract through to its end.");

            QuestFanoutSignal fanout = new QuestFanoutSignal(
                quest, QuestEventData.SignalCompleted, "PawnDiary.Event.QuestCompleted");

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new QuestPawnSignal(fanout, questPawn, questPawn.GetUniqueLoadID())),
                fanout.QuestDefName,
                questPawn,
                null);

            scope.RequireSoloRef(diaryEvent, questPawn);

            // The label must be scrubbed of the placeholder and of code-token/markup artifacts.
            string label = diaryEvent.interactionLabel ?? string.Empty;
            PawnDiaryRimTestScope.Require(
                label.IndexOf("QuestName", StringComparison.OrdinalIgnoreCase) < 0,
                "The completed-quest label leaked RimWorld's 'QuestName' placeholder.");
            PawnDiaryRimTestScope.Require(
                label.IndexOf('_') < 0 && label.IndexOf('<') < 0,
                "The completed-quest label was not humanized (still carries a code token or markup).");
            PawnDiaryRimTestScope.Require(
                label.StartsWith("Opportunity", StringComparison.Ordinal),
                "The completed-quest label was not humanized from the quest's defName.");

            RequireContextContains(diaryEvent, "signal=completed");
            // The model-facing quest label field must also be placeholder-free.
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("quest_label=", StringComparison.Ordinal) >= 0
                    && diaryEvent.gameContext.IndexOf("QuestName", StringComparison.OrdinalIgnoreCase) < 0,
                "The completed-quest game context did not carry a sanitized quest_label field.");
        }

        /// <summary>
        /// Odyssey O3. An exact gravcore root enriches the existing canonical Quest page with only its
        /// XML-owned visible site family; it must not invent recovery or a Mechhive ending choice.
        /// </summary>
        [Test]
        public static void ExactOdysseyQuestRootAddsCategoryWithoutInventingOutcome()
        {
            if (!ModsConfig.OdysseyActive)
            {
                Log.Message("[Pawn Diary RimTest] Odyssey inactive: exact Quest-root fixture skipped cleanly.");
                return;
            }

            Quest quest = BuildQuest(
                name: "Mechhive",
                rootDefName: "Gravcore_Mechhive",
                rootLabel: "mechhive",
                description: "The colony resolved the objective at the center of the mechanoid network.");
            QuestFanoutSignal fanout = new QuestFanoutSignal(
                quest, QuestEventData.SignalCompleted, "PawnDiary.Event.QuestCompleted");
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new QuestPawnSignal(fanout, questPawn, questPawn.GetUniqueLoadID())),
                fanout.QuestDefName,
                questPawn,
                null);

            RequireContextContains(diaryEvent, "odyssey_quest=true");
            RequireContextContains(diaryEvent, "odyssey_site_category=mechhive");
            RequireContextContains(diaryEvent, "odyssey_major_destination=true");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext.IndexOf("destroy", StringComparison.OrdinalIgnoreCase) < 0
                    && diaryEvent.gameContext.IndexOf("scavenge", StringComparison.OrdinalIgnoreCase) < 0
                    && diaryEvent.gameContext.IndexOf("recovered", StringComparison.OrdinalIgnoreCase) < 0,
                "Exact Odyssey Quest-root enrichment invented a terminal choice or gravcore recovery.");
        }

        /// <summary>
        /// EVT-16. A failed quest fans out to a solo page carrying the "failed" signal in its game context,
        /// confirming the outcome-to-signal mapping reaches the per-colonist entry.
        /// </summary>
        [Test]
        public static void FailedQuestFanoutEmitsFailedSignalPage()
        {
            Quest quest = BuildQuest(
                name: "Hold the ancient danger at bay",
                rootDefName: "PawnDiaryTest_FailedQuest",
                rootLabel: "hold the ancient danger at bay",
                description: "The threat overran the objective before the colony could finish it.");

            QuestFanoutSignal fanout = new QuestFanoutSignal(
                quest, QuestEventData.SignalFailed, "PawnDiary.Event.QuestFailed");

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new QuestPawnSignal(fanout, questPawn, questPawn.GetUniqueLoadID())),
                fanout.QuestDefName,
                questPawn,
                null);

            scope.RequireSoloRef(diaryEvent, questPawn);
            RequireContextContains(diaryEvent, "signal=failed");
            RequireContextContains(diaryEvent, "quest=" + fanout.QuestDefName);
        }

        /// <summary>
        /// EVT-16. Submitting the identical per-colonist quest entry twice in the same dedup window records
        /// exactly one page: the second submit is dropped by the generic event-type dedup. This is the
        /// mapless per-entry analogue of the fan-out's colony-wide "don't re-record the same quest" scan.
        /// </summary>
        [Test]
        public static void DuplicateQuestEntryIsDroppedByDedup()
        {
            Quest quest = BuildQuest(
                name: "Escort the wandering pilgrim",
                rootDefName: "PawnDiaryTest_DuplicateQuest",
                rootLabel: "escort the wandering pilgrim",
                description: "The colony agreed to walk a lone traveller safely to the next settlement.");

            QuestFanoutSignal fanout = new QuestFanoutSignal(
                quest, QuestEventData.SignalCompleted, "PawnDiary.Event.QuestCompleted");

            // First submit records the page.
            scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new QuestPawnSignal(fanout, questPawn, questPawn.GetUniqueLoadID())),
                fanout.QuestDefName,
                questPawn,
                null);

            // An identical second submit inside the forced dedup window is collapsed — no new page.
            scope.RequireNoNewEvent(
                () => DiaryEvents.Submit(new QuestPawnSignal(fanout, questPawn, questPawn.GetUniqueLoadID())));
        }

        // ----- test helpers -----------------------------------------------------------------------

        /// <summary>
        /// Builds a fully controlled quest without touching the real QuestManager. A unique id is drawn
        /// from the world's id manager so it can never collide with a live quest's bookkeeping id; the
        /// root is an unregistered QuestScriptDef the signal only reads through <c>quest.root</c>, and the
        /// parts list stays empty so faction/reward capture cleanly resolves to the "unknown"/"none"
        /// sentinels. The quest is never added to any manager, so it needs no cleanup.
        /// </summary>
        private static Quest BuildQuest(string name, string rootDefName, string rootLabel, string description)
        {
            return new Quest
            {
                id = Find.UniqueIDsManager.GetNextQuestID(),
                name = name,
                description = description,
                root = new QuestScriptDef
                {
                    defName = rootDefName,
                    label = rootLabel
                }
            };
        }

        /// <summary>
        /// Forces the generic event-type dedup window (shared by every source through
        /// DiaryTuning.Current) to a long, known value so a duplicate submit in the same synchronous frame
        /// is deterministically suppressed. The original value is restored in teardown.
        /// </summary>
        private static void ForceDeterministicDedupWindow()
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            int original = tuning.genericEventTypeDedupTicks;
            tuning.genericEventTypeDedupTicks = ForcedGenericDedupTicks;
            scope.RegisterCleanup(() => tuning.genericEventTypeDedupTicks = original);
        }

        /// <summary>Reads the component's private accepted-quest bookkeeping set via reflection.</summary>
        private static HashSet<int> KnownAcceptedQuestIds()
        {
            HashSet<int> set = KnownAcceptedQuestIdsField?.GetValue(scope.Component) as HashSet<int>;
            if (set == null)
            {
                throw new AssertionException(
                    "Could not read DiaryGameComponent.knownAcceptedQuestIds via reflection.");
            }

            return set;
        }

        private static void RequireContextContains(DiaryEvent diaryEvent, string expectedFragment)
        {
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf(expectedFragment, StringComparison.Ordinal) >= 0,
                "The quest event context did not contain the expected fragment '" + expectedFragment + "'.");
        }
    }
}

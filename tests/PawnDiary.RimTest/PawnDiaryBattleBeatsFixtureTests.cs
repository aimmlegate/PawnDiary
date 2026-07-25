// Loaded-game fixture for raid combat beats (Quality Wave §7.1, H1).
//
// The RULES — scoring, selection order, the retry/quiet/deadline decision, sanitizing, and the saved
// context fields — are pure and covered headlessly by DiaryPipelineTests. What only a loaded game can
// prove is the wiring around them:
//   (a) the real BattleBeatsBuilder runs against RimWorld's live Find.BattleLog without throwing and
//       reports "no battle" for a pawn who has not fought — the shape check that the 1.6 Battle/LogEntry
//       API this feature reflects over still exists;
//   (b) the generation gate: a fresh raid page refuses to queue and stamps a retry, an aged-out raid
//       page queues and records the permanent checked marker, a disabled feature marks and moves on,
//       and an already-mined page is never re-mined;
//   (c) the loaded prompt templates actually project battle_beats, at the exact field indexes the
//       Russian DefInjected labels are pinned to.
//
// This fixture deliberately does NOT create synthetic Battle/LogEntry rows. Battle.Add writes into the
// player's real combat log and each concerned pawn's records tracker, and there is no clean way to undo
// that — the same reason the raid suite drives one isolated per-pawn signal instead of the whole
// fan-out. Quoting a REAL fight is therefore a hands-on row in tests/SAVE_COMPATIBILITY_SMOKETEST.md,
// exactly as design/QUALITY_WAVE_IMPLEMENTATION_PLAN.md §13 allows for this feature.
//
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections;
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
    /// Pins the live-game half of H1: the combat-log scan against the real 1.6 API, the generation
    /// gate's retry/emit/marker behaviour, and the prompt-template projection.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryBattleBeatsFixtureTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // The gate lives on the partial DiaryGameComponent and is private, like the rest of the
        // generation queue. It hangs off QueueLlmRewrite rather than EnsureGenerationQueued because
        // that is the one funnel BOTH raid routes pass through: an ordinary walk-in raid arrives via
        // the periodic scanner, a drop-pod raid or infestation queues straight from its own emit tick.
        // A null handle means it was renamed, and every test fails loudly.
        private static readonly MethodInfo PrepareBattleBeatsMethod =
            typeof(DiaryGameComponent).GetMethod("TryPrepareBattleBeats", PrivateInstance);

        // The transient "do not queue until tick X" store. Keyed by eventId, so the shared harness scrub
        // (which matches pawn ids) cannot see it; the retry test removes its own key.
        private static readonly FieldInfo DelayedTicksField =
            typeof(DiaryGameComponent).GetField("delayedRaidGenerationReadyTicks", PrivateInstance);

        // Field indexes the Russian DefInjected labels are pinned to (fields.N.label). Inserting a row
        // anywhere above "combat beats" would silently attach the wrong translation, so these are
        // asserted rather than derived.
        private const int SoloImportantBattleBeatsIndex = 137;
        private const int PairCombatBattleBeatsIndex = 33;

        private static PawnDiaryRimTestScope scope;
        private static Pawn raidPawn;
        private static DiaryTuningDef tuningDef;
        private static int originalRaidGenerationDelayTicks;
        private static bool originalBattleBeatsEnabled;
        private static int originalBattleBeatsMaxAgeTicks;

        /// <summary>
        /// Opens a scope with the catch-all "raid" group enabled, creates one isolated
        /// generation-disabled colonist, and pins every tuning value these tests mutate so the
        /// developer's live tuning is restored even when a test throws partway through.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("raid");
            raidPawn = scope.CreateAdultColonist();

            tuningDef = DiaryTuning.Current;
            originalRaidGenerationDelayTicks = tuningDef.raidGenerationDelayTicks;
            originalBattleBeatsEnabled = tuningDef.battleBeatsEnabled;
            originalBattleBeatsMaxAgeTicks = tuningDef.battleBeatsMaxAgeTicks;
            // Force the anticipation delay off so the raid page takes the plain QueueSolo path and this
            // suite only ever exercises the battle-beats delay store (see the raid suite's note).
            tuningDef.raidGenerationDelayTicks = 0;
            scope.RegisterCleanup(() =>
            {
                if (tuningDef == null)
                {
                    return;
                }

                tuningDef.raidGenerationDelayTicks = originalRaidGenerationDelayTicks;
                tuningDef.battleBeatsEnabled = originalBattleBeatsEnabled;
                tuningDef.battleBeatsMaxAgeTicks = originalBattleBeatsMaxAgeTicks;
            });
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
                raidPawn = null;
                tuningDef = null;
            }
        }

        /// <summary>
        /// The scan runs against RimWorld's real combat log and reports no battle for a pawn who has
        /// never fought. This is the API shape check: Find.BattleLog.Battles, Battle.Concerns,
        /// Battle.LastEntryTimestamp, Battle.Entries and LogEntry.Tick must all still exist and behave,
        /// because the builder reaches them by name (and reflects over two protected damage fields).
        /// </summary>
        [Test]
        public static void CombatLogScanRunsAgainstTheRealApiAndFindsNoBattleForAPeacefulPawn()
        {
            RequireLoadedClock();
            int now = Find.TickManager.TicksGame;

            BattleBeatsInspection inspection = BattleBeatsBuilder.Inspect(
                raidPawn, "Pirate", now - 500, now, DiaryTuning.Current);

            PawnDiaryRimTestScope.Require(inspection != null && inspection.candidates != null,
                "The combat-log scan returned no inspection at all.");
            PawnDiaryRimTestScope.Require(!inspection.battleFound,
                "A freshly created colonist who has never fought reported a matching raid battle.");
            PawnDiaryRimTestScope.Require(inspection.candidates.Count == 0,
                "A freshly created colonist who has never fought produced combat beats.");

            // A disabled feature must not touch the combat log at all.
            DiaryTuning.Current.battleBeatsEnabled = false;
            PawnDiaryRimTestScope.Require(
                !BattleBeatsBuilder.Inspect(raidPawn, "Pirate", now - 500, now, DiaryTuning.Current).battleFound,
                "Disabled battle-beats mining still scanned the combat log.");
            DiaryTuning.Current.battleBeatsEnabled = originalBattleBeatsEnabled;
        }

        /// <summary>
        /// A raid page recorded this instant must NOT queue: the raiders may still be walking in, so the
        /// gate refuses and stamps a retry tick instead of writing a page that assumes the fight is over.
        /// </summary>
        [Test]
        public static void FreshRaidPageWaitsInsteadOfQueueing()
        {
            DiaryEvent diaryEvent = RecordRaidPage();
            PawnDiaryRimTestScope.Require(
                !BattleBeatsPolicy.AlreadyChecked(diaryEvent.gameContext),
                "A freshly recorded raid page was already marked as mined.");

            bool mayQueue = InvokePrepare(diaryEvent);

            PawnDiaryRimTestScope.Require(!mayQueue,
                "A raid page recorded this instant was allowed to queue before its fight resolved.");
            PawnDiaryRimTestScope.Require(
                !BattleBeatsPolicy.AlreadyChecked(diaryEvent.gameContext),
                "A raid page still waiting for its fight was already marked as mined.");
            PawnDiaryRimTestScope.Require(
                RemoveDelayKey(diaryEvent.eventId + "|" + DiaryEvent.InitiatorRole),
                "The waiting raid page did not stamp a retry tick, so the scanner would never come back.");
        }

        /// <summary>
        /// The hard deadline always wins. A raid page older than battleBeatsMaxAgeTicks queues from
        /// whatever evidence exists and permanently records that mining ran — the marker that stops a
        /// reloaded game from re-scanning a combat log that has since been pruned.
        /// </summary>
        [Test]
        public static void AgedOutRaidPageQueuesAndRecordsTheCheckedMarker()
        {
            DiaryEvent diaryEvent = RecordRaidPage();
            // Backdate the page past its own deadline. Only the deadline branch can fire from here, so
            // this test never depends on the developer's colony having (or not having) a live battle.
            diaryEvent.tick = Find.TickManager.TicksGame - (DiaryTuning.Current.battleBeatsMaxAgeTicks + 1);

            bool mayQueue = InvokePrepare(diaryEvent);

            PawnDiaryRimTestScope.Require(mayQueue,
                "A raid page past its hard deadline was still blocked from queueing.");
            PawnDiaryRimTestScope.Require(
                BattleBeatsPolicy.AlreadyChecked(diaryEvent.gameContext),
                "A queued raid page did not record the permanent battle_beats_checked marker.");
            PawnDiaryRimTestScope.Require(
                DiaryContextFields.HasField(diaryEvent.gameContext, RaidEventData.RaidContextKey),
                "Mining overwrote the raid page's own context fields.");

            // Second pass: an already mined page is never re-mined, so a reload cannot restart the scan.
            string mined = diaryEvent.gameContext;
            PawnDiaryRimTestScope.Require(InvokePrepare(diaryEvent),
                "An already mined raid page was blocked from queueing on a later pass.");
            PawnDiaryRimTestScope.Require(
                string.Equals(mined, diaryEvent.gameContext, StringComparison.Ordinal),
                "An already mined raid page was mined a second time.");
        }

        /// <summary>
        /// With the feature disabled the page queues immediately, carries no beats field, and still
        /// records the checked marker so it never re-enters the mining path.
        /// </summary>
        [Test]
        public static void DisabledMiningQueuesImmediatelyWithNoBeatsField()
        {
            DiaryTuning.Current.battleBeatsEnabled = false;
            DiaryEvent diaryEvent = RecordRaidPage();

            PawnDiaryRimTestScope.Require(InvokePrepare(diaryEvent),
                "Disabled battle-beats mining still blocked a raid page from queueing.");
            PawnDiaryRimTestScope.Require(
                BattleBeatsPolicy.AlreadyChecked(diaryEvent.gameContext),
                "Disabled battle-beats mining did not record the checked marker.");
            PawnDiaryRimTestScope.Require(
                !DiaryContextFields.HasField(diaryEvent.gameContext, BattleBeatsPolicy.BeatsContextKey),
                "Disabled battle-beats mining still wrote a beats field.");
        }

        /// <summary>
        /// Only raid pages are mined. Every other event must pass the gate untouched, because this check
        /// runs for every queued entry in the game.
        /// </summary>
        [Test]
        public static void NonRaidPagesPassTheGateUntouched()
        {
            DiaryEvent diaryEvent = RecordRaidPage();
            // Strip the raid marker to stand in for any non-raid page reaching the same gate.
            diaryEvent.gameContext = "thought=Sad; label=melancholy";

            PawnDiaryRimTestScope.Require(InvokePrepare(diaryEvent),
                "A non-raid page was blocked by the raid battle-beats gate.");
            PawnDiaryRimTestScope.Require(
                string.Equals("thought=Sad; label=melancholy", diaryEvent.gameContext, StringComparison.Ordinal),
                "A non-raid page had battle-beats fields written into its context.");

            // The recipient role can never own a raid page, so it must not mine either.
            diaryEvent.gameContext = RaidEventData.BuildGameContext("RaidEnemy", "Raid", "Pirate", "300");
            PawnDiaryRimTestScope.Require(
                InvokePrepare(diaryEvent, DiaryEvent.RecipientRole),
                "The recipient role was blocked by the raid battle-beats gate.");
            PawnDiaryRimTestScope.Require(
                !BattleBeatsPolicy.AlreadyChecked(diaryEvent.gameContext),
                "The recipient role mined a solo raid page it can never own.");
        }

        /// <summary>
        /// Both raid-capable templates project the beats field, at the exact indexes the Russian
        /// DefInjected labels are pinned to.
        /// </summary>
        [Test]
        public static void BothRaidTemplatesProjectBattleBeatsAtTheirPinnedIndexes()
        {
            RequireBattleBeatsField(DiaryPromptTemplates.SoloImportant, SoloImportantBattleBeatsIndex);
            RequireBattleBeatsField(DiaryPromptTemplates.PairCombat, PairCombatBattleBeatsIndex);
        }

        /// <summary>
        /// The shipped tuning must load from XML with a score for every kind token the builder can
        /// produce, or an unclassified beat would silently outrank a kill.
        /// </summary>
        [Test]
        public static void ShippedTuningCoversEveryBeatKind()
        {
            DiaryTuningDef tuning = DiaryTuning.Current;
            PawnDiaryRimTestScope.Require(tuning != null, "The Pawn Diary tuning Def was not loaded.");
            PawnDiaryRimTestScope.Require(tuning.battleBeatsMaxCount > 0
                    && tuning.battleBeatsScanBackBattles > 0
                    && tuning.battleBeatsScanBackEntries > 0
                    && tuning.battleBeatsRetryIntervalTicks > 0
                    && tuning.battleBeatsQuietTicks > 0
                    && tuning.battleBeatsMaxAgeTicks > 0
                    && tuning.battleBeatsMaxChars > 0,
                "A shipped battle-beats tuning value is non-positive, which would disable the feature.");

            string[] kinds =
            {
                BattleBeatsPolicy.KindTransition,
                BattleBeatsPolicy.KindHit,
                BattleBeatsPolicy.KindDeflected,
                BattleBeatsPolicy.KindMiss,
                BattleBeatsPolicy.KindOther
            };

            int previous = int.MaxValue;
            for (int i = 0; i < kinds.Length; i++)
            {
                int score = BattleBeatsPolicy.ScoreFor(kinds[i], tuning.battleBeatsScores, -1);
                PawnDiaryRimTestScope.Require(score >= 0,
                    "The shipped battle-beats score table has no row for kind '" + kinds[i] + "'.");
                PawnDiaryRimTestScope.Require(score < previous,
                    "The shipped battle-beats score table does not rank '" + kinds[i]
                    + "' below the preceding kind; a graze could outrank a kill.");
                previous = score;
            }
        }

        // ----- test helpers -----------------------------------------------------------------------

        /// <summary>
        /// Records one real solo raid page for the isolated test pawn, using the same per-colonist unit
        /// the raid fan-out yields (see PawnDiaryRaidFlowTests for why the whole fan-out is not driven).
        /// </summary>
        private static DiaryEvent RecordRaidPage()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                throw new AssertionException("The battle-beats fixture needs a loaded map (Find.CurrentMap was null).");
            }

            IncidentDef raidEnemy = DefDatabase<IncidentDef>.GetNamedSilentFail("RaidEnemy");
            if (raidEnemy == null)
            {
                throw new AssertionException("Required vanilla IncidentDef 'RaidEnemy' was not loaded.");
            }

            RaidFanoutSignal fanout = new RaidFanoutSignal(
                new IncidentParms { target = map, points = 260f }, raidEnemy);
            PawnDiaryRimTestScope.Require(!string.IsNullOrEmpty(fanout.ColonyDedupKey),
                "A RaidEnemy on the current map must be a diary-worthy raid (is the 'raid' group enabled?).");

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new RaidPawnSignal(fanout, raidPawn, raidPawn.GetUniqueLoadID())),
                "RaidEnemy",
                raidPawn,
                null);
            PawnDiaryRimTestScope.Require(
                DiaryContextFields.HasField(diaryEvent.gameContext, RaidEventData.RaidContextKey),
                "The recorded raid page carried no raid context marker to mine from.");
            return diaryEvent;
        }

        private static bool InvokePrepare(DiaryEvent diaryEvent, string povRole = null)
        {
            if (PrepareBattleBeatsMethod == null)
            {
                throw new AssertionException(
                    "Pawn Diary battle-beats fixture could not locate DiaryGameComponent.TryPrepareBattleBeats.");
            }

            object result = PrepareBattleBeatsMethod.Invoke(
                scope.Component,
                new object[] { diaryEvent, povRole ?? DiaryEvent.InitiatorRole, null });
            return result is bool && (bool)result;
        }

        /// <summary>Removes one stamped retry key and reports whether it was there.</summary>
        private static bool RemoveDelayKey(string key)
        {
            if (DelayedTicksField == null)
            {
                throw new AssertionException(
                    "Pawn Diary battle-beats fixture could not locate DiaryGameComponent.delayedRaidGenerationReadyTicks.");
            }

            IDictionary store = DelayedTicksField.GetValue(scope.Component) as IDictionary;
            if (store == null || !store.Contains(key))
            {
                return false;
            }

            store.Remove(key);
            return true;
        }

        private static void RequireBattleBeatsField(string templateKey, int expectedIndex)
        {
            List<DiaryPromptFieldDef> fields = DiaryPromptTemplates.FieldsFor(templateKey);
            PawnDiaryRimTestScope.Require(fields != null && fields.Count > expectedIndex,
                "The loaded '" + templateKey + "' prompt template has no field at index " + expectedIndex + ".");

            DiaryPromptFieldDef field = fields[expectedIndex];
            PawnDiaryRimTestScope.Require(field != null
                    && field.enabled
                    && string.Equals(field.source, "GameContext", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(field.contextKey, BattleBeatsPolicy.BeatsContextKey,
                        StringComparison.OrdinalIgnoreCase),
                "The loaded '" + templateKey + "' prompt template does not project battle_beats at index "
                + expectedIndex + ". The Russian DefInjected labels are pinned to that index, so a row was "
                + "inserted above it, or RimWorld loaded a stale Pawn Diary Def copy beside the RimTest DLL.");
        }

        private static void RequireLoadedClock()
        {
            PawnDiaryRimTestScope.Require(Find.TickManager != null,
                "The battle-beats fixture needs a loaded game clock.");
        }
    }
}

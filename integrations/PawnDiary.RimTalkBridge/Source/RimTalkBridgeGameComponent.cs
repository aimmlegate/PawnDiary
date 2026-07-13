// The RimTalk bridge's per-game owner. RimWorld auto-instantiates every GameComponent subclass with
// a (Game) constructor when a save/game loads, and ticks it. The bridge uses that lifecycle for two
// things:
//   • FinalizeInit — reset process-global static caches. Statics survive exit-to-menu + load, so a
//     stale cache from a previous colony would otherwise bleed into the new one (SKILL.md gotcha).
//   • GameComponentTick — a single throttled "pass" (~every 250 ticks) that refreshes the diary
//     context RimTalk reads, keeps persona sync current, and flushes finished conversations.
//
// Process-global REGISTRATION (RimTalk prompt hooks, Pawn Diary providers/listeners) lives in the
// mod constructor instead, because that runs once per process; this component runs once per game.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Session owner for the RimTalk bridge. Resets caches on load and drives the periodic pass.
    /// </summary>
    public class RimTalkBridgeGameComponent : GameComponent
    {
        // How often the periodic pass runs, in ticks (~4.16 s at 60 tps). The pass is cheap and
        // idempotent, so a coarse cadence keeps overhead off the per-frame path (performance gotcha).
        private const int PassIntervalTicks = 250;

        // Minimum TTL for the Level-1 diary-section cache refresh. A status listener already
        // invalidates the cache immediately when an entry changes, so this TTL only bounds how often
        // UNCHANGED pawns get re-read. It must NOT be coupled to conversationQuietTicks: that knob is
        // about chat flush latency and can be lowered to 250, which would otherwise force
        // GetContextSnapshot + GetWritingStyle to run for every colonist on every pass. This floor
        // keeps the refresh cost bounded regardless of the conversation setting (performance gotcha).
        private const int ContextRefreshTtlFloorTicks = 2500;

        // Last tick the pass ran. Compared by elapsed time (now - last), never TicksGame % N, so a dev
        // time-skip or save/load cannot desync the cadence (SKILL.md "Persistence & ticking").
        private int lastPassTick;

        // Rolling anti-spam timestamps are the only accepted-conversation state that survives a save.
        // The queue and in-flight handle remain transient by design; separate primitive fields below
        // preserve only assessment request cadence. Value/value Scribing keeps this plain pawn-id ->
        // tick map independent of live Pawn references.
        private Dictionary<string, int> conversationCooldownTicksByPawn = new Dictionary<string, int>();
        private List<string> conversationCooldownPawnKeys;
        private List<int> conversationCooldownTickValues;

        // Pawn Diary may temporarily force RimTalk's talk-initiation weight to zero for the
        // silent-focus hediff style. Preserve the player's previous value in the save so ending the
        // condition after a reload restores exactly what the player configured.
        private Dictionary<string, float> originalTalkWeightsByPawn = new Dictionary<string, float>();
        private List<string> originalTalkWeightPawnKeys;
        private List<float> originalTalkWeightValues;

        // Assessment requests spend tokens even when every candidate is later ignored. Persist the
        // day/gap counters so reloading cannot reopen the XML maxBatchesPerDay allowance. The queue and
        // active request remain transient; only these primitive cadence facts survive.
        private bool assessmentGateHaveDay;
        private int assessmentGateDayIndex;
        private int assessmentGateBatchesStartedToday;
        private bool assessmentGateHaveAttempt;
        private int assessmentGateLastAttemptTick;

        /// <summary>Required GameComponent constructor. RimWorld supplies the current game.</summary>
        /// <param name="game">The current RimWorld game instance.</param>
        public RimTalkBridgeGameComponent(Game game)
        {
        }

        /// <summary>
        /// Runs after the game is fully loaded. Clears static caches that could otherwise carry over
        /// from a previously played colony in the same process.
        /// </summary>
        public override void FinalizeInit()
        {
            DiaryContextInjector.ResetForNewGame();
            ColonyContextInjector.ResetForNewGame();
            SharedMemoryInjector.ResetForNewGame();
            PersonaSync.ResetForNewGame();
            RecentDiaryEventCache.ResetForNewGame();
            ConversationTracker.ResetForNewGame();
            ConversationTracker.RestorePawnCooldowns(conversationCooldownTicksByPawn);
            ConversationAssessmentCoordinator.ResetForNewGame();
            ConversationAssessmentCoordinator.RestoreAssessmentGate(new ConversationAssessmentBatchGateState
            {
                HaveDay = assessmentGateHaveDay,
                DayIndex = assessmentGateDayIndex,
                BatchesStartedToday = assessmentGateBatchesStartedToday,
                HaveAttempt = assessmentGateHaveAttempt,
                LastAttemptTick = assessmentGateLastAttemptTick
            });
            // The next GameComponentTick fires the first pass immediately (now - 0 >= interval for any
            // loaded game's TicksGame), which is fine — the caches were just cleared above.
            lastPassTick = 0;
        }

        /// <summary>Saves the rolling per-pawn chat-event cooldown so reload cannot bypass it.</summary>
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                conversationCooldownTicksByPawn = ConversationTracker.PawnCooldownSnapshot(now);

                ConversationAssessmentBatchGateState gate =
                    ConversationAssessmentCoordinator.AssessmentGateSnapshot();
                assessmentGateHaveDay = gate.HaveDay;
                assessmentGateDayIndex = gate.DayIndex;
                assessmentGateBatchesStartedToday = gate.BatchesStartedToday;
                assessmentGateHaveAttempt = gate.HaveAttempt;
                assessmentGateLastAttemptTick = gate.LastAttemptTick;
            }

            Scribe_Collections.Look(
                ref conversationCooldownTicksByPawn,
                "rimTalkConversationCooldownTicksByPawn",
                LookMode.Value,
                LookMode.Value,
                ref conversationCooldownPawnKeys,
                ref conversationCooldownTickValues);
            Scribe_Collections.Look(
                ref originalTalkWeightsByPawn,
                "rimTalkPersonaOriginalTalkWeightsByPawn",
                LookMode.Value,
                LookMode.Value,
                ref originalTalkWeightPawnKeys,
                ref originalTalkWeightValues);

            Scribe_Values.Look(ref assessmentGateHaveDay, "rimTalkAssessmentGateHaveDay", false);
            Scribe_Values.Look(ref assessmentGateDayIndex, "rimTalkAssessmentGateDayIndex", 0);
            Scribe_Values.Look(
                ref assessmentGateBatchesStartedToday,
                "rimTalkAssessmentGateBatchesStartedToday",
                0);
            Scribe_Values.Look(ref assessmentGateHaveAttempt, "rimTalkAssessmentGateHaveAttempt", false);
            Scribe_Values.Look(ref assessmentGateLastAttemptTick, "rimTalkAssessmentGateLastAttemptTick", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && conversationCooldownTicksByPawn == null)
            {
                conversationCooldownTicksByPawn = new Dictionary<string, int>();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && originalTalkWeightsByPawn == null)
            {
                originalTalkWeightsByPawn = new Dictionary<string, float>();
            }
        }

        /// <summary>Returns the saved pre-silent-focus weight for a pawn, when one is owned.</summary>
        internal bool TryGetOriginalTalkWeight(string pawnId, out float weight)
        {
            return originalTalkWeightsByPawn.TryGetValue(pawnId ?? string.Empty, out weight);
        }

        /// <summary>Captures a weight once; repeated periodic passes never replace the original.</summary>
        internal void RememberOriginalTalkWeight(string pawnId, float weight)
        {
            if (!string.IsNullOrWhiteSpace(pawnId) && !originalTalkWeightsByPawn.ContainsKey(pawnId))
            {
                originalTalkWeightsByPawn[pawnId] = weight;
            }
        }

        /// <summary>Releases a captured value after PersonaSync has restored it.</summary>
        internal void ForgetOriginalTalkWeight(string pawnId)
        {
            if (!string.IsNullOrWhiteSpace(pawnId))
            {
                originalTalkWeightsByPawn.Remove(pawnId);
            }
        }

        /// <summary>Throttled periodic work. Does nothing when RimTalk is absent or between intervals.</summary>
        public override void GameComponentTick()
        {
            if (!PawnDiaryRimTalkBridgeMod.RimTalkActive)
            {
                return;
            }

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - lastPassTick < PassIntervalTicks)
            {
                return;
            }

            lastPassTick = now;
            RunPass(now);
        }

        /// <summary>One periodic pass. Extended step by step as bridge features come online.</summary>
        private void RunPass(int now)
        {
            RefreshContextCaches(now);
            RefreshColonyContext(now);              // Feature 1: colony-situation block per map
            SharedMemoryInjector.ProcessQueue(now); // Feature 3: build pairs the provider requested
            SharedMemoryInjector.SyncAutoInject();  // Feature 3: reconcile the optional prompt entry
            PersonaSync.RunPass();
            ConversationTracker.ProcessDueConversations(now);
            ConversationAssessmentCoordinator.PollAndApply(now);
            ConversationAssessmentCoordinator.TryStartNewBatch(now);
        }

        /// <summary>
        /// Feature 1: rebuild the {{colony_events}} block for each colonist-bearing map whose cache is
        /// expired. Default OFF; skips entirely unless the toggle is on. Uses the same TTL floor as the
        /// diary section — colony state changes coarsely and the block is served from cache meanwhile.
        /// </summary>
        private void RefreshColonyContext(int now)
        {
            // Single shared gate (level + colony toggle + external-API enabled) so the refresh pass and
            // the read/build paths cannot disagree about whether the feature is on.
            if (!ColonyContextInjector.FeatureActive())
            {
                return;
            }

            HashSet<int> liveMaps = new HashSet<int>();
            foreach (Map map in Find.Maps)
            {
                if (map == null)
                {
                    continue;
                }

                liveMaps.Add(map.uniqueID);

                // A map with no free colonists has no one to talk: drop any block cached earlier (e.g. a
                // raid line) so SectionFor stops serving it once everyone has left or gone down. The read
                // path has no expiry of its own, so this is the only place that staleness is cleared.
                if (map.mapPawns == null || map.mapPawns.FreeColonistsSpawnedCount <= 0)
                {
                    ColonyContextInjector.ClearFor(map.uniqueID);
                    continue;
                }

                if (ColonyContextInjector.NeedsRefresh(map, now, ContextRefreshTtlFloorTicks))
                {
                    ColonyContextInjector.RefreshFor(map);
                }
            }

            // Evict blocks for maps that no longer exist (e.g. an abandoned settlement).
            ColonyContextInjector.RetainOnly(liveMaps);
        }

        /// <summary>
        /// Level 1: rebuild the RimTalk diary section for colonists whose cache is stale or expired.
        /// The TTL is floored at <see cref="ContextRefreshTtlFloorTicks"/> so lowering the conversation
        /// quiet window (a chat-flush latency knob) cannot accidentally turn this into a per-pass
        /// snapshot storm. A status listener still invalidates the cache immediately on entry changes.
        /// </summary>
        private void RefreshContextCaches(int now)
        {
            if (!PawnDiaryRimTalkBridgeMod.LevelAtLeast(1))
            {
                return;
            }

            // The quiet-ticks value is allowed to raise the TTL (a slower chat flush implies "fresh
            // enough" can be coarser too), but never to lower it below the floor.
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            int quietTicks = settings != null ? settings.conversationQuietTicks : ContextRefreshTtlFloorTicks;
            int ttl = quietTicks > ContextRefreshTtlFloorTicks ? quietTicks : ContextRefreshTtlFloorTicks;

            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonistsSpawned)
            {
                if (DiaryContextInjector.NeedsRefresh(pawn, now, ttl))
                {
                    DiaryContextInjector.RefreshFor(pawn);
                }
            }
        }
    }
}

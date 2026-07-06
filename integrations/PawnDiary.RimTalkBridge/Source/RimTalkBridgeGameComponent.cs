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

        // Last tick the pass ran. Compared by elapsed time (now - last), never TicksGame % N, so a dev
        // time-skip or save/load cannot desync the cadence (SKILL.md "Persistence & ticking").
        private int lastPassTick;

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
            PersonaSync.ResetForNewGame();
            ConversationTracker.ResetForNewGame();
            RimTalkEngineClient.ResetForNewGame();
            // Schedule the first pass one interval out so load settles before we touch pawns.
            lastPassTick = 0;
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
            PersonaSync.RunTierBPass();
            ConversationTracker.ProcessDueConversations(now);
            RimTalkEngineClient.DrainResults();
        }

        /// <summary>
        /// Level 1: rebuild the RimTalk diary section for colonists whose cache is stale or expired.
        /// The TTL reuses the conversation quiet window as a convenient "how fresh is fresh enough" knob.
        /// </summary>
        private void RefreshContextCaches(int now)
        {
            if (!PawnDiaryRimTalkBridgeMod.LevelAtLeast(1))
            {
                return;
            }

            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            int ttl = settings != null ? settings.conversationQuietTicks : 2500;

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

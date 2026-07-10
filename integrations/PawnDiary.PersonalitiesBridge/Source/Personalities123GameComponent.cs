// The bridge's per-game owner. RimWorld auto-instantiates every GameComponent subclass with a (Game)
// constructor when a save/game loads, and ticks it. The bridge uses that lifecycle for two things:
//   • FinalizeInit — clear the process-global Tier B bookkeeping (and any override placed under a
//     previous colony), since statics survive exit-to-menu + load and would otherwise bleed across.
//   • GameComponentTick — a single throttled pass (~every 250 ticks) that keeps the personality-led
//     outlook overrides in sync and clears them the moment the toggle turns off.
//
// Tier A needs no pass: it is a pull-based context provider registered once in the mod constructor.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using RimWorld;
using Verse;

namespace PawnDiaryPersonalities123
{
    /// <summary>
    /// Session owner for the 1-2-3 Personalities bridge. Resets caches on load and drives the periodic
    /// Tier B pass.
    /// </summary>
    public class Personalities123GameComponent : GameComponent
    {
        // How often the periodic pass runs, in ticks (~4.16 s at 60 tps). The pass is cheap and
        // idempotent (change-detected per pawn), so a coarse cadence keeps overhead off the hot path.
        private const int PassIntervalTicks = 250;

        // Last tick the pass ran. Compared by elapsed time (now - last), never TicksGame % N, so a dev
        // time-skip or save/load cannot desync the cadence.
        private int lastPassTick;

        /// <summary>Required GameComponent constructor. RimWorld supplies the current game.</summary>
        /// <param name="game">The current RimWorld game instance.</param>
        public Personalities123GameComponent(Game game)
        {
        }

        /// <summary>
        /// Runs after the game is fully loaded. Clears Tier B bookkeeping and any override that could
        /// otherwise carry over from a previously played colony in the same process.
        /// </summary>
        public override void FinalizeInit()
        {
            if (!PawnDiaryPersonalities123Mod.SimplePersonalitiesActive)
            {
                return;
            }

            EnneagramSync.ResetForNewGame();
            // The next GameComponentTick fires the first pass immediately (now - 0 >= interval for any
            // loaded game's TicksGame), which is fine — the bookkeeping was just cleared above.
            lastPassTick = 0;
        }

        /// <summary>Throttled periodic work. Does nothing when 1-2-3 Personalities is absent or between intervals.</summary>
        public override void GameComponentTick()
        {
            if (!PawnDiaryPersonalities123Mod.SimplePersonalitiesActive)
            {
                return;
            }

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - lastPassTick < PassIntervalTicks)
            {
                return;
            }

            lastPassTick = now;
            EnneagramSync.RunTierBPass();
        }
    }
}

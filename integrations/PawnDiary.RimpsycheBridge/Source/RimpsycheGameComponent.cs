// Per-game lifecycle owner for the Rimpsyche bridge.
//
// RimWorld discovers GameComponent subclasses by reflection (there is no main()). This component:
//   * resets process-static caches when a colony/save is loaded;
//   * restores/saves Tier C's primitive pair-cooldown ticks so reload cannot bypass the gate;
//   * drives Tier B's cheap debounced sync pass every ~250 game ticks.
//
// It intentionally names no RimPsyche.dll types. All target-mod reads stay in PsycheSync's
// NoInlining methods behind the cached active-mod guard.
//
// New to C#/RimWorld? See AGENTS.md (GameComponent / IExposable) and docs/lore/performance.md.
using System.Collections.Generic;
using Verse;

namespace PawnDiaryRimpsyche
{
    /// <summary>Per-save bridge cadence, static reset, and persisted conversation cooldown owner.</summary>
    public class RimpsycheGameComponent : GameComponent
    {
        private const int PassIntervalTicks = 250;

        private int lastPassTick;
        private bool firstPassPending = true;

        // Scribe can persist a primitive dictionary. The two temporary lists are required by RimWorld's
        // dictionary overload and are not meaningful bridge state themselves.
        private Dictionary<string, int> conversationCooldownTicksByPair =
            new Dictionary<string, int>();
        private List<string> conversationCooldownPairKeys;
        private List<int> conversationCooldownTickValues;

        /// <summary>Required framework constructor; RimWorld supplies the current Game.</summary>
        public RimpsycheGameComponent(Game game)
        {
        }

        /// <summary>Saves/restores the adapter's per-pair accepted-conversation timestamps.</summary>
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                conversationCooldownTicksByPair = ConversationCapture.CooldownSnapshot();
            }

            Scribe_Collections.Look(
                ref conversationCooldownTicksByPair,
                "rimpsycheConversationCooldownTicksByPair",
                LookMode.Value,
                LookMode.Value,
                ref conversationCooldownPairKeys,
                ref conversationCooldownTickValues);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && conversationCooldownTicksByPair == null)
            {
                conversationCooldownTicksByPair = new Dictionary<string, int>();
            }
        }

        /// <summary>
        /// Clears process-global state so exit-to-menu + load cannot leak another colony's cache, then
        /// restores this save's primitive cooldown map. Pawn Diary API calls are deferred to Tick because
        /// FinalizeInit may run off the main thread in RimWorld 1.6.
        /// </summary>
        public override void FinalizeInit()
        {
            PsycheSync.PrepareForNewGame();
            ConversationCapture.ResetForNewGame();
            ConversationCapture.RestoreCooldowns(conversationCooldownTicksByPair);
            firstPassPending = true;
            lastPassTick = 0;
        }

        /// <summary>Runs the Tier-B apply/reset choreography on a coarse elapsed-time cadence.</summary>
        public override void GameComponentTick()
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            long elapsed = (long)now - lastPassTick;
            if (!firstPassPending && elapsed >= 0 && elapsed < PassIntervalTicks)
            {
                return;
            }

            // A backwards/reset clock also runs immediately, then establishes a fresh elapsed baseline.
            firstPassPending = false;
            lastPassTick = now;
            PsycheSync.RunTierBPass();
        }
    }
}

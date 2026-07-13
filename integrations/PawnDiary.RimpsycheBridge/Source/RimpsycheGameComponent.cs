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

        // Tier-B synchronization state is saved with the game. Persist both the deterministic source key
        // and resolved target rule so a transformed result can be retried locally after reload/arbitration
        // without purchasing another LLM request.
        private Dictionary<string, string> personaSourceKeysByPawn = new Dictionary<string, string>();
        private Dictionary<string, string> personaTargetRulesByPawn = new Dictionary<string, string>();
        private List<string> personaSourcePawnKeys;
        private List<string> personaSourceValues;
        private List<string> personaTargetPawnKeys;
        private List<string> personaTargetValues;

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
            Scribe_Collections.Look(ref personaSourceKeysByPawn, "rimpsychePersonaSourceKeysByPawn",
                LookMode.Value, LookMode.Value, ref personaSourcePawnKeys, ref personaSourceValues);
            Scribe_Collections.Look(ref personaTargetRulesByPawn, "rimpsychePersonaTargetRulesByPawn",
                LookMode.Value, LookMode.Value, ref personaTargetPawnKeys, ref personaTargetValues);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && conversationCooldownTicksByPair == null)
            {
                conversationCooldownTicksByPair = new Dictionary<string, int>();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                personaSourceKeysByPawn = personaSourceKeysByPawn ?? new Dictionary<string, string>();
                personaTargetRulesByPawn = personaTargetRulesByPawn ?? new Dictionary<string, string>();
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

        internal bool TryGetPersonaTarget(string pawnId, string sourceKey, out string target)
        {
            target = string.Empty;
            string savedKey;
            return personaSourceKeysByPawn.TryGetValue(pawnId ?? string.Empty, out savedKey)
                && string.Equals(savedKey, sourceKey ?? string.Empty, System.StringComparison.Ordinal)
                && personaTargetRulesByPawn.TryGetValue(pawnId ?? string.Empty, out target);
        }

        internal void RememberPersonaTarget(string pawnId, string sourceKey, string target)
        {
            if (string.IsNullOrWhiteSpace(pawnId)) return;
            personaSourceKeysByPawn[pawnId] = sourceKey ?? string.Empty;
            personaTargetRulesByPawn[pawnId] = target ?? string.Empty;
        }

        internal bool HasPersonaTarget(string pawnId)
        {
            return !string.IsNullOrWhiteSpace(pawnId) && personaSourceKeysByPawn.ContainsKey(pawnId);
        }

        internal bool HasPersonaTargets { get { return personaSourceKeysByPawn.Count > 0; } }

        internal void ForgetPersonaTarget(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId)) return;
            personaSourceKeysByPawn.Remove(pawnId);
            personaTargetRulesByPawn.Remove(pawnId);
        }

        internal void ClearPersonaTargets()
        {
            personaSourceKeysByPawn.Clear();
            personaTargetRulesByPawn.Clear();
        }
    }
}

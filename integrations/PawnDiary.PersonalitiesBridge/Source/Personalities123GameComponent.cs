// The bridge's per-game owner. RimWorld auto-instantiates every GameComponent subclass with a (Game)
// constructor when a save/game loads, and ticks it. This component owns ALL of the seeding logic:
//
//   • A throttled pass (~every 250 ticks) that, per spawned free colonist, applies the active tier
//     (Off / map-to-internal-psychotype / built-in override / LLM transform) — but only when the pawn's
//     Enneagram ROOT or the selected mode has changed since we last handled them, so a player's own edits
//     to the seeded psychotype survive between personality changes.
//   • The change-detection bookkeeping (pawnId -> "<mode>:<root>") is SAVED with the game, so a reload
//     never re-seeds an unchanged pawn and clobbers those edits.
//   • Draining the async Tier-3 LLM transform: the pass fires a completion request, then later passes
//     poll the handle and write the result into the editable custom rule (or fall back to Tier 2).
//   • A one-time migration sweep that releases the LOCKED external overrides placed by earlier bridge
//     versions, since those would otherwise shadow the new editable layers.
//
// SP_Module1 isolation: this component names NO SPM1 types itself — it only calls EnneagramSync's
// [NoInlining] readers, and only after the SimplePersonalitiesActive guard.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System.Collections.Generic;
using PawnDiary.Integration;
using PawnDiaryPersonalities123.Pure;
using RimWorld;
using Verse;

namespace PawnDiaryPersonalities123
{
    /// <summary>
    /// Session owner for the 1-2-3 Personalities bridge: drives the periodic seeding pass, persists the
    /// per-pawn change-detection state, and runs the one-time old-override migration on load.
    /// </summary>
    public class Personalities123GameComponent : GameComponent
    {
        // How often the pass runs, in ticks (~4.16 s at 60 tps). The pass is cheap and change-detected,
        // so a coarse cadence keeps overhead off the hot path.
        private const int PassIntervalTicks = 250;

        // Requested output budget for one transform (a couple of sentences).
        private const int TransformMaxTokens = 200;

        // Last tick the pass ran. Compared by elapsed time (now - last), never TicksGame % N, so a dev
        // time-skip or save/load cannot desync the cadence.
        private int lastPassTick;

        // pawnId -> "<mode>:<root>" we last successfully applied. SAVED: this is what lets a reload skip
        // an unchanged pawn instead of re-seeding over the player's edits. Re-seeds when either the mode
        // or the pawn's Enneagram root changes.
        private Dictionary<string, string> handledKey = new Dictionary<string, string>();

        // One-time flag: have we released the locked external overrides from earlier bridge versions?
        private bool overridesSwept;

        // pawnId -> in-flight Tier-3 transform. In-memory only: a transform lost to a reload simply
        // re-fires next pass (handledKey was not advanced for it), which is idempotent.
        private readonly Dictionary<string, InFlightJob> inFlight = new Dictionary<string, InFlightJob>();

        /// <summary>Required GameComponent constructor. RimWorld supplies the current game.</summary>
        /// <param name="game">The current RimWorld game instance.</param>
        public Personalities123GameComponent(Game game)
        {
        }

        /// <summary>Saves the per-pawn change-detection state and the migration flag with the game.</summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref handledKey, "handledKey", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref overridesSwept, "overridesSwept", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && handledKey == null)
            {
                handledKey = new Dictionary<string, string>();
            }
        }

        /// <summary>Runs after the game is fully loaded. Schedules an immediate first pass and performs the
        /// one-time old-override migration.</summary>
        public override void FinalizeInit()
        {
            if (!PawnDiaryPersonalities123Mod.SimplePersonalitiesActive)
            {
                return;
            }

            // now - 0 >= interval for any loaded game's TicksGame, so the first tick fires a pass at once.
            lastPassTick = 0;
            MigrateOldOverridesOnce();
        }

        /// <summary>Throttled periodic seeding. Does nothing when 1-2-3 Personalities is absent, the mode
        /// is Off, or we are between intervals.</summary>
        public override void GameComponentTick()
        {
            if (!PawnDiaryPersonalities123Mod.SimplePersonalitiesActive)
            {
                return;
            }

            PawnDiaryPersonalities123Settings settings = PawnDiaryPersonalities123Mod.Settings;
            if (settings == null || settings.mode == Personalities123Mode.Off)
            {
                // Disabled: drop any pending transform so a result cannot land after the player turned it
                // off. Player-owned psychotype values are left untouched (we never clear them).
                if (inFlight.Count > 0)
                {
                    inFlight.Clear();
                }

                return;
            }

            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - lastPassTick < PassIntervalTicks)
            {
                return;
            }

            lastPassTick = now;

            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonistsSpawned)
            {
                SyncPawn(pawn, settings);
            }
        }

        // Applies the active tier to one pawn, or advances an in-flight transform. Change-detected so an
        // already-handled (mode, root) is left alone — that is what keeps player edits.
        private void SyncPawn(Pawn pawn, PawnDiaryPersonalities123Settings settings)
        {
            if (pawn == null)
            {
                return;
            }

            string id = pawn.GetUniqueLoadID();
            string root = EnneagramSync.RootDefNameFor(pawn);
            if (string.IsNullOrEmpty(root))
            {
                // No usable personality (cleared/invalid/unmapped animal root): abandon any pending
                // transform and leave the pawn's player-owned psychotype as-is.
                inFlight.Remove(id);
                return;
            }

            // A Tier-3 transform in flight takes priority: drain it before considering a fresh seed.
            InFlightJob job;
            if (inFlight.TryGetValue(id, out job))
            {
                ResolvePendingTransform(pawn, id, job);
                return;
            }

            string key = KeyFor(settings.mode, root);
            string applied;
            if (handledKey.TryGetValue(id, out applied) && applied == key)
            {
                return;
            }

            switch (settings.mode)
            {
                case Personalities123Mode.InternalPsychotype:
                    ApplyInternalPsychotype(pawn, id, key, root);
                    break;
                case Personalities123Mode.Override:
                    ApplyOverrideRule(pawn, id, key, root);
                    break;
                case Personalities123Mode.LlmTransform:
                    StartOrFallbackTransform(pawn, id, key, root, settings);
                    break;
            }
        }

        // Tier 1: point the pawn's base psychotype at the mapped built-in type (pinned so the auto-roll
        // does not overwrite it). An unmapped root is skipped.
        private void ApplyInternalPsychotype(Pawn pawn, string id, string key, string root)
        {
            string defName = EnneagramLensMapping.InternalPsychotypeForRoot(root);
            if (string.IsNullOrEmpty(defName))
            {
                return;
            }

            if (PawnDiaryApi.SetPsychotype(pawn, defName, true))
            {
                handledKey[id] = key;
            }
        }

        // Tier 2 (and the Tier-3 fallback): seed the editable custom rule from the built-in outlook text.
        private void ApplyOverrideRule(Pawn pawn, string id, string key, string root)
        {
            string rule = EnneagramSync.ResolveOutlookRule(root);
            if (string.IsNullOrWhiteSpace(rule))
            {
                return;
            }

            if (PawnDiaryApi.SetPsychotypeCustomRule(pawn, rule))
            {
                handledKey[id] = key;
            }
        }

        // Tier 3: fire the LLM transform (tracked as in-flight), or fall back to Tier 2 immediately when
        // there is no input or no usable lane / the API rejected the call.
        private void StartOrFallbackTransform(Pawn pawn, string id, string key, string root, PawnDiaryPersonalities123Settings settings)
        {
            string input = EnneagramSync.BuildTransformInputFor(pawn);
            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            int handle = PawnDiaryApi.RequestLlmCompletion(new ExternalLlmCompletionRequest
            {
                sourceId = BridgeIds.ModId,
                laneIndex = settings.transformLaneIndex,
                systemPrompt = settings.ResolveTransformPrompt(),
                userText = input,
                maxTokens = TransformMaxTokens
            });

            if (handle > 0)
            {
                inFlight[id] = new InFlightJob { handle = handle, key = key, root = root };
                return;
            }

            ApplyOverrideRule(pawn, id, key, root);
        }

        // Polls one in-flight transform. Success writes the transformed text into the editable custom
        // rule; anything else (failed / unknown / blank text) falls back to the built-in outlook rule.
        private void ResolvePendingTransform(Pawn pawn, string id, InFlightJob job)
        {
            LlmCompletionResult result = PawnDiaryApi.GetLlmCompletionResult(job.handle);
            if (result.status == LlmCompletionStatus.Pending)
            {
                return;
            }

            if (result.status == LlmCompletionStatus.Succeeded && !string.IsNullOrWhiteSpace(result.text))
            {
                if (PawnDiaryApi.SetPsychotypeCustomRule(pawn, result.text))
                {
                    handledKey[id] = job.key;
                }
            }
            else
            {
                ApplyOverrideRule(pawn, id, job.key, job.root);
            }

            inFlight.Remove(id);
        }

        // Releases the locked external psychotype overrides placed by earlier bridge versions (sourceId =
        // ModId). Runs once, and only when the public API is enabled (so we do not mark it done without
        // actually being able to clear anything). ResetPsychotypeOverride is a safe no-op for a pawn we
        // never touched, so sweeping a broad pawn set is harmless.
        private void MigrateOldOverridesOnce()
        {
            if (overridesSwept || !PawnDiaryApi.IsExternalApiEnabled)
            {
                return;
            }

            foreach (Pawn pawn in MigrationSweepPawns())
            {
                PawnDiaryApi.ResetPsychotypeOverride(pawn, BridgeIds.ModId);
            }

            overridesSwept = true;
        }

        // Every pawn an old bridge version may have given a locked override: spawned free colonists,
        // caravans and travelling transport pods, plus world pawns (covers downed/caravan/departed).
        // Deduplicated by load id.
        private static IEnumerable<Pawn> MigrationSweepPawns()
        {
            HashSet<string> seen = new HashSet<string>();
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn != null && seen.Add(pawn.GetUniqueLoadID()))
                {
                    yield return pawn;
                }
            }

            if (Find.WorldPawns != null)
            {
                foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive)
                {
                    if (pawn != null && seen.Add(pawn.GetUniqueLoadID()))
                    {
                        yield return pawn;
                    }
                }
            }
        }

        private static string KeyFor(Personalities123Mode mode, string root)
        {
            return mode + ":" + root;
        }

        // One tracked Tier-3 transform: the poll handle plus the (mode, root) it is seeding, so the result
        // is recorded against the right key even if the pawn's root changed while it was in flight.
        private sealed class InFlightJob
        {
            public int handle;
            public string key;
            public string root;
        }
    }
}

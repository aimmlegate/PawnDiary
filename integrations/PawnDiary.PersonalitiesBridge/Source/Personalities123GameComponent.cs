// The bridge's per-game owner. RimWorld auto-instantiates every GameComponent subclass with a (Game)
// constructor when a save/game loads, and ticks it. This component owns ALL of the seeding logic:
//
//   • A throttled pass (~every 250 ticks) that, per spawned free colonist, applies the active tier
//     (Off / map-to-internal-psychotype / built-in override / LLM transform) — but only when the pawn's
//     Enneagram ROOT or effective bridge/lane configuration has changed since we last handled them, so
//     a player's own edits to the seeded psychotype survive between personality/configuration changes.
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
using System;
using System.Collections.Generic;
using System.Text;
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

        // Requested output budget for one transform (a couple of sentences). Sized with headroom for
        // reasoning models whose thinking tokens count against the budget before the visible reply.
        private const int TransformMaxTokens = 300;
        private const string PreservedCustomMarker = "__preserved_custom__";

        // Last tick the pass ran. Compared by elapsed time (now - last), never TicksGame % N, so a dev
        // time-skip or save/load cannot desync the cadence.
        private int lastPassTick;

        // Effective bridge + Pawn Diary lane configuration last applied to this SAVE. Persisting this
        // catches menu edits after a restart and changes made in Pawn Diary's own API settings.
        private string lastAppliedConfigurationSignature = string.Empty;

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

        /// <summary>Saves per-pawn change detection, the configuration fingerprint, and migration state.</summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref handledKey, "handledKey", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref overridesSwept, "overridesSwept", false);
            Scribe_Values.Look(ref lastAppliedConfigurationSignature,
                "lastAppliedConfigurationSignature", string.Empty);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && handledKey == null)
            {
                handledKey = new Dictionary<string, string>();
            }
        }

        /// <summary>Runs after the game is fully loaded. Schedules an immediate first pass; the one-time
        /// old-override migration runs from the first tick (FinalizeInit is off the main thread in
        /// RimWorld 1.6, where the override-reset API would be rejected).</summary>
        public override void FinalizeInit()
        {
            // now - 0 >= interval for any loaded game's TicksGame, so the first tick fires a pass at once.
            lastPassTick = 0;
        }

        /// <summary>Throttled periodic seeding. Does nothing when 1-2-3 Personalities is absent, the mode
        /// is Off, or we are between intervals.</summary>
        public override void GameComponentTick()
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - lastPassTick < PassIntervalTicks)
            {
                return;
            }

            // Keep setup snapshots and signature building off the per-tick hot path. The same coarse
            // cadence already governs seeding, so checking configuration once per pass loses nothing.
            lastPassTick = now;
            PawnDiaryPersonalities123Settings settings = PawnDiaryPersonalities123Mod.Settings;
            DiaryApiSetupSnapshot setup = settings != null && settings.mode == Personalities123Mode.LlmTransform
                ? PawnDiaryApi.GetApiSetup()
                : null;
            string configurationSignature = ConfigurationSignature(settings, setup);

            // An empty signature is an old save from before this field existed. Adopt its current
            // configuration without clearing handled keys, which preserves player edits on upgrade.
            if (string.IsNullOrEmpty(lastAppliedConfigurationSignature))
            {
                lastAppliedConfigurationSignature = configurationSignature;
            }
            else if (!string.Equals(lastAppliedConfigurationSignature, configurationSignature,
                StringComparison.Ordinal))
            {
                lastAppliedConfigurationSignature = configurationSignature;
                handledKey.Clear();
                inFlight.Clear();
            }

            // The old locked-override migration must run even when 1-2-3 Personalities is currently
            // inactive; otherwise disabling that mod would strand its old override forever.
            MigrateOldOverridesOnce();

            if (!PawnDiaryPersonalities123Mod.SimplePersonalitiesActive)
            {
                return;
            }
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

            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonistsSpawned)
            {
                SyncPawn(pawn, settings, HasUsableLane(setup));
            }
        }

        /// <summary>
        /// True while a Tier-3 LLM transform is in flight for this pawn. Drives the voice editor's
        /// "generating…" status through the registered psychotype generator.
        /// </summary>
        public bool IsTransformInFlight(Pawn pawn)
        {
            return pawn != null && inFlight.ContainsKey(pawn.GetUniqueLoadID());
        }

        /// <summary>
        /// Forces a fresh Tier-3 transform for one pawn — the voice editor's Regenerate button. Only acts
        /// in the LLM mode with a usable personality; clears the pawn's change-detection and starts the
        /// transform right away so the loading status appears at once (main thread; the editor invokes it).
        /// </summary>
        public void RerollTransform(Pawn pawn)
        {
            if (pawn == null || !PawnDiaryPersonalities123Mod.SimplePersonalitiesActive)
            {
                return;
            }

            PawnDiaryPersonalities123Settings settings = PawnDiaryPersonalities123Mod.Settings;
            if (settings == null || settings.mode != Personalities123Mode.LlmTransform)
            {
                return;
            }

            string root = EnneagramSync.RootDefNameFor(pawn);
            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            string id = pawn.GetUniqueLoadID();
            handledKey.Remove(id);
            inFlight.Remove(id);
            DiaryApiSetupSnapshot setup = PawnDiaryApi.GetApiSetup();
            StartOrFallbackTransform(pawn, id, KeyFor(settings.mode, root), root, settings,
                HasUsableLane(setup));
        }

        // Applies the active tier to one pawn, or advances an in-flight transform. Change-detected so an
        // already-handled (mode, root) is left alone — that is what keeps player edits.
        private void SyncPawn(Pawn pawn, PawnDiaryPersonalities123Settings settings, bool hasUsableLane)
        {
            if (pawn == null)
            {
                return;
            }

            string id = pawn.GetUniqueLoadID();

            // Drain accepted work before current eligibility/root checks. A pawn can be disabled or
            // change personality mid-flight; polling still frees the bounded core handle, while a
            // rejected write remains cached on the job until the pawn becomes writable again.
            if (inFlight.TryGetValue(id, out InFlightJob job))
            {
                ResolvePendingTransform(pawn, id, job);
                return;
            }

            if (!PawnDiaryApi.IsDiaryEligible(pawn))
            {
                return;
            }

            string root = EnneagramSync.RootDefNameFor(pawn);
            if (string.IsNullOrEmpty(root))
            {
                // No usable personality (cleared/invalid/unmapped animal root): abandon any pending
                // transform and leave the pawn's player-owned psychotype as-is.
                inFlight.Remove(id);
                return;
            }

            string key = KeyFor(settings.mode, root);
            if (handledKey.TryGetValue(id, out string preserved)
                && string.Equals(preserved, PreservedCustomMarker, StringComparison.Ordinal))
            {
                // The migration found player-authored custom text beneath our old locked override.
                // Adopt it for the current mode/root instead of immediately overwriting it.
                handledKey[id] = key;
                return;
            }

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
                    ApplyOverrideRule(pawn, id, key, root, true);
                    break;
                case Personalities123Mode.LlmTransform:
                    StartOrFallbackTransform(pawn, id, key, root, settings, hasUsableLane);
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
        private bool ApplyOverrideRule(Pawn pawn, string id, string key, string root, bool markHandled)
        {
            string rule = EnneagramSync.ResolveOutlookRule(root);
            if (string.IsNullOrWhiteSpace(rule))
            {
                return false;
            }

            if (PawnDiaryApi.SetPsychotypeCustomRule(pawn, rule))
            {
                if (markHandled)
                {
                    handledKey[id] = key;
                }

                return true;
            }

            return false;
        }

        // Tier 3: fire the LLM transform (tracked as in-flight), or fall back to Tier 2 immediately when
        // there is no input or no usable lane / the API rejected the call.
        private void StartOrFallbackTransform(Pawn pawn, string id, string key, string root,
            PawnDiaryPersonalities123Settings settings, bool hasUsableLane)
        {
            string input = EnneagramSync.BuildTransformInputFor(pawn);
            if (string.IsNullOrWhiteSpace(input))
            {
                ApplyOverrideRule(pawn, id, key, root, true);
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

            // A durable lack of API/lane is handled until the configuration signature changes. A
            // temporary rejection (budget/admission pressure) keeps the key open so the next pass retries.
            ApplyOverrideRule(pawn, id, key, root, !hasUsableLane);
        }

        // Polls one in-flight transform. Success writes the transformed text into the editable custom
        // rule; anything else (failed / unknown / blank text) falls back to the built-in outlook rule.
        private void ResolvePendingTransform(Pawn pawn, string id, InFlightJob job)
        {
            if (!string.IsNullOrWhiteSpace(job.completedText))
            {
                if (PawnDiaryApi.SetPsychotypeCustomRule(pawn, job.completedText))
                {
                    handledKey[id] = job.key;
                    inFlight.Remove(id);
                }

                return;
            }

            if (job.fallbackPending)
            {
                if (ApplyOverrideRule(pawn, id, job.key, job.root, true))
                {
                    inFlight.Remove(id);
                }

                return;
            }

            LlmCompletionResult result = PawnDiaryApi.GetLlmCompletionResult(job.handle);
            if (result.status == LlmCompletionStatus.Pending)
            {
                return;
            }

            if (result.status == LlmCompletionStatus.Succeeded && !string.IsNullOrWhiteSpace(result.text))
            {
                // Keep the paid result in memory until the main-thread write succeeds. Never issue a
                // second HTTP request merely because a transient game-state guard rejected the write.
                job.completedText = result.text;
            }
            else
            {
                job.fallbackPending = true;
            }

            ResolvePendingTransform(pawn, id, job);
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
                DiaryPsychotypeSnapshot before = PawnDiaryApi.GetPsychotype(pawn);
                PawnDiaryApi.ResetPsychotypeOverride(pawn, BridgeIds.ModId);
                if (!string.IsNullOrWhiteSpace(before?.savedCustomRule))
                {
                    handledKey[pawn.GetUniqueLoadID()] = PreservedCustomMarker;
                }
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

        private static bool HasUsableLane(DiaryApiSetupSnapshot setup)
        {
            return PawnDiaryApi.IsExternalApiEnabled && setup != null && setup.activeLaneCount > 0;
        }

        // Stable, secret-free identity of every value that changes seeding output. Including Pawn
        // Diary's lane setup makes core-menu changes visible even when the bridge menu was never opened.
        private static string ConfigurationSignature(PawnDiaryPersonalities123Settings settings,
            DiaryApiSetupSnapshot setup)
        {
            StringBuilder signature = new StringBuilder(PawnDiaryPersonalities123Mod.SettingsSignature());
            signature.Append('|').Append(PawnDiaryApi.IsExternalApiEnabled);
            if (settings == null || settings.mode != Personalities123Mode.LlmTransform || setup == null)
            {
                return StableConfigurationHash(signature.ToString());
            }

            signature.Append('|').Append(setup.temperature)
                .Append('|').Append(setup.timeoutSeconds);
            DiaryApiLaneSnapshot lane = EffectiveLane(setup, settings.transformLaneIndex);
            if (lane != null)
            {
                signature.Append('|').Append(lane.index)
                    .Append(':').Append(lane.active)
                    .Append(':').Append(lane.url)
                    .Append(':').Append(lane.model)
                    .Append(':').Append(lane.authMode)
                    .Append(':').Append(lane.apiMode)
                    .Append(':').Append(lane.reasoningEffort)
                    .Append(':').Append(lane.reasoningTag)
                    .Append(':').Append(lane.hasApiKey)
                    // apiKey is normally withheld; when the player opted into key sharing, hashing it
                    // lets a key replacement trigger a retry without ever persisting the secret itself.
                    .Append(':').Append(lane.apiKey);
            }

            return StableConfigurationHash(signature.ToString());
        }

        private static DiaryApiLaneSnapshot EffectiveLane(DiaryApiSetupSnapshot setup, int requestedIndex)
        {
            if (setup?.lanes == null)
            {
                return null;
            }

            DiaryApiLaneSnapshot firstActive = null;
            for (int i = 0; i < setup.lanes.Count; i++)
            {
                DiaryApiLaneSnapshot lane = setup.lanes[i];
                if (lane == null || !lane.active)
                {
                    continue;
                }

                if (firstActive == null)
                {
                    firstActive = lane;
                }

                if (lane.index == requestedIndex)
                {
                    return lane;
                }
            }

            return firstActive;
        }

        // Persist only a deterministic fingerprint, never raw prompts or endpoint URLs (which can
        // contain query credentials). FNV-1a is stable across processes unlike string.GetHashCode().
        private static string StableConfigurationHash(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            ulong hash = 14695981039346656037UL;
            unchecked
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= 1099511628211UL;
                }
            }

            return hash.ToString("x16");
        }

        // One tracked Tier-3 transform: the poll handle plus the (mode, root) it is seeding, so the result
        // is recorded against the right key even if the pawn's root changed while it was in flight.
        private sealed class InFlightJob
        {
            public int handle;
            public string key;
            public string root;
            public string completedText;
            public bool fallbackPending;
        }
    }
}

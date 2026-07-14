// Per-game synchronization and persistence for the Powerful AI Integration bridge. Live PAI reads
// and Pawn Diary writes stay on the main game thread; pure text formatting is delegated to Source/Pure.
using System;
using System.Collections.Generic;
using PawnDiary.Integration;
using PawnDiaryPowerfulAiBridge.Pure;
using RimWorld;
using Verse;

namespace PawnDiaryPowerfulAiBridge
{
    /// <summary>Maintains reversible source-owned psychotype overrides for eligible colonists.</summary>
    public class PowerfulAiBridgeGameComponent : GameComponent
    {
        private int lastPassTick;
        private bool firstPassPending = true;
        private Dictionary<string, string> sourceKeysByPawn = new Dictionary<string, string>();
        private Dictionary<string, string> targetRulesByPawn = new Dictionary<string, string>();
        private Dictionary<string, InFlightJob> inFlight = new Dictionary<string, InFlightJob>();
        private List<string> sourceKeyKeys;
        private List<string> sourceKeyValues;
        private List<string> targetRuleKeys;
        private List<string> targetRuleValues;

        public PowerfulAiBridgeGameComponent(Game game)
        {
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref sourceKeysByPawn, "powerfulAiPersonaSourceKeys", LookMode.Value,
                LookMode.Value, ref sourceKeyKeys, ref sourceKeyValues);
            Scribe_Collections.Look(ref targetRulesByPawn, "powerfulAiPersonaTargetRules", LookMode.Value,
                LookMode.Value, ref targetRuleKeys, ref targetRuleValues);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                sourceKeysByPawn = sourceKeysByPawn ?? new Dictionary<string, string>();
                targetRulesByPawn = targetRulesByPawn ?? new Dictionary<string, string>();
            }
        }

        public override void FinalizeInit()
        {
            // FinalizeInit can be off-thread. Clear plain transient state only; API cleanup waits for tick.
            inFlight.Clear();
            PowerfulAiReflection.Reset();
            firstPassPending = true;
            lastPassTick = 0;
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int interval = Math.Max(1, PowerfulAiBridgeTuningDef.Current.passIntervalTicks);
            long elapsed = (long)now - lastPassTick;
            if (!firstPassPending && elapsed >= 0 && elapsed < interval)
            {
                return;
            }

            firstPassPending = false;
            lastPassTick = now;
            PawnDiaryPowerfulAiBridgeMod.EnsureGeneratorRegistered();
            RunPass();
        }

        internal bool IsTransformInFlight(Pawn pawn)
        {
            return pawn != null && inFlight.ContainsKey(pawn.GetUniqueLoadID());
        }

        internal void RerollTransform(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            CancelTransform(pawnId);
            sourceKeysByPawn.Remove(pawnId);
            targetRulesByPawn.Remove(pawnId);
            firstPassPending = true;
        }

        private void RunPass()
        {
            PawnDiaryPowerfulAiBridgeSettings settings = PawnDiaryPowerfulAiBridgeMod.Settings;
            if (!PawnDiaryApi.IsReady)
            {
                return;
            }

            if (!PawnDiaryPowerfulAiBridgeMod.PowerfulAiActive || settings == null
                || settings.mode == PowerfulAiPersonaMode.Disabled || !PawnDiaryApi.IsExternalApiEnabled)
            {
                ReleaseAllOverrides();
                return;
            }

            DiaryApiSetupSnapshot setup = settings.mode == PowerfulAiPersonaMode.LlmAssisted
                ? PawnDiaryApi.GetApiSetup()
                : null;
            string laneSignature = LaneSignature(setup, settings.transformLaneIndex);
            HashSet<string> seen = new HashSet<string>();
            foreach (Pawn pawn in TouchedPawns())
            {
                if (pawn == null || !seen.Add(pawn.GetUniqueLoadID()))
                {
                    continue;
                }

                SyncPawn(pawn, settings, laneSignature);
            }

            DropStaleTracking(seen);
        }

        private void SyncPawn(Pawn pawn, PawnDiaryPowerfulAiBridgeSettings settings, string laneSignature)
        {
            string pawnId = pawn.GetUniqueLoadID();
            PowerfulAiPersonaSnapshot snapshot;
            if (!PowerfulAiReflection.TryReadPersona(pawn, out snapshot))
            {
                ReleaseFor(pawn, pawnId);
                return;
            }

            PowerfulAiBridgeTuningDef tuning = PowerfulAiBridgeTuningDef.Current;
            string directRule = PersonaTransferText.BuildDirectRule(snapshot,
                Math.Max(1, tuning.personaMaxCharacters));
            if (string.IsNullOrWhiteSpace(directRule))
            {
                ReleaseFor(pawn, pawnId);
                return;
            }

            if (!PawnDiaryApi.IsDiaryEligible(pawn))
            {
                ReleaseFor(pawn, pawnId);
                return;
            }

            string promptFingerprint = settings.mode == PowerfulAiPersonaMode.LlmAssisted
                ? PersonaTransferText.StableFingerprint(tuning.systemPrompt)
                : string.Empty;
            string sourceKey = ((int)settings.mode) + "|" + PersonaTransferText.StableFingerprint(directRule)
                + "|" + laneSignature + "|" + promptFingerprint;

            InFlightJob job;
            if (inFlight.TryGetValue(pawnId, out job))
            {
                if (!string.Equals(job.sourceKey, sourceKey, StringComparison.Ordinal))
                {
                    CancelTransform(pawnId);
                }
                else
                {
                    ResolveTransform(pawn, pawnId, job);
                    return;
                }
            }

            string appliedKey;
            if (sourceKeysByPawn.TryGetValue(pawnId, out appliedKey)
                && string.Equals(appliedKey, sourceKey, StringComparison.Ordinal))
            {
                return;
            }

            if (settings.mode == PowerfulAiPersonaMode.Direct)
            {
                ApplyAndRemember(pawn, pawnId, sourceKey, directRule);
                return;
            }

            StartTransformOrFallback(pawn, pawnId, sourceKey, directRule, settings, tuning, laneSignature);
        }

        private void StartTransformOrFallback(Pawn pawn, string pawnId, string sourceKey, string directRule,
            PawnDiaryPowerfulAiBridgeSettings settings, PowerfulAiBridgeTuningDef tuning, string laneSignature)
        {
            if (string.IsNullOrWhiteSpace(laneSignature) || string.IsNullOrWhiteSpace(tuning.systemPrompt))
            {
                ApplyAndRemember(pawn, pawnId, sourceKey, directRule);
                return;
            }

            int handle = PawnDiaryApi.RequestLlmCompletion(new ExternalLlmCompletionRequest
            {
                sourceId = BridgeIds.ModId,
                laneIndex = settings.transformLaneIndex,
                systemPrompt = tuning.systemPrompt,
                userText = directRule,
                maxTokens = Math.Max(1, tuning.transformMaxTokens)
            });
            if (handle <= 0)
            {
                // A usable lane exists, so rejection can be temporary budget/admission pressure. Keep
                // direct text active as the safe fallback, but leave the source key open for retry.
                if (PawnDiaryApi.SetPsychotypeOverride(pawn, BridgeIds.ModId, directRule))
                {
                    targetRulesByPawn[pawnId] = directRule;
                    sourceKeysByPawn.Remove(pawnId);
                }
                return;
            }

            inFlight[pawnId] = new InFlightJob
            {
                handle = handle,
                sourceKey = sourceKey,
                fallbackRule = directRule
            };
        }

        private void ResolveTransform(Pawn pawn, string pawnId, InFlightJob job)
        {
            LlmCompletionResult result = PawnDiaryApi.GetLlmCompletionResult(job.handle);
            if (result.status == LlmCompletionStatus.Pending)
            {
                return;
            }

            string rule = result.status == LlmCompletionStatus.Succeeded
                && !string.IsNullOrWhiteSpace(result.text)
                    ? result.text
                    : job.fallbackRule;
            inFlight.Remove(pawnId);
            ApplyAndRemember(pawn, pawnId, job.sourceKey, rule);
        }

        private void ApplyAndRemember(Pawn pawn, string pawnId, string sourceKey, string rule)
        {
            if (!PawnDiaryApi.SetPsychotypeOverride(pawn, BridgeIds.ModId, rule))
            {
                return;
            }

            sourceKeysByPawn[pawnId] = sourceKey;
            targetRulesByPawn[pawnId] = rule;
        }

        private void ReleaseFor(Pawn pawn, string pawnId)
        {
            CancelTransform(pawnId);
            // targetRulesByPawn is the ownership ledger. A direct fallback can intentionally leave
            // sourceKeysByPawn empty so a temporarily rejected LLM request is retried next pass.
            if (!targetRulesByPawn.ContainsKey(pawnId))
            {
                return;
            }

            PawnDiaryApi.ResetPsychotypeOverride(pawn, BridgeIds.ModId);
            sourceKeysByPawn.Remove(pawnId);
            targetRulesByPawn.Remove(pawnId);
        }

        private void ReleaseAllOverrides()
        {
            CancelAllTransforms();
            if (targetRulesByPawn.Count == 0)
            {
                return;
            }

            foreach (Pawn pawn in TouchedPawns())
            {
                if (pawn == null)
                {
                    continue;
                }

                string pawnId = pawn.GetUniqueLoadID();
                if (targetRulesByPawn.ContainsKey(pawnId))
                {
                    PawnDiaryApi.ResetPsychotypeOverride(pawn, BridgeIds.ModId);
                }
            }

            sourceKeysByPawn.Clear();
            targetRulesByPawn.Clear();
        }

        private void DropStaleTracking(HashSet<string> seen)
        {
            HashSet<string> stale = new HashSet<string>();
            foreach (string pawnId in sourceKeysByPawn.Keys)
            {
                if (!seen.Contains(pawnId))
                {
                    stale.Add(pawnId);
                }
            }
            foreach (string pawnId in inFlight.Keys)
            {
                if (!seen.Contains(pawnId))
                {
                    stale.Add(pawnId);
                }
            }
            foreach (string pawnId in targetRulesByPawn.Keys)
            {
                if (!seen.Contains(pawnId))
                {
                    stale.Add(pawnId);
                }
            }

            foreach (string pawnId in stale)
            {
                sourceKeysByPawn.Remove(pawnId);
                targetRulesByPawn.Remove(pawnId);
                CancelTransform(pawnId);
            }
        }

        private void CancelTransform(string pawnId)
        {
            InFlightJob job;
            if (!inFlight.TryGetValue(pawnId, out job))
            {
                return;
            }

            PawnDiaryApi.CancelLlmCompletion(job.handle);
            inFlight.Remove(pawnId);
        }

        private void CancelAllTransforms()
        {
            List<InFlightJob> jobs = new List<InFlightJob>(inFlight.Values);
            inFlight.Clear();
            for (int i = 0; i < jobs.Count; i++)
            {
                PawnDiaryApi.CancelLlmCompletion(jobs[i].handle);
            }
        }

        private static string LaneSignature(DiaryApiSetupSnapshot setup, int requestedIndex)
        {
            if (setup?.lanes == null)
            {
                return string.Empty;
            }

            DiaryApiLaneSnapshot selected = null;
            foreach (DiaryApiLaneSnapshot lane in setup.lanes)
            {
                if (lane.active && lane.index == requestedIndex)
                {
                    selected = lane;
                    break;
                }
            }

            if (selected == null)
            {
                foreach (DiaryApiLaneSnapshot lane in setup.lanes)
                {
                    if (lane.active)
                    {
                        selected = lane;
                        break;
                    }
                }
            }

            return selected == null
                ? string.Empty
                : selected.index + "|" + (selected.url ?? string.Empty) + "|" + (selected.model ?? string.Empty);
        }

        private IEnumerable<Pawn> TouchedPawns()
        {
            HashSet<string> seen = new HashSet<string>();
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn != null && seen.Add(pawn.GetUniqueLoadID()))
                {
                    yield return pawn;
                }
            }

            // A previously eligible colonist can become a prisoner or leave the faction while still
            // spawned. Include only such tracked map pawns so their bridge-owned override is released
            // without scanning every animal and visitor through PAI reflection on every pass.
            if (Find.Maps != null)
            {
                foreach (Map map in Find.Maps)
                {
                    if (map?.mapPawns?.AllPawns == null)
                    {
                        continue;
                    }

                    foreach (Pawn pawn in map.mapPawns.AllPawns)
                    {
                        if (pawn == null)
                        {
                            continue;
                        }

                        string pawnId = pawn.GetUniqueLoadID();
                        if ((sourceKeysByPawn.ContainsKey(pawnId) || targetRulesByPawn.ContainsKey(pawnId)
                                || inFlight.ContainsKey(pawnId)) && seen.Add(pawnId))
                        {
                            yield return pawn;
                        }
                    }
                }
            }

            if (Find.WorldPawns == null)
            {
                yield break;
            }

            foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive)
            {
                if (pawn != null && seen.Add(pawn.GetUniqueLoadID()))
                {
                    yield return pawn;
                }
            }
        }

        private sealed class InFlightJob
        {
            public int handle;
            public string sourceKey = string.Empty;
            public string fallbackRule = string.Empty;
        }
    }
}

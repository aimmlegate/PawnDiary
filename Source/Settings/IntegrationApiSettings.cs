// Impure settings adapter for the public API's global setup surfaces: LLM lanes plus automatic-event
// enable/frequency settings. It copies live PawnDiarySettings/Def state into detached public DTOs and
// applies validated external writes with the same persistence paths as the settings window. Pure
// normalization stays in policy helpers; DefDatabase, settings mutation, and Scribe writes remain at
// this edge.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable", settings).
using System;
using System.Collections.Generic;
using PawnDiary.Integration;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Bridges the public integration API to saved LLM-lane and automatic-event settings, producing
    /// detached snapshots and persisting validated writes through their normal runtime adapters.
    /// </summary>
    internal static class IntegrationApiSettings
    {
        /// <summary>
        /// Builds a prompt-free snapshot of the current LLM API setup: global request knobs plus one
        /// lane snapshot per configured endpoint row. Never null. The raw per-lane apiKey is included ONLY
        /// when the player has opted into key sharing (PawnDiarySettings.enableExternalKeySharing); every
        /// snapshot still reports hasApiKey so an adapter can see a key is set without receiving it. See
        /// DiaryApiLaneSnapshot's security note.
        /// </summary>
        public static DiaryApiSetupSnapshot BuildSetupSnapshot(PawnDiarySettings settings)
        {
            DiaryApiSetupSnapshot snapshot = new DiaryApiSetupSnapshot();
            if (settings == null)
            {
                return snapshot;
            }

            // Sharing a plaintext provider key is gated on its own opt-in, separate from the master
            // integration toggle: any loaded mod can call GetApiSetup, so the raw key is withheld unless
            // the player deliberately enabled sharing.
            bool shareKeys = settings.enableExternalKeySharing;
            snapshot.keySharingEnabled = shareKeys;

            // Normalizes routing mode + backfills a default row so what we report matches what would
            // actually be used (the same call the settings window makes before drawing).
            settings.EnsureEndpointsList();

            snapshot.routingMode = ApiLaneImport.RoutingModeToken(settings.apiRoutingMode);
            snapshot.temperature = settings.temperature;
            snapshot.timeoutSeconds = settings.timeoutSeconds;
            snapshot.retryAttempts =
                LlmTransportPolicy.NormalizeRetryAttempts(settings.retryAttempts);
            snapshot.retryBaseDelaySeconds =
                LlmTransportPolicy.NormalizeRetryDelaySeconds(settings.retryBaseDelaySeconds);
            snapshot.maxTokens = settings.maxTokens;
            snapshot.maxConcurrentRequests = settings.maxConcurrentRequests;

            List<ApiEndpointConfig> lanes = settings.apiEndpoints;
            int count = lanes == null ? 0 : lanes.Count;
            snapshot.laneCount = count;

            int active = 0;
            for (int i = 0; i < count; i++)
            {
                ApiEndpointConfig e = lanes[i];
                if (e == null)
                {
                    continue;
                }

                bool isActive = IsActive(e);
                if (isActive)
                {
                    active++;
                }

                snapshot.lanes.Add(new DiaryApiLaneSnapshot
                {
                    index = i,
                    url = e.url ?? string.Empty,
                    model = e.model ?? string.Empty,
                    enabled = e.enabled,
                    active = isActive,
                    authMode = ApiLaneImport.AuthModeToken(e.authMode),
                    customAuthHeaderName = ApiEndpointPolicy.EffectiveAuthHeaderName(e.authMode, e.customAuthHeaderName),
                    apiMode = ApiLaneImport.ApiModeToken(e.apiMode),
                    reasoningEffort = e.reasoningEffort ?? string.Empty,
                    reasoningTag = e.reasoningTag ?? string.Empty,
                    contextDetailOverride = ApiLaneImport.ContextDetailOverrideToken(e.contextDetailOverride),
                    hasApiKey = !string.IsNullOrEmpty(e.apiKey),
                    apiKey = shareKeys ? (e.apiKey ?? string.Empty) : string.Empty,
                    addedBySourceId = e.addedBySourceId ?? string.Empty
                });
            }

            snapshot.activeLaneCount = active;
            return snapshot;
        }

        /// <summary>
        /// Adds a new API lane from an external request, persisting it and pushing the active-lane set
        /// live to the shared LlmClient so it can serve generation immediately. Never throws; the
        /// outcome (added / duplicate / missing-field) is reported on the returned result.
        /// </summary>
        public static AddApiLaneResult AddLane(PawnDiarySettings settings, ExternalApiLaneRequest request)
        {
            AddApiLaneResult result = new AddApiLaneResult { index = -1 };

            if (settings == null)
            {
                result.reason = "ineligible";
                return result;
            }

            if (request == null)
            {
                result.reason = "invalidRequest";
                return result;
            }

            string url = (request.url ?? string.Empty).Trim();
            string model = (request.model ?? string.Empty).Trim();
            string validation = ApiLaneImport.ValidateAddRequest(url, model);
            if (validation != ApiLaneImport.ReasonOk)
            {
                result.reason = validation; // missingUrl / missingModel
                return result;
            }

            settings.EnsureEndpointsList();

            // Normalize the incoming fields the same way the settings loader normalizes saved rows, so
            // the lane behaves identically to one the player added by hand and dedup compares like-for-like.
            string normalizedUrl = EndpointUtility.NormalizeBaseEndpoint(url);
            ApiAuthMode authMode = ApiEndpointPolicy.NormalizeAuthMode(ApiLaneImport.ParseAuthMode(request.authMode));
            ApiCompatibilityMode apiMode = ApiEndpointPolicy.NormalizeApiMode(ApiLaneImport.ParseApiMode(request.apiMode));
            string apiKey = request.apiKey ?? string.Empty;
            string headerName = authMode == ApiAuthMode.CustomHeader
                ? ApiEndpointPolicy.NormalizeCustomHeaderName(request.customAuthHeaderName)
                : ApiEndpointPolicy.DefaultCustomHeaderName;
            string reasoningEffort = ApiEndpointPolicy.NormalizeReasoningEffort(request.reasoningEffort);
            string reasoningTag = ApiEndpointPolicy.NormalizeReasoningTag(request.reasoningTag);
            PromptContextDetailOverride contextOverride =
                ApiLaneImport.ParseContextDetailOverride(request.contextDetailOverride);

            // Duplicate = same lane identity: normalized endpoint + trimmed model + apiMode + effective
            // auth + effective key (ForGate). Model is part of the identity on purpose — sharing one
            // endpoint across several models is a supported setup, so only an identical model counts as
            // a duplicate row. Using the *effective* key means a no-auth lane ignores a stale key, and
            // normalization makes a trailing slash or /chat/completions suffix not fool the check.
            if (request.avoidDuplicate)
            {
                ApiLaneIdentity newId = ApiLaneIdentity.ForGate(normalizedUrl, model, apiMode, authMode, headerName, apiKey);
                for (int i = 0; i < settings.apiEndpoints.Count; i++)
                {
                    ApiEndpointConfig existing = settings.apiEndpoints[i];
                    if (existing == null)
                    {
                        continue;
                    }

                    ApiLaneIdentity existingId = ApiLaneIdentity.ForGate(
                        existing.url,
                        existing.model,
                        existing.apiMode,
                        existing.authMode,
                        existing.customAuthHeaderName,
                        existing.apiKey);

                    if (newId == existingId)
                    {
                        result.alreadyExisted = true;
                        result.index = i;
                        result.active = IsActive(existing);
                        result.reason = "duplicate";
                        return result;
                    }
                }
            }

            ApiEndpointConfig lane = new ApiEndpointConfig(normalizedUrl, apiKey, model)
            {
                enabled = request.enabled,
                authMode = authMode,
                customAuthHeaderName = headerName,
                apiMode = apiMode,
                reasoningEffort = reasoningEffort,
                reasoningTag = reasoningTag,
                contextDetailOverride = contextOverride,
                // Persist the requesting mod's id so an API-injected lane stays attributable (blank for a
                // hand-added row). This does not gate anything; it keeps injected config from being silently
                // indistinguishable from the player's own.
                addedBySourceId = (request.sourceId ?? string.Empty).Trim()
            };
            settings.apiEndpoints.Add(lane);
            int index = settings.apiEndpoints.Count - 1;

            // Publish through the same verified transaction as the settings window. A failed write
            // removes this newly appended row so the API reports an honest read-only failure.
            settings.NormalizeEndpointUrls();
            if (!PawnDiaryMod.PersistSettingsImmediately(settings))
            {
                settings.apiEndpoints.RemoveAt(index);
                result.reason = "persistence_failed";
                return result;
            }

            result.added = true;
            result.index = index;
            result.active = IsActive(lane);
            result.reason = "added";
            return result;
        }

        /// <summary>
        /// Builds a snapshot of the automatic-capture event filters shown on the settings "Events" tab
        /// (all non-External, non-package-gated interaction groups), in the same order, each with its
        /// current saved on/off state. Never null.
        /// </summary>
        public static List<DiaryEventFilterSnapshot> BuildEventFilterSnapshots(PawnDiarySettings settings)
        {
            DiaryFrequencyPresetSnapshot frequencyPreset = settings?.FrequencyPresetSnapshot();
            return BuildEventFilterSnapshots(settings, frequencyPreset);
        }

        private static List<DiaryEventFilterSnapshot> BuildEventFilterSnapshots(
            PawnDiarySettings settings,
            DiaryFrequencyPresetSnapshot frequencyPreset)
        {
            List<DiaryEventFilterSnapshot> result = new List<DiaryEventFilterSnapshot>();
            if (settings == null)
            {
                return result;
            }

            if (!settings.TryFinalizeGroupEnabledSettingsAfterDefsLoaded())
            {
                return result;
            }

            // Reuse the exact list (and sort) the Events tab draws so the API and the UI never drift.
            List<DiaryInteractionGroupDef> groups = PawnDiaryMod.EventFilterGroupsForSettings();
            if (groups == null)
            {
                return result;
            }

            for (int i = 0; i < groups.Count; i++)
            {
                DiaryInteractionGroupDef group = groups[i];
                if (group == null)
                {
                    continue;
                }

                result.Add(new DiaryEventFilterSnapshot
                {
                    key = group.defName ?? string.Empty,
                    label = PawnDiaryMod.EventFilterLabel(group),
                    domain = group.domain.ToString(),
                    enabled = settings.IsGroupEnabled(group.defName),
                    defaultEnabled = group.defaultEnabled,
                    hasOverride = settings.HasGroupEnabledOverride(group.defName),
                    frequencyTier = DiaryFrequencyTiers.Normalize(group.frequencyTier),
                    presetFrequencyMultiplier = PawnDiarySettings.PresetGroupFrequencyMultiplier(
                        group,
                        frequencyPreset),
                    effectiveFrequencyMultiplier = settings.EffectiveGroupFrequencyMultiplier(
                        group,
                        frequencyPreset),
                    hasFrequencyOverride = settings.HasGroupFrequencyOverride(group.defName)
                });
            }

            return result;
        }

        /// <summary>
        /// Builds one detached snapshot of the selected global frequency preset and all settings-visible
        /// event rows. The old list-only getter delegates to the same row builder, so API-v8 fields and
        /// ordering remain identical while API-v9 callers can also discover the selected preset.
        /// </summary>
        public static DiaryEventFrequencySettingsSnapshot BuildEventFrequencySettingsSnapshot(
            PawnDiarySettings settings)
        {
            if (settings == null || !settings.TryFinalizeFrequencySettingsAfterDefsLoaded())
            {
                return null;
            }

            DiaryEventFrequencySettingsSnapshot result = new DiaryEventFrequencySettingsSnapshot();
            DiaryFrequencyPresetSnapshot preset = settings.FrequencyPresetSnapshot();
            // Keep selection identity separate from effective arithmetic. A temporarily absent
            // third-party preset remains the saved selection, while FrequencyPresetSnapshot safely
            // supplies Standard multipliers until that provider returns.
            string selectedPresetDefName = (settings.frequencyPresetDefName ?? string.Empty).Trim();
            DiaryFrequencyPresetDef presetDef = string.IsNullOrWhiteSpace(selectedPresetDefName)
                ? null
                : DefDatabase<DiaryFrequencyPresetDef>.GetNamedSilentFail(selectedPresetDefName);

            result.selectedPresetDefName = selectedPresetDefName;
            result.selectedPresetLabel = !string.IsNullOrWhiteSpace(presetDef?.label)
                ? presetDef.LabelCap.ToString()
                : selectedPresetDefName;
            result.hasCustomOverrides = settings.HasCustomFrequencyOverrides(preset);
            result.filters = BuildEventFilterSnapshots(settings, preset);
            return result;
        }

        /// <summary>
        /// Returns whether automatic capture is enabled for one event-filter group (by defName). Returns
        /// false for an unknown key or a group that is not part of the settings Events list (External or
        /// package-gated groups), matching what the settings tab manages.
        /// </summary>
        public static bool IsEventFilterEnabled(PawnDiarySettings settings, string key)
        {
            if (settings == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }


            if (!settings.TryFinalizeGroupEnabledSettingsAfterDefsLoaded())
            {
                return false;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ByKey(key);
            if (!PawnDiaryMod.IsSettingsEventFilterGroup(group))
            {
                return false;
            }

            return settings.IsGroupEnabled(group.defName);
        }

        /// <summary>
        /// Enables or disables automatic capture for one event-filter group, using the exact same saved
        /// flag as the settings Events tab (<see cref="PawnDiarySettings.SetGroupEnabled"/>, which drops
        /// the override when it matches the XML default), then persists. Returns false for an unknown key
        /// or a group outside the settings Events list.
        /// </summary>
        public static bool TrySetEventFilter(PawnDiarySettings settings, string key, bool enabled)
        {
            if (settings == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }


            if (!settings.TryFinalizeGroupEnabledSettingsAfterDefsLoaded())
            {
                return false;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ByKey(key);
            if (!PawnDiaryMod.IsSettingsEventFilterGroup(group))
            {
                return false;
            }

            settings.SetGroupEnabled(group.defName, enabled);
            return PawnDiaryMod.PersistSettingsImmediately(settings);
        }

        /// <summary>
        /// Selects one loaded frequency preset and clears sparse frequency overrides. Presets added by
        /// future Pawn Diary builds or third-party Defs remain valid without an API update.
        /// Validation happens before the settings helper because that helper intentionally recovers an
        /// unknown saved token to Standard, while a public write must fail without mutation.
        /// </summary>
        public static bool TrySetEventFrequencyPreset(
            PawnDiarySettings settings,
            string presetDefName)
        {
            if (settings == null)
            {
                return false;
            }

            DiaryFrequencyPresetDef preset = LoadedFrequencyPreset(presetDefName);
            if (preset == null || string.IsNullOrWhiteSpace(preset.defName))
            {
                return false;
            }

            if (!settings.TrySetFrequencyPreset(preset.defName, clearOverrides: true))
            {
                return false;
            }

            return PawnDiaryMod.PersistSettingsImmediately(settings);
        }

        /// <summary>
        /// Stores a finite, bounded frequency multiplier for one settings-visible group. The settings
        /// helper owns sparse inheritance, so passing the selected preset value removes the override.
        /// </summary>
        public static bool TrySetEventFrequencyMultiplier(
            PawnDiarySettings settings,
            string key,
            float multiplier)
        {
            if (settings == null
                || float.IsNaN(multiplier)
                || float.IsInfinity(multiplier)
                || multiplier < 0f
                || multiplier > DiaryFrequencyPolicy.MaximumMultiplier
                || !PawnDiarySettings.FrequencyDefinitionsReady())
            {
                return false;
            }

            // Validate the token before the lazy finalizer can migrate or normalize settings. The
            // readiness check above reads DefDatabase directly, so an early call never initializes
            // InteractionGroups while RimWorld is still binding Defs.
            DiaryInteractionGroupDef group = SettingsFrequencyGroup(key);
            if (group == null || !settings.TryFinalizeFrequencySettingsAfterDefsLoaded())
            {
                return false;
            }

            settings.SetGroupFrequencyOverride(group.defName, multiplier);
            return PawnDiaryMod.PersistSettingsImmediately(settings);
        }

        /// <summary>Clears one known settings-visible group's frequency override and persists.</summary>
        public static bool TryResetEventFrequencyMultiplier(PawnDiarySettings settings, string key)
        {
            if (settings == null || !PawnDiarySettings.FrequencyDefinitionsReady())
            {
                return false;
            }

            // Keep the invalid-key path observationally read-only, matching the setter above.
            DiaryInteractionGroupDef group = SettingsFrequencyGroup(key);
            if (group == null || !settings.TryFinalizeFrequencySettingsAfterDefsLoaded())
            {
                return false;
            }

            settings.ResetGroupFrequencyOverride(group.defName);
            return PawnDiaryMod.PersistSettingsImmediately(settings);
        }

        private static DiaryInteractionGroupDef SettingsFrequencyGroup(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ByKey(key.Trim());
            return PawnDiaryMod.IsSettingsEventFilterGroup(group) ? group : null;
        }

        private static DiaryFrequencyPresetDef LoadedFrequencyPreset(string presetDefName)
        {
            string token = (presetDefName ?? string.Empty).Trim();
            if (token.Length == 0)
            {
                return null;
            }

            DiaryFrequencyPresetDef exact =
                DefDatabase<DiaryFrequencyPresetDef>.GetNamedSilentFail(token);
            if (exact != null)
            {
                return exact;
            }

            List<DiaryFrequencyPresetDef> presets = DefDatabase<DiaryFrequencyPresetDef>.AllDefsListForReading;
            for (int i = 0; i < presets.Count; i++)
            {
                DiaryFrequencyPresetDef preset = presets[i];
                if (preset != null
                    && string.Equals(preset.defName, token, StringComparison.OrdinalIgnoreCase))
                {
                    return preset;
                }
            }

            return null;
        }

        // A lane serves generation only when enabled with both a URL and a model (mirrors ActiveEndpoints).
        private static bool IsActive(ApiEndpointConfig endpoint)
        {
            return endpoint != null
                && endpoint.enabled
                && !string.IsNullOrWhiteSpace(endpoint.url)
                && !string.IsNullOrWhiteSpace(endpoint.model);
        }
    }
}

// Impure settings adapter for the public "read / add LLM API lane" integration surface. This is the
// one place that turns the live PawnDiarySettings API lanes into the plain DiaryApiSetupSnapshot DTO,
// and that applies an ExternalApiLaneRequest by mutating + persisting settings. The pure token<->enum
// mapping and validation live in ApiLaneImport; the impure parts (settings mutation, LlmClient apply,
// Scribe persistence) stay here at the edge, mirroring how PawnDiaryMod.WriteSettings persists lane
// edits made in the settings window.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable", settings).
using System.Collections.Generic;
using PawnDiary.Integration;

namespace PawnDiary
{
    /// <summary>
    /// Bridges the public integration API to Pawn Diary's saved API-lane settings: builds a read-only
    /// setup snapshot and adds a new lane (persisting it and pushing it live to the shared LlmClient).
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

            // Persist and apply, mirroring the lane-relevant steps of PawnDiaryMod.WriteSettings so the
            // new lane takes effect for the next request and survives a restart.
            settings.NormalizeEndpointUrls();
            LlmClient.ApplyLaneConfiguration(settings.ActiveEndpoints());
            settings.Write();

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
            List<DiaryEventFilterSnapshot> result = new List<DiaryEventFilterSnapshot>();
            if (settings == null)
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
                    hasOverride = settings.HasGroupEnabledOverride(group.defName)
                });
            }

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

            DiaryInteractionGroupDef group = InteractionGroups.ByKey(key);
            if (!PawnDiaryMod.IsSettingsEventFilterGroup(group))
            {
                return false;
            }

            settings.SetGroupEnabled(group.defName, enabled);
            settings.Write();
            return true;
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

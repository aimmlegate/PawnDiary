// Async controller for the settings-window API tools. It keeps network request state out of
// PawnDiaryMod's immediate-mode UI code while preserving the main-thread handoff rules.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Owns the settings window's model-fetch and connection-test requests, including stale-result
    /// detection and the main-thread application of completed async results.
    /// </summary>
    internal sealed class ApiConnectionController
    {
        private readonly Func<PawnDiarySettings> settingsProvider;

        // Model IDs retrieved from the remote endpoint via FetchModels().
        private readonly List<string> fetchedModels = new List<string>();
        // Incremented on each FetchModels call so stale async results are discarded.
        private int fetchGeneration;
        // True while an async model-list request is in flight; disables the button.
        private bool isFetchingModels;
        // Which API row the current/last fetch targets, so its status + picker show on that row.
        private int fetchTargetIndex = -1;
        // Human-readable status line shown below the Fetch button. Written only on the main thread.
        private string fetchStatus;
        // Result handed back from the await continuation. RimWorld may resume awaits off the main
        // thread, so the continuation must not call .Translate() or edit shared UI collections.
        private volatile ModelFetchResult pendingFetchResult;

        // Row indices with a background capability-only refresh in flight. RefreshCapability is
        // fire-and-forget and many rows can refresh at once (it only touches the thread-safe
        // ModelCapabilityCache, never the single-flight picker state). The HashSet itself is guarded
        // because await continuations may resume on a worker thread while the UI thread cancels state.
        private readonly object capabilityRefreshLock = new object();
        private readonly HashSet<int> capabilityRefreshInFlight = new HashSet<int>();
        // Rows whose connection details changed WHILE a refresh was already running. A leading-edge
        // single-flight alone would drop that later change (the player's final URL/key edit), leaving
        // the capability cache stale; this records "run once more when the in-flight fetch finishes".
        private readonly HashSet<int> capabilityRefreshPending = new HashSet<int>();

        // Connection-test state is per-row so each API row's Test button runs independently: a
        // request on one row no longer blocks the others. The async HTTP continuation hands a
        // result back here and the settings window applies UI text and RimWorld log lines on the
        // main thread. This dictionary is main-thread-only (written before the await in
        // TestApiConnection, in ApplyPendingConnectionTestResult, and in the Cancel* methods).
        private readonly Dictionary<int, ConnectionTestRowState> connectionTestRows = new Dictionary<int, ConnectionTestRowState>();
        // Thread-safe handoff from N background continuations to the single main-thread drain.
        // Tests finish in arbitrary order, so this is a queue rather than a single slot (mirrors
        // LlmClient.Completed / PendingLogs ConcurrentQueue pattern). Drained each UI frame.
        private readonly ConcurrentQueue<ConnectionTestResult> pendingConnectionTestResults = new ConcurrentQueue<ConnectionTestResult>();

        /// <summary>Creates a controller that reads the live settings object only on UI/main-thread paths.</summary>
        public ApiConnectionController(Func<PawnDiarySettings> settingsProvider)
        {
            this.settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        }

        /// <summary>True while a model-list request is in flight.</summary>
        public bool IsFetchingModels
        {
            get { return isFetchingModels; }
        }

        /// <summary>The API row whose model fetch owns the current status and fetched-model picker.</summary>
        public int FetchTargetIndex
        {
            get { return fetchTargetIndex; }
        }

        /// <summary>The latest fetched model IDs for the current fetch target row.</summary>
        public List<string> FetchedModels
        {
            get { return fetchedModels; }
        }

        /// <summary>Returns the model-fetch status for the requested row, or null if another row owns it.</summary>
        public string ModelFetchStatusForRow(int index)
        {
            return fetchTargetIndex == index ? fetchStatus : null;
        }

        /// <summary>
        /// Returns the cached reasoning capability for one row's currently-selected model, or null
        /// when capability is unknown (provider did not advertise it). Reads the process-wide cache
        /// against the row's live endpoint+model so edits after a fetch are reflected immediately.
        /// </summary>
        public ModelReasoningCapability ModelCapabilityForRow(int index)
        {
            PawnDiarySettings settings = CurrentSettings();
            if (settings?.apiEndpoints == null || index < 0 || index >= settings.apiEndpoints.Count)
            {
                return null;
            }

            ApiEndpointConfig endpoint = settings.apiEndpoints[index];
            if (endpoint == null || string.IsNullOrWhiteSpace(endpoint.model))
            {
                return null;
            }

            return ModelCapabilityCache.Get(endpoint.url, endpoint.model);
        }

        /// <summary>True while a connection test for <paramref name="index"/> is in flight.</summary>
        public bool IsTestingConnection(int index)
        {
            return TryGetConnectionTestRow(index, out ConnectionTestRowState row) && row.isTesting;
        }

        /// <summary>Returns the connection-test status for the requested row, or null if it has none.</summary>
        public string ConnectionTestStatusForRow(int index)
        {
            return TryGetConnectionTestRow(index, out ConnectionTestRowState row) ? row.status : null;
        }

        /// <summary>Looks up the per-row connection-test state. Main-thread-only.</summary>
        private bool TryGetConnectionTestRow(int index, out ConnectionTestRowState row)
        {
            return connectionTestRows.TryGetValue(index, out row);
        }

        /// <summary>Counts the visible status lines for one API row.</summary>
        public int StatusLineCount(int index)
        {
            int count = 0;
            if (!string.IsNullOrEmpty(ModelFetchStatusForRow(index)))
            {
                count++;
            }

            if (!string.IsNullOrEmpty(ConnectionTestStatusForRow(index)))
            {
                count++;
            }

            return count;
        }

        /// <summary>Drains all completed async results. Call this from the settings UI main thread.</summary>
        public void ApplyPendingResults()
        {
            ApplyPendingFetchResult();
            ApplyPendingConnectionTestResult();
        }

        /// <summary>Invalidates both API settings-window async operations and clears their row state.</summary>
        public void CancelUiState()
        {
            CancelModelFetchUiState();
            CancelConnectionTestUiState();
            // Drop tracked in-flight capability refreshes too: a removed/moved/reset row no longer
            // maps to a valid index, so any result that lands afterwards is harmless (it writes only
            // the thread-safe cache) but we don't want the in-flight set to hold stale indices.
            lock (capabilityRefreshLock)
            {
                capabilityRefreshInFlight.Clear();
            }
        }

        /// <summary>
        /// Sends a tiny real generation request through one API row to verify endpoint, key, model,
        /// and compatibility mode. Each row runs independently: starting a test on row B while row A
        /// is still testing does not block or cancel A. All UI/log work stays on the main thread; the
        /// await continuation only enqueues a result object for ApplyPendingConnectionTestResult to
        /// consume. After the await, do not call .Translate(), read game state, or touch shared UI
        /// collections — only enqueue the immutable result snapshot.
        /// </summary>
        public async void TestApiConnection(int index)
        {
            // Get-or-create this row's state and bump its per-row generation so a stale in-flight
            // result from an earlier start (or before a cancel) is rejected on drain.
            ConnectionTestRowState row = GetOrCreateConnectionTestRow(index);
            int generation = ++row.generation;
            string url = string.Empty;
            string apiKey = string.Empty;
            string customAuthHeaderName = ApiEndpointPolicy.DefaultCustomHeaderName;
            string model = string.Empty;
            ApiAuthMode authMode = ApiAuthMode.BearerToken;
            ApiCompatibilityMode apiMode = ApiCompatibilityMode.OpenAIChatCompletions;
            string reasoningEffort = PawnDiarySettings.DefaultReasoningEffort;
            try
            {
                row.isTesting = true;
                row.status = "PawnDiary.Settings.TestingConnection".Translate();

                PawnDiarySettings settings = CurrentSettings();
                if (settings?.apiEndpoints == null || index < 0 || index >= settings.apiEndpoints.Count)
                {
                    row.isTesting = false;
                    row.status = null;
                    return;
                }

                ApiEndpointConfig endpoint = settings.apiEndpoints[index];
                if (endpoint == null)
                {
                    throw new InvalidOperationException("Endpoint row is missing.");
                }

                url = endpoint.url;
                apiKey = endpoint.apiKey;
                customAuthHeaderName = endpoint.customAuthHeaderName;
                model = endpoint.model;
                authMode = PawnDiarySettings.NormalizeAuthMode(endpoint.authMode);
                apiMode = PawnDiarySettings.NormalizeApiMode(endpoint.apiMode);
                reasoningEffort = PawnDiarySettings.NormalizeReasoningEffort(endpoint.reasoningEffort);
                int timeoutSeconds = settings.timeoutSeconds;
                float temperature = settings.temperature;
                string prompt = "PawnDiary.Settings.ConnectionTestPrompt".Translate();
                string validationError = ConnectionTestValidationError(url, model);
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    row.isTesting = false;
                    row.status = "PawnDiary.Settings.ConnectionTestFailed".Translate(validationError);
                    Log.Warning("[PawnDiary debug] API connection check failed for " + ConnectionTestLaneLabel(url, model, apiMode)
                        + ": " + validationError);
                    return;
                }

                // Best-effort: also refresh this row's reasoning capability in the background. Runs in
                // parallel with the test request and only updates the thread-safe cache, so a player
                // who tests but never clicks Fetch still gets reasoning-effort clamping.
                RefreshCapability(index);

                string sampleText = await LlmClient.TestConnection(new ApiEndpointConfig(url, apiKey, model)
                {
                    authMode = authMode,
                    customAuthHeaderName = customAuthHeaderName,
                    apiMode = apiMode,
                    reasoningEffort = reasoningEffort
                }, prompt, timeoutSeconds, temperature);

                pendingConnectionTestResults.Enqueue(new ConnectionTestResult
                {
                    generation = generation,
                    targetIndex = index,
                    success = true,
                    sampleText = sampleText,
                    endpointUrl = url,
                    apiKey = apiKey,
                    customAuthHeaderName = ApiEndpointPolicy.EffectiveAuthHeaderName(authMode, customAuthHeaderName),
                    model = model,
                    authMode = authMode,
                    apiMode = apiMode,
                    reasoningEffort = reasoningEffort
                });
            }
            catch (Exception ex)
            {
                pendingConnectionTestResults.Enqueue(new ConnectionTestResult
                {
                    generation = generation,
                    targetIndex = index,
                    success = false,
                    errorDetail = ex.Message,
                    endpointUrl = url,
                    apiKey = apiKey,
                    customAuthHeaderName = ApiEndpointPolicy.EffectiveAuthHeaderName(authMode, customAuthHeaderName),
                    model = model,
                    authMode = authMode,
                    apiMode = apiMode,
                    reasoningEffort = reasoningEffort
                });
            }
        }

        /// <summary>Gets the per-row state object for <paramref name="index"/>, creating it if absent.</summary>
        private ConnectionTestRowState GetOrCreateConnectionTestRow(int index)
        {
            if (!connectionTestRows.TryGetValue(index, out ConnectionTestRowState row))
            {
                row = new ConnectionTestRowState();
                connectionTestRows[index] = row;
            }

            return row;
        }

        /// <summary>
        /// Fetches the list of available model IDs from one API row's endpoint asynchronously,
        /// and auto-fills that row's model if it has none yet. Uses a generation counter so stale
        /// results from earlier (or reset) requests are discarded.
        /// </summary>
        public async void FetchModels(int index)
        {
            int generation = ++fetchGeneration;
            string url = string.Empty;
            string apiKey = string.Empty;
            string customAuthHeaderName = ApiEndpointPolicy.DefaultCustomHeaderName;
            ApiAuthMode authMode = ApiAuthMode.BearerToken;
            ApiCompatibilityMode apiMode = ApiCompatibilityMode.OpenAIChatCompletions;
            try
            {
                isFetchingModels = true;
                fetchTargetIndex = index;
                fetchStatus = "PawnDiary.Settings.FetchingModels".Translate();

                // Snapshot inputs on the main thread. After await, do not read game state, call
                // .Translate(), or edit shared collections; only hand a result back to ApplyPendingResults.
                PawnDiarySettings settings = CurrentSettings();
                if (settings?.apiEndpoints == null || index < 0 || index >= settings.apiEndpoints.Count)
                {
                    isFetchingModels = false;
                    return;
                }

                ApiEndpointConfig endpoint = settings.apiEndpoints[index];
                if (endpoint == null)
                {
                    throw new InvalidOperationException("Endpoint row is missing.");
                }

                url = endpoint.url;
                apiKey = endpoint.apiKey;
                customAuthHeaderName = endpoint.customAuthHeaderName;
                authMode = PawnDiarySettings.NormalizeAuthMode(endpoint.authMode);
                apiMode = endpoint.apiMode;
                int timeoutSeconds = settings.timeoutSeconds;

                ModelListResult fetchResult = await ModelListClient.FetchModels(url, apiKey, authMode, customAuthHeaderName, apiMode, timeoutSeconds);
                pendingFetchResult = new ModelFetchResult
                {
                    generation = generation,
                    targetIndex = index,
                    success = true,
                    models = fetchResult.Models,
                    capabilities = fetchResult.Capabilities,
                    endpointUrl = url,
                    apiKey = apiKey,
                    customAuthHeaderName = ApiEndpointPolicy.EffectiveAuthHeaderName(authMode, customAuthHeaderName),
                    authMode = authMode,
                    apiMode = apiMode
                };
            }
            catch (Exception ex)
            {
                pendingFetchResult = new ModelFetchResult
                {
                    generation = generation,
                    targetIndex = index,
                    success = false,
                    errorDetail = ex.Message,
                    endpointUrl = url,
                    apiKey = apiKey,
                    customAuthHeaderName = ApiEndpointPolicy.EffectiveAuthHeaderName(authMode, customAuthHeaderName),
                    authMode = authMode,
                    apiMode = apiMode
                };
            }
        }

        /// <summary>
        /// Background, non-blocking capability-only refresh for one row. Calls the same /models
        /// endpoint as <see cref="FetchModels"/> but updates ONLY the process-wide
        /// <see cref="ModelCapabilityCache"/> -- it never touches the single-flight picker state,
        /// status string, or auto-pick logic, so many rows can refresh at once without disturbing
        /// the UI. Used by the settings-open, URL/key-change, and Test-connection triggers so a
        /// player never has to click Fetch manually just to get reasoning-effort clamping.
        /// Providers that return no reasoning object (OpenAI-direct, GGUF) cache nothing and degrade
        /// gracefully. Fire-and-forget; the per-frame <see cref="ModelCapabilityForRow"/> read picks
        /// up new entries next frame.
        /// </summary>
        public async void RefreshCapability(int index)
        {
            // Snapshot inputs on the main thread. After await, do not read game state or call
            // .Translate(); only write immutable entries into the thread-safe cache.
            PawnDiarySettings settings = CurrentSettings();
            if (settings?.apiEndpoints == null || index < 0 || index >= settings.apiEndpoints.Count)
            {
                return;
            }

            ApiEndpointConfig endpoint = settings.apiEndpoints[index];
            if (endpoint == null || string.IsNullOrWhiteSpace(endpoint.url) || string.IsNullOrWhiteSpace(endpoint.model))
            {
                return;
            }

            // Single-flight per row: if a refresh is already running (e.g. mid-keystroke on the URL or
            // key), don't start a second. Instead remember that the row changed again so the in-flight
            // fetch re-runs once when it finishes and picks up the player's final edit.
            lock (capabilityRefreshLock)
            {
                if (!capabilityRefreshInFlight.Add(index))
                {
                    capabilityRefreshPending.Add(index);
                    return;
                }
            }

            string url = endpoint.url;
            string apiKey = endpoint.apiKey;
            string customAuthHeaderName = endpoint.customAuthHeaderName;
            ApiAuthMode authMode = PawnDiarySettings.NormalizeAuthMode(endpoint.authMode);
            ApiCompatibilityMode apiMode = endpoint.apiMode;
            int timeoutSeconds = settings.timeoutSeconds;

            try
            {
                ModelListResult fetchResult = await ModelListClient.FetchModels(
                    url, apiKey, authMode, customAuthHeaderName, apiMode, timeoutSeconds);

                // Cache only the capabilities the provider actually advertised; models without a
                // reasoning object stay absent (treated as "unknown" by readers -> graceful degrade).
                if (fetchResult?.Capabilities != null)
                {
                    foreach (KeyValuePair<string, ModelReasoningCapability> entry in fetchResult.Capabilities)
                    {
                        ModelCapabilityCache.Update(url, entry.Key, entry.Value);
                    }
                }
            }
            catch
            {
                // Capability refresh is best-effort: a failed/empty /models request must never break
                // the settings UI or spam logs. The row simply stays "capability unknown" and the
                // request passes through unclamped, exactly as before this feature existed.
            }
            finally
            {
                bool rerun;
                lock (capabilityRefreshLock)
                {
                    capabilityRefreshInFlight.Remove(index);
                    // Release the slot first, then re-run once if the row changed during this fetch,
                    // so the latest URL/key is what actually gets its capability cached.
                    rerun = capabilityRefreshPending.Remove(index);
                }

                if (rerun)
                {
                    RefreshCapability(index);
                }
            }
        }

        /// <summary>
        /// Refreshes capability for every row whose (endpoint, model) capability is not yet cached.
        /// Called once when the settings window opens so a returning player's already-configured
        /// lanes get clamping without a manual Fetch. Rows with no model are skipped (they cannot be
        /// targeted); they are covered by the URL-change or full-Fetch triggers once a model is set.
        /// </summary>
        public void RefreshCapabilityForUncachedRows()
        {
            PawnDiarySettings settings = CurrentSettings();
            if (settings?.apiEndpoints == null)
            {
                return;
            }

            for (int i = 0; i < settings.apiEndpoints.Count; i++)
            {
                ApiEndpointConfig endpoint = settings.apiEndpoints[i];
                if (endpoint == null
                    || string.IsNullOrWhiteSpace(endpoint.url)
                    || string.IsNullOrWhiteSpace(endpoint.model))
                {
                    continue;
                }

                if (ModelCapabilityCache.Get(endpoint.url, endpoint.model) == null)
                {
                    RefreshCapability(i);
                }
            }
        }

        /// <summary>Invalidates any in-flight model-list fetch and clears the per-row picker state.</summary>
        public void CancelModelFetchUiState()
        {
            fetchGeneration++;
            isFetchingModels = false;
            fetchTargetIndex = -1;
            pendingFetchResult = null;
            fetchedModels.Clear();
            fetchStatus = null;
        }

        /// <summary>
        /// Invalidates every in-flight connection test and clears all per-row test state. Called on
        /// row remove/move and "Reset connection". Any continuation that lands afterwards finds no
        /// matching row entry on drain, so its result is discarded.
        /// </summary>
        public void CancelConnectionTestUiState()
        {
            connectionTestRows.Clear();
            ConnectionTestResult stale;
            while (pendingConnectionTestResults.TryDequeue(out stale))
            {
                // Drain: drop anything already queued so it is never applied to a fresh state.
            }
        }

        private PawnDiarySettings CurrentSettings()
        {
            return settingsProvider();
        }

        private static string ConnectionTestValidationError(string url, string model)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return "PawnDiary.Settings.ConnectionTestMissingEndpoint".Translate();
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                return "PawnDiary.Settings.ConnectionTestMissingModel".Translate();
            }

            return null;
        }

        /// <summary>
        /// Drains a completed model fetch on the main thread: applies the localized status line, the
        /// fetched model list, and the optional auto-fill of a blank model.
        /// </summary>
        private void ApplyPendingFetchResult()
        {
            ModelFetchResult result = pendingFetchResult;
            if (result == null)
            {
                return;
            }

            pendingFetchResult = null;
            if (result.generation != fetchGeneration)
            {
                return;
            }

            isFetchingModels = false;
            PawnDiarySettings settings = CurrentSettings();
            if (!FetchTargetStillMatches(result, settings))
            {
                fetchTargetIndex = -1;
                fetchedModels.Clear();
                fetchStatus = null;
                return;
            }

            if (!result.success)
            {
                // TrimForStatus redacts + one-lines + caps length; errorDetail can be a raw exception
                // message or a provider error body, either of which may echo a key-bearing URL.
                fetchStatus = "PawnDiary.Settings.FetchFailed".Translate(TrimForStatus(result.errorDetail));
                return;
            }

            fetchedModels.Clear();
            if (result.models != null)
            {
                fetchedModels.AddRange(result.models);
            }

            // Feed the process-wide capability cache so the settings UI can guide the effort
            // dropdown and the generation path can clamp the outgoing reasoning_effort. Done on the
            // main thread (here); reads on the background thread are safe against immutable entries.
            if (result.capabilities != null && result.capabilities.Count > 0)
            {
                foreach (KeyValuePair<string, ModelReasoningCapability> entry in result.capabilities)
                {
                    ModelCapabilityCache.Update(result.endpointUrl, entry.Key, entry.Value);
                }
            }

            if (fetchedModels.Count == 0)
            {
                fetchStatus = "PawnDiary.Settings.NoModelsReturned".Translate();
                return;
            }

            fetchStatus = "PawnDiary.Settings.ModelsFound".Translate(fetchedModels.Count);

            // Auto-fill only a blank/placeholder model, and only if the target row still exists.
            if (result.targetIndex >= 0 && result.targetIndex < settings.apiEndpoints.Count)
            {
                ApiEndpointConfig endpoint = settings.apiEndpoints[result.targetIndex];
                if (endpoint != null
                    && (string.IsNullOrWhiteSpace(endpoint.model) || endpoint.model == PawnDiarySettings.DefaultModelName))
                {
                    endpoint.model = fetchedModels[0];
                }
            }
        }

        /// <summary>
        /// Applies completed API connection tests on the main thread: drains the result queue and
        /// updates each result's row status, writing a concise RimWorld log line with no API key.
        /// Multiple rows' tests can complete between frames, so all queued results are drained.
        /// </summary>
        private void ApplyPendingConnectionTestResult()
        {
            while (pendingConnectionTestResults.TryDequeue(out ConnectionTestResult result))
            {
                // Stale if the row no longer exists (removed/cancelled) or was restarted with a
                // newer generation. Drop silently — its UI state was already replaced or cleared.
                if (!connectionTestRows.TryGetValue(result.targetIndex, out ConnectionTestRowState row)
                    || result.generation != row.generation)
                {
                    continue;
                }

                if (!ConnectionTestTargetStillMatches(result, CurrentSettings()))
                {
                    // The row was edited/moved since the test started; clear its entry entirely.
                    connectionTestRows.Remove(result.targetIndex);
                    continue;
                }

                row.isTesting = false;
                if (result.success)
                {
                    row.status = "PawnDiary.Settings.ConnectionTestSucceeded".Translate(TrimForStatus(result.sampleText));
                    Log.Message("[PawnDiary debug] API connection check succeeded for " + ConnectionTestLaneLabel(result)
                        + " sample=\"" + TrimForLog(result.sampleText) + "\"");
                }
                else
                {
                    row.status = "PawnDiary.Settings.ConnectionTestFailed".Translate(TrimForStatus(result.errorDetail));
                    Log.Warning("[PawnDiary debug] API connection check failed for " + ConnectionTestLaneLabel(result)
                        + ": " + TrimForLog(result.errorDetail));
                }
            }
        }

        /// <summary>
        /// Returns true when the row that requested a model list still points at the same endpoint.
        /// A user can edit or remove rows while the HTTP request is in flight.
        /// </summary>
        private static bool FetchTargetStillMatches(ModelFetchResult result, PawnDiarySettings settings)
        {
            if (settings?.apiEndpoints == null || result == null || result.targetIndex < 0 || result.targetIndex >= settings.apiEndpoints.Count)
            {
                return false;
            }

            ApiEndpointConfig endpoint = settings.apiEndpoints[result.targetIndex];
            return endpoint != null
                && ApiLaneIdentity.ForFetchTarget(endpoint.url, endpoint.apiKey, endpoint.authMode, endpoint.customAuthHeaderName, endpoint.apiMode)
                == ApiLaneIdentity.ForFetchTarget(result.endpointUrl, result.apiKey, result.authMode, result.customAuthHeaderName, result.apiMode);
        }

        /// <summary>Returns true when the API row still matches the exact configuration that was tested.</summary>
        private static bool ConnectionTestTargetStillMatches(ConnectionTestResult result, PawnDiarySettings settings)
        {
            if (settings?.apiEndpoints == null || result == null || result.targetIndex < 0 || result.targetIndex >= settings.apiEndpoints.Count)
            {
                return false;
            }

            ApiEndpointConfig endpoint = settings.apiEndpoints[result.targetIndex];
            return endpoint != null
                && ApiLaneIdentity.ForConnectionTest(endpoint.url, endpoint.apiKey, endpoint.model, endpoint.authMode, endpoint.customAuthHeaderName, endpoint.apiMode, endpoint.reasoningEffort)
                == ApiLaneIdentity.ForConnectionTest(result.endpointUrl, result.apiKey, result.model, result.authMode, result.customAuthHeaderName, result.apiMode, result.reasoningEffort);
        }

        private static string ConnectionTestLaneLabel(ConnectionTestResult result)
        {
            if (result == null)
            {
                return "<null>";
            }

            return ConnectionTestLaneLabel(result.endpointUrl, result.model, result.apiMode);
        }

        private static string ConnectionTestLaneLabel(string endpointUrl, string modelName, ApiCompatibilityMode apiMode)
        {
            return ApiLaneLabels.Label(endpointUrl, modelName, apiMode);
        }

        private static string TrimForStatus(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            // Redact BEFORE collapsing/truncating so a secret can never survive as the kept prefix.
            // This status string is shown in the settings window (a surface players screenshot when
            // reporting problems); a query-param key echoed back in an error body must not reach it.
            value = ApiLaneLabels.OneLine(ApiLaneLabels.RedactSecrets(value));
            return value.Length <= 80 ? value : value.Substring(0, 80) + "...";
        }

        private static string TrimForLog(string value)
        {
            return ApiLaneLabels.TrimForLog(value);
        }

        // Result of one model fetch, handed from the await continuation to the main-thread draw.
        // Never mutated after construction; assigned to pendingFetchResult as a single reference write.
        private sealed class ModelFetchResult
        {
            public int generation;
            public int targetIndex;
            public bool success;
            public List<string> models;   // immutable snapshot returned by ModelListClient
            // Per-model reasoning capability advertised by the endpoint (OpenRouter, some gateways).
            // Null/empty when the provider does not report capability; absent models stay "unknown".
            public Dictionary<string, ModelReasoningCapability> capabilities;
            public string errorDetail;    // raw, untranslated exception message
            public string endpointUrl;    // row URL snapshot used to reject stale row edits
            public string apiKey;         // row key snapshot; never logged or shown
            public string customAuthHeaderName; // effective custom-header name; never logged or shown
            public ApiAuthMode authMode;  // auth style snapshot; changing it changes request shape
            public ApiCompatibilityMode apiMode; // row mode snapshot; changing modes changes model-list shape
        }

        // Per-row, main-thread-only state for one API connection test. A row owns its own
        // generation counter so a stale in-flight result (from before a cancel or restart) is
        // rejected on drain without affecting other rows.
        private sealed class ConnectionTestRowState
        {
            public int generation;
            public bool isTesting;
            public string status;
        }

        // Result of one connection test, handed from the await continuation to the main-thread draw.
        // Contains the API key only for stale-row matching; it is never displayed or logged.
        private sealed class ConnectionTestResult
        {
            public int generation;
            public int targetIndex;
            public bool success;
            public string sampleText;
            public string errorDetail;
            public string endpointUrl;
            public string apiKey;
            public string customAuthHeaderName;
            public string model;
            public ApiAuthMode authMode;
            public ApiCompatibilityMode apiMode;
            public string reasoningEffort;
        }
    }
}

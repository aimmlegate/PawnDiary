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
                fetchStatus = "PawnDiary.Settings.FetchFailed".Translate(result.errorDetail ?? string.Empty);
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

            value = ApiLaneLabels.OneLine(value);
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

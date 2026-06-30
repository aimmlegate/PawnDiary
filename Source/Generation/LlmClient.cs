// Async HTTP client for the LLM endpoint. A queue + concurrency gate (SemaphoreSlim) caps how
// many requests are in flight; each has a hard deadline that purges stuck requests; transient
// errors retry. Finished results are drained by DiaryGameComponent.GameComponentTick. `async
// Task` ≈ Promise and `CancellationToken` ≈ AbortSignal — see AGENTS.md ("async Task").
// The concurrency logic below is already commented inline; start there for details.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Describes a single generation request to a compatible LLM endpoint.
    /// Populated by the diary system and passed to <see cref="LlmClient.Enqueue"/>.
    /// </summary>
    public class LlmGenerationRequest
    {
        /// <summary>Identifies the game event this generation is for (e.g. "Raid_12").</summary>
        public string eventId;

        /// <summary>The pawn role whose point-of-view the diary entry should be written from.</summary>
        public string povRole;

        /// <summary>Stamped by <see cref="LlmClient.Enqueue"/>; used to discard results from stale sessions.</summary>
        public long sessionId;

        /// <summary>Optional system prompt that sets writing style or formatting rules for the model.</summary>
        public string systemPrompt;

        /// <summary>Raw narrative text describing the game event, sent as the user message.</summary>
        public string rawText;

        /// <summary>Base URL of the compatible API (e.g. "https://api.openai.com/v1").</summary>
        public string endpointUrl;

        /// <summary>Model identifier accepted by the API (e.g. "gpt-4o-mini").</summary>
        public string modelName;

        /// <summary>Bearer token for API authentication; may be empty for local endpoints.</summary>
        public string apiKey;

        /// <summary>How to send <see cref="apiKey"/> for this lane.</summary>
        public ApiAuthMode authMode;

        /// <summary>Header name used when <see cref="authMode"/> is CustomHeader.</summary>
        public string customAuthHeaderName;

        /// <summary>Request/response compatibility mode for this lane.</summary>
        public ApiCompatibilityMode apiMode;

        /// <summary>OpenAI Responses reasoning effort. "default" means no reasoning override.</summary>
        public string reasoningEffort;

        /// <summary>
        /// True when XML/settings event-prompt policy asked for this primary model explicitly. Forced
        /// primaries are attempted even if their lane is cooling; normal failover still runs on error.
        /// </summary>
        public bool forcePrimaryLane;

        /// <summary>
        /// Ordered alternate lanes (endpoint + key + model) to try if the primary lane above errors —
        /// "on error, use the next model". Optional; null/empty means no failover. Populated by the
        /// diary system with the other configured APIs.
        /// </summary>
        public List<ApiEndpointConfig> failoverTargets;

        /// <summary>Per-request wall-clock timeout in seconds. Also used as an overall deadline across retries.</summary>
        public int timeoutSeconds;

        /// <summary>Maximum number of tokens the model may generate in its response.</summary>
        public int maxTokens;

        /// <summary>Sampling temperature (0.0–2.0). Higher values produce more random output.</summary>
        public float temperature;

        /// <summary>Linked to the session cancellation source so stale requests are aborted on game load.</summary>
        public CancellationToken cancellationToken;

        /// <summary>True when this is a follow-up title-generation request (not a main diary entry).
        /// The result dispatcher uses the flag to route the response to the right handler: main
        /// entries call <see cref="DiaryEvent.ApplyLlmResult"/>, title calls call
        /// <see cref="DiaryEvent.MarkTitleComplete"/> / <see cref="DiaryEvent.MarkTitleFailed"/>. Defaults
        /// to false so the existing main-entry path is unchanged.</summary>
        public bool isTitleRequest;

        /// <summary>
        /// Prompt-time response rules. Captured before the request leaves the main thread so the
        /// background HTTP worker can clean the model text without rereading game state or XML.
        /// </summary>
        public DiaryResponseRules responseRules;
    }

    /// <summary>
    /// Holds the outcome of a single LLM generation attempt. Produced by <see cref="LlmClient"/>
    /// and consumed by <c>DiaryGameComponent.GameComponentTick</c> on the main thread.
    /// </summary>
    public class LlmGenerationResult
    {
        /// <summary>Mirrors the request's eventId so the consumer can match result back to the originating event.</summary>
        public string eventId;

        /// <summary>Mirrors the request's povRole for result routing.</summary>
        public string povRole;

        /// <summary>Session ID that produced this result; stale results are discarded on dequeue.</summary>
        public long sessionId;

        /// <summary>True when the model returned usable text; false when an error occurred.</summary>
        public bool success;

        /// <summary>The model's generated text when <see cref="success"/> is true.</summary>
        public string generatedText;

        /// <summary>The extracted final-answer text before local length/sentence cleanup.
        /// Known reasoning blocks are stripped before this is stored for debug-only UI inspection.</summary>
        public string rawResponse;

        /// <summary>Human-readable error message when <see cref="success"/> is false.</summary>
        public string error;

        /// <summary>Base URL of the API lane that actually produced the text — may differ from the
        /// primary lane after failover. Used to keep the recorded LLM meta (and recipient pinning) accurate.</summary>
        public string endpointUrl;

        /// <summary>Model of the API lane that actually produced the text (see <see cref="endpointUrl"/>).</summary>
        public string modelName;

        /// <summary>Compatibility mode of the lane that actually produced the text.</summary>
        public ApiCompatibilityMode apiMode;

        /// <summary>Bearer token of the lane that actually produced the text.
        /// Transient only: used for same-session follow-up pinning, never saved or logged.</summary>
        public string apiKey;

        /// <summary>Auth style of the lane that actually produced the text.</summary>
        public ApiAuthMode authMode;

        /// <summary>Header name used when authMode is CustomHeader.</summary>
        public string customAuthHeaderName;

        /// <summary>Mirror of <see cref="LlmGenerationRequest.isTitleRequest"/>. The dispatcher
        /// branches on this to route the result to the title handler instead of the main-entry
        /// handler. Defaults to false so existing main-entry results behave exactly as before.</summary>
        public bool isTitleRequest;
    }

    /// <summary>
    /// Static client that manages a bounded queue of LLM chat-completion requests.
    /// Every game save-load triggers a new session which cancels stale in-flight work.
    /// Results are written to a <see cref="ConcurrentQueue{T}"/> and drained on the
    /// main RimWorld tick by <c>DiaryGameComponent.GameComponentTick</c>.
    /// </summary>
    public static class LlmClient
    {
        /// <summary>Maximum retry attempts for a single request before giving up.</summary>
        private const int MaxAttempts = 3;

        /// <summary>Upper bound the semaphore cannot exceed, regardless of user settings.</summary>
        private const int MaxConcurrencyCap = 16;

        /// <summary>Hard cap for one endpoint response body, before JSON parsing or logging.</summary>
        private const int MaxResponseBytes = 1024 * 1024;

        /// <summary>Finished results awaiting consumption by the main-thread tick.</summary>
        private static readonly ConcurrentQueue<LlmGenerationResult> Completed = new ConcurrentQueue<LlmGenerationResult>();

        /// <summary>
        /// Debug log lines produced on background worker threads, awaiting flush on the main-thread
        /// tick. RimWorld's <c>Log.Message</c> appends to a shared queue that the in-game log window
        /// enumerates during OnGUI, so calling it off the main thread can corrupt that enumeration
        /// ("Collection was modified"). Like <see cref="Completed"/>, background work hands log lines
        /// across the thread boundary through this queue and the main thread does the actual logging.
        /// </summary>
        private static readonly ConcurrentQueue<string> PendingLogs = new ConcurrentQueue<string>();

        /// <summary>
        /// Cached from settings on the main thread. Background send workers read this volatile flag
        /// before queueing diagnostics so normal play does not fill RimWorld's log.
        /// </summary>
        private static volatile bool debugLoggingEnabled;

        /// <summary>Tracks in-flight request keys to prevent duplicate submissions for the same event + session.</summary>
        private static readonly ConcurrentDictionary<string, byte> PendingKeys = new ConcurrentDictionary<string, byte>();

        /// <summary>Shared HTTP client with no built-in timeout; deadlines are handled per-request via cancellation tokens.</summary>
        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan // disable HttpClient's own timeout; we use CancellationToken deadlines instead
        };

        /// <summary>Cancellation source for the current session; replaced on each <see cref="BeginSession"/> call.</summary>
        private static CancellationTokenSource sessionCancellation = new CancellationTokenSource();

        /// <summary>Monotonically increasing ID so stale results from previous sessions are ignored.</summary>
        private static long currentSessionId;

        // One gate per API lane (keyed by endpoint+model). Each lane caps how many of its own
        // requests are in flight at once; lanes run independently so several APIs work in
        // parallel. The cap per lane is `maxConcurrentRequests` (set it to 1 for a local model
        // that serves one request at a time). Gates are created lazily and rebuilt when the limit changes.
        private static ConcurrentDictionary<ApiLaneIdentity, SemaphoreSlim> sendGates = new ConcurrentDictionary<ApiLaneIdentity, SemaphoreSlim>();

        // Runtime-only transient-failure backoff per lane. Protected by a simple lock because the
        // main thread reads it for routing while background HTTP workers update it after failures.
        private static readonly object laneCooldownLock = new object();
        private static readonly Dictionary<ApiLaneIdentity, LaneCooldownState> laneCooldowns = new Dictionary<ApiLaneIdentity, LaneCooldownState>();
        // Set after settings are written. Null means we have not received a settings snapshot yet,
        // so old saves/early startup should keep accepting cooldown updates normally.
        private static HashSet<ApiLaneIdentity> configuredLaneKeys;

        // Advances deterministic lane selection for balanced and preference-weighted routing.
        // Incremented atomically because requests may be enqueued from different threads.
        private static int roundRobinCounter = -1;

        static LlmClient()
        {
            // .NET / Mono throttle outbound connections per host through
            // ServicePointManager.DefaultConnectionLimit (default 2). Without raising it, a
            // maxConcurrentRequests above 2 has no real effect for a single endpoint — extra
            // requests queue at the transport layer behind two connections. Raise the floor so the
            // per-lane SemaphoreSlim is the only thing limiting in-flight requests to one host.
            // This is a process-global setting; we only ever raise it, never lower it, so we don't
            // shrink a limit another mod may have already widened.
            try
            {
                if (System.Net.ServicePointManager.DefaultConnectionLimit < MaxConcurrencyCap)
                {
                    System.Net.ServicePointManager.DefaultConnectionLimit = MaxConcurrencyCap;
                }
            }
            catch
            {
                // Non-fatal: if the platform refuses the change, fall back to the default behavior.
            }
        }

        /// <summary>
        /// Starts a new session, cancelling all in-flight requests from the previous one
        /// and resetting the concurrency gate and result queue. Called on game load.
        /// </summary>
        public static void BeginSession()
        {
            ApplyDebugLoggingSetting();
            CancellationTokenSource oldCancellation = sessionCancellation;
            sessionCancellation = new CancellationTokenSource();
            sendGates = new ConcurrentDictionary<ApiLaneIdentity, SemaphoreSlim>(); // fresh per-lane gates at the current limit
            lock (laneCooldownLock)
            {
                laneCooldowns.Clear();
            }
            Interlocked.Increment(ref currentSessionId); // bump so stale results are ignored
            ClearCompleted();
            oldCancellation.Cancel(); // abort all in-flight requests from previous session
        }

        /// <summary>
        /// Discards the per-lane gates so the next request rebuilds them at the latest
        /// user limit. Safe to call at any time — in-flight workers hold their own gate
        /// reference and release that one, so they never touch the new gates.
        /// </summary>
        public static void ApplyConcurrency()
        {
            // Safe to swap at any time: in-flight workers hold their own gate reference
            // and release that one, so they never touch these new gates.
            sendGates = new ConcurrentDictionary<ApiLaneIdentity, SemaphoreSlim>();
        }

        /// <summary>
        /// Applies the current API lane snapshot after settings are saved. Gates are rebuilt at the
        /// latest concurrency limit, and cooldowns for removed or reconfigured lanes are discarded.
        /// </summary>
        public static void ApplyLaneConfiguration(List<ApiEndpointConfig> activeLanes)
        {
            // Safe to swap at any time: in-flight workers hold their own gate reference
            // and release that one, so they never touch these new gates.
            sendGates = new ConcurrentDictionary<ApiLaneIdentity, SemaphoreSlim>();

            HashSet<ApiLaneIdentity> activeKeys = BuildLaneKeySet(activeLanes);
            int removedCount = 0;
            lock (laneCooldownLock)
            {
                configuredLaneKeys = activeKeys;
                if (laneCooldowns.Count > 0)
                {
                    List<ApiLaneIdentity> staleKeys = new List<ApiLaneIdentity>();
                    foreach (ApiLaneIdentity key in laneCooldowns.Keys)
                    {
                        if (!activeKeys.Contains(key))
                        {
                            staleKeys.Add(key);
                        }
                    }

                    for (int i = 0; i < staleKeys.Count; i++)
                    {
                        if (laneCooldowns.Remove(staleKeys[i]))
                        {
                            removedCount++;
                        }
                    }
                }
            }

            if (removedCount > 0)
            {
                LogDebug("Pruned stale lane cooldowns after API settings change count=" + removedCount);
            }
        }

        /// <summary>
        /// Refreshes the cached debug-log gate from settings. Call this from main-thread settings
        /// paths after the player changes the dev debug toggle.
        /// </summary>
        public static void ApplyDebugLoggingSetting()
        {
            debugLoggingEnabled = PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.showLlmDebugInfo;
        }

        /// <summary>
        /// Returns true when verbose API/LLM diagnostics should be written to the RimWorld log.
        /// </summary>
        public static bool DebugLoggingEnabled()
        {
            return debugLoggingEnabled;
        }

        /// <summary>
        /// Sends one tiny real generation request through the selected API lane. Used by the
        /// settings window's "Test" button to prove that URL, model, key, and compatibility mode can
        /// actually produce text, not merely return a model list. The prompt is built on the main
        /// thread by the caller because prompt text is localized and .Translate() is not
        /// background-thread-safe.
        /// </summary>
        public static async Task<string> TestConnection(ApiEndpointConfig endpoint, string prompt, int timeoutSeconds, float temperature)
        {
            if (endpoint == null)
            {
                throw new InvalidOperationException("No API row was selected.");
            }

            if (string.IsNullOrWhiteSpace(endpoint.url))
            {
                throw new InvalidOperationException("The API endpoint URL is blank.");
            }

            if (string.IsNullOrWhiteSpace(endpoint.model))
            {
                throw new InvalidOperationException("The API model name is blank.");
            }

            using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds))))
            {
                SendResponse response = await SendOnce(new LlmGenerationRequest
                {
                    eventId = "connection-test",
                    povRole = "test",
                    systemPrompt = string.Empty,
                    rawText = string.IsNullOrWhiteSpace(prompt) ? "Reply with a short confirmation sentence." : prompt,
                    endpointUrl = endpoint.url,
                    modelName = endpoint.model,
                    apiKey = endpoint.apiKey,
                    authMode = PawnDiarySettings.NormalizeAuthMode(endpoint.authMode),
                    customAuthHeaderName = endpoint.customAuthHeaderName,
                    apiMode = endpoint.apiMode,
                    reasoningEffort = PawnDiarySettings.NormalizeReasoningEffort(endpoint.reasoningEffort),
                    timeoutSeconds = timeoutSeconds,
                    maxTokens = 32,
                    temperature = temperature,
                    isTitleRequest = true
                }, cancellation.Token);

                return response.CleanText;
            }
        }

        /// <summary>
        /// Returns the next round-robin index for spreading new events across the configured
        /// API lanes. Callers take it modulo the lane count. Always non-negative.
        /// </summary>
        public static int NextRoundRobinIndex()
        {
            // Mask off the sign so the value stays non-negative even after Int32 overflow.
            return Interlocked.Increment(ref roundRobinCounter) & int.MaxValue;
        }

        /// <summary>Returns true while a lane is temporarily skipped after transient failures.</summary>
        public static bool IsLaneCooling(ApiEndpointConfig lane)
        {
            if (lane == null)
            {
                return false;
            }

            return IsLaneCooling(ApiLaneIdentity.ForGate(lane.url, lane.model, lane.apiMode, lane.authMode, lane.customAuthHeaderName, lane.apiKey));
        }

        /// <summary>
        /// Builds the gate key identifying an API lane. Requests sharing endpoint, model, mode, auth
        /// style, and key share a lane (and therefore its in-flight cap and cooldown state).
        /// </summary>
        private static ApiLaneIdentity GateKey(LlmGenerationRequest request)
        {
            return LaneIdentity(request);
        }

        private static ApiLaneIdentity LaneIdentity(LlmGenerationRequest request)
        {
            if (request == null)
            {
                return default(ApiLaneIdentity);
            }

            return ApiLaneIdentity.ForGate(request.endpointUrl, request.modelName, request.apiMode, request.authMode, request.customAuthHeaderName, request.apiKey);
        }

        private static HashSet<ApiLaneIdentity> BuildLaneKeySet(List<ApiEndpointConfig> lanes)
        {
            HashSet<ApiLaneIdentity> keys = new HashSet<ApiLaneIdentity>();
            if (lanes == null)
            {
                return keys;
            }

            foreach (ApiEndpointConfig lane in lanes)
            {
                if (lane == null)
                {
                    continue;
                }

                ApiLaneIdentity key = ApiLaneIdentity.ForGate(lane.url, lane.model, lane.apiMode, lane.authMode, lane.customAuthHeaderName, lane.apiKey);
                if (!key.Empty)
                {
                    keys.Add(key);
                }
            }

            return keys;
        }

        /// <summary>
        /// Gets the gate for a lane, creating it at the current per-lane limit on first use.
        /// </summary>
        private static SemaphoreSlim GetOrCreateGate(LlmGenerationRequest request)
        {
            ApiLaneIdentity key = GateKey(request);
            return sendGates.GetOrAdd(key, _ => new SemaphoreSlim(ResolveConcurrency(), MaxConcurrencyCap));
        }

        /// <summary>
        /// Reads the user's concurrency setting, clamping it to [1, <see cref="MaxConcurrencyCap"/>].
        /// Falls back to 4 when settings are unavailable (e.g. during early init).
        /// </summary>
        private static int ResolveConcurrency()
        {
            int requested = PawnDiaryMod.Settings != null ? PawnDiaryMod.Settings.maxConcurrentRequests : 4;
            if (requested < 1)
            {
                return 1;
            }

            return requested > MaxConcurrencyCap ? MaxConcurrencyCap : requested;
        }

        private static bool IsLaneCooling(ApiLaneIdentity laneKey)
        {
            if (laneKey.Empty)
            {
                return false;
            }

            lock (laneCooldownLock)
            {
                LaneCooldownState state;
                return laneCooldowns.TryGetValue(laneKey, out state)
                    && state != null
                    && state.cooldownUntilUtc > DateTime.UtcNow;
            }
        }

        private static List<bool> SnapshotLaneReadiness(List<ApiEndpointConfig> lanes)
        {
            List<bool> ready = new List<bool>();
            if (lanes == null || lanes.Count == 0)
            {
                return ready;
            }

            foreach (ApiEndpointConfig lane in lanes)
            {
                ready.Add(lane != null && !IsLaneCooling(lane));
            }

            return ready;
        }

        private static bool HasReadyLane(List<bool> readiness)
        {
            if (readiness == null)
            {
                return false;
            }

            for (int i = 0; i < readiness.Count; i++)
            {
                if (readiness[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LaneWasReady(List<bool> readiness, int index)
        {
            return readiness != null && index >= 0 && index < readiness.Count && readiness[index];
        }

        private static void MarkLaneCooldown(LlmGenerationRequest request, string error)
        {
            ApiLaneIdentity key = LaneIdentity(request);
            if (key.Empty)
            {
                return;
            }

            int failureCount;
            int cooldownSeconds;
            lock (laneCooldownLock)
            {
                if (configuredLaneKeys != null && !configuredLaneKeys.Contains(key))
                {
                    return;
                }

                LaneCooldownState state;
                if (!laneCooldowns.TryGetValue(key, out state) || state == null)
                {
                    state = new LaneCooldownState();
                    laneCooldowns[key] = state;
                }

                state.failureCount++;
                failureCount = state.failureCount;
                cooldownSeconds = ApiEndpointPolicy.CooldownSecondsForFailures(failureCount);
                state.cooldownUntilUtc = DateTime.UtcNow.AddSeconds(cooldownSeconds);
            }

            LogDebug(
                "Cooling lane after transient failure for "
                + cooldownSeconds
                + "s failures="
                + failureCount
                + " lane="
                + LaneLabel(request)
                + " error="
                + TrimForLog(error));
        }

        private static void ClearLaneCooldown(LlmGenerationRequest request)
        {
            ApiLaneIdentity key = LaneIdentity(request);
            if (key.Empty)
            {
                return;
            }

            bool cleared;
            lock (laneCooldownLock)
            {
                cleared = laneCooldowns.Remove(key);
            }

            if (cleared)
            {
                LogDebug("Cleared lane cooldown after success lane=" + LaneLabel(request));
            }
        }

        /// <summary>
        /// Returns true if a request for this event+role is queued or in flight in the **current**
        /// session (its dedup key is present). The diary tick uses this to tell a genuinely-running
        /// generation apart from one orphaned on "Generating" by a cancelled session, so the orphan
        /// recovery never re-sends a request that is still in progress. Keyed on the current session
        /// id, so a leftover key from a previous session never reads as in flight.
        /// </summary>
        public static bool IsInFlight(string eventId, string povRole)
        {
            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(povRole))
            {
                return false;
            }

            long sessionId = Interlocked.Read(ref currentSessionId);
            return PendingKeys.ContainsKey(PendingKey(eventId, povRole, sessionId, false));
        }

        /// <summary>
        /// Accepts a generation request, stamps it with the current session, deduplicates
        /// it, and fires off the async send loop in the background. Returns immediately.
        /// </summary>
        public static void Enqueue(LlmGenerationRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.eventId) || string.IsNullOrWhiteSpace(request.povRole))
            {
                return;
            }

            request.sessionId = Interlocked.Read(ref currentSessionId); // stamp with current session
            request.cancellationToken = sessionCancellation.Token;
            string pendingKey = PendingKey(request.eventId, request.povRole, request.sessionId, request.isTitleRequest);
            if (!PendingKeys.TryAdd(pendingKey, 0)) // deduplicate: same event+role+session+kind is already queued
            {
                LogDebug("Skipped duplicate queued request event=" + request.eventId + " role=" + request.povRole + " session=" + request.sessionId);
                return;
            }

            // Capture this lane's gate now so a later BeginSession/ApplyConcurrency that swaps in
            // fresh gates can't cause this worker to release the wrong semaphore.
            SemaphoreSlim gate = GetOrCreateGate(request);
            LogDebug("Enqueued request event=" + request.eventId + " role=" + request.povRole + " primary=" + LaneLabel(request));
            Task.Run(() => SendWithRetries(request, gate));
        }

        /// <summary>
        /// Drains completed results belonging to the current session from the queue.
        /// Stale results from previous sessions are silently discarded.
        /// Returns true if a valid result was found; false otherwise.
        /// </summary>
        public static bool TryDequeueCompleted(out LlmGenerationResult result)
        {
            long sessionId = Interlocked.Read(ref currentSessionId);
            // Drain the queue entirely; stale results from older sessions are discarded
            while (Completed.TryDequeue(out result))
            {
                if (result != null && result.sessionId == sessionId)
                {
                    return true;
                }
            }

            result = null;
            return false;
        }

        /// <summary>
        /// Outcome of one API lane attempt, used to decide between reporting success, failing over
        /// to the next lane, or dropping the request because the session ended.
        /// </summary>
        private sealed class LaneResult
        {
            public bool Success;
            public string Text;
            public string RawText;
            public string Error;
            public bool TransientFailure;
            public bool Cancelled; // session ended mid-flight — abort entirely, no failover
        }

        /// <summary>Runtime failure count and backoff deadline for one API lane.</summary>
        private sealed class LaneCooldownState
        {
            public int failureCount;
            public DateTime cooldownUntilUtc;
        }

        /// <summary>
        /// Parsed endpoint response split into local-cleaned text for saving and final-answer text
        /// before local length/sentence cleanup for debug inspection.
        /// </summary>
        private sealed class SendResponse
        {
            public string CleanText;
            public string RawText;
        }

        /// <summary>
        /// Tries each configured lane in turn ("on error, use the next model"): the chosen lane
        /// first, then the failover lanes. A lane that returns a permanent error, exhausts its
        /// transient retries, or times out hands off to the next lane. Success reports which lane
        /// actually produced the text. Only when every lane fails is a failed result reported.
        /// </summary>
        private static async Task SendWithRetries(LlmGenerationRequest request, SemaphoreSlim primaryGate)
        {
            string pendingKey = PendingKey(request.eventId, request.povRole, request.sessionId, request.isTitleRequest);
            string lastError = null;

            try
            {
                List<ApiEndpointConfig> targets = BuildAttemptTargets(request);
                LogDebug(
                    "Attempt order event=" + request.eventId
                    + " role=" + request.povRole
                    + " lanes=[" + LaneList(targets) + "]");
                List<bool> readyAtStart = SnapshotLaneReadiness(targets);
                bool hasReadyLaneAtStart = HasReadyLane(readyAtStart);

                for (int t = 0; t < targets.Count; t++)
                {
                    // Point the request at this lane so the gate key, HTTP call, and reported
                    // result all reflect the model we are about to try.
                    ApiEndpointConfig target = targets[t];
                    bool forcedPrimaryAttempt = request.forcePrimaryLane && t == 0;
                    if (!forcedPrimaryAttempt && hasReadyLaneAtStart && !LaneWasReady(readyAtStart, t))
                    {
                        LogDebug("Skipped cooling lane event=" + request.eventId + " role=" + request.povRole + " lane=" + LaneLabel(target));
                        continue;
                    }

                    request.endpointUrl = target.url;
                    request.apiKey = target.apiKey;
                    request.authMode = PawnDiarySettings.NormalizeAuthMode(target.authMode);
                    request.customAuthHeaderName = target.customAuthHeaderName;
                    request.modelName = target.model;
                    request.apiMode = target.apiMode;
                    request.reasoningEffort = PawnDiarySettings.NormalizeReasoningEffort(target.reasoningEffort);
                    string laneLabel = LaneLabel(request);

                    // Reuse the gate captured at enqueue for the first lane; create per-lane gates
                    // for failover lanes. Each is acquired and released as the same reference here.
                    SemaphoreSlim gate = (t == 0) ? primaryGate : GetOrCreateGate(request);

                    try
                    {
                        // Wait our turn before touching the model, so at most N requests are ever
                        // in flight per lane. Time spent queued here does not count against the
                        // per-lane deadline below.
                        await gate.WaitAsync(request.cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return; // session ended while queued
                    }

                    try
                    {
                        // Drop requests whose session ended while they were queued, instead of
                        // wasting a slot on a result nobody will read.
                        if (request.sessionId != Interlocked.Read(ref currentSessionId))
                        {
                            LogDebug("Dropped stale request before attempt event=" + request.eventId + " role=" + request.povRole + " lane=" + laneLabel);
                            return;
                        }

                        LogDebug("Trying lane " + (t + 1) + "/" + targets.Count + " event=" + request.eventId + " role=" + request.povRole + " lane=" + laneLabel);
                        LaneResult outcome = await TryLane(request);
                        if (outcome.Cancelled)
                        {
                            LogDebug("Cancelled request event=" + request.eventId + " role=" + request.povRole + " lane=" + laneLabel);
                            return; // session cancelled mid-flight: drop quietly
                        }

                        if (outcome.Success)
                        {
                            ClearLaneCooldown(request);
                            LogDebug("Lane succeeded event=" + request.eventId + " role=" + request.povRole + " lane=" + laneLabel);
                            Completed.Enqueue(new LlmGenerationResult
                            {
                                    eventId = request.eventId,
                                    povRole = request.povRole,
                                    sessionId = request.sessionId,
                                    success = true,
                                    generatedText = outcome.Text,
                                    rawResponse = outcome.RawText,
                                endpointUrl = request.endpointUrl,
                                modelName = request.modelName,
                                apiMode = request.apiMode,
                                apiKey = request.apiKey,
                                authMode = request.authMode,
                                customAuthHeaderName = request.customAuthHeaderName,
                                isTitleRequest = request.isTitleRequest
                            });
                            return;
                        }

                        lastError = outcome.Error; // this lane failed — fall through to the next one
                        if (outcome.TransientFailure)
                        {
                            MarkLaneCooldown(request, outcome.Error);
                        }

                        LogDebug("Lane failed event=" + request.eventId + " role=" + request.povRole + " lane=" + laneLabel + " error=" + TrimForLog(outcome.Error));
                    }
                    finally
                    {
                        gate.Release();
                    }
                }

                // Every lane failed. Drop quietly if the session ended; otherwise report the last error.
                if (request.cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                Completed.Enqueue(new LlmGenerationResult
                {
                    eventId = request.eventId,
                    povRole = request.povRole,
                    sessionId = request.sessionId,
                    success = false,
                    // Redact: this error is stored on the event and shown in the diary tab, and a
                    // networking message can echo the key-bearing request URL (query-param auth).
                    error = ApiLaneLabels.RedactSecrets(lastError ?? "Unknown network error."),
                    isTitleRequest = request.isTitleRequest
                });
                LogDebug("All lanes failed event=" + request.eventId + " role=" + request.povRole + " lastError=" + TrimForLog(lastError));
            }
            catch (Exception ex)
            {
                // This runs as a fire-and-forget Task.Run, so an unexpected throw outside the per-lane
                // handling (e.g. building the attempt list) would otherwise become an unobserved task
                // exception and leave the entry stuck on "pending" until orphan recovery. Report it as
                // a normal failure instead, unless the session ended (then nobody is listening).
                if (!request.cancellationToken.IsCancellationRequested
                    && request.sessionId == Interlocked.Read(ref currentSessionId))
                {
                    Completed.Enqueue(new LlmGenerationResult
                    {
                        eventId = request.eventId,
                        povRole = request.povRole,
                        sessionId = request.sessionId,
                        success = false,
                        error = ApiLaneLabels.RedactSecrets(ex.Message),
                        isTitleRequest = request.isTitleRequest
                    });
                }

                LogDebug("Unexpected send error event=" + request.eventId + " role=" + request.povRole + " error=" + TrimForLog(ex.Message));
            }
            finally
            {
                PendingKeys.TryRemove(pendingKey, out _);
            }
        }

        /// <summary>
        /// Runs a single lane: the transient-retry loop under a per-lane hard deadline. Returns
        /// success+text, failure+error (so the caller can try the next lane), or Cancelled when the
        /// session ended. Never throws for normal network/timeout outcomes.
        /// </summary>
        private static async Task<LaneResult> TryLane(LlmGenerationRequest request)
        {
            // Hard deadline for this lane (across its retries). Once it fires we stop waiting on the
            // model and free the slot, so a stuck lane can't hold up the queue — we move to the next.
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(request.cancellationToken))
            {
                // Floor at 5s so a bad setting of "0" doesn't instantly cancel everything.
                deadline.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, request.timeoutSeconds)));

                string lastError = null;
                for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    try
                    {
                        SendResponse response = await SendOnce(request, deadline.Token);
                        return new LaneResult { Success = true, Text = response.CleanText, RawText = response.RawText };
                    }
                    catch (LlmPermanentException ex)
                    {
                        return new LaneResult { Success = false, Error = ex.Message }; // won't improve on retry — try the next lane
                    }
                    catch (Exception ex) when (IsTransientException(ex))
                    {
                        lastError = ex.Message;
                        // If the deadline already fired, stop retrying this lane immediately.
                        if (deadline.IsCancellationRequested)
                        {
                            break;
                        }

                        if (attempt < MaxAttempts)
                        {
                            try
                            {
                                await Task.Delay(500 * attempt, deadline.Token); // linear back-off: 0.5s, 1s, ...
                            }
                            catch (OperationCanceledException)
                            {
                                break; // deadline (or session) fired during back-off
                            }
                        }
                    }
                    catch (Exception ex) // unexpected / non-transient
                    {
                        return new LaneResult { Success = false, Error = ex.Message }; // try the next lane
                    }
                }

                // A real session cancellation is dropped; a timeout/exhausted retries fails over.
                if (request.cancellationToken.IsCancellationRequested)
                {
                    return new LaneResult { Cancelled = true };
                }

                return new LaneResult
                {
                    Success = false,
                    TransientFailure = true,
                    Error = deadline.IsCancellationRequested
                        ? "Timed out waiting for the model."
                        : (lastError ?? "Unknown network error.")
                };
            }
        }

        /// <summary>
        /// Builds the ordered list of lanes to attempt: the request's primary lane first, then any
        /// failover lanes, skipping blanks and duplicates. Always contains at least the primary.
        /// Returns fresh copies so mutating the in-flight request never touches the settings objects.
        /// </summary>
        private static List<ApiEndpointConfig> BuildAttemptTargets(LlmGenerationRequest request)
        {
            List<ApiEndpointConfig> targets = new List<ApiEndpointConfig>
            {
                new ApiEndpointConfig(request.endpointUrl, request.apiKey, request.modelName)
                {
                    authMode = PawnDiarySettings.NormalizeAuthMode(request.authMode),
                    customAuthHeaderName = request.customAuthHeaderName,
                    apiMode = request.apiMode,
                    reasoningEffort = PawnDiarySettings.NormalizeReasoningEffort(request.reasoningEffort)
                }
            };

            if (request.failoverTargets != null)
            {
                foreach (ApiEndpointConfig candidate in request.failoverTargets)
                {
                    if (candidate == null || string.IsNullOrWhiteSpace(candidate.url) || string.IsNullOrWhiteSpace(candidate.model))
                    {
                        LogDebug("Skipped blank failover lane while building attempt order event=" + request.eventId + " role=" + request.povRole);
                        continue;
                    }

                    bool duplicate = false;
                    foreach (ApiEndpointConfig existing in targets)
                    {
                        if (SameAttemptLane(existing, candidate))
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                    {
                        targets.Add(candidate.Copy());
                    }
                    else
                    {
                        LogDebug("Skipped duplicate failover lane while building attempt order event=" + request.eventId + " role=" + request.povRole + " lane=" + LaneLabel(candidate));
                    }
                }
            }

            return targets;
        }

        /// <summary>
        /// Sends a single HTTP request to the LLM endpoint and parses the response.
        /// Throws <see cref="LlmTransientException"/> for retryable HTTP errors and
        /// <see cref="LlmPermanentException"/> for non-retryable ones.
        /// </summary>
        private static async Task<SendResponse> SendOnce(LlmGenerationRequest request, CancellationToken cancellationToken)
        {
            string requestUrl = ApiRequestAuth.ApplyQueryAuth(
                EndpointUtility.BuildGenerationUrl(request.endpointUrl, request.apiMode),
                request.apiKey,
                request.authMode);
            using (HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, requestUrl))
            {
                ApiRequestAuth.ApplyHeaders(message, request.apiKey, request.authMode, request.customAuthHeaderName);

                message.Content = new StringContent(BuildRequestJson(request), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await Client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    string responseJson = await ReadCappedResponseString(response.Content, MaxResponseBytes, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        string error = $"HTTP {(int)response.StatusCode}: {TrimForLog(responseJson)}";
                        if (IsTransientStatusCode((int)response.StatusCode))
                        {
                            throw new LlmTransientException(error);
                        }

                        throw new LlmPermanentException(error);
                    }

                    Dictionary<string, object> responseRoot = LlmResponseParser.ParseResponseRoot(responseJson);
                    LlmResponseMode responseMode = ResponseModeFor(request.apiMode);
                    string generatedText = LlmResponseParser.ParseGeneratedText(responseRoot, responseMode);
                    string providerError = LlmResponseParser.ExtractProviderError(responseRoot, responseMode, !string.IsNullOrWhiteSpace(generatedText));
                    if (!string.IsNullOrWhiteSpace(providerError))
                    {
                        throw new LlmPermanentException(providerError);
                    }

                    string visibleText = LlmResponseParser.StripReasoningTextBlocks(generatedText);
                    if (string.IsNullOrWhiteSpace(visibleText))
                    {
                        providerError = LlmResponseParser.ExtractProviderError(responseRoot, responseMode, false);
                        throw new LlmPermanentException(string.IsNullOrWhiteSpace(providerError)
                            ? "The endpoint returned no message content."
                            : providerError);
                    }

                    // Some RP-tuned models ignore max_tokens and return very long entries anyway.
                    // Enforce a local hard cap (by whitespace-token count) so saved diary events
                    // never exceed the request's token budget, even when the endpoint misbehaves.
                    // Some endpoints also stop exactly at max_tokens, returning a mid-sentence
                    // fragment that is already under our local cap; clean that up for main diary
                    // text too. Titles are exempt because they should not end with sentence
                    // punctuation.
                    DiaryResponsePlan responsePlan = DiaryResponsePostprocessor.ApplySuccess(
                        visibleText,
                        ResponseRulesForRequest(request));

                    return new SendResponse
                    {
                        RawText = responsePlan.rawVisibleResponse,
                        CleanText = responsePlan.generatedText
                    };
                }
            }
        }

        /// <summary>
        /// Reads an HTTP body with a hard byte cap so a bad local server cannot allocate an unbounded
        /// string inside RimWorld. The cap is larger than any useful diary response.
        /// </summary>
        private static async Task<string> ReadCappedResponseString(HttpContent content, int maxBytes, CancellationToken cancellationToken)
        {
            if (content == null)
            {
                return string.Empty;
            }

            using (Stream stream = await content.ReadAsStreamAsync())
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[8192];
                int total = 0;
                while (true)
                {
                    int read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken);
                    if (read <= 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > maxBytes)
                    {
                        throw new LlmPermanentException("The endpoint returned a response that was too large.");
                    }

                    buffer.Write(chunk, 0, read);
                }

                return Encoding.UTF8.GetString(buffer.ToArray());
            }
        }

        private static DiaryResponseRules ResponseRulesForRequest(LlmGenerationRequest request)
        {
            DiaryResponseRules rules = request.responseRules
                ?? DiaryResponseRules.ForRequest(request.eventId, request.povRole, request.isTitleRequest, request.maxTokens);
            if (string.IsNullOrWhiteSpace(rules.eventId))
            {
                rules.eventId = request.eventId;
            }
            if (string.IsNullOrWhiteSpace(rules.targetRole))
            {
                rules.targetRole = request.povRole;
            }
            rules.isTitle = request.isTitleRequest;
            if (rules.maxTokens <= 0)
            {
                rules.maxTokens = request.maxTokens;
            }
            if (request.isTitleRequest)
            {
                rules.trimIncompleteSentence = false;
            }
            return rules;
        }

        /// <summary>
        /// Adapts the transport request into the pure request serializer's primitive snapshot.
        /// </summary>
        private static string BuildRequestJson(LlmGenerationRequest request)
        {
            return LlmRequestJsonBuilder.Build(new LlmRequestJsonInput
            {
                apiMode = request.apiMode,
                modelName = request.modelName,
                systemPrompt = request.systemPrompt,
                rawText = request.rawText,
                reasoningEffort = request.reasoningEffort,
                maxTokens = request.maxTokens,
                temperature = request.temperature
            });
        }

        /// <summary>
        /// Adapts the saved/settings compatibility enum to the pure parser's transport-free enum.
        /// </summary>
        private static LlmResponseMode ResponseModeFor(ApiCompatibilityMode mode)
        {
            switch (mode)
            {
                case ApiCompatibilityMode.OpenAIResponses:
                    return LlmResponseMode.OpenAIResponses;
                default:
                    return LlmResponseMode.OpenAIChatCompletions;
            }
        }

        private static bool SameAttemptLane(ApiEndpointConfig left, ApiEndpointConfig right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return ApiLaneIdentity.ForAttempt(left.url, left.model, left.apiMode, left.authMode, left.customAuthHeaderName, left.apiKey)
                == ApiLaneIdentity.ForAttempt(right.url, right.model, right.apiMode, right.authMode, right.customAuthHeaderName, right.apiKey);
        }

        /// <summary>
        /// Truncates a response body to 180 characters for inclusion in error messages,
        /// keeping logs readable without dumping the entire model output.
        /// </summary>
        private static string TrimForLog(string value)
        {
            return ApiLaneLabels.TrimForLog(value);
        }

        /// <summary>
        /// Determines whether an exception represents a transient failure that is worth
        /// retrying (network errors, timeouts, rate-limits). Non-transient exceptions
        /// cause immediate failure without retry.
        /// </summary>
        private static bool IsTransientException(Exception exception)
        {
            return exception is LlmTransientException
                || exception is HttpRequestException
                || exception is TaskCanceledException
                || exception is OperationCanceledException;
        }

        /// <summary>
        /// HTTP status codes considered transient: 429 (rate-limited) and 5xx (server errors).
        /// All others (4xx except 429) are treated as permanent client errors.
        /// </summary>
        private static bool IsTransientStatusCode(int statusCode)
        {
            return statusCode == 429 || statusCode >= 500;
        }

        /// <summary>Composes a deduplication key from the tuple that uniquely identifies an
        /// in-flight request. The <c>isTitleRequest</c> bit keeps title follow-ups from colliding
        /// with the main entry that produced them — they share event+role, but a session should
        /// be allowed to have both running at the same logical time (even if, in practice, the
        /// title is queued only after the main entry has finished).</summary>
        private static string PendingKey(string eventId, string povRole, long sessionId, bool isTitleRequest)
        {
            return sessionId + "|" + eventId + "|" + povRole + "|" + (isTitleRequest ? "title" : "main");
        }

        /// <summary>
        /// Writes one-line LLM lane diagnostics to the RimWorld log. These are intentionally
        /// English debug logs and never include API keys.
        ///
        /// Thread-safety: most callers run on background worker threads (the <c>Task.Run</c> send
        /// loop and its <c>await</c> continuations). <c>Log.Message</c> is **main-thread only** — it
        /// mutates the queue the in-game log window enumerates on the GUI thread, so an off-thread
        /// call races that enumeration and throws "Collection was modified". We therefore log
        /// directly only when already on the main thread, and otherwise defer the line to
        /// <see cref="FlushPendingLogs"/>, which the main-thread tick drains. (Same reason
        /// <c>.Translate()</c> is kept off these threads — see AGENTS.md.)
        /// </summary>
        private static void LogDebug(string message)
        {
            if (!debugLoggingEnabled)
            {
                return;
            }

            if (UnityData.IsInMainThread)
            {
                Log.Message("[PawnDiary debug] " + message);
            }
            else
            {
                PendingLogs.Enqueue(message);
            }
        }

        /// <summary>
        /// Writes any debug lines queued by background workers to the RimWorld log. Must be called
        /// on the main thread (the game tick does so); see <see cref="LogDebug"/> for why.
        /// </summary>
        public static void FlushPendingLogs()
        {
            string message;
            if (!debugLoggingEnabled)
            {
                while (PendingLogs.TryDequeue(out message))
                {
                }

                return;
            }

            while (PendingLogs.TryDequeue(out message))
            {
                Log.Message("[PawnDiary debug] " + message);
            }
        }

        private static string LaneList(List<ApiEndpointConfig> lanes)
        {
            if (lanes == null || lanes.Count == 0)
            {
                return "none";
            }

            List<string> labels = new List<string>();
            foreach (ApiEndpointConfig lane in lanes)
            {
                labels.Add(LaneLabel(lane));
            }

            return string.Join(" | ", labels.ToArray());
        }

        private static string LaneLabel(ApiEndpointConfig lane)
        {
            if (lane == null)
            {
                return "<null>";
            }

            return ApiLaneLabels.Label(lane.url, lane.model, lane.apiMode);
        }

        private static string LaneLabel(LlmGenerationRequest request)
        {
            if (request == null)
            {
                return "<null>";
            }

            return ApiLaneLabels.Label(request.endpointUrl, request.modelName, request.apiMode);
        }

        /// <summary>Drains all results from the completed queue, discarding them. Used on session transitions.</summary>
        private static void ClearCompleted()
        {
            LlmGenerationResult ignored;
            while (Completed.TryDequeue(out ignored))
            {
            }
        }

        /// <summary>
        /// Thrown for HTTP errors that may resolve on retry (e.g. 429, 503).
        /// Caught by <see cref="SendWithRetries"/> to trigger the retry loop.
        /// </summary>
        private class LlmTransientException : Exception
        {
            public LlmTransientException(string message)
                : base(message)
            {
            }
        }

        /// <summary>
        /// Thrown for HTTP errors that will not resolve on retry (e.g. 401, 404, 400).
        /// Causes <see cref="SendWithRetries"/> to report failure immediately.
        /// </summary>
        private class LlmPermanentException : Exception
        {
            public LlmPermanentException(string message)
                : base(message)
            {
            }
        }
    }
}

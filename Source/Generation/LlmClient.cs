// Async HTTP client for the LLM endpoint. A queue + concurrency gate (SemaphoreSlim) caps how
// many requests are in flight; each has a hard deadline that purges stuck requests; transient
// errors retry. Finished results are drained by DiaryGameComponent.GameComponentTick. `async
// Task` ≈ Promise and `CancellationToken` ≈ AbortSignal — see AGENTS.md ("async Task").
// The concurrency logic below is already commented inline; start there for details.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Describes a single chat-completion request to an OpenAI-compatible LLM endpoint.
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

        /// <summary>Optional system prompt that sets persona or formatting rules for the model.</summary>
        public string systemPrompt;

        /// <summary>Raw narrative text describing the game event, sent as the user message.</summary>
        public string rawText;

        /// <summary>Full base URL of the OpenAI-compatible API (e.g. "https://api.openai.com/v1").</summary>
        public string endpointUrl;

        /// <summary>Model identifier accepted by the API (e.g. "gpt-4o-mini").</summary>
        public string modelName;

        /// <summary>Bearer token for API authentication; may be empty for local endpoints.</summary>
        public string apiKey;

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
        /// <see cref="DiaryEvent.SetTitle"/> / <see cref="DiaryEvent.MarkTitleFailed"/>. Defaults
        /// to false so the existing main-entry path is unchanged.</summary>
        public bool isTitleRequest;
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

        /// <summary>Human-readable error message when <see cref="success"/> is false.</summary>
        public string error;

        /// <summary>Base URL of the API lane that actually produced the text — may differ from the
        /// primary lane after failover. Used to keep the recorded LLM meta (and recipient pinning) accurate.</summary>
        public string endpointUrl;

        /// <summary>Model of the API lane that actually produced the text (see <see cref="endpointUrl"/>).</summary>
        public string modelName;

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
        private static ConcurrentDictionary<string, SemaphoreSlim> sendGates = new ConcurrentDictionary<string, SemaphoreSlim>();

        // Rotates over the configured API lanes so successive requests spread across them.
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
            CancellationTokenSource oldCancellation = sessionCancellation;
            sessionCancellation = new CancellationTokenSource();
            sendGates = new ConcurrentDictionary<string, SemaphoreSlim>(); // fresh per-lane gates at the current limit
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
            sendGates = new ConcurrentDictionary<string, SemaphoreSlim>();
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

        /// <summary>
        /// Builds the gate key identifying an API lane: its endpoint plus model. Requests
        /// sharing both share a lane (and therefore its in-flight cap).
        /// </summary>
        private static string GateKey(LlmGenerationRequest request)
        {
            string endpoint = (request.endpointUrl ?? string.Empty).Trim().ToLowerInvariant();
            string model = (request.modelName ?? string.Empty).Trim();
            return endpoint + "\n" + model;
        }

        /// <summary>
        /// Gets the gate for a lane, creating it at the current per-lane limit on first use.
        /// </summary>
        private static SemaphoreSlim GetOrCreateGate(LlmGenerationRequest request)
        {
            string key = GateKey(request);
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
            public string Error;
            public bool Cancelled; // session ended mid-flight — abort entirely, no failover
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

                for (int t = 0; t < targets.Count; t++)
                {
                    // Point the request at this lane so the gate key, HTTP call, and reported
                    // result all reflect the model we are about to try.
                    ApiEndpointConfig target = targets[t];
                    request.endpointUrl = target.url;
                    request.apiKey = target.apiKey;
                    request.modelName = target.model;
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
                            LogDebug("Lane succeeded event=" + request.eventId + " role=" + request.povRole + " lane=" + laneLabel);
                            Completed.Enqueue(new LlmGenerationResult
                            {
                                eventId = request.eventId,
                                povRole = request.povRole,
                                sessionId = request.sessionId,
                                success = true,
                                generatedText = outcome.Text,
                                endpointUrl = request.endpointUrl,
                                modelName = request.modelName,
                                isTitleRequest = request.isTitleRequest
                            });
                            return;
                        }

                        lastError = outcome.Error; // this lane failed — fall through to the next one
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
                    error = lastError ?? "Unknown network error.",
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
                        error = ex.Message,
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
                        string generatedText = await SendOnce(request, deadline.Token);
                        return new LaneResult { Success = true, Text = generatedText };
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
                        if (string.Equals(existing.url, candidate.url, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(existing.model, candidate.model, StringComparison.Ordinal))
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                    {
                        targets.Add(new ApiEndpointConfig(candidate.url, candidate.apiKey, candidate.model));
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
        private static async Task<string> SendOnce(LlmGenerationRequest request, CancellationToken cancellationToken)
        {
            using (HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, EndpointUtility.BuildChatCompletionsUrl(request.endpointUrl)))
            {
                if (!string.IsNullOrWhiteSpace(request.apiKey))
                {
                    message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", request.apiKey.Trim());
                }

                message.Content = new StringContent(BuildRequestJson(request), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await Client.SendAsync(message, cancellationToken))
                {
                        string responseJson = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode)
                        {
                            string error = $"HTTP {(int)response.StatusCode}: {TrimForLog(responseJson)}";
                            if (IsTransientStatusCode((int)response.StatusCode))
                            {
                                throw new LlmTransientException(error);
                            }

                            throw new LlmPermanentException(error);
                        }

                        string generatedText = ParseGeneratedText(responseJson);
                        if (string.IsNullOrWhiteSpace(generatedText))
                        {
                            throw new LlmPermanentException("The endpoint returned no message content.");
                        }

                    // Some RP-tuned models ignore max_tokens and return very long entries anyway.
                    // Enforce a local hard cap (by whitespace-token count) so saved diary events
                    // never exceed the request's token budget, even when the endpoint misbehaves.
                    return TrimToMaxTokens(generatedText, request.maxTokens);
                }
            }
        }

        /// <summary>
        /// Enforces a hard upper bound on response length by counting whitespace-delimited tokens.
        /// This is a local safety net for endpoints/models that do not honor max_tokens.
        /// </summary>
        private static string TrimToMaxTokens(string text, int maxTokens)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim();
            if (maxTokens <= 0)
            {
                return trimmed;
            }

            bool insideToken = false;
            int tokenCount = 0;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (char.IsWhiteSpace(c))
                {
                    insideToken = false;
                    continue;
                }

                if (insideToken)
                {
                    continue;
                }

                insideToken = true;
                tokenCount++;
                if (tokenCount > maxTokens)
                {
                    string capped = trimmed.Substring(0, i).TrimEnd();
                    return string.IsNullOrEmpty(capped) ? string.Empty : capped + "...";
                }
            }

            return trimmed;
        }

        /// <summary>
        /// Builds the JSON body for a chat-completion request using manual concatenation
        /// rather than System.Text.Json, which may not be available in all RimWorld runtimes.
        /// </summary>
        private static string BuildRequestJson(LlmGenerationRequest request)
        {
            return "{"
                + "\"model\":\"" + JsonEscape(request.modelName) + "\","
                + "\"messages\":[" + BuildMessagesJson(request) + "],"
                + "\"temperature\":" + request.temperature.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"max_tokens\":" + request.maxTokens
                + "}";
        }

        /// <summary>
        /// Constructs the JSON array of message objects. Prepends a system message when
        /// the request includes one so the model adopts the intended persona.
        /// </summary>
        private static string BuildMessagesJson(LlmGenerationRequest request)
        {
            string userMessage = "{\"role\":\"user\",\"content\":\"" + JsonEscape(request.rawText) + "\"}";
            if (string.IsNullOrWhiteSpace(request.systemPrompt))
            {
                return userMessage;
            }

            return "{\"role\":\"system\",\"content\":\"" + JsonEscape(request.systemPrompt.Trim()) + "\"},"
                + userMessage;
        }

        /// <summary>
        /// Extracts the generated text from an OpenAI-style chat completion response.
        /// Supports the standard <c>choices[0].message.content</c> shape.
        /// </summary>
        private static string ParseGeneratedText(string json)
        {
            Dictionary<string, object> root = MiniJson.Deserialize(json ?? string.Empty) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue("choices", out object choicesObject))
            {
                return null;
            }

            object[] choices = choicesObject as object[];
            if (choices == null || choices.Length == 0)
            {
                return null;
            }

            Dictionary<string, object> firstChoice = choices[0] as Dictionary<string, object>;
            if (firstChoice == null)
            {
                return null;
            }

            if (firstChoice.TryGetValue("message", out object messageObject))
            {
                Dictionary<string, object> message = messageObject as Dictionary<string, object>;
                if (message != null && message.TryGetValue("content", out object contentObject))
                {
                    return contentObject as string;
                }
            }

            return null;
        }

        /// <summary>
        /// Minimal JSON string escaper — handles the characters most likely to appear in
        /// free-form narrative text (backslash, quote, newlines, tabs, control chars) and
        /// produces Unicode escapes for the rest. Avoids a full JSON library dependency.
        /// </summary>
        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length + 16); // pre-allocate slight overshoot to reduce copies
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(c))
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Truncates a response body to 180 characters for inclusion in error messages,
        /// keeping logs readable without dumping the entire model output.
        /// </summary>
        private static string TrimForLog(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = value.Trim();
            return value.Length <= 180 ? value : value.Substring(0, 180) + "...";
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

            return (string.IsNullOrWhiteSpace(lane.model) ? "<blank-model>" : lane.model)
                + " @ "
                + (string.IsNullOrWhiteSpace(lane.url) ? "<blank-url>" : EndpointUtility.BuildChatCompletionsUrl(lane.url));
        }

        private static string LaneLabel(LlmGenerationRequest request)
        {
            if (request == null)
            {
                return "<null>";
            }

            return (string.IsNullOrWhiteSpace(request.modelName) ? "<blank-model>" : request.modelName)
                + " @ "
                + (string.IsNullOrWhiteSpace(request.endpointUrl) ? "<blank-url>" : EndpointUtility.BuildChatCompletionsUrl(request.endpointUrl));
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

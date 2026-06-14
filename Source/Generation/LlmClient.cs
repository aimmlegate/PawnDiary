// Async HTTP client for the LLM endpoint. A queue + concurrency gate (SemaphoreSlim) caps how
// many requests are in flight; each has a hard deadline that purges stuck requests; transient
// errors retry. Finished results are drained by DiaryGameComponent.GameComponentTick. `async
// Task` ≈ Promise and `CancellationToken` ≈ AbortSignal — see CSHARP-NOTES.md ("async Task").
// The concurrency logic below is already commented inline; start there for details.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Text;
using System.Threading.Tasks;

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

        /// <summary>Per-request wall-clock timeout in seconds. Also used as an overall deadline across retries.</summary>
        public int timeoutSeconds;

        /// <summary>Maximum number of tokens the model may generate in its response.</summary>
        public int maxTokens;

        /// <summary>Sampling temperature (0.0–2.0). Higher values produce more random output.</summary>
        public float temperature;

        /// <summary>Linked to the session cancellation source so stale requests are aborted on game load.</summary>
        public CancellationToken cancellationToken;
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

        // Gates how many requests may actually be in flight (awaiting a response or
        // error from the model) at once. Everything else waits in line rather than
        // being dropped. The limit is configurable; this is just the pre-session value.
        private static SemaphoreSlim sendGate = new SemaphoreSlim(4, MaxConcurrencyCap);

        /// <summary>
        /// Starts a new session, cancelling all in-flight requests from the previous one
        /// and resetting the concurrency gate and result queue. Called on game load.
        /// </summary>
        public static void BeginSession()
        {
            CancellationTokenSource oldCancellation = sessionCancellation;
            sessionCancellation = new CancellationTokenSource();
            sendGate = new SemaphoreSlim(ResolveConcurrency(), MaxConcurrencyCap);
            Interlocked.Increment(ref currentSessionId); // bump so stale results are ignored
            ClearCompleted();
            oldCancellation.Cancel(); // abort all in-flight requests from previous session
        }

        /// <summary>
        /// Replaces the concurrency gate with a fresh semaphore reflecting the latest
        /// user setting. Safe to call at any time — in-flight workers hold their own
        /// gate reference and release that one.
        /// </summary>
        public static void ApplyConcurrency()
        {
            // Safe to swap at any time: in-flight workers hold their own gate reference
            // and release that one, so they never touch this new semaphore.
            sendGate = new SemaphoreSlim(ResolveConcurrency(), MaxConcurrencyCap);
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
        /// Cancels all in-flight requests for the current session without starting a new one.
        /// Used when the player manually triggers a cancel.
        /// </summary>
        public static void CancelSession()
        {
            sessionCancellation.Cancel();
            ClearCompleted();
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
            string pendingKey = PendingKey(request.eventId, request.povRole, request.sessionId);
            if (!PendingKeys.TryAdd(pendingKey, 0)) // deduplicate: same event+role+session is already queued
            {
                return;
            }

            // Capture the gate for this session so a later BeginSession that swaps in a
            // new gate can't cause this worker to release the wrong semaphore.
            SemaphoreSlim gate = sendGate;
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
        /// Core async loop: acquires a semaphore slot, applies a hard deadline across all
        /// retry attempts, and retries transient errors up to <see cref="MaxAttempts"/> times
        /// with linear back-off. Permanent errors or exhausted retries produce a failed result.
        /// </summary>
        private static async Task SendWithRetries(LlmGenerationRequest request, SemaphoreSlim gate)
        {
            string lastError = null;
            string pendingKey = PendingKey(request.eventId, request.povRole, request.sessionId);

            try
            {
                // Wait our turn before touching the model, so at most N requests are
                // ever in flight. Time spent queued here does not count against the
                // per-request deadline below.
                await gate.WaitAsync(request.cancellationToken);
            }
            catch (OperationCanceledException)
            {
                PendingKeys.TryRemove(pendingKey, out _);
                return;
            }

            try
            {
                // Drop requests whose session ended while they were queued, instead of
                // wasting a slot on a result nobody will read.
                if (request.sessionId != Interlocked.Read(ref currentSessionId))
                {
                    return;
                }

                // Hard deadline for the whole request (across retries). Once it fires we
                // stop waiting on the model and free the slot, so a stuck request can't
                // hold up the queue.
                using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(request.cancellationToken))
                {
                    // Floor at 5s so a bad setting of "0" doesn't instantly cancel everything
                    deadline.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, request.timeoutSeconds)));

                    for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                    {
                        try
                        {
                            string generatedText = await SendOnce(request, deadline.Token);
                            Completed.Enqueue(new LlmGenerationResult
                            {
                                eventId = request.eventId,
                                povRole = request.povRole,
                                sessionId = request.sessionId,
                                success = true,
                                generatedText = generatedText
                            });
                            return;
                        }
                        catch (LlmPermanentException ex)
                        {
                            Completed.Enqueue(new LlmGenerationResult
                            {
                                eventId = request.eventId,
                                povRole = request.povRole,
                                sessionId = request.sessionId,
                                success = false,
                                error = ex.Message
                            });
                            return;
                        }
                        catch (Exception ex) when (IsTransientException(ex))
                        {
                            lastError = ex.Message;
                            // If the overall deadline already fired, stop retrying immediately
                            if (deadline.IsCancellationRequested)
                            {
                                break;
                            }

                            if (attempt < MaxAttempts)
                            {
                                await Task.Delay(500 * attempt, deadline.Token); // linear back-off: 0.5s, 1s, ...
                            }
                        }
                        catch (Exception ex) // unexpected / non-transient: fail immediately
                        {
                            Completed.Enqueue(new LlmGenerationResult
                            {
                                eventId = request.eventId,
                                povRole = request.povRole,
                                sessionId = request.sessionId,
                                success = false,
                                error = ex.Message
                            });
                            return;
                        }
                    }

                    // Session ended mid-flight: drop quietly, the result is stale.
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
                        error = deadline.IsCancellationRequested
                            ? "Timed out waiting for the model."
                            : (lastError ?? "Unknown network error.")
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Reaches here if the deadline fired during a backoff delay. A real
                // session cancellation is dropped; a timeout is reported as a failure.
                if (!request.cancellationToken.IsCancellationRequested)
                {
                    Completed.Enqueue(new LlmGenerationResult
                    {
                        eventId = request.eventId,
                        povRole = request.povRole,
                        sessionId = request.sessionId,
                        success = false,
                        error = "Timed out waiting for the model."
                    });
                }
            }
            finally
            {
                gate.Release();
                PendingKeys.TryRemove(pendingKey, out _);
            }
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

                    return generatedText.Trim();
                }
            }
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
        /// Supports both the standard <c>choices[0].message.content</c> shape and the
        /// legacy <c>choices[0].text</c> shape used by older completions endpoints.
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

            if (firstChoice.TryGetValue("text", out object textObject))
            {
                return textObject as string;
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

        /// <summary>Composes a deduplication key from the triple that uniquely identifies an in-flight request.</summary>
        private static string PendingKey(string eventId, string povRole, long sessionId)
        {
            return sessionId + "|" + eventId + "|" + povRole;
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

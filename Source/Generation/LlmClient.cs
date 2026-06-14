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
    public class LlmGenerationRequest
    {
        public string eventId;
        public string povRole;
        public long sessionId;
        public string systemPrompt;
        public string rawText;
        public string endpointUrl;
        public string modelName;
        public string apiKey;
        public int timeoutSeconds;
        public int maxTokens;
        public float temperature;
        public CancellationToken cancellationToken;
    }

    public class LlmGenerationResult
    {
        public string eventId;
        public string povRole;
        public long sessionId;
        public bool success;
        public string generatedText;
        public string error;
    }

    public static class LlmClient
    {
        private const int MaxAttempts = 3;
        private const int MaxConcurrencyCap = 16;
        private static readonly ConcurrentQueue<LlmGenerationResult> Completed = new ConcurrentQueue<LlmGenerationResult>();
        private static readonly ConcurrentDictionary<string, byte> PendingKeys = new ConcurrentDictionary<string, byte>();
        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        private static CancellationTokenSource sessionCancellation = new CancellationTokenSource();
        private static long currentSessionId;

        // Gates how many requests may actually be in flight (awaiting a response or
        // error from the model) at once. Everything else waits in line rather than
        // being dropped. The limit is configurable; this is just the pre-session value.
        private static SemaphoreSlim sendGate = new SemaphoreSlim(4, MaxConcurrencyCap);

        public static void BeginSession()
        {
            CancellationTokenSource oldCancellation = sessionCancellation;
            sessionCancellation = new CancellationTokenSource();
            sendGate = new SemaphoreSlim(ResolveConcurrency(), MaxConcurrencyCap);
            Interlocked.Increment(ref currentSessionId);
            ClearCompleted();
            oldCancellation.Cancel();
        }

        public static void ApplyConcurrency()
        {
            // Safe to swap at any time: in-flight workers hold their own gate reference
            // and release that one, so they never touch this new semaphore.
            sendGate = new SemaphoreSlim(ResolveConcurrency(), MaxConcurrencyCap);
        }

        private static int ResolveConcurrency()
        {
            int requested = PawnDiaryMod.Settings != null ? PawnDiaryMod.Settings.maxConcurrentRequests : 4;
            if (requested < 1)
            {
                return 1;
            }

            return requested > MaxConcurrencyCap ? MaxConcurrencyCap : requested;
        }

        public static void CancelSession()
        {
            sessionCancellation.Cancel();
            ClearCompleted();
        }

        public static void Enqueue(LlmGenerationRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.eventId) || string.IsNullOrWhiteSpace(request.povRole))
            {
                return;
            }

            request.sessionId = Interlocked.Read(ref currentSessionId);
            request.cancellationToken = sessionCancellation.Token;
            string pendingKey = PendingKey(request.eventId, request.povRole, request.sessionId);
            if (!PendingKeys.TryAdd(pendingKey, 0))
            {
                return;
            }

            // Capture the gate for this session so a later BeginSession that swaps in a
            // new gate can't cause this worker to release the wrong semaphore.
            SemaphoreSlim gate = sendGate;
            Task.Run(() => SendWithRetries(request, gate));
        }

        public static bool TryDequeueCompleted(out LlmGenerationResult result)
        {
            long sessionId = Interlocked.Read(ref currentSessionId);
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
                            if (deadline.IsCancellationRequested)
                            {
                                break;
                            }

                            if (attempt < MaxAttempts)
                            {
                                await Task.Delay(500 * attempt, deadline.Token);
                            }
                        }
                        catch (Exception ex)
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

        private static string BuildRequestJson(LlmGenerationRequest request)
        {
            return "{"
                + "\"model\":\"" + JsonEscape(request.modelName) + "\","
                + "\"messages\":[" + BuildMessagesJson(request) + "],"
                + "\"temperature\":" + request.temperature.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"max_tokens\":" + request.maxTokens
                + "}";
        }

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

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length + 16);
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

        private static string TrimForLog(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = value.Trim();
            return value.Length <= 180 ? value : value.Substring(0, 180) + "...";
        }

        private static bool IsTransientException(Exception exception)
        {
            return exception is LlmTransientException
                || exception is HttpRequestException
                || exception is TaskCanceledException
                || exception is OperationCanceledException;
        }

        private static bool IsTransientStatusCode(int statusCode)
        {
            return statusCode == 429 || statusCode >= 500;
        }

        private static string PendingKey(string eventId, string povRole, long sessionId)
        {
            return sessionId + "|" + eventId + "|" + povRole;
        }

        private static void ClearCompleted()
        {
            LlmGenerationResult ignored;
            while (Completed.TryDequeue(out ignored))
            {
            }
        }

        private class LlmTransientException : Exception
        {
            public LlmTransientException(string message)
                : base(message)
            {
            }
        }

        private class LlmPermanentException : Exception
        {
            public LlmPermanentException(string message)
                : base(message)
            {
            }
        }
    }
}

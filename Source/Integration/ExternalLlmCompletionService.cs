// Adapter-facing one-shot LLM completion service. The public API (PawnDiaryApi.RequestLlmCompletion /
// GetLlmCompletionResult) is a thin validated facade over this; the real work — handle bookkeeping,
// endpoint selection, firing the background request, and marshalling the result back — lives here.
//
// Shape mirrors the entry-handle/poll idiom already used for events (SubmitEventWithHandle +
// GetEntryStatus): the caller gets an int handle, then polls until the slot is terminal. There is no
// callback delegate, so a reflection-only ("soft") integration can drive it without wiring up events.
//
// Threading: SendSingleCompletion runs on a background thread and its continuation writes the slot off
// the main thread, while polling reads on the main thread — so every slot access is under one lock.
//
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;

namespace PawnDiary.Integration
{
    /// <summary>
    /// Status of an adapter's one-shot LLM completion request. <see cref="Unknown"/> means the handle is
    /// not (or no longer) tracked — either never issued, or already polled to a terminal state.
    /// </summary>
    public enum LlmCompletionStatus
    {
        Unknown,
        Pending,
        Succeeded,
        Failed
    }

    /// <summary>
    /// A one-shot LLM completion request from another mod. Only <see cref="userText"/> is required; the
    /// lane defaults to the first active endpoint and the token budget is clamped defensively.
    /// </summary>
    public sealed class ExternalLlmCompletionRequest
    {
        /// <summary>Requesting mod's sourceId, for attribution in logs. Required.</summary>
        public string sourceId = string.Empty;

        /// <summary>Index into the player's configured lanes, or &lt; 0 for "first active lane".</summary>
        public int laneIndex = -1;

        /// <summary>System/instruction prompt (optional).</summary>
        public string systemPrompt = string.Empty;

        /// <summary>The text to transform/complete. Required.</summary>
        public string userText = string.Empty;

        /// <summary>Requested max response tokens; clamped to a safe band by the service.</summary>
        public int maxTokens = 200;
    }

    /// <summary>Poll result for a one-shot LLM completion handle. Never null from the public API.</summary>
    public sealed class LlmCompletionResult
    {
        public LlmCompletionStatus status = LlmCompletionStatus.Unknown;

        /// <summary>Cleaned response text when <see cref="status"/> is Succeeded; otherwise empty.</summary>
        public string text = string.Empty;

        /// <summary>Short error message when <see cref="status"/> is Failed; otherwise empty.</summary>
        public string error = string.Empty;
    }

    /// <summary>
    /// Process-global store for in-flight adapter LLM completions. Internal: the public contract is
    /// <see cref="PawnDiaryApi.RequestLlmCompletion"/> / <see cref="PawnDiaryApi.GetLlmCompletionResult"/>.
    /// </summary>
    internal static class ExternalLlmCompletionService
    {
        // Defensive schema limits (untrusted adapter input), not tuning policy.
        private const int MaxInputChars = 4000;
        private const int MinMaxTokens = 16;
        private const int MaxMaxTokens = 600;
        // Admission cap: an adapter that stops polling must not leak slots OR keep starting paid work.
        // Once full, Begin rejects new requests until callers consume terminal handles or a game reset
        // clears the session. Pending work is never silently evicted while its HTTP request keeps running.
        private const int MaxTrackedRequests = 64;

        private static readonly object gate = new object();
        private static readonly Dictionary<int, LlmCompletionResult> results = new Dictionary<int, LlmCompletionResult>();
        private static int nextHandle;

        /// <summary>
        /// Validates and starts a one-shot completion. Returns a handle &gt; 0 on accept, or 0 when the
        /// request is unusable (blank input, no usable lane, or settings missing). Main-thread only;
        /// never throws.
        /// </summary>
        public static int Begin(ExternalLlmCompletionRequest request, PawnDiarySettings settings)
        {
            if (request == null || settings == null)
            {
                return 0;
            }

            string userText = Cap(request.userText);
            if (string.IsNullOrWhiteSpace(userText))
            {
                return 0;
            }

            ApiEndpointConfig endpoint = ResolveEndpoint(settings, request.laneIndex);
            if (endpoint == null)
            {
                return 0;
            }

            // Detach the lane snapshot so a settings edit mid-flight cannot mutate this request.
            ApiEndpointConfig laneSnapshot = endpoint.Copy();
            string systemPrompt = Cap(request.systemPrompt);
            int maxTokens = Clamp(request.maxTokens, MinMaxTokens, MaxMaxTokens);
            int timeoutSeconds = settings.timeoutSeconds;
            float temperature = settings.temperature;

            int handle;
            lock (gate)
            {
                if (results.Count >= MaxTrackedRequests)
                {
                    return 0;
                }

                handle = AllocateHandle();
                results[handle] = new LlmCompletionResult { status = LlmCompletionStatus.Pending };
            }

            RunAsync(handle, laneSnapshot, systemPrompt, userText, maxTokens, timeoutSeconds, temperature);
            return handle;
        }

        /// <summary>
        /// Clears every tracked handle for a newly constructed Game. The linked LLM session token cancels
        /// the actual HTTP work; clearing here ensures late continuations cannot surface in the next game.
        /// </summary>
        public static void ResetSession()
        {
            lock (gate)
            {
                results.Clear();
            }
        }

        /// <summary>Returns the defensively clamped output-token estimate used for budget admission.</summary>
        public static int EffectiveMaxTokens(ExternalLlmCompletionRequest request)
        {
            return Clamp(request == null ? 0 : request.maxTokens, MinMaxTokens, MaxMaxTokens);
        }

        /// <summary>
        /// Returns a copy of the handle's current result. A terminal (Succeeded/Failed) result is dropped
        /// after this read to bound memory, so poll until terminal, then apply. Unknown = untracked handle.
        /// </summary>
        public static LlmCompletionResult Poll(int handle)
        {
            lock (gate)
            {
                LlmCompletionResult slot;
                if (!results.TryGetValue(handle, out slot))
                {
                    return new LlmCompletionResult { status = LlmCompletionStatus.Unknown };
                }

                LlmCompletionResult copy = new LlmCompletionResult
                {
                    status = slot.status,
                    text = slot.text,
                    error = slot.error
                };

                if (slot.status == LlmCompletionStatus.Succeeded || slot.status == LlmCompletionStatus.Failed)
                {
                    results.Remove(handle);
                }

                return copy;
            }
        }

        // Fire-and-forget background call. async void is deliberate (top-level task, like
        // ApiConnectionController.RefreshCapability): the try/catch guarantees no unobserved exception,
        // and the outcome is written into the slot for the poller to pick up.
        private static async void RunAsync(int handle, ApiEndpointConfig endpoint, string systemPrompt, string userText, int maxTokens, int timeoutSeconds, float temperature)
        {
            try
            {
                string text = await LlmClient.SendSingleCompletion(endpoint, systemPrompt, userText, maxTokens, timeoutSeconds, temperature);
                Complete(handle, LlmCompletionStatus.Succeeded, text, string.Empty);
            }
            catch (Exception e)
            {
                // This string crosses the public adapter boundary. Networking exceptions can contain a
                // key-bearing query URL, so apply the same redaction invariant as saved diary failures.
                Complete(handle, LlmCompletionStatus.Failed, string.Empty,
                    ApiLaneLabels.TrimForLog(e.Message));
            }
        }

        private static void Complete(int handle, LlmCompletionStatus status, string text, string error)
        {
            lock (gate)
            {
                LlmCompletionResult slot;
                if (results.TryGetValue(handle, out slot))
                {
                    slot.status = status;
                    slot.text = text ?? string.Empty;
                    slot.error = error ?? string.Empty;
                }
                // A game-session reset can remove the slot while cancellation is still unwinding.
                // In that case the old result is deliberately dropped.
            }
        }

        // Caller holds the lock.
        private static int AllocateHandle()
        {
            nextHandle = nextHandle >= int.MaxValue ? 1 : nextHandle + 1;
            return nextHandle;
        }

        // Picks the requested lane when it is usable, else the first usable (enabled + url + model) lane.
        private static ApiEndpointConfig ResolveEndpoint(PawnDiarySettings settings, int laneIndex)
        {
            List<ApiEndpointConfig> lanes = settings.apiEndpoints;
            if (lanes == null || lanes.Count == 0)
            {
                return null;
            }

            if (laneIndex >= 0 && laneIndex < lanes.Count && IsUsable(lanes[laneIndex]))
            {
                return lanes[laneIndex];
            }

            for (int i = 0; i < lanes.Count; i++)
            {
                if (IsUsable(lanes[i]))
                {
                    return lanes[i];
                }
            }

            return null;
        }

        private static bool IsUsable(ApiEndpointConfig endpoint)
        {
            return endpoint != null
                && endpoint.enabled
                && !string.IsNullOrWhiteSpace(endpoint.url)
                && !string.IsNullOrWhiteSpace(endpoint.model);
        }

        private static string Cap(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Length > MaxInputChars ? TextTruncation.SafePrefix(value, MaxInputChars) : value;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}

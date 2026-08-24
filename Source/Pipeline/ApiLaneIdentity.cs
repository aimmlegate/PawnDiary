// Pure API transport helpers. Runtime code passes plain endpoint fields into this file so lane
// identity, bounded queueing, admission, retry policy, and secret-safe labels stay in one audited
// place without depending on RimWorld, Verse, Unity, HTTP, or saved settings objects.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PawnDiary
{
    /// <summary>
    /// Canonical opaque identity key for one API lane under a specific comparison mode. Different call
    /// sites intentionally compare different fields: generation gates include effective auth,
    /// model-list fetches use exact raw row values, and UI connection tests also include reasoning
    /// effort. The completed comparison material is hashed before storage so credentials never remain
    /// in an in-memory dictionary key or escape through <see cref="ToString"/>. Player-facing endpoint
    /// labels use the separate <see cref="ApiLaneLabels"/> redaction path below.
    /// </summary>
    internal struct ApiLaneIdentity : IEquatable<ApiLaneIdentity>
    {
        private readonly string key;

        private ApiLaneIdentity(string comparisonMaterial)
        {
            key = Fingerprint(comparisonMaterial ?? string.Empty);
        }

        /// <summary>True for a default identity created because there was no request or row to key.</summary>
        public bool Empty
        {
            get { return string.IsNullOrEmpty(key); }
        }

        /// <summary>
        /// Identity for concurrency gates and transient-failure cooldowns. This preserves the old
        /// behavior: canonical generation URL, trimmed model, normalized compatibility mode, effective
        /// auth style, and effective key; reasoning effort does not split a lane. Gemini's optional
        /// <c>models/</c> prefix is the one provider-specific model spelling normalized here.
        /// </summary>
        public static ApiLaneIdentity ForGate(string endpointUrl, string modelName, ApiCompatibilityMode apiMode,
            ApiAuthMode authMode, string customAuthHeaderName, string apiKey)
        {
            return new ApiLaneIdentity(Join(
                "gate",
                NormalizedGenerationUrlKey(endpointUrl, modelName, apiMode),
                IdentityModelName(modelName, apiMode, true),
                ApiEndpointPolicy.NormalizeApiMode(apiMode).ToString(),
                ApiEndpointPolicy.NormalizeAuthMode(authMode).ToString(),
                ApiEndpointPolicy.EffectiveAuthHeaderName(authMode, customAuthHeaderName),
                ApiEndpointPolicy.EffectiveApiKey(authMode, apiKey)));
        }

        /// <summary>
        /// Identity for removing duplicate failover attempts. Model text stays raw here because the
        /// previous comparison did not trim it, except Gemini's equivalent <c>models/</c> prefix.
        /// </summary>
        public static ApiLaneIdentity ForAttempt(string endpointUrl, string modelName, ApiCompatibilityMode apiMode,
            ApiAuthMode authMode, string customAuthHeaderName, string apiKey)
        {
            return new ApiLaneIdentity(Join(
                "attempt",
                NormalizedGenerationUrlKey(endpointUrl, modelName, apiMode),
                IdentityModelName(modelName, apiMode, false),
                ApiEndpointPolicy.NormalizeApiMode(apiMode).ToString(),
                ApiEndpointPolicy.NormalizeAuthMode(authMode).ToString(),
                ApiEndpointPolicy.EffectiveAuthHeaderName(authMode, customAuthHeaderName),
                ApiEndpointPolicy.EffectiveApiKey(authMode, apiKey)));
        }

        /// <summary>Identity for endpoint+model generation-lane pinning when no auth data was saved.</summary>
        public static ApiLaneIdentity ForGeneration(string endpointUrl, string modelName, ApiCompatibilityMode apiMode)
        {
            return new ApiLaneIdentity(Join(
                "generation",
                NormalizedGenerationUrlKey(endpointUrl, modelName, apiMode),
                IdentityModelName(modelName, apiMode, false),
                ApiEndpointPolicy.NormalizeApiMode(apiMode).ToString()));
        }

        /// <summary>Identity for generation-lane pinning when auth must also match.</summary>
        public static ApiLaneIdentity ForGenerationWithAuth(string endpointUrl, string modelName,
            ApiCompatibilityMode apiMode, ApiAuthMode authMode, string customAuthHeaderName, string apiKey)
        {
            return new ApiLaneIdentity(Join(
                "generation-auth",
                NormalizedGenerationUrlKey(endpointUrl, modelName, apiMode),
                IdentityModelName(modelName, apiMode, false),
                ApiEndpointPolicy.NormalizeApiMode(apiMode).ToString(),
                ApiEndpointPolicy.NormalizeAuthMode(authMode).ToString(),
                ApiEndpointPolicy.EffectiveAuthHeaderName(authMode, customAuthHeaderName),
                ApiEndpointPolicy.EffectiveApiKey(authMode, apiKey)));
        }

        /// <summary>
        /// Exact row identity for model-list fetch results. This deliberately uses raw URL and key
        /// text because the old stale-result check invalidated a fetch after any row edit.
        /// </summary>
        public static ApiLaneIdentity ForFetchTarget(string endpointUrl, string apiKey, ApiAuthMode authMode,
            string customAuthHeaderName, ApiCompatibilityMode apiMode)
        {
            return new ApiLaneIdentity(Join(
                "fetch",
                Raw(endpointUrl),
                Raw(apiKey),
                ApiEndpointPolicy.NormalizeAuthMode(authMode).ToString(),
                ApiEndpointPolicy.EffectiveAuthHeaderName(authMode, customAuthHeaderName),
                ApiEndpointPolicy.NormalizeApiMode(apiMode).ToString()));
        }

        /// <summary>
        /// Exact row identity for connection-test results. This includes model and reasoning effort,
        /// and normalizes fields the test runner normalized when it captured its request snapshot.
        /// </summary>
        public static ApiLaneIdentity ForConnectionTest(string endpointUrl, string apiKey, string modelName,
            ApiAuthMode authMode, string customAuthHeaderName, ApiCompatibilityMode apiMode, string reasoningEffort)
        {
            return new ApiLaneIdentity(Join(
                "connection-test",
                Raw(endpointUrl),
                Raw(apiKey),
                Raw(modelName),
                ApiEndpointPolicy.NormalizeAuthMode(authMode).ToString(),
                ApiEndpointPolicy.EffectiveAuthHeaderName(authMode, customAuthHeaderName),
                ApiEndpointPolicy.NormalizeApiMode(apiMode).ToString(),
                ApiEndpointPolicy.NormalizeReasoningEffort(reasoningEffort)));
        }

        public bool Equals(ApiLaneIdentity other)
        {
            return string.Equals(key ?? string.Empty, other.key ?? string.Empty, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ApiLaneIdentity && Equals((ApiLaneIdentity)obj);
        }

        public override int GetHashCode()
        {
            return (key ?? string.Empty).GetHashCode();
        }

        public override string ToString()
        {
            return key ?? string.Empty;
        }

        public static bool operator ==(ApiLaneIdentity left, ApiLaneIdentity right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ApiLaneIdentity left, ApiLaneIdentity right)
        {
            return !left.Equals(right);
        }

        private static string NormalizedGenerationUrlKey(
            string endpointUrl,
            string modelName,
            ApiCompatibilityMode apiMode)
        {
            return EndpointUtility.BuildGenerationUrl(
                endpointUrl ?? string.Empty,
                modelName ?? string.Empty,
                apiMode).Trim().ToLowerInvariant();
        }

        private static string IdentityModelName(
            string modelName,
            ApiCompatibilityMode apiMode,
            bool trimNonGemini)
        {
            LlmProtocolMode protocolMode = EndpointUtility.ProtocolModeFor(apiMode);
            if (protocolMode == LlmProtocolMode.GeminiGenerateContent)
            {
                return LlmProtocolDispatcher.CanonicalModelName(modelName, protocolMode);
            }

            return trimNonGemini ? Trimmed(modelName) : Raw(modelName);
        }

        private static string Raw(string value)
        {
            return value ?? string.Empty;
        }

        private static string Trimmed(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static string Join(params string[] parts)
        {
            return string.Join("\n", parts ?? new string[0]);
        }

        private static string Fingerprint(string comparisonMaterial)
        {
            // SHA-256 preserves every existing equality distinction without retaining the URL, model,
            // or key that created it. Base64 is compact and safe for dictionary keys/debug inspection.
            using (SHA256 hash = SHA256.Create())
            {
                return Convert.ToBase64String(
                    hash.ComputeHash(Encoding.UTF8.GetBytes(comparisonMaterial ?? string.Empty)));
            }
        }
    }

    /// <summary>
    /// Stable, dynamically resizable asynchronous admission gate. Unlike replacing a
    /// <see cref="SemaphoreSlim"/> when settings change, updating this object's limit cannot create
    /// a second set of permits while callers still hold permits from the first set.
    /// </summary>
    internal sealed class AsyncAdmissionGate
    {
        private static readonly Task CompletedWait = Task.FromResult(true);
        private readonly object sync = new object();
        private readonly Queue<Waiter> waiters = new Queue<Waiter>();
        private int activeCount;
        private int limit;

        private sealed class Waiter
        {
            public readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
            public CancellationTokenRegistration registration;
            public bool hasRegistration;
            public int state; // 0 = waiting, 1 = admitted, 2 = cancelled
        }

        /// <summary>Creates a gate with at least one available concurrent slot.</summary>
        public AsyncAdmissionGate(int initialLimit)
        {
            limit = Math.Max(1, initialLimit);
        }

        /// <summary>The current configured admission limit.</summary>
        public int Limit
        {
            get
            {
                lock (sync)
                {
                    return limit;
                }
            }
        }

        /// <summary>Number of callers that currently hold this gate.</summary>
        public int ActiveCount
        {
            get
            {
                lock (sync)
                {
                    return activeCount;
                }
            }
        }

        /// <summary>
        /// Waits asynchronously until one slot is admitted. The caller must pair every successful
        /// wait with exactly one <see cref="Release"/>.
        /// </summary>
        public Task WaitAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledTask();
            }

            Waiter waiter;
            lock (sync)
            {
                if (activeCount < limit && waiters.Count == 0)
                {
                    activeCount++;
                    return CompletedWait;
                }

                waiter = new Waiter();
                waiters.Enqueue(waiter);
            }

            CancellationTokenRegistration registration = cancellationToken.Register(
                () => CancelWaiter(waiter));
            bool disposeRegistration;
            lock (sync)
            {
                disposeRegistration = waiter.state != 0;
                if (!disposeRegistration)
                {
                    waiter.registration = registration;
                    waiter.hasRegistration = true;
                }
            }

            // Admission/cancellation can win between enqueueing and registering the callback.
            // Dispose the now-unused registration after that race rather than retaining the token.
            if (disposeRegistration)
            {
                registration.Dispose();
            }

            return waiter.completion.Task;
        }

        /// <summary>
        /// Changes the limit without replacing gate identity. Shrinks take effect as current holders
        /// release; increases immediately admit as many queued callers as the new limit permits.
        /// </summary>
        public void UpdateLimit(int newLimit)
        {
            List<Waiter> admitted;
            List<Waiter> discarded;
            lock (sync)
            {
                limit = Math.Max(1, newLimit);
                admitted = AdmitAvailableWaitersLocked(out discarded);
            }

            DisposeRegistrations(discarded);
            CompleteAdmissions(admitted);
        }

        /// <summary>Releases one successfully admitted slot.</summary>
        public void Release()
        {
            List<Waiter> admitted;
            List<Waiter> discarded;
            lock (sync)
            {
                if (activeCount <= 0)
                {
                    throw new InvalidOperationException("Cannot release an API admission gate that is not held.");
                }

                activeCount--;
                admitted = AdmitAvailableWaitersLocked(out discarded);
            }

            DisposeRegistrations(discarded);
            CompleteAdmissions(admitted);
        }

        private void CancelWaiter(Waiter waiter)
        {
            bool cancelled = false;
            lock (sync)
            {
                if (waiter.state == 0)
                {
                    waiter.state = 2;
                    cancelled = true;
                }
            }

            if (cancelled)
            {
                waiter.completion.TrySetCanceled();
            }
        }

        private List<Waiter> AdmitAvailableWaitersLocked(out List<Waiter> discarded)
        {
            List<Waiter> admitted = null;
            discarded = null;
            while (activeCount < limit && waiters.Count > 0)
            {
                Waiter waiter = waiters.Dequeue();
                if (waiter.state != 0)
                {
                    if (waiter.hasRegistration)
                    {
                        if (discarded == null)
                        {
                            discarded = new List<Waiter>();
                        }

                        discarded.Add(waiter);
                    }

                    continue;
                }

                waiter.state = 1;
                activeCount++;
                if (admitted == null)
                {
                    admitted = new List<Waiter>();
                }

                admitted.Add(waiter);
            }

            return admitted;
        }

        private static void DisposeRegistrations(List<Waiter> waitersToDispose)
        {
            if (waitersToDispose == null)
            {
                return;
            }

            for (int i = 0; i < waitersToDispose.Count; i++)
            {
                waitersToDispose[i].registration.Dispose();
            }
        }

        private static void CompleteAdmissions(List<Waiter> admitted)
        {
            if (admitted == null)
            {
                return;
            }

            for (int i = 0; i < admitted.Count; i++)
            {
                Waiter waiter = admitted[i];
                if (waiter.hasRegistration)
                {
                    waiter.registration.Dispose();
                }

                // Complete outside the gate lock because a continuation is allowed to run inline.
                waiter.completion.TrySetResult(true);
            }
        }

        private static Task CancelledTask()
        {
            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
            completion.TrySetCanceled();
            return completion.Task;
        }
    }

    /// <summary>
    /// Opaque ownership token for one capacity-reserved transport item. A staged item is invisible
    /// until its owning main-thread transaction calls <see cref="BoundedTransportQueue{T}.Activate"/>.
    /// </summary>
    internal sealed class StagedTransportQueueItem<T>
    {
        internal readonly BoundedTransportQueue<T> owner;
        internal readonly T item;
        // 0 = staged, 1 = active/visible, 2 = cancelled or consumed.
        internal int state;

        internal StagedTransportQueueItem(BoundedTransportQueue<T> owner, T item)
        {
            this.owner = owner;
            this.item = item;
        }
    }

    /// <summary>
    /// Bounded FIFO used by the HTTP dispatcher. <see cref="TryStage"/> reserves capacity without
    /// making work visible, so the main thread can commit the matching saved state before
    /// <see cref="Activate"/> lets any worker dequeue it. The historical <see cref="TryEnqueue"/>
    /// remains an atomic stage-and-activate convenience for callers that own no saved transaction.
    /// </summary>
    internal sealed class BoundedTransportQueue<T>
    {
        private readonly ConcurrentQueue<T> queue = new ConcurrentQueue<T>();
        private readonly int capacity;
        private int reservedCount;
        private int visibleCount;

        public BoundedTransportQueue(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            this.capacity = capacity;
        }

        public int Capacity
        {
            get { return capacity; }
        }

        public int Count
        {
            get { return Volatile.Read(ref visibleCount); }
        }

        /// <summary>Active plus staged slots; this value never exceeds <see cref="Capacity"/>.</summary>
        public int ReservedCount
        {
            get { return Volatile.Read(ref reservedCount); }
        }

        /// <summary>
        /// Reserves one bounded slot without publishing the item to consumers. Returns false
        /// immediately when active and staged work already owns every slot.
        /// </summary>
        public bool TryStage(T item, out StagedTransportQueueItem<T> staged)
        {
            staged = null;
            while (true)
            {
                int observed = Volatile.Read(ref reservedCount);
                if (observed >= capacity)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(
                        ref reservedCount,
                        observed + 1,
                        observed) != observed)
                {
                    continue;
                }

                staged = new StagedTransportQueueItem<T>(this, item);
                return true;
            }
        }

        /// <summary>
        /// Publishes one still-staged item exactly once. A cancelled, consumed, foreign, or already
        /// active handle is rejected without changing capacity.
        /// </summary>
        public bool Activate(StagedTransportQueueItem<T> staged)
        {
            if (staged == null
                || !ReferenceEquals(staged.owner, this)
                || Interlocked.CompareExchange(ref staged.state, 1, 0) != 0)
            {
                return false;
            }

            try
            {
                // Publish the visible count before the queue node. A concurrent worker can otherwise
                // dequeue between Enqueue and Increment and transiently drive Count negative.
                Interlocked.Increment(ref visibleCount);
                queue.Enqueue(staged.item);
                return true;
            }
            catch
            {
                Volatile.Write(ref staged.state, 2);
                Interlocked.Decrement(ref visibleCount);
                Interlocked.Decrement(ref reservedCount);
                throw;
            }
        }

        /// <summary>Releases one invisible staged slot. Active work cannot be cancelled here.</summary>
        public bool Cancel(StagedTransportQueueItem<T> staged)
        {
            if (staged == null
                || !ReferenceEquals(staged.owner, this)
                || Interlocked.CompareExchange(ref staged.state, 2, 0) != 0)
            {
                return false;
            }

            Interlocked.Decrement(ref reservedCount);
            return true;
        }

        /// <summary>Returns false immediately when all bounded queue slots are reserved.</summary>
        public bool TryEnqueue(T item)
        {
            StagedTransportQueueItem<T> staged;
            if (!TryStage(item, out staged))
            {
                return false;
            }

            return Activate(staged);
        }

        /// <summary>Removes one item and releases its bounded queue slot.</summary>
        public bool TryDequeue(out T item)
        {
            if (!queue.TryDequeue(out item))
            {
                return false;
            }

            Interlocked.Decrement(ref visibleCount);
            Interlocked.Decrement(ref reservedCount);
            return true;
        }
    }

    /// <summary>Why one linked transport operation observed cancellation.</summary>
    internal enum TransportCancellationCause
    {
        /// <summary>The transport cancelled independently of our caller/session/deadline tokens.</summary>
        Transport,

        /// <summary>The caller cancelled, the game session ended, or the session was replaced.</summary>
        External,

        /// <summary>Only the operation's internal wall-clock deadline fired.</summary>
        Deadline
    }

    /// <summary>Pure HTTP transport decisions shared by runtime code and socket-free tests.</summary>
    internal static class LlmTransportPolicy
    {
        public const int MinimumTimeoutSeconds = 5;
        public const int MinimumRetryAttempts = 1;
        public const int MaximumRetryAttempts = 10;
        public const double MinimumRetryDelaySeconds = 0.1d;
        public const double MaximumRetryDelaySeconds = 10d;

        /// <summary>Clamps a configured per-lane concurrency value to its defensive bounds.</summary>
        public static int NormalizeConcurrency(int requested, int maximum)
        {
            if (maximum < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum));
            }

            if (requested < 1)
            {
                return 1;
            }

            return requested > maximum ? maximum : requested;
        }

        /// <summary>Clamps how many times one API lane may be attempted for a transient failure.</summary>
        public static int NormalizeRetryAttempts(int requested)
        {
            if (requested < MinimumRetryAttempts)
            {
                return MinimumRetryAttempts;
            }

            return requested > MaximumRetryAttempts ? MaximumRetryAttempts : requested;
        }

        /// <summary>Clamps the configurable first retry delay to a bounded positive duration.</summary>
        public static double NormalizeRetryDelaySeconds(double requested)
        {
            if (double.IsNaN(requested) || requested < MinimumRetryDelaySeconds)
            {
                return MinimumRetryDelaySeconds;
            }

            return requested > MaximumRetryDelaySeconds
                ? MaximumRetryDelaySeconds
                : requested;
        }

        /// <summary>
        /// Returns true only when one transient failure should schedule another physical HTTP attempt.
        /// A request deadline, a shared lane cooldown, a server Retry-After, or an exhausted attempt
        /// budget all stop the local retry loop.
        /// </summary>
        public static bool ShouldRetryTransientFailure(
            int failedAttemptNumber,
            int requestedAttempts,
            bool cancellationRequested,
            bool laneCooling,
            int retryAfterSeconds)
        {
            return !cancellationRequested
                && !laneCooling
                && retryAfterSeconds <= 0
                && Math.Max(1, failedAttemptNumber) < NormalizeRetryAttempts(requestedAttempts);
        }

        /// <summary>
        /// Returns true when a terminal transient failure should install or extend shared cooldown
        /// state. An overlapping local failure reuses an active cooldown, while a provider-directed
        /// Retry-After may still extend it.
        /// </summary>
        public static bool ShouldInstallTransientCooldown(bool laneCooling, int retryAfterSeconds)
        {
            return !laneCooling || retryAfterSeconds > 0;
        }

        /// <summary>
        /// Converts a parsed Retry-After duration to non-negative whole seconds without overflowing
        /// when a provider sends an extreme future HTTP date. The downstream cooldown policy applies
        /// its much smaller operational cap.
        /// </summary>
        public static int NormalizeRetryAfterSeconds(double seconds)
        {
            if (double.IsNaN(seconds) || seconds <= 0d)
            {
                return 0;
            }

            if (double.IsPositiveInfinity(seconds) || seconds >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)Math.Ceiling(seconds);
        }

        /// <summary>
        /// A success may clear only a cooldown that had already expired when that success completed.
        /// This prevents an older in-flight request from erasing a newer sibling's Retry-After.
        /// </summary>
        public static bool ShouldClearCooldownAfterSuccess(DateTime cooldownUntilUtc, DateTime succeededUtc)
        {
            return cooldownUntilUtc <= succeededUtc;
        }

        /// <summary>
        /// Returns true when a lane can be tried at an immediate failover handoff. The transport does
        /// not wait for a cooling failover lane, so a future expiry cannot reserve retry-delay budget.
        /// </summary>
        public static bool CanLaneRunAtImmediateHandoff(
            DateTime cooldownUntilUtc,
            DateTime nowUtc)
        {
            return cooldownUntilUtc <= nowUtc;
        }

        /// <summary>
        /// Returns a linear progressive backoff: after failure 1 wait one base interval, after failure
        /// 2 wait two intervals, and so on. The total request deadline remains the hard outer bound.
        /// </summary>
        public static TimeSpan ProgressiveRetryDelay(int failedAttemptNumber, double baseDelaySeconds)
        {
            int safeAttempt = failedAttemptNumber < 1
                ? 1
                : Math.Min(failedAttemptNumber, MaximumRetryAttempts);
            double safeBaseDelay = NormalizeRetryDelaySeconds(baseDelaySeconds);
            return TimeSpan.FromSeconds(safeBaseDelay * safeAttempt);
        }

        /// <summary>
        /// Returns true only when a local retry delay fits inside its fair share of the remaining
        /// request deadline. Each downstream failover lane reserves one equal share; a single-lane
        /// request may use the full remainder. The strict comparison leaves nonzero time for the
        /// physical retry after its delay completes.
        /// </summary>
        public static bool CanScheduleRetryDelay(
            TimeSpan retryDelay,
            DateTime deadlineUtc,
            DateTime nowUtc,
            int remainingFailoverLanes)
        {
            if (retryDelay <= TimeSpan.Zero)
            {
                return false;
            }

            TimeSpan remaining = Remaining(deadlineUtc, nowUtc);
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            long shareCount = Math.Max(0L, (long)remainingFailoverLanes) + 1L;
            long currentLaneDelayBudgetTicks = remaining.Ticks / shareCount;
            return retryDelay.Ticks < currentLaneDelayBudgetTicks;
        }

        /// <summary>Returns true only when an ordinary generation lane is not cooling.</summary>
        public static bool MayAttemptLane(bool laneCooling)
        {
            return !laneCooling;
        }

        /// <summary>
        /// Returns true when a queued request should reserve one more bounded dispatch worker. Active
        /// work is deliberately not compared with queue length: queue length excludes dequeued work,
        /// so one active request plus one newly queued request still needs a second worker.
        /// </summary>
        public static bool ShouldStartDispatchWorker(int queuedCount, int activeWorkers, int maximumWorkers)
        {
            return queuedCount > 0
                && activeWorkers >= 0
                && maximumWorkers > 0
                && activeWorkers < maximumWorkers;
        }

        /// <summary>
        /// Classifies a linked cancellation without inspecting exception types or live transport
        /// objects. External cancellation wins races with the internal deadline and must never cool
        /// a lane; a deadline-only cancellation represents a transient lane timeout.
        /// </summary>
        public static TransportCancellationCause ClassifyCancellation(
            bool linkedCancellationRequested,
            bool callerCancellationRequested,
            bool sessionCancellationRequested,
            bool sessionReplaced)
        {
            if (callerCancellationRequested || sessionCancellationRequested || sessionReplaced)
            {
                return TransportCancellationCause.External;
            }

            return linkedCancellationRequested
                ? TransportCancellationCause.Deadline
                : TransportCancellationCause.Transport;
        }

        /// <summary>HTTP 408, 429, and server-side 5xx statuses can reasonably succeed on retry.</summary>
        public static bool IsTransientStatusCode(int statusCode)
        {
            return statusCode == 408 || statusCode == 429 || statusCode >= 500;
        }

        /// <summary>
        /// Creates the total wall-clock deadline at admission time. Queue residence, gate waiting,
        /// retries, and failover all consume the same bounded request budget.
        /// </summary>
        public static DateTime CreateDeadlineUtc(DateTime enqueuedUtc, int timeoutSeconds)
        {
            return enqueuedUtc.AddSeconds(Math.Max(MinimumTimeoutSeconds, timeoutSeconds));
        }

        /// <summary>Returns zero after a queue/request deadline has expired.</summary>
        public static TimeSpan Remaining(DateTime deadlineUtc, DateTime nowUtc)
        {
            TimeSpan remaining = deadlineUtc - nowUtc;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Sanitized API lane labels for debug logs and settings connection-test logs. These labels are
    /// intentionally English diagnostics and never include API keys or URL query/fragment text.
    /// </summary>
    internal static class ApiLaneLabels
    {
        // Defensive diagnostic caps. Endpoint/model text is user-editable and can otherwise make one
        // lane label arbitrarily large even though no request payload needs that full text in a log.
        private const int ModelLabelMaxCharacters = 80;
        private const int EndpointLabelMaxCharacters = 120;

        /// <summary>Formats one endpoint/model/mode tuple for logs without leaking query-string keys.</summary>
        public static string Label(string endpointUrl, string modelName, ApiCompatibilityMode apiMode)
        {
            string model = DiagnosticComponent(modelName, "<blank-model>", ModelLabelMaxCharacters);
            string endpoint = string.IsNullOrWhiteSpace(endpointUrl)
                ? "<blank-url>"
                : DiagnosticComponent(
                    EndpointUtility.BuildGenerationUrl(
                        SanitizeEndpointUrlForLog(endpointUrl),
                        model,
                        apiMode),
                    "<blank-url>",
                    EndpointLabelMaxCharacters);
            return TrimForLog(
                model + " [" + ApiEndpointPolicy.NormalizeApiMode(apiMode) + "] @ " + endpoint);
        }

        /// <summary>Trims one-line log details to the shared diagnostic length cap.</summary>
        public static string TrimForLog(string value)
        {
            return TrimForLog(value, string.Empty, string.Empty);
        }

        /// <summary>
        /// Trims diagnostics after masking the exact configured API key and custom auth-header value.
        /// </summary>
        public static string TrimForLog(string value, string apiKey, string customAuthHeaderName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            // Redact before trimming so a secret can never be the surviving prefix of a long line.
            value = OneLine(RedactSecrets(value, apiKey, customAuthHeaderName));
            return TextTruncation.EllipsizedPrefix(value, 180);
        }

        // A bearer token or a key=/token= query parameter can ride along inside an arbitrary error
        // body or a networking exception message (some HTTP stacks echo the request URI). Anything
        // that reaches a log line or a player-visible error string passes through here first so the
        // secret is masked. These are intentionally broad: false positives only ever hide a value.
        private static readonly Regex QueryKeyPattern = new Regex(
            @"([?&](?:key|api[_-]?key|access_token|token|auth)=)[^&\s""']+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // Match the whole token after "Bearer " up to the next whitespace or quote. A token can
        // carry base64/base64url padding and separators (+ / = ~ : .), so an allow-list of characters
        // would leak the tail of such keys; stop only at a boundary a token never spans.
        private static readonly Regex BearerPattern = new Regex(
            @"\bBearer\s+[^\s""']+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // URI userinfo is itself a credential channel (`https://user:password@host`). Mask it in
        // arbitrary exception/status text in addition to stripping it from structured lane labels.
        private static readonly Regex UriUserInfoPattern = new Regex(
            @"(\b[a-z][a-z0-9+.-]*://)[^/\s?#@]+@",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Masks API secrets (a <c>key=</c>/<c>token=</c> query parameter or a <c>Bearer &lt;token&gt;</c>
        /// value) in arbitrary text — error bodies, exception messages, anything that might be logged
        /// or shown to the player. Returns the input unchanged when no secret pattern is present.
        /// </summary>
        public static string RedactSecrets(string value)
        {
            return RedactSecrets(value, string.Empty, string.Empty);
        }

        /// <summary>
        /// Masks generic auth patterns plus the exact key/header configured for the request. Exact
        /// replacement covers provider bodies that echo a bare key without a recognizable prefix.
        /// </summary>
        public static string RedactSecrets(string value, string apiKey, string customAuthHeaderName)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            if (!string.IsNullOrEmpty(apiKey)
                && !string.Equals(apiKey, "<redacted>", StringComparison.Ordinal))
            {
                // Exact replacement runs first so a very short configured key cannot corrupt the
                // "<redacted>" markers inserted by the broader pattern passes below.
                value = value.Replace(apiKey, "<redacted>");
            }

            value = UriUserInfoPattern.Replace(value, "$1<redacted>@");
            value = QueryKeyPattern.Replace(value, "$1<redacted>");
            value = BearerPattern.Replace(value, "Bearer <redacted>");

            string headerName = (customAuthHeaderName ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(headerName))
            {
                // Match both HTTP-style `X-Key: value` and JSON-style `"X-Key":"value"`.
                // An unquoted header value is consumed to a structural/line boundary rather than
                // the first space, because proxy-normalized credentials can contain spaces.
                string headerPattern = @"([""']?" + Regex.Escape(headerName)
                    + @"[""']?\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\r\n,;}\]]+)";
                value = Regex.Replace(
                    value,
                    headerPattern,
                    "$1<redacted>",
                    RegexOptions.IgnoreCase);
            }

            return value;
        }

        private static string SanitizeEndpointUrlForLog(string endpointUrl)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl))
            {
                return string.Empty;
            }

            string sanitized = OneLine(endpointUrl);

            int query = sanitized.IndexOf('?');
            int fragment = sanitized.IndexOf('#');
            int cut = -1;
            if (query >= 0)
            {
                cut = query;
            }

            if (fragment >= 0 && (cut < 0 || fragment < cut))
            {
                cut = fragment;
            }

            if (cut >= 0)
            {
                sanitized = sanitized.Substring(0, cut);
            }

            // Preserve the endpoint spelling/path for useful diagnostics, but remove every byte of
            // URI userinfo before it can enter a log. Limit the search to the authority so an '@' in
            // a later path segment remains ordinary path text.
            int schemeSeparator = sanitized.IndexOf("://", StringComparison.Ordinal);
            if (schemeSeparator >= 0)
            {
                int authorityStart = schemeSeparator + 3;
                int authorityEnd = sanitized.IndexOf('/', authorityStart);
                if (authorityEnd < 0)
                {
                    authorityEnd = sanitized.Length;
                }

                int authorityLength = authorityEnd - authorityStart;
                if (authorityLength > 0)
                {
                    int userInfoEnd = sanitized.LastIndexOf(
                        '@', authorityEnd - 1, authorityLength);
                    if (userInfoEnd >= authorityStart)
                    {
                        sanitized = sanitized.Substring(0, authorityStart)
                            + sanitized.Substring(userInfoEnd + 1);
                    }
                }
            }

            return sanitized;
        }

        /// <summary>Collapses whitespace/newlines/tabs to a single trimmed line. Shared so log/status
        /// trimmers (e.g. ApiConnectionController.TrimForStatus) don't each re-implement it.</summary>
        internal static string OneLine(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            bool pendingSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }
                builder.Append(c);
            }

            return builder.ToString();
        }

        private static string DiagnosticComponent(string value, string blankValue, int maxCharacters)
        {
            string oneLine = OneLine(value);
            if (string.IsNullOrWhiteSpace(oneLine))
            {
                return blankValue;
            }

            return TextTruncation.EllipsizedPrefix(oneLine, maxCharacters);
        }
    }
}

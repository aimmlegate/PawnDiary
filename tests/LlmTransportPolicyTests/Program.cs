// Socket-free regression tests for the LLM transport's pure queue, admission, retry, deadline, and
// credential-redaction seams. Runtime HTTP/RimWorld types are deliberately absent from this project.
using System;
using System.Threading;
using System.Threading.Tasks;
using PawnDiary;

namespace LlmTransportPolicyTests
{
    internal static class Program
    {
        private static int assertions;

        private static async Task<int> Main()
        {
            await TestStableResizableAdmissionGate();
            await TestCancelledAdmissionDoesNotStrandGate();
            TestBoundedQueue();
            TestTransactionalQueueStaging();
            await TestConcurrentActivationCountNeverNegative();
            TestTransportPolicy();
            TestExactSecretRedaction();

            Console.WriteLine("LlmTransportPolicyTests passed " + assertions + " assertions.");
            return 0;
        }

        private static async Task TestStableResizableAdmissionGate()
        {
            AsyncAdmissionGate gate = new AsyncAdmissionGate(1);
            await gate.WaitAsync(CancellationToken.None);
            AssertEqual("one active holder", 1, gate.ActiveCount);

            Task second = gate.WaitAsync(CancellationToken.None);
            AssertFalse("second waits at limit one", second.IsCompleted);

            gate.UpdateLimit(2);
            await CompletesSoon("limit increase admits existing waiter", second);
            AssertEqual("same gate now has two holders", 2, gate.ActiveCount);
            AssertEqual("updated limit visible", 2, gate.Limit);

            gate.UpdateLimit(1);
            Task third = gate.WaitAsync(CancellationToken.None);
            AssertFalse("shrink blocks new holder while two remain", third.IsCompleted);

            gate.Release();
            AssertFalse("shrink waits until active drops below new limit", third.IsCompleted);
            gate.Release();
            await CompletesSoon("queued holder admitted after shrink drains", third);
            AssertEqual("one holder after shrink settles", 1, gate.ActiveCount);
            gate.Release();
            AssertEqual("all gate slots released", 0, gate.ActiveCount);
        }

        private static async Task TestCancelledAdmissionDoesNotStrandGate()
        {
            AsyncAdmissionGate gate = new AsyncAdmissionGate(1);
            await gate.WaitAsync(CancellationToken.None);

            CancellationTokenSource cancellation = new CancellationTokenSource();
            Task cancelledWait = gate.WaitAsync(cancellation.Token);
            cancellation.Cancel();
            await AssertCancelled("queued admission observes caller cancellation", cancelledWait);

            gate.Release();
            Task next = gate.WaitAsync(CancellationToken.None);
            await CompletesSoon("cancelled waiter does not consume a future slot", next);
            gate.Release();
            cancellation.Dispose();
        }

        private static void TestBoundedQueue()
        {
            BoundedTransportQueue<string> queue = new BoundedTransportQueue<string>(2);
            AssertTrue("bounded queue accepts first", queue.TryEnqueue("first"));
            AssertTrue("bounded queue accepts second", queue.TryEnqueue("second"));
            AssertFalse("bounded queue rejects overflow", queue.TryEnqueue("overflow"));
            AssertEqual("bounded count never exceeds capacity", 2, queue.Count);

            string item;
            AssertTrue("bounded queue dequeues", queue.TryDequeue(out item));
            AssertEqual("bounded queue preserves FIFO", "first", item);
            AssertTrue("released queue slot can be reused", queue.TryEnqueue("third"));
            AssertTrue("second item dequeues", queue.TryDequeue(out item));
            AssertEqual("second FIFO item", "second", item);
            AssertTrue("third item dequeues", queue.TryDequeue(out item));
            AssertEqual("third FIFO item", "third", item);
            AssertEqual("bounded queue ends empty", 0, queue.Count);
        }

        private static void TestTransactionalQueueStaging()
        {
            BoundedTransportQueue<string> queue = new BoundedTransportQueue<string>(2);
            StagedTransportQueueItem<string> first;
            StagedTransportQueueItem<string> second;
            StagedTransportQueueItem<string> overflow;

            AssertTrue("stage reserves first slot", queue.TryStage("first", out first));
            AssertEqual("staged item is not visible", 0, queue.Count);
            AssertEqual("staged item owns capacity", 1, queue.ReservedCount);
            string item;
            AssertFalse("worker cannot dequeue before activation", queue.TryDequeue(out item));

            AssertTrue("stage reserves second slot", queue.TryStage("second", out second));
            AssertFalse("staged capacity rejects overflow", queue.TryStage("overflow", out overflow));
            AssertEqual("reserved count remains bounded", 2, queue.ReservedCount);

            AssertTrue("activation publishes exact staged item", queue.Activate(first));
            AssertFalse("activation is exactly once", queue.Activate(first));
            AssertEqual("one activated item is visible", 1, queue.Count);
            AssertTrue("activated item dequeues", queue.TryDequeue(out item));
            AssertEqual("activated payload preserved", "first", item);
            AssertEqual("dequeue releases only consumed reservation", 1, queue.ReservedCount);

            AssertTrue("cancelling invisible stage releases capacity", queue.Cancel(second));
            AssertFalse("cancel is exactly once", queue.Cancel(second));
            AssertFalse("cancelled stage cannot activate", queue.Activate(second));
            AssertEqual("all transactional reservations released", 0, queue.ReservedCount);
            AssertTrue("released staged slot can be reused", queue.TryEnqueue("third"));
            AssertTrue("legacy enqueue remains visible", queue.TryDequeue(out item));
            AssertEqual("legacy enqueue payload preserved", "third", item);

            BoundedTransportQueue<string> foreignQueue = new BoundedTransportQueue<string>(1);
            StagedTransportQueueItem<string> foreign;
            AssertTrue("foreign stage created", foreignQueue.TryStage("foreign", out foreign));
            AssertFalse("queue rejects foreign activation", queue.Activate(foreign));
            AssertFalse("queue rejects foreign cancellation", queue.Cancel(foreign));
            AssertTrue("owner can cancel foreign stage", foreignQueue.Cancel(foreign));
        }

        private static async Task TestConcurrentActivationCountNeverNegative()
        {
            BoundedTransportQueue<int> queue = new BoundedTransportQueue<int>(1);
            int observedNegative = 0;
            for (int iteration = 0; iteration < 2000; iteration++)
            {
                StagedTransportQueueItem<int> staged;
                AssertTrue("concurrent stage " + iteration,
                    queue.TryStage(iteration, out staged));
                Task<bool> consumer = Task.Run(() =>
                {
                    int item;
                    while (!queue.TryDequeue(out item))
                    {
                        if (queue.Count < 0) Interlocked.Exchange(ref observedNegative, 1);
                        Thread.Yield();
                    }
                    if (queue.Count < 0) Interlocked.Exchange(ref observedNegative, 1);
                    return item == iteration;
                });
                Task<bool> activator = Task.Run(() => queue.Activate(staged));
                AssertTrue("concurrent activation " + iteration, await activator);
                AssertTrue("concurrent payload " + iteration, await consumer);
                if (queue.Count < 0) Interlocked.Exchange(ref observedNegative, 1);
            }
            AssertEqual("activation/dequeue race never publishes a negative count",
                0, observedNegative);
            AssertEqual("concurrent queue ends empty", 0, queue.Count);
            AssertEqual("concurrent reservations end empty", 0, queue.ReservedCount);
        }

        private static void TestTransportPolicy()
        {
            AssertEqual("concurrency floors at one", 1, LlmTransportPolicy.NormalizeConcurrency(0, 16));
            AssertEqual("concurrency preserves configured value", 7, LlmTransportPolicy.NormalizeConcurrency(7, 16));
            AssertEqual("concurrency clamps to cap", 16, LlmTransportPolicy.NormalizeConcurrency(99, 16));
            AssertEqual("retry attempts floor at one", 1, LlmTransportPolicy.NormalizeRetryAttempts(0));
            AssertEqual("retry attempts preserve configured value", 5, LlmTransportPolicy.NormalizeRetryAttempts(5));
            AssertEqual("retry attempts clamp to cap", 10, LlmTransportPolicy.NormalizeRetryAttempts(99));
            AssertEqual(
                "retry attempts handle minimum integer",
                LlmTransportPolicy.MinimumRetryAttempts,
                LlmTransportPolicy.NormalizeRetryAttempts(int.MinValue));
            AssertEqual(
                "retry attempts handle maximum integer",
                LlmTransportPolicy.MaximumRetryAttempts,
                LlmTransportPolicy.NormalizeRetryAttempts(int.MaxValue));
            AssertEqual(
                "invalid retry delay uses defensive floor",
                LlmTransportPolicy.MinimumRetryDelaySeconds,
                LlmTransportPolicy.NormalizeRetryDelaySeconds(double.NaN));
            AssertEqual(
                "negative infinity retry delay uses defensive floor",
                LlmTransportPolicy.MinimumRetryDelaySeconds,
                LlmTransportPolicy.NormalizeRetryDelaySeconds(double.NegativeInfinity));
            AssertEqual(
                "positive infinity retry delay uses defensive ceiling",
                LlmTransportPolicy.MaximumRetryDelaySeconds,
                LlmTransportPolicy.NormalizeRetryDelaySeconds(double.PositiveInfinity));
            AssertEqual(
                "oversized retry delay clamps to ceiling",
                LlmTransportPolicy.MaximumRetryDelaySeconds,
                LlmTransportPolicy.NormalizeRetryDelaySeconds(999d));
            AssertEqual(
                "first retry waits one base interval",
                TimeSpan.FromSeconds(0.5d),
                LlmTransportPolicy.ProgressiveRetryDelay(1, 0.5d));
            AssertEqual(
                "invalid failure number still waits one interval",
                TimeSpan.FromSeconds(0.5d),
                LlmTransportPolicy.ProgressiveRetryDelay(int.MinValue, 0.5d));
            AssertEqual(
                "later retries wait progressively longer",
                TimeSpan.FromSeconds(2d),
                LlmTransportPolicy.ProgressiveRetryDelay(4, 0.5d));
            AssertEqual(
                "retry multiplier is defensively capped",
                TimeSpan.FromSeconds(5d),
                LlmTransportPolicy.ProgressiveRetryDelay(int.MaxValue, 0.5d));
            DateTime retryBudgetNow = new DateTime(2026, 7, 27, 1, 0, 0, DateTimeKind.Utc);
            AssertFalse(
                "ten-second backoff cannot consume a five-second failover deadline",
                LlmTransportPolicy.CanScheduleRetryDelay(
                    TimeSpan.FromSeconds(10),
                    retryBudgetNow.AddSeconds(5),
                    retryBudgetNow,
                    1));
            AssertTrue(
                "short backoff fits the current lane's fair share before one failover",
                LlmTransportPolicy.CanScheduleRetryDelay(
                    TimeSpan.FromSeconds(2),
                    retryBudgetNow.AddSeconds(5),
                    retryBudgetNow,
                    1));
            AssertTrue(
                "single-lane adapter may use the full remaining retry-delay budget",
                LlmTransportPolicy.CanScheduleRetryDelay(
                    TimeSpan.FromSeconds(4),
                    retryBudgetNow.AddSeconds(5),
                    retryBudgetNow,
                    0));
            AssertFalse(
                "three-second backoff does not fit when one immediately runnable failover reserves a share",
                LlmTransportPolicy.CanScheduleRetryDelay(
                    TimeSpan.FromSeconds(3),
                    retryBudgetNow.AddSeconds(5),
                    retryBudgetNow,
                    1));
            int readyFailoverCountAtAttemptStart =
                LlmTransportPolicy.CanLaneRunAtImmediateHandoff(
                    retryBudgetNow,
                    retryBudgetNow)
                    ? 1
                    : 0;
            AssertEqual(
                "ready failover initially reserves one retry-delay share",
                1,
                readyFailoverCountAtAttemptStart);
            int refreshedFailoverCountAfterConcurrentCooldown =
                LlmTransportPolicy.CanLaneRunAtImmediateHandoff(
                    retryBudgetNow.AddSeconds(4),
                    retryBudgetNow)
                    ? 1
                    : 0;
            AssertEqual(
                "fresh retry-decision snapshot drops a failover that became cooling in flight",
                0,
                refreshedFailoverCountAfterConcurrentCooldown);
            AssertTrue(
                "three-second primary retry survives a downstream lane cooling during the first send",
                LlmTransportPolicy.CanScheduleRetryDelay(
                    TimeSpan.FromSeconds(3),
                    retryBudgetNow.AddSeconds(5),
                    retryBudgetNow,
                    refreshedFailoverCountAfterConcurrentCooldown));
            AssertTrue(
                "lane whose cooldown has expired can reserve an immediate failover share",
                LlmTransportPolicy.CanLaneRunAtImmediateHandoff(
                    retryBudgetNow,
                    retryBudgetNow));
            AssertFalse(
                "lane with any future cooldown cannot reserve an immediate failover share",
                LlmTransportPolicy.CanLaneRunAtImmediateHandoff(
                    retryBudgetNow.AddTicks(1),
                    retryBudgetNow));
            AssertTrue(
                "transient failure below attempt limit schedules retry",
                LlmTransportPolicy.ShouldRetryTransientFailure(4, 5, false, false, 0));
            AssertFalse(
                "attempt limit counts the original send without off-by-one retry",
                LlmTransportPolicy.ShouldRetryTransientFailure(5, 5, false, false, 0));
            AssertFalse(
                "request cancellation stops retry",
                LlmTransportPolicy.ShouldRetryTransientFailure(1, 5, true, false, 0));
            AssertFalse(
                "shared lane cooldown stops retry",
                LlmTransportPolicy.ShouldRetryTransientFailure(1, 5, false, true, 0));
            AssertFalse(
                "server Retry-After stops local retry",
                LlmTransportPolicy.ShouldRetryTransientFailure(1, 5, false, false, 60));
            AssertFalse(
                "defensive attempt floor still permits only one send",
                LlmTransportPolicy.ShouldRetryTransientFailure(1, int.MinValue, false, false, 0));
            AssertTrue(
                "first terminal failure installs lane cooldown",
                LlmTransportPolicy.ShouldInstallTransientCooldown(false, 0));
            AssertFalse(
                "overlapping local failure reuses active cooldown",
                LlmTransportPolicy.ShouldInstallTransientCooldown(true, 0));
            AssertTrue(
                "provider Retry-After may extend active cooldown",
                LlmTransportPolicy.ShouldInstallTransientCooldown(true, 60));
            AssertEqual(
                "fractional Retry-After rounds up",
                2,
                LlmTransportPolicy.NormalizeRetryAfterSeconds(1.01d));
            AssertEqual(
                "extreme Retry-After saturates before integer conversion",
                int.MaxValue,
                LlmTransportPolicy.NormalizeRetryAfterSeconds(double.MaxValue));
            DateTime cooldownNow = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc);
            AssertFalse(
                "older in-flight success preserves active sibling cooldown",
                LlmTransportPolicy.ShouldClearCooldownAfterSuccess(
                    cooldownNow.AddSeconds(30),
                    cooldownNow));
            AssertTrue(
                "success may clear cooldown exactly at expiry",
                LlmTransportPolicy.ShouldClearCooldownAfterSuccess(
                    cooldownNow,
                    cooldownNow));
            AssertTrue(
                "success clears already expired cooldown history",
                LlmTransportPolicy.ShouldClearCooldownAfterSuccess(
                    cooldownNow.AddSeconds(-1),
                    cooldownNow));
            AssertFalse("cooling generation lane is never attempted", LlmTransportPolicy.MayAttemptLane(true));
            AssertTrue("ready generation lane may be attempted", LlmTransportPolicy.MayAttemptLane(false));
            AssertTrue(
                "staggered queued request starts beside one active worker",
                LlmTransportPolicy.ShouldStartDispatchWorker(1, 1, 64));
            AssertFalse(
                "empty queue does not start a worker",
                LlmTransportPolicy.ShouldStartDispatchWorker(0, 1, 64));
            AssertFalse(
                "worker cap remains hard",
                LlmTransportPolicy.ShouldStartDispatchWorker(1, 64, 64));
            AssertEqual(
                "deadline-only cancellation is transient timeout",
                TransportCancellationCause.Deadline,
                LlmTransportPolicy.ClassifyCancellation(true, false, false, false));
            AssertEqual(
                "caller cancellation wins a deadline race",
                TransportCancellationCause.External,
                LlmTransportPolicy.ClassifyCancellation(true, true, false, false));
            AssertEqual(
                "session cancellation is external",
                TransportCancellationCause.External,
                LlmTransportPolicy.ClassifyCancellation(true, false, true, false));
            AssertEqual(
                "session replacement is external",
                TransportCancellationCause.External,
                LlmTransportPolicy.ClassifyCancellation(false, false, false, true));
            AssertEqual(
                "transport-originated cancellation remains transient transport failure",
                TransportCancellationCause.Transport,
                LlmTransportPolicy.ClassifyCancellation(false, false, false, false));

            AssertTrue("HTTP 408 is transient", LlmTransportPolicy.IsTransientStatusCode(408));
            AssertTrue("HTTP 429 is transient", LlmTransportPolicy.IsTransientStatusCode(429));
            AssertTrue("HTTP 503 is transient", LlmTransportPolicy.IsTransientStatusCode(503));
            AssertFalse("HTTP 401 is permanent", LlmTransportPolicy.IsTransientStatusCode(401));

            DateTime enqueued = new DateTime(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);
            DateTime deadline = LlmTransportPolicy.CreateDeadlineUtc(enqueued, 2);
            AssertEqual(
                "deadline applies defensive five-second floor",
                enqueued.AddSeconds(LlmTransportPolicy.MinimumTimeoutSeconds),
                deadline);
            AssertEqual(
                "queue residence consumes request budget",
                TimeSpan.FromSeconds(2),
                LlmTransportPolicy.Remaining(deadline, enqueued.AddSeconds(3)));
            AssertEqual(
                "expired deadline has no negative remainder",
                TimeSpan.Zero,
                LlmTransportPolicy.Remaining(deadline, deadline.AddMilliseconds(1)));
        }

        private static void TestExactSecretRedaction()
        {
            const string exactKey = "sk-exact+token/with=punctuation";
            string bareEcho = "provider echoed " + exactKey + " without an auth prefix";
            string bareRedacted = ApiLaneLabels.RedactSecrets(bareEcho, exactKey, "X-Private-Auth");
            AssertFalse("bare exact API key is removed", bareRedacted.Contains(exactKey));
            AssertTrue("bare exact API key gets marker", bareRedacted.Contains("<redacted>"));

            string headerEcho = "X-Private-Auth: \"server-normalized-secret\"";
            string headerRedacted = ApiLaneLabels.RedactSecrets(headerEcho, exactKey, "X-Private-Auth");
            AssertFalse("custom-header value is removed", headerRedacted.Contains("server-normalized-secret"));
            AssertTrue("custom-header name remains diagnostic", headerRedacted.Contains("X-Private-Auth:"));

            string jsonHeaderEcho = "{\"X-Private-Auth\":\"json-normalized-secret\",\"error\":\"denied\"}";
            string jsonHeaderRedacted = ApiLaneLabels.RedactSecrets(
                jsonHeaderEcho,
                exactKey,
                "X-Private-Auth");
            AssertFalse(
                "quoted JSON custom-header value is removed",
                jsonHeaderRedacted.Contains("json-normalized-secret"));
            AssertTrue(
                "redacting one JSON header preserves following diagnostics",
                jsonHeaderRedacted.Contains("\"error\":\"denied\""));

            string connectionSample = "Connected " + exactKey
                + "; X-Private-Auth: provider-normalized-secret";
            string safeConnectionSample = ApiLaneLabels.RedactSecrets(
                connectionSample,
                exactKey,
                "X-Private-Auth");
            AssertFalse(
                "successful connection sample cannot expose exact API key",
                safeConnectionSample.Contains(exactKey));
            AssertFalse(
                "successful connection status cannot expose custom-header value",
                safeConnectionSample.Contains("provider-normalized-secret"));

            string generic = "GET /v1?key=query-secret Authorization: Bearer bearer-secret";
            string genericRedacted = ApiLaneLabels.RedactSecrets(generic);
            AssertFalse("query secret removed", genericRedacted.Contains("query-secret"));
            AssertFalse("bearer secret removed", genericRedacted.Contains("bearer-secret"));

            string longError = exactKey + new string('x', 250);
            string trimmed = ApiLaneLabels.TrimForLog(longError, exactKey, "X-Private-Auth");
            AssertFalse("exact secret is redacted before diagnostic truncation", trimmed.Contains(exactKey));
            AssertTrue("diagnostic still respects shared length cap", trimmed.Length <= 183);
        }

        private static async Task CompletesSoon(string name, Task task)
        {
            Task winner = await Task.WhenAny(task, Task.Delay(1000));
            AssertTrue(name, ReferenceEquals(winner, task));
            await task;
        }

        private static async Task AssertCancelled(string name, Task task)
        {
            try
            {
                await task;
                throw new InvalidOperationException("Assertion failed: " + name + " (task completed)");
            }
            catch (TaskCanceledException)
            {
                assertions++;
            }
        }

        private static void AssertTrue(string name, bool value)
        {
            if (!value)
            {
                throw new InvalidOperationException("Assertion failed: " + name);
            }

            assertions++;
        }

        private static void AssertFalse(string name, bool value)
        {
            AssertTrue(name, !value);
        }

        private static void AssertEqual<T>(string name, T expected, T actual)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    "Assertion failed: " + name + " expected=" + expected + " actual=" + actual);
            }

            assertions++;
        }
    }
}

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

        private static void TestTransportPolicy()
        {
            AssertEqual("concurrency floors at one", 1, LlmTransportPolicy.NormalizeConcurrency(0, 16));
            AssertEqual("concurrency preserves configured value", 7, LlmTransportPolicy.NormalizeConcurrency(7, 16));
            AssertEqual("concurrency clamps to cap", 16, LlmTransportPolicy.NormalizeConcurrency(99, 16));
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

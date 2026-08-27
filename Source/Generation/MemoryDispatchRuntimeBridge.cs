// MemoryDispatchRuntimeBridge.cs — bounded thread handoff for M2 invocation permits and receipts.
//
// Background HTTP workers may ask for permission and report an attempt, but only the main-thread
// DiaryGameComponent may inspect or mutate saved memory state. The worker awaits each reply, which
// makes permit-before-SendAsync and receipt-before-retry/result ordering explicit without passing a
// Pawn, Verse object, saved row, credential, or response body across this boundary.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PawnDiary
{
    /// <summary>Detached identity attached only to a frozen memory-aware transport request.</summary>
    internal sealed class MemoryDispatchTransportContext
    {
        public string logicalRequestId = string.Empty;
        public string logicalRequestKey = string.Empty;
        public string requestPurposeToken = string.Empty;
        public string eventIdOrOpportunityKey = string.Empty;
        public string povRoleToken = string.Empty;
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string evidenceEpochToken = string.Empty;
        public long ownerCancellationGeneration;
        public long globalCancellationGeneration;
        public long optionalRequestInvalidationGeneration;
        public string primaryVariantKey = string.Empty;
        public Dictionary<ApiLaneIdentity, string> laneVariantKeys =
            new Dictionary<ApiLaneIdentity, string>();

        public string VariantKeyFor(ApiLaneIdentity lane)
        {
            string value;
            if (!lane.Empty && laneVariantKeys != null
                && laneVariantKeys.TryGetValue(lane, out value))
            {
                return value ?? string.Empty;
            }

            return primaryVariantKey ?? string.Empty;
        }

        public bool IsValidFor(LlmGenerationRequest request)
        {
            return request != null
                && !string.IsNullOrWhiteSpace(logicalRequestId)
                && !string.IsNullOrWhiteSpace(logicalRequestKey)
                && MemoryDispatchTokens.IsPurpose(requestPurposeToken)
                && !string.IsNullOrWhiteSpace(eventIdOrOpportunityKey)
                && !string.IsNullOrWhiteSpace(povRoleToken)
                && !string.IsNullOrWhiteSpace(ownerPawnId)
                && !string.IsNullOrWhiteSpace(ownerEpochToken)
                && !string.IsNullOrWhiteSpace(evidenceEpochToken)
                && ownerCancellationGeneration >= 0
                && globalCancellationGeneration >= 0
                && optionalRequestInvalidationGeneration >= 0
                && string.Equals(request.eventId, eventIdOrOpportunityKey,
                    StringComparison.Ordinal)
                && DiaryEvent.RoleEquals(request.povRole, povRoleToken)
                && !string.IsNullOrWhiteSpace(VariantKeyFor(
                    ApiLaneIdentity.ForGate(
                        request.endpointUrl,
                        request.modelName,
                        request.apiMode,
                        request.authMode,
                        request.customAuthHeaderName,
                        request.apiKey)));
        }
    }

    /// <summary>One worker request awaiting a saved-state invocation transaction.</summary>
    internal sealed class MemoryInvocationPermitRequest
    {
        internal readonly long sessionId;
        internal readonly MemoryDispatchTransportContext context;
        internal readonly string variantKey;
        internal readonly string systemPrompt;
        internal readonly string userPrompt;
        internal readonly string attemptOriginToken;
        internal readonly int predecessorAttemptOrdinal;
        internal readonly TaskCompletionSource<MemoryInvocationCommitPermitV1> completion =
            new TaskCompletionSource<MemoryInvocationCommitPermitV1>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        internal MemoryInvocationPermitRequest(
            long sessionId,
            MemoryDispatchTransportContext context,
            string variantKey,
            string systemPrompt,
            string userPrompt,
            string attemptOriginToken,
            int predecessorAttemptOrdinal)
        {
            this.sessionId = sessionId;
            this.context = context;
            this.variantKey = variantKey ?? string.Empty;
            this.systemPrompt = systemPrompt ?? string.Empty;
            this.userPrompt = userPrompt ?? string.Empty;
            this.attemptOriginToken = attemptOriginToken ?? string.Empty;
            this.predecessorAttemptOrdinal = predecessorAttemptOrdinal;
        }
    }

    /// <summary>One invoked attempt awaiting its ordered main-thread receipt transaction.</summary>
    internal sealed class MemoryInvocationReceiptRequest
    {
        internal readonly long sessionId;
        internal readonly MemoryInvocationCommitPermitV1 permit;
        internal readonly string outcomeToken;
        internal readonly bool providerReturnedUsableResult;
        internal readonly TaskCompletionSource<bool> completion =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal MemoryInvocationReceiptRequest(
            long sessionId,
            MemoryInvocationCommitPermitV1 permit,
            string outcomeToken,
            bool providerReturnedUsableResult)
        {
            this.sessionId = sessionId;
            this.permit = permit;
            this.outcomeToken = outcomeToken ?? MemoryDispatchTokens.Invalid;
            this.providerReturnedUsableResult = providerReturnedUsableResult;
        }
    }

    /// <summary>
    /// Process-static, session-fenced bridge. Its queues are bounded by the transport's staged and
    /// active request ceiling because every worker can own at most one outstanding handoff.
    /// </summary>
    internal static class MemoryDispatchRuntimeBridge
    {
        private static readonly ConcurrentQueue<MemoryInvocationPermitRequest> PermitRequests =
            new ConcurrentQueue<MemoryInvocationPermitRequest>();
        private static readonly ConcurrentQueue<MemoryInvocationReceiptRequest> ReceiptRequests =
            new ConcurrentQueue<MemoryInvocationReceiptRequest>();
        private static readonly ConcurrentDictionary<string, MemoryRuntimeSendEnvelope>
            SendEnvelopesByPermitFingerprint =
                new ConcurrentDictionary<string, MemoryRuntimeSendEnvelope>(StringComparer.Ordinal);
        private static readonly object SessionFenceLock = new object();
        private static long rejectedThroughSessionId;

        internal static async Task<MemoryInvocationCommitPermitV1> RequestPermitAsync(
            LlmGenerationRequest request,
            string attemptOriginToken,
            int predecessorAttemptOrdinal,
            CancellationToken cancellationToken)
        {
            MemoryDispatchTransportContext context = request?.memoryDispatch;
            if (context == null) return null;
            if (!context.IsValidFor(request)
                || !MemoryDispatchTokens.IsAttemptOrigin(attemptOriginToken))
            {
                return null;
            }

            MemoryInvocationPermitRequest pending = new MemoryInvocationPermitRequest(
                request.sessionId,
                context,
                context.VariantKeyFor(ApiLaneIdentity.ForGate(
                    request.endpointUrl,
                    request.modelName,
                    request.apiMode,
                    request.authMode,
                    request.customAuthHeaderName,
                    request.apiKey)),
                request.systemPrompt,
                request.rawText,
                attemptOriginToken,
                predecessorAttemptOrdinal);
            lock (SessionFenceLock)
            {
                if (cancellationToken.IsCancellationRequested
                    || request.sessionId <= rejectedThroughSessionId) return null;
                PermitRequests.Enqueue(pending);
            }
            return await AwaitReply(pending.completion, cancellationToken);
        }

        internal static async Task<bool> PublishReceiptAsync(
            long sessionId,
            MemoryInvocationCommitPermitV1 permit,
            string outcomeToken,
            bool providerReturnedUsableResult,
            CancellationToken cancellationToken)
        {
            if (!MemoryDispatchPolicy.PermitFingerprintIsValid(permit)
                || !MemoryDispatchTokens.IsTerminalOutcome(outcomeToken))
            {
                return false;
            }

            MemoryInvocationReceiptRequest pending = new MemoryInvocationReceiptRequest(
                sessionId, permit, outcomeToken, providerReturnedUsableResult);
            lock (SessionFenceLock)
            {
                if (cancellationToken.IsCancellationRequested
                    || sessionId <= rejectedThroughSessionId) return false;
                ReceiptRequests.Enqueue(pending);
            }
            // Keep the physical-send claim across both accepted and rejected receipt handoffs.
            // Accepted claims leave only after result application; stale claims leave through the
            // main-thread logical-request/session fence. A mismatched duplicate receipt must never
            // reopen a same-permit send window merely because its callback was rejected.
            return await AwaitReply(pending.completion, cancellationToken);
        }

        /// <summary>
        /// Shares one compare-exchange owner for every equal permit while its result is outstanding.
        /// The bounded transport lifecycle removes the entry after terminal result application or
        /// session replacement, so duplicate scheduling cannot manufacture a fresh claim object.
        /// </summary>
        internal static MemoryRuntimeSendEnvelope GetOrCreateSendEnvelope(
            MemoryInvocationCommitPermitV1 permit)
        {
            if (!MemoryDispatchPolicy.PermitFingerprintIsValid(permit)) return null;
            lock (SessionFenceLock)
            {
                if (permit.sessionId <= rejectedThroughSessionId) return null;
                return SendEnvelopesByPermitFingerprint.GetOrAdd(
                    permit.permitFingerprint,
                    ignored => new MemoryRuntimeSendEnvelope(permit));
            }
        }

        /// <summary>
        /// Releases an acknowledged claim only after the main thread has terminally applied the
        /// matching result and removed its active saved row. Receipt acknowledgment alone is too
        /// early: equal duplicate work must remain unable to manufacture a fresh envelope during
        /// the receipt-to-result handoff.
        /// </summary>
        internal static void ReleaseSendEnvelope(MemoryInvocationCommitPermitV1 permit)
        {
            if (permit == null || string.IsNullOrEmpty(permit.permitFingerprint)) return;
            MemoryRuntimeSendEnvelope ignored;
            SendEnvelopesByPermitFingerprint.TryRemove(
                permit.permitFingerprint, out ignored);
        }

        /// <summary>Releases every runtime claim for a logical request after a main-thread fence
        /// (for example Brainwipe) has made all of its permits permanently stale.</summary>
        internal static void ReleaseLogicalRequestSendEnvelopes(string logicalRequestId)
        {
            if (string.IsNullOrWhiteSpace(logicalRequestId)) return;
            foreach (KeyValuePair<string, MemoryRuntimeSendEnvelope> pair
                in SendEnvelopesByPermitFingerprint)
            {
                if (!string.Equals(
                    pair.Value?.permit?.logicalRequestId,
                    logicalRequestId,
                    StringComparison.Ordinal)) continue;
                MemoryRuntimeSendEnvelope ignored;
                SendEnvelopesByPermitFingerprint.TryRemove(pair.Key, out ignored);
            }
        }

        internal static bool TryDequeuePermit(out MemoryInvocationPermitRequest request)
        {
            return PermitRequests.TryDequeue(out request);
        }

        internal static bool TryDequeueReceipt(out MemoryInvocationReceiptRequest request)
        {
            return ReceiptRequests.TryDequeue(out request);
        }

        internal static void ResolvePermit(
            MemoryInvocationPermitRequest request,
            MemoryInvocationCommitPermitV1 permit)
        {
            request?.completion.TrySetResult(permit);
        }

        internal static void ResolveReceipt(MemoryInvocationReceiptRequest request, bool accepted)
        {
            request?.completion.TrySetResult(accepted);
        }

        /// <summary>Rejects queued handoffs from replaced sessions so no worker remains stranded.</summary>
        internal static void RejectSession(long sessionId)
        {
            lock (SessionFenceLock)
            {
                if (sessionId > rejectedThroughSessionId)
                    rejectedThroughSessionId = sessionId;

                List<MemoryInvocationPermitRequest> keepPermits =
                    new List<MemoryInvocationPermitRequest>();
                MemoryInvocationPermitRequest permit;
                while (PermitRequests.TryDequeue(out permit))
                {
                    if (permit.sessionId <= rejectedThroughSessionId)
                        permit.completion.TrySetResult(null);
                    else keepPermits.Add(permit);
                }
                for (int index = 0; index < keepPermits.Count; index++)
                    PermitRequests.Enqueue(keepPermits[index]);

                List<MemoryInvocationReceiptRequest> keepReceipts =
                    new List<MemoryInvocationReceiptRequest>();
                MemoryInvocationReceiptRequest receipt;
                while (ReceiptRequests.TryDequeue(out receipt))
                {
                    if (receipt.sessionId <= rejectedThroughSessionId)
                        receipt.completion.TrySetResult(false);
                    else keepReceipts.Add(receipt);
                }
                for (int index = 0; index < keepReceipts.Count; index++)
                    ReceiptRequests.Enqueue(keepReceipts[index]);

                foreach (KeyValuePair<string, MemoryRuntimeSendEnvelope> pair
                    in SendEnvelopesByPermitFingerprint)
                {
                    if ((pair.Value?.permit?.sessionId ?? long.MaxValue)
                        > rejectedThroughSessionId) continue;
                    MemoryRuntimeSendEnvelope ignored;
                    SendEnvelopesByPermitFingerprint.TryRemove(pair.Key, out ignored);
                }
            }
        }

        private static async Task<T> AwaitReply<T>(
            TaskCompletionSource<T> completion,
            CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(() => completion.TrySetCanceled()))
            {
                try
                {
                    return await completion.Task;
                }
                catch (OperationCanceledException)
                {
                    return default(T);
                }
            }
        }
    }
}

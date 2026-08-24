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
                    ApiLaneIdentity.ForGeneration(
                        request.endpointUrl, request.modelName, request.apiMode)));
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
                context.VariantKeyFor(ApiLaneIdentity.ForGeneration(
                    request.endpointUrl, request.modelName, request.apiMode)),
                request.systemPrompt,
                request.rawText,
                attemptOriginToken,
                predecessorAttemptOrdinal);
            PermitRequests.Enqueue(pending);
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
            ReceiptRequests.Enqueue(pending);
            return await AwaitReply(pending.completion, cancellationToken);
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
            List<MemoryInvocationPermitRequest> keepPermits =
                new List<MemoryInvocationPermitRequest>();
            MemoryInvocationPermitRequest permit;
            while (PermitRequests.TryDequeue(out permit))
            {
                if (permit.sessionId == sessionId) permit.completion.TrySetResult(null);
                else keepPermits.Add(permit);
            }
            for (int index = 0; index < keepPermits.Count; index++)
                PermitRequests.Enqueue(keepPermits[index]);

            List<MemoryInvocationReceiptRequest> keepReceipts =
                new List<MemoryInvocationReceiptRequest>();
            MemoryInvocationReceiptRequest receipt;
            while (ReceiptRequests.TryDequeue(out receipt))
            {
                if (receipt.sessionId == sessionId) receipt.completion.TrySetResult(false);
                else keepReceipts.Add(receipt);
            }
            for (int index = 0; index < keepReceipts.Count; index++)
                ReceiptRequests.Enqueue(keepReceipts[index]);
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

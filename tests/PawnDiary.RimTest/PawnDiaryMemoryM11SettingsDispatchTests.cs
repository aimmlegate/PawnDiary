// PawnDiaryMemoryM11SettingsDispatchTests.cs — loaded-process M11 settings publication and safe
// transport-boundary tests. The suite never writes ModSettings and never starts an HTTP session.
//
// It compares the mutable settings adapter to the immutable publication currently consumed by the
// loaded component, then exercises the bounded stage/activate/cancel FIFO and redacted setup failure.
using System;
using RimTestRedux;

namespace PawnDiary.RimTests
{
    /// <summary>Committed policy handshake and network-free dispatch transaction boundaries.</summary>
    [TestSuite]
    public static class PawnDiaryMemoryM11SettingsDispatchTests
    {
        /// <summary>
        /// Loaded settings, process publication, and per-save applied fingerprint agree; capture and
        /// recall gates classify every category and an invalid token without changing player settings.
        /// </summary>
        [Test]
        public static void LoadedPolicyPublicationAndComponentGatesAgree()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            DiaryGameComponent component = DiaryGameComponent.Instance;
            Require(settings != null && component != null,
                "The settings/policy fixture requires a loaded game.");

            MemoryPolicySnapshot expected = MemoryPolicyNormalizer.Normalize(
                settings.memorySettingsSchemaVersion,
                settings.MemoryPolicyFields(),
                MemoryPolicyDefAdapter.Bounds());
            MemoryPolicySnapshot published = MemoryEffectivePolicyProvider.Current;
            Require(published != null
                    && expected.settingsSchemaVersion == published.settingsSchemaVersion
                    && expected.fingerprint == published.fingerprint
                    && MemoryEffectivePolicyProvider.PublicationRevision > 0
                    && component.MemoryPolicyIsReconciled(),
                "Settings, immutable publication, and the loaded save fingerprint diverged.");

            RequireGate(component, published,
                MemoryContractTokens.CategoryPersonal, MemoryCategoryBits.Personal);
            RequireGate(component, published,
                MemoryContractTokens.CategoryRelationships, MemoryCategoryBits.Relationships);
            RequireGate(component, published,
                MemoryContractTokens.CategoryFamily, MemoryCategoryBits.Family);
            RequireGate(component, published,
                MemoryContractTokens.CategoryFactions, MemoryCategoryBits.Factions);
            string captureStatus;
            string recallStatus;
            Require(!component.MemoryCategoryAllowsCapture(
                        "rimtest.invalid.category", out captureStatus)
                    && captureStatus == "InvalidCategory"
                    && !component.MemoryCategoryAllowsRecall(
                        "rimtest.invalid.category", out recallStatus)
                    && recallStatus == "InvalidCategory",
                "Unknown categories did not fail closed at both loaded consumers.");

            MemorySettingsPolicyFieldsV1 prior = published.ToFields();
            MemorySettingsPolicyFieldsV1 draft = MemoryPolicyNormalizer.Copy(prior);
            draft.memoryCategoryMask ^= MemoryCategoryBits.Family;
            bool priorOptional = prior.useMemoriesInWriting
                && prior.allowExtraMemoryAiRequests
                && prior.optionalRequestInvalidationGeneration > 0
                && prior.optionalRequestInvalidationGeneration < long.MaxValue;
            draft.useMemoriesInWriting = !priorOptional;
            draft.allowExtraMemoryAiRequests = !priorOptional;
            MemorySettingsCommitPlan plan = MemoryPolicyNormalizer.PrepareCommit(
                MemoryPolicyNormalizer.CurrentSettingsSchemaVersion,
                prior,
                draft,
                MemoryPolicyDefAdapter.Bounds());
            Require(plan.valid
                    && (plan.changedCaptureMask & MemoryCategoryBits.Family) != 0
                    && plan.candidate.captureInvalidationGenerationFamily
                        != prior.captureInvalidationGenerationFamily
                    && plan.candidate.captureInvalidationGenerationPersonal
                        == prior.captureInvalidationGenerationPersonal
                    && plan.candidate.captureInvalidationGenerationRelationships
                        == prior.captureInvalidationGenerationRelationships
                    && plan.candidate.captureInvalidationGenerationFactions
                        == prior.captureInvalidationGenerationFactions
                    && plan.optionalGenerationChanged
                    && plan.candidate.optionalRequestInvalidationGeneration
                        != prior.optionalRequestInvalidationGeneration,
                "Detached commit planning did not advance only the changed invalidation domains.");
            Require(MemoryEffectivePolicyProvider.Current.fingerprint == published.fingerprint
                    && component.MemoryPolicyIsReconciled(),
                "Detached settings planning unexpectedly published or reconciled a draft.");
        }

        /// <summary>
        /// Capacity is reserved before visibility, activation preserves FIFO, cancellation releases
        /// physical nodes, and setup failure returns exact logical identity with secrets redacted.
        /// </summary>
        [Test]
        public static void DispatchStageActivateCancelAndFailureAreBounded()
        {
            BoundedTransportQueue<string> queue = new BoundedTransportQueue<string>(2);
            StagedTransportQueueItem<string> first = null;
            StagedTransportQueueItem<string> second = null;
            StagedTransportQueueItem<string> refused = null;
            Require(queue.TryStage("first", out first)
                    && queue.TryStage("second", out second)
                    && !queue.TryStage("overflow", out refused)
                    && queue.Count == 0
                    && queue.ReservedCount == 2
                    && queue.PhysicalCount == 0,
                "Staging was visible, unbounded, or created a physical FIFO node too early.");
            Require(queue.Activate(first)
                    && queue.Activate(second)
                    && !queue.Activate(first)
                    && queue.Count == 2
                    && queue.ReservedCount == 2
                    && queue.PhysicalCount == 2,
                "Activation was not exactly-once or did not publish the reserved FIFO.");
            string dequeued;
            Require(queue.TryDequeue(out dequeued)
                    && dequeued == "first"
                    && queue.Count == 1
                    && queue.ReservedCount == 1,
                "The active transport queue lost FIFO ordering or capacity accounting.");
            Require(queue.Cancel(second)
                    && !queue.Cancel(second)
                    && !queue.TryDequeue(out dequeued)
                    && queue.Count == 0
                    && queue.ReservedCount == 0
                    && queue.PhysicalCount == 0,
                "Cancellation retained a tombstone, leaked capacity, or remained repeatable.");

            MemoryDispatchTransportContext dispatch = new MemoryDispatchTransportContext
            {
                logicalRequestId = "memory-logical-rimtest"
            };
            LlmGenerationRequest request = new LlmGenerationRequest
            {
                eventId = "rimtest-event",
                povRole = "initiator",
                sessionId = 77,
                apiKey = "rimtest-secret-key",
                authMode = ApiAuthMode.BearerToken,
                memoryDispatch = dispatch
            };
            LlmGenerationResult failed = LlmClient.CreateDispatchSetupFailureResult(
                request,
                new InvalidOperationException(
                    "Bearer rimtest-secret-key could not initialize"));
            Require(!failed.success
                    && failed.eventId == request.eventId
                    && failed.povRole == request.povRole
                    && failed.sessionId == request.sessionId
                    && failed.memoryLogicalRequestId == dispatch.logicalRequestId
                    && failed.memoryDispatchTerminalFailure
                    && failed.error.IndexOf(
                        "rimtest-secret-key", StringComparison.Ordinal) < 0,
                "A setup failure lost dispatch identity or exposed its credential.");
        }

        private static void RequireGate(
            DiaryGameComponent component,
            MemoryPolicySnapshot policy,
            string category,
            int bit)
        {
            string captureStatus;
            string recallStatus;
            bool capture = component.MemoryCategoryAllowsCapture(category, out captureStatus);
            bool recall = component.MemoryCategoryAllowsRecall(category, out recallStatus);
            Require(capture == policy.AllowsCapture(bit)
                    && recall == policy.AllowsRecall(bit)
                    && captureStatus == (capture ? "Allowed" : "Disabled")
                    && recallStatus == (recall ? "Allowed" : "Disabled"),
                "Loaded category gates disagreed for '" + category + "'.");
        }

        private static void Require(bool condition, string message)
        {
            PawnDiaryMemoryM11RuntimeFixture.Require(condition, message);
        }
    }
}

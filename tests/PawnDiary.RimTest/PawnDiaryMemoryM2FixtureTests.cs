// PawnDiaryMemoryM2FixtureTests.cs — LOADED RimTest coverage for the dormant M2 dispatch envelope.
//
// COMPILE-ONLY FOR AGENTS. These fixtures use real Scribe when the user next runs RimTest, but they
// never contact a provider: request/permit transitions remain detached and no HTTP worker is started.
using System;
using System.Collections.Generic;
using System.IO;
using PawnDiary;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    public static class PawnDiaryMemoryM2FixtureTests
    {
        [Test]
        public static void DiaryEventAcceptedPromptAndActiveRequestRoundTrip()
        {
            SavedActiveLogicalRequestV1 active = NewSavedRequest();
            DiaryEvent source = new DiaryEvent
            {
                eventId = "Event_M2_Scribe",
                tick = 123,
                date = "1st of Aprimay",
                interactionDefName = "Chat",
                interactionLabel = "chat",
                gameContext = "fixture=true",
                instruction = "fixture",
                colorCue = DiaryEvent.WhiteColorCue,
                solo = true,
                initiatorPawnId = "Pawn_M2"
            };
            source.SetAcceptedPromptPair(
                DiaryEvent.InitiatorRole,
                "exact system\r\nfixture",
                "exact user\nfixture");
            source.SetActiveMemoryLogicalRequestForRole(DiaryEvent.InitiatorRole, active);

            RunWithTempFile(path =>
            {
                DiaryEvent saved = source;
                SaveWithScribe(path, () => Scribe_Deep.Look(ref saved, "event"));
                DiaryEvent loaded = null;
                LoadVarsWithScribe(path, () => Scribe_Deep.Look(ref loaded, "event"));

                Require(loaded != null, "M2 DiaryEvent did not round-trip.");
                Require(loaded.AcceptedSystemPromptForRole(DiaryEvent.InitiatorRole)
                        == "exact system\r\nfixture"
                    && loaded.PromptForRole(DiaryEvent.InitiatorRole)
                        == "exact user\nfixture",
                    "The exact accepted prompt pair changed through Scribe.");
                SavedActiveLogicalRequestV1 loadedRequest =
                    loaded.ActiveMemoryLogicalRequestForRole(DiaryEvent.InitiatorRole);
                Require(loadedRequest != null
                        && loadedRequest.logicalRequestId == active.logicalRequestId
                        && loadedRequest.frozenVariants.Count == 1,
                    "The POV-owned active request did not round-trip.");
                Require(MemoryDispatchPolicy.ValidateRequest(
                        MemoryDispatchSavedAdapter.ToSnapshot(loadedRequest)),
                    "The round-tripped active request no longer validates.");
            });
        }

        [Test]
        public static void SavedAdapterCommitsPermitAndReceiptBeforeResult()
        {
            SavedActiveLogicalRequestV1 saved = NewSavedRequest();
            Require(saved.requestStateToken == MemoryRequestStateMachineContracts.Staged,
                "Fixture must begin staged.");
            Require(MemoryDispatchSavedAdapter.TryActivate(saved),
                "A valid committed row did not activate.");

            SavedActiveLogicalAttemptV1 attempt;
            Require(MemoryDispatchSavedAdapter.TryPrepareAttempt(
                    saved,
                    saved.frozenVariants[0].variantKey,
                    MemoryDispatchTokens.Initial,
                    0,
                    out attempt),
                "Initial physical attempt did not prepare.");
            MemoryDispatchFenceSnapshot fence = Fence(saved);
            MemoryInvocationCommitPlan invocation;
            Require(MemoryDispatchSavedAdapter.TryCommitInvocation(
                    saved, 1, fence, 0, 456, out invocation),
                "Invocation permit transaction did not commit.");
            Require(invocation.permit != null
                    && MemoryDispatchPolicy.PermitFingerprintIsValid(invocation.permit),
                "Committed permit fingerprint is invalid.");
            Require(!MemoryDispatchSavedAdapter.MarkResultApplied(saved, 1),
                "Result publication was allowed before its receipt.");
            Require(MemoryDispatchSavedAdapter.MarkReceiptApplied(saved, 1),
                "Invocation receipt did not apply.");
            Require(MemoryDispatchSavedAdapter.MarkReceiptApplied(saved, 1),
                "Idempotent receipt replay was rejected.");
            Require(MemoryDispatchSavedAdapter.MarkResultApplied(saved, 1),
                "Result did not publish after receipt.");
            Require(!MemoryDispatchSavedAdapter.MarkResultApplied(saved, 1),
                "Duplicate result publication was not rejected.");
        }

        [Test]
        public static void BrainwipeFenceRejectsOldPermitAndLoadedRowsNeverResend()
        {
            SavedActiveLogicalRequestV1 saved = NewSavedRequest();
            Require(MemoryDispatchSavedAdapter.TryActivate(saved), "Fixture activation failed.");
            SavedActiveLogicalAttemptV1 attempt;
            Require(MemoryDispatchSavedAdapter.TryPrepareAttempt(
                    saved,
                    saved.frozenVariants[0].variantKey,
                    MemoryDispatchTokens.Initial,
                    0,
                    out attempt),
                "Fixture attempt preparation failed.");
            MemoryDispatchFenceSnapshot oldFence = Fence(saved);
            MemoryInvocationCommitPlan invocation;
            Require(MemoryDispatchSavedAdapter.TryCommitInvocation(
                    saved, 1, oldFence, 0, 789, out invocation),
                "Fixture invocation failed.");

            MemoryDispatchFenceSnapshot postWipeFence = Fence(saved);
            postWipeFence.ownerEpochToken = EpochToken(99);
            postWipeFence.ownerCancellationGeneration++;
            MemoryTerminalCallbackPlan stale =
                MemoryDispatchSavedAdapter.PlanTerminalCallback(
                    saved,
                    invocation.permit,
                    postWipeFence,
                    MemoryDispatchTokens.Success,
                    true);
            Require(!stale.accepted && stale.outcomeToken == MemoryDispatchTokens.Stale,
                "An old-epoch result survived the Brainwipe fence.");

            MemoryLoadSettlementPlan load = MemoryDispatchPolicy.PlanLoadedRequestSettlement(
                MemoryDispatchSavedAdapter.ToSnapshot(saved));
            Require(load.valid && load.hadCommittedInvocation
                    && !load.restoreNormalPovRetryable
                    && load.outcomeToken
                        == MemoryDispatchTokens.LoadInterruptedAfterInvocation,
                "A loaded invoked request was not terminally settled without resend.");
        }

        private static SavedActiveLogicalRequestV1 NewSavedRequest()
        {
            MemoryLogicalRequestSnapshot request = new MemoryLogicalRequestSnapshot
            {
                logicalRequestSequence = 31,
                requestPurposeToken = MemoryDispatchTokens.NormalDiary,
                sessionId = 4,
                eventIdOrOpportunityKey = "Event_M2",
                povRoleToken = DiaryEvent.InitiatorRole,
                ownerPawnId = "Pawn_M2",
                ownerEpochToken = EpochToken(31),
                ownerCancellationGeneration = 2,
                globalCancellationGeneration = 3,
                requestStateToken = MemoryRequestStateMachineContracts.Staged
            };
            Require(MemoryIdentityCodec.TryCreateLogicalRequestId(
                    request.logicalRequestSequence, out request.logicalRequestId),
                "Could not create request id.");
            MemoryEvidenceIdentity evidence = new MemoryEvidenceIdentity
            {
                recordId = "record-m2",
                sourceOccurrenceId = "source-m2",
                rootIdOrEmpty = "root-m2"
            };
            MemoryGuardIdentity guard = new MemoryGuardIdentity
            {
                guardKind = "record",
                guardKey = "guard-m2"
            };
            request.reservedEvidence.Add(evidence);
            request.reservedGuards.Add(guard);

            MemoryFrozenPromptVariantSnapshot variant =
                new MemoryFrozenPromptVariantSnapshot
                {
                    variantOrdinal = 0,
                    templateIdentity = "template-m2",
                    contextDetailIdentity = "detail-m2",
                    systemPrompt = "system-m2",
                    userPrompt = "user-m2"
                };
            variant.receipt.evidence.Add(evidence);
            variant.receipt.guards.Add(guard);
            Require(MemoryIdentityCodec.TryCreateEvidenceSetFingerprint(
                    variant.receipt.evidence,
                    out variant.receipt.evidenceSetFingerprint),
                "Could not create evidence fingerprint.");
            Require(MemoryIdentityCodec.TryCreateReceiptPlanFingerprint(
                    variant.receipt.evidence,
                    variant.receipt.guards,
                    out variant.receipt.receiptPlanFingerprint),
                "Could not create receipt fingerprint.");
            string diagnosticFingerprint;
            Require(MemoryIdentityCodec.TryCreateDiagnosticProvenanceFingerprint(
                    variant.diagnostics, out diagnosticFingerprint),
                "Could not create diagnostic fingerprint.");
            Require(MemoryIdentityCodec.TryCreatePromptVariantKey(
                    request.logicalRequestId,
                    0,
                    request.requestPurposeToken,
                    variant.templateIdentity,
                    variant.contextDetailIdentity,
                    variant.systemPrompt,
                    variant.userPrompt,
                    variant.receipt.receiptPlanFingerprint,
                    diagnosticFingerprint,
                    out variant.variantKey),
                "Could not create variant key.");
            request.variants.Add(variant);
            Require(MemoryIdentityCodec.TryCreateEvidenceEpochToken(
                    request.requestPurposeToken,
                    request.eventIdOrOpportunityKey,
                    request.povRoleToken,
                    request.ownerPawnId,
                    request.ownerEpochToken,
                    request.reservedEvidence,
                    request.reservedGuards,
                    out request.evidenceEpochToken),
                "Could not create evidence epoch.");
            Require(MemoryIdentityCodec.TryCreateLogicalRequestKey(
                    request.requestPurposeToken,
                    request.eventIdOrOpportunityKey,
                    request.povRoleToken,
                    request.ownerPawnId,
                    request.ownerEpochToken,
                    request.evidenceEpochToken,
                    out request.logicalRequestKey),
                "Could not create request key.");

            SavedActiveLogicalRequestV1 saved;
            Require(MemoryDispatchSavedAdapter.TryCreateSavedRequest(request, out saved),
                "Could not materialize valid saved request.");
            return saved;
        }

        private static MemoryDispatchFenceSnapshot Fence(SavedActiveLogicalRequestV1 request)
        {
            return new MemoryDispatchFenceSnapshot
            {
                sessionId = request.sessionId,
                ownerPawnId = request.ownerPawnId,
                ownerEpochToken = request.ownerEpochToken,
                ownerCancellationGeneration = request.ownerCancellationGeneration,
                globalCancellationGeneration = request.globalCancellationGeneration,
                optionalRequestInvalidationGeneration =
                    request.optionalRequestInvalidationGeneration
            };
        }

        private static string EpochToken(long sequence)
        {
            string value = sequence.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            return OrdinalSegmentCodec.Segment("memory-epoch-v1")
                + OrdinalSegmentCodec.Segment(value);
        }

        private static void RunWithTempFile(Action<string> body)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "pawndiary_mem_m2_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                body(path);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void SaveWithScribe(string path, Action expose)
        {
            SafeSaver.Save(path, "savegame", () => expose());
        }

        private static void LoadVarsWithScribe(string path, Action expose)
        {
            Scribe.loader.InitLoading(path);
            try
            {
                expose();
                Scribe.loader.FinalizeLoading();
            }
            catch
            {
                Scribe.ForceStop();
                throw;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }
    }
}

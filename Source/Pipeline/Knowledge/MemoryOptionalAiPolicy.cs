// MemoryOptionalAiPolicy.cs — pure M10 opportunity, result, and request-freezing rules.
//
// The game adapter copies committed memory/reflection state into these plain DTOs. This file owns
// exact opportunity identity, one-slot ranking, deterministic-first result validation, and immutable
// logical-request construction. It has no Verse, Pawn, Def, UI, transport, credential, or localization
// dependency; background workers receive only the strings frozen by the main-thread adapter.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PawnDiary
{
    /// <summary>Stable saved disposition vocabulary for optional memory wording.</summary>
    internal static class MemoryOptionalWordingDispositionTokens
    {
        public const string None = "none";
        public const string Pending = "pending";
        public const string Disabled = "disabled";
        public const string Displaced = "displaced";
        public const string Expired = "expired";
        public const string Activated = "activated";
        public const string Success = "success";
        public const string Failed = "failed";
        public const string Malformed = "malformed";

        public static bool IsKnown(string value)
        {
            return value == None || value == Pending || value == Activated
                || value == Success || value == Failed || value == Malformed
                || value == Expired || value == Displaced || value == Disabled;
        }
    }

    /// <summary>
    /// Detached exact contents of the shipped owner wording slot. Historical field names remain
    /// stable because Event/Landmark wording deliberately reuses the Summary save/wire contract.
    /// </summary>
    internal sealed class SummaryWordingOpportunitySnapshot
    {
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public long ownerCancellationGeneration;
        public long globalCancellationGeneration;
        public long optionalRequestInvalidationGeneration;
        public string rootId = string.Empty;
        public string summaryRecordId = string.Empty;
        public long expectedRootStructuralRevision;
        public long expectedSummaryFactsRevision;
        public int expectedReducerRevision;
        public long expectedFormatRevision;
        public int expectedCategoryMask;
        public string projectionFingerprint = string.Empty;
        public long requestedTick;
        public long dueTick;
        public long expiryTick;
        public int configuredPriority;
        public int salience;
        public string opportunityKey = string.Empty;
    }

    /// <summary>One opportunity settled while repairing, replacing, or expiring an owner slot.</summary>
    internal sealed class SummaryWordingTerminalDecision
    {
        public SummaryWordingOpportunitySnapshot opportunity;
        public string dispositionToken = string.Empty;
    }

    /// <summary>Pure one-owner opportunity-slot plan.</summary>
    internal sealed class SummaryWordingSlotPlan
    {
        public bool valid;
        public SummaryWordingOpportunitySnapshot winner;
        public List<SummaryWordingTerminalDecision> terminal =
            new List<SummaryWordingTerminalDecision>();
    }

    /// <summary>Current committed Summary identity used for activation and result revalidation.</summary>
    internal sealed class SummaryWordingCurrentSnapshot
    {
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string rootId = string.Empty;
        public string summaryRecordId = string.Empty;
        public long rootStructuralRevision;
        public long summaryFactsRevision;
        public int reducerRevision;
        public long formatRevision;
        public int categoryMask;
        public string projectionFingerprint = string.Empty;
        public bool suppressed;
        public string deterministicWording = string.Empty;
    }

    /// <summary>Pure terminal application result; deterministic wording is never replaced here.</summary>
    internal sealed class SummaryWordingResultPlan
    {
        public bool identityMatched;
        public bool applyOptionalWording;
        public string optionalWording = string.Empty;
        public string dispositionToken = MemoryOptionalWordingDispositionTokens.None;
    }

    /// <summary>
    /// Current immutable Event/Landmark projection used by the optional wording cache. The
    /// deterministic sentence is the complete transformation input; canonical facts stay outside
    /// this display-only DTO and remain authoritative.
    /// </summary>
    internal sealed class MemoryBlockWordingCurrentSnapshot
    {
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string rootId = string.Empty;
        public string recordId = string.Empty;
        public string kind = string.Empty;
        public string category = string.Empty;
        public string deterministicWording = string.Empty;
        public long wordingFormatRevision;
        public int categoryMask;
        public string projectionFingerprint = string.Empty;
        public bool suppressed;
        public bool playerEdited;
    }

    /// <summary>Pure terminal plan for disposable Event/Landmark prose.</summary>
    internal sealed class MemoryBlockWordingResultPlan
    {
        public bool identityMatched;
        public bool applyOptionalWording;
        public string optionalWording = string.Empty;
        public string dispositionToken = MemoryOptionalWordingDispositionTokens.None;
    }

    /// <summary>One frozen prompt variant input before canonical hashes are derived.</summary>
    internal sealed class MemoryOptionalPromptVariantInput
    {
        public string templateIdentity = string.Empty;
        public string contextDetailIdentity = string.Empty;
        public string systemPrompt = string.Empty;
        public string userPrompt = string.Empty;
        public List<MemoryEvidenceIdentity> evidence = new List<MemoryEvidenceIdentity>();
        public List<MemoryGuardIdentity> guards = new List<MemoryGuardIdentity>();
        public List<MemoryDiagnosticIdentity> diagnostics = new List<MemoryDiagnosticIdentity>();
    }

    /// <summary>
    /// Detached inputs for one logical request. The M10 optional adapter introduced this DTO; M11
    /// also uses the same immutable graph builder for normal diary variants carrying Recall-v2
    /// receipts, avoiding a second hashing/validation implementation.
    /// </summary>
    internal sealed class MemoryOptionalRequestBuildInput
    {
        public long logicalRequestSequence;
        public string requestPurposeToken = string.Empty;
        public long sessionId;
        public string opportunityKey = string.Empty;
        public string povRoleToken = string.Empty;
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public long ownerCancellationGeneration;
        public long globalCancellationGeneration;
        public long optionalRequestInvalidationGeneration;
        public List<MemoryOptionalPromptVariantInput> variants =
            new List<MemoryOptionalPromptVariantInput>();
    }

    /// <summary>Pure M10 rules. Every comparison is ordinal and every numeric encoding invariant.</summary>
    internal static class MemoryOptionalAiPolicy
    {
        private const string SummaryOpportunityDomain = "summary-wording-opportunity-v1";
        private const string BlockWordingProjectionDomain = "memory-block-wording-projection-v1";
        private const int MaximumSegmentCharacters = 4096;
        // Matches the defensive composite-key ceiling used by the shared identity codec. Keeping the
        // check here prevents an opportunity key from becoming an unbounded saved carrier.
        private const int MaximumCompositeKeyCharacters = 2048;

        /// <summary>
        /// True only for an event observed strictly after the current optional-work baseline. The
        /// strict boundary intentionally treats same-tick ordering as ambiguous and therefore skips
        /// the optional call instead of risking retrospective work.
        /// </summary>
        public static bool IsMeaningfulEventAfterEligibilityBaseline(
            long originalEventTick,
            long eligibilityBaselineTick)
        {
            return originalEventTick >= 0
                && eligibilityBaselineTick >= 0
                && originalEventTick > eligibilityBaselineTick;
        }

        /// <summary>
        /// Plans the saved no-catch-up boundary. Enabling or an old save with no boundary observes
        /// current truth; ordinary reconciliation, including after reload, preserves the prior tick.
        /// </summary>
        public static long PlanMeaningfulEligibilityBaseline(
            bool allowsOptionalRequests,
            bool forceCurrentTruthBaseline,
            long savedBaselineTick,
            long currentTick)
        {
            if (!allowsOptionalRequests) return -1;
            if (forceCurrentTruthBaseline || savedBaselineTick < 0)
                return Math.Max(0L, currentTick);
            return savedBaselineTick;
        }

        /// <summary>
        /// Fail-closed preflight shared by candidate dispatch and the main-thread queue adapter. A
        /// published/saved-policy mismatch must never stage work under the old cancellation fence.
        /// </summary>
        public static bool CanStageOptionalRequest(
            bool policyReconciled,
            bool allowsOptionalRequests,
            long globalCancellationGeneration,
            long optionalRequestInvalidationGeneration)
        {
            return policyReconciled
                && allowsOptionalRequests
                && globalCancellationGeneration > 0
                && globalCancellationGeneration < long.MaxValue
                && optionalRequestInvalidationGeneration > 0
                && optionalRequestInvalidationGeneration < long.MaxValue;
        }

        /// <summary>
        /// Returns whether one bounded opportunity can still become due. Pre-due work wakes the
        /// common coordinator, while the exact expiry boundary and every later tick stay asleep.
        /// </summary>
        public static bool IsBoundedOpportunityWakeable(
            long requestedTick,
            long delayTicks,
            long expiryTicks,
            long nowTick)
        {
            if (requestedTick < 0 || delayTicks < 0 || expiryTicks <= 0 || nowTick < 0)
                return false;
            if (requestedTick > long.MaxValue - delayTicks) return false;
            long due = requestedTick + delayTicks;
            long expiry = due > long.MaxValue - expiryTicks
                ? long.MaxValue
                : due + expiryTicks;
            return nowTick < expiry;
        }

        /// <summary>Creates the canonical length-prefixed key carrying every saved identity field.</summary>
        public static bool TryCreateSummaryOpportunityKey(
            SummaryWordingOpportunitySnapshot value,
            out string key)
        {
            key = string.Empty;
            if (!ValidSummaryFields(value)) return false;
            string[] fields =
            {
                SummaryOpportunityDomain,
                value.ownerPawnId,
                value.ownerEpochToken,
                Invariant(value.ownerCancellationGeneration),
                Invariant(value.globalCancellationGeneration),
                Invariant(value.optionalRequestInvalidationGeneration),
                value.rootId,
                value.summaryRecordId,
                Invariant(value.expectedRootStructuralRevision),
                Invariant(value.expectedSummaryFactsRevision),
                Invariant(value.expectedReducerRevision),
                Invariant(value.expectedFormatRevision),
                Invariant(value.expectedCategoryMask),
                value.projectionFingerprint,
                Invariant(value.requestedTick),
                Invariant(value.dueTick),
                Invariant(value.expiryTick),
                Invariant(value.configuredPriority),
                Invariant(value.salience)
            };
            for (int index = 0; index < fields.Length; index++)
            {
                key += OrdinalSegmentCodec.Segment(fields[index]);
            }
            return key.Length <= MaximumCompositeKeyCharacters;
        }

        /// <summary>Parses only the exact canonical M10 tuple; malformed/trailing input fails closed.</summary>
        public static bool TryParseSummaryOpportunityKey(
            string key,
            out SummaryWordingOpportunitySnapshot value)
        {
            value = null;
            if (string.IsNullOrEmpty(key)
                || key.Length > MaximumCompositeKeyCharacters) return false;
            int offset = 0;
            string[] fields = new string[19];
            for (int index = 0; index < fields.Length; index++)
            {
                if (!OrdinalSegmentCodec.TryReadCanonicalSegment(
                        key,
                        ref offset,
                        MaximumSegmentCharacters,
                        false,
                        out fields[index])) return false;
            }
            if (offset != key.Length
                || !string.Equals(fields[0], SummaryOpportunityDomain, StringComparison.Ordinal))
                return false;

            long ownerGeneration;
            long globalGeneration;
            long optionalGeneration;
            long rootRevision;
            long factsRevision;
            int reducerRevision;
            long formatRevision;
            int categoryMask;
            long requestedTick;
            long dueTick;
            long expiryTick;
            int priority;
            int salience;
            if (!TryLong(fields[3], out ownerGeneration)
                || !TryLong(fields[4], out globalGeneration)
                || !TryLong(fields[5], out optionalGeneration)
                || !TryLong(fields[8], out rootRevision)
                || !TryLong(fields[9], out factsRevision)
                || !TryInt(fields[10], out reducerRevision)
                || !TryLong(fields[11], out formatRevision)
                || !TryInt(fields[12], out categoryMask)
                || !TryLong(fields[14], out requestedTick)
                || !TryLong(fields[15], out dueTick)
                || !TryLong(fields[16], out expiryTick)
                || !TryInt(fields[17], out priority)
                || !TryInt(fields[18], out salience)) return false;

            SummaryWordingOpportunitySnapshot parsed = new SummaryWordingOpportunitySnapshot
            {
                ownerPawnId = fields[1],
                ownerEpochToken = fields[2],
                ownerCancellationGeneration = ownerGeneration,
                globalCancellationGeneration = globalGeneration,
                optionalRequestInvalidationGeneration = optionalGeneration,
                rootId = fields[6],
                summaryRecordId = fields[7],
                expectedRootStructuralRevision = rootRevision,
                expectedSummaryFactsRevision = factsRevision,
                expectedReducerRevision = reducerRevision,
                expectedFormatRevision = formatRevision,
                expectedCategoryMask = categoryMask,
                projectionFingerprint = fields[13],
                requestedTick = requestedTick,
                dueTick = dueTick,
                expiryTick = expiryTick,
                configuredPriority = priority,
                salience = salience,
                opportunityKey = key
            };
            if (!IsValidSummaryOpportunity(parsed)) return false;
            value = parsed;
            return true;
        }

        /// <summary>Checks fields and demands byte-identical canonical key reconstruction.</summary>
        public static bool IsValidSummaryOpportunity(SummaryWordingOpportunitySnapshot value)
        {
            string expected;
            return ValidSummaryFields(value)
                && TryCreateSummaryOpportunityKey(value, out expected)
                && string.Equals(expected, value.opportunityKey, StringComparison.Ordinal);
        }

        /// <summary>
        /// Keeps at most one owner/epoch row. Expiry is exact at now &gt;= expiry; otherwise higher
        /// priority, salience, newer request tick, then smaller ordinal key wins.
        /// </summary>
        public static SummaryWordingSlotPlan PlanOwnerSlot(
            SummaryWordingOpportunitySnapshot existing,
            SummaryWordingOpportunitySnapshot incoming,
            long nowTick)
        {
            SummaryWordingSlotPlan plan = new SummaryWordingSlotPlan();
            if (nowTick < 0) return plan;
            bool existingValid = IsValidSummaryOpportunity(existing);
            bool incomingValid = IsValidSummaryOpportunity(incoming);
            if ((existing != null && !existingValid) || (incoming != null && !incomingValid))
                return plan;
            if (existingValid && incomingValid
                && (!Equal(existing.ownerPawnId, incoming.ownerPawnId)
                    || !Equal(existing.ownerEpochToken, incoming.ownerEpochToken))) return plan;

            plan.valid = true;
            if (existingValid && nowTick >= existing.expiryTick)
            {
                plan.terminal.Add(Terminal(existing, MemoryOptionalWordingDispositionTokens.Expired));
                existing = null;
                existingValid = false;
            }
            if (incomingValid && nowTick >= incoming.expiryTick)
            {
                plan.terminal.Add(Terminal(incoming, MemoryOptionalWordingDispositionTokens.Expired));
                incoming = null;
                incomingValid = false;
            }
            if (!existingValid)
            {
                plan.winner = incomingValid ? incoming : null;
                return plan;
            }
            if (!incomingValid)
            {
                plan.winner = existing;
                return plan;
            }

            int compared = CompareKeepOrder(existing, incoming);
            plan.winner = compared <= 0 ? existing : incoming;
            plan.terminal.Add(Terminal(
                ReferenceEquals(plan.winner, existing) ? incoming : existing,
                MemoryOptionalWordingDispositionTokens.Displaced));
            return plan;
        }

        /// <summary>
        /// Checks every frozen opportunity field against the current projection. Suppression is not
        /// part of identity so the main thread can remove a now-suppressed pending row deterministically.
        /// </summary>
        public static bool TargetsCurrentSummaryProjection(
            SummaryWordingOpportunitySnapshot opportunity,
            SummaryWordingCurrentSnapshot current)
        {
            return IsValidSummaryOpportunity(opportunity)
                && current != null
                && Equal(opportunity.ownerPawnId, current.ownerPawnId)
                && Equal(opportunity.ownerEpochToken, current.ownerEpochToken)
                && Equal(opportunity.rootId, current.rootId)
                && Equal(opportunity.summaryRecordId, current.summaryRecordId)
                && opportunity.expectedRootStructuralRevision == current.rootStructuralRevision
                && opportunity.expectedSummaryFactsRevision == current.summaryFactsRevision
                && opportunity.expectedReducerRevision == current.reducerRevision
                && opportunity.expectedFormatRevision == current.formatRevision
                && opportunity.expectedCategoryMask == current.categoryMask
                && Equal(opportunity.projectionFingerprint, current.projectionFingerprint);
        }

        /// <summary>Requires exact projection identity plus current non-suppression for activation.</summary>
        public static bool MatchesCurrentSummary(
            SummaryWordingOpportunitySnapshot opportunity,
            SummaryWordingCurrentSnapshot current)
        {
            return TargetsCurrentSummaryProjection(opportunity, current)
                && !current.suppressed;
        }

        /// <summary>
        /// Applies only bounded single-paragraph prose to the disposable cache. Failure, malformed,
        /// suppression, or any stale identity leaves deterministic wording untouched.
        /// </summary>
        public static SummaryWordingResultPlan PlanSummaryResult(
            SummaryWordingOpportunitySnapshot opportunity,
            SummaryWordingCurrentSnapshot current,
            bool providerSucceeded,
            string generatedWording,
            int maximumCharacters)
        {
            SummaryWordingResultPlan plan = new SummaryWordingResultPlan();
            if (!MatchesCurrentSummary(opportunity, current)) return plan;
            plan.identityMatched = true;
            if (!providerSucceeded)
            {
                plan.dispositionToken = MemoryOptionalWordingDispositionTokens.Failed;
                return plan;
            }

            string normalized;
            if (!MemoryNaturalWordingProjection.TryNormalizeOptionalWording(
                    generatedWording, maximumCharacters, out normalized))
            {
                plan.dispositionToken = MemoryOptionalWordingDispositionTokens.Malformed;
                return plan;
            }
            plan.applyOptionalWording = true;
            plan.optionalWording = normalized;
            plan.dispositionToken = MemoryOptionalWordingDispositionTokens.Success;
            return plan;
        }

        /// <summary>
        /// Selects disposable prose only for the exact current natural-writing projection. A mismatch,
        /// malformed cache, or non-success terminal state falls back to deterministic wording; a
        /// suppressed Summary exposes neither wording to a prompt.
        /// </summary>
        public static string SelectNaturalWritingWording(
            SummaryWordingCurrentSnapshot current,
            string optionalWording,
            string optionalFingerprint,
            long optionalFormatRevision,
            int optionalCategoryMask,
            string dispositionToken,
            int maximumOptionalCharacters)
        {
            return MemoryNaturalWordingProjection.Select(
                current?.suppressed ?? true,
                current?.deterministicWording,
                current == null ? null : new MemoryRecallNaturalWordingSnapshot
                {
                    currentProjectionFingerprint = current.projectionFingerprint,
                    currentFormatRevision = current.formatRevision,
                    currentCategoryMask = current.categoryMask,
                    optionalWording = optionalWording,
                    optionalFingerprint = optionalFingerprint,
                    optionalFormatRevision = optionalFormatRevision,
                    optionalCategoryMask = optionalCategoryMask,
                    optionalSucceeded = dispositionToken
                        == MemoryOptionalWordingDispositionTokens.Success
                },
                maximumOptionalCharacters);
        }

        /// <summary>
        /// Hashes exactly the deterministic Event/Landmark sentence that the small transformation
        /// sees. Player wording, optional prose, current truth, and mutable routing state are omitted
        /// so generated text can never become canonical input to its own validity check.
        /// </summary>
        public static bool TryCreateBlockWordingProjectionFingerprint(
            string recordId,
            string kind,
            string category,
            string deterministicWording,
            long wordingFormatRevision,
            out string fingerprint)
        {
            fingerprint = string.Empty;
            if (!Required(recordId)
                || (kind != MemoryContractTokens.KindEvent
                    && kind != MemoryContractTokens.KindLandmark)
                || !Required(category)
                || !Required(deterministicWording)
                || wordingFormatRevision <= 0) return false;
            string framed = OrdinalSegmentCodec.Segment(BlockWordingProjectionDomain)
                + OrdinalSegmentCodec.Segment(recordId)
                + OrdinalSegmentCodec.Segment(kind)
                + OrdinalSegmentCodec.Segment(category)
                + OrdinalSegmentCodec.Segment(Invariant(wordingFormatRevision))
                + OrdinalSegmentCodec.Segment(deterministicWording);
            try
            {
                byte[] bytes = new UTF8Encoding(false, true).GetBytes(framed);
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] digest = sha.ComputeHash(bytes);
                    StringBuilder text = new StringBuilder(digest.Length * 2);
                    for (int index = 0; index < digest.Length; index++)
                        text.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
                    fingerprint = text.ToString();
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is EncoderFallbackException
                || exception is CryptographicException)
            {
                return false;
            }
        }

        /// <summary>Requires the frozen identity to still target the exact deterministic source.</summary>
        public static bool TargetsCurrentBlockWording(
            SummaryWordingOpportunitySnapshot opportunity,
            MemoryBlockWordingCurrentSnapshot current)
        {
            string rebuiltFingerprint;
            return IsValidSummaryOpportunity(opportunity)
                && ValidBlockWordingCurrent(current)
                && TryCreateBlockWordingProjectionFingerprint(
                    current.recordId,
                    current.kind,
                    current.category,
                    current.deterministicWording,
                    current.wordingFormatRevision,
                    out rebuiltFingerprint)
                && Equal(rebuiltFingerprint, current.projectionFingerprint)
                && Equal(opportunity.ownerPawnId, current.ownerPawnId)
                && Equal(opportunity.ownerEpochToken, current.ownerEpochToken)
                && Equal(opportunity.rootId, current.rootId)
                && Equal(opportunity.summaryRecordId, current.recordId)
                && opportunity.expectedFormatRevision == current.wordingFormatRevision
                && opportunity.expectedCategoryMask == current.categoryMask
                && Equal(opportunity.projectionFingerprint,
                    current.projectionFingerprint);
        }

        /// <summary>Suppressed/player-authored blocks never accept or expose provider prose.</summary>
        public static bool MatchesCurrentBlockWording(
            SummaryWordingOpportunitySnapshot opportunity,
            MemoryBlockWordingCurrentSnapshot current)
        {
            return TargetsCurrentBlockWording(opportunity, current)
                && !current.suppressed && !current.playerEdited;
        }

        /// <summary>Validates provider text without ever replacing deterministic source facts.</summary>
        public static MemoryBlockWordingResultPlan PlanBlockWordingResult(
            SummaryWordingOpportunitySnapshot opportunity,
            MemoryBlockWordingCurrentSnapshot current,
            bool providerSucceeded,
            string generatedWording,
            int maximumCharacters)
        {
            MemoryBlockWordingResultPlan plan = new MemoryBlockWordingResultPlan();
            if (!MatchesCurrentBlockWording(opportunity, current)) return plan;
            plan.identityMatched = true;
            if (!providerSucceeded)
            {
                plan.dispositionToken = MemoryOptionalWordingDispositionTokens.Failed;
                return plan;
            }
            string normalized;
            if (!MemoryNaturalWordingProjection.TryNormalizeOptionalWording(
                    generatedWording, maximumCharacters, out normalized))
            {
                plan.dispositionToken = MemoryOptionalWordingDispositionTokens.Malformed;
                return plan;
            }
            plan.applyOptionalWording = true;
            plan.optionalWording = normalized;
            plan.dispositionToken = MemoryOptionalWordingDispositionTokens.Success;
            return plan;
        }

        /// <summary>Builds a complete staged immutable request graph or refuses before allocation.</summary>
        public static bool TryBuildLogicalRequest(
            MemoryOptionalRequestBuildInput input,
            out MemoryLogicalRequestSnapshot request)
        {
            request = null;
            if (input == null
                || input.logicalRequestSequence <= 0
                || input.sessionId <= 0
                || !MemoryDispatchTokens.IsPurpose(input.requestPurposeToken)
                || string.IsNullOrWhiteSpace(input.opportunityKey)
                || string.IsNullOrWhiteSpace(input.povRoleToken)
                || string.IsNullOrWhiteSpace(input.ownerPawnId)
                || string.IsNullOrWhiteSpace(input.ownerEpochToken)
                || input.ownerCancellationGeneration <= 0
                || input.ownerCancellationGeneration == long.MaxValue
                || input.globalCancellationGeneration <= 0
                || input.globalCancellationGeneration == long.MaxValue
                || (MemoryDispatchTokens.IsOptionalPurpose(input.requestPurposeToken)
                    ? input.optionalRequestInvalidationGeneration <= 0
                        || input.optionalRequestInvalidationGeneration == long.MaxValue
                    : input.optionalRequestInvalidationGeneration != 0)
                || input.variants == null || input.variants.Count == 0
                || input.variants.Count > MemoryDispatchPolicy.MaximumVariants) return false;

            MemoryLogicalRequestSnapshot candidate = new MemoryLogicalRequestSnapshot
            {
                logicalRequestSequence = input.logicalRequestSequence,
                requestPurposeToken = input.requestPurposeToken,
                sessionId = input.sessionId,
                eventIdOrOpportunityKey = input.opportunityKey,
                povRoleToken = input.povRoleToken,
                ownerPawnId = input.ownerPawnId,
                ownerEpochToken = input.ownerEpochToken,
                ownerCancellationGeneration = input.ownerCancellationGeneration,
                globalCancellationGeneration = input.globalCancellationGeneration,
                optionalRequestInvalidationGeneration =
                    input.optionalRequestInvalidationGeneration,
                requestStateToken = MemoryRequestStateMachineContracts.Staged
            };
            if (!MemoryIdentityCodec.TryCreateLogicalRequestId(
                    candidate.logicalRequestSequence, out candidate.logicalRequestId)) return false;

            for (int ordinal = 0; ordinal < input.variants.Count; ordinal++)
            {
                MemoryOptionalPromptVariantInput source = input.variants[ordinal];
                if (source == null) return false;
                MemoryFrozenPromptVariantSnapshot variant = new MemoryFrozenPromptVariantSnapshot
                {
                    variantOrdinal = ordinal,
                    templateIdentity = source.templateIdentity ?? string.Empty,
                    contextDetailIdentity = source.contextDetailIdentity ?? string.Empty,
                    systemPrompt = source.systemPrompt ?? string.Empty,
                    userPrompt = source.userPrompt ?? string.Empty,
                    diagnostics = CopyDiagnostics(source.diagnostics)
                };
                variant.receipt.evidence = CopyEvidence(source.evidence);
                variant.receipt.guards = CopyGuards(source.guards);
                if (variant.diagnostics == null
                    || variant.receipt.evidence == null
                    || variant.receipt.guards == null
                    || !MemoryIdentityCodec.TryCreateEvidenceSetFingerprint(
                        variant.receipt.evidence,
                        out variant.receipt.evidenceSetFingerprint)
                    || !MemoryIdentityCodec.TryCreateReceiptPlanFingerprint(
                        variant.receipt.evidence,
                        variant.receipt.guards,
                        out variant.receipt.receiptPlanFingerprint)) return false;
                string diagnosticsFingerprint;
                if (!MemoryIdentityCodec.TryCreateDiagnosticProvenanceFingerprint(
                        variant.diagnostics, out diagnosticsFingerprint)
                    || !MemoryIdentityCodec.TryCreatePromptVariantKey(
                        candidate.logicalRequestId,
                        ordinal,
                        candidate.requestPurposeToken,
                        variant.templateIdentity,
                        variant.contextDetailIdentity,
                        variant.systemPrompt,
                        variant.userPrompt,
                        variant.receipt.receiptPlanFingerprint,
                        diagnosticsFingerprint,
                        out variant.variantKey)) return false;
                candidate.variants.Add(variant);
            }

            candidate.reservedEvidence = CanonicalEvidenceUnion(candidate.variants);
            candidate.reservedGuards = CanonicalGuardUnion(candidate.variants);
            if (candidate.reservedEvidence == null || candidate.reservedGuards == null
                || !MemoryIdentityCodec.TryCreateEvidenceEpochToken(
                    candidate.requestPurposeToken,
                    candidate.eventIdOrOpportunityKey,
                    candidate.povRoleToken,
                    candidate.ownerPawnId,
                    candidate.ownerEpochToken,
                    candidate.reservedEvidence,
                    candidate.reservedGuards,
                    out candidate.evidenceEpochToken)
                || !MemoryIdentityCodec.TryCreateLogicalRequestKey(
                    candidate.requestPurposeToken,
                    candidate.eventIdOrOpportunityKey,
                    candidate.povRoleToken,
                    candidate.ownerPawnId,
                    candidate.ownerEpochToken,
                    candidate.evidenceEpochToken,
                    out candidate.logicalRequestKey)
                || !MemoryDispatchPolicy.ValidateRequest(candidate)) return false;
            request = candidate;
            return true;
        }

        private static bool ValidSummaryFields(SummaryWordingOpportunitySnapshot value)
        {
            return value != null
                && Required(value.ownerPawnId)
                && Required(value.ownerEpochToken)
                && value.ownerCancellationGeneration > 0
                && value.ownerCancellationGeneration < long.MaxValue
                && value.globalCancellationGeneration > 0
                && value.globalCancellationGeneration < long.MaxValue
                && value.optionalRequestInvalidationGeneration > 0
                && value.optionalRequestInvalidationGeneration < long.MaxValue
                && Required(value.rootId)
                && Required(value.summaryRecordId)
                && value.expectedRootStructuralRevision > 0
                && value.expectedSummaryFactsRevision > 0
                && value.expectedReducerRevision > 0
                && value.expectedFormatRevision > 0
                && value.expectedCategoryMask > 0
                && LowerSha256(value.projectionFingerprint)
                && value.requestedTick >= 0
                && value.dueTick >= value.requestedTick
                && value.expiryTick > value.dueTick;
        }

        private static bool ValidBlockWordingCurrent(
            MemoryBlockWordingCurrentSnapshot value)
        {
            return value != null
                && Required(value.ownerPawnId)
                && Required(value.ownerEpochToken)
                && Required(value.rootId)
                && Required(value.recordId)
                && (value.kind == MemoryContractTokens.KindEvent
                    || value.kind == MemoryContractTokens.KindLandmark)
                && Required(value.category)
                && Required(value.deterministicWording)
                && value.wordingFormatRevision > 0
                && value.categoryMask > 0
                && LowerSha256(value.projectionFingerprint);
        }

        private static int CompareKeepOrder(
            SummaryWordingOpportunitySnapshot left,
            SummaryWordingOpportunitySnapshot right)
        {
            int compared = right.configuredPriority.CompareTo(left.configuredPriority);
            if (compared != 0) return compared;
            compared = right.salience.CompareTo(left.salience);
            if (compared != 0) return compared;
            compared = right.requestedTick.CompareTo(left.requestedTick);
            return compared != 0
                ? compared
                : string.CompareOrdinal(left.opportunityKey, right.opportunityKey);
        }

        private static SummaryWordingTerminalDecision Terminal(
            SummaryWordingOpportunitySnapshot opportunity,
            string disposition)
        {
            return new SummaryWordingTerminalDecision
            {
                opportunity = opportunity,
                dispositionToken = disposition
            };
        }

        private static List<MemoryEvidenceIdentity> CanonicalEvidenceUnion(
            List<MemoryFrozenPromptVariantSnapshot> variants)
        {
            List<MemoryEvidenceIdentity> result = new List<MemoryEvidenceIdentity>();
            for (int index = 0; variants != null && index < variants.Count; index++)
            {
                List<MemoryEvidenceIdentity> copied =
                    CopyEvidence(variants[index]?.receipt?.evidence);
                if (copied == null) return null;
                result.AddRange(copied);
            }
            result.Sort(CompareEvidence);
            for (int index = result.Count - 1; index > 0; index--)
                if (CompareEvidence(result[index - 1], result[index]) == 0) result.RemoveAt(index);
            return result;
        }

        private static List<MemoryGuardIdentity> CanonicalGuardUnion(
            List<MemoryFrozenPromptVariantSnapshot> variants)
        {
            List<MemoryGuardIdentity> result = new List<MemoryGuardIdentity>();
            for (int index = 0; variants != null && index < variants.Count; index++)
            {
                List<MemoryGuardIdentity> copied =
                    CopyGuards(variants[index]?.receipt?.guards);
                if (copied == null) return null;
                result.AddRange(copied);
            }
            result.Sort((left, right) =>
            {
                int compared = string.CompareOrdinal(left.guardKind, right.guardKind);
                return compared != 0 ? compared : string.CompareOrdinal(left.guardKey, right.guardKey);
            });
            for (int index = result.Count - 1; index > 0; index--)
                if (Equal(result[index - 1].guardKind, result[index].guardKind)
                    && Equal(result[index - 1].guardKey, result[index].guardKey)) result.RemoveAt(index);
            return result;
        }

        private static List<MemoryEvidenceIdentity> CopyEvidence(
            List<MemoryEvidenceIdentity> source)
        {
            List<MemoryEvidenceIdentity> result = new List<MemoryEvidenceIdentity>();
            for (int index = 0; source != null && index < source.Count; index++)
            {
                MemoryEvidenceIdentity row = source[index];
                if (row == null) return null;
                result.Add(new MemoryEvidenceIdentity
                {
                    recordId = row.recordId ?? string.Empty,
                    sourceOccurrenceId = row.sourceOccurrenceId ?? string.Empty,
                    rootIdOrEmpty = row.rootIdOrEmpty ?? string.Empty
                });
            }
            return result;
        }

        private static List<MemoryGuardIdentity> CopyGuards(List<MemoryGuardIdentity> source)
        {
            List<MemoryGuardIdentity> result = new List<MemoryGuardIdentity>();
            for (int index = 0; source != null && index < source.Count; index++)
            {
                MemoryGuardIdentity row = source[index];
                if (row == null) return null;
                result.Add(new MemoryGuardIdentity
                {
                    guardKind = row.guardKind ?? string.Empty,
                    guardKey = row.guardKey ?? string.Empty
                });
            }
            return result;
        }

        private static List<MemoryDiagnosticIdentity> CopyDiagnostics(
            List<MemoryDiagnosticIdentity> source)
        {
            List<MemoryDiagnosticIdentity> result = new List<MemoryDiagnosticIdentity>();
            for (int index = 0; source != null && index < source.Count; index++)
            {
                MemoryDiagnosticIdentity row = source[index];
                if (row == null) return null;
                result.Add(new MemoryDiagnosticIdentity
                {
                    provenanceKindToken = row.provenanceKindToken ?? string.Empty,
                    sourceId = row.sourceId ?? string.Empty,
                    recordIdOrEmpty = row.recordIdOrEmpty ?? string.Empty,
                    sourceOccurrenceIdOrEmpty = row.sourceOccurrenceIdOrEmpty ?? string.Empty,
                    rootIdOrEmpty = row.rootIdOrEmpty ?? string.Empty,
                    lineOrdinal = row.lineOrdinal
                });
            }
            return result;
        }

        private static int CompareEvidence(MemoryEvidenceIdentity left, MemoryEvidenceIdentity right)
        {
            int compared = string.CompareOrdinal(left.recordId, right.recordId);
            if (compared != 0) return compared;
            compared = string.CompareOrdinal(left.sourceOccurrenceId, right.sourceOccurrenceId);
            return compared != 0
                ? compared
                : string.CompareOrdinal(left.rootIdOrEmpty, right.rootIdOrEmpty);
        }

        private static bool Required(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= MaximumSegmentCharacters
                && MemoryIdentityCodec.IsWellFormedUtf16(value);
        }

        private static bool LowerSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (!((current >= '0' && current <= '9')
                    || (current >= 'a' && current <= 'f'))) return false;
            }
            return true;
        }

        private static bool TryLong(string value, out long parsed)
        {
            return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
                && Invariant(parsed) == value;
        }

        private static bool TryInt(string value, out int parsed)
        {
            return int.TryParse(value, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out parsed)
                && Invariant(parsed) == value;
        }

        private static string Invariant(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Invariant(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static bool Equal(string left, string right)
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }
    }

    /// <summary>One bounded session-local settings-generation cutoff entry (§T12.3).</summary>
    internal sealed class MemoryInvokedGenerationCutoffEntry
    {
        public long sessionId;
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public long ownerCancellationGeneration;
        public long globalCancellationGeneration;
        public long cutoffInvocationSequence;
        public bool sealedByCancellation;
        public Dictionary<string, long> unsettledRequestSequences =
            new Dictionary<string, long>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Unscribed invocation-wins table. Registration happens at permit commit, settings cancellation
    /// seals the exact old generation, and terminal settlement removes the exact request. Brainwipe is
    /// never represented here because callers must first match the current owner epoch.
    /// </summary>
    internal sealed class MemoryInvokedGenerationCutoffTable
    {
        private readonly List<MemoryInvokedGenerationCutoffEntry> entries =
            new List<MemoryInvokedGenerationCutoffEntry>();

        public int EntryCount { get { return entries.Count; } }

        public int UnsettledRequestCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < entries.Count; index++)
                    count += entries[index].unsettledRequestSequences.Count;
                return count;
            }
        }

        /// <summary>
        /// Creates a fully detached copy for a settings reconciliation plan. The live invocation-wins
        /// table is not changed until the component publishes the complete prepared transaction.
        /// </summary>
        public MemoryInvokedGenerationCutoffTable Clone()
        {
            MemoryInvokedGenerationCutoffTable copy =
                new MemoryInvokedGenerationCutoffTable();
            for (int index = 0; index < entries.Count; index++)
            {
                MemoryInvokedGenerationCutoffEntry source = entries[index];
                if (source == null) continue;
                MemoryInvokedGenerationCutoffEntry entry =
                    new MemoryInvokedGenerationCutoffEntry
                    {
                        sessionId = source.sessionId,
                        ownerPawnId = source.ownerPawnId,
                        ownerEpochToken = source.ownerEpochToken,
                        ownerCancellationGeneration = source.ownerCancellationGeneration,
                        globalCancellationGeneration = source.globalCancellationGeneration,
                        cutoffInvocationSequence = source.cutoffInvocationSequence,
                        sealedByCancellation = source.sealedByCancellation
                    };
                foreach (KeyValuePair<string, long> request in
                    source.unsettledRequestSequences)
                {
                    entry.unsettledRequestSequences.Add(request.Key, request.Value);
                }
                copy.entries.Add(entry);
            }
            return copy;
        }

        public bool CanRegister(
            long sessionId,
            string ownerPawnId,
            string ownerEpochToken,
            long ownerGeneration,
            long globalGeneration,
            string logicalRequestId,
            long invocationSequence,
            int maximumEntries)
        {
            if (!ValidIdentity(sessionId, ownerPawnId, ownerEpochToken, ownerGeneration,
                    globalGeneration, logicalRequestId, invocationSequence)
                || maximumEntries <= 0) return false;
            MemoryInvokedGenerationCutoffEntry entry = Find(
                sessionId, ownerPawnId, ownerEpochToken, ownerGeneration, globalGeneration);
            long registered;
            if (entry != null && entry.unsettledRequestSequences.TryGetValue(
                    logicalRequestId, out registered)) return registered == invocationSequence;
            // The contract bounds exact unsettled requests, not merely generation-key rows. Many
            // requests can share one owner/generation entry, so EntryCount alone is insufficient.
            return UnsettledRequestCount < maximumEntries;
        }

        public bool TryRegister(
            long sessionId,
            string ownerPawnId,
            string ownerEpochToken,
            long ownerGeneration,
            long globalGeneration,
            string logicalRequestId,
            long invocationSequence,
            int maximumEntries)
        {
            if (!CanRegister(sessionId, ownerPawnId, ownerEpochToken, ownerGeneration,
                    globalGeneration, logicalRequestId, invocationSequence, maximumEntries))
                return false;
            MemoryInvokedGenerationCutoffEntry entry = Find(
                sessionId, ownerPawnId, ownerEpochToken, ownerGeneration, globalGeneration);
            if (entry == null)
            {
                entry = new MemoryInvokedGenerationCutoffEntry
                {
                    sessionId = sessionId,
                    ownerPawnId = ownerPawnId,
                    ownerEpochToken = ownerEpochToken,
                    ownerCancellationGeneration = ownerGeneration,
                    globalCancellationGeneration = globalGeneration
                };
                entries.Add(entry);
            }
            long prior;
            if (entry.unsettledRequestSequences.TryGetValue(logicalRequestId, out prior))
                return prior == invocationSequence;
            entry.unsettledRequestSequences.Add(logicalRequestId, invocationSequence);
            return true;
        }

        /// <summary>Seals every live entry for one exact superseded global generation.</summary>
        public int SealGeneration(long sessionId, long globalGeneration, long cutoffSequence)
        {
            if (sessionId <= 0 || globalGeneration <= 0 || cutoffSequence < 0) return 0;
            int sealedCount = 0;
            for (int index = 0; index < entries.Count; index++)
            {
                MemoryInvokedGenerationCutoffEntry entry = entries[index];
                if (entry.sessionId != sessionId
                    || entry.globalCancellationGeneration != globalGeneration) continue;
                entry.sealedByCancellation = true;
                entry.cutoffInvocationSequence = Math.Max(
                    entry.cutoffInvocationSequence, cutoffSequence);
                sealedCount++;
            }
            return sealedCount;
        }

        public bool AllowsInvocationWinner(
            long sessionId,
            string ownerPawnId,
            string ownerEpochToken,
            long ownerGeneration,
            long globalGeneration,
            string logicalRequestId,
            long invocationSequence)
        {
            MemoryInvokedGenerationCutoffEntry entry = Find(
                sessionId, ownerPawnId, ownerEpochToken, ownerGeneration, globalGeneration);
            long registered;
            return entry != null
                && entry.sealedByCancellation
                && entry.unsettledRequestSequences.TryGetValue(logicalRequestId, out registered)
                && registered == invocationSequence
                && invocationSequence > 0
                && invocationSequence <= entry.cutoffInvocationSequence;
        }

        public bool Settle(string logicalRequestId)
        {
            if (string.IsNullOrWhiteSpace(logicalRequestId)) return false;
            bool removed = false;
            for (int index = entries.Count - 1; index >= 0; index--)
            {
                removed = entries[index].unsettledRequestSequences.Remove(logicalRequestId)
                    || removed;
                if (entries[index].unsettledRequestSequences.Count == 0) entries.RemoveAt(index);
            }
            return removed;
        }

        public void Reset()
        {
            entries.Clear();
        }

        private MemoryInvokedGenerationCutoffEntry Find(
            long sessionId,
            string ownerPawnId,
            string ownerEpochToken,
            long ownerGeneration,
            long globalGeneration)
        {
            for (int index = 0; index < entries.Count; index++)
            {
                MemoryInvokedGenerationCutoffEntry entry = entries[index];
                if (entry.sessionId == sessionId
                    && entry.ownerCancellationGeneration == ownerGeneration
                    && entry.globalCancellationGeneration == globalGeneration
                    && string.Equals(entry.ownerPawnId, ownerPawnId, StringComparison.Ordinal)
                    && string.Equals(entry.ownerEpochToken, ownerEpochToken,
                        StringComparison.Ordinal)) return entry;
            }
            return null;
        }

        private static bool ValidIdentity(
            long sessionId,
            string ownerPawnId,
            string ownerEpochToken,
            long ownerGeneration,
            long globalGeneration,
            string logicalRequestId,
            long invocationSequence)
        {
            long parsed;
            return sessionId > 0
                && !string.IsNullOrWhiteSpace(ownerPawnId)
                && !string.IsNullOrWhiteSpace(ownerEpochToken)
                && ownerGeneration > 0
                && ownerGeneration < long.MaxValue
                && globalGeneration > 0
                && globalGeneration < long.MaxValue
                && invocationSequence > 0
                && MemoryIdentityCodec.TryParseLogicalRequestId(logicalRequestId, out parsed);
        }
    }
}

// PlayerMemoryPolicy.cs — pure ownership, validation, mutation planning, and retention protection
// for the pawn profile's saved memories. The UI and GameComponent pass detached snapshots or scalar
// record fields here; this file never reads Pawn, Verse, Defs, settings, or save objects.
//
// The background is factual memory, not persona instruction text. Its exact owner-bound identity
// prevents one pawn's profile from editing another pawn, while create/update plans contain no
// fabricated gameplay event, date, participant, subject, or fact metadata.
using System;

namespace PawnDiary
{
    /// <summary>What the save adapter should do with a planned background-memory edit.</summary>
    internal enum PlayerMemoryMutationAction
    {
        None,
        Create,
        Update,
        Delete,
        Rejected
    }

    /// <summary>Stable validation result for UI/adapters; values are not player-facing strings.</summary>
    internal enum PlayerMemoryValidationError
    {
        None,
        MissingOwnerPawnId,
        TextTooLong
    }

    /// <summary>
    /// Plain result of one proposed backstory edit. Create/Update include a fresh canonical record
    /// snapshot; Delete targets only <see cref="canonicalRecordId"/>.
    /// </summary>
    internal sealed class PlayerMemoryMutationPlan
    {
        public PlayerMemoryMutationAction action;
        public PlayerMemoryValidationError error;
        public string ownerPawnId = string.Empty;
        public string canonicalRecordId = string.Empty;
        public string canonicalDedupKey = string.Empty;
        public string normalizedText = string.Empty;
        public ImportantMemoryRecordSnapshot record;
    }

    /// <summary>
    /// Pure policy for the canonical player-authored background singleton and saved lifecycle rows
    /// that normal profile removal or automatic cap enforcement must preserve.
    /// </summary>
    internal static class PlayerMemoryPolicy
    {
        /// <summary>Returns the owner-bound stable record ID, or blank when the owner is invalid.</summary>
        public static string CanonicalBackstoryRecordId(string ownerPawnId)
        {
            string owner = NormalizeOwnerPawnId(ownerPawnId);
            return owner.Length == 0
                ? string.Empty
                : owner + "|" + KnowledgeTokens.EventKindPlayerBackstory;
        }

        /// <summary>
        /// Returns the owner-bound stable dedup key. It intentionally equals the record ID in v1,
        /// while remaining a separate method so either saved contract can evolve additively later.
        /// </summary>
        public static string CanonicalBackstoryDedupKey(string ownerPawnId)
        {
            return CanonicalBackstoryRecordId(ownerPawnId);
        }

        /// <summary>Missing, blank, or unknown provenance safely migrates to captured.</summary>
        public static string NormalizeSourceKind(string sourceKind)
        {
            string value = (sourceKind ?? string.Empty).Trim();
            return string.Equals(value, KnowledgeTokens.SourceKindPlayer,
                StringComparison.OrdinalIgnoreCase)
                ? KnowledgeTokens.SourceKindPlayer
                : KnowledgeTokens.SourceKindCaptured;
        }

        /// <summary>Missing, blank, or unknown scope safely migrates to contextual.</summary>
        public static string NormalizeRecallScope(string recallScope)
        {
            string value = (recallScope ?? string.Empty).Trim();
            return string.Equals(value, KnowledgeTokens.RecallScopeBackground,
                StringComparison.OrdinalIgnoreCase)
                ? KnowledgeTokens.RecallScopeBackground
                : KnowledgeTokens.RecallScopeContextual;
        }

        /// <summary>
        /// True only when every identity/provenance field exactly describes this owner's canonical
        /// background singleton. A merely player-like or background-like corrupt row is not promoted
        /// into protected/recallable canon.
        /// </summary>
        public static bool IsCanonicalBackstory(
            ImportantMemoryRecordSnapshot record,
            string ownerPawnId)
        {
            string owner = NormalizeOwnerPawnId(ownerPawnId);
            return record != null
                && string.Equals(record.ownerPawnId ?? string.Empty, owner, StringComparison.Ordinal)
                && IsCanonicalBackstory(
                    owner,
                    record.recordId,
                    record.dedupKey,
                    record.eventKind,
                    record.sourceKind,
                    record.recallScope);
        }

        /// <summary>
        /// Scalar form for save/eviction adapters that already know the owning state and should not
        /// allocate a detached snapshot merely to test protection.
        /// </summary>
        public static bool IsCanonicalBackstory(
            string ownerPawnId,
            string recordId,
            string dedupKey,
            string eventKind,
            string sourceKind,
            string recallScope)
        {
            string owner = NormalizeOwnerPawnId(ownerPawnId);
            string canonicalId = CanonicalBackstoryRecordId(owner);
            if (canonicalId.Length == 0)
            {
                return false;
            }

            return string.Equals(recordId ?? string.Empty, canonicalId, StringComparison.Ordinal)
                && string.Equals(dedupKey ?? string.Empty,
                    CanonicalBackstoryDedupKey(owner), StringComparison.Ordinal)
                && string.Equals(eventKind ?? string.Empty,
                    KnowledgeTokens.EventKindPlayerBackstory, StringComparison.Ordinal)
                && string.Equals(NormalizeSourceKind(sourceKind),
                    KnowledgeTokens.SourceKindPlayer, StringComparison.Ordinal)
                && string.Equals(NormalizeRecallScope(recallScope),
                    KnowledgeTokens.RecallScopeBackground, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when this exact owner's detached row is protected from automatic eviction and normal
        /// profile removal: either canonical player background or captured faction-joined lifecycle
        /// knowledge. The owner check prevents a detached row from borrowing another profile's canon.
        /// </summary>
        public static bool IsProtectedFromAutomaticEviction(
            ImportantMemoryRecordSnapshot record,
            string ownerPawnId)
        {
            string owner = NormalizeOwnerPawnId(ownerPawnId);
            return record != null
                && string.Equals(record.ownerPawnId ?? string.Empty, owner, StringComparison.Ordinal)
                && IsProtectedFromAutomaticEviction(
                    owner,
                    record.recordId,
                    record.dedupKey,
                    record.eventKind,
                    record.sourceKind,
                    record.recallScope);
        }

        /// <summary>
        /// Allocation-free form for persistence adapters whose owning state supplies the pawn id.
        /// Missing/legacy provenance fields normalize to captured/contextual, so an old arrival marker
        /// remains protected after migration just like a newly captured one.
        /// </summary>
        public static bool IsProtectedFromAutomaticEviction(
            string ownerPawnId,
            string recordId,
            string dedupKey,
            string eventKind,
            string sourceKind,
            string recallScope)
        {
            return IsCanonicalBackstory(
                    ownerPawnId,
                    recordId,
                    dedupKey,
                    eventKind,
                    sourceKind,
                    recallScope)
                || (string.Equals(
                        eventKind ?? string.Empty,
                        KnowledgeTokens.EventKindFactionJoined,
                        StringComparison.Ordinal)
                    && string.Equals(
                        NormalizeSourceKind(sourceKind),
                        KnowledgeTokens.SourceKindCaptured,
                        StringComparison.Ordinal)
                    && string.Equals(
                        NormalizeRecallScope(recallScope),
                        KnowledgeTokens.RecallScopeContextual,
                        StringComparison.Ordinal));
        }

        /// <summary>
        /// Strips markup/newlines and collapses whitespace to one prompt-safe line while preserving
        /// authored case and non-ASCII text. Length validation happens separately and never truncates.
        /// </summary>
        public static string NormalizePlayerText(string text)
        {
            return PromptTextSanitizer.OneLine(text);
        }

        /// <summary>
        /// Plans create/update/blank-to-delete for one owner's canonical backstory. A noncanonical
        /// supplied record is treated as absent, so this policy can never cross-edit another row.
        /// Text longer than the normalized configured limit is rejected without truncation.
        /// </summary>
        public static PlayerMemoryMutationPlan PlanBackstoryMutation(
            string ownerPawnId,
            ImportantMemoryRecordSnapshot existing,
            string proposedText,
            int maxChars)
        {
            string owner = NormalizeOwnerPawnId(ownerPawnId);
            PlayerMemoryMutationPlan plan = NewPlan(owner, proposedText);
            if (owner.Length == 0)
            {
                plan.action = PlayerMemoryMutationAction.Rejected;
                plan.error = PlayerMemoryValidationError.MissingOwnerPawnId;
                return plan;
            }

            int limit = maxChars > 0
                ? maxChars
                : KnowledgePolicySnapshot.CreateDefault().playerAuthoredMemoryMaxChars;
            if (plan.normalizedText.Length > limit)
            {
                plan.action = PlayerMemoryMutationAction.Rejected;
                plan.error = PlayerMemoryValidationError.TextTooLong;
                return plan;
            }

            bool hasCanonicalExisting = IsCanonicalBackstory(existing, owner);
            if (plan.normalizedText.Length == 0)
            {
                plan.action = hasCanonicalExisting
                    ? PlayerMemoryMutationAction.Delete
                    : PlayerMemoryMutationAction.None;
                return plan;
            }

            if (hasCanonicalExisting
                && string.Equals(existing.manualTextOverride ?? string.Empty,
                    plan.normalizedText, StringComparison.Ordinal))
            {
                plan.action = PlayerMemoryMutationAction.None;
                return plan;
            }

            plan.action = hasCanonicalExisting
                ? PlayerMemoryMutationAction.Update
                : PlayerMemoryMutationAction.Create;
            plan.record = CanonicalRecord(owner, plan.normalizedText);
            return plan;
        }

        private static PlayerMemoryMutationPlan NewPlan(string owner, string proposedText)
        {
            return new PlayerMemoryMutationPlan
            {
                ownerPawnId = owner,
                canonicalRecordId = CanonicalBackstoryRecordId(owner),
                canonicalDedupKey = CanonicalBackstoryDedupKey(owner),
                normalizedText = NormalizePlayerText(proposedText)
            };
        }

        private static ImportantMemoryRecordSnapshot CanonicalRecord(string owner, string text)
        {
            return new ImportantMemoryRecordSnapshot
            {
                ownerPawnId = owner,
                recordId = CanonicalBackstoryRecordId(owner),
                dedupKey = CanonicalBackstoryDedupKey(owner),
                sourceEventId = string.Empty,
                sourceKind = KnowledgeTokens.SourceKindPlayer,
                recallScope = KnowledgeTokens.RecallScopeBackground,
                eventKind = KnowledgeTokens.EventKindPlayerBackstory,
                topicKey = string.Empty,
                tick = 0,
                dateLabel = string.Empty,
                fallbackSummary = string.Empty,
                manualTextOverride = text
            };
        }

        private static string NormalizeOwnerPawnId(string ownerPawnId)
        {
            return (ownerPawnId ?? string.Empty).Trim();
        }
    }
}

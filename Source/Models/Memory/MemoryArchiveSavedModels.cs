// MemoryArchiveSavedModels.cs — the migration-only Imported archive rows and the raw legacy
// unresolved-owner wrapper (design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md §T6.8).
//
// The archive is a one-time preservation exception: inert, bounded, never promptable, never aged,
// never promoted. Resolved-owner rows live in that owner's PawnKnowledgeState; truly unresolved
// rows live in the component's unresolvedOwnerArchiveRows list. The raw wrapper preserves the
// shipped ImportantMemoryRecord shape untouched until a detached migration plan has captured it —
// raw loading must never call that class's semantic Normalize() first (§T13.1).
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>Bounded contribution evidence preserved for one imported Summary conflict
    /// (§T6.8). Field set matches SavedMemoryFactContribution minus derived mirrors.</summary>
    public class SavedImportedSummaryContributionEvidenceV1 : IExposable
    {
        public int schemaVersion = 1;
        public string contributionId = string.Empty;
        public string originChapterId = string.Empty;
        public string originRecordId = string.Empty;
        /// <summary>Zero-based; -1 is the missing-legacy sentinel (§T6.0).</summary>
        public int originFactOrdinal = -1;
        public string originFactId = string.Empty;
        public long originalEventTick;
        public bool ageUnknown;
        public string category = string.Empty;
        public string importance = string.Empty;
        public string canonicalValue = string.Empty;
        public bool majorTurningPoint;
        public bool reversal;
        public string sourceOccurrenceId = string.Empty;
        public List<string> subjectRefIds = new List<string>();
        public List<string> provenanceRefIds = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref contributionId, "contributionId", string.Empty);
            Scribe_Values.Look(ref originChapterId, "originChapterId", string.Empty);
            Scribe_Values.Look(ref originRecordId, "originRecordId", string.Empty);
            Scribe_Values.Look(ref originFactOrdinal, "originFactOrdinal", -1);
            Scribe_Values.Look(ref originFactId, "originFactId", string.Empty);
            Scribe_Values.Look(ref originalEventTick, "originalEventTick", 0);
            Scribe_Values.Look(ref ageUnknown, "ageUnknown", false);
            Scribe_Values.Look(ref category, "category", string.Empty);
            Scribe_Values.Look(ref importance, "importance", string.Empty);
            Scribe_Values.Look(ref canonicalValue, "canonicalValue", string.Empty);
            Scribe_Values.Look(ref majorTurningPoint, "majorTurningPoint", false);
            Scribe_Values.Look(ref reversal, "reversal", false);
            Scribe_Values.Look(ref sourceOccurrenceId, "sourceOccurrenceId", string.Empty);
            Scribe_Collections.Look(ref subjectRefIds, "subjectRefIds", LookMode.Value);
            Scribe_Collections.Look(ref provenanceRefIds, "provenanceRefIds", LookMode.Value);
        }

        public void Normalize()
        {
            contributionId = contributionId ?? string.Empty;
            originChapterId = originChapterId ?? string.Empty;
            originRecordId = originRecordId ?? string.Empty;
            originFactId = originFactId ?? string.Empty;
            category = category ?? string.Empty;
            importance = importance ?? string.Empty;
            canonicalValue = canonicalValue ?? string.Empty;
            sourceOccurrenceId = sourceOccurrenceId ?? string.Empty;
            subjectRefIds = subjectRefIds ?? new List<string>();
            provenanceRefIds = provenanceRefIds ?? new List<string>();
        }
    }

    /// <summary>One inert Imported archive row (§T6.8). Archive rows have no thread, TTL, edit,
    /// suppression, recall, summary, or exposure state — they are browse-only except Dev Forget.</summary>
    public class SavedImportedMemoryRow : IExposable
    {
        public int schemaVersion = 1;
        /// <summary>Generated once from the canonical tuple; invariant under input permutation.</summary>
        public string archiveRecordId = string.Empty;
        /// <summary>Exactly exact_id | blank | conflicting.</summary>
        public string savedOwnerIdentityKindToken = string.Empty;
        public string savedOwnerIdentityValue = string.Empty;
        /// <summary>Positive on unresolved component rows; resolved-owner rows save 0.</summary>
        public long reattributionGeneration;
        public string originalRecordId = string.Empty;
        public string sourceOccurrenceId = string.Empty;
        public string sourceEventId = string.Empty;
        public long originalEventTick;
        public bool ageUnknown;
        /// <summary>Saved only when it fits current-schema ceilings without truncation.</summary>
        public string importedWording = string.Empty;
        public string originalKindToken = string.Empty;
        public string originalSummaryRoleToken = string.Empty;
        public string originalCategoryToken = string.Empty;
        public string originalImportanceToken = string.Empty;
        public string routePolicyToken = string.Empty;
        public SavedMemorySubjectRef primarySubject;
        public List<SavedMemorySubjectRef> secondarySubjects = new List<SavedMemorySubjectRef>();
        public List<SavedMemoryCanonicalFact> canonicalFacts =
            new List<SavedMemoryCanonicalFact>();
        public List<SavedMemoryProvenance> provenance = new List<SavedMemoryProvenance>();
        public SavedImportedSummaryContributionEvidenceV1 summaryContributionEvidence;
        public string sourceTypeToken = string.Empty;
        public string conflictFingerprint = string.Empty;
        public long overflowRowCount;
        public long overflowLogicalBytes;
        public List<string> diagnosticTokens = new List<string>();
        public string migrationReasonToken = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref archiveRecordId, "archiveRecordId", string.Empty);
            Scribe_Values.Look(ref savedOwnerIdentityKindToken,
                "savedOwnerIdentityKindToken", string.Empty);
            Scribe_Values.Look(ref savedOwnerIdentityValue,
                "savedOwnerIdentityValue", string.Empty);
            Scribe_Values.Look(ref reattributionGeneration, "reattributionGeneration", 0);
            Scribe_Values.Look(ref originalRecordId, "originalRecordId", string.Empty);
            Scribe_Values.Look(ref sourceOccurrenceId, "sourceOccurrenceId", string.Empty);
            Scribe_Values.Look(ref sourceEventId, "sourceEventId", string.Empty);
            Scribe_Values.Look(ref originalEventTick, "originalEventTick", 0);
            Scribe_Values.Look(ref ageUnknown, "ageUnknown", false);
            Scribe_Values.Look(ref importedWording, "importedWording", string.Empty);
            Scribe_Values.Look(ref originalKindToken, "originalKindToken", string.Empty);
            Scribe_Values.Look(ref originalSummaryRoleToken,
                "originalSummaryRoleToken", string.Empty);
            Scribe_Values.Look(ref originalCategoryToken, "originalCategoryToken", string.Empty);
            Scribe_Values.Look(ref originalImportanceToken,
                "originalImportanceToken", string.Empty);
            Scribe_Values.Look(ref routePolicyToken, "routePolicyToken", string.Empty);
            Scribe_Deep.Look(ref primarySubject, "primarySubject");
            Scribe_Collections.Look(ref secondarySubjects, "secondarySubjects", LookMode.Deep);
            Scribe_Collections.Look(ref canonicalFacts, "canonicalFacts", LookMode.Deep);
            Scribe_Collections.Look(ref provenance, "provenance", LookMode.Deep);
            Scribe_Deep.Look(ref summaryContributionEvidence, "summaryContributionEvidence");
            Scribe_Values.Look(ref sourceTypeToken, "sourceTypeToken", string.Empty);
            Scribe_Values.Look(ref conflictFingerprint, "conflictFingerprint", string.Empty);
            Scribe_Values.Look(ref overflowRowCount, "overflowRowCount", 0);
            Scribe_Values.Look(ref overflowLogicalBytes, "overflowLogicalBytes", 0);
            Scribe_Collections.Look(ref diagnosticTokens, "diagnosticTokens", LookMode.Value);
            Scribe_Values.Look(ref migrationReasonToken, "migrationReasonToken", string.Empty);
        }

        public void Normalize()
        {
            archiveRecordId = archiveRecordId ?? string.Empty;
            savedOwnerIdentityKindToken = savedOwnerIdentityKindToken ?? string.Empty;
            savedOwnerIdentityValue = savedOwnerIdentityValue ?? string.Empty;
            originalRecordId = originalRecordId ?? string.Empty;
            sourceOccurrenceId = sourceOccurrenceId ?? string.Empty;
            sourceEventId = sourceEventId ?? string.Empty;
            importedWording = importedWording ?? string.Empty;
            originalKindToken = originalKindToken ?? string.Empty;
            originalSummaryRoleToken = originalSummaryRoleToken ?? string.Empty;
            originalCategoryToken = originalCategoryToken ?? string.Empty;
            originalImportanceToken = originalImportanceToken ?? string.Empty;
            routePolicyToken = routePolicyToken ?? string.Empty;
            sourceTypeToken = sourceTypeToken ?? string.Empty;
            conflictFingerprint = conflictFingerprint ?? string.Empty;
            migrationReasonToken = migrationReasonToken ?? string.Empty;
            diagnosticTokens = diagnosticTokens ?? new List<string>();
            secondarySubjects = secondarySubjects ?? new List<SavedMemorySubjectRef>();
            for (int i = secondarySubjects.Count - 1; i >= 0; i--)
            {
                if (secondarySubjects[i] == null)
                {
                    secondarySubjects.RemoveAt(i);
                    continue;
                }

                secondarySubjects[i].Normalize();
            }

            canonicalFacts = canonicalFacts ?? new List<SavedMemoryCanonicalFact>();
            for (int i = canonicalFacts.Count - 1; i >= 0; i--)
            {
                if (canonicalFacts[i] == null)
                {
                    canonicalFacts.RemoveAt(i);
                    continue;
                }

                canonicalFacts[i].Normalize();
            }

            provenance = provenance ?? new List<SavedMemoryProvenance>();
            for (int i = provenance.Count - 1; i >= 0; i--)
            {
                if (provenance[i] == null)
                {
                    provenance.RemoveAt(i);
                    continue;
                }

                provenance[i].Normalize();
            }

            if (primarySubject != null)
            {
                primarySubject.Normalize();
            }

            if (summaryContributionEvidence != null)
            {
                summaryContributionEvidence.Normalize();
            }
        }
    }

    /// <summary>
    /// The exact element type of the component's legacy rawUnresolvedOwnerArchiveInput list
    /// (§T6.8). It preserves one unresolved legacy record with input-local diagnostic coordinates
    /// BEFORE any semantic mapping. Raw loading must not run ImportantMemoryRecord.Normalize() or
    /// align its parallel lists first — the detached preservation/migration plan owns that step.
    /// </summary>
    public class SavedLegacyUnresolvedOwnerArchiveInputV1 : IExposable
    {
        public int schemaVersion = 1;
        /// <summary>Exactly exact_id | blank | conflicting.</summary>
        public string savedOwnerIdentityKindToken = string.Empty;
        public string savedOwnerIdentityValue = string.Empty;
        /// <summary>Nonnegative input-local diagnostic coordinates only; never identity.</summary>
        public int sourceContainerOrdinal = -1;
        public int sourceRecordOrdinal = -1;
        /// <summary>The shipped ImportantMemoryRecord shape, frozen by the §T6.8 token table.</summary>
        public ImportantMemoryRecord legacyRecord;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref savedOwnerIdentityKindToken,
                "savedOwnerIdentityKindToken", string.Empty);
            Scribe_Values.Look(ref savedOwnerIdentityValue,
                "savedOwnerIdentityValue", string.Empty);
            Scribe_Values.Look(ref sourceContainerOrdinal, "sourceContainerOrdinal", -1);
            Scribe_Values.Look(ref sourceRecordOrdinal, "sourceRecordOrdinal", -1);
            Scribe_Deep.Look(ref legacyRecord, "legacyRecord");
        }

        public void Normalize()
        {
            savedOwnerIdentityKindToken = savedOwnerIdentityKindToken ?? string.Empty;
            savedOwnerIdentityValue = savedOwnerIdentityValue ?? string.Empty;
        }
    }
}

// MemorySavedScalarSchema.cs — the exhaustive scalar/default/Scribe registry for every saved
// memory-system row (design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md §T6.0).
//
// The M0 benchmark froze 32 payload types; later additive memory work brings the exact field-path
// catalog to 406 while preserving every earlier path and type.
// (benchmarks/MemoryThreadBenchmarks/Catalog/memory-payload-atom-catalog-v1.json). This file is the
// code-owned mirror of that frozen catalog: each declared field appears exactly once, with its
// logical width, so Scribe wiring, MemoryLogicalPayloadSizer, migration validation, and tests all
// answer "is this field registered?" from one table. A new saved field without a registry entry is
// a test/build failure (§T6.0), enforced by tests/MemoryThreadTests parity checks against the
// catalog JSON.
//
// This file is pure plain C#: no Verse, Unity, settings, or live-game types. It is compiled into
// both the game assembly and the standalone pure test project.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Logical atom kinds mirroring the frozen catalog's atomKindToken values.</summary>
    internal enum MemorySavedAtomKind
    {
        /// <summary>C# bool; 1 logical byte.</summary>
        Bool = 0,
        /// <summary>C# int (signed-32); 4 logical bytes.</summary>
        Int32 = 1,
        /// <summary>C# long (signed-64); 8 logical bytes.</summary>
        Int64 = 2,
        /// <summary>C# string; variable length, charged by measured UTF-16 units when sized.</summary>
        String = 3,
        /// <summary>A nested deep row charged through its own row fields.</summary>
        Row = 4,
        /// <summary>A nullable singleton row: one absent/present byte plus the nested row.</summary>
        NullableRow = 5,
        /// <summary>A deep or value list; elements are charged individually.</summary>
        List = 6
    }

    /// <summary>One registered field of one saved row.</summary>
    internal sealed class MemorySavedFieldAtom
    {
        public MemorySavedFieldAtom(string fieldName, MemorySavedAtomKind kind)
        {
            fieldNameToken = fieldName;
            atomKind = kind;
        }

        /// <summary>Exact Scribe token / catalog field name.</summary>
        public string fieldNameToken { get; }
        public MemorySavedAtomKind atomKind { get; }
    }

    /// <summary>One saved row's ordered field list. Order matches the frozen catalog.</summary>
    internal sealed class MemorySavedRowFields
    {
        public MemorySavedRowFields(string rowName, MemorySavedFieldAtom[] atoms)
        {
            this.rowName = rowName;
            this.atoms = atoms;
        }

        public string rowName { get; }
        public MemorySavedFieldAtom[] atoms { get; }
    }

    /// <summary>
    /// The one canonical scalar-schema registry (§T6.0). Everything that sizes, validates, migrates,
    /// or Scribes a memory row asks this table instead of keeping a private copy of the shape.
    /// </summary>
    internal static class MemorySavedScalarSchema
    {
        /// <summary>Scribe omission never changes logical admission bytes: fixed widths per §T6.0.</summary>
        public static int LogicalWidthBytes(MemorySavedAtomKind kind)
        {
            switch (kind)
            {
                case MemorySavedAtomKind.Bool: return 1;
                case MemorySavedAtomKind.Int32: return 4;
                case MemorySavedAtomKind.Int64: return 8;
                case MemorySavedAtomKind.NullableRow: return 1;
                default: return 0;
            }
        }

        private static MemorySavedFieldAtom Atom(
            string fieldName,
            MemorySavedAtomKind kind)
        {
            return new MemorySavedFieldAtom(fieldName, kind);
        }

        // Begin generated table — keep byte-parity with memory-payload-atom-catalog-v1.json.
        private static MemorySavedRowFields[] rows = new MemorySavedRowFields[]
        {
            new MemorySavedRowFields(
                "PawnKnowledgeState",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("pawnId",                                         MemorySavedAtomKind.String),
                    Atom("autobiographicalEpochToken",                     MemorySavedAtomKind.String),
                    Atom("archiveOnly",                                    MemorySavedAtomKind.Bool),
                    Atom("epochFenceOnly",                                 MemorySavedAtomKind.Bool),
                    Atom("requestCancellationGeneration",                  MemorySavedAtomKind.Int64),
                    Atom("structuralRevision",                             MemorySavedAtomKind.Int64),
                    Atom("statusRevision",                                 MemorySavedAtomKind.Int64),
                    Atom("completedDiaryEntryOrdinal",                     MemorySavedAtomKind.Int64),
                    Atom("standaloneBlocks",                               MemorySavedAtomKind.List),
                    Atom("threadRoots",                                    MemorySavedAtomKind.List),
                    Atom("playerBackground",                               MemorySavedAtomKind.String),
                    Atom("ownerAwarenessSnapshots",                        MemorySavedAtomKind.List),
                    Atom("openCaptureEpisodes",                            MemorySavedAtomKind.List),
                    Atom("repetitionGuardRows",                            MemorySavedAtomKind.List),
                    Atom("importedArchiveRows",                            MemorySavedAtomKind.List),
                    Atom("migrationDiagnosticFlags",                       MemorySavedAtomKind.Int64),
                }),
            new MemorySavedRowFields(
                "SavedMemoryAwarenessSnapshot",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("snapshotId",                                     MemorySavedAtomKind.String),
                    Atom("scopeKindToken",                                 MemorySavedAtomKind.String),
                    Atom("subjectKind",                                    MemorySavedAtomKind.String),
                    Atom("subjectId",                                      MemorySavedAtomKind.String),
                    Atom("factStreamToken",                                MemorySavedAtomKind.String),
                    Atom("captureInvalidationGeneration",                  MemorySavedAtomKind.Int64),
                    Atom("knownnessEvidenceToken",                         MemorySavedAtomKind.String),
                    Atom("stateFacts",                                     MemorySavedAtomKind.List),
                    Atom("firstObservedTick",                              MemorySavedAtomKind.Int64),
                    Atom("lastObservedTick",                               MemorySavedAtomKind.Int64),
                    Atom("lastSourceOccurrenceId",                         MemorySavedAtomKind.String),
                    Atom("trackingStateToken",                             MemorySavedAtomKind.String),
                    Atom("snapshotRevision",                               MemorySavedAtomKind.Int64),
                }),
            new MemorySavedRowFields(
                "SavedMemoryCaptureEpisode",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("episodeId",                                      MemorySavedAtomKind.String),
                    Atom("captureRuleId",                                  MemorySavedAtomKind.String),
                    Atom("scopeKindToken",                                 MemorySavedAtomKind.String),
                    Atom("factStreamToken",                                MemorySavedAtomKind.String),
                    Atom("category",                                       MemorySavedAtomKind.String),
                    Atom("captureInvalidationGeneration",                  MemorySavedAtomKind.Int64),
                    Atom("episodeKindToken",                               MemorySavedAtomKind.String),
                    Atom("subjectKind",                                    MemorySavedAtomKind.String),
                    Atom("subjectId",                                      MemorySavedAtomKind.String),
                    Atom("pairOrStreamKey",                                MemorySavedAtomKind.String),
                    Atom("directionToken",                                 MemorySavedAtomKind.String),
                    Atom("baselineFacts",                                  MemorySavedAtomKind.List),
                    Atom("currentFacts",                                   MemorySavedAtomKind.List),
                    Atom("firstObservedTick",                              MemorySavedAtomKind.Int64),
                    Atom("lastObservedTick",                               MemorySavedAtomKind.Int64),
                    Atom("lastSourceOccurrenceId",                         MemorySavedAtomKind.String),
                    Atom("episodeRevision",                                MemorySavedAtomKind.Int64),
                }),
            new MemorySavedRowFields(
                "SavedMemoryStateFact",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("factKey",                                        MemorySavedAtomKind.String),
                    Atom("factValue",                                      MemorySavedAtomKind.String),
                }),
            new MemorySavedRowFields(
                "SavedMemoryThreadRoot",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("rootId",                                         MemorySavedAtomKind.String),
                    Atom("ownerPawnId",                                    MemorySavedAtomKind.String),
                    Atom("ownerEpochToken",                                MemorySavedAtomKind.String),
                    Atom("subjectKind",                                    MemorySavedAtomKind.String),
                    Atom("subjectId",                                      MemorySavedAtomKind.String),
                    Atom("frozenSubjectLabel",                             MemorySavedAtomKind.String),
                    Atom("structuralRevision",                             MemorySavedAtomKind.Int64),
                    Atom("statusRevision",                                 MemorySavedAtomKind.Int64),
                    Atom("lastAppliedReducerRevision",                     MemorySavedAtomKind.Int32),
                    Atom("nextChapterOrdinal",                             MemorySavedAtomKind.Int64),
                    Atom("chapters",                                       MemorySavedAtomKind.List),
                    Atom("visibleBlocks",                                  MemorySavedAtomKind.List),
                    Atom("rollingSummaryBlock",                            MemorySavedAtomKind.NullableRow),
                }),
            new MemorySavedRowFields(
                "SavedMemoryChapter",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("chapterId",                                      MemorySavedAtomKind.String),
                    Atom("ordinal",                                        MemorySavedAtomKind.Int64),
                    Atom("phaseToken",                                     MemorySavedAtomKind.String),
                    Atom("openedTick",                                     MemorySavedAtomKind.Int64),
                    Atom("lastActivityTick",                               MemorySavedAtomKind.Int64),
                    Atom("closedTick",                                     MemorySavedAtomKind.Int64),
                    Atom("closureReasonToken",                             MemorySavedAtomKind.String),
                    Atom("closed",                                         MemorySavedAtomKind.Bool),
                    Atom("closedSummaryRecordId",                          MemorySavedAtomKind.String),
                }),
            new MemorySavedRowFields(
                "SavedMemoryBlock",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("recordId",                                       MemorySavedAtomKind.String),
                    Atom("sourceOccurrenceId",                             MemorySavedAtomKind.String),
                    Atom("sourceEventId",                                  MemorySavedAtomKind.String),
                    Atom("captureRuleId",                                  MemorySavedAtomKind.String),
                    Atom("factDiscriminator",                              MemorySavedAtomKind.String),
                    Atom("ownerPawnId",                                    MemorySavedAtomKind.String),
                    Atom("ownerEpochToken",                                MemorySavedAtomKind.String),
                    Atom("kind",                                           MemorySavedAtomKind.String),
                    Atom("summaryRole",                                    MemorySavedAtomKind.String),
                    Atom("category",                                       MemorySavedAtomKind.String),
                    Atom("importance",                                     MemorySavedAtomKind.String),
                    Atom("originalEventTick",                              MemorySavedAtomKind.Int64),
                    Atom("ageUnknown",                                     MemorySavedAtomKind.Bool),
                    Atom("rootId",                                         MemorySavedAtomKind.String),
                    Atom("chapterId",                                      MemorySavedAtomKind.String),
                    Atom("primarySubject",                                 MemorySavedAtomKind.NullableRow),
                    Atom("secondarySubjects",                              MemorySavedAtomKind.List),
                    Atom("facts",                                          MemorySavedAtomKind.List),
                    Atom("provenance",                                     MemorySavedAtomKind.List),
                    Atom("automaticWording",                               MemorySavedAtomKind.String),
                    Atom("optionalLlmWording",                             MemorySavedAtomKind.String),
                    Atom("optionalLlmWordingRevision",                     MemorySavedAtomKind.Int64),
                    Atom("optionalLlmFingerprint",                         MemorySavedAtomKind.String),
                    Atom("optionalLlmFormatRevision",                      MemorySavedAtomKind.Int64),
                    Atom("optionalLlmCategoryMask",                        MemorySavedAtomKind.Int32),
                    Atom("playerWording",                                  MemorySavedAtomKind.String),
                    Atom("playerEdited",                                   MemorySavedAtomKind.Bool),
                    Atom("suppressed",                                     MemorySavedAtomKind.Bool),
                    Atom("requiredLifecycleLandmark",                      MemorySavedAtomKind.Bool),
                    Atom("formatRevision",                                 MemorySavedAtomKind.Int64),
                    Atom("lastAutomaticIncludedTick",                      MemorySavedAtomKind.Int64),
                    Atom("lastAutomaticIncludedEntryOrdinal",              MemorySavedAtomKind.Int64),
                    Atom("automaticInclusionCount",                        MemorySavedAtomKind.Int64),
                    Atom("providerExposureState",                          MemorySavedAtomKind.String),
                    Atom("lastProviderExposureTick",                       MemorySavedAtomKind.Int64),
                    Atom("summaryPayload",                                 MemorySavedAtomKind.NullableRow),
                }),
            new MemorySavedRowFields(
                "SavedMemorySubjectRef",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("subjectRefId",                                   MemorySavedAtomKind.String),
                    Atom("subjectKind",                                    MemorySavedAtomKind.String),
                    Atom("subjectId",                                      MemorySavedAtomKind.String),
                    Atom("frozenLabel",                                    MemorySavedAtomKind.String),
                    Atom("roleToken",                                      MemorySavedAtomKind.String),
                    Atom("knownnessToken",                                 MemorySavedAtomKind.String),
                }),
            new MemorySavedRowFields(
                "SavedMemoryCanonicalFact",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("factId",                                         MemorySavedAtomKind.String),
                    Atom("factKind",                                       MemorySavedAtomKind.String),
                    Atom("canonicalSubjectKind",                           MemorySavedAtomKind.String),
                    Atom("canonicalSubjectId",                             MemorySavedAtomKind.String),
                    Atom("aggregationToken",                               MemorySavedAtomKind.String),
                    Atom("canonicalValueKind",                             MemorySavedAtomKind.String),
                    Atom("canonicalValue",                                 MemorySavedAtomKind.String),
                    Atom("majorTurningPoint",                              MemorySavedAtomKind.Bool),
                    Atom("reversal",                                       MemorySavedAtomKind.Bool),
                }),
            new MemorySavedRowFields(
                "SavedMemoryProvenance",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("provenanceRefId",                                MemorySavedAtomKind.String),
                    Atom("sourceKindToken",                                MemorySavedAtomKind.String),
                    Atom("sourceOccurrenceId",                             MemorySavedAtomKind.String),
                    Atom("sourceEventId",                                  MemorySavedAtomKind.String),
                    Atom("captureRuleId",                                  MemorySavedAtomKind.String),
                    Atom("factDiscriminator",                              MemorySavedAtomKind.String),
                    Atom("integrationToken",                               MemorySavedAtomKind.String),
                }),
            new MemorySavedRowFields(
                "SavedMemorySummaryPayload",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("reducerRevision",                                MemorySavedAtomKind.Int32),
                    Atom("factsRevision",                                  MemorySavedAtomKind.Int64),
                    Atom("canonicalFactsFingerprint",                      MemorySavedAtomKind.String),
                    Atom("factBuckets",                                    MemorySavedAtomKind.List),
                    Atom("subjectRefs",                                    MemorySavedAtomKind.List),
                    Atom("provenanceRefs",                                 MemorySavedAtomKind.List),
                    Atom("derivedCategoryMask",                            MemorySavedAtomKind.Int32),
                    Atom("highestSurvivingImportance",                     MemorySavedAtomKind.String),
                    Atom("earliestSurvivingTick",                          MemorySavedAtomKind.Int64),
                    Atom("latestSurvivingTick",                            MemorySavedAtomKind.Int64),
                    Atom("deterministicWording",                           MemorySavedAtomKind.String),
                    Atom("optionalLlmWording",                             MemorySavedAtomKind.String),
                    Atom("optionalLlmFingerprint",                         MemorySavedAtomKind.String),
                    Atom("optionalLlmFormatRevision",                      MemorySavedAtomKind.Int64),
                    Atom("optionalLlmCategoryMask",                        MemorySavedAtomKind.Int32),
                    Atom("lastSettledWordingFingerprint",                  MemorySavedAtomKind.String),
                    Atom("lastSettledWordingReducerRevision",              MemorySavedAtomKind.Int32),
                    Atom("lastSettledWordingFormatRevision",               MemorySavedAtomKind.Int64),
                    Atom("lastWordingDispositionToken",                    MemorySavedAtomKind.String),
                }),
            new MemorySavedRowFields(
                "SavedMemoryFactBucket",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("bucketKey",                                      MemorySavedAtomKind.String),
                    Atom("factKind",                                       MemorySavedAtomKind.String),
                    Atom("canonicalSubjectKind",                           MemorySavedAtomKind.String),
                    Atom("canonicalSubjectId",                             MemorySavedAtomKind.String),
                    Atom("aggregationToken",                               MemorySavedAtomKind.String),
                    Atom("contributions",                                  MemorySavedAtomKind.List),
                    Atom("derivedCount",                                   MemorySavedAtomKind.Int32),
                    Atom("derivedRangeMin",                                MemorySavedAtomKind.String),
                    Atom("derivedRangeMax",                                MemorySavedAtomKind.String),
                    Atom("earliestSurvivingTick",                          MemorySavedAtomKind.Int64),
                    Atom("latestSurvivingTick",                            MemorySavedAtomKind.Int64),
                }),
            new MemorySavedRowFields(
                "SavedMemoryFactContribution",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("contributionId",                                 MemorySavedAtomKind.String),
                    Atom("originChapterId",                                MemorySavedAtomKind.String),
                    Atom("originRecordId",                                 MemorySavedAtomKind.String),
                    Atom("originFactOrdinal",                              MemorySavedAtomKind.Int32),
                    Atom("originFactId",                                   MemorySavedAtomKind.String),
                    Atom("originalEventTick",                              MemorySavedAtomKind.Int64),
                    Atom("ageUnknown",                                     MemorySavedAtomKind.Bool),
                    Atom("category",                                       MemorySavedAtomKind.String),
                    Atom("importance",                                     MemorySavedAtomKind.String),
                    Atom("canonicalValue",                                 MemorySavedAtomKind.String),
                    Atom("majorTurningPoint",                              MemorySavedAtomKind.Bool),
                    Atom("reversal",                                       MemorySavedAtomKind.Bool),
                    Atom("sourceOccurrenceId",                             MemorySavedAtomKind.String),
                    Atom("subjectRefIds",                                  MemorySavedAtomKind.List),
                    Atom("provenanceRefIds",                               MemorySavedAtomKind.List),
                }),
            new MemorySavedRowFields(
                "SavedImportedMemoryRow",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("archiveRecordId",                                MemorySavedAtomKind.String),
                    Atom("savedOwnerIdentityKindToken",                    MemorySavedAtomKind.String),
                    Atom("savedOwnerIdentityValue",                        MemorySavedAtomKind.String),
                    Atom("reattributionGeneration",                        MemorySavedAtomKind.Int64),
                    Atom("originalRecordId",                               MemorySavedAtomKind.String),
                    Atom("sourceOccurrenceId",                             MemorySavedAtomKind.String),
                    Atom("sourceEventId",                                  MemorySavedAtomKind.String),
                    Atom("originalEventTick",                              MemorySavedAtomKind.Int64),
                    Atom("ageUnknown",                                     MemorySavedAtomKind.Bool),
                    Atom("importedWording",                                MemorySavedAtomKind.String),
                    Atom("originalKindToken",                              MemorySavedAtomKind.String),
                    Atom("originalSummaryRoleToken",                       MemorySavedAtomKind.String),
                    Atom("originalCategoryToken",                          MemorySavedAtomKind.String),
                    Atom("originalImportanceToken",                        MemorySavedAtomKind.String),
                    Atom("routePolicyToken",                               MemorySavedAtomKind.String),
                    Atom("primarySubject",                                 MemorySavedAtomKind.NullableRow),
                    Atom("secondarySubjects",                              MemorySavedAtomKind.List),
                    Atom("canonicalFacts",                                 MemorySavedAtomKind.List),
                    Atom("provenance",                                     MemorySavedAtomKind.List),
                    Atom("summaryContributionEvidence",                    MemorySavedAtomKind.NullableRow),
                    Atom("sourceTypeToken",                                MemorySavedAtomKind.String),
                    Atom("conflictFingerprint",                            MemorySavedAtomKind.String),
                    Atom("overflowRowCount",                               MemorySavedAtomKind.Int64),
                    Atom("overflowLogicalBytes",                           MemorySavedAtomKind.Int64),
                    Atom("diagnosticTokens",                               MemorySavedAtomKind.List),
                    Atom("migrationReasonToken",                           MemorySavedAtomKind.String),
                }),
            new MemorySavedRowFields(
                "SavedImportedSummaryContributionEvidenceV1",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("contributionId",                                 MemorySavedAtomKind.String),
                    Atom("originChapterId",                                MemorySavedAtomKind.String),
                    Atom("originRecordId",                                 MemorySavedAtomKind.String),
                    Atom("originFactOrdinal",                              MemorySavedAtomKind.Int32),
                    Atom("originFactId",                                   MemorySavedAtomKind.String),
                    Atom("originalEventTick",                              MemorySavedAtomKind.Int64),
                    Atom("ageUnknown",                                     MemorySavedAtomKind.Bool),
                    Atom("category",                                       MemorySavedAtomKind.String),
                    Atom("importance",                                     MemorySavedAtomKind.String),
                    Atom("canonicalValue",                                 MemorySavedAtomKind.String),
                    Atom("majorTurningPoint",                              MemorySavedAtomKind.Bool),
                    Atom("reversal",                                       MemorySavedAtomKind.Bool),
                    Atom("sourceOccurrenceId",                             MemorySavedAtomKind.String),
                    Atom("subjectRefIds",                                  MemorySavedAtomKind.List),
                    Atom("provenanceRefIds",                               MemorySavedAtomKind.List),
                }),
            new MemorySavedRowFields(
                "SavedLegacyUnresolvedOwnerArchiveInputV1",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("savedOwnerIdentityKindToken",                    MemorySavedAtomKind.String),
                    Atom("savedOwnerIdentityValue",                        MemorySavedAtomKind.String),
                    Atom("sourceContainerOrdinal",                         MemorySavedAtomKind.Int32),
                    Atom("sourceRecordOrdinal",                            MemorySavedAtomKind.Int32),
                    Atom("legacyRecord",                                   MemorySavedAtomKind.NullableRow),
                }),
            new MemorySavedRowFields(
                "DiaryGameComponentMemory",
                new[]
                {
                    Atom("memoryComponentSchemaVersion",                   MemorySavedAtomKind.Int32),
                    Atom("lastIssuedAutobiographicalEpochSequence",        MemorySavedAtomKind.Int64),
                    Atom("lastIssuedAutobiographicalEpochFallbackChain",   MemorySavedAtomKind.String),
                    Atom("globalFactionSnapshotAllocatorGeneration",       MemorySavedAtomKind.Int64),
                    Atom("globalFactionSnapshots",                         MemorySavedAtomKind.List),
                    Atom("legacyOwnerEpochReservations",                   MemorySavedAtomKind.List),
                    Atom("globalOptionalRequestCancellationGeneration",    MemorySavedAtomKind.Int64),
                    Atom("optionalMeaningfulEligibilityBaselineTick",      MemorySavedAtomKind.Int64),
                    Atom("lastAppliedMemoryPolicyRevision",                MemorySavedAtomKind.Int64),
                    Atom("lastAppliedMemoryPolicyFingerprint",             MemorySavedAtomKind.String),
                    Atom("lastAppliedMemoryPolicyState",                   MemorySavedAtomKind.NullableRow),
                    Atom("unresolvedOwnerArchiveRows",                     MemorySavedAtomKind.List),
                    Atom("unresolvedArchiveMigrationState",                MemorySavedAtomKind.String),
                    Atom("rawUnresolvedOwnerArchiveInput",                 MemorySavedAtomKind.List),
                    Atom("rawUnresolvedArchiveReattributionGeneration",    MemorySavedAtomKind.Int64),
                    Atom("unresolvedArchiveReattributionGeneration",       MemorySavedAtomKind.Int64),
                    Atom("unresolvedArchiveStructuralRevision",            MemorySavedAtomKind.Int64),
                    Atom("unresolvedArchiveReattributionDisabled",         MemorySavedAtomKind.Bool),
                    Atom("memoryCoordinatorSchemaVersion",                 MemorySavedAtomKind.Int32),
                    Atom("summaryWordingOpportunities",                    MemorySavedAtomKind.List),
                    Atom("memoryDiagnosticCounters",                       MemorySavedAtomKind.List),
                    Atom("memoryAttemptAuditRows",                         MemorySavedAtomKind.List),
                    Atom("memoryDispatchSchemaVersion",                    MemorySavedAtomKind.Int32),
                    Atom("lastIssuedMemoryLogicalRequestSequence",         MemorySavedAtomKind.Int64),
                    Atom("activeMemoryCoordinatorRequests",                MemorySavedAtomKind.List),
                }),
            new MemorySavedRowFields(
                "SavedMemoryAppliedPolicyStateV1",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("saveNewMemories",                                MemorySavedAtomKind.Bool),
                    Atom("useMemoriesInWriting",                           MemorySavedAtomKind.Bool),
                    Atom("usePawnBackground",                              MemorySavedAtomKind.Bool),
                    Atom("allowExtraMemoryAiRequests",                     MemorySavedAtomKind.Bool),
                    Atom("occasionalMemoryReflections",                    MemorySavedAtomKind.Bool),
                    Atom("memoryCategoryMask",                             MemorySavedAtomKind.Int32),
                    Atom("captureInvalidationGenerationPersonal",          MemorySavedAtomKind.Int64),
                    Atom("captureInvalidationGenerationRelationships",     MemorySavedAtomKind.Int64),
                    Atom("captureInvalidationGenerationFamily",            MemorySavedAtomKind.Int64),
                    Atom("captureInvalidationGenerationFactions",          MemorySavedAtomKind.Int64),
                    Atom("optionalRequestInvalidationGeneration",          MemorySavedAtomKind.Int64),
                    Atom("minorMemoryLifetimeDays",                        MemorySavedAtomKind.Int32),
                    Atom("regularMemoryLifetimeDays",                      MemorySavedAtomKind.Int32),
                    Atom("memoryThreadTarget",                             MemorySavedAtomKind.Int32),
                    Atom("memoryReuseDays",                                MemorySavedAtomKind.Int32),
                    Atom("memoryRevisitEntryCount",                        MemorySavedAtomKind.Int32),
                }),
            new MemorySavedRowFields(
                "SavedSummaryWordingOpportunityV1",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("ownerPawnId",                                    MemorySavedAtomKind.String),
                    Atom("ownerEpochToken",                                MemorySavedAtomKind.String),
                    Atom("ownerCancellationGeneration",                    MemorySavedAtomKind.Int64),
                    Atom("globalCancellationGeneration",                   MemorySavedAtomKind.Int64),
                    Atom("optionalRequestInvalidationGeneration",          MemorySavedAtomKind.Int64),
                    Atom("rootId",                                         MemorySavedAtomKind.String),
                    Atom("summaryRecordId",                                MemorySavedAtomKind.String),
                    Atom("expectedRootStructuralRevision",                 MemorySavedAtomKind.Int64),
                    Atom("expectedSummaryFactsRevision",                   MemorySavedAtomKind.Int64),
                    Atom("expectedReducerRevision",                        MemorySavedAtomKind.Int32),
                    Atom("expectedFormatRevision",                         MemorySavedAtomKind.Int64),
                    Atom("expectedOptionalLlmWordingRevision",             MemorySavedAtomKind.Int64),
                    Atom("expectedCategoryMask",                           MemorySavedAtomKind.Int32),
                    Atom("projectionFingerprint",                          MemorySavedAtomKind.String),
                    Atom("requestedTick",                                  MemorySavedAtomKind.Int64),
                    Atom("dueTick",                                        MemorySavedAtomKind.Int64),
                    Atom("expiryTick",                                     MemorySavedAtomKind.Int64),
                    Atom("configuredPriority",                             MemorySavedAtomKind.Int32),
                    Atom("salience",                                       MemorySavedAtomKind.Int32),
                    Atom("opportunityKey",                                 MemorySavedAtomKind.String),
                }),
            new MemorySavedRowFields(
                "SavedMemoryDiagnosticCounter",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("reasonToken",                                    MemorySavedAtomKind.String),
                    Atom("scopeToken",                                     MemorySavedAtomKind.String),
                    Atom("saturatedCount",                                 MemorySavedAtomKind.Int64),
                }),
            new MemorySavedRowFields(
                "SavedMemoryAttemptAuditRow",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("logicalRequestId",                               MemorySavedAtomKind.String),
                    Atom("requestPurposeToken",                            MemorySavedAtomKind.String),
                    Atom("ownerPawnId",                                    MemorySavedAtomKind.String),
                    Atom("ownerEpochToken",                                MemorySavedAtomKind.String),
                    Atom("attemptOrdinal",                                 MemorySavedAtomKind.Int32),
                    Atom("variantKey",                                     MemorySavedAtomKind.String),
                    Atom("invocationTick",                                 MemorySavedAtomKind.Int64),
                    Atom("terminalTick",                                   MemorySavedAtomKind.Int64),
                    Atom("outcomeToken",                                   MemorySavedAtomKind.String),
                    Atom("potentialExposure",                              MemorySavedAtomKind.Bool),
                }),
            new MemorySavedRowFields(
                "SavedGlobalFactionSnapshot",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("factionInstanceId",                              MemorySavedAtomKind.String),
                    Atom("allocatorGeneration",                            MemorySavedAtomKind.Int64),
                    Atom("factionDefName",                                 MemorySavedAtomKind.String),
                    Atom("frozenDisplayLabel",                             MemorySavedAtomKind.String),
                    Atom("goodwill",                                       MemorySavedAtomKind.Int32),
                    Atom("relationKindToken",                              MemorySavedAtomKind.String),
                    Atom("leaderPawnId",                                   MemorySavedAtomKind.String),
                    Atom("defeated",                                       MemorySavedAtomKind.Bool),
                    Atom("removed",                                        MemorySavedAtomKind.Bool),
                    Atom("observedTick",                                   MemorySavedAtomKind.Int64),
                    Atom("trackingStateToken",                             MemorySavedAtomKind.String),
                    Atom("snapshotRevision",                               MemorySavedAtomKind.Int64),
                }),
            new MemorySavedRowFields(
                "SavedLegacyOwnerEpochReservation",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("ownerPawnId",                                    MemorySavedAtomKind.String),
                    Atom("reservedEpochSequence",                          MemorySavedAtomKind.Int64),
                }),
            new MemorySavedRowFields(
                "SavedMemoryRepetitionGuardRow",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("ownerEpochToken",                                MemorySavedAtomKind.String),
                    Atom("guardKind",                                      MemorySavedAtomKind.String),
                    Atom("guardKey",                                       MemorySavedAtomKind.String),
                    Atom("lastAutomaticIncludedTick",                      MemorySavedAtomKind.Int64),
                    Atom("lastAutomaticIncludedEntryOrdinal",              MemorySavedAtomKind.Int64),
                    Atom("automaticInclusionCount",                        MemorySavedAtomKind.Int64),
                    Atom("lastSourceOccurrenceId",                         MemorySavedAtomKind.String),
                    Atom("lastCommittedLogicalRequestId",                  MemorySavedAtomKind.String),
                    Atom("lastCommittedEvidenceSetFingerprint",            MemorySavedAtomKind.String),
                }),
            new MemorySavedRowFields(
                "PawnReflectionStateMemoryFields",
                new[]
                {
                    Atom("memoryReflectionSchemaVersion",                  MemorySavedAtomKind.Int32),
                    Atom("memoryOwnerEpochToken",                          MemorySavedAtomKind.String),
                    Atom("lastQuietMemoryEvaluatedAbsoluteDay",            MemorySavedAtomKind.Int32),
                    Atom("lastQuietMemoryActivatedAbsoluteQuadrum",        MemorySavedAtomKind.Int32),
                    Atom("lastQuietMemoryDecisionKey",                     MemorySavedAtomKind.String),
                }),
            new MemorySavedRowFields(
                "SavedActiveLogicalRequestV1",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("logicalRequestSequence",                         MemorySavedAtomKind.Int64),
                    Atom("logicalRequestId",                               MemorySavedAtomKind.String),
                    Atom("logicalRequestKey",                              MemorySavedAtomKind.String),
                    Atom("requestPurposeToken",                            MemorySavedAtomKind.String),
                    Atom("sessionId",                                      MemorySavedAtomKind.Int64),
                    Atom("eventIdOrOpportunityKey",                        MemorySavedAtomKind.String),
                    Atom("povRoleToken",                                   MemorySavedAtomKind.String),
                    Atom("ownerPawnId",                                    MemorySavedAtomKind.String),
                    Atom("ownerEpochToken",                                MemorySavedAtomKind.String),
                    Atom("evidenceEpochToken",                             MemorySavedAtomKind.String),
                    Atom("ownerCancellationGeneration",                    MemorySavedAtomKind.Int64),
                    Atom("globalCancellationGeneration",                   MemorySavedAtomKind.Int64),
                    Atom("optionalRequestInvalidationGeneration",          MemorySavedAtomKind.Int64),
                    Atom("requestStateToken",                              MemorySavedAtomKind.String),
                    Atom("lastIssuedAttemptOrdinal",                       MemorySavedAtomKind.Int32),
                    Atom("narrativeUseWinnerAttemptOrdinal",               MemorySavedAtomKind.Int32),
                    Atom("narrativeUseWinnerVariantKey",                   MemorySavedAtomKind.String),
                    Atom("frozenVariants",                                 MemorySavedAtomKind.List),
                    Atom("activeAttempts",                                 MemorySavedAtomKind.List),
                    Atom("reservedEvidenceEntries",                        MemorySavedAtomKind.List),
                    Atom("reservedGuardEntries",                           MemorySavedAtomKind.List),
                }),
            new MemorySavedRowFields(
                "SavedFrozenPromptVariantV1",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("variantOrdinal",                                 MemorySavedAtomKind.Int32),
                    Atom("variantKey",                                     MemorySavedAtomKind.String),
                    Atom("templateIdentity",                               MemorySavedAtomKind.String),
                    Atom("contextDetailIdentity",                          MemorySavedAtomKind.String),
                    Atom("systemPrompt",                                   MemorySavedAtomKind.String),
                    Atom("userPrompt",                                     MemorySavedAtomKind.String),
                    Atom("receiptPlan",                                    MemorySavedAtomKind.NullableRow),
                    Atom("diagnosticProvenance",                           MemorySavedAtomKind.List),
                }),
            new MemorySavedRowFields(
                "SavedFrozenEvidenceReceiptPlanV1",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("evidenceSetFingerprint",                         MemorySavedAtomKind.String),
                    Atom("evidenceEntries",                                MemorySavedAtomKind.List),
                    Atom("guardEntries",                                   MemorySavedAtomKind.List),
                }),
            new MemorySavedRowFields(
                "SavedFrozenEvidenceEntryV1",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("recordId",                                       MemorySavedAtomKind.String),
                    Atom("sourceOccurrenceId",                             MemorySavedAtomKind.String),
                    Atom("rootIdOrEmpty",                                  MemorySavedAtomKind.String),
                }),
            new MemorySavedRowFields(
                "SavedFrozenGuardEntryV1",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("guardKind",                                      MemorySavedAtomKind.String),
                    Atom("guardKey",                                       MemorySavedAtomKind.String),
                }),
            new MemorySavedRowFields(
                "SavedFrozenDiagnosticProvenanceV1",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("provenanceKindToken",                            MemorySavedAtomKind.String),
                    Atom("sourceId",                                       MemorySavedAtomKind.String),
                    Atom("recordIdOrEmpty",                                MemorySavedAtomKind.String),
                    Atom("sourceOccurrenceIdOrEmpty",                      MemorySavedAtomKind.String),
                    Atom("rootIdOrEmpty",                                  MemorySavedAtomKind.String),
                    Atom("lineOrdinal",                                    MemorySavedAtomKind.Int32),
                }),
            new MemorySavedRowFields(
                "SavedActiveLogicalAttemptV1",
                new[]
                {
                    Atom("schemaVersion",                                  MemorySavedAtomKind.Int32),
                    Atom("attemptOrdinal",                                 MemorySavedAtomKind.Int32),
                    Atom("variantKey",                                     MemorySavedAtomKind.String),
                    Atom("attemptOriginToken",                             MemorySavedAtomKind.String),
                    Atom("predecessorAttemptOrdinal",                      MemorySavedAtomKind.Int32),
                    Atom("attemptStateToken",                              MemorySavedAtomKind.String),
                    Atom("invocationSequence",                             MemorySavedAtomKind.Int64),
                    Atom("invocationTick",                                 MemorySavedAtomKind.Int64),
                    Atom("terminalTick",                                   MemorySavedAtomKind.Int64),
                    Atom("terminalOutcomeToken",                           MemorySavedAtomKind.String),
                    Atom("potentialExposureApplied",                       MemorySavedAtomKind.Bool),
                    Atom("narrativeUseApplied",                            MemorySavedAtomKind.Bool),
                    Atom("resultApplied",                                  MemorySavedAtomKind.Bool),
                }),
        };

        /// <summary>Returns every registered row in frozen catalog order.</summary>
        public static IReadOnlyList<MemorySavedRowFields> Rows()
        {
            return rows;
        }

        /// <summary>Looks up one row's ordered fields, or null for an unknown row name.</summary>
        public static MemorySavedRowFields Row(string rowName)
        {
            for (int index = 0; index < rows.Length; index++)
            {
                if (string.Equals(rows[index].rowName, rowName, StringComparison.Ordinal))
                {
                    return rows[index];
                }
            }

            return null;
        }

        /// <summary>Total number of registered field paths (406 after wording revision fences).</summary>
        public static int AtomCount()
        {
            int total = 0;
            for (int index = 0; index < rows.Length; index++)
            {
                total += rows[index].atoms.Length;
            }

            return total;
        }
    }

    /// <summary>
    /// Pure version-boundary policy for the owner envelope's schemaVersion token (§T6.1). The
    /// saved IExposable class and every migration/store path ask this policy instead of embedding
    /// private version literals, so the boundary stays testable without Verse.
    /// </summary>
    internal static class PawnKnowledgeStateSchemaPolicy
    {
        /// <summary>The only writable current shape.</summary>
        public const int CurrentVersion = 3;

        /// <summary>Missing pre-feature data reads as this shipped legacy default.</summary>
        public const int MissingLegacyDefault = 1;

        public enum VersionClass
        {
            /// <summary>Explicit version 0: raw malformed/legacy input until migration resolves it.</summary>
            RawLegacy = 0,
            /// <summary>Versions 1 and 2: supported legacy inputs pending component migration.</summary>
            LegacyPendingMigration = 1,
            /// <summary>Version 3: the only writable current shape.</summary>
            Current = 2,
            /// <summary>Above current: whole-save newer-format failure before gameplay.</summary>
            NewerThanCurrent = 3
        }

        public static VersionClass Classify(int schemaVersion)
        {
            if (schemaVersion >= CurrentVersion)
            {
                return schemaVersion == CurrentVersion
                    ? VersionClass.Current
                    : VersionClass.NewerThanCurrent;
            }

            if (schemaVersion == MissingLegacyDefault
                || schemaVersion == 2)
            {
                return VersionClass.LegacyPendingMigration;
            }

            return VersionClass.RawLegacy;
        }

        /// <summary>True only when an owner commit may write/stamp this version as current.</summary>
        public static bool CanWriteCurrent(int schemaVersion)
        {
            return Classify(schemaVersion) == VersionClass.Current;
        }

        /// <summary>True while the owner still needs the component migration transaction.</summary>
        public static bool IsMigrationPending(int schemaVersion)
        {
            VersionClass versionClass = Classify(schemaVersion);
            return versionClass == VersionClass.RawLegacy
                || versionClass == VersionClass.LegacyPendingMigration;
        }
    }
}

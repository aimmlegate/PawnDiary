// PawnDiaryMemoryM1FixtureTests.cs — LOADED RimTest fixtures for the M1 additive persistence
// envelope (design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md phase M1 exit gate).
//
// COMPILE-ONLY FOR AGENTS. The user runs these inside RimWorld/RimTest Redux against a copied
// save; agents never launch the game. Evidence returned by these fixtures feeds the M1/M11
// "new and legacy saves round-trip" cells that pure harnesses cannot produce.
//
// Covered here (loaded Scribe only):
// - a current-shape (v3) owner envelope round-trips every unified-memory collection;
// - a legacy v1 envelope stays v1 through real Scribe + Normalize (no eager stamp);
// - thread root/chapter/block/payload rows round-trip their stable tokens;
// - dispatch request/variant/attempt and optional coordinator/cadence rows round-trip;
// - the M10 summary-only wake decision is executable through the M11 CurrentRelease gate;
// - the raw unresolved-owner wrapper preserves its nested shipped legacy record untouched;
// - malformed legacy Scribe evidence reaches dry-run planning unchanged and remains retryable;
// - nested allocator/schema carriers cannot hide from recursive component scans;
// - owner/global byte accounting and null-hole sizing follow the frozen one-charge policy;
// - archive normalization and real pressure commits preserve physical culture fields exactly;
// - legacy commit ignores ordinary capture gates and carries its budget across owner transactions;
// - the logical-size walker validates a fully populated envelope against the frozen registry.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using PawnDiary;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    public static class PawnDiaryMemoryM1FixtureTests
    {
        private const string Label = "mem";
        private static PawnDiaryRecord legacyMissingKnowledgeTarget;

        [Test]
        public static void MemoryEnvelopeCurrentShapeRoundTrips()
        {
            PawnKnowledgeState source = PawnKnowledgeState.CreateCurrent("Pawn_M1");
            source.autobiographicalEpochToken = EpochToken(3);
            source.playerBackground = "Raised on tales of the rim.";
            SavedMemoryBlock standalone = NewBlock("rec-standalone", null);
            standalone.optionalLlmWording = "I still remember the day everything changed.";
            standalone.optionalLlmFingerprint = new string('e', 64);
            standalone.optionalLlmFormatRevision = 3;
            standalone.optionalLlmCategoryMask = 5;
            source.standaloneBlocks.Add(standalone);
            source.threadRoots.Add(NewRoot());
            source.ownerAwarenessSnapshots.Add(new SavedMemoryAwarenessSnapshot
            {
                snapshotId = "snap-1",
                scopeKindToken = "relationship",
                subjectKind = "pawn",
                subjectId = "Pawn_B",
                factStreamToken = "body_history",
                captureInvalidationGeneration = 1,
                knownnessEvidenceToken = "direct",
                trackingStateToken = "tracked",
                snapshotRevision = 2
            });
            source.openCaptureEpisodes.Add(new SavedMemoryCaptureEpisode
            {
                episodeId = "ep-1",
                captureRuleId = "rule-a",
                scopeKindToken = "faction",
                factStreamToken = "colony_membership",
                category = "factions"
            });
            source.repetitionGuardRows.Add(new SavedMemoryRepetitionGuardRow
            {
                ownerEpochToken = EpochToken(3),
                guardKind = "root",
                guardKey = "guard-key-1"
            });
            source.importedArchiveRows.Add(new SavedImportedMemoryRow
            {
                archiveRecordId = "archive-1",
                savedOwnerIdentityKindToken = "exact_id",
                savedOwnerIdentityValue = "Pawn_C",
                migrationReasonToken = "authored_conflict"
            });

            RunWithTempFile(path =>
            {
                PawnKnowledgeState saved = source;
                SaveWithScribe(path, () => Scribe_Deep.Look(ref saved, Label));

                PawnKnowledgeState loaded = null;
                LoadVarsWithScribe(path, () => Scribe_Deep.Look(ref loaded, Label));
                Require(loaded != null, "v3 envelope must round-trip.");
                Require(loaded.schemaVersion == PawnKnowledgeState.CurrentSchemaVersion,
                    "A current-shape envelope must load as version 3.");
                Require(loaded.structuralRevision == 1 && loaded.statusRevision == 1
                        && loaded.requestCancellationGeneration == 1
                        && loaded.completedDiaryEntryOrdinal == 1,
                    "Factory positive invariants must survive Scribe.");
                Require(loaded.autobiographicalEpochToken == EpochToken(3),
                    "The epoch token must survive unchanged.");
                Require(loaded.standaloneBlocks.Count == 1
                        && loaded.threadRoots.Count == 1
                        && loaded.ownerAwarenessSnapshots.Count == 1
                        && loaded.openCaptureEpisodes.Count == 1
                        && loaded.repetitionGuardRows.Count == 1
                        && loaded.importedArchiveRows.Count == 1,
                    "Every memory collection must round-trip its row count.");
                SavedMemoryBlock loadedStandalone = loaded.standaloneBlocks[0];
                Require(loadedStandalone.optionalLlmWording
                            == "I still remember the day everything changed."
                        && loadedStandalone.optionalLlmFingerprint == new string('e', 64)
                        && loadedStandalone.optionalLlmFormatRevision == 3
                        && loadedStandalone.optionalLlmCategoryMask == 5,
                    "The optional block wording cache must round-trip all four scalar fields.");
                Require(loaded.playerBackground == "Raised on tales of the rim.",
                    "The authored background must survive.");

                SavedMemoryThreadRoot root = loaded.threadRoots[0];
                Require(root.chapters.Count == 1 && root.chapters[0].closed
                        && root.chapters[0].ordinal == 1
                        && root.chapters[0].closureReasonToken == "formal_end",
                    "Chapter metadata must survive.");
                Require(root.visibleBlocks.Count == 1
                        && root.visibleBlocks[0].summaryPayload != null
                        && root.visibleBlocks[0].summaryPayload.factBuckets.Count == 1
                        && root.visibleBlocks[0].summaryPayload.factBuckets[0]
                            .contributions.Count == 1,
                    "Block + summary payload/bucket/contribution nesting must survive.");
                Require(root.rollingSummaryBlock != null
                        && root.rollingSummaryBlock.summaryRole == "rolling"
                        && root.rollingSummaryBlock.primarySubject != null
                        && root.rollingSummaryBlock.primarySubject.subjectId == "Pawn_B",
                    "Rolling summary singleton and primary subject must survive.");
                Require(root.visibleBlocks[0].providerExposureState == "potentially_sent"
                        && root.visibleBlocks[0].formatRevision == 7L,
                    "Exposure state and int64 format revision must survive.");

                loaded.Normalize();
                Require(loaded.schemaVersion == PawnKnowledgeState.CurrentSchemaVersion,
                    "Normalizing a current envelope must keep it current.");
            });
        }

        [Test]
        public static void ArchiveOnlyNormalizationPreservesCultureByteExactly()
        {
            const string ownerId = "Pawn_M11_ArchiveCulture";
            PawnKnowledgeState source = PawnKnowledgeState.CreateCurrent(ownerId);
            source.archiveOnly = true;
            source.originCultureDefName = "Culture_Origin_е\u0308_世界_🙂";
            source.originCultureSource = "captured/source:exact";
            source.adoptedCultureDefName = "Culture_Adopted_é_界_🙂";
            source.importedArchiveRows.Add(new SavedImportedMemoryRow
            {
                archiveRecordId = "archive-culture-1",
                savedOwnerIdentityKindToken = "exact_id",
                savedOwnerIdentityValue = ownerId,
                importedWording = "Preserved archive wording."
            });

            RunWithTempFile(path =>
            {
                PawnKnowledgeState saved = source;
                SaveWithScribe(path, () => Scribe_Deep.Look(ref saved, Label));
                PawnKnowledgeState loaded = null;
                LoadVarsWithScribe(path, () => Scribe_Deep.Look(ref loaded, Label));
                Require(loaded != null, "The archive-only culture fixture did not load.");
                loaded.Normalize();
                Require(loaded.archiveOnly
                        && string.Equals(loaded.originCultureDefName,
                            source.originCultureDefName, StringComparison.Ordinal)
                        && string.Equals(loaded.originCultureSource,
                            source.originCultureSource, StringComparison.Ordinal)
                        && string.Equals(loaded.adoptedCultureDefName,
                            source.adoptedCultureDefName, StringComparison.Ordinal),
                    "Archive-only normalization changed exact physical culture fields.");
            });
        }

        [Test]
        public static void LegacyEnvelopeStaysLegacyAndDefaultsAdditive()
        {
            var legacy = new PawnKnowledgeState
            {
                pawnId = "Pawn_Legacy",
                schemaVersion = 1
            };
            ImportantMemoryRecord record = NewLegacyRecord();
            legacy.records.Add(record);

            RunWithTempFile(path =>
            {
                PawnKnowledgeState saved = legacy;
                SaveWithScribe(path, () => Scribe_Deep.Look(ref saved, Label));
                PawnKnowledgeState loaded = null;
                LoadVarsWithScribe(path, () => Scribe_Deep.Look(ref loaded, Label));
                Require(loaded != null && loaded.schemaVersion == 1,
                    "A missing schema key loads as v1 (the pinned legacy default).");
                Require(string.IsNullOrEmpty(loaded.autobiographicalEpochToken)
                        && !loaded.archiveOnly && !loaded.epochFenceOnly
                        && loaded.requestCancellationGeneration == 0
                        && loaded.migrationDiagnosticFlags == 0
                        && loaded.standaloneBlocks.Count == 0
                        && loaded.threadRoots.Count == 0
                        && loaded.importedArchiveRows.Count == 0,
                    "Pre-feature saves read zero-value additive defaults, never fabricated state.");
                loaded.Normalize();
                Require(loaded.schemaVersion == 1,
                    "T6.1: Normalize must NOT stamp legacy envelopes current.");
            });
        }

        /// <summary>
        /// Saves an old PawnDiaryRecord shape with no knowledgeState key, lets the record's real
        /// PostLoadInit create its pinned raw envelope, commits M11 migration, then exercises the
        /// first factual admission. This is the exact pre-feature path that previously left all four
        /// positive current invariants at zero and made otherwise valid memories unusable.
        /// </summary>
        [Test]
        public static void MissingPreM11KnowledgeRowMigratesThenAdmitsFirstMemory()
        {
            const string ownerId = "Pawn_M11_MissingKnowledge";
            PawnDiaryRecord loadedRecord = null;
            RunWithTempFile(path =>
            {
                legacyMissingKnowledgeTarget = new PawnDiaryRecord
                {
                    pawnId = ownerId,
                    pawnName = "Missing knowledge fixture",
                    eventIds = new List<string> { "existing-page" }
                };
                LegacyMissingKnowledgeRowAdapter saved =
                    new LegacyMissingKnowledgeRowAdapter();
                SaveWithScribe(path, () => Scribe_Deep.Look(ref saved, "legacyDiary"));

                legacyMissingKnowledgeTarget = new PawnDiaryRecord();
                LegacyMissingKnowledgeRowAdapter loaded = null;
                LoadVarsWithScribe(path, () => Scribe_Deep.Look(ref loaded, "legacyDiary"));
                loadedRecord = legacyMissingKnowledgeTarget;
            });
            legacyMissingKnowledgeTarget = null;

            Require(loadedRecord != null && loadedRecord.pawnId == ownerId
                    && loadedRecord.eventIds.Count == 1
                    && loadedRecord.knowledgeState != null
                    && loadedRecord.knowledgeState.schemaVersion == 1
                    && loadedRecord.knowledgeState.requestCancellationGeneration == 0
                    && loadedRecord.knowledgeState.structuralRevision == 0
                    && loadedRecord.knowledgeState.statusRevision == 0
                    && loadedRecord.knowledgeState.completedDiaryEntryOrdinal == 0,
                "The real missing-key Scribe path did not preserve the pinned raw old-save shape.");

            DiaryGameComponent component = NewMemoryComponent(
                new List<PawnDiaryRecord> { loadedRecord }, null, null);
            component.RunMemoryMigrationCommit();
            PawnKnowledgeState migrated = loadedRecord.knowledgeState;
            Require(migrated.IsCurrentSchema()
                    && migrated.requestCancellationGeneration > 0
                    && migrated.structuralRevision > 0
                    && migrated.statusRevision > 0
                    && migrated.completedDiaryEntryOrdinal > 0
                    && string.IsNullOrEmpty(migrated.autobiographicalEpochToken),
                "Missing-row migration did not restore every positive current invariant.");

            MemoryPolicySnapshot prior = MemoryEffectivePolicyProvider.Current;
            MemorySettingsPolicyFieldsV1 fields = prior.ToFields();
            fields.saveNewMemories = true;
            fields.memoryCategoryMask |= MemoryCategoryBits.Personal;
            MemoryPolicySnapshot captureEnabled = MemoryPolicyNormalizer.Normalize(
                MemoryPolicyNormalizer.CurrentSettingsSchemaVersion,
                fields,
                MemoryPolicyDefAdapter.Bounds());
            Require(MemoryEffectivePolicyProvider.Publish(captureEnabled),
                "The old-save fixture could not publish its isolated capture policy.");
            try
            {
                const string sourceKind = "capture_signal";
                const string occurrence = "missing-row-first-occurrence";
                const string captureRule = "missing-row-first-rule";
                const string discriminator = "missing-row-first-fact";
                string provenance;
                Require(MemoryIdentityCodec.TryCreateProvenanceRefId(
                        sourceKind,
                        occurrence,
                        string.Empty,
                        captureRule,
                        discriminator,
                        string.Empty,
                        out provenance),
                    "The old-save fixture could not create factual provenance.");
                var draft = new FactualMemoryDraft
                {
                    ownerPawnId = ownerId,
                    sourceOccurrenceId = occurrence,
                    sourceKindToken = sourceKind,
                    captureRuleId = captureRule,
                    factDiscriminator = discriminator,
                    kind = MemoryContractTokens.KindLandmark,
                    category = MemoryContractTokens.CategoryPersonal,
                    importance = MemoryContractTokens.ImportanceImportant,
                    originalEventTick = Math.Max(0, Find.TickManager?.TicksGame ?? 0),
                    routeReliable = false,
                    chapterPhaseToken = "fixture",
                    chapterDirective = MemoryChapterDirectiveTokens.RemainStandalone,
                    automaticWording = "First memory after old-save migration.",
                    provenanceRefId = provenance
                };
                MethodInfo persist = typeof(DiaryGameComponent).GetMethod(
                    "PersistFactualDraft",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Require(persist != null && (bool)persist.Invoke(component, new object[] { draft }),
                    "The first factual memory after missing-row migration was refused.");
                Require(migrated.standaloneBlocks.Count == 1
                        && migrated.threadRoots.Count == 0
                        && !string.IsNullOrWhiteSpace(migrated.autobiographicalEpochToken)
                        && loadedRecord.reflectionState != null
                        && loadedRecord.reflectionState.memoryOwnerEpochToken
                            == migrated.autobiographicalEpochToken,
                    "First factual admission did not enroll and link the migrated old-save owner.");
            }
            finally
            {
                MemoryEffectivePolicyProvider.Publish(prior);
            }
        }

        [Test]
        public static void NewFactualAdmissionSchedulesSavedBlockWordingInSharedSlot()
        {
            const string ownerId = "Pawn_Block_Wording_Admission";
            PawnKnowledgeState state = PawnKnowledgeState.CreateCurrent(ownerId);
            var record = new PawnDiaryRecord
            {
                pawnId = ownerId,
                knowledgeState = state
            };
            DiaryGameComponent component = NewMemoryComponent(
                new List<PawnDiaryRecord> { record }, null, null);
            SetPrivateField(component, "globalOptionalRequestCancellationGeneration", 7L);

            MemoryPolicySnapshot prior = MemoryEffectivePolicyProvider.Current;
            MemorySettingsPolicyFieldsV1 fields = prior.ToFields();
            fields.saveNewMemories = true;
            fields.useMemoriesInWriting = true;
            fields.allowExtraMemoryAiRequests = true;
            fields.memoryCategoryMask |= MemoryCategoryBits.Personal;
            fields.optionalRequestInvalidationGeneration = Math.Max(
                1L, fields.optionalRequestInvalidationGeneration);
            MemoryPolicySnapshot enabled = MemoryPolicyNormalizer.Normalize(
                MemoryPolicyNormalizer.CurrentSettingsSchemaVersion,
                fields,
                MemoryPolicyDefAdapter.Bounds());
            Require(enabled.AllowsOptionalRequests
                    && MemoryEffectivePolicyProvider.Publish(enabled),
                "The block-wording fixture could not publish optional memory policy.");
            try
            {
                const string occurrence = "block-wording-admission-occurrence";
                const string captureRule = "block-wording-admission-rule";
                const string discriminator = "block-wording-admission-fact";
                string provenance;
                Require(MemoryIdentityCodec.TryCreateProvenanceRefId(
                        "capture_signal",
                        occurrence,
                        string.Empty,
                        captureRule,
                        discriminator,
                        string.Empty,
                        out provenance),
                    "The block-wording fixture could not create provenance.");
                var draft = new FactualMemoryDraft
                {
                    ownerPawnId = ownerId,
                    sourceOccurrenceId = occurrence,
                    sourceKindToken = "capture_signal",
                    captureRuleId = captureRule,
                    factDiscriminator = discriminator,
                    kind = MemoryContractTokens.KindEvent,
                    category = MemoryContractTokens.CategoryPersonal,
                    importance = MemoryContractTokens.ImportanceRegular,
                    originalEventTick = Math.Max(0, Find.TickManager?.TicksGame ?? 0),
                    routeReliable = false,
                    chapterPhaseToken = "fixture",
                    chapterDirective = MemoryChapterDirectiveTokens.RemainStandalone,
                    automaticWording = "I accepted a lasting change.",
                    provenanceRefId = provenance
                };
                MethodInfo persist = typeof(DiaryGameComponent).GetMethod(
                    "PersistFactualDraft",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Require(persist != null && (bool)persist.Invoke(component, new object[] { draft }),
                    "The block-wording factual admission was refused.");

                FieldInfo slotField = typeof(DiaryGameComponent).GetField(
                    "summaryWordingOpportunities",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var slot = slotField?.GetValue(component)
                    as List<SavedSummaryWordingOpportunityV1>;
                SavedMemoryBlock admitted = state.standaloneBlocks.Count == 1
                    ? state.standaloneBlocks[0]
                    : null;
                SavedSummaryWordingOpportunityV1 pending = slot?.Count == 1
                    ? slot[0]
                    : null;
                SummaryWordingOpportunitySnapshot parsed;
                string expectedFingerprint;
                Require(admitted != null
                        && pending != null
                        && pending.ownerPawnId == ownerId
                        && pending.ownerEpochToken == state.autobiographicalEpochToken
                        && pending.rootId == "memory-wording-standalone-v1"
                        && pending.summaryRecordId == admitted.recordId
                        && pending.configuredPriority > 0
                        && string.IsNullOrEmpty(admitted.optionalLlmWording)
                        && MemoryOptionalAiPolicy.TryParseSummaryOpportunityKey(
                            pending.opportunityKey, out parsed)
                        && MemoryOptionalAiPolicy.TryCreateBlockWordingProjectionFingerprint(
                            admitted.recordId,
                            admitted.kind,
                            admitted.category,
                            admitted.automaticWording,
                            pending.expectedFormatRevision,
                            out expectedFingerprint)
                        && pending.projectionFingerprint == expectedFingerprint,
                    "Successful factual admission did not publish one exact saved block-wording slot.");
            }
            finally
            {
                MemoryEffectivePolicyProvider.Publish(prior);
            }
        }

        [Test]
        public static void LegacyRawEvidenceReachesDryRunAndRemainsRetryable()
        {
            var legacy = new PawnKnowledgeState
            {
                pawnId = "Pawn_Legacy_Raw",
                schemaVersion = 1
            };
            ImportantMemoryRecord raw = NewLegacyRecord();
            raw.sourceKind = "removed-mod-source";
            raw.recallScope = "removed-mod-scope";
            raw.participantNames.RemoveAt(raw.participantNames.Count - 1);
            raw.factValues.RemoveAt(raw.factValues.Count - 1);
            legacy.records.Add(raw);

            RunWithTempFile(path =>
            {
                PawnKnowledgeState saved = legacy;
                SaveWithScribe(path, () => Scribe_Deep.Look(ref saved, Label));
                PawnKnowledgeState loaded = null;
                LoadVarsWithScribe(path, () => Scribe_Deep.Look(ref loaded, Label));
                Require(loaded != null && loaded.schemaVersion == 1,
                    "The adversarial legacy envelope must load as retryable v1.");

                loaded.Normalize();
                ImportantMemoryRecord loadedRaw = loaded.records[0];
                Require(loadedRaw.sourceKind == "removed-mod-source"
                        && loadedRaw.recallScope == "removed-mod-scope",
                    "Legacy Normalize changed unknown semantic tokens before planning.");
                Require(loadedRaw.participantIds.Count == 2
                        && loadedRaw.participantNames.Count == 1
                        && loadedRaw.factKeys.Count == 2
                        && loadedRaw.factValues.Count == 1,
                    "Legacy Normalize aligned/truncated malformed parallel-list evidence.");

                MemoryLegacyRecordSnapshot snapshot =
                    DiaryGameComponent.SnapshotLegacyRecord(loadedRaw);
                Require(snapshot.sourceKind == "removed-mod-source"
                        && snapshot.recallScope == "removed-mod-scope"
                        && snapshot.participantIds.Count == 2
                        && snapshot.participantNames.Count == 1
                        && snapshot.factKeys.Count == 2
                        && snapshot.factValues.Count == 1,
                    "The production dry-run snapshot did not receive the original legacy shape.");
                MemoryLegacyMigrationReport report =
                    MemoryThreadMigrationPolicy.PlanDryRun(new MemoryLegacyOwnerMigrationInput
                    {
                        ownerPawnId = loaded.pawnId,
                        records = new List<MemoryLegacyRecordSnapshot> { snapshot }
                    });
                Require(report.ownerRemainsRaw,
                    "Unequal fact-key/value evidence must keep the complete owner raw.");

                // A failed dry-run stamps/clears nothing. Save the loaded owner again and prove the
                // exact malformed evidence remains available for a later migration retry.
                PawnKnowledgeState retrySaved = loaded;
                SaveWithScribe(path, () => Scribe_Deep.Look(ref retrySaved, Label));
                PawnKnowledgeState retryLoaded = null;
                LoadVarsWithScribe(path, () => Scribe_Deep.Look(ref retryLoaded, Label));
                Require(retryLoaded.schemaVersion == 1
                        && retryLoaded.records.Count == 1
                        && retryLoaded.records[0].sourceKind == "removed-mod-source"
                        && retryLoaded.records[0].recallScope == "removed-mod-scope"
                        && retryLoaded.records[0].participantIds.Count == 2
                        && retryLoaded.records[0].participantNames.Count == 1
                        && retryLoaded.records[0].factKeys.Count == 2
                        && retryLoaded.records[0].factValues.Count == 1,
                    "A refused legacy migration was not byte-shape retryable on the next save/load.");
            });
        }

        [Test]
        public static void NestedBlocksAreAllocatorHighWaterCarriers()
        {
            PawnKnowledgeState state = PawnKnowledgeState.CreateCurrent("Pawn_Carrier");
            state.autobiographicalEpochToken = string.Empty;
            var root = new SavedMemoryThreadRoot
            {
                ownerPawnId = "Pawn_Carrier",
                ownerEpochToken = string.Empty
            };
            root.visibleBlocks.Add(new SavedMemoryBlock
            {
                ownerPawnId = "Pawn_Carrier",
                ownerEpochToken = EpochToken(41)
            });
            root.rollingSummaryBlock = new SavedMemoryBlock
            {
                ownerPawnId = "Pawn_Carrier",
                ownerEpochToken = EpochToken(99)
            };
            state.threadRoots.Add(root);

            var carriers = new List<string>();
            DiaryGameComponent.AddKnowledgeEpochTokenCarriers(carriers, state);
            MemorySavedCarrierRegistryPlan plan = MemorySavedIdentityCarrierRegistry.Plan(
                new MemorySavedCarrierScanInput
                {
                    lastIssuedAutobiographicalEpochSequence = 3,
                    epochTokenCarriers = carriers
                });
            Require(plan.canPublish && plan.repairedAutobiographicalHighWater == 99,
                "A nested visible/rolling block did not raise the allocator high-water.");
        }

        [Test]
        public static void TypedKnowledgeIdentitiesAreExhaustiveAllocatorCarriers()
        {
            const string owner = "Pawn_Carrier_Exact";
            PawnKnowledgeState state = PawnKnowledgeState.CreateCurrent(owner);
            state.autobiographicalEpochToken = string.Empty;

            var root = new SavedMemoryThreadRoot
            {
                ownerPawnId = owner,
                ownerEpochToken = string.Empty,
                rootId = CarrierRootId(owner, 41, 101),
                subjectKind = MemoryContractTokens.SubjectFaction,
                subjectId = CarrierFactionSubject(101)
            };
            root.chapters.Add(new SavedMemoryChapter
            {
                chapterId = CarrierChapterId(owner, 42, 102, 1)
            });
            var visible = new SavedMemoryBlock
            {
                ownerPawnId = owner,
                ownerEpochToken = EpochToken(43),
                recordId = CarrierRecordId(owner, 44, "visible"),
                rootId = CarrierRootId(owner, 45, 105),
                chapterId = CarrierChapterId(owner, 46, 106, 1),
                primarySubject = CarrierSubjectRef(107)
            };
            visible.secondarySubjects.Add(CarrierSubjectRef(108));
            visible.facts.Add(new SavedMemoryCanonicalFact
            {
                canonicalSubjectKind = MemoryContractTokens.SubjectFaction,
                canonicalSubjectId = CarrierFactionSubject(104)
            });
            visible.facts.Add(new SavedMemoryCanonicalFact
            {
                canonicalSubjectKind = MemoryContractTokens.SubjectFaction,
                canonicalSubjectId = CarrierFactionSubject(109)
            });
            visible.summaryPayload = new SavedMemorySummaryPayload();
            visible.summaryPayload.subjectRefs.Add(CarrierSubjectRef(103));
            visible.summaryPayload.subjectRefs.Add(CarrierSubjectRef(110));
            var bucket = new SavedMemoryFactBucket
            {
                canonicalSubjectKind = MemoryContractTokens.SubjectFaction,
                canonicalSubjectId = CarrierFactionSubject(111)
            };
            bucket.contributions.Add(new SavedMemoryFactContribution
            {
                originChapterId = CarrierChapterId(owner, 47, 112, 1),
                originRecordId = CarrierRecordId(owner, 48, "contribution")
            });
            visible.summaryPayload.factBuckets.Add(bucket);
            root.visibleBlocks.Add(visible);
            root.rollingSummaryBlock = new SavedMemoryBlock
            {
                ownerPawnId = owner,
                recordId = CarrierRollingSummaryId(owner, 49, 113)
            };
            state.threadRoots.Add(root);
            state.standaloneBlocks.Add(new SavedMemoryBlock
            {
                ownerPawnId = owner,
                recordId = CarrierClosedSummaryId(owner, 50, 114, 1)
            });

            string awarenessId;
            Require(KnowledgeRelationPolicy.TryCreateAwarenessId(
                    owner,
                    EpochToken(51),
                    KnowledgeObservationTokens.ScopeFaction,
                    MemoryContractTokens.SubjectFaction,
                    CarrierFactionSubject(115),
                    KnowledgeObservationTokens.StreamFactionConnection,
                    out awarenessId),
                "Could not create the awareness carrier fixture identity.");
            state.ownerAwarenessSnapshots.Add(new SavedMemoryAwarenessSnapshot
            {
                snapshotId = awarenessId,
                subjectKind = MemoryContractTokens.SubjectFaction,
                subjectId = CarrierFactionSubject(115)
            });

            string episodeId;
            Require(KnowledgeRelationPolicy.TryCreateEpisodeId(
                    owner,
                    EpochToken(52),
                    KnowledgeObservationTokens.ScopeFaction,
                    KnowledgeObservationTokens.StreamFactionConnection,
                    "fixture-rule",
                    "fixture-episode",
                    MemoryContractTokens.SubjectFaction,
                    CarrierFactionSubject(116),
                    "fixture-stream",
                    "changed",
                    out episodeId),
                "Could not create the episode carrier fixture identity.");
            state.openCaptureEpisodes.Add(new SavedMemoryCaptureEpisode
            {
                episodeId = episodeId,
                subjectKind = MemoryContractTokens.SubjectFaction,
                subjectId = CarrierFactionSubject(116)
            });
            state.repetitionGuardRows.Add(new SavedMemoryRepetitionGuardRow
            {
                ownerEpochToken = EpochToken(53),
                guardKind = MemoryRepetitionGuardKinds.Subject,
                guardKey = MemoryRepetitionGuardPolicy.SubjectKey(
                    MemoryContractTokens.SubjectFaction,
                    CarrierFactionSubject(117))
            });
            state.repetitionGuardRows.Add(new SavedMemoryRepetitionGuardRow
            {
                guardKind = MemoryRepetitionGuardKinds.Pair,
                guardKey = MemoryRepetitionGuardPolicy.PairKey(
                    CarrierFactionSubject(118),
                    CarrierFactionSubject(119))
            });

            var epochs = new List<string>();
            var factions = new List<long>();
            DiaryGameComponent.AddKnowledgeAllocatorCarriers(epochs, factions, state);
            for (long sequence = 41; sequence <= 53; sequence++)
                Require(epochs.Contains(EpochToken(sequence)),
                    "Typed knowledge identity omitted epoch carrier " + sequence + ".");
            for (long generation = 101; generation <= 119; generation++)
                Require(factions.Contains(generation),
                    "Typed knowledge identity omitted faction carrier " + generation + ".");

            MemorySavedCarrierRegistryPlan plan = MemorySavedIdentityCarrierRegistry.Plan(
                new MemorySavedCarrierScanInput
                {
                    epochTokenCarriers = epochs,
                    factionAllocatorGenerationCarriers = factions
                });
            Require(plan.canPublish
                    && plan.repairedAutobiographicalHighWater == 53
                    && plan.globalFactionAllocatorGeneration == 119,
                "Typed knowledge identity carriers did not repair both allocator high-waters.");
        }

        [Test]
        public static void NestedNewerSchemasAbortBeforePublication()
        {
            RunWithTempFile(path =>
            {
                PawnKnowledgeState source = PawnKnowledgeState.CreateCurrent("Pawn_Future_Fact");
                SavedMemoryBlock block = NewBlock("rec-future", null);
                block.summaryPayload.factBuckets[0].contributions[0].schemaVersion = 2;
                source.standaloneBlocks.Add(block);
                PawnKnowledgeState saved = source;
                SaveWithScribe(path, () => Scribe_Deep.Look(ref saved, Label));
                PawnKnowledgeState loaded = null;
                LoadVarsWithScribe(path, () => Scribe_Deep.Look(ref loaded, Label));
                RequireNewerSchemaRefused(
                    "deep summary contribution",
                    NewMemoryComponent(
                        new List<PawnDiaryRecord>
                        {
                            new PawnDiaryRecord
                            {
                                pawnId = "Pawn_Future_Fact",
                                knowledgeState = loaded
                            }
                        },
                        null,
                        null));
            });

            RunWithTempFile(path =>
            {
                var request = new SavedActiveLogicalRequestV1 { ownerPawnId = "Pawn_Request" };
                var receipt = new SavedFrozenEvidenceReceiptPlanV1();
                receipt.guardEntries.Add(new SavedFrozenGuardEntryV1 { schemaVersion = 2 });
                request.frozenVariants.Add(new SavedFrozenPromptVariantV1
                {
                    receiptPlan = receipt
                });
                SavedActiveLogicalRequestV1 saved = request;
                SaveWithScribe(path, () => Scribe_Deep.Look(ref saved, "futureRequest"));
                SavedActiveLogicalRequestV1 loaded = null;
                LoadVarsWithScribe(path, () => Scribe_Deep.Look(ref loaded, "futureRequest"));
                RequireNewerSchemaRefused(
                    "nested request receipt row",
                    NewMemoryComponent(null,
                        new List<SavedActiveLogicalRequestV1> { loaded }, null));
            });

            RunWithTempFile(path =>
            {
                PawnKnowledgeState source = PawnKnowledgeState.CreateCurrent("Pawn_Future_Archive");
                var archive = new SavedImportedMemoryRow
                {
                    primarySubject = new SavedMemorySubjectRef { schemaVersion = 2 }
                };
                source.importedArchiveRows.Add(archive);
                PawnKnowledgeState saved = source;
                SaveWithScribe(path, () => Scribe_Deep.Look(ref saved, Label));
                PawnKnowledgeState loaded = null;
                LoadVarsWithScribe(path, () => Scribe_Deep.Look(ref loaded, Label));
                RequireNewerSchemaRefused(
                    "nested archive subject",
                    NewMemoryComponent(
                        new List<PawnDiaryRecord>
                        {
                            new PawnDiaryRecord
                            {
                                pawnId = "Pawn_Future_Archive",
                                knowledgeState = loaded
                            }
                        },
                        null,
                        null));
            });
        }

        [Test]
        public static void DispatchRequestRowRoundTrips()
        {
            var attempt = new SavedActiveLogicalAttemptV1
            {
                attemptOrdinal = 1,
                variantKey = new string('a', 64),
                attemptOriginToken = "initial",
                predecessorAttemptOrdinal = 0,
                attemptStateToken = "invocation_committed",
                invocationSequence = 4,
                invocationTick = 12345
            };
            var variant = new SavedFrozenPromptVariantV1
            {
                variantOrdinal = 0,
                variantKey = new string('b', 64),
                templateIdentity = "tmpl-full-v1",
                contextDetailIdentity = "detail-balanced",
                systemPrompt = "system text",
                userPrompt = "user text",
                receiptPlan = new SavedFrozenEvidenceReceiptPlanV1
                {
                    evidenceSetFingerprint = new string('c', 64)
                }
            };
            variant.receiptPlan.evidenceEntries.Add(new SavedFrozenEvidenceEntryV1
            {
                recordId = "rec-1",
                sourceOccurrenceId = "occ-1",
                rootIdOrEmpty = string.Empty
            });
            variant.receiptPlan.guardEntries.Add(new SavedFrozenGuardEntryV1
            {
                guardKind = "pair",
                guardKey = "guard-key"
            });
            var request = new SavedActiveLogicalRequestV1
            {
                logicalRequestSequence = 9,
                logicalRequestId = "memory-logical-request",
                requestPurposeToken = "normal_diary",
                sessionId = 2,
                povRoleToken = "initiator",
                requestStateToken = "activated",
                lastIssuedAttemptOrdinal = 1
            };
            request.frozenVariants.Add(variant);
            request.activeAttempts.Add(attempt);

            RunWithTempFile(path =>
            {
                SavedActiveLogicalRequestV1 saved = request;
                SaveWithScribe(path, () =>
                    Scribe_Deep.Look(ref saved, "req"));
                SavedActiveLogicalRequestV1 loaded = null;
                LoadVarsWithScribe(path, () => Scribe_Deep.Look(ref loaded, "req"));
                Require(loaded != null
                        && loaded.frozenVariants.Count == 1
                        && loaded.activeAttempts.Count == 1
                        && loaded.frozenVariants[0].receiptPlan != null
                        && loaded.frozenVariants[0].receiptPlan.evidenceEntries.Count == 1
                        && loaded.frozenVariants[0].receiptPlan.guardEntries.Count == 1
                        && loaded.activeAttempts[0].invocationSequence == 4,
                    "Dispatch rows and their frozen plans must round-trip.");
                loaded.Normalize();
                Require(loaded.requestStateToken == "activated",
                    "Tokens normalize null-safe without semantic reinterpretation.");
            });
        }

        [Test]
        public static void OptionalCoordinatorAndQuietCadenceRowsRoundTrip()
        {
            var opportunity = new SavedSummaryWordingOpportunityV1
            {
                schemaVersion = 1,
                ownerPawnId = "Pawn_M10",
                ownerEpochToken = EpochToken(17),
                ownerCancellationGeneration = 3,
                globalCancellationGeneration = 4,
                optionalRequestInvalidationGeneration = 5,
                rootId = "root-m10",
                summaryRecordId = "summary-m10",
                expectedRootStructuralRevision = 6,
                expectedSummaryFactsRevision = 7,
                expectedReducerRevision = 8,
                expectedFormatRevision = 9,
                expectedCategoryMask = 15,
                projectionFingerprint = new string('a', 64),
                requestedTick = 100,
                dueTick = 200,
                expiryTick = 300,
                configuredPriority = 11,
                salience = 12,
                opportunityKey = "opportunity-m10"
            };
            var reflection = new PawnReflectionState
            {
                baselineOnNextOpportunity = false,
                linkedBaselineOnNextOpportunity = false,
                lastReflectionTick = 91,
                memoryReflectionSchemaVersion = 1,
                memoryOwnerEpochToken = EpochToken(17),
                lastQuietMemoryEvaluatedAbsoluteDay = 42,
                lastQuietMemoryActivatedAbsoluteQuadrum = 2,
                lastQuietMemoryDecisionKey = new string('b', 64)
            };

            RunWithTempFile(path =>
            {
                SavedSummaryWordingOpportunityV1 savedOpportunity = opportunity;
                PawnReflectionState savedReflection = reflection;
                SaveWithScribe(path, () =>
                {
                    Scribe_Deep.Look(ref savedOpportunity, "summaryOpportunity");
                    Scribe_Deep.Look(ref savedReflection, "reflectionCadence");
                });

                SavedSummaryWordingOpportunityV1 loadedOpportunity = null;
                PawnReflectionState loadedReflection = null;
                LoadVarsWithScribe(path, () =>
                {
                    Scribe_Deep.Look(ref loadedOpportunity, "summaryOpportunity");
                    Scribe_Deep.Look(ref loadedReflection, "reflectionCadence");
                });

                Require(loadedOpportunity != null
                        && loadedOpportunity.schemaVersion == 1
                        && loadedOpportunity.ownerPawnId == "Pawn_M10"
                        && loadedOpportunity.ownerEpochToken == EpochToken(17)
                        && loadedOpportunity.ownerCancellationGeneration == 3
                        && loadedOpportunity.globalCancellationGeneration == 4
                        && loadedOpportunity.optionalRequestInvalidationGeneration == 5
                        && loadedOpportunity.rootId == "root-m10"
                        && loadedOpportunity.summaryRecordId == "summary-m10"
                        && loadedOpportunity.expectedRootStructuralRevision == 6
                        && loadedOpportunity.expectedSummaryFactsRevision == 7
                        && loadedOpportunity.expectedReducerRevision == 8
                        && loadedOpportunity.expectedFormatRevision == 9
                        && loadedOpportunity.expectedCategoryMask == 15
                        && loadedOpportunity.projectionFingerprint == new string('a', 64)
                        && loadedOpportunity.requestedTick == 100
                        && loadedOpportunity.dueTick == 200
                        && loadedOpportunity.expiryTick == 300
                        && loadedOpportunity.configuredPriority == 11
                        && loadedOpportunity.salience == 12
                        && loadedOpportunity.opportunityKey == "opportunity-m10",
                    "The complete one-slot Summary opportunity row did not round-trip exactly.");
                Require(loadedReflection != null
                        && !loadedReflection.baselineOnNextOpportunity
                        && !loadedReflection.linkedBaselineOnNextOpportunity
                        && loadedReflection.lastReflectionTick == 91
                        && loadedReflection.memoryReflectionSchemaVersion == 1
                        && loadedReflection.memoryOwnerEpochToken == EpochToken(17)
                        && loadedReflection.lastQuietMemoryEvaluatedAbsoluteDay == 42
                        && loadedReflection.lastQuietMemoryActivatedAbsoluteQuadrum == 2
                        && loadedReflection.lastQuietMemoryDecisionKey == new string('b', 64),
                    "The five owner-scoped M10 cadence fields did not round-trip exactly.");
            });
        }

        [Test]
        public static void OptionalCoordinatorWakeFollowsCurrentReleaseGate()
        {
            Require(string.Equals(
                    MemorySystemActivationGate.BuildState,
                    MemorySystemActivationGate.CurrentRelease,
                    StringComparison.Ordinal),
                "The M11 coordinator fixture requires CurrentRelease.");

            var summaryOnly = new ReflectionCoordinatorWakeRequest
            {
                optionalMemoryRequestsEffective = MemorySystemActivationGate.IsCurrentRelease,
                pendingSummaryWordingCount = 1
            };
            Require(ReflectionCoordinator.HasPendingCoordinatorWork(summaryOnly),
                "CurrentRelease did not wake the coordinator for enabled Summary work.");

            summaryOnly.optionalMemoryRequestsEffective = true;
            Require(ReflectionCoordinator.HasPendingCoordinatorWork(summaryOnly),
                "An effectively enabled summary-only row did not wake the shared coordinator.");

            summaryOnly.optionalMemoryRequestsEffective = false;
            Require(!ReflectionCoordinator.HasPendingCoordinatorWork(summaryOnly),
                "Master Off admitted summary-only coordinator work.");

            summaryOnly.pendingAmbientInteractionCount = 1;
            Require(ReflectionCoordinator.HasPendingCoordinatorWork(summaryOnly),
                "Normal ambient readiness incorrectly depended on the optional-memory gate.");
        }

        [Test]
        public static void OptionalSummaryPolicyIsDeterministicFirstAndBounded()
        {
            Require(MemoryOptionalWordingDispositionTokens.IsKnown(
                        MemoryOptionalWordingDispositionTokens.None)
                    && MemoryOptionalWordingDispositionTokens.IsKnown(
                        MemoryOptionalWordingDispositionTokens.Pending)
                    && !MemoryOptionalWordingDispositionTokens.IsKnown("stale"),
                "Summary wording did not expose the exact canonical saved disposition vocabulary.");

            SummaryWordingOpportunitySnapshot first = M10Opportunity(
                "Pawn_M10_Policy", EpochToken(71), "root-a", "summary-a", 10, 100);
            SummaryWordingOpportunitySnapshot second = M10Opportunity(
                first.ownerPawnId, first.ownerEpochToken, "root-b", "summary-b", 9, 110);
            string key;
            Require(MemoryOptionalAiPolicy.TryCreateSummaryOpportunityKey(first, out key),
                "The canonical first Summary opportunity key was refused.");
            first.opportunityKey = key;
            Require(MemoryOptionalAiPolicy.TryCreateSummaryOpportunityKey(second, out key),
                "The canonical second Summary opportunity key was refused.");
            second.opportunityKey = key;

            SummaryWordingSlotPlan slot = MemoryOptionalAiPolicy.PlanOwnerSlot(
                first, second, 120);
            Require(slot.valid && ReferenceEquals(slot.winner, first)
                    && slot.terminal.Count == 1
                    && slot.terminal[0].dispositionToken
                        == MemoryOptionalWordingDispositionTokens.Displaced,
                "One owner retained more than one Summary opportunity or used unstable ranking.");

            var current = new SummaryWordingCurrentSnapshot
            {
                ownerPawnId = first.ownerPawnId,
                ownerEpochToken = first.ownerEpochToken,
                rootId = first.rootId,
                summaryRecordId = first.summaryRecordId,
                rootStructuralRevision = first.expectedRootStructuralRevision,
                summaryFactsRevision = first.expectedSummaryFactsRevision,
                reducerRevision = first.expectedReducerRevision,
                formatRevision = first.expectedFormatRevision,
                categoryMask = first.expectedCategoryMask,
                projectionFingerprint = first.projectionFingerprint,
                deterministicWording = "Canonical deterministic fallback."
            };
            SummaryWordingResultPlan malformed = MemoryOptionalAiPolicy.PlanSummaryResult(
                first, current, true, "not one line\nsecond line", 240);
            Require(malformed.identityMatched && !malformed.applyOptionalWording
                    && malformed.dispositionToken
                        == MemoryOptionalWordingDispositionTokens.Malformed
                    && current.deterministicWording == "Canonical deterministic fallback.",
                "Malformed optional prose replaced or altered deterministic Summary truth.");

            current.summaryFactsRevision++;
            SummaryWordingResultPlan stale = MemoryOptionalAiPolicy.PlanSummaryResult(
                first, current, true, "stale wording", 240);
            Require(!stale.identityMatched && !stale.applyOptionalWording
                    && stale.dispositionToken == MemoryOptionalWordingDispositionTokens.None
                    && current.deterministicWording == "Canonical deterministic fallback.",
                "A stale Summary result replaced deterministic wording.");

            var cutoffs = new MemoryInvokedGenerationCutoffTable();
            string requestId;
            Require(MemoryIdentityCodec.TryCreateLogicalRequestId(71, out requestId)
                    && cutoffs.TryRegister(1, first.ownerPawnId, first.ownerEpochToken,
                        2, 3, requestId, 1, 1),
                "Invocation-wins cutoff registration failed.");
            Require(!cutoffs.AllowsInvocationWinner(1, first.ownerPawnId,
                    first.ownerEpochToken, 2, 3, requestId, 1),
                "An unsealed generation bypassed cancellation.");
            cutoffs.SealGeneration(1, 3, 1);
            Require(cutoffs.AllowsInvocationWinner(1, first.ownerPawnId,
                    first.ownerEpochToken, 2, 3, requestId, 1)
                    && !cutoffs.AllowsInvocationWinner(1, first.ownerPawnId,
                        EpochToken(72), 2, 3, requestId, 1),
                "Invocation-wins either failed or bypassed the Brainwipe epoch fence.");
        }

        [Test]
        public static void M11LegacyMigrationCommitIsIdempotentAndCreatesNoCatchUpPage()
        {
            const string ownerId = "Pawn_M11_Migration";
            var diary = new PawnDiaryRecord
            {
                pawnId = ownerId,
                eventIds = new List<string> { "existing-page" },
                knowledgeState = new PawnKnowledgeState
                {
                    pawnId = ownerId,
                    schemaVersion = 1,
                    records = new List<ImportantMemoryRecord> { NewLegacyRecord() }
                }
            };
            List<PawnDiaryRecord> holders = new List<PawnDiaryRecord> { diary };
            DiaryGameComponent component = NewMemoryComponent(holders, null, null);
            component.RunMemoryMigrationCommit();

            PawnKnowledgeState migrated = diary.knowledgeState;
            Require(migrated != null && migrated.IsCurrentSchema()
                    && !string.IsNullOrWhiteSpace(migrated.autobiographicalEpochToken)
                    && migrated.records.Count == 0,
                "M11 did not atomically stamp the resolved legacy owner current.");
            Require(diary.eventIds.Count == 1 && diary.eventIds[0] == "existing-page",
                "Migration created a retrospective/catch-up diary page.");
            string epoch = migrated.autobiographicalEpochToken;
            int blocks = migrated.standaloneBlocks.Count;
            int imported = migrated.importedArchiveRows.Count;
            component.RunMemoryMigrationCommit();
            Require(ReferenceEquals(migrated, diary.knowledgeState)
                    && diary.knowledgeState.autobiographicalEpochToken == epoch
                    && diary.knowledgeState.standaloneBlocks.Count == blocks
                    && diary.knowledgeState.importedArchiveRows.Count == imported,
                "A second M11 migration pass changed an already-current owner.");
        }

        /// <summary>
        /// Migration preserves already-saved autobiography independently of the player's current
        /// capture preferences. Save-new Off and an empty category mask apply to future capture,
        /// never to the one-time conversion of legacy rows already present in the save.
        /// </summary>
        [Test]
        public static void LegacyMigrationIgnoresSaveAndCategoryCaptureGates()
        {
            MemoryPolicySnapshot prior = MemoryEffectivePolicyProvider.Current;
            MemorySettingsPolicyFieldsV1 fields = prior.ToFields();
            fields.saveNewMemories = false;
            fields.memoryCategoryMask = 0;
            MemoryPolicySnapshot disabled = MemoryPolicyNormalizer.Normalize(
                MemoryPolicyNormalizer.CurrentSettingsSchemaVersion,
                fields,
                MemoryPolicyDefAdapter.Bounds());
            Require(MemoryEffectivePolicyProvider.Publish(disabled),
                "The migration fixture could not publish its isolated disabled policy.");
            try
            {
                const string ownerId = "Pawn_M11_Migration_GatesOff";
                PawnDiaryRecord diary = new PawnDiaryRecord
                {
                    pawnId = ownerId,
                    knowledgeState = new PawnKnowledgeState
                    {
                        pawnId = ownerId,
                        schemaVersion = 1,
                        records = new List<ImportantMemoryRecord> { NewLegacyRecord() }
                    }
                };
                DiaryGameComponent component = NewMemoryComponent(
                    new List<PawnDiaryRecord> { diary }, null, null);

                component.RunMemoryMigrationCommit();

                PawnKnowledgeState migrated = diary.knowledgeState;
                Require(migrated != null && migrated.IsCurrentSchema()
                        && migrated.records.Count == 0
                        && !string.IsNullOrWhiteSpace(migrated.autobiographicalEpochToken)
                        && CountBlocks(migrated) == 1,
                    "Ordinary Save/category capture gates suppressed saved legacy migration.");
            }
            finally
            {
                MemoryEffectivePolicyProvider.Publish(prior);
            }
        }

        /// <summary>
        /// Every successful owner migration must update the detached running budget before the next
        /// owner is considered. One hundred 50-block owners fit at the shipped 5,000-block global
        /// soft cap; the next owner must remain wholly raw instead of being admitted against the
        /// stale pre-loop totals. Exact ordinal IDs pin which owner is the later refusal.
        /// </summary>
        [Test]
        public static void CumulativeMigrationBudgetRejectsOnlyLaterOverflowingOwner()
        {
            const int ownerCount = 101;
            const int recordsPerOwner = 50;
            var diaries = new List<PawnDiaryRecord>();
            for (int ownerIndex = 0; ownerIndex < ownerCount; ownerIndex++)
            {
                string ownerId = "Pawn_M11_Budget_" + ownerIndex.ToString("D3");
                var legacy = new PawnKnowledgeState
                {
                    pawnId = ownerId,
                    schemaVersion = 1,
                    records = new List<ImportantMemoryRecord>()
                };
                for (int rowIndex = 0; rowIndex < recordsPerOwner; rowIndex++)
                {
                    legacy.records.Add(NewCapacityLegacyRecord(ownerIndex, rowIndex));
                }

                diaries.Add(new PawnDiaryRecord
                {
                    pawnId = ownerId,
                    knowledgeState = legacy
                });
            }

            DiaryGameComponent component = NewMemoryComponent(diaries, null, null);
            component.RunMemoryMigrationCommit();

            for (int ownerIndex = 0; ownerIndex < ownerCount - 1; ownerIndex++)
            {
                PawnKnowledgeState migrated = diaries[ownerIndex].knowledgeState;
                Require(migrated.IsCurrentSchema()
                        && migrated.records.Count == 0
                        && CountBlocks(migrated) == recordsPerOwner,
                    "A pre-cap owner was refused or partly migrated at ordinal " + ownerIndex + ".");
            }

            PawnKnowledgeState refused = diaries[ownerCount - 1].knowledgeState;
            Require(!refused.IsCurrentSchema()
                    && refused.schemaVersion == 1
                    && refused.records.Count == recordsPerOwner
                    && CountBlocks(refused) == 0,
                "The later owner bypassed the cumulative global block budget or mutated partly.");
        }

        /// <summary>
        /// A pre-M11 owner can legitimately contain culture provenance but no legacy memory rows. It
        /// still needs a one-time schema commit; because it has no autobiography, migration must not
        /// allocate an epoch or consume an active-owner slot merely to stamp the envelope current.
        /// </summary>
        [Test]
        public static void EmptyLegacyOwnerMigratesCurrentWithoutEnrollment()
        {
            const string ownerId = "Pawn_M11_EmptyLegacy";
            PawnDiaryRecord diary = new PawnDiaryRecord
            {
                pawnId = ownerId,
                knowledgeState = new PawnKnowledgeState
                {
                    pawnId = ownerId,
                    schemaVersion = 1,
                    originCultureDefName = "Culture_EmptyLegacy",
                    originCultureSource = "legacy_fixture",
                    adoptedCultureDefName = "Culture_AdoptedLegacy",
                    records = new List<ImportantMemoryRecord>()
                }
            };
            DiaryGameComponent component = NewMemoryComponent(
                new List<PawnDiaryRecord> { diary }, null, null);

            component.RunMemoryMigrationCommit();

            PawnKnowledgeState migrated = diary.knowledgeState;
            FieldInfo allocatorField = typeof(DiaryGameComponent).GetField(
                "lastIssuedAutobiographicalEpochSequence",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(migrated != null && migrated.IsCurrentSchema()
                    && migrated.pawnId == ownerId
                    && string.IsNullOrEmpty(migrated.autobiographicalEpochToken)
                    && !migrated.archiveOnly && !migrated.epochFenceOnly
                    && migrated.records.Count == 0
                    && migrated.threadRoots.Count == 0
                    && migrated.standaloneBlocks.Count == 0,
                "An empty legacy envelope stayed migration-pending or became an active owner.");
            Require(migrated.originCultureDefName == "Culture_EmptyLegacy"
                    && migrated.originCultureSource == "legacy_fixture"
                    && migrated.adoptedCultureDefName == "Culture_AdoptedLegacy",
                "Empty-owner migration lost legacy culture provenance.");
            Require(allocatorField != null
                    && (long)allocatorField.GetValue(component) == 0,
                "Empty-owner migration consumed an autobiographical epoch allocation.");

            PawnKnowledgeState committed = migrated;
            component.RunMemoryMigrationCommit();
            Require(ReferenceEquals(committed, diary.knowledgeState)
                    && string.IsNullOrEmpty(diary.knowledgeState.autobiographicalEpochToken),
                "A second empty-owner migration pass enrolled or replaced current state.");
        }

        /// <summary>
        /// Current-schema duplicate repair must publish its active winner and preserve the other
        /// player-authored wording in Imported as one atomic, idempotent owner transaction.
        /// </summary>
        [Test]
        public static void AuthoredCurrentRootConflictArchivesAlternateAndReachesFixedPoint()
        {
            const string ownerId = "Pawn_M11_AuthoredRepair";
            const string subjectId = "Pawn_M11_AuthoredSubject";
            PawnKnowledgeState state = PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                ownerId, subjectId, "Same subject", 81);
            SavedMemoryThreadRoot first = state.threadRoots[0];
            SavedMemoryThreadRoot alternate = PawnDiaryMemoryM11RuntimeFixture.AddThreadRoot(
                state, subjectId, "Same subject", 82,
                MemoryContractTokens.SubjectPawn,
                MemoryContractTokens.CategoryRelationships);
            SavedMemoryBlock firstBlock = first.visibleBlocks[0];
            SavedMemoryBlock alternateBlock = alternate.visibleBlocks[0];
            alternate.rootId = first.rootId;
            alternate.chapters[0].chapterId = first.chapters[0].chapterId;
            alternateBlock.rootId = first.rootId;
            alternateBlock.chapterId = first.chapters[0].chapterId;
            alternateBlock.recordId = firstBlock.recordId;
            firstBlock.playerEdited = true;
            firstBlock.playerWording = "Authored repair wording A.";
            alternateBlock.playerEdited = true;
            alternateBlock.playerWording = "Authored repair wording B.";

            PawnDiaryRecord diary = new PawnDiaryRecord
            {
                pawnId = ownerId,
                knowledgeState = state
            };
            DiaryGameComponent component = NewMemoryComponent(
                new List<PawnDiaryRecord> { diary }, null, null);
            component.RebuildMemorySizeIndexes();
            int importedBefore = state.importedArchiveRows.Count;
            long revisionBefore = state.structuralRevision;

            Require(component.TryRepairSavedMemoryRoots(
                    state,
                    new MemoryReducerPolicy
                    {
                        nowTick = 0,
                        targetVisibleBlocks = 64,
                        maximumVisibleBlocks = 128
                    }),
                "Current-schema authored conflict repair did not publish.");
            Require(state.threadRoots.Count == 1
                    && state.threadRoots[0].visibleBlocks.Count == 1
                    && state.structuralRevision == revisionBefore + 1,
                "Authored conflict repair did not publish exactly one active winner.");
            Require(state.importedArchiveRows.Count == importedBefore + 1,
                "Authored conflict repair did not archive exactly one alternate.");
            SavedImportedMemoryRow archived =
                state.importedArchiveRows[state.importedArchiveRows.Count - 1];
            string activeWording = state.threadRoots[0].visibleBlocks[0].playerWording;
            Require(archived.migrationReasonToken == "authored_conflict"
                    && archived.savedOwnerIdentityKindToken == "exact_id"
                    && archived.savedOwnerIdentityValue == ownerId
                    && archived.originalRecordId == firstBlock.recordId
                    && archived.archiveRecordId.Length == 64
                    && ((activeWording == "Authored repair wording A."
                            && archived.importedWording == "Authored repair wording B.")
                        || (activeWording == "Authored repair wording B."
                            && archived.importedWording == "Authored repair wording A.")),
                "Authored conflict repair did not preserve the exact losing wording and owner.");

            long fixedRevision = state.structuralRevision;
            string fixedArchiveId = archived.archiveRecordId;
            Require(!component.TryRepairSavedMemoryRoots(
                        state,
                        new MemoryReducerPolicy
                        {
                            nowTick = 0,
                            targetVisibleBlocks = 64,
                            maximumVisibleBlocks = 128
                        })
                    && state.structuralRevision == fixedRevision
                    && state.importedArchiveRows.Count == importedBefore + 1
                    && state.importedArchiveRows[state.importedArchiveRows.Count - 1]
                        .archiveRecordId == fixedArchiveId,
                "Authored conflict repair did not reach an idempotent fixed point.");
        }

        [Test]
        public static void AuthoredRollingSummaryArchivesOneImportedRowPerContribution()
        {
            PawnKnowledgeState state = PawnKnowledgeState.CreateCurrent("Pawn_M1");
            state.autobiographicalEpochToken = EpochToken(3);
            SavedMemoryThreadRoot root = NewRoot();
            root.rollingSummaryBlock.playerEdited = true;
            root.rollingSummaryBlock.playerWording = "Exact authored rolling wording.";
            state.threadRoots.Add(root);
            DiaryGameComponent component = NewMemoryComponent(
                new List<PawnDiaryRecord>
                {
                    new PawnDiaryRecord { pawnId = state.pawnId, knowledgeState = state }
                },
                null,
                null);
            component.RebuildMemorySizeIndexes();

            Require(component.TryRepairSavedMemoryRoots(
                    state,
                    new MemoryReducerPolicy
                    {
                        nowTick = 0,
                        targetVisibleBlocks = 64,
                        maximumVisibleBlocks = 128
                    }),
                "Authored rolling-summary repair did not publish.");
            Require(state.importedArchiveRows.Count == 1
                    && state.importedArchiveRows[0].importedWording
                        == "Exact authored rolling wording."
                    && state.importedArchiveRows[0].summaryContributionEvidence != null
                    && state.importedArchiveRows[0].summaryContributionEvidence.contributionId
                        == "contrib-1"
                    && state.importedArchiveRows[0].summaryContributionEvidence.canonicalValue
                        == "Knight"
                    && state.threadRoots.Count == 1
                    && state.threadRoots[0].rollingSummaryBlock != null
                    && !state.threadRoots[0].rollingSummaryBlock.playerEdited
                    && string.IsNullOrEmpty(
                        state.threadRoots[0].rollingSummaryBlock.playerWording),
                "Rolling repair did not preserve its structured authored contribution in Imported.");
            long revision = state.structuralRevision;
            string archiveId = state.importedArchiveRows[0].archiveRecordId;
            Require(!component.TryRepairSavedMemoryRoots(
                        state,
                        new MemoryReducerPolicy
                        {
                            nowTick = 0,
                            targetVisibleBlocks = 64,
                            maximumVisibleBlocks = 128
                        })
                    && state.structuralRevision == revision
                    && state.importedArchiveRows.Count == 1
                    && state.importedArchiveRows[0].archiveRecordId == archiveId,
                "Authored rolling-summary repair did not reach a fixed point.");
        }

        [Test]
        public static void AuthoredRepairArchiveCapacityRefusalIsAtomic()
        {
            const string ownerId = "Pawn_M11_AuthoredCapacity";
            const string subjectId = "Pawn_M11_AuthoredCapacitySubject";
            PawnKnowledgeState state = PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                ownerId, subjectId, "Capacity subject", 91);
            SavedMemoryThreadRoot first = state.threadRoots[0];
            SavedMemoryThreadRoot alternate = PawnDiaryMemoryM11RuntimeFixture.AddThreadRoot(
                state, subjectId, "Capacity subject", 92,
                MemoryContractTokens.SubjectPawn,
                MemoryContractTokens.CategoryRelationships);
            SavedMemoryBlock firstBlock = first.visibleBlocks[0];
            SavedMemoryBlock alternateBlock = alternate.visibleBlocks[0];
            alternate.rootId = first.rootId;
            alternate.chapters[0].chapterId = first.chapters[0].chapterId;
            alternateBlock.rootId = first.rootId;
            alternateBlock.chapterId = first.chapters[0].chapterId;
            alternateBlock.recordId = firstBlock.recordId;
            firstBlock.playerEdited = true;
            firstBlock.playerWording = "Capacity authored A.";
            alternateBlock.playerEdited = true;
            alternateBlock.playerWording = "Capacity authored B.";
            for (int index = state.importedArchiveRows.Count; index <= 256; index++)
            {
                state.importedArchiveRows.Add(new SavedImportedMemoryRow
                {
                    archiveRecordId = "capacity-imported-" + index,
                    savedOwnerIdentityKindToken = "exact_id",
                    savedOwnerIdentityValue = ownerId,
                    importedWording = "x"
                });
            }
            List<SavedMemoryThreadRoot> rootsBefore = state.threadRoots;
            List<SavedImportedMemoryRow> importedBefore = state.importedArchiveRows;
            long revisionBefore = state.structuralRevision;
            DiaryGameComponent component = NewMemoryComponent(
                new List<PawnDiaryRecord>
                {
                    new PawnDiaryRecord { pawnId = ownerId, knowledgeState = state }
                },
                null,
                null);
            component.RebuildMemorySizeIndexes();

            Require(!component.TryRepairSavedMemoryRoots(
                        state,
                        new MemoryReducerPolicy
                        {
                            nowTick = 0,
                            targetVisibleBlocks = 64,
                            maximumVisibleBlocks = 128
                        })
                    && ReferenceEquals(state.threadRoots, rootsBefore)
                    && ReferenceEquals(state.importedArchiveRows, importedBefore)
                    && state.threadRoots.Count == 2
                    && state.importedArchiveRows.Count == 257
                    && state.structuralRevision == revisionBefore,
                "An over-cap authored repair partly mutated roots, Imported, or revision.");
        }

        /// <summary>
        /// Two authored repairs in one maintenance slice must spend one running Imported-row
        /// budget. The first owner may consume the final slot; the second then refuses atomically
        /// instead of independently reusing the same pre-slice headroom.
        /// </summary>
        [Test]
        public static void AuthoredRepairsShareOneCumulativeMigrationBudget()
        {
            const string firstOwnerId = "Pawn_M11_SharedRepairA";
            const string secondOwnerId = "Pawn_M11_SharedRepairB";
            PawnKnowledgeState first = BuildAuthoredConflictOwner(
                firstOwnerId, "Pawn_M11_SharedRepairSubjectA", 101);
            PawnKnowledgeState second = BuildAuthoredConflictOwner(
                secondOwnerId, "Pawn_M11_SharedRepairSubjectB", 111);
            DiaryGameComponent component = NewMemoryComponent(
                new List<PawnDiaryRecord>
                {
                    new PawnDiaryRecord { pawnId = firstOwnerId, knowledgeState = first },
                    new PawnDiaryRecord { pawnId = secondOwnerId, knowledgeState = second }
                },
                null,
                null);
            component.RebuildMemorySizeIndexes();

            MethodInfo createSession = typeof(DiaryGameComponent).GetMethod(
                "CreateMemoryMigrationBudgetSession",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo sharedRepair = null;
            MethodInfo[] candidates = typeof(DiaryGameComponent).GetMethods(
                BindingFlags.Instance | BindingFlags.NonPublic);
            for (int index = 0; index < candidates.Length; index++)
            {
                if (candidates[index].Name == "TryRepairSavedMemoryRoots"
                    && candidates[index].GetParameters().Length == 3)
                {
                    sharedRepair = candidates[index];
                    break;
                }
            }
            MethodInfo readCapacity = typeof(DiaryGameComponent).GetMethod(
                "ReadCapacityLong",
                BindingFlags.Static | BindingFlags.NonPublic);
            Require(createSession != null && sharedRepair != null && readCapacity != null,
                "Fixture could not find the shared authored-repair budget surface.");
            object session = createSession.Invoke(component, null);
            FieldInfo importedRows = session.GetType().GetField(
                "globalImportedRows", BindingFlags.Instance | BindingFlags.Public);
            Require(importedRows != null,
                "Fixture could not find the cumulative Imported-row total.");
            long importedRowCap = (long)readCapacity.Invoke(
                null, new object[] { "importedGlobalRows", 10000L, 40000L });
            Require(importedRowCap > 0 && importedRowCap <= int.MaxValue,
                "Fixture read an invalid Imported-row capacity.");
            importedRows.SetValue(session, (int)importedRowCap - 1);

            var policy = new MemoryReducerPolicy
            {
                nowTick = 0,
                targetVisibleBlocks = 64,
                maximumVisibleBlocks = 128
            };
            Require((bool)sharedRepair.Invoke(
                    component, new object[] { first, policy, session })
                    && first.threadRoots.Count == 1
                    && first.importedArchiveRows.Count == 1
                    && (int)importedRows.GetValue(session) == (int)importedRowCap,
                "The first repair did not consume the final shared Imported-row slot.");

            List<SavedMemoryThreadRoot> secondRoots = second.threadRoots;
            List<SavedImportedMemoryRow> secondImported = second.importedArchiveRows;
            long secondRevision = second.structuralRevision;
            Require(!(bool)sharedRepair.Invoke(
                        component, new object[] { second, policy, session })
                    && ReferenceEquals(second.threadRoots, secondRoots)
                    && ReferenceEquals(second.importedArchiveRows, secondImported)
                    && second.threadRoots.Count == 2
                    && second.importedArchiveRows.Count == 0
                    && second.structuralRevision == secondRevision
                    && (int)importedRows.GetValue(session) == (int)importedRowCap,
                "The second repair reused pre-slice headroom or partly mutated on refusal.");
        }

        /// <summary>
        /// An observation-scoped factual admission can have a nonpositive net delta when reducer
        /// folding removes more payload than it adds. The running session must still shrink by that
        /// exact amount instead of republishing its pre-admission totals at the slice tail.
        /// </summary>
        [Test]
        public static void ObservationScopedShrinkAdvancesRunningBudget()
        {
            const string ownerId = "Pawn_M11_ObservationShrink";
            PawnKnowledgeState state = PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                ownerId,
                "Pawn_M11_ObservationShrinkSubject",
                "Observation shrink subject",
                121,
                extraStandalone: 1);
            DiaryGameComponent component = NewMemoryComponent(
                new List<PawnDiaryRecord>
                {
                    new PawnDiaryRecord { pawnId = ownerId, knowledgeState = state }
                },
                null,
                null);
            component.RebuildMemoryM4Indexes();
            component.RebuildMemorySizeIndexes();

            MethodInfo createSession = typeof(DiaryGameComponent).GetMethod(
                "CreateMemoryObservationBudgetSession",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo validate = typeof(DiaryGameComponent).GetMethod(
                "ValidateDetachedCapacity",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo currentLimits = typeof(DiaryGameComponent).GetMethod(
                "CurrentMemoryBudgetLimits",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo activeBudget = typeof(DiaryGameComponent).GetField(
                "memoryObservationActiveAdmissionBudget",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(createSession != null && validate != null && currentLimits != null
                    && activeBudget != null,
                "Fixture could not find the scoped observation capacity seam.");
            object session = createSession.Invoke(component, null);
            FieldInfo globalField = session.GetType().GetField(
                "global", BindingFlags.Instance | BindingFlags.Public);
            Require(globalField != null,
                "Fixture could not find the observation session's global total.");
            MemoryPayloadBudgetTotals before =
                (MemoryPayloadBudgetTotals)globalField.GetValue(session);
            List<SavedMemoryBlock> retained =
                new List<SavedMemoryBlock>(state.standaloneBlocks);
            SavedMemoryBlock removed = retained[retained.Count - 1];
            retained.RemoveAt(retained.Count - 1);
            long removedBytes = SizeOf(removed);
            MemoryBudgetLimits limits =
                (MemoryBudgetLimits)currentLimits.Invoke(component, null);
            before.globalActiveBytes = checked(
                limits.activeGlobalBytes + removedBytes + 1);
            globalField.SetValue(session, before);

            MemoryStoreMutationOutcome outcome;
            activeBudget.SetValue(component, session);
            try
            {
                outcome = (MemoryStoreMutationOutcome)validate.Invoke(
                    component,
                    new object[]
                    {
                        state,
                        retained,
                        new List<SavedMemoryThreadRoot>(state.threadRoots),
                        false
                    });
            }
            finally
            {
                activeBudget.SetValue(component, null);
            }
            MemoryPayloadBudgetTotals after =
                (MemoryPayloadBudgetTotals)globalField.GetValue(session);
            Require(outcome == MemoryStoreMutationOutcome.Admitted
                    && before.globalActiveBytes - after.globalActiveBytes == removedBytes
                    && after.globalImportedBytes == before.globalImportedBytes
                    && state.standaloneBlocks.Count == 2,
                "A non-growing observation admission left stale totals or mutated saved truth.");
        }

        [Test]
        public static void M11DuplicateOwnerCommitPreservesPhysicalCultureAndUnrelatedReflection()
        {
            const string ownerId = "Pawn_M11_Duplicate";
            string epoch = EpochToken(61);
            PawnKnowledgeState firstState = PawnKnowledgeState.CreateCurrent(ownerId);
            firstState.autobiographicalEpochToken = epoch;
            firstState.originCultureDefName = "Culture_First";
            firstState.originCultureSource = "first-source";
            firstState.adoptedCultureDefName = "Culture_First_Adopted";
            firstState.records.Add(NewLegacyRecord());
            PawnKnowledgeState duplicateState = PawnKnowledgeState.CreateCurrent(ownerId);
            duplicateState.autobiographicalEpochToken = epoch;
            duplicateState.originCultureDefName = "Culture_Duplicate";
            duplicateState.originCultureSource = "duplicate-source";
            duplicateState.adoptedCultureDefName = "Culture_Duplicate_Adopted";

            var firstReflection = new PawnReflectionState
            {
                baselineOnNextOpportunity = false,
                linkedBaselineOnNextOpportunity = true,
                lastReflectionTick = 101,
                lastMajorArcTick = 102,
                pendingMajorArc = true,
                pendingMajorArcRequestedTick = 103,
                pendingMajorArcAvoidEventId = "evt-first",
                memoryReflectionSchemaVersion = 1,
                memoryOwnerEpochToken = epoch,
                lastQuietMemoryEvaluatedAbsoluteDay = 10,
                lastQuietMemoryActivatedAbsoluteQuadrum = 2
            };
            var duplicateReflection = new PawnReflectionState
            {
                baselineOnNextOpportunity = true,
                linkedBaselineOnNextOpportunity = false,
                lastReflectionTick = 201,
                lastCrossArcTick = 202,
                pendingMajorArc = true,
                pendingMajorArcRequestedTick = 203,
                pendingMajorArcAvoidEventId = "evt-duplicate",
                memoryReflectionSchemaVersion = 1,
                memoryOwnerEpochToken = epoch,
                lastQuietMemoryEvaluatedAbsoluteDay = 11,
                lastQuietMemoryActivatedAbsoluteQuadrum = 3
            };
            var first = new PawnDiaryRecord
            {
                pawnId = ownerId,
                knowledgeState = firstState,
                reflectionState = firstReflection
            };
            var duplicate = new PawnDiaryRecord
            {
                pawnId = ownerId,
                knowledgeState = duplicateState,
                reflectionState = duplicateReflection
            };
            DiaryGameComponent component = NewMemoryComponent(
                new List<PawnDiaryRecord> { first, duplicate }, null, null);
            component.RunMemoryMigrationCommit();

            Require(first.knowledgeState.IsCurrentSchema()
                    && first.knowledgeState.autobiographicalEpochToken == epoch
                    && first.knowledgeState.originCultureDefName == "Culture_First"
                    && first.knowledgeState.originCultureSource == "first-source"
                    && first.knowledgeState.adoptedCultureDefName == "Culture_First_Adopted",
                "The physical first holder did not retain only its own culture fields.");
            Require(duplicate.knowledgeState.IsCurrentSchema()
                    && string.IsNullOrEmpty(duplicate.knowledgeState.autobiographicalEpochToken)
                    && duplicate.knowledgeState.completedDiaryEntryOrdinal == 1
                    && duplicate.knowledgeState.originCultureDefName == "Culture_Duplicate"
                    && duplicate.knowledgeState.originCultureSource == "duplicate-source"
                    && duplicate.knowledgeState.adoptedCultureDefName
                        == "Culture_Duplicate_Adopted",
                "The duplicate holder did not become a valid inert state with its own culture.");
            Require(!first.reflectionState.baselineOnNextOpportunity
                    && first.reflectionState.linkedBaselineOnNextOpportunity
                    && first.reflectionState.lastReflectionTick == 101
                    && first.reflectionState.lastMajorArcTick == 102
                    && first.reflectionState.pendingMajorArc
                    && first.reflectionState.pendingMajorArcRequestedTick == 103
                    && first.reflectionState.pendingMajorArcAvoidEventId == "evt-first"
                    && first.reflectionState.memoryOwnerEpochToken == epoch
                    && first.reflectionState.lastQuietMemoryEvaluatedAbsoluteDay == 11
                    && first.reflectionState.lastQuietMemoryActivatedAbsoluteQuadrum == 3,
                "The primary reflection merge altered unrelated fields or lost cadence maxima.");
            Require(duplicate.reflectionState.baselineOnNextOpportunity
                    && !duplicate.reflectionState.linkedBaselineOnNextOpportunity
                    && duplicate.reflectionState.lastReflectionTick == 201
                    && duplicate.reflectionState.lastCrossArcTick == 202
                    && duplicate.reflectionState.pendingMajorArc
                    && duplicate.reflectionState.pendingMajorArcRequestedTick == 203
                    && duplicate.reflectionState.pendingMajorArcAvoidEventId == "evt-duplicate"
                    && duplicate.reflectionState.memoryReflectionSchemaVersion == 0
                    && string.IsNullOrEmpty(duplicate.reflectionState.memoryOwnerEpochToken)
                    && duplicate.reflectionState.lastQuietMemoryEvaluatedAbsoluteDay == -1
                    && duplicate.reflectionState.lastQuietMemoryActivatedAbsoluteQuadrum == -1
                    && string.IsNullOrEmpty(duplicate.reflectionState.lastQuietMemoryDecisionKey),
                "The duplicate reflection projection cleared fields outside the five owned fields.");

            PawnKnowledgeState committedFirst = first.knowledgeState;
            PawnKnowledgeState committedDuplicate = duplicate.knowledgeState;
            PawnReflectionState committedFirstReflection = first.reflectionState;
            PawnReflectionState committedDuplicateReflection = duplicate.reflectionState;
            component.RunMemoryMigrationCommit();
            Require(ReferenceEquals(committedFirst, first.knowledgeState)
                    && ReferenceEquals(committedDuplicate, duplicate.knowledgeState)
                    && ReferenceEquals(committedFirstReflection, first.reflectionState)
                    && ReferenceEquals(committedDuplicateReflection, duplicate.reflectionState),
                "A second duplicate-owner commit changed the published projection.");
        }

        /// <summary>
        /// Two already-current physical holders still represent one logical owner. Migration must
        /// consolidate their disjoint payload into the first holder and leave the duplicate inert;
        /// otherwise lookup and byte indexes silently expose and charge only the first container.
        /// </summary>
        [Test]
        public static void CurrentSchemaDuplicateOwnersConsolidateWithoutLegacyRows()
        {
            const string ownerId = "Pawn_M11_CurrentDuplicate";
            string epoch = EpochToken(68);
            PawnKnowledgeState firstState = PawnKnowledgeState.CreateCurrent(ownerId);
            firstState.autobiographicalEpochToken = epoch;
            firstState.originCultureDefName = "Culture_Current_First";
            PawnDiaryMemoryM11RuntimeFixture.AddThreadRoot(
                firstState,
                "Pawn_M11_CurrentSubjectA",
                "Current subject A",
                681,
                MemoryContractTokens.SubjectPawn,
                MemoryContractTokens.CategoryRelationships);
            PawnKnowledgeState duplicateState = PawnKnowledgeState.CreateCurrent(ownerId);
            duplicateState.autobiographicalEpochToken = epoch;
            duplicateState.originCultureDefName = "Culture_Current_Duplicate";
            PawnDiaryMemoryM11RuntimeFixture.AddThreadRoot(
                duplicateState,
                "Pawn_M11_CurrentSubjectB",
                "Current subject B",
                682,
                MemoryContractTokens.SubjectPawn,
                MemoryContractTokens.CategoryRelationships);
            var first = new PawnDiaryRecord
            {
                pawnId = ownerId,
                knowledgeState = firstState
            };
            var duplicate = new PawnDiaryRecord
            {
                pawnId = ownerId,
                knowledgeState = duplicateState
            };
            DiaryGameComponent component = NewMemoryComponent(
                new List<PawnDiaryRecord> { first, duplicate }, null, null);

            component.RunMemoryMigrationCommit();

            Require(first.knowledgeState.IsCurrentSchema()
                    && first.knowledgeState.autobiographicalEpochToken == epoch
                    && first.knowledgeState.threadRoots.Count == 2
                    && first.knowledgeState.originCultureDefName == "Culture_Current_First",
                "Current duplicate holders did not consolidate into the logical primary owner.");
            Require(duplicate.knowledgeState.IsCurrentSchema()
                    && string.IsNullOrEmpty(
                        duplicate.knowledgeState.autobiographicalEpochToken)
                    && duplicate.knowledgeState.threadRoots.Count == 0
                    && duplicate.knowledgeState.standaloneBlocks.Count == 0
                    && duplicate.knowledgeState.requestCancellationGeneration > 0
                    && duplicate.knowledgeState.structuralRevision > 0
                    && duplicate.knowledgeState.statusRevision > 0
                    && duplicate.knowledgeState.originCultureDefName
                        == "Culture_Current_Duplicate",
                "The consolidated current duplicate did not become a valid inert physical holder.");
            PawnKnowledgeState committedFirst = first.knowledgeState;
            PawnKnowledgeState committedDuplicate = duplicate.knowledgeState;
            component.RunMemoryMigrationCommit();
            Require(ReferenceEquals(committedFirst, first.knowledgeState)
                    && ReferenceEquals(committedDuplicate, duplicate.knowledgeState)
                    && first.knowledgeState.threadRoots.Count == 2,
                "Current duplicate consolidation did not reach a fixed point.");
            component.RebuildMemorySizeIndexes();
            MemoryPayloadBudgetTotals totals = component.GetGlobalBudgetTotals();
            Require(totals.globalActiveBytes > 0 && totals.globalImportedBytes >= 0,
                "The consolidated current owner was not represented by the byte index.");
        }

        /// <summary>
        /// Canonical inert duplicates are physical culture containers, not another logical owner.
        /// They must neither allocate an epoch nor erase an existing Brainwipe fence on reload.
        /// </summary>
        [Test]
        public static void CanonicalCurrentDuplicateShapesRemainMigrationFixedPoints()
        {
            const string emptyOwner = "Pawn_M11_CurrentEmptyDuplicates";
            PawnKnowledgeState emptyPrimary = PawnKnowledgeState.CreateCurrent(emptyOwner);
            PawnKnowledgeState emptyDuplicate = PawnKnowledgeState.CreateCurrent(emptyOwner);
            emptyDuplicate.originCultureDefName = "Culture_Inert_Physical";
            var emptyFirst = new PawnDiaryRecord
            {
                pawnId = emptyOwner,
                knowledgeState = emptyPrimary
            };
            var emptySecond = new PawnDiaryRecord
            {
                pawnId = emptyOwner,
                knowledgeState = emptyDuplicate
            };

            const string fenceOwner = "Pawn_M11_CurrentFenceDuplicates";
            PawnKnowledgeState fencePrimary = PawnKnowledgeState.CreateCurrent(fenceOwner);
            fencePrimary.autobiographicalEpochToken = EpochToken(69);
            fencePrimary.epochFenceOnly = true;
            PawnKnowledgeState fenceDuplicate = PawnKnowledgeState.CreateCurrent(fenceOwner);
            var fenceFirst = new PawnDiaryRecord
            {
                pawnId = fenceOwner,
                knowledgeState = fencePrimary
            };
            var fenceSecond = new PawnDiaryRecord
            {
                pawnId = fenceOwner,
                knowledgeState = fenceDuplicate
            };
            DiaryGameComponent component = NewMemoryComponent(
                new List<PawnDiaryRecord>
                {
                    emptyFirst, emptySecond, fenceFirst, fenceSecond
                },
                null,
                null);

            component.RunMemoryMigrationCommit();

            Require(ReferenceEquals(emptyPrimary, emptyFirst.knowledgeState)
                    && ReferenceEquals(emptyDuplicate, emptySecond.knowledgeState)
                    && string.IsNullOrEmpty(
                        emptyFirst.knowledgeState.autobiographicalEpochToken)
                    && emptySecond.knowledgeState.originCultureDefName
                        == "Culture_Inert_Physical",
                "Empty current duplicates allocated an epoch or lost physical culture.");
            Require(ReferenceEquals(fencePrimary, fenceFirst.knowledgeState)
                    && ReferenceEquals(fenceDuplicate, fenceSecond.knowledgeState)
                    && fenceFirst.knowledgeState.epochFenceOnly
                    && fenceFirst.knowledgeState.autobiographicalEpochToken == EpochToken(69)
                    && !fenceSecond.knowledgeState.epochFenceOnly,
                "Canonical Brainwipe duplicate holders lost their fence lifecycle on reload.");
        }

        /// <summary>
        /// Saves written by the earlier M11 implementation can contain zero current invariants,
        /// either on one cleared envelope or on an inert duplicate. Migration repairs those shapes
        /// once without allocating a blank owner's epoch or disturbing an enrolled primary.
        /// </summary>
        [Test]
        public static void ZeroInvariantCurrentShapesRepairOnce()
        {
            const string clearedOwner = "Pawn_M11_ZeroInvariantCleared";
            PawnKnowledgeState cleared = PawnKnowledgeState.CreateCurrent(clearedOwner);
            cleared.requestCancellationGeneration = 0;
            cleared.structuralRevision = 0;
            cleared.statusRevision = 0;
            cleared.completedDiaryEntryOrdinal = 0;
            var clearedDiary = new PawnDiaryRecord
            {
                pawnId = clearedOwner,
                knowledgeState = cleared
            };

            const string duplicateOwner = "Pawn_M11_ZeroInvariantDuplicate";
            PawnKnowledgeState primary = PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                duplicateOwner,
                "Pawn_M11_ZeroInvariantSubject",
                "Zero invariant subject",
                701);
            PawnKnowledgeState inert = PawnKnowledgeState.CreateCurrent(duplicateOwner);
            inert.requestCancellationGeneration = 0;
            inert.structuralRevision = 0;
            inert.statusRevision = 0;
            inert.completedDiaryEntryOrdinal = 0;
            inert.originCultureDefName = "Culture_Zero_Inert";
            var primaryDiary = new PawnDiaryRecord
            {
                pawnId = duplicateOwner,
                knowledgeState = primary
            };
            var inertDiary = new PawnDiaryRecord
            {
                pawnId = duplicateOwner,
                knowledgeState = inert
            };
            DiaryGameComponent component = NewMemoryComponent(
                new List<PawnDiaryRecord> { clearedDiary, primaryDiary, inertDiary },
                null,
                null);

            component.RunMemoryMigrationCommit();

            Require(clearedDiary.knowledgeState.IsCurrentSchema()
                    && string.IsNullOrEmpty(
                        clearedDiary.knowledgeState.autobiographicalEpochToken)
                    && clearedDiary.knowledgeState.requestCancellationGeneration > 0
                    && clearedDiary.knowledgeState.structuralRevision > 0
                    && clearedDiary.knowledgeState.statusRevision > 0
                    && clearedDiary.knowledgeState.completedDiaryEntryOrdinal > 0,
                "A single zero-invariant current envelope stayed permanently invalid.");
            Require(primaryDiary.knowledgeState.threadRoots.Count == 1
                    && !string.IsNullOrEmpty(
                        primaryDiary.knowledgeState.autobiographicalEpochToken)
                    && inertDiary.knowledgeState.requestCancellationGeneration > 0
                    && inertDiary.knowledgeState.structuralRevision > 0
                    && inertDiary.knowledgeState.statusRevision > 0
                    && inertDiary.knowledgeState.completedDiaryEntryOrdinal > 0
                    && inertDiary.knowledgeState.originCultureDefName
                        == "Culture_Zero_Inert",
                "A zero-invariant duplicate was skipped or damaged its enrolled primary.");

            PawnKnowledgeState repairedCleared = clearedDiary.knowledgeState;
            PawnKnowledgeState repairedPrimary = primaryDiary.knowledgeState;
            PawnKnowledgeState repairedInert = inertDiary.knowledgeState;
            component.RunMemoryMigrationCommit();
            Require(ReferenceEquals(repairedCleared, clearedDiary.knowledgeState)
                    && ReferenceEquals(repairedPrimary, primaryDiary.knowledgeState)
                    && ReferenceEquals(repairedInert, inertDiary.knowledgeState),
                "Zero-invariant compatibility repair did not reach a fixed point.");
        }

        /// <summary>
        /// A current Brainwipe fence mixed with an older duplicate must stay raw and fenced. Legacy
        /// rows are not allowed to cross that privacy boundary and become active in the wiped epoch.
        /// </summary>
        [Test]
        public static void MixedLegacyDuplicateCannotEraseCurrentBrainwipeFence()
        {
            const string ownerId = "Pawn_M11_MixedFenceLegacy";
            PawnKnowledgeState fence = PawnKnowledgeState.CreateCurrent(ownerId);
            fence.autobiographicalEpochToken = EpochToken(70);
            fence.epochFenceOnly = true;
            PawnKnowledgeState legacy = new PawnKnowledgeState
            {
                pawnId = ownerId,
                schemaVersion = 1,
                records = new List<ImportantMemoryRecord> { NewLegacyRecord() }
            };
            var currentDiary = new PawnDiaryRecord
            {
                pawnId = ownerId,
                knowledgeState = fence
            };
            var legacyDiary = new PawnDiaryRecord
            {
                pawnId = ownerId,
                knowledgeState = legacy
            };
            DiaryGameComponent component = NewMemoryComponent(
                new List<PawnDiaryRecord> { currentDiary, legacyDiary }, null, null);

            component.RunMemoryMigrationCommit();

            Require(ReferenceEquals(fence, currentDiary.knowledgeState)
                    && currentDiary.knowledgeState.epochFenceOnly
                    && currentDiary.knowledgeState.autobiographicalEpochToken == EpochToken(70)
                    && currentDiary.knowledgeState.threadRoots.Count == 0
                    && currentDiary.knowledgeState.standaloneBlocks.Count == 0
                    && ReferenceEquals(legacy, legacyDiary.knowledgeState)
                    && legacyDiary.knowledgeState.schemaVersion == 1
                    && legacyDiary.knowledgeState.records.Count == 1,
                "Mixed duplicate migration erased the fence or resurrected legacy memory.");
            component.RunMemoryMigrationCommit();
            Require(ReferenceEquals(fence, currentDiary.knowledgeState)
                    && ReferenceEquals(legacy, legacyDiary.knowledgeState)
                    && currentDiary.knowledgeState.epochFenceOnly,
                "A repeated mixed duplicate pass crossed the Brainwipe boundary.");
        }

        [Test]
        public static void M11PressureCommitPreservesPhysicalCultureFields()
        {
            const string ownerId = "Pawn_M11_PressureCulture";
            PawnKnowledgeState state = PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                ownerId, "Pawn_M11_PressureSubject", "Pressure subject", 71,
                extraStandalone: 128);
            state.originCultureDefName = "Culture_Pressure_Origin_世界_🙂";
            state.originCultureSource = "pressure-source/exact";
            state.adoptedCultureDefName = "Culture_Pressure_Adopted_é";
            string origin = state.originCultureDefName;
            string provenance = state.originCultureSource;
            string adopted = state.adoptedCultureDefName;
            int blocksBefore = CountBlocks(state);
            DiaryGameComponent component = NewMemoryComponent(
                new List<PawnDiaryRecord>
                {
                    new PawnDiaryRecord { pawnId = ownerId, knowledgeState = state }
                },
                null,
                null);
            MethodInfo pressure = typeof(DiaryGameComponent).GetMethod(
                "TryApplyMemoryPressureCaps",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(pressure != null, "The production pressure-commit seam was renamed.");

            pressure.Invoke(component, new object[]
            {
                Math.Max(0, Find.TickManager?.TicksGame ?? 0), null, null, null, string.Empty
            });

            Require(CountBlocks(state) < blocksBefore && CountBlocks(state) <= 128,
                "The fixture did not execute a real owner-cap pressure commit.");
            Require(string.Equals(state.originCultureDefName, origin, StringComparison.Ordinal)
                    && string.Equals(state.originCultureSource, provenance,
                        StringComparison.Ordinal)
                    && string.Equals(state.adoptedCultureDefName, adopted,
                        StringComparison.Ordinal),
                "Memory pressure changed exact physical culture fields.");
        }

        [Test]
        public static void M11SavedSummaryOpportunityRepairKeepsOneCanonicalSlot()
        {
            SummaryWordingOpportunitySnapshot first = M10Opportunity(
                "Pawn_M11_Repair", EpochToken(81), "root-a", "summary-a", 10, 100);
            SummaryWordingOpportunitySnapshot second = M10Opportunity(
                first.ownerPawnId, first.ownerEpochToken, "root-b", "summary-b", 9, 101);
            string key;
            Require(MemoryOptionalAiPolicy.TryCreateSummaryOpportunityKey(first, out key),
                "First repair fixture opportunity was invalid.");
            first.opportunityKey = key;
            Require(MemoryOptionalAiPolicy.TryCreateSummaryOpportunityKey(second, out key),
                "Second repair fixture opportunity was invalid.");
            second.opportunityKey = key;
            var rows = new List<SavedSummaryWordingOpportunityV1>
            {
                SaveOpportunity(first),
                SaveOpportunity(second),
                new SavedSummaryWordingOpportunityV1 { ownerPawnId = first.ownerPawnId }
            };

            RunWithTempFile(path =>
            {
                List<SavedSummaryWordingOpportunityV1> saved = rows;
                SaveWithScribe(path, () =>
                    Scribe_Collections.Look(ref saved, "opportunities", LookMode.Deep));
                List<SavedSummaryWordingOpportunityV1> loaded = null;
                LoadVarsWithScribe(path, () =>
                    Scribe_Collections.Look(ref loaded, "opportunities", LookMode.Deep));
                SummaryWordingOpportunitySnapshot winner = null;
                int terminal = 0;
                for (int index = 0; loaded != null && index < loaded.Count; index++)
                {
                    SummaryWordingOpportunitySnapshot incoming = SnapshotOpportunity(loaded[index]);
                    if (!MemoryOptionalAiPolicy.IsValidSummaryOpportunity(incoming)) continue;
                    SummaryWordingSlotPlan plan = MemoryOptionalAiPolicy.PlanOwnerSlot(
                        winner, incoming, 120);
                    Require(plan.valid, "Loaded Summary repair plan was invalid.");
                    winner = plan.winner;
                    terminal += plan.terminal.Count;
                }
                Require(winner != null && winner.opportunityKey == first.opportunityKey
                        && terminal == 1,
                    "Loaded Summary repair did not retain one deterministic owner slot.");
            });
        }

        [Test]
        public static void M11FailedSavedActivationRollsBackInvisibleTransportStage()
        {
            var queue = new BoundedTransportQueue<string>(1);
            StagedTransportQueueItem<string> staged;
            Require(queue.TryStage("not-yet-visible", out staged),
                "Fixture could not reserve the invisible transport stage.");
            var invalidSaved = new SavedActiveLogicalRequestV1
            {
                requestStateToken = MemoryRequestStateMachineContracts.Staged
            };
            Require(!MemoryDispatchSavedAdapter.TryActivate(invalidSaved),
                "An invalid saved request unexpectedly activated.");
            Require(queue.Cancel(staged) && queue.ReservedCount == 0 && queue.Count == 0,
                "Saved activation refusal leaked its invisible queue reservation.");
        }

        [Test]
        public static void M11InvocationWinsButNeverCrossesEpochFence()
        {
            var cutoffs = new MemoryInvokedGenerationCutoffTable();
            string requestId;
            Require(MemoryIdentityCodec.TryCreateLogicalRequestId(811, out requestId)
                    && cutoffs.TryRegister(9, "Pawn_M11_Winner", EpochToken(82),
                        3, 4, requestId, 17, 2),
                "Fixture could not register the committed physical invocation.");
            cutoffs.SealGeneration(9, 4, 17);
            Require(cutoffs.AllowsInvocationWinner(9, "Pawn_M11_Winner", EpochToken(82),
                        3, 4, requestId, 17)
                    && !cutoffs.AllowsInvocationWinner(9, "Pawn_M11_Winner", EpochToken(83),
                        3, 4, requestId, 17),
                "Invocation-wins was lost or escaped the exact Brainwipe epoch fence.");
        }

        [Test]
        public static void M11ReloadPreservesMeaningfulDelayWithoutCatchUp()
        {
            long savedBaseline = 100;
            RunWithTempFile(path =>
            {
                SaveWithScribe(path, () =>
                    Scribe_Values.Look(ref savedBaseline, "meaningfulBaseline", -1));
                long loadedBaseline = -1;
                LoadVarsWithScribe(path, () =>
                    Scribe_Values.Look(ref loadedBaseline, "meaningfulBaseline", -1));
                long planned = MemoryOptionalAiPolicy.PlanMeaningfulEligibilityBaseline(
                    true, false, loadedBaseline, 50000);
                Require(planned == 100
                        && !MemoryOptionalAiPolicy.IsMeaningfulEventAfterEligibilityBaseline(
                            99, planned)
                        && MemoryOptionalAiPolicy.IsMeaningfulEventAfterEligibilityBaseline(
                            101, planned),
                    "Reload changed the saved meaningful-event boundary or admitted catch-up.");
            });
        }

        [Test]
        public static void M11OptionalWordingRefreshesLibraryProjection()
        {
            var wording = new MemoryRecallNaturalWordingSnapshot
            {
                currentProjectionFingerprint = new string('d', 64),
                currentFormatRevision = 2,
                currentCategoryMask = 15,
                optionalWording = "First natural wording.",
                optionalFingerprint = new string('d', 64),
                optionalFormatRevision = 2,
                optionalCategoryMask = 15,
                optionalSucceeded = true
            };
            Require(MemoryNaturalWordingProjection.Select(
                    false, "Deterministic wording.", wording, 240)
                    == "First natural wording.",
                "Library projection ignored a current optional wording cache.");
            wording.optionalWording = "Refreshed natural wording.";
            Require(MemoryNaturalWordingProjection.Select(
                    false, "Deterministic wording.", wording, 240)
                    == "Refreshed natural wording.",
                "Library projection did not refresh after optional wording changed.");
            wording.currentProjectionFingerprint = new string('e', 64);
            Require(MemoryNaturalWordingProjection.Select(
                    false, "Deterministic wording.", wording, 240)
                    == "Deterministic wording.",
                "A stale optional cache replaced current deterministic Library truth.");
        }

        [Test]
        public static void M11PhysicalCancellationAndSetupFailureRemainCleanable()
        {
            var queue = new BoundedTransportQueue<string>(1);
            StagedTransportQueueItem<string> staged;
            Require(queue.TryStage("cancel-before-worker", out staged)
                    && queue.Activate(staged)
                    && queue.Cancel(staged)
                    && queue.PhysicalCount == 0
                    && queue.Count == 0
                    && queue.ReservedCount == 0,
                "Activated cancellation left a physical FIFO tombstone or reservation.");

            var request = new LlmGenerationRequest
            {
                eventId = "event-m11-cleanup",
                povRole = DiaryEvent.InitiatorRole,
                sessionId = 12,
                memoryDispatch = new MemoryDispatchTransportContext
                {
                    logicalRequestId = "logical-m11-cleanup"
                }
            };
            LlmGenerationResult failure = LlmClient.CreateDispatchSetupFailureResult(
                request, new InvalidOperationException("fixture setup failure"));
            Require(failure.memoryDispatchTerminalFailure
                    && failure.memoryLogicalRequestId == "logical-m11-cleanup"
                    && !failure.success,
                "Pre-permit worker failure lost the coordinator identity needed for cleanup.");
        }

        [Test]
        public static void RawUnresolvedWrapperPreservesLegacyRecord()
        {
            var wrapper = new SavedLegacyUnresolvedOwnerArchiveInputV1
            {
                savedOwnerIdentityKindToken = "conflicting",
                savedOwnerIdentityValue = new string('e', 64),
                sourceContainerOrdinal = 2,
                sourceRecordOrdinal = 5
            };
            wrapper.legacyRecord = NewLegacyRecord();

            RunWithTempFile(path =>
            {
                SavedLegacyUnresolvedOwnerArchiveInputV1 saved = wrapper;
                SaveWithScribe(path, () => Scribe_Deep.Look(ref saved, "raw"));
                SavedLegacyUnresolvedOwnerArchiveInputV1 loaded = null;
                LoadVarsWithScribe(path, () => Scribe_Deep.Look(ref loaded, "raw"));
                Require(loaded != null && loaded.legacyRecord != null,
                    "The raw wrapper and its nested legacy record must round-trip.");
                ImportantMemoryRecord legacy = loaded.legacyRecord;
                Require(legacy.recordId == "legacy-rec-1" && legacy.dedupKey == "dedup-1"
                        && legacy.tick == 9876
                        && legacy.participantIds.Count == 2
                        && legacy.participantNames.Count == 2
                        && legacy.factKeys.Count == 2 && legacy.factValues.Count == 2
                        && legacy.factValues[1] == "spouse",
                    "Parallel legacy lists must be preserved whole before any planning.");
                Require(loaded.sourceContainerOrdinal == 2 && loaded.sourceRecordOrdinal == 5,
                    "Input-local diagnostic coordinates are preserved as-is.");
            });
        }

        [Test]
        public static void LogicalSizingVisitsRowsAfterNullHoles()
        {
            var contributionA = new SavedMemoryFactContribution
            {
                contributionId = "contribution-a",
                canonicalValue = "alpha"
            };
            var contributionB = new SavedMemoryFactContribution
            {
                contributionId = "contribution-b",
                canonicalValue = "beta"
            };

            Require(SizeOf(NewBucket(contributionA))
                    == SizeOf(NewBucket(null, contributionA)),
                "A null-first nested row hid the later contribution from logical sizing.");
            Require(SizeOf(NewBucket(contributionA, contributionB))
                    == SizeOf(NewBucket(contributionA, null, contributionB)),
                "A null-middle nested row hid the later contribution from logical sizing.");
            Require(SizeOf(NewBucket(contributionA))
                    == SizeOf(NewBucket(contributionA, null)),
                "A null-last nested row changed the frozen non-null list encoding.");

            Require(SizeOf(NewContributionWithSubjectRefs("subject-a"))
                    == SizeOf(NewContributionWithSubjectRefs(null, "subject-a")),
                "A null-first value-list entry hid the later string from logical sizing.");
            Require(SizeOf(NewContributionWithSubjectRefs("subject-a", "subject-b"))
                    == SizeOf(NewContributionWithSubjectRefs("subject-a", null, "subject-b")),
                "A null-middle value-list entry hid the later string from logical sizing.");
            Require(SizeOf(NewContributionWithSubjectRefs("subject-a"))
                    == SizeOf(NewContributionWithSubjectRefs("subject-a", null)),
                "A null-last value-list entry changed the frozen non-null list encoding.");

            var evidenceA = new SavedFrozenEvidenceEntryV1 { recordId = "record-a" };
            var evidenceB = new SavedFrozenEvidenceEntryV1 { recordId = "record-b" };
            Require(SizeOf(NewRequestWithEvidence(evidenceA))
                    == SizeOf(NewRequestWithEvidence(null, evidenceA)),
                "A null-first request row hid the later reserved evidence entry.");
            Require(SizeOf(NewRequestWithEvidence(evidenceA, evidenceB))
                    == SizeOf(NewRequestWithEvidence(evidenceA, null, evidenceB)),
                "A null-middle request row hid the later reserved evidence entry.");
            Require(SizeOf(NewRequestWithEvidence(evidenceA))
                    == SizeOf(NewRequestWithEvidence(evidenceA, null)),
                "A null-last request row changed the frozen non-null list encoding.");
        }

        [Test]
        public static void MemoryBudgetAccountingChargesEachPhysicalByteOnce()
        {
            const string ownerId = "Pawn_Accounting";
            PawnKnowledgeState cultureFree = PawnKnowledgeState.CreateCurrent(ownerId);
            PawnKnowledgeState culturePopulated = PawnKnowledgeState.CreateCurrent(ownerId);
            culturePopulated.originCultureDefName = "Culture_Budget_Origin_世界";
            culturePopulated.originCultureSource = "captured";
            culturePopulated.adoptedCultureDefName = "Culture_Budget_Adopted_🙂";
            Require(SizeOf(cultureFree) == SizeOf(culturePopulated),
                "Pre-memory culture fields entered unified-memory byte accounting.");

            SavedActiveLogicalRequestV1 ownerRequest = NewRequestWithEvidence(
                new SavedFrozenEvidenceEntryV1 { recordId = "owner-evidence" });
            ownerRequest.ownerPawnId = ownerId;
            long requestBytes = SizeOf(ownerRequest);

            DiaryGameComponent ownerBaseline = NewMemoryComponent(
                NewCurrentDiaryList(ownerId), null, null);
            ownerBaseline.RebuildMemorySizeIndexes();
            DiaryGameComponent ownerWithRequest = NewMemoryComponent(
                NewCurrentDiaryList(ownerId),
                new List<SavedActiveLogicalRequestV1> { ownerRequest },
                null);
            ownerWithRequest.RebuildMemorySizeIndexes();

            DiaryGameComponent.MemoryOwnerByteTotals baselineOwner =
                ownerBaseline.GetOwnerByteTotals(ownerId);
            DiaryGameComponent.MemoryOwnerByteTotals requestOwner =
                ownerWithRequest.GetOwnerByteTotals(ownerId);
            MemoryPayloadBudgetTotals baselineGlobal = ownerBaseline.GetGlobalBudgetTotals();
            MemoryPayloadBudgetTotals requestGlobal = ownerWithRequest.GetGlobalBudgetTotals();
            Require(baselineOwner.valid && requestOwner.valid
                    && baselineGlobal.GlobalCombined() >= 0
                    && requestGlobal.GlobalCombined() >= 0,
                "Owner-attributed request accounting produced an invalid measured unit.");
            Require(requestOwner.activeBytes - baselineOwner.activeBytes == requestBytes,
                "An active request was not charged exactly once to its physical owner.");
            Require(requestOwner.importedBytes == baselineOwner.importedBytes,
                "An active request changed the owner's Imported byte subtotal.");
            Require(requestGlobal.globalActiveBytes - baselineGlobal.globalActiveBytes
                    == requestBytes,
                "An active request was lost or double-counted in global active bytes.");
            Require(requestGlobal.GlobalCombined() - baselineGlobal.GlobalCombined()
                    == requestBytes,
                "An active request was lost or double-counted in global combined bytes.");

            var unknownA = new SavedImportedMemoryRow { archiveRecordId = "unknown-a" };
            var unknownB = new SavedImportedMemoryRow { archiveRecordId = "unknown-b" };
            long unknownABytes = SizeOf(unknownA);
            long unknownBBytes = SizeOf(unknownB);
            MemoryPayloadBudgetTotals emptyUnknown = RebuildAndGetGlobal(
                new List<SavedImportedMemoryRow>());
            MemoryPayloadBudgetTotals oneUnknown = RebuildAndGetGlobal(
                new List<SavedImportedMemoryRow> { unknownA });
            MemoryPayloadBudgetTotals manyUnknown = RebuildAndGetGlobal(
                new List<SavedImportedMemoryRow> { unknownA, unknownB });
            Require(emptyUnknown.globalImportedBytes == 4,
                "The empty Unknown archive must charge its one four-byte list prefix.");
            Require(oneUnknown.globalImportedBytes == 4 + unknownABytes,
                "One Unknown archive row must charge one list prefix plus one row.");
            Require(manyUnknown.globalImportedBytes == 4 + unknownABytes + unknownBBytes,
                "Multiple Unknown archive rows must share one list prefix and charge each row once.");
            Require(emptyUnknown.globalActiveBytes == oneUnknown.globalActiveBytes
                    && oneUnknown.globalActiveBytes == manyUnknown.globalActiveBytes,
                "Unknown Imported rows leaked into the component/global active subtotal.");
            Require(oneUnknown.GlobalCombined() - emptyUnknown.GlobalCombined()
                    == unknownABytes
                    && manyUnknown.GlobalCombined() - oneUnknown.GlobalCombined()
                    == unknownBBytes,
                "Unknown archive rows were lost or counted twice in global combined bytes.");
        }

        /// <summary>
        /// Routine M4 admission updates only the changed owner's transient indexes. Its incremental
        /// totals and exact lookup must be indistinguishable from a forced full rebuild afterward.
        /// </summary>
        [Test]
        public static void IncrementalAdmissionIndexesMatchFullRebuild()
        {
            const string ownerId = "Pawn_Incremental_Admission";
            string epoch = EpochToken(301);
            PawnKnowledgeState state = PawnKnowledgeState.CreateCurrent(ownerId);
            state.autobiographicalEpochToken = epoch;
            string occurrence = "occurrence-incremental-admission";
            string captureRule = "capture-incremental-admission";
            string discriminator = "fact-incremental-admission";
            string recordId;
            Require(MemoryIdentityCodec.TryCreateRecordId(
                    new MemoryRecordIdentity
                    {
                        ownerPawnId = ownerId,
                        ownerEpochToken = epoch,
                        sourceOccurrenceId = occurrence,
                        captureRuleId = captureRule,
                        factDiscriminator = discriminator
                    },
                    out recordId),
                "The incremental admission fixture could not create a record identity.");
            SavedMemoryBlock block = new SavedMemoryBlock
            {
                schemaVersion = 1,
                recordId = recordId,
                sourceOccurrenceId = occurrence,
                captureRuleId = captureRule,
                factDiscriminator = discriminator,
                ownerPawnId = ownerId,
                ownerEpochToken = epoch,
                kind = MemoryContractTokens.KindLandmark,
                summaryRole = MemoryContractTokens.SummaryRoleNone,
                category = MemoryContractTokens.CategoryPersonal,
                importance = MemoryContractTokens.ImportanceImportant,
                ageUnknown = true,
                automaticWording = "Incremental admission fixture.",
                optionalLlmWording = "I remember this admission clearly.",
                optionalLlmFingerprint = new string('d', 64),
                optionalLlmFormatRevision = 4,
                optionalLlmCategoryMask = 3,
                providerExposureState = "not_sent",
                formatRevision = 1
            };
            block.Normalize();
            DiaryGameComponent component = NewMemoryComponent(
                new List<PawnDiaryRecord>
                {
                    new PawnDiaryRecord { pawnId = ownerId, knowledgeState = state }
                },
                null,
                null);
            component.RebuildMemorySizeIndexes();

            MemoryStoreAdmissionResult admitted = component.TryAdmitMemoryBlock(
                new MemoryStoreAdmissionRequest
                {
                    ownerPawnId = ownerId,
                    ownerEpochToken = epoch,
                    expectedOwnerStructuralRevision = state.structuralRevision,
                    routeReliable = false,
                    chapterPhaseToken = "fixture",
                    chapterDirective = MemoryChapterDirectiveTokens.ContinueCurrent,
                    nowTick = Math.Max(0, Find.TickManager?.TicksGame ?? 0),
                    block = block
                });
            Require(admitted.outcome == MemoryStoreMutationOutcome.Admitted,
                "The incremental index fixture did not admit its valid Standalone block.");

            DiaryGameComponent.MemoryOwnerByteTotals incrementalOwner =
                component.GetOwnerByteTotals(ownerId);
            MemoryPayloadBudgetTotals incrementalGlobal = component.GetGlobalBudgetTotals();
            SavedMemoryBlock incrementalLookup = component.FindStandaloneMemoryExact(
                ownerId, epoch, recordId);
            component.MarkMemoryM4IndexesDirty();
            component.RebuildMemorySizeIndexes();
            DiaryGameComponent.MemoryOwnerByteTotals rebuiltOwner =
                component.GetOwnerByteTotals(ownerId);
            MemoryPayloadBudgetTotals rebuiltGlobal = component.GetGlobalBudgetTotals();
            SavedMemoryBlock rebuiltLookup = component.FindStandaloneMemoryExact(
                ownerId, epoch, recordId);

            Require(incrementalOwner.valid && rebuiltOwner.valid
                    && incrementalOwner.activeBytes == rebuiltOwner.activeBytes
                    && incrementalOwner.importedBytes == rebuiltOwner.importedBytes
                    && incrementalGlobal.globalActiveBytes == rebuiltGlobal.globalActiveBytes
                    && incrementalGlobal.globalImportedBytes == rebuiltGlobal.globalImportedBytes
                    && incrementalLookup != null
                    && ReferenceEquals(incrementalLookup, rebuiltLookup)
                    && !ReferenceEquals(block, incrementalLookup)
                    && incrementalLookup.optionalLlmWording
                        == "I remember this admission clearly."
                    && incrementalLookup.optionalLlmFingerprint == new string('d', 64)
                    && incrementalLookup.optionalLlmFormatRevision == 4
                    && incrementalLookup.optionalLlmCategoryMask == 3,
                "Incremental admission clone or indexes diverged from the submitted block.");
        }

        [Test]
        public static void LogicalSizeWalkerValidatesPopulatedEnvelope()
        {
            // The registry-validated walker doubles as a live shape cross-check: any field added
            // to a model without a registry entry (or pushed out of order) fails right here.
            PawnKnowledgeState state = PawnKnowledgeState.CreateCurrent("Pawn_Size");
            state.autobiographicalEpochToken = EpochToken(1);
            state.standaloneBlocks.Add(NewBlock("rec-size", null));
            IMemoryLogicalSizeSource sized = state;
            var collector = new MemoryLogicalSizeCollector();
            try
            {
                sized.CollectFields(collector);
                Require(collector.Total() > 64,
                    "A populated envelope must size above its framing allowance.");
            }
            catch (MemoryLogicalSizeValidationException exception)
            {
                throw new AssertionException(
                    "Envelope sizing failed registry validation: " + exception.Message);
            }
        }

        // ----- Fixture scaffolding -----------------------------------------------------------------

        private static SavedMemoryThreadRoot NewRoot()
        {
            var root = new SavedMemoryThreadRoot
            {
                rootId = "root-1",
                ownerPawnId = "Pawn_M1",
                ownerEpochToken = EpochToken(3),
                subjectKind = "pawn",
                subjectId = "Pawn_B",
                structuralRevision = 1,
                statusRevision = 1,
                nextChapterOrdinal = 2
            };
            root.chapters.Add(new SavedMemoryChapter
            {
                chapterId = "chapter-1",
                ordinal = 1,
                phaseToken = "friend",
                openedTick = 10,
                lastActivityTick = 20,
                closedTick = 25,
                closureReasonToken = "formal_end",
                closed = true
            });
            root.visibleBlocks.Add(NewBlock("rec-root", "chapter-1"));
            root.rollingSummaryBlock = NewBlock("rec-rolling", null);
            root.rollingSummaryBlock.kind = "summary";
            root.rollingSummaryBlock.summaryRole = "rolling";
            return root;
        }

        private static SavedMemoryBlock NewBlock(string recordId, string chapterId)
        {
            var block = new SavedMemoryBlock
            {
                recordId = recordId,
                sourceOccurrenceId = "occ-" + recordId,
                sourceEventId = "evt-1",
                captureRuleId = "rule-x",
                factDiscriminator = "disc-x",
                ownerPawnId = "Pawn_M1",
                ownerEpochToken = EpochToken(3),
                kind = "landmark",
                summaryRole = "none",
                category = "personal",
                importance = "high",
                originalEventTick = 777,
                ageUnknown = false,
                chapterId = chapterId ?? string.Empty,
                formatRevision = 7,
                providerExposureState = "potentially_sent",
                automaticWording = "deterministic line"
            };
            block.primarySubject = new SavedMemorySubjectRef
            {
                subjectRefId = "ref-primary",
                subjectKind = "pawn",
                subjectId = "Pawn_B",
                frozenLabel = "B",
                roleToken = "primary",
                knownnessToken = "direct"
            };
            block.facts.Add(new SavedMemoryCanonicalFact
            {
                factId = "fact-1",
                factKind = "role",
                aggregationToken = "ordinal_set",
                canonicalValueKind = "ordinal",
                canonicalValue = "MoralGuide",
                majorTurningPoint = true
            });
            var payload = new SavedMemorySummaryPayload
            {
                reducerRevision = 1,
                factsRevision = 1,
                canonicalFactsFingerprint = new string('f', 64),
                deterministicWording = "summary wording"
            };
            var bucket = new SavedMemoryFactBucket
            {
                bucketKey = "bucket-1",
                factKind = "role",
                aggregationToken = "ordinal_set",
                derivedCount = 1
            };
            bucket.contributions.Add(new SavedMemoryFactContribution
            {
                contributionId = "contrib-1",
                originRecordId = "rec-source",
                originFactOrdinal = 0,
                originFactId = "fact-origin",
                originalEventTick = 700,
                category = "personal",
                importance = "medium",
                canonicalValue = "Knight"
            });
            payload.factBuckets.Add(bucket);
            if (chapterId != null)
            {
                // Only the visible root-block carries a summary payload in this fixture.
                block.summaryRole = "none";
            }
            else
            {
                block.summaryRole = "rolling";
            }

            block.summaryPayload = chapterId != null ? null : payload;
            return block;
        }

        private static ImportantMemoryRecord NewLegacyRecord()
        {
            // Complete frozen §T6.8 shape: parallel participant/fact lists populated so the
            // round-trip proves nothing is dropped or realigned before migration planning.
            ImportantMemoryRecord record = new ImportantMemoryRecord
            {
                recordId = "legacy-rec-1",
                dedupKey = "dedup-1",
                sourceEventId = "evt-legacy-1",
                eventKind = "relation.spouse.gained",
                topicKey = "relationship",
                tick = 9876,
                dateLabel = "Aprimday 12"
            };
            record.participantIds.Add("Pawn_B");
            record.participantIds.Add("Pawn_C");
            record.participantNames.Add("B");
            record.participantNames.Add("C");
            record.subjectKeys.Add("part:Heart");
            record.factKeys.Add("relation");
            record.factKeys.Add("status");
            record.factValues.Add("spouse");
            record.factValues.Add("married");
            return record;
        }

        private sealed class LegacyMissingKnowledgeRowAdapter : IExposable
        {
            public void ExposeData()
            {
                Require(legacyMissingKnowledgeTarget != null,
                    "The missing-knowledge Scribe adapter has no target record.");
                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    string pawnId = legacyMissingKnowledgeTarget.pawnId;
                    string pawnName = legacyMissingKnowledgeTarget.pawnName;
                    List<string> eventIds = legacyMissingKnowledgeTarget.eventIds;
                    Scribe_Values.Look(ref pawnId, "pawnId");
                    Scribe_Values.Look(ref pawnName, "pawnName");
                    Scribe_Collections.Look(ref eventIds, "eventIds", LookMode.Value);
                    return;
                }

                legacyMissingKnowledgeTarget.ExposeData();
            }
        }

        private static ImportantMemoryRecord NewCapacityLegacyRecord(
            int ownerIndex,
            int rowIndex)
        {
            string suffix = ownerIndex.ToString("D3") + "-" + rowIndex.ToString("D3");
            return new ImportantMemoryRecord
            {
                recordId = "legacy-cap-rec-" + suffix,
                sourceEventId = "legacy-cap-event-" + suffix,
                eventKind = "legacy.capacity.fixture",
                tick = 0
            };
        }

        private static int CountBlocks(PawnKnowledgeState state)
        {
            int count = state?.standaloneBlocks?.Count ?? 0;
            for (int index = 0; state?.threadRoots != null
                && index < state.threadRoots.Count; index++)
            {
                SavedMemoryThreadRoot root = state.threadRoots[index];
                count += root?.visibleBlocks?.Count ?? 0;
                if (root?.rollingSummaryBlock != null) count++;
            }

            return count;
        }

        private static SavedMemoryFactBucket NewBucket(
            params SavedMemoryFactContribution[] contributions)
        {
            return new SavedMemoryFactBucket
            {
                bucketKey = "null-hole-bucket",
                contributions = new List<SavedMemoryFactContribution>(contributions)
            };
        }

        private static SavedMemoryFactContribution NewContributionWithSubjectRefs(
            params string[] subjectRefIds)
        {
            return new SavedMemoryFactContribution
            {
                contributionId = "null-hole-contribution",
                subjectRefIds = new List<string>(subjectRefIds)
            };
        }

        private static SavedActiveLogicalRequestV1 NewRequestWithEvidence(
            params SavedFrozenEvidenceEntryV1[] evidenceEntries)
        {
            return new SavedActiveLogicalRequestV1
            {
                logicalRequestId = "null-hole-request",
                reservedEvidenceEntries =
                    new List<SavedFrozenEvidenceEntryV1>(evidenceEntries)
            };
        }

        private static long SizeOf(IMemoryLogicalSizeSource source)
        {
            MemoryLogicalSizeResult result = MemoryLogicalPayloadSizer.Size(source);
            Require(result.valid,
                "Logical sizing failed for an adversarial fixture: " + result.errorPath);
            return result.totalBytes;
        }

        private static List<PawnDiaryRecord> NewCurrentDiaryList(string ownerId)
        {
            return new List<PawnDiaryRecord>
            {
                new PawnDiaryRecord
                {
                    pawnId = ownerId,
                    knowledgeState = PawnKnowledgeState.CreateCurrent(ownerId)
                }
            };
        }

        private static PawnKnowledgeState BuildAuthoredConflictOwner(
            string ownerId,
            string subjectId,
            int firstOrdinal)
        {
            PawnKnowledgeState state = PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                ownerId, subjectId, "Shared repair subject", firstOrdinal);
            // This fixture measures only the row appended by repair. BuildCompleteOwner carries an
            // unrelated baseline Imported row for Library coverage, so remove it here.
            state.importedArchiveRows.Clear();
            SavedMemoryThreadRoot first = state.threadRoots[0];
            SavedMemoryThreadRoot alternate = PawnDiaryMemoryM11RuntimeFixture.AddThreadRoot(
                state,
                subjectId,
                "Shared repair subject",
                firstOrdinal + 1,
                MemoryContractTokens.SubjectPawn,
                MemoryContractTokens.CategoryRelationships);
            SavedMemoryBlock firstBlock = first.visibleBlocks[0];
            SavedMemoryBlock alternateBlock = alternate.visibleBlocks[0];
            alternate.rootId = first.rootId;
            alternate.chapters[0].chapterId = first.chapters[0].chapterId;
            alternateBlock.rootId = first.rootId;
            alternateBlock.chapterId = first.chapters[0].chapterId;
            alternateBlock.recordId = firstBlock.recordId;
            firstBlock.playerEdited = true;
            firstBlock.playerWording = "Shared repair authored wording A.";
            alternateBlock.playerEdited = true;
            alternateBlock.playerWording = "Shared repair authored wording B.";
            return state;
        }

        private static MemoryPayloadBudgetTotals RebuildAndGetGlobal(
            List<SavedImportedMemoryRow> unknownRows)
        {
            DiaryGameComponent component = NewMemoryComponent(null, null, unknownRows);
            component.RebuildMemorySizeIndexes();
            MemoryPayloadBudgetTotals totals = component.GetGlobalBudgetTotals();
            Require(totals.GlobalCombined() >= 0,
                "Unknown archive accounting produced an invalid global measured unit.");
            return totals;
        }

        internal static DiaryGameComponent NewMemoryComponent(
            List<PawnDiaryRecord> ownerDiaries,
            List<SavedActiveLogicalRequestV1> activeRequests,
            List<SavedImportedMemoryRow> unknownRows)
        {
            // The real constructor starts an LLM/game session, which a fixture must never do. This
            // allocation creates only the inert persistence shell needed by the pure index rebuild.
            var component = (DiaryGameComponent)FormatterServices.GetUninitializedObject(
                typeof(DiaryGameComponent));
            SetPrivateField(component, "diaries",
                ownerDiaries ?? new List<PawnDiaryRecord>());
            SetPrivateField(component, "activeMemoryCoordinatorRequests",
                activeRequests ?? new List<SavedActiveLogicalRequestV1>());
            SetPrivateField(component, "unresolvedOwnerArchiveRows",
                unknownRows ?? new List<SavedImportedMemoryRow>());
            SetPrivateField(component, "legacyOwnerEpochReservations",
                new List<SavedLegacyOwnerEpochReservation>());
            SetPrivateField(component, "globalFactionSnapshots",
                new List<SavedGlobalFactionSnapshot>());
            SetPrivateField(component, "rawUnresolvedOwnerArchiveInput",
                new List<SavedLegacyUnresolvedOwnerArchiveInputV1>());
            SetPrivateField(component, "summaryWordingOpportunities",
                new List<SavedSummaryWordingOpportunityV1>());
            SetPrivateField(component, "memoryDiagnosticCounters",
                new List<SavedMemoryDiagnosticCounter>());
            SetPrivateField(component, "memoryAttemptAuditRows",
                new List<SavedMemoryAttemptAuditRow>());
            SetPrivateField(component, "memoryByteTotalsByOwner",
                new Dictionary<string, DiaryGameComponent.MemoryOwnerByteTotals>(
                    StringComparer.Ordinal));
            SetPrivateField(component, "memoryM4OwnerById",
                new Dictionary<string, PawnKnowledgeState>(StringComparer.Ordinal));
            SetPrivateField(component, "memoryM4RootByCanonicalId",
                new Dictionary<string, SavedMemoryThreadRoot>(StringComparer.Ordinal));
            SetPrivateField(component, "memoryM4BlockByQualifiedRecord",
                new Dictionary<string, SavedMemoryBlock>(StringComparer.Ordinal));
            SetPrivateField(component, "memoryM4StandaloneByQualifiedRecord",
                new Dictionary<string, SavedMemoryBlock>(StringComparer.Ordinal));
            SetPrivateField(component, "memoryM4ChapterByQualifiedId",
                new Dictionary<string, SavedMemoryChapter>(StringComparer.Ordinal));
            SetPrivateField(component, "memoryM4SourceFactDedupByKey",
                new Dictionary<string, SavedMemoryBlock>(StringComparer.Ordinal));
            SetPrivateField(component, "memoryM4IndexGeneration", 1L);
            SetPrivateField(component, "memoryM4IndexesDirty", true);
            return component;
        }

        private static SavedSummaryWordingOpportunityV1 SaveOpportunity(
            SummaryWordingOpportunitySnapshot source)
        {
            return new SavedSummaryWordingOpportunityV1
            {
                schemaVersion = 1,
                ownerPawnId = source.ownerPawnId,
                ownerEpochToken = source.ownerEpochToken,
                ownerCancellationGeneration = source.ownerCancellationGeneration,
                globalCancellationGeneration = source.globalCancellationGeneration,
                optionalRequestInvalidationGeneration =
                    source.optionalRequestInvalidationGeneration,
                rootId = source.rootId,
                summaryRecordId = source.summaryRecordId,
                expectedRootStructuralRevision = source.expectedRootStructuralRevision,
                expectedSummaryFactsRevision = source.expectedSummaryFactsRevision,
                expectedReducerRevision = source.expectedReducerRevision,
                expectedFormatRevision = source.expectedFormatRevision,
                expectedCategoryMask = source.expectedCategoryMask,
                projectionFingerprint = source.projectionFingerprint,
                requestedTick = source.requestedTick,
                dueTick = source.dueTick,
                expiryTick = source.expiryTick,
                configuredPriority = source.configuredPriority,
                salience = source.salience,
                opportunityKey = source.opportunityKey
            };
        }

        private static SummaryWordingOpportunitySnapshot SnapshotOpportunity(
            SavedSummaryWordingOpportunityV1 source)
        {
            return source == null ? null : new SummaryWordingOpportunitySnapshot
            {
                ownerPawnId = source.ownerPawnId ?? string.Empty,
                ownerEpochToken = source.ownerEpochToken ?? string.Empty,
                ownerCancellationGeneration = source.ownerCancellationGeneration,
                globalCancellationGeneration = source.globalCancellationGeneration,
                optionalRequestInvalidationGeneration =
                    source.optionalRequestInvalidationGeneration,
                rootId = source.rootId ?? string.Empty,
                summaryRecordId = source.summaryRecordId ?? string.Empty,
                expectedRootStructuralRevision = source.expectedRootStructuralRevision,
                expectedSummaryFactsRevision = source.expectedSummaryFactsRevision,
                expectedReducerRevision = source.expectedReducerRevision,
                expectedFormatRevision = source.expectedFormatRevision,
                expectedCategoryMask = source.expectedCategoryMask,
                projectionFingerprint = source.projectionFingerprint ?? string.Empty,
                requestedTick = source.requestedTick,
                dueTick = source.dueTick,
                expiryTick = source.expiryTick,
                configuredPriority = source.configuredPriority,
                salience = source.salience,
                opportunityKey = source.opportunityKey ?? string.Empty
            };
        }

        private static void RequireNewerSchemaRefused(
            string label, DiaryGameComponent component)
        {
            try
            {
                component.ScanForNewerMemorySchemas();
            }
            catch (DiaryGameComponent.NewerPawnDiarySaveFormatException)
            {
                return;
            }

            throw new AssertionException(
                "A newer nested " + label + " did not abort the whole memory load boundary.");
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Fixture could not find private field: " + fieldName);
            field.SetValue(target, value);
        }

        private static SummaryWordingOpportunitySnapshot M10Opportunity(
            string owner,
            string epoch,
            string root,
            string summary,
            int priority,
            long requestedTick)
        {
            return new SummaryWordingOpportunitySnapshot
            {
                ownerPawnId = owner,
                ownerEpochToken = epoch,
                ownerCancellationGeneration = 2,
                globalCancellationGeneration = 3,
                optionalRequestInvalidationGeneration = 4,
                rootId = OrdinalSegmentCodec.Segment(root),
                summaryRecordId = OrdinalSegmentCodec.Segment(summary),
                expectedRootStructuralRevision = 5,
                expectedSummaryFactsRevision = 6,
                expectedReducerRevision = 1,
                expectedFormatRevision = 1,
                expectedCategoryMask = 15,
                projectionFingerprint = new string('c', 64),
                requestedTick = requestedTick,
                dueTick = requestedTick,
                expiryTick = requestedTick + 1000,
                configuredPriority = priority,
                salience = 1
            };
        }

        private static string CarrierFactionSubject(long generation)
        {
            string subjectId;
            Require(MemoryIdentityCodec.TryCreateFactionSubjectId(
                    "Faction_Carrier_" + generation, generation, out subjectId),
                "Could not create a faction carrier fixture identity.");
            return subjectId;
        }

        private static MemoryRootIdentity CarrierRootIdentity(
            string owner, long epochSequence, long factionGeneration)
        {
            return new MemoryRootIdentity
            {
                ownerPawnId = owner,
                ownerEpochToken = EpochToken(epochSequence),
                primarySubjectKind = MemoryContractTokens.SubjectFaction,
                primarySubjectId = CarrierFactionSubject(factionGeneration)
            };
        }

        private static string CarrierRootId(
            string owner, long epochSequence, long factionGeneration)
        {
            string rootId;
            Require(MemoryIdentityCodec.TryCreateRootId(
                    CarrierRootIdentity(owner, epochSequence, factionGeneration), out rootId),
                "Could not create a root carrier fixture identity.");
            return rootId;
        }

        private static string CarrierChapterId(
            string owner, long epochSequence, long factionGeneration, long ordinal)
        {
            string chapterId;
            Require(MemoryIdentityCodec.TryCreateChapterId(
                    CarrierRootId(owner, epochSequence, factionGeneration), ordinal, out chapterId),
                "Could not create a chapter carrier fixture identity.");
            return chapterId;
        }

        private static string CarrierRecordId(string owner, long epochSequence, string suffix)
        {
            string recordId;
            Require(MemoryIdentityCodec.TryCreateRecordId(new MemoryRecordIdentity
                {
                    ownerPawnId = owner,
                    ownerEpochToken = EpochToken(epochSequence),
                    sourceOccurrenceId = "carrier-occurrence-" + suffix,
                    captureRuleId = "carrier-rule",
                    factDiscriminator = suffix
                }, out recordId),
                "Could not create an Event record carrier fixture identity.");
            return recordId;
        }

        private static string CarrierRollingSummaryId(
            string owner, long epochSequence, long factionGeneration)
        {
            string recordId;
            Require(MemoryIdentityCodec.TryCreateRollingSummaryId(
                    CarrierRootIdentity(owner, epochSequence, factionGeneration), out recordId),
                "Could not create a rolling Summary carrier fixture identity.");
            return recordId;
        }

        private static string CarrierClosedSummaryId(
            string owner, long epochSequence, long factionGeneration, long ordinal)
        {
            string recordId;
            Require(MemoryIdentityCodec.TryCreateClosedSummaryId(
                    CarrierRootIdentity(owner, epochSequence, factionGeneration),
                    ordinal,
                    out recordId),
                "Could not create a Closed Summary carrier fixture identity.");
            return recordId;
        }

        private static SavedMemorySubjectRef CarrierSubjectRef(long factionGeneration)
        {
            return new SavedMemorySubjectRef
            {
                subjectKind = MemoryContractTokens.SubjectFaction,
                subjectId = CarrierFactionSubject(factionGeneration)
            };
        }

        private static string EpochToken(long sequence)
        {
            return sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)
                .Length > 0
                ? OrdinalSegmentCodec.Segment("memory-epoch-v1")
                    + OrdinalSegmentCodec.Segment(sequence.ToString(
                        System.Globalization.CultureInfo.InvariantCulture))
                : string.Empty;
        }

        private static void RunWithTempFile(Action<string> body)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "pawndiary_mem_m1_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                body(path);
            }
            finally
            {
                DeleteQuietly(path);
            }
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void SaveWithScribe(string path, Action expose)
        {
            bool started = false;
            try
            {
                Scribe.saver.InitSaving(path, "root");
                started = true;
                expose();
            }
            finally
            {
                if (started)
                {
                    Scribe.saver.FinalizeSaving();
                }

                Scribe.mode = LoadSaveMode.Inactive;
            }
        }

        private static void LoadVarsWithScribe(string path, Action expose)
        {
            bool started = false;
            try
            {
                Scribe.loader.InitLoading(path);
                started = true;
                Scribe.mode = LoadSaveMode.LoadingVars;
                expose();
            }
            finally
            {
                if (started)
                {
                    Scribe.loader.FinalizeLoading();
                }

                Scribe.mode = LoadSaveMode.Inactive;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new AssertionException(message);
            }
        }
    }
}

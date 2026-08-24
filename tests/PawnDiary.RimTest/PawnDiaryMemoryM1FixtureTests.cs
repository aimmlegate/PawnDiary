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
// - dispatch request/variant/attempt rows round-trip;
// - the raw unresolved-owner wrapper preserves its nested shipped legacy record untouched;
// - the logical-size walker validates a fully populated envelope against the frozen registry.
using System;
using System.Collections.Generic;
using System.IO;
using PawnDiary;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    public static class PawnDiaryMemoryM1FixtureTests
    {
        private const string Label = "mem";

        [Test]
        public static void MemoryEnvelopeCurrentShapeRoundTrips()
        {
            PawnKnowledgeState source = PawnKnowledgeState.CreateCurrent("Pawn_M1");
            source.autobiographicalEpochToken = EpochToken(3);
            source.playerBackground = "Raised on tales of the rim.";
            source.standaloneBlocks.Add(NewBlock("rec-standalone", null));
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
            return new ImportantMemoryRecord
            {
                recordId = "legacy-rec-1",
                dedupKey = "dedup-1",
                sourceEventId = "evt-legacy-1",
                eventKind = "relation.spouse.gained",
                topicKey = "relationship",
                tick = 9876,
                dateLabel = "Aprimday 12"
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

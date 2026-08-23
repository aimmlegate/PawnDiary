// MemorySavedModelsSizing.cs — logical-size walkers for every saved memory row.
//
// Each walker pushes its row's fields to the MemoryLogicalSizeCollector in EXACTLY the same
// declaration order as ExposeData, so the registry-validated walk doubles as an executable
// cross-check of the saved shape (a field added to one but not the other fails sizing/tests).
// Charges come from §T17.5 via the collector: 64-byte per-row framing, 1/4/8-byte scalars,
// 4-byte length prefixes plus exact UTF-8 bytes for strings, presence bytes for nullable
// singletons, and recursive nested rows.
//
// The two RAW legacy wrapper leaves are deliberately NOT registered schema rows: their exact
// current-schema encoding does not exist until migration stamps them (§T6.8), so they charge a
// documented UTF-16-unit estimate through UnregisteredRawRow instead of pretending to be atoms.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    public partial class SavedMemoryStateFact
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemoryStateFact");
            c.Int32("schemaVersion", schemaVersion);
            c.String("factKey", factKey);
            c.String("factValue", factValue);
            c.EndRow();
        }
    }

    public partial class SavedMemorySubjectRef
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemorySubjectRef");
            c.Int32("schemaVersion", schemaVersion);
            c.String("subjectRefId", subjectRefId);
            c.String("subjectKind", subjectKind);
            c.String("subjectId", subjectId);
            c.String("frozenLabel", frozenLabel);
            c.String("roleToken", roleToken);
            c.String("knownnessToken", knownnessToken);
            c.EndRow();
        }
    }

    public partial class SavedMemoryCanonicalFact
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemoryCanonicalFact");
            c.Int32("schemaVersion", schemaVersion);
            c.String("factId", factId);
            c.String("factKind", factKind);
            c.String("canonicalSubjectKind", canonicalSubjectKind);
            c.String("canonicalSubjectId", canonicalSubjectId);
            c.String("aggregationToken", aggregationToken);
            c.String("canonicalValueKind", canonicalValueKind);
            c.String("canonicalValue", canonicalValue);
            c.Boolean("majorTurningPoint", majorTurningPoint);
            c.Boolean("reversal", reversal);
            c.EndRow();
        }
    }

    public partial class SavedMemoryProvenance
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemoryProvenance");
            c.Int32("schemaVersion", schemaVersion);
            c.String("provenanceRefId", provenanceRefId);
            c.String("sourceKindToken", sourceKindToken);
            c.String("sourceOccurrenceId", sourceOccurrenceId);
            c.String("sourceEventId", sourceEventId);
            c.String("captureRuleId", captureRuleId);
            c.String("factDiscriminator", factDiscriminator);
            c.String("integrationToken", integrationToken);
            c.EndRow();
        }
    }

    public partial class SavedMemoryFactContribution
    {
        internal void SizeRefList(MemoryLogicalSizeCollector c, string name, List<string> refs)
        {
            int count = refs == null ? 0 : refs.Count;
            c.ListCount(name, count);
            for (int i = 0; i < count; i++)
            {
                c.ValueListStringElement(refs[i]);
            }
        }

        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemoryFactContribution");
            c.Int32("schemaVersion", schemaVersion);
            c.String("contributionId", contributionId);
            c.String("originChapterId", originChapterId);
            c.String("originRecordId", originRecordId);
            c.Int32("originFactOrdinal", originFactOrdinal);
            c.String("originFactId", originFactId);
            c.Int64("originalEventTick", originalEventTick);
            c.Boolean("ageUnknown", ageUnknown);
            c.String("category", category);
            c.String("importance", importance);
            c.String("canonicalValue", canonicalValue);
            c.Boolean("majorTurningPoint", majorTurningPoint);
            c.Boolean("reversal", reversal);
            c.String("sourceOccurrenceId", sourceOccurrenceId);
            SizeRefList(c, "subjectRefIds", subjectRefIds);
            SizeRefList(c, "provenanceRefIds", provenanceRefIds);
            c.EndRow();
        }
    }

    public partial class SavedMemoryFactBucket
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemoryFactBucket");
            c.Int32("schemaVersion", schemaVersion);
            c.String("bucketKey", bucketKey);
            c.String("factKind", factKind);
            c.String("canonicalSubjectKind", canonicalSubjectKind);
            c.String("canonicalSubjectId", canonicalSubjectId);
            c.String("aggregationToken", aggregationToken);
            c.ListCount("contributions", contributions == null ? 0 : contributions.Count);
            for (int i = 0; contributions != null && i < contributions.Count; i++)
            {
                if (contributions[i] != null)
                {
                    ((IMemoryLogicalSizeSource)contributions[i]).CollectFields(c);
                }
            }

            c.Int32("derivedCount", derivedCount);
            c.String("derivedRangeMin", derivedRangeMin);
            c.String("derivedRangeMax", derivedRangeMax);
            c.Int64("earliestSurvivingTick", earliestSurvivingTick);
            c.Int64("latestSurvivingTick", latestSurvivingTick);
            c.EndRow();
        }
    }

    public partial class SavedMemorySummaryPayload
    {
        private static void SizeRows<T>(
            MemoryLogicalSizeCollector c, string fieldName, List<T> rows)
            where T : class, IMemoryLogicalSizeSource
        {
            int count = rows == null ? 0 : rows.Count;
            c.ListCount(fieldName, count);
            for (int i = 0; rows != null && i < count; i++)
            {
                if (rows[i] != null)
                {
                    c.NestedRow(rows[i]);
                }
            }
        }

        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemorySummaryPayload");
            c.Int32("schemaVersion", schemaVersion);
            c.Int32("reducerRevision", reducerRevision);
            c.Int64("factsRevision", factsRevision);
            c.String("canonicalFactsFingerprint", canonicalFactsFingerprint);
            SizeRows(c, "factBuckets", factBuckets);
            SizeRows(c, "subjectRefs", subjectRefs);
            SizeRows(c, "provenanceRefs", provenanceRefs);
            c.Int32("derivedCategoryMask", derivedCategoryMask);
            c.String("highestSurvivingImportance", highestSurvivingImportance);
            c.Int64("earliestSurvivingTick", earliestSurvivingTick);
            c.Int64("latestSurvivingTick", latestSurvivingTick);
            c.String("deterministicWording", deterministicWording);
            c.String("optionalLlmWording", optionalLlmWording);
            c.String("optionalLlmFingerprint", optionalLlmFingerprint);
            c.Int64("optionalLlmFormatRevision", optionalLlmFormatRevision);
            c.Int32("optionalLlmCategoryMask", optionalLlmCategoryMask);
            c.String("lastSettledWordingFingerprint", lastSettledWordingFingerprint);
            c.Int32("lastSettledWordingReducerRevision", lastSettledWordingReducerRevision);
            c.Int64("lastSettledWordingFormatRevision", lastSettledWordingFormatRevision);
            c.String("lastWordingDispositionToken", lastWordingDispositionToken);
            c.EndRow();
        }
    }

    public partial class SavedMemoryBlock
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemoryBlock");
            c.Int32("schemaVersion", schemaVersion);
            c.String("recordId", recordId);
            c.String("sourceOccurrenceId", sourceOccurrenceId);
            c.String("sourceEventId", sourceEventId);
            c.String("captureRuleId", captureRuleId);
            c.String("factDiscriminator", factDiscriminator);
            c.String("ownerPawnId", ownerPawnId);
            c.String("ownerEpochToken", ownerEpochToken);
            c.String("kind", kind);
            c.String("summaryRole", summaryRole);
            c.String("category", category);
            c.String("importance", importance);
            c.Int64("originalEventTick", originalEventTick);
            c.Boolean("ageUnknown", ageUnknown);
            c.String("rootId", rootId);
            c.String("chapterId", chapterId);
            c.NullablePresence(
                "primarySubject", primarySubject != null);
            if (primarySubject != null)
            {
                c.NestedRow(primarySubject);
            }

            SizeSubjectList(c, secondarySubjects);
            SizeRowList(c, "facts", facts);
            SizeRowList(c, "provenance", provenance);
            c.String("automaticWording", automaticWording);
            c.String("playerWording", playerWording);
            c.Boolean("playerEdited", playerEdited);
            c.Boolean("suppressed", suppressed);
            c.Boolean("requiredLifecycleLandmark", requiredLifecycleLandmark);
            c.Int64("formatRevision", formatRevision);
            c.Int64("lastAutomaticIncludedTick", lastAutomaticIncludedTick);
            c.Int64("lastAutomaticIncludedEntryOrdinal", lastAutomaticIncludedEntryOrdinal);
            c.Int64("automaticInclusionCount", automaticInclusionCount);
            c.String("providerExposureState", providerExposureState);
            c.Int64("lastProviderExposureTick", lastProviderExposureTick);
            c.NullablePresence("summaryPayload", summaryPayload != null);
            if (summaryPayload != null)
            {
                c.NestedRow(summaryPayload);
            }

            c.EndRow();
        }

        private static void SizeSubjectList(
            MemoryLogicalSizeCollector c, List<SavedMemorySubjectRef> rows)
        {
            int count = rows == null ? 0 : rows.Count;
            c.ListCount("secondarySubjects", count);
            for (int i = 0; rows != null && i < count; i++)
            {
                if (rows[i] != null)
                {
                    c.NestedRow(rows[i]);
                }
            }
        }

        private static void SizeRowList<T>(
            MemoryLogicalSizeCollector c, string fieldName, List<T> rows)
            where T : class, IMemoryLogicalSizeSource
        {
            int count = rows == null ? 0 : rows.Count;
            c.ListCount(fieldName, count);
            for (int i = 0; rows != null && i < count; i++)
            {
                if (rows[i] != null)
                {
                    c.NestedRow(rows[i]);
                }
            }
        }
    }

    public partial class SavedMemoryChapter
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemoryChapter");
            c.Int32("schemaVersion", schemaVersion);
            c.String("chapterId", chapterId);
            c.Int64("ordinal", ordinal);
            c.String("phaseToken", phaseToken);
            c.Int64("openedTick", openedTick);
            c.Int64("lastActivityTick", lastActivityTick);
            c.Int64("closedTick", closedTick);
            c.String("closureReasonToken", closureReasonToken);
            c.Boolean("closed", closed);
            c.String("closedSummaryRecordId", closedSummaryRecordId);
            c.EndRow();
        }
    }

    public partial class SavedMemoryThreadRoot
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemoryThreadRoot");
            c.Int32("schemaVersion", schemaVersion);
            c.String("rootId", rootId);
            c.String("ownerPawnId", ownerPawnId);
            c.String("ownerEpochToken", ownerEpochToken);
            c.String("subjectKind", subjectKind);
            c.String("subjectId", subjectId);
            c.String("frozenSubjectLabel", frozenSubjectLabel);
            c.Int64("structuralRevision", structuralRevision);
            c.Int64("statusRevision", statusRevision);
            c.Int32("lastAppliedReducerRevision", lastAppliedReducerRevision);
            c.Int64("nextChapterOrdinal", nextChapterOrdinal);
            c.ListCount("chapters", chapters == null ? 0 : chapters.Count);
            for (int i = 0; chapters != null && i < chapters.Count; i++)
            {
                if (chapters[i] != null)
                {
                    c.NestedRow(chapters[i]);
                }
            }

            c.ListCount("visibleBlocks", visibleBlocks == null ? 0 : visibleBlocks.Count);
            for (int i = 0; visibleBlocks != null && i < visibleBlocks.Count; i++)
            {
                if (visibleBlocks[i] != null)
                {
                    c.NestedRow(visibleBlocks[i]);
                }
            }

            c.NullablePresence("rollingSummaryBlock", rollingSummaryBlock != null);
            if (rollingSummaryBlock != null)
            {
                c.NestedRow(rollingSummaryBlock);
            }

            c.EndRow();
        }
    }

    public partial class SavedMemoryAwarenessSnapshot
    {
        private static void SizeFacts(
            MemoryLogicalSizeCollector c, string fieldName, List<SavedMemoryStateFact> rows)
        {
            int count = rows == null ? 0 : rows.Count;
            c.ListCount(fieldName, count);
            for (int i = 0; rows != null && i < count; i++)
            {
                if (rows[i] != null)
                {
                    c.NestedRow(rows[i]);
                }
            }
        }

        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemoryAwarenessSnapshot");
            c.Int32("schemaVersion", schemaVersion);
            c.String("snapshotId", snapshotId);
            c.String("scopeKindToken", scopeKindToken);
            c.String("subjectKind", subjectKind);
            c.String("subjectId", subjectId);
            c.String("factStreamToken", factStreamToken);
            c.Int64("captureInvalidationGeneration", captureInvalidationGeneration);
            c.String("knownnessEvidenceToken", knownnessEvidenceToken);
            SizeFacts(c, "stateFacts", stateFacts);
            c.Int64("firstObservedTick", firstObservedTick);
            c.Int64("lastObservedTick", lastObservedTick);
            c.String("lastSourceOccurrenceId", lastSourceOccurrenceId);
            c.String("trackingStateToken", trackingStateToken);
            c.Int64("snapshotRevision", snapshotRevision);
            c.EndRow();
        }
    }

    public partial class SavedMemoryCaptureEpisode
    {
        private static void SizeFacts(
            MemoryLogicalSizeCollector c, string fieldName, List<SavedMemoryStateFact> rows)
        {
            int count = rows == null ? 0 : rows.Count;
            c.ListCount(fieldName, count);
            for (int i = 0; rows != null && i < count; i++)
            {
                if (rows[i] != null)
                {
                    c.NestedRow(rows[i]);
                }
            }
        }

        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemoryCaptureEpisode");
            c.Int32("schemaVersion", schemaVersion);
            c.String("episodeId", episodeId);
            c.String("captureRuleId", captureRuleId);
            c.String("scopeKindToken", scopeKindToken);
            c.String("factStreamToken", factStreamToken);
            c.String("category", category);
            c.Int64("captureInvalidationGeneration", captureInvalidationGeneration);
            c.String("episodeKindToken", episodeKindToken);
            c.String("subjectKind", subjectKind);
            c.String("subjectId", subjectId);
            c.String("pairOrStreamKey", pairOrStreamKey);
            c.String("directionToken", directionToken);
            SizeFacts(c, "baselineFacts", baselineFacts);
            SizeFacts(c, "currentFacts", currentFacts);
            c.Int64("firstObservedTick", firstObservedTick);
            c.Int64("lastObservedTick", lastObservedTick);
            c.String("lastSourceOccurrenceId", lastSourceOccurrenceId);
            c.Int64("episodeRevision", episodeRevision);
            c.EndRow();
        }
    }

    public partial class SavedGlobalFactionSnapshot
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedGlobalFactionSnapshot");
            c.Int32("schemaVersion", schemaVersion);
            c.String("factionInstanceId", factionInstanceId);
            c.Int64("allocatorGeneration", allocatorGeneration);
            c.String("factionDefName", factionDefName);
            c.String("frozenDisplayLabel", frozenDisplayLabel);
            c.Int32("goodwill", goodwill);
            c.String("relationKindToken", relationKindToken);
            c.String("leaderPawnId", leaderPawnId);
            c.Boolean("defeated", defeated);
            c.Boolean("removed", removed);
            c.Int64("observedTick", observedTick);
            c.String("trackingStateToken", trackingStateToken);
            c.Int64("snapshotRevision", snapshotRevision);
            c.EndRow();
        }
    }

    public partial class SavedMemoryAppliedPolicyStateV1
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemoryAppliedPolicyStateV1");
            c.Int32("schemaVersion", schemaVersion);
            c.Boolean("saveNewMemories", saveNewMemories);
            c.Boolean("useMemoriesInWriting", useMemoriesInWriting);
            c.Boolean("usePawnBackground", usePawnBackground);
            c.Boolean("allowExtraMemoryAiRequests", allowExtraMemoryAiRequests);
            c.Boolean("occasionalMemoryReflections", occasionalMemoryReflections);
            c.Int32("memoryCategoryMask", memoryCategoryMask);
            c.Int64("captureInvalidationGenerationPersonal",
                captureInvalidationGenerationPersonal);
            c.Int64("captureInvalidationGenerationRelationships",
                captureInvalidationGenerationRelationships);
            c.Int64("captureInvalidationGenerationFamily",
                captureInvalidationGenerationFamily);
            c.Int64("captureInvalidationGenerationFactions",
                captureInvalidationGenerationFactions);
            c.Int64("optionalRequestInvalidationGeneration",
                optionalRequestInvalidationGeneration);
            c.Int32("minorMemoryLifetimeDays", minorMemoryLifetimeDays);
            c.Int32("regularMemoryLifetimeDays", regularMemoryLifetimeDays);
            c.Int32("memoryThreadTarget", memoryThreadTarget);
            c.Int32("memoryReuseDays", memoryReuseDays);
            c.Int32("memoryRevisitEntryCount", memoryRevisitEntryCount);
            c.EndRow();
        }
    }

    public partial class SavedSummaryWordingOpportunityV1
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedSummaryWordingOpportunityV1");
            c.Int32("schemaVersion", schemaVersion);
            c.String("ownerPawnId", ownerPawnId);
            c.String("ownerEpochToken", ownerEpochToken);
            c.Int64("ownerCancellationGeneration", ownerCancellationGeneration);
            c.Int64("globalCancellationGeneration", globalCancellationGeneration);
            c.Int64("optionalRequestInvalidationGeneration",
                optionalRequestInvalidationGeneration);
            c.String("rootId", rootId);
            c.String("summaryRecordId", summaryRecordId);
            c.Int64("expectedRootStructuralRevision", expectedRootStructuralRevision);
            c.Int64("expectedSummaryFactsRevision", expectedSummaryFactsRevision);
            c.Int32("expectedReducerRevision", expectedReducerRevision);
            c.Int64("expectedFormatRevision", expectedFormatRevision);
            c.Int32("expectedCategoryMask", expectedCategoryMask);
            c.String("projectionFingerprint", projectionFingerprint);
            c.Int64("requestedTick", requestedTick);
            c.Int64("dueTick", dueTick);
            c.Int64("expiryTick", expiryTick);
            c.Int32("configuredPriority", configuredPriority);
            c.Int32("salience", salience);
            c.String("opportunityKey", opportunityKey);
            c.EndRow();
        }
    }

    public partial class SavedMemoryDiagnosticCounter
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemoryDiagnosticCounter");
            c.Int32("schemaVersion", schemaVersion);
            c.String("reasonToken", reasonToken);
            c.String("scopeToken", scopeToken);
            c.Int64("saturatedCount", saturatedCount);
            c.EndRow();
        }
    }

    public partial class SavedMemoryAttemptAuditRow
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemoryAttemptAuditRow");
            c.Int32("schemaVersion", schemaVersion);
            c.String("logicalRequestId", logicalRequestId);
            c.String("requestPurposeToken", requestPurposeToken);
            c.String("ownerPawnId", ownerPawnId);
            c.String("ownerEpochToken", ownerEpochToken);
            c.Int32("attemptOrdinal", attemptOrdinal);
            c.String("variantKey", variantKey);
            c.Int64("invocationTick", invocationTick);
            c.Int64("terminalTick", terminalTick);
            c.String("outcomeToken", outcomeToken);
            c.Boolean("potentialExposure", potentialExposure);
            c.EndRow();
        }
    }

    public partial class SavedLegacyOwnerEpochReservation
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedLegacyOwnerEpochReservation");
            c.Int32("schemaVersion", schemaVersion);
            c.String("ownerPawnId", ownerPawnId);
            c.Int64("reservedEpochSequence", reservedEpochSequence);
            c.EndRow();
        }
    }

    public partial class SavedMemoryRepetitionGuardRow
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedMemoryRepetitionGuardRow");
            c.Int32("schemaVersion", schemaVersion);
            c.String("ownerEpochToken", ownerEpochToken);
            c.String("guardKind", guardKind);
            c.String("guardKey", guardKey);
            c.Int64("lastAutomaticIncludedTick", lastAutomaticIncludedTick);
            c.Int64("lastAutomaticIncludedEntryOrdinal", lastAutomaticIncludedEntryOrdinal);
            c.Int64("automaticInclusionCount", automaticInclusionCount);
            c.String("lastSourceOccurrenceId", lastSourceOccurrenceId);
            c.String("lastCommittedLogicalRequestId", lastCommittedLogicalRequestId);
            c.String("lastCommittedEvidenceSetFingerprint",
                lastCommittedEvidenceSetFingerprint);
            c.EndRow();
        }
    }

    public partial class SavedImportedSummaryContributionEvidenceV1
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedImportedSummaryContributionEvidenceV1");
            c.Int32("schemaVersion", schemaVersion);
            c.String("contributionId", contributionId);
            c.String("originChapterId", originChapterId);
            c.String("originRecordId", originRecordId);
            c.Int32("originFactOrdinal", originFactOrdinal);
            c.String("originFactId", originFactId);
            c.Int64("originalEventTick", originalEventTick);
            c.Boolean("ageUnknown", ageUnknown);
            c.String("category", category);
            c.String("importance", importance);
            c.String("canonicalValue", canonicalValue);
            c.Boolean("majorTurningPoint", majorTurningPoint);
            c.Boolean("reversal", reversal);
            c.String("sourceOccurrenceId", sourceOccurrenceId);
            SizeValueList(c, "subjectRefIds", subjectRefIds);
            SizeValueList(c, "provenanceRefIds", provenanceRefIds);
            c.EndRow();
        }

        private static void SizeValueList(
            MemoryLogicalSizeCollector c, string fieldName, List<string> values)
        {
            int count = values == null ? 0 : values.Count;
            c.ListCount(fieldName, count);
            for (int i = 0; values != null && i < count; i++)
            {
                c.ValueListStringElement(values[i]);
            }
        }
    }

    public partial class SavedImportedMemoryRow
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedImportedMemoryRow");
            c.Int32("schemaVersion", schemaVersion);
            c.String("archiveRecordId", archiveRecordId);
            c.String("savedOwnerIdentityKindToken", savedOwnerIdentityKindToken);
            c.String("savedOwnerIdentityValue", savedOwnerIdentityValue);
            c.Int64("reattributionGeneration", reattributionGeneration);
            c.String("originalRecordId", originalRecordId);
            c.String("sourceOccurrenceId", sourceOccurrenceId);
            c.String("sourceEventId", sourceEventId);
            c.Int64("originalEventTick", originalEventTick);
            c.Boolean("ageUnknown", ageUnknown);
            c.String("importedWording", importedWording);
            c.String("originalKindToken", originalKindToken);
            c.String("originalSummaryRoleToken", originalSummaryRoleToken);
            c.String("originalCategoryToken", originalCategoryToken);
            c.String("originalImportanceToken", originalImportanceToken);
            c.String("routePolicyToken", routePolicyToken);
            c.NullablePresence("primarySubject", primarySubject != null);
            if (primarySubject != null)
            {
                c.NestedRow(primarySubject);
            }

            int secondary = secondarySubjects == null ? 0 : secondarySubjects.Count;
            c.ListCount("secondarySubjects", secondary);
            for (int i = 0; secondarySubjects != null && i < secondary; i++)
            {
                if (secondarySubjects[i] != null)
                {
                    c.NestedRow(secondarySubjects[i]);
                }
            }

            int factCount = canonicalFacts == null ? 0 : canonicalFacts.Count;
            c.ListCount("canonicalFacts", factCount);
            for (int i = 0; canonicalFacts != null && i < factCount; i++)
            {
                if (canonicalFacts[i] != null)
                {
                    c.NestedRow(canonicalFacts[i]);
                }
            }

            int provCount = provenance == null ? 0 : provenance.Count;
            c.ListCount("provenance", provCount);
            for (int i = 0; provenance != null && i < provCount; i++)
            {
                if (provenance[i] != null)
                {
                    c.NestedRow(provenance[i]);
                }
            }

            c.NullablePresence(
                "summaryContributionEvidence", summaryContributionEvidence != null);
            if (summaryContributionEvidence != null)
            {
                c.NestedRow(summaryContributionEvidence);
            }

            c.String("sourceTypeToken", sourceTypeToken);
            c.String("conflictFingerprint", conflictFingerprint);
            c.Int64("overflowRowCount", overflowRowCount);
            c.Int64("overflowLogicalBytes", overflowLogicalBytes);
            int diagnosticCount = diagnosticTokens == null ? 0 : diagnosticTokens.Count;
            c.ListCount("diagnosticTokens", diagnosticCount);
            for (int i = 0; diagnosticTokens != null && i < diagnosticCount; i++)
            {
                c.ValueListStringElement(diagnosticTokens[i]);
            }

            c.String("migrationReasonToken", migrationReasonToken);
            c.EndRow();
        }
    }

    public partial class SavedLegacyUnresolvedOwnerArchiveInputV1
    {
        /// <summary>
        /// Raw pre-schema leaves keep their legacy encoding until migration stamps them (§T6.8),
        /// so this wrapper charges the nested ImportantMemoryRecord as measured UTF-16 units via
        /// the collector's unregistered-raw-row escape instead of fake schema atoms.
        /// </summary>
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedLegacyUnresolvedOwnerArchiveInputV1");
            c.Int32("schemaVersion", schemaVersion);
            c.String("savedOwnerIdentityKindToken", savedOwnerIdentityKindToken);
            c.String("savedOwnerIdentityValue", savedOwnerIdentityValue);
            c.Int32("sourceContainerOrdinal", sourceContainerOrdinal);
            c.Int32("sourceRecordOrdinal", sourceRecordOrdinal);
            c.NullablePresence("legacyRecord", legacyRecord != null);
            if (legacyRecord != null)
            {
                c.UnregisteredRawRow(LegacyRecordUtf16Units(legacyRecord));
            }

            c.EndRow();
        }

        internal static int LegacyRecordUtf16Units(ImportantMemoryRecord record)
        {
            if (record == null)
            {
                return 0;
            }

            int units = (record.recordId ?? string.Empty).Length
                + (record.dedupKey ?? string.Empty).Length
                + (record.sourceEventId ?? string.Empty).Length
                + (record.sourceKind ?? string.Empty).Length
                + (record.recallScope ?? string.Empty).Length
                + (record.eventKind ?? string.Empty).Length
                + (record.topicKey ?? string.Empty).Length
                + (record.dateLabel ?? string.Empty).Length
                + (record.fallbackSummary ?? string.Empty).Length
                + (record.manualTextOverride ?? string.Empty).Length;
            units += SumUnits(record.participantIds) + SumUnits(record.participantNames)
                + SumUnits(record.subjectKeys) + SumUnits(record.factKeys)
                + SumUnits(record.factValues);
            return units;
        }

        private static int SumUnits(List<string> values)
        {
            if (values == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < values.Count; i++)
            {
                total += values[i] == null ? 0 : values[i].Length;
            }

            return total;
        }
    }

    public partial class SavedFrozenEvidenceEntryV1
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedFrozenEvidenceEntryV1");
            c.Int32("schemaVersion", schemaVersion);
            c.String("recordId", recordId);
            c.String("sourceOccurrenceId", sourceOccurrenceId);
            c.String("rootIdOrEmpty", rootIdOrEmpty);
            c.EndRow();
        }
    }

    public partial class SavedFrozenGuardEntryV1
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedFrozenGuardEntryV1");
            c.Int32("schemaVersion", schemaVersion);
            c.String("guardKind", guardKind);
            c.String("guardKey", guardKey);
            c.EndRow();
        }
    }

    public partial class SavedFrozenDiagnosticProvenanceV1
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedFrozenDiagnosticProvenanceV1");
            c.Int32("schemaVersion", schemaVersion);
            c.String("provenanceKindToken", provenanceKindToken);
            c.String("sourceId", sourceId);
            c.String("recordIdOrEmpty", recordIdOrEmpty);
            c.String("sourceOccurrenceIdOrEmpty", sourceOccurrenceIdOrEmpty);
            c.String("rootIdOrEmpty", rootIdOrEmpty);
            c.Int32("lineOrdinal", lineOrdinal);
            c.EndRow();
        }
    }

    public partial class SavedFrozenEvidenceReceiptPlanV1
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedFrozenEvidenceReceiptPlanV1");
            c.Int32("schemaVersion", schemaVersion);
            c.String("evidenceSetFingerprint", evidenceSetFingerprint);
            c.ListCount("evidenceEntries", evidenceEntries == null ? 0 : evidenceEntries.Count);
            for (int i = 0; evidenceEntries != null && i < evidenceEntries.Count; i++)
            {
                if (evidenceEntries[i] != null)
                {
                    c.NestedRow(evidenceEntries[i]);
                }
            }

            c.ListCount("guardEntries", guardEntries == null ? 0 : guardEntries.Count);
            for (int i = 0; guardEntries != null && i < guardEntries.Count; i++)
            {
                if (guardEntries[i] != null)
                {
                    c.NestedRow(guardEntries[i]);
                }
            }

            c.EndRow();
        }
    }

    public partial class SavedFrozenPromptVariantV1
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedFrozenPromptVariantV1");
            c.Int32("schemaVersion", schemaVersion);
            c.Int32("variantOrdinal", variantOrdinal);
            c.String("variantKey", variantKey);
            c.String("templateIdentity", templateIdentity);
            c.String("contextDetailIdentity", contextDetailIdentity);
            c.String("systemPrompt", systemPrompt);
            c.String("userPrompt", userPrompt);
            c.NullablePresence("receiptPlan", receiptPlan != null);
            if (receiptPlan != null)
            {
                c.NestedRow(receiptPlan);
            }

            int diagnostics = diagnosticProvenance == null ? 0 : diagnosticProvenance.Count;
            c.ListCount("diagnosticProvenance", diagnostics);
            for (int i = 0; diagnosticProvenance != null && i < diagnostics; i++)
            {
                if (diagnosticProvenance[i] != null)
                {
                    c.NestedRow(diagnosticProvenance[i]);
                }
            }

            c.EndRow();
        }
    }

    public partial class SavedActiveLogicalAttemptV1
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedActiveLogicalAttemptV1");
            c.Int32("schemaVersion", schemaVersion);
            c.Int32("attemptOrdinal", attemptOrdinal);
            c.String("variantKey", variantKey);
            c.String("attemptOriginToken", attemptOriginToken);
            c.Int32("predecessorAttemptOrdinal", predecessorAttemptOrdinal);
            c.String("attemptStateToken", attemptStateToken);
            c.Int64("invocationSequence", invocationSequence);
            c.Int64("invocationTick", invocationTick);
            c.Int64("terminalTick", terminalTick);
            c.String("terminalOutcomeToken", terminalOutcomeToken);
            c.Boolean("potentialExposureApplied", potentialExposureApplied);
            c.Boolean("narrativeUseApplied", narrativeUseApplied);
            c.Boolean("resultApplied", resultApplied);
            c.EndRow();
        }
    }

    public partial class SavedActiveLogicalRequestV1
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("SavedActiveLogicalRequestV1");
            c.Int32("schemaVersion", schemaVersion);
            c.Int64("logicalRequestSequence", logicalRequestSequence);
            c.String("logicalRequestId", logicalRequestId);
            c.String("logicalRequestKey", logicalRequestKey);
            c.String("requestPurposeToken", requestPurposeToken);
            c.Int64("sessionId", sessionId);
            c.String("eventIdOrOpportunityKey", eventIdOrOpportunityKey);
            c.String("povRoleToken", povRoleToken);
            c.String("ownerPawnId", ownerPawnId);
            c.String("ownerEpochToken", ownerEpochToken);
            c.String("evidenceEpochToken", evidenceEpochToken);
            c.Int64("ownerCancellationGeneration", ownerCancellationGeneration);
            c.Int64("globalCancellationGeneration", globalCancellationGeneration);
            c.Int64("optionalRequestInvalidationGeneration",
                optionalRequestInvalidationGeneration);
            c.String("requestStateToken", requestStateToken);
            c.Int32("lastIssuedAttemptOrdinal", lastIssuedAttemptOrdinal);
            c.Int32("narrativeUseWinnerAttemptOrdinal", narrativeUseWinnerAttemptOrdinal);
            c.String("narrativeUseWinnerVariantKey", narrativeUseWinnerVariantKey);
            c.ListCount("frozenVariants", frozenVariants == null ? 0 : frozenVariants.Count);
            for (int i = 0; frozenVariants != null && i < frozenVariants.Count; i++)
            {
                if (frozenVariants[i] != null)
                {
                    c.NestedRow(frozenVariants[i]);
                }
            }

            c.ListCount("activeAttempts", activeAttempts == null ? 0 : activeAttempts.Count);
            for (int i = 0; activeAttempts != null && i < activeAttempts.Count; i++)
            {
                if (activeAttempts[i] != null)
                {
                    c.NestedRow(activeAttempts[i]);
                }
            }

            int evidenceCount =
                reservedEvidenceEntries == null ? 0 : reservedEvidenceEntries.Count;
            c.ListCount("reservedEvidenceEntries", evidenceCount);
            for (int i = 0; reservedEvidenceEntries != null && i < evidenceCount; i++)
            {
                if (reservedEvidenceEntries[i] != null)
                {
                    c.NestedRow(reservedEvidenceEntries[i]);
                }
            }

            int guardCount = reservedGuardEntries == null ? 0 : reservedGuardEntries.Count;
            c.ListCount("reservedGuardEntries", guardCount);
            for (int i = 0; reservedGuardEntries != null && i < guardCount; i++)
            {
                if (reservedGuardEntries[i] != null)
                {
                    c.NestedRow(reservedGuardEntries[i]);
                }
            }

            c.EndRow();
        }
    }

    public partial class PawnKnowledgeState : IExposable, IMemoryLogicalSizeSource
    {
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            // NOTE: the envelope is sized as "PawnKnowledgeState" minus its culture fields, which
            // belong to the pre-memory feature and stay outside every memory byte budget (§T6.1).
            // The registry therefore has no envelope row; this walker sizes ONLY the memory-owned
            // fields under a synthetic framing row so budgets never double-count culture state.
            c.BeginRow("PawnKnowledgeState");
            c.Int32("schemaVersion", schemaVersion);
            c.String("pawnId", pawnId);
            c.String("autobiographicalEpochToken", autobiographicalEpochToken);
            c.Boolean("archiveOnly", archiveOnly);
            c.Boolean("epochFenceOnly", epochFenceOnly);
            c.Int64("requestCancellationGeneration", requestCancellationGeneration);
            c.Int64("structuralRevision", structuralRevision);
            c.Int64("statusRevision", statusRevision);
            c.Int64("completedDiaryEntryOrdinal", completedDiaryEntryOrdinal);
            SizeBlocks(c, "standaloneBlocks", standaloneBlocks);
            SizeRoots(c, threadRoots);
            c.String("playerBackground", playerBackground);
            SizeAwareness(c, ownerAwarenessSnapshots);
            SizeEpisodes(c, openCaptureEpisodes);
            SizeGuards(c, repetitionGuardRows);
            SizeArchive(c, importedArchiveRows);
            c.Int64("migrationDiagnosticFlags", migrationDiagnosticFlags);
            c.EndRow();
        }

        private static void SizeBlocks(
            MemoryLogicalSizeCollector c, string fieldName, List<SavedMemoryBlock> rows)
        {
            int count = rows == null ? 0 : rows.Count;
            c.ListCount(fieldName, count);
            for (int i = 0; rows != null && i < count; i++)
            {
                if (rows[i] != null)
                {
                    c.NestedRow(rows[i]);
                }
            }
        }

        private static void SizeRoots(
            MemoryLogicalSizeCollector c, List<SavedMemoryThreadRoot> rows)
        {
            int count = rows == null ? 0 : rows.Count;
            c.ListCount("threadRoots", count);
            for (int i = 0; rows != null && i < count; i++)
            {
                if (rows[i] != null)
                {
                    c.NestedRow(rows[i]);
                }
            }
        }

        private static void SizeAwareness(
            MemoryLogicalSizeCollector c, List<SavedMemoryAwarenessSnapshot> rows)
        {
            int count = rows == null ? 0 : rows.Count;
            c.ListCount("ownerAwarenessSnapshots", count);
            for (int i = 0; rows != null && i < count; i++)
            {
                if (rows[i] != null)
                {
                    c.NestedRow(rows[i]);
                }
            }
        }

        private static void SizeEpisodes(
            MemoryLogicalSizeCollector c, List<SavedMemoryCaptureEpisode> rows)
        {
            int count = rows == null ? 0 : rows.Count;
            c.ListCount("openCaptureEpisodes", count);
            for (int i = 0; rows != null && i < count; i++)
            {
                if (rows[i] != null)
                {
                    c.NestedRow(rows[i]);
                }
            }
        }

        private static void SizeGuards(
            MemoryLogicalSizeCollector c, List<SavedMemoryRepetitionGuardRow> rows)
        {
            int count = rows == null ? 0 : rows.Count;
            c.ListCount("repetitionGuardRows", count);
            for (int i = 0; rows != null && i < count; i++)
            {
                if (rows[i] != null)
                {
                    c.NestedRow(rows[i]);
                }
            }
        }

        private static void SizeArchive(
            MemoryLogicalSizeCollector c, List<SavedImportedMemoryRow> rows)
        {
            int count = rows == null ? 0 : rows.Count;
            c.ListCount("importedArchiveRows", count);
            for (int i = 0; rows != null && i < count; i++)
            {
                if (rows[i] != null)
                {
                    c.NestedRow(rows[i]);
                }
            }
        }
    }
}

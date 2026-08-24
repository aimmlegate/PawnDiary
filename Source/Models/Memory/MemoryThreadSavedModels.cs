// MemoryThreadSavedModels.cs — the saved Event/Landmark/Summary thread rows
// (design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md §T6.2–T6.7).
//
// These classes are ordinary IExposable data bags: explicit stable Scribe tokens, null-safe shape
// repair, and no policy, live Pawn/Def references, UI, or transport logic. Field names and order
// mirror the frozen M0 payload catalog exactly; MemorySavedScalarSchema is the executable registry
// of every field declared here. Semantic decisions (TTL, reduction, eligibility) stay in pure
// pipeline helpers — this file only stores and heals shapes.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable"): ExposeData runs for BOTH save and load;
// Scribe_* mirrors each field to XML. Missing XML keys fall back to the defaults shown below.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One bounded canonical key/value row inside an awareness snapshot or capture episode
    /// (§T6.1.1). The plan names this row <c>SavedMemoryStateFactV1</c>; the frozen M0 payload
    /// catalog registers the shorter corpus name <c>SavedMemoryStateFact</c>, which wins because
    /// the catalog is the verified atom registry. Serialized tokens are unaffected either way.
    /// </summary>
    public partial class SavedMemoryStateFact : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string factKey = string.Empty;
        public string factValue = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref factKey, "factKey", string.Empty);
            Scribe_Values.Look(ref factValue, "factValue", string.Empty);
        }

        /// <summary>Null-safe shape repair only; never chooses semantic values.</summary>
        public void Normalize()
        {
            factKey = factKey ?? string.Empty;
            factValue = factValue ?? string.Empty;
        }
    }

    /// <summary>One typed exact subject reference on a block or Summary payload (§T6.5).</summary>
    public partial class SavedMemorySubjectRef : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string subjectRefId = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        /// <summary>Presentation only; never identity or lookup.</summary>
        public string frozenLabel = string.Empty;
        public string roleToken = string.Empty;
        /// <summary>Knownness captured at the event boundary (direct/captured/existing_news).</summary>
        public string knownnessToken = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref subjectRefId, "subjectRefId", string.Empty);
            Scribe_Values.Look(ref subjectKind, "subjectKind", string.Empty);
            Scribe_Values.Look(ref subjectId, "subjectId", string.Empty);
            Scribe_Values.Look(ref frozenLabel, "frozenLabel", string.Empty);
            Scribe_Values.Look(ref roleToken, "roleToken", string.Empty);
            Scribe_Values.Look(ref knownnessToken, "knownnessToken", string.Empty);
        }

        public void Normalize()
        {
            subjectRefId = subjectRefId ?? string.Empty;
            subjectKind = subjectKind ?? string.Empty;
            subjectId = subjectId ?? string.Empty;
            frozenLabel = frozenLabel ?? string.Empty;
            roleToken = roleToken ?? string.Empty;
            knownnessToken = knownnessToken ?? string.Empty;
        }
    }

    /// <summary>One immutable canonical fact on an Event/Landmark block (§T6.5).</summary>
    public partial class SavedMemoryCanonicalFact : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string factId = string.Empty;
        public string factKind = string.Empty;
        public string canonicalSubjectKind = string.Empty;
        public string canonicalSubjectId = string.Empty;
        public string aggregationToken = string.Empty;
        public string canonicalValueKind = string.Empty;
        public string canonicalValue = string.Empty;
        public bool majorTurningPoint;
        public bool reversal;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref factId, "factId", string.Empty);
            Scribe_Values.Look(ref factKind, "factKind", string.Empty);
            Scribe_Values.Look(ref canonicalSubjectKind, "canonicalSubjectKind", string.Empty);
            Scribe_Values.Look(ref canonicalSubjectId, "canonicalSubjectId", string.Empty);
            Scribe_Values.Look(ref aggregationToken, "aggregationToken", string.Empty);
            Scribe_Values.Look(ref canonicalValueKind, "canonicalValueKind", string.Empty);
            Scribe_Values.Look(ref canonicalValue, "canonicalValue", string.Empty);
            Scribe_Values.Look(ref majorTurningPoint, "majorTurningPoint", false);
            Scribe_Values.Look(ref reversal, "reversal", false);
        }

        public void Normalize()
        {
            factId = factId ?? string.Empty;
            factKind = factKind ?? string.Empty;
            canonicalSubjectKind = canonicalSubjectKind ?? string.Empty;
            canonicalSubjectId = canonicalSubjectId ?? string.Empty;
            aggregationToken = aggregationToken ?? string.Empty;
            canonicalValueKind = canonicalValueKind ?? string.Empty;
            canonicalValue = canonicalValue ?? string.Empty;
        }
    }

    /// <summary>One provenance row on an Event/Landmark block or Summary payload (§T6.5).</summary>
    public partial class SavedMemoryProvenance : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string provenanceRefId = string.Empty;
        /// <summary>Exactly diary_event | capture_signal | legacy_migration | integration.</summary>
        public string sourceKindToken = string.Empty;
        public string sourceOccurrenceId = string.Empty;
        public string sourceEventId = string.Empty;
        public string captureRuleId = string.Empty;
        public string factDiscriminator = string.Empty;
        public string integrationToken = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref provenanceRefId, "provenanceRefId", string.Empty);
            Scribe_Values.Look(ref sourceKindToken, "sourceKindToken", string.Empty);
            Scribe_Values.Look(ref sourceOccurrenceId, "sourceOccurrenceId", string.Empty);
            Scribe_Values.Look(ref sourceEventId, "sourceEventId", string.Empty);
            Scribe_Values.Look(ref captureRuleId, "captureRuleId", string.Empty);
            Scribe_Values.Look(ref factDiscriminator, "factDiscriminator", string.Empty);
            Scribe_Values.Look(ref integrationToken, "integrationToken", string.Empty);
        }

        public void Normalize()
        {
            provenanceRefId = provenanceRefId ?? string.Empty;
            sourceKindToken = sourceKindToken ?? string.Empty;
            sourceOccurrenceId = sourceOccurrenceId ?? string.Empty;
            sourceEventId = sourceEventId ?? string.Empty;
            captureRuleId = captureRuleId ?? string.Empty;
            factDiscriminator = factDiscriminator ?? string.Empty;
            integrationToken = integrationToken ?? string.Empty;
        }
    }

    /// <summary>One dated contribution inside a Summary fact bucket (§T6.7).</summary>
    public partial class SavedMemoryFactContribution : IExposable, IMemoryLogicalSizeSource
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
        /// <summary>Current-schema string value lists into the payload's subject/provenance tables.</summary>
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

    /// <summary>One aggregation bucket of a Summary payload (§T6.7).</summary>
    public partial class SavedMemoryFactBucket : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string bucketKey = string.Empty;
        public string factKind = string.Empty;
        public string canonicalSubjectKind = string.Empty;
        public string canonicalSubjectId = string.Empty;
        public string aggregationToken = string.Empty;
        public List<SavedMemoryFactContribution> contributions =
            new List<SavedMemoryFactContribution>();
        public int derivedCount;
        /// <summary>Empty for every non-int64_range token (§T6.7).</summary>
        public string derivedRangeMin = string.Empty;
        public string derivedRangeMax = string.Empty;
        public long earliestSurvivingTick;
        public long latestSurvivingTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref bucketKey, "bucketKey", string.Empty);
            Scribe_Values.Look(ref factKind, "factKind", string.Empty);
            Scribe_Values.Look(ref canonicalSubjectKind, "canonicalSubjectKind", string.Empty);
            Scribe_Values.Look(ref canonicalSubjectId, "canonicalSubjectId", string.Empty);
            Scribe_Values.Look(ref aggregationToken, "aggregationToken", string.Empty);
            Scribe_Collections.Look(ref contributions, "contributions", LookMode.Deep);
            Scribe_Values.Look(ref derivedCount, "derivedCount", 0);
            Scribe_Values.Look(ref derivedRangeMin, "derivedRangeMin", string.Empty);
            Scribe_Values.Look(ref derivedRangeMax, "derivedRangeMax", string.Empty);
            Scribe_Values.Look(ref earliestSurvivingTick, "earliestSurvivingTick", 0);
            Scribe_Values.Look(ref latestSurvivingTick, "latestSurvivingTick", 0);
        }

        public void Normalize()
        {
            bucketKey = bucketKey ?? string.Empty;
            factKind = factKind ?? string.Empty;
            canonicalSubjectKind = canonicalSubjectKind ?? string.Empty;
            canonicalSubjectId = canonicalSubjectId ?? string.Empty;
            aggregationToken = aggregationToken ?? string.Empty;
            derivedRangeMin = derivedRangeMin ?? string.Empty;
            derivedRangeMax = derivedRangeMax ?? string.Empty;
            contributions = contributions ?? new List<SavedMemoryFactContribution>();
            for (int i = contributions.Count - 1; i >= 0; i--)
            {
                if (contributions[i] == null)
                {
                    contributions.RemoveAt(i);
                    continue;
                }

                contributions[i].Normalize();
            }
        }
    }

    /// <summary>The sole canonical structured source of one Summary block (§T6.6).</summary>
    public partial class SavedMemorySummaryPayload : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public int reducerRevision;
        public long factsRevision;
        public string canonicalFactsFingerprint = string.Empty;
        public List<SavedMemoryFactBucket> factBuckets = new List<SavedMemoryFactBucket>();
        public List<SavedMemorySubjectRef> subjectRefs = new List<SavedMemorySubjectRef>();
        public List<SavedMemoryProvenance> provenanceRefs = new List<SavedMemoryProvenance>();
        /// <summary>Only the four known category low bits may be set.</summary>
        public int derivedCategoryMask;
        public string highestSurvivingImportance = string.Empty;
        public long earliestSurvivingTick;
        public long latestSurvivingTick;
        public string deterministicWording = string.Empty;
        /// <summary>Erasable optional LLM prose; it can never add facts or identity.</summary>
        public string optionalLlmWording = string.Empty;
        public string optionalLlmFingerprint = string.Empty;
        public long optionalLlmFormatRevision;
        public int optionalLlmCategoryMask;
        public string lastSettledWordingFingerprint = string.Empty;
        public int lastSettledWordingReducerRevision;
        public long lastSettledWordingFormatRevision;
        /// <summary>none|pending|activated|success|failed|malformed|expired|displaced|disabled.</summary>
        public string lastWordingDispositionToken = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref reducerRevision, "reducerRevision", 0);
            Scribe_Values.Look(ref factsRevision, "factsRevision", 0);
            Scribe_Values.Look(ref canonicalFactsFingerprint, "canonicalFactsFingerprint", string.Empty);
            Scribe_Collections.Look(ref factBuckets, "factBuckets", LookMode.Deep);
            Scribe_Collections.Look(ref subjectRefs, "subjectRefs", LookMode.Deep);
            Scribe_Collections.Look(ref provenanceRefs, "provenanceRefs", LookMode.Deep);
            Scribe_Values.Look(ref derivedCategoryMask, "derivedCategoryMask", 0);
            Scribe_Values.Look(ref highestSurvivingImportance, "highestSurvivingImportance", string.Empty);
            Scribe_Values.Look(ref earliestSurvivingTick, "earliestSurvivingTick", 0);
            Scribe_Values.Look(ref latestSurvivingTick, "latestSurvivingTick", 0);
            Scribe_Values.Look(ref deterministicWording, "deterministicWording", string.Empty);
            Scribe_Values.Look(ref optionalLlmWording, "optionalLlmWording", string.Empty);
            Scribe_Values.Look(ref optionalLlmFingerprint, "optionalLlmFingerprint", string.Empty);
            Scribe_Values.Look(ref optionalLlmFormatRevision, "optionalLlmFormatRevision", 0);
            Scribe_Values.Look(ref optionalLlmCategoryMask, "optionalLlmCategoryMask", 0);
            Scribe_Values.Look(ref lastSettledWordingFingerprint, "lastSettledWordingFingerprint", string.Empty);
            Scribe_Values.Look(ref lastSettledWordingReducerRevision, "lastSettledWordingReducerRevision", 0);
            Scribe_Values.Look(ref lastSettledWordingFormatRevision, "lastSettledWordingFormatRevision", 0);
            Scribe_Values.Look(ref lastWordingDispositionToken, "lastWordingDispositionToken", string.Empty);
        }

        public void Normalize()
        {
            canonicalFactsFingerprint = canonicalFactsFingerprint ?? string.Empty;
            highestSurvivingImportance = highestSurvivingImportance ?? string.Empty;
            deterministicWording = deterministicWording ?? string.Empty;
            optionalLlmWording = optionalLlmWording ?? string.Empty;
            optionalLlmFingerprint = optionalLlmFingerprint ?? string.Empty;
            lastSettledWordingFingerprint = lastSettledWordingFingerprint ?? string.Empty;
            lastWordingDispositionToken = lastWordingDispositionToken ?? string.Empty;
            factBuckets = factBuckets ?? new List<SavedMemoryFactBucket>();
            for (int i = factBuckets.Count - 1; i >= 0; i--)
            {
                if (factBuckets[i] == null)
                {
                    factBuckets.RemoveAt(i);
                    continue;
                }

                factBuckets[i].Normalize();
            }

            subjectRefs = subjectRefs ?? new List<SavedMemorySubjectRef>();
            for (int i = subjectRefs.Count - 1; i >= 0; i--)
            {
                if (subjectRefs[i] == null)
                {
                    subjectRefs.RemoveAt(i);
                    continue;
                }

                subjectRefs[i].Normalize();
            }

            provenanceRefs = provenanceRefs ?? new List<SavedMemoryProvenance>();
            for (int i = provenanceRefs.Count - 1; i >= 0; i--)
            {
                if (provenanceRefs[i] == null)
                {
                    provenanceRefs.RemoveAt(i);
                    continue;
                }

                provenanceRefs[i].Normalize();
            }
        }
    }

    /// <summary>One active standalone Event/Landmark/Summary memory block (§T6.4).</summary>
    public partial class SavedMemoryBlock : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string recordId = string.Empty;
        public string sourceOccurrenceId = string.Empty;
        public string sourceEventId = string.Empty;
        public string captureRuleId = string.Empty;
        public string factDiscriminator = string.Empty;
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        /// <summary>Exactly event | landmark | summary (§6.1 stable tokens).</summary>
        public string kind = string.Empty;
        /// <summary>Exactly none | closed | rolling.</summary>
        public string summaryRole = string.Empty;
        /// <summary>Event/Landmark only; empty for Summary.</summary>
        public string category = string.Empty;
        public string importance = string.Empty;
        public long originalEventTick;
        public bool ageUnknown;
        /// <summary>Empty for Standalone/Imported rows.</summary>
        public string rootId = string.Empty;
        /// <summary>Empty for Standalone/rolling rows.</summary>
        public string chapterId = string.Empty;
        public SavedMemorySubjectRef primarySubject;
        public List<SavedMemorySubjectRef> secondarySubjects = new List<SavedMemorySubjectRef>();
        public List<SavedMemoryCanonicalFact> facts = new List<SavedMemoryCanonicalFact>();
        public List<SavedMemoryProvenance> provenance = new List<SavedMemoryProvenance>();
        public string automaticWording = string.Empty;
        public string playerWording = string.Empty;
        public bool playerEdited;
        public bool suppressed;
        public bool requiredLifecycleLandmark;
        public long formatRevision;
        public long lastAutomaticIncludedTick;
        public long lastAutomaticIncludedEntryOrdinal;
        public long automaticInclusionCount;
        /// <summary>Exactly not_sent | potentially_sent | confirmed_sent; monotonic (§T6.4).</summary>
        public string providerExposureState = string.Empty;
        public long lastProviderExposureTick;
        /// <summary>Non-null only for kind=summary; the sole canonical structured source.</summary>
        public SavedMemorySummaryPayload summaryPayload;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref recordId, "recordId", string.Empty);
            Scribe_Values.Look(ref sourceOccurrenceId, "sourceOccurrenceId", string.Empty);
            Scribe_Values.Look(ref sourceEventId, "sourceEventId", string.Empty);
            Scribe_Values.Look(ref captureRuleId, "captureRuleId", string.Empty);
            Scribe_Values.Look(ref factDiscriminator, "factDiscriminator", string.Empty);
            Scribe_Values.Look(ref ownerPawnId, "ownerPawnId", string.Empty);
            Scribe_Values.Look(ref ownerEpochToken, "ownerEpochToken", string.Empty);
            Scribe_Values.Look(ref kind, "kind", string.Empty);
            Scribe_Values.Look(ref summaryRole, "summaryRole", string.Empty);
            Scribe_Values.Look(ref category, "category", string.Empty);
            Scribe_Values.Look(ref importance, "importance", string.Empty);
            Scribe_Values.Look(ref originalEventTick, "originalEventTick", 0);
            Scribe_Values.Look(ref ageUnknown, "ageUnknown", false);
            Scribe_Values.Look(ref rootId, "rootId", string.Empty);
            Scribe_Values.Look(ref chapterId, "chapterId", string.Empty);
            Scribe_Deep.Look(ref primarySubject, "primarySubject");
            Scribe_Collections.Look(ref secondarySubjects, "secondarySubjects", LookMode.Deep);
            Scribe_Collections.Look(ref facts, "facts", LookMode.Deep);
            Scribe_Collections.Look(ref provenance, "provenance", LookMode.Deep);
            Scribe_Values.Look(ref automaticWording, "automaticWording", string.Empty);
            Scribe_Values.Look(ref playerWording, "playerWording", string.Empty);
            Scribe_Values.Look(ref playerEdited, "playerEdited", false);
            Scribe_Values.Look(ref suppressed, "suppressed", false);
            Scribe_Values.Look(ref requiredLifecycleLandmark, "requiredLifecycleLandmark", false);
            Scribe_Values.Look(ref formatRevision, "formatRevision", 0);
            Scribe_Values.Look(ref lastAutomaticIncludedTick, "lastAutomaticIncludedTick", 0);
            Scribe_Values.Look(
                ref lastAutomaticIncludedEntryOrdinal, "lastAutomaticIncludedEntryOrdinal", 0);
            Scribe_Values.Look(ref automaticInclusionCount, "automaticInclusionCount", 0);
            Scribe_Values.Look(ref providerExposureState, "providerExposureState", string.Empty);
            Scribe_Values.Look(ref lastProviderExposureTick, "lastProviderExposureTick", 0);
            Scribe_Deep.Look(ref summaryPayload, "summaryPayload");
        }

        public void Normalize()
        {
            recordId = recordId ?? string.Empty;
            sourceOccurrenceId = sourceOccurrenceId ?? string.Empty;
            sourceEventId = sourceEventId ?? string.Empty;
            captureRuleId = captureRuleId ?? string.Empty;
            factDiscriminator = factDiscriminator ?? string.Empty;
            ownerPawnId = ownerPawnId ?? string.Empty;
            ownerEpochToken = ownerEpochToken ?? string.Empty;
            kind = kind ?? string.Empty;
            summaryRole = summaryRole ?? string.Empty;
            category = category ?? string.Empty;
            importance = importance ?? string.Empty;
            rootId = rootId ?? string.Empty;
            chapterId = chapterId ?? string.Empty;
            automaticWording = automaticWording ?? string.Empty;
            playerWording = playerWording ?? string.Empty;
            providerExposureState = providerExposureState ?? string.Empty;
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

            facts = facts ?? new List<SavedMemoryCanonicalFact>();
            for (int i = facts.Count - 1; i >= 0; i--)
            {
                if (facts[i] == null)
                {
                    facts.RemoveAt(i);
                    continue;
                }

                facts[i].Normalize();
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

            if (summaryPayload != null)
            {
                summaryPayload.Normalize();
            }
        }
    }

    /// <summary>One flat chapter metadata row inside a thread root (§T6.3).</summary>
    public partial class SavedMemoryChapter : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        public string chapterId = string.Empty;
        public long ordinal;
        public string phaseToken = string.Empty;
        public long openedTick;
        public long lastActivityTick;
        /// <summary>Zero while the chapter is open; <c>closed</c> disambiguates.</summary>
        public long closedTick;
        /// <summary>Exactly formal_end | reversal | lifecycle | inactivity | repair.</summary>
        public string closureReasonToken = string.Empty;
        public bool closed;
        /// <summary>Empty or exactly one visible-block summary record ID in the same chapter.</summary>
        public string closedSummaryRecordId = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref chapterId, "chapterId", string.Empty);
            Scribe_Values.Look(ref ordinal, "ordinal", 0);
            Scribe_Values.Look(ref phaseToken, "phaseToken", string.Empty);
            Scribe_Values.Look(ref openedTick, "openedTick", 0);
            Scribe_Values.Look(ref lastActivityTick, "lastActivityTick", 0);
            Scribe_Values.Look(ref closedTick, "closedTick", 0);
            Scribe_Values.Look(ref closureReasonToken, "closureReasonToken", string.Empty);
            Scribe_Values.Look(ref closed, "closed", false);
            Scribe_Values.Look(ref closedSummaryRecordId, "closedSummaryRecordId", string.Empty);
        }

        public void Normalize()
        {
            chapterId = chapterId ?? string.Empty;
            phaseToken = phaseToken ?? string.Empty;
            closureReasonToken = closureReasonToken ?? string.Empty;
            closedSummaryRecordId = closedSummaryRecordId ?? string.Empty;
        }
    }

    /// <summary>One exact-subject thread root: flat chapters plus visible blocks (§T6.2).</summary>
    public partial class SavedMemoryThreadRoot : IExposable, IMemoryLogicalSizeSource
    {
        public int schemaVersion = 1;
        /// <summary>Recomputed from the canonical tuple during repair; never a second root's key.</summary>
        public string rootId = string.Empty;
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
        /// <summary>Presentation only; never lookup/equality/ordering identity.</summary>
        public string frozenSubjectLabel = string.Empty;
        public long structuralRevision;
        public long statusRevision;
        public int lastAppliedReducerRevision;
        public long nextChapterOrdinal;
        /// <summary>Metadata only; blocks live in the flat visibleBlocks list.</summary>
        public List<SavedMemoryChapter> chapters = new List<SavedMemoryChapter>();
        /// <summary>One flat chronological list; rows reference a chapterId.</summary>
        public List<SavedMemoryBlock> visibleBlocks = new List<SavedMemoryBlock>();
        /// <summary>Null or the root's single active rolling-summary block.</summary>
        public SavedMemoryBlock rollingSummaryBlock;

        public void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref rootId, "rootId", string.Empty);
            Scribe_Values.Look(ref ownerPawnId, "ownerPawnId", string.Empty);
            Scribe_Values.Look(ref ownerEpochToken, "ownerEpochToken", string.Empty);
            Scribe_Values.Look(ref subjectKind, "subjectKind", string.Empty);
            Scribe_Values.Look(ref subjectId, "subjectId", string.Empty);
            Scribe_Values.Look(ref frozenSubjectLabel, "frozenSubjectLabel", string.Empty);
            Scribe_Values.Look(ref structuralRevision, "structuralRevision", 0);
            Scribe_Values.Look(ref statusRevision, "statusRevision", 0);
            Scribe_Values.Look(ref lastAppliedReducerRevision, "lastAppliedReducerRevision", 0);
            Scribe_Values.Look(ref nextChapterOrdinal, "nextChapterOrdinal", 0);
            Scribe_Collections.Look(ref chapters, "chapters", LookMode.Deep);
            Scribe_Collections.Look(ref visibleBlocks, "visibleBlocks", LookMode.Deep);
            Scribe_Deep.Look(ref rollingSummaryBlock, "rollingSummaryBlock");
        }

        public void Normalize()
        {
            rootId = rootId ?? string.Empty;
            ownerPawnId = ownerPawnId ?? string.Empty;
            ownerEpochToken = ownerEpochToken ?? string.Empty;
            subjectKind = subjectKind ?? string.Empty;
            subjectId = subjectId ?? string.Empty;
            frozenSubjectLabel = frozenSubjectLabel ?? string.Empty;
            chapters = chapters ?? new List<SavedMemoryChapter>();
            for (int i = chapters.Count - 1; i >= 0; i--)
            {
                if (chapters[i] == null)
                {
                    chapters.RemoveAt(i);
                    continue;
                }

                chapters[i].Normalize();
            }

            visibleBlocks = visibleBlocks ?? new List<SavedMemoryBlock>();
            for (int i = visibleBlocks.Count - 1; i >= 0; i--)
            {
                if (visibleBlocks[i] == null)
                {
                    visibleBlocks.RemoveAt(i);
                    continue;
                }

                visibleBlocks[i].Normalize();
            }

            if (rollingSummaryBlock != null)
            {
                rollingSummaryBlock.Normalize();
            }
        }
    }
}

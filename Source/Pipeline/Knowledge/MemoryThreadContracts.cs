// MemoryThreadContracts.cs — plain, stable identity vocabulary for the unified memory system.
//
// These objects describe canonical owner/root/source identities without referring to Verse, Unity,
// saved rows, settings, or display text. Game-facing adapters may populate them, while pure codecs
// and planners consume them. Keeping labels out of these contracts prevents translated or renamed
// prose from becoming identity by accident.
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PawnDiary
{
    /// <summary>Stable, non-localized tokens used by memory identity and saved contracts.</summary>
    internal static class MemoryContractTokens
    {
        public const string KindEvent = "event";
        public const string KindLandmark = "landmark";
        public const string KindSummary = "summary";

        public const string ImportanceMinor = "low";
        public const string ImportanceRegular = "medium";
        public const string ImportanceImportant = "high";

        public const string CategoryPersonal = "personal";
        public const string CategoryRelationships = "relationships";
        public const string CategoryFamily = "family";
        public const string CategoryFactions = "factions";

        public const string SubjectPawn = "pawn";
        public const string SubjectFaction = "faction";
        public const string SubjectStream = "stream";

        // Stream identities are saved tokens, not free-form Def data. Keeping the complete M0
        // allowlist here lets pure route/root validation reject a typo before it becomes identity.
        public const string StreamBodyHistory = "body_history";
        public const string StreamColonyMembership = "colony_membership";
        public const string StreamGrowth = "growth";
        public const string StreamBelief = "belief";
        public const string StreamIdeologyRole = "ideology_role";
        public const string StreamRoyalTitle = "royal_title";
        public const string StreamPsylink = "psylink";
        public const string StreamGeneticIdentity = "genetic_identity";
        public const string StreamMechlink = "mechlink";
        public const string StreamPersonaBond = "persona_bond";

        public const string SummaryRoleNone = "none";
        public const string SummaryRoleRolling = "rolling";
        public const string SummaryRoleClosed = "closed";

        /// <summary>True only for one append-only memory-kind token.</summary>
        public static bool IsKnownKind(string value)
        {
            return value == KindEvent || value == KindLandmark || value == KindSummary;
        }

        /// <summary>True only for one append-only importance token.</summary>
        public static bool IsKnownImportance(string value)
        {
            return value == ImportanceMinor
                || value == ImportanceRegular
                || value == ImportanceImportant;
        }

        /// <summary>True only for one of the four player-visible memory categories.</summary>
        public static bool IsKnownCategory(string value)
        {
            return value == CategoryPersonal
                || value == CategoryRelationships
                || value == CategoryFamily
                || value == CategoryFactions;
        }

        /// <summary>True only for an exact continuing-subject root kind.</summary>
        public static bool IsKnownRootSubjectKind(string value)
        {
            return value == SubjectPawn || value == SubjectFaction || value == SubjectStream;
        }

        /// <summary>Returns the declaration-order closed set of continuing stream identities.</summary>
        public static List<string> StreamSubjectTokens()
        {
            return new List<string>
            {
                StreamBodyHistory,
                StreamColonyMembership,
                StreamGrowth,
                StreamBelief,
                StreamIdeologyRole,
                StreamRoyalTitle,
                StreamPsylink,
                StreamGeneticIdentity,
                StreamMechlink,
                StreamPersonaBond
            };
        }

        /// <summary>True only for one exact allowlisted continuing-stream token.</summary>
        public static bool IsKnownStreamSubjectToken(string value)
        {
            return value == StreamBodyHistory
                || value == StreamColonyMembership
                || value == StreamGrowth
                || value == StreamBelief
                || value == StreamIdeologyRole
                || value == StreamRoyalTitle
                || value == StreamPsylink
                || value == StreamGeneticIdentity
                || value == StreamMechlink
                || value == StreamPersonaBond;
        }

        /// <summary>True only for a known kind paired with a legal exact subject identity.</summary>
        public static bool IsValidRootSubject(string subjectKind, string subjectId)
        {
            if (!IsKnownRootSubjectKind(subjectKind)) return false;
            if (subjectKind == SubjectStream) return IsKnownStreamSubjectToken(subjectId);
            if (subjectKind == SubjectFaction)
            {
                string ignoredFactionInstanceId;
                long ignoredAllocatorGeneration;
                return MemoryIdentityCodec.TryParseFactionSubjectId(
                    subjectId,
                    out ignoredFactionInstanceId,
                    out ignoredAllocatorGeneration);
            }
            return true;
        }

        /// <summary>True only for an explicitly saved Summary role.</summary>
        public static bool IsKnownSummaryRole(string value)
        {
            return value == SummaryRoleNone
                || value == SummaryRoleRolling
                || value == SummaryRoleClosed;
        }
    }

    /// <summary>The internal activation state remains legacy-only until the M11 integration commit.</summary>
    internal static class MemorySystemActivationGate
    {
        public const string LegacyShadow = "LegacyShadow";
        public const string CurrentRelease = "CurrentRelease";

        // M0–M10 compile contracts and shadow fixtures, but public behavior stays on the shipped path.
        public const string BuildState = LegacyShadow;
    }

    /// <summary>
    /// Append-only request lifecycle vocabulary frozen by M0. It is behavior-inert until M2 wires the
    /// transactional dispatcher, but tools can already reject an invented or backward transition.
    /// </summary>
    internal static class MemoryRequestStateMachineContracts
    {
        public const string SchemaToken = "memory-request-state-machine-v1";
        public const string Staged = "staged";
        public const string Activated = "activated";
        public const string InvocationCommitted = "invocation_committed";
        public const string SettlementPending = "settlement_pending";

        public const string AttemptPrepared = "prepared";
        public const string AttemptInvocationCommitted = "invocation_committed";
        public const string AttemptReceiptApplied = "receipt_applied";
        public const string AttemptTerminalPending = "terminal_pending";

        /// <summary>Returns the declaration-order stable state registry.</summary>
        public static List<string> States()
        {
            return new List<string>
            {
                Staged, Activated, InvocationCommitted, SettlementPending
            };
        }

        /// <summary>Returns the declaration-order stable transition registry.</summary>
        public static List<string> Transitions()
        {
            return new List<string>
            {
                Staged + ">" + Activated,
                Staged + ">" + SettlementPending,
                Activated + ">" + InvocationCommitted,
                Activated + ">" + SettlementPending,
                InvocationCommitted + ">" + SettlementPending
            };
        }

        /// <summary>Returns the declaration-order saved physical-attempt state registry.</summary>
        public static List<string> AttemptStates()
        {
            return new List<string>
            {
                AttemptPrepared, AttemptInvocationCommitted, AttemptReceiptApplied,
                AttemptTerminalPending
            };
        }

        /// <summary>True only for one declared forward request transition.</summary>
        public static bool CanTransition(string from, string to)
        {
            return Transitions().Contains((from ?? string.Empty) + ">" + (to ?? string.Empty));
        }
    }

    /// <summary>One ordered M0 capacity coordinate; slash-delimited values are atomic tuples.</summary>
    internal sealed class MemoryCapacityContractRow
    {
        public string name = string.Empty;
        public string valueEncoding = string.Empty;
    }

    /// <summary>
    /// Code-owned absolute ceilings and behavior-inert M0 production fallbacks. XML must match the
    /// provisional fallback vector; future normalizers may lower it but can never exceed ceilings.
    /// </summary>
    internal static class MemoryCapacityContracts
    {
        /// <summary>Returns the ordered M0 provisional production fallback vector.</summary>
        public static List<MemoryCapacityContractRow> ProvisionalProduction()
        {
            return Rows(new[]
            {
                "eventFacts=4", "blockProvenanceRows=2", "factKeyValueUnits=48/128",
                "secondarySubjects=4", "factBuckets=16",
                "datedContributionDescriptorMatchCaps=32/32/32", "distinctSubjects=4",
                "subjectRefsPerContribution=2", "provenanceTotal=16",
                "provenancePerContribution=2", "summaryDeterministicWordingUnits=240",
                "summaryOptionalLlmWordingUnits=240", "blockWordingUnits=240",
                "playerBackgroundUnits=225", "frozenDisplayLabelUnits=80",
                "rawIdentitySegmentUnits=128", "compositeKeyUnits=1024/2048",
                "libraryWindowRows=64", "chapterHeaderWindowRows=32", "cachedOwnerStates=4",
                "sliceWorkItems=30", "sliceTargetMicroseconds=375",
                "manageableBlocksPerOwner=128", "globalBlockCaps=5000/6000",
                "editedBlocksOwner=32", "editedBlocksGlobal=1000", "activeOwnerBytes=196608",
                "combinedOwnerBytes=262144", "activeGlobalBytes=6291456",
                "combinedGlobalBytes=8388608", "libraryCommandEntries=32",
                "guardRowsOwnerGlobal=512/10000", "coordinatorOpportunitiesOwnerGlobal=2/1000",
                "attemptAuditRowsPerRequestGlobal=4/1024", "runtimeQueueEntries=128",
                "activeRequestsOwnerGlobal=8/128", "frozenVariantAttemptCaps=4/4",
                "frozenEvidenceGuardDiagnosticCaps=2/8/16", "frozenPromptUnits=4096",
                "acceptedPromptPairsEscapedBytesGlobal=500/4194304", "factionSnapshots=256",
                "dirtyObservationKeys=1024", "legacyEpochReservations=64",
                "awarenessFacts=4", "awarenessRows=128", "openEpisodes=16",
                "ownerSlotTriple=1000/1001/1000", "searchQueryBounds=80/160",
                "rowPreviewUnits=120", "normalizedSearchFieldUnits=120",
                "rowSearchProjectionUnits=480", "currentStatusFieldTextCaps=4/240",
                "devReasonCountTextCaps=8/80", "copyDiagnosticUnits=2000",
                "importedTextUnits=2000", "importedOwnerCount=1000",
                "importedOwnerRows=256", "importedUnknownRows=1000",
                "importedGlobalRows=10000", "importedPreviewChunkUnits=240/1000",
                "importedSearchScratchUnits=49152", "importedOwnerUnknownBytes=262144/2097152",
                "importedGlobalBytes=8388608", "libraryOwnerEntries=2048"
            });
        }

        /// <summary>Returns the ordered absolute defensive ceiling bundle D.</summary>
        public static List<MemoryCapacityContractRow> DefensiveCeilings()
        {
            return Rows(new[]
            {
                "eventFacts=32", "blockProvenanceRows=16", "factKeyValueUnits=192/512",
                "secondarySubjects=32", "factBuckets=64",
                "datedContributionDescriptorMatchCaps=128/128/128", "distinctSubjects=32",
                "subjectRefsPerContribution=8", "provenanceTotal=128",
                "provenancePerContribution=8", "summaryDeterministicWordingUnits=1200",
                "summaryOptionalLlmWordingUnits=1200", "blockWordingUnits=1200",
                "playerBackgroundUnits=1200", "frozenDisplayLabelUnits=320",
                "rawIdentitySegmentUnits=512", "compositeKeyUnits=4096/8192",
                "libraryWindowRows=256", "chapterHeaderWindowRows=128", "cachedOwnerStates=8",
                "sliceWorkItems=240", "sliceTargetMicroseconds=1000",
                "manageableBlocksPerOwner=1024", "globalBlockCaps=40000/44000",
                "editedBlocksOwner=128", "editedBlocksGlobal=4000", "activeOwnerBytes=2097152",
                "combinedOwnerBytes=4194304", "activeGlobalBytes=25165824",
                "combinedGlobalBytes=33554432", "libraryCommandEntries=128",
                "guardRowsOwnerGlobal=2048/40000", "coordinatorOpportunitiesOwnerGlobal=8/4000",
                "attemptAuditRowsPerRequestGlobal=16/4096", "runtimeQueueEntries=512",
                "activeRequestsOwnerGlobal=32/512", "frozenVariantAttemptCaps=16/16",
                "frozenEvidenceGuardDiagnosticCaps=2/32/64", "frozenPromptUnits=32768",
                "acceptedPromptPairsEscapedBytesGlobal=4000/67108864", "factionSnapshots=1024",
                "dirtyObservationKeys=4096", "legacyEpochReservations=512",
                "awarenessFacts=16", "awarenessRows=512", "openEpisodes=64",
                "ownerSlotTriple=4000/4001/4000", "searchQueryBounds=320/640",
                "rowPreviewUnits=480", "normalizedSearchFieldUnits=480",
                "rowSearchProjectionUnits=1920", "currentStatusFieldTextCaps=16/1200",
                "devReasonCountTextCaps=32/320", "copyDiagnosticUnits=8000",
                "importedTextUnits=8000", "importedOwnerCount=4000",
                "importedOwnerRows=1024", "importedUnknownRows=4000",
                "importedGlobalRows=40000", "importedPreviewChunkUnits=1200/4000",
                "importedSearchScratchUnits=262144", "importedOwnerUnknownBytes=2097152/16777216",
                "importedGlobalBytes=33554432", "libraryOwnerEntries=8192"
            });
        }

        private static List<MemoryCapacityContractRow> Rows(string[] encodedRows)
        {
            List<MemoryCapacityContractRow> rows = new List<MemoryCapacityContractRow>();
            for (int index = 0; index < encodedRows.Length; index++)
            {
                int separator = encodedRows[index].IndexOf('=');
                rows.Add(new MemoryCapacityContractRow
                {
                    name = encodedRows[index].Substring(0, separator),
                    valueEncoding = encodedRows[index].Substring(separator + 1)
                });
            }
            return rows;
        }
    }

    /// <summary>The complete detached settings tuple frozen by M0 for later M5 persistence.</summary>
    internal sealed class MemorySettingsPolicyFieldsV1
    {
        public const string SchemaToken = "memory-settings-policy-fields-v1";

        public bool saveNewMemories = true;
        public bool useMemoriesInWriting = true;
        public bool usePawnBackground = true;
        public bool allowExtraMemoryAiRequests;
        public bool occasionalMemoryReflections;
        public int memoryCategoryMask = 15;
        public long captureInvalidationGenerationPersonal = 1;
        public long captureInvalidationGenerationRelationships = 1;
        public long captureInvalidationGenerationFamily = 1;
        public long captureInvalidationGenerationFactions = 1;
        public long optionalRequestInvalidationGeneration = 1;
        public int minorMemoryLifetimeDays = 15;
        public int regularMemoryLifetimeDays = 60;
        public int memoryThreadTarget = 12;
        public int memoryReuseDays = 5;
        public int memoryRevisitEntryCount = 3;

        /// <summary>Creates the all-features-on benchmark profile for one authenticated N.</summary>
        public static MemorySettingsPolicyFieldsV1 CreateBenchmarkProfile(int threadTarget)
        {
            return new MemorySettingsPolicyFieldsV1
            {
                allowExtraMemoryAiRequests = true,
                occasionalMemoryReflections = true,
                memoryThreadTarget = threadTarget
            };
        }
    }

    /// <summary>Canonical §T14.3 encoding of a complete settings policy tuple.</summary>
    internal static class MemorySettingsPolicyCodec
    {
        public static string Encode(MemorySettingsPolicyFieldsV1 policy)
        {
            if (policy == null) return string.Empty;
            string[] fields =
            {
                MemorySettingsPolicyFieldsV1.SchemaToken,
                Bool(policy.saveNewMemories), Bool(policy.useMemoriesInWriting),
                Bool(policy.usePawnBackground), Bool(policy.allowExtraMemoryAiRequests),
                Bool(policy.occasionalMemoryReflections),
                policy.memoryCategoryMask.ToString(CultureInfo.InvariantCulture),
                policy.captureInvalidationGenerationPersonal.ToString(CultureInfo.InvariantCulture),
                policy.captureInvalidationGenerationRelationships.ToString(CultureInfo.InvariantCulture),
                policy.captureInvalidationGenerationFamily.ToString(CultureInfo.InvariantCulture),
                policy.captureInvalidationGenerationFactions.ToString(CultureInfo.InvariantCulture),
                policy.optionalRequestInvalidationGeneration.ToString(CultureInfo.InvariantCulture),
                policy.minorMemoryLifetimeDays.ToString(CultureInfo.InvariantCulture),
                policy.regularMemoryLifetimeDays.ToString(CultureInfo.InvariantCulture),
                policy.memoryThreadTarget.ToString(CultureInfo.InvariantCulture),
                policy.memoryReuseDays.ToString(CultureInfo.InvariantCulture),
                policy.memoryRevisitEntryCount.ToString(CultureInfo.InvariantCulture)
            };
            StringBuilder result = new StringBuilder();
            for (int index = 0; index < fields.Length; index++)
            {
                result.Append(OrdinalSegmentCodec.Segment(fields[index]));
            }
            return result.ToString();
        }

        private static string Bool(bool value)
        {
            return value ? "1" : "0";
        }
    }

    /// <summary>One exact, detached subject used by canonical identity codecs.</summary>
    internal sealed class MemoryTypedSubject
    {
        public string subjectKind = string.Empty;
        public string subjectId = string.Empty;
    }

    /// <summary>The exact owner/epoch/subject tuple defining one flat memory thread root.</summary>
    internal sealed class MemoryRootIdentity
    {
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string primarySubjectKind = string.Empty;
        public string primarySubjectId = string.Empty;
    }

    /// <summary>The exact private identity shared by one Event or Landmark replay.</summary>
    internal sealed class MemoryRecordIdentity
    {
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string sourceOccurrenceId = string.Empty;
        public string captureRuleId = string.Empty;
        public string factDiscriminator = string.Empty;
    }

    /// <summary>
    /// Detached evidence used only when a source adapter can prove the fallback tuple uniquely names
    /// one gameplay occurrence in its persistent deduplication domain.
    /// </summary>
    internal sealed class MemorySourceOccurrenceFallback
    {
        public string stableSignalToken = string.Empty;
        public long eventTickInvariant;
        public long sourceLocalSequenceInvariant;
        public string factDiscriminator = string.Empty;
        public List<MemoryTypedSubject> subjects = new List<MemoryTypedSubject>();
        public bool sourceProvesUniqueness;
    }

    /// <summary>Detached allocator input used to reserve one autobiographical epoch atomically.</summary>
    internal sealed class MemoryEpochAllocationRequest
    {
        public string ownerPawnId = string.Empty;
        public long lastIssuedSequence;
        public string fallbackChain = string.Empty;
        public List<string> liveEpochCarriers = new List<string>();
        public bool isTargetBrainwipe;
    }

    /// <summary>
    /// Pure epoch-allocation result. Callers publish these fields together only when canMutate is true.
    /// </summary>
    internal sealed class MemoryEpochAllocationPlan
    {
        public const string Normal = "normal";
        public const string Fallback = "fallback";
        public const string InvalidAllocatorState = "invalid_allocator_state";

        public bool canMutate;
        public string outcomeToken = InvalidAllocatorState;
        public string epochToken = string.Empty;
        public long nextSequence;
        public string nextFallbackChain = string.Empty;
        public bool repairedFallbackChain;
        public long probeOrdinal = -1;
        public string priorFallbackChain = string.Empty;
        public string stepHash = string.Empty;
    }

    /// <summary>Parsed fields from one deterministic opaque-ID collision repair.</summary>
    internal sealed class MemoryRepairIdentity
    {
        public string kindToken = string.Empty;
        public string originalOpaqueId = string.Empty;
        public string identityHash = string.Empty;
        public string payloadHash = string.Empty;
        public long collisionOrdinal;
    }

    /// <summary>One frozen prompt-memory evidence identity in exact rendered line order.</summary>
    internal sealed class MemoryEvidenceIdentity
    {
        public string recordId = string.Empty;
        public string sourceOccurrenceId = string.Empty;
        public string rootIdOrEmpty = string.Empty;
    }

    /// <summary>One canonical repetition-guard identity.</summary>
    internal sealed class MemoryGuardIdentity
    {
        public string guardKind = string.Empty;
        public string guardKey = string.Empty;
    }

    /// <summary>One frozen diagnostic-provenance identity in canonical line-first order.</summary>
    internal sealed class MemoryDiagnosticIdentity
    {
        public string provenanceKindToken = string.Empty;
        public string sourceId = string.Empty;
        public string recordIdOrEmpty = string.Empty;
        public string sourceOccurrenceIdOrEmpty = string.Empty;
        public string rootIdOrEmpty = string.Empty;
        public int lineOrdinal;
    }

    /// <summary>The exact declaration-order fields authenticated by one transient send permit.</summary>
    internal sealed class MemoryInvocationPermitIdentity
    {
        public string logicalRequestId = string.Empty;
        public string logicalRequestKey = string.Empty;
        public string requestPurposeToken = string.Empty;
        public long sessionId;
        public string eventIdOrOpportunityKey = string.Empty;
        public string povRoleToken = string.Empty;
        public string ownerPawnId = string.Empty;
        public string ownerEpochToken = string.Empty;
        public string evidenceEpochToken = string.Empty;
        public long ownerCancellationGeneration;
        public long globalCancellationGeneration;
        public long optionalRequestInvalidationGeneration;
        public int attemptOrdinal;
        public string variantKey = string.Empty;
        public string receiptPlanFingerprint = string.Empty;
        public long invocationSequence;
        public long invocationTick;
        public int narrativeUseWinnerAttemptOrdinal;
    }
}

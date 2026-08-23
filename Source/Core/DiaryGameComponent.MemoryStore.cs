// DiaryGameComponent.MemoryStore.cs — the component-saved unified-memory metadata and the
// owner-scoped memory-store operations (design/MEMORY_SYSTEM_IMPLEMENTATION_PLAN.md §T6.9,
// §14.1, WP-STORE M1 slice).
//
// This partial owns ONLY cross-owner/component concerns: allocator/cancellation generations, the
// global faction snapshots, the bounded coordinator opportunity rows consumed later by the
// EXISTING reflection coordinator (never a second scheduler), terminal Dev audit rows, migration
// metadata that cannot belong to one resolved owner, and dispatch schema/allocator rows. Owner
// payloads live in each PawnKnowledgeState envelope. Transient size indexes are derivatives of
// saved lists and are rebuilt after load/new game — they are never Scribed.
//
// While MemorySystemActivationGate stays LegacyShadow this file persists additive shape only:
// nothing here captures, recalls, schedules, or mutates gameplay behavior.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        // ---- Saved §T6.9 component fields (tokens = field names) ----

        /// <summary>Current 1; missing old-save default 0 (§T6.9).</summary>
        private int memoryComponentSchemaVersion;
        private long lastIssuedAutobiographicalEpochSequence;
        /// <summary>Empty = no fallback allocation committed; nonempty = 64 lowercase hex.</summary>
        private string lastIssuedAutobiographicalEpochFallbackChain = string.Empty;
        private long globalFactionSnapshotAllocatorGeneration;
        private List<SavedGlobalFactionSnapshot> globalFactionSnapshots =
            new List<SavedGlobalFactionSnapshot>();
        private List<SavedLegacyOwnerEpochReservation> legacyOwnerEpochReservations =
            new List<SavedLegacyOwnerEpochReservation>();
        private long globalOptionalRequestCancellationGeneration;
        private long lastAppliedMemoryPolicyRevision;
        private string lastAppliedMemoryPolicyFingerprint = string.Empty;
        private SavedMemoryAppliedPolicyStateV1 lastAppliedMemoryPolicyState;
        private List<SavedImportedMemoryRow> unresolvedOwnerArchiveRows =
            new List<SavedImportedMemoryRow>();
        /// <summary>Exactly LegacyRaw | MigrationPending | Current (§T6.9).</summary>
        private string unresolvedArchiveMigrationState = MemoryArchiveStates.LegacyRaw;
        private List<SavedLegacyUnresolvedOwnerArchiveInputV1> rawUnresolvedOwnerArchiveInput =
            new List<SavedLegacyUnresolvedOwnerArchiveInputV1>();
        private long rawUnresolvedArchiveReattributionGeneration;
        private long unresolvedArchiveReattributionGeneration;
        private long unresolvedArchiveStructuralRevision;
        private bool unresolvedArchiveReattributionDisabled;
        private int memoryCoordinatorSchemaVersion;
        private List<SavedSummaryWordingOpportunityV1> summaryWordingOpportunities =
            new List<SavedSummaryWordingOpportunityV1>();
        private List<SavedMemoryDiagnosticCounter> memoryDiagnosticCounters =
            new List<SavedMemoryDiagnosticCounter>();
        private List<SavedMemoryAttemptAuditRow> memoryAttemptAuditRows =
            new List<SavedMemoryAttemptAuditRow>();
        private int memoryDispatchSchemaVersion;
        private long lastIssuedMemoryLogicalRequestSequence;
        private List<SavedActiveLogicalRequestV1> activeMemoryCoordinatorRequests =
            new List<SavedActiveLogicalRequestV1>();

        // ---- Transient derivatives (never Scribed; rebuilt after load/new game) ----

        private readonly Dictionary<string, MemoryOwnerByteTotals> memoryByteTotalsByOwner =
            new Dictionary<string, MemoryOwnerByteTotals>(StringComparer.Ordinal);
        private long memoryComponentActiveBytesTotal;

        internal static class MemoryArchiveStates
        {
            public const string LegacyRaw = "LegacyRaw";
            public const string MigrationPending = "MigrationPending";
            public const string Current = "Current";
        }

        internal struct MemoryOwnerByteTotals
        {
            public bool valid;
            public long activeBytes;
            public long importedBytes;
        }

        /// <summary>Scribes every §T6.9 field through the existing ExposeKnowledgeData seam.
        /// Tokens equal field names exactly.</summary>
        private void ExposeMemoryComponentData()
        {
            Scribe_Values.Look(ref memoryComponentSchemaVersion, "memoryComponentSchemaVersion", 0);
            Scribe_Values.Look(
                ref lastIssuedAutobiographicalEpochSequence,
                "lastIssuedAutobiographicalEpochSequence", 0);
            Scribe_Values.Look(
                ref lastIssuedAutobiographicalEpochFallbackChain,
                "lastIssuedAutobiographicalEpochFallbackChain", string.Empty);
            Scribe_Values.Look(
                ref globalFactionSnapshotAllocatorGeneration,
                "globalFactionSnapshotAllocatorGeneration", 0);
            Scribe_Collections.Look(
                ref globalFactionSnapshots, "globalFactionSnapshots", LookMode.Deep);
            Scribe_Collections.Look(
                ref legacyOwnerEpochReservations, "legacyOwnerEpochReservations", LookMode.Deep);
            Scribe_Values.Look(
                ref globalOptionalRequestCancellationGeneration,
                "globalOptionalRequestCancellationGeneration", 0);
            Scribe_Values.Look(
                ref lastAppliedMemoryPolicyRevision, "lastAppliedMemoryPolicyRevision", 0);
            Scribe_Values.Look(
                ref lastAppliedMemoryPolicyFingerprint,
                "lastAppliedMemoryPolicyFingerprint", string.Empty);
            Scribe_Deep.Look(ref lastAppliedMemoryPolicyState, "lastAppliedMemoryPolicyState");
            Scribe_Collections.Look(
                ref unresolvedOwnerArchiveRows, "unresolvedOwnerArchiveRows", LookMode.Deep);
            Scribe_Values.Look(
                ref unresolvedArchiveMigrationState,
                "unresolvedArchiveMigrationState",
                MemoryArchiveStates.LegacyRaw);
            Scribe_Collections.Look(
                ref rawUnresolvedOwnerArchiveInput,
                "rawUnresolvedOwnerArchiveInput",
                LookMode.Deep);
            Scribe_Values.Look(
                ref rawUnresolvedArchiveReattributionGeneration,
                "rawUnresolvedArchiveReattributionGeneration", 0);
            Scribe_Values.Look(
                ref unresolvedArchiveReattributionGeneration,
                "unresolvedArchiveReattributionGeneration", 0);
            Scribe_Values.Look(
                ref unresolvedArchiveStructuralRevision,
                "unresolvedArchiveStructuralRevision", 0);
            Scribe_Values.Look(
                ref unresolvedArchiveReattributionDisabled,
                "unresolvedArchiveReattributionDisabled", false);
            Scribe_Values.Look(ref memoryCoordinatorSchemaVersion,
                "memoryCoordinatorSchemaVersion", 0);
            Scribe_Collections.Look(
                ref summaryWordingOpportunities, "summaryWordingOpportunities", LookMode.Deep);
            Scribe_Collections.Look(
                ref memoryDiagnosticCounters, "memoryDiagnosticCounters", LookMode.Deep);
            Scribe_Collections.Look(
                ref memoryAttemptAuditRows, "memoryAttemptAuditRows", LookMode.Deep);
            Scribe_Values.Look(ref memoryDispatchSchemaVersion,
                "memoryDispatchSchemaVersion", 0);
            Scribe_Values.Look(
                ref lastIssuedMemoryLogicalRequestSequence,
                "lastIssuedMemoryLogicalRequestSequence", 0);
            Scribe_Collections.Look(
                ref activeMemoryCoordinatorRequests,
                "activeMemoryCoordinatorRequests",
                LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NormalizeLoadedMemoryComponent();
            }
        }

        /// <summary>
        /// §T6.9 load normalization: null lists heal empty, missing schema versions read 0 with
        /// current-1 semantics applied lazily, zero reattribution/structural counters initialize to
        /// their positive invariants BEFORE any migration/Brainwipe/Library publication, and the
        /// raw/current taxonomy derives without semantic stamping. Culture/page state untouched.
        /// </summary>
        private void NormalizeLoadedMemoryComponent()
        {
            globalFactionSnapshots = globalFactionSnapshots ?? new List<SavedGlobalFactionSnapshot>();
            legacyOwnerEpochReservations =
                legacyOwnerEpochReservations ?? new List<SavedLegacyOwnerEpochReservation>();
            unresolvedOwnerArchiveRows =
                unresolvedOwnerArchiveRows ?? new List<SavedImportedMemoryRow>();
            rawUnresolvedOwnerArchiveInput =
                rawUnresolvedOwnerArchiveInput ?? new List<SavedLegacyUnresolvedOwnerArchiveInputV1>();
            summaryWordingOpportunities =
                summaryWordingOpportunities ?? new List<SavedSummaryWordingOpportunityV1>();
            memoryDiagnosticCounters =
                memoryDiagnosticCounters ?? new List<SavedMemoryDiagnosticCounter>();
            memoryAttemptAuditRows =
                memoryAttemptAuditRows ?? new List<SavedMemoryAttemptAuditRow>();
            activeMemoryCoordinatorRequests =
                activeMemoryCoordinatorRequests ?? new List<SavedActiveLogicalRequestV1>();

            for (int i = globalFactionSnapshots.Count - 1; i >= 0; i--)
            {
                if (globalFactionSnapshots[i] == null)
                {
                    globalFactionSnapshots.RemoveAt(i);
                    continue;
                }

                globalFactionSnapshots[i].Normalize();
            }

            for (int i = legacyOwnerEpochReservations.Count - 1; i >= 0; i--)
            {
                if (legacyOwnerEpochReservations[i] == null)
                {
                    legacyOwnerEpochReservations.RemoveAt(i);
                    continue;
                }

                legacyOwnerEpochReservations[i].Normalize();
            }

            for (int i = unresolvedOwnerArchiveRows.Count - 1; i >= 0; i--)
            {
                if (unresolvedOwnerArchiveRows[i] == null)
                {
                    unresolvedOwnerArchiveRows.RemoveAt(i);
                    continue;
                }

                unresolvedOwnerArchiveRows[i].Normalize();
            }

            for (int i = rawUnresolvedOwnerArchiveInput.Count - 1; i >= 0; i--)
            {
                if (rawUnresolvedOwnerArchiveInput[i] == null)
                {
                    rawUnresolvedOwnerArchiveInput.RemoveAt(i);
                    continue;
                }

                rawUnresolvedOwnerArchiveInput[i].Normalize();
            }

            for (int i = summaryWordingOpportunities.Count - 1; i >= 0; i--)
            {
                if (summaryWordingOpportunities[i] == null)
                {
                    summaryWordingOpportunities.RemoveAt(i);
                    continue;
                }

                summaryWordingOpportunities[i].Normalize();
            }

            for (int i = memoryDiagnosticCounters.Count - 1; i >= 0; i--)
            {
                if (memoryDiagnosticCounters[i] == null)
                {
                    memoryDiagnosticCounters.RemoveAt(i);
                    continue;
                }

                memoryDiagnosticCounters[i].Normalize();
            }

            for (int i = memoryAttemptAuditRows.Count - 1; i >= 0; i--)
            {
                if (memoryAttemptAuditRows[i] == null)
                {
                    memoryAttemptAuditRows.RemoveAt(i);
                    continue;
                }

                memoryAttemptAuditRows[i].Normalize();
            }

            for (int i = activeMemoryCoordinatorRequests.Count - 1; i >= 0; i--)
            {
                if (activeMemoryCoordinatorRequests[i] == null)
                {
                    activeMemoryCoordinatorRequests.RemoveAt(i);
                    continue;
                }

                activeMemoryCoordinatorRequests[i].Normalize();
            }

            if (lastAppliedMemoryPolicyState != null)
            {
                lastAppliedMemoryPolicyState.Normalize();
            }

            // Zero means missing for these positive current invariants (§T6.9).
            if (unresolvedArchiveReattributionGeneration == 0)
            {
                unresolvedArchiveReattributionGeneration = 1;
            }

            if (rawUnresolvedArchiveReattributionGeneration == 0)
            {
                rawUnresolvedArchiveReattributionGeneration =
                    unresolvedArchiveReattributionGeneration;
            }

            if (unresolvedArchiveStructuralRevision == 0)
            {
                unresolvedArchiveStructuralRevision = 1;
            }

            // Taxonomy derivation: mixed payload is inert MigrationPending until one atomic plan
            // chooses a complete representation (§T6.9); otherwise derive from what is present.
            bool hasCurrent = unresolvedOwnerArchiveRows.Count > 0;
            bool hasRaw = rawUnresolvedOwnerArchiveInput.Count > 0;
            if (hasCurrent && hasRaw)
            {
                unresolvedArchiveMigrationState = MemoryArchiveStates.MigrationPending;
            }
            else if (hasCurrent)
            {
                unresolvedArchiveMigrationState = MemoryArchiveStates.Current;
            }
            else if (hasRaw)
            {
                unresolvedArchiveMigrationState = MemoryArchiveStates.LegacyRaw;
            }
            else if (unresolvedArchiveMigrationState != MemoryArchiveStates.MigrationPending)
            {
                unresolvedArchiveMigrationState = MemoryArchiveStates.LegacyRaw;
            }
        }

        /// <summary>Clean-start values for a fresh game (called from ResetKnowledgeForNewGame).</summary>
        private void ResetMemoryComponentForNewGame()
        {
            memoryComponentSchemaVersion = 1;
            memoryCoordinatorSchemaVersion = 1;
            memoryDispatchSchemaVersion = 1;
            lastIssuedAutobiographicalEpochSequence = 0;
            lastIssuedAutobiographicalEpochFallbackChain = string.Empty;
            globalFactionSnapshotAllocatorGeneration = 0;
            globalOptionalRequestCancellationGeneration = 1;
            lastAppliedMemoryPolicyRevision = 0;
            lastAppliedMemoryPolicyFingerprint = string.Empty;
            lastAppliedMemoryPolicyState = null;
            globalFactionSnapshots.Clear();
            legacyOwnerEpochReservations.Clear();
            unresolvedOwnerArchiveRows.Clear();
            rawUnresolvedOwnerArchiveInput.Clear();
            unresolvedArchiveMigrationState = MemoryArchiveStates.LegacyRaw;
            rawUnresolvedArchiveReattributionGeneration = 1;
            unresolvedArchiveReattributionGeneration = 1;
            unresolvedArchiveStructuralRevision = 1;
            unresolvedArchiveReattributionDisabled = false;
            summaryWordingOpportunities.Clear();
            memoryDiagnosticCounters.Clear();
            memoryAttemptAuditRows.Clear();
            lastIssuedMemoryLogicalRequestSequence = 0;
            activeMemoryCoordinatorRequests.Clear();
            RebuildMemorySizeIndexes();
        }

        // ---- Owner-scoped store operations (typed results; never create on read) ----

        /// <summary>Returns the owner's CURRENT-schema envelope, or null when absent/legacy. A
        /// read API never creates or normalizes an envelope (§T6.1).</summary>
        internal PawnKnowledgeState FindCurrentMemoryEnvelope(string pawnId)
        {
            PawnDiaryRecord diary = LookupDiaryByPawnId(pawnId);
            if (diary?.knowledgeState == null
                || !diary.knowledgeState.IsCurrentSchema())
            {
                return null;
            }

            return diary.knowledgeState;
        }

        /// <summary>
        /// Lookup-or-create for the owner's current-shape envelope (§T8.2 step 1). A legacy v1/v2
        /// state is returned untouched as null-equivalent for memory purposes — component migration
        /// owns stamping it current; capture must treat it as MigrationPending (§T13.5).
        /// </summary>
        internal PawnKnowledgeState EnsureCurrentMemoryEnvelope(PawnDiaryRecord diary)
        {
            if (diary == null || string.IsNullOrWhiteSpace(diary.pawnId))
            {
                return null;
            }

            if (diary.knowledgeState == null)
            {
                diary.knowledgeState = PawnKnowledgeState.CreateCurrent(diary.pawnId);
                return diary.knowledgeState;
            }

            return diary.knowledgeState.IsCurrentSchema() ? diary.knowledgeState : null;
        }

        /// <summary>True when the exact owner may receive new memory work right now. Legacy/raw
        /// owners report MigrationPending semantics via false plus the out token (§T13.5).</summary>
        internal bool IsOwnerEnrolledForMemory(PawnDiaryRecord diary, out string statusToken)
        {
            PawnKnowledgeState state = diary?.knowledgeState;
            if (state == null)
            {
                statusToken = "no_envelope";
                return false;
            }

            switch (PawnKnowledgeStateSchemaPolicy.Classify(state.schemaVersion))
            {
                case PawnKnowledgeStateSchemaPolicy.VersionClass.Current:
                    statusToken = "current";
                    return true;
                case PawnKnowledgeStateSchemaPolicy.VersionClass.LegacyPendingMigration:
                case PawnKnowledgeStateSchemaPolicy.VersionClass.RawLegacy:
                    statusToken = "migration_pending";
                    return false;
                default:
                    statusToken = "newer_schema";
                    return false;
            }
        }

        // ---- Transient byte accounting (derived; rebuilt, never canonical saved state) ----

        /// <summary>Rebuilds the transient per-owner/global logical-size indexes from saved truth.
        /// Bounded work per §T17.5: one sizer walk per envelope plus its archive rows separately.</summary>
        internal void RebuildMemorySizeIndexes()
        {
            memoryByteTotalsByOwner.Clear();
            memoryComponentActiveBytesTotal = SizeComponentMemoryBytes();

            if (diaries == null)
            {
                return;
            }

            for (int i = 0; i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                if (diary == null || string.IsNullOrWhiteSpace(diary.pawnId)
                    || diary.knowledgeState == null
                    || !diary.knowledgeState.IsCurrentSchema())
                {
                    continue;
                }

                memoryByteTotalsByOwner[diary.pawnId] = MeasureOwner(diary.knowledgeState);
            }
        }

        private MemoryOwnerByteTotals MeasureOwner(PawnKnowledgeState state)
        {
            var totals = new MemoryOwnerByteTotals { valid = false, activeBytes = 0, importedBytes = 0 };
            MemoryLogicalSizeResult whole =
                MemoryLogicalPayloadSizer.Size(state);

            // Imported rows are charged to importedBytes, excluded from activeOwnerBytes (§T17.5).
            long imported = 0;
            for (int i = 0; state.importedArchiveRows != null && i < state.importedArchiveRows.Count; i++)
            {
                if (state.importedArchiveRows[i] == null)
                {
                    continue;
                }

                MemoryLogicalSizeResult row =
                    MemoryLogicalPayloadSizer.Size(state.importedArchiveRows[i]);
                if (!row.valid)
                {
                    totals.valid = false;
                    return totals;
                }

                imported += row.totalBytes;
            }

            totals.valid = whole.valid;
            totals.activeBytes = whole.valid ? whole.totalBytes - imported : 0;
            totals.importedBytes = imported;
            return totals;
        }

        /// <summary>Component-global active memory metadata bytes (§T17.5): faction snapshots,
        /// opportunities, diagnostics, audit rows, and ownerless active requests. Unresolved
        /// Imported rows are excluded (they are Unknown-archive bytes).</summary>
        private long SizeComponentMemoryBytes()
        {
            long total = 0;
            total += SizeList(globalFactionSnapshots);
            total += SizeList(summaryWordingOpportunities);
            total += SizeList(memoryDiagnosticCounters);
            total += SizeList(memoryAttemptAuditRows);
            for (int i = 0; i < activeMemoryCoordinatorRequests.Count; i++)
            {
                SavedActiveLogicalRequestV1 request = activeMemoryCoordinatorRequests[i];
                // Neutral/ownerless rows charge component-global; owner-attributed nested rows are
                // already charged to their owner (§T17.5), so skip rows carrying a nonblank owner.
                if (request == null
                    || !string.IsNullOrWhiteSpace(request.ownerPawnId))
                {
                    continue;
                }

                MemoryLogicalSizeResult result = MemoryLogicalPayloadSizer.Size(request);
                if (result.valid)
                {
                    total += result.totalBytes;
                }
            }

            return total;
        }

        private static long SizeList<T>(List<T> rows) where T : class, IMemoryLogicalSizeSource
        {
            long total = 4; // list-count prefix
            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                if (rows[i] == null)
                {
                    continue;
                }

                MemoryLogicalSizeResult result = MemoryLogicalPayloadSizer.Size(rows[i]);
                if (result.valid)
                {
                    total += result.totalBytes;
                }
            }

            return total;
        }

        /// <summary>The measured unit pair for one exact owner, or invalid when unenrolled.</summary>
        internal MemoryOwnerByteTotals GetOwnerByteTotals(string pawnId)
        {
            MemoryOwnerByteTotals totals;
            return memoryByteTotalsByOwner.TryGetValue(pawnId ?? string.Empty, out totals)
                ? totals
                : new MemoryOwnerByteTotals { valid = false };
        }

        internal MemoryPayloadBudgetTotals GetGlobalBudgetTotals()
        {
            long owners = 0;
            foreach (MemoryOwnerByteTotals totals in memoryByteTotalsByOwner.Values)
            {
                if (totals.valid)
                {
                    owners += totals.activeBytes;
                }
            }

            return new MemoryPayloadBudgetTotals
            {
                globalActiveBytes = checked(owners + memoryComponentActiveBytesTotal),
                globalImportedBytes = GlobalImportedBytes()
            };
        }

        private long GlobalImportedBytes()
        {
            long imported = 0;
            foreach (MemoryOwnerByteTotals totals in memoryByteTotalsByOwner.Values)
            {
                if (totals.valid)
                {
                    imported += totals.importedBytes;
                }
            }

            // Unknown-archive bytes count once toward global combined (§T17.5).
            imported += SizeList(unresolvedOwnerArchiveRows);
            return imported;
        }

        /// <summary>Adds or coalesces one bounded diagnostic counter (unknown tokens fold into the
        /// allowlisted "other" row rather than retaining prose; counts stick at MaxValue, §T6.9).</summary>
        internal void RecordMemoryDiagnostic(string reasonToken, string scopeToken)
        {
            const string OtherReason = "other";
            if (string.IsNullOrWhiteSpace(reasonToken))
            {
                reasonToken = OtherReason;
            }

            if (string.IsNullOrWhiteSpace(scopeToken))
            {
                scopeToken = OtherReason;
            }
            else
            {
                scopeToken = scopeToken.Trim();
            }

            for (int i = 0; i < memoryDiagnosticCounters.Count; i++)
            {
                SavedMemoryDiagnosticCounter counter = memoryDiagnosticCounters[i];
                if (string.Equals(counter.reasonToken, reasonToken, StringComparison.Ordinal)
                    && string.Equals(counter.scopeToken, scopeToken, StringComparison.Ordinal))
                {
                    if (counter.saturatedCount < long.MaxValue)
                    {
                        counter.saturatedCount++;
                    }

                    return;
                }
            }

            memoryDiagnosticCounters.Add(new SavedMemoryDiagnosticCounter
            {
                schemaVersion = 1,
                reasonToken = reasonToken,
                scopeToken = scopeToken,
                saturatedCount = 1
            });
        }
    }
}

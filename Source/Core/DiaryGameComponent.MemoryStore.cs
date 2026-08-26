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
using System.Globalization;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent : IMemoryLogicalSizeSource
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
        /// <summary>
        /// Saved tick boundary only; it is not a work row, queue, or retry backlog. Missing old-save
        /// value -1 baselines current truth on the first reconciled pass.
        /// </summary>
        private long optionalMeaningfulEligibilityBaselineTick = -1;
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
                ref optionalMeaningfulEligibilityBaselineTick,
                "optionalMeaningfulEligibilityBaselineTick", -1);
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
            ResetMemoryMaintenanceTransient(true);
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

            if (globalOptionalRequestCancellationGeneration == 0)
            {
                globalOptionalRequestCancellationGeneration = 1;
            }

            // Taxonomy derivation (§T6.9): mixed payload is inert MigrationPending until one
            // atomic plan chooses a complete representation; an already-CURRENT state with both
            // lists empty stays Current (empty-but-valid), never regressing to LegacyRaw.
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
            else if (unresolvedArchiveMigrationState != MemoryArchiveStates.MigrationPending
                && unresolvedArchiveMigrationState != MemoryArchiveStates.Current)
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
            invokedGenerationCutoffs.Reset();
            optionalMeaningfulEligibilityBaselineTick = -1;
            ResetMemoryMaintenanceTransient(true);
            ResetMemoryLibraryTransient();
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
            var currentOwners = new Dictionary<string, PawnKnowledgeState>(StringComparer.Ordinal);
            for (int i = 0; diaries != null && i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                if (diary == null || string.IsNullOrWhiteSpace(diary.pawnId)
                    || diary.knowledgeState == null
                    || !diary.knowledgeState.IsCurrentSchema())
                {
                    continue;
                }

                // Duplicate containers group by exact owner before publication. The physical first
                // holder is the current lookup owner (§T13.2), so request bytes are never added once
                // per duplicate container.
                if (!currentOwners.ContainsKey(diary.pawnId))
                {
                    currentOwners.Add(diary.pawnId, diary.knowledgeState);
                }
            }

            var currentOwnerIds = new HashSet<string>(currentOwners.Keys, StringComparer.Ordinal);
            Dictionary<string, long> activeRequestBytesByOwner;
            memoryComponentActiveBytesTotal = SizeComponentMemoryBytes(
                currentOwnerIds,
                out activeRequestBytesByOwner);

            foreach (KeyValuePair<string, PawnKnowledgeState> owner in currentOwners)
            {
                long requestBytes = activeRequestBytesByOwner.TryGetValue(
                    owner.Key, out long measuredRequestBytes)
                    ? measuredRequestBytes
                    : 0;
                MemoryOwnerByteTotals totals = MeasureOwner(owner.Value, requestBytes);
                memoryByteTotalsByOwner[owner.Key] = totals;
                if (!totals.valid)
                {
                    RecordMemoryDiagnostic("size_invalid", "owner");
                }
            }
            if (memoryM4IndexesDirty)
            {
                RebuildMemoryM4Indexes();
            }
        }

        private MemoryOwnerByteTotals MeasureOwner(
            PawnKnowledgeState state, long ownerAttributedActiveRequestBytes)
        {
            var totals = new MemoryOwnerByteTotals { valid = false, activeBytes = 0, importedBytes = 0 };
            MemoryLogicalSizeResult whole =
                MemoryLogicalPayloadSizer.Size(state);
            MemoryLogicalSizeResult imported = SizeListValidated(state?.importedArchiveRows);
            if (!whole.valid || !imported.valid || ownerAttributedActiveRequestBytes < 0)
            {
                return totals;
            }

            try
            {
                long activeWithoutRequests = checked(whole.totalBytes - imported.totalBytes);
                if (activeWithoutRequests < 0)
                {
                    return totals;
                }

                totals.activeBytes = checked(
                    activeWithoutRequests + ownerAttributedActiveRequestBytes);
                totals.importedBytes = imported.totalBytes;
                totals.valid = true;
            }
            catch (OverflowException)
            {
                // Any overflow invalidates the complete owner index; never publish a smaller total.
            }

            return totals;
        }

        /// <summary>
        /// Sizes the component-global memory metadata through the REGISTERED
        /// DiaryGameComponentMemory row shape (§T17.5): every §T6.9 scalar, the applied-policy
        /// singleton, and each owned list with its framing — then subtracts owner-attributed
        /// active-request bytes so they are charged exactly once to their owners. Returns -1 when
        /// ANY nested walk is invalid (invalid budget state propagates; never a silent undercount).
        /// </summary>
        private long SizeComponentMemoryBytes(
            HashSet<string> currentOwnerIds,
            out Dictionary<string, long> activeRequestBytesByOwner)
        {
            activeRequestBytesByOwner =
                new Dictionary<string, long>(StringComparer.Ordinal);
            MemoryLogicalSizeResult whole = MemoryLogicalPayloadSizer.Size(this);
            MemoryLogicalSizeResult unknownArchive =
                SizeListValidated(unresolvedOwnerArchiveRows);
            if (!whole.valid || !unknownArchive.valid)
            {
                RecordMemoryDiagnostic("size_invalid", "component");
                return -1;
            }

            // Owner-attributed request rows ride activeOwnerBytes (§T17.5); measure them here so
            // they can be excluded from the component subtotal without breaking registry order.
            try
            {
                long ownerAttributedComponentBytes = 0;
                long externallyStoredComponentBytes = 0;
                for (int i = 0;
                    activeMemoryCoordinatorRequests != null
                        && i < activeMemoryCoordinatorRequests.Count;
                    i++)
                {
                    if (!TryAttributeActiveRequestBytes(
                            activeMemoryCoordinatorRequests[i],
                            currentOwnerIds,
                            activeRequestBytesByOwner,
                            storedInsideComponentRow: true,
                            ref ownerAttributedComponentBytes,
                            ref externallyStoredComponentBytes)) return -1;
                }

                IReadOnlyList<DiaryEvent> hotEvents = events?.AllEvents;
                for (int index = 0; hotEvents != null && index < hotEvents.Count; index++)
                {
                    DiaryEvent diaryEvent = hotEvents[index];
                    if (!TryAttributeActiveRequestBytes(
                            diaryEvent?.ActiveMemoryLogicalRequestForRole(
                                DiaryEvent.InitiatorRole),
                            currentOwnerIds,
                            activeRequestBytesByOwner,
                            storedInsideComponentRow: false,
                            ref ownerAttributedComponentBytes,
                            ref externallyStoredComponentBytes)
                        || !TryAttributeActiveRequestBytes(
                            diaryEvent?.ActiveMemoryLogicalRequestForRole(
                                DiaryEvent.RecipientRole),
                            currentOwnerIds,
                            activeRequestBytesByOwner,
                            storedInsideComponentRow: false,
                            ref ownerAttributedComponentBytes,
                            ref externallyStoredComponentBytes)
                        || !TryAttributeActiveRequestBytes(
                            diaryEvent?.ActiveMemoryLogicalRequestForRole(
                                DiaryEvent.NeutralRole),
                            currentOwnerIds,
                            activeRequestBytesByOwner,
                            storedInsideComponentRow: false,
                            ref ownerAttributedComponentBytes,
                            ref externallyStoredComponentBytes)) return -1;
                }

                // Unknown Imported rows (including their one list framing prefix) move from the
                // registered component walk into globalImportedBytes. Owner-attributed request rows
                // likewise move to the exact owner. Each physical byte therefore appears once.
                long componentBytes = checked(
                    whole.totalBytes - ownerAttributedComponentBytes);
                componentBytes = checked(componentBytes - unknownArchive.totalBytes);
                componentBytes = checked(componentBytes + externallyStoredComponentBytes);
                if (componentBytes < 0)
                {
                    RecordMemoryDiagnostic("size_invalid", "component");
                    return -1;
                }

                return componentBytes;
            }
            catch (OverflowException)
            {
                RecordMemoryDiagnostic("size_invalid", "component");
                return -1;
            }
        }

        private bool TryAttributeActiveRequestBytes(
            SavedActiveLogicalRequestV1 request,
            HashSet<string> currentOwnerIds,
            Dictionary<string, long> activeRequestBytesByOwner,
            bool storedInsideComponentRow,
            ref long ownerAttributedComponentBytes,
            ref long externallyStoredComponentBytes)
        {
            if (request == null) return true;
            MemoryLogicalSizeResult result = MemoryLogicalPayloadSizer.Size(request);
            if (!result.valid)
            {
                RecordMemoryDiagnostic("size_invalid", "component");
                return false;
            }

            bool currentOwner = !string.IsNullOrWhiteSpace(request.ownerPawnId)
                && currentOwnerIds != null
                && currentOwnerIds.Contains(request.ownerPawnId);
            if (currentOwner)
            {
                long prior = activeRequestBytesByOwner.TryGetValue(
                    request.ownerPawnId, out long measured)
                    ? measured
                    : 0;
                activeRequestBytesByOwner[request.ownerPawnId] =
                    checked(prior + result.totalBytes);
                if (storedInsideComponentRow)
                {
                    ownerAttributedComponentBytes = checked(
                        ownerAttributedComponentBytes + result.totalBytes);
                }
            }
            else if (!storedInsideComponentRow)
            {
                // A DiaryEvent request is physically outside the registered component row. Orphaned
                // or ownerless metadata still belongs to the component/global subtotal exactly once.
                externallyStoredComponentBytes = checked(
                    externallyStoredComponentBytes + result.totalBytes);
            }
            return true;
        }

        /// <summary>
        /// Registry-ordered logical-size walker for the component's saved §T6.9 memory fields.
        /// This class also implements IMemoryLogicalSizeSource for the §T17.5 walk; field
        /// names/order mirror the frozen DiaryGameComponentMemory catalog row exactly.
        /// </summary>
        void IMemoryLogicalSizeSource.CollectFields(MemoryLogicalSizeCollector c)
        {
            c.BeginRow("DiaryGameComponentMemory");
            c.Int32("memoryComponentSchemaVersion", memoryComponentSchemaVersion);
            c.Int64("lastIssuedAutobiographicalEpochSequence",
                lastIssuedAutobiographicalEpochSequence);
            c.String("lastIssuedAutobiographicalEpochFallbackChain",
                lastIssuedAutobiographicalEpochFallbackChain ?? string.Empty);
            c.Int64("globalFactionSnapshotAllocatorGeneration",
                globalFactionSnapshotAllocatorGeneration);
            SizeRows(c, "globalFactionSnapshots", globalFactionSnapshots);
            SizeRows(c, "legacyOwnerEpochReservations", legacyOwnerEpochReservations);
            c.Int64("globalOptionalRequestCancellationGeneration",
                globalOptionalRequestCancellationGeneration);
            c.Int64("optionalMeaningfulEligibilityBaselineTick",
                optionalMeaningfulEligibilityBaselineTick);
            c.Int64("lastAppliedMemoryPolicyRevision", lastAppliedMemoryPolicyRevision);
            c.String("lastAppliedMemoryPolicyFingerprint",
                lastAppliedMemoryPolicyFingerprint ?? string.Empty);
            c.NullablePresence("lastAppliedMemoryPolicyState",
                lastAppliedMemoryPolicyState != null);
            if (lastAppliedMemoryPolicyState != null)
            {
                c.NestedRow(lastAppliedMemoryPolicyState);
            }

            SizeRows(c, "unresolvedOwnerArchiveRows", unresolvedOwnerArchiveRows);
            c.String("unresolvedArchiveMigrationState", unresolvedArchiveMigrationState ?? string.Empty);
            SizeRows(c, "rawUnresolvedOwnerArchiveInput", rawUnresolvedOwnerArchiveInput);
            c.Int64("rawUnresolvedArchiveReattributionGeneration",
                rawUnresolvedArchiveReattributionGeneration);
            c.Int64("unresolvedArchiveReattributionGeneration",
                unresolvedArchiveReattributionGeneration);
            c.Int64("unresolvedArchiveStructuralRevision", unresolvedArchiveStructuralRevision);
            c.Boolean("unresolvedArchiveReattributionDisabled",
                unresolvedArchiveReattributionDisabled);
            c.Int32("memoryCoordinatorSchemaVersion", memoryCoordinatorSchemaVersion);
            SizeRows(c, "summaryWordingOpportunities", summaryWordingOpportunities);
            SizeRows(c, "memoryDiagnosticCounters", memoryDiagnosticCounters);
            SizeRows(c, "memoryAttemptAuditRows", memoryAttemptAuditRows);
            c.Int32("memoryDispatchSchemaVersion", memoryDispatchSchemaVersion);
            c.Int64("lastIssuedMemoryLogicalRequestSequence",
                lastIssuedMemoryLogicalRequestSequence);
            SizeRows(c, "activeMemoryCoordinatorRequests", activeMemoryCoordinatorRequests);
            c.EndRow();
        }

        private static void SizeRows<T>(
            MemoryLogicalSizeCollector c, string fieldName, List<T> rows)
            where T : class, IMemoryLogicalSizeSource
        {
            int count = MemorySavedSizingUtil.NonNullCount(rows);
            c.ListCount(fieldName, count);
            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                if (rows[i] != null)
                {
                    c.NestedRow(rows[i]);
                }
            }
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
            // -1 component/owner bytes mean an invalid walk somewhere: propagate invalid budget
            // state (negative total) instead of admitting against a silent undercount (§T17.5).
            if (memoryComponentActiveBytesTotal < 0)
            {
                return new MemoryPayloadBudgetTotals { globalActiveBytes = -1, globalImportedBytes = 0 };
            }

            // Unknown-archive bytes count once toward global combined (§T17.5); invalid rows
            // inside it invalidate the whole Unknown unit rather than undercounting.
            MemoryLogicalSizeResult unknownRows = SizeListValidated(unresolvedOwnerArchiveRows);
            if (!unknownRows.valid)
            {
                return new MemoryPayloadBudgetTotals { globalActiveBytes = -1, globalImportedBytes = 0 };
            }

            try
            {
                long owners = 0;
                long imported = 0;
                foreach (MemoryOwnerByteTotals totals in memoryByteTotalsByOwner.Values)
                {
                    if (!totals.valid || totals.activeBytes < 0 || totals.importedBytes < 0)
                    {
                        return new MemoryPayloadBudgetTotals
                            { globalActiveBytes = -1, globalImportedBytes = 0 };
                    }

                    owners = checked(owners + totals.activeBytes);
                    imported = checked(imported + totals.importedBytes);
                }

                long globalActive = checked(owners + memoryComponentActiveBytesTotal);
                long globalImported = checked(imported + unknownRows.totalBytes);
                // Prove the combined value is representable now; downstream admission must never
                // receive two individually valid subtotals whose sum wraps.
                checked
                {
                    long ignoredCombined = globalActive + globalImported;
                }

                return new MemoryPayloadBudgetTotals
                {
                    globalActiveBytes = globalActive,
                    globalImportedBytes = globalImported
                };
            }
            catch (OverflowException)
            {
                return new MemoryPayloadBudgetTotals { globalActiveBytes = -1, globalImportedBytes = 0 };
            }
        }

        /// <summary>
        /// Checks the current exact active-request row and byte budgets before a detached staged row
        /// becomes canonical. All totals come from the shared logical sizer; malformed indexes or
        /// capacity values fail closed rather than admitting against an undercount.
        /// </summary>
        private bool CanAdmitActiveMemoryRequest(SavedActiveLogicalRequestV1 request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ownerPawnId)) return false;
            MemoryLogicalSizeResult requestSize = MemoryLogicalPayloadSizer.Size(request);
            if (!requestSize.valid || requestSize.totalBytes < 0) return false;

            RebuildMemorySizeIndexes();
            MemoryOwnerByteTotals owner = GetOwnerByteTotals(request.ownerPawnId);
            MemoryPayloadBudgetTotals global = GetGlobalBudgetTotals();
            if (!owner.valid || global.globalActiveBytes < 0
                || global.globalImportedBytes < 0) return false;

            int ownerRequests = 0;
            int globalRequests = 0;
            CountActiveRequests(activeMemoryCoordinatorRequests, request.ownerPawnId,
                ref ownerRequests, ref globalRequests);
            IReadOnlyList<DiaryEvent> hotEvents = events?.AllEvents;
            for (int index = 0; hotEvents != null && index < hotEvents.Count; index++)
            {
                DiaryEvent diaryEvent = hotEvents[index];
                CountActiveRequest(diaryEvent?.ActiveMemoryLogicalRequestForRole(
                    DiaryEvent.InitiatorRole), request.ownerPawnId,
                    ref ownerRequests, ref globalRequests);
                CountActiveRequest(diaryEvent?.ActiveMemoryLogicalRequestForRole(
                    DiaryEvent.RecipientRole), request.ownerPawnId,
                    ref ownerRequests, ref globalRequests);
                CountActiveRequest(diaryEvent?.ActiveMemoryLogicalRequestForRole(
                    DiaryEvent.NeutralRole), request.ownerPawnId,
                    ref ownerRequests, ref globalRequests);
            }

            int ownerRequestCap;
            int globalRequestCap;
            ReadCapacityPair(
                "activeRequestsOwnerGlobal",
                8,
                128,
                32,
                512,
                out ownerRequestCap,
                out globalRequestCap);
            if (ownerRequests >= ownerRequestCap || globalRequests >= globalRequestCap)
                return false;

            MemoryBudgetLimits limits = new MemoryBudgetLimits
            {
                activeOwnerBytes = ReadCapacityLong(
                    "activeOwnerBytes", 196608, 2097152),
                combinedOwnerBytes = ReadCapacityLong(
                    "combinedOwnerBytes", 262144, 4194304),
                activeGlobalBytes = ReadCapacityLong(
                    "activeGlobalBytes", 6291456, 25165824),
                combinedGlobalBytes = ReadCapacityLong(
                    "combinedGlobalBytes", 8388608, 33554432)
            };
            MemoryBudgetDecision decision = ActiveMemoryPayloadBudget.TryAdmit(
                limits,
                owner.activeBytes,
                owner.importedBytes,
                requestSize.totalBytes,
                0,
                global);
            return decision.outcome == MemoryBudgetOutcome.Admitted;
        }

        private static void CountActiveRequests(
            List<SavedActiveLogicalRequestV1> requests,
            string ownerPawnId,
            ref int ownerCount,
            ref int globalCount)
        {
            for (int index = 0; requests != null && index < requests.Count; index++)
                CountActiveRequest(requests[index], ownerPawnId, ref ownerCount, ref globalCount);
        }

        private static void CountActiveRequest(
            SavedActiveLogicalRequestV1 request,
            string ownerPawnId,
            ref int ownerCount,
            ref int globalCount)
        {
            if (request == null) return;
            globalCount++;
            if (string.Equals(request.ownerPawnId, ownerPawnId, StringComparison.Ordinal))
                ownerCount++;
        }

        /// <summary>
        /// Moves terminal attempt metadata into the bounded Dev audit before its active request row
        /// is removed. Duplicate callback/load settlement is idempotent by request ID + ordinal.
        /// </summary>
        private void AppendTerminalMemoryAttemptAudits(
            SavedActiveLogicalRequestV1 request,
            string fallbackOutcomeToken,
            long fallbackTerminalTick)
        {
            if (request == null || request.activeAttempts == null) return;
            memoryAttemptAuditRows = memoryAttemptAuditRows
                ?? new List<SavedMemoryAttemptAuditRow>();
            long safeTerminalTick = Math.Max(1, fallbackTerminalTick);
            for (int index = 0; index < request.activeAttempts.Count; index++)
            {
                SavedActiveLogicalAttemptV1 attempt = request.activeAttempts[index];
                if (attempt == null || attempt.attemptOrdinal <= 0
                    || AuditContains(request.logicalRequestId, attempt.attemptOrdinal)) continue;
                string outcome = MemoryDispatchTokens.IsTerminalOutcome(
                        attempt.terminalOutcomeToken)
                    ? attempt.terminalOutcomeToken
                    : fallbackOutcomeToken;
                if (!MemoryDispatchTokens.IsTerminalOutcome(outcome))
                    outcome = MemoryDispatchTokens.Invalid;
                memoryAttemptAuditRows.Add(new SavedMemoryAttemptAuditRow
                {
                    schemaVersion = 1,
                    logicalRequestId = request.logicalRequestId ?? string.Empty,
                    requestPurposeToken = request.requestPurposeToken ?? string.Empty,
                    ownerPawnId = request.ownerPawnId ?? string.Empty,
                    ownerEpochToken = request.ownerEpochToken ?? string.Empty,
                    attemptOrdinal = attempt.attemptOrdinal,
                    variantKey = attempt.variantKey ?? string.Empty,
                    invocationTick = attempt.invocationTick,
                    terminalTick = attempt.terminalTick > 0
                        ? attempt.terminalTick
                        : safeTerminalTick,
                    outcomeToken = outcome,
                    potentialExposure = attempt.potentialExposureApplied
                });
            }

            int perRequestCap;
            int globalCap;
            ReadCapacityPair(
                "attemptAuditRowsPerRequestGlobal",
                4,
                1024,
                16,
                4096,
                out perRequestCap,
                out globalCap);
            TrimMemoryAttemptAudits(perRequestCap, globalCap);
        }

        private bool AuditContains(string logicalRequestId, int attemptOrdinal)
        {
            for (int index = 0;
                memoryAttemptAuditRows != null && index < memoryAttemptAuditRows.Count;
                index++)
            {
                SavedMemoryAttemptAuditRow row = memoryAttemptAuditRows[index];
                if (row != null && row.attemptOrdinal == attemptOrdinal
                    && string.Equals(row.logicalRequestId, logicalRequestId,
                        StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private void TrimMemoryAttemptAudits(int perRequestCap, int globalCap)
        {
            memoryAttemptAuditRows.Sort(CompareMemoryAttemptAuditAge);
            Dictionary<string, int> countByRequest =
                new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < memoryAttemptAuditRows.Count; index++)
            {
                string requestId = memoryAttemptAuditRows[index]?.logicalRequestId
                    ?? string.Empty;
                countByRequest[requestId] = countByRequest.TryGetValue(
                    requestId, out int count) ? count + 1 : 1;
            }
            for (int index = 0; index < memoryAttemptAuditRows.Count;)
            {
                string requestId = memoryAttemptAuditRows[index]?.logicalRequestId
                    ?? string.Empty;
                if (countByRequest[requestId] > perRequestCap)
                {
                    memoryAttemptAuditRows.RemoveAt(index);
                    countByRequest[requestId]--;
                }
                else index++;
            }
            while (memoryAttemptAuditRows.Count > globalCap)
                memoryAttemptAuditRows.RemoveAt(0);
        }

        private static int CompareMemoryAttemptAuditAge(
            SavedMemoryAttemptAuditRow left,
            SavedMemoryAttemptAuditRow right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            int compared = left.terminalTick.CompareTo(right.terminalTick);
            if (compared != 0) return compared;
            compared = string.CompareOrdinal(left.logicalRequestId, right.logicalRequestId);
            return compared != 0
                ? compared
                : left.attemptOrdinal.CompareTo(right.attemptOrdinal);
        }

        private static long ReadCapacityLong(
            string name,
            long fallback,
            long defensiveCeiling)
        {
            DiaryKnowledgeTuningDef tuning = DefDatabase<DiaryKnowledgeTuningDef>
                .GetNamedSilentFail(DiaryKnowledgePolicy.TuningDefName);
            for (int index = 0; tuning?.memoryCapacityVector != null
                && index < tuning.memoryCapacityVector.Count; index++)
            {
                DiaryMemoryCapacityValueRow row = tuning.memoryCapacityVector[index];
                long parsed;
                if (row != null && string.Equals(row.name, name, StringComparison.Ordinal)
                    && long.TryParse(row.valueEncoding, NumberStyles.None,
                        CultureInfo.InvariantCulture, out parsed)
                    && parsed >= 0 && parsed <= defensiveCeiling) return parsed;
            }
            return fallback;
        }

        private static void ReadCapacityPair(
            string name,
            int firstFallback,
            int secondFallback,
            int firstDefensiveCeiling,
            int secondDefensiveCeiling,
            out int first,
            out int second)
        {
            first = firstFallback;
            second = secondFallback;
            DiaryKnowledgeTuningDef tuning = DefDatabase<DiaryKnowledgeTuningDef>
                .GetNamedSilentFail(DiaryKnowledgePolicy.TuningDefName);
            for (int index = 0; tuning?.memoryCapacityVector != null
                && index < tuning.memoryCapacityVector.Count; index++)
            {
                DiaryMemoryCapacityValueRow row = tuning.memoryCapacityVector[index];
                if (row == null || !string.Equals(row.name, name, StringComparison.Ordinal))
                    continue;
                string[] parts = (row.valueEncoding ?? string.Empty).Split('/');
                int parsedFirst;
                int parsedSecond;
                if (parts.Length == 2
                    && int.TryParse(parts[0], NumberStyles.None,
                        CultureInfo.InvariantCulture, out parsedFirst)
                    && int.TryParse(parts[1], NumberStyles.None,
                        CultureInfo.InvariantCulture, out parsedSecond)
                    && parsedFirst >= 0 && parsedFirst <= firstDefensiveCeiling
                    && parsedSecond >= 0 && parsedSecond <= secondDefensiveCeiling)
                {
                    first = parsedFirst;
                    second = parsedSecond;
                }
                return;
            }
        }

        /// <summary>Reads one slash-delimited capacity-vector coordinate with defensive bounds.</summary>
        private static long ReadCapacityTuplePart(
            string name,
            int partIndex,
            long fallback,
            long defensiveCeiling)
        {
            if (partIndex < 0) return fallback;
            DiaryKnowledgeTuningDef tuning = DefDatabase<DiaryKnowledgeTuningDef>
                .GetNamedSilentFail(DiaryKnowledgePolicy.TuningDefName);
            for (int index = 0; tuning?.memoryCapacityVector != null
                && index < tuning.memoryCapacityVector.Count; index++)
            {
                DiaryMemoryCapacityValueRow row = tuning.memoryCapacityVector[index];
                if (row == null || !string.Equals(row.name, name, StringComparison.Ordinal)) continue;
                string[] parts = (row.valueEncoding ?? string.Empty).Split('/');
                long parsed;
                return partIndex < parts.Length
                    && long.TryParse(parts[partIndex], NumberStyles.None,
                        CultureInfo.InvariantCulture, out parsed)
                    && parsed >= 0 && parsed <= defensiveCeiling
                        ? parsed
                        : fallback;
            }
            return fallback;
        }

        /// <summary>Sizes one deep list of rows with the shared framing/count rule; validity of
        /// every element propagates (invalid → valid=false) so callers never admit on partials.</summary>
        private static MemoryLogicalSizeResult SizeListValidated<T>(List<T> rows)
            where T : class, IMemoryLogicalSizeSource
        {
            long bytes = 4; // list-count prefix
            try
            {
                for (int i = 0; rows != null && i < rows.Count; i++)
                {
                    if (rows[i] == null)
                    {
                        continue;
                    }

                    MemoryLogicalSizeResult result = MemoryLogicalPayloadSizer.Size(rows[i]);
                    if (!result.valid)
                    {
                        return MemoryLogicalSizeResult.Invalid("list-element:" + i);
                    }

                    bytes = checked(bytes + result.totalBytes);
                }
            }
            catch (OverflowException)
            {
                return MemoryLogicalSizeResult.Invalid("list-total:overflow");
            }

            return new MemoryLogicalSizeResult
            {
                valid = true,
                totalBytes = bytes,
                errorPath = string.Empty
            };
        }

        /// <summary>Allowlisted reason tokens (§T6.9: unknown bounded tokens fold into "other"
        /// rather than retaining prose). Extend only with new stable, reviewed reasons.</summary>
        private static readonly string[] DiagnosticReasonAllowlist =
        {
            "legacy_dry_run",
            "legacy_owner_raw",
            "legacy_automatic_duplicate",
            "legacy_authored_conflict",
            "legacy_report_truncated",
            "size_invalid",
            "capacity_refused",
            "newer_reducer_revision",
            "repair_refused",
            MemoryThreadRepairPolicy.AutomaticConflictDiagnosticToken,
            "other"
        };

        private const int MaximumDiagnosticCounterRows = 64;

        // ---- §T13.2 allocator-carrier registry: exhaustive scan → pure plan → atomic publish ----

        /// <summary>
        /// Walks EVERY §T13.2 carrier family in the loaded save (envelope/root/block/guard/
        /// awareness-episode/opportunity/request/audit epoch tokens, reservation sequences,
        /// faction generations), plans the repaired high-waters through the pure registry, and —
        /// when the plan is publishable — commits high-water/reservation repairs in one block.
        /// Must run BEFORE any epoch allocation, reservation, semantic migration, or Brainwipe.
        /// </summary>
        internal void CollectAndPublishAllocatorCarriers()
        {
            var input = new MemorySavedCarrierScanInput
            {
                lastIssuedAutobiographicalEpochSequence = lastIssuedAutobiographicalEpochSequence,
                lastIssuedAutobiographicalEpochFallbackChain =
                    lastIssuedAutobiographicalEpochFallbackChain ?? string.Empty,
                globalFactionSnapshotAllocatorGeneration = globalFactionSnapshotAllocatorGeneration
            };

            foreach (SavedLegacyOwnerEpochReservation reservation in legacyOwnerEpochReservations)
            {
                input.legacyReservations.Add(new MemoryLegacyEpochReservationInput
                {
                    ownerPawnId = reservation.ownerPawnId ?? string.Empty,
                    reservedEpochSequence = reservation.reservedEpochSequence
                });
            }

            foreach (SavedGlobalFactionSnapshot snapshot in globalFactionSnapshots)
            {
                if (snapshot != null)
                {
                    input.factionAllocatorGenerationCarriers.Add(snapshot.allocatorGeneration);
                }
            }

            if (diaries != null)
            {
                for (int i = 0; i < diaries.Count; i++)
                {
                    PawnDiaryRecord diary = diaries[i];
                    PawnKnowledgeState state = diary?.knowledgeState;
                    if (state == null)
                    {
                        continue;
                    }

                    AddKnowledgeEpochTokenCarriers(input.epochTokenCarriers, state);
                    if (diary.reflectionState != null)
                    {
                        AddEpochToken(input.epochTokenCarriers,
                            diary.reflectionState.memoryOwnerEpochToken);
                    }
                }
            }

            for (int i = 0; i < summaryWordingOpportunities.Count; i++)
            {
                AddEpochToken(input.epochTokenCarriers,
                    summaryWordingOpportunities[i]?.ownerEpochToken);
            }

            for (int i = 0; i < memoryAttemptAuditRows.Count; i++)
            {
                AddEpochToken(input.epochTokenCarriers,
                    memoryAttemptAuditRows[i]?.ownerEpochToken);
            }

            AddRequestEpochTokens(input.epochTokenCarriers, activeMemoryCoordinatorRequests);
            AddEventRequestEpochTokens(input.epochTokenCarriers);

            MemorySavedCarrierRegistryPlan plan =
                MemorySavedIdentityCarrierRegistry.Plan(input);
            if (!plan.canPublish)
            {
                RecordMemoryDiagnostic("other", "component");
                return;
            }

            // Atomic publication of every repaired allocator field (§T13.2).
            lastIssuedAutobiographicalEpochSequence = plan.repairedAutobiographicalHighWater;
            lastIssuedAutobiographicalEpochFallbackChain = plan.effectiveFallbackChain;
            globalFactionSnapshotAllocatorGeneration = plan.globalFactionAllocatorGeneration;
            legacyOwnerEpochReservations.Clear();
            for (int i = 0; i < plan.normalizedReservations.Count; i++)
            {
                legacyOwnerEpochReservations.Add(new SavedLegacyOwnerEpochReservation
                {
                    schemaVersion = 1,
                    ownerPawnId = plan.normalizedReservations[i].ownerPawnId,
                    reservedEpochSequence = plan.normalizedReservations[i].reservedEpochSequence
                });
            }

            if (plan.factionGenerationSaturated)
            {
                RecordMemoryDiagnostic("other", "component");
            }
        }

        /// <summary>
        /// Recursively collects every direct epoch-token field inside one owner envelope. In
        /// particular, root-owned visible/rolling blocks are carriers in their own right and may be
        /// the sole high-water witness in a corrupt-low save (§T13.2).
        /// </summary>
        internal static void AddKnowledgeEpochTokenCarriers(
            List<string> carriers, PawnKnowledgeState state)
        {
            if (carriers == null || state == null)
            {
                return;
            }

            AddEpochToken(carriers, state.autobiographicalEpochToken);
            AddEpochTokens(carriers, state.standaloneBlocks, b => b?.ownerEpochToken);
            AddEpochTokens(carriers, state.repetitionGuardRows, r => r?.ownerEpochToken);
            for (int i = 0; state.threadRoots != null && i < state.threadRoots.Count; i++)
            {
                SavedMemoryThreadRoot root = state.threadRoots[i];
                if (root == null)
                {
                    continue;
                }

                AddEpochToken(carriers, root.ownerEpochToken);
                AddEpochTokens(carriers, root.visibleBlocks, b => b?.ownerEpochToken);
                AddEpochToken(carriers, root.rollingSummaryBlock?.ownerEpochToken);
            }
        }

        /// <summary>
        /// Returns every currently saved autobiographical epoch carrier after high-water repair.
        /// Target Brainwipe passes this detached set to the checked allocator so a corrupt-low
        /// counter or fallback-chain repair cannot reuse an old epoch.
        /// </summary>
        private List<string> SnapshotAutobiographicalEpochCarriers()
        {
            List<string> carriers = new List<string>();
            if (diaries != null)
            {
                for (int index = 0; index < diaries.Count; index++)
                {
                    PawnDiaryRecord diary = diaries[index];
                    if (diary?.knowledgeState != null)
                    {
                        AddKnowledgeEpochTokenCarriers(carriers, diary.knowledgeState);
                    }
                    AddEpochToken(carriers, diary?.reflectionState?.memoryOwnerEpochToken);
                }
            }
            AddEpochTokens(carriers, summaryWordingOpportunities, row => row?.ownerEpochToken);
            AddEpochTokens(carriers, memoryAttemptAuditRows, row => row?.ownerEpochToken);
            AddRequestEpochTokens(carriers, activeMemoryCoordinatorRequests);
            AddEventRequestEpochTokens(carriers);
            return carriers;
        }

        private void AddEventRequestEpochTokens(List<string> carriers)
        {
            IReadOnlyList<DiaryEvent> hotEvents = events?.AllEvents;
            for (int index = 0; hotEvents != null && index < hotEvents.Count; index++)
            {
                DiaryEvent diaryEvent = hotEvents[index];
                AddEpochToken(carriers, diaryEvent?.ActiveMemoryLogicalRequestForRole(
                    DiaryEvent.InitiatorRole)?.ownerEpochToken);
                AddEpochToken(carriers, diaryEvent?.ActiveMemoryLogicalRequestForRole(
                    DiaryEvent.RecipientRole)?.ownerEpochToken);
                AddEpochToken(carriers, diaryEvent?.ActiveMemoryLogicalRequestForRole(
                    DiaryEvent.NeutralRole)?.ownerEpochToken);
            }
        }

        private static void AddEpochToken(List<string> carriers, string token)
        {
            if (!string.IsNullOrEmpty(token))
            {
                carriers.Add(token);
            }
        }

        private static void AddEpochTokens<T>(
            List<string> carriers, List<T> rows, Func<T, string> selector) where T : class
        {
            if (rows == null)
            {
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                T row = rows[i];
                if (row != null)
                {
                    AddEpochToken(carriers, selector(row));
                }
            }
        }

        private static void AddRequestEpochTokens(
            List<string> carriers, List<SavedActiveLogicalRequestV1> requests)
        {
            if (requests == null)
            {
                return;
            }

            for (int i = 0; i < requests.Count; i++)
            {
                AddEpochToken(carriers, requests[i]?.ownerEpochToken);
            }
        }

        /// <summary>Adds or coalesces one bounded diagnostic counter. Unknown reason/scope tokens
        /// fold into the allowlisted "other" row (never prose/pawnIds); rows cap at 64 with
        /// overflow coalescing; counts stick at long.MaxValue (§T6.9).</summary>
        internal void RecordMemoryDiagnostic(string reasonToken, string scopeToken)
        {
            if (!IsAllowlistedReason(reasonToken))
            {
                reasonToken = "other";
            }

            // Scopes are component-level buckets, never raw owner ids or free text.
            if (scopeToken != "owner" && scopeToken != "component")
            {
                scopeToken = "other";
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

            if (memoryDiagnosticCounters.Count >= MaximumDiagnosticCounterRows)
            {
                // Row-cap overflow coalesces into one allowlisted saturated row.
                RecordMemoryDiagnostic("other", "other");
                return;
            }

            memoryDiagnosticCounters.Add(new SavedMemoryDiagnosticCounter
            {
                schemaVersion = 1,
                reasonToken = reasonToken,
                scopeToken = scopeToken,
                saturatedCount = 1
            });
        }

        /// <summary>
        /// Adds one compatibility/repair diagnostic row without incrementing it on every maintenance
        /// snapshot rebuild. The saved row itself is the bounded once-marker for this save.
        /// </summary>
        internal void RecordMemoryDiagnosticOnce(string reasonToken, string scopeToken)
        {
            string normalizedReason = IsAllowlistedReason(reasonToken) ? reasonToken : "other";
            string normalizedScope = scopeToken == "owner" || scopeToken == "component"
                ? scopeToken : "other";
            for (int i = 0; i < memoryDiagnosticCounters.Count; i++)
            {
                SavedMemoryDiagnosticCounter counter = memoryDiagnosticCounters[i];
                if (counter != null && counter.reasonToken == normalizedReason
                    && counter.scopeToken == normalizedScope) return;
            }
            RecordMemoryDiagnostic(normalizedReason, normalizedScope);
        }

        private static bool IsAllowlistedReason(string reasonToken)
        {
            for (int i = 0; i < DiagnosticReasonAllowlist.Length; i++)
            {
                if (string.Equals(DiagnosticReasonAllowlist[i], reasonToken, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

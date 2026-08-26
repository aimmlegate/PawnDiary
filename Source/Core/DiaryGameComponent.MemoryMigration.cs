// DiaryGameComponent.MemoryMigration.cs — impure adapter for legacy-memory dry-run reporting and
// the final M11 per-owner commit transaction (§§T13.1–T13.5).
//
// LegacyShadow remains report-only. CurrentRelease reuses the same pure plan, reserves an epoch,
// constructs the complete replacement off to the side, verifies whole-owner/global bounds, then
// publishes every duplicate container in one block. Neither path creates events/pages/requests.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private sealed class LegacyOwnerCommitPlan
        {
            public string ownerPawnId = string.Empty;
            public string epochToken = string.Empty;
            public PawnKnowledgeState replacement;
            public MemoryLegacyMigrationReport report;
            public List<PawnKnowledgeState> holderKnowledgeStates =
                new List<PawnKnowledgeState>();
            public List<PawnReflectionState> holderReflectionStates =
                new List<PawnReflectionState>();
            public List<SavedLegacyOwnerEpochReservation> remainingEpochReservations =
                new List<SavedLegacyOwnerEpochReservation>();
        }

        /// <summary>Selects report-only or commit migration from the single activation gate.</summary>
        private void RunMemoryMigration()
        {
            if (MemorySystemActivationGate.IsCurrentRelease)
                RunMemoryMigrationCommit();
            else
                RunMemoryMigrationDryRunReport();
        }

        /// <summary>
        /// Runs the bounded per-owner dry-run migration report over every envelope that still
        /// carries legacy records. Duplicate containers group by exact ordinal pawn ID FIRST
        /// (§T13.2), owners process in ordinal order up to the bounded cap, and failure is
        /// isolated like every other load repair: a throw here must never abort the load.
        /// </summary>
        private void RunMemoryMigrationDryRunReport()
        {
            if (diaries == null)
            {
                return;
            }

            List<MemoryLegacyRuleMapEntry> ruleMap = SnapshotLegacyRuleMap();
            long maxKnownTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

            // Group ALL containers by exact owner id before planning (§T13.2 duplicate-container
            // rule). A current-shape envelope whose records list still holds LegacyShadow-era
            // captures participates too — mixed-format rows must never hide from the planner.
            SortedDictionary<string, List<MemoryLegacyRecordSnapshot>> ownersByPawnId =
                new SortedDictionary<string, List<MemoryLegacyRecordSnapshot>>(
                    StringComparer.Ordinal);
            foreach (PawnDiaryRecord diary in diaries)
            {
                if (diary == null || string.IsNullOrWhiteSpace(diary.pawnId)
                    || diary.knowledgeState == null
                    || diary.knowledgeState.records == null
                    || diary.knowledgeState.records.Count == 0)
                {
                    continue;
                }

                if (!ownersByPawnId.TryGetValue(diary.pawnId, out var bucket))
                {
                    bucket = new List<MemoryLegacyRecordSnapshot>();
                    ownersByPawnId[diary.pawnId] = bucket;
                }

                foreach (ImportantMemoryRecord record in diary.knowledgeState.records)
                {
                    // Raw-preservation rule (§T13.1): the snapshot copies the loaded shape as-is —
                    // no semantic Normalize of tokens, no list alignment beyond null-safety.
                    bucket.Add(SnapshotLegacyRecord(record));
                }
            }

            int reportedOwners = 0;
            bool truncated = false;
            foreach (KeyValuePair<string, List<MemoryLegacyRecordSnapshot>> owner
                in ownersByPawnId)
            {
                if (reportedOwners >= 64)
                {
                    truncated = true;
                    break;
                }

                var input = new MemoryLegacyOwnerMigrationInput
                {
                    ownerPawnId = owner.Key,
                    // Epoch resolution/reservation arrives with the M11 commit slice; dry-run
                    // reports mapping only and never stamps this token.
                    ownerEpochToken = string.Empty,
                    maxKnownTick = maxKnownTick,
                    ruleMap = ruleMap
                };
                input.records.AddRange(owner.Value);

                MemoryLegacyMigrationReport report =
                    MemoryThreadMigrationPolicy.PlanDryRun(input);
                RecordMemoryDiagnostic("legacy_dry_run", "owner");
                if (report.ownerRemainsRaw)
                {
                    RecordMemoryDiagnostic("legacy_owner_raw", "owner");
                }

                if (report.droppedAutomaticAlternateCount > 0)
                {
                    RecordMemoryDiagnostic("legacy_automatic_duplicate", "owner");
                }

                if (report.archivedAuthoredConflictCount > 0)
                {
                    RecordMemoryDiagnostic("legacy_authored_conflict", "owner");
                }

                reportedOwners++;
            }

            if (truncated)
            {
                RecordMemoryDiagnostic("legacy_report_truncated", "component");
            }
        }

        /// <summary>
        /// Commits legacy owners independently and atomically. A refused owner retains every raw
        /// row plus its saved epoch reservation, so a later compatible build can retry without
        /// allocating a second identity. This seam is internal for loaded-component fixtures; the
        /// normal lifecycle reaches it only through <see cref="RunMemoryMigration"/>.
        /// </summary>
        internal void RunMemoryMigrationCommit()
        {
            if (diaries == null) return;

            List<MemoryLegacyRuleMapEntry> ruleMap = SnapshotLegacyRuleMap();
            long maxKnownTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            var groups = new SortedDictionary<string, List<PawnDiaryRecord>>(
                StringComparer.Ordinal);
            for (int index = 0; index < diaries.Count; index++)
            {
                PawnDiaryRecord diary = diaries[index];
                if (diary == null || string.IsNullOrWhiteSpace(diary.pawnId)) continue;
                if (!groups.TryGetValue(diary.pawnId, out List<PawnDiaryRecord> holders))
                {
                    holders = new List<PawnDiaryRecord>();
                    groups.Add(diary.pawnId, holders);
                }
                holders.Add(diary);
            }

            int ownerCap = (int)ReadCapacityLong("importedOwnerCount", 1000, 4000);
            int visited = 0;
            RebuildMemorySizeIndexes();
            foreach (KeyValuePair<string, List<PawnDiaryRecord>> group in groups)
            {
                if (!GroupCarriesLegacyRows(group.Value)) continue;
                if (visited++ >= ownerCap)
                {
                    RecordMemoryDiagnostic("capacity_refused", "owner");
                    continue;
                }

                try
                {
                    LegacyOwnerCommitPlan commit;
                    if (!TryPrepareLegacyOwnerCommit(
                            group.Key, group.Value, ruleMap, maxKnownTick, out commit))
                    {
                        RecordMemoryDiagnostic("legacy_owner_raw", "owner");
                        continue;
                    }

                    PublishLegacyOwnerCommit(group.Value, commit);
                    MarkMemoryM4IndexesDirty();
                    RebuildMemorySizeIndexes();
                    if (commit.report.droppedAutomaticAlternateCount > 0)
                        RecordMemoryDiagnostic("legacy_automatic_duplicate", "owner");
                    if (commit.report.archivedAuthoredConflictCount > 0)
                        RecordMemoryDiagnostic("legacy_authored_conflict", "owner");
                }
                catch (Exception)
                {
                    // Per-owner isolation is intentional: a malformed modded payload must not
                    // prevent unrelated pawns from loading or destroy this owner's raw retry input.
                    RecordMemoryDiagnostic("legacy_owner_raw", "owner");
                }
            }

            TryCommitUnresolvedLegacyArchive(maxKnownTick);
            if ((rawUnresolvedOwnerArchiveInput?.Count ?? 0) == 0)
                unresolvedArchiveMigrationState = MemoryArchiveStates.Current;
        }

        private static bool GroupCarriesLegacyRows(List<PawnDiaryRecord> holders)
        {
            for (int index = 0; holders != null && index < holders.Count; index++)
                if ((holders[index]?.knowledgeState?.records?.Count ?? 0) > 0) return true;
            return false;
        }

        private bool TryPrepareLegacyOwnerCommit(
            string ownerPawnId,
            List<PawnDiaryRecord> holders,
            List<MemoryLegacyRuleMapEntry> ruleMap,
            long maxKnownTick,
            out LegacyOwnerCommitPlan commit)
        {
            commit = null;
            string epochToken;
            if (!TryResolveLegacyOwnerEpoch(ownerPawnId, holders, out epochToken)) return false;

            var input = new MemoryLegacyOwnerMigrationInput
            {
                ownerPawnId = ownerPawnId,
                ownerEpochToken = epochToken,
                maxKnownTick = maxKnownTick,
                ruleMap = ruleMap
            };
            for (int holderIndex = 0; holderIndex < holders.Count; holderIndex++)
            {
                List<ImportantMemoryRecord> rows = holders[holderIndex]?.knowledgeState?.records;
                for (int rowIndex = 0; rows != null && rowIndex < rows.Count; rowIndex++)
                    input.records.Add(SnapshotLegacyRecord(rows[rowIndex]));
            }

            MemoryLegacyMigrationReport report = MemoryThreadMigrationPolicy.PlanDryRun(input);
            if (report.ownerRemainsRaw || string.IsNullOrWhiteSpace(report.reportFingerprint))
                return false;

            PawnKnowledgeState replacement;
            if (!TryBuildLegacyOwnerReplacement(
                    ownerPawnId, epochToken, holders, report, maxKnownTick, out replacement)
                || !LegacyReplacementWithinBounds(ownerPawnId, replacement)) return false;

            commit = new LegacyOwnerCommitPlan
            {
                ownerPawnId = ownerPawnId,
                epochToken = epochToken,
                replacement = replacement,
                report = report
            };
            BuildLegacyPublicationProjections(holders, commit);
            return true;
        }

        /// <summary>
        /// Reuses the group's sole valid epoch or publishes one normal-sequence reservation before
        /// any semantic conversion. Conflicting/malformed epochs and fallback-only allocation stay
        /// raw because the v1 reservation row can represent only a positive numeric sequence.
        /// </summary>
        private bool TryResolveLegacyOwnerEpoch(
            string ownerPawnId,
            List<PawnDiaryRecord> holders,
            out string epochToken)
        {
            epochToken = string.Empty;
            var epochs = new SortedSet<string>(StringComparer.Ordinal);
            for (int index = 0; holders != null && index < holders.Count; index++)
            {
                var carriers = new List<string>();
                AddKnowledgeEpochTokenCarriers(carriers, holders[index]?.knowledgeState);
                string reflectionEpoch = holders[index]?.reflectionState?.memoryOwnerEpochToken
                    ?? string.Empty;
                if (!string.IsNullOrEmpty(reflectionEpoch)) carriers.Add(reflectionEpoch);
                for (int carrierIndex = 0; carrierIndex < carriers.Count; carrierIndex++)
                {
                    string candidate = carriers[carrierIndex] ?? string.Empty;
                    if (candidate.Length == 0) continue;
                    bool ignoredFallback;
                    if (!MemoryIdentityCodec.TryValidateEpochToken(candidate, out ignoredFallback))
                        return false;
                    epochs.Add(candidate);
                }
            }
            if (epochs.Count > 1) return false;
            foreach (string existing in epochs)
            {
                epochToken = existing;
                return true;
            }

            long reservedSequence = 0;
            for (int index = 0; legacyOwnerEpochReservations != null
                && index < legacyOwnerEpochReservations.Count; index++)
            {
                SavedLegacyOwnerEpochReservation reservation =
                    legacyOwnerEpochReservations[index];
                if (reservation == null
                    || !string.Equals(reservation.ownerPawnId, ownerPawnId,
                        StringComparison.Ordinal)) continue;
                if (reservation.reservedEpochSequence <= 0
                    || (reservedSequence > 0
                        && reservedSequence != reservation.reservedEpochSequence)) return false;
                reservedSequence = reservation.reservedEpochSequence;
            }
            if (reservedSequence > 0)
            {
                if (!MemoryIdentityCodec.TryCreateNormalEpochToken(
                        reservedSequence, out epochToken)) return false;
                List<string> live = SnapshotAutobiographicalEpochCarriers();
                return live == null || !live.Contains(epochToken);
            }

            int reservationCap = (int)ReadCapacityLong("legacyEpochReservations", 64, 256);
            if ((legacyOwnerEpochReservations?.Count ?? 0) >= reservationCap) return false;
            MemoryEpochAllocationPlan allocation = MemoryIdentityCodec.PlanEpochAllocation(
                new MemoryEpochAllocationRequest
                {
                    ownerPawnId = ownerPawnId,
                    lastIssuedSequence = lastIssuedAutobiographicalEpochSequence,
                    fallbackChain = lastIssuedAutobiographicalEpochFallbackChain ?? string.Empty,
                    liveEpochCarriers = SnapshotAutobiographicalEpochCarriers(),
                    isTargetBrainwipe = false
                });
            bool ignored;
            long sequence;
            if (!allocation.canMutate
                || allocation.outcomeToken != MemoryEpochAllocationPlan.Normal
                || !MemoryIdentityCodec.TryParseEpochToken(
                    allocation.epochToken, out ignored, out sequence)
                || ignored || sequence <= 0) return false;

            // Reservation publication precedes semantic mapping. If later planning/cap checks
            // refuse, this row intentionally remains and the next load reuses the same token.
            lastIssuedAutobiographicalEpochSequence = allocation.nextSequence;
            lastIssuedAutobiographicalEpochFallbackChain =
                allocation.nextFallbackChain ?? string.Empty;
            legacyOwnerEpochReservations.Add(new SavedLegacyOwnerEpochReservation
            {
                schemaVersion = 1,
                ownerPawnId = ownerPawnId,
                reservedEpochSequence = sequence
            });
            epochToken = allocation.epochToken;
            return true;
        }

        private bool TryBuildLegacyOwnerReplacement(
            string ownerPawnId,
            string epochToken,
            List<PawnDiaryRecord> holders,
            MemoryLegacyMigrationReport report,
            long maxKnownTick,
            out PawnKnowledgeState replacement)
        {
            replacement = PawnKnowledgeState.CreateCurrent(ownerPawnId);
            replacement.autobiographicalEpochToken = epochToken;
            replacement.records.Clear();
            var blockIds = new HashSet<string>(StringComparer.Ordinal);
            var rootIds = new HashSet<string>(StringComparer.Ordinal);
            var guardIds = new HashSet<string>(StringComparer.Ordinal);
            var archiveIds = new HashSet<string>(StringComparer.Ordinal);
            var currentRoots = new List<SavedMemoryThreadRoot>();

            // Culture belongs to the physical container, not the logical duplicate-owner group.
            // Only the preserved first holder's culture moves into its replacement; each duplicate
            // inert projection keeps its own culture byte-for-byte.
            CopyFirstCulture(replacement, holders?[0]?.knowledgeState);

            for (int index = 0; holders != null && index < holders.Count; index++)
            {
                PawnKnowledgeState source = holders[index]?.knowledgeState;
                if (source == null) continue;
                if (!source.IsCurrentSchema()) continue;
                if (source.requestCancellationGeneration < 0 || source.structuralRevision < 0
                    || source.statusRevision < 0 || source.completedDiaryEntryOrdinal < 0
                    || source.requestCancellationGeneration == long.MaxValue
                    || source.structuralRevision == long.MaxValue
                    || source.statusRevision == long.MaxValue)
                    return false;
                if (!string.IsNullOrEmpty(source.autobiographicalEpochToken)
                    && !string.Equals(source.autobiographicalEpochToken, epochToken,
                        StringComparison.Ordinal)) return false;

                bool enrolled = string.Equals(
                    source.autobiographicalEpochToken, epochToken, StringComparison.Ordinal);
                bool hasActiveRows = (source.standaloneBlocks?.Count ?? 0) > 0
                    || (source.threadRoots?.Count ?? 0) > 0
                    || (source.ownerAwarenessSnapshots?.Count ?? 0) > 0
                    || (source.openCaptureEpisodes?.Count ?? 0) > 0
                    || (source.repetitionGuardRows?.Count ?? 0) > 0;
                if (!enrolled && hasActiveRows) return false;

                replacement.requestCancellationGeneration = Math.Max(
                    replacement.requestCancellationGeneration,
                    source.requestCancellationGeneration);
                replacement.structuralRevision = Math.Max(
                    replacement.structuralRevision, source.structuralRevision);
                replacement.statusRevision = Math.Max(
                    replacement.statusRevision, source.statusRevision);
                replacement.completedDiaryEntryOrdinal = Math.Max(
                    replacement.completedDiaryEntryOrdinal,
                    source.completedDiaryEntryOrdinal);
                replacement.migrationDiagnosticFlags |= Math.Max(0, source.migrationDiagnosticFlags);
                if (string.IsNullOrWhiteSpace(replacement.playerBackground)
                    && !string.IsNullOrWhiteSpace(source.playerBackground))
                    replacement.playerBackground = source.playerBackground;
                if (!AddUniqueRows(replacement.importedArchiveRows,
                        source.importedArchiveRows, row => row?.archiveRecordId, archiveIds,
                        CloneImportedArchiveRow))
                    return false;
                if (!enrolled) continue;
                if (!AddUniqueRows(replacement.standaloneBlocks,
                        source.standaloneBlocks, row => row?.recordId, blockIds,
                        CloneSavedBlock)
                    || !AddDetachedRows(
                        currentRoots, source.threadRoots, CloneSavedRoot)
                    || !AddDetachedRows(
                        replacement.ownerAwarenessSnapshots,
                        source.ownerAwarenessSnapshots,
                        CloneSavedAwareness)
                    || !AddDetachedRows(
                        replacement.openCaptureEpisodes,
                        source.openCaptureEpisodes,
                        CloneSavedEpisode)
                    || !AddUniqueRows(replacement.repetitionGuardRows,
                        source.repetitionGuardRows,
                        row => (row?.ownerEpochToken ?? string.Empty) + "\u001f"
                            + (row?.guardKind ?? string.Empty) + "\u001f"
                            + (row?.guardKey ?? string.Empty), guardIds,
                        CloneRepetitionGuardRow)) return false;
            }

            MemoryReducerPolicy reducer = BuildMemoryReducerPolicy(maxKnownTick);
            if (!TryRepairLegacyCurrentRoots(
                    replacement, currentRoots, reducer, rootIds, blockIds)
                || !TryRepairLegacyCurrentObservationRows(replacement)) return false;
            string chosenBackground = replacement.playerBackground ?? string.Empty;
            for (int index = 0; report.rows != null && index < report.rows.Count; index++)
            {
                MemoryLegacyMappedRecord row = report.rows[index];
                if (row == null) continue;
                if (row.disposition == MemoryLegacyMappedRecord.DispositionDropAutomatic) continue;
                if (row.disposition == MemoryLegacyMappedRecord.DispositionPlayerBackground)
                {
                    if (string.IsNullOrWhiteSpace(row.backgroundText)) continue;
                    if (string.IsNullOrWhiteSpace(chosenBackground))
                        chosenBackground = row.backgroundText;
                    else if (!string.Equals(chosenBackground, row.backgroundText,
                        StringComparison.Ordinal))
                    {
                        SavedImportedMemoryRow archived = BuildLegacyArchiveRow(
                            ownerPawnId, epochToken, row, "background_conflict");
                        if (archived == null || !archiveIds.Add(archived.archiveRecordId))
                            return false;
                        replacement.importedArchiveRows.Add(archived);
                    }
                    continue;
                }
                if (row.disposition == MemoryLegacyMappedRecord.DispositionArchiveAuthored)
                {
                    SavedImportedMemoryRow archived = BuildLegacyArchiveRow(
                        ownerPawnId, epochToken, row, "authored_conflict");
                    if (archived == null) return false;
                    if (archiveIds.Add(archived.archiveRecordId))
                        replacement.importedArchiveRows.Add(archived);
                    continue;
                }

                SavedMemoryBlock block = BuildLegacyActiveBlock(ownerPawnId, epochToken, row);
                if (block == null) return false;
                if (!block.playerEdited && MemoryThreadReducer.IsExpired(
                        maxKnownTick, block.originalEventTick, block.ageUnknown,
                        block.importance, reducer.minorLifetimeTicks,
                        reducer.regularLifetimeTicks)) continue;
                // A current nested block is an identity carrier too. Never let a mapped legacy row
                // collide with it and disappear merely because only standalone IDs were seeded.
                if (!blockIds.Add(block.recordId)
                    || !TryPlaceLegacyActiveBlock(
                        replacement, block, row, reducer, rootIds)
                    || !TryCollectReplacementIdentitySets(
                        replacement, rootIds, blockIds)) return false;
            }
            replacement.playerBackground = chosenBackground;
            replacement.archiveOnly = false;
            replacement.epochFenceOnly = false;
            replacement.structuralRevision = CheckedMigrationRevision(
                replacement.structuralRevision);
            replacement.statusRevision = CheckedMigrationRevision(replacement.statusRevision);
            replacement.Normalize();
            return true;
        }

        private static bool TryRepairLegacyCurrentRoots(
            PawnKnowledgeState replacement,
            List<SavedMemoryThreadRoot> currentRoots,
            MemoryReducerPolicy reducer,
            HashSet<string> rootIds,
            HashSet<string> blockIds)
        {
            currentRoots = currentRoots ?? new List<SavedMemoryThreadRoot>();
            currentRoots.Sort(delegate(SavedMemoryThreadRoot left, SavedMemoryThreadRoot right)
            {
                string leftState = MemoryThreadReducer.CanonicalState(
                    ToReducerRoot(left, reducer));
                string rightState = MemoryThreadReducer.CanonicalState(
                    ToReducerRoot(right, reducer));
                int state = string.CompareOrdinal(leftState, rightState);
                if (state != 0) return state;
                return string.CompareOrdinal(
                    left?.frozenSubjectLabel ?? string.Empty,
                    right?.frozenSubjectLabel ?? string.Empty);
            });
            var projected = new List<MemoryReducerRoot>();
            for (int index = 0; index < currentRoots.Count; index++)
                projected.Add(ToReducerRoot(currentRoots[index], reducer));
            MemoryThreadRepairResult repair = MemoryThreadRepairPolicy.Repair(projected, reducer);
            // Authored quarantines need a lossless SavedImportedMemoryRow projection. Until one can
            // be proven, retaining the complete raw owner is safer than dropping that archive row.
            if (repair.refused || repair.archivedRoots.Count > 0) return false;

            replacement.threadRoots = new List<SavedMemoryThreadRoot>();
            for (int index = 0; index < repair.activeRoots.Count; index++)
            {
                MemoryReducerRoot root = repair.activeRoots[index];
                replacement.threadRoots.Add(FromReducerRoot(
                    root,
                    BuildOriginalRegistryRoot(currentRoots, root, reducer)));
            }
            return TryCollectReplacementIdentitySets(replacement, rootIds, blockIds);
        }

        private bool TryRepairLegacyCurrentObservationRows(PawnKnowledgeState replacement)
        {
            int awarenessCap = (int)ReadCapacityLong("awarenessRows", 128, 512);
            int episodeCap = (int)ReadCapacityLong("openEpisodes", 16, 64);
            KnowledgeObservationPolicySnapshot observationPolicy =
                DiaryKnowledgePolicy.MemoryObservationSnapshot();
            observationPolicy.maximumStateFacts = (int)ReadCapacityLong(
                "awarenessFacts", 4, 16);
            ReadCapacityPair(
                "factKeyValueUnits",
                48,
                128,
                192,
                512,
                out observationPolicy.maximumFactKeyCharacters,
                out observationPolicy.maximumFactValueCharacters);
            observationPolicy = observationPolicy.Normalized();
            MemoryPolicySnapshot capturePolicy = MemoryEffectivePolicyProvider.Current;
            NormalizeOwnerAwarenessRows(
                replacement, awarenessCap, observationPolicy, capturePolicy);
            NormalizeOwnerEpisodeRows(
                replacement,
                awarenessCap,
                episodeCap,
                observationPolicy,
                capturePolicy);
            return true;
        }

        private static bool TryCollectReplacementIdentitySets(
            PawnKnowledgeState replacement,
            HashSet<string> rootIds,
            HashSet<string> blockIds)
        {
            rootIds.Clear();
            blockIds.Clear();
            for (int index = 0; replacement?.standaloneBlocks != null
                && index < replacement.standaloneBlocks.Count; index++)
            {
                string recordId = replacement.standaloneBlocks[index]?.recordId ?? string.Empty;
                if (recordId.Length == 0 || !blockIds.Add(recordId)) return false;
            }
            for (int rootIndex = 0; replacement?.threadRoots != null
                && rootIndex < replacement.threadRoots.Count; rootIndex++)
            {
                SavedMemoryThreadRoot root = replacement.threadRoots[rootIndex];
                if (root == null || string.IsNullOrWhiteSpace(root.rootId)
                    || !rootIds.Add(root.rootId)) return false;
                for (int blockIndex = 0; root.visibleBlocks != null
                    && blockIndex < root.visibleBlocks.Count; blockIndex++)
                {
                    string recordId = root.visibleBlocks[blockIndex]?.recordId ?? string.Empty;
                    if (recordId.Length == 0 || !blockIds.Add(recordId)) return false;
                }
                string rollingId = root.rollingSummaryBlock?.recordId ?? string.Empty;
                if (rollingId.Length > 0 && !blockIds.Add(rollingId)) return false;
            }
            return true;
        }

        private static void CopyFirstCulture(
            PawnKnowledgeState target, PawnKnowledgeState source)
        {
            if (target == null || source == null) return;
            if (string.IsNullOrWhiteSpace(target.originCultureDefName)
                && !string.IsNullOrWhiteSpace(source.originCultureDefName))
                target.originCultureDefName = source.originCultureDefName;
            if (string.IsNullOrWhiteSpace(target.originCultureSource)
                && !string.IsNullOrWhiteSpace(source.originCultureSource))
                target.originCultureSource = source.originCultureSource;
            if (string.IsNullOrWhiteSpace(target.adoptedCultureDefName)
                && !string.IsNullOrWhiteSpace(source.adoptedCultureDefName))
                target.adoptedCultureDefName = source.adoptedCultureDefName;
        }

        private static bool AddUniqueRows<T>(
            List<T> target,
            List<T> source,
            Func<T, string> identity,
            HashSet<string> seen,
            Func<T, T> clone) where T : class
        {
            for (int index = 0; source != null && index < source.Count; index++)
            {
                T row = source[index];
                if (row == null) continue;
                string key = identity(row) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key)) return false;
                // A duplicate key must be resolved by its row-family repair policy. Silently keeping
                // whichever physical holder happened to appear first is data loss and permutation-
                // dependent. Families without a semantic merger leave the whole owner raw instead.
                if (!seen.Add(key)) return false;
                T detached = clone(row);
                if (detached == null) return false;
                target.Add(detached);
            }
            return true;
        }

        private static bool AddDetachedRows<T>(
            List<T> target,
            List<T> source,
            Func<T, T> clone) where T : class
        {
            for (int index = 0; source != null && index < source.Count; index++)
            {
                if (source[index] == null) continue;
                T detached = clone(source[index]);
                if (detached == null) return false;
                target.Add(detached);
            }
            return true;
        }

        private static SavedMemoryRepetitionGuardRow CloneRepetitionGuardRow(
            SavedMemoryRepetitionGuardRow value)
        {
            return value == null ? null : new SavedMemoryRepetitionGuardRow
            {
                schemaVersion = value.schemaVersion,
                ownerEpochToken = value.ownerEpochToken,
                guardKind = value.guardKind,
                guardKey = value.guardKey,
                lastAutomaticIncludedTick = value.lastAutomaticIncludedTick,
                lastAutomaticIncludedEntryOrdinal = value.lastAutomaticIncludedEntryOrdinal,
                automaticInclusionCount = value.automaticInclusionCount,
                lastSourceOccurrenceId = value.lastSourceOccurrenceId,
                lastCommittedLogicalRequestId = value.lastCommittedLogicalRequestId,
                lastCommittedEvidenceSetFingerprint = value.lastCommittedEvidenceSetFingerprint
            };
        }

        private static SavedImportedMemoryRow CloneImportedArchiveRow(SavedImportedMemoryRow value)
        {
            if (value == null) return null;
            var copy = new SavedImportedMemoryRow
            {
                schemaVersion = value.schemaVersion,
                archiveRecordId = value.archiveRecordId,
                savedOwnerIdentityKindToken = value.savedOwnerIdentityKindToken,
                savedOwnerIdentityValue = value.savedOwnerIdentityValue,
                reattributionGeneration = value.reattributionGeneration,
                originalRecordId = value.originalRecordId,
                sourceOccurrenceId = value.sourceOccurrenceId,
                sourceEventId = value.sourceEventId,
                originalEventTick = value.originalEventTick,
                ageUnknown = value.ageUnknown,
                importedWording = value.importedWording,
                originalKindToken = value.originalKindToken,
                originalSummaryRoleToken = value.originalSummaryRoleToken,
                originalCategoryToken = value.originalCategoryToken,
                originalImportanceToken = value.originalImportanceToken,
                routePolicyToken = value.routePolicyToken,
                primarySubject = CloneSubject(value.primarySubject),
                sourceTypeToken = value.sourceTypeToken,
                conflictFingerprint = value.conflictFingerprint,
                overflowRowCount = value.overflowRowCount,
                overflowLogicalBytes = value.overflowLogicalBytes,
                diagnosticTokens = value.diagnosticTokens == null
                    ? new List<string>() : new List<string>(value.diagnosticTokens),
                migrationReasonToken = value.migrationReasonToken
            };
            for (int index = 0; value.secondarySubjects != null
                && index < value.secondarySubjects.Count; index++)
                if (value.secondarySubjects[index] != null)
                    copy.secondarySubjects.Add(CloneSubject(value.secondarySubjects[index]));
            for (int index = 0; value.canonicalFacts != null
                && index < value.canonicalFacts.Count; index++)
                if (value.canonicalFacts[index] != null)
                    copy.canonicalFacts.Add(CloneFact(value.canonicalFacts[index]));
            for (int index = 0; value.provenance != null
                && index < value.provenance.Count; index++)
                if (value.provenance[index] != null)
                    copy.provenance.Add(CloneProvenance(value.provenance[index]));
            SavedImportedSummaryContributionEvidenceV1 evidence =
                value.summaryContributionEvidence;
            if (evidence != null)
            {
                copy.summaryContributionEvidence =
                    new SavedImportedSummaryContributionEvidenceV1
                    {
                        schemaVersion = evidence.schemaVersion,
                        contributionId = evidence.contributionId,
                        originChapterId = evidence.originChapterId,
                        originRecordId = evidence.originRecordId,
                        originFactOrdinal = evidence.originFactOrdinal,
                        originFactId = evidence.originFactId,
                        originalEventTick = evidence.originalEventTick,
                        ageUnknown = evidence.ageUnknown,
                        category = evidence.category,
                        importance = evidence.importance,
                        canonicalValue = evidence.canonicalValue,
                        majorTurningPoint = evidence.majorTurningPoint,
                        reversal = evidence.reversal,
                        sourceOccurrenceId = evidence.sourceOccurrenceId,
                        subjectRefIds = evidence.subjectRefIds == null
                            ? new List<string>() : new List<string>(evidence.subjectRefIds),
                        provenanceRefIds = evidence.provenanceRefIds == null
                            ? new List<string>() : new List<string>(evidence.provenanceRefIds)
                    };
            }
            return copy;
        }

        private static SavedMemoryBlock BuildLegacyActiveBlock(
            string ownerPawnId,
            string epochToken,
            MemoryLegacyMappedRecord row)
        {
            string recordId;
            if (row == null || !MemoryIdentityCodec.TryCreateRecordId(
                    new MemoryRecordIdentity
                    {
                        ownerPawnId = ownerPawnId,
                        ownerEpochToken = epochToken,
                        sourceOccurrenceId = row.sourceOccurrenceId,
                        captureRuleId = row.captureRuleId,
                        factDiscriminator = row.factDiscriminator
                    }, out recordId)) return null;
            var block = new SavedMemoryBlock
            {
                schemaVersion = 1,
                recordId = recordId,
                sourceOccurrenceId = row.sourceOccurrenceId ?? string.Empty,
                sourceEventId = row.originSourceEventId ?? string.Empty,
                captureRuleId = row.captureRuleId ?? string.Empty,
                factDiscriminator = row.factDiscriminator ?? string.Empty,
                ownerPawnId = ownerPawnId,
                ownerEpochToken = epochToken,
                kind = row.kindToken,
                summaryRole = MemoryContractTokens.SummaryRoleNone,
                category = row.categoryToken,
                importance = row.importanceToken,
                originalEventTick = row.originalEventTick,
                ageUnknown = row.ageUnknown,
                playerEdited = row.playerEdited,
                suppressed = row.suppressed,
                requiredLifecycleLandmark = false,
                formatRevision = 1,
                providerExposureState = "not_sent"
            };
            if (row.playerEdited) block.playerWording = row.importedWording ?? string.Empty;
            else block.automaticWording = row.importedWording ?? string.Empty;
            for (int index = 0; row.facts != null && index < row.facts.Count; index++)
            {
                MemoryLegacyMappedFact fact = row.facts[index];
                if (fact == null) continue;
                block.facts.Add(new SavedMemoryCanonicalFact
                {
                    schemaVersion = 1,
                    factId = fact.factId ?? string.Empty,
                    factKind = fact.factKind ?? string.Empty,
                    canonicalSubjectKind = fact.canonicalSubjectKind ?? string.Empty,
                    canonicalSubjectId = fact.canonicalSubjectId ?? string.Empty,
                    aggregationToken = fact.aggregationToken ?? string.Empty,
                    canonicalValueKind = fact.canonicalValueKind ?? string.Empty,
                    canonicalValue = fact.canonicalValue ?? string.Empty
                });
            }
            block.provenance.Add(new SavedMemoryProvenance
            {
                schemaVersion = 1,
                provenanceRefId = row.provenanceRefId ?? string.Empty,
                sourceKindToken = "legacy_migration",
                sourceOccurrenceId = row.sourceOccurrenceId ?? string.Empty,
                sourceEventId = row.originSourceEventId ?? string.Empty,
                captureRuleId = row.captureRuleId ?? string.Empty,
                factDiscriminator = row.factDiscriminator ?? string.Empty,
                integrationToken = string.Empty
            });
            block.Normalize();
            return block;
        }

        /// <summary>
        /// Routes only the conservative legacy case proved by T13.3: a known social/family mapping
        /// with exactly one distinct non-owner participant. Everything ambiguous stays Standalone.
        /// </summary>
        private static bool TryPlaceLegacyActiveBlock(
            PawnKnowledgeState replacement,
            SavedMemoryBlock block,
            MemoryLegacyMappedRecord row,
            MemoryReducerPolicy reducer,
            HashSet<string> rootIds)
        {
            string subjectId;
            if (block.ageUnknown
                || !TryResolveLegacyPawnSubject(row, replacement.pawnId, out subjectId))
            {
                replacement.standaloneBlocks.Add(block);
                return true;
            }

            string rootId;
            if (!MemoryIdentityCodec.TryCreateRootId(new MemoryRootIdentity
                {
                    ownerPawnId = replacement.pawnId,
                    ownerEpochToken = replacement.autobiographicalEpochToken,
                    primarySubjectKind = MemoryContractTokens.SubjectPawn,
                    primarySubjectId = subjectId
                }, out rootId))
            {
                PlaceLegacyStandalone(replacement, block);
                return true;
            }
            int rootIndex = FindSavedRootIndex(replacement.threadRoots, rootId);
            if (rootIndex >= 0 && IsLateAfterClosedBoundary(
                    replacement.threadRoots[rootIndex], block.originalEventTick, block.ageUnknown))
            {
                PlaceLegacyStandalone(replacement, block);
                return true;
            }

            SavedMemoryThreadRoot root;
            bool createsRoot = rootIndex < 0;
            if (rootIndex < 0)
            {
                root = new SavedMemoryThreadRoot
                {
                    schemaVersion = 1,
                    rootId = rootId,
                    ownerPawnId = replacement.pawnId,
                    ownerEpochToken = replacement.autobiographicalEpochToken,
                    subjectKind = MemoryContractTokens.SubjectPawn,
                    subjectId = subjectId,
                    frozenSubjectLabel = LegacyParticipantLabel(row, subjectId),
                    structuralRevision = 1,
                    statusRevision = 1,
                    nextChapterOrdinal = 1
                };
                if (rootIds.Contains(rootId))
                {
                    PlaceLegacyStandalone(replacement, block);
                    return true;
                }
            }
            else
            {
                // Build the whole placement/reduction off to the side. Chapter saturation or a
                // reducer refusal must not leave a partly-mutated root when this row falls back.
                root = CloneSavedRoot(replacement.threadRoots[rootIndex]);
            }

            string subjectRefId;
            if (!MemoryIdentityCodec.TryCreateSubjectRefId(
                    MemoryContractTokens.SubjectPawn,
                    subjectId,
                    "participant",
                    KnowledgeObservationTokens.EvidenceCaptured,
                    out subjectRefId))
            {
                PlaceLegacyStandalone(replacement, block);
                return true;
            }
            block.primarySubject = new SavedMemorySubjectRef
            {
                schemaVersion = 1,
                subjectRefId = subjectRefId,
                subjectKind = MemoryContractTokens.SubjectPawn,
                subjectId = subjectId,
                frozenLabel = LegacyParticipantLabel(row, subjectId),
                roleToken = "participant",
                knownnessToken = KnowledgeObservationTokens.EvidenceCaptured
            };
            bool saturated;
            SavedMemoryChapter chapter = FindOrCreateOpenChapter(
                root, block.originalEventTick, string.Empty, out saturated);
            if (chapter == null)
            {
                PlaceLegacyStandalone(replacement, block);
                return true;
            }
            block.rootId = root.rootId;
            block.chapterId = chapter.chapterId;
            root.visibleBlocks.Add(block);
            chapter.lastActivityTick = Math.Max(chapter.lastActivityTick, block.originalEventTick);
            MemoryThreadReductionResult reduction = MemoryThreadReducer.Reduce(
                ToReducerRoot(root, reducer), reducer);
            if (reduction.refused)
            {
                PlaceLegacyStandalone(replacement, block);
                return true;
            }
            SavedMemoryThreadRoot reduced = FromReducerRoot(reduction.replacement, root);
            if (createsRoot)
            {
                if (!rootIds.Add(rootId))
                {
                    PlaceLegacyStandalone(replacement, block);
                    return true;
                }
                replacement.threadRoots.Add(reduced);
            }
            else
            {
                replacement.threadRoots[rootIndex] = reduced;
            }
            return true;
        }

        private static void PlaceLegacyStandalone(
            PawnKnowledgeState replacement,
            SavedMemoryBlock block)
        {
            block.rootId = string.Empty;
            block.chapterId = string.Empty;
            replacement.standaloneBlocks.Add(block);
        }

        private static bool TryResolveLegacyPawnSubject(
            MemoryLegacyMappedRecord row,
            string ownerPawnId,
            out string subjectId)
        {
            subjectId = string.Empty;
            if (row == null
                || (row.categoryToken != MemoryContractTokens.CategoryRelationships
                    && row.categoryToken != MemoryContractTokens.CategoryFamily)) return false;
            var subjects = new SortedSet<string>(StringComparer.Ordinal);
            for (int index = 0; row.participantIds != null
                && index < row.participantIds.Count; index++)
            {
                string candidate = row.participantIds[index] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(candidate)
                    || string.Equals(candidate, ownerPawnId, StringComparison.Ordinal)) continue;
                subjects.Add(candidate);
            }
            if (subjects.Count != 1) return false;
            foreach (string subject in subjects) subjectId = subject;
            return MemoryContractTokens.IsValidRootSubject(
                MemoryContractTokens.SubjectPawn, subjectId);
        }

        private static string LegacyParticipantLabel(MemoryLegacyMappedRecord row, string subjectId)
        {
            string result = string.Empty;
            for (int index = 0; row?.participantIds != null
                && index < row.participantIds.Count; index++)
            {
                if (!string.Equals(row.participantIds[index], subjectId, StringComparison.Ordinal))
                    continue;
                string label = index < (row.participantNames?.Count ?? 0)
                    ? row.participantNames[index] ?? string.Empty
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(label)
                    && (result.Length == 0 || string.CompareOrdinal(label, result) < 0))
                    result = label;
            }
            return result;
        }

        private static SavedImportedMemoryRow BuildLegacyArchiveRow(
            string ownerPawnId,
            string epochToken,
            MemoryLegacyMappedRecord row,
            string reasonToken)
        {
            if (row == null) return null;
            var single = new MemoryLegacyMigrationReport
            {
                ownerPawnId = ownerPawnId,
                ownerEpochToken = epochToken,
                maxKnownTick = 0
            };
            single.rows.Add(row);
            string archiveId = MemoryThreadMigrationPolicy.Fingerprint(single);
            if (string.IsNullOrWhiteSpace(archiveId)) return null;
            var archived = new SavedImportedMemoryRow
            {
                schemaVersion = 1,
                archiveRecordId = archiveId,
                savedOwnerIdentityKindToken = "exact_id",
                savedOwnerIdentityValue = ownerPawnId,
                reattributionGeneration = 0,
                originalRecordId = row.originRecordId ?? string.Empty,
                sourceOccurrenceId = row.sourceOccurrenceId ?? string.Empty,
                sourceEventId = row.originSourceEventId ?? string.Empty,
                originalEventTick = row.originalEventTick,
                ageUnknown = row.ageUnknown,
                importedWording = row.importedWording ?? string.Empty,
                originalKindToken = row.kindToken ?? string.Empty,
                originalSummaryRoleToken = MemoryContractTokens.SummaryRoleNone,
                originalCategoryToken = row.categoryToken ?? string.Empty,
                originalImportanceToken = row.importanceToken ?? string.Empty,
                routePolicyToken = row.recallScope ?? string.Empty,
                sourceTypeToken = row.sourceKind ?? string.Empty,
                conflictFingerprint = archiveId,
                migrationReasonToken = reasonToken ?? string.Empty
            };
            // The registered archive schema predates the raw legacy wrapper. Preserve its complete
            // length-framed field/list encoding as one inert evidence token in addition to the
            // queryable structured fields; no authored fact/name/date can disappear.
            archived.diagnosticTokens.Add(
                "legacy_payload_v1:" + MemoryThreadMigrationPolicy.CanonicalMappedRecordEncoding(row));
            for (int index = 0; row.facts != null && index < row.facts.Count; index++)
            {
                MemoryLegacyMappedFact fact = row.facts[index];
                if (fact == null) continue;
                archived.canonicalFacts.Add(new SavedMemoryCanonicalFact
                {
                    schemaVersion = 1,
                    factId = fact.factId ?? string.Empty,
                    factKind = fact.factKind ?? string.Empty,
                    canonicalSubjectKind = fact.canonicalSubjectKind ?? string.Empty,
                    canonicalSubjectId = fact.canonicalSubjectId ?? string.Empty,
                    aggregationToken = fact.aggregationToken ?? string.Empty,
                    canonicalValueKind = fact.canonicalValueKind ?? string.Empty,
                    canonicalValue = fact.canonicalValue ?? string.Empty
                });
            }
            archived.Normalize();
            return archived;
        }

        private bool LegacyReplacementWithinBounds(
            string ownerPawnId, PawnKnowledgeState replacement)
        {
            int importedRows = (int)ReadCapacityLong("importedOwnerRows", 256, 1024);
            int importedText = (int)ReadCapacityLong("importedTextUnits", 2000, 8000);
            int replacementBlocks = CountBlocks(
                replacement.standaloneBlocks, replacement.threadRoots);
            int replacementEdited = CountEdited(
                replacement.standaloneBlocks, replacement.threadRoots);
            PawnKnowledgeState priorState = FindCurrentMemoryEnvelope(ownerPawnId);
            int priorBlocks = priorState == null ? 0 : CountBlocks(
                priorState.standaloneBlocks, priorState.threadRoots);
            int priorEdited = priorState == null ? 0 : CountEdited(
                priorState.standaloneBlocks, priorState.threadRoots);
            int globalSoft;
            int globalHard;
            ReadCapacityPair("globalBlockCaps", 5000, 6000, 40000, 44000,
                out globalSoft, out globalHard);
            if ((replacement.importedArchiveRows?.Count ?? 0) > importedRows
                || replacementBlocks > ReadCapacityLong(
                    "manageableBlocksPerOwner", 128, 1024)
                || replacementEdited > ReadCapacityLong("editedBlocksOwner", 32, 128)
                || memoryM4GlobalActiveBlockCount - priorBlocks + replacementBlocks > globalSoft
                || memoryM4GlobalEditedBlockCount - priorEdited + replacementEdited
                    > ReadCapacityLong("editedBlocksGlobal", 1000, 4000)) return false;
            for (int index = 0; replacement.importedArchiveRows != null
                && index < replacement.importedArchiveRows.Count; index++)
                if ((replacement.importedArchiveRows[index]?.importedWording?.Length ?? 0)
                    > importedText) return false;

            MemoryOwnerByteTotals projected = MeasureOwner(replacement, 0);
            if (!projected.valid
                || projected.importedBytes > ReadCapacityTuplePart(
                    "importedOwnerUnknownBytes", 0, 262144, 2097152)
                || projected.activeBytes > ReadCapacityLong(
                    "activeOwnerBytes", 196608, 2097152)
                || checked(projected.activeBytes + projected.importedBytes)
                    > ReadCapacityLong("combinedOwnerBytes", 262144, 4194304)) return false;

            MemoryOwnerByteTotals prior = GetOwnerByteTotals(ownerPawnId);
            long oldActive = prior.valid ? prior.activeBytes : 0;
            long oldImported = prior.valid ? prior.importedBytes : 0;
            long activeDelta = checked(projected.activeBytes - oldActive);
            long importedDelta = checked(projected.importedBytes - oldImported);
            int globalImportedRows;
            int globalImportedOwners;
            CountCurrentImportedArchiveUsage(
                ownerPawnId, replacement, out globalImportedRows, out globalImportedOwners);
            if (globalImportedRows > ReadCapacityLong("importedGlobalRows", 10000, 40000)
                || globalImportedOwners > ReadCapacityLong(
                    "importedOwnerCount", 1000, 4000)) return false;

            MemoryBudgetDecision budget = ActiveMemoryPayloadBudget.TryAdmit(
                new MemoryBudgetLimits
                {
                    activeOwnerBytes = ReadCapacityLong("activeOwnerBytes", 196608, 2097152),
                    combinedOwnerBytes = ReadCapacityLong("combinedOwnerBytes", 262144, 4194304),
                    activeGlobalBytes = ReadCapacityLong("activeGlobalBytes", 6291456, 25165824),
                    combinedGlobalBytes = ReadCapacityLong("combinedGlobalBytes", 8388608, 33554432)
                },
                oldActive,
                oldImported,
                activeDelta,
                importedDelta,
                GetGlobalBudgetTotals());
            return budget.outcome == MemoryBudgetOutcome.Admitted;
        }

        private void CountCurrentImportedArchiveUsage(
            string replacingOwnerPawnId,
            PawnKnowledgeState replacement,
            out int rows,
            out int owners)
        {
            rows = unresolvedOwnerArchiveRows?.Count ?? 0;
            owners = 0;
            var rowsByOwner = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; diaries != null && index < diaries.Count; index++)
            {
                PawnDiaryRecord diary = diaries[index];
                if (diary == null || string.IsNullOrWhiteSpace(diary.pawnId)
                    || string.Equals(
                        diary.pawnId, replacingOwnerPawnId, StringComparison.Ordinal)) continue;
                PawnKnowledgeState state = diary.knowledgeState;
                if (state == null || !state.IsCurrentSchema()) continue;
                int count = state.importedArchiveRows?.Count ?? 0;
                int prior;
                rowsByOwner.TryGetValue(diary.pawnId, out prior);
                rowsByOwner[diary.pawnId] = checked(prior + count);
            }
            rowsByOwner[replacingOwnerPawnId ?? string.Empty] =
                replacement?.importedArchiveRows?.Count ?? 0;
            foreach (KeyValuePair<string, int> pair in rowsByOwner)
            {
                rows = checked(rows + pair.Value);
                if (pair.Value > 0) owners++;
            }
        }

        /// <summary>
        /// Converts the component's raw unresolved-owner wrapper into the inert Imported archive as
        /// one all-or-nothing list replacement. Input-local container/row ordinals remain diagnostic
        /// only and deliberately do not participate in archive identity.
        /// </summary>
        private void TryCommitUnresolvedLegacyArchive(long maxKnownTick)
        {
            if (rawUnresolvedOwnerArchiveInput == null
                || rawUnresolvedOwnerArchiveInput.Count == 0
                || unresolvedArchiveStructuralRevision == long.MaxValue) return;
            long migrationGeneration = rawUnresolvedArchiveReattributionGeneration > 0
                ? rawUnresolvedArchiveReattributionGeneration
                : Math.Max(1, unresolvedArchiveReattributionGeneration);
            var projected = new List<SavedImportedMemoryRow>(
                unresolvedOwnerArchiveRows ?? new List<SavedImportedMemoryRow>());
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < projected.Count; index++)
            {
                string id = projected[index]?.archiveRecordId ?? string.Empty;
                if (id.Length == 0 || !ids.Add(id)) return;
            }
            for (int index = 0; index < rawUnresolvedOwnerArchiveInput.Count; index++)
            {
                SavedLegacyUnresolvedOwnerArchiveInputV1 raw =
                    rawUnresolvedOwnerArchiveInput[index];
                if (raw == null || raw.legacyRecord == null) return;
                MemoryLegacyRecordSnapshot snapshot = SnapshotLegacyRecord(raw.legacyRecord);
                MemoryLegacyMappedRecord mapped = MapUnresolvedLegacyArchiveEvidence(snapshot);
                SavedImportedMemoryRow archived = BuildLegacyArchiveRow(
                    (raw.savedOwnerIdentityKindToken ?? string.Empty) + "\u001f"
                        + (raw.savedOwnerIdentityValue ?? string.Empty),
                    string.Empty,
                    mapped,
                    "unresolved_owner");
                if (archived == null) return;
                archived.savedOwnerIdentityKindToken =
                    raw.savedOwnerIdentityKindToken ?? string.Empty;
                archived.savedOwnerIdentityValue = raw.savedOwnerIdentityValue ?? string.Empty;
                archived.reattributionGeneration = migrationGeneration;
                archived.ageUnknown = snapshot.tick <= 0 || snapshot.tick > maxKnownTick;
                archived.originalEventTick = archived.ageUnknown ? 0 : snapshot.tick;
                if (ids.Add(archived.archiveRecordId)) projected.Add(archived);
            }

            if (projected.Count > ReadCapacityLong("importedUnknownRows", 1000, 4000)
                || CountAllImportedRowsWithUnknown(projected.Count)
                    > ReadCapacityLong("importedGlobalRows", 10000, 40000)) return;
            MemoryLogicalSizeResult size = SizeListValidated(projected);
            MemoryLogicalSizeResult priorUnknownSize =
                SizeListValidated(unresolvedOwnerArchiveRows);
            MemoryPayloadBudgetTotals global = GetGlobalBudgetTotals();
            if (!size.valid || !priorUnknownSize.valid
                || global.globalActiveBytes < 0 || global.globalImportedBytes < 0
                || size.totalBytes > ReadCapacityTuplePart(
                    "importedOwnerUnknownBytes", 1, 2097152, 16777216)
                || !UnresolvedArchiveFitsGlobalBudgets(
                    size.totalBytes, priorUnknownSize.totalBytes, global)) return;

            unresolvedOwnerArchiveRows = projected;
            rawUnresolvedOwnerArchiveInput =
                new List<SavedLegacyUnresolvedOwnerArchiveInputV1>();
            unresolvedArchiveMigrationState = MemoryArchiveStates.Current;
            rawUnresolvedArchiveReattributionGeneration = migrationGeneration;
            unresolvedArchiveStructuralRevision++;
        }

        /// <summary>
        /// Charges only the Unknown archive replacement's growth against the existing global
        /// Imported and combined totals. Checked arithmetic makes malformed/saturated saves refuse
        /// the whole swap while retaining their raw migration input for a future compatible build.
        /// </summary>
        private bool UnresolvedArchiveFitsGlobalBudgets(
            long projectedUnknownBytes,
            long priorUnknownBytes,
            MemoryPayloadBudgetTotals global)
        {
            try
            {
                long delta = checked(projectedUnknownBytes - priorUnknownBytes);
                long projectedImported = checked(global.globalImportedBytes + delta);
                long projectedCombined = checked(global.globalActiveBytes + projectedImported);
                return projectedImported >= 0
                    && projectedCombined >= 0
                    && global.globalActiveBytes <= ReadCapacityLong(
                        "activeGlobalBytes", 6291456, 25165824)
                    && projectedImported <= ReadCapacityLong(
                        "importedGlobalBytes", 8388608, 33554432)
                    && projectedCombined <= ReadCapacityLong(
                        "combinedGlobalBytes", 8388608, 33554432);
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private int CountAllImportedRowsWithUnknown(int projectedUnknownRows)
        {
            int total = projectedUnknownRows;
            for (int index = 0; diaries != null && index < diaries.Count; index++)
            {
                PawnDiaryRecord diary = diaries[index];
                if (diary == null || string.IsNullOrWhiteSpace(diary.pawnId)
                    || diary.knowledgeState == null
                    || !diary.knowledgeState.IsCurrentSchema()) continue;
                total = checked(total
                    + (diary.knowledgeState.importedArchiveRows?.Count ?? 0));
            }
            return total;
        }

        private static MemoryLegacyMappedRecord MapUnresolvedLegacyArchiveEvidence(
            MemoryLegacyRecordSnapshot snapshot)
        {
            var row = new MemoryLegacyMappedRecord
            {
                disposition = MemoryLegacyMappedRecord.DispositionArchiveAuthored,
                sourceOccurrenceId = snapshot.sourceEventId ?? string.Empty,
                originalEventTick = Math.Max(0, snapshot.tick),
                ageUnknown = snapshot.tick <= 0,
                importedWording = !string.IsNullOrWhiteSpace(snapshot.manualTextOverride)
                    ? snapshot.manualTextOverride
                    : snapshot.fallbackSummary ?? string.Empty,
                originRecordId = snapshot.recordId ?? string.Empty,
                dedupKey = snapshot.dedupKey ?? string.Empty,
                originSourceEventId = snapshot.sourceEventId ?? string.Empty,
                sourceKind = snapshot.sourceKind ?? string.Empty,
                recallScope = snapshot.recallScope ?? string.Empty,
                eventKind = snapshot.eventKind ?? string.Empty,
                topicKey = snapshot.topicKey ?? string.Empty,
                dateLabel = snapshot.dateLabel ?? string.Empty,
                fallbackSummary = snapshot.fallbackSummary ?? string.Empty,
                playerEdited = !string.IsNullOrWhiteSpace(snapshot.manualTextOverride)
            };
            CopySafe(snapshot.participantIds, row.participantIds);
            CopySafe(snapshot.participantNames, row.participantNames);
            CopySafe(snapshot.subjectKeys, row.subjectKeys);
            CopySafe(snapshot.factKeys, row.factKeys);
            CopySafe(snapshot.factValues, row.factValues);
            return row;
        }

        private static long CheckedMigrationRevision(long current)
        {
            return current < 1 ? 1 : current == long.MaxValue ? long.MaxValue : current + 1;
        }

        /// <summary>
        /// Builds every object reference used by the commit tail before publication begins. The
        /// publish block below then performs assignments only: no constructors, hashing, callbacks,
        /// normalization, or list growth can strand a partly-cleared duplicate group (§T13.5).
        /// </summary>
        private void BuildLegacyPublicationProjections(
            List<PawnDiaryRecord> holders,
            LegacyOwnerCommitPlan commit)
        {
            commit.holderKnowledgeStates.Add(commit.replacement);
            commit.holderReflectionStates.Add(BuildMergedLegacyReflectionState(
                holders, holders[0], commit.epochToken));
            for (int index = 1; index < holders.Count; index++)
            {
                PawnKnowledgeState prior = holders[index]?.knowledgeState;
                PawnKnowledgeState inert = PawnKnowledgeState.CreateCurrent(commit.ownerPawnId);
                if (prior != null) CopyFirstCulture(inert, prior);
                inert.autobiographicalEpochToken = string.Empty;
                inert.archiveOnly = false;
                inert.epochFenceOnly = false;
                inert.requestCancellationGeneration = 0;
                inert.structuralRevision = 0;
                inert.statusRevision = 0;
                // The owner-local completion ordinal is always one-based, even on an unenrolled
                // duplicate holder. Zero is invalid and would make later policy fail closed.
                inert.completedDiaryEntryOrdinal = 1;
                commit.holderKnowledgeStates.Add(inert);

                PawnReflectionState duplicate = CloneReflectionState(
                    holders[index]?.reflectionState);
                if (duplicate != null)
                {
                    duplicate.memoryReflectionSchemaVersion = 0;
                    duplicate.memoryOwnerEpochToken = string.Empty;
                    duplicate.lastQuietMemoryEvaluatedAbsoluteDay = -1;
                    duplicate.lastQuietMemoryActivatedAbsoluteQuadrum = -1;
                    duplicate.lastQuietMemoryDecisionKey = string.Empty;
                }
                commit.holderReflectionStates.Add(duplicate);
            }

            for (int index = 0; legacyOwnerEpochReservations != null
                && index < legacyOwnerEpochReservations.Count; index++)
            {
                SavedLegacyOwnerEpochReservation reservation =
                    legacyOwnerEpochReservations[index];
                if (reservation == null || string.Equals(
                        reservation.ownerPawnId,
                        commit.ownerPawnId,
                        StringComparison.Ordinal)) continue;
                commit.remainingEpochReservations.Add(new SavedLegacyOwnerEpochReservation
                {
                    schemaVersion = reservation.schemaVersion,
                    ownerPawnId = reservation.ownerPawnId,
                    reservedEpochSequence = reservation.reservedEpochSequence
                });
            }
        }

        private void PublishLegacyOwnerCommit(
            List<PawnDiaryRecord> holders, LegacyOwnerCommitPlan commit)
        {
            // Counts were built from this exact holder list during preparation. No statement in
            // this loop allocates or invokes policy code, so publication cannot throw midway.
            for (int index = 0; index < holders.Count; index++)
            {
                holders[index].knowledgeState = commit.holderKnowledgeStates[index];
                holders[index].reflectionState = commit.holderReflectionStates[index];
            }
            legacyOwnerEpochReservations = commit.remainingEpochReservations;
        }

        private static PawnReflectionState BuildMergedLegacyReflectionState(
            List<PawnDiaryRecord> holders, PawnDiaryRecord primary, string epochToken)
        {
            int evaluatedDay = -1;
            int activatedQuadrum = -1;
            for (int index = 0; holders != null && index < holders.Count; index++)
            {
                PawnReflectionState state = holders[index]?.reflectionState;
                if (state == null) continue;
                if (state.lastQuietMemoryEvaluatedAbsoluteDay >= -1)
                    evaluatedDay = Math.Max(
                        evaluatedDay, state.lastQuietMemoryEvaluatedAbsoluteDay);
                if (state.lastQuietMemoryActivatedAbsoluteQuadrum >= -1)
                    activatedQuadrum = Math.Max(
                        activatedQuadrum, state.lastQuietMemoryActivatedAbsoluteQuadrum);
            }
            bool sawPass = false;
            bool sawFail = false;
            bool sawInvalid = false;
            for (int index = 0; holders != null && index < holders.Count; index++)
            {
                PawnReflectionState state = holders[index]?.reflectionState;
                if (state == null
                    || state.lastQuietMemoryEvaluatedAbsoluteDay != evaluatedDay) continue;
                bool passed;
                if (!MemoryDeterministicRngV1.TryReadDecision(
                        primary.pawnId, epochToken, evaluatedDay,
                        state.lastQuietMemoryDecisionKey, out passed))
                {
                    sawInvalid = true;
                    continue;
                }
                if (passed) sawPass = true;
                else sawFail = true;
            }
            string decisionKey = evaluatedDay < 0
                ? string.Empty
                : MemoryDeterministicRngV1.CreateDecisionKey(
                    primary.pawnId,
                    epochToken,
                    evaluatedDay,
                    sawPass && !sawFail && !sawInvalid);
            PawnReflectionState merged = CloneReflectionState(primary?.reflectionState)
                ?? new PawnReflectionState();
            merged.memoryReflectionSchemaVersion = 1;
            merged.memoryOwnerEpochToken = epochToken;
            merged.lastQuietMemoryEvaluatedAbsoluteDay = evaluatedDay;
            merged.lastQuietMemoryActivatedAbsoluteQuadrum = activatedQuadrum;
            merged.lastQuietMemoryDecisionKey = decisionKey;
            return merged;
        }

        private static PawnReflectionState CloneReflectionState(PawnReflectionState source)
        {
            if (source == null) return null;
            return new PawnReflectionState
            {
                baselineOnNextOpportunity = source.baselineOnNextOpportunity,
                linkedBaselineOnNextOpportunity = source.linkedBaselineOnNextOpportunity,
                lastReflectionTick = source.lastReflectionTick,
                lastMajorArcTick = source.lastMajorArcTick,
                lastCrossArcTick = source.lastCrossArcTick,
                lastBeliefTick = source.lastBeliefTick,
                lastQuadrumTick = source.lastQuadrumTick,
                lastDayTick = source.lastDayTick,
                pendingMajorArc = source.pendingMajorArc,
                pendingMajorArcRequestedTick = source.pendingMajorArcRequestedTick,
                pendingMajorArcAvoidEventId = source.pendingMajorArcAvoidEventId,
                memoryReflectionSchemaVersion = source.memoryReflectionSchemaVersion,
                memoryOwnerEpochToken = source.memoryOwnerEpochToken,
                lastQuietMemoryEvaluatedAbsoluteDay =
                    source.lastQuietMemoryEvaluatedAbsoluteDay,
                lastQuietMemoryActivatedAbsoluteQuadrum =
                    source.lastQuietMemoryActivatedAbsoluteQuadrum,
                lastQuietMemoryDecisionKey = source.lastQuietMemoryDecisionKey
            };
        }

        internal static MemoryLegacyRecordSnapshot SnapshotLegacyRecord(ImportantMemoryRecord record)
        {
            var snapshot = new MemoryLegacyRecordSnapshot
            {
                recordId = record.recordId ?? string.Empty,
                dedupKey = record.dedupKey ?? string.Empty,
                sourceEventId = record.sourceEventId ?? string.Empty,
                sourceKind = record.sourceKind ?? KnowledgeTokens.SourceKindCaptured,
                recallScope = record.recallScope ?? KnowledgeTokens.RecallScopeContextual,
                eventKind = record.eventKind ?? string.Empty,
                topicKey = record.topicKey ?? string.Empty,
                tick = record.tick,
                dateLabel = record.dateLabel ?? string.Empty,
                fallbackSummary = record.fallbackSummary ?? string.Empty,
                manualTextOverride = record.manualTextOverride ?? string.Empty
            };
            CopySafe(record.participantIds, snapshot.participantIds);
            CopySafe(record.participantNames, snapshot.participantNames);
            CopySafe(record.subjectKeys, snapshot.subjectKeys);
            CopySafe(record.factKeys, snapshot.factKeys);
            CopySafe(record.factValues, snapshot.factValues);
            return snapshot;
        }

        private static void CopySafe(List<string> source, List<string> target)
        {
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                target.Add(source[i] ?? string.Empty);
            }
        }

        /// <summary>Snapshots the frozen memory-legacy-map-v1 catalog from the shipped Defs:
        /// one entry per eventKind with its current rule ID and kind/category/importance tokens.</summary>
        private static List<MemoryLegacyRuleMapEntry> SnapshotLegacyRuleMap()
        {
            var map = new List<MemoryLegacyRuleMapEntry>();
            try
            {
                List<ImportantEventRule> rules = DiaryKnowledgePolicy.ImportantEventRules();
                var seenKinds = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < rules.Count; i++)
                {
                    ImportantEventRule rule = rules[i];
                    if (rule == null || string.IsNullOrWhiteSpace(rule.eventKind)
                        || !seenKinds.Add(rule.eventKind))
                    {
                        continue;
                    }

                    var entry = new MemoryLegacyRuleMapEntry
                    {
                        eventKind = rule.eventKind,
                        captureRuleId = rule.defName ?? string.Empty,
                        memoryKind = NormalizeKind(rule.memoryKind),
                        category = MemoryContractTokens.IsKnownCategory(rule.memoryCategory)
                            ? rule.memoryCategory
                            : MemoryContractTokens.CategoryPersonal,
                        baseImportance =
                            MemoryContractTokens.IsKnownImportance(rule.baseImportance)
                                ? rule.baseImportance
                                : MemoryContractTokens.ImportanceImportant
                    };
                    entry.factDescriptors.AddRange(rule.memoryFacts ?? new List<MemoryFactDescriptor>());
                    map.Add(entry);
                }
            }
            catch (Exception)
            {
                // A missing/failed Def database simply maps nothing: every known-kind row then
                // follows the conservative unmapped arm. Never abort the load here.
            }

            return map;
        }

        private static string NormalizeKind(string memoryKind)
        {
            return memoryKind == MemoryContractTokens.KindEvent
                ? MemoryContractTokens.KindEvent
                : MemoryContractTokens.KindLandmark;
        }
    }
}

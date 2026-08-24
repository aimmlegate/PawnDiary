// DiaryGameComponent.MemoryLibrary.cs — main-thread M5 no-create Library repository adapter.
//
// Saved models never escape this partial. Ordinary update builds complete detached owner snapshots,
// publishes them through one loaded-session revision clock, and drains queued commands outside IMGUI
// draw. Queries page only immutable DTOs; opening or searching the Library never creates an owner,
// allocates an epoch, resolves culture from live pawn state, or normalizes saved rows.
using System;
using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private sealed class MemoryLibraryPublication
        {
            internal string fingerprint = string.Empty;
            internal long revision;
        }

        private readonly MemoryLibraryPublicationClock memoryLibraryClock =
            new MemoryLibraryPublicationClock();
        private readonly Dictionary<string, MemoryLibraryOwnerIndexSnapshot> memoryLibraryOwners =
            new Dictionary<string, MemoryLibraryOwnerIndexSnapshot>(StringComparer.Ordinal);
        private readonly List<MemoryLibraryOwnerRow> memoryLibraryDirectory =
            new List<MemoryLibraryOwnerRow>();
        private readonly Dictionary<string, MemoryLibraryPublication> memoryLibraryListPublications =
            new Dictionary<string, MemoryLibraryPublication>(StringComparer.Ordinal);
        private readonly Dictionary<string, MemoryLibraryPublication> memoryLibraryDetailPublications =
            new Dictionary<string, MemoryLibraryPublication>(StringComparer.Ordinal);
        private readonly Dictionary<string, MemoryLibraryPublication> memoryLibraryTextPublications =
            new Dictionary<string, MemoryLibraryPublication>(StringComparer.Ordinal);
        private readonly List<MemoryLibraryCommand> memoryLibraryPendingCommands =
            new List<MemoryLibraryCommand>();
        private readonly Dictionary<string, MemoryLibraryCommandResult> memoryLibraryCommandResults =
            new Dictionary<string, MemoryLibraryCommandResult>(StringComparer.Ordinal);
        private long memoryLibraryDirectoryRevision;
        private string memoryLibraryDirectoryFingerprint = string.Empty;
        private bool memoryLibraryRevisionSaturated;
        private long memoryLibraryAdditionalLegacyRawOwners;
        private long memoryLibraryAdditionalZeroOwners;

        /// <summary>Clears every loaded-session publication/cache/command identity.</summary>
        private void ResetMemoryLibraryTransient()
        {
            memoryLibraryClock.Reset();
            memoryLibraryOwners.Clear();
            memoryLibraryDirectory.Clear();
            memoryLibraryListPublications.Clear();
            memoryLibraryDetailPublications.Clear();
            memoryLibraryTextPublications.Clear();
            memoryLibraryPendingCommands.Clear();
            memoryLibraryCommandResults.Clear();
            memoryLibraryDirectoryRevision = 0;
            memoryLibraryDirectoryFingerprint = string.Empty;
            memoryLibraryRevisionSaturated = false;
            memoryLibraryAdditionalLegacyRawOwners = 0;
            memoryLibraryAdditionalZeroOwners = 0;
        }

        /// <summary>
        /// Rebuilds the detached directory/index outside draw only when its complete source tuple changed.
        /// </summary>
        private void RefreshMemoryLibraryPublications()
        {
            if (!MemoryPolicyIsReconciled()) return;
            MemoryLibraryLimits limits = BuildMemoryLibraryLimits();
            List<MemoryLibraryOwnerIndexSnapshot> indexed = BuildMemoryLibraryOwnerSnapshots(limits);
            string fingerprint = DirectoryFingerprint(indexed);
            if (memoryLibraryDirectoryRevision > 0
                && string.Equals(fingerprint, memoryLibraryDirectoryFingerprint,
                    StringComparison.Ordinal)) return;
            if (!memoryLibraryClock.TryAllocate(out long revision))
            {
                memoryLibraryRevisionSaturated = true;
                return;
            }

            memoryLibraryOwners.Clear();
            memoryLibraryDirectory.Clear();
            for (int index = 0; index < indexed.Count; index++)
            {
                MemoryLibraryOwnerIndexSnapshot snapshot = indexed[index];
                MemoryLibraryOwnerRow row = snapshot?.ownerRow;
                if (row == null) continue;
                memoryLibraryDirectory.Add(row);
                string ownerKey = OwnerIndexKey(row.primaryHandle);
                if (!string.IsNullOrEmpty(ownerKey)) memoryLibraryOwners[ownerKey] = snapshot;
            }
            memoryLibraryDirectoryRevision = revision;
            memoryLibraryDirectoryFingerprint = fingerprint;
            memoryLibraryListPublications.Clear();
            memoryLibraryDetailPublications.Clear();
            memoryLibraryTextPublications.Clear();
        }

        /// <summary>Returns one detached paged owner directory; never creates saved state.</summary>
        internal MemoryLibraryOwnerResult QueryMemoryLibraryOwners(MemoryLibraryOwnerQuery query)
        {
            if (memoryLibraryRevisionSaturated)
                return new MemoryLibraryOwnerResult
                {
                    status = MemoryLibraryStatuses.Invalid,
                    reasonToken = "library_revision_saturated"
                };
            if (memoryLibraryDirectoryRevision <= 0)
                return new MemoryLibraryOwnerResult { status = MemoryLibraryStatuses.Preparing };
            return MemoryLibraryIndexPolicy.QueryOwners(
                memoryLibraryDirectory,
                query,
                memoryLibraryDirectoryRevision,
                BuildMemoryLibraryLimits(),
                memoryLibraryAdditionalLegacyRawOwners,
                memoryLibraryAdditionalZeroOwners);
        }

        /// <summary>Returns one owner/view window pinned to a transient list publication.</summary>
        internal MemoryLibraryListResult QueryMemoryLibraryList(MemoryLibraryListQuery query)
        {
            if (memoryLibraryRevisionSaturated)
                return InvalidList("library_revision_saturated");
            if (query?.primaryHandle == null || memoryLibraryDirectoryRevision <= 0)
                return new MemoryLibraryListResult
                {
                    status = memoryLibraryDirectoryRevision <= 0
                        ? MemoryLibraryStatuses.Preparing : MemoryLibraryStatuses.Invalid
                };
            string ownerKey = OwnerIndexKey(query.primaryHandle);
            if (!memoryLibraryOwners.TryGetValue(ownerKey, out MemoryLibraryOwnerIndexSnapshot owner))
                return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Missing };

            // The sole no-envelope active-owner form is proven by the current directory revision.
            bool zeroNoEpoch = query.primaryHandle.scopeToken == MemoryLibraryScopes.Active
                && string.IsNullOrEmpty(query.primaryHandle.epochTokenOrEmpty)
                && query.activeOwnerEpochKey == null
                && owner.ownerRow.threadCount == 0
                && owner.ownerRow.standaloneCount == 0
                && owner.ownerRow.importedCount == 0;
            if (zeroNoEpoch)
            {
                if (query.listStart != 0 || query.expectedDirectoryRevision <= 0
                    || (query.viewTag != MemoryLibraryViews.Threads
                        && query.viewTag != MemoryLibraryViews.Standalone))
                    return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Invalid };
                if (query.expectedDirectoryRevision != memoryLibraryDirectoryRevision)
                    return new MemoryLibraryListResult { status = MemoryLibraryStatuses.Stale };
                long zeroRevision = ResolveLibraryPublication(
                    memoryLibraryListPublications,
                    ListQueryFingerprint(query, owner.ownerRow),
                    query.expectedListSnapshotRevision);
                if (zeroRevision <= 0)
                    return query.expectedListSnapshotRevision > 0
                        ? new MemoryLibraryListResult { status = MemoryLibraryStatuses.Stale }
                        : InvalidList("library_revision_saturated");
                return new MemoryLibraryListResult
                {
                    status = MemoryLibraryStatuses.Ready,
                    directoryRevision = memoryLibraryDirectoryRevision,
                    listSnapshotRevision = zeroRevision,
                    emptyStateToken = "no_memories",
                    ttlValidUntilTickExclusive = NextMemoryLibraryDayBoundary()
                };
            }

            string fingerprint = ListQueryFingerprint(query, owner.ownerRow);
            long revision = ResolveLibraryPublication(
                memoryLibraryListPublications,
                fingerprint,
                query.expectedListSnapshotRevision);
            if (revision <= 0)
                return query.expectedListSnapshotRevision > 0
                    ? new MemoryLibraryListResult { status = MemoryLibraryStatuses.Stale }
                    : InvalidList("library_revision_saturated");
            long now = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            return MemoryLibraryIndexPolicy.QueryList(
                owner,
                query,
                memoryLibraryDirectoryRevision,
                revision,
                MemoryEffectivePolicyProvider.PublicationRevision,
                1,
                now / 60000L,
                NextMemoryLibraryDayBoundary(),
                BuildMemoryLibraryLimits());
        }

        /// <summary>Returns an independently paged selected-root detail stream.</summary>
        internal MemoryThreadDetailResult QueryMemoryThreadDetail(MemoryThreadDetailQuery query)
        {
            if (memoryLibraryRevisionSaturated)
                return new MemoryThreadDetailResult
                {
                    status = MemoryLibraryStatuses.Invalid,
                    reasonToken = "library_revision_saturated"
                };
            if (query?.rootHandle == null)
                return new MemoryThreadDetailResult { status = MemoryLibraryStatuses.Invalid };
            string ownerKey = OwnerIndexKey(new MemoryLibraryOwnerHandle(
                MemoryLibraryScopes.Active,
                query.rootHandle.ownerPawnId,
                query.rootHandle.epochToken));
            if (!memoryLibraryOwners.TryGetValue(ownerKey, out MemoryLibraryOwnerIndexSnapshot owner))
                return new MemoryThreadDetailResult { status = MemoryLibraryStatuses.Missing };
            string fingerprint = DetailQueryFingerprint(query);
            long revision = ResolveLibraryPublication(
                memoryLibraryDetailPublications,
                fingerprint,
                query.expectedDetailSnapshotRevision);
            if (revision <= 0)
                return new MemoryThreadDetailResult
                {
                    status = query.expectedDetailSnapshotRevision > 0
                        ? MemoryLibraryStatuses.Stale : MemoryLibraryStatuses.Invalid,
                    reasonToken = query.expectedDetailSnapshotRevision > 0
                        ? string.Empty : "library_revision_saturated"
                };
            return MemoryLibraryIndexPolicy.QueryThreadDetail(
                owner, query, revision, NextMemoryLibraryDayBoundary(), BuildMemoryLibraryLimits());
        }

        /// <summary>Returns one bounded active-block detail after exact placement/revision checks.</summary>
        internal MemoryBlockDetailResult QueryMemoryBlockDetail(MemoryBlockDetailQuery query)
        {
            MemoryBlockDetailResult result = new MemoryBlockDetailResult();
            if (query?.recordHandle == null || string.IsNullOrWhiteSpace(query.recordHandle.ownerPawnId)
                || string.IsNullOrWhiteSpace(query.recordHandle.epochToken)
                || string.IsNullOrWhiteSpace(query.recordHandle.recordId)
                || query.targetStructuralRevision <= 0
                || (query.projectionToken != "full" && query.projectionToken != "filtered"))
                return result;
            PawnKnowledgeState owner = FindCurrentMemoryEnvelope(query.recordHandle.ownerPawnId);
            if (owner == null || owner.autobiographicalEpochToken != query.recordHandle.epochToken)
            {
                result.status = MemoryLibraryStatuses.Missing;
                return result;
            }
            SavedMemoryThreadRoot root = null;
            SavedMemoryBlock block = null;
            long structural;
            long status;
            if (string.Equals(query.placementToken, "standalone", StringComparison.Ordinal))
            {
                if (query.rootHandle != null) return result;
                block = FindSavedBlock(owner.standaloneBlocks, query.recordHandle.recordId);
                structural = owner.structuralRevision;
                status = owner.statusRevision;
            }
            else
            {
                if (!RootAndRecordHandlesMatch(query.rootHandle, query.recordHandle)) return result;
                root = FindSavedRoot(owner.threadRoots, query.rootHandle.rootId);
                if (root == null)
                {
                    result.status = MemoryLibraryStatuses.Missing;
                    return result;
                }
                block = FindSavedBlock(root.visibleBlocks, query.recordHandle.recordId)
                    ?? (root.rollingSummaryBlock?.recordId == query.recordHandle.recordId
                        ? root.rollingSummaryBlock : null);
                structural = root.structuralRevision;
                status = root.statusRevision;
            }
            if (block == null)
            {
                result.status = MemoryLibraryStatuses.Missing;
                return result;
            }
            if (structural != query.targetStructuralRevision)
            {
                result.status = MemoryLibraryStatuses.Stale;
                return result;
            }
            MemoryLibraryLimits limits = BuildMemoryLibraryLimits();
            MemoryBlockRow row = BuildMemoryBlockRow(
                block, root, owner.structuralRevision, PawnDiaryRecordName(owner.pawnId), limits);
            if (query.projectionToken == "filtered")
            {
                string normalizedSearch = MemoryLibraryPolicy.NormalizeSearch(
                    query.search, limits.searchScalars, limits.searchUtf16Units);
                if (!MemoryLibraryPolicy.MatchesFilters(row, query.filters)
                    || !MemoryLibraryPolicy.SearchMatches(row.normalizedSearch, normalizedSearch))
                    return result;
            }
            result.status = MemoryLibraryStatuses.Ready;
            result.row = row;
            result.detail = BuildMemoryBlockDetail(block, limits);
            result.targetStructuralRevision = structural;
            result.targetStatusRevision = status;
            result.ttlValidUntilTickExclusive = MemoryLibraryPolicy.TtlValidUntil(
                NextMemoryLibraryDayBoundary(), row.projectedNextExpiryTick);
            return result;
        }

        /// <summary>Pages the complete bounded preserved Imported wording without copying it into rows.</summary>
        internal MemoryImportedDetailResult QueryMemoryImportedDetail(
            MemoryImportedDetailQuery query)
        {
            MemoryImportedDetailResult result = new MemoryImportedDetailResult();
            if (query?.archiveHandle == null) return result;
            SavedImportedMemoryRow row;
            long structural;
            if (!TryFindImportedRow(query.archiveHandle, out row, out structural))
            {
                result.status = MemoryLibraryStatuses.Missing;
                return result;
            }
            if (query.targetStructuralRevision <= 0) return result;
            if (query.targetStructuralRevision != structural)
            {
                result.status = MemoryLibraryStatuses.Stale;
                return result;
            }
            string streamFingerprint = MemoryLibraryPolicy.StreamFingerprint(
                ArchiveHandleKey(query.archiveHandle),
                query.textCount.ToString(CultureInfo.InvariantCulture));
            string contentFingerprint = MemoryLibraryPolicy.StreamFingerprint(
                row.importedWording ?? string.Empty);
            long revision = ResolveLibraryPublicationWithContent(
                memoryLibraryTextPublications,
                streamFingerprint,
                contentFingerprint,
                query.expectedArchiveTextSnapshotRevision);
            if (revision <= 0)
            {
                result.status = query.expectedArchiveTextSnapshotRevision > 0
                    ? MemoryLibraryStatuses.Stale : MemoryLibraryStatuses.Invalid;
                result.reasonToken = query.expectedArchiveTextSnapshotRevision > 0
                    ? string.Empty : "library_revision_saturated";
                return result;
            }
            string text = row.importedWording ?? string.Empty;
            MemoryLibraryTextCursorPlan cursor = MemoryLibraryPolicy.NormalizeTextCursor(
                text,
                query.textStart,
                query.textCount,
                BuildMemoryLibraryLimits().importedTextChunkUtf16Units,
                query.expectedArchiveTextSnapshotRevision);
            if (!cursor.valid) return result;
            result.status = MemoryLibraryStatuses.Ready;
            result.archiveHandle = query.archiveHandle;
            result.textChunk = text.Substring(cursor.start, cursor.count);
            result.returnedTextStart = cursor.start;
            result.previousTextStart = cursor.previousStart;
            result.nextTextStart = cursor.end;
            result.totalTextLength = text.Length;
            result.hasPrevious = cursor.hasPrevious;
            result.hasMore = cursor.hasMore;
            result.archiveTextSnapshotRevision = revision;
            result.targetStructuralRevision = structural;
            return result;
        }

        /// <summary>Returns one actionless compatibility panel for an exact directory handle/revision.</summary>
        internal MemoryCompatibilityResult QueryMemoryCompatibility(MemoryCompatibilityQuery query)
        {
            MemoryCompatibilityResult result = new MemoryCompatibilityResult();
            if (query?.compatibilityHandle == null) return result;
            if (query.sourcePayloadRevision <= 0) return result;
            MemoryLibraryOwnerRow row = FindCompatibilityDirectoryRow(query.compatibilityHandle);
            if (row == null)
            {
                result.status = MemoryLibraryStatuses.Missing;
                return result;
            }
            if (query.sourcePayloadRevision != row.compatibilitySourcePayloadRevision)
            {
                result.status = MemoryLibraryStatuses.Stale;
                return result;
            }
            long count = CompatibilityRowCount(query.compatibilityHandle);
            result.status = MemoryLibraryStatuses.Ready;
            result.sourcePayloadRevision = row.compatibilitySourcePayloadRevision;
            result.pending = new MemoryLegacyPendingDto
            {
                handle = query.compatibilityHandle,
                stateToken = "preparing",
                reasonToken = query.compatibilityHandle.scopeToken,
                rowCount = count,
                logicalByteCount = 0,
                sourcePayloadRevision = row.compatibilitySourcePayloadRevision
            };
            return result;
        }

        /// <summary>Queues one detached command for the next safe component-update drain.</summary>
        internal bool TryEnqueueMemoryLibraryCommand(MemoryLibraryCommand command)
        {
            if (!ValidLibraryCommandEnvelope(command)) return false;
            string key = LibraryCommandKey(command.libraryClientToken, command.commandId);
            if (memoryLibraryCommandResults.ContainsKey(key)) return true;
            for (int index = 0; index < memoryLibraryPendingCommands.Count; index++)
            {
                MemoryLibraryCommand pending = memoryLibraryPendingCommands[index];
                if (pending != null && LibraryCommandKey(
                    pending.libraryClientToken, pending.commandId) == key) return true;
            }
            int cap = BuildMemoryLibraryLimits().commandEntries;
            if (memoryLibraryPendingCommands.Count + memoryLibraryCommandResults.Count >= cap)
                return false;
            memoryLibraryPendingCommands.Add(command);
            return true;
        }

        /// <summary>Consumes one terminal result; later replay re-resolves Missing/Stale precedence.</summary>
        internal bool TryTakeMemoryLibraryCommandResult(
            string clientToken,
            long commandId,
            out MemoryLibraryCommandResult result)
        {
            string key = LibraryCommandKey(clientToken, commandId);
            if (!memoryLibraryCommandResults.TryGetValue(key, out result)) return false;
            memoryLibraryCommandResults.Remove(key);
            return true;
        }

        private void DrainMemoryLibraryCommands()
        {
            if (memoryLibraryPendingCommands.Count == 0) return;
            List<MemoryLibraryCommand> pending =
                new List<MemoryLibraryCommand>(memoryLibraryPendingCommands);
            memoryLibraryPendingCommands.Clear();
            for (int index = 0; index < pending.Count; index++)
            {
                MemoryLibraryCommand command = pending[index];
                string key = LibraryCommandKey(command.libraryClientToken, command.commandId);
                if (memoryLibraryCommandResults.ContainsKey(key)) continue;
                try
                {
                    memoryLibraryCommandResults[key] = ApplyMemoryLibraryCommand(command);
                }
                catch (Exception exception)
                {
                    memoryLibraryCommandResults[key] = NewLibraryCommandResult(command);
                    Log.ErrorOnce(
                        "[Pawn Diary] One Memory Library command failed without replay: " + exception,
                        ("PawnDiary.Memory.Library.Command." + key).GetHashCode());
                }
            }
        }

        /// <summary>Prunes one closed UI client's pending intents and unconsumed terminal results.</summary>
        internal void AbandonMemoryLibraryClient(string clientToken)
        {
            if (string.IsNullOrWhiteSpace(clientToken)) return;
            memoryLibraryPendingCommands.RemoveAll(command => command != null
                && string.Equals(command.libraryClientToken, clientToken, StringComparison.Ordinal));
            List<string> resultKeys = new List<string>();
            foreach (KeyValuePair<string, MemoryLibraryCommandResult> pair
                in memoryLibraryCommandResults)
                if (string.Equals(pair.Value?.libraryClientToken,
                    clientToken, StringComparison.Ordinal)) resultKeys.Add(pair.Key);
            for (int index = 0; index < resultKeys.Count; index++)
                memoryLibraryCommandResults.Remove(resultKeys[index]);
        }

        private MemoryLibraryCommandResult ApplyMemoryLibraryCommand(MemoryLibraryCommand command)
        {
            MemoryLibraryCommandResult result = NewLibraryCommandResult(command);
            if (!ValidLibraryCommandEnvelope(command)) return result;
            bool imported = command.archiveHandle != null;
            if (imported)
            {
                if (command.actionToken != MemoryLibraryActions.ForgetPermanent)
                {
                    result.status = MemoryLibraryCommandStatuses.Ineligible;
                    return result;
                }
                return ForgetImportedMemory(command, result);
            }
            return MutateActiveMemory(command, result);
        }

        private MemoryLibraryCommandResult MutateActiveMemory(
            MemoryLibraryCommand command,
            MemoryLibraryCommandResult result)
        {
            MemoryRecordHandle handle = command.recordHandle;
            if (handle == null || string.IsNullOrWhiteSpace(handle.ownerPawnId)
                || string.IsNullOrWhiteSpace(handle.epochToken)
                || string.IsNullOrWhiteSpace(handle.recordId)) return result;
            PawnKnowledgeState owner = FindCurrentMemoryEnvelope(handle.ownerPawnId);
            if (owner == null || owner.autobiographicalEpochToken != handle.epochToken)
            {
                result.status = MemoryLibraryCommandStatuses.Missing;
                return result;
            }

            bool standalone = string.Equals(command.placementToken, "standalone",
                StringComparison.Ordinal);
            List<SavedMemoryBlock> detachedStandalone = CloneSavedBlocks(owner.standaloneBlocks);
            List<SavedMemoryThreadRoot> detachedRoots = CloneSavedRoots(owner.threadRoots);
            SavedMemoryThreadRoot root = null;
            SavedMemoryBlock block;
            long targetRevision;
            if (standalone)
            {
                if (command.rootHandle != null) return result;
                block = FindSavedBlock(detachedStandalone, handle.recordId);
                targetRevision = owner.structuralRevision;
            }
            else
            {
                if (!RootAndRecordHandlesMatch(command.rootHandle, handle)) return result;
                root = FindSavedRoot(detachedRoots, command.rootHandle.rootId);
                if (root == null)
                {
                    result.status = MemoryLibraryCommandStatuses.Missing;
                    return result;
                }
                block = FindSavedBlock(root.visibleBlocks, handle.recordId)
                    ?? (root.rollingSummaryBlock?.recordId == handle.recordId
                        ? root.rollingSummaryBlock : null);
                targetRevision = root.structuralRevision;
            }
            if (block == null)
            {
                result.status = MemoryLibraryCommandStatuses.Missing;
                return result;
            }
            if (targetRevision != command.targetStructuralRevision)
            {
                result.status = MemoryLibraryCommandStatuses.Stale;
                return result;
            }
            MemoryBlockRow dto = BuildMemoryBlockRow(
                block, root, owner.structuralRevision, PawnDiaryRecordName(owner.pawnId),
                BuildMemoryLibraryLimits());
            MemoryLibraryMutationEligibility eligibility = MemoryLibraryPolicy.CheckEligibility(
                command.actionToken,
                dto,
                false,
                command.hasDesiredSuppressed,
                command.wordingDraft,
                BuildMemoryLibraryLimits().blockTextUtf16Units);
            if (!eligibility.validAction)
            {
                result.status = MemoryLibraryCommandStatuses.Invalid;
                return result;
            }
            if (!eligibility.eligible)
            {
                result.status = MemoryLibraryCommandStatuses.Ineligible;
                return result;
            }
            if (command.actionToken == MemoryLibraryActions.ForgetPermanent && !Prefs.DevMode)
            {
                result.status = MemoryLibraryCommandStatuses.Unauthorized;
                return result;
            }
            if (targetRevision == long.MaxValue
                || ((command.actionToken == MemoryLibraryActions.SaveWording
                        || command.actionToken == MemoryLibraryActions.UseOriginalWording)
                    && block.formatRevision == long.MaxValue))
            {
                result.status = MemoryLibraryCommandStatuses.RevisionSaturated;
                return result;
            }

            if (command.actionToken == MemoryLibraryActions.SetSuppressed)
            {
                if (block.suppressed == command.desiredSuppressed)
                {
                    result.status = MemoryLibraryCommandStatuses.Success;
                    result.resultingStructuralRevision = targetRevision;
                    return result;
                }
                block.suppressed = command.desiredSuppressed;
            }
            else if (command.actionToken == MemoryLibraryActions.SaveWording)
            {
                block.playerWording = command.wordingDraft;
                block.playerEdited = true;
                block.formatRevision++;
                MemoryStoreMutationOutcome capacity = ValidateDetachedCapacity(
                    owner, detachedStandalone, detachedRoots, false);
                if (capacity != MemoryStoreMutationOutcome.Admitted)
                {
                    result.status = MemoryLibraryCommandStatuses.CapFull;
                    return result;
                }
            }
            else if (command.actionToken == MemoryLibraryActions.UseOriginalWording)
            {
                block.playerWording = string.Empty;
                block.playerEdited = false;
                block.formatRevision++;
            }
            else if (command.actionToken == MemoryLibraryActions.ForgetPermanent)
            {
                if (standalone) detachedStandalone.Remove(block);
                else ForgetRootBlock(detachedRoots, root, block, owner);
            }

            long nextRevision;
            if (standalone)
            {
                nextRevision = owner.structuralRevision + 1;
                owner.structuralRevision = nextRevision;
            }
            else
            {
                root.structuralRevision++;
                nextRevision = root.structuralRevision;
                if (!detachedRoots.Contains(root))
                {
                    if (owner.structuralRevision == long.MaxValue)
                    {
                        result.status = MemoryLibraryCommandStatuses.RevisionSaturated;
                        return result;
                    }
                    owner.structuralRevision++;
                }
            }
            owner.standaloneBlocks = detachedStandalone;
            owner.threadRoots = detachedRoots;
            MarkMemoryLibraryMutationCommitted();
            result.status = MemoryLibraryCommandStatuses.Success;
            result.resultingStructuralRevision = nextRevision;
            return result;
        }

        private MemoryLibraryCommandResult ForgetImportedMemory(
            MemoryLibraryCommand command,
            MemoryLibraryCommandResult result)
        {
            SavedImportedMemoryRow row;
            long structural;
            if (!TryFindImportedRow(command.archiveHandle, out row, out structural))
            {
                result.status = MemoryLibraryCommandStatuses.Missing;
                return result;
            }
            if (structural != command.targetStructuralRevision)
            {
                result.status = MemoryLibraryCommandStatuses.Stale;
                return result;
            }
            if (!Prefs.DevMode)
            {
                result.status = MemoryLibraryCommandStatuses.Unauthorized;
                return result;
            }
            if (structural == long.MaxValue)
            {
                result.status = MemoryLibraryCommandStatuses.RevisionSaturated;
                return result;
            }
            if (command.archiveHandle.archiveScopeToken == MemoryLibraryScopes.UnresolvedImported)
            {
                List<SavedImportedMemoryRow> retained = new List<SavedImportedMemoryRow>();
                for (int index = 0; unresolvedOwnerArchiveRows != null
                    && index < unresolvedOwnerArchiveRows.Count; index++)
                    if (!ReferenceEquals(unresolvedOwnerArchiveRows[index], row))
                        retained.Add(unresolvedOwnerArchiveRows[index]);
                unresolvedOwnerArchiveRows = retained;
                unresolvedArchiveStructuralRevision++;
                result.resultingStructuralRevision = unresolvedArchiveStructuralRevision;
            }
            else
            {
                PawnKnowledgeState owner = FindCurrentMemoryEnvelope(
                    command.archiveHandle.exactOwnerPawnIdOrEmpty);
                if (owner == null)
                {
                    result.status = MemoryLibraryCommandStatuses.Missing;
                    return result;
                }
                List<SavedImportedMemoryRow> retained = new List<SavedImportedMemoryRow>();
                for (int index = 0; owner.importedArchiveRows != null
                    && index < owner.importedArchiveRows.Count; index++)
                    if (!ReferenceEquals(owner.importedArchiveRows[index], row))
                        retained.Add(owner.importedArchiveRows[index]);
                owner.importedArchiveRows = retained;
                owner.structuralRevision++;
                result.resultingStructuralRevision = owner.structuralRevision;
            }
            MarkMemoryLibraryMutationCommitted();
            result.status = MemoryLibraryCommandStatuses.Success;
            return result;
        }

        private void MarkMemoryLibraryMutationCommitted()
        {
            memoryLibraryDirectoryFingerprint = string.Empty;
            memoryM4IndexesDirty = true;
            memoryMaintenanceDirty = true;
            try
            {
                RebuildMemorySizeIndexes();
            }
            catch (Exception exception)
            {
                // Saved mutation is already complete. Keep every derivative dirty for the next bounded
                // rebuild and never turn a committed command into a missing terminal result.
                memoryM4IndexesDirty = true;
                memoryMaintenanceDirty = true;
                Log.ErrorOnce(
                    "[Pawn Diary] Memory Library indexes will rebuild after a committed mutation: "
                        + exception,
                    "PawnDiary.Memory.Library.IndexRebuild".GetHashCode());
            }
            DiaryStateVersion.Bump();
        }

        private List<MemoryLibraryOwnerIndexSnapshot> BuildMemoryLibraryOwnerSnapshots(
            MemoryLibraryLimits limits)
        {
            Dictionary<string, PawnDiaryRecord> firstDiary =
                new Dictionary<string, PawnDiaryRecord>(StringComparer.Ordinal);
            for (int index = 0; diaries != null && index < diaries.Count; index++)
            {
                PawnDiaryRecord diary = diaries[index];
                if (diary != null && !string.IsNullOrWhiteSpace(diary.pawnId)
                    && !firstDiary.ContainsKey(diary.pawnId)) firstDiary.Add(diary.pawnId, diary);
            }
            Dictionary<string, Pawn> active = new Dictionary<string, Pawn>(StringComparer.Ordinal);
            foreach (Pawn pawn in PawnsFinder
                .AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn == null) continue;
                string id = pawn.GetUniqueLoadID();
                if (!string.IsNullOrWhiteSpace(id) && !active.ContainsKey(id)) active.Add(id, pawn);
            }

            List<MemoryLibraryOwnerIndexSnapshot> data =
                new List<MemoryLibraryOwnerIndexSnapshot>();
            List<MemoryLibraryOwnerIndexSnapshot> zero =
                new List<MemoryLibraryOwnerIndexSnapshot>();
            long legacyOmitted = 0;
            foreach (KeyValuePair<string, PawnDiaryRecord> pair in firstDiary)
            {
                PawnDiaryRecord diary = pair.Value;
                PawnKnowledgeState state = diary.knowledgeState;
                Pawn live;
                active.TryGetValue(pair.Key, out live);
                string name = live?.LabelShortCap ?? diary.pawnName ?? pair.Key;
                if (state != null && state.IsCurrentSchema())
                {
                    MemoryLibraryOwnerIndexSnapshot snapshot = MemoryLibraryIndexPolicy.BuildOwner(
                        BuildOwnerInput(diary, state, name, live != null, limits), limits);
                    if (snapshot != null)
                    {
                        bool hasData = snapshot.ownerRow.threadCount > 0
                            || snapshot.ownerRow.standaloneCount > 0
                            || snapshot.ownerRow.importedCount > 0
                            || snapshot.ownerRow.compatibilityHandle != null;
                        if (hasData) data.Add(snapshot);
                        else if (live != null) zero.Add(snapshot);
                    }
                }
                else if (state != null)
                {
                    data.Add(BuildCompatibilityOnlyOwner(diary, state, name));
                }
                else if (live != null)
                {
                    zero.Add(BuildZeroOwner(pair.Key, name));
                }
            }
            foreach (KeyValuePair<string, Pawn> pair in active)
                if (!firstDiary.ContainsKey(pair.Key))
                    zero.Add(BuildZeroOwner(pair.Key, pair.Value.LabelShortCap));

            if ((unresolvedOwnerArchiveRows != null && unresolvedOwnerArchiveRows.Count > 0)
                || (rawUnresolvedOwnerArchiveInput != null
                    && rawUnresolvedOwnerArchiveInput.Count > 0))
                data.Add(BuildUnknownOwner(limits));

            data.Sort(CompareOwnerSnapshots);
            zero.Sort(CompareOwnerSnapshots);
            int cap = (int)ReadCapacityLong("libraryOwnerEntries", 2048, 8001);
            List<MemoryLibraryOwnerIndexSnapshot> result =
                new List<MemoryLibraryOwnerIndexSnapshot>();
            for (int index = 0; index < data.Count && result.Count < cap; index++)
                result.Add(data[index]);
            if (data.Count > result.Count) legacyOmitted = data.Count - result.Count;
            int includedZero = 0;
            for (int index = 0; index < zero.Count && result.Count < cap; index++)
            {
                result.Add(zero[index]);
                includedZero++;
            }
            memoryLibraryAdditionalLegacyRawOwners = legacyOmitted;
            memoryLibraryAdditionalZeroOwners = zero.Count - includedZero;
            return result;
        }

        private MemoryLibraryOwnerIndexInput BuildOwnerInput(
            PawnDiaryRecord diary,
            PawnKnowledgeState state,
            string displayName,
            bool active,
            MemoryLibraryLimits limits)
        {
            string scope = state.archiveOnly
                ? MemoryLibraryScopes.ArchiveOnly : MemoryLibraryScopes.Active;
            MemoryLibraryOwnerHandle handle = new MemoryLibraryOwnerHandle(
                scope, diary.pawnId, state.autobiographicalEpochToken ?? string.Empty);
            MemoryLibraryOwnerIndexInput input = new MemoryLibraryOwnerIndexInput
            {
                primaryHandle = handle,
                ownerEpochKey = !state.archiveOnly
                    && !string.IsNullOrWhiteSpace(state.autobiographicalEpochToken)
                    ? new MemoryOwnerEpochKey
                    {
                        ownerPawnId = diary.pawnId,
                        epochToken = state.autobiographicalEpochToken
                    }
                    : null,
                displayName = displayName,
                lifecycleToken = active ? "active" : state.archiveOnly ? "archive" : "saved",
                culture = BuildMemoryOwnerCultureDto(state, limits),
                structuralRevision = state.structuralRevision,
                statusRevision = state.statusRevision,
                snapshotNowTick = Math.Max(0, Find.TickManager?.TicksGame ?? 0),
                nextLocalizedDayBoundary = NextMemoryLibraryDayBoundary()
            };
            bool inert = false;
            for (int index = 0; state.threadRoots != null && index < state.threadRoots.Count; index++)
            {
                SavedMemoryThreadRoot root = state.threadRoots[index];
                if (root == null) continue;
                if (HasUnknownNewerReducerRevision(root)) { inert = true; continue; }
                MemoryLibraryRootIndexInput rootInput = BuildRootInput(
                    root, state, displayName, limits);
                if (rootInput != null) input.roots.Add(rootInput);
            }
            for (int index = 0; state.standaloneBlocks != null
                && index < state.standaloneBlocks.Count; index++)
            {
                SavedMemoryBlock block = state.standaloneBlocks[index];
                if (block != null) input.standalone.Add(BuildMemoryBlockRow(
                    block, null, state.structuralRevision, displayName, limits));
            }
            for (int index = 0; state.importedArchiveRows != null
                && index < state.importedArchiveRows.Count; index++)
            {
                SavedImportedMemoryRow row = state.importedArchiveRows[index];
                if (row != null) input.imported.Add(BuildImportedRow(
                    row, scope, diary.pawnId, state.structuralRevision, limits));
            }
            if (inert)
            {
                input.compatibilityHandle = new MemoryLibraryOwnerHandle(
                    MemoryLibraryScopes.InertCurrentExact, diary.pawnId, string.Empty);
                input.compatibilitySourcePayloadRevision = Math.Max(
                    1, Math.Max(state.structuralRevision, state.statusRevision));
            }
            return input;
        }

        private MemoryLibraryRootIndexInput BuildRootInput(
            SavedMemoryThreadRoot root,
            PawnKnowledgeState owner,
            string ownerDisplayName,
            MemoryLibraryLimits limits)
        {
            if (root == null || string.IsNullOrWhiteSpace(root.rootId)) return null;
            MemoryRootHandle handle = new MemoryRootHandle
            {
                ownerPawnId = root.ownerPawnId ?? string.Empty,
                epochToken = root.ownerEpochToken ?? string.Empty,
                rootId = root.rootId
            };
            MemoryLibraryRootIndexInput result = new MemoryLibraryRootIndexInput
            {
                currentStatus = BuildCurrentStatusDto(owner, root, limits),
                header = new MemoryThreadHeaderRow
                {
                    rootHandle = handle,
                    subjectLabel = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                        root.frozenSubjectLabel, limits.frozenDisplayLabelUtf16Units),
                    subjectTypeToken = root.subjectKind == "pawn" ? "Person" : root.subjectKind,
                    latestActivityTick = LatestRootTick(root),
                    chapterCount = root.chapters?.Count ?? 0,
                    structuralRevision = root.structuralRevision,
                    statusRevision = root.statusRevision,
                    normalizedSearch = MemoryLibraryPolicy.BuildSearchProjection(
                        new[] { root.frozenSubjectLabel, ownerDisplayName },
                        limits.normalizedFieldUtf16Units,
                        limits.rowProjectionUtf16Units)
                }
            };
            for (int index = 0; root.visibleBlocks != null
                && index < root.visibleBlocks.Count; index++)
            {
                SavedMemoryBlock block = root.visibleBlocks[index];
                if (block != null) result.children.Add(BuildMemoryBlockRow(
                    block, root, root.structuralRevision, ownerDisplayName, limits));
            }
            if (root.rollingSummaryBlock != null) result.children.Add(BuildMemoryBlockRow(
                root.rollingSummaryBlock, root, root.structuralRevision, ownerDisplayName, limits));
            for (int index = 0; root.chapters != null && index < root.chapters.Count; index++)
            {
                SavedMemoryChapter chapter = root.chapters[index];
                if (chapter == null) continue;
                result.chapters.Add(new MemoryChapterRow
                {
                    chapterId = chapter.chapterId ?? string.Empty,
                    ordinal = chapter.ordinal,
                    phaseToken = chapter.phaseToken ?? string.Empty,
                    openedTick = chapter.openedTick,
                    lastActivityTick = chapter.lastActivityTick,
                    closedTick = chapter.closedTick,
                    closureReasonToken = chapter.closureReasonToken ?? string.Empty,
                    closed = chapter.closed
                });
            }
            result.chapters.Sort((left, right) => right.ordinal.CompareTo(left.ordinal));
            result.children.Sort(delegate(MemoryBlockRow left, MemoryBlockRow right)
            {
                if (left.rollingSummary != right.rollingSummary)
                    return left.rollingSummary ? -1 : 1;
                long leftChapter = ChapterOrdinal(result.chapters, left.chapterId);
                long rightChapter = ChapterOrdinal(result.chapters, right.chapterId);
                int chapter = rightChapter.CompareTo(leftChapter);
                if (chapter != 0) return chapter;
                int tick = left.originalTick.CompareTo(right.originalTick);
                return tick != 0 ? tick : string.Compare(
                    left.recordHandle?.recordId, right.recordHandle?.recordId,
                    StringComparison.Ordinal);
            });
            return result;
        }

        private static MemoryCurrentStatusDto BuildCurrentStatusDto(
            PawnKnowledgeState owner,
            SavedMemoryThreadRoot root,
            MemoryLibraryLimits limits)
        {
            SavedMemoryAwarenessSnapshot selected = null;
            for (int index = 0; owner?.ownerAwarenessSnapshots != null
                && index < owner.ownerAwarenessSnapshots.Count; index++)
            {
                SavedMemoryAwarenessSnapshot candidate = owner.ownerAwarenessSnapshots[index];
                if (candidate != null
                    && string.Equals(candidate.subjectKind, root.subjectKind, StringComparison.Ordinal)
                    && string.Equals(candidate.subjectId, root.subjectId, StringComparison.Ordinal))
                {
                    selected = candidate;
                    break;
                }
            }
            if (selected == null) return new MemoryCurrentStatusDto();
            MemoryCurrentStatusDto result = new MemoryCurrentStatusDto
            {
                statusToken = string.IsNullOrWhiteSpace(selected.trackingStateToken)
                    ? "Unknown" : selected.trackingStateToken,
                knownnessEvidenceToken = selected.knownnessEvidenceToken ?? string.Empty,
                sourceCaptureGeneration = selected.captureInvalidationGeneration,
                capturedTick = selected.lastObservedTick,
                statusSnapshotRevision = selected.snapshotRevision
            };
            int fieldCap = Math.Min(4, selected.stateFacts?.Count ?? 0);
            for (int index = 0; index < fieldCap; index++)
            {
                SavedMemoryStateFact fact = selected.stateFacts[index];
                if (fact != null) result.frozenDisplayFields.Add(
                    MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                        (fact.factKey ?? string.Empty) + ": " + (fact.factValue ?? string.Empty),
                        limits.blockTextUtf16Units));
            }
            return result;
        }

        private MemoryBlockRow BuildMemoryBlockRow(
            SavedMemoryBlock block,
            SavedMemoryThreadRoot root,
            long targetStructuralRevision,
            string ownerDisplayName,
            MemoryLibraryLimits limits)
        {
            bool summary = block.kind == MemoryContractTokens.KindSummary;
            bool rolling = summary
                && block.summaryRole == MemoryContractTokens.SummaryRoleRolling;
            bool closed = summary
                && block.summaryRole == MemoryContractTokens.SummaryRoleClosed;
            int categoryMask = summary
                ? block.summaryPayload?.derivedCategoryMask ?? 0
                : MemoryCategoryBits.ForToken(block.category);
            int importance = MemoryLibraryPolicy.ImportanceMask(summary
                ? block.summaryPayload?.highestSurvivingImportance
                : block.importance);
            string automatic = summary
                ? block.summaryPayload?.deterministicWording ?? block.automaticWording
                : block.automaticWording;
            string wording = block.playerEdited && !string.IsNullOrEmpty(block.playerWording)
                ? block.playerWording : automatic;
            string primary = block.primarySubject?.frozenLabel ?? string.Empty;
            List<string> searchFields = new List<string> { wording, primary };
            for (int index = 0; block.secondarySubjects != null
                && index < block.secondarySubjects.Count; index++)
                searchFields.Add(block.secondarySubjects[index]?.frozenLabel ?? string.Empty);
            if (summary)
            {
                for (int bucketIndex = 0; block.summaryPayload?.factBuckets != null
                    && bucketIndex < block.summaryPayload.factBuckets.Count; bucketIndex++)
                {
                    SavedMemoryFactBucket bucket = block.summaryPayload.factBuckets[bucketIndex];
                    for (int contributionIndex = 0; bucket?.contributions != null
                        && contributionIndex < bucket.contributions.Count; contributionIndex++)
                    {
                        SavedMemoryFactContribution contribution =
                            bucket.contributions[contributionIndex];
                        if (contribution == null) continue;
                        searchFields.Add(contribution.canonicalValue);
                        searchFields.Add(contribution.category);
                    }
                }
            }
            int dateTick = (int)Math.Max(0, Math.Min(int.MaxValue, block.originalEventTick));
            searchFields.Add(KnowledgeDateLabelAt(null, dateTick));
            searchFields.Add(block.category ?? string.Empty);
            searchFields.Add(ownerDisplayName ?? string.Empty);
            bool threaded = root != null;
            bool eventKind = block.kind == MemoryContractTokens.KindEvent;
            bool landmark = block.kind == MemoryContractTokens.KindLandmark;
            bool canEdit = !rolling && (landmark || closed || (!threaded && eventKind));
            MemoryPolicySnapshot policy = MemoryEffectivePolicyProvider.Current;
            long expiry = SummaryFutureExpiry(block, policy);
            return new MemoryBlockRow
            {
                recordHandle = new MemoryRecordHandle
                {
                    ownerPawnId = block.ownerPawnId ?? string.Empty,
                    epochToken = block.ownerEpochToken ?? string.Empty,
                    recordId = block.recordId ?? string.Empty
                },
                rootHandle = threaded ? new MemoryRootHandle
                {
                    ownerPawnId = root.ownerPawnId ?? string.Empty,
                    epochToken = root.ownerEpochToken ?? string.Empty,
                    rootId = root.rootId ?? string.Empty
                } : null,
                chapterId = block.chapterId ?? string.Empty,
                targetStructuralRevision = targetStructuralRevision,
                kind = block.kind ?? string.Empty,
                summaryRole = block.summaryRole ?? string.Empty,
                projectedCategoryMask = categoryMask,
                projectedHighestImportanceMask = importance,
                originalTick = summary
                    ? block.summaryPayload?.earliestSurvivingTick ?? block.originalEventTick
                    : block.originalEventTick,
                activityTick = summary
                    ? block.summaryPayload?.latestSurvivingTick ?? block.originalEventTick
                    : block.originalEventTick,
                projectedNextExpiryTick = expiry,
                displayWording = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                    wording, limits.blockTextUtf16Units),
                primarySubjectLabel = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                    primary, limits.frozenDisplayLabelUtf16Units),
                playerEdited = block.playerEdited,
                suppressed = block.suppressed,
                canSuppress = true,
                canSaveWording = canEdit,
                canUseOriginal = block.playerEdited && !rolling,
                canDevForget = true,
                lastAutomaticIncludedTick = block.lastAutomaticIncludedTick,
                automaticInclusionCount = block.automaticInclusionCount,
                providerExposureState = block.providerExposureState ?? string.Empty,
                normalizedSearch = MemoryLibraryPolicy.BuildSearchProjection(
                    searchFields,
                    limits.normalizedFieldUtf16Units,
                    limits.rowProjectionUtf16Units),
                rollingSummary = rolling,
                closedSummary = closed,
                ageUnknown = block.ageUnknown
            };
        }

        private long SummaryFutureExpiry(SavedMemoryBlock block, MemoryPolicySnapshot policy)
        {
            if (block == null || policy == null || block.playerEdited) return long.MaxValue;
            long now = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            if (block.kind != MemoryContractTokens.KindSummary)
                return MemoryLibraryPolicy.FutureExpiryTick(
                    block.originalEventTick,
                    block.ageUnknown,
                    block.playerEdited,
                    MemoryLibraryPolicy.ImportanceMask(block.importance),
                    policy.minorMemoryLifetimeTicks,
                    policy.regularMemoryLifetimeTicks,
                    now);
            long earliest = long.MaxValue;
            for (int bucketIndex = 0; block.summaryPayload?.factBuckets != null
                && bucketIndex < block.summaryPayload.factBuckets.Count; bucketIndex++)
            {
                SavedMemoryFactBucket bucket = block.summaryPayload.factBuckets[bucketIndex];
                for (int index = 0; bucket?.contributions != null
                    && index < bucket.contributions.Count; index++)
                {
                    SavedMemoryFactContribution contribution = bucket.contributions[index];
                    if (contribution == null) continue;
                    earliest = Math.Min(earliest, MemoryLibraryPolicy.FutureExpiryTick(
                        contribution.originalEventTick,
                        contribution.ageUnknown,
                        false,
                        MemoryLibraryPolicy.ImportanceMask(contribution.importance),
                        policy.minorMemoryLifetimeTicks,
                        policy.regularMemoryLifetimeTicks,
                        now));
                }
            }
            return earliest;
        }

        private MemoryBlockDetail BuildMemoryBlockDetail(
            SavedMemoryBlock block,
            MemoryLibraryLimits limits)
        {
            MemoryBlockDetail result = new MemoryBlockDetail
            {
                automaticWording = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                    block.kind == MemoryContractTokens.KindSummary
                        ? block.summaryPayload?.deterministicWording
                        : block.automaticWording,
                    limits.blockTextUtf16Units),
                playerWording = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                    block.playerWording, limits.blockTextUtf16Units),
                sourcePageLinkToken = block.sourceEventId ?? string.Empty
            };
            for (int index = 0; block.facts != null && index < block.facts.Count && index < 16; index++)
            {
                SavedMemoryCanonicalFact fact = block.facts[index];
                if (fact != null) result.factDescriptors.Add(
                    (fact.factKind ?? string.Empty) + ":" +
                    MemoryLibraryPolicy.ClampUtf16CompleteScalar(fact.canonicalValue, 240));
            }
            if (block.kind == MemoryContractTokens.KindSummary)
            {
                for (int bucketIndex = 0; block.summaryPayload?.factBuckets != null
                    && bucketIndex < block.summaryPayload.factBuckets.Count
                    && result.factDescriptors.Count < 16; bucketIndex++)
                {
                    SavedMemoryFactBucket bucket = block.summaryPayload.factBuckets[bucketIndex];
                    if (bucket == null) continue;
                    result.factDescriptors.Add((bucket.factKind ?? string.Empty) + ":" +
                        MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                            bucket.derivedRangeMax, 240));
                }
                for (int index = 0; block.summaryPayload?.subjectRefs != null
                    && index < block.summaryPayload.subjectRefs.Count
                    && result.subjectDescriptors.Count < 16; index++)
                    AddSubjectDetail(result.subjectDescriptors,
                        block.summaryPayload.subjectRefs[index],
                        limits.frozenDisplayLabelUtf16Units);
                for (int index = 0; block.summaryPayload?.provenanceRefs != null
                    && index < block.summaryPayload.provenanceRefs.Count && index < 8; index++)
                    AddProvenanceDetail(result.provenanceDescriptors,
                        block.summaryPayload.provenanceRefs[index]);
            }
            else
            {
                AddSubjectDetail(result.subjectDescriptors, block.primarySubject,
                    limits.frozenDisplayLabelUtf16Units);
                for (int index = 0; block.secondarySubjects != null
                    && index < block.secondarySubjects.Count
                    && result.subjectDescriptors.Count < 16; index++)
                    AddSubjectDetail(result.subjectDescriptors, block.secondarySubjects[index],
                        limits.frozenDisplayLabelUtf16Units);
                for (int index = 0; block.provenance != null
                    && index < block.provenance.Count && index < 8; index++)
                    AddProvenanceDetail(result.provenanceDescriptors, block.provenance[index]);
            }
            if (Prefs.DevMode)
            {
                result.devIdentifiersAndReasons.Add("record=" + (block.recordId ?? string.Empty));
                result.devIdentifiersAndReasons.Add("source=" + (block.sourceOccurrenceId ?? string.Empty));
                result.devIdentifiersAndReasons.Add("root=" + (block.rootId ?? string.Empty));
                result.devIdentifiersAndReasons.Add("chapter=" + (block.chapterId ?? string.Empty));
            }
            return result;
        }

        private static void AddSubjectDetail(
            List<string> target,
            SavedMemorySubjectRef subject,
            int labelCap)
        {
            if (subject == null || target == null) return;
            target.Add((subject.subjectKind ?? string.Empty) + ":" +
                MemoryLibraryPolicy.ClampUtf16CompleteScalar(subject.frozenLabel, labelCap));
        }

        private static void AddProvenanceDetail(
            List<string> target,
            SavedMemoryProvenance provenance)
        {
            if (target == null || provenance == null) return;
            target.Add((provenance.sourceKindToken ?? string.Empty) + ":" +
                (provenance.sourceOccurrenceId ?? string.Empty));
        }

        private MemoryImportedRow BuildImportedRow(
            SavedImportedMemoryRow row,
            string scope,
            string ownerId,
            long structuralRevision,
            MemoryLibraryLimits limits)
        {
            return new MemoryImportedRow
            {
                archiveHandle = new MemoryArchiveHandle
                {
                    archiveScopeToken = scope,
                    exactOwnerPawnIdOrEmpty = ownerId ?? string.Empty,
                    archiveRecordId = row.archiveRecordId ?? string.Empty
                },
                preview = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                    row.importedWording, limits.importedPreviewUtf16Units),
                normalizedSearch = MemoryLibraryPolicy.NormalizeSearch(
                    row.importedWording,
                    limits.rowProjectionUtf16Units,
                    limits.rowProjectionUtf16Units),
                originalTick = row.originalEventTick,
                ageUnknown = row.ageUnknown,
                migrationReasonToken = row.migrationReasonToken ?? string.Empty,
                targetStructuralRevision = structuralRevision
            };
        }

        private MemoryOwnerCultureDto BuildMemoryOwnerCultureDto(
            PawnKnowledgeState state,
            MemoryLibraryLimits limits)
        {
            MemoryOwnerCultureDto result = new MemoryOwnerCultureDto();
            ResolveCultureDisplay(state?.originCultureDefName, out result.originStateToken,
                out result.originDisplayLabel, limits);
            string source = state?.originCultureSource ?? string.Empty;
            result.originProvenanceToken = MemoryLibraryPolicy.CultureProvenanceToken(source);
            ResolveCultureDisplay(state?.adoptedCultureDefName, out result.adoptedStateToken,
                out result.adoptedDisplayLabel, limits);
            return result;
        }

        private static void ResolveCultureDisplay(
            string defName,
            out string stateToken,
            out string displayLabel,
            MemoryLibraryLimits limits)
        {
            if (string.IsNullOrWhiteSpace(defName))
            {
                stateToken = "none";
                displayLabel = string.Empty;
                return;
            }
            DiaryCultureProfileDef def =
                DefDatabase<DiaryCultureProfileDef>.GetNamedSilentFail(defName.Trim());
            stateToken = MemoryLibraryPolicy.CultureStateToken(defName, def != null);
            displayLabel = def == null ? string.Empty
                : MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                    def.LabelCap.ToString(), limits.frozenDisplayLabelUtf16Units);
        }

        private MemoryLibraryOwnerIndexSnapshot BuildZeroOwner(string ownerId, string name)
        {
            return MemoryLibraryIndexPolicy.BuildOwner(new MemoryLibraryOwnerIndexInput
            {
                primaryHandle = new MemoryLibraryOwnerHandle(
                    MemoryLibraryScopes.Active, ownerId, string.Empty),
                displayName = name,
                lifecycleToken = "active",
                structuralRevision = 0,
                statusRevision = 0,
                nextLocalizedDayBoundary = NextMemoryLibraryDayBoundary()
            }, BuildMemoryLibraryLimits());
        }

        private MemoryLibraryOwnerIndexSnapshot BuildCompatibilityOnlyOwner(
            PawnDiaryRecord diary,
            PawnKnowledgeState state,
            string name)
        {
            MemoryLibraryOwnerHandle compatibility = new MemoryLibraryOwnerHandle(
                MemoryLibraryScopes.LegacyRawExact, diary.pawnId, string.Empty);
            return new MemoryLibraryOwnerIndexSnapshot
            {
                ownerRow = new MemoryLibraryOwnerRow
                {
                    compatibilityHandle = compatibility,
                    displayName = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                        name, BuildMemoryLibraryLimits().frozenDisplayLabelUtf16Units),
                    lifecycleToken = "migration_pending",
                    culture = BuildMemoryOwnerCultureDto(state, BuildMemoryLibraryLimits()),
                    legacyRawPending = true,
                    compatibilitySourcePayloadRevision = Math.Max(
                        1, Math.Max(state.structuralRevision, state.statusRevision)),
                    normalizedSearch = MemoryLibraryPolicy.NormalizeSearch(name, 80, 160)
                }
            };
        }

        private MemoryLibraryOwnerIndexSnapshot BuildUnknownOwner(MemoryLibraryLimits limits)
        {
            bool current = unresolvedOwnerArchiveRows != null
                && unresolvedOwnerArchiveRows.Count > 0;
            MemoryLibraryOwnerHandle primary = current
                ? new MemoryLibraryOwnerHandle(
                    MemoryLibraryScopes.UnresolvedImported, string.Empty, string.Empty)
                : null;
            MemoryLibraryOwnerHandle compatibility = rawUnresolvedOwnerArchiveInput != null
                && rawUnresolvedOwnerArchiveInput.Count > 0
                ? new MemoryLibraryOwnerHandle(
                    MemoryLibraryScopes.LegacyRawUnknown, string.Empty, string.Empty)
                : null;
            MemoryLibraryOwnerIndexSnapshot result = new MemoryLibraryOwnerIndexSnapshot
            {
                ownerRow = new MemoryLibraryOwnerRow
                {
                    primaryHandle = primary,
                    compatibilityHandle = compatibility,
                    displayName = "PawnDiary.Memory.Library.UnknownOwner".Translate().ToString(),
                    lifecycleToken = "unknown",
                    importedCount = unresolvedOwnerArchiveRows?.Count ?? 0,
                    hasArchive = true,
                    legacyRawPending = compatibility != null,
                    structuralRevision = unresolvedArchiveStructuralRevision,
                    compatibilitySourcePayloadRevision = compatibility == null ? 0
                        : Math.Max(1, rawUnresolvedArchiveReattributionGeneration)
                }
            };
            result.ownerRow.normalizedSearch = MemoryLibraryPolicy.NormalizeSearch(
                result.ownerRow.displayName, limits.searchScalars, limits.searchUtf16Units);
            for (int index = 0; unresolvedOwnerArchiveRows != null
                && index < unresolvedOwnerArchiveRows.Count; index++)
            {
                SavedImportedMemoryRow row = unresolvedOwnerArchiveRows[index];
                if (row != null) result.imported.Add(BuildImportedRow(
                    row,
                    MemoryLibraryScopes.UnresolvedImported,
                    string.Empty,
                    unresolvedArchiveStructuralRevision,
                    limits));
            }
            return result;
        }

        private string DirectoryFingerprint(List<MemoryLibraryOwnerIndexSnapshot> rows)
        {
            List<string> fields = new List<string>();
            for (int index = 0; rows != null && index < rows.Count; index++)
            {
                MemoryLibraryOwnerRow row = rows[index]?.ownerRow;
                if (row == null) continue;
                fields.Add(OwnerIndexKey(row.primaryHandle));
                fields.Add(OwnerIndexKey(row.compatibilityHandle));
                fields.Add(row.displayName ?? string.Empty);
                fields.Add(row.lifecycleToken ?? string.Empty);
                fields.Add(row.threadCount.ToString(CultureInfo.InvariantCulture));
                fields.Add(row.standaloneCount.ToString(CultureInfo.InvariantCulture));
                fields.Add(row.importedCount.ToString(CultureInfo.InvariantCulture));
                fields.Add(row.latestActivityTick.ToString(CultureInfo.InvariantCulture));
                fields.Add(row.structuralRevision.ToString(CultureInfo.InvariantCulture));
                fields.Add(row.statusRevision.ToString(CultureInfo.InvariantCulture));
                fields.Add(row.compatibilitySourcePayloadRevision
                    .ToString(CultureInfo.InvariantCulture));
                fields.Add(row.hasArchive ? "1" : "0");
                fields.Add(row.legacyRawPending ? "1" : "0");
                fields.Add(CultureFingerprint(row.culture));
                fields.Add(OwnerSnapshotFingerprint(rows[index]));
            }
            fields.Add(memoryLibraryAdditionalLegacyRawOwners.ToString(CultureInfo.InvariantCulture));
            fields.Add(memoryLibraryAdditionalZeroOwners.ToString(CultureInfo.InvariantCulture));
            return MemoryLibraryPolicy.StreamFingerprint(fields.ToArray());
        }

        private static string OwnerSnapshotFingerprint(MemoryLibraryOwnerIndexSnapshot snapshot)
        {
            List<string> fields = new List<string>();
            for (int rootIndex = 0; snapshot?.roots != null
                && rootIndex < snapshot.roots.Count; rootIndex++)
            {
                MemoryLibraryRootIndexInput root = snapshot.roots[rootIndex];
                fields.Add(RootHandleKey(root?.header?.rootHandle));
                fields.Add(root?.header?.normalizedSearch ?? string.Empty);
                fields.Add((root?.header?.structuralRevision ?? 0)
                    .ToString(CultureInfo.InvariantCulture));
                for (int childIndex = 0; root?.children != null
                    && childIndex < root.children.Count; childIndex++)
                    AddBlockFingerprintFields(fields, root.children[childIndex]);
            }
            for (int index = 0; snapshot?.standalone != null
                && index < snapshot.standalone.Count; index++)
                AddBlockFingerprintFields(fields, snapshot.standalone[index]);
            for (int index = 0; snapshot?.imported != null
                && index < snapshot.imported.Count; index++)
            {
                MemoryImportedRow row = snapshot.imported[index];
                fields.Add(ArchiveHandleKey(row?.archiveHandle));
                fields.Add(row?.preview ?? string.Empty);
                fields.Add(row?.normalizedSearch ?? string.Empty);
                fields.Add((row?.targetStructuralRevision ?? 0)
                    .ToString(CultureInfo.InvariantCulture));
            }
            return MemoryLibraryPolicy.StreamFingerprint(fields.ToArray());
        }

        private static void AddBlockFingerprintFields(
            List<string> fields,
            MemoryBlockRow row)
        {
            fields.Add(row?.recordHandle?.recordId ?? string.Empty);
            fields.Add(row?.displayWording ?? string.Empty);
            fields.Add(row?.normalizedSearch ?? string.Empty);
            fields.Add(row != null && row.playerEdited ? "1" : "0");
            fields.Add(row != null && row.suppressed ? "1" : "0");
            fields.Add((row?.targetStructuralRevision ?? 0)
                .ToString(CultureInfo.InvariantCulture));
            fields.Add((row?.projectedNextExpiryTick ?? long.MaxValue)
                .ToString(CultureInfo.InvariantCulture));
        }

        private static string CultureFingerprint(MemoryOwnerCultureDto culture)
        {
            if (culture == null) return string.Empty;
            return string.Join("|", culture.originStateToken, culture.originDisplayLabel,
                culture.originProvenanceToken, culture.adoptedStateToken,
                culture.adoptedDisplayLabel);
        }

        private long ResolveLibraryPublication(
            Dictionary<string, MemoryLibraryPublication> cache,
            string fingerprint,
            long expectedRevision)
        {
            if (expectedRevision < 0) return 0;
            if (cache.TryGetValue(fingerprint, out MemoryLibraryPublication existing))
                return expectedRevision == 0 || expectedRevision == existing.revision
                    ? existing.revision : 0;
            if (expectedRevision > 0) return 0;
            return AllocateLibraryPublication(cache, fingerprint);
        }

        private long ResolveLibraryPublicationWithContent(
            Dictionary<string, MemoryLibraryPublication> cache,
            string streamFingerprint,
            string contentFingerprint,
            long expectedRevision)
        {
            if (expectedRevision < 0) return 0;
            if (cache.TryGetValue(streamFingerprint, out MemoryLibraryPublication existing))
            {
                if (!string.Equals(existing.fingerprint, contentFingerprint,
                    StringComparison.Ordinal))
                {
                    if (expectedRevision > 0) return 0;
                    cache.Remove(streamFingerprint);
                    return AllocateLibraryPublication(
                        cache, streamFingerprint, contentFingerprint);
                }
                return expectedRevision == 0 || expectedRevision == existing.revision
                    ? existing.revision : 0;
            }
            if (expectedRevision > 0) return 0;
            return AllocateLibraryPublication(cache, streamFingerprint, contentFingerprint);
        }

        private long AllocateLibraryPublication(
            Dictionary<string, MemoryLibraryPublication> cache,
            string fingerprint)
        {
            return AllocateLibraryPublication(cache, fingerprint, fingerprint);
        }

        private long AllocateLibraryPublication(
            Dictionary<string, MemoryLibraryPublication> cache,
            string cacheKey,
            string contentFingerprint)
        {
            if (cache.TryGetValue(cacheKey, out MemoryLibraryPublication existing))
                return existing.revision;
            int cap = Math.Max(1, (int)ReadCapacityLong("cachedOwnerStates", 4, 8));
            if (cache.Count >= cap) cache.Clear();
            if (!memoryLibraryClock.TryAllocate(out long revision))
            {
                memoryLibraryRevisionSaturated = true;
                return 0;
            }
            cache[cacheKey] = new MemoryLibraryPublication
            {
                fingerprint = contentFingerprint,
                revision = revision
            };
            return revision;
        }

        private string ListQueryFingerprint(
            MemoryLibraryListQuery query,
            MemoryLibraryOwnerRow owner)
        {
            MemoryLibraryLimits limits = BuildMemoryLibraryLimits();
            string search = MemoryLibraryPolicy.NormalizeSearch(
                query.search, limits.searchScalars, limits.searchUtf16Units);
            return MemoryLibraryPolicy.StreamFingerprint(
                OwnerIndexKey(query.primaryHandle),
                OwnerEpochKey(query.activeOwnerEpochKey),
                query.viewTag ?? string.Empty,
                FiltersKey(query.filters),
                search,
                query.sortToken ?? string.Empty,
                Math.Min(query.listCount, limits.libraryWindowRows)
                    .ToString(CultureInfo.InvariantCulture));
        }

        private string DetailQueryFingerprint(MemoryThreadDetailQuery query)
        {
            MemoryLibraryLimits limits = BuildMemoryLibraryLimits();
            return MemoryLibraryPolicy.StreamFingerprint(
                RootHandleKey(query.rootHandle),
                FiltersKey(query.filters),
                MemoryLibraryPolicy.NormalizeSearch(
                    query.search, limits.searchScalars, limits.searchUtf16Units),
                Math.Min(query.detailCount, limits.libraryWindowRows)
                    .ToString(CultureInfo.InvariantCulture));
        }

        private MemoryLibraryLimits BuildMemoryLibraryLimits()
        {
            return new MemoryLibraryLimits
            {
                libraryWindowRows = (int)ReadCapacityLong("libraryWindowRows", 64, 256),
                libraryWindowCeiling = 256,
                chapterHeaderRows = (int)ReadCapacityLong(
                    "chapterHeaderWindowRows", 32, 128),
                searchScalars = (int)ReadCapacityTuplePart("searchQueryBounds", 0, 80, 320),
                searchUtf16Units = (int)ReadCapacityTuplePart("searchQueryBounds", 1, 160, 640),
                normalizedFieldUtf16Units = (int)ReadCapacityLong(
                    "normalizedSearchFieldUnits", 120, 1200),
                rowProjectionUtf16Units = (int)ReadCapacityLong(
                    "rowSearchProjectionUnits", 480, 4800),
                frozenDisplayLabelUtf16Units = (int)ReadCapacityLong(
                    "frozenDisplayLabelUnits", 80, 320),
                blockTextUtf16Units = (int)ReadCapacityLong("blockWordingUnits", 240, 1200),
                importedPreviewUtf16Units = (int)ReadCapacityTuplePart(
                    "importedPreviewChunkUnits", 0, 240, 1000),
                importedTextChunkUtf16Units = (int)ReadCapacityTuplePart(
                    "importedPreviewChunkUnits", 1, 1000, 4000),
                commandEntries = (int)ReadCapacityLong("libraryCommandEntries", 32, 128)
            };
        }

        private long NextMemoryLibraryDayBoundary()
        {
            long now = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            long day = now / 60000L;
            return day >= long.MaxValue / 60000L - 1
                ? long.MaxValue : (day + 1) * 60000L;
        }

        private static MemoryLibraryListResult InvalidList(string reason)
        {
            return new MemoryLibraryListResult
            {
                status = MemoryLibraryStatuses.Invalid,
                reasonToken = reason ?? string.Empty
            };
        }

        private bool TryFindImportedRow(
            MemoryArchiveHandle handle,
            out SavedImportedMemoryRow row,
            out long structuralRevision)
        {
            row = null;
            structuralRevision = 0;
            if (handle == null || string.IsNullOrWhiteSpace(handle.archiveRecordId)) return false;
            if (handle.archiveScopeToken == MemoryLibraryScopes.UnresolvedImported
                && string.IsNullOrEmpty(handle.exactOwnerPawnIdOrEmpty))
            {
                row = FindImported(unresolvedOwnerArchiveRows, handle.archiveRecordId);
                structuralRevision = unresolvedArchiveStructuralRevision;
                return row != null;
            }
            if (handle.archiveScopeToken != MemoryLibraryScopes.Active
                && handle.archiveScopeToken != MemoryLibraryScopes.ArchiveOnly) return false;
            PawnKnowledgeState owner = FindCurrentMemoryEnvelope(handle.exactOwnerPawnIdOrEmpty);
            if (owner == null) return false;
            row = FindImported(owner.importedArchiveRows, handle.archiveRecordId);
            structuralRevision = owner.structuralRevision;
            return row != null;
        }

        private static SavedImportedMemoryRow FindImported(
            List<SavedImportedMemoryRow> rows,
            string id)
        {
            for (int index = 0; rows != null && index < rows.Count; index++)
                if (rows[index]?.archiveRecordId == id) return rows[index];
            return null;
        }

        private static SavedMemoryBlock FindSavedBlock(List<SavedMemoryBlock> rows, string id)
        {
            for (int index = 0; rows != null && index < rows.Count; index++)
                if (rows[index]?.recordId == id) return rows[index];
            return null;
        }

        private static SavedMemoryThreadRoot FindSavedRoot(
            List<SavedMemoryThreadRoot> rows,
            string id)
        {
            for (int index = 0; rows != null && index < rows.Count; index++)
                if (rows[index]?.rootId == id) return rows[index];
            return null;
        }

        private static bool RootAndRecordHandlesMatch(
            MemoryRootHandle root,
            MemoryRecordHandle record)
        {
            return root != null && record != null
                && !string.IsNullOrWhiteSpace(root.rootId)
                && root.ownerPawnId == record.ownerPawnId
                && root.epochToken == record.epochToken;
        }

        private static void ForgetRootBlock(
            List<SavedMemoryThreadRoot> roots,
            SavedMemoryThreadRoot root,
            SavedMemoryBlock block,
            PawnKnowledgeState owner)
        {
            if (ReferenceEquals(root.rollingSummaryBlock, block)) root.rollingSummaryBlock = null;
            else root.visibleBlocks.Remove(block);
            if (block.summaryRole == MemoryContractTokens.SummaryRoleClosed)
            {
                for (int index = 0; root.chapters != null && index < root.chapters.Count; index++)
                {
                    SavedMemoryChapter chapter = root.chapters[index];
                    if (chapter?.closedSummaryRecordId == block.recordId)
                        chapter.closedSummaryRecordId = string.Empty;
                }
            }
            for (int index = root.chapters.Count - 1; index >= 0; index--)
            {
                SavedMemoryChapter chapter = root.chapters[index];
                bool referenced = false;
                for (int blockIndex = 0; root.visibleBlocks != null
                    && blockIndex < root.visibleBlocks.Count; blockIndex++)
                    if (root.visibleBlocks[blockIndex]?.chapterId == chapter?.chapterId)
                        referenced = true;
                if (!referenced && chapter != null && !chapter.closed)
                    root.chapters.RemoveAt(index);
            }
            if ((root.visibleBlocks == null || root.visibleBlocks.Count == 0)
                && root.rollingSummaryBlock == null) roots.Remove(root);
        }

        private static bool ValidLibraryCommandEnvelope(MemoryLibraryCommand command)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.libraryClientToken)
                || command.libraryClientToken.Length > 120 || command.commandId <= 0
                || command.targetStructuralRevision <= 0) return false;
            bool active = command.recordHandle != null;
            bool imported = command.archiveHandle != null;
            return active != imported;
        }

        private static MemoryLibraryCommandResult NewLibraryCommandResult(
            MemoryLibraryCommand command)
        {
            return new MemoryLibraryCommandResult
            {
                libraryClientToken = command?.libraryClientToken ?? string.Empty,
                commandId = command?.commandId ?? 0,
                status = MemoryLibraryCommandStatuses.Invalid
            };
        }

        private MemoryLibraryOwnerRow FindCompatibilityDirectoryRow(
            MemoryLibraryOwnerHandle handle)
        {
            string key = OwnerIndexKey(handle);
            for (int index = 0; index < memoryLibraryDirectory.Count; index++)
                if (OwnerIndexKey(memoryLibraryDirectory[index]?.compatibilityHandle) == key)
                    return memoryLibraryDirectory[index];
            return null;
        }

        private long CompatibilityRowCount(MemoryLibraryOwnerHandle handle)
        {
            if (handle.scopeToken == MemoryLibraryScopes.LegacyRawUnknown)
                return rawUnresolvedOwnerArchiveInput?.Count ?? 0;
            if (handle.scopeToken == MemoryLibraryScopes.InertCurrentExact)
            {
                PawnKnowledgeState owner = FindCurrentMemoryEnvelope(handle.exactOwnerPawnIdOrEmpty);
                long count = 0;
                for (int index = 0; owner?.threadRoots != null
                    && index < owner.threadRoots.Count; index++)
                    if (HasUnknownNewerReducerRevision(owner.threadRoots[index])) count++;
                return count;
            }
            PawnDiaryRecord diary = LookupDiaryByPawnId(handle.exactOwnerPawnIdOrEmpty);
            return diary?.knowledgeState?.records?.Count ?? 0;
        }

        private string PawnDiaryRecordName(string ownerId)
        {
            return LookupDiaryByPawnId(ownerId)?.pawnName ?? ownerId ?? string.Empty;
        }

        private static long LatestRootTick(SavedMemoryThreadRoot root)
        {
            long latest = 0;
            for (int index = 0; root?.visibleBlocks != null
                && index < root.visibleBlocks.Count; index++)
                latest = Math.Max(latest, root.visibleBlocks[index]?.originalEventTick ?? 0);
            latest = Math.Max(latest, root?.rollingSummaryBlock?.summaryPayload?.latestSurvivingTick ?? 0);
            return latest;
        }

        private static long ChapterOrdinal(List<MemoryChapterRow> chapters, string chapterId)
        {
            for (int index = 0; chapters != null && index < chapters.Count; index++)
                if (chapters[index]?.chapterId == chapterId) return chapters[index].ordinal;
            return long.MinValue;
        }

        private static int CompareOwnerSnapshots(
            MemoryLibraryOwnerIndexSnapshot left,
            MemoryLibraryOwnerIndexSnapshot right)
        {
            return string.Compare(
                OwnerIndexKey(left?.ownerRow?.primaryHandle)
                    + OwnerIndexKey(left?.ownerRow?.compatibilityHandle),
                OwnerIndexKey(right?.ownerRow?.primaryHandle)
                    + OwnerIndexKey(right?.ownerRow?.compatibilityHandle),
                StringComparison.Ordinal);
        }

        private static string OwnerIndexKey(MemoryLibraryOwnerHandle handle)
        {
            return handle == null ? string.Empty : string.Join("|",
                handle.scopeToken ?? string.Empty,
                handle.exactOwnerPawnIdOrEmpty ?? string.Empty,
                handle.epochTokenOrEmpty ?? string.Empty);
        }

        private static string OwnerEpochKey(MemoryOwnerEpochKey key)
        {
            return key == null ? string.Empty
                : (key.ownerPawnId ?? string.Empty) + "|" + (key.epochToken ?? string.Empty);
        }

        private static string RootHandleKey(MemoryRootHandle handle)
        {
            return handle == null ? string.Empty : string.Join("|",
                handle.ownerPawnId ?? string.Empty,
                handle.epochToken ?? string.Empty,
                handle.rootId ?? string.Empty);
        }

        private static string ArchiveHandleKey(MemoryArchiveHandle handle)
        {
            return handle == null ? string.Empty : string.Join("|",
                handle.archiveScopeToken ?? string.Empty,
                handle.exactOwnerPawnIdOrEmpty ?? string.Empty,
                handle.archiveRecordId ?? string.Empty);
        }

        private static string FiltersKey(MemoryLibraryFilters filters)
        {
            MemoryLibraryFilters value = filters ?? new MemoryLibraryFilters();
            return string.Join("|",
                value.importanceMask.ToString(CultureInfo.InvariantCulture),
                value.categoryMask.ToString(CultureInfo.InvariantCulture),
                value.stateToken ?? string.Empty);
        }

        private static string LibraryCommandKey(string client, long commandId)
        {
            return (client ?? string.Empty) + "|" + commandId.ToString(CultureInfo.InvariantCulture);
        }
    }
}

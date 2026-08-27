// MemoryLibraryUiPolicy.cs — pure Phase-M9 Library navigation, draft, lifetime, and viewport rules.
//
// The RimWorld dialog owns only detached instances of these classes. Repository reads and command
// drains remain in DiaryGameComponent, while immediate-mode drawing changes session buffers only.
// Keeping these decisions free of Verse/Unity types lets the standalone MemoryThreadTests exercise
// owner identity, conflict, TTL, and virtualization behavior without launching the game.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Allocation-free source/publication stamp used by the UI to avoid repeating identical
    /// repository queries after every detached result has settled.
    /// </summary>
    internal struct MemoryLibraryUiRepositoryStamp : IEquatable<MemoryLibraryUiRepositoryStamp>
    {
        public int diaryStateRevision;
        public long observationPublicationRevision;
        public long settingsRevision;
        public long ttlDayRevision;
        public long directoryRevision;
        public long publicationRevision;
        public long languageDisplayRevision;
        public int diaryCount;
        public int unresolvedCount;
        public int rawUnresolvedCount;

        public bool Equals(MemoryLibraryUiRepositoryStamp other)
        {
            return diaryStateRevision == other.diaryStateRevision
                && observationPublicationRevision == other.observationPublicationRevision
                && settingsRevision == other.settingsRevision
                && ttlDayRevision == other.ttlDayRevision
                && directoryRevision == other.directoryRevision
                && publicationRevision == other.publicationRevision
                && languageDisplayRevision == other.languageDisplayRevision
                && diaryCount == other.diaryCount
                && unresolvedCount == other.unresolvedCount
                && rawUnresolvedCount == other.rawUnresolvedCount;
        }
    }

    /// <summary>Pure cadence rule for bounded Library repository polling outside GUI draw.</summary>
    internal static class MemoryLibraryUiPollPolicy
    {
        /// <summary>
        /// Ready detached publications normally sleep until their repository stamp changes. Exact
        /// TTL can cross mid-day without changing that stamp, so a reached finite publication
        /// deadline is itself immediate repository work.
        /// </summary>
        public static bool ReachedPublicationExpiry(
            long nowTick,
            long listExpiryTickExclusive,
            long threadExpiryTickExclusive,
            long blockExpiryTickExclusive)
        {
            return Reached(nowTick, listExpiryTickExclusive)
                || Reached(nowTick, threadExpiryTickExclusive)
                || Reached(nowTick, blockExpiryTickExclusive);
        }

        private static bool Reached(long nowTick, long expiryTickExclusive)
        {
            return nowTick >= 0 && expiryTickExclusive > 0
                && expiryTickExclusive != long.MaxValue
                && nowTick >= expiryTickExclusive;
        }

        public static bool ShouldPoll(
            int currentFrame,
            int lastPollFrame,
            int configuredIntervalFrames,
            bool immediateWork,
            bool waitingForPublication)
        {
            // A Ready detached result is immutable until this adapter observes a changed repository
            // stamp. Do no work at all in that warm state; the interval is only a retry cadence for
            // sliced Preparing publications.
            if (!immediateWork && !waitingForPublication) return false;
            if (immediateWork || lastPollFrame < 0 || currentFrame < lastPollFrame) return true;
            int interval = configuredIntervalFrames >= 1 && configuredIntervalFrames <= 60
                ? configuredIntervalFrames : 6;
            return (long)currentFrame - lastPollFrame >= interval;
        }

        /// <summary>
        /// Paused real-time updates may advance observation only for an open Library that is waiting
        /// on an unstable publication. Normal observation cadence remains game-tick driven.
        /// </summary>
        public static bool ShouldAdvancePausedObservation(
            bool gamePaused,
            bool hasActiveClient,
            bool observationPublicationStable)
        {
            return gamePaused && hasActiveClient && !observationPublicationStable;
        }
    }

    internal static class MemoryLibraryUiLifetimeTokens
    {
        public const string Minor = "minor";
        public const string Regular = "regular";
        public const string Important = "important";
        public const string Protected = "protected";
        public const string Due = "due";
        public const string Mixed = "mixed";
        public const string Unknown = "unknown";
    }

    /// <summary>Detached lifetime facts formatted by the main-thread Library display adapter.</summary>
    internal sealed class MemoryLibraryUiLifetime
    {
        public string stateToken = MemoryLibraryUiLifetimeTokens.Unknown;
        public int importanceMask;
        public long expiryTick = long.MaxValue;
        public long remainingTicks;
        public bool containsDueContribution;
    }

    /// <summary>Pure fixed-card viewport range; the renderer never visits rows outside this window.</summary>
    internal sealed class MemoryLibraryUiVirtualWindow
    {
        public int firstIndex;
        public int endExclusive;
        public int materializedCount;
        public float contentHeight;
    }

    /// <summary>
    /// One bounded step while resolving an exact owner across a paged immutable directory. The UI
    /// retains the canonical first-row fallback but does not change selection until the walk either
    /// finds its exact target or exhausts the pinned publication.
    /// </summary>
    internal sealed class MemoryLibraryUiOwnerWalkStep
    {
        public MemoryLibraryOwnerRow fallback;
        public MemoryLibraryOwnerRow selected;
        public bool continuePaging;
    }

    /// <summary>
    /// One detached wording draft. Structural conflicts retain text; status-only refreshes replace
    /// only the status fence and never invalidate the player's work.
    /// </summary>
    internal sealed class MemoryLibraryUiEditDraft
    {
        public MemoryRecordHandle recordHandle;
        public MemoryRootHandle rootHandle;
        public string placementToken = string.Empty;
        public long targetStructuralRevision;
        public long latestStatusRevision;
        public string text = string.Empty;
        public bool structuralConflict;
        public string terminalStatus = string.Empty;
    }

    /// <summary>Session-only Library state. It owns no saved collection, Pawn, Def, or GUI object.</summary>
    internal sealed class MemoryLibraryUiSession
    {
        public MemoryLibraryOwnerHandle selectedOwnerHandle;
        public MemoryOwnerEpochKey selectedOwnerEpochKey;
        public MemoryLibraryOwnerHandle selectedCompatibilityHandle;
        public string selectedOwnerDisplayName = string.Empty;
        public string selectedView = MemoryLibraryViews.Threads;
        public MemoryLibraryFilters filters = new MemoryLibraryFilters();
        public string ownerSearch = string.Empty;
        public string memorySearch = string.Empty;
        public string sortToken = "newest";
        public MemoryRootHandle selectedRootHandle;
        public MemoryRecordHandle selectedRecordHandle;
        public MemoryArchiveHandle selectedArchiveHandle;
        public string selectedPlacementToken = string.Empty;
        public bool narrowDetailOpen;
        public MemoryLibraryUiEditDraft editDraft;
        public MemoryLibraryCommand pendingCommand;
        public string feedbackStatus = string.Empty;

        /// <summary>Clears every owner-bound selection/draft without touching repository state.</summary>
        public void ClearOwner()
        {
            selectedOwnerHandle = null;
            selectedOwnerEpochKey = null;
            selectedCompatibilityHandle = null;
            selectedOwnerDisplayName = string.Empty;
            selectedView = MemoryLibraryViews.Threads;
            ClearOwnerContent();
        }

        /// <summary>Selects one exact directory row; equal labels never participate in identity.</summary>
        public void SelectOwner(MemoryLibraryOwnerRow row)
        {
            if (row == null)
            {
                ClearOwner();
                return;
            }
            selectedOwnerHandle = MemoryLibraryUiPolicy.Copy(
                row.primaryHandle ?? row.compatibilityHandle);
            selectedOwnerEpochKey = MemoryLibraryUiPolicy.Copy(row.activeOwnerEpochKey);
            selectedCompatibilityHandle = MemoryLibraryUiPolicy.Copy(row.compatibilityHandle);
            selectedOwnerDisplayName = row.displayName ?? string.Empty;
            selectedView = !MemoryLibraryUiPolicy.HasActiveViews(row)
                && MemoryLibraryUiPolicy.HasImportedViewContent(row)
                ? MemoryLibraryViews.Imported : MemoryLibraryViews.Threads;
            ClearOwnerContent();
            if (selectedView == MemoryLibraryViews.Imported)
                MemoryLibraryUiPolicy.ClearImportedIncompatibleFilters(filters);
        }

        /// <summary>
        /// Reconciles the canonical first owner window. Search-empty and noncanonical pages preserve
        /// a valid exact selection rather than pretending the directory itself became empty.
        /// </summary>
        public void ReconcileOwnerDirectory(
            MemoryLibraryOwnerResult result,
            string preferredExactOwnerId,
            bool canonicalFirstWindow)
        {
            if (result == null || result.status != MemoryLibraryStatuses.Ready) return;
            if (result.directoryRowCount == 0)
            {
                ClearOwner();
                return;
            }
            if (result.rows == null || result.rows.Count == 0 || !canonicalFirstWindow) return;

            MemoryLibraryOwnerRow selected = MemoryLibraryUiPolicy.ResolveOwnerRow(
                null, result.rows, selectedOwnerHandle);
            if (selected == null && !string.IsNullOrEmpty(preferredExactOwnerId))
            {
                for (int index = 0; index < result.rows.Count; index++)
                {
                    MemoryLibraryOwnerRow candidate = result.rows[index];
                    string ownerId = candidate?.primaryHandle?.exactOwnerPawnIdOrEmpty
                        ?? candidate?.compatibilityHandle?.exactOwnerPawnIdOrEmpty;
                    if (string.Equals(ownerId, preferredExactOwnerId, StringComparison.Ordinal))
                    {
                        selected = candidate;
                        break;
                    }
                }
            }
            // Owner-directory paging changes only the selector window. It must not silently replace a
            // still-valid exact selection merely because that row is outside this materialized page.
            if (selected == null && selectedOwnerHandle != null) return;
            selected = selected ?? result.rows[0];
            if (MemoryLibraryUiPolicy.Same(selected?.primaryHandle, selectedOwnerHandle)
                || MemoryLibraryUiPolicy.Same(selected?.compatibilityHandle, selectedOwnerHandle))
            {
                // Directory/status refreshes may replace detached row instances, but must not erase
                // an edit draft or detail selection for the same exact owner.
                selectedOwnerHandle = MemoryLibraryUiPolicy.Copy(
                    selected.primaryHandle ?? selected.compatibilityHandle);
                selectedOwnerEpochKey = MemoryLibraryUiPolicy.Copy(selected.activeOwnerEpochKey);
                selectedCompatibilityHandle = MemoryLibraryUiPolicy.Copy(selected.compatibilityHandle);
                selectedOwnerDisplayName = selected.displayName ?? string.Empty;
                if (selectedView == MemoryLibraryViews.Imported
                    && !MemoryLibraryUiPolicy.HasImportedViewContent(selected))
                    SelectView(MemoryLibraryViews.Threads);
                else if (!MemoryLibraryUiPolicy.HasActiveViews(selected)
                    && selectedView != MemoryLibraryViews.Imported
                    && MemoryLibraryUiPolicy.HasImportedViewContent(selected))
                    SelectView(MemoryLibraryViews.Imported);
                return;
            }
            SelectOwner(selected);
        }

        /// <summary>
        /// Stages one detached explicit command. The dialog drains this only from WindowUpdate, so
        /// Layout/Repaint and repeated click-event drawing cannot reach component mutation APIs.
        /// </summary>
        public bool StageCommand(MemoryLibraryCommand command)
        {
            if (command == null || pendingCommand != null) return false;
            pendingCommand = command;
            return true;
        }

        /// <summary>Transfers staged work to the non-drawing update adapter exactly once.</summary>
        public MemoryLibraryCommand TakeStagedCommand()
        {
            MemoryLibraryCommand command = pendingCommand;
            pendingCommand = null;
            return command;
        }

        /// <summary>Changes primary view and clears only view-owned selection state.</summary>
        public void SelectView(string view)
        {
            if (view != MemoryLibraryViews.Threads
                && view != MemoryLibraryViews.Standalone
                && view != MemoryLibraryViews.Imported) return;
            selectedView = view;
            selectedRootHandle = null;
            selectedRecordHandle = null;
            selectedArchiveHandle = null;
            selectedPlacementToken = string.Empty;
            narrowDetailOpen = false;
            editDraft = null;
            pendingCommand = null;
            feedbackStatus = string.Empty;
            if (view == MemoryLibraryViews.Imported)
                MemoryLibraryUiPolicy.ClearImportedIncompatibleFilters(filters);
        }

        private void ClearOwnerContent()
        {
            selectedRootHandle = null;
            selectedRecordHandle = null;
            selectedArchiveHandle = null;
            selectedPlacementToken = string.Empty;
            narrowDetailOpen = false;
            editDraft = null;
            feedbackStatus = string.Empty;
        }

    }

    /// <summary>Pure Phase-M9 presentation/session rules shared by the dialog and fixtures.</summary>
    internal static class MemoryLibraryUiPolicy
    {
        /// <summary>
        /// Returns the exact saved identity used by transient card-format caches. Repository queries
        /// may re-project equivalent row objects under the same publication revision, so reference
        /// identity is never a valid cache key.
        /// </summary>
        public static string ListRowCacheKey(MemoryLibraryListRow row)
        {
            MemoryRootHandle root = row?.thread?.rootHandle;
            if (root != null)
                return "thread\n" + (root.ownerPawnId ?? string.Empty) + "\n"
                    + (root.epochToken ?? string.Empty) + "\n" + (root.rootId ?? string.Empty);
            MemoryRecordHandle record = row?.standalone?.recordHandle;
            if (record != null)
                return "standalone\n" + (record.ownerPawnId ?? string.Empty) + "\n"
                    + (record.epochToken ?? string.Empty) + "\n"
                    + (record.recordId ?? string.Empty);
            MemoryArchiveHandle archive = row?.imported?.archiveHandle;
            if (archive != null)
                return "imported\n" + (archive.archiveScopeToken ?? string.Empty) + "\n"
                    + (archive.exactOwnerPawnIdOrEmpty ?? string.Empty) + "\n"
                    + (archive.archiveRecordId ?? string.Empty);
            return string.Empty;
        }

        public static bool CanOpen(
            bool currentRelease,
            bool programPlaying,
            bool hasGame,
            bool hasComponent)
        {
            return currentRelease && programPlaying && hasGame && hasComponent;
        }

        /// <summary>Fails closed for inactive releases and future-version read-only payloads.</summary>
        public static bool CanMutate(bool currentRelease, bool compatibilityFailClosed)
        {
            return currentRelease && !compatibilityFailClosed;
        }

        public static bool HasImportedViewContent(MemoryLibraryOwnerRow owner)
        {
            return owner != null && (owner.importedCount > 0 || owner.compatibilityHandle != null);
        }

        /// <summary>
        /// Threads and Standalone require an active primary handle. A zero-memory active owner has
        /// no epoch yet by design, but still exposes both typed empty views without creating one.
        /// </summary>
        public static bool HasActiveViews(MemoryLibraryOwnerRow owner)
        {
            return owner?.primaryHandle != null
                && owner.primaryHandle.scopeToken == MemoryLibraryScopes.Active;
        }

        /// <summary>Resolves the honest empty-state token before the UI localizes it.</summary>
        public static string ListEmptyState(
            string backendToken,
            string search,
            MemoryLibraryFilters filters)
        {
            if (!string.IsNullOrWhiteSpace(search)) return "no_matches";
            if ((filters?.importanceMask ?? 0) != 0
                || (filters?.categoryMask ?? 0) != 0
                || (filters?.stateToken ?? "all") != "all")
                return "no_filter_matches";
            return backendToken ?? string.Empty;
        }

        /// <summary>
        /// Missing current evidence is represented by the DTO sentinel tick -1. Tick zero remains
        /// a valid captured game instant and must not be mistaken for missing evidence.
        /// </summary>
        public static bool HasCapturedCurrentStatus(MemoryCurrentStatusDto current)
        {
            return current != null && current.capturedTick >= 0;
        }

        /// <summary>
        /// Uses the freshest proven enclosing revision for a selected block without mutating the
        /// immutable row that supplied its exact identity. The detail query still proves membership.
        /// </summary>
        public static long BlockDetailStructuralRevision(
            MemoryBlockRow selected,
            MemoryThreadHeaderRow currentThreadHeader,
            long currentOwnerStructuralRevision)
        {
            if (selected == null) return 0;
            if (selected.rootHandle == null)
                return currentOwnerStructuralRevision > 0
                    ? currentOwnerStructuralRevision : selected.targetStructuralRevision;
            if (currentThreadHeader?.structuralRevision > 0
                && SameRoot(selected.rootHandle, currentThreadHeader.rootHandle))
                return currentThreadHeader.structuralRevision;
            return selected.targetStructuralRevision;
        }

        /// <summary>Uses the freshest proven Imported owner fence without rewriting any DTO.</summary>
        public static long ImportedDetailStructuralRevision(
            MemoryImportedRow selected,
            MemoryLibraryOwnerRow currentOwner,
            long currentListOwnerStructuralRevision)
        {
            if (selected == null) return 0;
            long rowRevision = Math.Max(0, selected.targetStructuralRevision);
            long ownerRevision = Math.Max(0, currentOwner?.structuralRevision ?? 0);
            long listRevision = Math.Max(0, currentListOwnerStructuralRevision);
            return Math.Max(rowRevision, Math.Max(ownerRevision, listRevision));
        }

        /// <summary>Rejects a menu option captured from any superseded owner directory.</summary>
        public static bool CanApplyOwnerMenuOption(
            long capturedDirectoryRevision,
            MemoryLibraryOwnerResult currentDirectory)
        {
            return capturedDirectoryRevision > 0
                && currentDirectory?.status == MemoryLibraryStatuses.Ready
                && currentDirectory.directoryRevision == capturedDirectoryRevision;
        }

        /// <summary>
        /// Plans one page of an exact-handle or preferred-ID owner walk. The canonical fallback is
        /// retained across pages and selected only after the pinned directory is exhausted.
        /// </summary>
        public static MemoryLibraryUiOwnerWalkStep PlanOwnerWalk(
            List<MemoryLibraryOwnerRow> rows,
            bool hasMore,
            MemoryLibraryOwnerRow fallback,
            MemoryLibraryOwnerHandle exactHandle,
            string preferredExactOwnerId)
        {
            MemoryLibraryUiOwnerWalkStep result = new MemoryLibraryUiOwnerWalkStep
            {
                fallback = fallback
            };
            if (result.fallback == null && rows != null && rows.Count > 0)
                result.fallback = rows[0];
            for (int index = 0; rows != null && index < rows.Count; index++)
            {
                MemoryLibraryOwnerRow candidate = rows[index];
                bool handleMatch = exactHandle != null
                    && (Same(candidate?.primaryHandle, exactHandle)
                        || Same(candidate?.compatibilityHandle, exactHandle));
                string ownerId = candidate?.primaryHandle?.exactOwnerPawnIdOrEmpty
                    ?? candidate?.compatibilityHandle?.exactOwnerPawnIdOrEmpty;
                bool idMatch = exactHandle == null
                    && !string.IsNullOrWhiteSpace(preferredExactOwnerId)
                    && string.Equals(ownerId, preferredExactOwnerId, StringComparison.Ordinal);
                if (handleMatch || idMatch)
                {
                    result.selected = candidate;
                    return result;
                }
            }
            if (hasMore) result.continuePaging = true;
            else result.selected = result.fallback;
            return result;
        }

        /// <summary>
        /// Retains a row already resolved by a complete owner walk, otherwise finds the exact handle
        /// on the current materialized page. Labels and owner IDs alone never participate.
        /// </summary>
        public static MemoryLibraryOwnerRow ResolveOwnerRow(
            MemoryLibraryOwnerRow resolved,
            List<MemoryLibraryOwnerRow> currentRows,
            MemoryLibraryOwnerHandle selectedHandle)
        {
            if (selectedHandle == null) return null;
            if (Same(resolved?.primaryHandle, selectedHandle)
                || Same(resolved?.compatibilityHandle, selectedHandle)) return resolved;
            for (int index = 0; currentRows != null && index < currentRows.Count; index++)
            {
                MemoryLibraryOwnerRow row = currentRows[index];
                if (Same(row?.primaryHandle, selectedHandle)
                    || Same(row?.compatibilityHandle, selectedHandle)) return row;
            }
            return null;
        }

        public static void ClearImportedIncompatibleFilters(MemoryLibraryFilters filters)
        {
            if (filters == null) return;
            filters.importanceMask = 0;
            filters.categoryMask = 0;
            filters.stateToken = "all";
        }

        /// <summary>Returns an overscanned but hard-bounded fixed-height row range.</summary>
        public static MemoryLibraryUiVirtualWindow Virtualize(
            int totalRows,
            float scrollY,
            float viewportHeight,
            float rowHeight,
            int overscanRows,
            int maximumMaterializedRows)
        {
            MemoryLibraryUiVirtualWindow result = new MemoryLibraryUiVirtualWindow();
            int total = Math.Max(0, totalRows);
            float height = rowHeight > 0f ? rowHeight : 1f;
            int overscan = Math.Max(0, overscanRows);
            int maximum = Math.Max(1, maximumMaterializedRows);
            result.contentHeight = total * height;
            if (total == 0) return result;

            double safeScroll = Math.Max(0d, scrollY);
            double safeViewport = Math.Max(1d, viewportHeight);
            int first = Math.Min(total - 1,
                Math.Max(0, (int)Math.Floor(safeScroll / height) - overscan));
            int visibleEnd = Math.Min(total,
                (int)Math.Ceiling((safeScroll + safeViewport) / height) + overscan);
            if (visibleEnd <= first) visibleEnd = Math.Min(total, first + 1);
            if (visibleEnd - first > maximum) visibleEnd = first + maximum;
            result.firstIndex = first;
            result.endExclusive = visibleEnd;
            result.materializedCount = visibleEnd - first;
            return result;
        }

        /// <summary>Starts one bounded detached draft from a selected stable block detail.</summary>
        public static MemoryLibraryUiEditDraft BeginEdit(
            MemoryBlockDetailResult detail,
            int textCap)
        {
            if (detail?.status != MemoryLibraryStatuses.Ready || detail.row == null
                || !detail.row.canSaveWording || detail.targetStructuralRevision <= 0)
                return null;
            string text = detail.detail?.playerWording;
            if (string.IsNullOrEmpty(text)) text = detail.row.displayWording;
            return new MemoryLibraryUiEditDraft
            {
                recordHandle = Copy(detail.row.recordHandle),
                rootHandle = Copy(detail.row.rootHandle),
                placementToken = detail.row.rootHandle == null ? "standalone" : string.Empty,
                targetStructuralRevision = detail.targetStructuralRevision,
                latestStatusRevision = detail.targetStatusRevision,
                text = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                    text ?? string.Empty, Math.Max(1, textCap))
            };
        }

        /// <summary>
        /// Merges one refreshed selected detail into an open draft. Text is never replaced. A changed
        /// structural fence becomes an explicit retry conflict; a status-only change is transparent.
        /// </summary>
        public static void MergeDetailRefresh(
            MemoryLibraryUiEditDraft draft,
            MemoryBlockDetailResult detail)
        {
            if (draft == null || detail?.status != MemoryLibraryStatuses.Ready
                || detail.row == null
                || !Same(draft.recordHandle, detail.row.recordHandle)) return;
            if (draft.targetStructuralRevision != detail.targetStructuralRevision)
            {
                draft.structuralConflict = true;
                // Preserve the exact fence captured when editing began. Saving this retained draft
                // must resolve Stale instead of silently blessing older prose against a newer root.
            }
            draft.latestStatusRevision = detail.targetStatusRevision;
        }

        /// <summary>Retains drafts for every typed conflict/refusal; only Save success consumes one.</summary>
        public static bool ApplyEditCommandResult(
            MemoryLibraryUiEditDraft draft,
            MemoryLibraryCommandResult result)
        {
            if (draft == null || result == null) return false;
            draft.terminalStatus = result.status ?? string.Empty;
            if (result.status == MemoryLibraryCommandStatuses.Stale)
                draft.structuralConflict = true;
            return result.status == MemoryLibraryCommandStatuses.Success;
        }

        /// <summary>Builds one exact active-block command from a detached actionable row/draft.</summary>
        public static MemoryLibraryCommand BuildBlockCommand(
            string clientToken,
            long commandId,
            string action,
            MemoryBlockRow row,
            bool desiredSuppressed,
            MemoryLibraryUiEditDraft draft)
        {
            if (string.IsNullOrWhiteSpace(clientToken) || commandId <= 0) return null;
            if (action == MemoryLibraryActions.SaveWording)
            {
                if (draft == null || draft.recordHandle == null) return null;
                return new MemoryLibraryCommand
                {
                    libraryClientToken = clientToken,
                    commandId = commandId,
                    actionToken = action,
                    recordHandle = Copy(draft.recordHandle),
                    rootHandle = Copy(draft.rootHandle),
                    placementToken = draft.placementToken ?? string.Empty,
                    targetStructuralRevision = draft.targetStructuralRevision,
                    wordingDraft = draft.text ?? string.Empty
                };
            }
            if (row?.recordHandle == null) return null;
            return new MemoryLibraryCommand
            {
                libraryClientToken = clientToken,
                commandId = commandId,
                actionToken = action ?? string.Empty,
                recordHandle = Copy(row.recordHandle),
                rootHandle = Copy(row.rootHandle),
                placementToken = row.rootHandle == null ? "standalone" : string.Empty,
                targetStructuralRevision = row.targetStructuralRevision,
                hasDesiredSuppressed = action == MemoryLibraryActions.SetSuppressed,
                desiredSuppressed = desiredSuppressed
            };
        }

        /// <summary>Builds one exact Imported Dev-Forget command; archive text revisions are excluded.</summary>
        public static MemoryLibraryCommand BuildImportedForgetCommand(
            string clientToken,
            long commandId,
            MemoryImportedRow row)
        {
            if (string.IsNullOrWhiteSpace(clientToken) || commandId <= 0
                || row?.archiveHandle == null) return null;
            return new MemoryLibraryCommand
            {
                libraryClientToken = clientToken,
                commandId = commandId,
                actionToken = MemoryLibraryActions.ForgetPermanent,
                archiveHandle = Copy(row.archiveHandle),
                targetStructuralRevision = row.targetStructuralRevision
            };
        }

        /// <summary>Classifies exact TTL state from original ticks; prompting never refreshes age.</summary>
        public static MemoryLibraryUiLifetime Lifetime(
            MemoryBlockRow row,
            long nowTick,
            long minorLifetimeTicks,
            long regularLifetimeTicks)
        {
            MemoryLibraryUiLifetime result = new MemoryLibraryUiLifetime();
            if (row == null) return result;
            result.importanceMask = row.projectedHighestImportanceMask;
            if (row.playerEdited)
            {
                result.stateToken = MemoryLibraryUiLifetimeTokens.Protected;
                return result;
            }
            if (row.summaryContributions != null && row.summaryContributions.Count > 0)
                return SummaryLifetime(row, nowTick, minorLifetimeTicks, regularLifetimeTicks);
            return ScalarLifetime(
                row.originalTick,
                row.ageUnknown,
                row.projectedHighestImportanceMask,
                nowTick,
                minorLifetimeTicks,
                regularLifetimeTicks);
        }

        /// <summary>True when removing edit protection would expose any constituent to due cleanup.</summary>
        public static bool PastNormalLifetime(
            MemoryBlockRow row,
            long nowTick,
            long minorLifetimeTicks,
            long regularLifetimeTicks)
        {
            if (row == null) return false;
            if (row.summaryContributions != null && row.summaryContributions.Count > 0)
            {
                for (int index = 0; index < row.summaryContributions.Count; index++)
                {
                    MemorySummaryContributionDescriptor contribution = row.summaryContributions[index];
                    if (contribution != null && IsDue(
                            contribution.originalTick,
                            contribution.ageUnknown,
                            contribution.importanceMask,
                            nowTick,
                            minorLifetimeTicks,
                            regularLifetimeTicks)) return true;
                }
                return false;
            }
            return IsDue(row.originalTick, row.ageUnknown, row.projectedHighestImportanceMask,
                nowTick, minorLifetimeTicks, regularLifetimeTicks);
        }

        private static MemoryLibraryUiLifetime SummaryLifetime(
            MemoryBlockRow row,
            long nowTick,
            long minorLifetimeTicks,
            long regularLifetimeTicks)
        {
            List<string> states = new List<string>();
            long nextExpiry = long.MaxValue;
            bool due = false;
            int highest = 0;
            for (int index = 0; index < row.summaryContributions.Count; index++)
            {
                MemorySummaryContributionDescriptor contribution = row.summaryContributions[index];
                if (contribution == null) continue;
                MemoryLibraryUiLifetime item = ScalarLifetime(
                    contribution.originalTick,
                    contribution.ageUnknown,
                    contribution.importanceMask,
                    nowTick,
                    minorLifetimeTicks,
                    regularLifetimeTicks);
                string family = LifetimeFamily(item.stateToken, contribution.importanceMask);
                if (!states.Contains(family)) states.Add(family);
                if (item.stateToken == MemoryLibraryUiLifetimeTokens.Due) due = true;
                if (item.expiryTick < nextExpiry && item.expiryTick > nowTick)
                    nextExpiry = item.expiryTick;
                highest = HigherImportance(highest, contribution.importanceMask);
            }
            if (states.Count == 0)
                return ScalarLifetime(row.originalTick, row.ageUnknown,
                    row.projectedHighestImportanceMask, nowTick,
                    minorLifetimeTicks, regularLifetimeTicks);
            if (states.Count == 1 && !due)
            {
                MemorySummaryContributionDescriptor first = null;
                for (int index = 0; index < row.summaryContributions.Count; index++)
                {
                    if (row.summaryContributions[index] != null)
                    {
                        first = row.summaryContributions[index];
                        break;
                    }
                }
                if (first == null)
                    return ScalarLifetime(row.originalTick, row.ageUnknown,
                        row.projectedHighestImportanceMask, nowTick,
                        minorLifetimeTicks, regularLifetimeTicks);
                MemoryLibraryUiLifetime single = ScalarLifetime(
                    first.originalTick, first.ageUnknown, first.importanceMask,
                    nowTick, minorLifetimeTicks, regularLifetimeTicks);
                single.importanceMask = highest;
                // Contributions with one lifetime family still have distinct original ticks. Report
                // the earliest constituent deadline, never whichever fact bucket happened to sort first.
                if (nextExpiry != long.MaxValue)
                {
                    single.expiryTick = nextExpiry;
                    single.remainingTicks = Math.Max(0, nextExpiry - nowTick);
                }
                return single;
            }
            return new MemoryLibraryUiLifetime
            {
                stateToken = states.Count > 1
                    ? MemoryLibraryUiLifetimeTokens.Mixed
                    : MemoryLibraryUiLifetimeTokens.Due,
                importanceMask = highest,
                expiryTick = nextExpiry,
                remainingTicks = nextExpiry == long.MaxValue ? 0 : Math.Max(0, nextExpiry - nowTick),
                containsDueContribution = due
            };
        }

        private static MemoryLibraryUiLifetime ScalarLifetime(
            long originalTick,
            bool ageUnknown,
            int importanceMask,
            long nowTick,
            long minorLifetimeTicks,
            long regularLifetimeTicks)
        {
            MemoryLibraryUiLifetime result = new MemoryLibraryUiLifetime
            {
                importanceMask = importanceMask
            };
            if (ageUnknown || originalTick < 0)
            {
                result.stateToken = MemoryLibraryUiLifetimeTokens.Unknown;
                return result;
            }
            if ((importanceMask & MemoryLibraryPolicy.ImportanceImportant) != 0)
            {
                result.stateToken = MemoryLibraryUiLifetimeTokens.Important;
                return result;
            }
            long lifetime = (importanceMask & MemoryLibraryPolicy.ImportanceRegular) != 0
                ? regularLifetimeTicks : minorLifetimeTicks;
            if (lifetime <= 0 || originalTick > long.MaxValue - lifetime)
            {
                result.stateToken = MemoryLibraryUiLifetimeTokens.Unknown;
                return result;
            }
            result.expiryTick = originalTick + lifetime;
            if (nowTick >= result.expiryTick)
            {
                result.stateToken = MemoryLibraryUiLifetimeTokens.Due;
                result.containsDueContribution = true;
                return result;
            }
            result.stateToken = (importanceMask & MemoryLibraryPolicy.ImportanceRegular) != 0
                ? MemoryLibraryUiLifetimeTokens.Regular
                : MemoryLibraryUiLifetimeTokens.Minor;
            result.remainingTicks = result.expiryTick - nowTick;
            return result;
        }

        private static bool IsDue(
            long originalTick,
            bool ageUnknown,
            int importanceMask,
            long nowTick,
            long minorLifetimeTicks,
            long regularLifetimeTicks)
        {
            if (ageUnknown || originalTick < 0
                || (importanceMask & MemoryLibraryPolicy.ImportanceImportant) != 0) return false;
            long lifetime = (importanceMask & MemoryLibraryPolicy.ImportanceRegular) != 0
                ? regularLifetimeTicks : minorLifetimeTicks;
            return lifetime > 0 && originalTick <= long.MaxValue - lifetime
                && nowTick >= originalTick + lifetime;
        }

        private static string LifetimeFamily(string state, int importanceMask)
        {
            if (state == MemoryLibraryUiLifetimeTokens.Due)
                return (importanceMask & MemoryLibraryPolicy.ImportanceRegular) != 0
                    ? "regular_due" : "minor_due";
            return state ?? string.Empty;
        }

        private static int HigherImportance(int left, int right)
        {
            if ((left & MemoryLibraryPolicy.ImportanceImportant) != 0
                || (right & MemoryLibraryPolicy.ImportanceImportant) != 0)
                return MemoryLibraryPolicy.ImportanceImportant;
            if ((left & MemoryLibraryPolicy.ImportanceRegular) != 0
                || (right & MemoryLibraryPolicy.ImportanceRegular) != 0)
                return MemoryLibraryPolicy.ImportanceRegular;
            return (left & MemoryLibraryPolicy.ImportanceMinor) != 0
                || (right & MemoryLibraryPolicy.ImportanceMinor) != 0
                ? MemoryLibraryPolicy.ImportanceMinor : 0;
        }

        public static bool Same(MemoryLibraryOwnerHandle left, MemoryLibraryOwnerHandle right)
        {
            return left != null && right != null
                && string.Equals(left.scopeToken, right.scopeToken, StringComparison.Ordinal)
                && string.Equals(left.exactOwnerPawnIdOrEmpty,
                    right.exactOwnerPawnIdOrEmpty, StringComparison.Ordinal)
                && string.Equals(left.epochTokenOrEmpty,
                    right.epochTokenOrEmpty, StringComparison.Ordinal);
        }

        public static bool Same(MemoryRecordHandle left, MemoryRecordHandle right)
        {
            return left != null && right != null
                && string.Equals(left.ownerPawnId, right.ownerPawnId, StringComparison.Ordinal)
                && string.Equals(left.epochToken, right.epochToken, StringComparison.Ordinal)
                && string.Equals(left.recordId, right.recordId, StringComparison.Ordinal);
        }

        private static bool SameRoot(MemoryRootHandle left, MemoryRootHandle right)
        {
            return left != null && right != null
                && string.Equals(left.ownerPawnId, right.ownerPawnId, StringComparison.Ordinal)
                && string.Equals(left.epochToken, right.epochToken, StringComparison.Ordinal)
                && string.Equals(left.rootId, right.rootId, StringComparison.Ordinal);
        }

        public static MemoryLibraryOwnerHandle Copy(MemoryLibraryOwnerHandle source)
        {
            return source == null ? null : new MemoryLibraryOwnerHandle(
                source.scopeToken, source.exactOwnerPawnIdOrEmpty, source.epochTokenOrEmpty);
        }

        public static MemoryOwnerEpochKey Copy(MemoryOwnerEpochKey source)
        {
            return source == null ? null : new MemoryOwnerEpochKey
            {
                ownerPawnId = source.ownerPawnId ?? string.Empty,
                epochToken = source.epochToken ?? string.Empty
            };
        }

        public static MemoryRootHandle Copy(MemoryRootHandle source)
        {
            return source == null ? null : new MemoryRootHandle
            {
                ownerPawnId = source.ownerPawnId ?? string.Empty,
                epochToken = source.epochToken ?? string.Empty,
                rootId = source.rootId ?? string.Empty
            };
        }

        public static MemoryRecordHandle Copy(MemoryRecordHandle source)
        {
            return source == null ? null : new MemoryRecordHandle
            {
                ownerPawnId = source.ownerPawnId ?? string.Empty,
                epochToken = source.epochToken ?? string.Empty,
                recordId = source.recordId ?? string.Empty
            };
        }

        public static MemoryArchiveHandle Copy(MemoryArchiveHandle source)
        {
            return source == null ? null : new MemoryArchiveHandle
            {
                archiveScopeToken = source.archiveScopeToken ?? string.Empty,
                exactOwnerPawnIdOrEmpty = source.exactOwnerPawnIdOrEmpty ?? string.Empty,
                archiveRecordId = source.archiveRecordId ?? string.Empty
            };
        }
    }
}

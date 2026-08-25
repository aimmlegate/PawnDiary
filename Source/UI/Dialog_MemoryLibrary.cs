// Dialog_MemoryLibrary.cs — Phase-M9 detached query/update adapter for the unified Memory Library.
//
// The component is touched only from WindowUpdate or PostClose. Immediate-mode drawing lives in the
// companion Layout file and may change only detached session buffers or stage an explicit command.
// This separation prevents Layout/Repaint from cleaning, compacting, mutating, or publishing saved
// memory revisions. New to RimWorld UI? See docs/lore/ui.md.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>Singleton, non-pausing Library for exact memory owners and their detached rows.</summary>
    internal sealed partial class Dialog_MemoryLibrary : Window
    {
        private const int PageSize = 64;
        private const int ImportedTextPageSize = 480;
        private static long lifecycleGeneration = 1;

        private readonly DiaryGameComponent component;
        private readonly string clientToken = Guid.NewGuid().ToString("N");
        private readonly long openedGeneration;
        private readonly MemoryLibraryUiSession session = new MemoryLibraryUiSession();

        private string preferredOwnerId = string.Empty;
        private MemoryLibraryOwnerRow preferredFallback;
        private int ownerStart;
        private long ownerExpectedDirectoryRevision;
        private MemoryLibraryOwnerResult owners;
        private MemoryLibraryOwnerRow selectedOwner;

        private int listStart;
        private long listExpectedSnapshotRevision;
        private MemoryLibraryListResult list;
        private int detailStart;
        private long detailExpectedSnapshotRevision;
        private MemoryThreadDetailResult threadDetail;
        private MemoryBlockDetailResult blockDetail;
        private int importedTextStart;
        private long importedTextExpectedSnapshotRevision;
        private MemoryImportedDetailResult importedDetail;
        private MemoryCompatibilityResult compatibility;
        private LoreMemorySnapshotForDev lore;

        private long nextCommandId = 1;
        private long pendingCommandId;
        private string pendingAction = string.Empty;
        private long detachedNowTick;
        private long detachedMinorLifetimeTicks = 15L * 60000L;
        private long detachedRegularLifetimeTicks = 60L * 60000L;
        private int detachedCategoryMask = MemoryCategoryBits.KnownMask;
        private int detachedTextCap = 480;
        private bool detachedCompatibilityFailClosed;
        private bool loreExpanded;
        private bool diagnosticsExpanded;
        private bool ownerSearchDirty = true;
        private bool listQueryDirty = true;
        private bool detailQueryDirty = true;

        private Vector2 listScroll;
        private Vector2 detailScroll;
        private Vector2 loreScroll;
        private readonly Dictionary<string, string> cachedBlockBadges =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> cachedLifetimeLabels =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<long, string> cachedDateLabels =
            new Dictionary<long, string>();
        private string displayCacheSignature = string.Empty;
        private int selectedLoreTopicIndex;

        private Dialog_MemoryLibrary(DiaryGameComponent source, string preferredExactOwnerId)
        {
            component = source;
            preferredOwnerId = preferredExactOwnerId ?? string.Empty;
            openedGeneration = lifecycleGeneration;
            forcePause = false;
            draggable = true;
            resizeable = false;
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            onlyOneOfTypeAllowed = true;
        }

        public override Vector2 InitialSize
        {
            get
            {
                DiaryUiStyleDef style = DiaryJournalView.UiStyle;
                float margin = Mathf.Max(0f, style.memoryLibraryScreenMargin);
                float availableWidth = Mathf.Max(360f, UI.screenWidth - margin);
                float availableHeight = Mathf.Max(360f, UI.screenHeight - margin);
                float preferredWidth = Mathf.Max(
                    style.memoryLibraryMinWidth, style.memoryLibraryWidth);
                float preferredHeight = Mathf.Max(
                    style.memoryLibraryMinHeight, style.memoryLibraryHeight);
                return new Vector2(
                    Mathf.Min(preferredWidth, availableWidth),
                    Mathf.Min(preferredHeight, availableHeight));
            }
        }

        /// <summary>M8 settings callback. It remains null unless FinalizeInit installs it in CurrentRelease.</summary>
        internal static void Open()
        {
            OpenForOwner(string.Empty);
        }

        /// <summary>Opens or focuses the singleton Library on one exact diary owner ID.</summary>
        internal static void OpenForOwner(string ownerPawnId)
        {
            DiaryGameComponent source = DiaryGameComponent.Instance;
            if (!MemoryLibraryUiPolicy.CanOpen(
                    MemorySystemActivationGate.IsCurrentRelease,
                    Current.ProgramState == ProgramState.Playing,
                    Current.Game != null,
                    source != null))
            {
                Messages.Message(
                    "PawnDiary.Memory.Library.Unavailable".Translate(),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }
            if (Find.WindowStack == null) return;

            Dialog_MemoryLibrary existing = Find.WindowStack.Windows
                .OfType<Dialog_MemoryLibrary>().FirstOrDefault();
            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(ownerPawnId))
                    existing.PreferOwner(ownerPawnId);
                Find.WindowStack.Notify_ManuallySetFocus(existing);
                return;
            }
            Find.WindowStack.Add(new Dialog_MemoryLibrary(source, ownerPawnId));
        }

        /// <summary>Clears static UI state and closes stale windows across game transitions.</summary>
        internal static void ResetForGameTransition()
        {
            if (lifecycleGeneration < long.MaxValue) lifecycleGeneration++;
            Find.WindowStack?.TryRemove(typeof(Dialog_MemoryLibrary), true);
        }

        private void PreferOwner(string ownerPawnId)
        {
            preferredOwnerId = ownerPawnId ?? string.Empty;
            preferredFallback = null;
            ownerStart = 0;
            ownerExpectedDirectoryRevision = 0;
            session.ownerSearch = string.Empty;
            ownerSearchDirty = true;
        }

        /// <summary>Performs all repository reads, command enqueue/drain, and dev lore projection.</summary>
        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (openedGeneration != lifecycleGeneration
                || component == null || component != DiaryGameComponent.Instance
                || !MemorySystemActivationGate.IsCurrentRelease
                || Current.ProgramState != ProgramState.Playing || Current.Game == null)
            {
                Close(false);
                return;
            }

            detachedNowTick = Math.Max(0, Find.TickManager?.TicksGame ?? 0);
            MemoryPolicySnapshot policy = MemoryEffectivePolicyProvider.Current;
            if (policy != null)
            {
                detachedMinorLifetimeTicks = policy.minorMemoryLifetimeTicks;
                detachedRegularLifetimeTicks = policy.regularMemoryLifetimeTicks;
                detachedCategoryMask = policy.memoryCategoryMask;
                detachedCompatibilityFailClosed = policy.compatibilityFailClosed;
            }
            detachedTextCap = Math.Max(1,
                DiaryKnowledgePolicy.Snapshot(false).fallbackSummaryMaxChars);
            DrainStagedCommand();
            PollCommandResult();
            RefreshOwners();
            RefreshSelectedOwnerReference();
            RefreshList();
            RefreshSelectedDetail();
            RefreshCompatibility();
            RefreshLoreDiagnostics();
            RefreshDisplayCaches();
        }

        public override void PostClose()
        {
            component?.AbandonMemoryLibraryClient(clientToken);
            session.ClearOwner();
            owners = null;
            list = null;
            threadDetail = null;
            blockDetail = null;
            importedDetail = null;
            compatibility = null;
            lore = null;
            cachedBlockBadges.Clear();
            cachedLifetimeLabels.Clear();
            cachedDateLabels.Clear();
            displayCacheSignature = string.Empty;
            base.PostClose();
        }

        private void RefreshOwners()
        {
            if (ownerSearchDirty)
            {
                ownerStart = 0;
                ownerExpectedDirectoryRevision = 0;
                preferredFallback = null;
                ownerSearchDirty = false;
            }
            MemoryLibraryOwnerResult result = component.QueryMemoryLibraryOwners(
                new MemoryLibraryOwnerQuery
                {
                    search = session.ownerSearch ?? string.Empty,
                    sortToken = "name",
                    start = ownerStart,
                    count = PageSize,
                    expectedDirectoryRevision = ownerExpectedDirectoryRevision
                });
            owners = result;
            if (result == null) return;
            if (result.status == MemoryLibraryStatuses.Stale)
            {
                ownerExpectedDirectoryRevision = 0;
                ownerStart = 0;
                return;
            }
            if (result.status != MemoryLibraryStatuses.Ready) return;
            ownerExpectedDirectoryRevision = result.directoryRevision;

            string before = OwnerKey(session.selectedOwnerHandle);
            bool canonical = ownerStart == 0 && string.IsNullOrWhiteSpace(session.ownerSearch);
            if (!string.IsNullOrWhiteSpace(preferredOwnerId) && canonical)
            {
                if (preferredFallback == null && result.rows.Count > 0) preferredFallback = result.rows[0];
                MemoryLibraryOwnerRow found = FindExactOwner(result.rows, preferredOwnerId);
                if (found != null)
                {
                    session.SelectOwner(found);
                    preferredOwnerId = string.Empty;
                }
                else if (result.hasMore)
                {
                    ownerStart = result.nextStart;
                    ownerExpectedDirectoryRevision = result.directoryRevision;
                    return;
                }
                else
                {
                    session.SelectOwner(preferredFallback);
                    preferredOwnerId = string.Empty;
                    ownerStart = 0;
                }
            }
            else
            {
                session.ReconcileOwnerDirectory(result, string.Empty, canonical);
            }
            if (!string.Equals(before, OwnerKey(session.selectedOwnerHandle), StringComparison.Ordinal))
                ResetOwnerQueries();
        }

        private void RefreshSelectedOwnerReference()
        {
            if (owners?.rows == null) return;
            for (int index = 0; index < owners.rows.Count; index++)
            {
                MemoryLibraryOwnerRow row = owners.rows[index];
                if (MemoryLibraryUiPolicy.Same(row?.primaryHandle, session.selectedOwnerHandle)
                    || MemoryLibraryUiPolicy.Same(row?.compatibilityHandle, session.selectedOwnerHandle)
                    || MemoryLibraryUiPolicy.Same(row?.primaryHandle, session.selectedCompatibilityHandle))
                {
                    selectedOwner = row;
                    return;
                }
            }
        }

        private void RefreshList()
        {
            if (session.selectedOwnerHandle == null || selectedOwner?.primaryHandle == null)
            {
                list = null;
                return;
            }
            if (listQueryDirty)
            {
                listStart = 0;
                listExpectedSnapshotRevision = 0;
                listQueryDirty = false;
            }
            if (list != null && list.ttlValidUntilTickExclusive <= detachedNowTick)
                listExpectedSnapshotRevision = 0;
            MemoryLibraryListResult result = component.QueryMemoryLibraryList(
                new MemoryLibraryListQuery
                {
                    primaryHandle = MemoryLibraryUiPolicy.Copy(selectedOwner.primaryHandle),
                    activeOwnerEpochKey = MemoryLibraryUiPolicy.Copy(selectedOwner.activeOwnerEpochKey),
                    viewTag = session.selectedView,
                    filters = CopyFilters(session.filters),
                    search = session.memorySearch ?? string.Empty,
                    sortToken = session.sortToken ?? "newest",
                    listStart = listStart,
                    listCount = PageSize,
                    expectedDirectoryRevision = Math.Max(0, ownerExpectedDirectoryRevision),
                    expectedListSnapshotRevision = Math.Max(0, listExpectedSnapshotRevision)
                });
            list = result;
            if (result == null) return;
            if (result.status == MemoryLibraryStatuses.Stale)
            {
                listExpectedSnapshotRevision = 0;
                return;
            }
            if (result.status == MemoryLibraryStatuses.Ready)
                listExpectedSnapshotRevision = result.listSnapshotRevision;
        }

        private void RefreshSelectedDetail()
        {
            if (detailQueryDirty)
            {
                detailStart = 0;
                detailExpectedSnapshotRevision = 0;
                importedTextStart = 0;
                importedTextExpectedSnapshotRevision = 0;
                threadDetail = null;
                blockDetail = null;
                importedDetail = null;
                detailQueryDirty = false;
            }
            if (session.selectedView == MemoryLibraryViews.Threads
                && session.selectedRootHandle != null)
            {
                if (threadDetail != null && threadDetail.ttlValidUntilTickExclusive <= detachedNowTick)
                    detailExpectedSnapshotRevision = 0;
                threadDetail = component.QueryMemoryThreadDetail(new MemoryThreadDetailQuery
                {
                    rootHandle = MemoryLibraryUiPolicy.Copy(session.selectedRootHandle),
                    filters = CopyFilters(session.filters),
                    search = session.memorySearch ?? string.Empty,
                    detailStart = detailStart,
                    detailCount = PageSize,
                    expectedDetailSnapshotRevision = Math.Max(0, detailExpectedSnapshotRevision)
                });
                if (threadDetail.status == MemoryLibraryStatuses.Stale)
                    detailExpectedSnapshotRevision = 0;
                else if (threadDetail.status == MemoryLibraryStatuses.Ready)
                    detailExpectedSnapshotRevision = threadDetail.detailSnapshotRevision;
            }

            if (session.selectedRecordHandle != null)
            {
                MemoryBlockRow selected = FindSelectedBlockRow();
                if (selected != null)
                {
                    MemoryBlockDetailResult refreshed = component.QueryMemoryBlockDetail(
                        new MemoryBlockDetailQuery
                        {
                            recordHandle = MemoryLibraryUiPolicy.Copy(selected.recordHandle),
                            rootHandle = MemoryLibraryUiPolicy.Copy(selected.rootHandle),
                            placementToken = selected.rootHandle == null ? "standalone" : string.Empty,
                            targetStructuralRevision = selected.targetStructuralRevision,
                            projectionToken = "full"
                        });
                    if (refreshed.status == MemoryLibraryStatuses.Stale)
                    {
                        listExpectedSnapshotRevision = 0;
                        detailExpectedSnapshotRevision = 0;
                    }
                    else if (refreshed.status == MemoryLibraryStatuses.Ready)
                    {
                        blockDetail = refreshed;
                        MemoryLibraryUiPolicy.MergeDetailRefresh(session.editDraft, refreshed);
                    }
                }
            }

            if (session.selectedView == MemoryLibraryViews.Imported
                && session.selectedArchiveHandle != null)
            {
                MemoryImportedRow row = FindSelectedImportedRow();
                if (row != null)
                {
                    importedDetail = component.QueryMemoryImportedDetail(
                        new MemoryImportedDetailQuery
                        {
                            archiveHandle = MemoryLibraryUiPolicy.Copy(row.archiveHandle),
                            textStart = importedTextStart,
                            textCount = ImportedTextPageSize,
                            expectedArchiveTextSnapshotRevision = importedTextExpectedSnapshotRevision,
                            targetStructuralRevision = row.targetStructuralRevision
                        });
                    if (importedDetail.status == MemoryLibraryStatuses.Stale)
                        importedTextExpectedSnapshotRevision = 0;
                    else if (importedDetail.status == MemoryLibraryStatuses.Ready)
                        importedTextExpectedSnapshotRevision = importedDetail.archiveTextSnapshotRevision;
                }
            }
        }

        private void RefreshCompatibility()
        {
            if (session.selectedCompatibilityHandle == null)
            {
                compatibility = null;
                return;
            }
            compatibility = component.QueryMemoryCompatibility(new MemoryCompatibilityQuery
            {
                compatibilityHandle = MemoryLibraryUiPolicy.Copy(session.selectedCompatibilityHandle),
                sourcePayloadRevision = selectedOwner?.compatibilitySourcePayloadRevision ?? 0
            });
        }

        private void RefreshLoreDiagnostics()
        {
            if (!Prefs.DevMode || !loreExpanded)
            {
                lore = null;
                return;
            }
            string ownerId = session.selectedOwnerEpochKey?.ownerPawnId
                ?? session.selectedOwnerHandle?.exactOwnerPawnIdOrEmpty;
            lore = component.LoreMemoryForDev(ownerId);
        }

        private void DrainStagedCommand()
        {
            if (pendingCommandId != 0) return;
            MemoryLibraryCommand command = session.TakeStagedCommand();
            if (command == null) return;
            if (!component.TryEnqueueMemoryLibraryCommand(command))
            {
                session.feedbackStatus = "QueueFull";
                return;
            }
            pendingCommandId = command.commandId;
            pendingAction = command.actionToken ?? string.Empty;
        }

        private void PollCommandResult()
        {
            if (pendingCommandId <= 0) return;
            if (!component.TryTakeMemoryLibraryCommandResult(
                    clientToken, pendingCommandId, out MemoryLibraryCommandResult result)) return;
            pendingCommandId = 0;
            session.feedbackStatus = result?.status ?? MemoryLibraryCommandStatuses.Invalid;
            bool saved = pendingAction == MemoryLibraryActions.SaveWording
                && MemoryLibraryUiPolicy.ApplyEditCommandResult(session.editDraft, result);
            if (saved) session.editDraft = null;
            pendingAction = string.Empty;
            listExpectedSnapshotRevision = 0;
            detailExpectedSnapshotRevision = 0;
            importedTextExpectedSnapshotRevision = 0;
        }

        private long AllocateCommandId()
        {
            if (!MutationsAvailable())
            {
                session.feedbackStatus = MemoryLibraryCommandStatuses.Unauthorized;
                return 0;
            }
            if (nextCommandId <= 0 || nextCommandId == long.MaxValue)
            {
                session.feedbackStatus = MemoryLibraryCommandStatuses.RevisionSaturated;
                return 0;
            }
            return nextCommandId++;
        }

        private bool MutationsAvailable()
        {
            return MemoryLibraryUiPolicy.CanMutate(
                MemorySystemActivationGate.IsCurrentRelease,
                detachedCompatibilityFailClosed);
        }

        private void ResetOwnerQueries()
        {
            selectedOwner = null;
            compatibility = null;
            lore = null;
            loreExpanded = false;
            selectedLoreTopicIndex = 0;
            ResetListQuery();
        }

        private void ResetListQuery()
        {
            listQueryDirty = true;
            listExpectedSnapshotRevision = 0;
            list = null;
            listScroll = Vector2.zero;
            ResetDetailQuery();
        }

        private void ResetDetailQuery()
        {
            detailQueryDirty = true;
            detailExpectedSnapshotRevision = 0;
            importedTextExpectedSnapshotRevision = 0;
            threadDetail = null;
            blockDetail = null;
            importedDetail = null;
            detailScroll = Vector2.zero;
        }

        private static MemoryLibraryOwnerRow FindExactOwner(
            List<MemoryLibraryOwnerRow> rows, string ownerId)
        {
            if (rows == null) return null;
            for (int index = 0; index < rows.Count; index++)
            {
                MemoryLibraryOwnerRow row = rows[index];
                string candidate = row?.primaryHandle?.exactOwnerPawnIdOrEmpty
                    ?? row?.compatibilityHandle?.exactOwnerPawnIdOrEmpty;
                if (string.Equals(candidate, ownerId, StringComparison.Ordinal)) return row;
            }
            return null;
        }

        private MemoryBlockRow FindSelectedBlockRow()
        {
            if (threadDetail?.blocks != null)
            {
                for (int index = 0; index < threadDetail.blocks.Count; index++)
                    if (MemoryLibraryUiPolicy.Same(threadDetail.blocks[index]?.recordHandle,
                        session.selectedRecordHandle)) return threadDetail.blocks[index];
            }
            if (list?.rows != null)
            {
                for (int index = 0; index < list.rows.Count; index++)
                {
                    MemoryBlockRow row = list.rows[index]?.standalone;
                    if (MemoryLibraryUiPolicy.Same(row?.recordHandle,
                        session.selectedRecordHandle)) return row;
                }
            }
            return blockDetail?.row;
        }

        private MemoryImportedRow FindSelectedImportedRow()
        {
            if (list?.rows == null) return null;
            for (int index = 0; index < list.rows.Count; index++)
            {
                MemoryImportedRow row = list.rows[index]?.imported;
                if (Same(row?.archiveHandle, session.selectedArchiveHandle)) return row;
            }
            return null;
        }

        private static MemoryLibraryFilters CopyFilters(MemoryLibraryFilters source)
        {
            return new MemoryLibraryFilters
            {
                importanceMask = source?.importanceMask ?? 0,
                categoryMask = source?.categoryMask ?? 0,
                stateToken = source?.stateToken ?? "all"
            };
        }

        private static bool Same(MemoryArchiveHandle left, MemoryArchiveHandle right)
        {
            return left != null && right != null
                && left.archiveScopeToken == right.archiveScopeToken
                && left.exactOwnerPawnIdOrEmpty == right.exactOwnerPawnIdOrEmpty
                && left.archiveRecordId == right.archiveRecordId;
        }

        private static bool Same(MemoryRootHandle left, MemoryRootHandle right)
        {
            return left != null && right != null
                && left.ownerPawnId == right.ownerPawnId
                && left.epochToken == right.epochToken
                && left.rootId == right.rootId;
        }

        private static string OwnerKey(MemoryLibraryOwnerHandle handle)
        {
            return handle == null ? string.Empty : (handle.scopeToken ?? string.Empty) + "\n"
                + (handle.exactOwnerPawnIdOrEmpty ?? string.Empty) + "\n"
                + (handle.epochTokenOrEmpty ?? string.Empty);
        }
    }
}

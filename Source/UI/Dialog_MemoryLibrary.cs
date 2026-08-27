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
        private MemoryLibraryOwnerHandle ownerWalkHandle;
        private int ownerStart;
        private long ownerExpectedDirectoryRevision;
        private long selectedOwnerValidatedDirectoryRevision;
        private MemoryLibraryOwnerHandle selectedOwnerValidationHandle;
        private MemoryLibraryOwnerRow selectedOwnerValidationFallback;
        private int selectedOwnerValidationStart;
        private long selectedOwnerValidationExpectedDirectoryRevision;
        private MemoryLibraryOwnerResult owners;
        private MemoryLibraryOwnerRow selectedOwner;

        private int listStart;
        private long listExpectedSnapshotRevision;
        private MemoryLibraryListResult list;
        private int detailStart;
        private long detailExpectedSnapshotRevision;
        private MemoryThreadDetailResult threadDetail;
        private MemoryBlockDetailResult blockDetail;
        private MemoryBlockRow selectedBlockRow;
        private int importedTextStart;
        private long importedTextExpectedSnapshotRevision;
        private MemoryImportedDetailResult importedDetail;
        private MemoryImportedRow selectedImportedRow;
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
        private int detachedSearchScalarCap = 80;
        private int detachedSearchUtf16Cap = 160;
        private int detachedDiagnosticTextCap = 2000;
        private bool detachedCompatibilityFailClosed;
        private bool loreExpanded;
        private bool diagnosticsExpanded;
        private bool ownerSearchDirty = true;
        private bool listQueryDirty = true;
        private bool detailQueryDirty = true;

        private Vector2 listScroll;
        private Vector2 detailScroll;
        private Vector2 blockDetailScroll;
        private Vector2 currentStatusScroll;
        private Vector2 loreScroll;
        private readonly Dictionary<string, string> cachedBlockBadges =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> cachedLifetimeLabels =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<long, string> cachedDateLabels =
            new Dictionary<long, string>();
        private readonly Dictionary<string, string> cachedUsageLabels =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private string cachedThreadHeaderText = string.Empty;
        private string cachedBlockFactsText = string.Empty;
        private string cachedDiagnosticText = string.Empty;
        private string cachedCultureTitle = string.Empty;
        private string cachedCultureOrigin = string.Empty;
        private string cachedCultureAdopted = string.Empty;
        private string cachedCultureExplanation = string.Empty;
        private bool cachedCultureHasAdopted;
        private string displayCacheSignature = string.Empty;
        private int selectedLoreTopicIndex;
        private int lastRepositoryPollFrame = -1;

        private Dialog_MemoryLibrary(DiaryGameComponent source, string preferredExactOwnerId)
        {
            component = source;
            preferredOwnerId = preferredExactOwnerId ?? string.Empty;
            openedGeneration = lifecycleGeneration;
            MemoryLibraryLimits inputLimits = source?.MemoryLibraryInputLimitsForUi()
                ?? new MemoryLibraryLimits();
            detachedSearchScalarCap = Math.Max(1, inputLimits.searchScalars);
            detachedSearchUtf16Cap = Math.Max(1, inputLimits.searchUtf16Units);
            detachedDiagnosticTextCap = Math.Max(1, inputLimits.copyDiagnosticUtf16Units);
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
            ownerWalkHandle = null;
            ownerStart = 0;
            ownerExpectedDirectoryRevision = 0;
            selectedOwnerValidatedDirectoryRevision = 0;
            ResetSelectedOwnerValidation();
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
            bool immediateRepositoryWork = ownerSearchDirty || listQueryDirty || detailQueryDirty
                || pendingCommandId > 0 || session.pendingCommand != null;
            int currentFrame = Time.frameCount;
            if (!MemoryLibraryUiPollPolicy.ShouldPoll(
                    currentFrame,
                    lastRepositoryPollFrame,
                    DiaryUiStyles.Current.memoryLibraryRepositoryPollFrames,
                    immediateRepositoryWork)) return;
            lastRepositoryPollFrame = currentFrame;
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
            cachedUsageLabels.Clear();
            cachedThreadHeaderText = string.Empty;
            cachedBlockFactsText = string.Empty;
            cachedDiagnosticText = string.Empty;
            cachedCultureTitle = string.Empty;
            cachedCultureOrigin = string.Empty;
            cachedCultureAdopted = string.Empty;
            cachedCultureExplanation = string.Empty;
            cachedCultureHasAdopted = false;
            selectedBlockRow = null;
            selectedImportedRow = null;
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
                ownerWalkHandle = null;
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
                preferredFallback = null;
                selectedOwnerValidatedDirectoryRevision = 0;
                ResetSelectedOwnerValidation();
                if (string.IsNullOrWhiteSpace(session.ownerSearch)
                    && string.IsNullOrWhiteSpace(preferredOwnerId)
                    && session.selectedOwnerHandle != null)
                    ownerWalkHandle = MemoryLibraryUiPolicy.Copy(session.selectedOwnerHandle);
                return;
            }
            if (result.status != MemoryLibraryStatuses.Ready) return;
            ownerExpectedDirectoryRevision = result.directoryRevision;

            string before = OwnerKey(session.selectedOwnerHandle);
            MemoryLibraryOwnerRow resolvedOwner = null;
            if (result.directoryRowCount == 0)
            {
                session.ReconcileOwnerDirectory(result, string.Empty, true);
                selectedOwner = null;
                preferredOwnerId = string.Empty;
                ownerWalkHandle = null;
                preferredFallback = null;
                ownerStart = 0;
                selectedOwnerValidatedDirectoryRevision = result.directoryRevision;
                ResetSelectedOwnerValidation();
                if (!string.Equals(before, OwnerKey(session.selectedOwnerHandle),
                        StringComparison.Ordinal)) ResetOwnerQueries();
                return;
            }

            bool searchEmpty = string.IsNullOrWhiteSpace(session.ownerSearch);
            if (!searchEmpty && session.selectedOwnerHandle != null
                && selectedOwnerValidatedDirectoryRevision != result.directoryRevision)
            {
                ValidateSelectedOwner(result.directoryRevision);
                // Validation owns any searched selection transition and already retained the exact
                // unfiltered row. Do not process that same transition again against the searched page.
                before = OwnerKey(session.selectedOwnerHandle);
            }
            bool canonical = ownerStart == 0 && searchEmpty;
            bool needsValidation = canonical && session.selectedOwnerHandle != null
                && selectedOwnerValidatedDirectoryRevision != result.directoryRevision;
            if (needsValidation && ownerWalkHandle == null
                && string.IsNullOrWhiteSpace(preferredOwnerId))
                ownerWalkHandle = MemoryLibraryUiPolicy.Copy(session.selectedOwnerHandle);
            bool walking = searchEmpty && (ownerWalkHandle != null
                || !string.IsNullOrWhiteSpace(preferredOwnerId));
            if (walking)
            {
                MemoryLibraryUiOwnerWalkStep step = MemoryLibraryUiPolicy.PlanOwnerWalk(
                    result.rows, result.hasMore, preferredFallback, ownerWalkHandle,
                    preferredOwnerId);
                preferredFallback = step.fallback;
                if (step.continuePaging)
                {
                    ownerStart = result.nextStart;
                    ownerExpectedDirectoryRevision = result.directoryRevision;
                    return;
                }
                if (step.selected != null)
                {
                    bool exactRefresh = ownerWalkHandle != null
                        && (MemoryLibraryUiPolicy.Same(step.selected.primaryHandle, ownerWalkHandle)
                            || MemoryLibraryUiPolicy.Same(
                                step.selected.compatibilityHandle, ownerWalkHandle));
                    if (exactRefresh)
                        session.ReconcileOwnerDirectory(result, string.Empty, true);
                    else
                        session.SelectOwner(step.selected);
                    selectedOwner = step.selected;
                    resolvedOwner = step.selected;
                    selectedOwnerValidatedDirectoryRevision = result.directoryRevision;
                }
                preferredOwnerId = string.Empty;
                ownerWalkHandle = null;
                preferredFallback = null;
                ownerStart = 0;
            }
            else
            {
                session.ReconcileOwnerDirectory(result, string.Empty, canonical);
                if (canonical && session.selectedOwnerHandle != null)
                    selectedOwnerValidatedDirectoryRevision = result.directoryRevision;
            }
            if (!string.Equals(before, OwnerKey(session.selectedOwnerHandle), StringComparison.Ordinal))
                ResetOwnerQueries(MemoryLibraryUiPolicy.ResolveOwnerRow(
                    resolvedOwner, result.rows, session.selectedOwnerHandle));
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
                RestartListStream();
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
                RestartListStream();
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
                    RestartDetailStream();
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
                    RestartDetailStream();
                else if (threadDetail.status == MemoryLibraryStatuses.Ready)
                    detailExpectedSnapshotRevision = threadDetail.detailSnapshotRevision;
            }

            if (session.selectedRecordHandle != null)
            {
                MemoryBlockRow selected = FindSelectedBlockRow();
                if (selected != null)
                {
                    long targetStructuralRevision =
                        MemoryLibraryUiPolicy.BlockDetailStructuralRevision(
                            selected, threadDetail?.header,
                            list?.ownerStructuralRevision ?? 0);
                    MemoryBlockDetailResult refreshed = component.QueryMemoryBlockDetail(
                        new MemoryBlockDetailQuery
                        {
                            recordHandle = MemoryLibraryUiPolicy.Copy(selected.recordHandle),
                            rootHandle = MemoryLibraryUiPolicy.Copy(selected.rootHandle),
                            placementToken = selected.rootHandle == null ? "standalone" : string.Empty,
                            targetStructuralRevision = targetStructuralRevision,
                            projectionToken = "full"
                        });
                    blockDetail = refreshed;
                    if (refreshed?.status == MemoryLibraryStatuses.Stale)
                    {
                        RestartListStream();
                        RestartDetailStream();
                    }
                    else if (refreshed?.status == MemoryLibraryStatuses.Ready)
                    {
                        selectedBlockRow = refreshed.row;
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
                    long targetStructuralRevision =
                        MemoryLibraryUiPolicy.ImportedDetailStructuralRevision(
                            row, selectedOwner, list?.ownerStructuralRevision ?? 0);
                    importedDetail = component.QueryMemoryImportedDetail(
                        new MemoryImportedDetailQuery
                        {
                            archiveHandle = MemoryLibraryUiPolicy.Copy(row.archiveHandle),
                            textStart = importedTextStart,
                            textCount = ImportedTextPageSize,
                            expectedArchiveTextSnapshotRevision = importedTextExpectedSnapshotRevision,
                            targetStructuralRevision = targetStructuralRevision
                        });
                    if (importedDetail.status == MemoryLibraryStatuses.Stale)
                    {
                        RestartListStream();
                        RestartImportedTextStream();
                    }
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
            RestartListStream();
            RestartDetailStream();
            RestartImportedTextStream();
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

        private void ResetOwnerQueries(MemoryLibraryOwnerRow retainedOwner = null)
        {
            selectedOwner = retainedOwner;
            ownerWalkHandle = null;
            compatibility = null;
            lore = null;
            loreExpanded = false;
            selectedLoreTopicIndex = 0;
            selectedBlockRow = null;
            selectedImportedRow = null;
            ResetListQuery();
        }

        /// <summary>
        /// Revalidates a selection against the complete unfiltered directory while the visible
        /// selector remains searched. This keeps off-window selections but clears removed owners.
        /// </summary>
        private void ValidateSelectedOwner(long directoryRevision)
        {
            MemoryLibraryOwnerHandle selected = session.selectedOwnerHandle;
            if (selected == null)
            {
                ResetSelectedOwnerValidation();
                return;
            }
            if (!MemoryLibraryUiPolicy.Same(selectedOwnerValidationHandle, selected)
                || selectedOwnerValidationExpectedDirectoryRevision != directoryRevision)
            {
                selectedOwnerValidationHandle = MemoryLibraryUiPolicy.Copy(selected);
                selectedOwnerValidationStart = 0;
                selectedOwnerValidationExpectedDirectoryRevision = directoryRevision;
            }
            MemoryLibraryOwnerResult validation = component.QueryMemoryLibraryOwners(
                new MemoryLibraryOwnerQuery
                {
                    search = string.Empty,
                    sortToken = "name",
                    start = selectedOwnerValidationStart,
                    count = PageSize,
                    expectedDirectoryRevision = selectedOwnerValidationExpectedDirectoryRevision
                });
            if (validation == null || validation.status == MemoryLibraryStatuses.Preparing) return;
            if (validation.status != MemoryLibraryStatuses.Ready
                || validation.directoryRevision != directoryRevision)
            {
                selectedOwnerValidatedDirectoryRevision = 0;
                ResetSelectedOwnerValidation();
                return;
            }
            MemoryLibraryUiOwnerWalkStep step = MemoryLibraryUiPolicy.PlanOwnerWalk(
                validation.rows, validation.hasMore, selectedOwnerValidationFallback,
                selectedOwnerValidationHandle, string.Empty);
            selectedOwnerValidationFallback = step.fallback;
            if (step.continuePaging)
            {
                selectedOwnerValidationStart = validation.nextStart;
                return;
            }

            string before = OwnerKey(session.selectedOwnerHandle);
            bool found = step.selected != null
                && (MemoryLibraryUiPolicy.Same(
                        step.selected.primaryHandle, selectedOwnerValidationHandle)
                    || MemoryLibraryUiPolicy.Same(
                        step.selected.compatibilityHandle, selectedOwnerValidationHandle));
            if (found)
            {
                session.ReconcileOwnerDirectory(validation, string.Empty, true);
                selectedOwner = step.selected;
            }
            else if (step.selected != null)
            {
                session.SelectOwner(step.selected);
                selectedOwner = step.selected;
            }
            else
            {
                session.ClearOwner();
                selectedOwner = null;
            }
            selectedOwnerValidatedDirectoryRevision = validation.directoryRevision;
            ResetSelectedOwnerValidation();
            if (!string.Equals(before, OwnerKey(session.selectedOwnerHandle),
                    StringComparison.Ordinal))
                // The searched owner page may not contain the canonical replacement. Keep the
                // validated detached row so list queries can continue while the search stays open.
                ResetOwnerQueries(selectedOwner);
        }

        private void ResetSelectedOwnerValidation()
        {
            selectedOwnerValidationHandle = null;
            selectedOwnerValidationFallback = null;
            selectedOwnerValidationStart = 0;
            selectedOwnerValidationExpectedDirectoryRevision = 0;
        }

        private void ResetListQuery()
        {
            listQueryDirty = true;
            RestartListStream();
            list = null;
            listScroll = Vector2.zero;
            ResetDetailQuery();
        }

        private void ResetDetailQuery()
        {
            detailQueryDirty = true;
            RestartDetailStream();
            RestartImportedTextStream();
            threadDetail = null;
            blockDetail = null;
            importedDetail = null;
            detailScroll = Vector2.zero;
            blockDetailScroll = Vector2.zero;
            currentStatusScroll = Vector2.zero;
        }

        private void RestartListStream()
        {
            listStart = 0;
            listExpectedSnapshotRevision = 0;
            listScroll = Vector2.zero;
        }

        private void RestartDetailStream()
        {
            detailStart = 0;
            detailExpectedSnapshotRevision = 0;
            detailScroll = Vector2.zero;
            currentStatusScroll = Vector2.zero;
        }

        private void RestartImportedTextStream()
        {
            importedTextStart = 0;
            importedTextExpectedSnapshotRevision = 0;
            detailScroll = Vector2.zero;
        }

        private MemoryBlockRow FindSelectedBlockRow()
        {
            if (threadDetail?.blocks != null)
            {
                for (int index = 0; index < threadDetail.blocks.Count; index++)
                    if (MemoryLibraryUiPolicy.Same(threadDetail.blocks[index]?.recordHandle,
                        session.selectedRecordHandle))
                    {
                        selectedBlockRow = threadDetail.blocks[index];
                        return selectedBlockRow;
                    }
            }
            if (list?.rows != null)
            {
                for (int index = 0; index < list.rows.Count; index++)
                {
                    MemoryBlockRow row = list.rows[index]?.standalone;
                    if (MemoryLibraryUiPolicy.Same(row?.recordHandle,
                        session.selectedRecordHandle))
                    {
                        selectedBlockRow = row;
                        return selectedBlockRow;
                    }
                }
            }
            return MemoryLibraryUiPolicy.Same(selectedBlockRow?.recordHandle,
                session.selectedRecordHandle) ? selectedBlockRow : null;
        }

        private MemoryImportedRow FindSelectedImportedRow()
        {
            for (int index = 0; list?.rows != null && index < list.rows.Count; index++)
            {
                MemoryImportedRow row = list.rows[index]?.imported;
                if (Same(row?.archiveHandle, session.selectedArchiveHandle))
                {
                    selectedImportedRow = row;
                    return selectedImportedRow;
                }
            }
            return Same(selectedImportedRow?.archiveHandle, session.selectedArchiveHandle)
                ? selectedImportedRow : null;
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

// Phase-M9 pure UI-policy fixtures. These exercise detached behavior rather than searching source
// text, so owner identity, imported gating, TTL, conflicts, commands, and viewport bounds are proven
// without loading RimWorld or Unity.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PawnDiary;

namespace MemoryThreadTests
{
    internal static class MemoryM9Fixtures
    {
        private static int assertions;

        public static int Run()
        {
            assertions = 0;
            OwnerSelectionUsesExactHandles();
            ImportedVisibilityAndFiltersAreExact();
            CapacityAndEmptyStateRulesAreExact();
            DetachedDraftsHandleStatusAndStructuralConflicts();
            CommandsCarryExactIdentityAndRevision();
            DrawingStagesButDoesNotExecuteCommands();
            LifetimeStatesUseOriginalTicks();
            VirtualizationIsBoundedAtMinimumViewport();
            ActivationAndNoGameGateFailClosed();
            LibraryLocalizationHasEnglishRussianPlaceholderParity();
            return assertions;
        }

        private static void OwnerSelectionUsesExactHandles()
        {
            MemoryLibraryOwnerRow first = Owner("pawn-a", "epoch-a", "Same label", 1);
            MemoryLibraryOwnerRow second = Owner("pawn-b", "epoch-b", "Same label", 1);
            MemoryLibraryOwnerResult directory = new MemoryLibraryOwnerResult
            {
                status = MemoryLibraryStatuses.Ready,
                directoryRowCount = 2,
                totalMatchedRows = 2,
                directoryRevision = 1,
                rows = new List<MemoryLibraryOwnerRow> { first, second }
            };
            MemoryLibraryUiSession session = new MemoryLibraryUiSession();
            session.ReconcileOwnerDirectory(directory, "pawn-b", true);
            Equal("m9.owner.preferred.exact", "pawn-b",
                session.selectedOwnerHandle.exactOwnerPawnIdOrEmpty);
            session.SelectOwner(first);
            session.editDraft = new MemoryLibraryUiEditDraft { text = "keep me" };
            directory.rows.Reverse();
            session.ReconcileOwnerDirectory(directory, string.Empty, true);
            Equal("m9.owner.same-label.retained-by-handle", "pawn-a",
                session.selectedOwnerHandle.exactOwnerPawnIdOrEmpty);
            Equal("m9.owner.refresh-retains-draft", "keep me", session.editDraft.text);

            MemoryLibraryOwnerResult offWindow = new MemoryLibraryOwnerResult
            {
                status = MemoryLibraryStatuses.Ready,
                directoryRowCount = 2,
                totalMatchedRows = 2,
                directoryRevision = 1,
                rows = new List<MemoryLibraryOwnerRow> { second }
            };
            session.ReconcileOwnerDirectory(offWindow, string.Empty, true);
            Equal("m9.owner.off-window-retains-selection", "pawn-a",
                session.selectedOwnerHandle.exactOwnerPawnIdOrEmpty);
            Equal("m9.owner.off-window-retains-draft", "keep me", session.editDraft.text);

            MemoryLibraryUiOwnerWalkStep firstWalk = MemoryLibraryUiPolicy.PlanOwnerWalk(
                new List<MemoryLibraryOwnerRow> { first }, true, null,
                second.primaryHandle, string.Empty);
            Equal("m9.owner.walk-page-zero-continues", true, firstWalk.continuePaging);
            Equal("m9.owner.walk-retains-canonical-fallback", "pawn-a",
                firstWalk.fallback.primaryHandle.exactOwnerPawnIdOrEmpty);
            MemoryLibraryUiOwnerWalkStep secondWalk = MemoryLibraryUiPolicy.PlanOwnerWalk(
                new List<MemoryLibraryOwnerRow> { second }, false, firstWalk.fallback,
                second.primaryHandle, string.Empty);
            Equal("m9.owner.walk-finds-later-page", "pawn-b",
                secondWalk.selected.primaryHandle.exactOwnerPawnIdOrEmpty);
            MemoryLibraryUiOwnerWalkStep exhaustedWalk = MemoryLibraryUiPolicy.PlanOwnerWalk(
                new List<MemoryLibraryOwnerRow> { first }, false, firstWalk.fallback,
                Owner("missing", "epoch", "Missing", 0).primaryHandle, string.Empty);
            Equal("m9.owner.walk-exhausted-uses-fallback", "pawn-a",
                exhaustedWalk.selected.primaryHandle.exactOwnerPawnIdOrEmpty);

            session.SelectOwner(second);
            Equal("m9.owner.explicit-change-uses-exact-handle", "pawn-b",
                session.selectedOwnerHandle.exactOwnerPawnIdOrEmpty);
            Equal("m9.owner.explicit-change-clears-draft", null, session.editDraft);
            session.SelectOwner(first);

            MemoryLibraryOwnerResult noMatches = new MemoryLibraryOwnerResult
            {
                status = MemoryLibraryStatuses.Ready,
                directoryRowCount = 2,
                totalMatchedRows = 0,
                directoryRevision = 1,
                ownerEmptyStateToken = "no_matches"
            };
            session.ReconcileOwnerDirectory(noMatches, string.Empty, false);
            Equal("m9.owner.search-empty.retains-selection", "pawn-a",
                session.selectedOwnerHandle.exactOwnerPawnIdOrEmpty);
            MemoryLibraryOwnerResult noOwners = new MemoryLibraryOwnerResult
            {
                status = MemoryLibraryStatuses.Ready,
                directoryRowCount = 0,
                ownerEmptyStateToken = "no_owners"
            };
            session.editDraft = new MemoryLibraryUiEditDraft { text = "detached" };
            session.ReconcileOwnerDirectory(noOwners, string.Empty, true);
            Equal("m9.owner.empty.clears-selection", null, session.selectedOwnerHandle);
            Equal("m9.owner.empty.clears-draft", null, session.editDraft);

            // Relationship phases are detail-only. Two headers with one exact root ID each remain two
            // cards even when both frozen labels and all chapter phase tokens collide.
            MemoryLibraryOwnerIndexInput input = new MemoryLibraryOwnerIndexInput
            {
                primaryHandle = first.primaryHandle,
                ownerEpochKey = first.activeOwnerEpochKey
            };
            input.roots.Add(Root("root-a", "Same subject", "relationship_phase"));
            input.roots.Add(Root("root-b", "Same subject", "relationship_phase"));
            MemoryLibraryOwnerIndexSnapshot snapshot = MemoryLibraryIndexPolicy.BuildOwner(
                input, new MemoryLibraryLimits());
            Equal("m9.root.labels-phases.do-not-collapse", 2, snapshot.roots.Count);
            Equal("m9.root.cards.match-exact-roots", 2, snapshot.ownerRow.threadCount);
        }

        private static void DrawingStagesButDoesNotExecuteCommands()
        {
            MemoryLibraryUiSession session = new MemoryLibraryUiSession();
            int repositoryMutations = 0;
            MemoryLibraryCommand command = new MemoryLibraryCommand
            {
                libraryClientToken = "client",
                commandId = 10,
                actionToken = MemoryLibraryActions.SetSuppressed
            };
            Equal("m9.draw.stage.accepted", true, session.StageCommand(command));
            Equal("m9.draw.stage.no-repository-mutation", 0, repositoryMutations);
            Equal("m9.draw.stage.second-rejected", false,
                session.StageCommand(new MemoryLibraryCommand()));
            MemoryLibraryCommand drained = session.TakeStagedCommand();
            if (drained != null) repositoryMutations++;
            Equal("m9.update.drain.exact-command", command, drained);
            Equal("m9.update.drain.mutates-once", 1, repositoryMutations);
            Equal("m9.update.drain.empty", null, session.TakeStagedCommand());
        }

        private static void ImportedVisibilityAndFiltersAreExact()
        {
            MemoryLibraryOwnerRow active = Owner("pawn", "epoch", "Owner", 0);
            Equal("m9.imported.empty.hidden", false,
                MemoryLibraryUiPolicy.HasImportedViewContent(active));
            active.importedCount = 1;
            Equal("m9.imported.current.visible", true,
                MemoryLibraryUiPolicy.HasImportedViewContent(active));
            active.importedCount = 0;
            active.compatibilityHandle = new MemoryLibraryOwnerHandle(
                MemoryLibraryScopes.InertCurrentExact, "pawn", string.Empty);
            Equal("m9.imported.compat.visible", true,
                MemoryLibraryUiPolicy.HasImportedViewContent(active));

            MemoryLibraryUiSession session = new MemoryLibraryUiSession();
            session.filters.importanceMask = MemoryLibraryPolicy.ImportanceImportant;
            session.filters.categoryMask = MemoryCategoryBits.Family;
            session.filters.stateToken = "suppressed";
            session.SelectView(MemoryLibraryViews.Imported);
            Equal("m9.imported.clears.importance", 0, session.filters.importanceMask);
            Equal("m9.imported.clears.category", 0, session.filters.categoryMask);
            Equal("m9.imported.clears.state", "all", session.filters.stateToken);

            MemoryLibraryOwnerRow rawOnly = new MemoryLibraryOwnerRow
            {
                displayName = "Unknown owner",
                compatibilityHandle = new MemoryLibraryOwnerHandle(
                    MemoryLibraryScopes.LegacyRawUnknown, string.Empty, string.Empty)
            };
            session.SelectOwner(rawOnly);
            Equal("m9.imported.raw-only.default-view", MemoryLibraryViews.Imported,
                session.selectedView);
            Equal("m9.imported.raw-only-has-no-active-tabs", false,
                MemoryLibraryUiPolicy.HasActiveViews(rawOnly));
            Equal("m9.imported.raw-only.exact-handle", MemoryLibraryScopes.LegacyRawUnknown,
                session.selectedOwnerHandle.scopeToken);
            MemoryLibraryOwnerResult rawDirectory = new MemoryLibraryOwnerResult
            {
                status = MemoryLibraryStatuses.Ready,
                directoryRowCount = 1,
                totalMatchedRows = 1,
                rows = new List<MemoryLibraryOwnerRow> { rawOnly }
            };
            session.editDraft = new MemoryLibraryUiEditDraft { text = "raw draft" };
            session.ReconcileOwnerDirectory(rawDirectory, string.Empty, true);
            Equal("m9.imported.raw-refresh-retains-handle", MemoryLibraryScopes.LegacyRawUnknown,
                session.selectedOwnerHandle.scopeToken);
            Equal("m9.imported.raw-refresh-retains-draft", "raw draft", session.editDraft.text);

            MemoryLibraryUiSession disappearing = new MemoryLibraryUiSession();
            disappearing.SelectOwner(active);
            disappearing.SelectView(MemoryLibraryViews.Imported);
            active.compatibilityHandle = null;
            MemoryLibraryOwnerResult refreshed = new MemoryLibraryOwnerResult
            {
                status = MemoryLibraryStatuses.Ready,
                directoryRowCount = 1,
                rows = new List<MemoryLibraryOwnerRow> { active }
            };
            disappearing.ReconcileOwnerDirectory(refreshed, string.Empty, true);
            Equal("m9.imported.removal-returns-threads", MemoryLibraryViews.Threads,
                disappearing.selectedView);
        }

        private static void DetachedDraftsHandleStatusAndStructuralConflicts()
        {
            MemoryBlockDetailResult detail = Detail("record", 7, 11);
            MemoryLibraryUiEditDraft draft = MemoryLibraryUiPolicy.BeginEdit(detail, 480);
            Equal("m9.draft.starts-detached", "saved wording", draft.text);
            detail.targetStatusRevision = 12;
            MemoryLibraryUiPolicy.MergeDetailRefresh(draft, detail);
            Equal("m9.draft.status-refresh-keeps-text", "saved wording", draft.text);
            Equal("m9.draft.status-refresh-updates-status", 12L, draft.latestStatusRevision);
            Equal("m9.draft.status-refresh-no-conflict", false, draft.structuralConflict);

            draft.text = "player keeps this";
            detail.targetStructuralRevision = 8;
            detail.row.targetStructuralRevision = 8;
            MemoryLibraryUiPolicy.MergeDetailRefresh(draft, detail);
            Equal("m9.draft.structural-keeps-text", "player keeps this", draft.text);
            Equal("m9.draft.structural-rebases-fence", 8L, draft.targetStructuralRevision);
            Equal("m9.draft.structural-conflict", true, draft.structuralConflict);
            Equal("m9.draft.stale-retained", false,
                MemoryLibraryUiPolicy.ApplyEditCommandResult(draft,
                    new MemoryLibraryCommandResult { status = MemoryLibraryCommandStatuses.Stale }));
            Equal("m9.draft.cap-retained", false,
                MemoryLibraryUiPolicy.ApplyEditCommandResult(draft,
                    new MemoryLibraryCommandResult { status = MemoryLibraryCommandStatuses.CapFull }));
            Equal("m9.draft.success-consumed", true,
                MemoryLibraryUiPolicy.ApplyEditCommandResult(draft,
                    new MemoryLibraryCommandResult { status = MemoryLibraryCommandStatuses.Success }));
        }

        private static void CapacityAndEmptyStateRulesAreExact()
        {
            MemoryLibraryOwnerRow zeroMemory = new MemoryLibraryOwnerRow
            {
                primaryHandle = new MemoryLibraryOwnerHandle(
                    MemoryLibraryScopes.Active, "zero", string.Empty)
            };
            Equal("m9.views.zero-memory-active", true,
                MemoryLibraryUiPolicy.HasActiveViews(zeroMemory));
            zeroMemory.primaryHandle = new MemoryLibraryOwnerHandle(
                MemoryLibraryScopes.ArchiveOnly, "zero", string.Empty);
            Equal("m9.views.archive-only-no-active", false,
                MemoryLibraryUiPolicy.HasActiveViews(zeroMemory));

            MemoryLibraryFilters filters = new MemoryLibraryFilters
            {
                importanceMask = MemoryLibraryPolicy.ImportanceImportant,
                stateToken = "all"
            };
            Equal("m9.empty.filter-overrides-backend-no-memories", "no_filter_matches",
                MemoryLibraryUiPolicy.ListEmptyState("no_memories", string.Empty, filters));
            Equal("m9.empty.search-overrides-filter", "no_matches",
                MemoryLibraryUiPolicy.ListEmptyState("no_memories", "needle", filters));
            filters.importanceMask = 0;
            Equal("m9.empty.genuine-no-memories", "no_memories",
                MemoryLibraryUiPolicy.ListEmptyState("no_memories", string.Empty, filters));

            Equal("m9.current-status.default-is-missing", false,
                MemoryLibraryUiPolicy.HasCapturedCurrentStatus(new MemoryCurrentStatusDto()));
            Equal("m9.current-status.tick-zero-is-captured", true,
                MemoryLibraryUiPolicy.HasCapturedCurrentStatus(
                    new MemoryCurrentStatusDto { capturedTick = 0 }));

            List<string> fields = new List<string>();
            Equal("m9.capacity.first-field", true, MemoryLibraryPolicy.TryAppendBoundedText(
                fields, "1234", 2, 6));
            Equal("m9.capacity.total-clamps-second", true,
                MemoryLibraryPolicy.TryAppendBoundedText(fields, "😀z", 2, 6));
            Equal("m9.capacity.total-exact", 6, fields[0].Length + fields[1].Length);
            Equal("m9.capacity.scalar-complete", "😀", fields[1]);
            Equal("m9.capacity.count-refuses-third", false,
                MemoryLibraryPolicy.TryAppendBoundedText(fields, "x", 2, 6));
            Equal("m9.search.repairs-unpaired-surrogate", "�",
                MemoryLibraryPolicy.RepairMalformedUtf16("\ud800"));
        }

        private static void CommandsCarryExactIdentityAndRevision()
        {
            MemoryBlockDetailResult detail = Detail("record", 17, 20);
            MemoryLibraryUiEditDraft draft = MemoryLibraryUiPolicy.BeginEdit(detail, 480);
            draft.text = "new words";
            MemoryLibraryCommand edit = MemoryLibraryUiPolicy.BuildBlockCommand(
                "client", 4, MemoryLibraryActions.SaveWording, detail.row, false, draft);
            Equal("m9.command.edit.owner", "pawn", edit.recordHandle.ownerPawnId);
            Equal("m9.command.edit.epoch", "epoch", edit.recordHandle.epochToken);
            Equal("m9.command.edit.record", "record", edit.recordHandle.recordId);
            Equal("m9.command.edit.root", "root", edit.rootHandle.rootId);
            Equal("m9.command.edit.revision", 17L, edit.targetStructuralRevision);
            Equal("m9.command.edit.text", "new words", edit.wordingDraft);

            MemoryLibraryCommand suppress = MemoryLibraryUiPolicy.BuildBlockCommand(
                "client", 5, MemoryLibraryActions.SetSuppressed,
                detail.row, true, null);
            Equal("m9.command.suppress.explicit-desired", true, suppress.desiredSuppressed);
            Equal("m9.command.suppress.has-desired", true, suppress.hasDesiredSuppressed);

            MemoryImportedRow imported = new MemoryImportedRow
            {
                archiveHandle = new MemoryArchiveHandle
                {
                    archiveScopeToken = MemoryLibraryScopes.UnresolvedImported,
                    archiveRecordId = "archive-record"
                },
                targetStructuralRevision = 91
            };
            MemoryLibraryCommand forget = MemoryLibraryUiPolicy.BuildImportedForgetCommand(
                "client", 6, imported);
            Equal("m9.command.imported.identity", "archive-record",
                forget.archiveHandle.archiveRecordId);
            Equal("m9.command.imported.revision", 91L, forget.targetStructuralRevision);
        }

        private static void LifetimeStatesUseOriginalTicks()
        {
            const long minor = 900;
            const long regular = 3600;
            MemoryBlockRow row = Block("ttl", 100, MemoryLibraryPolicy.ImportanceMinor);
            Equal("m9.ttl.before-boundary", MemoryLibraryUiLifetimeTokens.Minor,
                MemoryLibraryUiPolicy.Lifetime(row, 999, minor, regular).stateToken);
            Equal("m9.ttl.exact-boundary", MemoryLibraryUiLifetimeTokens.Due,
                MemoryLibraryUiPolicy.Lifetime(row, 1000, minor, regular).stateToken);
            row.playerEdited = true;
            Equal("m9.ttl.edited-protected", MemoryLibraryUiLifetimeTokens.Protected,
                MemoryLibraryUiPolicy.Lifetime(row, 1000, minor, regular).stateToken);
            Equal("m9.ttl.original-warning", true,
                MemoryLibraryUiPolicy.PastNormalLifetime(row, 1000, minor, regular));
            row.playerEdited = false;
            row.ageUnknown = true;
            row.projectedHighestImportanceMask = MemoryLibraryPolicy.ImportanceImportant;
            Equal("m9.ttl.unknown-important", MemoryLibraryUiLifetimeTokens.Unknown,
                MemoryLibraryUiPolicy.Lifetime(row, 5000, minor, regular).stateToken);
            row.ageUnknown = false;
            Equal("m9.ttl.important-no-limit", MemoryLibraryUiLifetimeTokens.Important,
                MemoryLibraryUiPolicy.Lifetime(row, 5000, minor, regular).stateToken);

            MemoryBlockRow summary = Block("summary", 100,
                MemoryLibraryPolicy.ImportanceImportant);
            summary.kind = "Summary";
            summary.summaryContributions.Add(new MemorySummaryContributionDescriptor
            {
                originalTick = 100,
                importanceMask = MemoryLibraryPolicy.ImportanceMinor
            });
            summary.summaryContributions.Add(new MemorySummaryContributionDescriptor
            {
                originalTick = 200,
                importanceMask = MemoryLibraryPolicy.ImportanceImportant
            });
            MemoryLibraryUiLifetime mixed = MemoryLibraryUiPolicy.Lifetime(
                summary, 500, minor, regular);
            Equal("m9.ttl.summary-mixed", MemoryLibraryUiLifetimeTokens.Mixed,
                mixed.stateToken);
            Equal("m9.ttl.summary-next-original-expiry", 1000L, mixed.expiryTick);

            MemoryBlockRow sameFamily = Block("same-family", 300,
                MemoryLibraryPolicy.ImportanceMinor);
            sameFamily.kind = "Summary";
            sameFamily.summaryContributions.Add(new MemorySummaryContributionDescriptor
            {
                originalTick = 300,
                importanceMask = MemoryLibraryPolicy.ImportanceMinor
            });
            sameFamily.summaryContributions.Add(new MemorySummaryContributionDescriptor
            {
                originalTick = 100,
                importanceMask = MemoryLibraryPolicy.ImportanceMinor
            });
            Equal("m9.ttl.summary-same-family-earliest-expiry", 1000L,
                MemoryLibraryUiPolicy.Lifetime(sameFamily, 500, minor, regular).expiryTick);
            sameFamily.summaryContributions.Reverse();
            Equal("m9.ttl.summary-same-family-order-independent", 1000L,
                MemoryLibraryUiPolicy.Lifetime(sameFamily, 500, minor, regular).expiryTick);
        }

        private static void VirtualizationIsBoundedAtMinimumViewport()
        {
            MemoryLibraryUiVirtualWindow first = MemoryLibraryUiPolicy.Virtualize(
                256, 0f, 80f, 40f, 2, 24);
            Equal("m9.virtual.first", 0, first.firstIndex);
            Equal("m9.virtual.overscan", 4, first.materializedCount);
            Equal("m9.virtual.content", 10240f, first.contentHeight);
            MemoryLibraryUiVirtualWindow middle = MemoryLibraryUiPolicy.Virtualize(
                256, 4000f, 10000f, 40f, 4, 24);
            Equal("m9.virtual.maximum", 24, middle.materializedCount);
            Equal("m9.virtual.middle-start", 96, middle.firstIndex);
            MemoryLibraryUiVirtualWindow minimum = MemoryLibraryUiPolicy.Virtualize(
                1, -100f, 0f, 0f, -2, 0);
            Equal("m9.virtual.minimum-progress", 1, minimum.materializedCount);
            Equal("m9.virtual.minimum-end", 1, minimum.endExclusive);
            MemoryLibraryUiVirtualWindow excessive = MemoryLibraryUiPolicy.Virtualize(
                3, 100000f, 40f, 40f, 0, 24);
            Equal("m9.virtual.excessive-scroll-clamps-first", 2, excessive.firstIndex);
            Equal("m9.virtual.excessive-scroll-clamps-end", 3, excessive.endExclusive);
            Equal("m9.virtual.excessive-scroll-materializes-one", 1,
                excessive.materializedCount);
        }

        private static void ActivationAndNoGameGateFailClosed()
        {
            Equal("m9.open.legacy-shadow", false,
                MemoryLibraryUiPolicy.CanOpen(false, true, true, true));
            Equal("m9.open.no-game", false,
                MemoryLibraryUiPolicy.CanOpen(true, true, false, true));
            Equal("m9.open.no-component", false,
                MemoryLibraryUiPolicy.CanOpen(true, true, true, false));
            Equal("m9.open.current-loaded", true,
                MemoryLibraryUiPolicy.CanOpen(true, true, true, true));
            Equal("m9.mutation.inactive-release", false,
                MemoryLibraryUiPolicy.CanMutate(false, false));
            Equal("m9.mutation.future-version", false,
                MemoryLibraryUiPolicy.CanMutate(true, true));
            Equal("m9.mutation.current-version", true,
                MemoryLibraryUiPolicy.CanMutate(true, false));
        }

        private static void LibraryLocalizationHasEnglishRussianPlaceholderParity()
        {
            string root = FindRepositoryRoot();
            string[] uiFiles =
            {
                Path.Combine(root, "Source", "UI", "Dialog_MemoryLibrary.cs"),
                Path.Combine(root, "Source", "UI", "Dialog_MemoryLibrary.Layout.cs"),
                Path.Combine(root, "Source", "UI", "DiaryJournalView.cs")
            };
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            Regex keyPattern = new Regex("PawnDiary\\.Memory\\.Library\\.[A-Za-z0-9.]+",
                RegexOptions.CultureInvariant);
            for (int fileIndex = 0; fileIndex < uiFiles.Length; fileIndex++)
                foreach (Match match in keyPattern.Matches(File.ReadAllText(uiFiles[fileIndex])))
                    used.Add(match.Value);

            Dictionary<string, string> english = ReadKeyed(Path.Combine(root,
                "Languages", "English", "Keyed", "PawnDiary.xml"));
            Dictionary<string, string> russian = ReadKeyed(Path.Combine(root,
                "Languages", "Russian (Русский)", "Keyed", "PawnDiary.xml"));
            foreach (string key in used.OrderBy(value => value, StringComparer.Ordinal))
            {
                Equal("m9.localization.english." + key, true, english.ContainsKey(key));
                Equal("m9.localization.russian." + key, true, russian.ContainsKey(key));
                if (english.ContainsKey(key) && russian.ContainsKey(key))
                    Equal("m9.localization.placeholders." + key,
                        PlaceholderSignature(english[key]), PlaceholderSignature(russian[key]));
            }
        }

        private static Dictionary<string, string> ReadKeyed(string path)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(
                StringComparer.Ordinal);
            foreach (XElement element in XDocument.Load(path).Root.Elements())
            {
                if (!result.TryAdd(element.Name.LocalName, element.Value))
                    throw new InvalidOperationException("duplicate localization key: "
                        + element.Name.LocalName + " in " + path);
            }
            return result;
        }

        private static string PlaceholderSignature(string value)
        {
            return string.Join(",", Regex.Matches(value ?? string.Empty, "\\{[0-9]+\\}")
                .Cast<Match>().Select(match => match.Value)
                .Distinct(StringComparer.Ordinal).OrderBy(token => token, StringComparer.Ordinal));
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(Environment.CurrentDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                directory = directory.Parent;
            if (directory == null) throw new InvalidOperationException("repository root not found");
            return directory.FullName;
        }

        private static MemoryLibraryOwnerRow Owner(
            string pawnId,
            string epoch,
            string label,
            int threadCount)
        {
            return new MemoryLibraryOwnerRow
            {
                primaryHandle = new MemoryLibraryOwnerHandle(
                    MemoryLibraryScopes.Active, pawnId, epoch),
                activeOwnerEpochKey = new MemoryOwnerEpochKey
                {
                    ownerPawnId = pawnId,
                    epochToken = epoch
                },
                displayName = label,
                threadCount = threadCount
            };
        }

        private static MemoryLibraryRootIndexInput Root(
            string rootId,
            string label,
            string phase)
        {
            MemoryRootHandle handle = new MemoryRootHandle
            {
                ownerPawnId = "pawn-a",
                epochToken = "epoch-a",
                rootId = rootId
            };
            MemoryBlockRow child = Block(rootId + "-record", 10,
                MemoryLibraryPolicy.ImportanceRegular);
            child.rootHandle = handle;
            child.chapterId = rootId + "-chapter";
            return new MemoryLibraryRootIndexInput
            {
                header = new MemoryThreadHeaderRow
                {
                    rootHandle = handle,
                    subjectLabel = label,
                    normalizedSearch = "SAME SUBJECT"
                },
                children = new List<MemoryBlockRow> { child },
                chapters = new List<MemoryChapterRow>
                {
                    new MemoryChapterRow
                    {
                        chapterId = child.chapterId,
                        phaseToken = phase
                    }
                }
            };
        }

        private static MemoryBlockDetailResult Detail(
            string recordId,
            long structural,
            long status)
        {
            MemoryBlockRow row = Block(recordId, 10, MemoryLibraryPolicy.ImportanceRegular);
            row.rootHandle = new MemoryRootHandle
            {
                ownerPawnId = "pawn",
                epochToken = "epoch",
                rootId = "root"
            };
            row.canSaveWording = true;
            row.targetStructuralRevision = structural;
            return new MemoryBlockDetailResult
            {
                status = MemoryLibraryStatuses.Ready,
                row = row,
                detail = new MemoryBlockDetail { playerWording = "saved wording" },
                targetStructuralRevision = structural,
                targetStatusRevision = status
            };
        }

        private static MemoryBlockRow Block(string id, long tick, int importance)
        {
            return new MemoryBlockRow
            {
                recordHandle = new MemoryRecordHandle
                {
                    ownerPawnId = "pawn",
                    epochToken = "epoch",
                    recordId = id
                },
                kind = "Event",
                originalTick = tick,
                projectedImportanceMask = importance,
                projectedHighestImportanceMask = importance,
                projectedCategoryMask = MemoryCategoryBits.Personal,
                displayWording = id,
                canSuppress = true
            };
        }

        private static void Equal<T>(string name, T expected, T actual)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(name + ": expected " + expected
                    + ", got " + actual + ".");
        }
    }
}

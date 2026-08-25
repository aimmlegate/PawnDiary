// MemoryLibraryIndexPolicy.cs — pure construction and paging over detached Library snapshots.
//
// The component copies saved truth into bounded input DTOs. This policy computes complete-domain
// counts, visibility, ordering, TTL deadlines, and windows without ever seeing a repository object.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    internal static class MemoryLibraryIndexPolicy
    {
        public static MemoryLibraryOwnerIndexSnapshot BuildOwner(
            MemoryLibraryOwnerIndexInput input,
            MemoryLibraryLimits limits)
        {
            if (input == null || !MemoryLibraryPolicy.ValidOwnerHandle(input.primaryHandle))
                return null;
            MemoryLibraryLimits cap = limits ?? new MemoryLibraryLimits();
            MemoryLibraryOwnerIndexSnapshot result = new MemoryLibraryOwnerIndexSnapshot
            {
                roots = Copy(input.roots),
                standalone = Copy(input.standalone),
                imported = Copy(input.imported)
            };
            long latest = 0;
            long earliest = long.MaxValue;
            int edited = 0;
            int suppressed = 0;
            for (int rootIndex = 0; rootIndex < result.roots.Count; rootIndex++)
            {
                MemoryLibraryRootIndexInput root = result.roots[rootIndex];
                if (root?.header == null) continue;
                latest = Math.Max(latest, root.header.latestActivityTick);
                edited = 0;
                suppressed = 0;
                int highestImportance = 0;
                long rootEarliest = long.MaxValue;
                for (int childIndex = 0; childIndex < root.children.Count; childIndex++)
                {
                    MemoryBlockRow child = root.children[childIndex];
                    if (child == null) continue;
                    if (child.playerEdited) edited++;
                    if (child.suppressed) suppressed++;
                    latest = Math.Max(latest, Math.Max(child.activityTick, child.originalTick));
                    earliest = Math.Min(earliest, child.projectedNextExpiryTick);
                    rootEarliest = Math.Min(rootEarliest, child.projectedNextExpiryTick);
                    highestImportance = HighestImportance(
                        highestImportance, child.projectedHighestImportanceMask);
                }
                root.header.targetCountedVisibleBlockCount = CountNonRolling(root.children);
                root.header.manageableMemoryCount = root.children.Count;
                root.header.editedCount = edited;
                root.header.suppressedCount = suppressed;
                root.header.highestImportanceMask = highestImportance;
                root.rootEarliestFiniteExpiryTickExclusive = rootEarliest;
            }
            for (int index = 0; index < result.standalone.Count; index++)
            {
                MemoryBlockRow row = result.standalone[index];
                if (row == null) continue;
                latest = Math.Max(latest, Math.Max(row.activityTick, row.originalTick));
                earliest = Math.Min(earliest, row.projectedNextExpiryTick);
            }
            for (int index = 0; index < result.imported.Count; index++)
                latest = Math.Max(latest, result.imported[index]?.originalTick ?? 0);
            result.ownerEarliestFiniteExpiryTickExclusive = earliest;
            result.ownerRow = new MemoryLibraryOwnerRow
            {
                primaryHandle = input.primaryHandle,
                activeOwnerEpochKey = input.ownerEpochKey,
                compatibilityHandle = input.compatibilityHandle,
                displayName = MemoryLibraryPolicy.ClampUtf16CompleteScalar(
                    input.displayName, cap.frozenDisplayLabelUtf16Units),
                lifecycleToken = input.lifecycleToken ?? string.Empty,
                culture = input.culture,
                threadCount = result.roots.Count,
                standaloneCount = result.standalone.Count,
                importedCount = result.imported.Count,
                latestActivityTick = latest,
                hasArchive = result.imported.Count > 0 || input.compatibilityHandle != null,
                legacyRawPending = input.compatibilityHandle != null,
                structuralRevision = input.structuralRevision,
                statusRevision = input.statusRevision,
                compatibilitySourcePayloadRevision = input.compatibilitySourcePayloadRevision,
                normalizedSearch = MemoryLibraryPolicy.NormalizeSearch(
                    input.displayName, cap.searchScalars, cap.searchUtf16Units)
            };
            return result;
        }

        public static MemoryLibraryOwnerResult QueryOwners(
            List<MemoryLibraryOwnerRow> canonicalRows,
            MemoryLibraryOwnerQuery query,
            long directoryRevision,
            MemoryLibraryLimits limits,
            long omittedLegacy,
            long omittedZero)
        {
            MemoryLibraryOwnerResult result = new MemoryLibraryOwnerResult();
            MemoryLibraryLimits cap = limits ?? new MemoryLibraryLimits();
            if (query == null || directoryRevision <= 0 || query.expectedDirectoryRevision < 0
                || (query.expectedDirectoryRevision > 0
                    && query.expectedDirectoryRevision != directoryRevision))
            {
                result.status = query != null && query.expectedDirectoryRevision > 0
                    ? MemoryLibraryStatuses.Stale : MemoryLibraryStatuses.Invalid;
                return result;
            }
            List<MemoryLibraryOwnerRow> source = canonicalRows
                ?? new List<MemoryLibraryOwnerRow>();
            string search = MemoryLibraryPolicy.NormalizeSearch(
                query.search, cap.searchScalars, cap.searchUtf16Units);
            List<MemoryLibraryOwnerRow> matched = new List<MemoryLibraryOwnerRow>();
            for (int index = 0; index < source.Count; index++)
            {
                MemoryLibraryOwnerRow row = source[index];
                if (row != null && MemoryLibraryPolicy.SearchMatches(row.normalizedSearch, search))
                    matched.Add(row);
            }
            matched.Sort((left, right) => CompareOwners(left, right, query.sortToken));
            MemoryLibraryCursorPlan cursor = MemoryLibraryPolicy.NormalizeRowCursor(
                query.start, query.count,
                Math.Min(cap.libraryWindowRows, cap.libraryWindowCeiling),
                query.expectedDirectoryRevision, matched.Count);
            if (!cursor.valid) return result;
            result.status = MemoryLibraryStatuses.Ready;
            result.directoryRowCount = source.Count;
            result.totalMatchedRows = matched.Count;
            result.additionalLegacyRawOwnersNotShown = Math.Max(0, omittedLegacy);
            result.additionalZeroMemoryOwnersNotShown = Math.Max(0, omittedZero);
            ApplyCursor(result, cursor);
            result.directoryRevision = directoryRevision;
            result.ownerEmptyStateToken = source.Count == 0
                ? "no_owners" : matched.Count == 0 ? "no_matches" : "none";
            for (int index = 0; index < cursor.returnedCount; index++)
                result.rows.Add(matched[cursor.start + index]);
            return result;
        }

        public static MemoryLibraryListResult QueryList(
            MemoryLibraryOwnerIndexSnapshot snapshot,
            MemoryLibraryListQuery query,
            long directoryRevision,
            long listRevision,
            long committedSettingsRevision,
            long languageRevision,
            long ttlDayRevision,
            long nextDayBoundary,
            MemoryLibraryLimits limits)
        {
            MemoryLibraryListResult result = new MemoryLibraryListResult();
            MemoryLibraryLimits cap = limits ?? new MemoryLibraryLimits();
            if (snapshot?.ownerRow == null || query == null || listRevision <= 0
                || directoryRevision <= 0 || !MemoryLibraryPolicy.ValidOwnerHandle(query.primaryHandle))
                return result;
            if (query.expectedDirectoryRevision > 0
                && query.expectedDirectoryRevision != directoryRevision)
            {
                result.status = MemoryLibraryStatuses.Stale;
                return result;
            }
            if (query.expectedListSnapshotRevision > 0
                && query.expectedListSnapshotRevision != listRevision)
            {
                result.status = MemoryLibraryStatuses.Stale;
                return result;
            }
            if (!SameHandle(snapshot.ownerRow.primaryHandle, query.primaryHandle)) return result;

            bool activeView = query.viewTag == MemoryLibraryViews.Threads
                || query.viewTag == MemoryLibraryViews.Standalone;
            if (activeView && !MemoryLibraryPolicy.HandlesMatch(
                query.primaryHandle, query.activeOwnerEpochKey)) return result;
            if (query.viewTag == MemoryLibraryViews.Imported
                && query.activeOwnerEpochKey != null) return result;

            string search = MemoryLibraryPolicy.NormalizeSearch(
                query.search, cap.searchScalars, cap.searchUtf16Units);
            List<MemoryLibraryListRow> eligible = new List<MemoryLibraryListRow>();
            List<MemoryLibraryListRow> matched = new List<MemoryLibraryListRow>();
            if (query.viewTag == MemoryLibraryViews.Threads)
                SelectRoots(snapshot.roots, query.filters, search, cap, eligible, matched);
            else if (query.viewTag == MemoryLibraryViews.Standalone)
                SelectStandalone(snapshot.standalone, query.filters, search, cap, eligible, matched);
            else if (query.viewTag == MemoryLibraryViews.Imported)
                SelectImported(snapshot.imported, search, eligible, matched);
            else return result;

            matched.Sort((left, right) => CompareListRows(left, right, query.sortToken));
            MemoryLibraryCursorPlan cursor = MemoryLibraryPolicy.NormalizeRowCursor(
                query.listStart, query.listCount,
                Math.Min(cap.libraryWindowRows, cap.libraryWindowCeiling),
                query.expectedListSnapshotRevision, matched.Count);
            if (!cursor.valid) return result;
            result.status = MemoryLibraryStatuses.Ready;
            result.totalEligibleRows = eligible.Count;
            result.totalMatchedRows = matched.Count;
            result.ttlValidUntilTickExclusive = MemoryLibraryPolicy.TtlValidUntil(
                nextDayBoundary, snapshot.ownerEarliestFiniteExpiryTickExclusive);
            result.returnedStart = cursor.start;
            result.returnedCount = cursor.returnedCount;
            result.nextStart = cursor.nextStart;
            result.hasPrevious = cursor.hasPrevious;
            result.hasMore = cursor.hasMore;
            result.directoryRevision = directoryRevision;
            result.listSnapshotRevision = listRevision;
            result.ownerStructuralRevision = snapshot.ownerRow.structuralRevision;
            result.ownerStatusRevision = snapshot.ownerRow.statusRevision;
            result.committedSettingsRevision = committedSettingsRevision;
            result.languageDisplayRevision = languageRevision;
            result.ttlDayRevision = ttlDayRevision;
            result.emptyStateToken = eligible.Count == 0
                ? "no_memories" : matched.Count == 0 ? "no_matches" : "none";
            for (int index = 0; index < cursor.returnedCount; index++)
                result.rows.Add(matched[cursor.start + index]);
            return result;
        }

        public static MemoryThreadDetailResult QueryThreadDetail(
            MemoryLibraryOwnerIndexSnapshot snapshot,
            MemoryThreadDetailQuery query,
            long detailRevision,
            long nextDayBoundary,
            MemoryLibraryLimits limits)
        {
            MemoryThreadDetailResult result = new MemoryThreadDetailResult();
            MemoryLibraryLimits cap = limits ?? new MemoryLibraryLimits();
            if (snapshot == null || query?.rootHandle == null || detailRevision <= 0) return result;
            if (query.expectedDetailSnapshotRevision > 0
                && query.expectedDetailSnapshotRevision != detailRevision)
            {
                result.status = MemoryLibraryStatuses.Stale;
                return result;
            }
            MemoryLibraryRootIndexInput root = FindRoot(snapshot.roots, query.rootHandle);
            if (root == null) { result.status = MemoryLibraryStatuses.Missing; return result; }
            string search = MemoryLibraryPolicy.NormalizeSearch(
                query.search, cap.searchScalars, cap.searchUtf16Units);
            bool headerHit = MemoryLibraryPolicy.SearchMatches(
                root.header?.normalizedSearch, search);
            List<MemoryBlockRow> filtered = new List<MemoryBlockRow>();
            bool allSuppressed = root.children.Count > 0;
            for (int index = 0; index < root.children.Count; index++)
            {
                MemoryBlockRow row = root.children[index];
                if (row != null && !row.suppressed) allSuppressed = false;
                if (MemoryLibraryPolicy.TryProjectRow(
                    row, query.filters, search, headerHit, cap, out MemoryBlockRow projected))
                {
                    filtered.Add(projected);
                }
            }
            MemoryLibraryCursorPlan cursor = MemoryLibraryPolicy.NormalizeRowCursor(
                query.detailStart, query.detailCount,
                Math.Min(cap.libraryWindowRows, cap.libraryWindowCeiling),
                query.expectedDetailSnapshotRevision, filtered.Count);
            if (!cursor.valid) return result;
            result.status = MemoryLibraryStatuses.Ready;
            result.header = root.header;
            result.currentStatus = root.currentStatus;
            result.shownManageableCount = filtered.Count;
            result.totalManageableCount = root.children.Count;
            result.allBlocksSuppressedForWriting = allSuppressed;
            result.returnedStart = cursor.start;
            result.returnedCount = cursor.returnedCount;
            result.nextStart = cursor.nextStart;
            result.hasPrevious = cursor.hasPrevious;
            result.hasMore = cursor.hasMore;
            result.detailSnapshotRevision = detailRevision;
            result.ttlValidUntilTickExclusive = MemoryLibraryPolicy.TtlValidUntil(
                nextDayBoundary, root.rootEarliestFiniteExpiryTickExclusive);
            HashSet<string> includedChapters = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < cursor.returnedCount; index++)
            {
                MemoryBlockRow block = filtered[cursor.start + index];
                if (!string.IsNullOrEmpty(block.chapterId)
                    && !includedChapters.Contains(block.chapterId)
                    && includedChapters.Count < cap.chapterHeaderRows)
                {
                    MemoryChapterRow chapter = FindChapter(root.chapters, block.chapterId);
                    if (chapter == null) break;
                    MemoryChapterRow context = CopyChapter(chapter);
                    context.continuedFromPrevious = cursor.start > 0
                        && string.Equals(filtered[cursor.start - 1]?.chapterId,
                            block.chapterId, StringComparison.Ordinal);
                    context.returnedChildStart = result.blocks.Count;
                    context.returnedChildCount = 0;
                    result.chapters.Add(context);
                    includedChapters.Add(block.chapterId);
                }
                if (!string.IsNullOrEmpty(block.chapterId)
                    && !includedChapters.Contains(block.chapterId)) break;
                result.blocks.Add(block);
                if (result.chapters.Count > 0
                    && result.chapters[result.chapters.Count - 1].chapterId == block.chapterId)
                    result.chapters[result.chapters.Count - 1].returnedChildCount++;
            }
            // Header-cap packing can stop early; continuation advances only actual child rows.
            result.returnedCount = result.blocks.Count;
            result.nextStart = checked(result.returnedStart + result.returnedCount);
            result.hasMore = result.nextStart < filtered.Count;
            for (int index = 0; index < result.chapters.Count; index++)
            {
                MemoryChapterRow chapter = result.chapters[index];
                chapter.continuesInNext = result.hasMore
                    && string.Equals(filtered[result.nextStart]?.chapterId,
                        chapter.chapterId, StringComparison.Ordinal);
            }
            return result;
        }

        private static void SelectRoots(
            List<MemoryLibraryRootIndexInput> roots,
            MemoryLibraryFilters filters,
            string search,
            MemoryLibraryLimits limits,
            List<MemoryLibraryListRow> eligible,
            List<MemoryLibraryListRow> matched)
        {
            for (int index = 0; roots != null && index < roots.Count; index++)
            {
                MemoryLibraryRootIndexInput root = roots[index];
                if (root?.header == null) continue;
                bool hasFiltered = false;
                bool childSearch = false;
                bool headerHit = MemoryLibraryPolicy.SearchMatches(
                    root.header.normalizedSearch, search);
                for (int child = 0; child < root.children.Count; child++)
                {
                    MemoryBlockRow row = root.children[child];
                    if (!MemoryLibraryPolicy.TryProjectRow(
                        row, filters, string.Empty, true, limits,
                        out MemoryBlockRow ignored)) continue;
                    hasFiltered = true;
                    if (MemoryLibraryPolicy.TryProjectRow(
                        row, filters, search, headerHit, limits,
                        out ignored))
                        childSearch = true;
                }
                if (!hasFiltered) continue;
                MemoryLibraryListRow item = new MemoryLibraryListRow
                {
                    tag = MemoryLibraryRowTags.Thread,
                    thread = root.header
                };
                eligible.Add(item);
                if (string.IsNullOrEmpty(search)
                    || childSearch || headerHit)
                    matched.Add(item);
            }
        }

        private static void SelectStandalone(
            List<MemoryBlockRow> rows,
            MemoryLibraryFilters filters,
            string search,
            MemoryLibraryLimits limits,
            List<MemoryLibraryListRow> eligible,
            List<MemoryLibraryListRow> matched)
        {
            for (int index = 0; rows != null && index < rows.Count; index++)
            {
                MemoryBlockRow row = rows[index];
                if (!MemoryLibraryPolicy.TryProjectRow(
                    row, filters, string.Empty, true, limits,
                    out MemoryBlockRow eligibleProjection)) continue;
                MemoryLibraryListRow item = new MemoryLibraryListRow
                {
                    tag = MemoryLibraryRowTags.Standalone,
                    standalone = eligibleProjection
                };
                eligible.Add(item);
                if (MemoryLibraryPolicy.TryProjectRow(
                    row, filters, search, false, limits,
                    out MemoryBlockRow matchedProjection))
                    matched.Add(new MemoryLibraryListRow
                    {
                        tag = MemoryLibraryRowTags.Standalone,
                        standalone = matchedProjection
                    });
            }
        }

        private static void SelectImported(
            List<MemoryImportedRow> rows,
            string search,
            List<MemoryLibraryListRow> eligible,
            List<MemoryLibraryListRow> matched)
        {
            for (int index = 0; rows != null && index < rows.Count; index++)
            {
                MemoryImportedRow row = rows[index];
                if (row == null) continue;
                MemoryLibraryListRow item = new MemoryLibraryListRow
                {
                    tag = MemoryLibraryRowTags.Imported,
                    imported = row
                };
                eligible.Add(item);
                if (MemoryLibraryPolicy.SearchMatches(row.normalizedSearch, search)) matched.Add(item);
            }
        }

        private static int CompareOwners(
            MemoryLibraryOwnerRow left,
            MemoryLibraryOwnerRow right,
            string sort)
        {
            int value = sort == "latest"
                ? right.latestActivityTick.CompareTo(left.latestActivityTick)
                : string.Compare(left.displayName, right.displayName,
                    StringComparison.OrdinalIgnoreCase);
            if (value != 0) return value;
            return string.Compare(
                left.primaryHandle?.exactOwnerPawnIdOrEmpty,
                right.primaryHandle?.exactOwnerPawnIdOrEmpty,
                StringComparison.Ordinal);
        }

        private static int CompareListRows(
            MemoryLibraryListRow left,
            MemoryLibraryListRow right,
            string sort)
        {
            long leftTick = RowTick(left);
            long rightTick = RowTick(right);
            int tick = sort == "oldest"
                ? leftTick.CompareTo(rightTick)
                : rightTick.CompareTo(leftTick);
            return tick != 0
                ? tick
                : string.Compare(RowIdentity(left), RowIdentity(right), StringComparison.Ordinal);
        }

        private static long RowTick(MemoryLibraryListRow row)
        {
            if (row?.thread != null) return row.thread.latestActivityTick;
            if (row?.standalone != null) return Math.Max(
                row.standalone.activityTick, row.standalone.originalTick);
            return row?.imported?.originalTick ?? 0;
        }

        private static string RowIdentity(MemoryLibraryListRow row)
        {
            if (row?.thread?.rootHandle != null) return row.thread.rootHandle.rootId ?? string.Empty;
            if (row?.standalone?.recordHandle != null)
                return row.standalone.recordHandle.recordId ?? string.Empty;
            return row?.imported?.archiveHandle?.archiveRecordId ?? string.Empty;
        }

        private static bool SameHandle(
            MemoryLibraryOwnerHandle left,
            MemoryLibraryOwnerHandle right)
        {
            return left != null && right != null
                && string.Equals(left.scopeToken, right.scopeToken, StringComparison.Ordinal)
                && string.Equals(left.exactOwnerPawnIdOrEmpty,
                    right.exactOwnerPawnIdOrEmpty, StringComparison.Ordinal)
                && string.Equals(left.epochTokenOrEmpty,
                    right.epochTokenOrEmpty, StringComparison.Ordinal);
        }

        private static MemoryLibraryRootIndexInput FindRoot(
            List<MemoryLibraryRootIndexInput> roots,
            MemoryRootHandle handle)
        {
            for (int index = 0; roots != null && index < roots.Count; index++)
            {
                MemoryRootHandle candidate = roots[index]?.header?.rootHandle;
                if (candidate != null
                    && candidate.ownerPawnId == handle.ownerPawnId
                    && candidate.epochToken == handle.epochToken
                    && candidate.rootId == handle.rootId) return roots[index];
            }
            return null;
        }

        private static MemoryChapterRow FindChapter(List<MemoryChapterRow> rows, string id)
        {
            for (int index = 0; rows != null && index < rows.Count; index++)
                if (rows[index]?.chapterId == id) return rows[index];
            return null;
        }

        private static MemoryChapterRow CopyChapter(MemoryChapterRow source)
        {
            return new MemoryChapterRow
            {
                chapterId = source.chapterId,
                ordinal = source.ordinal,
                phaseToken = source.phaseToken,
                openedTick = source.openedTick,
                lastActivityTick = source.lastActivityTick,
                closedTick = source.closedTick,
                closureReasonToken = source.closureReasonToken,
                closed = source.closed
            };
        }

        private static int CountNonRolling(List<MemoryBlockRow> rows)
        {
            int result = 0;
            for (int index = 0; rows != null && index < rows.Count; index++)
                if (rows[index] != null && !rows[index].rollingSummary) result++;
            return result;
        }

        private static int HighestImportance(int left, int right)
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

        private static List<T> Copy<T>(List<T> source)
        {
            return source == null ? new List<T>() : new List<T>(source);
        }

        private static void ApplyCursor(
            MemoryLibraryOwnerResult result,
            MemoryLibraryCursorPlan cursor)
        {
            result.returnedStart = cursor.start;
            result.returnedCount = cursor.returnedCount;
            result.nextStart = cursor.nextStart;
            result.hasPrevious = cursor.hasPrevious;
            result.hasMore = cursor.hasMore;
        }
    }
}

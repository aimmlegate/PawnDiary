// Dialog_MemoryLibrary.Rail.cs — subject-first owner strip and persistent navigation rail.
//
// The rail reads detached owner/thread projections prepared in WindowUpdate. Its controls change
// only session navigation and never mutate saved memory during RimWorld's repeated IMGUI passes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    internal sealed partial class Dialog_MemoryLibrary
    {
        private const int ThreadRailSectionRow = 1;
        private const int ThreadRailThreadRow = 2;
        private const int ThreadRailPinnedRow = 3;
        private const int ThreadRailStateRow = 4;

        /// <summary>One cached, variable-height draw row in the returned Threads window.</summary>
        private sealed class MemoryLibraryThreadRailDrawRow
        {
            public int kind;
            public float height;
            public string label = string.Empty;
            public MemoryLibraryUiRailThreadRow thread;
            public MemoryLibraryUiPinnedRow pinned;
        }

        private MemoryLibraryUiRailProjection threadRailProjection =
            new MemoryLibraryUiRailProjection();
        private readonly List<MemoryLibraryThreadRailDrawRow> threadRailDrawRows =
            new List<MemoryLibraryThreadRailDrawRow>();
        private float[] threadRailDrawOffsets = { 0f };
        private readonly Dictionary<string, string> cachedThreadRailTitles =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> cachedThreadRailMetadata =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> cachedThreadRailInitials =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private string threadRailDisplayCacheSignature = string.Empty;
        private string cachedOwnerSubjectTitle = string.Empty;

        private float DrawOwnerBar(Rect rect, float gap)
        {
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            float stripHeight = Mathf.Max(40f, style.memoryLibraryControlHeight + 12f);
            Rect strip = new Rect(rect.x, rect.y, rect.width, stripHeight);
            Widgets.DrawMenuSection(strip);
            Rect inner = strip.ContractedBy(6f);
            float controlHeight = Mathf.Max(24f, style.memoryLibraryControlHeight);
            float changeWidth = Mathf.Clamp(inner.width * 0.16f, 112f, 180f);
            float controlsWidth = Mathf.Clamp(inner.width * 0.36f, 240f,
                Mathf.Max(240f, inner.width - changeWidth - gap - 160f));
            Rect change = new Rect(inner.xMax - changeWidth,
                inner.y + (inner.height - controlHeight) * 0.5f,
                changeWidth, controlHeight);
            Rect controls = new Rect(change.x - gap - controlsWidth,
                change.y, controlsWidth, controlHeight);
            Rect title = new Rect(inner.x, inner.y,
                Mathf.Max(80f, controls.x - inner.x - gap), inner.height);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(title, cachedOwnerSubjectTitle);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            DrawMemoryLibrarySearchRow(controls);
            if (Widgets.ButtonText(change, T("PawnDiary.Memory.Library.ChangeOwner")))
                OpenOwnerMenu();
            return strip.yMax;
        }

        /// <summary>
        /// Draws the persistent sectioned Threads rail plus the count-backed fixed navigation rows.
        /// The shell should call this for the wide left pane instead of DrawListPane.
        /// </summary>
        private void DrawSubjectRail(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(6f);
            bool hasPaging = threadRail?.status == MemoryLibraryStatuses.Ready
                && (threadRail.hasPrevious || threadRail.hasMore);
            float footerHeight = hasPaging
                ? Mathf.Max(24f, DiaryJournalView.UiStyle.memoryLibraryControlHeight)
                : 0f;
            float gap = Mathf.Max(2f, DiaryJournalView.UiStyle.memoryLibraryCardGap);
            Rect scrollOut = new Rect(inner.x, inner.y, inner.width,
                Mathf.Max(0f, inner.height - footerHeight - (hasPaging ? gap : 0f)));
            MemoryLibraryUiVirtualWindow window = MemoryLibraryUiPolicy.Virtualize(
                threadRailDrawOffsets,
                threadRailScroll.y,
                scrollOut.height,
                DiaryJournalView.UiStyle.memoryLibraryOverscanRows,
                DiaryJournalView.UiStyle.memoryLibraryMaximumMaterializedRows);
            Rect view = new Rect(0f, 0f, Mathf.Max(0f, scrollOut.width - 16f),
                Mathf.Max(scrollOut.height, window.contentHeight));
            Widgets.BeginScrollView(scrollOut, ref threadRailScroll, view);
            try
            {
                for (int index = window.firstIndex;
                    index < window.endExclusive && index < threadRailDrawRows.Count;
                    index++)
                {
                    float rowY = threadRailDrawOffsets[index];
                    float rowHeight = threadRailDrawOffsets[index + 1] - rowY;
                    DrawSubjectRailRow(new Rect(0f, rowY, view.width,
                        Mathf.Max(1f, rowHeight - 4f)), threadRailDrawRows[index]);
                }
            }
            finally { Widgets.EndScrollView(); }

            if (hasPaging)
            {
                Rect footer = new Rect(inner.x, scrollOut.yMax + gap, inner.width, footerHeight);
                DrawThreadRailPaging(footer);
            }
        }

        /// <summary>
        /// Draws the narrow root rail and reports whether it consumed the body. The shell can use
        /// <c>if (DrawNarrowSubjectRail(body)) return;</c> before drawing right-pane content.
        /// </summary>
        private bool DrawNarrowSubjectRail(Rect rect)
        {
            if (session.narrowDetailOpen) return false;
            DrawSubjectRail(rect);
            return true;
        }

        /// <summary>
        /// True when the selected right-pane view should show its list rather than a chosen item or
        /// compatibility preview. This keeps archive-only Imported lists and compatibility-only raw
        /// previews distinct without adding a new session or DTO token.
        /// </summary>
        private bool RightPaneShowsContentList()
        {
            if (session.selectedView == MemoryLibraryViews.Standalone)
                return session.selectedRecordHandle == null;
            if (session.selectedView != MemoryLibraryViews.Imported) return false;
            return selectedOwner?.primaryHandle != null
                && session.selectedArchiveHandle == null;
        }

        private void DrawSubjectRailRow(Rect rect, MemoryLibraryThreadRailDrawRow row)
        {
            if (row == null) return;
            if (row.kind == ThreadRailSectionRow)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(rect, row.label ?? string.Empty);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                return;
            }
            if (row.kind == ThreadRailStateRow)
            {
                DrawCenteredState(rect, row.label);
                return;
            }
            if (row.kind == ThreadRailPinnedRow)
            {
                DrawPinnedRailRow(rect, row);
                return;
            }
            DrawThreadRailRow(rect, row.thread);
        }

        private void DrawThreadRailRow(Rect rect, MemoryLibraryUiRailThreadRow projected)
        {
            MemoryThreadHeaderRow thread = projected?.thread;
            if (thread?.rootHandle == null) return;
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            bool selected = session.selectedView == MemoryLibraryViews.Threads
                && Same(thread.rootHandle, session.selectedRootHandle);
            Widgets.DrawBoxSolid(rect, selected
                ? style.MemoryLibrarySelectedCardBackground
                : style.MemoryLibraryCardBackground);
            Widgets.DrawHighlightIfMouseover(rect);
            Rect inner = rect.ContractedBy(7f);
            float avatarSize = Mathf.Min(Mathf.Max(26f, style.memoryLibraryRailAvatarSize),
                Mathf.Max(26f, inner.height));
            Rect avatar = new Rect(inner.x, inner.y + (inner.height - avatarSize) * 0.5f,
                avatarSize, avatarSize);
            string key = ThreadRailKey(thread.rootHandle);
            cachedThreadRailInitials.TryGetValue(key, out string initials);
            DrawThreadAvatar(avatar, thread.subjectTypeToken, initials);
            Rect text = new Rect(avatar.xMax + 8f, inner.y,
                Mathf.Max(20f, inner.xMax - avatar.xMax - 12f), inner.height);
            cachedThreadRailTitles.TryGetValue(key, out string title);
            cachedThreadRailMetadata.TryGetValue(key, out string metadata);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(text.x, text.y, text.width, 24f), title ?? string.Empty);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(text.x, text.y + 25f, text.width,
                Mathf.Max(18f, text.height - 25f)), metadata ?? string.Empty);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            if (projected.hasAttention)
            {
                float dot = Mathf.Max(4f, style.memoryLibraryAttentionDotSize);
                Widgets.DrawBoxSolid(new Rect(rect.xMax - dot - 6f, rect.y + 6f, dot, dot),
                    style.MemoryLibraryAttentionDotColor);
            }
            if (Widgets.ButtonInvisible(rect)) SelectThreadRailRow(thread);
        }

        private void DrawPinnedRailRow(Rect rect, MemoryLibraryThreadRailDrawRow row)
        {
            MemoryLibraryUiPinnedRow pinned = row?.pinned;
            if (pinned == null) return;
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            bool selected = string.Equals(
                session.selectedView, pinned.viewToken, StringComparison.Ordinal);
            Widgets.DrawBoxSolid(rect, selected
                ? style.MemoryLibrarySelectedCardBackground
                : style.MemoryLibraryCardBackground);
            Widgets.DrawHighlightIfMouseover(rect);
            Rect inner = rect.ContractedBy(7f);
            float avatarSize = Mathf.Min(Mathf.Max(26f, style.memoryLibraryRailAvatarSize),
                Mathf.Max(26f, inner.height));
            Rect avatar = new Rect(inner.x, inner.y + (inner.height - avatarSize) * 0.5f,
                avatarSize, avatarSize);
            DrawGlyphAvatar(avatar,
                DiaryButtonTextures.Memory,
                style.MemoryLibraryCategoryMixedColor);
            Rect text = new Rect(avatar.xMax + 8f, inner.y,
                Mathf.Max(20f, inner.xMax - avatar.xMax - 8f), inner.height);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(text.x, text.y, text.width, 24f), row.label);
            if (pinned.viewToken == MemoryLibraryUiViews.Culture)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(text.x, text.y + 25f, text.width,
                    Mathf.Max(18f, text.height - 25f)), cachedCultureOrigin);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
            else if (pinned.count > 0)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(text.x, text.y + 25f, text.width,
                    Mathf.Max(18f, text.height - 25f)),
                    T("PawnDiary.Memory.Library.Rail.PinnedCount", pinned.count));
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
            if (Widgets.ButtonInvisible(rect)) SelectPinnedRailRow(pinned.viewToken);
        }

        private void DrawThreadRailPaging(Rect rect)
        {
            float buttonWidth = Mathf.Clamp(rect.width * 0.25f, 62f, 88f);
            Rect previous = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect next = new Rect(rect.xMax - buttonWidth, rect.y, buttonWidth, rect.height);
            bool ready = threadRail?.status == MemoryLibraryStatuses.Ready;
            if (Widgets.ButtonText(previous, T("PawnDiary.Memory.Library.Previous"),
                    true, true, ready && threadRail.hasPrevious))
            {
                threadRailStart = Math.Max(0, threadRail.returnedStart - PageSize);
                threadRailExpectedSnapshotRevision = threadRail.listSnapshotRevision;
                threadRailScroll = Vector2.zero;
                repositoryNavigationDirty = true;
            }
            if (Widgets.ButtonText(next, T("PawnDiary.Memory.Library.Next"),
                    true, true, ready && threadRail.hasMore))
            {
                threadRailStart = threadRail.nextStart;
                threadRailExpectedSnapshotRevision = threadRail.listSnapshotRevision;
                threadRailScroll = Vector2.zero;
                repositoryNavigationDirty = true;
            }
            if (!ready) return;
            int first = threadRail.returnedCount > 0 ? threadRail.returnedStart + 1 : 0;
            int last = threadRail.returnedStart + threadRail.returnedCount;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(previous.xMax + 2f, rect.y,
                Mathf.Max(0f, next.x - previous.xMax - 4f), rect.height),
                T("PawnDiary.Memory.Library.Showing",
                    first, last, threadRail.totalMatchedRows));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void SelectThreadRailRow(MemoryThreadHeaderRow thread)
        {
            if (thread?.rootHandle == null) return;
            bool viewChanged = session.selectedView != MemoryLibraryViews.Threads;
            bool rootChanged = !Same(thread.rootHandle, session.selectedRootHandle);
            if (viewChanged)
            {
                session.SelectView(MemoryLibraryViews.Threads);
                // Only the right-pane content stream becomes idle. The rail cursor/revision/scroll
                // are intentionally untouched by its own selection.
                ResetContentListQuery();
            }
            session.selectedRootHandle = MemoryLibraryUiPolicy.Copy(thread.rootHandle);
            session.selectedRecordHandle = null;
            session.selectedArchiveHandle = null;
            session.selectedPlacementToken = string.Empty;
            session.narrowDetailOpen = true;
            session.editDraft = null;
            session.feedbackStatus = string.Empty;
            selectedBlockRow = null;
            selectedImportedRow = null;
            diagnosticsExpanded = false;
            if (viewChanged || rootChanged) ResetDetailQuery();
        }

        private void SelectPinnedRailRow(string view)
        {
            if (view != MemoryLibraryViews.Standalone && view != MemoryLibraryViews.Imported
                && view != MemoryLibraryUiViews.Culture)
                return;
            if (session.selectedView != view)
            {
                session.SelectView(view);
                selectedBlockRow = null;
                selectedImportedRow = null;
                session.feedbackStatus = string.Empty;
                // A pinned-view change owns only the right-pane list/detail lifecycle. The Threads
                // rail remains on the exact page the player was browsing.
                ResetContentListQuery();
            }
            else if (session.selectedRecordHandle != null
                || session.selectedArchiveHandle != null)
            {
                // Re-entering an already-selected pin from the narrow rail means "show this
                // section", not "reopen the last card". Clear only the section-owned detail.
                session.selectedRecordHandle = null;
                session.selectedArchiveHandle = null;
                session.selectedPlacementToken = string.Empty;
                session.editDraft = null;
                session.feedbackStatus = string.Empty;
                selectedBlockRow = null;
                selectedImportedRow = null;
                diagnosticsExpanded = false;
                ResetDetailQuery();
            }
            session.narrowDetailOpen = true;
        }

        /// <summary>
        /// Gives a newly selected owner useful right-pane content without mutating selection from an
        /// IMGUI draw pass. Explicit thread, independent-memory, archive, and culture choices remain.
        /// </summary>
        private void ReconcileSubjectRailSelection()
        {
            if (session.selectedView != MemoryLibraryViews.Threads
                || session.selectedRootHandle != null
                || threadRail?.status != MemoryLibraryStatuses.Ready) return;
            MemoryLibraryUiRailProjection rail = MemoryLibraryUiPolicy.ComposeRail(
                threadRail.rows, selectedOwner);
            MemoryLibraryUiRailSelection selection =
                MemoryLibraryUiPolicy.DefaultRailSelection(rail);
            if (selection == null) return;
            if (selection.viewToken == MemoryLibraryViews.Threads
                && selection.rootHandle != null)
            {
                session.selectedRootHandle = MemoryLibraryUiPolicy.Copy(selection.rootHandle);
                session.narrowDetailOpen = false;
                ResetDetailQuery();
                return;
            }
            session.SelectView(selection.viewToken);
            session.narrowDetailOpen = false;
            ResetContentListQuery();
        }

        /// <summary>
        /// Rebuilds all translated rail labels and its variable-height row plan during WindowUpdate.
        /// Draw passes only read these detached caches and the already-composed projection.
        /// </summary>
        private void RefreshThreadRailDisplayCaches()
        {
            string signature = string.Join("|",
                OwnerKey(session.selectedOwnerHandle),
                OwnerKey(selectedOwner?.compatibilityHandle),
                selectedOwner?.displayName ?? string.Empty,
                selectedOwner?.lifecycleToken ?? string.Empty,
                selectedOwner?.structuralRevision ?? 0,
                selectedOwner?.statusRevision ?? 0,
                selectedOwner?.threadCount ?? 0,
                selectedOwner?.standaloneCount ?? 0,
                selectedOwner?.importedCount ?? 0,
                selectedOwner?.compatibilitySourcePayloadRevision ?? 0,
                threadRail?.status ?? string.Empty,
                threadRail?.listSnapshotRevision ?? 0,
                threadRail?.returnedStart ?? 0,
                threadRail?.returnedCount ?? 0,
                session.selectedView ?? string.Empty,
                detachedNowTick / 60000L,
                LanguageDatabase.activeLanguage?.GetHashCode() ?? 0);
            if (string.Equals(
                    threadRailDisplayCacheSignature, signature, StringComparison.Ordinal)) return;
            ClearThreadRailPresentationCaches();
            threadRailDisplayCacheSignature = signature;

            string ownerName = EmptyFallback(session.selectedOwnerDisplayName,
                T("PawnDiary.Memory.Library.SelectOwner"));
            cachedOwnerSubjectTitle = T("PawnDiary.Memory.Library.SubjectTitle", ownerName);

            List<MemoryLibraryListRow> rows = threadRail?.status == MemoryLibraryStatuses.Ready
                ? threadRail.rows : null;
            threadRailProjection = MemoryLibraryUiPolicy.ComposeRail(rows, selectedOwner);
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            float sectionHeight = Mathf.Max(18f, style.memoryLibraryRailSectionHeaderHeight);
            float rowHeight = Mathf.Max(46f, style.memoryLibraryRailRowHeight);

            AddThreadRailSection(
                T("PawnDiary.Memory.Library.Rail.People"),
                threadRailProjection.people,
                sectionHeight,
                rowHeight);
            AddThreadRailSection(
                T("PawnDiary.Memory.Library.Rail.FactionsStories"),
                threadRailProjection.factionsAndStories,
                sectionHeight,
                rowHeight);
            if (threadRailProjection.people.Count == 0
                && threadRailProjection.factionsAndStories.Count == 0
                && MemoryLibraryUiPolicy.HasActiveViews(selectedOwner))
            {
                threadRailDrawRows.Add(new MemoryLibraryThreadRailDrawRow
                {
                    kind = ThreadRailStateRow,
                    height = rowHeight,
                    label = threadRail?.status == MemoryLibraryStatuses.Ready
                        ? T("PawnDiary.Memory.Library.Rail.Empty")
                        : QueryStateText(threadRail?.status)
                });
            }
            for (int index = 0; index < threadRailProjection.pinned.Count; index++)
            {
                MemoryLibraryUiPinnedRow pinned = threadRailProjection.pinned[index];
                threadRailDrawRows.Add(new MemoryLibraryThreadRailDrawRow
                {
                    kind = ThreadRailPinnedRow,
                    height = rowHeight,
                    pinned = pinned,
                    label = pinned.viewToken == MemoryLibraryUiViews.Culture
                        ? T("PawnDiary.Memory.Library.CulturalContext")
                        : pinned.viewToken == MemoryLibraryViews.Imported
                            ? T("PawnDiary.Memory.Library.Rail.OldRecords")
                            : T("PawnDiary.Memory.Library.Rail.OtherMemories")
                });
            }
            threadRailDrawOffsets = new float[threadRailDrawRows.Count + 1];
            for (int index = 0; index < threadRailDrawRows.Count; index++)
                threadRailDrawOffsets[index + 1] = threadRailDrawOffsets[index]
                    + Mathf.Max(1f, threadRailDrawRows[index].height);
        }

        private void AddThreadRailSection(
            string label,
            List<MemoryLibraryUiRailThreadRow> rows,
            float sectionHeight,
            float rowHeight)
        {
            if (rows == null || rows.Count == 0) return;
            threadRailDrawRows.Add(new MemoryLibraryThreadRailDrawRow
            {
                kind = ThreadRailSectionRow,
                height = sectionHeight,
                label = label ?? string.Empty
            });
            for (int index = 0; index < rows.Count; index++)
            {
                MemoryLibraryUiRailThreadRow projected = rows[index];
                MemoryThreadHeaderRow thread = projected?.thread;
                if (thread?.rootHandle == null) continue;
                string key = ThreadRailKey(thread.rootHandle);
                string title = EmptyFallback(thread.subjectLabel,
                    T("PawnDiary.Memory.Library.UnknownSubject"));
                cachedThreadRailTitles[key] = title;
                cachedThreadRailInitials[key] = Initials(title);
                if (thread.latestActivityTick >= 0)
                {
                    CacheDate(thread.latestActivityTick);
                    cachedThreadRailMetadata[key] = T(
                        "PawnDiary.Memory.Library.Rail.ThreadMeta",
                        thread.manageableMemoryCount,
                        DateLabel(thread.latestActivityTick));
                }
                else
                {
                    cachedThreadRailMetadata[key] = T(
                        "PawnDiary.Memory.Library.Rail.ThreadMetaNoDate",
                        thread.manageableMemoryCount);
                }
                threadRailDrawRows.Add(new MemoryLibraryThreadRailDrawRow
                {
                    kind = ThreadRailThreadRow,
                    height = rowHeight,
                    thread = projected
                });
            }
        }

        private void ClearThreadRailPresentationCaches()
        {
            threadRailProjection = new MemoryLibraryUiRailProjection();
            threadRailDrawRows.Clear();
            threadRailDrawOffsets = new[] { 0f };
            cachedThreadRailTitles.Clear();
            cachedThreadRailMetadata.Clear();
            cachedThreadRailInitials.Clear();
            threadRailDisplayCacheSignature = string.Empty;
            cachedOwnerSubjectTitle = string.Empty;
        }

        private static void DrawThreadAvatar(Rect rect, string subjectType, string initials)
        {
            DiaryUiStyleDef style = DiaryJournalView.UiStyle;
            if (string.Equals(subjectType, "Person", StringComparison.OrdinalIgnoreCase))
            {
                DrawInitialAvatar(rect, initials, style.MemoryLibraryCategoryPersonalColor);
                return;
            }
            if (string.Equals(subjectType, "Faction", StringComparison.OrdinalIgnoreCase))
            {
                DrawInitialAvatar(rect, initials, style.MemoryLibraryCategoryFactionsColor);
                return;
            }
            DrawInitialAvatar(rect, initials, style.MemoryLibraryCategoryMixedColor);
        }

        private static void DrawInitialAvatar(Rect rect, string initials, Color accent)
        {
            Widgets.DrawBoxSolid(rect, new Color(accent.r, accent.g, accent.b, 0.32f));
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            GUI.color = accent;
            Widgets.Label(rect, string.IsNullOrEmpty(initials) ? "?" : initials);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawGlyphAvatar(Rect rect, Texture2D glyph, Color accent)
        {
            Widgets.DrawBoxSolid(rect, new Color(accent.r, accent.g, accent.b, 0.24f));
            if (glyph == null) return;
            Color prior = GUI.color;
            GUI.color = accent;
            GUI.DrawTexture(rect.ContractedBy(Mathf.Max(4f, rect.width * 0.20f)), glyph,
                ScaleMode.ScaleToFit, true);
            GUI.color = prior;
        }

        private static string Initials(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "?";
            string repaired = MemoryLibraryPolicy.RepairMalformedUtf16(value).Trim();
            string[] words = repaired.Split((char[])null,
                StringSplitOptions.RemoveEmptyEntries);
            StringBuilder initials = new StringBuilder(4);
            for (int index = 0; index < words.Length && initials.Length < 2; index++)
            {
                string element = StringInfo.GetNextTextElement(words[index]);
                if (!string.IsNullOrEmpty(element)) initials.Append(element.ToUpperInvariant());
            }
            return initials.Length == 0 ? "?" : initials.ToString();
        }

        private static string ThreadRailKey(MemoryRootHandle handle)
        {
            return handle == null ? string.Empty : (handle.ownerPawnId ?? string.Empty) + "\n"
                + (handle.epochToken ?? string.Empty) + "\n" + (handle.rootId ?? string.Empty);
        }

        private bool HasExactSelectedOwner()
        {
            return !string.IsNullOrWhiteSpace(
                selectedOwner?.primaryHandle?.exactOwnerPawnIdOrEmpty)
                || !string.IsNullOrWhiteSpace(
                    selectedOwner?.compatibilityHandle?.exactOwnerPawnIdOrEmpty);
        }

        private void OpenOwnerMenu()
        {
            if (owners?.rows == null || owners.rows.Count == 0) return;
            long capturedDirectoryRevision = owners.directoryRevision;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (owners.hasPrevious)
                options.Add(new FloatMenuOption(
                    T("PawnDiary.Memory.Library.Previous"),
                    delegate
                    {
                        ownerStart = Math.Max(0, owners.returnedStart - PageSize);
                        ownerExpectedDirectoryRevision = capturedDirectoryRevision;
                        repositoryNavigationDirty = true;
                    }));
            for (int index = 0; index < owners.rows.Count; index++)
            {
                MemoryLibraryOwnerRow captured = owners.rows[index];
                if (captured == null) continue;
                options.Add(new FloatMenuOption(
                    T("PawnDiary.Memory.Library.OwnerMenuRow",
                        EmptyFallback(captured.displayName,
                            T("PawnDiary.Memory.Library.UnknownOwner")),
                        LifecycleLabel(captured.lifecycleToken), captured.threadCount,
                        captured.standaloneCount, captured.importedCount),
                    delegate
                    {
                        if (!MemoryLibraryUiPolicy.CanApplyOwnerMenuOption(
                                capturedDirectoryRevision, owners))
                        {
                            // WindowUpdate may republish the directory while this menu is open.
                            // Never bless its captured row with the newer revision.
                            ownerStart = 0;
                            ownerExpectedDirectoryRevision = 0;
                            selectedOwnerValidatedDirectoryRevision = 0;
                            ResetSelectedOwnerValidation();
                            repositoryNavigationDirty = true;
                            return;
                        }
                        string before = OwnerKey(session.selectedOwnerHandle);
                        session.SelectOwner(captured);
                        selectedOwner = captured;
                        preferredOwnerId = string.Empty;
                        ownerWalkHandle = null;
                        preferredFallback = null;
                        ownerStart = 0;
                        ownerExpectedDirectoryRevision = capturedDirectoryRevision;
                        ResetSelectedOwnerValidation();
                        if (!string.Equals(before, OwnerKey(session.selectedOwnerHandle),
                                StringComparison.Ordinal)) ResetOwnerQueries(captured);
                        selectedOwnerValidatedDirectoryRevision = capturedDirectoryRevision;
                    }));
            }
            if (owners.hasMore)
                options.Add(new FloatMenuOption(
                    T("PawnDiary.Memory.Library.Next"),
                    delegate
                    {
                        ownerStart = owners.nextStart;
                        ownerExpectedDirectoryRevision = capturedDirectoryRevision;
                        repositoryNavigationDirty = true;
                    }));
            if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>Draws the selected owner's saved cultural context as a rail destination.</summary>
        private void DrawCultureContextDetail(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 30f), cachedCultureTitle);
            Text.Font = GameFont.Small;
            float y = rect.y + 38f;
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f), cachedCultureOrigin);
            y += 28f;
            if (cachedCultureHasAdopted)
            {
                Widgets.Label(new Rect(rect.x, y, rect.width, 24f), cachedCultureAdopted);
                y += 28f;
            }
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x, y, rect.width,
                Mathf.Max(22f, rect.yMax - y)),
                cachedCultureExplanation);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private string OwnerEmptyText()
        {
            if (!string.IsNullOrWhiteSpace(preferredOwnerId)
                || owners?.status == MemoryLibraryStatuses.Preparing)
                return T("PawnDiary.Memory.Library.Preparing");
            return owners?.ownerEmptyStateToken == "no_matches"
                ? T("PawnDiary.Memory.Library.NoOwnerMatches")
                : T("PawnDiary.Memory.Library.NoOwners");
        }

        private static string CultureLine(string state, string label, string provenance)
        {
            string display = CultureStateLabel(state, label, true);
            return provenance == "none" || string.IsNullOrWhiteSpace(provenance)
                ? T("PawnDiary.Memory.Library.CultureOriginNoProvenance", display)
                : T("PawnDiary.Memory.Library.CultureOrigin", display,
                    CultureProvenanceLabel(provenance));
        }

        private static string LifecycleLabel(string token)
        {
            if (token == "active") return T("PawnDiary.Memory.Library.OwnerActive");
            if (token == "archive" || token == "saved")
                return T("PawnDiary.Memory.Library.OwnerDeparted");
            if (token == "migration_pending")
                return T("PawnDiary.Memory.Library.OwnerMigrationPending");
            return T("PawnDiary.Memory.Library.OwnerUnknown");
        }

        private static string CultureStateLabel(string state, string label, bool origin)
        {
            if (!string.IsNullOrWhiteSpace(label)) return label;
            if (state == "none") return origin
                ? T("PawnDiary.Memory.Library.CultureUnknown")
                : T("PawnDiary.Memory.Library.CultureNone");
            if (state == "recorded" || state == "resolved")
                return T("PawnDiary.Memory.Library.CultureRecorded");
            if (state == "inferred") return T("PawnDiary.Memory.Library.CultureInferred");
            if (state == "unavailable") return T("PawnDiary.Memory.Library.CultureUnavailable");
            return origin ? T("PawnDiary.Memory.Library.CultureUnknown")
                : T("PawnDiary.Memory.Library.CultureNone");
        }

        private static string CultureProvenanceLabel(string token)
        {
            if (token == "recorded" || token == "captured")
                return T("PawnDiary.Memory.Library.CultureRecorded");
            if (token == "inferred") return T("PawnDiary.Memory.Library.CultureInferred");
            if (token == "unknown") return T("PawnDiary.Memory.Library.CultureUnknown");
            return token == "none"
                ? T("PawnDiary.Memory.Library.CultureNone")
                : T("PawnDiary.Memory.Library.CultureUnavailable");
        }
    }
}

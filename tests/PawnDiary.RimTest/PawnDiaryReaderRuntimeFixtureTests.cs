// Loaded-game coverage for the standalone Diary reader's impure edges.
//
// The ordering/search rules and Markdown formatter are plain C# and have standalone tests. This suite
// covers the half those tests cannot reach: merging saved diary rows with RimWorld's live-colonist
// roster, aggregating real DiaryEvent lifecycle state into reader badges, exposing the reader through
// the loaded MainButtonDef and patched pawn gizmos, and exporting the component's real year-index view
// to a temporary Markdown file. It deliberately does not call immediate-mode drawing methods; pixel
// layout, scrolling, UI scale, and translated-string fit remain visual checks.
//
// Every pawn and diary row belongs to PawnDiaryRimTestScope. The filesystem test arms a unique-marker
// sweep before export (plus exact-path deletion after success), and the window/gizmo tests restore the
// developer's exact open-window, selection, and UI-host state even when an assertion fails.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the loaded reader directory, status cache, entry-point wiring, and Markdown adapter
    /// consume real component and RimWorld state without opening or drawing a Window.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryReaderRuntimeFixtureTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string ReaderButtonDefName = "PawnDiary_DiaryReader";
        private const string TestDefName = "PawnDiary_RimTest_Reader";
        private static readonly MethodInfo RebuildCommandStatusCacheMethod =
            typeof(DiaryGameComponent).GetMethod("RebuildCommandStatusCache", PrivateInstance);
        private static readonly FieldInfo ReaderSelectedSubjectField =
            typeof(Dialog_DiaryReader).GetField("selectedSubject", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn firstPawn;
        private static Pawn secondPawn;

        /// <summary>Creates two isolated, generation-disabled reader subjects.</summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            firstPawn = scope.CreateAdultColonist();
            secondPawn = scope.CreateAdultColonist();
        }

        /// <summary>Restores all component, pawn, selection, setting, and temporary-file state.</summary>
        [AfterEach]
        public static void TearDown()
        {
            try
            {
                scope?.TearDown();
            }
            finally
            {
                scope = null;
                firstPawn = null;
                secondPawn = null;
            }
        }

        /// <summary>
        /// A current live colonist is listed even with zero pages, while a saved but unresolved subject
        /// with one page is retained under the departed section with its cached name.
        /// </summary>
        [Test]
        public static void DirectoryMergesLiveZeroPageAndSavedDepartedSubjects()
        {
            scope.SpawnAsLiveColonist(firstPawn);
            DiaryEvent departedPage = RecordSolo(secondPawn, "A remembered page.");
            departedPage.MarkInjectedTextComplete(
                DiaryEvent.InitiatorRole,
                "A page belonging to someone no longer in the active colony.");

            DiaryReaderPawnDirectory directory = new DiaryReaderPawnDirectory();
            directory.RefreshIfNeeded(
                scope.Component,
                "unknown pawn",
                DiaryReaderSortMode.LivingDeparted,
                string.Empty,
                true);

            DiaryReaderPawnRow live = RequireRow(directory.Rows, firstPawn.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(
                live.Subject.Pawn == firstPawn && !live.Departed && live.EntryCount == 0,
                "The current live test colonist should be listed as living with zero pages.");

            DiaryReaderPawnRow departed = RequireRow(directory.Rows, secondPawn.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(
                departed.Departed && departed.EntryCount == 1,
                "The saved unspawned subject should be retained as one departed-page row.");
            PawnDiaryRimTestScope.Require(
                string.Equals(
                    departed.Subject.DisplayName,
                    scope.RequireDiaryRecord(secondPawn).pawnName,
                    StringComparison.Ordinal),
                "The unresolved departed row did not fall back to its saved diary name.");
            PawnDiaryRimTestScope.Require(
                directory.EligibleRowCount >= 2 && directory.GroupedByDeparture,
                "The reader directory did not expose its living/departed grouping metadata.");
        }

        /// <summary>
        /// ReaderStatusForId and GlobalReaderStatus combine saved unread counts with live pending
        /// slots while keeping failed generation diagnostics out of player-facing badges and
        /// latest-page metadata.
        /// </summary>
        [Test]
        public static void ReaderActivityAggregatesAndInvalidatesLifecycleState()
        {
            PawnDiaryRecord record = scope.RequireDiaryRecord(firstPawn);
            record.hasUnreadGeneratedEntry = true;
            record.unreadGeneratedEntryCount = 2;
            PawnDiaryRimTestScope.Require(
                RebuildCommandStatusCacheMethod != null,
                "Pawn Diary RimTest could not locate RebuildCommandStatusCache.");
            RebuildCommandStatusCacheMethod.Invoke(scope.Component, null);

            DiaryEvent completed = RecordSolo(firstPawn, "A completed raw page.");
            completed.tick = 10;
            completed.date = "1st of Aprimay, 5500";
            completed.MarkInjectedTextComplete(DiaryEvent.InitiatorRole, "A completed reader page.");

            DiaryEvent pending = RecordSolo(firstPawn, "A pending raw page.");
            pending.tick = 20;
            pending.date = "2nd of Aprimay, 5500";
            pending.MarkQueued(DiaryEvent.InitiatorRole);

            DiaryEvent failed = RecordSolo(firstPawn, "A failed raw page.");
            failed.tick = 30;
            failed.date = "3rd of Aprimay, 5500";
            failed.MarkFailed(DiaryEvent.InitiatorRole, "fixture failure");

            DiaryGameComponent.DiaryCommandStatus perPawn =
                scope.Component.ReaderStatusForId(firstPawn.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(
                perPawn.unacknowledgedCount == 2
                    && perPawn.pendingCount == 1
                    && perPawn.failedCount == 0
                    && perPawn.HasNewPages
                    && perPawn.IsWriting
                    && !perPawn.HasFailures,
                "The per-pawn reader status exposed failed generation diagnostics.");

            DiaryGameComponent.DiaryCommandStatus global = scope.Component.GlobalReaderStatus();
            PawnDiaryRimTestScope.Require(
                global.unacknowledgedCount >= 2
                    && global.pendingCount >= 1
                    && global.failedCount == 0,
                "The global reader status exposed failed generation diagnostics.");

            List<DiaryGameComponent.DiaryReaderPawnInfo> rows =
                new List<DiaryGameComponent.DiaryReaderPawnInfo>();
            scope.Component.CollectDiaryReaderPawns(rows);
            DiaryGameComponent.DiaryReaderPawnInfo info =
                RequireInfo(rows, firstPawn.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(
                info.hotEntryCount == 3
                    && info.unreadCount == 2
                    && info.hasLatestEntry
                    && info.latestEntryTick == 10
                    && string.Equals(info.latestEntryDate, completed.date, StringComparison.Ordinal),
                "The reader projection treated a failed request as the latest readable page.");

            pending.MarkInjectedTextComplete(DiaryEvent.InitiatorRole, "The pending page finished.");
            failed.MarkInjectedTextComplete(DiaryEvent.InitiatorRole, "The failed page was regenerated.");
            DiaryGameComponent.DiaryCommandStatus settled =
                scope.Component.ReaderStatusForId(firstPawn.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(
                settled.pendingCount == 0 && settled.failedCount == 0,
                "DiaryStateVersion did not invalidate the reader activity cache after completion.");
        }

        /// <summary>
        /// The shipped MainButtonDef resolves the intended worker and its visibility follows the saved
        /// reader-host toggle. Classic mode keeps the bottom-menu entry hidden.
        /// </summary>
        [Test]
        public static void ReaderMainButtonWiringAndVisibilityFollowMode()
        {
            PawnDiarySettings settings = RequireSettings();
            bool originalReaderMode = settings.useDiaryReaderWindow;
            scope.RegisterCleanup(() => settings.useDiaryReaderWindow = originalReaderMode);

            MainButtonDef def = DefDatabase<MainButtonDef>.GetNamedSilentFail(ReaderButtonDefName);
            PawnDiaryRimTestScope.Require(
                def != null && def.Worker is MainButtonWorker_DiaryReader,
                "The loaded Diary reader MainButtonDef did not instantiate MainButtonWorker_DiaryReader.");

            settings.useDiaryReaderWindow = false;
            PawnDiaryRimTestScope.Require(
                !DiaryUiRouter.ReaderWindowMode && !def.Worker.Visible,
                "Classic inspect-tab mode should hide the standalone Diary main button.");

            settings.useDiaryReaderWindow = true;
            PawnDiaryRimTestScope.Require(
                DiaryUiRouter.ReaderWindowMode && def.Worker.Visible,
                "Reader-window mode should expose the standalone Diary main button in a loaded game.");
            PawnDiaryRimTestScope.Require(
                !DiaryUiRouter.OpenDiaryFor(null) && !DiaryUiRouter.OpenDiaryAt(null, "unused"),
                "Diary UI routing should reject a null subject without mutating window state.");
        }

        /// <summary>
        /// The loaded main-button worker opens one real reader Window seeded from selection, routing a
        /// second pawn focuses that same singleton, and activating the button again closes it cleanly.
        /// No immediate-mode draw callback is invoked.
        /// </summary>
        [Test]
        public static void MainButtonWorkerOpensFocusesAndClosesSingletonReader()
        {
            scope.SpawnAsLiveColonist(firstPawn);
            scope.SpawnAsLiveColonist(secondPawn);
            PawnDiaryRimTestScope.Require(
                Find.WindowStack != null && CurrentReaderWindow() == null,
                "Close the standalone Diary reader before running its window-lifecycle fixture.");
            PawnDiaryRimTestScope.Require(
                ReaderSelectedSubjectField != null,
                "Pawn Diary RimTest could not locate Dialog_DiaryReader.selectedSubject.");

            PawnDiarySettings settings = RequireSettings();
            bool originalReaderMode = settings.useDiaryReaderWindow;
            scope.RegisterCleanup(() => settings.useDiaryReaderWindow = originalReaderMode);
            settings.useDiaryReaderWindow = true;
            SnapshotAndOwnSelection();
            Find.Selector.ClearSelection();
            Find.Selector.Select(firstPawn, true, false);

            MainButtonDef def = DefDatabase<MainButtonDef>.GetNamedSilentFail(ReaderButtonDefName);
            PawnDiaryRimTestScope.Require(
                def != null && def.Worker is MainButtonWorker_DiaryReader,
                "The loaded reader MainButtonDef could not supply its production worker.");
            // Arm teardown before crossing WindowStack.Add. If production throws after adding a window,
            // or accidentally creates more than one, every reader is still test-owned because the
            // precondition above proved the stack began with none.
            scope.RegisterCleanup(CloseAllReaderWindows);
            def.Worker.Activate();
            Dialog_DiaryReader opened = CurrentReaderWindow();
            PawnDiaryRimTestScope.Require(
                opened != null
                    && SelectedSubject(opened).PawnId == firstPawn.GetUniqueLoadID(),
                "The main-button worker did not seed one reader from the selected pawn.");

            PawnDiaryRimTestScope.Require(
                DiaryUiRouter.OpenDiaryFor(secondPawn),
                "Routing a second live pawn into the open reader failed.");
            PawnDiaryRimTestScope.Require(
                ReferenceEquals(CurrentReaderWindow(), opened)
                    && SelectedSubject(opened).PawnId == secondPawn.GetUniqueLoadID(),
                "Routing a second pawn created another reader or failed to focus the singleton.");

            def.Worker.Activate();
            PawnDiaryRimTestScope.Require(
                CurrentReaderWindow() == null,
                "Activating the loaded reader main button twice did not close its singleton Window.");
        }

        /// <summary>
        /// The real Harmony-patched Pawn.GetGizmos route adds exactly one Diary command only in classic
        /// command mode with one eligible pawn selected. Inspect-tab mode, reader mode, and multi-select
        /// each suppress the redundant command.
        /// </summary>
        [Test]
        public static void PatchedPawnGizmoHonorsHostModeAndSingleSelection()
        {
            scope.SpawnAsLiveColonist(firstPawn);
            scope.SpawnAsLiveColonist(secondPawn);

            PawnDiarySettings settings = RequireSettings();
            bool originalShowTab = settings.showDiaryInspectTab;
            bool originalReaderMode = settings.useDiaryReaderWindow;
            scope.RegisterCleanup(() =>
            {
                settings.showDiaryInspectTab = originalShowTab;
                settings.useDiaryReaderWindow = originalReaderMode;
            });
            SnapshotAndOwnSelection();

            settings.showDiaryInspectTab = false;
            settings.useDiaryReaderWindow = false;
            Find.Selector.ClearSelection();
            Find.Selector.Select(firstPawn, true, false);
            PawnDiaryRimTestScope.Require(
                CountDiaryCommands(firstPawn) == 1,
                "A singly selected eligible pawn in classic command mode should expose one Diary gizmo.");

            settings.showDiaryInspectTab = true;
            PawnDiaryRimTestScope.Require(
                CountDiaryCommands(firstPawn) == 0,
                "The visible inspect tab should suppress the redundant Diary gizmo.");

            settings.showDiaryInspectTab = false;
            settings.useDiaryReaderWindow = true;
            PawnDiaryRimTestScope.Require(
                CountDiaryCommands(firstPawn) == 0,
                "The standalone reader mode should suppress the pawn Diary gizmo.");

            settings.useDiaryReaderWindow = false;
            Find.Selector.Select(secondPawn, true, false);
            PawnDiaryRimTestScope.Require(
                CountDiaryCommands(firstPawn) == 0,
                "The Diary gizmo must stay single-select only.");
        }

        /// <summary>
        /// The player-facing export adapter walks the loaded component's normal visibility index, writes
        /// only completed prose, reports its page count, and rejects a blank subject without writing.
        /// </summary>
        [Test]
        public static void MarkdownExportWritesOnlyPlayerVisiblePagesAndCleansItsFile()
        {
            DiaryEvent visible = RecordSolo(firstPawn, "Visible raw fixture text.");
            visible.date = "4th of Aprimay, 5500";
            visible.MarkInjectedTextComplete(
                DiaryEvent.InitiatorRole,
                "VISIBLE_READER_MARKDOWN_FIXTURE");

            DiaryEvent pending = RecordSolo(firstPawn, "PENDING_READER_MARKDOWN_FIXTURE");
            pending.date = "5th of Aprimay, 5500";
            pending.MarkQueued(DiaryEvent.InitiatorRole);

            // TryExport resets its out path when a write fails. A disk-full/partial-write failure could
            // therefore leave a file the exact-path cleanup below cannot discover. Give this export a
            // unique filename marker and arm a narrowly scoped sweep before crossing the filesystem edge.
            string exportMarker =
                "PawnDiaryRimTestReader" + Guid.NewGuid().ToString("N");
            string exportFolder = Path.Combine(
                GenFilePaths.SaveDataFolderPath,
                "PawnDiaryExports");
            scope.RegisterCleanup(
                () => DeleteFixtureExports(exportFolder, exportMarker));

            string path;
            int pageCount;
            string error;
            bool exported = scope.Component.TryExportPawnDiaryMarkdown(
                firstPawn.GetUniqueLoadID(),
                exportMarker,
                true,
                out path,
                out pageCount,
                out error);
            if (!string.IsNullOrWhiteSpace(path))
            {
                scope.RegisterCleanup(() =>
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                });
            }

            PawnDiaryRimTestScope.Require(
                exported && string.IsNullOrWhiteSpace(error) && pageCount == 1 && File.Exists(path),
                "The loaded Markdown adapter did not write exactly one visible page: " + error);
            string markdown = File.ReadAllText(path);
            PawnDiaryRimTestScope.Require(
                markdown.IndexOf("VISIBLE_READER_MARKDOWN_FIXTURE", StringComparison.Ordinal) >= 0,
                "The Markdown export omitted the completed visible page.");
            PawnDiaryRimTestScope.Require(
                markdown.IndexOf("PENDING_READER_MARKDOWN_FIXTURE", StringComparison.Ordinal) < 0,
                "The Markdown export leaked a pending/raw-only page.");

            string invalidPath;
            int invalidCount;
            string invalidError;
            PawnDiaryRimTestScope.Require(
                !scope.Component.TryExportPawnDiaryMarkdown(
                    "   ",
                    firstPawn.LabelShortCap,
                    true,
                    out invalidPath,
                    out invalidCount,
                    out invalidError)
                    && string.IsNullOrEmpty(invalidPath)
                    && invalidCount == 0
                    && !string.IsNullOrWhiteSpace(invalidError),
                "A blank export subject should fail before creating a file.");
        }

        private static DiaryEvent RecordSolo(Pawn pawn, string rawText)
        {
            DiaryEvent diaryEvent = scope.Component.AddSoloEvent(
                pawn,
                null,
                TestDefName,
                "reader fixture",
                rawText,
                string.Empty,
                "rimtest_reader=1");
            PawnDiaryRimTestScope.Require(
                diaryEvent != null,
                "The reader fixture could not create a test-owned diary event.");
            return diaryEvent;
        }

        private static DiaryReaderPawnRow RequireRow(
            IReadOnlyList<DiaryReaderPawnRow> rows,
            string pawnId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].Subject.PawnId, pawnId, StringComparison.Ordinal))
                {
                    return rows[i];
                }
            }

            throw new AssertionException("The reader directory did not contain test subject " + pawnId + ".");
        }

        private static DiaryGameComponent.DiaryReaderPawnInfo RequireInfo(
            IList<DiaryGameComponent.DiaryReaderPawnInfo> rows,
            string pawnId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].pawnId, pawnId, StringComparison.Ordinal))
                {
                    return rows[i];
                }
            }

            throw new AssertionException("The saved reader projection did not contain " + pawnId + ".");
        }

        private static PawnDiarySettings RequireSettings()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            if (settings == null)
            {
                throw new AssertionException("Pawn Diary settings were unavailable in the loaded game.");
            }

            return settings;
        }

        private static int CountDiaryCommands(Pawn pawn)
        {
            string label = "PawnDiaryTabLabel".Translate().Resolve();
            int count = 0;
            IEnumerable<Gizmo> gizmos = pawn?.GetGizmos();
            if (gizmos == null)
            {
                return 0;
            }

            foreach (Gizmo gizmo in gizmos)
            {
                Command_Action command = gizmo as Command_Action;
                if (command != null
                    && string.Equals(command.defaultLabel, label, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Closes every standalone reader created after this fixture proved none were initially open.
        /// Copies first because Window.Close mutates WindowStack.Windows.
        /// </summary>
        private static void CloseAllReaderWindows()
        {
            if (Find.WindowStack == null)
            {
                return;
            }

            List<Dialog_DiaryReader> readers = new List<Dialog_DiaryReader>();
            IList<Window> windows = Find.WindowStack.Windows;
            for (int i = 0; i < windows.Count; i++)
            {
                Dialog_DiaryReader reader = windows[i] as Dialog_DiaryReader;
                if (reader != null)
                {
                    readers.Add(reader);
                }
            }

            for (int i = 0; i < readers.Count; i++)
            {
                readers[i].Close(false);
            }
        }

        /// <summary>
        /// Deletes only files carrying this test invocation's GUID marker. This catches a partial write
        /// whose production catch block intentionally cleared the returned path.
        /// </summary>
        private static void DeleteFixtureExports(string exportFolder, string exportMarker)
        {
            if (string.IsNullOrWhiteSpace(exportFolder)
                || string.IsNullOrWhiteSpace(exportMarker)
                || !Directory.Exists(exportFolder))
            {
                return;
            }

            string pattern = "PawnDiary-" + exportMarker + "-*.md";
            string[] fixtureFiles = Directory.GetFiles(
                exportFolder,
                pattern,
                SearchOption.TopDirectoryOnly);
            for (int i = 0; i < fixtureFiles.Length; i++)
            {
                File.Delete(fixtureFiles[i]);
            }
        }

        private static Dialog_DiaryReader CurrentReaderWindow()
        {
            if (Find.WindowStack == null)
            {
                return null;
            }

            IList<Window> windows = Find.WindowStack.Windows;
            for (int i = 0; i < windows.Count; i++)
            {
                Dialog_DiaryReader reader = windows[i] as Dialog_DiaryReader;
                if (reader != null)
                {
                    return reader;
                }
            }

            return null;
        }

        private static DiaryReaderSubject SelectedSubject(Dialog_DiaryReader reader)
        {
            return reader == null || ReaderSelectedSubjectField == null
                ? default(DiaryReaderSubject)
                : (DiaryReaderSubject)ReaderSelectedSubjectField.GetValue(reader);
        }

        private static void SnapshotAndOwnSelection()
        {
            if (Find.Selector == null)
            {
                throw new AssertionException("The loaded game has no RimWorld selector.");
            }

            List<object> original =
                new List<object>(Find.Selector.SelectedObjectsListForReading);
            scope.RegisterCleanup(() =>
            {
                if (Find.Selector == null)
                {
                    return;
                }

                Find.Selector.ClearSelection();
                for (int i = 0; i < original.Count; i++)
                {
                    object selected = original[i];
                    if (selected == null)
                    {
                        continue;
                    }

                    try
                    {
                        Find.Selector.Select(selected, true, false);
                    }
                    catch
                    {
                        // Selection is ephemeral UI state. If vanilla destroyed one formerly selected
                        // object during another callback, restore every still-valid object and continue.
                    }
                }
            });
        }
    }
}

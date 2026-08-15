// Loaded-game persistence fixtures for player-created and player-edited diary pages. These tests do
// not draw the editor: they exercise its detached snapshot/CAS boundary, hot+archive ownership, normal
// retention, and stale async-result protection against real DiaryGameComponent stores.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Integration;
using RimWorld;
using RimTestRedux;
using UnityEngine;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Proves manual page writes remain canonical, bounded, and race-safe.</summary>
    [TestSuite]
    public static class PawnDiaryManualEntryFixtureTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string FixtureDefName = "PawnDiary_RimTest_ManualEdit";
        private static readonly FieldInfo EventsField =
            typeof(DiaryGameComponent).GetField("events", PrivateInstance);
        private static readonly FieldInfo ArchiveField =
            typeof(DiaryGameComponent).GetField("archive", PrivateInstance);
        private static readonly MethodInfo ApplyLlmResultMethod =
            typeof(DiaryGameComponent).GetMethod("ApplyLlmResult", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn firstPawn;
        private static Pawn secondPawn;

        [BeforeEach]
        public static void SetUp()
        {
            DiaryGameComponent.ResetPlayerEntryDraftTestSeams();
            scope = PawnDiaryRimTestScope.Begin();
            firstPawn = scope.CreateAdultColonist();
            secondPawn = scope.CreateAdultColonist();
            PawnDiaryRimTestScope.Require(
                EventsField != null && ArchiveField != null && ApplyLlmResultMethod != null,
                "Manual-entry fixtures could not resolve the component persistence/result seams.");
        }

        [AfterEach]
        public static void TearDown()
        {
            try
            {
                scope?.TearDown();
            }
            finally
            {
                DiaryGameComponent.ResetPlayerEntryDraftTestSeams();
                scope = null;
                firstPawn = null;
                secondPawn = null;
            }
        }

        /// <summary>
        /// A hot edit replaces provider-authored state once, stale snapshots are rejected, and saving
        /// unchanged detached buffers does not dirty render state.
        /// </summary>
        [Test]
        public static void HotEditUsesCanonicalCompareAndSwap()
        {
            DiaryEvent page = RecordSolo(firstPawn, "Original factual line.");
            page.ApplyLlmResult(new LlmGenerationResult
            {
                povRole = DiaryEvent.InitiatorRole,
                success = true,
                generatedText = "Original provider prose.",
                rawResponse = "provider raw response"
            });
            page.SetPrompt(DiaryEvent.InitiatorRole, "provider prompt");
            page.SetLlmMeta(DiaryEvent.InitiatorRole, "https://provider.invalid/v1", "fixture-model");
            page.SetTitle(DiaryEvent.InitiatorRole, "Original title");
            page.MarkQueued(DiaryEvent.InitiatorRole);
            page.MarkTitleQueued(DiaryEvent.InitiatorRole);

            ManualDiaryEntrySnapshot opened = RequireSnapshot(firstPawn, page);
            int before = DiaryStateVersion.Current;
            PawnDiaryRimTestScope.Require(
                scope.Component.TryEditManualEntry(opened, "  First line.\r\n\r\nSecond line.  ", " New\r\ntitle "),
                "The exact hot manual-entry snapshot was rejected.");

            DiaryEntryView edited = page.ToViewFor(firstPawn.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(
                edited != null
                    && string.Equals(edited.GeneratedText, "First line.\nSecond line.", StringComparison.Ordinal)
                    && string.Equals(edited.Title, "New title", StringComparison.Ordinal)
                    && DiaryEvent.RoleEquals(edited.LlmStatus, DiaryEvent.CompleteStatus)
                    && string.IsNullOrEmpty(edited.LlmPrompt)
                    && string.IsNullOrEmpty(edited.LlmRawResponse)
                    && string.IsNullOrEmpty(edited.LlmEndpoint)
                    && string.IsNullOrEmpty(edited.LlmModel)
                    && DiaryEvent.RoleEquals(page.initiatorTitleStatus, DiaryEvent.CompleteStatus),
                "The hot manual edit did not become the canonical provider-free page.");
            PawnDiaryRimTestScope.Require(
                DiaryStateVersion.Current == before + 1,
                "One hot manual edit should invalidate rendered state exactly once.");

            ManualDiaryEntrySnapshot after = RequireSnapshot(firstPawn, page);
            int beforeNoOp = DiaryStateVersion.Current;
            PawnDiaryRimTestScope.Require(
                scope.Component.TryEditManualEntry(after, after.Body, after.Title)
                    && DiaryStateVersion.Current == beforeNoOp,
                "Saving unchanged manual-entry buffers should succeed without a state-version bump.");
            PawnDiaryRimTestScope.Require(
                !scope.Component.TryEditManualEntry(opened, "stale overwrite", "stale title"),
                "A stale dialog-open snapshot overwrote newer canonical prose.");
        }

        /// <summary>
        /// Main and title completions that arrive after a manual Save are consumed as stale: they do not
        /// overwrite text, mark unread, retry, or emit a dependent pair transition.
        /// </summary>
        [Test]
        public static void LateMainAndTitleResultsCannotOverwriteManualSave()
        {
            DiaryEvent page = RecordSolo(firstPawn, "Pending factual line.");
            page.MarkQueued(DiaryEvent.InitiatorRole);
            page.MarkTitleQueued(DiaryEvent.InitiatorRole);
            ManualDiaryEntrySnapshot opened = RequireSnapshot(firstPawn, page);
            PawnDiaryRecord record = scope.RequireDiaryRecord(firstPawn);
            int unreadBefore = record.unreadGeneratedEntryCount;

            PawnDiaryRimTestScope.Require(
                scope.Component.TryEditManualEntry(opened, "The player won the race.", "Player title"),
                "The pending page could not be completed manually.");

            ApplyResult(new LlmGenerationResult
            {
                eventId = page.eventId,
                povRole = DiaryEvent.InitiatorRole,
                success = true,
                generatedText = "Late provider body.",
                rawResponse = "Late provider body."
            });
            ApplyResult(new LlmGenerationResult
            {
                eventId = page.eventId,
                povRole = DiaryEvent.InitiatorRole,
                isTitleRequest = true,
                success = true,
                generatedText = "Late provider title"
            });
            ApplyResult(new LlmGenerationResult
            {
                eventId = page.eventId,
                povRole = DiaryEvent.InitiatorRole,
                success = false,
                error = "late transport failure"
            });

            DiaryEntryView view = page.ToViewFor(firstPawn.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(
                view != null
                    && string.Equals(view.GeneratedText, "The player won the race.", StringComparison.Ordinal)
                    && string.Equals(view.Title, "Player title", StringComparison.Ordinal)
                    && DiaryEvent.RoleEquals(view.LlmStatus, DiaryEvent.CompleteStatus)
                    && record.unreadGeneratedEntryCount == unreadBefore,
                "A stale body/title completion changed manual prose, lifecycle, or unread state.");
        }

        /// <summary>
        /// If one POV has compacted while its pair stays hot for the other pawn, editing the compact row
        /// also updates the hidden hot slot and every archived linked preview without restoring ownership.
        /// </summary>
        [Test]
        public static void MixedRetentionEditKeepsHotArchiveAndLinksConsistent()
        {
            DiaryEvent pair = scope.Component.AddPairwiseEvent(
                firstPawn,
                secondPawn,
                FixtureDefName,
                "manual mixed fixture",
                "First raw.",
                "Second raw.",
                string.Empty,
                "rimtest_manual_mixed=true");
            PawnDiaryRimTestScope.Require(pair != null, "Could not create the mixed-retention pair.");
            pair.MarkInjectedTextComplete(DiaryEvent.InitiatorRole, "First original page.");
            pair.MarkInjectedTextComplete(DiaryEvent.RecipientRole, "Second original page.");
            pair.MarkTitleComplete(DiaryEvent.InitiatorRole, "First original title");
            pair.MarkTitleComplete(DiaryEvent.RecipientRole, "Second original title");

            string firstId = firstPawn.GetUniqueLoadID();
            string secondId = secondPawn.GetUniqueLoadID();
            ArchivedDiaryEntry firstArchive = ArchivedDiaryEntry.FromEvent(
                pair, firstId, pair.ToViewFor(firstId), false);
            ArchivedDiaryEntry secondArchive = ArchivedDiaryEntry.FromEvent(
                pair, secondId, pair.ToViewFor(secondId), false);
            PawnDiaryRimTestScope.Require(
                Archive().AddOrKeep(firstArchive) && Archive().AddOrKeep(secondArchive),
                "Could not stage the mixed-retention archive rows.");
            scope.RegisterCleanup(() => Archive().RemoveForEventIds(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pair.eventId }));

            PawnDiaryRecord firstRecord = scope.RequireDiaryRecord(firstPawn);
            firstRecord.eventIds.Remove(pair.eventId);
            ManualDiaryEntrySnapshot opened = RequireSnapshot(firstId, pair.eventId, DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(opened.Archived,
                "The compacted POV did not resolve through archive ownership.");

            string editedBody = "A deliberately long player-authored first-page preview that proves the linked "
                + "archive mirror is refreshed from the canonical replacement instead of retaining old prose.";
            int before = DiaryStateVersion.Current;
            PawnDiaryRimTestScope.Require(
                scope.Component.TryEditManualEntry(opened, editedBody, "Edited first title"),
                "The exact mixed-retention POV rejected a manual edit.");

            PawnDiaryRimTestScope.Require(
                string.Equals(firstArchive.generatedText, editedBody, StringComparison.Ordinal)
                    && string.Equals(pair.initiatorGeneratedText, editedBody, StringComparison.Ordinal)
                    && string.Equals(pair.initiatorTitle, "Edited first title", StringComparison.Ordinal)
                    && string.Equals(secondArchive.linkedPreviewText,
                        DiaryEvent.TruncateForPreview(editedBody), StringComparison.Ordinal)
                    && string.Equals(secondArchive.linkedTitle, "Edited first title", StringComparison.Ordinal)
                    && secondArchive.linkedGenerated
                    && !firstRecord.eventIds.Contains(pair.eventId)
                    && Events().ContainsEvent(pair.eventId)
                    && DiaryStateVersion.Current == before + 1,
                "Mixed retention diverged, restored a removed owner ref, or invalidated more than once.");

            // Complete the retention transition: with both POVs now archive-only, changing the second
            // compact page must refresh the first compact page's frozen link without a master event.
            PawnDiaryRecord secondRecord = scope.RequireDiaryRecord(secondPawn);
            secondRecord.eventIds.Remove(pair.eventId);
            Events().RemoveEvent(pair.eventId);
            PawnDiaryRimTestScope.Require(!Events().ContainsEvent(pair.eventId),
                "The shared event remained hot after both fixture owners released it.");

            ManualDiaryEntrySnapshot secondOpened = RequireSnapshot(
                secondId, pair.eventId, DiaryEvent.RecipientRole);
            int beforeBothArchived = DiaryStateVersion.Current;
            PawnDiaryRimTestScope.Require(
                secondOpened.Archived
                    && scope.Component.TryEditManualEntry(
                        secondOpened,
                        "Second archive-only replacement.",
                        "Edited second title")
                    && string.Equals(secondArchive.generatedText,
                        "Second archive-only replacement.", StringComparison.Ordinal)
                    && string.Equals(secondArchive.title, "Edited second title", StringComparison.Ordinal)
                    && string.Equals(firstArchive.linkedPreviewText,
                        "Second archive-only replacement.", StringComparison.Ordinal)
                    && string.Equals(firstArchive.linkedTitle,
                        "Edited second title", StringComparison.Ordinal)
                    && firstArchive.linkedGenerated
                    && DiaryStateVersion.Current == beforeBothArchived + 1,
                "Two archive-only POVs did not keep their frozen partner link synchronized.");
        }

        /// <summary>
        /// Player-created pages bypass only automatic-writing gates, carry their localized label, never
        /// become unread, cannot regenerate, and compact as completed canonical archive rows.
        /// </summary>
        [Test]
        public static void ManualCreateCompletesBeforeOrdinaryRetention()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            PawnDiaryRimTestScope.Require(settings != null,
                "Pawn Diary settings were unavailable for manual-create retention coverage.");
            int oldActive = settings.maxActiveDiaryEvents;
            int oldArchive = settings.maxArchivedDiaryEvents;
            scope.RegisterCleanup(() =>
            {
                settings.maxActiveDiaryEvents = oldActive;
                settings.maxArchivedDiaryEvents = oldArchive;
            });
            settings.maxActiveDiaryEvents = PawnDiarySettings.MinActiveDiaryEvents;
            settings.maxArchivedDiaryEvents = Math.Max(2, oldArchive);

            scope.Component.SetDiaryGenerationEnabled(firstPawn, false);
            int expectedTick = Find.TickManager.TicksGame;
            string expectedDate = GenDate.DateFullStringAt(Find.TickManager.TicksAbs, Vector2.zero);
            string firstEventId;
            PawnDiaryRimTestScope.Require(
                scope.Component.TryCreateManualEntry(
                    firstPawn,
                    " First manual page.\r\n\r\nIt keeps paragraphs. ",
                    string.Empty,
                    "My journal note",
                    out firstEventId),
                "Manual creation should bypass the pawn's automatic-generation switch.");

            string secondEventId;
            PawnDiaryRimTestScope.Require(
                scope.Component.TryCreateManualEntry(
                    firstPawn,
                    "Second manual page.",
                    "Second title",
                    "My journal note",
                    out secondEventId),
                "The second manual page could not be created.");

            string pawnId = firstPawn.GetUniqueLoadID();
            ArchivedDiaryEntry compacted = Archive().Find(
                firstEventId, pawnId, DiaryEvent.InitiatorRole);
            DiaryEvent newest = Events().FindEvent(secondEventId);
            PawnDiaryRecord record = scope.RequireDiaryRecord(firstPawn);
            PawnDiaryRimTestScope.Require(
                compacted != null
                    && string.Equals(compacted.generatedText,
                        "First manual page.\nIt keeps paragraphs.", StringComparison.Ordinal)
                    && DiaryEvent.RoleEquals(compacted.status, DiaryEvent.CompleteStatus)
                    && string.IsNullOrEmpty(compacted.title)
                    && compacted.tick == expectedTick
                    && string.Equals(compacted.date, expectedDate, StringComparison.Ordinal)
                    && newest != null
                    && newest.tick == expectedTick
                    && string.Equals(newest.date, expectedDate, StringComparison.Ordinal)
                    && string.Equals(newest.interactionDefName,
                        "PawnDiary_ManualEntry", StringComparison.Ordinal)
                    && string.Equals(newest.gameContext,
                        ManualDiaryEntryFacts.GameContext, StringComparison.Ordinal)
                    && ManualDiaryEntryFacts.IsPlayerCreated(newest.gameContext)
                    && string.Equals(newest.interactionLabel, "My journal note", StringComparison.Ordinal)
                    && string.Equals(newest.initiatorGeneratedText, "Second manual page.", StringComparison.Ordinal)
                    && string.Equals(newest.initiatorTitle, "Second title", StringComparison.Ordinal)
                    && DiaryEvent.RoleEquals(newest.initiatorStatus, DiaryEvent.CompleteStatus)
                    && DiaryEvent.RoleEquals(newest.initiatorTitleStatus, DiaryEvent.CompleteStatus)
                    && string.IsNullOrEmpty(newest.initiatorLlmEndpoint)
                    && string.IsNullOrEmpty(newest.initiatorLlmModel)
                    && record.unreadGeneratedEntryCount == 0
                    && !record.hasUnreadGeneratedEntry,
                "Manual creation did not complete before retention or incorrectly marked pages unread.");

            DiaryEntryView newestView = newest.ToViewFor(pawnId);
            PawnDiaryRimTestScope.Require(
                newestView != null
                    && newestView.PlayerCreated
                    && string.Equals(newestView.GroupLabel, "My journal note", StringComparison.Ordinal)
                    && !scope.Component.RegenerateEntry(firstPawn, newestView),
                "The manual marker was relabelled by the catch-all group or remained regenerable.");

            ManualDiaryEntrySnapshot archiveSnapshot = RequireSnapshot(
                pawnId, firstEventId, DiaryEvent.InitiatorRole);
            int beforeArchiveEdit = DiaryStateVersion.Current;
            PawnDiaryRimTestScope.Require(
                archiveSnapshot.Archived
                    && archiveSnapshot.PlayerCreated
                    && scope.Component.TryEditManualEntry(
                        archiveSnapshot,
                        "Edited compact manual page.",
                        "Archive title")
                    && DiaryStateVersion.Current == beforeArchiveEdit + 1
                    && string.Equals(compacted.generatedText,
                        "Edited compact manual page.", StringComparison.Ordinal),
                "An archive-only manual page was not editable through its stable identity.");
        }

        /// <summary>
        /// Invalid player input is rejected before creating a shell, and an invalid edit cannot dirty the
        /// existing page. These are the persistence-side guarantees behind Cancel/blank-body UI paths.
        /// </summary>
        [Test]
        public static void BlankCreateAndEditAreAtomicNoOps()
        {
            PawnDiaryRecord record = scope.RequireDiaryRecord(firstPawn);
            int eventCount = Events().Count;
            int archiveCount = Archive().Count;
            int referenceCount = record.eventIds.Count;
            int unreadCount = record.unreadGeneratedEntryCount;
            int version = DiaryStateVersion.Current;
            string rejectedEventId = "must be cleared";

            PawnDiaryRimTestScope.Require(
                !scope.Component.TryCreateManualEntry(
                    firstPawn,
                    " \r\n\t ",
                    "Unused title",
                    "Personal entry",
                    out rejectedEventId)
                    && string.IsNullOrEmpty(rejectedEventId)
                    && Events().Count == eventCount
                    && Archive().Count == archiveCount
                    && record.eventIds.Count == referenceCount
                    && record.unreadGeneratedEntryCount == unreadCount
                    && DiaryStateVersion.Current == version,
                "Blank creation left an event, reference, unread flag, archive row, or render mutation.");

            scope.Component.SetDiaryGenerationEnabled(firstPawn, false);
            DiaryEvent page = RecordSolo(firstPawn, "Stable source facts.");
            page.MarkInjectedTextComplete(DiaryEvent.InitiatorRole, "Stable visible prose.");
            page.MarkTitleComplete(DiaryEvent.InitiatorRole, "Stable title");
            ManualDiaryEntrySnapshot opened = RequireSnapshot(firstPawn, page);
            int beforeBlankEdit = DiaryStateVersion.Current;

            PawnDiaryRimTestScope.Require(
                !scope.Component.TryEditManualEntry(opened, " \n\t ", "Changed title")
                    && string.Equals(page.initiatorGeneratedText,
                        "Stable visible prose.", StringComparison.Ordinal)
                    && string.Equals(page.initiatorTitle, "Stable title", StringComparison.Ordinal)
                    && DiaryStateVersion.Current == beforeBlankEdit,
                "A blank edit changed canonical prose, title, or render state.");
        }

        /// <summary>
        /// The create dialog is detached and may outlive its pawn. Recheck death at the persistence
        /// boundary so clicking Save after a death cannot append a posthumous first-person page even if
        /// the header button is no longer visible.
        /// </summary>
        [Test]
        public static void DeadAfterOpenCreateIsAnAtomicNoOp()
        {
            RegisterDeadPawnCleanup(firstPawn);
            firstPawn.Kill(null);
            PawnDiaryRimTestScope.Require(firstPawn.Dead,
                "The dead-after-open fixture pawn did not enter the dead state.");

            PawnDiaryRecord record = scope.RequireDiaryRecord(firstPawn);
            int eventCount = Events().Count;
            int archiveCount = Archive().Count;
            int referenceCount = record.eventIds.Count;
            int unreadCount = record.unreadGeneratedEntryCount;
            int version = DiaryStateVersion.Current;
            string eventId = "must be cleared";

            PawnDiaryRimTestScope.Require(
                !scope.Component.TryCreateManualEntry(
                    firstPawn,
                    "This page must never exist.",
                    "Posthumous draft",
                    "Personal entry",
                    out eventId)
                    && string.IsNullOrEmpty(eventId)
                    && Events().Count == eventCount
                    && Archive().Count == archiveCount
                    && record.eventIds.Count == referenceCount
                    && record.unreadGeneratedEntryCount == unreadCount
                    && DiaryStateVersion.Current == version,
                "Saving a draft after its pawn died changed events, archive, ownership, unread, or render state.");
        }

        /// <summary>
        /// Player writes are observable through the same public status-listener contract as injected
        /// final prose: one terminal callback after create/edit, and none for no-op or stale writes.
        /// </summary>
        [Test]
        public static void ManualWritesNotifyExactlyOnceAndFailuresStaySilent()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            PawnDiaryRimTestScope.Require(settings != null,
                "Pawn Diary settings were unavailable for listener coverage.");
            bool oldIntegrations = settings.allowExternalIntegrations;
            settings.allowExternalIntegrations = true;
            scope.RegisterCleanup(() => settings.allowExternalIntegrations = oldIntegrations);

            List<DiaryEntryStatusSnapshot> notifications = new List<DiaryEntryStatusSnapshot>();
            string listenerId = "PawnDiary.RimTest.ManualWrites." + Guid.NewGuid().ToString("N");
            PawnDiaryApi.RegisterEntryStatusListener(listenerId, snapshot => notifications.Add(snapshot));
            scope.RegisterCleanup(() => PawnDiaryApi.UnregisterEntryStatusListener(listenerId));

            string eventId;
            PawnDiaryRimTestScope.Require(
                scope.Component.TryCreateManualEntry(
                    firstPawn,
                    "Listener-visible created prose.",
                    "Created title",
                    "Personal entry",
                    out eventId),
                "Could not create the listener fixture page.");
            PawnDiaryRimTestScope.Require(
                notifications.Count == 1
                    && notifications[0] != null
                    && notifications[0].handle != null
                    && string.Equals(notifications[0].handle.eventId, eventId, StringComparison.Ordinal)
                    && notifications[0].complete
                    && notifications[0].hasGeneratedText
                    && string.Equals(notifications[0].title, "Created title", StringComparison.Ordinal)
                    && string.Equals(notifications[0].summary,
                        "Listener-visible created prose.", StringComparison.Ordinal),
                "Manual creation did not publish exactly one complete canonical status snapshot.");

            DiaryEvent page = Events().FindEvent(eventId);
            ManualDiaryEntrySnapshot opened = RequireSnapshot(firstPawn, page);
            notifications.Clear();
            PawnDiaryRimTestScope.Require(
                scope.Component.TryEditManualEntry(
                    opened,
                    "Listener-visible edited prose.",
                    "Edited title",
                    "Combat"),
                "Could not edit the listener fixture page.");
            PawnDiaryRimTestScope.Require(
                notifications.Count == 1
                    && notifications[0] != null
                    && notifications[0].complete
                    && string.Equals(notifications[0].title, "Edited title", StringComparison.Ordinal)
                    && string.Equals(notifications[0].summary,
                        "Listener-visible edited prose.", StringComparison.Ordinal),
                "Manual text/type editing did not publish exactly one updated canonical status snapshot.");

            ManualDiaryEntrySnapshot current = RequireSnapshot(firstPawn, page);
            notifications.Clear();
            PawnDiaryRimTestScope.Require(
                scope.Component.TryEditManualEntry(current, current.Body, current.Title)
                    && notifications.Count == 0,
                "A no-op Save notified status listeners.");
            PawnDiaryRimTestScope.Require(
                !scope.Component.TryEditManualEntry(opened, "Stale body", "Stale title")
                    && notifications.Count == 0,
                "A rejected stale Save notified status listeners.");

            string rejectedId = "must be cleared";
            PawnDiaryRimTestScope.Require(
                !scope.Component.TryCreateManualEntry(
                    firstPawn,
                    "Rejected unknown-type prose.",
                    "Rejected title",
                    "Personal entry",
                    "NotARealPlayerEntryType",
                    out rejectedId)
                    && string.IsNullOrEmpty(rejectedId)
                    && notifications.Count == 0,
                "A rejected unknown-type create notified status listeners or returned an event id.");
        }

        /// <summary>
        /// A player write is independent of both automatic generation and the public-integration switch,
        /// while its validated category is saved on the exact POV and immediately drives display policy.
        /// </summary>
        [Test]
        public static void TypedDirectCreateIgnoresAutomaticAndExternalGenerationSwitches()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            bool oldIntegrations = settings.allowExternalIntegrations;
            settings.allowExternalIntegrations = false;
            scope.RegisterCleanup(() => settings.allowExternalIntegrations = oldIntegrations);
            scope.Component.SetDiaryGenerationEnabled(firstPawn, false);

            string eventId;
            PawnDiaryRimTestScope.Require(
                scope.Component.TryCreateManualEntry(
                    firstPawn,
                    "I chose to record this fight myself.",
                    "My own battle",
                    "Personal entry",
                    "Combat",
                    out eventId),
                "A typed direct create was incorrectly gated by automatic generation or integrations.");

            DiaryEvent page = Events().FindEvent(eventId);
            ManualDiaryEntrySnapshot snapshot = RequireSnapshot(firstPawn, page);
            DiaryEntryView view = page?.ToViewFor(firstPawn.GetUniqueLoadID());
            PawnDiaryRecord record = scope.RequireDiaryRecord(firstPawn);
            PawnDiaryRimTestScope.Require(
                page != null
                    && snapshot != null
                    && string.Equals(snapshot.EntryTypeKey, "Combat", StringComparison.Ordinal)
                    && !snapshot.EntryTypeLocked
                    && view != null
                    && view.Important
                    && string.Equals(view.TextDecorationContext?.domain, "Raid", StringComparison.Ordinal)
                    && record.unreadGeneratedEntryCount == 0
                    && !record.hasUnreadGeneratedEntry,
                "Typed direct creation lost its per-POV category or produced generated/unread state.");
        }

        /// <summary>
        /// Category compare-and-swap follows the same exact POV through hot pair, mixed-retention, and
        /// archive-only storage. The partner category is never rewritten as collateral damage.
        /// </summary>
        [Test]
        public static void EntryTypeCasIsPerPovAcrossHotMixedAndArchiveRetention()
        {
            DiaryEvent pair = scope.Component.AddPairwiseEvent(
                firstPawn,
                secondPawn,
                FixtureDefName,
                "typed pair fixture",
                "First source.",
                "Second source.",
                string.Empty,
                "rimtest_manual_type_pair=true");
            PawnDiaryRimTestScope.Require(pair != null, "Could not create the typed pair fixture.");
            pair.MarkInjectedTextComplete(DiaryEvent.InitiatorRole, "First typed page.");
            pair.MarkInjectedTextComplete(DiaryEvent.RecipientRole, "Second typed page.");
            pair.MarkTitleComplete(DiaryEvent.InitiatorRole, "First typed title");
            pair.MarkTitleComplete(DiaryEvent.RecipientRole, "Second typed title");

            string firstId = firstPawn.GetUniqueLoadID();
            string secondId = secondPawn.GetUniqueLoadID();
            ManualDiaryEntrySnapshot staleFirst = RequireSnapshot(firstId, pair.eventId,
                DiaryEvent.InitiatorRole);
            ManualDiaryEntrySnapshot secondOpened = RequireSnapshot(secondId, pair.eventId,
                DiaryEvent.RecipientRole);
            int beforeFirst = DiaryStateVersion.Current;
            PawnDiaryRimTestScope.Require(
                scope.Component.TryEditManualEntry(
                    staleFirst, staleFirst.Body, staleFirst.Title, "Combat")
                    && string.Equals(pair.EntryTypeKeyForRole(DiaryEvent.InitiatorRole),
                        "Combat", StringComparison.Ordinal)
                    && string.IsNullOrEmpty(pair.EntryTypeKeyForRole(DiaryEvent.RecipientRole))
                    && DiaryStateVersion.Current == beforeFirst + 1,
                "A hot initiator type-only Save changed its partner or invalidated incorrectly.");
            PawnDiaryRimTestScope.Require(
                !scope.Component.TryEditManualEntry(
                    staleFirst, staleFirst.Body, staleFirst.Title, "Work"),
                "A stale pre-category snapshot bypassed the type compare-and-swap boundary.");

            int beforeSecond = DiaryStateVersion.Current;
            PawnDiaryRimTestScope.Require(
                scope.Component.TryEditManualEntry(
                    secondOpened, secondOpened.Body, secondOpened.Title, "Social")
                    && string.Equals(pair.EntryTypeKeyForRole(DiaryEvent.InitiatorRole),
                        "Combat", StringComparison.Ordinal)
                    && string.Equals(pair.EntryTypeKeyForRole(DiaryEvent.RecipientRole),
                        "Social", StringComparison.Ordinal)
                    && DiaryStateVersion.Current == beforeSecond + 1,
                "The partner's independent hot category did not commit exactly once.");

            ArchivedDiaryEntry firstArchive = ArchivedDiaryEntry.FromEvent(
                pair, firstId, pair.ToViewFor(firstId), false);
            ArchivedDiaryEntry secondArchive = ArchivedDiaryEntry.FromEvent(
                pair, secondId, pair.ToViewFor(secondId), false);
            PawnDiaryRimTestScope.Require(
                Archive().AddOrKeep(firstArchive) && Archive().AddOrKeep(secondArchive),
                "Could not stage typed archive mirrors.");
            scope.RegisterCleanup(() => Archive().RemoveForEventIds(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pair.eventId }));

            PawnDiaryRecord firstRecord = scope.RequireDiaryRecord(firstPawn);
            firstRecord.eventIds.Remove(pair.eventId);
            ManualDiaryEntrySnapshot mixedFirst = RequireSnapshot(
                firstId, pair.eventId, DiaryEvent.InitiatorRole);
            int beforeMixed = DiaryStateVersion.Current;
            PawnDiaryRimTestScope.Require(
                mixedFirst.Archived
                    && scope.Component.TryEditManualEntry(
                        mixedFirst, mixedFirst.Body, mixedFirst.Title, "Health")
                    && string.Equals(firstArchive.entryTypeKey, "Health", StringComparison.Ordinal)
                    && string.Equals(pair.EntryTypeKeyForRole(DiaryEvent.InitiatorRole),
                        "Health", StringComparison.Ordinal)
                    && string.Equals(pair.EntryTypeKeyForRole(DiaryEvent.RecipientRole),
                        "Social", StringComparison.Ordinal)
                    && DiaryStateVersion.Current == beforeMixed + 1,
                "A mixed-retention category diverged between its archive and hidden hot POV.");

            PawnDiaryRecord secondRecord = scope.RequireDiaryRecord(secondPawn);
            secondRecord.eventIds.Remove(pair.eventId);
            Events().RemoveEvent(pair.eventId);
            ManualDiaryEntrySnapshot archivedSecond = RequireSnapshot(
                secondId, pair.eventId, DiaryEvent.RecipientRole);
            int beforeArchive = DiaryStateVersion.Current;
            PawnDiaryRimTestScope.Require(
                archivedSecond.Archived
                    && scope.Component.TryEditManualEntry(
                        archivedSecond, archivedSecond.Body, archivedSecond.Title, "Work")
                    && string.Equals(secondArchive.entryTypeKey, "Work", StringComparison.Ordinal)
                    && string.Equals(firstArchive.entryTypeKey, "Health", StringComparison.Ordinal)
                    && DiaryStateVersion.Current == beforeArchive + 1,
                "An archive-only category Save changed its partner or missed invalidation.");
        }

        /// <summary>Arrival/death neutral pages remain text-editable but their boundary category is locked.</summary>
        [Test]
        public static void NeutralLifeBoundaryRejectsEntryTypeMutationAtomically()
        {
            DiaryEvent boundary = RecordSolo(firstPawn, "Boundary source facts.");
            string pawnId = firstPawn.GetUniqueLoadID();
            boundary.gameContext = "arrival_description=true; arrival_pawn_id=" + pawnId;
            boundary.MarkInjectedTextComplete(DiaryEvent.NeutralRole, "Neutral arrival prose.");
            boundary.MarkTitleComplete(DiaryEvent.NeutralRole, "Neutral arrival title");
            ManualDiaryEntrySnapshot opened = RequireSnapshot(
                pawnId, boundary.eventId, DiaryEvent.NeutralRole);
            int before = DiaryStateVersion.Current;

            PawnDiaryRimTestScope.Require(
                opened.EntryTypeLocked
                    && string.IsNullOrEmpty(opened.EntryTypeKey)
                    && !scope.Component.TryEditManualEntry(
                        opened,
                        "Changed neutral prose.",
                        "Changed neutral title",
                        "Combat")
                    && string.Equals(boundary.neutralGeneratedText,
                        "Neutral arrival prose.", StringComparison.Ordinal)
                    && string.Equals(boundary.neutralTitle,
                        "Neutral arrival title", StringComparison.Ordinal)
                    && string.IsNullOrEmpty(boundary.EntryTypeKeyForRole(DiaryEvent.NeutralRole))
                    && DiaryStateVersion.Current == before,
                "A neutral life-boundary category change partially mutated the page.");
        }

        /// <summary>The loaded Def catalog is deterministic, localized, and detached for normal UI use.</summary>
        [Test]
        public static void PlayerEntryTypeAndTemplateCatalogsLoadInStableOrder()
        {
            List<PlayerEntryTypeSnapshot> types = DiaryPlayerEntryTypes.ForUi();
            List<PlayerEntryTemplateSnapshot> templates = DiaryPlayerPromptTemplates.ForUi();
            string[] expectedTypes =
            {
                "Personal", "Important", "InnerThoughts", "Social", "Work",
                "Health", "Combat", "Colony", "Reflection"
            };
            string[] expectedPromptKeys =
            {
                "PlayerPersonal", "PlayerImportant", "PlayerInnerThoughts", "PlayerSocial",
                "PlayerWork", "PlayerHealth", "PlayerCombat", "PlayerColony", "PlayerReflection"
            };
            string[] expectedTemplates =
            {
                DiaryPipelineTemplates.SoloDefault,
                DiaryPipelineTemplates.SoloImportant,
                DiaryPipelineTemplates.SoloInternalState
            };
            PawnDiaryRimTestScope.Require(types.Count == expectedTypes.Length,
                "The loaded player-entry type catalog did not contain the nine shipped rows.");
            PawnDiaryRimTestScope.Require(templates.Count == expectedTemplates.Length,
                "The loaded player template catalog exposed a non-opted or missing row.");
            for (int i = 0; i < expectedTypes.Length; i++)
            {
                PlayerEntryTypeSnapshot row = types[i];
                PawnDiaryRimTestScope.Require(
                    row != null
                        && string.Equals(row.entryTypeKey, expectedTypes[i], StringComparison.Ordinal)
                        && string.Equals(row.eventPromptKey, expectedPromptKeys[i], StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(row.defaultTemplateKey)
                        && !string.IsNullOrWhiteSpace(row.label)
                        && !string.IsNullOrWhiteSpace(row.description),
                    "Loaded player-entry type ordering/localization diverged at row " + i + ".");
                DiaryEventPromptDef prompt = DiaryEventPrompts.ForKey(row.eventPromptKey);
                PawnDiaryRimTestScope.Require(
                    prompt != null
                        && !string.IsNullOrWhiteSpace(prompt.prompt)
                        && !string.IsNullOrWhiteSpace(prompt.enhancement),
                    "Player-entry type did not resolve its shipped generic prompt at row " + i + ".");
            }
            for (int i = 0; i < expectedTemplates.Length; i++)
            {
                PlayerEntryTemplateSnapshot row = templates[i];
                PawnDiaryRimTestScope.Require(
                    row != null
                        && string.Equals(row.templateKey, expectedTemplates[i], StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(row.label)
                        && !string.IsNullOrWhiteSpace(row.description),
                "Loaded player template ordering/localization diverged at row " + i + ".");
            }
        }

        /// <summary>
        /// Any XML template opted into the composer must cross the Def-to-pure boundary under its exact
        /// key; showing a custom row and then silently planning with SoloDefault is forbidden.
        /// </summary>
        [Test]
        public static void CustomPlayerSelectableTemplateIsCopiedIntoPromptPolicy()
        {
            const string customKey = "RimTestPlayerSelectableCustom";
            DiaryPromptTemplateDef custom = new DiaryPromptTemplateDef
            {
                defName = "PawnDiary_RimTest_PlayerSelectableCustom",
                label = "fixture custom template",
                description = "Fixture-only custom composer template.",
                templateKey = customKey,
                playerSelectable = true,
                playerOrder = 777,
                systemPrompt = "CUSTOM_TEMPLATE_SYSTEM_SENTINEL",
                finalInstruction = "CUSTOM_TEMPLATE_FINAL_SENTINEL",
                maxTokens = 277,
                fields = new List<DiaryPromptFieldDef>
                {
                    new DiaryPromptFieldDef
                    {
                        label = "fixture facts",
                        source = "PovText"
                    }
                }
            };
            List<DiaryPromptTemplateDef> defs =
                DefDatabase<DiaryPromptTemplateDef>.AllDefsListForReading;
            defs.Add(custom);
            scope.RegisterCleanup(() => defs.Remove(custom));

            List<PlayerEntryTemplateSnapshot> uiRows = DiaryPlayerPromptTemplates.ForUi();
            DiaryPolicySnapshot policy = DiaryPipelineAdapters.PolicyFor(
                new DiaryEventPayload
                {
                    defName = "PlayerPersonal",
                    playerEntryTypeKey = "Personal",
                    solo = true,
                    display = new DiaryDisplayPayload { important = false }
                });
            PlayerEntryTemplateSnapshot uiRow = uiRows.Find(row => string.Equals(
                row?.templateKey, customKey, StringComparison.OrdinalIgnoreCase));
            DiaryTemplatePolicy copied = policy.templates.Find(row => string.Equals(
                row?.templateKey, customKey, StringComparison.OrdinalIgnoreCase));
            PawnDiaryRimTestScope.Require(
                uiRow != null
                    && copied != null
                    && copied.playerSelectable
                    && copied.maxTokens == 277
                    && string.Equals(
                        copied.systemPrompt, custom.systemPrompt, StringComparison.Ordinal)
                    && copied.fields.Count == 1
                    && string.Equals(
                        copied.fields[0].source, "PovText", StringComparison.Ordinal),
                "A custom player-selectable template was shown by the UI but absent/corrupt in prompt policy.");
        }

        /// <summary>
        /// A non-selectable patch Def may share a built-in template key. The fixed policy bootstrap must
        /// be replaced by the exact first selectable Def that the composer exposes for that key.
        /// </summary>
        [Test]
        public static void NonSelectableBuiltInKeyCollisionKeepsSelectableTemplateBound()
        {
            DiaryPromptTemplateDef collision = new DiaryPromptTemplateDef
            {
                defName = "PawnDiary_RimTest_NonSelectableSoloDefaultCollision",
                label = "hidden collision",
                templateKey = DiaryPipelineTemplates.SoloDefault,
                playerSelectable = false,
                playerOrder = -999,
                systemPrompt = "NON_SELECTABLE_BUILT_IN_COLLISION_MUST_NOT_BIND",
                maxTokens = 991,
                fields = new List<DiaryPromptFieldDef>
                {
                    new DiaryPromptFieldDef { label = "wrong", source = "EntryText" }
                }
            };
            List<DiaryPromptTemplateDef> defs =
                DefDatabase<DiaryPromptTemplateDef>.AllDefsListForReading;
            defs.Insert(0, collision);
            scope.RegisterCleanup(() => defs.Remove(collision));

            DiaryPromptTemplateDef expected = defs.Find(row =>
                !ReferenceEquals(row, collision)
                    && row != null
                    && row.playerSelectable
                    && string.Equals(
                        row.templateKey,
                        DiaryPipelineTemplates.SoloDefault,
                        StringComparison.OrdinalIgnoreCase));
            List<PlayerEntryTemplateSnapshot> uiRows = DiaryPlayerPromptTemplates.ForUi();
            DiaryPolicySnapshot policy = DiaryPipelineAdapters.PolicyFor(
                new DiaryEventPayload
                {
                    defName = "PlayerPersonal",
                    playerEntryTypeKey = "Personal",
                    solo = true,
                    display = new DiaryDisplayPayload { important = false }
                });
            PlayerEntryTemplateSnapshot uiRow = uiRows.Find(row => string.Equals(
                row?.templateKey,
                DiaryPipelineTemplates.SoloDefault,
                StringComparison.OrdinalIgnoreCase));
            DiaryTemplatePolicy copied = policy.templates.Find(row => string.Equals(
                row?.templateKey,
                DiaryPipelineTemplates.SoloDefault,
                StringComparison.OrdinalIgnoreCase));
            string expectedSystemPrompt = DiaryPromptTemplates.SystemPromptFor(
                expected, DiaryPipelineTemplates.SoloDefault);
            List<DiaryPromptFieldDef> expectedFields = DiaryPromptTemplates.FieldsFor(
                expected, DiaryPipelineTemplates.SoloDefault);
            PawnDiaryRimTestScope.Require(
                expected != null
                    && uiRow != null
                    && uiRow.displayOrder == expected.playerOrder
                    && copied != null
                    && copied.playerSelectable
                    && copied.playerOrder == expected.playerOrder
                    && copied.maxTokens == expected.maxTokens
                    && string.Equals(
                        copied.systemPrompt, expectedSystemPrompt, StringComparison.Ordinal)
                    && copied.fields.Count == expectedFields.Count
                    && copied.fields.Count > 0
                    && string.Equals(
                        copied.fields[0].source, expectedFields[0].source, StringComparison.Ordinal)
                    && !string.Equals(
                        copied.systemPrompt, collision.systemPrompt, StringComparison.Ordinal),
                "A hidden built-in-key collision rebound the policy away from the UI's selectable Def.");
        }

        /// <summary>
        /// A different template whose defName aliases a selectable templateKey must not hijack the
        /// policy after the UI has selected the later exact Def.
        /// </summary>
        [Test]
        public static void DefNameAliasCollisionKeepsCustomSelectableTemplateBound()
        {
            const string customKey = "RimTestSelectableAliasTarget";
            DiaryPromptTemplateDef alias = new DiaryPromptTemplateDef
            {
                defName = customKey,
                label = "hidden alias",
                templateKey = "RimTestDifferentTemplateKey",
                playerSelectable = false,
                systemPrompt = "DEF_NAME_ALIAS_MUST_NOT_BIND",
                maxTokens = 992
            };
            DiaryPromptTemplateDef selectable = new DiaryPromptTemplateDef
            {
                defName = "PawnDiary_RimTest_SelectableAliasTarget",
                label = "selectable alias target",
                description = "Fixture selectable row behind a defName alias.",
                templateKey = customKey,
                playerSelectable = true,
                playerOrder = 778,
                systemPrompt = "SELECTABLE_ALIAS_TARGET_SYSTEM",
                finalInstruction = "SELECTABLE_ALIAS_TARGET_FINAL",
                maxTokens = 278,
                fields = new List<DiaryPromptFieldDef>
                {
                    new DiaryPromptFieldDef { label = "facts", source = "PovText" }
                }
            };
            List<DiaryPromptTemplateDef> defs =
                DefDatabase<DiaryPromptTemplateDef>.AllDefsListForReading;
            defs.Insert(0, alias);
            defs.Add(selectable);
            scope.RegisterCleanup(() =>
            {
                defs.Remove(selectable);
                defs.Remove(alias);
            });

            List<PlayerEntryTemplateSnapshot> uiRows = DiaryPlayerPromptTemplates.ForUi();
            DiaryPolicySnapshot policy = DiaryPipelineAdapters.PolicyFor(
                new DiaryEventPayload
                {
                    defName = "PlayerPersonal",
                    playerEntryTypeKey = "Personal",
                    solo = true,
                    display = new DiaryDisplayPayload { important = false }
                });
            PlayerEntryTemplateSnapshot uiRow = uiRows.Find(row => string.Equals(
                row?.templateKey, customKey, StringComparison.OrdinalIgnoreCase));
            DiaryTemplatePolicy copied = policy.templates.Find(row => string.Equals(
                row?.templateKey, customKey, StringComparison.OrdinalIgnoreCase));
            PawnDiaryRimTestScope.Require(
                uiRow != null
                    && uiRow.displayOrder == selectable.playerOrder
                    && copied != null
                    && copied.playerSelectable
                    && copied.playerOrder == selectable.playerOrder
                    && copied.maxTokens == selectable.maxTokens
                    && string.Equals(
                        copied.systemPrompt, selectable.systemPrompt, StringComparison.Ordinal)
                    && copied.fields.Count == 1
                    && string.Equals(
                        copied.fields[0].source, "PovText", StringComparison.Ordinal),
                "A defName alias rebound a custom UI template to the wrong loaded Def.");
        }

        /// <summary>A usable forced model wins the requested row; unknown/disabled matches fall back.</summary>
        [Test]
        public static void InternalCompletionEndpointSelectionHonorsForcedModel()
        {
            PawnDiarySettings settings = new PawnDiarySettings
            {
                apiEndpoints = new List<ApiEndpointConfig>
                {
                    new ApiEndpointConfig("https://disabled.invalid/v1", string.Empty, "forced-model")
                    {
                        enabled = false
                    },
                    new ApiEndpointConfig("https://requested.invalid/v1", string.Empty, "requested-model")
                    {
                        enabled = true,
                        contextDetailOverride = PromptContextDetailOverride.Compact
                    },
                    new ApiEndpointConfig("https://forced.invalid/v1", string.Empty, "forced-model")
                    {
                        enabled = true,
                        contextDetailOverride = PromptContextDetailOverride.Full
                    }
                }
            };

            ExternalLlmEndpointSelection forced =
                ExternalLlmCompletionService.ResolveEndpointSelectionSnapshot(
                    settings, 1, " FORCED-MODEL ");
            ExternalLlmEndpointSelection unknown =
                ExternalLlmCompletionService.ResolveEndpointSelectionSnapshot(
                    settings, 1, "missing-model");
            PawnDiaryRimTestScope.Require(
                forced != null
                    && forced.laneIndex == 2
                    && forced.endpoint != settings.apiEndpoints[2]
                    && string.Equals(forced.endpoint.model, "forced-model", StringComparison.Ordinal)
                    && forced.endpoint.contextDetailOverride == PromptContextDetailOverride.Full
                    && unknown != null
                    && unknown.laneIndex == 1
                    && string.Equals(
                        unknown.endpoint.model, "requested-model", StringComparison.Ordinal),
                "Internal composer endpoint selection ignored a usable forced model or lost fallback semantics.");
        }

        /// <summary>
        /// Reset must restore the internal trusted completion boundary itself. Comparing delegates is
        /// hermetic: the test never invokes the completion method, resolves a lane, or starts network work.
        /// </summary>
        [Test]
        public static void PlayerEntryDraftResetRestoresTrustedCompletionBoundary()
        {
            DiaryGameComponent.BeginDraftCompletion = (ignoredRequest, ignoredSettings) => 0;
            DiaryGameComponent.ResetPlayerEntryDraftTestSeams();

            Func<ExternalLlmCompletionRequest, PawnDiarySettings, int> expected =
                ExternalLlmCompletionService.BeginTrusted;
            Func<ExternalLlmCompletionRequest, PawnDiarySettings, int> actual =
                DiaryGameComponent.BeginDraftCompletion;
            PawnDiaryRimTestScope.Require(
                actual != null
                    && actual == expected
                    && actual.Target == null
                    && actual.Method.IsAssembly
                    && !actual.Method.IsPublic,
                "ResetPlayerEntryDraftTestSeams did not restore the internal BeginTrusted completion boundary.");
        }

        /// <summary>
        /// Player categories own generic guidance. They must never inherit the capture assumptions of the
        /// display domains they resemble: Personal is not necessarily a social interaction, Combat is not
        /// necessarily a raid, Colony is not necessarily a quest, and Reflection is not necessarily daily.
        /// </summary>
        [Test]
        public static void ContextDraftTypesDoNotLeakCaptureSpecificGuidance()
        {
            scope.SpawnAsLiveColonist(firstPawn);
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            bool oldIntegrations = settings.allowExternalIntegrations;
            int oldMaxTokens = settings.maxTokens;
            settings.allowExternalIntegrations = false;
            settings.maxTokens = 137;
            scope.RegisterCleanup(() =>
            {
                settings.allowExternalIntegrations = oldIntegrations;
                settings.maxTokens = oldMaxTokens;
            });

            string[,] cases =
            {
                { "Personal", "PlayerPersonal", "GENERIC_PLAYER_PERSONAL_GUIDANCE" },
                { "Combat", "PlayerCombat", "GENERIC_PLAYER_COMBAT_GUIDANCE" },
                { "Colony", "PlayerColony", "GENERIC_PLAYER_COLONY_GUIDANCE" },
                { "Reflection", "PlayerReflection", "GENERIC_PLAYER_REFLECTION_GUIDANCE" }
            };
            string[,] forbiddenOverrides =
            {
                { "Interaction", "CAPTURE_SOCIAL_INTERACTION_MUST_NOT_LEAK" },
                { "Raid", "CAPTURE_RAID_ARRIVAL_STRATEGY_MUST_NOT_LEAK" },
                { "Quest", "CAPTURE_QUEST_LIFECYCLE_REWARD_MUST_NOT_LEAK" },
                { "DayReflection", "CAPTURE_END_OF_DAY_MUST_NOT_LEAK" }
            };
            for (int i = 0; i < forbiddenOverrides.GetLength(0); i++)
                OverrideEventPromptForTest(
                    settings, forbiddenOverrides[i, 0], forbiddenOverrides[i, 1]);
            for (int i = 0; i < cases.GetLength(0); i++)
                OverrideEventPromptForTest(settings, cases[i, 1], cases[i, 2]);

            List<ExternalLlmCompletionRequest> captured =
                new List<ExternalLlmCompletionRequest>();
            int nextHandle = 700;
            DiaryGameComponent.ResolveDraftEndpoint = (ignoredSettings, ignoredLane, ignoredForcedModel) =>
                new ExternalLlmEndpointSelection
                {
                    laneIndex = ignoredLane >= 0 ? ignoredLane : 0,
                    endpoint = new ApiEndpointConfig(
                        "https://fixture.invalid/v1", string.Empty, "fixture-model")
                    {
                        enabled = true,
                        contextDetailOverride = PromptContextDetailOverride.Compact
                    }
                };
            DiaryGameComponent.BeginDraftCompletion = (request, ignoredSettings) =>
            {
                captured.Add(request);
                return ++nextHandle;
            };
            DiaryGameComponent.CancelDraftCompletion = ignoredHandle => true;

            PawnDiaryRecord record = scope.RequireDiaryRecord(firstPawn);
            int eventCount = Events().Count;
            int archiveCount = Archive().Count;
            int referenceCount = record.eventIds.Count;
            int unreadCount = record.unreadGeneratedEntryCount;
            PawnKnowledgeState knowledgeState = record.KnowledgeStateOrNull();
            int knowledgeCount = knowledgeState?.records?.Count ?? 0;
            int version = DiaryStateVersion.Current;
            string[] forbiddenNaturalLanguage =
            {
                "supplied social text",
                "arrival mode and strategy",
                "quest lifecycle",
                "invent rewards",
                "end-of-day"
            };

            for (int i = 0; i < cases.GetLength(0); i++)
            {
                PlayerEntryDraftStartResult started = scope.Component.StartPlayerEntryDraft(
                    firstPawn,
                    new PlayerEntryComposerRequest
                    {
                        mode = PlayerEntryComposerMode.Context,
                        entryTypeKey = cases[i, 0],
                        templateKey = DiaryPipelineTemplates.SoloDefault,
                        factualSummary = "PLAYER_SUPPLIED_FACTS_" + cases[i, 0],
                        maxTokens = PlayerEntryComposerPolicy.UseTemplateOrSettingsMaxTokens
                    });
                ExternalLlmCompletionRequest prompt = captured.Count == i + 1 ? captured[i] : null;
                bool clean = prompt != null
                    && prompt.userText.IndexOf(cases[i, 2], StringComparison.Ordinal) >= 0;
                if (clean)
                {
                    for (int forbidden = 0; forbidden < forbiddenOverrides.GetLength(0); forbidden++)
                    {
                        clean &= prompt.userText.IndexOf(
                            forbiddenOverrides[forbidden, 1], StringComparison.Ordinal) < 0;
                    }
                    for (int forbidden = 0; forbidden < forbiddenNaturalLanguage.Length; forbidden++)
                    {
                        clean &= prompt.userText.IndexOf(
                            forbiddenNaturalLanguage[forbidden],
                            StringComparison.OrdinalIgnoreCase) < 0;
                    }
                }

                PawnDiaryRimTestScope.Require(
                    started != null
                        && started.accepted
                        && string.Equals(started.entryTypeKey, cases[i, 0], StringComparison.Ordinal)
                        && prompt != null
                        && prompt.maxTokens == 137
                        && clean
                        && scope.Component.CancelPlayerEntryDraft(started.handle),
                    "Context draft inherited capture-specific guidance for " + cases[i, 0] + ".");
            }

            RequireNoComposerPersistence(
                record,
                eventCount,
                archiveCount,
                referenceCount,
                unreadCount,
                knowledgeState,
                knowledgeCount,
                version,
                null,
                "capturing generic player-type prompt guidance");
        }

        /// <summary>
        /// Context generation sends the selected template plus current pawn, voice, surroundings,
        /// continuity, event guidance, and read-only past to the completion seam. The returned prose is
        /// still only a review draft until the ordinary typed Save commits it.
        /// </summary>
        [Test]
        public static void ContextDraftCapturesRichPromptButMutatesOnlyOnReviewedSave()
        {
            scope.SpawnAsLiveColonist(firstPawn);
            scope.SpawnAsLiveColonist(secondPawn);
            PawnDiaryRecord record = scope.RequireDiaryRecord(firstPawn);
            record.customWritingStyleRule = "CONTEXT_PERSONA_SENTINEL";
            PawnDiaryRimTestScope.Require(
                scope.Component.TrySetBackgroundMemoryForProfile(
                    firstPawn, "CONTEXT_RELEVANT_PAST_SENTINEL"),
                "Could not seed the read-only relevant-past context fixture.");

            DiaryEvent previous = RecordSolo(firstPawn, "Previous context source.");
            previous.MarkInjectedTextComplete(
                DiaryEvent.InitiatorRole,
                "CONTEXT_LAST_OPENER_SENTINEL. CONTEXT_PREVIOUS_ENDING_SENTINEL.");
            previous.MarkTitleComplete(DiaryEvent.InitiatorRole, "Previous context title");

            PawnDiarySettings settings = PawnDiaryMod.Settings;
            int oldActive = settings.maxActiveDiaryEvents;
            int oldArchive = settings.maxArchivedDiaryEvents;
            bool oldIntegrations = settings.allowExternalIntegrations;
            settings.maxActiveDiaryEvents = PawnDiarySettings.MinActiveDiaryEvents;
            settings.maxArchivedDiaryEvents = Math.Max(4, oldArchive);
            // Player-invoked generation is a first-party composer action, so the global external
            // integration bridge switch must not disable its explicitly requested provider call.
            settings.allowExternalIntegrations = false;
            scope.RegisterCleanup(() =>
            {
                settings.maxActiveDiaryEvents = oldActive;
                settings.maxArchivedDiaryEvents = oldArchive;
                settings.allowExternalIntegrations = oldIntegrations;
            });

            const string combatPromptKey = "PlayerCombat";
            DiaryEventPromptDef combatPrompt = DiaryEventPrompts.ForKey(combatPromptKey);
            string combatXmlPrompt = combatPrompt?.prompt ?? string.Empty;
            bool hadCombatOverride = settings.eventPromptOverrides.HasOverride(combatPromptKey);
            string oldCombatPrompt = settings.eventPromptOverrides.Effective(
                combatPromptKey, combatXmlPrompt);
            settings.eventPromptOverrides.Set(
                combatPromptKey, "CONTEXT_EVENT_GUIDANCE_SENTINEL", combatXmlPrompt);
            string combatXmlForcedModel = combatPrompt?.forcedModel ?? string.Empty;
            bool hadCombatForcedModelOverride =
                settings.eventForcedModelOverrides.HasOverride(combatPromptKey);
            string oldCombatForcedModel = settings.eventForcedModelOverrides.Effective(
                combatPromptKey, combatXmlForcedModel);
            const string forcedComposerModel = "fixture-forced-composer-model";
            settings.eventForcedModelOverrides.Set(
                combatPromptKey, forcedComposerModel, combatXmlForcedModel);
            scope.RegisterCleanup(() =>
            {
                if (hadCombatOverride)
                    settings.eventPromptOverrides.Set(
                        combatPromptKey, oldCombatPrompt, combatXmlPrompt);
                else
                    settings.eventPromptOverrides.Reset(combatPromptKey);
                if (hadCombatForcedModelOverride)
                    settings.eventForcedModelOverrides.Set(
                        combatPromptKey, oldCombatForcedModel, combatXmlForcedModel);
                else
                    settings.eventForcedModelOverrides.Reset(combatPromptKey);
            });

            List<DiaryEntryStatusSnapshot> notifications = new List<DiaryEntryStatusSnapshot>();
            string listenerId = "PawnDiary.RimTest.ContextDraft." + Guid.NewGuid().ToString("N");
            PawnDiaryApi.RegisterEntryStatusListener(listenerId, snapshot => notifications.Add(snapshot));
            scope.RegisterCleanup(() => PawnDiaryApi.UnregisterEntryStatusListener(listenerId));

            ExternalLlmCompletionRequest captured = null;
            List<string> forcedModelLookups = new List<string>();
            DiaryGameComponent.ResolveDraftEndpoint = (ignoredSettings, ignoredLane, forcedModel) =>
            {
                forcedModelLookups.Add(forcedModel ?? string.Empty);
                bool forced = string.Equals(
                    forcedModel, forcedComposerModel, StringComparison.OrdinalIgnoreCase);
                return new ExternalLlmEndpointSelection
                {
                    laneIndex = forced ? 4 : (ignoredLane >= 0 ? ignoredLane : 0),
                    endpoint = new ApiEndpointConfig(
                        "https://fixture.invalid/v1",
                        string.Empty,
                        forced ? forcedComposerModel : "fixture-initial-model")
                    {
                        enabled = true,
                        contextDetailOverride = forced
                            ? PromptContextDetailOverride.Full
                            : PromptContextDetailOverride.Compact
                    }
                };
            };
            DiaryGameComponent.BeginDraftCompletion = (request, ignoredSettings) =>
            {
                captured = request;
                return 401;
            };

            int eventCount = Events().Count;
            int archiveCount = Archive().Count;
            int referenceCount = record.eventIds.Count;
            int unreadCount = record.unreadGeneratedEntryCount;
            PawnKnowledgeState knowledgeState = record.KnowledgeStateOrNull();
            int knowledgeCount = knowledgeState?.records?.Count ?? 0;
            int version = DiaryStateVersion.Current;
            string pawnSummary = DiaryContextBuilder.BuildPawnSummary(firstPawn);
            string surroundings = DiaryContextBuilder.BuildSurroundingsSummary(firstPawn);
            string identity = DiaryContextBuilder.BuildIdentitySummary(firstPawn, null);

            PlayerEntryDraftStartResult started = scope.Component.StartPlayerEntryDraft(
                firstPawn,
                new PlayerEntryComposerRequest
                {
                    mode = PlayerEntryComposerMode.Context,
                    entryTypeKey = "Combat",
                    templateKey = DiaryPipelineTemplates.SoloImportant,
                    factualSummary = "CONTEXT_FACTUAL_SUMMARY_SENTINEL",
                    customInstruction = "CONTEXT_CUSTOM_INSTRUCTION_SENTINEL",
                    laneIndex = 7,
                    maxTokens = PlayerEntryComposerPolicy.UseTemplateOrSettingsMaxTokens
                });

            string templateSystem = DiaryPromptTemplates.SystemPromptFor(
                DiaryPipelineTemplates.SoloImportant);
            PawnDiaryRimTestScope.Require(
                started != null
                    && started.accepted
                    && started.handle == 401
                    && string.Equals(started.entryTypeKey, "Combat", StringComparison.Ordinal)
                    && string.Equals(started.templateKey,
                        DiaryPipelineTemplates.SoloImportant, StringComparison.Ordinal)
                    && captured != null
                    && string.Equals(captured.sourceId,
                        "PawnDiary.PlayerComposer", StringComparison.Ordinal)
                    && captured.laneIndex == 4
                    && captured.maxTokens == 200
                    && forcedModelLookups.Count == 2
                    && string.IsNullOrEmpty(forcedModelLookups[0])
                    && string.Equals(
                        forcedModelLookups[1], forcedComposerModel, StringComparison.Ordinal)
                    && captured.systemPrompt.Contains(templateSystem)
                    && captured.systemPrompt.Contains("CONTEXT_PERSONA_SENTINEL")
                    && captured.userText.Contains("CONTEXT_FACTUAL_SUMMARY_SENTINEL")
                    && captured.userText.Contains("CONTEXT_CUSTOM_INSTRUCTION_SENTINEL")
                    && captured.userText.Contains("CONTEXT_EVENT_GUIDANCE_SENTINEL")
                    && captured.userText.Contains(firstPawn.LabelShortCap)
                    && !string.IsNullOrWhiteSpace(pawnSummary)
                    && captured.userText.Contains(pawnSummary)
                    && !string.IsNullOrWhiteSpace(surroundings)
                    && captured.userText.Contains(surroundings)
                    && !string.IsNullOrWhiteSpace(identity)
                    && captured.userText.Contains(identity)
                    && captured.userText.Contains("CONTEXT_LAST_OPENER_SENTINEL")
                    && captured.userText.Contains("CONTEXT_PREVIOUS_ENDING_SENTINEL")
                    && captured.userText.Contains("CONTEXT_RELEVANT_PAST_SENTINEL"),
                "Context draft did not send the selected rich, read-only prompt envelope.");
            RequireNoComposerPersistence(
                record,
                eventCount,
                archiveCount,
                referenceCount,
                unreadCount,
                knowledgeState,
                knowledgeCount,
                version,
                notifications,
                "starting a context draft");

            DiaryGameComponent.PollDraftCompletion = handle =>
                new LlmCompletionResult
                {
                    status = handle == 401
                        ? LlmCompletionStatus.Succeeded
                        : LlmCompletionStatus.Unknown,
                    text = "  REVIEWED_DRAFT_FIRST.\r\n\r\nREVIEWED_DRAFT_SECOND.  "
                };
            PlayerEntryDraftPollResult completed = scope.Component.PollPlayerEntryDraft(401);
            PawnDiaryRimTestScope.Require(
                completed != null
                    && completed.status == PlayerEntryDraftStatus.Succeeded
                    && string.Equals(completed.text,
                        "REVIEWED_DRAFT_FIRST.\nREVIEWED_DRAFT_SECOND.",
                        StringComparison.Ordinal),
                "A successful transient completion did not become a clean review-only body.");
            RequireNoComposerPersistence(
                record,
                eventCount,
                archiveCount,
                referenceCount,
                unreadCount,
                knowledgeState,
                knowledgeCount,
                version,
                notifications,
                "polling a successful context draft");

            string savedEventId;
            PawnDiaryRimTestScope.Require(
                scope.Component.TryCreateManualEntry(
                    firstPawn,
                    completed.text,
                    "Reviewed title",
                    "Combat",
                    started.entryTypeKey,
                    out savedEventId),
                "Explicit Save rejected the reviewed context draft.");
            DiaryEvent saved = Events().FindEvent(savedEventId);
            PawnDiaryRimTestScope.Require(
                saved != null
                    && string.Equals(saved.initiatorGeneratedText,
                        completed.text, StringComparison.Ordinal)
                    && string.Equals(saved.EntryTypeKeyForRole(DiaryEvent.InitiatorRole),
                        "Combat", StringComparison.Ordinal)
                    && record.unreadGeneratedEntryCount == unreadCount
                    && ReferenceEquals(record.KnowledgeStateOrNull(), knowledgeState)
                    && (record.KnowledgeStateOrNull()?.records?.Count ?? 0) == knowledgeCount
                    && notifications.Count == 0,
                "Reviewed Save lost its prose/type, created unread/knowledge, or bypassed the disabled integration switch.");

            notifications.Clear();
            string laterId;
            PawnDiaryRimTestScope.Require(
                scope.Component.TryCreateManualEntry(
                    firstPawn,
                    "A later page forces ordinary retention.",
                    "Later",
                    "Personal",
                    "Personal",
                    out laterId),
                "Could not stage retention after the reviewed Save.");
            ArchivedDiaryEntry retained = Archive().Find(
                savedEventId, firstPawn.GetUniqueLoadID(), DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(
                retained != null
                    && string.Equals(retained.generatedText,
                        completed.text, StringComparison.Ordinal)
                    && string.Equals(retained.entryTypeKey, "Combat", StringComparison.Ordinal),
                "The explicitly saved reviewed draft did not survive ordinary archive retention.");
        }

        /// <summary>
        /// Full Prompt is a strict raw envelope: no persona, pawn, context, continuity, template, or
        /// event guidance is injected. Cancel/failure/session-reset outcomes remain discardable and a
        /// fresh request can retry without leaving any canonical page.
        /// </summary>
        [Test]
        public static void RawDraftIsExactAndCancelFailureUnknownRemainRetryable()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            bool oldIntegrations = settings.allowExternalIntegrations;
            settings.allowExternalIntegrations = false;
            scope.RegisterCleanup(() => settings.allowExternalIntegrations = oldIntegrations);

            PawnDiaryRecord record = scope.RequireDiaryRecord(firstPawn);
            record.customWritingStyleRule = "RAW_PERSONA_MUST_NOT_LEAK";
            DiaryEvent previous = RecordSolo(firstPawn, "RAW_CONTEXT_MUST_NOT_LEAK");
            previous.MarkInjectedTextComplete(
                DiaryEvent.InitiatorRole, "RAW_CONTINUITY_MUST_NOT_LEAK.");

            List<ExternalLlmCompletionRequest> captured =
                new List<ExternalLlmCompletionRequest>();
            int nextHandle = 500;
            int canceledHandle = 0;
            DiaryGameComponent.ResolveDraftEndpoint = (ignoredSettings, ignoredLane, ignoredForcedModel) =>
                new ExternalLlmEndpointSelection
                {
                    laneIndex = ignoredLane >= 0 ? ignoredLane : 0,
                    endpoint = new ApiEndpointConfig(
                        "https://fixture.invalid/v1", string.Empty, "fixture-model")
                    {
                        enabled = true,
                        contextDetailOverride = PromptContextDetailOverride.Full
                    }
                };
            DiaryGameComponent.BeginDraftCompletion = (completionRequest, ignoredSettings) =>
            {
                captured.Add(completionRequest);
                return ++nextHandle;
            };
            DiaryGameComponent.CancelDraftCompletion = handle =>
            {
                canceledHandle = handle;
                return true;
            };

            int eventCount = Events().Count;
            int archiveCount = Archive().Count;
            int referenceCount = record.eventIds.Count;
            int unreadCount = record.unreadGeneratedEntryCount;
            PawnKnowledgeState knowledgeState = record.KnowledgeStateOrNull();
            int knowledgeCount = knowledgeState?.records?.Count ?? 0;
            int version = DiaryStateVersion.Current;
            const string rawSystem = "  RAW_SYSTEM\r\n\tKEEP\u0000_THIS  ";
            const string rawUser = "\n RAW_USER\u0001_KEEP_EXACT \r\n";
            PlayerEntryComposerRequest request = new PlayerEntryComposerRequest
            {
                mode = PlayerEntryComposerMode.FullPrompt,
                entryTypeKey = "Reflection",
                templateKey = DiaryPipelineTemplates.SoloInternalState,
                factualSummary = "RAW_FACTS_MUST_NOT_LEAK",
                customInstruction = "RAW_INSTRUCTION_MUST_NOT_LEAK",
                title = "RAW_TITLE_MUST_NOT_LEAK",
                body = "RAW_BODY_MUST_NOT_LEAK",
                systemPrompt = rawSystem,
                userPrompt = rawUser,
                laneIndex = 2,
                maxTokens = 222
            };

            PlayerEntryDraftStartResult first = scope.Component.StartPlayerEntryDraft(firstPawn, request);
            string expectedSystem = PlayerEntryComposerPolicy.CleanRawPrompt(
                rawSystem, PlayerEntryComposerPolicy.RawPromptMaxCharacters);
            string expectedUser = PlayerEntryComposerPolicy.CleanRawPrompt(
                rawUser, PlayerEntryComposerPolicy.RawPromptMaxCharacters);
            PawnDiaryRimTestScope.Require(
                first != null
                    && first.accepted
                    && first.handle == 501
                    && string.IsNullOrEmpty(first.templateKey)
                    && captured.Count == 1
                    && string.Equals(captured[0].systemPrompt,
                        expectedSystem, StringComparison.Ordinal)
                    && string.Equals(captured[0].userText,
                        expectedUser, StringComparison.Ordinal)
                    && captured[0].laneIndex == 2
                    && captured[0].maxTokens == 222
                    && !captured[0].systemPrompt.Contains("RAW_PERSONA_MUST_NOT_LEAK")
                    && !captured[0].userText.Contains("RAW_CONTEXT_MUST_NOT_LEAK")
                    && !captured[0].userText.Contains("RAW_CONTINUITY_MUST_NOT_LEAK")
                    && !captured[0].userText.Contains("RAW_FACTS_MUST_NOT_LEAK")
                    && !captured[0].userText.Contains("RAW_INSTRUCTION_MUST_NOT_LEAK")
                    && !captured[0].userText.Contains("RAW_TITLE_MUST_NOT_LEAK")
                    && !captured[0].userText.Contains("RAW_BODY_MUST_NOT_LEAK"),
                "Full Prompt added context/template data or changed the sanitized raw envelope.");
            RequireNoComposerPersistence(
                record,
                eventCount,
                archiveCount,
                referenceCount,
                unreadCount,
                knowledgeState,
                knowledgeCount,
                version,
                null,
                "starting a raw draft");

            PawnDiaryRimTestScope.Require(
                scope.Component.CancelPlayerEntryDraft(first.handle)
                    && canceledHandle == first.handle,
                "Cancel did not discard the exact transient raw handle.");
            RequireNoComposerPersistence(
                record,
                eventCount,
                archiveCount,
                referenceCount,
                unreadCount,
                knowledgeState,
                knowledgeCount,
                version,
                null,
                "canceling a raw draft");

            PlayerEntryDraftStartResult failedStart =
                scope.Component.StartPlayerEntryDraft(firstPawn, request);
            DiaryGameComponent.PollDraftCompletion = handle =>
            {
                if (handle == failedStart.handle)
                {
                    return new LlmCompletionResult
                    {
                        status = LlmCompletionStatus.Failed,
                        error = "fixture failure"
                    };
                }
                return new LlmCompletionResult { status = LlmCompletionStatus.Unknown };
            };
            PlayerEntryDraftPollResult failed =
                scope.Component.PollPlayerEntryDraft(failedStart.handle);
            PawnDiaryRimTestScope.Require(
                failed.status == PlayerEntryDraftStatus.Failed
                    && string.Equals(failed.error, "fixture failure", StringComparison.Ordinal)
                    && string.IsNullOrEmpty(failed.text),
                "A failed draft did not surface a retryable transient failure.");

            PlayerEntryDraftStartResult resetStart =
                scope.Component.StartPlayerEntryDraft(firstPawn, request);
            PlayerEntryDraftPollResult unknown =
                scope.Component.PollPlayerEntryDraft(resetStart.handle);
            PawnDiaryRimTestScope.Require(
                resetStart.accepted
                    && unknown.status == PlayerEntryDraftStatus.Unknown
                    && string.IsNullOrEmpty(unknown.text)
                    && string.IsNullOrEmpty(unknown.error),
                "A session-reset/unknown handle did not remain a clean retryable UI failure.");

            PlayerEntryDraftStartResult retry =
                scope.Component.StartPlayerEntryDraft(firstPawn, request);
            PawnDiaryRimTestScope.Require(
                retry.accepted
                    && retry.handle > resetStart.handle
                    && captured.Count == 4
                    && scope.Component.CancelPlayerEntryDraft(retry.handle),
                "Failure/unknown did not permit a fresh transient retry.");
            RequireNoComposerPersistence(
                record,
                eventCount,
                archiveCount,
                referenceCount,
                unreadCount,
                knowledgeState,
                knowledgeCount,
                version,
                null,
                "failure, unknown, and retry paths");
        }

        /// <summary>
        /// Legacy/provider pages may exceed today's editor caps. Editing one field must preserve the
        /// untouched sibling byte-for-byte instead of silently truncating it at the current XML limit.
        /// </summary>
        [Test]
        public static void OneFieldEditPreservesOverCapSiblingByteExact()
        {
            scope.Component.SetDiaryGenerationEnabled(firstPawn, false);
            string oversizedBody = new string('B', DiaryGameComponent.ManualEntryBodyMaxCharacters + 37)
                + "\r\nlegacy trailer  ";
            DiaryEvent bodyPage = RecordSolo(firstPawn, "Legacy body source.");
            bodyPage.MarkInjectedTextComplete(DiaryEvent.InitiatorRole, oversizedBody);
            bodyPage.MarkTitleComplete(DiaryEvent.InitiatorRole, "Old short title");
            ManualDiaryEntrySnapshot bodyOpened = RequireSnapshot(firstPawn, bodyPage);

            PawnDiaryRimTestScope.Require(
                scope.Component.TryEditManualEntry(bodyOpened, oversizedBody, "New short title")
                    && string.Equals(bodyPage.initiatorGeneratedText,
                        oversizedBody, StringComparison.Ordinal)
                    && string.Equals(bodyPage.initiatorTitle,
                        "New short title", StringComparison.Ordinal),
                "A title-only edit truncated or normalized the legacy over-cap body.");

            string oversizedTitle = new string('T', DiaryGameComponent.ManualEntryTitleMaxCharacters + 19)
                + "  ";
            DiaryEvent titlePage = RecordSolo(firstPawn, "Legacy title source.");
            titlePage.MarkInjectedTextComplete(DiaryEvent.InitiatorRole, "Old short body.");
            titlePage.MarkTitleComplete(DiaryEvent.InitiatorRole, oversizedTitle);
            ManualDiaryEntrySnapshot titleOpened = RequireSnapshot(firstPawn, titlePage);

            PawnDiaryRimTestScope.Require(
                scope.Component.TryEditManualEntry(titleOpened, "New short body.", oversizedTitle)
                    && string.Equals(titlePage.initiatorGeneratedText,
                        "New short body.", StringComparison.Ordinal)
                    && string.Equals(titlePage.initiatorTitle,
                        oversizedTitle, StringComparison.Ordinal),
                "A body-only edit truncated or normalized the legacy over-cap title.");
        }

        /// <summary>
        /// A genuinely archive-only page has no hot setter available, so the archive adapter itself must
        /// clear stale/provider attribution, preserve identity and chronology, and invalidate once.
        /// </summary>
        [Test]
        public static void ArchiveOnlyEditIsCanonicalWithoutHotBacking()
        {
            scope.Component.SetDiaryGenerationEnabled(firstPawn, false);
            DiaryEvent page = RecordSolo(firstPawn, "Archived source facts.");
            page.MarkInjectedTextComplete(DiaryEvent.InitiatorRole, "Old archived prose.");
            page.SetLlmMeta(
                DiaryEvent.InitiatorRole,
                "https://provider.invalid/v1",
                "archive-provider-model");
            page.MarkTitleComplete(DiaryEvent.InitiatorRole, "Old archive title");

            string pawnId = firstPawn.GetUniqueLoadID();
            ArchivedDiaryEntry archived = ArchivedDiaryEntry.FromEvent(
                page, pawnId, page.ToViewFor(pawnId), true);
            PawnDiaryRimTestScope.Require(
                archived != null && Archive().AddOrKeep(archived),
                "Could not stage the archive-only canonical-edit row.");
            scope.RegisterCleanup(() => Archive().RemoveForEventIds(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { page.eventId }));

            PawnDiaryRecord record = scope.RequireDiaryRecord(firstPawn);
            record.eventIds.Remove(page.eventId);
            Events().RemoveEvent(page.eventId);
            PawnDiaryRimTestScope.Require(!Events().ContainsEvent(page.eventId),
                "Archive-only fixture unexpectedly retained a hot backing event.");

            string archiveKey = archived.ArchiveKey;
            int originalTick = archived.tick;
            string originalDate = archived.date;
            ManualDiaryEntrySnapshot opened = RequireSnapshot(
                pawnId, page.eventId, DiaryEvent.InitiatorRole);
            int before = DiaryStateVersion.Current;

            PawnDiaryRimTestScope.Require(
                opened.Archived
                    && scope.Component.TryEditManualEntry(
                        opened,
                        "  Canonical archive first.\r\n\r\nCanonical archive second.  ",
                        " Edited archive title ")
                    && string.Equals(archived.ArchiveKey, archiveKey, StringComparison.Ordinal)
                    && archived.tick == originalTick
                    && string.Equals(archived.date, originalDate, StringComparison.Ordinal)
                    && string.Equals(archived.generatedText,
                        "Canonical archive first.\nCanonical archive second.", StringComparison.Ordinal)
                    && string.Equals(archived.title,
                        "Edited archive title", StringComparison.Ordinal)
                    && DiaryEvent.RoleEquals(archived.status, DiaryEvent.CompleteStatus)
                    && string.IsNullOrEmpty(archived.llmModel)
                    && !archived.archivedGenerationStale
                    && !record.eventIds.Contains(page.eventId)
                    && DiaryStateVersion.Current == before + 1,
                "Archive-only edit changed identity/chronology or kept stale provider lifecycle state.");
        }

        private static DiaryEvent RecordSolo(Pawn pawn, string rawText)
        {
            DiaryEvent diaryEvent = scope.Component.AddSoloEvent(
                pawn,
                null,
                FixtureDefName,
                "manual edit fixture",
                rawText,
                string.Empty,
                "rimtest_manual_edit=true");
            PawnDiaryRimTestScope.Require(diaryEvent != null,
                "The manual-entry fixture could not create a hot page.");
            return diaryEvent;
        }

        private static ManualDiaryEntrySnapshot RequireSnapshot(Pawn pawn, DiaryEvent diaryEvent)
        {
            return RequireSnapshot(
                pawn.GetUniqueLoadID(),
                diaryEvent.eventId,
                DiaryEvent.InitiatorRole);
        }

        private static ManualDiaryEntrySnapshot RequireSnapshot(
            string pawnId,
            string eventId,
            string povRole)
        {
            ManualDiaryEntrySnapshot snapshot;
            PawnDiaryRimTestScope.Require(
                scope.Component.TryGetManualEntrySnapshot(pawnId, eventId, povRole, out snapshot)
                    && snapshot != null,
                "Could not resolve the exact manual-entry editor snapshot.");
            return snapshot;
        }

        private static void ApplyResult(LlmGenerationResult result)
        {
            ApplyLlmResultMethod.Invoke(scope.Component, new object[] { result });
        }

        private static DiaryEventRepository Events()
        {
            return EventsField.GetValue(scope.Component) as DiaryEventRepository;
        }

        private static DiaryArchiveRepository Archive()
        {
            return ArchiveField.GetValue(scope.Component) as DiaryArchiveRepository;
        }

        private static void OverrideEventPromptForTest(
            PawnDiarySettings settings,
            string promptKey,
            string replacement)
        {
            DiaryEventPromptDef prompt = DiaryEventPrompts.ForKey(promptKey);
            PawnDiaryRimTestScope.Require(prompt != null,
                "Could not resolve the event-prompt override fixture key " + promptKey + ".");
            string xmlValue = prompt.prompt ?? string.Empty;
            bool hadOverride = settings.eventPromptOverrides.HasOverride(promptKey);
            string oldValue = settings.eventPromptOverrides.Effective(promptKey, xmlValue);
            settings.eventPromptOverrides.Set(promptKey, replacement, xmlValue);
            scope.RegisterCleanup(() =>
            {
                if (hadOverride)
                    settings.eventPromptOverrides.Set(promptKey, oldValue, xmlValue);
                else
                    settings.eventPromptOverrides.Reset(promptKey);
            });
        }

        private static void RequireNoComposerPersistence(
            PawnDiaryRecord record,
            int eventCount,
            int archiveCount,
            int referenceCount,
            int unreadCount,
            PawnKnowledgeState knowledgeState,
            int knowledgeCount,
            int version,
            List<DiaryEntryStatusSnapshot> notifications,
            string action)
        {
            PawnDiaryRimTestScope.Require(
                Events().Count == eventCount
                    && Archive().Count == archiveCount
                    && record.eventIds.Count == referenceCount
                    && record.unreadGeneratedEntryCount == unreadCount
                    && ReferenceEquals(record.KnowledgeStateOrNull(), knowledgeState)
                    && (record.KnowledgeStateOrNull()?.records?.Count ?? 0) == knowledgeCount
                    && DiaryStateVersion.Current == version
                    && (notifications == null || notifications.Count == 0),
                "The transient composer changed page/archive/ownership/unread/knowledge/render/listener state while "
                    + action + ".");
        }

        /// <summary>Removes corpse/world-pawn holders left by a real Pawn.Kill fixture.</summary>
        private static void RegisterDeadPawnCleanup(Pawn pawn)
        {
            scope.RegisterCleanup(() =>
            {
                if (pawn != null
                    && !pawn.Destroyed
                    && Find.WorldPawns != null
                    && Find.WorldPawns.Contains(pawn))
                {
                    Find.WorldPawns.RemovePawn(pawn);
                }
            });
            scope.RegisterCleanup(() =>
            {
                Corpse corpse = pawn?.ParentHolder as Corpse;
                if (corpse != null && !corpse.Destroyed)
                {
                    corpse.Destroy(DestroyMode.Vanish);
                }
            });
        }
    }
}

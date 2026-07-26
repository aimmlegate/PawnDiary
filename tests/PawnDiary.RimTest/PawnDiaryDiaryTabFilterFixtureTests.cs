// Loaded-game regression fixtures for the reusable journal's per-pawn filter lifecycle. These tests do
// not draw Unity GUI: reflection invokes the exact private state-transition seams, which is enough to
// prove a first visit starts clean, year changes cannot leave invisible filters active, and the
// filter-panel prompt selector/purge tools cannot leak synthetic ownership or stale saved state.
using System;
using System.Collections.Generic;
using System.Reflection;
using RimTestRedux;
using UnityEngine;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Pins hidden-panel pawn reset and year-specific tag reset behavior.</summary>
    [TestSuite]
    public static class PawnDiaryDiaryTabFilterFixtureTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo ActivePawnIdField =
            typeof(DiaryJournalView).GetField("activePawnUiStateId", PrivateInstance);
        private static readonly FieldInfo FavoritesOnlyField =
            typeof(DiaryJournalView).GetField("filterFavoritesOnly", PrivateInstance);
        private static readonly FieldInfo ActiveTagsField =
            typeof(DiaryJournalView).GetField("filterActiveTags", PrivateInstance);
        private static readonly FieldInfo SelectedYearField =
            typeof(DiaryJournalView).GetField("selectedYear", PrivateInstance);
        private static readonly MethodInfo ActivatePawnStateMethod =
            typeof(DiaryJournalView).GetMethod("ActivatePawnUiState", PrivateInstance);
        private static readonly MethodInfo SelectYearMethod =
            typeof(DiaryJournalView).GetMethod("SelectYear", PrivateInstance);
        private static readonly FieldInfo EventsField =
            typeof(DiaryGameComponent).GetField("events", PrivateInstance);
        private static readonly FieldInfo ArchiveField =
            typeof(DiaryGameComponent).GetField("archive", PrivateInstance);
        private static readonly FieldInfo KnowledgeReportsField =
            typeof(DiaryGameComponent).GetField("knowledgeReportsByPawnId", PrivateInstance);
        private static readonly MethodInfo ApplyLlmResultMethod =
            typeof(DiaryGameComponent).GetMethod("ApplyLlmResult", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn firstPawn;
        private static Pawn secondPawn;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            firstPawn = scope.CreateAdultColonist();
            secondPawn = scope.CreateAdultColonist();
            RequireReflectionSeams();
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
                scope = null;
                firstPawn = null;
                secondPawn = null;
            }
        }

        /// <summary>A first visit to another pawn starts with clean filters even before any geometry draw.</summary>
        [Test]
        public static void HiddenPanelResetsFiltersBeforeGeometryReturn()
        {
            DiaryJournalView journal = new DiaryJournalView();
            ActivatePawnStateMethod.Invoke(journal, new object[] { firstPawn.GetUniqueLoadID() });
            FavoritesOnlyField.SetValue(journal, true);
            ActiveTags(journal).Add("Social");

            ActivatePawnStateMethod.Invoke(journal, new object[] { secondPawn.GetUniqueLoadID() });

            PawnDiaryRimTestScope.Require(
                string.Equals(ActivePawnIdField.GetValue(journal) as string,
                    secondPawn.GetUniqueLoadID(), StringComparison.Ordinal),
                "The Diary journal did not advance its active per-pawn state key.");
            PawnDiaryRimTestScope.Require(!(bool)FavoritesOnlyField.GetValue(journal)
                    && ActiveTags(journal).Count == 0,
                "The hidden Diary filter panel leaked the previous pawn's active filters.");
        }

        /// <summary>Changing years clears only year-specific tag chips, not favorites-only selection.</summary>
        [Test]
        public static void YearChangeClearsInvisibleTagSelections()
        {
            DiaryJournalView journal = new DiaryJournalView();
            SelectedYearField.SetValue(journal, 5501);
            FavoritesOnlyField.SetValue(journal, true);
            ActiveTags(journal).Add("Raid");

            SelectYearMethod.Invoke(journal, new object[] { 5502 });

            PawnDiaryRimTestScope.Require((int)SelectedYearField.GetValue(journal) == 5502,
                "The Diary journal did not select the requested year.");
            PawnDiaryRimTestScope.Require(ActiveTags(journal).Count == 0,
                "A tag absent from the new year remained invisibly active.");
            PawnDiaryRimTestScope.Require((bool)FavoritesOnlyField.GetValue(journal),
                "Changing years unexpectedly cleared the independent favorites-only filter.");
        }

        /// <summary>
        /// A pair fixture may read the second pawn for realistic context, but the filter selector owns
        /// and queues the prompt only for the pawn whose diary is currently open.
        /// </summary>
        [Test]
        public static void PromptSelectorPairFixtureAddsOnlyTheSelectedPawnDiaryReference()
        {
            scope.EnablePromptCapture();
            scope.Component.SetDiaryGenerationEnabled(firstPawn, true);
            scope.Component.SetDiaryGenerationEnabled(secondPawn, true);

            DiaryGameComponent.DevPromptSuiteEntry pairEntry = FirstPairEntry();
            PawnDiaryRimTestScope.Require(pairEntry != null,
                "The prompt-suite catalog did not expose a pair fixture.");
            bool shown = scope.Component.ShowPromptSuiteEntryForCurrentPawnForDev(
                firstPawn,
                pairEntry,
                secondPawn);

            PawnDiaryRimTestScope.Require(shown,
                "The current-pawn prompt selector did not build its pair fixture.");
            PawnDiaryRimTestScope.Require(
                scope.RequireDiaryRecord(firstPawn).eventIds.Count == 1,
                "The selected pawn did not receive exactly one prompt-fixture diary reference.");
            PawnDiaryRimTestScope.Require(
                scope.RequireDiaryRecord(secondPawn).eventIds.Count == 0,
                "The context partner incorrectly received the selected pawn's prompt fixture.");

            string eventId = scope.RequireDiaryRecord(firstPawn).eventIds[0];
            DiaryEvent diaryEvent = EventRepository().FindEvent(eventId);
            PawnDiaryRimTestScope.Require(diaryEvent != null
                    && diaryEvent.solo
                    && string.IsNullOrWhiteSpace(diaryEvent.recipientPawnId)
                    && diaryEvent.IsSkipped(DiaryEvent.RecipientRole),
                "The selected-pawn pair fixture retained a recipient owner or queueable recipient role.");
            PawnDiaryRimTestScope.Require(diaryEvent.RoleForPawn(secondPawn.GetUniqueLoadID()) == null,
                "Repository-wide role lookup still treated the context partner as a fixture participant.");
            DiaryEntryView selectedView = diaryEvent.ToViewFor(firstPawn.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(selectedView != null && selectedView.LinkedEntry == null,
                "The selected-pawn pair fixture retained a dead linked entry to the context partner.");
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(scope.CapturedPrompt(diaryEvent, DiaryEvent.InitiatorRole)),
                "The pair prompt was not captured before the fixture partner was detached.");
        }

        /// <summary>
        /// Purging one pawn removes its hot/archive/favorite/counter state, deletes orphaned master rows,
        /// and retains only the partner-owned half of a shared pair. A late result for the purged role
        /// cannot resurrect it, while the surviving partner can still capture its base prompt.
        /// </summary>
        [Test]
        public static void PurgeHistoryRetiresOnlyTheSelectedPawnRole()
        {
            scope.EnablePromptCapture();
            scope.Component.SetDiaryGenerationEnabled(firstPawn, true);
            scope.Component.SetDiaryGenerationEnabled(secondPawn, true);

            DiaryGameComponent.DevPromptSuiteEntry pairEntry = FirstPairEntry();
            PawnDiaryRimTestScope.Require(pairEntry != null,
                "The prompt-suite catalog did not expose a pair fixture.");
            bool shown = scope.Component.ShowPromptSuiteEntryForDev(
                firstPawn,
                pairEntry,
                secondPawn);
            PawnDiaryRimTestScope.Require(shown
                    && scope.RequireDiaryRecord(firstPawn).eventIds.Count == 1
                    && scope.RequireDiaryRecord(secondPawn).eventIds.Count == 1,
                "The shared pair fixture did not establish both pre-purge diary references.");

            PawnDiaryRecord firstDiary = scope.RequireDiaryRecord(firstPawn);
            PawnDiaryRecord secondDiary = scope.RequireDiaryRecord(secondPawn);
            string sharedEventId = firstDiary.eventIds[0];
            DiaryEvent sharedEvent = EventRepository().FindEvent(sharedEventId);
            PawnDiaryRimTestScope.Require(sharedEvent != null,
                "The shared pair fixture had no master event row.");

            DiaryEvent orphanedEvent = scope.Component.AddSoloEvent(
                firstPawn,
                null,
                "PawnDiary_DevPurgeOrphan",
                "dev purge orphan",
                "synthetic orphan",
                string.Empty,
                "dev_purge_fixture=true");
            PawnDiaryRimTestScope.Require(orphanedEvent != null && firstDiary.eventIds.Count == 2,
                "The purge fixture could not establish an orphanable selected-pawn event.");

            firstDiary.favoriteEntryKeys.Add(sharedEventId + "|" + DiaryEvent.InitiatorRole);
            firstDiary.favoriteEntryKeys.Add(orphanedEvent.eventId + "|" + DiaryEvent.InitiatorRole);
            firstDiary.acknowledgedGeneratedEntryCount = 7;
            firstDiary.unreadGeneratedEntryCount = 3;
            firstDiary.hasUnreadGeneratedEntry = true;
            secondDiary.favoriteEntryKeys.Add(sharedEventId + "|" + DiaryEvent.RecipientRole);
            secondDiary.acknowledgedGeneratedEntryCount = 4;
            secondDiary.unreadGeneratedEntryCount = 2;
            secondDiary.hasUnreadGeneratedEntry = true;

            scope.Component.AcknowledgeGeneratedEntriesFor(
                firstPawn,
                5,
                2,
                scope.Component.RenderTokenFor(firstPawn));
            // AcknowledgeGeneratedEntriesFor intentionally resets saved unread state; restore the stale
            // fixture counters after seeding the transient command cache.
            firstDiary.acknowledgedGeneratedEntryCount = 7;
            firstDiary.unreadGeneratedEntryCount = 3;
            firstDiary.hasUnreadGeneratedEntry = true;

            string firstArchiveId = "dev-purge-archive-" + Guid.NewGuid().ToString("N");
            string secondArchiveId = "dev-purge-archive-" + Guid.NewGuid().ToString("N");
            ArchiveRepository().AddOrKeep(new ArchivedDiaryEntry
            {
                eventId = firstArchiveId,
                pawnId = firstPawn.GetUniqueLoadID(),
                povRole = DiaryEvent.InitiatorRole
            });
            ArchiveRepository().AddOrKeep(new ArchivedDiaryEntry
            {
                eventId = secondArchiveId,
                pawnId = secondPawn.GetUniqueLoadID(),
                povRole = DiaryEvent.RecipientRole
            });

            // Simulate an initiator request that was already in flight when the player clicked purge.
            sharedEvent.initiatorStatus = DiaryEvent.PendingStatus;
            sharedEvent.initiatorTitleStatus = DiaryEvent.PendingStatus;
            sharedEvent.recipientStatus = DiaryEvent.NotGeneratedStatus;
            int removed = scope.Component.PurgeDiaryHistoryForPawnForDev(firstPawn);

            PawnDiaryRimTestScope.Require(removed == 3,
                "The purge did not report both selected-pawn hot pages plus its archive row.");
            PawnDiaryRimTestScope.Require(
                firstDiary.eventIds.Count == 0
                    && firstDiary.favoriteEntryKeys.Count == 0
                    && firstDiary.acknowledgedGeneratedEntryCount == 0
                    && firstDiary.unreadGeneratedEntryCount == 0
                    && !firstDiary.hasUnreadGeneratedEntry,
                "The purge left hot refs, favorites, or generated-page counters on the selected pawn.");
            PawnDiaryRimTestScope.Require(
                secondDiary.eventIds.Count == 1
                    && secondDiary.eventIds[0] == sharedEventId
                    && secondDiary.favoriteEntryKeys.Count == 1
                    && secondDiary.acknowledgedGeneratedEntryCount == 4
                    && secondDiary.unreadGeneratedEntryCount == 2
                    && secondDiary.hasUnreadGeneratedEntry,
                "The purge degraded the other pawn's shared pair state.");
            PawnDiaryRimTestScope.Require(
                EventRepository().ContainsEvent(sharedEventId)
                    && !EventRepository().ContainsEvent(orphanedEvent.eventId),
                "The purge did not preserve the shared master row and delete the orphaned master row.");
            PawnDiaryRimTestScope.Require(
                ArchiveRepository().CountForPawn(firstPawn.GetUniqueLoadID()) == 0
                    && ArchiveRepository().CountForPawn(secondPawn.GetUniqueLoadID()) == 1,
                "The purge did not remove only the selected pawn's archive rows.");
            PawnDiaryRimTestScope.Require(
                string.IsNullOrWhiteSpace(sharedEvent.initiatorPawnId)
                    && sharedEvent.IsSkipped(DiaryEvent.InitiatorRole)
                    && sharedEvent.initiatorTitleStatus == DiaryEvent.SkippedStatus
                    && sharedEvent.recipientPawnId == secondPawn.GetUniqueLoadID(),
                "The shared master row did not terminalize and detach only the purged role.");
            PawnDiaryRimTestScope.Require(
                DiaryEvent.RoleEquals(sharedEvent.recipientStatus, DiaryEvent.PromptOnlyStatus),
                "Initiator purge did not immediately release the surviving recipient to its base prompt.");
            DiaryGameComponent.DiaryCommandStatus commandStatus =
                scope.Component.CommandStatusFor(firstPawn);
            PawnDiaryRimTestScope.Require(commandStatus.completedCount == 0
                    && commandStatus.pendingCount == 0
                    && commandStatus.unacknowledgedCount == 0,
                "The purge left a stale selected-pawn command-status cache.");

            ApplyLlmResultMethod.Invoke(scope.Component, new object[]
            {
                new LlmGenerationResult
                {
                    eventId = sharedEventId,
                    povRole = DiaryEvent.InitiatorRole,
                    success = true,
                    generatedText = "late synthetic prose",
                    rawResponse = "late synthetic prose"
                }
            });

            PawnDiaryRimTestScope.Require(sharedEvent.IsSkipped(DiaryEvent.InitiatorRole)
                    && string.IsNullOrWhiteSpace(sharedEvent.initiatorGeneratedText)
                    && firstDiary.unreadGeneratedEntryCount == 0,
                "A late result resurrected generated state or unread count for the purged role.");
            PawnDiaryRimTestScope.Require(
                DiaryEvent.RoleEquals(sharedEvent.recipientStatus, DiaryEvent.PromptOnlyStatus),
                "A late purged-role result disturbed the surviving recipient's captured base prompt.");
            DiaryEntryView survivingView = sharedEvent.ToViewFor(secondPawn.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(survivingView != null && survivingView.LinkedEntry == null,
                "The surviving page retained a dead link to the purged role.");
        }

        /// <summary>
        /// Full-history purge repairs both mixed retention (A archived, B still hot) and fully compacted
        /// pairs. The partner's page/body survives, but no hot role or archived link still targets A;
        /// an unrelated archived link remains untouched.
        /// </summary>
        [Test]
        public static void PurgeHistorySeversMixedAndArchivedPairOwnership()
        {
            string firstPawnId = firstPawn.GetUniqueLoadID();
            string secondPawnId = secondPawn.GetUniqueLoadID();
            PawnDiaryRecord firstDiary = scope.RequireDiaryRecord(firstPawn);
            PawnDiaryRecord secondDiary = scope.RequireDiaryRecord(secondPawn);

            DiaryEvent mixedEvent = scope.Component.AddPairwiseEvent(
                firstPawn,
                secondPawn,
                "PawnDiary_DevMixedRetentionPair",
                "mixed retention pair",
                "first mixed fact",
                "second mixed fact",
                string.Empty,
                "dev_purge_mixed_retention=true");
            mixedEvent.initiatorGeneratedText = "First pawn's completed mixed page.";
            mixedEvent.recipientGeneratedText = "Second pawn's completed mixed page.";
            mixedEvent.initiatorStatus = DiaryEvent.CompleteStatus;
            mixedEvent.recipientStatus = DiaryEvent.CompleteStatus;
            ArchivedDiaryEntry mixedFirstArchive = ArchivedDiaryEntry.FromEvent(
                mixedEvent,
                firstPawnId,
                mixedEvent.ToViewFor(firstPawnId),
                false);
            PawnDiaryRimTestScope.Require(
                ArchiveRepository().AddOrKeep(mixedFirstArchive)
                    && firstDiary.eventIds.Remove(mixedEvent.eventId)
                    && secondDiary.eventIds.Contains(mixedEvent.eventId),
                "The mixed-retention fixture could not archive only the first POV.");

            DiaryEvent coldEvent = scope.Component.AddPairwiseEvent(
                firstPawn,
                secondPawn,
                "PawnDiary_DevColdRetentionPair",
                "cold retention pair",
                "first cold fact",
                "second cold fact",
                string.Empty,
                "dev_purge_cold_retention=true");
            coldEvent.initiatorGeneratedText = "First pawn's completed cold page.";
            coldEvent.recipientGeneratedText = "Second pawn's completed cold page.";
            coldEvent.initiatorStatus = DiaryEvent.CompleteStatus;
            coldEvent.recipientStatus = DiaryEvent.CompleteStatus;
            ArchivedDiaryEntry coldFirstArchive = ArchivedDiaryEntry.FromEvent(
                coldEvent,
                firstPawnId,
                coldEvent.ToViewFor(firstPawnId),
                false);
            ArchivedDiaryEntry coldSecondArchive = ArchivedDiaryEntry.FromEvent(
                coldEvent,
                secondPawnId,
                coldEvent.ToViewFor(secondPawnId),
                false);
            PawnDiaryRimTestScope.Require(
                ArchiveRepository().AddOrKeep(coldFirstArchive)
                    && ArchiveRepository().AddOrKeep(coldSecondArchive)
                    && firstDiary.eventIds.Remove(coldEvent.eventId)
                    && secondDiary.eventIds.Remove(coldEvent.eventId),
                "The fully compacted pair fixture could not archive both POVs.");
            EventRepository().RemoveEvent(coldEvent.eventId);

            string unrelatedEventId = "dev-unrelated-archive-" + Guid.NewGuid().ToString("N");
            ArchivedDiaryEntry unrelatedArchive = new ArchivedDiaryEntry
            {
                eventId = unrelatedEventId,
                pawnId = secondPawnId,
                povRole = DiaryEvent.RecipientRole,
                generatedText = "Unrelated archived body.",
                linkedPawnId = "Pawn_Unrelated",
                linkedPawnName = "Unrelated",
                linkedRole = DiaryEvent.InitiatorRole,
                linkedPreviewText = "Keep this unrelated preview.",
                linkedGenerated = true,
                linkedTitle = "Unrelated title"
            };
            PawnDiaryRimTestScope.Require(ArchiveRepository().AddOrKeep(unrelatedArchive),
                "The unrelated archived-link control row could not be added.");
            HashSet<string> cleanupArchiveIds = new HashSet<string>(
                new[] { mixedEvent.eventId, coldEvent.eventId, unrelatedEventId },
                StringComparer.OrdinalIgnoreCase);
            scope.RegisterCleanup(() => ArchiveRepository().RemoveForEventIds(cleanupArchiveIds));

            PawnDiaryRimTestScope.Require(
                coldSecondArchive != null
                    && string.Equals(coldSecondArchive.linkedPawnId, firstPawnId, StringComparison.Ordinal),
                "The fully compacted partner row did not begin with a link to the first pawn.");

            int removed = scope.Component.PurgeDiaryHistoryForPawnForDev(firstPawn);

            PawnDiaryRimTestScope.Require(removed == 2
                    && ArchiveRepository().CountForPawn(firstPawnId) == 0,
                "Mixed/cold purge did not report and remove exactly the first pawn's two archive rows.");
            PawnDiaryRimTestScope.Require(
                EventRepository().ContainsEvent(mixedEvent.eventId)
                    && secondDiary.eventIds.Count == 1
                    && secondDiary.eventIds[0] == mixedEvent.eventId
                    && string.IsNullOrWhiteSpace(mixedEvent.initiatorPawnId)
                    && mixedEvent.IsSkipped(DiaryEvent.InitiatorRole)
                    && string.Equals(mixedEvent.recipientPawnId, secondPawnId, StringComparison.Ordinal)
                    && DiaryEvent.RoleEquals(mixedEvent.recipientStatus, DiaryEvent.CompleteStatus),
                "Mixed retention purge did not preserve only the hot partner role and master row.");
            DiaryEntryView mixedPartnerView = mixedEvent.ToViewFor(secondPawnId);
            PawnDiaryRimTestScope.Require(
                mixedPartnerView != null
                    && mixedPartnerView.LinkedEntry == null
                    && string.Equals(
                        mixedPartnerView.GeneratedText,
                        "Second pawn's completed mixed page.",
                        StringComparison.Ordinal),
                "Mixed retention purge left a dead hot link or changed the partner's prose.");
            PawnDiaryRimTestScope.Require(
                ArchiveRepository().Contains(
                    coldEvent.eventId,
                    secondPawnId,
                    DiaryEvent.RecipientRole)
                    && coldSecondArchive.ToView().LinkedEntry == null
                    && string.IsNullOrWhiteSpace(coldSecondArchive.linkedPawnId)
                    && string.IsNullOrWhiteSpace(coldSecondArchive.linkedPreviewText)
                    && !coldSecondArchive.linkedGenerated
                    && string.Equals(
                        coldSecondArchive.generatedText,
                        "Second pawn's completed cold page.",
                        StringComparison.Ordinal),
                "Fully compacted purge removed the partner row/body or left its dead link to the purged pawn.");
            PawnDiaryRimTestScope.Require(
                string.Equals(unrelatedArchive.linkedPawnId, "Pawn_Unrelated", StringComparison.Ordinal)
                    && string.Equals(
                        unrelatedArchive.linkedPreviewText,
                        "Keep this unrelated preview.",
                        StringComparison.Ordinal)
                    && unrelatedArchive.linkedGenerated,
                "Full-history purge cleared an archived link that did not target the purged pawn.");
        }

        /// <summary>Metadata-only purge mutations still invalidate render/cache state and report no pages.</summary>
        [Test]
        public static void PurgeEmptyHistoryStillClearsMetadataAndInvalidatesState()
        {
            PawnDiaryRecord diary = scope.RequireDiaryRecord(firstPawn);
            diary.favoriteEntryKeys.Add("stale-event|" + DiaryEvent.InitiatorRole);
            diary.acknowledgedGeneratedEntryCount = 9;
            diary.unreadGeneratedEntryCount = 2;
            diary.hasUnreadGeneratedEntry = true;
            scope.Component.AcknowledgeGeneratedEntriesFor(
                firstPawn,
                4,
                1,
                scope.Component.RenderTokenFor(firstPawn));
            diary.acknowledgedGeneratedEntryCount = 9;
            diary.unreadGeneratedEntryCount = 2;
            diary.hasUnreadGeneratedEntry = true;
            int versionBefore = DiaryStateVersion.Current;

            int removed = scope.Component.PurgeDiaryHistoryForPawnForDev(firstPawn);

            DiaryGameComponent.DiaryCommandStatus commandStatus =
                scope.Component.CommandStatusFor(firstPawn);
            PawnDiaryRimTestScope.Require(removed == 0,
                "A metadata-only purge incorrectly reported a removed diary page.");
            PawnDiaryRimTestScope.Require(diary.favoriteEntryKeys.Count == 0
                    && diary.acknowledgedGeneratedEntryCount == 0
                    && diary.unreadGeneratedEntryCount == 0
                    && !diary.hasUnreadGeneratedEntry,
                "A metadata-only purge left stale favorites or counters.");
            PawnDiaryRimTestScope.Require(commandStatus.completedCount == 0
                    && commandStatus.pendingCount == 0
                    && commandStatus.unacknowledgedCount == 0,
                "A metadata-only purge left its transient command-status cache.");
            PawnDiaryRimTestScope.Require(DiaryStateVersion.Current > versionBefore,
                "A metadata-only purge did not invalidate rendered diary state.");
        }

        /// <summary>Every prompt-suite construction path suppresses synthetic lifelong knowledge.</summary>
        [Test]
        public static void PromptSuiteFixturesDoNotCaptureSyntheticKnowledge()
        {
            scope.EnablePromptCapture();
            scope.Component.SetDiaryGenerationEnabled(firstPawn, true);
            scope.Component.SetDiaryGenerationEnabled(secondPawn, true);

            DiaryGameComponent.DevPromptSuiteEntry soloEntry = EntryById("PersonaBondFormed");
            DiaryGameComponent.DevPromptSuiteEntry pairEntry = EntryById("Romance");
            PawnDiaryRimTestScope.Require(soloEntry != null && pairEntry != null,
                "The prompt-suite catalog did not expose the important-event fixtures.");

            PawnDiaryRimTestScope.Require(
                scope.Component.ShowPromptSuiteEntryForDev(firstPawn, soloEntry, secondPawn),
                "The full prompt suite could not build its synthetic solo fixture.");
            string soloEventId = scope.RequireDiaryRecord(firstPawn).eventIds[0];
            PawnDiaryRimTestScope.Require(!HasKnowledgeFromEvent(firstPawn, soloEventId),
                "The full prompt suite deposited its synthetic solo fixture into lifelong knowledge.");

            PawnDiaryRimTestScope.Require(
                scope.Component.ShowPromptSuiteEntryForDev(firstPawn, pairEntry, secondPawn),
                "The full prompt suite could not build its synthetic pair fixture.");
            string pairEventId = scope.RequireDiaryRecord(firstPawn).eventIds[0];
            PawnDiaryRimTestScope.Require(!HasKnowledgeFromEvent(firstPawn, pairEventId)
                    && !HasKnowledgeFromEvent(secondPawn, pairEventId),
                "The full prompt suite deposited its synthetic pair fixture into lifelong knowledge.");
        }

        /// <summary>
        /// Synthetic prompt previews may temporarily exceed the hot cap, but creating one must never run
        /// retention and compact or delete an older real page. Covers the solo, full-pair, and
        /// current-pawn-only pair factory seams at the destructive archive-cap-zero boundary.
        /// </summary>
        [Test]
        public static void PromptSuiteFactoriesNeverApplyRealDiaryRetention()
        {
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            PawnDiaryRimTestScope.Require(settings != null,
                "Pawn Diary settings were unavailable for the retention-safety fixture.");
            int originalHotLimit = settings.maxActiveDiaryEvents;
            int originalArchiveLimit = settings.maxArchivedDiaryEvents;
            scope.RegisterCleanup(() =>
            {
                settings.maxActiveDiaryEvents = originalHotLimit;
                settings.maxArchivedDiaryEvents = originalArchiveLimit;
            });
            settings.maxActiveDiaryEvents = PawnDiarySettings.MinActiveDiaryEvents;
            settings.maxArchivedDiaryEvents = 0;

            scope.EnablePromptCapture();
            scope.Component.SetDiaryGenerationEnabled(firstPawn, true);
            scope.Component.SetDiaryGenerationEnabled(secondPawn, true);
            DiaryEvent realEvent = scope.Component.AddSoloEvent(
                firstPawn,
                null,
                "PawnDiary_RealRetentionSentinel",
                "real retention sentinel",
                "real player-authored history",
                string.Empty,
                "retention_safety_fixture=true");
            realEvent.initiatorGeneratedText = "A real completed diary page that must survive.";
            realEvent.initiatorStatus = DiaryEvent.CompleteStatus;

            DiaryGameComponent.DevPromptSuiteEntry soloEntry = EntryById("MentalBreak");
            DiaryGameComponent.DevPromptSuiteEntry pairEntry = FirstPairEntry();
            PawnDiaryRimTestScope.Require(soloEntry != null && pairEntry != null,
                "The prompt-suite catalog did not expose both solo and pair fixtures.");

            PawnDiaryRimTestScope.Require(
                scope.Component.ShowPromptSuiteEntryForDev(firstPawn, soloEntry, secondPawn),
                "The full prompt suite could not build its solo retention fixture.");
            RequireRealRetentionSentinel(realEvent, "the full-suite solo helper");

            PawnDiaryRimTestScope.Require(
                scope.Component.ShowPromptSuiteEntryForDev(firstPawn, pairEntry, secondPawn),
                "The full prompt suite could not build its pair retention fixture.");
            RequireRealRetentionSentinel(realEvent, "the full-suite pair helper");

            PawnDiaryRimTestScope.Require(
                scope.Component.ShowPromptSuiteEntryForCurrentPawnForDev(
                    firstPawn,
                    pairEntry,
                    secondPawn),
                "The current-pawn selector could not build its pair retention fixture.");
            RequireRealRetentionSentinel(realEvent, "the current-pawn pair helper");
        }

        /// <summary>
        /// Clear recognizes a fully compacted archive-only fixture, removes its exact old provenance, and
        /// does not prefix-collide with another favorite key.
        /// </summary>
        [Test]
        public static void ClearPromptSuiteScrubsExactSavedFixtureState()
        {
            scope.EnablePromptCapture();
            scope.Component.SetDiaryGenerationEnabled(firstPawn, true);
            DiaryGameComponent.DevPromptSuiteEntry entry = EntryById("PersonaBondFormed");
            PawnDiaryRimTestScope.Require(entry != null
                    && scope.Component.ShowPromptSuiteEntryForDev(firstPawn, entry, secondPawn),
                "The prompt-suite fixture could not be built for cleanup coverage.");

            PawnDiaryRecord diary = scope.RequireDiaryRecord(firstPawn);
            string eventId = diary.eventIds[0];
            DiaryEvent fixtureEvent = EventRepository().FindEvent(eventId);
            PawnDiaryRimTestScope.Require(fixtureEvent != null,
                "The prompt-suite cleanup fixture had no hot master row to compact.");
            diary.knowledgeState = diary.knowledgeState ?? new PawnKnowledgeState
            {
                pawnId = firstPawn.GetUniqueLoadID()
            };
            diary.knowledgeState.records.Add(new ImportantMemoryRecord
            {
                recordId = "dev-fixture-memory",
                dedupKey = "dev-fixture-memory",
                sourceEventId = eventId
            });
            string exactFavorite = eventId + "|" + DiaryEvent.InitiatorRole;
            string prefixCollisionFavorite = eventId + "2|" + DiaryEvent.InitiatorRole;
            diary.favoriteEntryKeys.Add(exactFavorite);
            diary.favoriteEntryKeys.Add(prefixCollisionFavorite);
            ArchiveRepository().AddOrKeep(new ArchivedDiaryEntry
            {
                eventId = eventId,
                pawnId = firstPawn.GetUniqueLoadID(),
                povRole = DiaryEvent.InitiatorRole,
                decorationGameContext = fixtureEvent.gameContext
            });
            KnowledgeReports()[firstPawn.GetUniqueLoadID()] =
                new DiaryGameComponent.KnowledgeDebugReport { eventId = eventId };

            // Simulate the fully compacted state: only the archive row and exact saved metadata remain.
            EventRepository().RemoveEvent(eventId);
            diary.eventIds.Clear();
            int removed = scope.Component.ClearPromptSuiteForDev();

            PawnDiaryRimTestScope.Require(removed == 1
                    && !EventRepository().ContainsEvent(eventId)
                    && diary.eventIds.Count == 0
                    && ArchiveRepository().CountForPawn(firstPawn.GetUniqueLoadID()) == 0,
                "Clear prompt suite left the synthetic page in a hot/archive store or pawn ref.");
            PawnDiaryRimTestScope.Require(!HasKnowledgeFromEvent(firstPawn, eventId),
                "Clear prompt suite left exact synthetic knowledge provenance.");
            PawnDiaryRimTestScope.Require(!diary.favoriteEntryKeys.Contains(exactFavorite)
                    && diary.favoriteEntryKeys.Contains(prefixCollisionFavorite),
                "Clear prompt suite missed its exact favorite or removed a prefix-colliding favorite.");
            PawnDiaryRimTestScope.Require(
                !KnowledgeReports().ContainsKey(firstPawn.GetUniqueLoadID()),
                "Clear prompt suite left a stale synthetic knowledge-debug report.");
        }

        private static HashSet<string> ActiveTags(DiaryJournalView journal)
        {
            return ActiveTagsField.GetValue(journal) as HashSet<string>;
        }

        private static DiaryGameComponent.DevPromptSuiteEntry FirstPairEntry()
        {
            IReadOnlyList<DiaryGameComponent.DevPromptSuiteEntry> entries =
                DiaryGameComponent.AllSuiteEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i]?.pair == true)
                {
                    return entries[i];
                }
            }

            return null;
        }

        private static DiaryGameComponent.DevPromptSuiteEntry EntryById(string id)
        {
            IReadOnlyList<DiaryGameComponent.DevPromptSuiteEntry> entries =
                DiaryGameComponent.AllSuiteEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i]?.id, id, StringComparison.Ordinal))
                {
                    return entries[i];
                }
            }

            return null;
        }

        private static DiaryEventRepository EventRepository()
        {
            return EventsField.GetValue(scope.Component) as DiaryEventRepository;
        }

        private static DiaryArchiveRepository ArchiveRepository()
        {
            return ArchiveField.GetValue(scope.Component) as DiaryArchiveRepository;
        }

        private static Dictionary<string, DiaryGameComponent.KnowledgeDebugReport> KnowledgeReports()
        {
            return KnowledgeReportsField.GetValue(scope.Component)
                as Dictionary<string, DiaryGameComponent.KnowledgeDebugReport>;
        }

        private static bool HasKnowledgeFromEvent(Pawn pawn, string eventId)
        {
            PawnKnowledgeState state = scope.RequireDiaryRecord(pawn).knowledgeState;
            if (state?.records == null)
            {
                return false;
            }

            for (int i = 0; i < state.records.Count; i++)
            {
                ImportantMemoryRecord record = state.records[i];
                if (record != null
                    && string.Equals(record.sourceEventId, eventId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RequireRealRetentionSentinel(DiaryEvent realEvent, string factoryPath)
        {
            PawnDiaryRecord diary = scope.RequireDiaryRecord(firstPawn);
            PawnDiaryRimTestScope.Require(realEvent != null
                    && EventRepository().ContainsEvent(realEvent.eventId)
                    && diary.eventIds.Contains(realEvent.eventId)
                    && ArchiveRepository().CountForPawn(firstPawn.GetUniqueLoadID()) == 0,
                "Creating a synthetic prompt through " + factoryPath
                    + " compacted or deleted an older real diary page at the configured hot cap.");
        }

        private static void RequireReflectionSeams()
        {
            PawnDiaryRimTestScope.Require(ActivePawnIdField != null && FavoritesOnlyField != null
                    && ActiveTagsField != null && SelectedYearField != null
                    && ActivatePawnStateMethod != null && SelectYearMethod != null
                    && EventsField != null && ArchiveField != null
                    && KnowledgeReportsField != null && ApplyLlmResultMethod != null,
                "The Diary filter fixture could not resolve one or more private lifecycle seams.");
        }
    }
}

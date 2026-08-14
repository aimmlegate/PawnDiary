// Loaded-game tests for Pawn Diary's Anomaly Brainwipe memory-reset boundary.
//
// The vanilla Brainwipe outcome is deliberately not invoked here: it irreversibly rewrites a real
// pawn's memories several in-game hours after a psychic ritual. These fixtures instead exercise the
// three reversible boundaries Pawn Diary owns around it:
//   1. DiaryGameComponent.ForgetDiaryHistory clears only autobiographical history.
//   2. BrainwipeArrivalSignal creates the first anxious-amnesiac page after that reset.
//   3. PsychicRitualBrainwipeOutcomePatch obeys the Anomaly ownership gate in the loaded profile.
//   4. A translated-notice failure cannot suppress that boundary or disable a later reset.
//
// All pawns and repository rows belong to PawnDiaryRimTestScope. The archive and day-digest rows
// seeded below use those disposable pawn IDs and receive explicit emergency cleanup callbacks, so a
// failed assertion cannot remove or retain any player-owned history.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Verifies that Brainwipe forgets personal history, preserves voice identity, and establishes a
    /// DLC-safe post-wipe arrival boundary without invoking vanilla's destructive ritual outcome.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryBrainwipeFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string ArrivalGroupKey = "arrival";

        private static readonly FieldInfo ArchiveField =
            typeof(DiaryGameComponent).GetField("archive", PrivateInstance);
        private static readonly FieldInfo DayDigestStatesField =
            typeof(DiaryGameComponent).GetField("dayDigestStates", PrivateInstance);
        private static readonly FieldInfo PendingDayDigestField =
            typeof(DiaryGameComponent).GetField("pendingDayDigest", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn target;
        private static Pawn partner;

        /// <summary>Creates two isolated colonists and enables only the arrival route used here.</summary>
        [BeforeEach]
        public static void SetUp()
        {
            RequireReflectionSurface();
            PsychicRitualBrainwipeOutcomePatch.SetHistoryClearedNoticeOverrideForTests(null);
            scope = PawnDiaryRimTestScope.Begin(ArrivalGroupKey);
            target = scope.CreateAdultColonist();
            partner = scope.CreateAdultColonist();
        }

        /// <summary>Removes every disposable pawn, page, archive row, and digest fixture.</summary>
        [AfterEach]
        public static void TearDown()
        {
            PsychicRitualBrainwipeOutcomePatch.SetHistoryClearedNoticeOverrideForTests(null);
            scope?.TearDown();
            scope = null;
            target = null;
            partner = null;
        }

        /// <summary>
        /// The component reset clears hot/cold pages and narrative bookkeeping, keeps a shared page for
        /// its other owner, and preserves every player-authored voice/generation choice.
        /// </summary>
        [Test]
        public static void ComponentResetForgetsHistoryButPreservesVoiceAndSharedOtherOwner()
        {
            PawnDiaryRecord targetRecord = scope.RequireDiaryRecord(target);
            PawnDiaryRecord partnerRecord = scope.RequireDiaryRecord(partner);
            ConfigureVoiceIdentity(targetRecord);

            DiaryEvent solo = scope.Component.AddSoloEvent(
                target,
                null,
                "PawnDiary_RimTest_BrainwipeSolo",
                "brainwipe fixture",
                "A private pre-wipe memory.",
                string.Empty,
                "rimtest_brainwipe=solo");
            DiaryEvent shared = scope.Component.AddPairwiseEvent(
                target,
                partner,
                "PawnDiary_RimTest_BrainwipeShared",
                "brainwipe fixture",
                "A shared pre-wipe memory.",
                "A shared pre-wipe memory.",
                string.Empty,
                "rimtest_brainwipe=shared");
            PawnDiaryRimTestScope.Require(
                solo != null && shared != null,
                "The Brainwipe fixture could not seed its hot diary pages.");

            string targetId = target.GetUniqueLoadID();
            DiaryArchiveRepository archive = RequireArchive();
            ArchivedDiaryEntry archived = new ArchivedDiaryEntry
            {
                eventId = "pawndiary-rimtest-brainwipe-archive-" + Guid.NewGuid().ToString("N"),
                pawnId = targetId,
                povRole = DiaryEvent.InitiatorRole,
                tick = Find.TickManager.TicksGame,
                date = "RimTest Brainwipe archive",
                text = "Disposable archived pre-wipe memory.",
            };
            PawnDiaryRimTestScope.Require(
                archive.AddOrKeep(archived),
                "The Brainwipe fixture could not seed its disposable archive row.");
            scope.RegisterCleanup(() => archive.RemoveForPawn(targetId));

            List<PawnDayDigestState> dayDigests = RequireDayDigests();
            IDictionary pendingDayDigests = RequirePendingDayDigests();
            int digestDay = GenDate.DaysPassed;
            string digestKey = targetId + "|" + digestDay;
            scope.Component.AddDayDigestLine(
                targetId,
                digestDay,
                "rimtest_brainwipe",
                "Disposable pre-wipe digest line.",
                Find.TickManager.TicksGame);
            PawnDayDigestState digest =
                scope.Component.DayDigestStateFor(targetId, digestDay);
            PawnDiaryRimTestScope.Require(
                digest != null
                    && dayDigests.Contains(digest)
                    && pendingDayDigests.Contains(digestKey),
                "The Brainwipe fixture could not seed both saved and indexed day-digest state.");
            digest.lowSalienceCount = 1;
            scope.RegisterCleanup(() =>
            {
                dayDigests.Remove(digest);
                pendingDayDigests.Remove(digestKey);
            });

            targetRecord.favoriteEntryKeys.Add(
                solo.eventId + "|" + DiaryEvent.InitiatorRole);
            targetRecord.hasUnreadGeneratedEntry = true;
            targetRecord.unreadGeneratedEntryCount = 4;
            targetRecord.acknowledgedGeneratedEntryCount = 3;

            PawnKnowledgeState knowledge = targetRecord.EnsureKnowledgeState();
            knowledge.originCultureDefName = "PawnDiary_RimTest_OriginCulture";
            knowledge.originCultureSource = "rimtest";
            knowledge.adoptedCultureDefName = "PawnDiary_RimTest_AdoptedCulture";
            knowledge.records.Add(new ImportantMemoryRecord
            {
                recordId = "pawndiary-rimtest-brainwipe-memory",
                dedupKey = "rimtest-brainwipe-memory",
                sourceEventId = solo.eventId,
                eventKind = "rimtest",
                tick = Find.TickManager.TicksGame,
                fallbackSummary = "Disposable important memory.",
            });
            PlayerMemoryMutationPlan backgroundPlan = PlayerMemoryPolicy.PlanBackstoryMutation(
                targetId,
                null,
                "I remember a childhood before this colony.",
                450);
            knowledge.records.Add(ImportantMemoryRecord.FromSnapshot(backgroundPlan.record));

            PawnArcScheduleState oldArcSchedule = new PawnArcScheduleState();
            PawnBeliefState oldBeliefState = new PawnBeliefState();
            PawnReflectionState oldReflectionState = new PawnReflectionState();
            targetRecord.arcSchedule = oldArcSchedule;
            targetRecord.beliefState = oldBeliefState;
            targetRecord.reflectionState = oldReflectionState;

            bool removedPlayerBackground = scope.Component.ForgetDiaryHistory(target);

            PawnDiaryRimTestScope.Require(
                targetRecord.eventIds.Count == 0
                    && targetRecord.favoriteEntryKeys.Count == 0
                    && !targetRecord.hasUnreadGeneratedEntry
                    && targetRecord.unreadGeneratedEntryCount == 0
                    && targetRecord.acknowledgedGeneratedEntryCount == 0,
                "Brainwipe did not clear the target's page references, favorites, or unread state.");
            PawnDiaryRimTestScope.Require(
                removedPlayerBackground
                    && knowledge.records.Count == 0
                    && knowledge.originCultureDefName == "PawnDiary_RimTest_OriginCulture"
                    && knowledge.originCultureSource == "rimtest"
                    && knowledge.adoptedCultureDefName == "PawnDiary_RimTest_AdoptedCulture",
                "Brainwipe erased culture provenance or retained important episodic memories.");
            PawnDiaryRimTestScope.Require(
                !ReferenceEquals(targetRecord.arcSchedule, oldArcSchedule)
                    && !ReferenceEquals(targetRecord.beliefState, oldBeliefState)
                    && !ReferenceEquals(targetRecord.reflectionState, oldReflectionState),
                "Brainwipe did not replace every narrative scheduler/cache with a fresh baseline.");
            PawnDiaryRimTestScope.Require(
                archive.CountForPawn(targetId) == 0
                    && !dayDigests.Contains(digest)
                    && !pendingDayDigests.Contains(digestKey)
                    && scope.Component.DayDigestStateFor(targetId, digestDay) == null,
                "Brainwipe retained a cold archive row or saved/indexed day digest for its target.");
            PawnDiaryRimTestScope.Require(
                scope.Component.FindEventById(solo.eventId) == null
                    && scope.Component.FindEventById(shared.eventId) != null
                    && partnerRecord.eventIds.Contains(shared.eventId),
                "Brainwipe pruned a still-shared event or retained an orphaned solo event.");
            RequireVoiceIdentityPreserved(targetRecord);
        }

        /// <summary>
        /// Submitting the production signal after a reset produces exactly one new arrival page with
        /// the brainwipe/amnesia/anxiety context and marks it as the pawn's arrival description.
        /// </summary>
        [Test]
        public static void ArrivalSignalCreatesFirstPostWipeAutobiographicalBoundary()
        {
            scope.Component.AddSoloEvent(
                target,
                null,
                "PawnDiary_RimTest_PreBrainwipe",
                "brainwipe fixture",
                "A page that must disappear.",
                string.Empty,
                "rimtest_brainwipe=before");
            bool removedPlayerBackground = scope.Component.ForgetDiaryHistory(target);
            PawnDiaryRimTestScope.Require(
                !removedPlayerBackground,
                "A reset without a canonical player background reported that it removed one.");

            DiaryEvents.Submit(new BrainwipeArrivalSignal(target));

            PawnDiaryRecord record = scope.RequireDiaryRecord(target);
            PawnDiaryRimTestScope.Require(
                record.eventIds.Count == 1,
                "The post-Brainwipe arrival signal did not establish exactly one new page.");
            DiaryEvent boundary = scope.Component.FindEventById(record.eventIds[0]);
            PawnDiaryRimTestScope.Require(
                boundary != null
                    && boundary.interactionDefName == BrainwipeArrivalSignal.BrainwipeArrivalDefName
                    && boundary.gameContext.IndexOf("arrival_source=brainwipe", StringComparison.Ordinal) >= 0
                    && boundary.gameContext.IndexOf("memory_state=amnesia", StringComparison.Ordinal) >= 0
                    && boundary.gameContext.IndexOf("emotional_state=anxiety", StringComparison.Ordinal) >= 0
                    && boundary.IsArrivalDescriptionFor(target.GetUniqueLoadID()),
                "The post-Brainwipe boundary omitted its stable def, context, or arrival marker.");
        }

        /// <summary>
        /// The loaded patch adapter is a strict no-op without Anomaly, while an Anomaly-enabled profile
        /// clears the prior page and routes the same first-arrival signal tested above.
        /// </summary>
        [Test]
        public static void PatchAdapterHonorsLoadedAnomalyOwnershipGate()
        {
            DiaryEvent prior = scope.Component.AddSoloEvent(
                target,
                null,
                "PawnDiary_RimTest_BrainwipePatchPrior",
                "brainwipe fixture",
                "A page used to verify the DLC ownership gate.",
                string.Empty,
                "rimtest_brainwipe=patch_gate");
            PawnDiaryRimTestScope.Require(
                prior != null,
                "The Brainwipe patch fixture could not seed its prior page.");

            PsychicRitualBrainwipeOutcomePatch.Postfix(target);

            PawnDiaryRecord record = scope.RequireDiaryRecord(target);
            if (!ModsConfig.AnomalyActive)
            {
                PawnDiaryRimTestScope.Require(
                    record.eventIds.Count == 1
                        && record.eventIds[0] == prior.eventId
                        && scope.Component.FindEventById(prior.eventId) != null,
                    "The Brainwipe patch mutated diary state while Anomaly was inactive.");
                return;
            }

            PawnDiaryRimTestScope.Require(
                record.eventIds.Count == 1 && record.eventIds[0] != prior.eventId,
                "The Anomaly-enabled Brainwipe patch did not replace pre-wipe history with one boundary.");
            DiaryEvent boundary = scope.Component.FindEventById(record.eventIds[0]);
            PawnDiaryRimTestScope.Require(
                scope.Component.FindEventById(prior.eventId) == null
                    && boundary != null
                    && boundary.interactionDefName == BrainwipeArrivalSignal.BrainwipeArrivalDefName,
                "The Anomaly-enabled Brainwipe patch retained its old page or omitted the new boundary.");
        }

        /// <summary>
        /// A failure in the optional translated notice happens after the new arrival boundary and opens
        /// only the notice circuit. A second wipe must still clear its newly seeded history and create a
        /// fresh boundary, while the disabled notice adapter is not retried.
        /// </summary>
        [Test]
        public static void NoticeFailureKeepsArrivalAndLaterBrainwipeCleanup()
        {
            if (!ModsConfig.AnomalyActive)
            {
                return;
            }

            DiaryEvent firstPrior = AddPriorPage("First");
            AddPlayerBackground("I remember a first disposable childhood.");
            int noticeAttempts = 0;
            PsychicRitualBrainwipeOutcomePatch.SetHistoryClearedNoticeOverrideForTests(pawn =>
            {
                noticeAttempts++;
                throw new ExpectedBrainwipeNoticeFault();
            });

            PsychicRitualBrainwipeOutcomePatch.Postfix(target);

            PawnDiaryRecord record = scope.RequireDiaryRecord(target);
            DiaryEvent firstBoundary = RequireOnlyBrainwipeBoundary(
                record,
                firstPrior,
                "The failing notice suppressed the first post-Brainwipe boundary.");
            PawnDiaryRimTestScope.Require(
                noticeAttempts == 1,
                "The failing notice adapter was not attempted exactly once.");

            DiaryEvent secondPrior = AddPriorPage("Second");
            AddPlayerBackground("I remember a second disposable childhood.");
            PsychicRitualBrainwipeOutcomePatch.Postfix(target);

            DiaryEvent secondBoundary = RequireOnlyBrainwipeBoundary(
                record,
                secondPrior,
                "The notice fault disabled the main Brainwipe cleanup context on the later wipe.");
            PawnDiaryRimTestScope.Require(
                firstBoundary.eventId != secondBoundary.eventId
                    && scope.Component.FindEventById(firstBoundary.eventId) == null,
                "The later Brainwipe retained its earlier autobiographical boundary.");
            PawnDiaryRimTestScope.Require(
                noticeAttempts == 1,
                "The isolated notice circuit retried its known-failing adapter on the later wipe.");
        }

        private static DiaryEvent AddPriorPage(string suffix)
        {
            DiaryEvent prior = scope.Component.AddSoloEvent(
                target,
                null,
                "PawnDiary_RimTest_BrainwipeNoticePrior" + suffix,
                "brainwipe notice fixture",
                "A disposable page before Brainwipe " + suffix + ".",
                string.Empty,
                "rimtest_brainwipe=notice_failure");
            PawnDiaryRimTestScope.Require(
                prior != null,
                "The notice-failure fixture could not seed its " + suffix + " prior page.");
            return prior;
        }

        private static void AddPlayerBackground(string text)
        {
            string pawnId = target.GetUniqueLoadID();
            PlayerMemoryMutationPlan plan = PlayerMemoryPolicy.PlanBackstoryMutation(
                pawnId,
                null,
                text,
                450);
            PawnDiaryRimTestScope.Require(
                plan.action == PlayerMemoryMutationAction.Create && plan.record != null,
                "The notice-failure fixture could not plan a canonical player background.");
            scope.RequireDiaryRecord(target)
                .EnsureKnowledgeState()
                .records.Add(ImportantMemoryRecord.FromSnapshot(plan.record));
        }

        private static DiaryEvent RequireOnlyBrainwipeBoundary(
            PawnDiaryRecord record,
            DiaryEvent prior,
            string failure)
        {
            PawnDiaryRimTestScope.Require(
                record.eventIds.Count == 1
                    && record.eventIds[0] != prior.eventId
                    && scope.Component.FindEventById(prior.eventId) == null,
                failure);
            DiaryEvent boundary = scope.Component.FindEventById(record.eventIds[0]);
            PawnDiaryRimTestScope.Require(
                boundary != null
                    && boundary.interactionDefName == BrainwipeArrivalSignal.BrainwipeArrivalDefName,
                failure);
            return boundary;
        }

        private static void ConfigureVoiceIdentity(PawnDiaryRecord record)
        {
            record.personaDefName = DiaryPersonas.Default.defName;
            record.externalWritingStyleOverrideRule = "Preserve external writing style.";
            record.externalWritingStyleOverrideSourceId = "rimtest.brainwipe";
            record.customWritingStyleRule = "Preserve custom writing style.";
            record.psychotypeDefName = DiaryPsychotypes.NeutralDefName;
            record.externalPsychotypeOverrideRule = "Preserve external outlook.";
            record.externalPsychotypeOverrideSourceId = "rimtest.brainwipe";
            record.customPsychotypeRule = "Preserve custom outlook.";
            record.voiceStageBand = "adult";
            record.psychotypePinned = true;
            record.writingStylePinned = true;
            record.diaryGenerationEnabled = false;
        }

        private static void RequireVoiceIdentityPreserved(PawnDiaryRecord record)
        {
            PawnDiaryRimTestScope.Require(
                record.personaDefName == DiaryPersonas.Default.defName
                    && record.externalWritingStyleOverrideRule == "Preserve external writing style."
                    && record.externalWritingStyleOverrideSourceId == "rimtest.brainwipe"
                    && record.customWritingStyleRule == "Preserve custom writing style."
                    && record.psychotypeDefName == DiaryPsychotypes.NeutralDefName
                    && record.externalPsychotypeOverrideRule == "Preserve external outlook."
                    && record.externalPsychotypeOverrideSourceId == "rimtest.brainwipe"
                    && record.customPsychotypeRule == "Preserve custom outlook."
                    && record.voiceStageBand == "adult"
                    && record.psychotypePinned
                    && record.writingStylePinned
                    && !record.diaryGenerationEnabled,
                "Brainwipe changed a player-authored voice, pin, or generation setting.");
        }

        private static DiaryArchiveRepository RequireArchive()
        {
            DiaryArchiveRepository archive =
                ArchiveField.GetValue(scope.Component) as DiaryArchiveRepository;
            if (archive == null)
            {
                throw new AssertionException(
                    "Pawn Diary's loaded archive repository was unavailable to the Brainwipe fixture.");
            }

            return archive;
        }

        private static List<PawnDayDigestState> RequireDayDigests()
        {
            List<PawnDayDigestState> states =
                DayDigestStatesField.GetValue(scope.Component) as List<PawnDayDigestState>;
            if (states == null)
            {
                throw new AssertionException(
                    "Pawn Diary's loaded day-digest store was unavailable to the Brainwipe fixture.");
            }

            return states;
        }

        private static IDictionary RequirePendingDayDigests()
        {
            IDictionary pending =
                PendingDayDigestField.GetValue(scope.Component) as IDictionary;
            if (pending == null)
            {
                throw new AssertionException(
                    "Pawn Diary's loaded day-digest index was unavailable to the Brainwipe fixture.");
            }

            return pending;
        }

        private static void RequireReflectionSurface()
        {
            PawnDiaryRimTestScope.Require(
                ArchiveField != null
                    && DayDigestStatesField != null
                    && PendingDayDigestField != null,
                "Pawn Diary's archive/day-digest stores changed; update the Brainwipe fixture.");
        }

        private sealed class ExpectedBrainwipeNoticeFault : Exception
        {
            public ExpectedBrainwipeNoticeFault()
                : base("intentional Brainwipe notice adapter fault")
            {
            }
        }
    }
}

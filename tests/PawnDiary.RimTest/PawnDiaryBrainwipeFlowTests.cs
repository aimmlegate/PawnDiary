// Loaded-game tests for Pawn Diary's Anomaly Brainwipe memory-reset boundary.
//
// The vanilla Brainwipe outcome is deliberately not invoked here: it irreversibly rewrites a real
// pawn's memories several in-game hours after a psychic ritual. These fixtures instead exercise the
// four reversible boundaries Pawn Diary owns around it:
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
using PawnDiary.Capture;
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
        private static readonly FieldInfo PendingInteractionBatchesField =
            typeof(DiaryGameComponent).GetField("pendingInteractionBatches", PrivateInstance);
        private static readonly FieldInfo PendingAmbientInteractionNotesField =
            typeof(DiaryGameComponent).GetField("pendingAmbientInteractionNotes", PrivateInstance);
        private static readonly FieldInfo PendingAmbientThoughtNotesField =
            typeof(DiaryGameComponent).GetField("pendingAmbientThoughtNotes", PrivateInstance);
        private static readonly FieldInfo PendingTaleBatchesField =
            typeof(DiaryGameComponent).GetField("pendingTaleBatches", PrivateInstance);
        private static readonly FieldInfo PendingDayHediffsField =
            typeof(DiaryGameComponent).GetField("pendingDayHediffs", PrivateInstance);
        private static readonly FieldInfo WrittenAmbientInteractionNotesField =
            typeof(DiaryGameComponent).GetField("writtenAmbientInteractionNotes", PrivateInstance);
        private static readonly FieldInfo WrittenAmbientThoughtNotesField =
            typeof(DiaryGameComponent).GetField("writtenAmbientThoughtNotes", PrivateInstance);
        private static readonly FieldInfo WrittenDayReflectionsField =
            typeof(DiaryGameComponent).GetField("writtenDayReflections", PrivateInstance);
        private static readonly FieldInfo RejectedAmbientFrequencyKeysField =
            typeof(DiaryGameComponent).GetField(
                "rejectedAmbientInteractionFrequencyKeys", PrivateInstance);
        private static readonly FieldInfo AcceptedAmbientFrequencyKeysField =
            typeof(DiaryGameComponent).GetField(
                "acceptedAmbientInteractionFrequencyKeys", PrivateInstance);
        private static readonly FieldInfo PendingBiotechGrowthField =
            typeof(DiaryGameComponent).GetField("pendingBiotechGrowthMoments", PrivateInstance);
        private static readonly FieldInfo PendingBiotechBirthField =
            typeof(DiaryGameComponent).GetField("pendingBiotechBirths", PrivateInstance);
        private static readonly FieldInfo PendingRoyalSuccessionsField =
            typeof(DiaryGameComponent).GetField("royaltyPendingSuccessions", PrivateInstance);
        private static readonly FieldInfo PersonaBondsField =
            typeof(DiaryGameComponent).GetField("royaltyPersonaBonds", PrivateInstance);
        private static readonly FieldInfo BiotechFamilyArcsField =
            typeof(DiaryGameComponent).GetField("biotechFamilyArcs", PrivateInstance);
        private static readonly FieldInfo OdysseyActiveJourneyField =
            typeof(DiaryGameComponent).GetField("odysseyActiveJourney", PrivateInstance);
        private static readonly FieldInfo AnomalyCreepJoinerArcsField =
            typeof(DiaryGameComponent).GetField("anomalyCreepJoinerArcs", PrivateInstance);
        private static readonly FieldInfo ActiveThoughtProgressionsField =
            typeof(DiaryGameComponent).GetField("activeThoughtProgressions", PrivateInstance);
        private static readonly FieldInfo ActiveHediffProgressionsField =
            typeof(DiaryGameComponent).GetField("activeHediffProgressions", PrivateInstance);
        private static readonly FieldInfo RecentEventsField =
            typeof(DiaryGameComponent).GetField("recentEvents", PrivateInstance);
        private static readonly FieldInfo WrittenQuadrumReflectionsField =
            typeof(DiaryGameComponent).GetField("writtenQuadrumReflections", PrivateInstance);
        private static readonly FieldInfo SocialReflectionPairCooldownsField =
            typeof(DiaryGameComponent).GetField("socialReflectionPairCooldowns", PrivateInstance);
        private static readonly FieldInfo SocialReflectionWriterCooldownsField =
            typeof(DiaryGameComponent).GetField("socialReflectionWriterCooldowns", PrivateInstance);
        private static readonly FieldInfo SocialReflectionHandledSourcesField =
            typeof(DiaryGameComponent).GetField("socialReflectionHandledSources", PrivateInstance);
        private static readonly FieldInfo BaselineThoughtProgressionsField =
            typeof(DiaryGameComponent).GetField("baselineThoughtProgressionsOnNextScan", PrivateInstance);
        private static readonly FieldInfo BaselineHediffProgressionsField =
            typeof(DiaryGameComponent).GetField("baselineHediffProgressionsOnNextScan", PrivateInstance);
        private static readonly FieldInfo GlobalFactionSnapshotsField =
            typeof(DiaryGameComponent).GetField("globalFactionSnapshots", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static PawnDiaryMemoryM11RuntimeFixture.AllocatorSnapshot allocatorSnapshot;
        private static Pawn target;
        private static Pawn partner;

        /// <summary>Creates two isolated colonists and enables only the arrival route used here.</summary>
        [BeforeEach]
        public static void SetUp()
        {
            RequireReflectionSurface();
            PsychicRitualBrainwipeOutcomePatch.SetHistoryClearedNoticeOverrideForTests(null);
            scope = PawnDiaryRimTestScope.Begin(ArrivalGroupKey);
            allocatorSnapshot =
                new PawnDiaryMemoryM11RuntimeFixture.AllocatorSnapshot(scope.Component);
            target = scope.CreateAdultColonist();
            partner = scope.CreateAdultColonist();
        }

        /// <summary>Removes every disposable pawn, page, archive row, and digest fixture.</summary>
        [AfterEach]
        public static void TearDown()
        {
            PsychicRitualBrainwipeOutcomePatch.SetHistoryClearedNoticeOverrideForTests(null);
            DiaryGameComponent component = scope?.Component;
            try
            {
                // Remove every fixture carrier before lowering saved monotonic metadata. Otherwise
                // a later real allocation could reuse an identity that the fixture still owns.
                scope?.TearDown();
            }
            finally
            {
                allocatorSnapshot?.Restore(component);
                allocatorSnapshot = null;
                scope = null;
                target = null;
                partner = null;
            }
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

            // Model a completion already in flight for the target. Disabling only the disposable
            // partner's generation keeps the role-detach assertion network-free when it releases the
            // surviving recipient from sequential-pair waiting.
            shared.MarkQueued(DiaryEvent.InitiatorRole);
            shared.initiatorTitleStatus = DiaryEvent.PendingStatus;
            partnerRecord.diaryGenerationEnabled = false;

            string targetId = target.GetUniqueLoadID();
            string partnerId = partner.GetUniqueLoadID();
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

            ArchivedDiaryEntry linkedPartnerArchive = new ArchivedDiaryEntry
            {
                eventId = "pawndiary-rimtest-brainwipe-linked-" + Guid.NewGuid().ToString("N"),
                pawnId = partnerId,
                povRole = DiaryEvent.RecipientRole,
                tick = Find.TickManager.TicksGame,
                date = "RimTest Brainwipe partner archive",
                text = "The partner's independent archived memory.",
                linkedPawnId = targetId,
                linkedPawnName = target.LabelShort,
                linkedRole = DiaryEvent.InitiatorRole,
                linkedPreviewText = "A preview of the wiped pawn's retired POV.",
                linkedGenerated = true,
                linkedTitle = "Retired POV"
            };
            PawnDiaryRimTestScope.Require(
                archive.AddOrKeep(linkedPartnerArchive),
                "The Brainwipe fixture could not seed its disposable partner archive link.");
            scope.RegisterCleanup(() => archive.RemoveForEventIds(
                new HashSet<string>(StringComparer.Ordinal)
                {
                    linkedPartnerArchive.eventId
                }));

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

            PawnKnowledgeState knowledge =
                PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                    targetId,
                    partnerId,
                    partner.LabelShortCap,
                    41);
            targetRecord.knowledgeState = knowledge;
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

            // Seed every current-envelope collection not already represented by the complete-owner
            // builder. Brainwipe must reclaim them together before the new epoch becomes observable.
            knowledge.playerBackground = "A current M11 background that must be forgotten.";
            knowledge.ownerAwarenessSnapshots.Add(new SavedMemoryAwarenessSnapshot
            {
                snapshotId = "rimtest-brainwipe-awareness",
                scopeKindToken = "relationship",
                subjectKind = MemoryContractTokens.SubjectPawn,
                subjectId = partnerId,
                factStreamToken = "relationship",
                captureInvalidationGeneration = 1,
                knownnessEvidenceToken = "direct",
                trackingStateToken = "tracked",
                snapshotRevision = 1
            });
            knowledge.openCaptureEpisodes.Add(new SavedMemoryCaptureEpisode
            {
                episodeId = "rimtest-brainwipe-episode",
                captureRuleId = "rimtest.brainwipe",
                scopeKindToken = "relationship",
                factStreamToken = "relationship",
                category = MemoryContractTokens.CategoryRelationships
            });
            knowledge.repetitionGuardRows.Add(new SavedMemoryRepetitionGuardRow
            {
                ownerEpochToken = knowledge.autobiographicalEpochToken,
                guardKind = "root",
                guardKey = "rimtest-brainwipe-guard"
            });
            string oldEpoch = knowledge.autobiographicalEpochToken;
            long oldCancellation = knowledge.requestCancellationGeneration;

            PawnKnowledgeState partnerKnowledge =
                PawnDiaryMemoryM11RuntimeFixture.BuildCompleteOwner(
                    partnerId,
                    targetId,
                    target.LabelShortCap,
                    42);
            partnerRecord.knowledgeState = partnerKnowledge;
            SavedMemoryBlock partnerBlock = partnerKnowledge.standaloneBlocks[0];

            List<SavedGlobalFactionSnapshot> factionTruth =
                GlobalFactionSnapshotsField.GetValue(scope.Component)
                    as List<SavedGlobalFactionSnapshot>;
            PawnDiaryRimTestScope.Require(factionTruth != null,
                "The Brainwipe fixture could not inspect global faction current truth.");
            SavedGlobalFactionSnapshot globalTruth = new SavedGlobalFactionSnapshot
            {
                factionInstanceId = "Faction_RimTest_Brainwipe_" + Guid.NewGuid().ToString("N"),
                allocatorGeneration = 1,
                factionDefName = "RimTestFaction",
                frozenDisplayLabel = "Shared global truth",
                goodwill = 25,
                relationKindToken = "neutral",
                observedTick = Find.TickManager.TicksGame,
                trackingStateToken = "tracked",
                snapshotRevision = 1
            };
            factionTruth.Add(globalTruth);
            scope.RegisterCleanup(() => factionTruth.Remove(globalTruth));

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
                knowledge.IsCurrentSchema()
                    && knowledge.autobiographicalEpochToken != oldEpoch
                    && !string.IsNullOrWhiteSpace(knowledge.autobiographicalEpochToken)
                    && knowledge.epochFenceOnly
                    && knowledge.requestCancellationGeneration > oldCancellation
                    && knowledge.structuralRevision == 1
                    && knowledge.statusRevision == 1
                    && knowledge.completedDiaryEntryOrdinal == 1
                    && knowledge.threadRoots.Count == 0
                    && knowledge.standaloneBlocks.Count == 0
                    && knowledge.importedArchiveRows.Count == 0
                    && knowledge.ownerAwarenessSnapshots.Count == 0
                    && knowledge.openCaptureEpisodes.Count == 0
                    && knowledge.repetitionGuardRows.Count == 0
                    && string.IsNullOrEmpty(knowledge.playerBackground),
                "Brainwipe did not publish one empty current M11 epoch fence atomically.");
            PawnDiaryRimTestScope.Require(
                ReferenceEquals(partnerRecord.knowledgeState, partnerKnowledge)
                    && ReferenceEquals(partnerKnowledge.standaloneBlocks[0], partnerBlock)
                    && partnerKnowledge.threadRoots.Count == 1
                    && partnerKnowledge.standaloneBlocks.Count == 1
                    && partnerKnowledge.importedArchiveRows.Count == 1
                    && factionTruth.Contains(globalTruth),
                "Brainwipe changed another POV or component-global faction truth.");
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
            PawnDiaryRimTestScope.Require(
                string.IsNullOrWhiteSpace(shared.initiatorPawnId)
                    && shared.recipientPawnId == partnerId
                    && shared.IsSkipped(DiaryEvent.InitiatorRole)
                    && shared.initiatorTitleStatus == DiaryEvent.SkippedStatus
                    && string.IsNullOrWhiteSpace(shared.RoleForPawn(targetId)),
                "Brainwipe left its retired shared role visible or generation-eligible.");
            PawnDiaryRimTestScope.Require(
                archive.Find(
                    linkedPartnerArchive.eventId,
                    partnerId,
                    DiaryEvent.RecipientRole) == linkedPartnerArchive
                    && string.IsNullOrWhiteSpace(linkedPartnerArchive.linkedPawnId)
                    && string.IsNullOrWhiteSpace(linkedPartnerArchive.linkedPreviewText)
                    && !linkedPartnerArchive.linkedGenerated,
                "Brainwipe removed the partner archive page or retained its link to the wiped POV.");
            RequireVoiceIdentityPreserved(targetRecord);
        }

        /// <summary>
        /// Saved progression and delayed DLC ownership are memory too: a reset must establish a new
        /// arrival/death/persona baseline and discard unresolved boss/succession ownership that could
        /// otherwise finish after the pawn forgot it. The fixture touches only primitive saved rows, so
        /// it remains valid when neither Biotech nor Royalty is active.
        /// </summary>
        [Test]
        public static void ComponentResetRebaselinesProgressionAndDelayedDlcOwnership()
        {
            string targetId = target.GetUniqueLoadID();
            string partnerId = partner.GetUniqueLoadID();
            PawnDiaryRecord record = scope.RequireDiaryRecord(target);
            PawnProgressionState progression = record.EnsureProgressionState();
            progression.arrivalAnniversaryStartTick = 1234;
            progression.arrivalAnniversaryBoundaryResolved = true;
            progression.lastArrivalAnniversaryYear = 7;
            progression.bondedDeathMemories.Add(new BondedDeathMemoryState
            {
                victimId = "PawnDiary_RimTest_PreWipeVictim",
                victimName = "Forgotten victim",
                relationDefName = "Spouse",
                relationLabel = "spouse",
                deathTick = 4321,
                lastProcessedAnniversaryYear = 2,
            });
            progression.lastBondedDeathDiscoveryTick = 5432;
            progression.bondedDeathHistoryMigrationComplete = false;
            progression.lastBondedDeathPageDay = GenDate.DaysPassed;

            MechanitorObservationState mechanitor = progression.EnsureBiotechState()
                .EnsureMechanitorObservation();
            MechanitorBossCallObservationState unresolved = new MechanitorBossCallObservationState
            {
                bossgroupDefName = "PawnDiary_RimTest_UnresolvedGroup",
                bossDefName = "PawnDiary_RimTest_UnresolvedBoss",
                calledTick = 6000,
                defeatedObserved = false,
            };
            MechanitorBossCallObservationState completed = new MechanitorBossCallObservationState
            {
                bossgroupDefName = "PawnDiary_RimTest_CompletedGroup",
                bossDefName = "PawnDiary_RimTest_CompletedBoss",
                calledTick = 7000,
                defeatedObserved = true,
            };
            mechanitor.bossCalls.Add(unresolved);
            mechanitor.bossCalls.Add(completed);

            string suffix = Guid.NewGuid().ToString("N");
            List<RoyalSuccessionState> successions =
                RequireTypedList<RoyalSuccessionState>(PendingRoyalSuccessionsField);
            RoyalSuccessionState targetSuccession = new RoyalSuccessionState
            {
                correlationId = "rimtest-brainwipe-target-" + suffix,
                heirPawnId = targetId
            };
            RoyalSuccessionState prefixSuccession = new RoyalSuccessionState
            {
                correlationId = "rimtest-brainwipe-prefix-" + suffix,
                heirPawnId = targetId + "_prefix"
            };
            RoyalSuccessionState partnerSuccession = new RoyalSuccessionState
            {
                correlationId = "rimtest-brainwipe-partner-" + suffix,
                heirPawnId = partnerId
            };
            successions.Add(targetSuccession);
            successions.Add(prefixSuccession);
            successions.Add(partnerSuccession);
            scope.RegisterCleanup(() => successions.RemoveAll(row => row?.correlationId != null
                && row.correlationId.EndsWith(suffix, StringComparison.Ordinal)));

            List<PersonaBondState> personaBonds =
                RequireTypedList<PersonaBondState>(PersonaBondsField);
            PersonaBondState pendingPersona = PersonaFixture(
                "PawnDiary_RimTest_PendingPersona_" + suffix,
                targetId,
                PersonaBondPhaseTokens.SeparationPending);
            pendingPersona.previousPawnId = partnerId;
            pendingPersona.pendingSeparationTick = 1;
            pendingPersona.firstConsequentialKillObserved = true;
            pendingPersona.firstConsequentialKillEventRecorded = true;
            PersonaBondState separatedPersona = PersonaFixture(
                "PawnDiary_RimTest_SeparatedPersona_" + suffix,
                targetId,
                PersonaBondPhaseTokens.Separated);
            separatedPersona.previousPawnId = partnerId;
            separatedPersona.separationEmitted = true;
            PersonaBondState prefixPersona = PersonaFixture(
                "PawnDiary_RimTest_PrefixPersona_" + suffix,
                targetId + "_prefix",
                PersonaBondPhaseTokens.Separated);
            prefixPersona.separationEmitted = true;
            PersonaBondState partnerPersona = PersonaFixture(
                "PawnDiary_RimTest_PartnerPersona_" + suffix,
                partnerId,
                PersonaBondPhaseTokens.SeparationPending);
            partnerPersona.pendingSeparationTick = 2;
            PersonaBondState terminalPersona = PersonaFixture(
                "PawnDiary_RimTest_TerminalPersona_" + suffix,
                targetId,
                PersonaBondPhaseTokens.Ended);
            terminalPersona.endedTick = 3;
            terminalPersona.endCauseToken = PersonaEndCauseTokens.PawnDeath;
            personaBonds.Add(pendingPersona);
            personaBonds.Add(separatedPersona);
            personaBonds.Add(prefixPersona);
            personaBonds.Add(partnerPersona);
            personaBonds.Add(terminalPersona);
            scope.RegisterCleanup(() => personaBonds.RemoveAll(row => row?.weaponThingId != null
                && row.weaponThingId.EndsWith(suffix, StringComparison.Ordinal)));

            int wipeTick = Find.TickManager.TicksGame;
            scope.Component.ForgetDiaryHistory(target);

            PawnDiaryRimTestScope.Require(
                progression.arrivalAnniversaryStartTick == wipeTick
                    && progression.arrivalAnniversaryBoundaryResolved
                    && progression.lastArrivalAnniversaryYear == 0,
                "Brainwipe did not make its boundary the pawn's new arrival-anniversary epoch.");
            PawnDiaryRimTestScope.Require(
                progression.bondedDeathMemories.Count == 0
                    && progression.lastBondedDeathDiscoveryTick == wipeTick
                    && progression.bondedDeathHistoryMigrationComplete
                    && progression.lastBondedDeathPageDay == int.MinValue,
                "Brainwipe retained or could rediscover a pre-wipe bonded-death memory.");
            PawnDiaryRimTestScope.Require(
                mechanitor.bossCalls.Count == 1
                    && ReferenceEquals(mechanitor.bossCalls[0], completed),
                "Brainwipe retained unresolved boss ownership or removed inert completed deduplication.");
            PawnDiaryRimTestScope.Require(
                !successions.Contains(targetSuccession)
                    && successions.Contains(prefixSuccession)
                    && successions.Contains(partnerSuccession),
                "Brainwipe did not remove only the exact target-owned pending succession.");

            PersonaBondState resetPending = personaBonds.Find(row =>
                row?.weaponThingId == pendingPersona.weaponThingId);
            PersonaBondState resetSeparated = personaBonds.Find(row =>
                row?.weaponThingId == separatedPersona.weaponThingId);
            PawnDiaryRimTestScope.Require(
                resetPending != null
                    && resetPending.phaseToken == PersonaBondPhaseTokens.Active
                    && resetPending.previousPawnId.Length == 0
                    && resetPending.bondStartedTick == wipeTick
                    && resetPending.lastPrimaryObservedTick == wipeTick
                    && resetPending.pendingSeparationTick == -1
                    && !resetPending.separationEmitted
                    && resetPending.firstConsequentialKillObserved
                    && resetPending.firstConsequentialKillEventRecorded,
                "Brainwipe did not rebaseline pending persona autobiography while preserving dedup facts.");
            PawnDiaryRimTestScope.Require(
                resetSeparated != null
                    && resetSeparated.phaseToken == PersonaBondPhaseTokens.Active
                    && resetSeparated.previousPawnId.Length == 0
                    && !resetSeparated.separationEmitted,
                "Brainwipe left a separated persona able to emit a false recovery page.");
            PawnDiaryRimTestScope.Require(
                ReferenceEquals(personaBonds.Find(row => row?.weaponThingId
                    == prefixPersona.weaponThingId), prefixPersona)
                    && ReferenceEquals(personaBonds.Find(row => row?.weaponThingId
                        == partnerPersona.weaponThingId), partnerPersona)
                    && ReferenceEquals(personaBonds.Find(row => row?.weaponThingId
                        == terminalPersona.weaponThingId), terminalPersona),
                "Brainwipe rewrote a prefix-collision, unrelated, or inert terminal persona row.");
        }

        /// <summary>
        /// Shared DLC arcs keep world truth for other participants while projecting the wiped pawn's
        /// exact autobiography: family support becomes child-POV-relative, an active Odyssey journey
        /// excludes only that landing writer, and only non-terminal CreepJoiner continuity is forgotten.
        /// No DLC ownership flag is required because every seeded row is a detached primitive model.
        /// </summary>
        [Test]
        public static void ComponentResetProjectsSharedDlcArcsPerExactPov()
        {
            string targetId = target.GetUniqueLoadID();
            string partnerId = partner.GetUniqueLoadID();
            string prefixId = targetId + "_prefix";
            string otherChildId = "PawnDiary_RimTest_BrainwipeFamily_"
                + Guid.NewGuid().ToString("N");
            scope.RequireDiaryRecord(target);

            List<BiotechFamilyArcState> familyArcs =
                RequireTypedList<BiotechFamilyArcState>(BiotechFamilyArcsField);
            BiotechFamilyArcState targetChildArc = new BiotechFamilyArcState
            {
                familyArcId = "biotech-family|" + targetId,
                childId = targetId,
                birtherId = "PawnDiary_RimTest_Birther",
                fatherId = partnerId,
                birthOutcomeToken = BiotechBirthOutcomeTokens.Healthy,
                birthTick = Math.Max(1, Find.TickManager.TicksGame - 100),
                recordedGrowthAges = new List<int> { 7, 10 },
                supporters = new List<FamilySupportObservationState>
                {
                    FamilySupportRow(targetId, 9),
                    FamilySupportRow(partnerId, 5),
                    FamilySupportRow(prefixId, 3)
                }
            };
            BiotechFamilyArcState otherChildArc = new BiotechFamilyArcState
            {
                familyArcId = "biotech-family|" + otherChildId,
                childId = otherChildId,
                fatherId = targetId,
                supporters = new List<FamilySupportObservationState>
                {
                    FamilySupportRow(targetId, 4),
                    FamilySupportRow(partnerId, 6)
                }
            };
            familyArcs.Add(targetChildArc);
            familyArcs.Add(otherChildArc);
            scope.RegisterCleanup(() =>
            {
                List<BiotechFamilyArcState> current =
                    BiotechFamilyArcsField.GetValue(scope.Component)
                        as List<BiotechFamilyArcState>;
                current?.RemoveAll(row => ReferenceEquals(row, targetChildArc)
                    || ReferenceEquals(row, otherChildArc)
                    || row?.familyArcId == targetChildArc.familyArcId
                    || row?.familyArcId == otherChildArc.familyArcId);
            });

            object oldJourney = OdysseyActiveJourneyField.GetValue(scope.Component);
            OdysseyJourneyState journey = new OdysseyJourneyState
            {
                journeyId = "odyssey-journey|PawnDiary_RimTest_Ship|100",
                shipStableId = "PawnDiary_RimTest_Ship",
                shipName = "Disposable memory ship",
                departureTick = 100,
                launchQualityBand = OdysseyLaunchQualityTokens.Excellent,
                sourceComplete = true,
                origin = new OdysseyLocationState
                {
                    stableKey = "rimtest-origin",
                    visibleLabel = "Disposable origin"
                },
                writers = new List<OdysseyWriterState>
                {
                    OdysseyWriter(targetId, OdysseyJourneyRoleTokens.Pilot),
                    OdysseyWriter(partnerId, OdysseyJourneyRoleTokens.Copilot),
                    OdysseyWriter(prefixId, OdysseyJourneyRoleTokens.Crew)
                }
            };
            OdysseyActiveJourneyField.SetValue(scope.Component, journey);
            scope.RegisterCleanup(() => OdysseyActiveJourneyField.SetValue(
                scope.Component, oldJourney));

            List<CreepJoinerArcState> creepArcs =
                RequireTypedList<CreepJoinerArcState>(AnomalyCreepJoinerArcsField);
            CreepJoinerArcState targetNonterminal = new CreepJoinerArcState
            {
                pawnId = targetId,
                arrivalEventId = "PawnDiary_RimTest_PreWipeArrival",
                joinedTick = 100,
                lastVisiblePhase = AnomalyOutcomeTokens.SurgicalReveal,
                lastVisibleEventId = "PawnDiary_RimTest_PreWipeReveal",
                terminal = false,
                schemaVersion = AnomalyPersistencePolicy.CurrentCreepJoinerArcSchemaVersion
            };
            CreepJoinerArcState targetTerminal = new CreepJoinerArcState
            {
                pawnId = targetId,
                joinedTick = 100,
                lastVisiblePhase = AnomalyOutcomeTokens.Rejected,
                lastVisibleEventId = "PawnDiary_RimTest_TerminalWorldTruth",
                terminal = true,
                schemaVersion = AnomalyPersistencePolicy.CurrentCreepJoinerArcSchemaVersion
            };
            CreepJoinerArcState prefixNonterminal = new CreepJoinerArcState
            {
                pawnId = prefixId,
                joinedTick = 100,
                lastVisiblePhase = AnomalyOutcomeTokens.SurgicalReveal,
                lastVisibleEventId = "PawnDiary_RimTest_PrefixReveal",
                terminal = false,
                schemaVersion = AnomalyPersistencePolicy.CurrentCreepJoinerArcSchemaVersion
            };
            creepArcs.Add(targetNonterminal);
            creepArcs.Add(targetTerminal);
            creepArcs.Add(prefixNonterminal);
            scope.RegisterCleanup(() =>
            {
                List<CreepJoinerArcState> current =
                    AnomalyCreepJoinerArcsField.GetValue(scope.Component)
                        as List<CreepJoinerArcState>;
                current?.RemoveAll(row => row != null && (row.pawnId == targetId
                    || row.pawnId == prefixId));
            });

            scope.Component.ForgetDiaryHistory(target);

            FamilySupportObservationState targetAdultOtherChild =
                otherChildArc.supporters.Find(row => row.adultId == targetId);
            PawnDiaryRimTestScope.Require(
                targetChildArc.supporters.Exists(row => row.adultId == targetId
                        && row.adultMemoryBoundaryActive)
                    && targetAdultOtherChild != null
                    && targetAdultOtherChild.adultMemoryBoundaryActive
                    && targetChildArc.supporters.Exists(row => row.adultId == prefixId
                        && !row.adultMemoryBoundaryActive),
                "Brainwipe did not retain shared supporter truth with only the exact adult POV rebased.");
            FamilySupportObservationState childView =
                BiotechFamilyMemoryResetPolicy.ProjectSupporterForPov(
                    targetChildArc,
                    targetChildArc.supporters.Find(row => row.adultId == partnerId),
                    targetId);
            FamilySupportObservationState adultView =
                BiotechFamilyMemoryResetPolicy.ProjectSupporterForPov(
                    targetChildArc,
                    targetChildArc.supporters.Find(row => row.adultId == partnerId),
                    partnerId);
            PawnDiaryRimTestScope.Require(
                childView != null && childView.lessonCount == 0
                    && adultView != null && adultView.lessonCount == 5
                    && !BiotechFamilyMemoryResetPolicy.HasObservedUpbringingForPov(
                        targetChildArc, targetId)
                    && BiotechFamilyMemoryResetPolicy.HasObservedUpbringingForPov(
                        targetChildArc, partnerId),
                "Brainwipe erased shared adult truth or exposed pre-wipe upbringing to the child POV.");
            FamilySupportObservationState wipedAdultView =
                BiotechFamilyMemoryResetPolicy.ProjectSupporterForPov(
                    otherChildArc,
                    targetAdultOtherChild,
                    targetId);
            FamilySupportObservationState survivingOtherChildView =
                BiotechFamilyMemoryResetPolicy.ProjectSupporterForPov(
                    otherChildArc,
                    targetAdultOtherChild,
                    otherChildId);
            PawnDiaryRimTestScope.Require(
                wipedAdultView != null && wipedAdultView.lessonCount == 0
                    && survivingOtherChildView != null
                    && survivingOtherChildView.lessonCount == 4
                    && !BiotechFamilyMemoryResetPolicy.HasObservedUpbringingForPov(
                        otherChildArc, targetId)
                    && BiotechFamilyMemoryResetPolicy.HasObservedUpbringingForPov(
                        otherChildArc, otherChildId),
                "Brainwipe exposed the adult's old support or erased the other child's own memory.");
            PawnDiaryRimTestScope.Require(
                targetChildArc.birtherId == "PawnDiary_RimTest_Birther"
                    && targetChildArc.fatherId == partnerId
                    && targetChildArc.recordedGrowthAges.Count == 2
                    && otherChildArc.fatherId == targetId
                    && otherChildArc.supporters.Find(row => row.adultId == partnerId).lessonCount == 6,
                "Brainwipe damaged family identity, milestones, parent truth, or an unaffected POV.");

            PawnDiaryRimTestScope.Require(
                journey.memoryExcludedWriterPawnIds.Count == 1
                    && journey.memoryExcludedWriterPawnIds[0] == targetId
                    && journey.writers.Count == 3,
                "Brainwipe damaged the shared Odyssey journey or missed its exact writer exclusion.");
            List<OdysseyWriterCandidate> survivingWriters =
                OdysseyWriterMemoryBoundaryPolicy.ExcludeWriters(
                    journey.ToSnapshot().writers,
                    journey.memoryExcludedWriterPawnIds);
            PawnDiaryRimTestScope.Require(
                survivingWriters.Count == 2
                    && survivingWriters.Exists(row => row.pawnId == partnerId)
                    && survivingWriters.Exists(row => row.pawnId == prefixId)
                    && !survivingWriters.Exists(row => row.pawnId == targetId),
                "Odyssey writer projection removed an unaffected/prefix writer or retained the target.");
            PawnDiaryRimTestScope.Require(
                OdysseyWriterMemoryBoundaryPolicy.ExcludeWriters(
                    new List<OdysseyWriterCandidate>
                    {
                        new OdysseyWriterCandidate { pawnId = targetId }
                    },
                    new OdysseyJourneySnapshot().memoryExcludedWriterPawnIds).Count == 1,
                "A fresh post-wipe Odyssey journey did not restore the target writer.");

            List<CreepJoinerArcState> afterCreepReset =
                RequireTypedList<CreepJoinerArcState>(AnomalyCreepJoinerArcsField);
            CreepJoinerArcState retainedTarget = afterCreepReset.Find(row => row?.pawnId == targetId);
            PawnDiaryRimTestScope.Require(
                retainedTarget != null && retainedTarget.terminal
                    && retainedTarget.lastVisiblePhase == AnomalyOutcomeTokens.Rejected
                    && retainedTarget.lastVisibleEventId
                        == "PawnDiary_RimTest_TerminalWorldTruth"
                    && afterCreepReset.Exists(row => row?.pawnId == prefixId
                        && !row.terminal
                        && row.lastVisiblePhase == AnomalyOutcomeTokens.SurgicalReveal)
                    && !afterCreepReset.Exists(row => ReferenceEquals(row, targetNonterminal)),
                "Brainwipe removed terminal CreepJoiner truth or retained exact nonterminal autobiography.");
        }

        /// <summary>
        /// Every delayed writer store is resolved at the reset boundary. The wiped pawn receives no
        /// page from pre-wipe facts, while the other POV of a shared interaction remains a solo memory
        /// for its independent owner. Same-day guards are also released for genuinely new facts.
        /// </summary>
        [Test]
        public static void ComponentResetSettlesEveryPendingWriterWithoutResurrectingHistory()
        {
            string targetId = target.GetUniqueLoadID();
            string partnerId = partner.GetUniqueLoadID();
            PawnDiaryRecord targetRecord = scope.RequireDiaryRecord(target);
            PawnDiaryRecord partnerRecord = scope.RequireDiaryRecord(partner);

            IDictionary interactionBatches = RequireDictionary(PendingInteractionBatchesField);
            string interactionKey = "rimtestBrainwipe|" + targetId + "|" + partnerId;
            object interactionBatch = NewPendingState(
                "PendingInteractionBatch",
                "initiator", target,
                "recipient", partner,
                "initiatorPawnId", targetId,
                "recipientPawnId", partnerId,
                "frequencyGroupKey", "rimtestBrainwipe",
                "frequencyAdmissionAccepted", true,
                "firstTick", Find.TickManager.TicksGame,
                "lastTick", Find.TickManager.TicksGame,
                "firstDefName", "PawnDiary_RimTest_BrainwipePendingInteraction",
                "firstLabel", "pending Brainwipe interaction",
                "firstInitiatorText", "A pre-wipe interaction from the target POV.",
                "firstRecipientText", "A pre-wipe interaction from the partner POV.");
            PendingList(interactionBatch, "initiatorLines").Add(
                "A pre-wipe interaction from the target POV.");
            PendingList(interactionBatch, "recipientLines").Add(
                "A pre-wipe interaction from the partner POV.");
            interactionBatches[interactionKey] = interactionBatch;
            scope.RegisterCleanup(() => interactionBatches.Remove(interactionKey));

            string ambientInteractionKey = DailyEmissionGuardPolicy.InteractionKey(
                "rimtestBrainwipe", targetId, GenDate.DaysPassed);
            IDictionary ambientInteractions = RequireDictionary(PendingAmbientInteractionNotesField);
            ambientInteractions[ambientInteractionKey] = NewPendingState(
                "PendingAmbientInteractionNote", "pawnId", targetId);
            scope.RegisterCleanup(() => ambientInteractions.Remove(ambientInteractionKey));

            string ambientThoughtKey = DailyEmissionGuardPolicy.ThoughtKey(
                targetId, GenDate.DaysPassed);
            IDictionary ambientThoughts = RequireDictionary(PendingAmbientThoughtNotesField);
            ambientThoughts[ambientThoughtKey] = NewPendingState(
                "PendingAmbientThoughtNote", "pawnId", targetId);
            scope.RegisterCleanup(() => ambientThoughts.Remove(ambientThoughtKey));

            string taleKey = "rimtestBrainwipe|tale|" + targetId;
            IDictionary taleBatches = RequireDictionary(PendingTaleBatchesField);
            taleBatches[taleKey] = NewPendingState(
                "PendingTaleBatch", "pawnId", targetId);
            scope.RegisterCleanup(() => taleBatches.Remove(taleKey));

            string dayKey = targetId + "|" + GenDate.DaysPassed;
            IDictionary dayHediffs = RequireDictionary(PendingDayHediffsField);
            dayHediffs[dayKey] = NewDayHediffList();
            scope.RegisterCleanup(() => dayHediffs.Remove(dayKey));

            HashSet<string> writtenAmbientInteractions = RequireStringSet(
                WrittenAmbientInteractionNotesField);
            HashSet<string> writtenAmbientThoughts = RequireStringSet(
                WrittenAmbientThoughtNotesField);
            HashSet<string> writtenDayReflections = RequireStringSet(
                WrittenDayReflectionsField);
            IList rejectedFrequencyKeys = RequireList(RejectedAmbientFrequencyKeysField);
            IList acceptedFrequencyKeys = RequireList(AcceptedAmbientFrequencyKeysField);
            writtenAmbientInteractions.Add(ambientInteractionKey);
            writtenAmbientThoughts.Add(ambientThoughtKey);
            writtenDayReflections.Add(dayKey);
            rejectedFrequencyKeys.Add(ambientInteractionKey);
            acceptedFrequencyKeys.Add(ambientInteractionKey);
            scope.RegisterCleanup(() =>
            {
                writtenAmbientInteractions.Remove(ambientInteractionKey);
                writtenAmbientThoughts.Remove(ambientThoughtKey);
                writtenDayReflections.Remove(dayKey);
                rejectedFrequencyKeys.Remove(ambientInteractionKey);
                acceptedFrequencyKeys.Remove(ambientInteractionKey);
            });

            List<PendingBiotechGrowthMoment> pendingGrowth =
                RequireTypedList<PendingBiotechGrowthMoment>(PendingBiotechGrowthField);
            PendingBiotechGrowthMoment targetGrowth = new PendingBiotechGrowthMoment
            {
                pawnId = targetId,
                birthdayAge = 7
            };
            PendingBiotechGrowthMoment partnerGrowth = new PendingBiotechGrowthMoment
            {
                pawnId = partnerId,
                birthdayAge = 10
            };
            pendingGrowth.Add(targetGrowth);
            pendingGrowth.Add(partnerGrowth);
            scope.RegisterCleanup(() =>
            {
                pendingGrowth.Remove(targetGrowth);
                pendingGrowth.Remove(partnerGrowth);
            });

            string sharedBirthChildId = "PawnDiary_RimTest_BrainwipeSharedBirth_"
                + Guid.NewGuid().ToString("N");
            string targetOnlyBirthChildId = "PawnDiary_RimTest_BrainwipeTargetBirth_"
                + Guid.NewGuid().ToString("N");
            List<PendingBiotechBirthState> pendingBirths =
                RequireTypedList<PendingBiotechBirthState>(PendingBiotechBirthField);
            PendingBiotechBirthState sharedBirth = PendingBirth(
                sharedBirthChildId,
                targetId,
                partnerId,
                includePartner: true);
            PendingBiotechBirthState targetOnlyBirth = PendingBirth(
                targetOnlyBirthChildId,
                targetId,
                partnerId,
                includePartner: false);
            pendingBirths.Add(sharedBirth);
            pendingBirths.Add(targetOnlyBirth);
            scope.RegisterCleanup(() => pendingBirths.RemoveAll(row => row?.snapshot != null
                && (row.snapshot.childId == sharedBirthChildId
                    || row.snapshot.childId == targetOnlyBirthChildId)));

            int partnerEntriesBefore = partnerRecord.eventIds.Count;
            scope.Component.ForgetDiaryHistory(target);

            PawnDiaryRimTestScope.Require(
                !interactionBatches.Contains(interactionKey)
                    && !ambientInteractions.Contains(ambientInteractionKey)
                    && !ambientThoughts.Contains(ambientThoughtKey)
                    && !taleBatches.Contains(taleKey)
                    && !dayHediffs.Contains(dayKey),
                "Brainwipe retained a delayed writer-owned pre-wipe source.");
            PawnDiaryRimTestScope.Require(
                !writtenAmbientInteractions.Contains(ambientInteractionKey)
                    && !writtenAmbientThoughts.Contains(ambientThoughtKey)
                    && !writtenDayReflections.Contains(dayKey)
                    && !rejectedFrequencyKeys.Contains(ambientInteractionKey)
                    && !acceptedFrequencyKeys.Contains(ambientInteractionKey),
                "Brainwipe retained a same-day guard that blocked post-wipe memories.");
            PawnDiaryRimTestScope.Require(
                targetRecord.eventIds.Count == 0
                    && partnerRecord.eventIds.Count == partnerEntriesBefore + 1,
                "Brainwipe recreated the wiped POV or discarded the partner's independent POV.");
            DiaryEvent survivor = scope.Component.FindEventById(
                partnerRecord.eventIds[partnerRecord.eventIds.Count - 1]);
            PawnDiaryRimTestScope.Require(
                survivor != null
                    && survivor.solo
                    && survivor.initiatorPawnId == partnerId
                    && survivor.interactionDefName
                        == "PawnDiary_RimTest_BrainwipePendingInteraction",
                "The shared pending interaction did not settle as the partner's solo memory.");
            PawnDiaryRimTestScope.Require(
                !pendingGrowth.Contains(targetGrowth)
                    && pendingGrowth.Contains(partnerGrowth),
                "Brainwipe retained its delayed growth writer or removed the partner's row.");
            PendingBiotechBirthState survivingBirth = pendingBirths.Find(row => row?.snapshot != null
                && row.snapshot.childId == sharedBirthChildId);
            PawnDiaryRimTestScope.Require(
                survivingBirth != null
                    && !pendingBirths.Exists(row => row?.snapshot != null
                        && row.snapshot.childId == targetOnlyBirthChildId)
                    && survivingBirth.writers?.writers?.Count == 1
                    && survivingBirth.writers.writers[0].pawnId == partnerId
                    && survivingBirth.eventContext?.writers?.Count == 1
                    && survivingBirth.eventContext.writers[0].pawnId == partnerId
                    && survivingBirth.eventContext.writers[0].continuity
                        == "partner birth continuity",
                "Brainwipe did not project delayed birth ownership to the surviving writer.");
        }

        /// <summary>
        /// The reset also replaces live scanner baselines and removes only exact target-owned
        /// correlation/dedup/pacing rows. Prefix-collision pawns, the other POV of a pair, shared
        /// occurrence keys, terminal source ownership, and the Anomaly Tale claim all survive.
        /// </summary>
        [Test]
        public static void ComponentResetClearsExactTransientOwnershipAndRebaselinesPacing()
        {
            string targetId = target.GetUniqueLoadID();
            string partnerId = partner.GetUniqueLoadID();
            string prefixId = targetId + "0";
            int now = Find.TickManager?.TicksGame ?? 0;

            IDictionary thoughts = RequireDictionary(ActiveThoughtProgressionsField);
            string targetThoughtKey = targetId + "|rimtest_brainwipe_target_thought";
            string prefixThoughtKey = prefixId + "|rimtest_brainwipe_prefix_thought";
            string partnerThoughtKey = partnerId + "|rimtest_brainwipe_partner_thought";
            object targetThought = NewPendingState(
                "ActiveThoughtProgressionState", "currentSeverity", 99,
                "currentStageKey", "RimTest|99");
            object prefixThought = NewPendingState(
                "ActiveThoughtProgressionState", "currentSeverity", 17,
                "currentStageKey", "RimTest|17");
            object partnerThought = NewPendingState(
                "ActiveThoughtProgressionState", "currentSeverity", 23,
                "currentStageKey", "RimTest|23");
            thoughts[targetThoughtKey] = targetThought;
            thoughts[prefixThoughtKey] = prefixThought;
            thoughts[partnerThoughtKey] = partnerThought;

            IDictionary hediffs = RequireDictionary(ActiveHediffProgressionsField);
            string targetHediffKey = targetId + "|RimTest_TargetHediff|whole";
            string prefixHediffKey = prefixId + "|RimTest_PrefixHediff|whole";
            string partnerHediffKey = partnerId + "|RimTest_PartnerHediff|whole";
            object targetHediff = NewPendingState(
                "ActiveHediffProgressionState", "currentStage", 99);
            object prefixHediff = NewPendingState(
                "ActiveHediffProgressionState", "currentStage", 17);
            object partnerHediff = NewPendingState(
                "ActiveHediffProgressionState", "currentStage", 23);
            hediffs[targetHediffKey] = targetHediff;
            hediffs[prefixHediffKey] = prefixHediff;
            hediffs[partnerHediffKey] = partnerHediff;
            scope.RegisterCleanup(() =>
            {
                thoughts.Remove(targetThoughtKey);
                thoughts.Remove(prefixThoughtKey);
                thoughts.Remove(partnerThoughtKey);
                hediffs.Remove(targetHediffKey);
                hediffs.Remove(prefixHediffKey);
                hediffs.Remove(partnerHediffKey);
            });
            bool thoughtGlobalBaseline = (bool)BaselineThoughtProgressionsField.GetValue(scope.Component);
            bool hediffGlobalBaseline = (bool)BaselineHediffProgressionsField.GetValue(scope.Component);

            IDictionary recent = RequireDictionary(RecentEventsField);
            string[] removedRecentKeys =
            {
                "thought|" + targetId + "|RimTest_Thought",
                "romance|" + partnerId + "|" + targetId + "|RimTest_Romance",
                "event-type|Thought|GenerateSolo|" + targetId,
                "external|rimtest-event|" + targetId,
                "anniversary-arrival|" + targetId + "|1",
                "mechanitor-mech-loss|" + targetId + "|RimTestMech",
                "royal-succession|RimTestDeceased|" + targetId
                    + "|Empire|Acolyte|" + now
            };
            string[] retainedRecentKeys =
            {
                "thought|" + prefixId + "|RimTest_Prefix",
                "thought|" + partnerId + "|RimTest_Partner",
                "ritual|RimTest_Ritual|" + targetId + "|" + partnerId + "|" + now,
                "raid|RimTest_Raid|" + targetId,
                "external-custom|rimtest-event|" + targetId,
                "anomaly-study|rimtest-occurrence|" + targetId,
                "royal-succession|" + targetId + "|" + partnerId
                    + "|Empire|Acolyte|" + now
            };
            for (int i = 0; i < removedRecentKeys.Length; i++)
            {
                recent[removedRecentKeys[i]] = NewPendingState(
                    "RecentEventEntry", "tick", now, "windowTicks", 60000);
            }
            for (int i = 0; i < retainedRecentKeys.Length; i++)
            {
                recent[retainedRecentKeys[i]] = NewPendingState(
                    "RecentEventEntry", "tick", now, "windowTicks", 60000);
            }

            HashSet<string> quadrum = RequireStringSet(WrittenQuadrumReflectionsField);
            string targetQuadrum = targetId + "|17";
            string prefixQuadrum = prefixId + "|17";
            string partnerQuadrum = partnerId + "|17";
            quadrum.Add(targetQuadrum);
            quadrum.Add(prefixQuadrum);
            quadrum.Add(partnerQuadrum);
            scope.RegisterCleanup(() =>
            {
                quadrum.Remove(targetQuadrum);
                quadrum.Remove(prefixQuadrum);
                quadrum.Remove(partnerQuadrum);
            });

            List<SocialReflectionWriterCooldownState> writerCooldowns =
                RequireTypedList<SocialReflectionWriterCooldownState>(
                    SocialReflectionWriterCooldownsField);
            List<SocialReflectionPairCooldownState> pairCooldowns =
                RequireTypedList<SocialReflectionPairCooldownState>(
                    SocialReflectionPairCooldownsField);
            List<SocialReflectionHandledSourceState> handledSources =
                RequireTypedList<SocialReflectionHandledSourceState>(
                    SocialReflectionHandledSourcesField);
            SocialReflectionWriterCooldownState targetWriter = new SocialReflectionWriterCooldownState
            {
                writerPawnId = targetId,
                cooldownUntilTick = now + 180000,
                reservationSourceKey = "rimtest-target-writer"
            };
            SocialReflectionWriterCooldownState prefixWriter = new SocialReflectionWriterCooldownState
            {
                writerPawnId = prefixId,
                cooldownUntilTick = now + 180000,
                reservationSourceKey = "rimtest-prefix-writer"
            };
            SocialReflectionWriterCooldownState partnerWriter = new SocialReflectionWriterCooldownState
            {
                writerPawnId = partnerId,
                cooldownUntilTick = now + 180000,
                reservationSourceKey = "rimtest-partner-writer"
            };
            SocialReflectionPairCooldownState targetPair = new SocialReflectionPairCooldownState
            {
                pairKey = SocialReflectionPolicy.PairKey(targetId, partnerId),
                cooldownUntilTick = now + 900000,
                reservationSourceKey = "rimtest-target-pair"
            };
            SocialReflectionPairCooldownState prefixPair = new SocialReflectionPairCooldownState
            {
                pairKey = SocialReflectionPolicy.PairKey(prefixId, partnerId),
                cooldownUntilTick = now + 900000,
                reservationSourceKey = "rimtest-prefix-pair"
            };
            SocialReflectionHandledSourceState handled = new SocialReflectionHandledSourceState
            {
                sourceKey = "rimtest-handled|" + targetId,
                reflectionEventId = "rimtest-terminal-owner",
                handledTick = now
            };
            writerCooldowns.Add(targetWriter);
            writerCooldowns.Add(prefixWriter);
            writerCooldowns.Add(partnerWriter);
            pairCooldowns.Add(targetPair);
            pairCooldowns.Add(prefixPair);
            handledSources.Add(handled);
            scope.RegisterCleanup(() =>
            {
                writerCooldowns.Remove(targetWriter);
                writerCooldowns.Remove(prefixWriter);
                writerCooldowns.Remove(partnerWriter);
                pairCooldowns.Remove(targetPair);
                pairCooldowns.Remove(prefixPair);
                handledSources.Remove(handled);
            });

            List<AnomalyRecentStudyFact> originalStudies =
                AnomalyRecentStudyCache.SnapshotForTests();
            List<AnomalyStudyTaleClaim> originalClaims =
                AnomalyStudySuppressionCache.SnapshotForTests();
            scope.RegisterCleanup(() =>
            {
                AnomalyRecentStudyCache.RestoreForTests(originalStudies);
                AnomalyStudySuppressionCache.RestoreForTests(originalClaims);
            });
            string targetEntity = "RimTest_TargetEntity_" + Guid.NewGuid().ToString("N");
            string partnerEntity = "RimTest_PartnerEntity_" + Guid.NewGuid().ToString("N");
            PawnDiaryRimTestScope.Require(
                AnomalyRecentStudyCache.Register(new AnomalyRecentStudyFact
                {
                    studierPawnId = targetId,
                    studiedEntityId = targetEntity,
                    studiedDefName = "RimTestEntity",
                    studiedTick = now
                }, now, 60000)
                && AnomalyRecentStudyCache.Register(new AnomalyRecentStudyFact
                {
                    studierPawnId = partnerId,
                    studiedEntityId = partnerEntity,
                    studiedDefName = "RimTestEntity",
                    studiedTick = now
                }, now, 60000)
                && AnomalyStudySuppressionCache.Register(new AnomalyStudyTaleClaim
                {
                    studierPawnId = targetId,
                    studiedEntityId = targetEntity,
                    studiedDefName = "RimTestEntity",
                    studyJobId = "RimTestJob",
                    acceptedTick = now
                }, now, 60000),
                "Brainwipe fixture could not seed Anomaly transient ownership.");

            int titleRecentBaseline = RoyalTitleThoughtCorrelation.RecentCountForTests;
            RoyalTitleThoughtSnapshot targetTitleThought = new RoyalTitleThoughtSnapshot
            {
                pawnId = targetId,
                titleDefName = "RimTestAcolyte",
                relationshipToken = RoyalTitleThoughtRelationshipTokens.Award,
                tick = now
            };
            RoyalTitleThoughtSnapshot prefixTitleThought = new RoyalTitleThoughtSnapshot
            {
                pawnId = prefixId,
                titleDefName = "RimTestAcolyte",
                relationshipToken = RoyalTitleThoughtRelationshipTokens.Award,
                tick = now
            };
            RoyalTitleThoughtSnapshot partnerTitleThought = new RoyalTitleThoughtSnapshot
            {
                pawnId = partnerId,
                titleDefName = "RimTestAcolyte",
                relationshipToken = RoyalTitleThoughtRelationshipTokens.Award,
                tick = now
            };
            PawnDiaryRimTestScope.Require(
                RoyalTitleThoughtCorrelation.TryStage(
                    targetTitleThought, new ThoughtSignal(target, null), now, 2500, 128)
                && RoyalTitleThoughtCorrelation.TryStage(
                    prefixTitleThought, new ThoughtSignal(target, null), now, 2500, 128)
                && RoyalTitleThoughtCorrelation.TryStage(
                    partnerTitleThought, new ThoughtSignal(partner, null), now, 2500, 128),
                "Brainwipe fixture could not stage exact Royal title thoughts.");
            RoyalTitleThoughtCorrelation.Claim(
                targetId, "RimTestBaron", "RimTestCount", now, 2500);
            scope.RegisterCleanup(() =>
            {
                RoyalTitleThoughtCorrelation.ForgetPawn(targetId);
                RoyalTitleThoughtCorrelation.ForgetPawn(prefixId);
                RoyalTitleThoughtCorrelation.ForgetPawn(partnerId);
            });

            BeliefPolicySnapshot beliefPolicy = BeliefPolicySnapshot.CreateDefault();
            BeliefMutationSnapshot targetMutation = MutationFixture(targetId, now, 1);
            BeliefMutationSnapshot prefixMutation = MutationFixture(prefixId, now, 3);
            BeliefMutationSnapshot partnerMutation = MutationFixture(partnerId, now, 5);
            BeliefMutationCache.RecordOrMerge(targetMutation, beliefPolicy);
            BeliefMutationCache.RecordOrMerge(prefixMutation, beliefPolicy);
            BeliefMutationCache.RecordOrMerge(partnerMutation, beliefPolicy);
            BeliefHistoryCorrelationCache.Observe(new BeliefHistoryObservation
            {
                tick = now,
                historyEventDefName = "RimTestSharedHistory",
                visiblePawnIds = new List<string> { targetId, partnerId }
            }, beliefPolicy);
            BeliefHistoryCorrelationCache.Observe(new BeliefHistoryObservation
            {
                tick = now,
                historyEventDefName = "RimTestPrefixHistory",
                visiblePawnIds = new List<string> { prefixId }
            }, beliefPolicy);
            scope.RegisterCleanup(() =>
            {
                BeliefMutationCache.ForgetPawn(targetId);
                BeliefMutationCache.ForgetPawn(prefixId);
                BeliefMutationCache.ForgetPawn(partnerId);
                BeliefHistoryCorrelationCache.ForgetPawn(targetId);
                BeliefHistoryCorrelationCache.ForgetPawn(prefixId);
                BeliefHistoryCorrelationCache.ForgetPawn(partnerId);
            });

            DeathrestObservationState targetDeathrest = scope.RequireDiaryRecord(target)
                .EnsureProgressionState().EnsureBiotechState().EnsureDeathrestObservation();
            DeathrestObservationState partnerDeathrest = scope.RequireDiaryRecord(partner)
                .EnsureProgressionState().EnsureBiotechState().EnsureDeathrestObservation();
            targetDeathrest.observationVersion = 0;
            targetDeathrest.severeInterruptionsRecorded = 7;
            targetDeathrest.lastRecordedTick = Math.Max(0, now - 10);
            partnerDeathrest.observationVersion = 0;
            partnerDeathrest.severeInterruptionsRecorded = 5;
            partnerDeathrest.lastRecordedTick = Math.Max(0, now - 20);

            scope.Component.ForgetDiaryHistory(target);

            PawnDiaryRimTestScope.Require(
                !thoughts.Contains(targetThoughtKey)
                    && ReferenceEquals(thoughts[prefixThoughtKey], prefixThought)
                    && ReferenceEquals(thoughts[partnerThoughtKey], partnerThought)
                    && !hediffs.Contains(targetHediffKey)
                    && ReferenceEquals(hediffs[prefixHediffKey], prefixHediff)
                    && ReferenceEquals(hediffs[partnerHediffKey], partnerHediff)
                    && (bool)BaselineThoughtProgressionsField.GetValue(scope.Component)
                        == thoughtGlobalBaseline
                    && (bool)BaselineHediffProgressionsField.GetValue(scope.Component)
                        == hediffGlobalBaseline,
                "Brainwipe changed a global/other-pawn progression baseline or kept its stale state.");
            for (int i = 0; i < removedRecentKeys.Length; i++)
            {
                PawnDiaryRimTestScope.Require(!recent.Contains(removedRecentKeys[i]),
                    "Brainwipe retained target recent-event key '" + removedRecentKeys[i] + "'.");
            }
            for (int i = 0; i < retainedRecentKeys.Length; i++)
            {
                PawnDiaryRimTestScope.Require(recent.Contains(retainedRecentKeys[i]),
                    "Brainwipe removed shared/opaque/prefix key '" + retainedRecentKeys[i] + "'.");
            }
            PawnDiaryRimTestScope.Require(
                !quadrum.Contains(targetQuadrum)
                    && quadrum.Contains(prefixQuadrum)
                    && quadrum.Contains(partnerQuadrum),
                "Brainwipe did not release only the exact target quadrum guard.");
            PawnDiaryRimTestScope.Require(
                !writerCooldowns.Contains(targetWriter)
                    && writerCooldowns.Contains(prefixWriter)
                    && writerCooldowns.Contains(partnerWriter)
                    && !pairCooldowns.Contains(targetPair)
                    && pairCooldowns.Contains(prefixPair)
                    && handledSources.Contains(handled),
                "Brainwipe damaged unrelated/terminal social ownership or retained target pacing.");
            PawnDiaryRimTestScope.Require(
                !AnomalyRecentStudyCache.Matches(targetEntity, targetId, now, 60000)
                    && AnomalyRecentStudyCache.Matches(partnerEntity, partnerId, now, 60000)
                    && AnomalyStudySuppressionCache.TryConsume(new AnomalyStudiedTaleFacts
                    {
                        studierPawnId = targetId,
                        studiedEntityId = targetEntity,
                        studiedDefName = "RimTestEntity",
                        studyJobId = "RimTestJob",
                        tick = now
                    }, 60000),
                "Brainwipe did not separate recent-studier context from consume-once Tale ownership.");
            PawnDiaryRimTestScope.Require(
                !RoyalTitleThoughtCorrelation.HasPendingForPawn(targetId)
                    && RoyalTitleThoughtCorrelation.HasPendingForPawn(prefixId)
                    && RoyalTitleThoughtCorrelation.HasPendingForPawn(partnerId)
                    && RoyalTitleThoughtCorrelation.RecentCountForTests == titleRecentBaseline,
                "Brainwipe retained its Royal title thought owner or removed another pawn's row.");
            PawnDiaryRimTestScope.Require(
                BeliefMutationCache.PeekLatest(targetId, now, beliefPolicy) == null
                    && BeliefMutationCache.PeekLatest(prefixId, now, beliefPolicy) != null
                    && BeliefMutationCache.PeekLatest(partnerId, now, beliefPolicy) != null
                    && BeliefHistoryCorrelationCache.NearbyDefNames(
                        targetId, now, beliefPolicy).Count == 0
                    && BeliefHistoryCorrelationCache.NearbyDefNames(
                        partnerId, now, beliefPolicy).Contains("RimTestSharedHistory")
                    && BeliefHistoryCorrelationCache.NearbyDefNames(
                        prefixId, now, beliefPolicy).Contains("RimTestPrefixHistory"),
                "Brainwipe did not remove exact belief evidence while preserving shared/other POVs.");
            PawnDiaryRimTestScope.Require(
                targetDeathrest.observationVersion
                    == DeathrestInterruptionPolicy.CurrentObservationVersion
                    && targetDeathrest.severeInterruptionsRecorded == 0
                    && targetDeathrest.lastRecordedTick == -1
                    && partnerDeathrest.observationVersion == 0
                    && partnerDeathrest.severeInterruptionsRecorded == 5
                    && partnerDeathrest.lastRecordedTick == Math.Max(0, now - 20),
                "Brainwipe did not restart only the target Deathrest lifetime/cooldown.");
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

            PawnKnowledgeState knowledge = record.EnsureKnowledgeState();
            bool memoryCaptureEnabled = MemoryEffectivePolicyProvider.Current.AllowsCapture(
                MemoryCategoryBits.Personal);
            if (memoryCaptureEnabled)
            {
                PawnDiaryRimTestScope.Require(
                    knowledge.threadRoots.Count == 0
                        && knowledge.standaloneBlocks.Count == 1
                        && knowledge.standaloneBlocks[0].kind
                            == MemoryContractTokens.KindLandmark
                        && knowledge.standaloneBlocks[0].importance
                            == MemoryContractTokens.ImportanceImportant
                        && knowledge.standaloneBlocks[0].requiredLifecycleLandmark
                        && string.IsNullOrEmpty(
                            knowledge.standaloneBlocks[0].rootId),
                    "The first enabled post-Brainwipe memory was not one Important Standalone Landmark.");
            }
            else
            {
                PawnDiaryRimTestScope.Require(
                    knowledge.epochFenceOnly
                        && knowledge.threadRoots.Count == 0
                        && knowledge.standaloneBlocks.Count == 0,
                    "Save-new Off admitted a post-Brainwipe memory instead of retaining only the fence.");
            }

            DiaryEvents.Submit(new BrainwipeArrivalSignal(target));
            PawnDiaryRimTestScope.Require(
                record.eventIds.Count == 1
                    && knowledge.threadRoots.Count == 0
                    && knowledge.standaloneBlocks.Count == (memoryCaptureEnabled ? 1 : 0),
                "Replaying the Brainwipe arrival created a second page, root, or Landmark.");
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

        private static IDictionary RequireDictionary(FieldInfo field)
        {
            IDictionary dictionary = field?.GetValue(scope.Component) as IDictionary;
            if (dictionary == null)
            {
                throw new AssertionException(
                    "Brainwipe fixture could not access dictionary '" + field?.Name + "'.");
            }

            return dictionary;
        }

        private static HashSet<string> RequireStringSet(FieldInfo field)
        {
            HashSet<string> set = field?.GetValue(scope.Component) as HashSet<string>;
            if (set == null)
            {
                throw new AssertionException(
                    "Brainwipe fixture could not access string set '" + field?.Name + "'.");
            }

            return set;
        }

        private static IList RequireList(FieldInfo field)
        {
            IList list = field?.GetValue(scope.Component) as IList;
            if (list == null)
            {
                throw new AssertionException(
                    "Brainwipe fixture could not access list '" + field?.Name + "'.");
            }

            return list;
        }

        private static List<T> RequireTypedList<T>(FieldInfo field)
        {
            List<T> list = field?.GetValue(scope.Component) as List<T>;
            if (list == null)
            {
                throw new AssertionException(
                    "Brainwipe fixture could not access typed list '" + field?.Name + "'.");
            }

            return list;
        }

        private static FamilySupportObservationState FamilySupportRow(
            string adultId,
            int lessonCount)
        {
            return new FamilySupportObservationState
            {
                adultId = adultId,
                lastDisplayName = "Disposable supporter",
                relationToken = BiotechFamilyRoleTokens.Parent,
                lessonCount = lessonCount,
                summarizedLessonCount = Math.Max(0, lessonCount - 1),
                firstObservedTick = 1,
                lastObservedTick = Math.Max(1, Find.TickManager.TicksGame - 1)
            };
        }

        private static OdysseyWriterState OdysseyWriter(string pawnId, string roleToken)
        {
            return new OdysseyWriterState
            {
                pawnId = pawnId,
                displayName = "Disposable journey writer",
                roleToken = roleToken
            };
        }

        private static PendingBiotechBirthState PendingBirth(
            string childId,
            string targetId,
            string partnerId,
            bool includePartner)
        {
            BirthWriterSelection writers = new BirthWriterSelection
            {
                writers = new List<BirthWriterFact>
                {
                    new BirthWriterFact
                    {
                        pawnId = targetId,
                        displayName = "wiped writer",
                        roleToken = BiotechFamilyRoleTokens.Birther
                    }
                }
            };
            BirthEventContextSnapshot context = new BirthEventContextSnapshot
            {
                writers = new List<BirthWriterContextSnapshot>
                {
                    new BirthWriterContextSnapshot
                    {
                        pawnId = targetId,
                        continuity = "target birth continuity"
                    }
                }
            };
            if (includePartner)
            {
                writers.writers.Add(new BirthWriterFact
                {
                    pawnId = partnerId,
                    displayName = "surviving writer",
                    roleToken = BiotechFamilyRoleTokens.Father
                });
                context.writers.Add(new BirthWriterContextSnapshot
                {
                    pawnId = partnerId,
                    continuity = "partner birth continuity"
                });
            }

            return new PendingBiotechBirthState
            {
                snapshot = new BirthMutationSnapshot
                {
                    familyArcId = "biotech-family|" + childId,
                    childId = childId,
                    outcomeToken = BiotechBirthOutcomeTokens.Healthy,
                    methodToken = BiotechBirthMethodTokens.Pregnancy,
                    birthTick = Find.TickManager.TicksGame,
                    correlationId = "birth|biotech-family|" + childId
                },
                writers = writers,
                eventContext = context,
                createdTick = Find.TickManager.TicksGame
            };
        }

        private static BeliefMutationSnapshot MutationFixture(
            string pawnId,
            int tick,
            long sequence)
        {
            return new BeliefMutationSnapshot
            {
                pawnId = pawnId,
                capturedTick = tick,
                beforeIdeologyId = "RimTestIdeology",
                afterIdeologyId = "RimTestIdeology",
                hasBeforeCertainty = true,
                beforeCertainty = 0.5f,
                hasAfterCertainty = true,
                afterCertainty = 0.6f,
                certaintyChanged = true,
                causeTokens = new List<string> { BeliefMutationCauseTokens.CertaintyOffset },
                startedSequence = sequence,
                completedSequence = sequence + 1,
                observedMutation = true
            };
        }

        private static PersonaBondState PersonaFixture(
            string weaponThingId,
            string pawnId,
            string phaseToken)
        {
            return new PersonaBondState
            {
                weaponThingId = weaponThingId,
                weaponDefName = "PawnDiary_RimTest_PersonaWeapon",
                lastDisplayName = "Disposable persona weapon",
                bondEpoch = 4,
                currentPawnId = pawnId,
                currentPawnName = "Disposable owner",
                phaseToken = phaseToken,
                bondStartedTick = 1,
                lastPrimaryObservedTick = 1,
            };
        }

        private static object NewPendingState(string nestedTypeName, params object[] fieldPairs)
        {
            Type stateType = typeof(DiaryGameComponent).GetNestedType(
                nestedTypeName, BindingFlags.NonPublic);
            if (stateType == null || fieldPairs == null || fieldPairs.Length % 2 != 0)
            {
                throw new AssertionException(
                    "Brainwipe fixture could not construct pending state '" + nestedTypeName + "'.");
            }

            object state = Activator.CreateInstance(stateType, true);
            for (int i = 0; i < fieldPairs.Length; i += 2)
            {
                string fieldName = fieldPairs[i] as string;
                FieldInfo field = stateType.GetField(
                    fieldName ?? string.Empty,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null)
                {
                    throw new AssertionException(
                        "Brainwipe fixture could not set pending field '" + fieldName + "'.");
                }

                field.SetValue(state, fieldPairs[i + 1]);
            }

            return state;
        }

        private static IList PendingList(object state, string fieldName)
        {
            FieldInfo field = state?.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            IList list = field?.GetValue(state) as IList;
            if (list == null)
            {
                throw new AssertionException(
                    "Brainwipe fixture could not access pending list '" + fieldName + "'.");
            }

            return list;
        }

        private static object NewDayHediffList()
        {
            Type recordType = typeof(DiaryGameComponent).GetNestedType(
                "DayHediffRecord", BindingFlags.NonPublic);
            if (recordType == null)
            {
                throw new AssertionException(
                    "Brainwipe fixture could not locate DayHediffRecord.");
            }

            return Activator.CreateInstance(typeof(List<>).MakeGenericType(recordType));
        }

        private static void RequireReflectionSurface()
        {
            PawnDiaryRimTestScope.Require(
                ArchiveField != null
                    && DayDigestStatesField != null
                    && PendingDayDigestField != null
                    && PendingInteractionBatchesField != null
                    && PendingAmbientInteractionNotesField != null
                    && PendingAmbientThoughtNotesField != null
                    && PendingTaleBatchesField != null
                    && PendingDayHediffsField != null
                    && WrittenAmbientInteractionNotesField != null
                    && WrittenAmbientThoughtNotesField != null
                    && WrittenDayReflectionsField != null
                    && RejectedAmbientFrequencyKeysField != null
                    && AcceptedAmbientFrequencyKeysField != null
                    && PendingBiotechGrowthField != null
                    && PendingBiotechBirthField != null
                    && PendingRoyalSuccessionsField != null
                    && PersonaBondsField != null
                    && BiotechFamilyArcsField != null
                    && OdysseyActiveJourneyField != null
                    && AnomalyCreepJoinerArcsField != null
                    && ActiveThoughtProgressionsField != null
                    && ActiveHediffProgressionsField != null
                    && RecentEventsField != null
                    && WrittenQuadrumReflectionsField != null
                    && SocialReflectionPairCooldownsField != null
                    && SocialReflectionWriterCooldownsField != null
                    && SocialReflectionHandledSourcesField != null
                    && BaselineThoughtProgressionsField != null
                    && BaselineHediffProgressionsField != null
                    && GlobalFactionSnapshotsField != null,
                "Pawn Diary's Brainwipe-owned stores changed; update the fixture.");
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

// Loaded-game flow and persistence coverage for Quality Wave H7 social reflections.
//
// These tests deliberately never advance RimWorld's clock and never call an LLM. They isolate the
// live component's private H7 stores, drive the real admission/tick/factory boundaries with disposable
// colonists, and restore the player's exact list objects afterward. The Scribe fixture delegates to
// DiaryGameComponent.ExposeSocialReflectionData itself, so save-key or LookMode drift fails here.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable", "persistence & ticking") and
// docs/lore/scribe-saving.md. Reflection is test-only because production intentionally keeps these
// saved collections private.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves H7's loaded scheduler admission, cooldown/reset ownership, frozen retry/deadline behavior,
    /// source-prose qualification, actual Scribe round-trip, subject-memory filter, and writer-only event.
    /// </summary>
    [TestSuite]
    public static class PawnDiarySocialReflectionFlowTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic =
            BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly FieldInfo PendingField =
            typeof(DiaryGameComponent).GetField("pendingSocialReflections", PrivateInstance);
        private static readonly FieldInfo PairCooldownsField =
            typeof(DiaryGameComponent).GetField("socialReflectionPairCooldowns", PrivateInstance);
        private static readonly FieldInfo WriterCooldownsField =
            typeof(DiaryGameComponent).GetField("socialReflectionWriterCooldowns", PrivateInstance);
        private static readonly FieldInfo HandledSourcesField =
            typeof(DiaryGameComponent).GetField("socialReflectionHandledSources", PrivateInstance);
        private static readonly FieldInfo NextSchedulerTickField =
            typeof(DiaryGameComponent).GetField(
                "nextSocialReflectionSchedulerTick",
                PrivateInstance);
        private static readonly MethodInfo ExposeSocialReflectionDataMethod =
            typeof(DiaryGameComponent).GetMethod(
                "ExposeSocialReflectionData",
                PrivateInstance);
        private static readonly MethodInfo TickSocialReflectionsMethod =
            typeof(DiaryGameComponent).GetMethod(
                "TickSocialReflections",
                PrivateInstance);
        private static readonly MethodInfo BuildHotSourceCandidateMethod =
            typeof(DiaryGameComponent).GetMethod(
                "BuildHotSocialReflectionSourceCandidate",
                PrivateStatic);
        private static readonly MethodInfo BuildArchivedSourceCandidateMethod =
            typeof(DiaryGameComponent).GetMethod(
                "BuildArchivedSocialReflectionSourceCandidate",
                PrivateStatic);
        private static readonly Type SourceResolutionType =
            typeof(DiaryGameComponent).GetNestedType(
                "SocialReflectionSourceResolution",
                BindingFlags.NonPublic);
        private static readonly MethodInfo ResolveSourceCandidateMethod =
            SourceResolutionType?.GetMethod("FromCandidate", AnyStatic);
        private static readonly FieldInfo ResolutionStateField =
            SourceResolutionType?.GetField("state", AnyInstance);
        private static readonly FieldInfo ResolutionTitleField =
            SourceResolutionType?.GetField("title", AnyInstance);
        private static readonly FieldInfo ResolutionEndingField =
            SourceResolutionType?.GetField("ending", AnyInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn writer;
        private static Pawn subject;
        private static RawSocialReflectionState originalState;
        private static DiaryGameComponent socialReflectionScribeTarget;

        /// <summary>
        /// Isolates the live H7 stores, guarantees deterministic acceptance, creates two resolvable
        /// diarists, and disables only the generic same-frame dispatcher dedup used by fixture signals.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            RequireProductionWiring();
            scope = PawnDiaryRimTestScope.Begin(
                "heartfelt",
                "insults",
                "smalltalk",
                "socialReflection",
                "dayreflection");
            originalState = RawSocialReflectionState.Capture(scope.Component);
            SetSocialReflectionState(
                scope.Component,
                new List<PendingSocialReflectionState>(),
                new List<SocialReflectionPairCooldownState>(),
                new List<SocialReflectionWriterCooldownState>(),
                new List<SocialReflectionHandledSourceState>(),
                0);

            DiarySocialReflectionDef feature = FeatureDef();
            float[] originalChances = feature.chanceBands
                .Select(row => row == null ? 0f : row.chance)
                .ToArray();
            for (int i = 0; i < feature.chanceBands.Count; i++)
            {
                if (feature.chanceBands[i] != null)
                {
                    feature.chanceBands[i].chance = 1f;
                }
            }
            scope.RegisterCleanup(() =>
            {
                for (int i = 0; i < feature.chanceBands.Count
                    && i < originalChances.Length; i++)
                {
                    if (feature.chanceBands[i] != null)
                    {
                        feature.chanceBands[i].chance = originalChances[i];
                    }
                }
            });

            DiaryTuningDef tuning = DiaryTuning.Current;
            int originalDedupTicks = tuning.genericEventTypeDedupTicks;
            tuning.genericEventTypeDedupTicks = 0;
            scope.RegisterCleanup(() =>
                tuning.genericEventTypeDedupTicks = originalDedupTicks);

            bool originalMemorySetting = PawnDiaryMod.Settings.enableMemorySystem;
            PawnDiaryMod.Settings.enableMemorySystem = true;
            scope.RegisterCleanup(() =>
                PawnDiaryMod.Settings.enableMemorySystem = originalMemorySetting);

            writer = scope.CreateAdultColonist();
            subject = scope.CreateAdultColonist();
            writer.Name = new NameTriple(
                "ReflectionWriterFirst",
                "H7ReflectionWriter",
                "Fixture");
            subject.Name = new NameTriple(
                "ReflectionSubjectFirst",
                "H7SnapshotSubject",
                "Fixture");
            scope.SpawnAsLiveColonist(writer);
            scope.SpawnAsLiveColonist(subject);
        }

        /// <summary>Restores H7 state before the shared scope destroys its disposable pawn identities.</summary>
        [AfterEach]
        public static void TearDown()
        {
            try
            {
                if (scope?.Component != null && originalState != null)
                {
                    originalState.Restore(scope.Component);
                }
            }
            finally
            {
                try
                {
                    scope?.TearDown();
                }
                finally
                {
                    socialReflectionScribeTarget = null;
                    originalState = null;
                    writer = null;
                    subject = null;
                    scope = null;
                }
            }
        }

        /// <summary>
        /// The real interaction dispatcher honors the source toggle, then commits one deterministic
        /// initiator-owned pending row and rejects the same stable source on every repeated callback.
        /// </summary>
        [Test]
        public static void AllowlistEligibilityOwnershipDelayAndOncePerSource()
        {
            InteractionDef deepTalk = RequireDef<InteractionDef>("DeepTalk");
            InteractionDef chitchat = RequireDef<InteractionDef>("Chitchat");
            DiaryInteractionGroupDef heartfelt = InteractionGroups.Classify(deepTalk);
            DiaryInteractionGroupDef smallTalk = InteractionGroups.Classify(chitchat);
            Require(heartfelt != null
                    && heartfelt.defName == "heartfelt"
                    && heartfelt.socialReflectionEligible,
                "DeepTalk did not resolve to the H7-eligible heartfelt group.");
            Require(smallTalk != null
                    && smallTalk.defName == "smalltalk"
                    && !smallTalk.socialReflectionEligible,
                "Chitchat must remain outside the H7 source allowlist.");

            int now = Find.TickManager.TicksGame;
            PawnDiaryMod.Settings.SetGroupEnabled(heartfelt.defName, false);
            DiaryEvents.Submit(new InteractionSignal(
                writer,
                subject,
                deepTalk,
                "disabled source must not schedule",
                "disabled source recipient",
                1900000001));
            Require(PendingRows().Count == 0,
                "A disabled source group still scheduled an H7 row.");

            PawnDiaryMod.Settings.SetGroupEnabled(heartfelt.defName, true);
            string excluded = scope.Component.TryScheduleSocialReflection(
                writer,
                subject,
                chitchat,
                smallTalk,
                "routine chatter",
                1900000002,
                now);
            Require(string.IsNullOrEmpty(excluded) && PendingRows().Count == 0,
                "An excluded smalltalk source scheduled an H7 row.");

            Pawn nonColonist = scope.CreateTrackedPawn(PawnKindDefOf.Colonist, null);
            string oneEligible = scope.Component.TryScheduleSocialReflection(
                writer,
                nonColonist,
                deepTalk,
                heartfelt,
                "only one participant is eligible",
                1900000003,
                now);
            Require(string.IsNullOrEmpty(oneEligible) && PendingRows().Count == 0,
                "H7 accepted a source without two diary-eligible colonists.");

            InteractionSignal acceptedSignal = new InteractionSignal(
                writer,
                subject,
                deepTalk,
                "The writer remembered every word.",
                "The subject answered.",
                1900000004);
            DiaryEvents.Submit(acceptedSignal);

            List<PendingSocialReflectionState> pending = PendingRows();
            Require(pending.Count == 1,
                "The allowlisted two-diarist source did not commit exactly one pending H7 row.");
            PendingSocialReflectionState row = pending[0];
            string writerId = writer.GetUniqueLoadID();
            string subjectId = subject.GetUniqueLoadID();
            string expectedSource = SocialReflectionPolicy.SourceKey(
                "1900000004",
                now,
                deepTalk.defName,
                writerId,
                subjectId);
            SocialReflectionPolicySnapshot policy = DiarySocialReflectionPolicy.Snapshot();
            Require(row.sourceKey == expectedSource
                    && row.writerPawnId == writerId
                    && row.subjectPawnId == subjectId,
                "The pending row lost stable source identity or initiator-only ownership.");
            Require(row.dueTick - row.acceptedTick >= policy.minimumDelayTicks
                    && row.dueTick - row.acceptedTick <= policy.maximumDelayTicks
                    && row.dueTick - row.acceptedTick
                        == SocialReflectionPolicy.DelayTicks(
                            expectedSource,
                            policy.minimumDelayTicks,
                            policy.maximumDelayTicks),
                "The loaded scheduler did not freeze the deterministic 6-24-hour delay.");
            Require(row.nextAttemptTick == row.dueTick
                    && row.retryIntervalTicks == policy.retryIntervalTicks
                    && row.deadlineTick == row.dueTick + policy.proseGraceTicks,
                "The accepted row did not freeze due/retry/deadline timing.");
            Require(PairCooldownRows().Count == 1
                    && WriterCooldownRows().Count == 1
                    && PairCooldownRows()[0].cooldownUntilTick
                        == row.acceptedTick + policy.pairCooldownTicks
                    && WriterCooldownRows()[0].cooldownUntilTick
                        == row.acceptedTick + policy.writerCooldownTicks,
                "H7 did not reserve the 15-day pair and 3-day writer cooldowns at acceptance.");
            Require(HandledRows().Count == 1
                    && HandledRows()[0].sourceKey == expectedSource,
                "H7 did not durably claim the accepted source before delayed emission.");

            string duplicate = scope.Component.TryScheduleSocialReflection(
                writer,
                subject,
                deepTalk,
                heartfelt,
                "replayed callback",
                1900000004,
                now);
            Require(string.IsNullOrEmpty(duplicate)
                    && PendingRows().Count == 1
                    && HandledRows().Count == 1,
                "A repeated callback rerolled or duplicated an already-owned source.");

            // Pin the post-pacing matrix without depending on whether this loaded colony's B6 policy
            // happened to register the ordinary source page.
            row.sourceProseUnavailable = false;
            acceptedSignal.OnAcceptedEmissionCompleted(
                scope.Component,
                CaptureDecision.RouteBatch,
                false);
            Require(!row.sourceProseUnavailable,
                "A pair batch was marked facts-only before its future batch prose could exist.");
            acceptedSignal.OnAcceptedEmissionCompleted(
                scope.Component,
                CaptureDecision.RouteAmbient,
                false);
            Require(row.sourceProseUnavailable,
                "An ambient source did not become facts-only for its normal due tick.");
            row.sourceProseUnavailable = false;
            acceptedSignal.OnAcceptedEmissionCompleted(
                scope.Component,
                CaptureDecision.GeneratePair,
                true);
            Require(!row.sourceProseUnavailable,
                "A registered direct source page was incorrectly marked facts-only.");
            acceptedSignal.OnAcceptedEmissionCompleted(
                scope.Component,
                CaptureDecision.GeneratePair,
                false);
            Require(row.sourceProseUnavailable,
                "A B6-folded direct source did not become facts-only at the scheduler boundary.");
        }

        /// <summary>
        /// Drives a real direct interaction through DispatchCore after both disposable diaries reach
        /// B6's daily allowance. The folded source must trigger the post-emission callback, and its H7
        /// row must register at ordinary due even though the missing-prose grace deadline is still future.
        /// </summary>
        [Test]
        public static void DispatchFoldMarksFactsOnlyAndEmitsAtOrdinaryDue()
        {
            InteractionDef deepTalk = RequireDef<InteractionDef>("DeepTalk");
            DiaryInteractionGroupDef heartfelt = InteractionGroups.Classify(deepTalk);
            Require(heartfelt != null && heartfelt.socialReflectionEligible,
                "DeepTalk did not resolve to an H7-eligible group.");

            bool originalImportant = heartfelt.important;
            bool originalCombat = heartfelt.combat;
            DiaryTuningDef tuning = DiaryTuning.Current;
            int originalCap = tuning.lowSalienceDailySoftCap;
            bool originalDaySummary = tuning.daySummaryEnabled;
            heartfelt.important = false;
            heartfelt.combat = false;
            tuning.lowSalienceDailySoftCap = 1;
            tuning.daySummaryEnabled = true;
            scope.RegisterCleanup(() =>
            {
                heartfelt.important = originalImportant;
                heartfelt.combat = originalCombat;
                tuning.lowSalienceDailySoftCap = originalCap;
                tuning.daySummaryEnabled = originalDaySummary;
            });

            string writerId = writer.GetUniqueLoadID();
            string subjectId = subject.GetUniqueLoadID();
            int day = Find.TickManager.TicksAbs / GenDate.TicksPerDay;
            RegisterDayDigestCleanup(writerId, subjectId);
            scope.Component.RecordLowSalienceEmission(writerId, day);
            scope.Component.RecordLowSalienceEmission(subjectId, day);

            int now = Find.TickManager.TicksGame;
            InteractionSignal foldedSignal = new InteractionSignal(
                writer,
                subject,
                deepTalk,
                "PDTEST_H7_DISPATCH_FOLDED_SOURCE",
                "PDTEST_H7_DISPATCH_FOLDED_RECIPIENT",
                1900000005);
            InteractionEventData payload = foldedSignal.Payload as InteractionEventData;
            Require(payload != null && !payload.RouteToBatch && !payload.RouteToAmbient,
                "The dispatch fixture did not capture a direct pair decision before B6 pacing.");

            // No canonical source page should register: B6 owns the accepted interaction as digest
            // evidence. OnAcceptedEmissionCompleted must still run and freeze H7 as facts-only.
            scope.RequireNoNewEvent(() => DiaryEvents.Submit(foldedSignal));
            Require(PendingRows().Count == 1 && PendingRows()[0].sourceProseUnavailable,
                "DispatchCore did not mark the B6-folded accepted source facts-only.");

            PendingSocialReflectionState row = PendingRows()[0];
            SocialReflectionPolicySnapshot policy = DiarySocialReflectionPolicy.Snapshot();
            row.dueTick = now;
            row.nextAttemptTick = now;
            row.deadlineTick = now + Math.Max(1, policy.proseGraceTicks);
            NextSchedulerTickField.SetValue(scope.Component, 0);

            DiaryEvent reflection = scope.FireAndRequireEvent(
                () => InvokeTick(now),
                SocialReflectionEventData.DefNameToken,
                writer,
                null);
            scope.RequireSoloRef(reflection, writer);
            Require(now < row.deadlineTick
                    && reflection.tick == now
                    && PendingRows().Count == 0
                    && HandledRows().Count == 1
                    && HandledRows()[0].reflectionEventId == reflection.eventId,
                "The facts-only H7 row waited for its future prose deadline instead of registering "
                    + "at the ordinary due tick.");
        }

        /// <summary>
        /// A complete named-relation change clears only pair pacing; the same writer remains blocked,
        /// while a reverse-POV source may replace the stale pending owner.
        /// </summary>
        [Test]
        public static void RelationChangeClearsOnlyPairThenAllowsReverseReplacement()
        {
            InteractionDef deepTalk = RequireDef<InteractionDef>("DeepTalk");
            DiaryInteractionGroupDef heartfelt = InteractionGroups.Classify(deepTalk);
            int now = Find.TickManager.TicksGame;
            string first = scope.Component.TryScheduleSocialReflection(
                writer,
                subject,
                deepTalk,
                heartfelt,
                "before the relation changed",
                1900000101,
                now);
            Require(!string.IsNullOrWhiteSpace(first)
                    && PendingRows().Count == 1
                    && PairCooldownRows().Count == 1
                    && WriterCooldownRows().Count == 1,
                "Could not establish the initial H7 reservation for the relation-reset fixture.");

            writer.relations.AddDirectRelation(PawnRelationDefOf.Lover, subject);
            scope.RegisterCleanup(() =>
            {
                if (writer?.relations != null && subject != null
                    && writer.relations.DirectRelationExists(
                        PawnRelationDefOf.Lover,
                        subject))
                {
                    writer.relations.TryRemoveDirectRelation(
                        PawnRelationDefOf.Lover,
                        subject);
                }
            });

            string sameWriter = scope.Component.TryScheduleSocialReflection(
                writer,
                subject,
                deepTalk,
                heartfelt,
                "same writer after relation change",
                1900000102,
                now);
            Require(string.IsNullOrEmpty(sameWriter),
                "A named-relation change incorrectly cleared the writer cooldown.");
            Require(PendingRows().Count == 1
                    && PendingRows()[0].sourceKey == first
                    && PairCooldownRows().Count == 0
                    && WriterCooldownRows().Count == 1
                    && WriterCooldownRows()[0].writerPawnId == writer.GetUniqueLoadID(),
                "The relation change must clear only pair pacing while retaining writer pacing.");

            string reverse = scope.Component.TryScheduleSocialReflection(
                subject,
                writer,
                deepTalk,
                heartfelt,
                "the other pawn now has the later perspective",
                1900000103,
                now);
            Require(!string.IsNullOrWhiteSpace(reverse)
                    && PendingRows().Count == 1
                    && PendingRows()[0].sourceKey == reverse
                    && PendingRows()[0].writerPawnId == subject.GetUniqueLoadID()
                    && PendingRows()[0].subjectPawnId == writer.GetUniqueLoadID(),
                "A changed relation did not allow the reviewed reverse-POV pending replacement.");
            Require(WriterCooldownRows().Count == 1
                    && WriterCooldownRows()[0].writerPawnId == subject.GetUniqueLoadID()
                    && PairCooldownRows().Count == 1
                    && PairCooldownRows()[0].reservationSourceKey == reverse,
                "Reverse replacement retained stale reservation ownership.");
        }

        /// <summary>
        /// Later source-group changes leave an accepted schedule alone, while the independent H7 switch
        /// cancels immediately, releases both reservations, and still keeps once-per-source ownership.
        /// </summary>
        [Test]
        public static void IndependentToggleCancelsAndReleasesWithoutRerollingSource()
        {
            InteractionDef deepTalk = RequireDef<InteractionDef>("DeepTalk");
            DiaryInteractionGroupDef heartfelt = InteractionGroups.Classify(deepTalk);
            int now = Find.TickManager.TicksGame;
            string source = scope.Component.TryScheduleSocialReflection(
                writer,
                subject,
                deepTalk,
                heartfelt,
                "accepted before settings changed",
                1900000201,
                now);
            Require(!string.IsNullOrWhiteSpace(source),
                "Could not establish the H7 row for toggle cancellation.");

            PawnDiaryMod.Settings.SetGroupEnabled(heartfelt.defName, false);
            InvokeTick(now);
            Require(PendingRows().Count == 1
                    && PairCooldownRows().Count == 1
                    && WriterCooldownRows().Count == 1,
                "Changing the original source-group setting revoked an accepted H7 schedule.");

            PawnDiaryMod.Settings.SetGroupEnabled("socialReflection", false);
            InvokeTick(now);
            Require(PendingRows().Count == 0
                    && PairCooldownRows().Count == 0
                    && WriterCooldownRows().Count == 0,
                "Disabling H7 did not cancel pending rows and release both reservations immediately.");
            Require(HandledRows().Count == 1 && HandledRows()[0].sourceKey == source,
                "Pre-registration cancellation lost once-per-source ownership.");

            PawnDiaryMod.Settings.SetGroupEnabled("socialReflection", true);
            PawnDiaryMod.Settings.SetGroupEnabled(heartfelt.defName, true);
            string replay = scope.Component.TryScheduleSocialReflection(
                writer,
                subject,
                deepTalk,
                heartfelt,
                "the same source returned after re-enable",
                1900000201,
                now);
            Require(string.IsNullOrEmpty(replay) && PendingRows().Count == 0,
                "Re-enabling H7 rerolled a previously accepted/cancelled source.");
        }

        /// <summary>
        /// Missing source prose retries on the row's saved hourly cadence, falls back exactly at the
        /// deadline, registers one current-tick writer-only page, and freezes live subject/source facts.
        /// </summary>
        [Test]
        public static void RetryDeadlineEmitsLaterFrozenWriterOnlyPage()
        {
            AddSocialMemoryReason(writer, subject);
            int now = Find.TickManager.TicksGame;
            int sourceTick = Math.Max(0, now - 1);
            int dueTick = now + 10000;
            int deadlineTick = dueTick + 5000;
            int savedRetryInterval = 2500;
            string writerId = writer.GetUniqueLoadID();
            string subjectId = subject.GetUniqueLoadID();
            string pairKey = SocialReflectionPolicy.PairKey(writerId, subjectId);
            string sourceKey = SocialReflectionPolicy.SourceKey(
                "1900000301",
                sourceTick,
                "DeepTalk",
                writerId,
                subjectId);
            PendingSocialReflectionState row = new PendingSocialReflectionState
            {
                sourceKey = sourceKey,
                playLogEntryId = 1900000301,
                interactionTick = sourceTick,
                interactionDefName = "DeepTalk",
                interactionLabel = "deep talk",
                initiatorText = "PDTEST_H7_FROZEN_SOURCE_TEXT",
                writerPawnId = writerId,
                subjectPawnId = subjectId,
                pairKey = pairKey,
                relationSignature = string.Empty,
                acceptedTick = sourceTick,
                dueTick = dueTick,
                nextAttemptTick = dueTick,
                retryIntervalTicks = savedRetryInterval,
                deadlineTick = deadlineTick
            };
            PendingRows().Add(row);
            PairCooldownRows().Add(new SocialReflectionPairCooldownState
            {
                pairKey = pairKey,
                relationSignature = string.Empty,
                cooldownUntilTick = now + 1000,
                reservationSourceKey = sourceKey
            });
            WriterCooldownRows().Add(new SocialReflectionWriterCooldownState
            {
                writerPawnId = writerId,
                cooldownUntilTick = now + 1000,
                reservationSourceKey = sourceKey
            });
            HandledRows().Add(new SocialReflectionHandledSourceState
            {
                sourceKey = sourceKey,
                handledTick = sourceTick
            });
            NextSchedulerTickField.SetValue(scope.Component, 0);

            InvokeTick(dueTick);
            Require(PendingRows().Count == 1
                    && row.nextAttemptTick == dueTick + savedRetryInterval,
                "The first missing-prose attempt did not advance by the row's frozen retry interval.");

            DiarySocialReflectionDef feature = FeatureDef();
            int originalRetry = feature.retryIntervalTicks;
            feature.retryIntervalTicks = 1;
            scope.RegisterCleanup(() => feature.retryIntervalTicks = originalRetry);
            InvokeTick(row.nextAttemptTick);
            Require(row.nextAttemptTick == deadlineTick,
                "An XML edit after acceptance changed the saved row's retry cadence or deadline.");

            // The two cadence probes above use synthetic future ticks without mutating RimWorld's clock.
            // Reset the same saved row to the real current tick before asking the dispatcher to register
            // the page, so production never receives an event dated in the fixture's artificial future.
            row.dueTick = now;
            row.nextAttemptTick = now;
            row.deadlineTick = now;
            NextSchedulerTickField.SetValue(scope.Component, 0);
            string frozenSubjectName = subject.LabelShort;
            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => InvokeTick(now),
                SocialReflectionEventData.DefNameToken,
                writer,
                null);
            scope.RequireSoloRef(diaryEvent, writer);
            Require(!scope.RequireDiaryRecord(subject).eventIds.Contains(diaryEvent.eventId),
                "The H7 subject received a forbidden diary cross-reference.");
            Require(diaryEvent.tick == now
                    && (now == 0 || diaryEvent.tick > sourceTick),
                "The delayed H7 page was not dated at the later current tick.");
            Require(DiaryContextFields.Value(
                        diaryEvent.gameContext,
                        SocialReflectionEventData.SourceTickContextKey)
                    == sourceTick.ToString()
                    && DiaryContextFields.Value(
                        diaryEvent.gameContext,
                        SocialReflectionEventData.SourcePlayLogIdContextKey)
                    == "1900000301"
                    && DiaryContextFields.Value(
                        diaryEvent.gameContext,
                        SocialReflectionEventData.SourceInteractionDefContextKey)
                    == "DeepTalk",
                "The delayed page did not retain its hidden original source identity/tick.");
            Require(DiaryContextFields.Value(
                        diaryEvent.gameContext,
                        "social_reflection_subject_name")
                    == frozenSubjectName
                    && DiaryContextFields.Value(
                        diaryEvent.gameContext,
                        "social_reflection_subject_age")
                    == "30"
                    && !string.IsNullOrWhiteSpace(DiaryContextFields.Value(
                        diaryEvent.gameContext,
                        "social_reflection_subject_life_stage"))
                    && !string.IsNullOrWhiteSpace(DiaryContextFields.Value(
                        diaryEvent.gameContext,
                        "social_reflection_subject_appearance"))
                    && !string.IsNullOrWhiteSpace(DiaryContextFields.Value(
                        diaryEvent.gameContext,
                        "social_reflection_sentiment"))
                    && !string.IsNullOrWhiteSpace(DiaryContextFields.Value(
                        diaryEvent.gameContext,
                        "social_reflection_reasons")),
                "The emission boundary did not capture the reviewed qualitative live subject snapshot.");
            string sourceFacts = DiaryContextFields.Value(
                diaryEvent.gameContext,
                "social_reflection_source_facts");
            Require(sourceFacts.IndexOf("PDTEST_H7_FROZEN_SOURCE_TEXT", StringComparison.Ordinal) >= 0
                    && sourceFacts.IndexOf("deep talk", StringComparison.OrdinalIgnoreCase) >= 0
                    && string.IsNullOrEmpty(DiaryContextFields.Value(
                        diaryEvent.gameContext,
                        "social_reflection_source_title"))
                    && string.IsNullOrEmpty(DiaryContextFields.Value(
                        diaryEvent.gameContext,
                        "social_reflection_source_ending")),
                "Deadline fallback did not preserve both source facts or incorrectly invented prose.");
            Require(diaryEvent.gameContext.IndexOf("opinion=", StringComparison.OrdinalIgnoreCase) < 0
                    && diaryEvent.gameContext.IndexOf("beauty=", StringComparison.OrdinalIgnoreCase) < 0,
                "The saved H7 snapshot leaked raw opinion or beauty numbers.");
            Require(PendingRows().Count == 0
                    && PairCooldownRows().Count == 1
                    && WriterCooldownRows().Count == 1,
                "Post-registration completion released cooldowns that must remain reserved.");
            Require(HandledRows().Count == 1
                    && HandledRows()[0].sourceKey == sourceKey
                    && HandledRows()[0].reflectionEventId == diaryEvent.eventId,
                "Registered H7 completion did not retain its durable source-to-page owner.");

            string frozenContext = diaryEvent.gameContext;
            subject.Name = new NameTriple(
                "ChangedSubjectFirst",
                "PDTEST_H7_LATE_SUBJECT_NAME",
                "Fixture");
            DiaryPromptRequest request = DiaryPipelineAdapters.BuildPromptRequest(
                diaryEvent,
                DiaryEvent.InitiatorRole,
                personaRule: string.Empty,
                psychotypeRule: string.Empty,
                promptEnchantment: string.Empty,
                humorCue: string.Empty,
                priorInitiatorEntry: null,
                entryText: null,
                titleRequest: false,
                maxTokens: 0,
                contextDetailLevel: PromptContextDetailLevel.Full);
            DiaryPromptPlan plan = DiaryPromptPlanner.Build(request);
            Require(diaryEvent.gameContext == frozenContext
                    && plan.userPrompt.IndexOf(
                        frozenSubjectName,
                        StringComparison.Ordinal) >= 0
                    && plan.userPrompt.IndexOf(
                        "PDTEST_H7_LATE_SUBJECT_NAME",
                        StringComparison.Ordinal) < 0,
                "Regeneration reread the subject's later live state instead of the frozen snapshot.");
            Require(request.payload.display != null
                    && request.payload.display.colorCue == diaryEvent.colorCue,
                "Regeneration did not preserve the saved polarity cue from the delayed event.");
        }

        /// <summary>
        /// The dedicated H7 retrieval path requires exact subject overlap, caps output at two lines,
        /// excludes the canonical source event, and never deposits the reflection as new knowledge.
        /// </summary>
        [Test]
        public static void SubjectMemoryIsExactCappedAndReflectionNeverDeposits()
        {
            Pawn unrelated = scope.CreateAdultColonist();
            PawnKnowledgeState knowledge =
                scope.RequireDiaryRecord(writer).EnsureKnowledgeState();
            int now = Find.TickManager.TicksGame;
            string sourceEventId = "PDTEST_H7_EXCLUDED_SOURCE_EVENT";
            AddMemoryRecord(
                knowledge,
                "subject-one",
                "PDTEST_H7_SUBJECT_MEMORY_ONE",
                subject,
                "source-one",
                now - 500);
            AddMemoryRecord(
                knowledge,
                "subject-two",
                "PDTEST_H7_SUBJECT_MEMORY_TWO",
                subject,
                "source-two",
                now - 400);
            AddMemoryRecord(
                knowledge,
                "subject-three",
                "PDTEST_H7_SUBJECT_MEMORY_THREE",
                subject,
                "source-three",
                now - 300);
            AddMemoryRecord(
                knowledge,
                "excluded-source",
                "PDTEST_H7_EXCLUDED_SOURCE_MEMORY",
                subject,
                sourceEventId,
                now - 200);
            AddMemoryRecord(
                knowledge,
                "unrelated",
                "PDTEST_H7_UNRELATED_MEMORY",
                unrelated,
                "source-unrelated",
                now - 100);
            int recordsBefore = knowledge.records.Count;

            SocialReflectionPolicySnapshot policy = DiarySocialReflectionPolicy.Snapshot();
            SocialReflectionFormattedContext formatted =
                SocialReflectionContextFormatter.Format(
                    new SocialReflectionSubjectFacts
                    {
                        displayName = subject.LabelShort,
                        biologicalAgeYears = 30,
                        lifeStageLabel = "adult",
                        beauty = 0f,
                        primaryRelationLabel = string.Empty,
                        opinion = 0,
                        opinionBandLabel = "neutral"
                    },
                    policy);
            string gameContext = SocialReflectionEventData.BuildGameContext(
                "They spoke beside the fields.",
                string.Empty,
                string.Empty,
                formatted,
                subject.GetUniqueLoadID(),
                sourceEventId,
                "PDTEST_H7_MEMORY_SOURCE_KEY",
                now - 1000,
                1900000401,
                "DeepTalk");
            DiaryEvent diaryEvent = scope.Component.AddSocialReflectionEvent(
                writer,
                subject,
                SocialReflectionEventData.DefNameToken,
                "reflection",
                "The writer reflected.",
                "Stay with known facts.",
                gameContext,
                DiaryEvent.QuietColorCue);

            string memory = diaryEvent.MemoryContextForRole(DiaryEvent.InitiatorRole);
            Require(!string.IsNullOrWhiteSpace(memory) && memory.Split('\n').Length <= 2,
                "H7 did not inject a non-empty subject memory block capped at two lines.");
            Require(memory.IndexOf(
                        "PDTEST_H7_EXCLUDED_SOURCE_MEMORY",
                        StringComparison.Ordinal) < 0
                    && memory.IndexOf(
                        "PDTEST_H7_UNRELATED_MEMORY",
                        StringComparison.Ordinal) < 0,
                "H7 memory included its source event or a record about another pawn.");
            Require(knowledge.records.Count == recordsBefore,
                "Registering H7 deposited reflection knowledge and enabled recursive evidence.");
        }

        /// <summary>
        /// Same-pair completed hot/archive pages qualify for an ending excerpt, while solo ambient,
        /// wrong-subject, H7, and stale archive rows fail closed.
        /// </summary>
        [Test]
        public static void PairAndArchiveProseQualifyWhileAmbientAndReflectionSourcesDoNot()
        {
            string writerId = writer.GetUniqueLoadID();
            string subjectId = subject.GetUniqueLoadID();
            PendingSocialReflectionState row = new PendingSocialReflectionState
            {
                sourceKey = "PDTEST_H7_PROSE_SOURCE",
                playLogEntryId = 1900000501,
                writerPawnId = writerId,
                subjectPawnId = subjectId,
                interactionDefName = "Insult"
            };

            DiaryEvent pairBatch = CompletedPairSource(
                "PDTEST_H7_PAIR_BATCH",
                "InsultBatch",
                writerId,
                subjectId,
                row.playLogEntryId);
            object hotCandidate = BuildHotSourceCandidateMethod.Invoke(
                null,
                new object[] { pairBatch, row });
            Require(hotCandidate != null,
                "A completed same-pair batch was rejected as H7 source prose.");
            object hotResolution = ResolveSourceCandidateMethod.Invoke(
                null,
                new[] { hotCandidate, DiarySocialReflectionPolicy.Snapshot() });
            Require(ResolutionState(hotResolution) == "Ready"
                    && (string)ResolutionTitleField.GetValue(hotResolution)
                        == "PDTEST_H7_SOURCE_TITLE"
                    && (string)ResolutionEndingField.GetValue(hotResolution)
                        == "Second sentence. Third sentence.",
                "The completed pair source did not yield its title and final two sentences.");

            DiaryEvent ambient = CompletedPairSource(
                "PDTEST_H7_AMBIENT",
                "SmallTalkAmbientDay",
                writerId,
                subjectId,
                row.playLogEntryId);
            ambient.solo = true;
            Require(BuildHotSourceCandidateMethod.Invoke(
                    null,
                    new object[] { ambient, row }) == null,
                "An ambient solo note was accepted as authoritative same-pair source prose.");

            DiaryEvent wrongSubject = CompletedPairSource(
                "PDTEST_H7_WRONG_SUBJECT",
                "InsultBatch",
                writerId,
                "PDTEST_H7_OTHER_SUBJECT",
                row.playLogEntryId);
            Require(BuildHotSourceCandidateMethod.Invoke(
                    null,
                    new object[] { wrongSubject, row }) == null,
                "A different-subject pair page was accepted as H7 source prose.");

            DiaryEvent reflection = CompletedPairSource(
                "PDTEST_H7_REFLECTION_SOURCE",
                SocialReflectionEventData.DefNameToken,
                writerId,
                subjectId,
                row.playLogEntryId);
            Require(BuildHotSourceCandidateMethod.Invoke(
                    null,
                    new object[] { reflection, row }) == null,
                "A social reflection was accepted as source for another social reflection.");

            ArchivedDiaryEntry archive = new ArchivedDiaryEntry
            {
                eventId = "PDTEST_H7_ARCHIVE_SOURCE",
                pawnId = writerId,
                povRole = DiaryEvent.InitiatorRole,
                interactionDefName = "InsultBatch",
                linkedPawnId = subjectId,
                playLogEntryIds = new List<int> { row.playLogEntryId },
                status = DiaryEvent.CompleteStatus,
                title = "PDTEST_H7_ARCHIVE_TITLE",
                generatedText = "Archive first. Archive second. Archive third."
            };
            object archiveCandidate = BuildArchivedSourceCandidateMethod.Invoke(
                null,
                new object[] { archive, row });
            Require(archiveCandidate != null,
                "A completed exact archived same-pair POV was rejected as source prose.");
            archive.archivedGenerationStale = true;
            object staleCandidate = BuildArchivedSourceCandidateMethod.Invoke(
                null,
                new object[] { archive, row });
            object staleResolution = ResolveSourceCandidateMethod.Invoke(
                null,
                new[] { staleCandidate, DiarySocialReflectionPolicy.Snapshot() });
            Require(ResolutionState(staleResolution) == "FactsOnly",
                "A stale archived generation was treated as authoritative source prose.");
            archive.archivedGenerationStale = false;
            archive.linkedPawnId = "PDTEST_H7_OTHER_SUBJECT";
            Require(BuildArchivedSourceCandidateMethod.Invoke(
                    null,
                    new object[] { archive, row }) == null,
                "An archived POV linked to another subject was accepted as H7 source prose.");
            archive.linkedPawnId = subjectId;
            archive.interactionDefName = SocialReflectionEventData.DefNameToken;
            Require(BuildArchivedSourceCandidateMethod.Invoke(
                    null,
                    new object[] { archive, row }) == null,
                "An archived H7 page was accepted as recursive reflection evidence.");
        }

        /// <summary>
        /// The actual component method deep-round-trips all four stores and frozen pending fields; a
        /// pre-H7 root with no keys loads to safe non-null empties.
        /// </summary>
        [Test]
        public static void ActualComponentScribeRoundTripsAndNormalizesOldSaves()
        {
            int now = Find.TickManager.TicksGame;
            string writerId = writer.GetUniqueLoadID();
            string subjectId = subject.GetUniqueLoadID();
            string pairKey = SocialReflectionPolicy.PairKey(writerId, subjectId);
            string sourceKey = SocialReflectionPolicy.SourceKey(
                "1900000601",
                now,
                "DeepTalk",
                writerId,
                subjectId);
            PendingSocialReflectionState savedPending = new PendingSocialReflectionState
            {
                sourceKey = sourceKey,
                sourceEventId = "PDTEST_H7_SAVED_SOURCE_EVENT",
                playLogEntryId = 1900000601,
                interactionTick = now,
                interactionDefName = "DeepTalk",
                interactionLabel = " deep talk ",
                initiatorText = " saved source text ",
                writerPawnId = writerId,
                subjectPawnId = subjectId,
                pairKey = "corrupt-pair-key-is-repaired",
                relationSignature = " relation-signature ",
                sourceProseUnavailable = true,
                acceptedTick = now,
                dueTick = now + 15000,
                // Deliberately corrupt: normalization must wake at the frozen deadline rather than
                // leaving this row stranded beyond its promised prose-grace window.
                nextAttemptTick = now + 90000,
                retryIntervalTicks = 3777,
                deadlineTick = now + 75000
            };
            string discardedSourceKey = "PDTEST_H7_DISCARDED_PENDING_SOURCE";
            PendingSocialReflectionState discardedPending =
                new PendingSocialReflectionState
                {
                    sourceKey = discardedSourceKey,
                    playLogEntryId = -1,
                    interactionTick = now,
                    interactionDefName = "DeepTalk",
                    writerPawnId = writerId,
                    subjectPawnId = subjectId,
                    pairKey = pairKey,
                    acceptedTick = now,
                    dueTick = now + 1,
                    nextAttemptTick = now + 1,
                    retryIntervalTicks = 2500,
                    deadlineTick = now + 2501
                };
            SocialReflectionPairCooldownState savedPair =
                new SocialReflectionPairCooldownState
                {
                    pairKey = pairKey,
                    relationSignature = " relation-signature ",
                    cooldownUntilTick = now + 900000,
                    reservationSourceKey = sourceKey
                };
            SocialReflectionPairCooldownState discardedPair =
                new SocialReflectionPairCooldownState
                {
                    pairKey = pairKey,
                    cooldownUntilTick = now + 999999,
                    reservationSourceKey = discardedSourceKey
                };
            SocialReflectionWriterCooldownState savedWriter =
                new SocialReflectionWriterCooldownState
                {
                    writerPawnId = writerId,
                    cooldownUntilTick = now + 180000,
                    reservationSourceKey = sourceKey
                };
            SocialReflectionWriterCooldownState discardedWriter =
                new SocialReflectionWriterCooldownState
                {
                    writerPawnId = writerId,
                    cooldownUntilTick = now + 999999,
                    reservationSourceKey = discardedSourceKey
                };
            SocialReflectionHandledSourceState savedHandled =
                new SocialReflectionHandledSourceState
                {
                    sourceKey = sourceKey,
                    reflectionEventId = "PDTEST_H7_SAVED_REFLECTION_EVENT",
                    handledTick = now
                };
            SetSocialReflectionState(
                scope.Component,
                new List<PendingSocialReflectionState>
                {
                    savedPending,
                    discardedPending
                },
                new List<SocialReflectionPairCooldownState>
                {
                    savedPair,
                    discardedPair
                },
                new List<SocialReflectionWriterCooldownState>
                {
                    savedWriter,
                    discardedWriter
                },
                new List<SocialReflectionHandledSourceState> { savedHandled },
                987654321);
            socialReflectionScribeTarget = scope.Component;

            ScribeRoundTripWithAfterSave(
                new ActualSocialReflectionStateAdapter(),
                () => SetSocialReflectionState(
                    scope.Component,
                    null,
                    null,
                    null,
                    null,
                    -1));

            Require(PendingRows().Count == 1
                    && PairCooldownRows().Count == 1
                    && WriterCooldownRows().Count == 1
                    && HandledRows().Count == 1,
                "The actual component lost one or more H7 saved collections.");
            PendingSocialReflectionState loaded = PendingRows()[0];
            Require(!ReferenceEquals(loaded, savedPending)
                    && loaded.sourceKey == sourceKey
                    && loaded.sourceEventId == "PDTEST_H7_SAVED_SOURCE_EVENT"
                    && loaded.pairKey == pairKey
                    && loaded.interactionLabel == "deep talk"
                    && loaded.initiatorText == "saved source text"
                    && loaded.sourceProseUnavailable
                    && loaded.dueTick == now + 15000
                    && loaded.nextAttemptTick == loaded.deadlineTick
                    && loaded.nextAttemptTick == now + 75000
                    && loaded.retryIntervalTicks == 3777
                    && loaded.deadlineTick == now + 75000,
                "The pending H7 row did not deep-load, clamp its corrupt wake to the frozen deadline, "
                    + "and normalize its complete schedule/facts.");
            Require(PairCooldownRows()[0].cooldownUntilTick == now + 900000
                    && PairCooldownRows()[0].reservationSourceKey == sourceKey
                    && WriterCooldownRows()[0].cooldownUntilTick == now + 180000
                    && WriterCooldownRows()[0].reservationSourceKey == sourceKey
                    && HandledRows()[0].reflectionEventId
                        == "PDTEST_H7_SAVED_REFLECTION_EVENT"
                    && (int)NextSchedulerTickField.GetValue(scope.Component) == 0,
                "Normalization failed to release a discarded row's reservations while preserving "
                    + "the retained pending owner's cooldowns, or the transient wake-up reset drifted.");

            string duplicateAfterLoad = scope.Component.TryScheduleSocialReflection(
                writer,
                subject,
                RequireDef<InteractionDef>("DeepTalk"),
                InteractionGroups.ByKey("heartfelt"),
                "save/load must not reroll",
                1900000601,
                now);
            Require(string.IsNullOrEmpty(duplicateAfterLoad) && PendingRows().Count == 1,
                "Save/load allowed the accepted stable source to reroll.");

            SetSocialReflectionState(scope.Component, null, null, null, null, -1);
            ScribeRoundTripWithAfterSave(
                new LegacyMissingSocialReflectionStateAdapter(),
                () => SetSocialReflectionState(
                    scope.Component,
                    null,
                    null,
                    null,
                    null,
                    -1));
            Require(PendingRows() != null
                    && PairCooldownRows() != null
                    && WriterCooldownRows() != null
                    && HandledRows() != null
                    && PendingRows().Count == 0
                    && PairCooldownRows().Count == 0
                    && WriterCooldownRows().Count == 0
                    && HandledRows().Count == 0,
                "A pre-H7 save with missing keys did not normalize to safe empty stores.");
        }

        // ---- fixture builders and actual-component Scribe adapter ---------------------------------

        private static DiaryEvent CompletedPairSource(
            string eventId,
            string defName,
            string writerId,
            string subjectId,
            int playLogEntryId)
        {
            DiaryEvent diaryEvent = new DiaryEvent
            {
                eventId = eventId,
                interactionDefName = defName,
                initiatorPawnId = writerId,
                recipientPawnId = subjectId,
                solo = false,
                initiatorStatus = DiaryEvent.CompleteStatus,
                initiatorTitle = "PDTEST_H7_SOURCE_TITLE",
                initiatorGeneratedText =
                    "First sentence. Second sentence. Third sentence."
            };
            diaryEvent.AddPlayLogEntryId(playLogEntryId);
            return diaryEvent;
        }

        private static void AddSocialMemoryReason(Pawn pawn, Pawn otherPawn)
        {
            ThoughtDef slighted = DefDatabase<ThoughtDef>.GetNamedSilentFail("Slighted");
            Require(slighted != null,
                "The base-game Slighted social-memory ThoughtDef was not loaded.");
            pawn.needs.mood.thoughts.memories.TryGainMemory(
                slighted,
                otherPawn,
                null);
            List<string> reasons = DiaryContextBuilder.BuildSocialThoughtReasons(
                pawn,
                otherPawn,
                3);
            Require(reasons.Count > 0,
                "The loaded social memory did not produce a qualitative H7 reason.");
        }

        private static void AddMemoryRecord(
            PawnKnowledgeState state,
            string id,
            string renderedText,
            Pawn participant,
            string sourceEventId,
            int tick)
        {
            state.records.Add(new ImportantMemoryRecord
            {
                recordId = "PDTEST_H7_MEMORY_" + id,
                dedupKey = "PDTEST_H7_MEMORY_DEDUP_" + id,
                sourceEventId = sourceEventId,
                eventKind = "relation.spouse.gained",
                topicKey = "relation",
                tick = Math.Max(0, tick),
                dateLabel = "PDTEST_H7_MEMORY_DATE_" + id,
                participantIds = new List<string>
                {
                    participant.GetUniqueLoadID()
                },
                participantNames = new List<string>
                {
                    participant.LabelShort
                },
                fallbackSummary = renderedText,
                manualTextOverride = renderedText
            });
        }

        private sealed class ActualSocialReflectionStateAdapter : IExposable
        {
            public void ExposeData()
            {
                InvokeActualSocialReflectionExposeData();
            }
        }

        private sealed class LegacyMissingSocialReflectionStateAdapter : IExposable
        {
            public void ExposeData()
            {
                if (Scribe.mode != LoadSaveMode.Saving)
                {
                    InvokeActualSocialReflectionExposeData();
                }
            }
        }

        private sealed class RawSocialReflectionState
        {
            public List<PendingSocialReflectionState> pending;
            public List<SocialReflectionPairCooldownState> pairCooldowns;
            public List<SocialReflectionWriterCooldownState> writerCooldowns;
            public List<SocialReflectionHandledSourceState> handledSources;
            public int nextSchedulerTick;

            public static RawSocialReflectionState Capture(
                DiaryGameComponent component)
            {
                return new RawSocialReflectionState
                {
                    pending = PendingField.GetValue(component)
                        as List<PendingSocialReflectionState>,
                    pairCooldowns = PairCooldownsField.GetValue(component)
                        as List<SocialReflectionPairCooldownState>,
                    writerCooldowns = WriterCooldownsField.GetValue(component)
                        as List<SocialReflectionWriterCooldownState>,
                    handledSources = HandledSourcesField.GetValue(component)
                        as List<SocialReflectionHandledSourceState>,
                    nextSchedulerTick =
                        (int)NextSchedulerTickField.GetValue(component)
                };
            }

            public void Restore(DiaryGameComponent component)
            {
                SetSocialReflectionState(
                    component,
                    pending,
                    pairCooldowns,
                    writerCooldowns,
                    handledSources,
                    nextSchedulerTick);
            }
        }

        private static void ScribeRoundTripWithAfterSave<T>(
            T original,
            Action afterSave)
            where T : class, IExposable
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pawndiary_social_reflection_"
                    + Guid.NewGuid().ToString("N")
                    + ".xml");
            T loaded = null;
            try
            {
                Scribe.saver.InitSaving(path, "root");
                T saveRef = original;
                Scribe_Deep.Look(ref saveRef, "obj");
                Scribe.saver.FinalizeSaving();
                afterSave?.Invoke();

                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "obj");
                Scribe.loader.FinalizeLoading();
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive)
                {
                    Scribe.ForceStop();
                }
                try
                {
                    if (System.IO.File.Exists(path))
                    {
                        System.IO.File.Delete(path);
                    }
                }
                catch
                {
                    // Best-effort temp cleanup must not hide the real Scribe assertion.
                }
            }

            Require(loaded != null,
                "The H7 component Scribe adapter loaded null.");
        }

        // ---- reflection/state helpers ---------------------------------------------------------------

        private static void RequireProductionWiring()
        {
            Require(PendingField != null
                    && PairCooldownsField != null
                    && WriterCooldownsField != null
                    && HandledSourcesField != null
                    && NextSchedulerTickField != null
                    && ExposeSocialReflectionDataMethod != null
                    && TickSocialReflectionsMethod != null
                    && BuildHotSourceCandidateMethod != null
                    && BuildArchivedSourceCandidateMethod != null
                    && SourceResolutionType != null
                    && ResolveSourceCandidateMethod != null
                    && ResolutionStateField != null
                    && ResolutionTitleField != null
                    && ResolutionEndingField != null,
                "Could not resolve the production H7 scheduler/persistence test seams.");
        }

        private static void InvokeActualSocialReflectionExposeData()
        {
            Require(socialReflectionScribeTarget != null,
                "The H7 actual-component Scribe adapter has no target.");
            ExposeSocialReflectionDataMethod.Invoke(
                socialReflectionScribeTarget,
                null);
        }

        private static void InvokeTick(int tick)
        {
            TickSocialReflectionsMethod.Invoke(
                scope.Component,
                new object[] { tick });
        }

        private static string ResolutionState(object resolution)
        {
            object state = resolution == null
                ? null
                : ResolutionStateField.GetValue(resolution);
            return state == null ? string.Empty : state.ToString();
        }

        private static void SetSocialReflectionState(
            DiaryGameComponent component,
            List<PendingSocialReflectionState> pending,
            List<SocialReflectionPairCooldownState> pairCooldowns,
            List<SocialReflectionWriterCooldownState> writerCooldowns,
            List<SocialReflectionHandledSourceState> handledSources,
            int nextSchedulerTick)
        {
            PendingField.SetValue(component, pending);
            PairCooldownsField.SetValue(component, pairCooldowns);
            WriterCooldownsField.SetValue(component, writerCooldowns);
            HandledSourcesField.SetValue(component, handledSources);
            NextSchedulerTickField.SetValue(component, nextSchedulerTick);
        }

        private static void RegisterDayDigestCleanup(params string[] pawnIds)
        {
            FieldInfo rowsField = typeof(DiaryGameComponent).GetField(
                "dayDigestStates",
                PrivateInstance);
            MethodInfo rebuild = typeof(DiaryGameComponent).GetMethod(
                "RebuildDayDigestIndex",
                PrivateInstance);
            Require(rowsField != null && rebuild != null,
                "Could not resolve B6's saved digest rows for fixture cleanup.");
            DiaryGameComponent component = scope.Component;
            scope.RegisterCleanup(() =>
            {
                List<PawnDayDigestState> rows =
                    rowsField.GetValue(component) as List<PawnDayDigestState>;
                if (rows != null)
                {
                    rows.RemoveAll(row => row != null
                        && Array.IndexOf(pawnIds, row.pawnId) >= 0);
                    rebuild.Invoke(component, null);
                }
            });
        }

        private static List<PendingSocialReflectionState> PendingRows()
        {
            return PendingField.GetValue(scope.Component)
                as List<PendingSocialReflectionState>;
        }

        private static List<SocialReflectionPairCooldownState> PairCooldownRows()
        {
            return PairCooldownsField.GetValue(scope.Component)
                as List<SocialReflectionPairCooldownState>;
        }

        private static List<SocialReflectionWriterCooldownState> WriterCooldownRows()
        {
            return WriterCooldownsField.GetValue(scope.Component)
                as List<SocialReflectionWriterCooldownState>;
        }

        private static List<SocialReflectionHandledSourceState> HandledRows()
        {
            return HandledSourcesField.GetValue(scope.Component)
                as List<SocialReflectionHandledSourceState>;
        }

        private static DiarySocialReflectionDef FeatureDef()
        {
            DiarySocialReflectionDef feature =
                DefDatabase<DiarySocialReflectionDef>.GetNamedSilentFail(
                    "Diary_SocialReflection");
            Require(feature != null
                    && feature.chanceBands != null
                    && feature.chanceBands.Count > 0,
                "The loaded Diary_SocialReflection Def is missing its chance bands.");
            return feature;
        }

        private static T RequireDef<T>(string defName) where T : Def
        {
            T def = DefDatabase<T>.GetNamedSilentFail(defName);
            Require(def != null,
                "Required " + typeof(T).Name + " '" + defName + "' was not loaded.");
            return def;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new AssertionException(message);
            }
        }
    }
}

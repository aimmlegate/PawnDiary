// Loaded-game coverage for the pawn profile's per-pawn diary-generation switch and backlog preview.
//
// The profile dialog must inspect saved state without creating or normalizing a diary record. When a
// disabled pawn has resumable pages, the count shown before Save must also predict what the existing
// SetDiaryGenerationEnabled(..., true) path will actually release. These fixtures seed isolated saved
// pages through the production event factory, exercise the read-only profile helpers, and use Prompt
// Test Mode for the resume step so the real queue renders a prompt but never reaches an LLM provider.
// Bootstrap coverage also proves deferred ordinary work stays visible and schedules a fresh scan.
//
// New to C#/RimWorld? Reflection below reads the component's private saved-record stores so the
// no-create contract can be asserted directly. See AGENTS.md and PawnDiaryRimTestScope's header.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the profile read model is mutation-free and its resumable count stays aligned with the
    /// existing per-pawn resume queue for solo, neutral-arrival, and pair POV ownership.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryPawnProfileGenerationFixtureTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic =
            BindingFlags.Static | BindingFlags.NonPublic;

        private static readonly FieldInfo DiariesField =
            typeof(DiaryGameComponent).GetField("diaries", PrivateInstance);
        private static readonly FieldInfo DiariesByIdField =
            typeof(DiaryGameComponent).GetField("diariesById", PrivateInstance);
        private static readonly FieldInfo InitialArrivalScanPendingField =
            typeof(DiaryGameComponent).GetField("initialArrivalScanPending", PrivateInstance);
        private static readonly FieldInfo GenerationScanRequestedField =
            typeof(DiaryGameComponent).GetField("generationScanRequested", PrivateInstance);
        private static readonly MethodInfo CompleteInitialArrivalBootstrapMethod =
            typeof(DiaryGameComponent).GetMethod(
                "CompleteInitialArrivalBootstrap",
                PrivateInstance);
        private static readonly MethodInfo HasCompletedMainTextNeedingTitleMethod =
            typeof(DiaryGameComponent).GetMethod(
                "HasCompletedMainTextNeedingTitle",
                PrivateStatic);
        private static readonly MethodInfo LlmEnqueueMethod =
            typeof(LlmClient).GetMethod(
                "Enqueue",
                BindingFlags.Static | BindingFlags.Public);

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;
        private static string interceptedTitleEventId;
        private static string interceptedTitlePovRole;
        private static LlmGenerationRequest interceptedTitleRequest;

        /// <summary>
        /// Starts with one eligible, generation-disabled colonist and Prompt Test Mode enabled. The
        /// latter is a safety rail for tests that resume work through the real queue.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            scope.EnablePromptCapture(PromptContextDetailLevel.Full);
            pawn = scope.CreateAdultColonist();
        }

        /// <summary>Restores settings and removes every test pawn, page, and saved diary row.</summary>
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
                pawn = null;
            }
        }

        /// <summary>
        /// A profile inspection for an eligible pawn with no saved row returns the compatibility default
        /// (generation enabled), reports no backlog, and leaves both private stores empty for that pawn.
        /// </summary>
        [Test]
        public static void ProfileReadsDefaultTrueWithoutCreatingDiaryRecord()
        {
            Require(
                RawDiaryRecordExists(pawn),
                "The no-create fixture needs the setup-created diary row before detaching it.");
            DetachRawDiaryRecord(pawn);
            Require(
                !RawDiaryRecordExists(pawn),
                "The no-create fixture did not remove the setup diary row from both raw stores.");

            Require(
                scope.Component.DiaryGenerationEnabledForProfile(pawn),
                "An eligible pawn with no saved profile row did not receive the enabled compatibility default.");
            Require(
                scope.Component.PendingGenerationBacklogCountForProfile(pawn) == 0,
                "A pawn with no saved diary row reported resumable generation work.");

            // These are the same voice/outlook reads the profile constructor and repaint path use. They
            // must resolve safe catalog fallbacks without materializing or normalizing a saved row.
            WritingStyleResolution style = scope.Component.ResolveWritingStyleFor(pawn);
            PsychotypeResolution outlook = scope.Component.ResolvePsychotypeForDisplay(pawn);
            Require(style != null, "The no-record profile did not resolve a writing-style fallback.");
            Require(outlook != null, "The no-record profile did not resolve an outlook fallback.");
            Require(
                string.IsNullOrEmpty(scope.Component.CustomWritingStyleRuleFor(pawn))
                    && string.IsNullOrEmpty(scope.Component.CustomPsychotypeRuleFor(pawn))
                    && !scope.Component.WritingStylePinnedFor(pawn)
                    && !scope.Component.PsychotypePinnedFor(pawn),
                "No-record profile readers returned saved custom or pin state.");
            Require(
                !RawDiaryRecordExists(pawn),
                "Reading generation, voice, or outlook profile state created/indexed a saved diary row.");
        }

        /// <summary>
        /// Display reads also leave an existing partial/legacy row byte-for-byte in its raw fallback
        /// state. Normalization belongs to load/generation/write paths, never opening or repainting UI.
        /// </summary>
        [Test]
        public static void ProfileReadsDoNotNormalizeExistingLegacyRecord()
        {
            DetachRawDiaryRecord(pawn);
            string pawnId = pawn.GetUniqueLoadID();
            PawnDiaryRecord legacy = new PawnDiaryRecord
            {
                pawnId = pawnId,
                pawnName = pawn.LabelShortCap,
                personaDefName = null,
                psychotypeDefName = null,
                progressionState = null,
                arcSchedule = null,
                diaryGenerationEnabled = false
            };
            RequireDiaryRows().Add(legacy);
            RequireDiaryIndex().Add(pawnId, legacy);

            Require(
                !scope.Component.DiaryGenerationEnabledForProfile(pawn),
                "The profile reader ignored an existing disabled legacy row.");
            scope.Component.PendingGenerationBacklogCountForProfile(pawn);
            scope.Component.ResolveWritingStyleFor(pawn);
            scope.Component.ResolvePsychotypeForDisplay(pawn);
            scope.Component.CustomWritingStyleRuleFor(pawn);
            scope.Component.CustomPsychotypeRuleFor(pawn);
            scope.Component.WritingStylePinnedFor(pawn);
            scope.Component.PsychotypePinnedFor(pawn);

            Require(
                legacy.personaDefName == null
                    && legacy.psychotypeDefName == null
                    && legacy.progressionState == null
                    && legacy.arcSchedule == null,
                "Opening/inspecting the profile normalized the existing legacy row in memory.");
        }

        /// <summary>
        /// If the pawn becomes ineligible while its profile is open, its existing saved flag remains the
        /// read model. The explicit write must then run and reject normally instead of a derived false
        /// eligibility value being mistaken for an already-saved pause.
        /// </summary>
        [Test]
        public static void ExistingEnabledFlagRemainsVisibleWhilePawnIsIneligible()
        {
            string pawnId = pawn.GetUniqueLoadID();
            PawnDiaryRecord diary;
            Require(
                RequireDiaryIndex().TryGetValue(pawnId, out diary) && diary != null,
                "The ineligibility fixture could not find the setup pawn's saved diary row.");

            bool originalGenerationEnabled = diary.diaryGenerationEnabled;
            Faction originalFaction = pawn.Faction;
            diary.diaryGenerationEnabled = true;
            try
            {
                pawn.SetFaction(null);
                Require(
                    !DiaryGameComponent.IsDiaryEligible(pawn),
                    "The factionless fixture pawn unexpectedly remained diary-eligible.");
                Require(
                    scope.Component.DiaryGenerationEnabledForProfile(pawn),
                    "The profile reader replaced an existing enabled flag with derived ineligibility.");
                Require(
                    !scope.Component.TrySetDiaryGenerationEnabledForIntegration(pawn, false),
                    "The normal generation writer unexpectedly accepted an ineligible pawn.");
                Require(
                    diary.diaryGenerationEnabled,
                    "The rejected generation write changed the existing saved flag.");
            }
            finally
            {
                // Restore the disabled setup flag before crossing SetFaction back to the player. This
                // keeps any arrival observation from releasing generation work during fixture cleanup.
                diary.diaryGenerationEnabled = originalGenerationEnabled;
                if (pawn.Faction != originalFaction)
                {
                    pawn.SetFaction(originalFaction);
                }
            }
        }

        /// <summary>
        /// The compatibility default is also preference state: a pawn whose profile has no saved row is
        /// still "enabled" while temporarily ineligible. Save must therefore attempt the normal writer
        /// (and report its rejection), never mistake eligibility=false for an already-persisted pause.
        /// </summary>
        [Test]
        public static void MissingRowEnabledDefaultRemainsVisibleWhilePawnIsIneligible()
        {
            string pawnId = pawn.GetUniqueLoadID();
            List<PawnDiaryRecord> rows = RequireDiaryRows();
            Dictionary<string, PawnDiaryRecord> index = RequireDiaryIndex();
            PawnDiaryRecord detached;
            Require(
                index.TryGetValue(pawnId, out detached) && detached != null,
                "The missing-row ineligibility fixture could not find its setup diary row.");

            bool originalGenerationEnabled = detached.diaryGenerationEnabled;
            Faction originalFaction = pawn.Faction;
            rows.RemoveAll(
                row => row != null && string.Equals(row.pawnId, pawnId, StringComparison.Ordinal));
            index.Remove(pawnId);
            DiaryStateVersion.Bump();
            try
            {
                pawn.SetFaction(null);
                Require(
                    !DiaryGameComponent.IsDiaryEligible(pawn),
                    "The factionless missing-row fixture pawn unexpectedly remained diary-eligible.");
                Require(
                    scope.Component.DiaryGenerationEnabledForProfile(pawn),
                    "The profile reader replaced the missing-row enabled default with derived ineligibility.");
                Require(
                    !scope.Component.TrySetDiaryGenerationEnabledForIntegration(pawn, false),
                    "The normal generation writer unexpectedly accepted the ineligible missing-row pawn.");
                Require(
                    !RawDiaryRecordExists(pawn),
                    "The rejected generation write created a saved row for an ineligible pawn.");
            }
            finally
            {
                // Reattach the exact setup record (disabled in the ordinary fixture) before restoring
                // faction, so the SetFaction arrival boundary cannot accidentally release LLM work.
                rows.RemoveAll(
                    row => row != null && string.Equals(row.pawnId, pawnId, StringComparison.Ordinal));
                index.Remove(pawnId);
                detached.diaryGenerationEnabled = originalGenerationEnabled;
                rows.Add(detached);
                index[pawnId] = detached;
                DiaryStateVersion.Bump();
                if (pawn.Faction != originalFaction)
                {
                    pawn.SetFaction(originalFaction);
                }
            }
        }

        /// <summary>
        /// Only a queueable not-generated solo POV contributes to the profile backlog. Every ordinary
        /// terminal/in-flight status is excluded, matching DiaryEvent.CanQueueGeneration.
        /// </summary>
        [Test]
        public static void SoloBacklogExcludesPendingAndTerminalStatuses()
        {
            DiaryEvent page = SeedSoloPage("status-exclusion");
            RequireBacklog(pawn, 1, "A fresh not-generated solo page was not counted.");

            page.MarkQueued(DiaryEvent.InitiatorRole);
            RequireBacklog(pawn, 0, "A pending solo page was counted as resumable backlog.");

            page.ResetPendingToNotGenerated(DiaryEvent.InitiatorRole);
            RequireBacklog(pawn, 1, "Resetting pending to not-generated did not restore the backlog.");

            page.MarkFailed(DiaryEvent.InitiatorRole, "Pawn profile fixture failure");
            RequireBacklog(pawn, 0, "A failed solo page was counted as automatically resumable.");

            page.PrepareForRegeneration(DiaryEvent.InitiatorRole);
            RequireBacklog(pawn, 1, "Preparing the failed page for regeneration did not restore it.");

            page.MarkSkipped(DiaryEvent.InitiatorRole, "Pawn profile fixture generic skip");
            RequireBacklog(pawn, 0, "A generically skipped solo page was counted as resumable.");

            page.PrepareForRegeneration(DiaryEvent.InitiatorRole);
            page.MarkPromptOnly(DiaryEvent.InitiatorRole, "Pawn profile fixture prompt capture");
            RequireBacklog(pawn, 0, "A prompt-only solo page was counted as resumable.");

            page.PrepareForRegeneration(DiaryEvent.InitiatorRole);
            page.MarkInjectedTextComplete(
                DiaryEvent.InitiatorRole,
                "A completed pawn-profile fixture page.");
            RequireBacklog(pawn, 0, "A completed solo page was counted as resumable.");
        }

        /// <summary>
        /// The preview count for a disabled pawn predicts the existing resume setter: one solo page is
        /// counted before enable, then the setter queues that exact POV and Prompt Test Mode settles it
        /// as prompt-only without contacting a provider.
        /// </summary>
        [Test]
        public static void DisabledSoloBacklogMatchesResumeQueue()
        {
            DiaryEvent page = SeedSoloPage("resume-parity");
            Require(
                !scope.Component.DiaryGenerationEnabledForProfile(pawn),
                "The resume fixture pawn did not begin with generation disabled.");
            RequireBacklog(pawn, 1, "The disabled pawn's queueable solo page was not previewed.");

            scope.Component.SetDiaryGenerationEnabled(pawn, true);

            Require(
                scope.Component.DiaryGenerationEnabledForProfile(pawn),
                "The existing generation setter did not persist the enabled value.");
            scope.CapturedPrompt(page, DiaryEvent.InitiatorRole);
            RequireBacklog(pawn, 0, "The resumed solo page remained in the preview backlog after queueing.");
        }

        /// <summary>
        /// Regeneration deliberately leaves the previous prose visible while its replacement is queued.
        /// Title catch-up must wait for that replacement main request to complete instead of dispatching
        /// a title for the stale retained text in parallel.
        /// </summary>
        [Test]
        public static void RegenerationRetainedTextCannotQueueStaleTitle()
        {
            if (HasCompletedMainTextNeedingTitleMethod == null)
            {
                throw new AssertionException(
                    "The profile fixture could not locate the missing-title eligibility rule.");
            }

            DiaryEvent page = SeedSoloPage("regeneration-title-ordering");
            page.MarkInjectedTextComplete(
                DiaryEvent.InitiatorRole,
                "The previous completed pawn-profile fixture page.");
            Require(
                MissingTitleIsQueueable(page, DiaryEvent.InitiatorRole),
                "A completed main page with missing title was not eligible for title catch-up.");

            page.PrepareForRegeneration(DiaryEvent.InitiatorRole);
            Require(
                page.HasGeneratedTextForRole(DiaryEvent.InitiatorRole),
                "The regeneration fixture no longer retained the previous prose.");
            Require(
                !MissingTitleIsQueueable(page, DiaryEvent.InitiatorRole),
                "Title catch-up accepted retained stale prose before regeneration completed.");

            page.MarkInjectedTextComplete(
                DiaryEvent.InitiatorRole,
                "The replacement completed pawn-profile fixture page.");
            Require(
                MissingTitleIsQueueable(page, DiaryEvent.InitiatorRole),
                "Title catch-up did not reopen after replacement prose completed.");
        }

        /// <summary>
        /// A neutral arrival can finish its main prose while its exact owner is disabled, which blocks the
        /// normal title follow-up. Re-enabling that pawn must revisit the already-complete neutral slot and
        /// queue its title without requeueing or replacing the completed main text. The transport prefix is
        /// a test-only safety boundary: it captures the fully built title request and suppresses provider IO.
        /// </summary>
        [Test]
        public static void ResumeQueuesMissingTitleForCompletedNeutralArrival()
        {
            if (LlmEnqueueMethod == null)
            {
                throw new AssertionException(
                    "The profile fixture could not locate the LLM enqueue transport boundary.");
            }

            string pawnId = pawn.GetUniqueLoadID();
            DiaryEvent arrival = scope.Component.AddSoloEvent(
                pawn,
                null,
                ArrivalSignal.ArrivalDefName,
                "pawn profile title-resume fixture",
                "A completed arrival page lost its title follow-up while generation was paused.",
                string.Empty,
                ArrivalEventData.BuildGameContext(
                    pawn.LabelShortCap,
                    pawnId,
                    "arrival_source=pawn_profile_title_resume_fixture"));
            Require(arrival != null, "The title-resume fixture could not seed a neutral arrival page.");
            Require(
                arrival.IsArrivalDescriptionFor(pawnId),
                "The title-resume arrival did not identify the profile pawn as its exact owner.");
            arrival.MarkInjectedTextComplete(
                DiaryEvent.NeutralRole,
                "I had already finished writing about my arrival.");

            PawnDiaryRecord diary = RequireDiaryIndex()[pawnId];
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            bool originalGenerationEnabled = diary.diaryGenerationEnabled;
            bool originalGenerateTitles = settings.generateTitles;
            bool originalPromptTestMode = settings.promptTestMode;
            List<ApiEndpointConfig> originalEndpoints = settings.apiEndpoints;
            string harmonyId =
                "PawnDiary.RimTest.ProfileTitleResume." + Guid.NewGuid().ToString("N");
            Harmony transportIntercept = new Harmony(harmonyId);
            try
            {
                Require(
                    !originalGenerationEnabled,
                    "The title-resume fixture pawn did not begin with generation disabled.");
                Require(
                    originalPromptTestMode,
                    "The title-resume fixture lost its Prompt Test Mode safety rail.");
                Require(
                    MissingTitleIsQueueable(arrival, DiaryEvent.NeutralRole),
                    "The completed neutral arrival was not eligible for its missing title follow-up.");

                interceptedTitleEventId = arrival.eventId;
                interceptedTitlePovRole = DiaryEvent.NeutralRole;
                interceptedTitleRequest = null;
                transportIntercept.Patch(
                    LlmEnqueueMethod,
                    prefix: new HarmonyMethod(
                        typeof(PawnDiaryPawnProfileGenerationFixtureTests),
                        nameof(InterceptLlmEnqueueWithoutProvider)));

                settings.generateTitles = true;
                settings.apiEndpoints = new List<ApiEndpointConfig>
                {
                    new ApiEndpointConfig(
                        "http://127.0.0.1:1/v1",
                        string.Empty,
                        "pawndiary-rimtest-intercepted-title")
                };
                // QueueTitleRequest intentionally exits in Prompt Test Mode. The transport is already
                // intercepted above, so temporarily cross only that guard to exercise the complete
                // production title planner without permitting a provider request.
                settings.promptTestMode = false;

                scope.Component.SetDiaryGenerationEnabled(pawn, true);

                Require(
                    diary.diaryGenerationEnabled,
                    "Resuming the profile did not persist the pawn's enabled generation flag.");
                Require(
                    DiaryEvent.RoleEquals(
                        arrival.StatusForRole(DiaryEvent.NeutralRole),
                        DiaryEvent.CompleteStatus)
                        && arrival.HasGeneratedTextForRole(DiaryEvent.NeutralRole)
                        && string.Equals(
                            arrival.DisplayTextForRole(DiaryEvent.NeutralRole),
                            "I had already finished writing about my arrival.",
                            StringComparison.Ordinal),
                    "Title-only resume changed or requeued the completed neutral main text.");
                Require(
                    arrival.IsTitlePending(DiaryEvent.NeutralRole),
                    "Resuming the profile did not mark the missing neutral title as queued.");
                Require(
                    interceptedTitleRequest != null
                        && interceptedTitleRequest.isTitleRequest
                        && string.Equals(
                            interceptedTitleRequest.eventId,
                            arrival.eventId,
                            StringComparison.Ordinal)
                        && DiaryEvent.RoleEquals(
                            interceptedTitleRequest.povRole,
                            DiaryEvent.NeutralRole),
                    "The resume path did not send the completed neutral slot to the title queue boundary.");
            }
            finally
            {
                transportIntercept.UnpatchAll(harmonyId);
                diary.diaryGenerationEnabled = originalGenerationEnabled;
                settings.generateTitles = originalGenerateTitles;
                settings.promptTestMode = originalPromptTestMode;
                settings.apiEndpoints = originalEndpoints;
                interceptedTitleEventId = null;
                interceptedTitlePovRole = null;
                interceptedTitleRequest = null;
            }
        }

        /// <summary>
        /// A load/new-game generation pass can run while the starting-arrival prerequisite still blocks
        /// ordinary pages. The profile must keep that deferred page visible, and completing bootstrap must
        /// request another pass instead of leaving the page stranded until an unrelated later trigger.
        /// </summary>
        [Test]
        public static void ArrivalBootstrapDeferralRemainsVisibleAndSchedulesResume()
        {
            if (InitialArrivalScanPendingField == null
                || GenerationScanRequestedField == null
                || CompleteInitialArrivalBootstrapMethod == null)
            {
                throw new AssertionException(
                    "The profile fixture could not locate the arrival-bootstrap generation state.");
            }

            DiaryEvent page = SeedSoloPage("arrival-bootstrap-deferral");
            PawnDiaryRecord diary = RequireDiaryIndex()[pawn.GetUniqueLoadID()];
            bool originalArrivalPending =
                (bool)InitialArrivalScanPendingField.GetValue(scope.Component);
            bool originalGenerationScanRequested =
                (bool)GenerationScanRequestedField.GetValue(scope.Component);
            bool originalGenerationEnabled = diary.diaryGenerationEnabled;
            try
            {
                InitialArrivalScanPendingField.SetValue(scope.Component, true);
                GenerationScanRequestedField.SetValue(scope.Component, false);

                // The real enable path tries its scoped queue immediately. Bootstrap deliberately
                // defers that attempt, leaving this ordinary page in the resumable state.
                scope.Component.SetDiaryGenerationEnabled(pawn, true);
                Require(
                    DiaryEvent.RoleEquals(
                        page.StatusForRole(DiaryEvent.InitiatorRole),
                        DiaryEvent.NotGeneratedStatus)
                        && string.IsNullOrWhiteSpace(
                            page.PromptForRole(DiaryEvent.InitiatorRole)),
                    "The starting-arrival gate did not defer the ordinary profile page.");
                RequireBacklog(
                    pawn,
                    1,
                    "The profile hid ordinary resumable work while arrival bootstrap was pending.");

                CompleteInitialArrivalBootstrapMethod.Invoke(scope.Component, null);
                Require(
                    !(bool)InitialArrivalScanPendingField.GetValue(scope.Component),
                    "Completing arrival bootstrap left its generation gate closed.");
                Require(
                    (bool)GenerationScanRequestedField.GetValue(scope.Component),
                    "Completing arrival bootstrap did not schedule the deferred generation scan.");
            }
            finally
            {
                diary.diaryGenerationEnabled = originalGenerationEnabled;
                InitialArrivalScanPendingField.SetValue(scope.Component, originalArrivalPending);
                GenerationScanRequestedField.SetValue(
                    scope.Component,
                    originalGenerationScanRequested);
            }
        }

        /// <summary>
        /// Arrival descriptions belong to their neutral slot, not the factory's initiator slot. A direct
        /// queue attempt while the exact owner is disabled must remain untouched; re-enabling then queues
        /// only that neutral prompt. A failed initiator POV therefore cannot hide or receive this work.
        /// </summary>
        [Test]
        public static void NeutralArrivalBacklogUsesExactOwnerAndRole()
        {
            string pawnId = pawn.GetUniqueLoadID();
            DiaryEvent arrival = scope.Component.AddSoloEvent(
                pawn,
                null,
                ArrivalSignal.ArrivalDefName,
                "pawn profile arrival fixture",
                "A pawn profile arrival fixture page.",
                string.Empty,
                ArrivalEventData.BuildGameContext(
                    pawn.LabelShortCap,
                    pawnId,
                    "arrival_source=pawn_profile_fixture"));
            Require(arrival != null, "The profile fixture could not seed a neutral arrival page.");
            Require(
                arrival.IsArrivalDescriptionFor(pawnId),
                "The seeded arrival page did not identify the profile pawn as its exact owner.");

            arrival.MarkFailed(DiaryEvent.InitiatorRole, "The neutral slot must own this work");
            scope.Component.QueueArrivalDescriptionFor(arrival);
            Require(
                DiaryEvent.RoleEquals(
                    arrival.StatusForRole(DiaryEvent.NeutralRole),
                    DiaryEvent.NotGeneratedStatus),
                "A disabled profile allowed its neutral arrival page to enter generation.");
            Require(
                string.IsNullOrWhiteSpace(arrival.PromptForRole(DiaryEvent.NeutralRole)),
                "A disabled profile captured a neutral arrival prompt before re-enable.");
            RequireBacklog(pawn, 1, "The queueable neutral arrival slot was not counted for its owner.");

            scope.Component.SetDiaryGenerationEnabled(pawn, true);

            scope.CapturedPrompt(arrival, DiaryEvent.NeutralRole);
            Require(
                DiaryEvent.RoleEquals(
                    arrival.StatusForRole(DiaryEvent.InitiatorRole),
                    DiaryEvent.FailedStatus),
                "Resuming the neutral arrival unexpectedly changed its initiator slot.");
            RequireBacklog(pawn, 0, "The queued neutral arrival remained in the preview backlog.");
        }

        /// <summary>
        /// A pair page contributes only the POV owned by the queried pawn. Making the initiator terminal
        /// removes only that pawn's count while the disabled recipient retains one resumable POV.
        /// </summary>
        [Test]
        public static void PairBacklogCountsEachPawnsOwnedPov()
        {
            Pawn partner = scope.CreateAdultColonist();
            DiaryEvent pair = scope.Component.AddPairwiseEvent(
                pawn,
                partner,
                "DeepTalk",
                "pawn profile pair fixture",
                "I remembered a conversation from my side.",
                "I remembered the same conversation from my side.",
                string.Empty,
                "interaction=DeepTalk; source=pawn_profile_fixture");
            Require(pair != null, "The profile fixture could not seed a pair page.");

            RequireBacklog(pawn, 1, "The pair initiator did not own one resumable POV.");
            RequireBacklog(partner, 1, "The pair recipient did not own one resumable POV.");

            pair.MarkFailed(DiaryEvent.InitiatorRole, "Pawn profile fixture initiator failure");
            RequireBacklog(pawn, 0, "The failed initiator POV still counted for its owner.");
            RequireBacklog(partner, 1, "Changing the initiator POV removed the recipient's backlog.");

            pair.MarkPromptOnly(DiaryEvent.RecipientRole, "Pawn profile fixture recipient capture");
            RequireBacklog(partner, 0, "The terminal recipient POV still counted for its owner.");
        }

        /// <summary>Creates one registered solo page without queueing it.</summary>
        private static DiaryEvent SeedSoloPage(string marker)
        {
            DiaryEvent page = scope.Component.AddSoloEvent(
                pawn,
                null,
                "Inspired_Creativity",
                "pawn profile inspiration fixture",
                "A sudden idea became a private diary memory.",
                string.Empty,
                "inspiration=Inspired_Creativity; source=pawn_profile_" + marker);
            Require(page != null, "The profile fixture could not seed a solo page: " + marker + ".");
            Require(
                DiaryEvent.RoleEquals(
                    page.StatusForRole(DiaryEvent.InitiatorRole),
                    DiaryEvent.NotGeneratedStatus),
                "The seeded solo page did not begin in the not-generated state.");
            return page;
        }

        /// <summary>Asserts the profile preview count with a scenario-specific failure message.</summary>
        private static void RequireBacklog(Pawn subject, int expected, string message)
        {
            int actual = scope.Component.PendingGenerationBacklogCountForProfile(subject);
            Require(actual == expected, message + " Expected " + expected + ", got " + actual + ".");
        }

        /// <summary>Invokes the production missing-title eligibility rule without dispatching an LLM request.</summary>
        private static bool MissingTitleIsQueueable(DiaryEvent page, string povRole)
        {
            return (bool)HasCompletedMainTextNeedingTitleMethod.Invoke(
                null,
                new object[] { page, povRole });
        }

        /// <summary>
        /// Harmony prefix used only by the title-resume fixture. It captures the target request at the
        /// last boundary before background transport and suppresses every enqueue while installed, so a
        /// failing assertion can never contact either the synthetic endpoint or the player's providers.
        /// </summary>
        private static bool InterceptLlmEnqueueWithoutProvider(LlmGenerationRequest request)
        {
            if (request != null
                && request.isTitleRequest
                && string.Equals(request.eventId, interceptedTitleEventId, StringComparison.Ordinal)
                && DiaryEvent.RoleEquals(request.povRole, interceptedTitlePovRole))
            {
                interceptedTitleRequest = request;
            }

            return false;
        }

        /// <summary>
        /// Removes the setup-created row from both raw stores, producing the backward-compatible
        /// "eligible pawn with no profile row" state without calling a production creator.
        /// </summary>
        private static void DetachRawDiaryRecord(Pawn subject)
        {
            List<PawnDiaryRecord> rows = RequireDiaryRows();
            Dictionary<string, PawnDiaryRecord> index = RequireDiaryIndex();
            string pawnId = subject.GetUniqueLoadID();
            PawnDiaryRecord indexed;
            Require(
                index.TryGetValue(pawnId, out indexed) && indexed != null,
                "The setup diary row was absent from the raw pawn-id index.");

            int removed = rows.RemoveAll(
                row => row != null && string.Equals(row.pawnId, pawnId, StringComparison.Ordinal));
            index.Remove(pawnId);
            Require(removed == 1, "Expected to detach exactly one setup diary row, removed " + removed + ".");
            DiaryStateVersion.Bump();
        }

        /// <summary>Checks both raw stores agree that an exact pawn record exists or is absent.</summary>
        private static bool RawDiaryRecordExists(Pawn subject)
        {
            string pawnId = subject.GetUniqueLoadID();
            List<PawnDiaryRecord> rows = RequireDiaryRows();
            Dictionary<string, PawnDiaryRecord> index = RequireDiaryIndex();
            bool inRows = rows.Exists(
                row => row != null && string.Equals(row.pawnId, pawnId, StringComparison.Ordinal));
            bool inIndex = index.ContainsKey(pawnId);
            Require(inRows == inIndex, "The raw diary list and pawn-id index disagreed for the fixture pawn.");
            return inRows;
        }

        private static List<PawnDiaryRecord> RequireDiaryRows()
        {
            List<PawnDiaryRecord> rows =
                DiariesField?.GetValue(scope.Component) as List<PawnDiaryRecord>;
            if (rows == null)
            {
                throw new AssertionException(
                    "The profile fixture could not read DiaryGameComponent.diaries.");
            }

            return rows;
        }

        private static Dictionary<string, PawnDiaryRecord> RequireDiaryIndex()
        {
            Dictionary<string, PawnDiaryRecord> index =
                DiariesByIdField?.GetValue(scope.Component)
                    as Dictionary<string, PawnDiaryRecord>;
            if (index == null)
            {
                throw new AssertionException(
                    "The profile fixture could not read DiaryGameComponent.diariesById.");
            }

            return index;
        }

        private static void Require(bool condition, string message)
        {
            PawnDiaryRimTestScope.Require(condition, message);
        }
    }
}

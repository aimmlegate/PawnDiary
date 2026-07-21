// Loaded-game fixtures for Ideology Phase 2 mutation infrastructure, exact interaction consumers, and
// the exact IdeoChange crisis consumer on the existing solo mental-state page.
// These tests invoke real Pawn_IdeoTracker methods patched by production, inspect their detached
// before/after facts, prove exact ability ownership leaves the enriched downstream PlayLog interaction
// as the sole diary page, and force the real MentalState_IdeoChange.PreStart boundary once.
// Every cache, tuning value, setting, PlayLog row, temporary Ideo/Harmony object, RNG scope, and
// disposable pawn is restored by finally-backed teardown or an explicit try/finally block.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Exercises optional-DLC wiring, mutation coalescing, failure isolation, and ownership.</summary>
    [TestSuite]
    public static class PawnDiaryIdeologyPhase2InfrastructureTests
    {
        private const string LogPrefix = "[PawnDiary RimTest Ideology Phase 2 infrastructure] ";
        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;
        private static Pawn otherPawn;
        private static DiaryTuningDef tuning;
        private static float originalMinimumChance;
        private static float originalMaximumChance;
        private static float originalGenerationWeight;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(
                "conversion", "heartfelt", "abilityUsed", "beliefCrisis");
            pawn = scope.CreateAdultColonist();
            otherPawn = scope.CreateAdultColonist();
            BeliefMutationCache.Reset();
            scope.RegisterCleanup(BeliefMutationCache.Reset);

            tuning = DiaryTuning.Current;
            originalMinimumChance = tuning.abilityUseMinChance;
            originalMaximumChance = tuning.abilityUseMaxChance;
            originalGenerationWeight = PawnDiaryMod.Settings.generationChanceWeight;
            tuning.abilityUseMinChance = 1f;
            tuning.abilityUseMaxChance = 1f;
            PawnDiaryMod.Settings.generationChanceWeight = 1f;
            scope.RegisterCleanup(() =>
            {
                if (tuning != null)
                {
                    tuning.abilityUseMinChance = originalMinimumChance;
                    tuning.abilityUseMaxChance = originalMaximumChance;
                }
                if (PawnDiaryMod.Settings != null)
                    PawnDiaryMod.Settings.generationChanceWeight = originalGenerationWeight;
            });
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
                pawn = null;
                otherPawn = null;
                tuning = null;
            }
        }

        /// <summary>
        /// Verifies all three exact signatures are owned by the production patch when Ideology is
        /// active, and that registration plus live projection remain inert in a no-DLC profile.
        /// </summary>
        [Test]
        public static void ExactHarmonyWiringMatchesOptionalDlcState()
        {
            MethodBase conversion = AccessTools.DeclaredMethod(typeof(Pawn_IdeoTracker),
                nameof(Pawn_IdeoTracker.IdeoConversionAttempt),
                new[] { typeof(float), typeof(Ideo), typeof(bool) });
            MethodBase offset = AccessTools.DeclaredMethod(typeof(Pawn_IdeoTracker),
                nameof(Pawn_IdeoTracker.OffsetCertainty), new[] { typeof(float) });
            MethodBase setIdeology = AccessTools.DeclaredMethod(typeof(Pawn_IdeoTracker),
                nameof(Pawn_IdeoTracker.SetIdeo), new[] { typeof(Ideo) });
            PawnDiaryRimTestScope.Require(conversion != null && offset != null && setIdeology != null,
                "The installed RimWorld assembly did not expose the verified Ideology signatures.");

            bool active = ModsConfig.IdeologyActive;
            PawnDiaryRimTestScope.Require(
                DiaryIdeologyMutationPatches.ConversionAttemptHookReady == active
                    && DiaryIdeologyMutationPatches.OffsetCertaintyHookReady == active
                    && DiaryIdeologyMutationPatches.SetIdeologyHookReady == active,
                "Ideology mutation readiness did not match ModsConfig.IdeologyActive.");
            AssertOwnedPatchState(conversion, active);
            AssertOwnedPatchState(offset, active);
            AssertOwnedPatchState(setIdeology, active);

            BeliefMutationState state;
            bool captured = DlcContext.TryCaptureBeliefMutationState(pawn.ideo, out state);
            if (!active)
            {
                PawnDiaryRimTestScope.Require(!captured && state == null && BeliefMutationCache.Count == 0,
                    "The mutation adapter/cache must be inert without Ideology.");
                PawnDiaryRimTestScope.Require(BeliefMutationEvidenceAdapter.ForInteraction(
                        "Reassure", "heartfelt", pawn.GetUniqueLoadID(), otherPawn.GetUniqueLoadID(),
                        Find.TickManager.TicksGame) == null,
                    "The interaction evidence adapter must be inert without Ideology.");
                AssertNoDlcConvertAbilityKeepsOrdinaryRoute();
                return;
            }

            if (Find.IdeoManager?.classicMode == true)
            {
                // Classic Ideology mode keeps the package active while vanilla tracker mutations are
                // disabled. Patch/policy ownership still matters, but a live mutable state does not.
                Log.Message(LogPrefix + "live mutation projection: not applicable (classic mode). ");
            }
            else
            {
                PawnDiaryRimTestScope.Require(captured && state != null,
                    "An Ideology-active pawn did not project a detached mutation state.");
            }
            BeliefPolicySnapshot ownership = DiaryBeliefPolicy.Snapshot();
            PawnDiaryRimTestScope.Require(
                BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Thought,
                    "FailedConvertAbilityInitiator", true, ownership.enabled,
                    ownership.canonicalEventOwnershipRules) == "conversion"
                && BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Thought,
                    "FailedConvertAbilityRecipient", true, ownership.enabled,
                    ownership.canonicalEventOwnershipRules) == "conversion",
                "The loaded XML policy did not assign both failed-conversion thoughts to conversion.");
            AssertLoadedMutationRule(ownership, "ConvertIdeoAttempt", "conversion", "conversion",
                BeliefMutationCauseTokens.ConversionAttempt, BeliefMutationConversionResultTokens.Known,
                BeliefMutationCertaintyDirectionTokens.Any, BeliefMutationIdeologyChangeTokens.Any, true);
            AssertLoadedMutationRule(ownership, "Convert_Success", "conversion", "conversion",
                BeliefMutationCauseTokens.ConversionAttempt, BeliefMutationConversionResultTokens.Success,
                BeliefMutationCertaintyDirectionTokens.Any, BeliefMutationIdeologyChangeTokens.Changed, true);
            AssertLoadedMutationRule(ownership, "Convert_Failure", "conversion", "conversion",
                BeliefMutationCauseTokens.ConversionAttempt, BeliefMutationConversionResultTokens.Failure,
                BeliefMutationCertaintyDirectionTokens.Decrease, BeliefMutationIdeologyChangeTokens.Unchanged, true);
            AssertLoadedMutationRule(ownership, "Reassure", "heartfelt", "reassurance",
                BeliefMutationCauseTokens.CertaintyOffset, BeliefMutationConversionResultTokens.None,
                BeliefMutationCertaintyDirectionTokens.Increase, BeliefMutationIdeologyChangeTokens.Unchanged, false);
        }

        /// <summary>Mechanical cache observation alone must never authorize or emit a diary page.</summary>
        [Test]
        public static void MutationCacheAloneCannotCreatePage()
        {
            if (!RequireActiveTracker()) return;
            EnsureCurrentIdeology();
            float offset = pawn.ideo.Certainty > 0.5f ? -0.05f : 0.05f;
            BeliefMutationCache.Reset();
            scope.RequireNoNewEvent(() => pawn.ideo.OffsetCertainty(offset));
            PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 1,
                "The no-page tracker call did not leave its detached cache fact.");
            PawnDiaryRimTestScope.Require(BeliefMutationEvidenceAdapter.ForInteraction(
                    "DeepTalk", "heartfelt", otherPawn.GetUniqueLoadID(), pawn.GetUniqueLoadID(),
                    Find.TickManager.TicksGame) == null,
                "An unrelated authorized event name could read mutation evidence without an exact XML rule.");
        }

        /// <summary>
        /// Invokes real SetIdeo and OffsetCertainty methods. Nested calls must coalesce to one exact
        /// earliest-before/latest-after row; a later sequential call remains a separate row.
        /// </summary>
        [Test]
        public static void RealTrackerCallsCaptureBeforeAfterAndCoalesceOnlyNestedWork()
        {
            if (!RequireActiveTracker()) return;
            Ideo beforeIdeology = EnsureCurrentIdeology();
            Ideo afterIdeology = FindOrCreateDifferentIdeology(beforeIdeology);

            BeliefMutationCache.Reset();
            pawn.ideo.SetIdeo(afterIdeology);
            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            BeliefMutationSnapshot changed = BeliefMutationCache.PeekLatest(
                pawn.GetUniqueLoadID(), Find.TickManager.TicksGame, policy);
            PawnDiaryRimTestScope.Require(changed != null && changed.ideologyChanged,
                "Real SetIdeo did not leave a detached ideology-change row.");
            PawnDiaryRimTestScope.Require(changed.beforeIdeologyId == beforeIdeology.GetUniqueLoadID()
                    && changed.afterIdeologyId == afterIdeology.GetUniqueLoadID(),
                "SetIdeo did not preserve the actual before/after ideoligion identities.");
            PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 1,
                "Nested work inside one SetIdeo call did not coalesce to one row.");

            float offset = pawn.ideo.Certainty > 0.5f ? -0.05f : 0.05f;
            float certaintyBeforeOffset = pawn.ideo.Certainty;
            pawn.ideo.OffsetCertainty(offset);
            BeliefMutationSnapshot certainty = BeliefMutationCache.PeekLatest(
                pawn.GetUniqueLoadID(), Find.TickManager.TicksGame, policy);
            PawnDiaryRimTestScope.Require(certainty != null && certainty.certaintyChanged,
                "Real OffsetCertainty did not leave a detached certainty-change row.");
            PawnDiaryRimTestScope.Require(
                Math.Abs(certainty.beforeCertainty - certaintyBeforeOffset) < 0.0001f
                    && Math.Abs(certainty.afterCertainty - pawn.ideo.Certainty) < 0.0001f,
                "OffsetCertainty did not preserve its actual before/after certainty facts.");
            PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 2,
                "A sequential same-tick certainty action was incorrectly merged into SetIdeo.");
        }

        /// <summary>
        /// Calls the real conversion boundary and verifies its return value, attempted Ideo, earliest
        /// before state, and final live after state survive any nested certainty/SetIdeo calls as one row.
        /// </summary>
        [Test]
        public static void RealConversionAttemptOwnsOneCoalescedMechanicalResult()
        {
            if (!RequireActiveTracker()) return;
            Ideo beforeIdeology = EnsureCurrentIdeology();
            Ideo attemptedIdeology = FindOrCreateDifferentIdeology(beforeIdeology);

            // Vanilla converts only after certainty is exhausted. Establish that boundary before the
            // observed call, then reset the cache so the fixture deterministically exercises nested
            // SetIdeo work instead of usually recording a failed, non-nested 0.05 reduction.
            pawn.ideo.OffsetCertainty(-pawn.ideo.Certainty);
            BeliefMutationCache.Reset();
            float beforeCertainty = pawn.ideo.Certainty;
            bool result = pawn.ideo.IdeoConversionAttempt(0.05f, attemptedIdeology,
                applyCertaintyFactor: false);
            BeliefMutationSnapshot mutation = BeliefMutationCache.PeekLatest(
                pawn.GetUniqueLoadID(), Find.TickManager.TicksGame, DiaryBeliefPolicy.Snapshot());
            PawnDiaryRimTestScope.Require(mutation != null && mutation.conversionSucceeded == result,
                "Real IdeoConversionAttempt did not retain its exact bool result.");
            PawnDiaryRimTestScope.Require(result,
                "The zero-certainty conversion fixture did not enter vanilla's success/SetIdeo path.");
            PawnDiaryRimTestScope.Require(
                mutation.beforeIdeologyId == beforeIdeology.GetUniqueLoadID()
                    && mutation.attemptedIdeologyId == attemptedIdeology.GetUniqueLoadID(),
                "Conversion capture lost its actual before/attempted ideoligion identities.");
            PawnDiaryRimTestScope.Require(Math.Abs(mutation.beforeCertainty - beforeCertainty) < 0.0001f
                    && mutation.afterIdeologyId == pawn.ideo.Ideo.GetUniqueLoadID()
                    && Math.Abs(mutation.afterCertainty - pawn.ideo.Certainty) < 0.0001f,
                "Conversion capture did not preserve its final live ideology/certainty facts.");
            PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 1
                    && mutation.causeTokens.Contains(BeliefMutationCauseTokens.ConversionAttempt)
                    && mutation.causeTokens.Contains(BeliefMutationCauseTokens.SetIdeology),
                "Nested conversion work did not coalesce under one conversion-owned row.");
        }

        /// <summary>
        /// Forces the real IdeoChange mental-state boundary once. Vanilla PreStart performs its
        /// conversion attempt before Pawn Diary's successful-start postfix, so the existing solo page
        /// must freeze that exact mutation without consuming it or creating a second page.
        /// </summary>
        [Test]
        public static void RealIdeoChangeBoundaryEnrichesItsSingleSoloPage()
        {
            if (!ModsConfig.IdeologyActive)
            {
                PawnDiaryRimTestScope.Require(
                    BeliefMutationEvidenceAdapter.ForMentalState(
                        "IdeoChange", "beliefCrisis", pawn.GetUniqueLoadID(),
                        Find.TickManager.TicksGame) == null
                        && BeliefMutationCache.Count == 0,
                    "The IdeoChange consumer/cache must remain inert without Ideology.");
                Log.Message(LogPrefix
                    + "real IdeoChange boundary: not applicable (Ideology inactive). ");
                return;
            }

            MentalStateDef stateDef = DefDatabase<MentalStateDef>.GetNamedSilentFail("IdeoChange");
            MentalBreakDef breakDef = DefDatabase<MentalBreakDef>.GetNamedSilentFail("IdeoChange");
            PawnDiaryRimTestScope.Require(stateDef != null && breakDef?.Worker != null,
                "The Ideology-active fixture did not load the real IdeoChange mental-state boundary.");
            if (Find.IdeoManager?.classicMode == true)
            {
                PawnDiaryRimTestScope.Require(!breakDef.Worker.BreakCanOccur(pawn)
                        && BeliefMutationCache.Count == 0,
                    "Classic Ideology mode should make vanilla IdeoChange not applicable.");
                Log.Message(LogPrefix
                    + "real IdeoChange boundary: not applicable (classic mode). ");
                return;
            }

            scope.SpawnAsLiveColonist(pawn);
            Ideo beforeIdeology = EnsureCurrentIdeology();
            EnsureRegisteredAlternativeIdeology(beforeIdeology);
            DiaryInteractionGroupDef crisisGroup = InteractionGroups.ClassifyMentalState(stateDef);
            PawnDiaryRimTestScope.Require(crisisGroup?.defName == "beliefCrisis",
                "The exact IdeoChange group did not win before the generic mental-state fallback.");
            DiaryEventPromptDef crisisPrompt = DefDatabase<DiaryEventPromptDef>
                .GetNamedSilentFail("DiaryEventPrompt_IdeoChange");
            PawnDiaryRimTestScope.Require(crisisPrompt?.eventType == "IdeoChange",
                "The exact localized IdeoChange event prompt was not loaded.");
            BeliefMutationEventRule crisisRule = BeliefMutationEventSelector.RuleFor(
                BeliefMutationEventSourceTokens.MentalState,
                "IdeoChange",
                "beliefCrisis",
                ideologyActive: true,
                policyEnabled: DiaryBeliefPolicy.Snapshot().enabled,
                DiaryBeliefPolicy.Snapshot().mutationEventRules);
            PawnDiaryRimTestScope.Require(crisisRule != null
                    && crisisRule.subjectRole == BeliefMutationSubjectRoleTokens.Initiator
                    && crisisRule.evidenceGroupKey == "crisis",
                "The loaded belief policy did not expose the exact IdeoChange crisis rule.");

            // Starting at zero certainty guarantees that vanilla's fixed 0.5 reduction enters the
            // observed SetIdeo path. Reset setup evidence so only PreStart's real mutation remains.
            pawn.ideo.OffsetCertainty(-pawn.ideo.Certainty);
            BeliefMutationCache.Reset();
            float beforeCertainty = pawn.ideo.Certainty;
            int playLogCount = Find.PlayLog?.AllEntries?.Count ?? 0;

            DiaryEvent page;
            Rand.PushState(681247);
            try
            {
                page = scope.FireAndRequireEvent(
                    () =>
                    {
                        bool started = pawn.mindState.mentalStateHandler.TryStartMentalState(
                            stateDef,
                            "Pawn Diary RimTest IdeoChange boundary",
                            forced: true);
                        PawnDiaryRimTestScope.Require(started,
                            "Vanilla refused to start the forced IdeoChange mental state.");
                    },
                    "IdeoChange",
                    pawn,
                    null,
                    rejectOtherTestPawnEvents: true);
            }
            finally
            {
                Rand.PopState();
            }

            scope.RequireSoloRef(page, pawn);
            AssertGameContextContains(page, "mental_state=IdeoChange");
            AssertGameContextContains(page, BeliefMutationEventSelector.CrisisGameContextMarker);
            PawnDiaryRimTestScope.Require((Find.PlayLog?.AllEntries?.Count ?? 0) == playLogCount,
                "The IdeoChange boundary unexpectedly changed PlayLog.");

            BeliefMutationSnapshot mutation = BeliefMutationCache.PeekLatest(
                pawn.GetUniqueLoadID(), Find.TickManager.TicksGame, DiaryBeliefPolicy.Snapshot());
            PawnDiaryRimTestScope.Require(mutation != null
                    && mutation.conversionSucceeded == true
                    && mutation.ideologyChanged
                    && mutation.beforeIdeologyId == beforeIdeology.GetUniqueLoadID()
                    && mutation.afterIdeologyId == pawn.ideo.Ideo.GetUniqueLoadID()
                    && mutation.attemptedIdeologyId == mutation.afterIdeologyId
                    && Math.Abs(mutation.beforeCertainty - beforeCertainty) < 0.0001f
                    && Math.Abs(mutation.afterCertainty - pawn.ideo.Certainty) < 0.0001f
                    && mutation.causeTokens.Contains(BeliefMutationCauseTokens.ConversionAttempt)
                    && mutation.causeTokens.Contains(BeliefMutationCauseTokens.SetIdeology),
                "The real IdeoChange page did not retain its truthful coalesced mutation facts.");
            AssertContextContains(page, DiaryEvent.InitiatorRole,
                "previous ideoligion: " + mutation.beforeIdeologyName);
            AssertContextContains(page, DiaryEvent.InitiatorRole,
                "current ideoligion: " + mutation.afterIdeologyName);
            AssertContextContains(page, DiaryEvent.InitiatorRole, "conversion result: success");
            AssertContextContains(page, DiaryEvent.InitiatorRole, "certainty before:");
            AssertContextContains(page, DiaryEvent.InitiatorRole, "certainty after:");
            PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 1,
                "The existing solo crisis page consumed or duplicated its mutation cache row.");
        }

        /// <summary>
        /// Forces the DlcContext mutation projection seam to throw during a real vanilla method call.
        /// Vanilla certainty must still change, no partial cache state may survive, and the temporary
        /// patch is always removed in finally.
        /// </summary>
        [Test]
        public static void ProjectionFailurePreservesVanillaMutationAndLeavesNoPartialState()
        {
            if (!RequireActiveTracker()) return;
            EnsureCurrentIdeology();
            BeliefMutationCache.Reset();
            MethodInfo target = AccessTools.Method(typeof(DlcContext),
                nameof(DlcContext.TryCaptureBeliefMutationState),
                new[] { typeof(Pawn_IdeoTracker), typeof(BeliefMutationState).MakeByRefType() });
            PawnDiaryRimTestScope.Require(target != null,
                "Could not resolve the guarded mutation projection seam.");
            Harmony harmony = new Harmony("PawnDiary.RimTest.IdeologyMutationFailure."
                + Guid.NewGuid().ToString("N"));
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(
                    typeof(PawnDiaryIdeologyPhase2InfrastructureTests), nameof(ThrowMutationProjection)));
                float before = pawn.ideo.Certainty;
                float offset = before > 0.5f ? -0.05f : 0.05f;
                pawn.ideo.OffsetCertainty(offset);
                PawnDiaryRimTestScope.Require(Math.Abs(pawn.ideo.Certainty - before) > 0.0001f,
                    "A Pawn Diary projection failure prevented vanilla OffsetCertainty.");
                PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 0,
                    "A failed projection left a partial mutation cache row.");
            }
            finally
            {
                harmony.Unpatch(target, HarmonyPatchType.Prefix, harmony.Id);
            }
        }

        /// <summary>
        /// The exact Convert ability route is dropped before random sampling, while its real vanilla
        /// Convert_Success PlayLog row creates exactly one pair page through the existing Harmony hook.
        /// </summary>
        [Test]
        public static void ConvertAbilityAndDownstreamInteractionProduceOneCanonicalPage()
        {
            if (!RequireActiveTracker()) return;
            InteractionDef interaction = DefDatabase<InteractionDef>.GetNamedSilentFail("Convert_Success");
            PawnDiaryRimTestScope.Require(interaction != null,
                "The Ideology-active fixture could not load Convert_Success.");
            Ideo beforeIdeology = EnsureCurrentIdeology(otherPawn);
            Ideo attemptedIdeology = EnsureCurrentIdeology(pawn);
            if (ReferenceEquals(beforeIdeology, attemptedIdeology))
            {
                attemptedIdeology = FindOrCreateDifferentIdeology(beforeIdeology);
                pawn.ideo.SetIdeo(attemptedIdeology);
            }
            otherPawn.ideo.OffsetCertainty(-otherPawn.ideo.Certainty);
            BeliefMutationCache.Reset();
            Ability ability = new Ability(pawn, BuildSyntheticAbilityDef("Convert"));
            PlayLogEntry_Interaction entry = GeneratedSpeechPlayLog.CreateInteractionEntry(
                interaction, pawn, otherPawn);
            PawnDiaryRimTestScope.Require(entry != null,
                "Could not create the downstream Convert_Success PlayLog row.");
            scope.TrackPlayLogEntry(entry);

            DiaryEvent page = scope.FireAndRequireEvent(() =>
            {
                DiaryEvents.Submit(new AbilitySignal(
                    ability, new LocalTargetInfo(otherPawn), LocalTargetInfo.Invalid));
                bool converted = otherPawn.ideo.IdeoConversionAttempt(
                    0.05f, attemptedIdeology, applyCertaintyFactor: false);
                PawnDiaryRimTestScope.Require(converted,
                    "The deterministic zero-certainty fixture did not convert the target.");
                Find.PlayLog.Add(entry);
            }, "Convert_Success", pawn, otherPawn, rejectOtherTestPawnEvents: true);
            scope.RequirePairRefs(page, pawn, otherPawn);
            AssertGameContextContains(page, BeliefMutationEventSelector.ConversionGameContextMarker);
            AssertContextContains(page, DiaryEvent.InitiatorRole,
                "belief change subject: " + otherPawn.LabelShort);
            AssertContextContains(page, DiaryEvent.InitiatorRole, "conversion result: success");
            AssertContextContains(page, DiaryEvent.RecipientRole, "conversion result: success");
            AssertContextContains(page, DiaryEvent.RecipientRole, "certainty delta:");
            AssertContextContains(page, DiaryEvent.InitiatorRole, "mutation cause:");
            AssertContextContains(page, DiaryEvent.InitiatorRole,
                BeliefMutationCauseTokens.ConversionAttempt);
            PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 1,
                "Two POV contexts consumed or duplicated the one conversion mutation row.");

            Rand.PushState(847231);
            float expectedNext = Rand.Value;
            Rand.PopState();
            Rand.PushState(847231);
            try
            {
                scope.RequireNoNewEvent(() => DiaryEvents.Submit(new AbilitySignal(
                    ability, new LocalTargetInfo(otherPawn), LocalTargetInfo.Invalid)));
                float actualNext = Rand.Value;
                PawnDiaryRimTestScope.Require(Math.Abs(actualNext - expectedNext) < 0.000001f,
                    "The downstream-covered ability consumed Rand before it was dropped.");
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>
        /// Runs vanilla's random-conversion worker, including its normal certainty factor, before the
        /// exact ConvertIdeoAttempt PlayLog row and requires the observed bool result on both POVs.
        /// </summary>
        [Test]
        public static void ConvertIdeoAttemptWorkerPublishesKnownResultToItsExactPage()
        {
            if (!RequireActiveTracker()) return;
            InteractionDef interaction = DefDatabase<InteractionDef>.GetNamedSilentFail("ConvertIdeoAttempt");
            PawnDiaryRimTestScope.Require(interaction?.Worker != null,
                "The Ideology-active fixture could not load the ConvertIdeoAttempt worker.");

            // The vanilla worker requires an initiator with usable Social/ConversionPower and a spawned
            // recipient with a live interactions tracker. Random harness pawns may be incapable of Social,
            // while unspawned pawns deliberately omit dynamic components. Choose a bounded valid initiator,
            // then establish the same spawned-map prerequisites as a real social interaction.
            Pawn converter = pawn;
            Pawn target = otherPawn;
            if (!CanUseConversionPower(converter) && CanUseConversionPower(target))
            {
                converter = otherPawn;
                target = pawn;
            }
            for (int attempt = 0; !CanUseConversionPower(converter) && attempt < 8; attempt++)
                converter = scope.CreateAdultColonist();
            PawnDiaryRimTestScope.Require(CanUseConversionPower(converter),
                "Could not generate a bounded ConvertIdeoAttempt initiator with usable ConversionPower.");

            scope.SpawnAsLiveColonist(converter);
            scope.SpawnAsLiveColonist(target);
            PawnDiaryRimTestScope.Require(converter.interactions != null && target.interactions != null,
                "The spawned ConvertIdeoAttempt fixture did not initialize live interaction trackers.");

            Ideo beforeIdeology = EnsureCurrentIdeology(target);
            Ideo attemptedIdeology = EnsureCurrentIdeology(converter);
            if (ReferenceEquals(beforeIdeology, attemptedIdeology))
            {
                attemptedIdeology = FindOrCreateDifferentIdeology(beforeIdeology);
                converter.ideo.SetIdeo(attemptedIdeology);
            }
            target.ideo.OffsetCertainty(0.5f - target.ideo.Certainty);
            BeliefMutationCache.Reset();
            PlayLogEntry_Interaction entry = GeneratedSpeechPlayLog.CreateInteractionEntry(
                interaction, converter, target);
            PawnDiaryRimTestScope.Require(entry != null,
                "Could not create the ConvertIdeoAttempt PlayLog row.");
            scope.TrackPlayLogEntry(entry);

            bool? observedResult = null;
            DiaryEvent page = scope.FireAndRequireEvent(() =>
            {
                List<RulePackDef> extraSentencePacks = new List<RulePackDef>();
                string letterText;
                string letterLabel;
                LetterDef letterDef;
                LookTargets lookTargets;
                // The failure prose/social-fight branch samples Verse.Rand. Keep that real branch
                // deterministic without leaking its draw into the loaded colony or the next test.
                Rand.PushState(584921);
                try
                {
                    interaction.Worker.Interacted(converter, target, extraSentencePacks,
                        out letterText, out letterLabel, out letterDef, out lookTargets);
                }
                finally
                {
                    Rand.PopState();
                }

                BeliefMutationSnapshot mutation = BeliefMutationCache.PeekLatest(
                    target.GetUniqueLoadID(), Find.TickManager.TicksGame,
                    DiaryBeliefPolicy.Snapshot());
                PawnDiaryRimTestScope.Require(mutation?.conversionSucceeded.HasValue == true,
                    "Vanilla's ConvertIdeoAttempt worker did not leave an exact conversion result.");
                observedResult = mutation.conversionSucceeded;
                Find.PlayLog.Add(entry);
            }, "ConvertIdeoAttempt", converter, target, rejectOtherTestPawnEvents: true);

            scope.RequirePairRefs(page, converter, target);
            AssertGameContextContains(page, BeliefMutationEventSelector.ConversionGameContextMarker);
            AssertContextContains(page, DiaryEvent.InitiatorRole,
                "belief change subject: " + target.LabelShort);
            string expectedResult = observedResult == true ? "success" : "failure";
            AssertContextContains(page, DiaryEvent.InitiatorRole,
                "conversion result: " + expectedResult);
            AssertContextContains(page, DiaryEvent.RecipientRole,
                "conversion result: " + expectedResult);
            AssertContextContains(page, DiaryEvent.RecipientRole, "certainty delta:");
            AssertContextContains(page, DiaryEvent.InitiatorRole,
                BeliefMutationCauseTokens.ConversionAttempt);
        }

        /// <summary>
        /// The other shipped ability-owned interaction follows the same contract: Reassure's visible
        /// PlayLog interaction owns one pair page and its generic ability route emits nothing.
        /// </summary>
        [Test]
        public static void ReassureAbilityAndDownstreamInteractionProduceOneCanonicalPage()
        {
            if (!RequireActiveTracker()) return;
            InteractionDef interaction = DefDatabase<InteractionDef>.GetNamedSilentFail("Reassure");
            PawnDiaryRimTestScope.Require(interaction != null,
                "The Ideology-active fixture could not load Reassure.");
            EnsureCurrentIdeology(otherPawn);
            otherPawn.ideo.OffsetCertainty(0.4f - otherPawn.ideo.Certainty);
            BeliefMutationCache.Reset();
            Ability ability = new Ability(pawn, BuildSyntheticAbilityDef("Reassure"));
            PlayLogEntry_Interaction entry = GeneratedSpeechPlayLog.CreateInteractionEntry(
                interaction, pawn, otherPawn);
            PawnDiaryRimTestScope.Require(entry != null,
                "Could not create the downstream Reassure PlayLog row.");
            scope.TrackPlayLogEntry(entry);

            DiaryEvent page = scope.FireAndRequireEvent(() =>
            {
                DiaryEvents.Submit(new AbilitySignal(
                    ability, new LocalTargetInfo(otherPawn), LocalTargetInfo.Invalid));
                otherPawn.ideo.OffsetCertainty(0.1f);
                Find.PlayLog.Add(entry);
            }, "Reassure", pawn, otherPawn, rejectOtherTestPawnEvents: true);
            scope.RequirePairRefs(page, pawn, otherPawn);
            AssertContextContains(page, DiaryEvent.InitiatorRole, "certainty delta: +10%");
            AssertContextContains(page, DiaryEvent.RecipientRole, "certainty delta: +10%");
            AssertContextContains(page, DiaryEvent.InitiatorRole,
                BeliefMutationCauseTokens.CertaintyOffset);
            PawnDiaryRimTestScope.Require(
                page.BeliefContextForRole(DiaryEvent.RecipientRole)
                    .IndexOf("conversion result:", StringComparison.Ordinal) < 0,
                "Reassurance incorrectly claimed a conversion result.");
            PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 1,
                "Two reassurance POVs consumed or duplicated the one certainty mutation row.");
        }

        /// <summary>Real failed conversion mechanics enrich only the exact failure PlayLog row.</summary>
        [Test]
        public static void FailedConversionPlayLogKeepsActualFailureAndCertaintyLoss()
        {
            if (!RequireActiveTracker()) return;
            InteractionDef interaction = DefDatabase<InteractionDef>.GetNamedSilentFail("Convert_Failure");
            PawnDiaryRimTestScope.Require(interaction != null,
                "The Ideology-active fixture could not load Convert_Failure.");
            Ideo beforeIdeology = EnsureCurrentIdeology(otherPawn);
            Ideo attemptedIdeology = EnsureCurrentIdeology(pawn);
            if (ReferenceEquals(beforeIdeology, attemptedIdeology))
            {
                attemptedIdeology = FindOrCreateDifferentIdeology(beforeIdeology);
                pawn.ideo.SetIdeo(attemptedIdeology);
            }
            otherPawn.ideo.OffsetCertainty(1f - otherPawn.ideo.Certainty);
            BeliefMutationCache.Reset();
            PlayLogEntry_Interaction entry = GeneratedSpeechPlayLog.CreateInteractionEntry(
                interaction, pawn, otherPawn);
            PawnDiaryRimTestScope.Require(entry != null,
                "Could not create the downstream Convert_Failure PlayLog row.");
            scope.TrackPlayLogEntry(entry);

            DiaryEvent page = scope.FireAndRequireEvent(() =>
            {
                bool converted = otherPawn.ideo.IdeoConversionAttempt(
                    0.05f, attemptedIdeology, applyCertaintyFactor: false);
                PawnDiaryRimTestScope.Require(!converted,
                    "The full-certainty failure fixture unexpectedly converted the target.");
                Find.PlayLog.Add(entry);
            }, "Convert_Failure", pawn, otherPawn, rejectOtherTestPawnEvents: true);
            scope.RequirePairRefs(page, pawn, otherPawn);
            AssertGameContextContains(page, BeliefMutationEventSelector.ConversionGameContextMarker);
            AssertContextContains(page, DiaryEvent.InitiatorRole, "conversion result: failure");
            AssertContextContains(page, DiaryEvent.RecipientRole, "conversion result: failure");
            AssertContextContains(page, DiaryEvent.RecipientRole, "certainty delta: -5%");
            PawnDiaryRimTestScope.Require(
                page.BeliefContextForRole(DiaryEvent.RecipientRole)
                    .IndexOf("current ideoligion: " + attemptedIdeology.name,
                        StringComparison.Ordinal) < 0,
                "A failed conversion page claimed the attempted ideoligion became current.");
        }

        /// <summary>
        /// When only the converter is diary-eligible, the solo page still receives the exact target
        /// mechanics, explicitly labels whose belief changed, and carries the critical prompt marker.
        /// </summary>
        [Test]
        public static void SoloConversionPageLabelsTheIneligibleTargetMutation()
        {
            if (!RequireActiveTracker()) return;
            InteractionDef interaction = DefDatabase<InteractionDef>.GetNamedSilentFail("Convert_Failure");
            PawnDiaryRimTestScope.Require(interaction != null,
                "The Ideology-active fixture could not load Convert_Failure.");
            Pawn outsider = scope.CreateTrackedPawn(PawnKindDefOf.Colonist, faction: null);
            PawnDiaryRimTestScope.Require(DiaryGameComponent.IsDiaryEligible(pawn)
                    && !DiaryGameComponent.IsDiaryEligible(outsider),
                "The solo conversion fixture did not establish exactly one diary-eligible participant.");

            Ideo beforeIdeology = EnsureCurrentIdeology(outsider);
            Ideo attemptedIdeology = EnsureCurrentIdeology(pawn);
            if (ReferenceEquals(beforeIdeology, attemptedIdeology))
            {
                attemptedIdeology = FindOrCreateDifferentIdeology(beforeIdeology);
                pawn.ideo.SetIdeo(attemptedIdeology);
            }
            outsider.ideo.OffsetCertainty(1f - outsider.ideo.Certainty);
            BeliefMutationCache.Reset();
            PlayLogEntry_Interaction entry = GeneratedSpeechPlayLog.CreateInteractionEntry(
                interaction, pawn, outsider);
            PawnDiaryRimTestScope.Require(entry != null,
                "Could not create the solo downstream Convert_Failure PlayLog row.");
            scope.TrackPlayLogEntry(entry);

            DiaryEvent page = scope.FireAndRequireEvent(() =>
            {
                bool converted = outsider.ideo.IdeoConversionAttempt(
                    0.05f, attemptedIdeology, applyCertaintyFactor: false);
                PawnDiaryRimTestScope.Require(!converted,
                    "The full-certainty solo fixture unexpectedly converted the target.");
                Find.PlayLog.Add(entry);
            }, "Convert_Failure", pawn, null, rejectOtherTestPawnEvents: true);

            scope.RequireSoloRef(page, pawn);
            AssertGameContextContains(page, BeliefMutationEventSelector.ConversionGameContextMarker);
            AssertContextContains(page, DiaryEvent.InitiatorRole,
                "belief change subject: " + outsider.LabelShort);
            AssertContextContains(page, DiaryEvent.InitiatorRole, "conversion result: failure");
        }

        /// <summary>
        /// Ownership follows the effective downstream setting: disabling conversion releases the
        /// ordinary Convert ability route, so the player never loses both possible page owners.
        /// </summary>
        [Test]
        public static void DisabledDownstreamGroupReleasesGenericAbilityOwner()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix + "disabled downstream owner: not applicable (Ideology inactive). ");
                return;
            }
            PawnDiaryMod.Settings.SetGroupEnabled("conversion", false);
            Ability ability = new Ability(pawn, BuildSyntheticAbilityDef("Convert"));
            DiaryEvent page = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new AbilitySignal(
                    ability, new LocalTargetInfo(otherPawn), LocalTargetInfo.Invalid)),
                "Convert", pawn, null, rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(page, pawn);
        }

        /// <summary>
        /// Conversion rituals keep their generic start page because vanilla has no cancel signal from
        /// which a deferred canonical owner could be emitted safely.
        /// </summary>
        [Test]
        public static void ConversionRitualKeepsGenericStartOwner()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix + "conversion ritual start owner: not applicable (Ideology inactive). ");
                return;
            }
            Ability ability = new Ability(pawn, BuildSyntheticAbilityDef("ConversionRitual"));
            DiaryEvent page = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new AbilitySignal(
                    ability, new LocalTargetInfo(otherPawn), LocalTargetInfo.Invalid)),
                "ConversionRitual", pawn, null, rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(page, pawn);
        }

        private static void AssertNoDlcConvertAbilityKeepsOrdinaryRoute()
        {
            Ability ability = new Ability(pawn, BuildSyntheticAbilityDef("Convert"));
            DiaryEvent page = scope.FireAndRequireEvent(
                () => DiaryEvents.Submit(new AbilitySignal(
                    ability, new LocalTargetInfo(otherPawn), LocalTargetInfo.Invalid)),
                "Convert", pawn, null, rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(page, pawn);
        }

        private static void AssertOwnedPatchState(MethodBase target, bool expected)
        {
            Patches info = Harmony.GetPatchInfo(target);
            bool prefix = info?.Prefixes.Any(patch => patch.PatchMethod?.DeclaringType
                == typeof(DiaryIdeologyMutationPatches)) == true;
            bool postfix = info?.Postfixes.Any(patch => patch.PatchMethod?.DeclaringType
                == typeof(DiaryIdeologyMutationPatches)) == true;
            bool finalizer = info?.Finalizers.Any(patch => patch.PatchMethod?.DeclaringType
                == typeof(DiaryIdeologyMutationPatches)) == true;
            PawnDiaryRimTestScope.Require(prefix == expected && postfix == expected && finalizer == expected,
                "Production Ideology mutation patch ownership did not match DLC availability for "
                    + target.Name + ".");
        }

        private static void AssertLoadedMutationRule(
            BeliefPolicySnapshot policy,
            string interactionDefName,
            string downstreamGroupDefName,
            string evidenceGroupKey,
            string requiredCauseToken,
            string conversionResult,
            string certaintyDirection,
            string ideologyChange,
            bool requireAttemptedIdeology)
        {
            BeliefMutationEventRule rule = BeliefMutationEventSelector.RuleFor(
                BeliefMutationEventSourceTokens.Interaction,
                interactionDefName,
                downstreamGroupDefName,
                ideologyActive: true,
                policyEnabled: policy?.enabled == true,
                policy?.mutationEventRules);
            PawnDiaryRimTestScope.Require(rule != null
                    && rule.subjectRole == BeliefMutationSubjectRoleTokens.Recipient
                    && rule.evidenceGroupKey == evidenceGroupKey
                    && rule.requiredCauseToken == requiredCauseToken
                    && rule.conversionResult == conversionResult
                    && rule.certaintyDirection == certaintyDirection
                    && rule.ideologyChange == ideologyChange
                    && rule.requireAttemptedIdeology == requireAttemptedIdeology,
                "The loaded XML did not preserve the exact mechanical mutation rule for "
                    + interactionDefName + ".");
        }

        private static void AssertContextContains(DiaryEvent diaryEvent, string role, string expected)
        {
            string context = diaryEvent?.BeliefContextForRole(role) ?? string.Empty;
            PawnDiaryRimTestScope.Require(
                context.IndexOf(expected, StringComparison.Ordinal) >= 0,
                "The " + role + " belief context did not contain '" + expected + "'. Context: "
                    + context);
        }

        private static void AssertGameContextContains(DiaryEvent diaryEvent, string expected)
        {
            string context = diaryEvent?.gameContext ?? string.Empty;
            PawnDiaryRimTestScope.Require(
                context.IndexOf(expected, StringComparison.Ordinal) >= 0,
                "The interaction game context did not contain '" + expected + "'. Context: "
                    + context);
        }

        private static bool RequireActiveTracker()
        {
            if (ModsConfig.IdeologyActive)
            {
                if (Find.IdeoManager?.classicMode == true)
                {
                    Log.Message(LogPrefix + "tracker mutation: not applicable (classic mode). ");
                    return false;
                }
                PawnDiaryRimTestScope.Require(pawn?.ideo != null,
                    "An Ideology-active fixture pawn did not have a tracker.");
                return true;
            }
            PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 0,
                "The mutation cache must remain empty without Ideology.");
            Log.Message(LogPrefix + "tracker mutation: not applicable (Ideology inactive). ");
            return false;
        }

        private static bool CanUseConversionPower(Pawn candidate)
        {
            return candidate != null && StatDefOf.ConversionPower?.Worker != null
                && !StatDefOf.ConversionPower.Worker.IsDisabledFor(candidate);
        }

        private static Ideo EnsureCurrentIdeology()
        {
            return EnsureCurrentIdeology(pawn);
        }

        private static Ideo EnsureCurrentIdeology(Pawn candidate)
        {
            PawnDiaryRimTestScope.Require(candidate?.ideo != null,
                "The Ideology-active fixture pawn did not expose a tracker.");
            Ideo current = candidate.ideo.Ideo;
            if (current != null) return current;
            Ideo loaded = Find.IdeoManager?.IdeosListForReading?.FirstOrDefault(value => value != null);
            PawnDiaryRimTestScope.Require(loaded != null,
                "The Ideology-active fixture did not expose an ideoligion.");
            candidate.ideo.SetIdeo(loaded);
            BeliefMutationCache.Reset();
            return loaded;
        }

        private static Ideo FindOrCreateDifferentIdeology(Ideo current)
        {
            Ideo loaded = Find.IdeoManager?.IdeosListForReading?
                .FirstOrDefault(value => value != null && !ReferenceEquals(value, current));
            if (loaded != null) return loaded;

            Ideo generated = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms
            {
                forFaction = Faction.OfPlayer.def,
                fixedIdeo = true
            });
            PawnDiaryRimTestScope.Require(generated != null && !ReferenceEquals(generated, current),
                "RimWorld did not provide a second loaded or disposable ideoligion fixture.");
            return generated;
        }

        /// <summary>
        /// Ensures vanilla IdeoChange's weighted manager lookup has a different candidate. A generated
        /// fallback is registered only for this test and is removed after restoring the disposable pawn.
        /// </summary>
        private static Ideo EnsureRegisteredAlternativeIdeology(Ideo current)
        {
            IdeoManager manager = Find.IdeoManager;
            PawnDiaryRimTestScope.Require(manager != null,
                "The Ideology-active fixture did not expose an IdeoManager.");
            Ideo loaded = manager.IdeosListForReading
                .FirstOrDefault(value => value != null && !ReferenceEquals(value, current));
            if (loaded != null) return loaded;

            Ideo generated = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms
            {
                forFaction = Faction.OfPlayer.def,
                fixedIdeo = true
            });
            PawnDiaryRimTestScope.Require(generated != null && manager.Add(generated),
                "Could not register a disposable alternative ideoligion for IdeoChange.");
            scope.RegisterCleanup(() =>
            {
                if (pawn?.ideo?.Ideo == generated && current != null)
                    pawn.ideo.SetIdeo(current);
                BeliefMutationCache.Reset();
                if (manager.IdeosListForReading.Contains(generated)) manager.Remove(generated);
                BeliefMutationCache.Reset();
            });
            return generated;
        }

        private static AbilityDef BuildSyntheticAbilityDef(string defName)
        {
            return new AbilityDef
            {
                defName = defName,
                label = "ideology infrastructure fixture",
                abilityClass = typeof(Ability),
                comps = new List<AbilityCompProperties>(),
                cooldownTicksRange = new IntRange(2500, 2500),
                hostile = false,
                verbProperties = new VerbProperties
                {
                    verbClass = typeof(Verb_CastAbility),
                    isPrimary = true
                }
            };
        }

        private static bool ThrowMutationProjection()
        {
            throw new InvalidOperationException("Synthetic Ideology mutation projection failure.");
        }
    }
}

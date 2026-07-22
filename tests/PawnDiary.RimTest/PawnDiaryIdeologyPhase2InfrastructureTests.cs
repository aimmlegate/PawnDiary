// Loaded-game fixtures for Ideology Phase 2 mutation infrastructure, exact interaction consumers,
// exact Counsel mood outcomes, and the exact IdeoChange crisis consumer on the existing solo mental-state page. The real boundary
// also proves vanilla's nested silent Wander_OwnRoom/Wander_Sad transition does not become a second
// diary page.
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
using PawnDiary.Capture;
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
        // Fixture expectations intentionally live in tests. Production reads these exact identities
        // and mood tokens from DiaryBeliefPolicyDef.xml instead of maintaining a second C# catalog.
        private const string CounselSuccessDefName = "Counsel_Success";
        private const string CounselFailureDefName = "Counsel_Failure";
        private const string CounselSuccessContext =
            "counsel_result=success; counsel_mood_effect=relief_or_boost";
        private const string CounselFailureContext =
            "counsel_result=failure; counsel_mood_effect=penalty";
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
                "conversion", "heartfelt", "abilityUsed", "beliefCrisis", "counsel");
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

            // A third-party base-game MentalStateDef can legally reuse a DLC DefName while the DLC is
            // absent. Live classification must skip the dormant beliefCrisis row and retain the ordinary
            // mental-break fallback instead of silently losing that modded page.
            MentalStateDef syntheticIdeoChange = new MentalStateDef
            {
                defName = MentalStateEventData.IdeoChangeDefName
            };
            string expectedMentalStateGroup = active ? "beliefCrisis" : "mentalbreak";
            PawnDiaryRimTestScope.Require(
                InteractionGroups.ClassifyMentalState(syntheticIdeoChange)?.defName
                    == expectedMentalStateGroup,
                "Live IdeoChange classification did not respect the current DLC availability boundary.");

            BeliefMutationState state;
            bool captured = DlcContext.TryCaptureBeliefMutationState(pawn.ideo, out state);
            BeliefPolicySnapshot ownership = DiaryBeliefPolicy.Snapshot();
            DiaryInteractionGroupDef counselGroup = InteractionGroups.ByKey(CounselEventPolicy.GroupDefName);
            PawnDiaryRimTestScope.Require(counselGroup != null
                    && counselGroup.MissingRequiredPackage() == !active
                    && counselGroup.tones != null && counselGroup.tones.Count >= 2
                    && PawnDiaryMod.Settings.IsGroupEnabled(CounselEventPolicy.GroupDefName) == active,
                "The exact Counsel group availability/tone policy did not match the loaded profile.");
            InteractionDef exactCounsel = new InteractionDef { defName = CounselSuccessDefName };
            InteractionDef caseVariantCounsel = new InteractionDef { defName = "counsel_success" };
            // Loaded Defs receive this hash during RimWorld's normal resolve lifecycle. Synthetic Defs
            // must do the same before entering the production Def-keyed classification memo; otherwise
            // Verse considers every unresolved InteractionDef (hash 0) equal and the second assertion
            // merely reuses the first synthetic Def's cached result.
            exactCounsel.ResolveDefNameHash();
            caseVariantCounsel.ResolveDefNameHash();
            PawnDiaryRimTestScope.Require(
                InteractionGroups.Classify(exactCounsel)?.defName
                    == (active ? CounselEventPolicy.GroupDefName : "other")
                && InteractionGroups.Classify(caseVariantCounsel)?.defName == "other",
                "Live Counsel classification did not preserve availability fallback and ordinal identity.");
            AssertLoadedCounselRule(ownership, CounselSuccessDefName, "success", "relief_or_boost");
            AssertLoadedCounselRule(ownership, CounselFailureDefName, "failure", "penalty");
            string expectedCounselOwner = active ? CounselEventPolicy.GroupDefName : string.Empty;
            PawnDiaryRimTestScope.Require(
                BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Ability, "Counsel", active, ownership.enabled,
                    ownership.canonicalEventOwnershipRules) == expectedCounselOwner
                && BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Thought, "Counselled", active, ownership.enabled,
                    ownership.canonicalEventOwnershipRules) == expectedCounselOwner
                && BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Thought, "Counselled_MoodBoost", active, ownership.enabled,
                    ownership.canonicalEventOwnershipRules) == expectedCounselOwner
                && BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Thought, "CounselFailed", active, ownership.enabled,
                    ownership.canonicalEventOwnershipRules) == expectedCounselOwner,
                "The loaded exact Counsel ability/thought ownership did not match DLC availability.");
            if (!active)
            {
                PawnDiaryRimTestScope.Require(!captured && state == null && BeliefMutationCache.Count == 0,
                    "The mutation adapter/cache must be inert without Ideology.");
                PawnDiaryRimTestScope.Require(BeliefMutationEvidenceAdapter.ForInteraction(
                        "Reassure", "heartfelt", pawn.GetUniqueLoadID(), otherPawn.GetUniqueLoadID(),
                        Find.TickManager.TicksGame) == null,
                    "The interaction evidence adapter must be inert without Ideology.");
                PawnDiaryRimTestScope.Require(BeliefMutationEvidenceAdapter.ForCounselInteraction(
                        CounselSuccessDefName, CounselEventPolicy.GroupDefName) == null,
                    "The Counsel context adapter must be inert without Ideology.");
                InteractionDef syntheticCounsel = new InteractionDef
                {
                    defName = CounselSuccessDefName,
                    label = "synthetic inactive counsel"
                };
                DiaryEvent ordinaryFallback = scope.FireAndRequireEvent(
                    () => DiaryEvents.Submit(new InteractionSignal(
                        pawn, otherPawn, syntheticCounsel, string.Empty, string.Empty, 0)),
                    CounselSuccessDefName, pawn, otherPawn, rejectOtherTestPawnEvents: true);
                scope.RequirePairRefs(ordinaryFallback, pawn, otherPawn);
                PawnDiaryRimTestScope.Require(
                    (ordinaryFallback.gameContext ?? string.Empty).IndexOf(
                        "counsel_result=", StringComparison.Ordinal) < 0,
                    "A DLC-off exact-name third-party interaction received dormant Counsel context.");
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

            // Simulate an upgraded save: old conversion was explicitly disabled and the new Counsel
            // key is absent. The old choice wins until the player explicitly enables Counsel, whose
            // default-equal override must then remain stored and effective.
            PawnDiaryMod.Settings.groupEnabled.Remove(CounselEventPolicy.GroupDefName);
            PawnDiaryMod.Settings.SetGroupEnabled("conversion", false);
            PawnDiaryRimTestScope.Require(
                !PawnDiaryMod.Settings.IsGroupEnabled(CounselEventPolicy.GroupDefName),
                "An upgraded explicit conversion=false save unexpectedly enabled new Counsel pages.");
            PawnDiaryMod.Settings.SetGroupEnabled(CounselEventPolicy.GroupDefName, true);
            MethodInfo normalizeSettings = AccessTools.Method(
                typeof(PawnDiarySettings), "NormalizeGroupEnabledOverrides");
            PawnDiaryRimTestScope.Require(normalizeSettings != null,
                "The loaded settings fixture could not resolve post-Scribe override normalization.");
            normalizeSettings.Invoke(PawnDiaryMod.Settings, null);
            PawnDiaryRimTestScope.Require(
                PawnDiaryMod.Settings.IsGroupEnabled(CounselEventPolicy.GroupDefName)
                    && PawnDiaryMod.Settings.HasGroupEnabledOverride(CounselEventPolicy.GroupDefName),
                "An explicit Counsel enable did not survive normalization against inherited conversion=false intent.");
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
        /// conversion attempt and then silently transitions to a wander state before Pawn Diary's outer
        /// successful-start postfix, so the existing crisis page must freeze the exact mutation while
        /// suppressing only that nested implementation-detail page.
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
            scope.RequireNoEventForTestPawns(MentalStateEventData.WanderOwnRoomDefName);
            scope.RequireNoEventForTestPawns(MentalStateEventData.WanderSadDefName);
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
        /// Forces vanilla's real IdeoChange attempt to fail by starting above its fixed 50% certainty
        /// reduction. The one crisis page must report challenged convictions and falling certainty,
        /// never a conversion or a nested wander page.
        /// </summary>
        [Test]
        public static void RealIdeoChangeFailureKeepsOneTruthfulCrisisPage()
        {
            if (!RequireActiveTracker()) return;
            MentalStateDef stateDef = DefDatabase<MentalStateDef>.GetNamedSilentFail("IdeoChange");
            MentalBreakDef breakDef = DefDatabase<MentalBreakDef>.GetNamedSilentFail("IdeoChange");
            PawnDiaryRimTestScope.Require(stateDef != null && breakDef?.Worker != null,
                "The Ideology-active fixture did not load the real IdeoChange mental-state boundary.");

            scope.SpawnAsLiveColonist(pawn);
            Ideo beforeIdeology = EnsureCurrentIdeology();
            EnsureRegisteredAlternativeIdeology(beforeIdeology);
            for (int attempt = 0; pawn.ideo.Certainty <= 0.5f && attempt < 4; attempt++)
                pawn.ideo.OffsetCertainty(1f);
            PawnDiaryRimTestScope.Require(pawn.ideo.Certainty > 0.5f,
                "The fixture could not raise certainty above vanilla IdeoChange's fixed reduction.");
            BeliefMutationCache.Reset();
            float beforeCertainty = pawn.ideo.Certainty;
            int playLogCount = Find.PlayLog?.AllEntries?.Count ?? 0;

            DiaryEvent page;
            Rand.PushState(913547);
            try
            {
                page = scope.FireAndRequireEvent(
                    () =>
                    {
                        bool started = pawn.mindState.mentalStateHandler.TryStartMentalState(
                            stateDef,
                            "Pawn Diary RimTest IdeoChange failure boundary",
                            forced: true);
                        PawnDiaryRimTestScope.Require(started,
                            "Vanilla refused to start the forced failed IdeoChange mental state.");
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
            scope.RequireNoEventForTestPawns(MentalStateEventData.WanderOwnRoomDefName);
            scope.RequireNoEventForTestPawns(MentalStateEventData.WanderSadDefName);
            PawnDiaryRimTestScope.Require((Find.PlayLog?.AllEntries?.Count ?? 0) == playLogCount,
                "The failed IdeoChange boundary unexpectedly changed PlayLog.");

            BeliefMutationSnapshot mutation = BeliefMutationCache.PeekLatest(
                pawn.GetUniqueLoadID(), Find.TickManager.TicksGame, DiaryBeliefPolicy.Snapshot());
            PawnDiaryRimTestScope.Require(mutation != null
                    && mutation.conversionSucceeded == false
                    && !mutation.ideologyChanged
                    && mutation.beforeIdeologyId == beforeIdeology.GetUniqueLoadID()
                    && mutation.afterIdeologyId == mutation.beforeIdeologyId
                    && mutation.attemptedIdeologyId.Length > 0
                    && mutation.attemptedIdeologyId != mutation.beforeIdeologyId
                    && Math.Abs(mutation.beforeCertainty - beforeCertainty) < 0.0001f
                    && Math.Abs(mutation.afterCertainty - (beforeCertainty - 0.5f)) < 0.0001f
                    && mutation.causeTokens.Contains(BeliefMutationCauseTokens.ConversionAttempt)
                    && !mutation.causeTokens.Contains(BeliefMutationCauseTokens.SetIdeology),
                "The real failed IdeoChange page did not retain its unchanged-identity mutation facts.");
            AssertGameContextContains(page, BeliefMutationEventSelector.CrisisGameContextMarker);
            AssertContextContains(page, DiaryEvent.InitiatorRole, "conversion result: failure");
            AssertContextContains(page, DiaryEvent.InitiatorRole, "certainty delta: -50%");
            AssertContextContains(page, DiaryEvent.InitiatorRole,
                "current ideoligion: " + mutation.afterIdeologyName);
            PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 1,
                "The failed crisis page consumed or duplicated its mutation cache row.");
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
        /// Mutation evidence is optional enrichment. A selector failure before event construction
        /// must return null evidence and preserve the already-authorized ordinary mental-state page.
        /// </summary>
        [Test]
        public static void EvidenceSelectionFailureKeepsOrdinaryCrisisPage()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix
                    + "evidence failure isolation: not applicable (Ideology inactive). ");
                return;
            }

            MentalStateDef stateDef = DefDatabase<MentalStateDef>.GetNamedSilentFail("IdeoChange");
            PawnDiaryRimTestScope.Require(stateDef != null,
                "The Ideology-active fixture did not load IdeoChange for failure isolation.");
            EnsureCurrentIdeology();
            BeliefMutationCache.Reset();
            MethodInfo target = AccessTools.Method(typeof(BeliefMutationEventSelector),
                nameof(BeliefMutationEventSelector.SelectCrisisOrCurrent), new[]
                {
                    typeof(BeliefMutationEventRule),
                    typeof(string),
                    typeof(int),
                    typeof(int),
                    typeof(BeliefMutationSnapshot),
                    typeof(string)
                });
            PawnDiaryRimTestScope.Require(target != null,
                "Could not resolve the crisis-evidence selector seam for failure isolation.");
            Harmony harmony = new Harmony("PawnDiary.RimTest.IdeologyEvidenceFailure."
                + Guid.NewGuid().ToString("N"));
            harmony.Patch(target, prefix: new HarmonyMethod(
                typeof(PawnDiaryIdeologyPhase2InfrastructureTests),
                nameof(ThrowBeliefEvidenceSelection)));
            try
            {
                DiaryEvent page = scope.FireAndRequireEvent(
                    () => DiaryEvents.Submit(new MentalStateSignal(
                        pawn, stateDef, null, "synthetic optional-enrichment failure")),
                    "IdeoChange", pawn, null, rejectOtherTestPawnEvents: true);
                scope.RequireSoloRef(page, pawn);
                AssertGameContextContains(page, "mental_state=IdeoChange");
                PawnDiaryRimTestScope.Require(
                    string.IsNullOrEmpty(page.BeliefContextForRole(DiaryEvent.InitiatorRole))
                        && (page.gameContext ?? string.Empty).IndexOf(
                            BeliefMutationEventSelector.CrisisGameContextMarker,
                            StringComparison.Ordinal) < 0,
                    "A failed mutation-evidence selection left partial crisis enrichment on the page.");
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
            scope.SpawnAsLiveColonist(pawn);
            scope.SpawnAsLiveColonist(otherPawn);
            Ideo beforeIdeology = EnsureCurrentIdeology(otherPawn);
            Ideo attemptedIdeology = EnsureCurrentIdeology(pawn);
            if (ReferenceEquals(beforeIdeology, attemptedIdeology))
            {
                attemptedIdeology = FindOrCreateDifferentIdeology(beforeIdeology);
                pawn.ideo.SetIdeo(attemptedIdeology);
            }
            otherPawn.ideo.OffsetCertainty(-otherPawn.ideo.Certainty);
            BeliefMutationCache.Reset();
            Ability ability = BuildExecutableVanillaAbility(
                pawn, "Convert", typeof(CompAbilityEffect_Convert));
            List<LogEntry> addedPlayLogEntries = null;

            DiaryEvent page = scope.FireAndRequireEvent(
                () => addedPlayLogEntries = ActivateAndTrackPlayLog(ability, otherPawn),
                "Convert_Success", pawn, otherPawn, rejectOtherTestPawnEvents: true);
            PawnDiaryRimTestScope.Require(ReferenceEquals(otherPawn.ideo.Ideo, attemptedIdeology)
                    && addedPlayLogEntries?.Count == 1,
                "The real Convert ability did not convert the target and add one downstream PlayLog row.");
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
            Pawn caster = pawn;
            for (int attempt = 0;
                 caster.GetStatValue(StatDefOf.NegotiationAbility) <= 0.0001f && attempt < 8;
                 attempt++)
            {
                caster = scope.CreateAdultColonist();
            }
            PawnDiaryRimTestScope.Require(
                caster.GetStatValue(StatDefOf.NegotiationAbility) > 0.0001f,
                "Could not generate a bounded Reassure caster with positive NegotiationAbility.");
            Ideo sharedIdeology = EnsureCurrentIdeology(caster);
            EnsureCurrentIdeology(otherPawn);
            if (!ReferenceEquals(otherPawn.ideo.Ideo, sharedIdeology))
                otherPawn.ideo.SetIdeo(sharedIdeology);
            scope.SpawnAsLiveColonist(caster);
            scope.SpawnAsLiveColonist(otherPawn);
            otherPawn.ideo.OffsetCertainty(-1f);
            BeliefMutationCache.Reset();
            float beforeCertainty = otherPawn.ideo.Certainty;
            Ability ability = BuildExecutableVanillaAbility(
                caster, "Reassure", typeof(CompAbilityEffect_Reassure));
            List<LogEntry> addedPlayLogEntries = null;

            DiaryEvent page = scope.FireAndRequireEvent(
                () => addedPlayLogEntries = ActivateAndTrackPlayLog(ability, otherPawn),
                "Reassure", caster, otherPawn, rejectOtherTestPawnEvents: true);
            float certaintyDelta = otherPawn.ideo.Certainty - beforeCertainty;
            PawnDiaryRimTestScope.Require(certaintyDelta > 0.0001f
                    && addedPlayLogEntries?.Count == 1,
                "The real Reassure ability did not increase certainty and add one PlayLog row.");
            scope.RequirePairRefs(page, caster, otherPawn);
            string expectedDelta = "certainty delta: " + FormatSignedPercent(certaintyDelta);
            AssertContextContains(page, DiaryEvent.InitiatorRole, expectedDelta);
            AssertContextContains(page, DiaryEvent.RecipientRole, expectedDelta);
            AssertContextContains(page, DiaryEvent.InitiatorRole,
                BeliefMutationCauseTokens.CertaintyOffset);
            PawnDiaryRimTestScope.Require(
                page.BeliefContextForRole(DiaryEvent.RecipientRole)
                    .IndexOf("conversion result:", StringComparison.Ordinal) < 0,
                "Reassurance incorrectly claimed a conversion result.");
            PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 1,
                "Two reassurance POVs consumed or duplicated the one certainty mutation row.");
        }

        /// <summary>
        /// Executes vanilla Counsel's real no-negative-thought success branch. The fixed mood boost is
        /// applied first and the exact PlayLog row remains the sole pair page; neither the thought nor
        /// the generic ability callback may create a second owner or consume RNG before being dropped.
        /// </summary>
        [Test]
        public static void CounselSuccessMoodBoostUsesOneDownstreamPageAndPreservesCoveredRng()
        {
            if (!RequireCounselActive()) return;
            Pawn caster = PrepareCounselCaster(socialLevel: 20);
            PrepareCounselPair(caster, otherPawn);
            Ability ability = BuildExecutableVanillaAbility(
                caster, "Counsel", typeof(CompAbilityEffect_Counsel));
            CompAbilityEffect_Counsel effect = ability.CompOfType<CompAbilityEffect_Counsel>();
            PawnDiaryRimTestScope.Require(effect != null,
                "The executable Counsel fixture did not instantiate CompAbilityEffect_Counsel.");
            RemoveCounselEligibleNegativeMemories(otherPawn, effect.Props.minMoodOffset);
            AssertCoveredCounselAbilityPreservesRand(ability);
            float chance = effect.ChanceForPawn(otherPawn);
            int seed = FindCounselSeed(chance, expectedSuccess: true);
            float beforeCertainty = otherPawn.ideo.Certainty;
            Ideo beforeIdeology = otherPawn.ideo.Ideo;
            List<LogEntry> addedPlayLogEntries = null;

            DiaryEvent page;
            Rand.PushState(seed);
            try
            {
                page = scope.FireAndRequireEvent(
                    () => addedPlayLogEntries = ActivateAndTrackPlayLog(ability, otherPawn),
                    CounselSuccessDefName, caster, otherPawn,
                    rejectOtherTestPawnEvents: true);
            }
            finally
            {
                Rand.PopState();
            }

            Thought_Memory successMemory = FindCounselMemory(otherPawn, "Counselled_MoodBoost");
            PawnDiaryRimTestScope.Require(addedPlayLogEntries?.Count == 1
                    && successMemory != null && successMemory.MoodOffset() > 0f
                    && !HasAnyCounselMemory(otherPawn, "Counselled")
                    && !HasAnyCounselMemory(otherPawn, "CounselFailed"),
                "Vanilla Counsel no-negative success did not select only its fixed mood-boost branch.");
            AssertCounselPage(page, caster, otherPawn, beforeIdeology, beforeCertainty,
                CounselSuccessDefName, CounselSuccessContext);
        }

        /// <summary>
        /// Executes vanilla Counsel's other success subbranch against a deterministic eligible negative
        /// memory and proves the compensating Counselled thought, not the generic mood boost, was used.
        /// </summary>
        [Test]
        public static void CounselSuccessRelievesEligibleNegativeMemoryOnOneDownstreamPage()
        {
            if (!RequireCounselActive()) return;
            Pawn caster = PrepareCounselCaster(socialLevel: 20);
            PrepareCounselPair(caster, otherPawn);
            Ability ability = BuildExecutableVanillaAbility(
                caster, "Counsel", typeof(CompAbilityEffect_Counsel));
            CompAbilityEffect_Counsel effect = ability.CompOfType<CompAbilityEffect_Counsel>();
            PawnDiaryRimTestScope.Require(effect != null,
                "The executable Counsel relief fixture did not instantiate CompAbilityEffect_Counsel.");
            RemoveCounselEligibleNegativeMemories(otherPawn, effect.Props.minMoodOffset);
            Thought_Memory negative = AddSyntheticCounselTargetMemory(
                otherPawn, effect.Props.minMoodOffset - 1f);
            float expectedRelief = -negative.MoodOffset();
            float chance = effect.ChanceForPawn(otherPawn);
            int seed = FindCounselSeed(chance, expectedSuccess: true);
            float beforeCertainty = otherPawn.ideo.Certainty;
            Ideo beforeIdeology = otherPawn.ideo.Ideo;
            List<LogEntry> addedPlayLogEntries = null;

            DiaryEvent page;
            Rand.PushState(seed);
            try
            {
                page = scope.FireAndRequireEvent(
                    () => addedPlayLogEntries = ActivateAndTrackPlayLog(ability, otherPawn),
                    CounselSuccessDefName, caster, otherPawn,
                    rejectOtherTestPawnEvents: true);
            }
            finally
            {
                Rand.PopState();
            }

            Thought_Memory relief = FindCounselMemory(otherPawn, "Counselled");
            PawnDiaryRimTestScope.Require(addedPlayLogEntries?.Count == 1
                    && relief != null
                    && Math.Abs(relief.MoodOffset() - expectedRelief) < 0.0001f
                    && !HasAnyCounselMemory(otherPawn, "Counselled_MoodBoost", "CounselFailed"),
                "Vanilla Counsel eligible-negative success did not select only its relief branch.");
            AssertCounselPage(page, caster, otherPawn, beforeIdeology, beforeCertainty,
                CounselSuccessDefName, CounselSuccessContext);
        }

        /// <summary>
        /// Executes vanilla Counsel's real failure branch and proves the short negative mood memory is
        /// reported truthfully without any fabricated certainty loss, conversion result, or duplicate page.
        /// </summary>
        [Test]
        public static void CounselFailureUsesOneDownstreamPageAndNoBeliefMutation()
        {
            if (!RequireCounselActive()) return;
            Pawn caster = PrepareCounselCaster(socialLevel: 0);
            PrepareCounselPair(caster, otherPawn);
            Ability ability = BuildExecutableVanillaAbility(
                caster, "Counsel", typeof(CompAbilityEffect_Counsel));
            CompAbilityEffect_Counsel effect = ability.CompOfType<CompAbilityEffect_Counsel>();
            PawnDiaryRimTestScope.Require(effect != null,
                "The executable Counsel fixture did not instantiate CompAbilityEffect_Counsel.");
            float chance = effect.ChanceForPawn(otherPawn);
            int seed = FindCounselSeed(chance, expectedSuccess: false);
            float beforeCertainty = otherPawn.ideo.Certainty;
            Ideo beforeIdeology = otherPawn.ideo.Ideo;
            List<LogEntry> addedPlayLogEntries = null;

            DiaryEvent page;
            Rand.PushState(seed);
            try
            {
                page = scope.FireAndRequireEvent(
                    () => addedPlayLogEntries = ActivateAndTrackPlayLog(ability, otherPawn),
                    CounselFailureDefName, caster, otherPawn,
                    rejectOtherTestPawnEvents: true);
            }
            finally
            {
                Rand.PopState();
            }

            Thought_Memory failureMemory = FindCounselMemory(otherPawn, "CounselFailed");
            PawnDiaryRimTestScope.Require(addedPlayLogEntries?.Count == 1
                    && failureMemory != null && failureMemory.MoodOffset() < 0f
                    && !HasAnyCounselMemory(otherPawn, "Counselled", "Counselled_MoodBoost"),
                "Vanilla Counsel failure did not add exactly its downstream PlayLog row and failure memory.");
            AssertCounselPage(page, caster, otherPawn, beforeIdeology, beforeCertainty,
                CounselFailureDefName, CounselFailureContext);
        }

        /// <summary>
        /// Optional Counsel context policy may fail without losing the canonical PlayLog page. This
        /// patches only the pure rule seam; the existing interaction route and pair ownership stay real.
        /// </summary>
        [Test]
        public static void CounselContextFailureKeepsOrdinaryCanonicalPage()
        {
            if (!RequireCounselActive()) return;
            PrepareCounselPair(pawn, otherPawn);
            InteractionDef interaction = DefDatabase<InteractionDef>.GetNamedSilentFail(
                CounselSuccessDefName);
            PawnDiaryRimTestScope.Require(interaction != null,
                "The Ideology-active fixture could not load Counsel_Success.");
            PlayLogEntry_Interaction entry = GeneratedSpeechPlayLog.CreateInteractionEntry(
                interaction, pawn, otherPawn);
            PawnDiaryRimTestScope.Require(entry != null,
                "Could not create the exact Counsel_Success PlayLog row for failure isolation.");
            scope.TrackPlayLogEntry(entry);

            MethodInfo target = AccessTools.Method(typeof(CounselEventPolicy),
                nameof(CounselEventPolicy.RuleFor), new[]
                {
                    typeof(string), typeof(string), typeof(bool), typeof(bool),
                    typeof(IReadOnlyList<CounselEventRule>)
                });
            PawnDiaryRimTestScope.Require(target != null,
                "Could not resolve the Counsel context-rule seam for failure isolation.");
            Harmony harmony = new Harmony("PawnDiary.RimTest.CounselContextFailure."
                + Guid.NewGuid().ToString("N"));
            harmony.Patch(target, prefix: new HarmonyMethod(
                typeof(PawnDiaryIdeologyPhase2InfrastructureTests),
                nameof(ThrowCounselContextSelection)));
            try
            {
                DiaryEvent page = scope.FireAndRequireEvent(
                    () => Find.PlayLog.Add(entry), CounselSuccessDefName,
                    pawn, otherPawn, rejectOtherTestPawnEvents: true);
                scope.RequirePairRefs(page, pawn, otherPawn);
                PawnDiaryRimTestScope.Require(
                    (page.gameContext ?? string.Empty).IndexOf(
                        "counsel_result=", StringComparison.Ordinal) < 0
                    && string.IsNullOrEmpty(page.BeliefContextForRole(DiaryEvent.InitiatorRole))
                    && string.IsNullOrEmpty(page.BeliefContextForRole(DiaryEvent.RecipientRole)),
                    "Failed optional Counsel context policy left partial enrichment on the ordinary page.");
            }
            finally
            {
                harmony.Unpatch(target, HarmonyPatchType.Prefix, harmony.Id);
            }
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

        /// <summary>Requires one exact XML-owned Counsel outcome row and its context tokens.</summary>
        private static void AssertLoadedCounselRule(
            BeliefPolicySnapshot policy,
            string interactionDefName,
            string resultToken,
            string moodEffectToken)
        {
            CounselEventRule rule = CounselEventPolicy.RuleFor(
                interactionDefName,
                CounselEventPolicy.GroupDefName,
                ideologyActive: true,
                policyEnabled: policy?.enabled == true,
                policy?.counselEventRules);
            PawnDiaryRimTestScope.Require(rule != null
                    && rule.resultToken == resultToken
                    && rule.moodEffectToken == moodEffectToken,
                "The loaded XML did not preserve the exact Counsel context rule for "
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

        /// <summary>
        /// Counsel itself remains usable in classic Ideology mode because it changes mood, not the
        /// mutable certainty model. Therefore an active profile must run the fixture or fail its exact
        /// prerequisites; only a genuinely inactive DLC profile is not applicable.
        /// </summary>
        private static bool RequireCounselActive()
        {
            if (!ModsConfig.IdeologyActive)
            {
                PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 0,
                    "The Counsel fixture found belief mutations while Ideology was inactive.");
                Log.Message(LogPrefix + "Counsel mechanics: not applicable (Ideology inactive). ");
                return false;
            }

            PawnDiaryRimTestScope.Require(pawn?.ideo != null && otherPawn?.ideo != null
                    && pawn.needs?.mood?.thoughts?.memories != null
                    && otherPawn.needs?.mood?.thoughts?.memories != null,
                "The Ideology-active Counsel fixture lacked trackers or mood-memory handlers.");
            PawnDiaryRimTestScope.Require(
                DefDatabase<AbilityDef>.GetNamedSilentFail("Counsel") != null
                    && DefDatabase<InteractionDef>.GetNamedSilentFail(
                        CounselSuccessDefName) != null
                    && DefDatabase<InteractionDef>.GetNamedSilentFail(
                        CounselFailureDefName) != null,
                "The Ideology-active profile did not load Counsel and both verified result Defs.");
            return true;
        }

        /// <summary>Selects a bounded disposable caster whose real Social skill can drive Counsel.</summary>
        private static Pawn PrepareCounselCaster(int socialLevel)
        {
            Pawn caster = pawn;
            SkillRecord social = caster.skills?.GetSkill(SkillDefOf.Social);
            for (int attempt = 0;
                 (social == null || social.TotallyDisabled) && attempt < 8;
                 attempt++)
            {
                caster = scope.CreateAdultColonist();
                social = caster.skills?.GetSkill(SkillDefOf.Social);
            }
            PawnDiaryRimTestScope.Require(social != null && !social.TotallyDisabled,
                "Could not generate a bounded Counsel caster with a usable Social skill.");
            social.Level = socialLevel;
            return caster;
        }

        /// <summary>
        /// Establishes the real same-Ideo/healthy-target preconditions and removes only stale Counsel
        /// memories from the disposable listener so the installed ability's Valid check is deterministic.
        /// </summary>
        private static void PrepareCounselPair(Pawn caster, Pawn listener)
        {
            PawnDiaryRimTestScope.Require(caster != null && listener != null
                    && !ReferenceEquals(caster, listener)
                    && caster.ideo != null && listener.ideo != null,
                "The Counsel fixture requires two distinct pawns with Ideology trackers.");
            if (!ReferenceEquals(caster.ideo.Ideo, listener.ideo.Ideo))
            {
                if (caster.ideo.Ideo != null)
                    listener.ideo.SetIdeo(caster.ideo.Ideo);
                else if (listener.ideo.Ideo != null)
                    caster.ideo.SetIdeo(listener.ideo.Ideo);
            }
            PawnDiaryRimTestScope.Require(ReferenceEquals(caster.ideo.Ideo, listener.ideo.Ideo),
                "The deterministic Counsel fixture could not establish vanilla's same-Ideo precondition.");

            MemoryThoughtHandler memories = listener.needs?.mood?.thoughts?.memories;
            PawnDiaryRimTestScope.Require(memories != null,
                "The Counsel listener did not expose a mood-memory handler.");
            foreach (string defName in new[] { "Counselled", "Counselled_MoodBoost", "CounselFailed" })
            {
                ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
                PawnDiaryRimTestScope.Require(thought != null,
                    "The Ideology-active profile did not load Counsel thought '" + defName + "'.");
                memories.RemoveMemoriesOfDef(thought);
            }

            scope.SpawnAsLiveColonist(caster);
            scope.SpawnAsLiveColonist(listener);
            PawnDiaryRimTestScope.Require(caster.Map == listener.Map
                    && caster.MentalStateDef == null && listener.MentalStateDef == null,
                "The Counsel fixture could not establish a shared live map and healthy targets.");
            BeliefMutationCache.Reset();
        }

        /// <summary>Removes every memory vanilla could select for Counsel's relief subbranch.</summary>
        private static void RemoveCounselEligibleNegativeMemories(Pawn listener, float threshold)
        {
            MemoryThoughtHandler memories = listener?.needs?.mood?.thoughts?.memories;
            List<Thought> moodThoughts = new List<Thought>();
            listener?.needs?.mood?.thoughts?.GetAllMoodThoughts(moodThoughts);
            List<Thought_Memory> remove = moodThoughts
                .OfType<Thought_Memory>()
                .Where(memory => memory.MoodOffset() <= threshold)
                .ToList();
            for (int i = 0; i < remove.Count; i++) memories?.RemoveMemory(remove[i]);

            moodThoughts.Clear();
            listener?.needs?.mood?.thoughts?.GetAllMoodThoughts(moodThoughts);
            PawnDiaryRimTestScope.Require(
                !moodThoughts.OfType<Thought_Memory>()
                    .Any(memory => memory.MoodOffset() <= threshold),
                "The Counsel fixture could not clear eligible negative memories deterministically.");
        }

        /// <summary>Adds one disposable memory which vanilla must select for Counsel relief.</summary>
        private static Thought_Memory AddSyntheticCounselTargetMemory(Pawn listener, float moodOffset)
        {
            ThoughtDef thoughtDef = new ThoughtDef
            {
                defName = "PawnDiary_RimTest_CounselTarget_" + Guid.NewGuid().ToString("N"),
                durationDays = 1f,
                thoughtClass = typeof(Thought_Memory),
                stages = new List<ThoughtStage>
                {
                    new ThoughtStage
                    {
                        label = "synthetic Counsel target",
                        baseMoodEffect = moodOffset
                    }
                }
            };
            Thought_Memory memory = ThoughtMaker.MakeThought(thoughtDef) as Thought_Memory;
            listener?.needs?.mood?.thoughts?.memories?.TryGainMemory(memory);
            PawnDiaryRimTestScope.Require(memory != null
                    && listener.needs.mood.thoughts.memories.Memories.Contains(memory)
                    && Math.Abs(memory.MoodOffset() - moodOffset) < 0.0001f,
                "The Counsel fixture could not add its deterministic eligible negative memory.");
            return memory;
        }

        /// <summary>Proves canonical ownership drops Counsel before AbilitySignal's chance roll.</summary>
        private static void AssertCoveredCounselAbilityPreservesRand(Ability ability)
        {
            const int Seed = 847239;
            Rand.PushState(Seed);
            float expectedNext = Rand.Value;
            Rand.PopState();
            Rand.PushState(Seed);
            try
            {
                scope.RequireNoNewEvent(() => DiaryEvents.Submit(new AbilitySignal(
                    ability, new LocalTargetInfo(otherPawn), LocalTargetInfo.Invalid)));
                float actualNext = Rand.Value;
                PawnDiaryRimTestScope.Require(Math.Abs(actualNext - expectedNext) < 0.000001f,
                    "The downstream-covered Counsel ability consumed Rand before it was dropped.");
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>Finds a deterministic seed for vanilla's first Counsel success roll.</summary>
        private static int FindCounselSeed(float chance, bool expectedSuccess)
        {
            PawnDiaryRimTestScope.Require(chance >= 0f && chance <= 1.3f,
                "Vanilla Counsel returned an invalid success chance: " + chance + ".");
            for (int seed = 1; seed <= 100000; seed++)
            {
                bool succeeded;
                Rand.PushState(seed);
                try
                {
                    succeeded = Rand.Chance(chance);
                }
                finally
                {
                    Rand.PopState();
                }
                if (succeeded == expectedSuccess) return seed;
            }
            throw new AssertionException("Could not find a bounded RNG seed for Counsel "
                + (expectedSuccess ? "success" : "failure") + " at chance " + chance + ".");
        }

        private static Thought_Memory FindCounselMemory(Pawn listener, params string[] defNames)
        {
            return listener?.needs?.mood?.thoughts?.memories?.Memories?.FirstOrDefault(memory =>
                memory?.def != null && defNames.Contains(memory.def.defName));
        }

        private static bool HasAnyCounselMemory(Pawn listener, params string[] defNames)
        {
            return FindCounselMemory(listener, defNames) != null;
        }

        /// <summary>Asserts exact ownership, roles, mood truth, prompt choice, and absent mutation facts.</summary>
        private static void AssertCounselPage(
            DiaryEvent page,
            Pawn caster,
            Pawn listener,
            Ideo beforeIdeology,
            float beforeCertainty,
            string expectedDefName,
            string expectedMoodContext)
        {
            scope.RequirePairRefs(page, caster, listener);
            PawnDiaryRimTestScope.Require(ReferenceEquals(listener.ideo.Ideo, beforeIdeology)
                    && Math.Abs(listener.ideo.Certainty - beforeCertainty) < 0.0001f,
                "Counsel changed ideoligion or certainty even though vanilla only changes mood thoughts.");
            PawnDiaryRimTestScope.Require(BeliefMutationCache.Count == 0,
                "Counsel manufactured a belief mutation cache row.");
            AssertGameContextContains(page, expectedMoodContext);
            PawnDiaryRimTestScope.Require(
                string.IsNullOrEmpty(page.BeliefContextForRole(DiaryEvent.InitiatorRole))
                    && string.IsNullOrEmpty(page.BeliefContextForRole(DiaryEvent.RecipientRole)),
                "Context-only Counsel unexpectedly invoked or persisted doctrine interpretation.");
            string allContext = (page.gameContext ?? string.Empty) + "\n"
                + (page.BeliefContextForRole(DiaryEvent.InitiatorRole) ?? string.Empty) + "\n"
                + (page.BeliefContextForRole(DiaryEvent.RecipientRole) ?? string.Empty);
            PawnDiaryRimTestScope.Require(
                allContext.IndexOf("belief_event=conversion", StringComparison.Ordinal) < 0
                    && allContext.IndexOf("certainty delta:", StringComparison.Ordinal) < 0
                    && allContext.IndexOf("conversion result:", StringComparison.Ordinal) < 0
                    && allContext.IndexOf("mutation cause:", StringComparison.Ordinal) < 0,
                "Counsel context fabricated conversion, certainty, or mutation mechanics: " + allContext);

            DiaryPromptRequest request = DiaryPipelineAdapters.BuildPromptRequest(
                page, DiaryEvent.InitiatorRole, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, titleRequest: false, maxTokens: 512);
            PawnDiaryRimTestScope.Require(request?.policy?.group != null
                    && request.policy.group.defName == CounselEventPolicy.GroupDefName
                    && request.policy.group.eventPromptKey == expectedDefName,
                "The exact Counsel page did not select its group and source-specific prompt policy.");
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

        /// <summary>
        /// Builds an executable test Ability from the installed vanilla Def's real effect-comp policy.
        /// The clone deliberately omits its cooldown group so a direct test activation cannot mutate
        /// unrelated role abilities or ritual cooldowns in the loaded player's colony.
        /// </summary>
        private static Ability BuildExecutableVanillaAbility(
            Pawn caster,
            string defName,
            Type expectedEffectCompType)
        {
            AbilityDef source = DefDatabase<AbilityDef>.GetNamedSilentFail(defName);
            PawnDiaryRimTestScope.Require(source?.comps != null
                    && source.verbProperties != null
                    && source.comps.Any(properties => properties?.compClass == expectedEffectCompType),
                "The installed Ideology AbilityDef '" + defName
                    + "' did not expose its expected vanilla effect comp.");
            AbilityDef executable = new AbilityDef
            {
                defName = source.defName,
                label = source.label,
                abilityClass = typeof(Ability),
                comps = new List<AbilityCompProperties>(source.comps),
                cooldownTicksRange = new IntRange(0, 0),
                hostile = source.hostile,
                verbProperties = source.verbProperties
            };
            return new Ability(caster, executable);
        }

        /// <summary>
        /// Activates the real vanilla effect comps through the production Ability.Activate Harmony
        /// boundary, captures exactly the PlayLog rows that call created, and registers them for cleanup.
        /// </summary>
        private static List<LogEntry> ActivateAndTrackPlayLog(Ability ability, Pawn target)
        {
            List<LogEntry> existing = new List<LogEntry>(Find.PlayLog.AllEntries);
            bool activated = ability.Activate(
                new LocalTargetInfo(target), LocalTargetInfo.Invalid);
            PawnDiaryRimTestScope.Require(activated,
                "Vanilla Ability.Activate rejected the executable Ideology fixture.");
            List<LogEntry> added = Find.PlayLog.AllEntries
                .Where(entry => entry != null && !existing.Contains(entry))
                .ToList();
            for (int i = 0; i < added.Count; i++) scope.TrackPlayLogEntry(added[i]);
            PawnDiaryRimTestScope.Require(added.Count == 1
                    && added[0] is PlayLogEntry_Interaction
                    && added[0].Concerns(ability.pawn)
                    && added[0].Concerns(target),
                "The vanilla Ideology ability did not create exactly one pair interaction PlayLog row.");
            return added;
        }

        private static string FormatSignedPercent(float value)
        {
            float clamped = Math.Max(-1f, Math.Min(1f, value));
            return Math.Round(clamped * 100f, MidpointRounding.AwayFromZero)
                .ToString("+0;-0;0", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }

        private static bool ThrowMutationProjection()
        {
            throw new InvalidOperationException("Synthetic Ideology mutation projection failure.");
        }

        private static bool ThrowBeliefEvidenceSelection()
        {
            throw new InvalidOperationException("Synthetic Ideology evidence-selection failure.");
        }

        private static bool ThrowCounselContextSelection()
        {
            throw new InvalidOperationException("Synthetic Counsel context-selection failure.");
        }
    }
}

// In-game ritual-capture tests for Pawn Diary's Ideology/psychic-ritual fan-out (EVT-17), including
// the exact completed conversion-ritual and authority-speech Phase 2 slices.
//
// A finished ritual is a colony perspective FAN-OUT: RitualFanoutSignal (Ideology,
// LordJob_Ritual.ApplyOutcome) and PsychicRitualFanoutSignal (Anomaly, psychic ritual) each emit one
// solo diary entry per organizer/invoker, target, participant, and spectator, sharing one ritual-level
// dedup window, and each perspective carries its own localized role label, instruction, and
// "ritual="/"psychic_ritual=" game-context marker.
//
// Live vanilla ritual objects cannot be safely constructed mapless, so the suite uses the production
// signals' internal fact-based fixture seams. Those seams bypass only reflection over LordJob_Ritual /
// PsychicRitual; they still drive the real fan-out order, uniqueness filter, colony dedup, child
// capture decision, AddSoloEvent persistence, diary references, and prompt-context assembly.
//
// The suite proves in-game, deterministically, with no map and without owning any DLC:
//   * both ritual fan-outs emit one unique page per organizer/invoker, target, participant, and
//     spectator, and a second dispatch is suppressed by the shared ritual-level dedup window;
//   * the per-perspective game-context markers the prompt receives are well-formed and field-ordered,
//     with the ritual quality label drawn from live XML tuning (DiaryTuningDef.ritualQualityBands);
//   * every organizer/target/participant/spectator (and psychic invoker) role LABEL and INSTRUCTION
//     translation key resolves to a non-empty, distinct string in the loaded language data — the
//     fan-out's localization contract, which the out-of-game policy tests cannot exercise;
//   * the DLC-gated context fields the ritual marker embeds (royal title, ideological role) stay
//     base-game safe: absent DLC yields empty accessors and a clean "none" fallback, never a crash.
//
// Coverage-matrix ID (TEST_COVERAGE_PLAN.md §3): EVT-17 Ritual (DLC).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PawnDiary;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves the Ideology/psychic ritual fan-outs' unique per-perspective pages, colony dedup,
    /// context, localization, and DLC-safe fields in a loaded game. Fact fixtures bypass only unsafe
    /// construction of live ritual jobs; the production fan-out/child pipeline remains under test.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryRitualFlowTests
    {
        // A synthetic ritual identity. The fan-out stores whatever defName/title the live ritual had;
        // these tests exercise the pure/localization surface, so any stable non-empty values work.
        private const string RitualDefName = "RimTest_Ritual";
        private const string RitualTitle = "RimTest Ceremony";
        private const string RitualBehavior = "RitualBehaviorWorker_RimTest";

        private static PawnDiaryRimTestScope scope;
        private static Pawn firstPawn;
        private static readonly FieldInfo EventsField = typeof(DiaryGameComponent).GetField(
            "events", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Opens a fresh test scope and creates one isolated adult colonist (generation disabled) for the
        /// DLC-context assertions. No interaction groups are needed: this suite never drives the
        /// group-gated capture constructor.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(
                ConversionRitualPolicy.GroupDefName, ConversionRitualPolicy.LegacyGroupDefName);
            firstPawn = scope.CreateAdultColonist();
            BeliefMutationCache.Reset();
            ConversionRitualEvidenceAdapter.SetFailureForTests(false);
            AuthoritySpeechEvidenceAdapter.SetFailureForTests(false);
            scope.RegisterCleanup(() =>
            {
                ConversionRitualEvidenceAdapter.SetFailureForTests(false);
                AuthoritySpeechEvidenceAdapter.SetFailureForTests(false);
                BeliefMutationCache.Reset();
            });
        }

        /// <summary>
        /// Restores every mutation and audits that no test-owned state survived — even when the test
        /// above threw partway through.
        /// </summary>
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
                ConversionRitualEvidenceAdapter.SetFailureForTests(false);
                AuthoritySpeechEvidenceAdapter.SetFailureForTests(false);
            }
        }

        /// <summary>
        /// EVT-17. Verifies the Ideology ritual game-context marker the fan-out hands the prompt: the
        /// load-bearing "ritual=" prefix, the locked field order, the per-perspective "ritual_perspective="
        /// field, the finished outcome, and a quality label taken from live XML tuning
        /// (DiaryTuningDef.ritualQualityBands) rather than a hardcoded band.
        /// </summary>
        [Test]
        public static void IdeologyRitualContextMarkerUsesLiveTuningAndLockedFieldOrder()
        {
            // Quality label is computed by the SAME production path the fan-out uses, against the live,
            // XML-loaded bands. 0.9f is a fixed input so the resulting label is deterministic.
            string quality = RitualEventData.QualityLabel(0.9f, DiaryTuning.Current.ritualQualityBands);
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(quality),
                "Live ritual quality bands produced no label for a 0.9 ritual.");

            string context = RitualEventData.BuildGameContext(
                RitualDefName,
                RitualTitle,
                RitualBehavior,
                RitualEventData.PerspectiveOrganizer,
                "speaker",
                royalTitle: string.Empty,
                ideologicalRole: string.Empty,
                RitualFanoutSignal.RitualOutcomeFinished,
                quality);

            PawnDiaryRimTestScope.Require(
                context.StartsWith("ritual=" + RitualDefName, StringComparison.Ordinal),
                "The ritual context must lead with the load-bearing 'ritual=<defName>' marker.");
            RequireContains(context, "ritual_title=" + RitualTitle);
            RequireContains(context, "ritual_behavior=" + RitualBehavior);
            RequireContains(context, "ritual_perspective=" + RitualEventData.PerspectiveOrganizer);
            RequireContains(context, "outcome=" + RitualFanoutSignal.RitualOutcomeFinished);
            RequireContains(context, "quality=" + quality);

            // Field order is part of the contract (locked by the out-of-game tests too). Confirm the
            // marker sequence in-game so a re-order that still compiles is caught here as well.
            RequireOrdered(context, "ritual=", "ritual_title=", "ritual_behavior=", "ritual_perspective=",
                "ritual_role=", "royal_title=", "ideological_role=", "outcome=", "quality=");
        }

        /// <summary>
        /// EVT-17. Verifies the Ideology fan-out's per-perspective localization contract: each of the
        /// organizer/target/participant/spectator perspectives resolves to a non-empty role label AND a
        /// non-empty role instruction in the loaded language, and the four role labels are pairwise
        /// distinct so no two perspectives collapse to the same prompt guidance.
        /// </summary>
        [Test]
        public static void IdeologyRitualPerspectiveLabelsAndInstructionsResolve()
        {
            string[] perspectives =
            {
                RitualEventData.PerspectiveOrganizer,
                RitualEventData.PerspectiveTarget,
                RitualEventData.PerspectiveParticipant,
                RitualEventData.PerspectiveSpectator,
            };

            string[] labels = new string[perspectives.Length];
            for (int i = 0; i < perspectives.Length; i++)
            {
                labels[i] = RitualFanoutSignal.RitualPerspectiveLabel(perspectives[i]);
                PawnDiaryRimTestScope.Require(
                    !string.IsNullOrWhiteSpace(labels[i]),
                    "Ritual perspective '" + perspectives[i] + "' resolved to an empty role label.");

                string instruction = RitualFanoutSignal.RitualInstructionFor(perspectives[i]);
                PawnDiaryRimTestScope.Require(
                    !string.IsNullOrWhiteSpace(instruction),
                    "Ritual perspective '" + perspectives[i] + "' resolved to an empty role instruction.");
            }

            RequirePairwiseDistinct(labels, "Ideology ritual role labels");
        }

        /// <summary>
        /// EVT-17. Verifies the Anomaly psychic-ritual fan-out's context + localization wiring, which is
        /// DLC-content-free to evaluate: the "psychic_ritual=" marker (deliberately WITHOUT the Ideology
        /// role/title fields) is well-formed, and each invoker/target/participant/spectator perspective
        /// resolves to a non-empty, pairwise-distinct label and a non-empty instruction. The live psychic
        /// event-creation fan-out itself is DLC-gated (see the no-op branch and productionSeamNeeded).
        /// </summary>
        [Test]
        public static void PsychicRitualContextMarkerAndPerspectiveWiringResolve()
        {
            string quality = RitualEventData.QualityLabel(0.9f, DiaryTuning.Current.ritualQualityBands);
            string context = RitualEventData.BuildPsychicGameContext(
                RitualDefName,
                RitualEventData.PerspectiveInvoker,
                RitualFanoutSignal.RitualOutcomeFinished,
                quality);

            PawnDiaryRimTestScope.Require(
                context.StartsWith("psychic_ritual=" + RitualDefName, StringComparison.Ordinal),
                "The psychic ritual context must lead with the 'psychic_ritual=<defName>' marker.");
            RequireContains(context, "psychic_ritual_perspective=" + RitualEventData.PerspectiveInvoker);
            RequireContains(context, "outcome=" + RitualFanoutSignal.RitualOutcomeFinished);
            RequireContains(context, "quality=" + quality);
            // Psychic context deliberately omits the Ideology-only role/title fields.
            PawnDiaryRimTestScope.Require(
                context.IndexOf("ritual_role=", StringComparison.Ordinal) < 0
                    && context.IndexOf("royal_title=", StringComparison.Ordinal) < 0,
                "The psychic ritual context must not carry the Ideology ritual role/title fields.");

            string[] perspectives =
            {
                RitualEventData.PerspectiveInvoker,
                RitualEventData.PerspectiveTarget,
                RitualEventData.PerspectiveParticipant,
                RitualEventData.PerspectiveSpectator,
            };

            string[] labels = new string[perspectives.Length];
            for (int i = 0; i < perspectives.Length; i++)
            {
                labels[i] = PsychicRitualFanoutSignal.PsychicRitualPerspectiveLabel(perspectives[i]);
                PawnDiaryRimTestScope.Require(
                    !string.IsNullOrWhiteSpace(labels[i]),
                    "Psychic ritual perspective '" + perspectives[i] + "' resolved to an empty label.");

                string instruction = PsychicRitualFanoutSignal.PsychicRitualInstructionFor(perspectives[i]);
                PawnDiaryRimTestScope.Require(
                    !string.IsNullOrWhiteSpace(instruction),
                    "Psychic ritual perspective '" + perspectives[i] + "' resolved to an empty instruction.");
            }

            RequirePairwiseDistinct(labels, "psychic ritual role labels");
        }

        /// <summary>
        /// EVT-17 / Ideology. Drives the production ritual fan-out from copied fixture facts through
        /// all four perspective pages. Duplicate role membership is deliberately supplied so the
        /// pawn-ID uniqueness filter and ritual-level second-dispatch dedup are both exercised.
        /// </summary>
        [Test]
        public static void IdeologyRitualFixtureFanoutEmitsUniquePerspectivePagesOnce()
        {
            Pawn target = scope.CreateAdultColonist();
            Pawn participant = scope.CreateAdultColonist();
            Pawn spectator = scope.CreateAdultColonist();
            HashSet<string> before = SnapshotEventIds();

            RitualFanoutSignal fanout = RitualFanoutSignal.CreateTestFixture(
                firstPawn,
                target,
                new List<Pawn> { firstPawn, participant },
                new List<Pawn> { participant, spectator },
                RitualDefName,
                RitualTitle,
                RitualBehavior,
                0.82f,
                "fixture ritual theme");
            PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(fanout.ColonyDedupKey),
                "The Ideology ritual fixture did not create a colony dedup key.");

            scope.Component.Dispatch(fanout);
            List<DiaryEvent> emitted = NewEventsSince(before);
            PawnDiaryRimTestScope.Require(emitted.Count == 4,
                "The Ideology ritual fixture should emit exactly four unique perspective pages, got "
                + emitted.Count + ".");
            RequirePerspectiveEvent(emitted, "ritual_perspective=" + RitualEventData.PerspectiveOrganizer, firstPawn);
            RequirePerspectiveEvent(emitted, "ritual_perspective=" + RitualEventData.PerspectiveTarget, target);
            RequirePerspectiveEvent(emitted, "ritual_perspective=" + RitualEventData.PerspectiveParticipant, participant);
            RequirePerspectiveEvent(emitted, "ritual_perspective=" + RitualEventData.PerspectiveSpectator, spectator);

            int afterFirst = EventRepository().Count;
            scope.Component.Dispatch(fanout);
            PawnDiaryRimTestScope.Require(EventRepository().Count == afterFirst,
                "A repeated Ideology ritual fan-out escaped the colony dedup window.");
        }

        /// <summary>
        /// Ideology Phase 2. Proves the loaded package gate, full ordinal classifier identity, dedicated
        /// prompt policy, and legacy ritual-setting inheritance without constructing a live LordJob.
        /// </summary>
        [Test]
        public static void ConversionRitualClassificationAndSettingMigrationRespectDlcAvailability()
        {
            bool active = ModsConfig.IdeologyActive;
            DiaryInteractionGroupDef exact = InteractionGroups.ByKey(ConversionRitualPolicy.GroupDefName);
            PawnDiaryRimTestScope.Require(exact != null
                    && exact.MissingRequiredPackage() == !active
                    && exact.tones != null && exact.tones.Count >= 2,
                "The loaded conversion-ritual group did not preserve its Ideology gate/tone contract.");
            PawnDiaryRimTestScope.Require(
                InteractionGroups.ClassifyRitual(
                    "Conversion;RitualBehaviorWorker_Conversion")?.defName
                    == (active ? ConversionRitualPolicy.GroupDefName
                        : ConversionRitualPolicy.LegacyGroupDefName),
                "The exact conversion ritual classifier did not respect DLC availability.");
            PawnDiaryRimTestScope.Require(
                InteractionGroups.ClassifyRitual("Conversion")?.defName
                    == ConversionRitualPolicy.LegacyGroupDefName
                && InteractionGroups.ClassifyRitual(
                    "conversion;RitualBehaviorWorker_Conversion")?.defName
                    == ConversionRitualPolicy.LegacyGroupDefName
                && InteractionGroups.ClassifyRitual(
                    "Conversion;ModdedConversionWorker")?.defName
                    == ConversionRitualPolicy.LegacyGroupDefName,
                "A def-only, case-variant, or modded conversion ritual escaped to the exact group.");

            ConversionRitualPolicySnapshot policy = DiaryConversionRitualPolicy.Snapshot();
            PawnDiaryRimTestScope.Require(
                policy.behaviorWorkerClassName
                    == typeof(RitualBehaviorWorker_Conversion).FullName
                    && policy.outcomeWorkerClassName
                    == typeof(RitualOutcomeEffectWorker_Conversion).FullName,
                "The loaded conversion policy did not match the installed fully-qualified worker types.");
            PawnDiaryRimTestScope.Require(
                ConversionRitualPolicy.Matches(
                    "Conversion", typeof(RitualBehaviorWorker_Conversion).FullName,
                    typeof(RitualOutcomeEffectWorker_Conversion).FullName,
                    ConversionRitualPolicy.GroupDefName,
                    active, policy) == active,
                "The loaded exact conversion policy did not match current DLC availability.");

            if (!active)
            {
                PawnDiaryRimTestScope.Require(
                    !PawnDiaryMod.Settings.IsGroupEnabled(ConversionRitualPolicy.GroupDefName),
                    "The exact conversion ritual setting must be inert without Ideology.");
                return;
            }

            PawnDiaryMod.Settings.groupEnabled.Remove(ConversionRitualPolicy.GroupDefName);
            PawnDiaryMod.Settings.SetGroupEnabled(ConversionRitualPolicy.LegacyGroupDefName, false);
            PawnDiaryRimTestScope.Require(
                !PawnDiaryMod.Settings.IsGroupEnabled(ConversionRitualPolicy.GroupDefName),
                "An upgraded ritualFinished=false save unexpectedly enabled conversion rituals.");
            PawnDiaryMod.Settings.SetGroupEnabled(ConversionRitualPolicy.GroupDefName, true);
            MethodInfo normalize = typeof(PawnDiarySettings).GetMethod(
                "NormalizeGroupEnabledOverrides", BindingFlags.Instance | BindingFlags.NonPublic);
            PawnDiaryRimTestScope.Require(normalize != null,
                "Could not resolve conversion-ritual settings normalization.");
            normalize.Invoke(PawnDiaryMod.Settings, null);
            PawnDiaryRimTestScope.Require(
                PawnDiaryMod.Settings.IsGroupEnabled(ConversionRitualPolicy.GroupDefName)
                    && PawnDiaryMod.Settings.HasGroupEnabledOverride(
                        ConversionRitualPolicy.GroupDefName),
                "An explicit conversion-ritual enable did not survive legacy-setting normalization.");
        }

        /// <summary>
        /// Ideology Phase 2. Uses the real patched SetIdeo mutation boundary, then the production
        /// completed-ritual fan-out seam, to prove per-role evidence, target-only before/after facts,
        /// prompt ownership, dedup, RNG neutrality, later-pawn independence, and real Scribe persistence.
        /// </summary>
        [Test]
        public static void CompletedConversionRitualPerspectivesFreezeExactTargetMutation()
        {
            if (!RequireMutableIdeologyProfile()) return;

            Pawn target = scope.CreateAdultColonist();
            Pawn participant = scope.CreateAdultColonist();
            Pawn spectator = scope.CreateAdultColonist();
            Ideo organizerIdeology = GenerateRegisteredIdeology(
                firstPawn, target, participant, spectator);
            Ideo oldTargetIdeology = GenerateRegisteredIdeology(target);
            Ideo laterTargetIdeology = GenerateRegisteredIdeology(target);
            firstPawn.ideo.SetIdeo(organizerIdeology);
            target.ideo.SetIdeo(oldTargetIdeology);
            participant.ideo.SetIdeo(organizerIdeology);
            spectator.ideo.SetIdeo(organizerIdeology);

            ConversionRitualPolicySnapshot policy = DiaryConversionRitualPolicy.Snapshot();
            Precept_Role moralGuide = organizerIdeology.RolesListForReading.FirstOrDefault(role =>
                role?.def?.defName == policy.organizerIdeologyRoleDefName
                    && role.ChosenPawnSingle() == null && role.RequirementsMet(firstPawn));
            PawnDiaryRimTestScope.Require(moralGuide != null,
                "The disposable Ideology did not expose an assignable exact moral-guide role.");
            moralGuide.Assign(firstPawn, false);
            scope.RegisterCleanup(() =>
            {
                if (moralGuide.IsAssigned(firstPawn)) moralGuide.Unassign(firstPawn, false);
            });

            BeliefMutationCache.Reset();
            target.ideo.SetIdeo(organizerIdeology);
            BeliefMutationSnapshot captured = BeliefMutationCache.PeekLatest(
                target.GetUniqueLoadID(), Find.TickManager.TicksGame, DiaryBeliefPolicy.Snapshot());
            PawnDiaryRimTestScope.Require(captured != null && captured.ideologyChanged
                    && captured.afterIdeologyId == organizerIdeology.GetUniqueLoadID(),
                "The real SetIdeo completion mutation was not available to the ritual fan-out.");
            BeliefSourcePreceptFact capturedRole;
            BeliefMutationSnapshot capturedTarget;
            bool adapterCaptured = ConversionRitualEvidenceAdapter.TryCapture(
                firstPawn, target, Find.TickManager.TicksGame, policy,
                out capturedRole, out capturedTarget);
            PawnDiaryRimTestScope.Require(adapterCaptured
                    && capturedRole?.defName == policy.organizerIdeologyRoleDefName
                    && capturedTarget != null,
                "The registered conversion fixture did not project its exact organizer role and "
                    + "target mutation through the production evidence adapter.");

            RitualFanoutSignal fanout = null;
            const int Seed = 730421;
            Rand.PushState(Seed);
            float expectedNext = Rand.Value;
            Rand.PopState();
            Rand.PushState(Seed);
            try
            {
                fanout = ExactConversionFixture(
                    target, participant, spectator, progress: 1f);
                float actualNext = Rand.Value;
                PawnDiaryRimTestScope.Require(Math.Abs(actualNext - expectedNext) < 0.000001f,
                    "Conversion-ritual enrichment consumed global Rand for cosmetic selection.");
            }
            finally
            {
                Rand.PopState();
            }

            HashSet<string> before = SnapshotEventIds();
            scope.Component.Dispatch(fanout);
            List<DiaryEvent> emitted = NewEventsSince(before);
            PawnDiaryRimTestScope.Require(emitted.Count == 4,
                "The exact conversion ritual should emit four unique perspective pages, got "
                    + emitted.Count + ".");
            DiaryEvent organizerPage = PageForPerspective(
                emitted, RitualEventData.PerspectiveOrganizer, firstPawn);
            DiaryEvent targetPage = PageForPerspective(
                emitted, RitualEventData.PerspectiveTarget, target);
            DiaryEvent participantPage = PageForPerspective(
                emitted, RitualEventData.PerspectiveParticipant, participant);
            DiaryEvent spectatorPage = PageForPerspective(
                emitted, RitualEventData.PerspectiveSpectator, spectator);

            RequireContains(organizerPage.gameContext, "conversion_ritual_role=converter");
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(organizerPage.BeliefContextForRole(DiaryEvent.InitiatorRole)),
                "The organizer page did not freeze its exact moral-guide/proselytizing context.");
            RequireContains(targetPage.gameContext, "conversion_ritual_role=convertee");
            RequireContains(targetPage.gameContext, "conversion_ritual_result=converted");
            RequireContains(targetPage.gameContext, "belief_event=conversion");
            string frozenTargetBelief = targetPage.BeliefContextForRole(DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(frozenTargetBelief)
                    && frozenTargetBelief.IndexOf(oldTargetIdeology.name,
                        StringComparison.OrdinalIgnoreCase) >= 0
                    && frozenTargetBelief.IndexOf(organizerIdeology.name,
                        StringComparison.OrdinalIgnoreCase) >= 0,
                "The target page did not persist its event-time before/after ideoligion facts: "
                    + frozenTargetBelief);

            RequireContains(participantPage.gameContext, "conversion_ritual_role=participant");
            AssertNoTargetMutationLeak(participantPage);
            RequireContains(spectatorPage.gameContext, "conversion_ritual_role=spectator");
            AssertNoTargetMutationLeak(spectatorPage);
            PawnDiaryRimTestScope.Require(string.IsNullOrEmpty(
                    spectatorPage.BeliefContextForRole(DiaryEvent.InitiatorRole)),
                "The spectator page received belief enrichment despite the XML 'none' mode.");

            DiaryPromptRequest request = DiaryPipelineAdapters.BuildPromptRequest(
                targetPage, DiaryEvent.InitiatorRole, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, titleRequest: false, maxTokens: 512);
            PawnDiaryRimTestScope.Require(request?.policy?.group?.defName
                    == ConversionRitualPolicy.GroupDefName
                    && request.policy.group.eventPromptKey == ConversionRitualPolicy.GroupDefName,
                "The completed conversion page did not select the dedicated group prompt.");

            int afterFirst = EventRepository().Count;
            scope.Component.Dispatch(fanout);
            PawnDiaryRimTestScope.Require(EventRepository().Count == afterFirst,
                "A repeated exact conversion fan-out escaped the existing ritual dedup window.");

            target.ideo.SetIdeo(laterTargetIdeology);
            target.ideo.OffsetCertainty(target.ideo.Certainty > 0.5f ? -0.05f : 0.05f);
            PawnDiaryRimTestScope.Require(
                targetPage.BeliefContextForRole(DiaryEvent.InitiatorRole) == frozenTargetBelief,
                "Later pawn belief changes rewrote the saved conversion-ritual context.");
            DiaryEvent loaded = PawnDiaryScribeRoundTripFixtureTests
                .ScribeRoundTripForTests(targetPage);
            PawnDiaryRimTestScope.Require(
                loaded.BeliefContextForRole(DiaryEvent.InitiatorRole) == frozenTargetBelief
                    && loaded.gameContext == targetPage.gameContext,
                "Conversion-ritual event-time facts did not survive a real Scribe round-trip.");
        }

        /// <summary>
        /// A masterful-looking fixture with only a real certainty mutation must remain non-conversion,
        /// and an optional adapter exception must leave the ordinary completed ritual page intact.
        /// </summary>
        [Test]
        public static void ConversionRitualQualityCannotProveConversionAndFailureIsFailOpen()
        {
            if (!RequireMutableIdeologyProfile()) return;
            Pawn target = scope.CreateAdultColonist();
            Pawn participant = scope.CreateAdultColonist();
            Pawn spectator = scope.CreateAdultColonist();
            Pawn roleOnlyOrganizer = scope.CreateAdultColonist();
            Pawn roleOnlyTarget = scope.CreateAdultColonist();
            Pawn roleOnlyParticipant = scope.CreateAdultColonist();
            Pawn roleOnlySpectator = scope.CreateAdultColonist();
            Pawn failedOrganizer = scope.CreateAdultColonist();
            Pawn failedTarget = scope.CreateAdultColonist();
            Pawn failedParticipant = scope.CreateAdultColonist();
            Pawn failedSpectator = scope.CreateAdultColonist();
            Ideo shared = GenerateRegisteredIdeology(new[]
            {
                firstPawn, target, participant, spectator,
                roleOnlyOrganizer, roleOnlyTarget, roleOnlyParticipant, roleOnlySpectator,
                failedOrganizer, failedTarget, failedParticipant, failedSpectator
            });
            firstPawn.ideo.SetIdeo(shared);
            target.ideo.SetIdeo(shared);
            participant.ideo.SetIdeo(shared);
            spectator.ideo.SetIdeo(shared);
            BeliefMutationCache.Reset();
            float delta = target.ideo.Certainty > 0.2f ? -0.1f : 0.1f;
            target.ideo.OffsetCertainty(delta);

            HashSet<string> before = SnapshotEventIds();
            scope.Component.Dispatch(ExactConversionFixture(
                target, participant, spectator, progress: 1f));
            DiaryEvent targetPage = PageForPerspective(
                NewEventsSince(before), RitualEventData.PerspectiveTarget, target);
            string expectedResult = delta < 0f ? "certainty_decreased" : "certainty_increased";
            RequireContains(targetPage.gameContext,
                "conversion_ritual_result=" + expectedResult);
            PawnDiaryRimTestScope.Require(
                targetPage.gameContext.IndexOf("conversion_ritual_result=converted",
                    StringComparison.Ordinal) < 0,
                "A high quality label manufactured conversion without an ideology transition.");

            // Missing mutation evidence is a role-only fallback, not an all-or-nothing adapter fault.
            // The exact assignment still determines each page's POV, while no result or before/after
            // belief facts may be invented for the target.
            roleOnlyOrganizer.ideo.SetIdeo(shared);
            roleOnlyTarget.ideo.SetIdeo(shared);
            roleOnlyParticipant.ideo.SetIdeo(shared);
            roleOnlySpectator.ideo.SetIdeo(shared);
            BeliefMutationCache.Reset();
            HashSet<string> roleOnlyBefore = SnapshotEventIds();
            scope.Component.Dispatch(ExactConversionFixture(
                roleOnlyTarget, roleOnlyParticipant, roleOnlySpectator, progress: 1f,
                organizer: roleOnlyOrganizer));
            List<DiaryEvent> roleOnly = NewEventsSince(roleOnlyBefore);
            PawnDiaryRimTestScope.Require(roleOnly.Count == 4,
                "Missing target mutation evidence suppressed the exact ritual fan-out; got "
                    + roleOnly.Count + " pages.");
            DiaryEvent roleOnlyTargetPage = PageForPerspective(
                roleOnly, RitualEventData.PerspectiveTarget, roleOnlyTarget);
            PageForPerspective(
                roleOnly, RitualEventData.PerspectiveSpectator, roleOnlySpectator);
            RequireContains(roleOnlyTargetPage.gameContext,
                "conversion_ritual_role=convertee");
            PawnDiaryRimTestScope.Require(
                roleOnlyTargetPage.gameContext.IndexOf("conversion_ritual_result=",
                    StringComparison.Ordinal) < 0
                    && string.IsNullOrEmpty(roleOnlyTargetPage.BeliefContextForRole(
                        DiaryEvent.InitiatorRole)),
                "Missing target mutation evidence fabricated conversion mechanics.");

            // Each same-tick fixture uses a fresh organizer as well as a fresh target. This preserves
            // the production 60-tick per-pawn event-type dedup instead of making it suppress the reused
            // organizer page and falsely attributing that expected suppression to optional enrichment.
            failedOrganizer.ideo.SetIdeo(shared);
            failedTarget.ideo.SetIdeo(shared);
            failedParticipant.ideo.SetIdeo(shared);
            failedSpectator.ideo.SetIdeo(shared);
            ConversionRitualEvidenceAdapter.SetFailureForTests(true);
            try
            {
                HashSet<string> failureBefore = SnapshotEventIds();
                scope.Component.Dispatch(ExactConversionFixture(
                    failedTarget, failedParticipant, failedSpectator, progress: 1f,
                    organizer: failedOrganizer));
                List<DiaryEvent> ordinary = NewEventsSince(failureBefore);
                PawnDiaryRimTestScope.Require(ordinary.Count == 4,
                    "An optional enrichment failure suppressed the ordinary ritual fan-out; got "
                        + ordinary.Count + " pages.");
                DiaryEvent ordinaryTarget = PageForPerspective(
                    ordinary, RitualEventData.PerspectiveTarget, failedTarget);
                PageForPerspective(
                    ordinary, RitualEventData.PerspectiveSpectator, failedSpectator);
                PawnDiaryRimTestScope.Require(
                    ordinaryTarget.gameContext.IndexOf("conversion_ritual_",
                        StringComparison.Ordinal) < 0
                        && string.IsNullOrEmpty(ordinaryTarget.BeliefContextForRole(
                            DiaryEvent.InitiatorRole)),
                    "A failed conversion adapter left partial or fabricated enrichment on the page.");
            }
            finally
            {
                ConversionRitualEvidenceAdapter.SetFailureForTests(false);
            }
        }

        /// <summary>
        /// Ideology Phase 2. Confirms the loaded XML identities against the installed runtime types and
        /// proves that throne ownership remains Royal while leader speech remains the ordinary ritual owner.
        /// </summary>
        [Test]
        public static void AuthoritySpeechLoadedPolicyMatchesInstalledRoutesAndOwners()
        {
            AuthoritySpeechPolicySnapshot policy = DiaryAuthoritySpeechPolicy.Snapshot();
            Type throneBehavior = typeof(LordJob_Ritual).Assembly.GetType(
                "RimWorld.RitualBehaviorWorker_ThroneSpeech", false);
            Type leaderBehavior = typeof(LordJob_Ritual).Assembly.GetType(
                "RimWorld.RitualBehaviorWorker_Speech", false);
            Type speechOutcome = typeof(LordJob_Ritual).Assembly.GetType(
                "RimWorld.RitualOutcomeEffectWorker_Speech", false);
            PawnDiaryRimTestScope.Require(
                throneBehavior != null && leaderBehavior != null && speechOutcome != null,
                "Installed RimWorld 1.6 speech worker types could not be resolved.");

            DiaryInteractionGroupDef throneOwner = InteractionGroups.ClassifyRitual(
                "ThroneSpeech;RitualBehaviorWorker_ThroneSpeech");
            DiaryInteractionGroupDef leaderOwner = InteractionGroups.ClassifyRitual(
                "LeaderSpeech;RitualBehaviorWorker_Speech");
            PawnDiaryRimTestScope.Require(
                leaderOwner?.defName == ConversionRitualPolicy.LegacyGroupDefName,
                "Leader speech no longer kept the ordinary ritual page owner.");
            if (ModsConfig.RoyaltyActive)
                PawnDiaryRimTestScope.Require(throneOwner?.defName == "ritualRoyal",
                    "Throne speech no longer kept the Royal ritual page owner.");

            AuthoritySpeechRouteSnapshot leader = AuthoritySpeechPolicy.Match(
                "LeaderSpeech", leaderBehavior.FullName, speechOutcome.FullName,
                leaderOwner?.defName, "speaker", ModsConfig.IdeologyActive,
                ModsConfig.RoyaltyActive, policy);
            PawnDiaryRimTestScope.Require((leader != null) == ModsConfig.IdeologyActive,
                "Loaded leader-speech enrichment did not follow Ideology availability.");
            AuthoritySpeechRouteSnapshot throne = AuthoritySpeechPolicy.Match(
                "ThroneSpeech", throneBehavior.FullName, speechOutcome.FullName,
                throneOwner?.defName, "speaker", ModsConfig.IdeologyActive,
                ModsConfig.RoyaltyActive, policy);
            PawnDiaryRimTestScope.Require((throne != null)
                    == (ModsConfig.IdeologyActive && ModsConfig.RoyaltyActive),
                "Loaded throne-speech enrichment did not require both owning DLCs.");
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(policy.speakerProjection.promptInstruction)
                    && !string.IsNullOrWhiteSpace(policy.witnessProjection.promptInstruction),
                "Authority-speech DefInjected prompt policy was not loaded.");
        }

        /// <summary>
        /// Ideology Phase 2. Drives the production ritual fan-out for exact leader speech and, when
        /// available, throne speech. It proves per-POV isolation, no duplicate page, RNG neutrality,
        /// original group ownership, frozen later-state behavior, and a real Scribe round-trip.
        /// </summary>
        [Test]
        public static void AuthoritySpeechPerspectivesAreBoundedFrozenAndKeepExistingOwners()
        {
            if (!RequireAuthorityIdeologyProfile()) return;

            Pawn witness = scope.CreateAdultColonist();
            Pawn spectator = scope.CreateAdultColonist();
            Ideo shared = GenerateRegisteredIdeology(firstPawn, witness, spectator);
            InstallAuthorityDoctrine(shared);
            firstPawn.ideo.SetIdeo(shared);
            witness.ideo.SetIdeo(shared);
            spectator.ideo.SetIdeo(shared);
            AssignExactLeader(shared, firstPawn);

            RitualFanoutSignal leaderFanout = null;
            const int Seed = 481237;
            Rand.PushState(Seed);
            float expectedNext = Rand.Value;
            Rand.PopState();
            Rand.PushState(Seed);
            try
            {
                leaderFanout = ExactAuthoritySpeechFixture(
                    false, firstPawn, witness, spectator);
                float actualNext = Rand.Value;
                PawnDiaryRimTestScope.Require(Math.Abs(actualNext - expectedNext) < 0.000001f,
                    "Authority-speech enrichment consumed global Rand.");
            }
            finally
            {
                Rand.PopState();
            }

            HashSet<string> before = SnapshotEventIds();
            scope.Component.Dispatch(leaderFanout);
            List<DiaryEvent> emitted = NewEventsSince(before);
            PawnDiaryRimTestScope.Require(emitted.Count == 3,
                "Leader speech should emit one unique speaker and two witness pages, got "
                    + emitted.Count + ".");
            DiaryEvent speakerPage = PageForPawn(emitted, firstPawn);
            DiaryEvent witnessPage = PageForPawn(emitted, witness);
            DiaryEvent spectatorPage = PageForPawn(emitted, spectator);
            string frozenSpeaker = speakerPage.BeliefContextForRole(DiaryEvent.InitiatorRole);
            string witnessContext = witnessPage.BeliefContextForRole(DiaryEvent.InitiatorRole);
            string spectatorContext = spectatorPage.BeliefContextForRole(DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(frozenSpeaker)
                    && frozenSpeaker.IndexOf("role:", StringComparison.Ordinal) >= 0
                    && frozenSpeaker.IndexOf("certainty:", StringComparison.Ordinal) >= 0,
                "Exact leader speaker did not freeze bounded current role/certainty context: "
                    + frozenSpeaker);
            RequireWitnessAuthorityIsolation(witnessContext, "participant witness");
            RequireWitnessAuthorityIsolation(spectatorContext, "spectator witness");

            AuthoritySpeechPolicySnapshot policy = DiaryAuthoritySpeechPolicy.Snapshot();
            RequireContains(speakerPage.instruction, policy.speakerProjection.promptInstruction);
            RequireContains(witnessPage.instruction, policy.witnessProjection.promptInstruction);
            DiaryPromptRequest leaderRequest = DiaryPipelineAdapters.BuildPromptRequest(
                speakerPage, DiaryEvent.InitiatorRole, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, titleRequest: false, maxTokens: 512);
            PawnDiaryRimTestScope.Require(
                leaderRequest?.policy?.group?.defName == ConversionRitualPolicy.LegacyGroupDefName,
                "Leader speech moved away from the ordinary ritual prompt/page owner.");

            int afterFirst = EventRepository().Count;
            scope.Component.Dispatch(leaderFanout);
            PawnDiaryRimTestScope.Require(EventRepository().Count == afterFirst,
                "A repeated authority-speech fan-out escaped the existing ritual dedup window.");
            firstPawn.ideo.OffsetCertainty(firstPawn.ideo.Certainty > 0.5f ? -0.05f : 0.05f);
            PawnDiaryRimTestScope.Require(
                speakerPage.BeliefContextForRole(DiaryEvent.InitiatorRole) == frozenSpeaker,
                "Later speaker certainty rewrote saved authority-speech context.");
            DiaryEvent loaded = PawnDiaryScribeRoundTripFixtureTests
                .ScribeRoundTripForTests(speakerPage);
            PawnDiaryRimTestScope.Require(
                loaded.BeliefContextForRole(DiaryEvent.InitiatorRole) == frozenSpeaker
                    && loaded.gameContext == speakerPage.gameContext,
                "Authority-speech event-time context did not survive a real Scribe round-trip.");

            if (!ModsConfig.RoyaltyActive) return;
            // Reuse the profile whose leader-speech pages just proved that visible authority-related
            // doctrine exists. A second generated Ideology is a doctrine lottery: it may truthfully
            // contain no matching stance, in which case production must leave the speech ordinary.
            // This half of the fixture is testing the exact throne route and retained Royal owner,
            // not asking an unrelated random profile to satisfy the enrichment precondition again.
            HashSet<string> throneBefore = SnapshotEventIds();
            RitualFanoutSignal throneFanout = ExactAuthoritySpeechFixture(
                true, firstPawn, witness, spectator);
            scope.Component.Dispatch(throneFanout);
            List<DiaryEvent> thronePages = NewEventsSince(throneBefore);
            PawnDiaryRimTestScope.Require(thronePages.Count == 3,
                "Throne speech should emit one speaker and two witness pages without duplication.");
            DiaryEvent thronePage = PageForPawn(thronePages, firstPawn);
            PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(
                    thronePage.BeliefContextForRole(DiaryEvent.InitiatorRole)),
                "Exact throne speech did not attach relevant authority context.");
            DiaryPromptRequest throneRequest = DiaryPipelineAdapters.BuildPromptRequest(
                thronePage, DiaryEvent.InitiatorRole, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, titleRequest: false, maxTokens: 512);
            PawnDiaryRimTestScope.Require(throneRequest?.policy?.group?.defName == "ritualRoyal",
                "Throne speech moved away from the Royal ritual prompt/page owner.");
        }

        /// <summary>Optional authority enrichment faults must preserve ordinary ritual pages.</summary>
        [Test]
        public static void AuthoritySpeechAdapterFailureKeepsOrdinaryRitualFanout()
        {
            if (!RequireAuthorityIdeologyProfile()) return;
            Pawn organizer = scope.CreateAdultColonist();
            Pawn witness = scope.CreateAdultColonist();
            Pawn spectator = scope.CreateAdultColonist();
            Ideo shared = GenerateRegisteredIdeology(organizer, witness, spectator);
            organizer.ideo.SetIdeo(shared);
            witness.ideo.SetIdeo(shared);
            spectator.ideo.SetIdeo(shared);
            AssignExactLeader(shared, organizer);

            AuthoritySpeechEvidenceAdapter.SetFailureForTests(true);
            try
            {
                HashSet<string> before = SnapshotEventIds();
                RitualFanoutSignal fanout = ExactAuthoritySpeechFixture(
                    false, organizer, witness, spectator);
                scope.Component.Dispatch(fanout);
                List<DiaryEvent> ordinary = NewEventsSince(before);
                PawnDiaryRimTestScope.Require(ordinary.Count == 3,
                    "Authority adapter failure suppressed the ordinary ritual fan-out.");
                for (int i = 0; i < ordinary.Count; i++)
                    PawnDiaryRimTestScope.Require(string.IsNullOrEmpty(
                            ordinary[i].BeliefContextForRole(DiaryEvent.InitiatorRole)),
                        "Authority adapter failure left partial belief enrichment.");
            }
            finally
            {
                AuthoritySpeechEvidenceAdapter.SetFailureForTests(false);
            }
        }

        /// <summary>
        /// EVT-17 / Anomaly. Drives the production psychic-ritual fan-out through invoker, target,
        /// participant, and spectator pages without constructing or firing a real colony ritual.
        /// Duplicate assignment rows must collapse by pawn ID and the repeat dispatch must be inert.
        /// </summary>
        [Test]
        public static void PsychicRitualFixtureFanoutEmitsUniquePerspectivePagesOnce()
        {
            Pawn target = scope.CreateAdultColonist();
            Pawn participant = scope.CreateAdultColonist();
            Pawn spectator = scope.CreateAdultColonist();
            HashSet<string> before = SnapshotEventIds();

            PsychicRitualFanoutSignal fanout = PsychicRitualFanoutSignal.CreateTestFixture(
                firstPawn,
                target,
                new List<Pawn> { firstPawn, participant },
                new List<Pawn> { participant, spectator },
                "VoidProvocation",
                "void provocation",
                0.91f,
                "fixture psychic ritual theme");
            PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(fanout.ColonyDedupKey),
                "The psychic ritual fixture did not create a colony dedup key.");

            scope.Component.Dispatch(fanout);
            List<DiaryEvent> emitted = NewEventsSince(before);
            PawnDiaryRimTestScope.Require(emitted.Count == 4,
                "The psychic ritual fixture should emit exactly four unique perspective pages, got "
                + emitted.Count + ".");
            RequirePerspectiveEvent(emitted, "psychic_ritual_perspective=" + RitualEventData.PerspectiveInvoker, firstPawn);
            RequirePerspectiveEvent(emitted, "psychic_ritual_perspective=" + RitualEventData.PerspectiveTarget, target);
            RequirePerspectiveEvent(emitted, "psychic_ritual_perspective=" + RitualEventData.PerspectiveParticipant, participant);
            RequirePerspectiveEvent(emitted, "psychic_ritual_perspective=" + RitualEventData.PerspectiveSpectator, spectator);

            int afterFirst = EventRepository().Count;
            scope.Component.Dispatch(fanout);
            PawnDiaryRimTestScope.Require(EventRepository().Count == afterFirst,
                "A repeated psychic ritual fan-out escaped the colony dedup window.");
        }

        /// <summary>
        /// EVT-17. The explicit DLC gate. The Ideology ritual context embeds each pawn's royal title
        /// (Royalty) and ideological role (Ideology) via the double-guarded DlcContext accessors. This
        /// asserts the base-game-safety contract those fields depend on: when the owning DLC is absent
        /// the accessor is a clean, non-null empty string (never a crash), and the assembled ritual
        /// marker degrades to the "none" fallback rather than a malformed field. When a DLC is present
        /// the accessor is still non-null (the test pawn simply holds no title/role). No state is created
        /// on either branch, so the no-op path is a true "not applicable" pass.
        /// </summary>
        [Test]
        public static void RitualDlcContextFieldsAreGatedAndBaseGameSafe()
        {
            string royalTitle = DlcContext.RoyalTitle(firstPawn);
            string ideologicalRole = DlcContext.IdeologicalRole(firstPawn);

            PawnDiaryRimTestScope.Require(
                royalTitle != null && ideologicalRole != null,
                "DLC context accessors must never return null for the ritual context.");

            if (!ModsConfig.RoyaltyActive)
            {
                PawnDiaryRimTestScope.Require(
                    royalTitle.Length == 0,
                    "Without Royalty, the ritual royal-title context must be empty (not applicable).");
            }

            if (!ModsConfig.IdeologyActive)
            {
                PawnDiaryRimTestScope.Require(
                    ideologicalRole.Length == 0,
                    "Without Ideology, the ritual ideological-role context must be empty (not applicable).");
            }

            // The assembled marker must stay well-formed even when both DLC fields are empty: the pure
            // builder substitutes the "none" fallback, so a base-game colony never emits a broken field.
            string context = RitualEventData.BuildGameContext(
                RitualDefName,
                RitualTitle,
                RitualBehavior,
                RitualEventData.PerspectiveParticipant,
                RitualFanoutSignal.RitualPerspectiveLabel(RitualEventData.PerspectiveParticipant),
                royalTitle,
                ideologicalRole,
                RitualFanoutSignal.RitualOutcomeFinished,
                RitualEventData.QualityLabel(0.5f, DiaryTuning.Current.ritualQualityBands));

            if (royalTitle.Length == 0)
            {
                RequireContains(context, "royal_title=none");
            }

            if (ideologicalRole.Length == 0)
            {
                RequireContains(context, "ideological_role=none");
            }
        }

        // ----- helpers ---------------------------------------------------------------------------

        private static bool RequireAuthorityIdeologyProfile()
        {
            if (!ModsConfig.IdeologyActive)
            {
                AuthoritySpeechPolicySnapshot policy = DiaryAuthoritySpeechPolicy.Snapshot();
                PawnDiaryRimTestScope.Require(
                    AuthoritySpeechPolicy.Match(
                        "LeaderSpeech", "RimWorld.RitualBehaviorWorker_Speech",
                        "RimWorld.RitualOutcomeEffectWorker_Speech",
                        ConversionRitualPolicy.LegacyGroupDefName, "speaker",
                        false, ModsConfig.RoyaltyActive, policy) == null,
                    "The no-Ideology authority-speech route did not fail closed.");
                return false;
            }
            if (Find.IdeoManager?.classicMode == true)
            {
                Log.Message("[PawnDiary RimTest Ritual] Authority-speech doctrine fixture not "
                    + "applicable in classic Ideology mode.");
                return false;
            }
            PawnDiaryRimTestScope.Require(firstPawn?.ideo != null,
                "The Ideology-active authority-speech fixture pawn did not expose a tracker.");
            return true;
        }

        private static void AssignExactLeader(Ideo ideology, Pawn pawn)
        {
            Precept_Role leader = ideology?.RolesListForReading.FirstOrDefault(role =>
                role?.def?.defName == "IdeoRole_Leader"
                    && role.ChosenPawnSingle() == null && role.RequirementsMet(pawn));
            PawnDiaryRimTestScope.Require(leader != null,
                "The registered authority-speech Ideology exposed no assignable leader role.");
            leader.Assign(pawn, false);
            scope.RegisterCleanup(() =>
            {
                if (leader.IsAssigned(pawn)) leader.Unassign(pawn, false);
            });
        }

        /// <summary>
        /// Replaces the generated Slavery stance with one installed, visibly authority-related stance.
        /// Randomly generated Ideologies may truthfully contain no doctrine matched by authority_speech;
        /// a positive integration fixture must establish that precondition instead of hoping for it.
        /// </summary>
        private static void InstallAuthorityDoctrine(Ideo ideology)
        {
            PreceptDef target = DefDatabase<PreceptDef>.GetNamedSilentFail("Slavery_Acceptable");
            PawnDiaryRimTestScope.Require(ideology != null && target?.issue != null
                    && Faction.OfPlayer?.def != null,
                "The installed authority-speech doctrine fixture Defs were not available.");

            // New to RimWorld Ideology tests? An Ideo may hold only one stance for an IssueDef. Remove
            // the generated Slavery stance before asking vanilla's PreceptMaker/AddPrecept path to add
            // the exact fixture stance; the disposable Ideo is removed wholesale during scope cleanup.
            Precept existing = ideology.PreceptsListForReading.FirstOrDefault(precept =>
                precept?.def?.issue == target.issue);
            if (existing != null) ideology.RemovePrecept(existing, false);
            Precept authority = PreceptMaker.MakePrecept(target);
            ideology.AddPrecept(authority, false, Faction.OfPlayer.def, null);
            PawnDiaryRimTestScope.Require(
                ideology.PreceptsListForReading.Any(precept => precept?.def == target),
                "The disposable authority-speech Ideology did not retain Slavery_Acceptable.");
        }

        private static RitualFanoutSignal ExactAuthoritySpeechFixture(
            bool throne, Pawn speaker, Pawn witness, Pawn spectator)
        {
            string groupKey = throne ? "ritualRoyal" : ConversionRitualPolicy.LegacyGroupDefName;
            DiaryInteractionGroupDef group = InteractionGroups.ByKey(groupKey);
            PawnDiaryRimTestScope.Require(group != null,
                "The existing authority-speech owner group was not loaded: " + groupKey);
            string defName = throne ? "ThroneSpeech" : "LeaderSpeech";
            string behavior = throne
                ? "RitualBehaviorWorker_ThroneSpeech"
                : "RitualBehaviorWorker_Speech";
            string behaviorType = throne
                ? "RimWorld.RitualBehaviorWorker_ThroneSpeech"
                : "RimWorld.RitualBehaviorWorker_Speech";
            RitualFanoutSignal signal = RitualFanoutSignal.CreateTestFixture(
                speaker,
                null,
                // Vanilla Participants also contains spectators. Keeping that membership proves the
                // existing order/dedup behavior is unchanged; both non-speaker modes are equally bounded.
                new List<Pawn> { speaker, witness, spectator },
                new List<Pawn> { spectator },
                defName,
                throne ? "throne speech" : "leader speech",
                behavior,
                0.9f,
                group.instruction,
                "RitualOutcomeEffectWorker_Speech",
                behaviorType,
                "RimWorld.RitualOutcomeEffectWorker_Speech",
                "speaker");
            PawnDiaryRimTestScope.Require(signal != null
                    && !string.IsNullOrWhiteSpace(signal.ColonyDedupKey),
                "The exact authority-speech fixture did not build a valid fan-out.");
            return signal;
        }

        private static DiaryEvent PageForPawn(List<DiaryEvent> events, Pawn expectedPawn)
        {
            string pawnId = expectedPawn?.GetUniqueLoadID() ?? string.Empty;
            DiaryEvent match = events.SingleOrDefault(candidate =>
                string.Equals(candidate?.initiatorPawnId, pawnId, StringComparison.Ordinal));
            PawnDiaryRimTestScope.Require(match != null,
                "No unique ritual page belonged to pawn '" + pawnId + "'.");
            scope.RequireSoloRef(match, expectedPawn);
            return match;
        }

        private static void RequireWitnessAuthorityIsolation(string context, string label)
        {
            PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(context),
                "The " + label + " received no shared authority doctrine.");
            PawnDiaryRimTestScope.Require(context.Length <= 320
                    && context.IndexOf("role:", StringComparison.Ordinal) < 0
                    && context.IndexOf("certainty:", StringComparison.Ordinal) < 0
                    && context.IndexOf("relevant meme:", StringComparison.Ordinal) < 0
                    && context.IndexOf("structure:", StringComparison.Ordinal) < 0
                    && context.IndexOf("deity:", StringComparison.Ordinal) < 0,
                "The " + label + " inherited speaker-only or oversized authority context: " + context);
        }

        private static bool RequireMutableIdeologyProfile()
        {
            if (!ModsConfig.IdeologyActive)
            {
                PawnDiaryRimTestScope.Require(
                    InteractionGroups.ClassifyRitual(
                        "Conversion;RitualBehaviorWorker_Conversion")?.defName
                        == ConversionRitualPolicy.LegacyGroupDefName
                        && BeliefMutationCache.Count == 0,
                    "The base-only conversion ritual path was not an inert generic fallback.");
                return false;
            }
            if (Find.IdeoManager?.classicMode == true)
            {
                Log.Message("[PawnDiary RimTest Ritual] Conversion mutation fixture not applicable "
                    + "in classic Ideology mode.");
                return false;
            }
            PawnDiaryRimTestScope.Require(firstPawn?.ideo != null,
                "The Ideology-active ritual fixture pawn did not expose a tracker.");
            return true;
        }

        /// <summary>
        /// Generates an Ideology, registers it with the loaded game's manager, and restores every named
        /// user before removing it. Registration matters here: the test exercises full belief projection
        /// and a Scribe round-trip, both of which require the Ideology to be real managed save state.
        /// </summary>
        private static Ideo GenerateRegisteredIdeology(params Pawn[] users)
        {
            IdeoManager manager = Find.IdeoManager;
            PawnDiaryRimTestScope.Require(manager != null,
                "The conversion-ritual fixture did not expose a loaded IdeoManager.");
            Ideo generated = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms
            {
                forFaction = Faction.OfPlayer.def,
                fixedIdeo = true
            });
            PawnDiaryRimTestScope.Require(generated != null && manager.Add(generated),
                "RimWorld could not register a conversion-ritual ideoligion fixture.");

            List<Pawn> restorePawns = new List<Pawn>();
            List<Ideo> originalIdeologies = new List<Ideo>();
            if (users != null)
            {
                for (int i = 0; i < users.Length; i++)
                {
                    Pawn user = users[i];
                    if (user == null || restorePawns.Contains(user)) continue;
                    restorePawns.Add(user);
                    originalIdeologies.Add(user.ideo?.Ideo);
                }
            }

            scope.RegisterCleanup(() =>
            {
                Ideo fallback = manager.IdeosListForReading.FirstOrDefault(value =>
                    value != null && !ReferenceEquals(value, generated));
                for (int i = 0; i < restorePawns.Count; i++)
                {
                    Pawn user = restorePawns[i];
                    if (!ReferenceEquals(user?.ideo?.Ideo, generated)) continue;
                    Ideo restore = originalIdeologies[i] ?? fallback;
                    if (restore != null) user.ideo.SetIdeo(restore);
                }

                BeliefMutationCache.Reset();
                if (manager.IdeosListForReading.Contains(generated)) manager.Remove(generated);
                BeliefMutationCache.Reset();
            });
            return generated;
        }

        private static RitualFanoutSignal ExactConversionFixture(
            Pawn target, Pawn participant, Pawn spectator, float progress, Pawn organizer = null)
        {
            DiaryInteractionGroupDef group = InteractionGroups.ByKey(
                ConversionRitualPolicy.GroupDefName);
            PawnDiaryRimTestScope.Require(group != null,
                "The exact conversion ritual group was not loaded.");
            Pawn effectiveOrganizer = organizer ?? firstPawn;
            RitualFanoutSignal signal = RitualFanoutSignal.CreateTestFixture(
                effectiveOrganizer,
                target,
                // Live RitualRoleAssignments.Participants contains spectators too. Mirroring that
                // membership here makes adapter-failure coverage exercise the real role-order trap.
                new List<Pawn> { effectiveOrganizer, participant, spectator },
                new List<Pawn> { spectator },
                "Conversion",
                "conversion ritual",
                "RitualBehaviorWorker_Conversion",
                progress,
                group.instruction,
                "RitualOutcomeEffectWorker_Conversion",
                typeof(RitualBehaviorWorker_Conversion).FullName,
                typeof(RitualOutcomeEffectWorker_Conversion).FullName);
            PawnDiaryRimTestScope.Require(signal != null
                    && !string.IsNullOrWhiteSpace(signal.ColonyDedupKey),
                "The exact conversion ritual fixture did not build a valid fan-out.");
            return signal;
        }

        private static DiaryEvent PageForPerspective(
            List<DiaryEvent> events, string perspective, Pawn expectedPawn)
        {
            string marker = "ritual_perspective=" + perspective;
            DiaryEvent match = events.SingleOrDefault(candidate => candidate?.gameContext != null
                && candidate.gameContext.IndexOf(marker, StringComparison.Ordinal) >= 0);
            PawnDiaryRimTestScope.Require(match != null,
                "No exact conversion ritual page carried '" + marker + "'.");
            scope.RequireSoloRef(match, expectedPawn);
            return match;
        }

        private static void AssertNoTargetMutationLeak(DiaryEvent page)
        {
            string context = (page?.gameContext ?? string.Empty) + "\n"
                + (page?.BeliefContextForRole(DiaryEvent.InitiatorRole) ?? string.Empty);
            PawnDiaryRimTestScope.Require(
                context.IndexOf("conversion_ritual_result=", StringComparison.Ordinal) < 0
                    && context.IndexOf("belief_event=conversion", StringComparison.Ordinal) < 0
                    && context.IndexOf("before ideoligion:", StringComparison.OrdinalIgnoreCase) < 0
                    && context.IndexOf("after ideoligion:", StringComparison.OrdinalIgnoreCase) < 0
                    && context.IndexOf("conversion result:", StringComparison.OrdinalIgnoreCase) < 0
                    && context.IndexOf("mutation cause:", StringComparison.OrdinalIgnoreCase) < 0,
                "A non-target conversion ritual page received the convertee's mutation facts: "
                    + context);
        }

        private static HashSet<string> SnapshotEventIds()
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<DiaryEvent> events = EventRepository().AllEvents;
            for (int i = 0; i < events.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(events[i]?.eventId))
                {
                    ids.Add(events[i].eventId);
                }
            }

            return ids;
        }

        private static List<DiaryEvent> NewEventsSince(HashSet<string> before)
        {
            List<DiaryEvent> result = new List<DiaryEvent>();
            IReadOnlyList<DiaryEvent> events = EventRepository().AllEvents;
            for (int i = 0; i < events.Count; i++)
            {
                DiaryEvent diaryEvent = events[i];
                if (diaryEvent != null && !before.Contains(diaryEvent.eventId))
                {
                    result.Add(diaryEvent);
                }
            }

            return result;
        }

        private static DiaryEventRepository EventRepository()
        {
            DiaryEventRepository repository = EventsField?.GetValue(scope.Component) as DiaryEventRepository;
            if (repository == null)
            {
                throw new AssertionException("EVT-17 could not read DiaryGameComponent.events.");
            }

            return repository;
        }

        private static void RequirePerspectiveEvent(
            List<DiaryEvent> events,
            string contextMarker,
            Pawn expectedPawn)
        {
            DiaryEvent match = null;
            for (int i = 0; i < events.Count; i++)
            {
                DiaryEvent candidate = events[i];
                if (candidate?.gameContext != null
                    && candidate.gameContext.IndexOf(contextMarker, StringComparison.Ordinal) >= 0)
                {
                    PawnDiaryRimTestScope.Require(match == null,
                        "More than one ritual page carried perspective marker '" + contextMarker + "'.");
                    match = candidate;
                }
            }

            PawnDiaryRimTestScope.Require(match != null,
                "No ritual page carried perspective marker '" + contextMarker + "'.");
            scope.RequireSoloRef(match, expectedPawn);
        }

        private static void RequireContains(string haystack, string needle)
        {
            PawnDiaryRimTestScope.Require(
                haystack != null && haystack.IndexOf(needle, StringComparison.Ordinal) >= 0,
                "Expected the ritual context to contain '" + needle + "', but it was: " + haystack);
        }

        // Asserts the given markers appear in the string in strictly increasing position order. This
        // guards the field-order contract the prompt policies and downstream tests rely on.
        private static void RequireOrdered(string haystack, params string[] markers)
        {
            int previous = -1;
            for (int i = 0; i < markers.Length; i++)
            {
                int at = haystack == null ? -1 : haystack.IndexOf(markers[i], StringComparison.Ordinal);
                PawnDiaryRimTestScope.Require(
                    at > previous,
                    "Ritual context field '" + markers[i] + "' was missing or out of order in: " + haystack);
                previous = at;
            }
        }

        private static void RequirePairwiseDistinct(string[] values, string label)
        {
            for (int i = 0; i < values.Length; i++)
            {
                for (int j = i + 1; j < values.Length; j++)
                {
                    PawnDiaryRimTestScope.Require(
                        !string.Equals(values[i], values[j], StringComparison.Ordinal),
                        "Two " + label + " collapsed to the same string ('" + values[i] + "').");
                }
            }
        }
    }
}

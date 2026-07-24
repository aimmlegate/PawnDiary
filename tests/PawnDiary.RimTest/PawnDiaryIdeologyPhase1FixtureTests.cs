// Loaded-game coverage for Ideology Phase 1's guarded projection and non-emitting correlation seam.
// These fixtures use disposable pawns and real loaded Ideo/Precept/Thought/HistoryEvent objects, but
// assert only on the detached contracts that production code exposes. No reflection behavior or
// Phase 2 mutation page is exercised here.
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
    /// <summary>
    /// Proves exact Thought.sourcePrecept capture, one event-time context write, and a bounded
    /// HistoryEventsManager observer that enriches evidence without creating a diary page.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryIdeologyPhase1FixtureTests
    {
        private const string LogPrefix = "[PawnDiary RimTest Ideology Phase 1] ";
        private const string BiotechPackageId = "Ludeon.RimWorld.Biotech";
        private const string AnomalyPackageId = "Ludeon.RimWorld.Anomaly";
        private const string OdysseyPackageId = "Ludeon.RimWorld.Odyssey";
        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("hediffPartGainedArtificial");
            pawn = scope.CreateAdultColonist();
            BeliefHistoryCorrelationCache.Reset();
            BeliefMutationCache.Reset();
            scope.RegisterCleanup(BeliefHistoryCorrelationCache.Reset);
            scope.RegisterCleanup(BeliefMutationCache.Reset);
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
            }
        }

        /// <summary>
        /// Builds a real precept-backed Thought_Memory. The signal must freeze exact source identity,
        /// and its one emitted page must retain a bounded event-time belief block.
        /// </summary>
        [Test]
        public static void ExactThoughtSourceReachesOneFrozenEventContext()
        {
            if (!ModsConfig.IdeologyActive)
            {
                PawnDiaryRimTestScope.Require(!DlcContext.CaptureBeliefSnapshot(pawn).ideologyActive,
                    "The Phase 1 snapshot must be inactive without Ideology.");
                Log.Message(LogPrefix + "exact thought source: not applicable (Ideology inactive). ");
                return;
            }

            Precept sourcePrecept;
            ThoughtDef thoughtDef;
            Ideo fixtureIdeo = CreateIdeoWithThoughtPrecept(pawn, out sourcePrecept, out thoughtDef);
            PawnDiaryRimTestScope.Require(fixtureIdeo != null && sourcePrecept != null && thoughtDef != null,
                "The active-Ideology fixture could not create a typed PreceptComp_Thought.");

            PawnDiaryRimTestScope.Require(pawn.ideo != null,
                "An Ideology-active colonist must have a Pawn_IdeoTracker.");
            pawn.ideo.SetIdeo(fixtureIdeo);
            Thought_Memory thought = ThoughtMaker.MakeThought(thoughtDef, sourcePrecept);
            scope.RegisterCleanup(() => pawn?.needs?.mood?.thoughts?.memories
                ?.RemoveMemoriesOfDef(thoughtDef));

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => pawn.needs.mood.thoughts.memories.TryGainMemory(thought, null),
                thoughtDef.defName,
                pawn,
                null);
            // Vanilla has now accepted and attached the memory. Constructing a detached signal without
            // emitting it lets this fixture inspect the same source-precept projection the Harmony
            // postfix used, while the event above proves the real TryGainMemory boundary fired once.
            ThoughtSignal capturedSignal = new ThoughtSignal(pawn, thought);
            BeliefEventEvidence evidence = capturedSignal.CapturedBeliefEvidence;
            PawnDiaryRimTestScope.Require(evidence != null
                    && evidence.sourcePreceptInstanceId == sourcePrecept.GetUniqueLoadID()
                    && evidence.sourcePreceptDefName == sourcePrecept.def.defName,
                "ThoughtSignal did not freeze Thought.sourcePrecept's exact instance and Def identity.");
            string context = diaryEvent.BeliefContextForRole(DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(context)
                    && context.Contains("relevant precept:")
                    && context.Length <= DiaryBeliefPolicy.Snapshot().maximumTotalCharacters,
                "The exact source-precept event did not retain one bounded event-time belief context.");
            PawnDiaryRimTestScope.Require(
                context == BeliefContextFormatter.NormalizeSaved(context, DiaryBeliefPolicy.Snapshot()),
                "The event-time belief block was not already in its stable saved form.");
            BeliefSnapshot snapshot = DlcContext.CaptureBeliefSnapshot(
                pawn, DiaryBeliefPolicy.Snapshot());
            string expectedKey = "ideology|interpretation|" + pawn.GetUniqueLoadID() + "|"
                + snapshot.ideologyId + "|instance|" + sourcePrecept.GetUniqueLoadID();
            List<NarrativeEvidence> narrativeEvidence = diaryEvent.NarrativeEvidenceForRole(
                DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(
                narrativeEvidence.Count == 1
                    && narrativeEvidence[0].eventId == diaryEvent.eventId
                    && narrativeEvidence[0].povPawnId == pawn.GetUniqueLoadID()
                    && narrativeEvidence[0].sourceDomain == "thought"
                    && narrativeEvidence[0].sourceDefName == thoughtDef.defName
                    && narrativeEvidence[0].facet == NarrativeFacetTokens.AmbientPressure
                    && diaryEvent.NarrativeSelectedCandidateKeysForRole(
                        DiaryEvent.InitiatorRole).Contains(expectedKey)
                    && !string.IsNullOrWhiteSpace(
                        diaryEvent.NarrativeContextForRole(DiaryEvent.InitiatorRole)),
                "The real Phase-1 source snapshot did not produce one exact N3-I interpretation lens.");
        }

        /// <summary>
        /// Proves the fixed provider list treats an unavailable Ideology provider as an empty result,
        /// leaving a non-Ideology candidate byte-for-byte unchanged. The base-only profile also checks
        /// the real guarded DlcContext inactive snapshot.
        /// </summary>
        [Test]
        public static void IdeologyUnavailableLeavesOtherNarrativeProvidersUnchanged()
        {
            if (!ModsConfig.IdeologyActive)
            {
                PawnDiaryRimTestScope.Require(!DlcContext.CaptureBeliefSnapshot(pawn).ideologyActive,
                    "The real base-only belief snapshot must remain inactive.");
            }

            string pawnId = pawn.GetUniqueLoadID();
            int tick = Find.TickManager.TicksGame;
            NarrativeEvidence evidence = new NarrativeEvidence
            {
                eventId = "ideology-inactive-composition",
                tick = tick,
                povPawnId = pawnId,
                povRole = DiaryEvent.InitiatorRole,
                facet = NarrativeFacetTokens.IdentityTransition,
                phase = "fixture",
                subjectKind = NarrativeSubjectKindTokens.Pawn,
                subjectId = pawnId,
                beliefTopics = new List<string> { "identity" },
                salience = NarrativeSalienceTokens.Meaningful,
                pawnCanKnow = true,
                sourceDomain = "fixture",
                sourceDefName = "OtherProvider"
            };
            NarrativeLensCandidate other = new NarrativeLensCandidate
            {
                candidateKey = "core|fixture|other-provider",
                provider = NarrativeProviderTokens.Core,
                category = NarrativeCategoryTokens.Identity,
                text = "A bounded non-Ideology fixture remains available.",
                facet = evidence.facet,
                subjectKind = evidence.subjectKind,
                subjectId = evidence.subjectId,
                sourceEventId = evidence.eventId,
                sourceTick = tick,
                salience = evidence.salience,
                pawnCanKnow = true,
                providerAvailable = true,
                hasVerifiedPovConnection = true
            };
            NarrativeContextBuildRequest baselineRequest = new NarrativeContextBuildRequest
            {
                eventId = evidence.eventId,
                eventTick = tick,
                povPawnId = pawnId,
                povRole = DiaryEvent.InitiatorRole,
                evidence = new List<NarrativeEvidence> { evidence },
                coreCandidates = new List<NarrativeLensCandidate> { other },
                contextDetailLevel = PromptContextDetailLevel.Full
            };
            NarrativeContextBuildResult baseline = NarrativeContextBuilder.Build(baselineRequest);
            baselineRequest.ideology = new IdeologyNarrativeSnapshot
            {
                providerAvailable = false,
                sourceEvidence = evidence
            };
            NarrativeContextBuildResult unavailable = NarrativeContextBuilder.Build(baselineRequest);
            PawnDiaryRimTestScope.Require(
                baseline.selection.narrativeContext == unavailable.selection.narrativeContext
                    && baseline.selection.selectedCandidates.Count == 1
                    && unavailable.selection.selectedCandidates.Count == 1
                    && baseline.selection.selectedCandidates[0].candidateKey
                        == unavailable.selection.selectedCandidates[0].candidateKey,
                "An unavailable Ideology provider changed another provider's ordinary selection.");
        }

        /// <summary>Locks the shared same-language policy snapshot used by frequent history hooks.</summary>
        [Test]
        public static void BeliefPolicySnapshotIsCachedForTheActiveLanguage()
        {
            BeliefPolicySnapshot first = DiaryBeliefPolicy.Snapshot();
            BeliefPolicySnapshot second = DiaryBeliefPolicy.Snapshot();
            DiaryBeliefPolicyDef loaded =
                DefDatabase<DiaryBeliefPolicyDef>.GetNamedSilentFail("Diary_BeliefPolicy");
            PawnDiaryRimTestScope.Require(ReferenceEquals(first, second),
                "Belief policy rebuilt its deep-copied snapshot inside one active language.");
            PawnDiaryRimTestScope.Require(DiaryBeliefPolicy.Enabled == first.enabled,
                "The fast Enabled gate disagreed with the cached belief-policy snapshot.");
            PawnDiaryRimTestScope.Require(loaded != null
                    && loaded.maximumAutomaticDiagnosticSamples == 4096
                    && first.maximumAutomaticDiagnosticSamples == 4096,
                "The loaded XML automatic-diagnostic limit did not reach the immutable snapshot.");
            PawnDiaryRimTestScope.Require(
                loaded.correlationOverrides == null || loaded.correlationOverrides.Count == 0,
                "The shipped exact belief-correction list must remain empty.");
        }

        /// <summary>
        /// Exercises the impure session holder without requiring Ideology content. The aggregate must
        /// reset cleanly and expose only fixed mechanical tokens from the loaded policy.
        /// </summary>
        [Test]
        public static void AutomaticCoverageDiagnosticsUseLoadedBoundAndReset()
        {
            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            BeliefContextBuilder.ResetAutomaticCoverageDiagnostics();
            scope.RegisterCleanup(BeliefContextBuilder.ResetAutomaticCoverageDiagnostics);
            PawnDiaryRimTestScope.Require(
                BeliefContextBuilder.AutomaticCoverageDiagnosticsForDev().Contains("observed=0"),
                "The automatic-coverage aggregate did not start empty.");

            BeliefContextBuilder.RecordAutomaticCoverageForTests(
                BeliefAutomaticCoverageDiagnostics.Accepted(
                    BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation,
                    BeliefRelevanceSourceTokens.ThoughtCorrelation,
                    BeliefRelevanceTierTokens.ExactCorrelation,
                    1),
                policy);
            string diagnostic = BeliefContextBuilder.AutomaticCoverageDiagnosticsForDev();
            PawnDiaryRimTestScope.Require(
                diagnostic.Contains("observed=1")
                    && diagnostic.Contains("last_outcome=exact_correlation")
                    && diagnostic.Contains("last_source=thought_correlation")
                    && diagnostic.Contains("last_tier=exact_correlation")
                    && diagnostic.Contains("rejection_reasons="),
                "The loaded automatic-coverage aggregate lost its fixed winner tokens.");

            BeliefPolicyBuilder boundedBuilder = BeliefPolicyBuilder.CreateDefault();
            boundedBuilder.maximumAutomaticDiagnosticSamples = 1;
            BeliefPolicySnapshot boundedPolicy = boundedBuilder.Build();
            BeliefContextBuilder.ResetAutomaticCoverageDiagnostics();
            BeliefContextBuilder.RecordAutomaticCoverageForTests(
                new BeliefAutomaticCoverageDiagnostic
                {
                    outcome = "Player-authored doctrine",
                    reason = "Private ideology text",
                    winnerSource = "Secret source",
                    winnerTier = "Diary prose",
                    confidenceBand = "Custom name",
                    confidence = float.NaN,
                    runnerUpGap = float.PositiveInfinity,
                    candidateCount = int.MaxValue
                },
                boundedPolicy);
            BeliefContextBuilder.RecordAutomaticCoverageForTests(
                BeliefAutomaticCoverageDiagnostics.Accepted(
                    BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation,
                    BeliefRelevanceSourceTokens.HistoryCorrelation,
                    BeliefRelevanceTierTokens.ExactCorrelation,
                    1),
                boundedPolicy);
            string bounded = BeliefContextBuilder.AutomaticCoverageDiagnosticsForDev();
            PawnDiaryRimTestScope.Require(
                bounded.Contains("observed=1") && bounded.Contains("dropped=1")
                    && bounded.IndexOf("Player-authored", StringComparison.Ordinal) < 0
                    && bounded.IndexOf("Private ideology", StringComparison.Ordinal) < 0
                    && bounded.IndexOf("Secret source", StringComparison.Ordinal) < 0
                    && bounded.IndexOf("Diary prose", StringComparison.Ordinal) < 0
                    && bounded.IndexOf("Custom name", StringComparison.Ordinal) < 0,
                "The loaded automatic-coverage holder was unbounded or exposed authored text.");

            BeliefContextBuilder.ResetAutomaticCoverageDiagnostics();
            PawnDiaryRimTestScope.Require(
                BeliefContextBuilder.AutomaticCoverageDiagnosticsForDev().Contains("observed=0"),
                "The automatic-coverage aggregate did not reset.");
        }

        /// <summary>
        /// Discovers one ordinary public HistoryEventDef/ThoughtDef component link from each active
        /// official DLC, projects it through the real guarded adapter, and proves exact metadata beats
        /// unrelated lexical bait without a production precept catalog or correction.
        /// </summary>
        [Test]
        public static void OfficialDlcPreceptMetadataResolvesStructurallyWithoutCorrections()
        {
            if (!ModsConfig.IdeologyActive)
            {
                PawnDiaryRimTestScope.Require(!DlcContext.CaptureBeliefSnapshot(pawn).ideologyActive,
                    "Official DLC belief metadata must remain inert when Ideology is inactive.");
                Log.Message(LogPrefix + "official DLC belief metadata: not applicable "
                    + "(Ideology inactive); guarded snapshot returned empty.");
                return;
            }

            VerifyOfficialDlcMetadata("Biotech", BiotechPackageId, ModsConfig.BiotechActive);
            VerifyOfficialDlcMetadata("Anomaly", AnomalyPackageId, ModsConfig.AnomalyActive);
            VerifyOfficialDlcMetadata("Odyssey", OdysseyPackageId, ModsConfig.OdysseyActive);
        }

        /// <summary>
        /// Compiled loaded-policy coverage for the conservative negative paths: equal lexical candidates
        /// are ambiguous, an unrelated ordinary event stays empty, and an unavailable DLC snapshot no-ops.
        /// </summary>
        [Test]
        public static void AutomaticCoverageWeakAmbiguousAndUnavailableEvidenceStaysEmpty()
        {
            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            string pawnId = pawn.GetUniqueLoadID();
            int tick = Find.TickManager.TicksGame;
            BeliefPreceptFact first = DetachedPrecept(
                "FixtureDoctrineA91", "FixtureIssueA91", "crystal mercy iron duty");
            BeliefPreceptFact second = DetachedPrecept(
                "FixtureDoctrineB72", "FixtureIssueB72", "crystal mercy iron duty");
            BeliefSnapshot ambiguousSnapshot = DetachedSnapshot(pawnId, tick, first, second);
            BeliefEventEvidence ambiguousEvidence = BeliefEventEvidenceFactory.ForEvent(
                pawnId, tick, "fixture", "EventR53", DiaryEvent.InitiatorRole,
                "crystal mercy iron duty", string.Empty);
            BeliefStanceResolution ambiguous = EventRelativeStanceResolver.Resolve(
                new BeliefResolutionRequest
                {
                    snapshot = ambiguousSnapshot,
                    evidence = ambiguousEvidence,
                    policy = policy,
                    mode = BeliefResolutionModeTokens.EventEnrichment
                });
            PawnDiaryRimTestScope.Require(
                ambiguous.stances.Count == 0
                    && ambiguous.automaticCoverage.outcome
                        == BeliefAutomaticCoverageOutcomeTokens.Ambiguous
                    && ambiguous.automaticCoverage.reason
                        == BeliefAutomaticCoverageReasonTokens.RunnerUpAmbiguity
                    && ambiguous.automaticCoverage.candidateCount == 2,
                "Equal loaded-policy lexical candidates did not fail closed as ambiguous.");

            BeliefSnapshot unrelatedSnapshot = DetachedSnapshot(
                pawnId, tick, DetachedPrecept(
                    "DoctrineK41", "IssueK41", "silent basalt orchard"));
            BeliefEventEvidence unrelatedEvidence = BeliefEventEvidenceFactory.ForEvent(
                pawnId, tick, "fixture", "EventQ72", DiaryEvent.InitiatorRole,
                "cobalt river", string.Empty);
            BeliefStanceResolution unrelated = EventRelativeStanceResolver.Resolve(
                new BeliefResolutionRequest
                {
                    snapshot = unrelatedSnapshot,
                    evidence = unrelatedEvidence,
                    policy = policy,
                    mode = BeliefResolutionModeTokens.EventEnrichment
                });
            PawnDiaryRimTestScope.Require(
                unrelated.stances.Count == 0
                    && unrelated.automaticCoverage.outcome
                        == BeliefAutomaticCoverageOutcomeTokens.NoMatch
                    && string.IsNullOrEmpty(BeliefContextFormatter.Format(
                        unrelated, NarrativeDetailLevelTokens.Full, policy)),
                "Weak unrelated ordinary evidence gained belief context.");

            unrelatedSnapshot.ideologyActive = false;
            BeliefStanceResolution unavailable = EventRelativeStanceResolver.Resolve(
                new BeliefResolutionRequest
                {
                    snapshot = unrelatedSnapshot,
                    evidence = unrelatedEvidence,
                    policy = policy,
                    mode = BeliefResolutionModeTokens.EventEnrichment
                });
            PawnDiaryRimTestScope.Require(
                unavailable.stances.Count == 0
                    && unavailable.automaticCoverage.reason
                        == BeliefAutomaticCoverageReasonTokens.UnavailableSnapshot,
                "A DLC-unavailable detached snapshot did not cleanly no-op.");
        }

        /// <summary>
        /// Forces the guarded live snapshot boundary to throw and proves optional enrichment cannot
        /// cancel the already-authorized ordinary body-change page.
        /// </summary>
        [Test]
        public static void BeliefEnrichmentFailureKeepsTheOrdinaryEvent()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix + "enrichment failure isolation: not applicable (Ideology inactive). ");
                return;
            }

            Ideo fixture = Find.IdeoManager?.IdeosListForReading?.FirstOrDefault(ideo => ideo != null);
            PawnDiaryRimTestScope.Require(fixture != null && pawn.ideo != null,
                "The failure-isolation fixture needs one loaded ideoligion.");
            pawn.ideo.SetIdeo(fixture);
            HediffDef pegLeg = DefDatabase<HediffDef>.GetNamedSilentFail("PegLeg");
            BodyPartRecord leg = pawn.health.hediffSet.GetNotMissingParts()
                .FirstOrDefault(part => part?.def?.defName == "Leg");
            PawnDiaryRimTestScope.Require(pegLeg != null && leg != null,
                "The failure-isolation fixture needs the vanilla peg leg and a leg.");
            Hediff hediff = HediffMaker.MakeHediff(pegLeg, pawn);
            scope.RegisterCleanup(() =>
            {
                if (pawn?.health?.hediffSet?.hediffs?.Contains(hediff) == true)
                    pawn.health.RemoveHediff(hediff);
            });

            System.Reflection.MethodInfo target = AccessTools.Method(
                typeof(DlcContext),
                nameof(DlcContext.CaptureBeliefSnapshot),
                new[] { typeof(Pawn), typeof(BeliefPolicySnapshot) });
            PawnDiaryRimTestScope.Require(target != null,
                "Could not resolve the guarded belief snapshot overload for the failure fixture.");
            Harmony harmony = new Harmony("PawnDiary.RimTest.BeliefFailure." + Guid.NewGuid().ToString("N"));
            harmony.Patch(target, prefix: new HarmonyMethod(
                typeof(PawnDiaryIdeologyPhase1FixtureTests), nameof(ThrowBeliefSnapshotFixture)));
            try
            {
                DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                    () => pawn.health.AddHediff(hediff, leg), pegLeg.defName, pawn, null);
                PawnDiaryRimTestScope.Require(
                    string.IsNullOrEmpty(diaryEvent.BeliefContextForRole(DiaryEvent.InitiatorRole)),
                    "A failed optional belief snapshot left partial context on the ordinary page.");
                PawnDiaryRimTestScope.Require(
                    string.IsNullOrEmpty(diaryEvent.NarrativeContextForRole(DiaryEvent.InitiatorRole))
                        && diaryEvent.NarrativeSelectedCandidateKeysForRole(
                            DiaryEvent.InitiatorRole).Count == 0,
                    "A failed resolver snapshot left a partial N3-I candidate on the ordinary page.");
            }
            finally
            {
                harmony.Unpatch(target, HarmonyPatchType.Prefix, harmony.Id);
            }
        }

        /// <summary>
        /// Forces only the pure Ideology provider to fail after the real Phase-1 resolver succeeds.
        /// The canonical thought page and its saved belief block must survive with no partial lens.
        /// </summary>
        [Test]
        public static void IdeologyProviderFailureKeepsTheCanonicalThoughtPage()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix + "provider failure isolation: not applicable (Ideology inactive). ");
                return;
            }

            Precept sourcePrecept;
            ThoughtDef thoughtDef;
            Ideo fixtureIdeo = CreateIdeoWithThoughtPrecept(pawn, out sourcePrecept, out thoughtDef);
            PawnDiaryRimTestScope.Require(fixtureIdeo != null && sourcePrecept != null && thoughtDef != null,
                "The provider-failure fixture could not create a typed PreceptComp_Thought.");
            pawn.ideo.SetIdeo(fixtureIdeo);
            Thought_Memory thought = ThoughtMaker.MakeThought(thoughtDef, sourcePrecept);
            thought.pawn = pawn;
            ThoughtSignal signal = new ThoughtSignal(pawn, thought);

            System.Reflection.MethodInfo target = AccessTools.Method(
                typeof(IdeologyNarrativeProvider),
                nameof(IdeologyNarrativeProvider.Build),
                new[] { typeof(List<NarrativeEvidence>), typeof(IdeologyNarrativeSnapshot) });
            PawnDiaryRimTestScope.Require(target != null,
                "Could not resolve the pure Ideology provider for failure isolation.");
            Harmony harmony = new Harmony(
                "PawnDiary.RimTest.IdeologyProviderFailure." + Guid.NewGuid().ToString("N"));
            harmony.Patch(target, prefix: new HarmonyMethod(
                typeof(PawnDiaryIdeologyPhase1FixtureTests), nameof(ThrowIdeologyProviderFixture)));
            try
            {
                DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                    () => signal.Emit(scope.Component, CaptureDecision.GenerateSolo),
                    thoughtDef.defName,
                    pawn,
                    null);
                PawnDiaryRimTestScope.Require(
                    !string.IsNullOrWhiteSpace(
                        diaryEvent.BeliefContextForRole(DiaryEvent.InitiatorRole))
                        && string.IsNullOrWhiteSpace(
                            diaryEvent.NarrativeContextForRole(DiaryEvent.InitiatorRole))
                        && diaryEvent.NarrativeSelectedCandidateKeysForRole(
                            DiaryEvent.InitiatorRole).Count == 0,
                    "An Ideology provider exception cancelled the page or left a partial lens.");
            }
            finally
            {
                harmony.Unpatch(target, HarmonyPatchType.Prefix, harmony.Id);
            }
        }

        /// <summary>
        /// Drives the real AddHediff hook with vanilla situational precepts whose opposite workers share
        /// one doctrine. Exact active ThoughtDef truth must retain the legacy approval/rejection cue.
        /// </summary>
        [Test]
        public static void VanillaBodyModificationWorkersPreserveAttitudeParity()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix + "body-mod stance parity: not applicable (Ideology inactive). ");
                return;
            }

            AssertBodyModificationAttitude("BodyMod_Approved", "approves");
        }

        /// <summary>Companion negative worker fixture for vanilla body-mod rejection.</summary>
        [Test]
        public static void VanillaBodyModificationNegativeWorkerPreservesAttitudeParity()
        {
            if (!ModsConfig.IdeologyActive)
            {
                Log.Message(LogPrefix + "negative body-mod stance parity: not applicable (Ideology inactive). ");
                return;
            }

            AssertBodyModificationAttitude("BodyMod_Disapproved", "despises");
        }

        /// <summary>
        /// Calls the real vanilla manager with an uncorrelated event. The Harmony observer must add only
        /// one plain cache row and never emit a DiaryEvent; a separately projected exact live correlation
        /// must then reach the next authorized builder pass as structural evidence.
        /// </summary>
        [Test]
        public static void HistoryObserverIsBoundedNonEmittingAndStructurallyUseful()
        {
            HistoryEventDef historyDef = DefDatabase<HistoryEventDef>.AllDefsListForReading
                .FirstOrDefault(def => def != null && !string.IsNullOrWhiteSpace(def.defName));
            PawnDiaryRimTestScope.Require(historyDef != null && Find.HistoryEventsManager != null,
                "The loaded game must expose a HistoryEventDef and HistoryEventsManager.");
            string correlatedDefName = string.Empty;

            if (ModsConfig.IdeologyActive)
            {
                Ideo fixtureIdeo = Find.IdeoManager?.IdeosListForReading?.FirstOrDefault(ideo => ideo != null);
                if (fixtureIdeo != null && pawn.ideo != null)
                {
                    pawn.ideo.SetIdeo(fixtureIdeo);
                    BeliefSnapshot snapshot = DlcContext.CaptureBeliefSnapshot(pawn);
                    HashSet<string> correlatedHistoryDefs = new HashSet<string>(
                        snapshot.precepts
                            .Where(precept => precept?.correlations != null)
                            .SelectMany(precept => precept.correlations)
                            .Where(correlation => correlation != null
                                && correlation.kind == BeliefCorrelationKindTokens.HistoryEvent
                                && !string.IsNullOrWhiteSpace(correlation.defName))
                            .Select(correlation => correlation.defName),
                        StringComparer.OrdinalIgnoreCase);
                    correlatedDefName = snapshot.precepts
                        .Where(precept => precept?.visible == true && precept.correlations != null)
                        .SelectMany(precept => precept.correlations)
                        .FirstOrDefault(correlation => correlation != null
                            && correlation.kind == BeliefCorrelationKindTokens.HistoryEvent
                            && !string.IsNullOrWhiteSpace(correlation.defName)
                            && DefDatabase<HistoryEventDef>.GetNamedSilentFail(correlation.defName) != null)?.defName
                        ?? string.Empty;

                    // A correlated vanilla event can legitimately grant a visible Thought_Memory, whose
                    // ordinary Thought hook owns its own page. Use an uncorrelated Def for the real manager
                    // call so "observer emitted nothing" is measured without suppressing that valid route.
                    HistoryEventDef uncorrelated = DefDatabase<HistoryEventDef>.AllDefsListForReading
                        .FirstOrDefault(def => def != null && !string.IsNullOrWhiteSpace(def.defName)
                            && !correlatedHistoryDefs.Contains(def.defName));
                    PawnDiaryRimTestScope.Require(uncorrelated != null,
                        "The loaded Ideology fixture needs one HistoryEventDef unrelated to its current precepts.");
                    historyDef = uncorrelated;
                }
            }

            // SetIdeo can legitimately record its own HistoryEvents. This assertion owns only the one
            // explicit RecordEvent call below, so establish the exact sidecar baseline after all setup.
            // Scope teardown already registered a finally-run reset for failure cleanup.
            BeliefHistoryCorrelationCache.Reset();
            HistoryEvent observed = new HistoryEvent(
                historyDef,
                pawn.Named(HistoryEventArgsNames.Doer));
            scope.RequireNoNewEvent(() => Find.HistoryEventsManager.RecordEvent(observed));

            if (!ModsConfig.IdeologyActive || pawn.ideo?.Ideo == null)
            {
                PawnDiaryRimTestScope.Require(BeliefHistoryCorrelationCache.Count == 0,
                    "The history observer must remain inert without an active pawn ideoligion.");
                Log.Message(LogPrefix + "history observer enrichment: not applicable (no active pawn ideoligion). ");
                return;
            }

            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            List<string> nearby = BeliefHistoryCorrelationCache.NearbyDefNames(
                pawn.GetUniqueLoadID(), Find.TickManager.TicksGame, policy);
            PawnDiaryRimTestScope.Require(BeliefHistoryCorrelationCache.Count == 1
                    && nearby.Count == 1 && nearby[0] == historyDef.defName,
                "The non-emitting history observer did not retain one exact nearby identity.");

            if (!string.IsNullOrWhiteSpace(correlatedDefName))
            {
                // Exercise the same guarded live-argument projection with the correlated identity, but do
                // not ask vanilla to apply its gameplay consequences a second time. The first call above
                // already proves the actual Harmony wiring; this detached row proves the enrichment seam.
                HistoryEventDef correlatedDef = DefDatabase<HistoryEventDef>.GetNamedSilentFail(
                    correlatedDefName);
                BeliefHistoryObservation correlatedObservation = null;
                PawnDiaryRimTestScope.Require(correlatedDef != null
                        && DlcContext.TryCaptureBeliefHistoryObservation(
                            new HistoryEvent(correlatedDef, pawn.Named(HistoryEventArgsNames.Doer)),
                            out correlatedObservation),
                    "The guarded adapter could not project the selected exact HistoryEvent correlation.");
                BeliefHistoryCorrelationCache.Reset();
                BeliefHistoryCorrelationCache.Observe(correlatedObservation, policy);
                BeliefEventEvidence evidence = BeliefEventEvidenceFactory.ForEvent(
                    pawn.GetUniqueLoadID(), Find.TickManager.TicksGame, "synthetic",
                    "HistoryObserverFixture", "initiator", "ordinary fixture", string.Empty);
                BeliefContextBuildResult built = BeliefContextBuilder.Build(
                    pawn, evidence, "history-observer-fixture", Find.TickManager.TicksGame,
                    DiaryEvent.InitiatorRole);
                PawnDiaryRimTestScope.Require(built.evaluated && built.resolution.stances.Count > 0
                        && built.resolution.stances[0].relevanceSource
                            == BeliefRelevanceSourceTokens.HistoryCorrelation,
                    "An exact cached history identity did not outrank lexical inference.");
            }
            else
            {
                Log.Message(LogPrefix + "history observer exact resolver branch: no loaded current "
                    + "visible precept exposed a resolvable HistoryEventDef; cache/non-emission still passed.");
            }
        }

        private static void VerifyOfficialDlcMetadata(
            string packName,
            string packageId,
            bool packageActive)
        {
            if (!packageActive)
            {
                Log.Message(LogPrefix + "package=" + packageId
                    + "; status=not_applicable; reason=dlc_inactive");
                return;
            }

            PreceptDef target;
            string correlationKind;
            string correlationDefName;
            string sourceField;
            PawnDiaryRimTestScope.Require(
                TryFindOfficialCorrelation(
                    packageId, out target, out correlationKind, out correlationDefName, out sourceField),
                packName + " exposed no safe public ThoughtDef/HistoryEventDef precept metadata.");
            BeliefSnapshot snapshot = CaptureSinglePreceptSnapshot(target);
            snapshot.precepts.RemoveAll(
                precept => !string.Equals(
                    precept?.defName, target.defName, StringComparison.Ordinal));
            BeliefPreceptFact projected = snapshot.precepts.FirstOrDefault();
            BeliefCorrelationFact projectedCorrelation = projected?.correlations?.FirstOrDefault(
                correlation => correlation != null
                    && string.Equals(correlation.kind, correlationKind, StringComparison.Ordinal)
                    && string.Equals(
                        correlation.defName, correlationDefName, StringComparison.Ordinal));
            PawnDiaryRimTestScope.Require(
                snapshot.ideologyActive && projected != null
                    && projected.issue?.defName == target.issue.defName
                    && projectedCorrelation != null
                    && string.Equals(
                        projectedCorrelation.sourceFieldToken, sourceField, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(projectedCorrelation.sourceComponentKind),
                packName + " ordinary precept metadata did not cross the guarded projection boundary.");

            BeliefPreceptFact lexicalBait = DetachedPrecept(
                "LexicalBait_" + packName,
                "LexicalBaitIssue_" + packName,
                "luminous authority ceremony");
            lexicalBait.impactRank = 3;
            snapshot.precepts.Add(lexicalBait);
            BeliefEventEvidence evidence = BeliefEventEvidenceFactory.ForEvent(
                snapshot.pawnId,
                snapshot.capturedTick,
                "fixture",
                "OfficialMetadataFixture_" + packName,
                DiaryEvent.InitiatorRole,
                lexicalBait.displayLabel,
                string.Empty);
            if (correlationKind == BeliefCorrelationKindTokens.HistoryEvent)
                evidence.historyEventDefNames.Add(correlationDefName);
            else
                evidence.thoughtDefNames.Add(correlationDefName);

            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            BeliefStanceResolution resolved = EventRelativeStanceResolver.Resolve(
                new BeliefResolutionRequest
                {
                    snapshot = snapshot,
                    evidence = evidence,
                    policy = policy,
                    mode = BeliefResolutionModeTokens.EventEnrichment,
                    deterministicSeed = 17
                });
            PawnDiaryRimTestScope.Require(
                resolved.stances.Count == 1
                    && resolved.stances[0].precept.defName == target.defName
                    && resolved.stances[0].relevanceTier
                        == BeliefRelevanceTierTokens.ExactCorrelation
                    && resolved.automaticCoverage.outcome
                        == BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation
                    && resolved.automaticCoverage.reason
                        == BeliefAutomaticCoverageReasonTokens.None
                    && resolved.automaticCoverage.candidateCount >= 1
                    && policy.correlationOverrides.Count == 0,
                packName + " structural metadata did not beat unrelated high-impact lexical bait.");

            BeliefContextBuilder.ResetAutomaticCoverageDiagnostics();
            scope.RegisterCleanup(BeliefContextBuilder.ResetAutomaticCoverageDiagnostics);
            BeliefContextBuilder.RecordAutomaticCoverageForTests(
                resolved.automaticCoverage, policy);
            string diagnostic = BeliefContextBuilder.AutomaticCoverageDiagnosticsForDev();
            PawnDiaryRimTestScope.Require(
                diagnostic.Contains("last_outcome=exact_correlation")
                    && diagnostic.Contains("last_candidates=")
                    && diagnostic.IndexOf(target.defName, StringComparison.OrdinalIgnoreCase) < 0
                    && diagnostic.IndexOf(target.issue.defName, StringComparison.OrdinalIgnoreCase) < 0
                    && diagnostic.IndexOf(correlationDefName, StringComparison.OrdinalIgnoreCase) < 0,
                packName + " automatic-coverage output exposed authored text or runtime candidate IDs.");
            Log.Message(LogPrefix + "package=" + packageId + "; status=resolved; tier="
                + BeliefRelevanceTierTokens.ExactCorrelation + "; source=" + correlationKind
                + "; corrections=0");

            VerifyMetadataPoorOfficialPreceptStaysUnsupported(packName, packageId, policy);
        }

        private static void VerifyMetadataPoorOfficialPreceptStaysUnsupported(
            string packName,
            string packageId,
            BeliefPolicySnapshot policy)
        {
            PreceptDef unsupported = OfficialPrecepts(packageId).FirstOrDefault(def =>
            {
                string ignoredKind;
                string ignoredDefName;
                string ignoredField;
                return !TryFindDirectCorrelation(
                    def, true, out ignoredKind, out ignoredDefName, out ignoredField)
                    && !TryFindDirectCorrelation(
                        def, false, out ignoredKind, out ignoredDefName, out ignoredField)
                    && (def.preceptClass == null
                        || !typeof(Precept_Ritual).IsAssignableFrom(def.preceptClass));
            });
            if (unsupported == null)
            {
                Log.Message(LogPrefix + "package=" + packageId
                    + "; metadata_poor_status=none_safe_to_instantiate");
                return;
            }

            BeliefSnapshot snapshot = CaptureSinglePreceptSnapshot(unsupported);
            snapshot.precepts.RemoveAll(
                precept => !string.Equals(
                    precept?.defName, unsupported.defName, StringComparison.Ordinal));
            BeliefPreceptFact projected = snapshot.precepts.FirstOrDefault();
            PawnDiaryRimTestScope.Require(
                projected != null && projected.correlations.Count == 0,
                packName + " metadata-poor fixture unexpectedly projected a structural correlation.");
            BeliefEventEvidence evidence = BeliefEventEvidenceFactory.ForEvent(
                snapshot.pawnId, snapshot.capturedTick, "fixture", "EventZ19",
                DiaryEvent.InitiatorRole, "cobalt river", string.Empty);
            evidence.historyEventDefNames.Add("UnrelatedHistoryQ72");
            BeliefStanceResolution unresolved = EventRelativeStanceResolver.Resolve(
                new BeliefResolutionRequest
                {
                    snapshot = snapshot,
                    evidence = evidence,
                    policy = policy,
                    mode = BeliefResolutionModeTokens.EventEnrichment
                });
            PawnDiaryRimTestScope.Require(
                unresolved.stances.Count == 0
                    && unresolved.automaticCoverage.outcome
                        == BeliefAutomaticCoverageOutcomeTokens.NoMatch,
                packName + " metadata-poor precept guessed belief context from unrelated evidence.");
            Log.Message(LogPrefix + "package=" + packageId
                + "; metadata_poor_status=unsupported; structural_links=0; corrections=0");
        }

        private static bool TryFindOfficialCorrelation(
            string packageId,
            out PreceptDef target,
            out string correlationKind,
            out string correlationDefName,
            out string sourceField)
        {
            List<PreceptDef> candidates = OfficialPrecepts(packageId);
            // Prefer a public eventDef field because that is the most conservative ordinary-event
            // correlation. Fall back to the equally safe public thought field when a pack has no event.
            for (int pass = 0; pass < 2; pass++)
            {
                bool history = pass == 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (!TryFindDirectCorrelation(
                            candidates[i], history,
                            out correlationKind, out correlationDefName, out sourceField))
                        continue;
                    target = candidates[i];
                    return true;
                }
            }

            target = null;
            correlationKind = string.Empty;
            correlationDefName = string.Empty;
            sourceField = string.Empty;
            return false;
        }

        private static bool TryFindDirectCorrelation(
            PreceptDef precept,
            bool history,
            out string correlationKind,
            out string correlationDefName,
            out string sourceField)
        {
            correlationKind = string.Empty;
            correlationDefName = string.Empty;
            sourceField = string.Empty;
            if (precept?.comps == null) return false;
            for (int componentIndex = 0; componentIndex < precept.comps.Count; componentIndex++)
            {
                PreceptComp component = precept.comps[componentIndex];
                if (component == null) continue;
                FieldInfo[] fields = component.GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.Public)
                    .OrderBy(field => field.Name, StringComparer.Ordinal)
                    .ToArray();
                for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                {
                    FieldInfo field = fields[fieldIndex];
                    bool expectedName = history
                        ? string.Equals(field.Name, "eventDef", StringComparison.OrdinalIgnoreCase)
                        : string.Equals(field.Name, "thought", StringComparison.OrdinalIgnoreCase);
                    bool expectedType = history
                        ? typeof(HistoryEventDef).IsAssignableFrom(field.FieldType)
                        : typeof(ThoughtDef).IsAssignableFrom(field.FieldType);
                    if (!expectedName || !expectedType) continue;

                    // Read a public field only after both its exact name and Def type are proven. This
                    // mirrors production's conservative projector and never invokes a reflected getter.
                    Def value;
                    try { value = field.GetValue(component) as Def; }
                    catch { continue; }
                    if (value == null || string.IsNullOrWhiteSpace(value.defName)) continue;
                    correlationKind = history
                        ? BeliefCorrelationKindTokens.HistoryEvent
                        : BeliefCorrelationKindTokens.Thought;
                    correlationDefName = value.defName;
                    sourceField = field.Name;
                    return true;
                }
            }

            return false;
        }

        private static List<PreceptDef> OfficialPrecepts(string packageId)
        {
            return DefDatabase<PreceptDef>.AllDefsListForReading
                .Where(def => def != null && def.issue != null && !string.IsNullOrWhiteSpace(def.defName)
                    && string.Equals(
                        def.modContentPack?.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(def => def.defName, StringComparer.Ordinal)
                .ToList();
        }

        private static BeliefSnapshot CaptureSinglePreceptSnapshot(PreceptDef target)
        {
            PawnDiaryRimTestScope.Require(target?.issue != null && pawn?.ideo != null
                    && Faction.OfPlayer != null,
                "The official DLC precept fixture lacks a target, Ideology tracker, or player faction.");
            Ideo fixture = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms
            {
                forFaction = Faction.OfPlayer.def,
                fixedIdeo = true
            });
            PawnDiaryRimTestScope.Require(fixture != null,
                "The official DLC precept fixture could not generate a disposable ideoligion.");
            List<Precept> existing = fixture.PreceptsListForReading
                .Where(precept => precept?.def?.issue == target.issue)
                .ToList();
            for (int i = 0; i < existing.Count; i++) fixture.RemovePrecept(existing[i], false);
            fixture.AddPrecept(
                PreceptMaker.MakePrecept(target), false, Faction.OfPlayer.def, null);
            pawn.ideo.SetIdeo(fixture);
            BeliefHistoryCorrelationCache.Reset();
            return DlcContext.CaptureBeliefSnapshot(pawn, DiaryBeliefPolicy.Snapshot());
        }

        private static BeliefSnapshot DetachedSnapshot(
            string pawnId,
            int tick,
            params BeliefPreceptFact[] precepts)
        {
            BeliefSnapshot snapshot = new BeliefSnapshot
            {
                ideologyActive = true,
                pawnId = pawnId,
                capturedTick = tick,
                ideologyId = "RimTestIdeology",
                ideologyName = "RimTest Ideoligion"
            };
            if (precepts != null) snapshot.precepts.AddRange(precepts);
            return snapshot;
        }

        private static BeliefPreceptFact DetachedPrecept(
            string defName,
            string issueDefName,
            string text)
        {
            return new BeliefPreceptFact
            {
                instanceId = defName + "#fixture",
                defName = defName,
                issue = new BeliefIssueFact
                {
                    defName = issueDefName,
                    label = text,
                    description = text
                },
                displayLabel = text,
                description = text,
                visible = true,
                impactRank = 1
            };
        }

        private static Ideo CreateIdeoWithThoughtPrecept(
            Pawn targetPawn,
            out Precept sourcePrecept,
            out ThoughtDef thoughtDef)
        {
            sourcePrecept = null;
            thoughtDef = null;
            if (targetPawn?.ideo == null) return null;

            List<PreceptDef> candidates = DefDatabase<PreceptDef>.AllDefsListForReading.Where(def =>
            {
                List<PreceptComp> components = def?.comps;
                if (components == null) return false;
                for (int i = 0; i < components.Count; i++)
                {
                    PreceptComp_Thought thought = components[i] as PreceptComp_Thought;
                    if (thought?.thought != null && thought.thought.durationDays > 0f
                        && typeof(Thought_Memory).IsAssignableFrom(thought.thought.ThoughtClass)
                        && InteractionGroups.ClassifyThought(thought.thought) != null)
                    {
                        return true;
                    }
                }

                return false;
            }).OrderBy(def => def.defName, StringComparer.Ordinal).ToList();

            for (int i = 0; i < candidates.Count; i++)
            {
                PreceptDef target = candidates[i];
                PreceptComp_Thought thoughtComp = target.comps.OfType<PreceptComp_Thought>()
                    .FirstOrDefault(comp => comp?.thought != null
                        && comp.thought.durationDays > 0f
                        && typeof(Thought_Memory).IsAssignableFrom(comp.thought.ThoughtClass)
                        && InteractionGroups.ClassifyThought(comp.thought) != null);
                if (target.issue == null || thoughtComp?.thought == null || Faction.OfPlayer == null)
                    continue;

                Ideo fixture = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms
                {
                    forFaction = Faction.OfPlayer.def,
                    fixedIdeo = true
                });
                if (fixture == null) continue;
                Precept existing = fixture.PreceptsListForReading
                    .FirstOrDefault(precept => precept?.def?.issue == target.issue);
                if (existing != null) fixture.RemovePrecept(existing, false);
                Precept candidatePrecept = PreceptMaker.MakePrecept(target);
                fixture.AddPrecept(candidatePrecept, false, Faction.OfPlayer.def, null);
                targetPawn.ideo.SetIdeo(fixture);

                DiaryInteractionGroupDef group = InteractionGroups.ClassifyThought(thoughtComp.thought);
                PawnDiaryMod.Settings.SetGroupEnabled(group.defName, true);
                Thought_Memory probe = ThoughtMaker.MakeThought(thoughtComp.thought, candidatePrecept);
                probe.pawn = targetPawn;
                ThoughtSignal probeSignal = new ThoughtSignal(targetPawn, probe);
                CaptureDecision decision = ThoughtEventData.Decide(
                    probeSignal.Payload as ThoughtEventData,
                    probeSignal.BuildContext());
                if (decision != CaptureDecision.GenerateSolo) continue;

                sourcePrecept = candidatePrecept;
                thoughtDef = thoughtComp.thought;
                return fixture;
            }

            return null;
        }

        private static bool ThrowBeliefSnapshotFixture()
        {
            throw new InvalidOperationException("Synthetic belief snapshot failure.");
        }

        private static bool ThrowIdeologyProviderFixture()
        {
            throw new InvalidOperationException("Synthetic Ideology narrative-provider failure.");
        }

        private static void AssertBodyModificationAttitude(string preceptDefName, string expectedAttitude)
        {
            PreceptDef target = DefDatabase<PreceptDef>.GetNamedSilentFail(preceptDefName);
            HediffDef pegLeg = DefDatabase<HediffDef>.GetNamedSilentFail("PegLeg");
            BodyPartRecord leg = pawn.health.hediffSet.GetNotMissingParts()
                .FirstOrDefault(part => part?.def?.defName == "Leg");
            PawnDiaryRimTestScope.Require(target?.issue != null && pegLeg != null && leg != null,
                "The vanilla body-modification fixture Defs/body part were not loaded.");

            IdeoGenerationParms parms = new IdeoGenerationParms
            {
                forFaction = Faction.OfPlayer.def,
                fixedIdeo = true
            };
            Ideo fixture = IdeoGenerator.GenerateIdeo(parms);
            PawnDiaryRimTestScope.Require(fixture != null,
                "RimWorld did not generate a disposable Ideology fixture.");
            Precept existing = fixture.PreceptsListForReading
                .FirstOrDefault(precept => precept?.def?.issue == target.issue);
            if (existing != null) fixture.RemovePrecept(existing, false);
            Precept targetPrecept = PreceptMaker.MakePrecept(target);
            fixture.AddPrecept(targetPrecept, false, Faction.OfPlayer.def, null);

            // Traits have stronger intentional precedence than ideology. Remove either vanilla body
            // trait from this disposable pawn so the assertion isolates the precept worker path. The
            // negative vanilla worker is not valid while despawned, so exercise it on the loaded map
            // exactly as production does; the shared scope destroys the spawned pawn in its finally path.
            pawn.story.traits.allTraits.RemoveAll(trait =>
                trait?.def?.defName == "Transhumanist" || trait?.def?.defName == "BodyPurist");
            scope.SpawnAsLiveColonist(pawn);
            pawn.ideo.SetIdeo(fixture);
            Hediff hediff = HediffMaker.MakeHediff(pegLeg, pawn);
            scope.RegisterCleanup(() =>
            {
                if (pawn?.health?.hediffSet?.hediffs?.Contains(hediff) == true)
                    pawn.health.RemoveHediff(hediff);
            });

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => pawn.health.AddHediff(hediff, leg), pegLeg.defName, pawn, null);
            PawnDiaryRimTestScope.Require(
                string.Equals(
                    DiaryContextFields.Value(diaryEvent.gameContext, "body_attitude"),
                    expectedAttitude,
                    StringComparison.Ordinal),
                preceptDefName + " did not preserve body_attitude=" + expectedAttitude
                    + " through the real AddHediff capture path.");
            BeliefSnapshot belief = DlcContext.CaptureBeliefSnapshot(pawn);
            string expectedKey = "ideology|interpretation|" + pawn.GetUniqueLoadID() + "|"
                + belief.ideologyId + "|instance|" + targetPrecept.GetUniqueLoadID();
            List<NarrativeEvidence> frozenEvidence = diaryEvent.NarrativeEvidenceForRole(
                DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(
                frozenEvidence.Count == 1
                    && frozenEvidence[0].eventId == diaryEvent.eventId
                    && frozenEvidence[0].tick == diaryEvent.tick
                    && diaryEvent.NarrativeSelectedCandidateKeysForRole(
                        DiaryEvent.InitiatorRole).Contains(expectedKey)
                    && !string.IsNullOrWhiteSpace(
                        diaryEvent.NarrativeContextForRole(DiaryEvent.InitiatorRole)),
                preceptDefName + " did not re-stamp and persist the prepared body N3-I lens.");
        }
    }
}

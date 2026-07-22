// Loaded-game coverage for Ideology Phase 1's guarded projection and non-emitting correlation seam.
// These fixtures use disposable pawns and real loaded Ideo/Precept/Thought/HistoryEvent objects, but
// assert only on the detached contracts that production code exposes. No reflection behavior or
// Phase 2 mutation page is exercised here.
using System;
using System.Collections.Generic;
using System.Linq;
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
            PawnDiaryRimTestScope.Require(ReferenceEquals(first, second),
                "Belief policy rebuilt its deep-copied snapshot inside one active language.");
            PawnDiaryRimTestScope.Require(DiaryBeliefPolicy.Enabled == first.enabled,
                "The fast Enabled gate disagreed with the cached belief-policy snapshot.");
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

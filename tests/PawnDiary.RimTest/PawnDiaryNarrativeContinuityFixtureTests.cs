// In-game fixtures for Narrative Continuity's main-thread policy adapter. Core rows prove the N1
// storage/prompt seam; N2-O adds a detached Odyssey home row to prove loaded-Def provider selection
// without requiring a live gravship in the test map.
using System;
using System.Collections.Generic;
using RimTestRedux;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Exercises the loaded Def-backed builder through core and detached Odyssey fixtures, then proves
    /// selected frozen text reaches a first-person prompt. No live DLC tracker is required here.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryNarrativeContinuityFixtureTests
    {
        private static PawnDiaryRimTestScope scope;

        [BeforeEach]
        public static void SetUp()
        {
            // The scope gives Def-backed adapters the same loaded-game safety and cleanup conventions
            // as the other RimTest suites. No capture group or live pawn is needed for this DTO fixture.
            scope = PawnDiaryRimTestScope.Begin();
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
            }
        }

        /// <summary>
        /// Full accepts two short exact-arc core facts, Balanced accepts its documented exact-arc pair,
        /// and Compact keeps one. Empty candidates retain safe evidence/reference history but emit no
        /// prompt text, which is the normal no-DLC/N1 runtime behavior.
        /// </summary>
        [Test]
        public static void CoreFixtureUsesBudgetsAndReachesFirstPersonPrompt()
        {
            NarrativeContextBuildResult zero = Build(PromptContextDetailLevel.Full,
                new List<NarrativeLensCandidate>());
            Require(zero.evidence.Count == 1, "Authorized core evidence should persist even with zero candidates.");
            Require(zero.selection.selectedCandidates.Count == 0 && string.IsNullOrEmpty(zero.selection.narrativeContext),
                "Zero candidates should produce no optional prompt context.");
            Require(zero.selection.references.Count == 1,
                "Zero candidates should still preserve one compact continuity reference.");

            NarrativeContextBuildResult one = Build(PromptContextDetailLevel.Full,
                new List<NarrativeLensCandidate> { Candidate("core-one", NarrativeCategoryTokens.Identity, "First core fact.") });
            Require(one.selection.selectedCandidates.Count == 1,
                "One eligible core candidate should select exactly one lens.");

            List<NarrativeLensCandidate> twoCandidates = new List<NarrativeLensCandidate>
            {
                Candidate("core-one", NarrativeCategoryTokens.Identity, "First core fact."),
                Candidate("core-two", NarrativeCategoryTokens.Bond, "Second core fact.")
            };
            NarrativeContextBuildResult full = Build(PromptContextDetailLevel.Full, twoCandidates);
            NarrativeContextBuildResult balanced = Build(PromptContextDetailLevel.Balanced, twoCandidates);
            NarrativeContextBuildResult compact = Build(PromptContextDetailLevel.Compact, twoCandidates);
            Require(full.selection.selectedCandidates.Count == 2,
                "Full detail should select two complete core facts.");
            Require(balanced.selection.selectedCandidates.Count == 2,
                "Balanced detail should retain its configured short exact-arc pair.");
            Require(compact.selection.selectedCandidates.Count == 1,
                "Compact detail should cap the same input to one lens.");

            DiaryEvent diaryEvent = new DiaryEvent
            {
                eventId = "rimtest-narrative-prompt",
                tick = 100,
                date = "8th Aprimay 5502",
                interactionDefName = "Chat",
                interactionLabel = "chat",
                gameContext = "source=rimtest_narrative",
                colorCue = DiaryEvent.QuietColorCue,
                solo = true,
                initiatorPawnId = "Thing_Human_NarrativeFixture",
                initiatorName = "Mira",
                initiatorText = "Mira repaired the generator.",
            };
            diaryEvent.ApplyNarrativeContext(DiaryEvent.InitiatorRole, full);

            DiaryEventPayload payload = DiaryPipelineAdapters.ToPayload(diaryEvent);
            DiaryPolicySnapshot policy = DiaryPipelineAdapters.PolicyFor(payload);
            DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = payload,
                policy = policy,
                povRole = DiaryPipelineRoles.Initiator,
                maxTokens = 30
            });
            Require(plan.userPrompt != null && plan.userPrompt.IndexOf("First core fact.", StringComparison.Ordinal) >= 0
                    && plan.userPrompt.IndexOf("Second core fact.", StringComparison.Ordinal) >= 0,
                "The frozen selected facts should reach the first-person prompt through the N1 payload.");
        }

        /// <summary>Loaded N2-O orchestration selects an exact occupied-home snapshot and fails closed.</summary>
        [Test]
        public static void OdysseyHomeFixtureUsesLoadedProviderAndKnowledgeGates()
        {
            OdysseyNarrativeSnapshot odyssey = new OdysseyNarrativeSnapshot
            {
                providerAvailable = true,
                povPawnId = "Thing_Human_NarrativeFixture",
                shipStableId = "WorldObject_RimTestShip",
                shipName = "Wayfarer",
                journeyId = "odyssey-journey|WorldObject_RimTestShip|50",
                locationKey = "planet-layer-1-tile-42",
                locationLabel = "Frozen plain",
                homeText = "At this event, the writer was aboard Wayfarer at the frozen plain.",
                sourceTick = 90,
                pawnCanKnow = true,
                hasVerifiedPovConnection = true
            };
            NarrativeContextBuildResult selected = NarrativeContextBuilder.Build(
                new NarrativeContextBuildRequest
                {
                    eventId = "rimtest-odyssey-narrative",
                    eventTick = 100,
                    povPawnId = odyssey.povPawnId,
                    povRole = DiaryEvent.InitiatorRole,
                    evidence = new List<NarrativeEvidence> { Evidence() },
                    odyssey = odyssey,
                    contextDetailLevel = PromptContextDetailLevel.Full,
                    deterministicSeed = 17
                });
            Require(selected.selection.selectedCandidates.Count == 1
                    && selected.selection.selectedCandidates[0].provider == NarrativeProviderTokens.Odyssey,
                "The loaded fixed provider list did not select the detached Odyssey home lens.");
            Require(selected.selection.narrativeContext.IndexOf("Wayfarer", StringComparison.Ordinal) >= 0,
                "The selected N2-O factual unit lost its frozen visible ship name.");

            odyssey.hasVerifiedPovConnection = false;
            NarrativeContextBuildResult disconnected = NarrativeContextBuilder.Build(
                new NarrativeContextBuildRequest
                {
                    eventId = "rimtest-odyssey-disconnected",
                    eventTick = 100,
                    povPawnId = odyssey.povPawnId,
                    povRole = DiaryEvent.InitiatorRole,
                    evidence = new List<NarrativeEvidence> { Evidence() },
                    odyssey = odyssey,
                    contextDetailLevel = PromptContextDetailLevel.Full
                });
            Require(disconnected.selection.selectedCandidates.Count == 0
                    && string.IsNullOrEmpty(disconnected.selection.narrativeContext),
                "A disconnected Odyssey POV received optional home context.");
        }

        /// <summary>
        /// N4.2 wiring: one hot page's own evidence and one compact archive row's saved reference
        /// project into the same detached selector opportunity without re-reading any DLC state.
        /// </summary>
        [Test]
        public static void HotAndArchiveExactReferencesReachCrossArcOpportunity()
        {
            const string pawnId = "Thing_Human_NarrativeFixture";
            DiaryEvent hot = new DiaryEvent
            {
                eventId = "rimtest-cross-hot",
                tick = 100,
                date = "1st Aprimay 5502",
                interactionDefName = "RimTestHot",
                interactionLabel = "first chapter",
                solo = true,
                initiatorPawnId = pawnId,
                initiatorName = "Mira",
                initiatorText = "Mira began the journey."
            };
            NarrativeEvidence hotEvidence = Evidence();
            hotEvidence.eventId = hot.eventId;
            hotEvidence.tick = hot.tick;
            hotEvidence.povPawnId = pawnId;
            hotEvidence.phase = "departed";
            hotEvidence.arcKey = "journey|rimtest-ship";
            hotEvidence.subjectKind = NarrativeSubjectKindTokens.Ship;
            hotEvidence.subjectId = "rimtest-ship";
            hotEvidence.facet = NarrativeFacetTokens.JourneyChapter;
            hot.ApplyNarrativeContext(DiaryEvent.InitiatorRole, new NarrativeContextBuildResult
            {
                evidence = new List<NarrativeEvidence> { hotEvidence },
                selection = new NarrativeContextSelection()
            });

            ArchivedDiaryEntry archived = new ArchivedDiaryEntry
            {
                eventId = "rimtest-cross-archive",
                pawnId = pawnId,
                povRole = DiaryEvent.RecipientRole,
                tick = 200,
                date = "2nd Aprimay 5502",
                text = "Mira reached the destination.",
                interactionDefName = "RimTestArchive",
                interactionLabel = "second chapter",
                narrativeReferences = NarrativeStatePersistence.FromReferences(
                    new List<NarrativeReference>
                    {
                        new NarrativeReference
                        {
                            facet = NarrativeFacetTokens.JourneyChapter,
                            phase = "landed",
                            subjectKind = NarrativeSubjectKindTokens.Ship,
                            subjectId = "rimtest-ship",
                            arcKey = "journey|rimtest-ship",
                            sourceEventId = "rimtest-cross-archive",
                            sourceTick = 200
                        }
                    })
            };

            CrossArcMemoryCandidate hotCandidate = DiaryGameComponent.CrossArcCandidateFromEvent(
                hot, pawnId, DiaryEvent.InitiatorRole);
            CrossArcMemoryCandidate archiveCandidate =
                DiaryGameComponent.CrossArcCandidateFromArchive(archived);
            Require(hotCandidate != null && hotCandidate.references.Count > 0,
                "The hot-page projection dropped its own exact source evidence.");
            Require(archiveCandidate != null && archiveCandidate.references.Count == 1,
                "The compact archive projection dropped its saved exact reference.");
            Require(hotCandidate.povRole == DiaryEvent.InitiatorRole
                    && archiveCandidate.povRole == DiaryEvent.RecipientRole,
                "Cross-arc projection did not preserve each source page's real POV role.");

            CrossArcMemorySelection selected = CrossArcReflectionMemorySelector.Select(
                new CrossArcMemorySelectionRequest
                {
                    pawnId = pawnId,
                    currentTick = 300,
                    minimumLinkedMemories = 2,
                    minimumDistinctPhases = 2,
                    maximumSpanTicks = 1000,
                    requireChangeOrConsequence = true,
                    changeOrConsequenceFacets = new List<string>
                    {
                        NarrativeFacetTokens.JourneyChapter
                    },
                    candidates = new List<CrossArcMemoryCandidate>
                    {
                        archiveCandidate,
                        hotCandidate
                    }
                });
            Require(selected.qualified && selected.sourceEventIds.Count == 2,
                "Hot plus archive exact references did not produce one detached cross-arc opportunity.");
        }

        private static NarrativeContextBuildResult Build(
            PromptContextDetailLevel detail,
            List<NarrativeLensCandidate> candidates)
        {
            return NarrativeContextBuilder.Build(new NarrativeContextBuildRequest
            {
                eventId = "rimtest-narrative",
                eventTick = 100,
                povPawnId = "Thing_Human_NarrativeFixture",
                povRole = DiaryEvent.InitiatorRole,
                evidence = new List<NarrativeEvidence> { Evidence() },
                // N1's sole candidate lane is explicitly provider-neutral core fixture data. Future DLC
                // sources must call this only after their own DLC and POV-knowledge guards.
                coreCandidates = candidates,
                contextDetailLevel = detail,
                deterministicSeed = 17
            });
        }

        private static NarrativeEvidence Evidence()
        {
            return new NarrativeEvidence
            {
                facet = NarrativeFacetTokens.IdentityTransition,
                phase = "opened",
                subjectKind = NarrativeSubjectKindTokens.Pawn,
                subjectId = "Thing_Human_NarrativeFixture",
                arcKey = "core|fixture",
                beliefTopics = new List<string> { "belonging" },
                salience = NarrativeSalienceTokens.Meaningful,
                pawnCanKnow = true,
                sourceDomain = "core",
                sourceDefName = "RimTestFixture",
            };
        }

        private static NarrativeLensCandidate Candidate(string key, string category, string text)
        {
            return new NarrativeLensCandidate
            {
                candidateKey = key,
                provider = NarrativeProviderTokens.Core,
                category = category,
                text = text,
                facet = NarrativeFacetTokens.IdentityTransition,
                subjectKind = NarrativeSubjectKindTokens.Pawn,
                subjectId = "Thing_Human_NarrativeFixture",
                arcKey = "core|fixture",
                sourceEventId = "core-fixture-source",
                sourceTick = 90,
                salience = NarrativeSalienceTokens.Meaningful,
                pawnCanKnow = true,
                providerAvailable = true,
                hasVerifiedPovConnection = true,
            };
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

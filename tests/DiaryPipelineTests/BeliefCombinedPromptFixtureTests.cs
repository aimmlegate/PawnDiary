// Master Wave 12 / Ideology Phase 5C pure combined-prompt fixtures. These tests deliberately join
// the detached production seams that smaller suites exercise separately:
// event evidence -> belief resolver -> bounded saved belief block -> N3-I candidate -> shared lens
// selector -> Full/Balanced/Compact prompt projection. No RimWorld, Verse, DefDatabase, or DLC type
// is referenced, so inactive/missing DLC behavior remains a normal empty result.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private sealed class Phase5CCombinedFixture
        {
            public string label;
            public string domain;
            public string sourceDefName;
            public string provider;
            public string category;
            public string facet;
            public string phase;
            public string contextKey;
            public string contextLabel;
            public string contextValue;
            public string primaryText;
            public string topic;
            public string matchKind;
            public string matchIdentity;
            public string expectedOutcome;
            public bool supportsCosmology;
            public bool suppressCosmology;
            public bool proveAliasBait;
            public bool proveLexicalBait;
            public bool expectBalancedNarrative;
            public bool expectCompactNarrative;
            public bool expectBalancedBelief = true;
        }

        private static void RunBeliefCombinedPhase5CFixtures()
        {
            XElement beliefDef;
            BeliefPolicySnapshot beliefPolicy = LoadPhase5CBeliefPolicy(out beliefDef);
            XElement narrativeDef;
            NarrativePolicySnapshot narrativePolicy = LoadPhase5CNarrativePolicy(out narrativeDef);

            AssertEqual("Phase 5C uses the XML belief repetition penalty", 450,
                (int)beliefPolicy.recentSelectionPenalty);
            AssertEqual("Phase 5C uses the XML narrative repetition penalty", 45,
                (int)narrativePolicy.repetitionPenalty);
            AssertEqual("Phase 5C keeps the XML global narrative lens cap", 2,
                narrativePolicy.maxSelectedCandidates);
            AssertEqual("Phase 5C correction audit keeps shipped overrides empty", 0,
                beliefPolicy.correlationOverrides.Count);
            AssertEqual("Phase 5C correction XML remains an empty list", 0,
                beliefDef.Element("correlationOverrides")?.Elements("li").Count() ?? 0);

            Phase5CCombinedFixture[] fixtures =
            {
                new Phase5CCombinedFixture
                {
                    label = "social thought",
                    domain = "Thought",
                    sourceDefName = "SharedSocialThought_5C",
                    provider = NarrativeProviderTokens.Core,
                    category = NarrativeCategoryTokens.Chapter,
                    facet = NarrativeFacetTokens.AmbientPressure,
                    phase = "social_thought",
                    contextKey = "thought_def",
                    contextLabel = "thought",
                    contextValue = "SharedSocialThought_5C",
                    primaryText = "The witnessed social thought remains the primary event fact.",
                    topic = "social_thought",
                    matchKind = BeliefCorrelationKindTokens.Thought,
                    matchIdentity = "SharedSocialThought_5C",
                    expectedOutcome = BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation,
                    proveLexicalBait = true,
                    expectCompactNarrative = true
                },
                new Phase5CCombinedFixture
                {
                    label = "ideological role authority",
                    domain = "Progression",
                    sourceDefName = "RoleAssigned_5C",
                    provider = NarrativeProviderTokens.Royalty,
                    category = NarrativeCategoryTokens.Identity,
                    facet = NarrativeFacetTokens.IdentityTransition,
                    phase = "authority_role",
                    contextKey = "role",
                    contextLabel = "ideological role",
                    contextValue = "MoralGuide",
                    primaryText = "The visible authority appointment remains the primary event fact.",
                    topic = "authority_speech",
                    matchKind = "issue",
                    matchIdentity = "AuthorityIssue_5C",
                    expectedOutcome = BeliefAutomaticCoverageOutcomeTokens.StructuralCorrelation,
                    supportsCosmology = true,
                    suppressCosmology = true,
                    proveAliasBait = true
                },
                new Phase5CCombinedFixture
                {
                    label = "ritual",
                    domain = "Ritual",
                    sourceDefName = "RitualOutcome_5C",
                    provider = NarrativeProviderTokens.Core,
                    category = NarrativeCategoryTokens.Chapter,
                    facet = NarrativeFacetTokens.IdentityTransition,
                    phase = "ritual_complete",
                    contextKey = "ritual_type",
                    contextLabel = "ritual type",
                    contextValue = "IdeologicalRitual",
                    primaryText = "The completed ritual remains the primary observed fact.",
                    topic = "rituals",
                    matchKind = "source",
                    matchIdentity = "RitualPrecept_5C",
                    expectedOutcome = BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation,
                    proveAliasBait = true,
                    expectCompactNarrative = true
                },
                new Phase5CCombinedFixture
                {
                    label = "conversion",
                    domain = "Interaction",
                    sourceDefName = "Convert_Success",
                    provider = NarrativeProviderTokens.Core,
                    category = NarrativeCategoryTokens.Identity,
                    facet = NarrativeFacetTokens.IdentityTransition,
                    phase = "conversion",
                    contextKey = "belief_event",
                    contextLabel = "belief event",
                    contextValue = "conversion",
                    primaryText = "The observed conversion result remains the primary event fact.",
                    topic = "conversion",
                    matchKind = BeliefCorrelationKindTokens.HistoryEvent,
                    matchIdentity = "HistoryConversion_5C",
                    expectedOutcome = BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation,
                    expectCompactNarrative = true
                },
                new Phase5CCombinedFixture
                {
                    label = "apostasy",
                    domain = "MentalState",
                    sourceDefName = "IdeoChange",
                    provider = NarrativeProviderTokens.Core,
                    category = NarrativeCategoryTokens.Identity,
                    facet = NarrativeFacetTokens.IdentityTransition,
                    phase = "apostasy",
                    contextKey = "belief_event",
                    contextLabel = "belief event",
                    contextValue = "apostasy",
                    primaryText = "The visible break with the former ideoligion remains primary.",
                    topic = "apostasy",
                    matchKind = BeliefCorrelationKindTokens.HistoryEvent,
                    matchIdentity = "HistoryApostasy_5C",
                    expectedOutcome = BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation,
                    expectCompactNarrative = true
                },
                new Phase5CCombinedFixture
                {
                    label = "Biotech belief evidence",
                    domain = "Progression",
                    sourceDefName = "XenotypeChanged_5C",
                    provider = NarrativeProviderTokens.Biotech,
                    category = NarrativeCategoryTokens.Identity,
                    facet = NarrativeFacetTokens.IdentityTransition,
                    phase = "xenotype_changed",
                    contextKey = "xenotype",
                    contextLabel = "xenotype",
                    contextValue = "Hussar",
                    primaryText = "The verified xenotype transition remains the primary event fact.",
                    topic = "xenotype",
                    matchKind = BeliefCorrelationKindTokens.Thought,
                    matchIdentity = "BiotechThought_5C",
                    expectedOutcome = BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation,
                    supportsCosmology = true,
                    expectBalancedNarrative = true,
                    expectBalancedBelief = false
                },
                new Phase5CCombinedFixture
                {
                    label = "Anomaly belief evidence",
                    domain = "Ritual",
                    sourceDefName = "PsychicRitual_5C",
                    provider = NarrativeProviderTokens.Anomaly,
                    category = NarrativeCategoryTokens.Pressure,
                    facet = NarrativeFacetTokens.AmbientPressure,
                    phase = "psychic_ritual",
                    contextKey = "ritual_type",
                    contextLabel = "ritual type",
                    contextValue = "PsychicRitual",
                    primaryText = "Only the visible psychic ritual result is treated as known.",
                    topic = "psychic_ritual",
                    matchKind = BeliefCorrelationKindTokens.Thought,
                    matchIdentity = "AnomalyThought_5C",
                    expectedOutcome = BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation
                },
                new Phase5CCombinedFixture
                {
                    label = "Odyssey belief evidence",
                    domain = "GravshipJourney",
                    sourceDefName = "GravshipLanding_5C",
                    provider = NarrativeProviderTokens.Odyssey,
                    category = NarrativeCategoryTokens.Home,
                    facet = NarrativeFacetTokens.JourneyChapter,
                    phase = "landing",
                    contextKey = "journey_phase",
                    contextLabel = "journey phase",
                    contextValue = "landing",
                    primaryText = "The verified gravship landing remains the primary journey fact.",
                    topic = "space_habitat",
                    matchKind = BeliefCorrelationKindTokens.HistoryEvent,
                    matchIdentity = "OdysseyHistory_5C",
                    expectedOutcome = BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation
                }
            };

            List<BeliefStanceResolution> resolutions = new List<BeliefStanceResolution>();
            for (int i = 0; i < fixtures.Length; i++)
                resolutions.Add(AssertPhase5CCombinedFixture(
                    fixtures[i], beliefPolicy, narrativePolicy, beliefDef));

            AssertPhase5CAmbiguityAndInactivePaths(beliefPolicy);
            AssertPhase5CEventRepetition(beliefPolicy, beliefDef);
            AssertPhase5CDiagnosticsRemainBoundedAndPrivate(resolutions);
            AssertPhase5CLegacyDlcFieldCoexistence(beliefPolicy);

            AssertEqual("Full narrative budget is the shipped XML value", "320",
                Phase5CDetailValue(narrativeDef, NarrativeDetailLevelTokens.Full, "characterBudget"));
            AssertEqual("Balanced narrative lens cap is the shipped XML value", "1",
                Phase5CDetailValue(narrativeDef, NarrativeDetailLevelTokens.Balanced, "maxLenses"));
            AssertEqual("Compact narrative budget is the shipped XML value", "110",
                Phase5CDetailValue(narrativeDef, NarrativeDetailLevelTokens.Compact, "characterBudget"));
        }

        private static BeliefStanceResolution AssertPhase5CCombinedFixture(
            Phase5CCombinedFixture fixture,
            BeliefPolicySnapshot beliefPolicy,
            NarrativePolicySnapshot narrativePolicy,
            XElement beliefDef)
        {
            BeliefPreceptFact target = Phase5CPrecept(
                fixture.label + "_target",
                fixture.matchKind == "issue" ? fixture.matchIdentity : fixture.label + "_issue",
                fixture.label + " doctrine",
                fixture.label + " doctrine is relevant only to this exact observed event.");
            BeliefPreceptFact lexicalBait = Phase5CPrecept(
                fixture.label + "_lexical_bait",
                fixture.label + "_lexical_issue",
                fixture.label + " luminous orchard doctrine",
                "This tempting lexical row is unrelated to the live event correlation.");
            BeliefPreceptFact aliasBait = Phase5CPrecept(
                fixture.label + "_alias_bait",
                fixture.label + "_alias_issue",
                "ritual ceremony custom doctrine",
                "This tempting semantic alias is unrelated to the exact event metadata.");
            BeliefPreceptFact unrelated = Phase5CPrecept(
                fixture.label + "_unrelated",
                fixture.label + "_unrelated_issue",
                "distant basalt weather doctrine",
                "This doctrine is unrelated and must stay out of every prompt.");

            if (fixture.matchKind == BeliefCorrelationKindTokens.Thought
                || fixture.matchKind == BeliefCorrelationKindTokens.HistoryEvent)
            {
                target.correlations.Add(new BeliefCorrelationFact
                {
                    kind = fixture.matchKind,
                    defName = fixture.matchIdentity,
                    sourceComponentKind = "Phase5CFixtureComponent",
                    sourceFieldToken = fixture.matchKind,
                    valence = BeliefValenceTokens.Neutral
                });
            }

            BeliefSnapshot snapshot = Phase5CSnapshot(
                fixture, target, lexicalBait, aliasBait, unrelated);
            BeliefEventEvidence evidence = Phase5CEvidence(
                fixture, target, lexicalBait);
            BeliefStanceResolution resolution = EventRelativeStanceResolver.Resolve(
                new BeliefResolutionRequest
                {
                    snapshot = snapshot,
                    evidence = evidence,
                    policy = beliefPolicy,
                    mode = BeliefResolutionModeTokens.EventEnrichment,
                    deterministicSeed = 37
                });

            AssertEqual(fixture.label + " selects exactly one event-relative stance", 1,
                resolution.stances.Count);
            AssertEqual(fixture.label + " selects the exact live target", target.defName,
                resolution.stances[0].precept.defName);
            AssertEqual(fixture.label + " records the expected automatic tier outcome",
                fixture.expectedOutcome, resolution.automaticCoverage.outcome);
            AssertEqual(fixture.label + " requires no exceptional correction", string.Empty,
                resolution.stances[0].correctionKey);

            if (fixture.proveAliasBait)
            {
                BeliefSnapshot aliasOnly = Phase5CSnapshot(fixture, aliasBait);
                aliasOnly.structure = null;
                aliasOnly.deities.Clear();
                BeliefStanceResolution aliasResolution = EventRelativeStanceResolver.Resolve(
                    new BeliefResolutionRequest
                    {
                        snapshot = aliasOnly,
                        evidence = evidence,
                        policy = beliefPolicy,
                        mode = BeliefResolutionModeTokens.EventEnrichment,
                        deterministicSeed = 37
                    });
                AssertEqual(fixture.label + " adversary proves the lower semantic alias is live",
                    BeliefAutomaticCoverageOutcomeTokens.SemanticAlias,
                    aliasResolution.automaticCoverage.outcome);
                AssertEqual(fixture.label + " exact/structural metadata beats the semantic alias",
                    target.defName, resolution.stances[0].precept.defName);
            }
            if (fixture.proveLexicalBait)
            {
                BeliefSnapshot lexicalOnly = Phase5CSnapshot(fixture, lexicalBait);
                BeliefStanceResolution lexicalResolution = EventRelativeStanceResolver.Resolve(
                    new BeliefResolutionRequest
                    {
                        snapshot = lexicalOnly,
                        evidence = evidence,
                        policy = beliefPolicy,
                        mode = BeliefResolutionModeTokens.EventEnrichment,
                        deterministicSeed = 37
                    });
                AssertEqual(fixture.label + " adversary proves the lower lexical match is live",
                    BeliefAutomaticCoverageOutcomeTokens.GuardedLexical,
                    lexicalResolution.automaticCoverage.outcome);
                AssertEqual(fixture.label + " exact metadata beats the lexical match",
                    target.defName, resolution.stances[0].precept.defName);
            }

            string fullBeliefContext = BeliefContextFormatter.Format(
                resolution, NarrativeDetailLevelTokens.Full, beliefPolicy);
            AssertContains(fixture.label + " bounded context contains the selected doctrine",
                fullBeliefContext, target.displayLabel);
            AssertTrue(fixture.label + " bounded context excludes bait and unrelated doctrine",
                fullBeliefContext.IndexOf(lexicalBait.displayLabel, StringComparison.Ordinal) < 0
                && fullBeliefContext.IndexOf(aliasBait.displayLabel, StringComparison.Ordinal) < 0
                && fullBeliefContext.IndexOf(unrelated.displayLabel, StringComparison.Ordinal) < 0);
            AssertTrue(fixture.label + " saved block stays inside XML character and line caps",
                fullBeliefContext.Length <= beliefPolicy.maximumTotalCharacters
                && Phase5CLineCount(fullBeliefContext) <= beliefPolicy.maximumTotalLines);

            string interpretation = IdeologyInterpretationFactFormatter.Format(
                ChildValue(beliefDef, "interpretationFactFormat"),
                ChildValue(beliefDef, "interpretationFactWithoutDescriptionFormat"),
                resolution.ideologyName,
                resolution.stances[0].precept.displayLabel,
                resolution.stances[0].precept.description,
                IdeologyNarrativeSnapshotFactory.MaximumNarrativeTextCharacters);
            IdeologyNarrativeSnapshot prepared = IdeologyNarrativeSnapshotFactory.Create(
                resolution, evidence.narrative, beliefPolicy, interpretation);
            AssertTrue(fixture.label + " exact result reaches the N3-I snapshot seam",
                prepared != null);
            IdeologyNarrativeSnapshot pageSnapshot = IdeologyNarrativeSnapshotFactory.ForPage(
                prepared,
                evidence.narrative.eventId,
                evidence.narrative.tick,
                evidence.narrative.povPawnId,
                evidence.narrative.povRole);
            List<NarrativeLensCandidate> ideologyCandidates = IdeologyNarrativeProvider.Build(
                new List<NarrativeEvidence> { evidence.narrative }, pageSnapshot);
            AssertEqual(fixture.label + " creates one N3-I interpretation candidate", 1,
                ideologyCandidates.Count);

            PromptContextDetailLevel[] promptLevels =
            {
                PromptContextDetailLevel.Full,
                PromptContextDetailLevel.Balanced,
                PromptContextDetailLevel.Compact
            };
            for (int levelIndex = 0; levelIndex < promptLevels.Length; levelIndex++)
            {
                PromptContextDetailLevel promptLevel = promptLevels[levelIndex];
                string narrativeLevel = Phase5CNarrativeLevel(promptLevel);
                NarrativeDetailBudget narrativeBudget = narrativePolicy.detailBudgets.First(
                    row => row.detailLevel == narrativeLevel);
                NarrativeLensCandidate primary = Phase5CPrimaryCandidate(fixture, evidence.narrative);
                NarrativeContextSelection selected = NarrativeContextSelector.Select(
                    new NarrativeContextRequest
                    {
                        evidence = new List<NarrativeEvidence> { evidence.narrative },
                        candidates = new List<NarrativeLensCandidate>
                        {
                            ideologyCandidates[0],
                            primary
                        },
                        policy = narrativePolicy,
                        currentTick = evidence.narrative.tick,
                        deterministicSeed = 37,
                        detailLevel = narrativeLevel,
                        promptCharacterBudget = narrativeBudget.characterBudget
                    });
                int expectedLensCap = Math.Min(
                    narrativePolicy.maxSelectedCandidates, narrativeBudget.maxLenses);
                AssertTrue(fixture.label + " " + promptLevel
                        + " stays inside the shared global lens cap",
                    selected.selectedCandidates.Count <= expectedLensCap);
                AssertTrue(fixture.label + " " + promptLevel
                        + " selects at least one complete narrative fact",
                    selected.selectedCandidates.Count > 0);
                AssertTrue(fixture.label + " " + promptLevel
                        + " keeps complete narrative facts inside the XML budget",
                    selected.narrativeContext.Length <= narrativeBudget.characterBudget);
                if (promptLevel == PromptContextDetailLevel.Full)
                {
                    AssertTrue(fixture.label + " Full selects the N3-I interpretation beside the source lens",
                        selected.selectedCandidates.Count == 2
                        && selected.selectedCandidates.Any(
                            row => row.category == NarrativeCategoryTokens.Interpretation));
                }

                DiaryEventPayload payload = SoloPayload(
                    evidence.narrative.eventId,
                    fixture.label + " combined event",
                    "Alice recorded the event without any optional narrative lens.");
                payload.domain = fixture.domain;
                payload.gameContext = fixture.contextKey + "=" + fixture.contextValue;
                payload.initiator.beliefContext = fullBeliefContext;
                payload.initiator.narrativeContext = selected.narrativeContext;
                // This fixture calibrates the two optional context systems against each other. Remove
                // unrelated continuity prose so Compact spends its real budget on the current event.
                payload.initiator.continuity = string.Empty;
                payload.initiator.lastOpener = string.Empty;
                payload.initiator.previousEntryEnding = string.Empty;

                DiaryPolicySnapshot promptPolicy = Policy(combat: false, important: true);
                promptPolicy.beliefPolicy = beliefPolicy;
                promptPolicy.beliefContextInstruction =
                    ChildValue(beliefDef, "promptFieldInstruction");
                promptPolicy.narrativeContextInstruction =
                    "Use only these selected, source-owned interpretation facts.";
                DiaryTemplatePolicy template =
                    promptPolicy.Template(DiaryPipelineTemplates.SoloImportant);
                template.fields.Add(Field(
                    "narrative context", NarrativeContextPrompt.Source));
                template.fields.Add(Field(
                    "belief context", BeliefContextPrompt.Source));

                DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
                {
                    payload = payload,
                    policy = promptPolicy,
                    povRole = DiaryPipelineRoles.Initiator,
                    contextDetailLevel = promptLevel
                });
                bool narrativeKept = plan.contextSelectionReport.kept.Any(
                    row => row.source == NarrativeContextPrompt.Source);
                bool narrativeExpected = promptLevel == PromptContextDetailLevel.Full
                    || promptLevel == PromptContextDetailLevel.Balanced
                        && fixture.expectBalancedNarrative
                    || promptLevel == PromptContextDetailLevel.Compact
                        && fixture.expectCompactNarrative;
                if (narrativeExpected)
                {
                    AssertTrue(fixture.label + " " + promptLevel
                            + " keeps the selected narrative field",
                        narrativeKept);
                    for (int selectedIndex = 0;
                        selectedIndex < selected.selectedCandidates.Count;
                        selectedIndex++)
                    {
                        AssertContains(fixture.label + " " + promptLevel
                                + " projects selected narrative fact " + selectedIndex,
                            plan.userPrompt,
                            selected.selectedCandidates[selectedIndex].text);
                    }
                }
                else
                {
                    AssertTrue(fixture.label + " " + promptLevel
                            + " explicitly reports the whole narrative field as budget-cut",
                        !narrativeKept
                        && plan.contextSelectionReport.cut.Any(
                            row => row.source == NarrativeContextPrompt.Source)
                        && selected.selectedCandidates.All(
                            row => plan.userPrompt.IndexOf(
                                row.text, StringComparison.Ordinal) < 0));
                }
                bool beliefKept = plan.contextSelectionReport.kept.Any(
                    row => row.source == BeliefContextPrompt.Source);
                bool beliefExpected = promptLevel == PromptContextDetailLevel.Full
                    || promptLevel == PromptContextDetailLevel.Balanced
                        && fixture.expectBalancedBelief;
                if (!beliefExpected)
                {
                    AssertTrue(fixture.label + " " + promptLevel
                            + " deterministically cuts the whole optional belief field",
                        !beliefKept
                        && plan.contextSelectionReport.cut.Any(
                            row => row.source == BeliefContextPrompt.Source)
                        && plan.userPrompt.IndexOf(
                            target.displayLabel, StringComparison.Ordinal) < 0
                        && !plan.userPrompt.Contains("belief context:"));
                }
                else
                {
                    AssertTrue(fixture.label + " " + promptLevel
                            + " keeps the whole optional belief field",
                        beliefKept);
                    AssertContains(fixture.label + " " + promptLevel
                            + " projects the selected belief",
                        plan.userPrompt, target.displayLabel);
                }
                AssertTrue(fixture.label + " " + promptLevel
                        + " excludes ambiguous and unrelated doctrine",
                    plan.userPrompt.IndexOf(lexicalBait.displayLabel, StringComparison.Ordinal) < 0
                    && plan.userPrompt.IndexOf(aliasBait.displayLabel, StringComparison.Ordinal) < 0
                    && plan.userPrompt.IndexOf(unrelated.displayLabel, StringComparison.Ordinal) < 0);
                AssertTrue(fixture.label + " " + promptLevel
                        + " keeps the prompt-wide context budget intact",
                    plan.contextSelectionReport.outputChars
                        <= plan.contextSelectionReport.budgetChars);
                AssertTrue(fixture.label + " " + promptLevel
                        + " keeps at most one bounded belief field",
                    plan.contextSelectionReport.kept.Count(
                        row => row.source == BeliefContextPrompt.Source) <= 1);

                bool structureExpected = beliefKept && fixture.supportsCosmology
                    && !fixture.suppressCosmology
                    && promptLevel != PromptContextDetailLevel.Compact;
                bool deityExpected = beliefKept && fixture.supportsCosmology
                    && !fixture.suppressCosmology;
                AssertTrue(fixture.label + " " + promptLevel
                        + " structure appears only when supported and budgeted",
                    structureExpected
                        == plan.userPrompt.Contains("structure: Phase 5C Structure"));
                AssertTrue(fixture.label + " " + promptLevel
                        + " deity appears only when supported and projected",
                    deityExpected
                        == plan.userPrompt.Contains("deity: Phase 5C Deity"));
                if (promptLevel == PromptContextDetailLevel.Full)
                {
                    AssertContains(fixture.label + " Full prompt receives the selected N3-I fact",
                        plan.userPrompt, interpretation);
                }
            }

            return resolution;
        }

        private static void AssertPhase5CAmbiguityAndInactivePaths(
            BeliefPolicySnapshot policy)
        {
            BeliefPreceptFact silver = Phase5CPrecept(
                "AmbiguousSilver_5C", "AmbiguousIssueSilver_5C",
                "silver orchard covenant mercy", "silver interpretation");
            BeliefPreceptFact golden = Phase5CPrecept(
                "AmbiguousGolden_5C", "AmbiguousIssueGolden_5C",
                "golden lantern covenant mercy", "golden interpretation");
            Phase5CCombinedFixture fixture = new Phase5CCombinedFixture
            {
                label = "ambiguous doctrine",
                domain = "Thought",
                sourceDefName = "AmbiguousThought_5C",
                facet = NarrativeFacetTokens.AmbientPressure,
                phase = "ambiguous",
                topic = "ambiguous"
            };
            BeliefSnapshot snapshot = Phase5CSnapshot(fixture, silver, golden);
            BeliefEventEvidence evidence = Phase5CEvidence(fixture, null, null);
            evidence.matchFields.Add(new BeliefEvidenceTextFact
            {
                field = "event_label",
                value = "silver orchard covenant mercy golden lantern"
            });
            BeliefStanceResolution ambiguous = EventRelativeStanceResolver.Resolve(
                new BeliefResolutionRequest
                {
                    snapshot = snapshot,
                    evidence = evidence,
                    policy = policy,
                    mode = BeliefResolutionModeTokens.EventEnrichment,
                    deterministicSeed = 3
                });
            AssertEqual("Phase 5C near-tied doctrine is rejected as ambiguous",
                BeliefAutomaticCoverageOutcomeTokens.Ambiguous,
                ambiguous.automaticCoverage.outcome);
            AssertEqual("Phase 5C ambiguous doctrine emits no stance", 0,
                ambiguous.stances.Count);
            AssertEqual("Phase 5C ambiguous doctrine emits no saved belief block",
                string.Empty,
                BeliefContextFormatter.Format(
                    ambiguous, NarrativeDetailLevelTokens.Full, policy));
            AssertTrue("Phase 5C ambiguous doctrine cannot create an N3-I candidate",
                IdeologyNarrativeSnapshotFactory.Create(
                    ambiguous, evidence.narrative, policy, "must remain absent") == null);
            AssertPhase5CEmptyBeliefPrompt(
                "Phase 5C ambiguous doctrine", string.Empty, policy);

            snapshot.ideologyActive = false;
            BeliefStanceResolution inactive = EventRelativeStanceResolver.Resolve(
                new BeliefResolutionRequest
                {
                    snapshot = snapshot,
                    evidence = evidence,
                    policy = policy,
                    mode = BeliefResolutionModeTokens.EventEnrichment
                });
            AssertEqual("Phase 5C no-Ideology path remains unavailable rather than guessed",
                BeliefAutomaticCoverageReasonTokens.UnavailableSnapshot,
                inactive.automaticCoverage.reason);
            AssertEqual("Phase 5C no-Ideology path emits no belief block", string.Empty,
                BeliefContextFormatter.Format(
                    inactive, NarrativeDetailLevelTokens.Full, policy));
            AssertPhase5CEmptyBeliefPrompt(
                "Phase 5C no-Ideology path", string.Empty, policy);
        }

        private static void AssertPhase5CEmptyBeliefPrompt(
            string label,
            string beliefContext,
            BeliefPolicySnapshot beliefPolicy)
        {
            DiaryEventPayload payload = SoloPayload(
                "phase5c-empty-belief",
                "ordinary event",
                "Alice recorded an ordinary event.");
            payload.initiator.beliefContext = beliefContext;
            DiaryPolicySnapshot promptPolicy = Policy(
                combat: false, important: false);
            promptPolicy.beliefPolicy = beliefPolicy;
            promptPolicy.beliefContextInstruction =
                "This instruction must disappear with an empty block.";
            promptPolicy.Template(DiaryPipelineTemplates.SoloDefault).fields.Add(
                Field("belief context", BeliefContextPrompt.Source));
            DiaryPromptPlan plan = DiaryPromptPlanner.Build(
                new DiaryPromptRequest
                {
                    payload = payload,
                    policy = promptPolicy,
                    povRole = DiaryPipelineRoles.Initiator,
                    contextDetailLevel = PromptContextDetailLevel.Full
                });
            AssertTrue(label + " leaves the ordinary prompt unchanged",
                !plan.userPrompt.Contains("belief context:")
                && !plan.userPrompt.Contains(
                    "This instruction must disappear"));
        }

        private static void AssertPhase5CEventRepetition(
            BeliefPolicySnapshot policy,
            XElement beliefDef)
        {
            BeliefPreceptFact first = Phase5CPrecept(
                "RepeatFirst_5C", "RepeatIssueFirst_5C",
                "first exact repeated stance", "first exact meaning");
            BeliefPreceptFact second = Phase5CPrecept(
                "RepeatSecond_5C", "RepeatIssueSecond_5C",
                "second exact repeated stance", "second exact meaning");
            first.correlations.Add(new BeliefCorrelationFact
            {
                kind = BeliefCorrelationKindTokens.Thought,
                defName = "SharedRepeatThought_5C",
                valence = BeliefValenceTokens.Neutral
            });
            second.correlations.Add(new BeliefCorrelationFact
            {
                kind = BeliefCorrelationKindTokens.Thought,
                defName = "SharedRepeatThought_5C",
                valence = BeliefValenceTokens.Neutral
            });
            Phase5CCombinedFixture fixture = new Phase5CCombinedFixture
            {
                label = "event repetition",
                domain = "Thought",
                sourceDefName = "SharedRepeatThought_5C",
                facet = NarrativeFacetTokens.AmbientPressure,
                phase = "repeat",
                topic = "repeat"
            };
            BeliefSnapshot snapshot = Phase5CSnapshot(fixture, first, second);
            BeliefEventEvidence evidence = Phase5CEvidence(fixture, null, null);
            evidence.thoughtDefNames.Add("SharedRepeatThought_5C");
            evidence.projection = new BeliefContextProjection
            {
                maximumSelectedStances = 1,
                maximumSupportingMemes = 0,
                maximumContextCharacters = 320,
                includeStructure = false,
                includeDeity = false
            };

            bool changed = false;
            for (int seed = 1; seed <= 256 && !changed; seed++)
            {
                BeliefStanceResolution initial = EventRelativeStanceResolver.Resolve(
                    new BeliefResolutionRequest
                    {
                        snapshot = snapshot,
                        evidence = evidence,
                        policy = policy,
                        mode = BeliefResolutionModeTokens.EventEnrichment,
                        deterministicSeed = seed
                    });
                AssertEqual("Phase 5C initial repetition selection remains singular for seed " + seed,
                    1, initial.stances.Count);
                string interpretation = IdeologyInterpretationFactFormatter.Format(
                    ChildValue(beliefDef, "interpretationFactFormat"),
                    ChildValue(beliefDef, "interpretationFactWithoutDescriptionFormat"),
                    initial.ideologyName,
                    initial.stances[0].precept.displayLabel,
                    initial.stances[0].precept.description,
                    IdeologyNarrativeSnapshotFactory.MaximumNarrativeTextCharacters);
                IdeologyNarrativeSnapshot n3 = IdeologyNarrativeSnapshotFactory.Create(
                    initial, evidence.narrative, policy, interpretation);
                List<NarrativeLensCandidate> candidate = IdeologyNarrativeProvider.Build(
                    new List<NarrativeEvidence> { evidence.narrative }, n3);
                AssertEqual("Phase 5C repetition provider remains singular for seed " + seed,
                    1, candidate.Count);
                List<string> recent = IdeologyNarrativeSelectionHistory.PreceptDefNames(
                    new List<string> { candidate[0].candidateKey },
                    snapshot,
                    policy.maximumRecentSelections);
                BeliefStanceResolution later = EventRelativeStanceResolver.Resolve(
                    new BeliefResolutionRequest
                    {
                        snapshot = snapshot,
                        evidence = evidence,
                        policy = policy,
                        mode = BeliefResolutionModeTokens.EventEnrichment,
                        deterministicSeed = seed,
                        recentSelectionDefNames = recent
                    });
                AssertEqual("Phase 5C later repetition selection remains singular for seed " + seed,
                    1, later.stances.Count);
                changed = later.stances[0].precept.defName
                    != initial.stances[0].precept.defName;
            }

            AssertTrue("Phase 5C shipped repetition penalty changes a later exact-correlation selection",
                changed);
        }

        private static void AssertPhase5CDiagnosticsRemainBoundedAndPrivate(
            List<BeliefStanceResolution> resolutions)
        {
            BeliefAutomaticCoverageAggregate aggregate =
                new BeliefAutomaticCoverageAggregate();
            for (int i = 0; i < resolutions.Count; i++)
                BeliefAutomaticCoverageDiagnostics.Add(
                    aggregate, resolutions[i].automaticCoverage, 3);
            AssertEqual("Phase 5C combined diagnostic sample stays fixed-memory", 3,
                aggregate.observedCount);
            AssertEqual("Phase 5C combined diagnostic reports all omitted samples",
                resolutions.Count - 3, aggregate.droppedCount);
            string formatted = BeliefAutomaticCoverageDiagnostics.Format(aggregate);
            AssertTrue("Phase 5C combined diagnostics contain only fixed mechanics tokens",
                formatted.IndexOf("_target", StringComparison.OrdinalIgnoreCase) < 0
                && formatted.IndexOf("doctrine", StringComparison.OrdinalIgnoreCase) < 0
                && formatted.IndexOf("Alice", StringComparison.OrdinalIgnoreCase) < 0);
        }

        private static void AssertPhase5CLegacyDlcFieldCoexistence(
            BeliefPolicySnapshot beliefPolicy)
        {
            AssertPhase5CLegacyDlcFieldFixture(
                "Ideology + Royalty",
                "Progression",
                "title=Acolyte",
                new[] { "royal title: Acolyte" },
                "A current royal duty remains the primary event fact.",
                beliefPolicy);
            AssertPhase5CLegacyDlcFieldFixture(
                "Ideology + Biotech",
                "Progression",
                "xenotype=Hussar",
                new[] { "xenotype: Hussar" },
                "A verified xenotype transition remains the primary event fact.",
                beliefPolicy);
            AssertPhase5CLegacyDlcFieldFixture(
                "Ideology + Anomaly",
                "Ritual",
                "ritual_type=PsychicRitual",
                new[] { "ritual type: PsychicRitual" },
                "The visible psychic ritual result is known; hidden outcomes are absent.",
                beliefPolicy);
            AssertPhase5CLegacyDlcFieldFixture(
                "Ideology + Odyssey",
                "GravshipJourney",
                "journey_phase=landing",
                new[] { "journey phase: landing" },
                "The verified landing remains the primary journey fact.",
                beliefPolicy);
            AssertPhase5CLegacyDlcFieldFixture(
                "all DLC contexts",
                "Ritual",
                "title=Acolyte; xenotype=Hussar; ritual_type=PsychicRitual; journey_phase=landing",
                new[]
                {
                    "royal title: Acolyte",
                    "xenotype: Hussar",
                    "ritual type: PsychicRitual",
                    "journey phase: landing"
                },
                "The visible psychic ritual result is known.\n"
                + "The verified gravship landing remains current.",
                beliefPolicy);
        }

        private static void AssertPhase5CLegacyDlcFieldFixture(
            string label,
            string domain,
            string gameContext,
            string[] expectedDlcFields,
            string narrativeContext,
            BeliefPolicySnapshot beliefPolicy)
        {
            DiaryEventPayload payload = SoloPayload(
                "phase5c-dlc-" + Phase5CSafeToken(label),
                "combined DLC event",
                "Alice recorded one verified change.");
            payload.domain = domain;
            payload.gameContext = gameContext;
            payload.initiator.beliefContext =
                "relevant precept: the verified change matters";
            payload.initiator.narrativeContext = narrativeContext;

            DiaryPolicySnapshot policy = Policy(combat: false, important: true);
            policy.beliefPolicy = beliefPolicy;
            policy.beliefContextInstruction = "Use this event-relevant live stance.";
            policy.narrativeContextInstruction =
                "Use only these selected, source-owned DLC facts.";
            DiaryTemplatePolicy template =
                policy.Template(DiaryPipelineTemplates.SoloImportant);
            template.fields.Add(ContextField("ritual type", "ritual_type"));
            template.fields.Add(Field(
                "narrative context", NarrativeContextPrompt.Source));
            template.fields.Add(Field(
                "belief context", BeliefContextPrompt.Source));

            PromptContextDetailLevel[] levels =
            {
                PromptContextDetailLevel.Full,
                PromptContextDetailLevel.Balanced,
                PromptContextDetailLevel.Compact
            };
            for (int levelIndex = 0; levelIndex < levels.Length; levelIndex++)
            {
                PromptContextDetailLevel level = levels[levelIndex];
                DiaryPromptPlan plan = DiaryPromptPlanner.Build(
                    new DiaryPromptRequest
                    {
                        payload = payload,
                        policy = policy,
                        povRole = DiaryPipelineRoles.Initiator,
                        contextDetailLevel = level
                    });

                AssertTrue(label + " keeps relevant Ideology context in " + level,
                    plan.contextSelectionReport.kept.Any(
                        row => row.source == BeliefContextPrompt.Source));
                AssertContains(label + " renders relevant Ideology context in " + level,
                    plan.userPrompt,
                    "belief context: Use this event-relevant live stance.");
                for (int fieldIndex = 0;
                    fieldIndex < expectedDlcFields.Length;
                    fieldIndex++)
                {
                    AssertContains(label + " keeps DLC fact " + fieldIndex
                            + " beside Ideology in " + level,
                        plan.userPrompt,
                        expectedDlcFields[fieldIndex]);
                }
                AssertTrue(label + " " + level
                        + " obeys the shared context-character cap",
                    plan.contextSelectionReport.outputChars
                        <= plan.contextSelectionReport.budgetChars);
                if (level == PromptContextDetailLevel.Full)
                {
                    AssertContains(label + " Full prompt carries the bounded selected lens",
                        plan.userPrompt,
                        "narrative context: Use only these selected, source-owned DLC facts.");
                }
            }
        }

        private static BeliefSnapshot Phase5CSnapshot(
            Phase5CCombinedFixture fixture,
            params BeliefPreceptFact[] precepts)
        {
            BeliefSnapshot snapshot = new BeliefSnapshot
            {
                ideologyActive = true,
                pawnId = "Phase5CPawn",
                capturedTick = 5000,
                ideologyId = "Phase5CIdeology",
                ideologyName = "Phase 5C Ideoligion",
                roleName = "Moral guide",
                certainty = new BeliefCertaintyFact
                {
                    hasCurrent = true,
                    current = 0.72f
                }
            };
            if (precepts != null) snapshot.precepts.AddRange(precepts);
            if (fixture != null && fixture.supportsCosmology)
            {
                BeliefMemeFact meme = new BeliefMemeFact
                {
                    defName = "Phase5CMeme",
                    label = "Phase 5C Meme",
                    description = "A bounded supporting worldview fact."
                };
                snapshot.memes.Add(meme);
                snapshot.structure = new BeliefMemeFact
                {
                    defName = "Phase5CStructure",
                    label = "Phase 5C Structure",
                    description = "A bounded structure outlook.",
                    isStructure = true
                };
                snapshot.deities.Add(new BeliefDeityFact
                {
                    name = "Phase 5C Deity",
                    relatedMemeDefName = meme.defName
                });
                if (precepts != null && precepts.Length > 0 && precepts[0] != null)
                    precepts[0].associatedMemeDefNames.Add(meme.defName);
            }
            return snapshot;
        }

        private static BeliefEventEvidence Phase5CEvidence(
            Phase5CCombinedFixture fixture,
            BeliefPreceptFact target,
            BeliefPreceptFact lexicalBait)
        {
            BeliefEventEvidence evidence = new BeliefEventEvidence
            {
                narrative = new NarrativeEvidence
                {
                    eventId = "phase5c-" + Phase5CSafeToken(fixture.label),
                    tick = 5000,
                    povPawnId = "Phase5CPawn",
                    povRole = DiaryPipelineRoles.Initiator,
                    facet = fixture.facet,
                    phase = fixture.phase,
                    subjectKind = NarrativeSubjectKindTokens.Pawn,
                    subjectId = "Phase5CPawn",
                    subjectLabel = "Alice",
                    beliefTopics = new List<string> { fixture.topic },
                    salience = NarrativeSalienceTokens.Meaningful,
                    pawnCanKnow = true,
                    sourceDomain = fixture.domain,
                    sourceDefName = fixture.sourceDefName
                },
                groupKey = Phase5CSafeToken(fixture.label)
            };
            if (fixture.matchKind == BeliefCorrelationKindTokens.Thought)
                evidence.thoughtDefNames.Add(fixture.matchIdentity);
            else if (fixture.matchKind == BeliefCorrelationKindTokens.HistoryEvent)
                evidence.historyEventDefNames.Add(fixture.matchIdentity);
            else if (fixture.matchKind == "issue")
                evidence.issueDefNames.Add(fixture.matchIdentity);
            else if (fixture.matchKind == "source" && target != null)
            {
                evidence.sourcePreceptInstanceId = target.instanceId;
                evidence.sourcePreceptDefName = target.defName;
            }
            if (fixture.proveAliasBait)
                evidence.semanticAliasTokens.Add("rituals");
            if (lexicalBait != null)
            {
                evidence.matchFields.Add(new BeliefEvidenceTextFact
                {
                    field = "event_label",
                    value = lexicalBait.displayLabel
                });
            }
            if (fixture.suppressCosmology)
            {
                evidence.projection = new BeliefContextProjection
                {
                    maximumSelectedStances = 1,
                    maximumSupportingMemes = 1,
                    maximumContextCharacters = 320,
                    includeRole = true,
                    includeCertainty = true,
                    includeStructure = false,
                    includeDeity = false,
                    includeNarrativeInterpretation = true
                };
            }
            return evidence;
        }

        private static BeliefPreceptFact Phase5CPrecept(
            string defName,
            string issueDefName,
            string label,
            string description)
        {
            string stable = Phase5CSafeToken(defName);
            return new BeliefPreceptFact
            {
                instanceId = stable + "_instance",
                defName = stable,
                issue = new BeliefIssueFact
                {
                    defName = Phase5CSafeToken(issueDefName),
                    label = label,
                    description = description
                },
                displayLabel = label,
                description = description,
                visible = true,
                impactRank = 1
            };
        }

        private static NarrativeLensCandidate Phase5CPrimaryCandidate(
            Phase5CCombinedFixture fixture,
            NarrativeEvidence evidence)
        {
            return new NarrativeLensCandidate
            {
                candidateKey = fixture.provider + "|" + fixture.category + "|"
                    + Phase5CSafeToken(fixture.label),
                provider = fixture.provider,
                category = fixture.category,
                text = fixture.primaryText,
                facet = evidence.facet,
                subjectKind = evidence.subjectKind,
                subjectId = evidence.subjectId,
                topicTokens = new List<string> { "primary_" + Phase5CSafeToken(fixture.topic) },
                sourceEventId = evidence.eventId,
                sourceTick = evidence.tick,
                salience = evidence.salience,
                pawnCanKnow = true,
                providerAvailable = true,
                hasVerifiedPovConnection = true
            };
        }

        private static BeliefPolicySnapshot LoadPhase5CBeliefPolicy(
            out XElement beliefDef)
        {
            XDocument document = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryBeliefPolicyDef.xml"));
            beliefDef = FindDef(
                document, "PawnDiary.DiaryBeliefPolicyDef", "Diary_BeliefPolicy");
            BeliefPolicyBuilder builder = BeliefPolicyBuilder.CreateDefault();
            builder.enabled = Phase5CBool(beliefDef, "enabled");
            builder.maximumPreceptCandidates =
                Phase5CInt(beliefDef, "maximumPreceptCandidates");
            builder.maximumMemeCandidates =
                Phase5CInt(beliefDef, "maximumMemeCandidates");
            builder.maximumDeityCandidates =
                Phase5CInt(beliefDef, "maximumDeityCandidates");
            builder.maximumSelectedStances = Phase5CInt(beliefDef, "maximumSelectedStances");
            builder.defaultSelectedStances = Phase5CInt(beliefDef, "defaultSelectedStances");
            builder.maximumSupportingMemes = Phase5CInt(beliefDef, "maximumSupportingMemes");
            builder.maximumRecentSelections = Phase5CInt(beliefDef, "maximumRecentSelections");
            builder.maximumReflectedBeliefSourceIds =
                Phase5CInt(beliefDef, "maximumReflectedBeliefSourceIds");
            builder.beliefScanIntervalTicks =
                Phase5CInt(beliefDef, "beliefScanIntervalTicks");
            builder.maximumBeliefPawnsPerScan =
                Phase5CInt(beliefDef, "maximumBeliefPawnsPerScan");
            builder.pendingBeliefEvidenceMaxAgeTicks =
                Phase5CInt(beliefDef, "pendingBeliefEvidenceMaxAgeTicks");
            builder.maximumHistoryCorrelationEntries =
                Phase5CInt(beliefDef, "maximumHistoryCorrelationEntries");
            builder.historyCorrelationWindowTicks =
                Phase5CInt(beliefDef, "historyCorrelationWindowTicks");
            builder.maximumMutationCorrelationEntries =
                Phase5CInt(beliefDef, "maximumMutationCorrelationEntries");
            builder.mutationCorrelationWindowTicks =
                Phase5CInt(beliefDef, "mutationCorrelationWindowTicks");
            builder.maximumAutomaticDiagnosticSamples =
                Phase5CInt(beliefDef, "maximumAutomaticDiagnosticSamples");
            builder.maximumFieldCharacters =
                Phase5CInt(beliefDef, "maximumFieldCharacters");
            builder.maximumNormalizedTokensPerField =
                Phase5CInt(beliefDef, "maximumNormalizedTokensPerField");
            builder.maximumLexicalFieldsPerDocument =
                Phase5CInt(beliefDef, "maximumLexicalFieldsPerDocument");
            builder.maximumLexicalTokensPerDocument =
                Phase5CInt(beliefDef, "maximumLexicalTokensPerDocument");
            builder.maximumDescriptionCharacters =
                Phase5CInt(beliefDef, "maximumDescriptionCharacters");
            builder.maximumIdentifierCharacters =
                Phase5CInt(beliefDef, "maximumIdentifierCharacters");
            builder.maximumTotalCharacters = Phase5CInt(beliefDef, "maximumTotalCharacters");
            builder.maximumTotalLines = Phase5CInt(beliefDef, "maximumTotalLines");
            builder.minimumLexicalConfidence =
                Phase5CFloat(beliefDef, "minimumLexicalConfidence");
            builder.lexicalRunnerUpMargin =
                Phase5CFloat(beliefDef, "lexicalRunnerUpMargin");
            builder.fuzzyRunnerUpMargin =
                Phase5CFloat(beliefDef, "fuzzyRunnerUpMargin");
            builder.minimumDistinctiveTokenMatches =
                Phase5CInt(beliefDef, "minimumDistinctiveTokenMatches");
            builder.uniqueTokenMinimumCharacters =
                Phase5CInt(beliefDef, "uniqueTokenMinimumCharacters");
            builder.fuzzyTokenMinimumCharacters =
                Phase5CInt(beliefDef, "fuzzyTokenMinimumCharacters");
            builder.fuzzyMinimumDistinctiveMatches =
                Phase5CInt(beliefDef, "fuzzyMinimumDistinctiveMatches");
            builder.fuzzySimilarityMinimum =
                Phase5CFloat(beliefDef, "fuzzySimilarityMinimum");
            builder.commonTokenMinimumDocuments =
                Phase5CInt(beliefDef, "commonTokenMinimumDocuments");
            builder.commonTokenDocumentFraction =
                Phase5CFloat(beliefDef, "commonTokenDocumentFraction");
            builder.phraseMatchScore = Phase5CFloat(beliefDef, "phraseMatchScore");
            builder.tokenMatchScore = Phase5CFloat(beliefDef, "tokenMatchScore");
            builder.uniqueTokenBonus = Phase5CFloat(beliefDef, "uniqueTokenBonus");
            builder.fuzzyMatchScore = Phase5CFloat(beliefDef, "fuzzyMatchScore");
            builder.selectionWeightBase =
                Phase5CFloat(beliefDef, "selectionWeightBase");
            builder.secondSlotMinimumScore =
                Phase5CFloat(beliefDef, "secondSlotMinimumScore");
            builder.recentSelectionPenalty =
                Phase5CFloat(beliefDef, "recentSelectionPenalty");
            builder.requiredByMemeBonus =
                Phase5CFloat(beliefDef, "requiredByMemeBonus");
            builder.proselytizingRoleBonus =
                Phase5CFloat(beliefDef, "proselytizingRoleBonus");
            builder.meaningfulSalienceBonus =
                Phase5CFloat(beliefDef, "meaningfulSalienceBonus");
            builder.majorSalienceBonus =
                Phase5CFloat(beliefDef, "majorSalienceBonus");
            builder.terminalSalienceBonus =
                Phase5CFloat(beliefDef, "terminalSalienceBonus");
            builder.highImpactBonus = Phase5CFloat(beliefDef, "highImpactBonus");
            builder.mediumImpactBonus = Phase5CFloat(beliefDef, "mediumImpactBonus");
            builder.lowImpactBonus = Phase5CFloat(beliefDef, "lowImpactBonus");
            builder.certaintyMeaningfulDelta =
                Phase5CFloat(beliefDef, "certaintyMeaningfulDelta");
            builder.certaintyMajorDelta =
                Phase5CFloat(beliefDef, "certaintyMajorDelta");
            builder.includeStructure = Phase5CBool(beliefDef, "includeStructure");
            builder.includeRelatedDeity = Phase5CBool(beliefDef, "includeRelatedDeity");
            builder.includeKeyDeity = Phase5CBool(beliefDef, "includeKeyDeity");
            builder.allowDeterministicAlternativeDeity =
                Phase5CBool(beliefDef, "allowDeterministicAlternativeDeity");
            builder.quietReflectionChance =
                Phase5CFloat(beliefDef, "quietReflectionChance");
            builder.recentBeliefEventWindowTicks =
                Phase5CInt(beliefDef, "recentBeliefEventWindowTicks");
            builder.beliefReflectionCooldownTicks =
                Phase5CInt(beliefDef, "beliefReflectionCooldownTicks");
            builder.maximumBeliefReflectionsPerQuadrum =
                Phase5CInt(beliefDef, "maximumBeliefReflectionsPerQuadrum");
            builder.beliefReflectionMaxTokens =
                Phase5CInt(beliefDef, "beliefReflectionMaxTokens");

            Phase5CCopyBeliefScores(
                beliefDef, "tierScores", builder.tierScores);
            Phase5CCopyBeliefScores(
                beliefDef, "eventFieldWeights", builder.eventFieldWeights);
            Phase5CCopyBeliefScores(
                beliefDef, "beliefFieldWeights", builder.beliefFieldWeights);
            builder.certaintyBands.Clear();
            foreach (XElement row in Phase5CRows(beliefDef, "certaintyBands"))
            {
                builder.certaintyBands.Add(new BeliefCertaintyBand(
                    ChildValue(row, "token"),
                    Phase5CFloat(row, "minimum"),
                    ChildValue(row, "phrase")));
            }
            builder.semanticAliases.Clear();
            foreach (XElement row in Phase5CRows(beliefDef, "semanticAliases"))
            {
                builder.semanticAliases.Add(new BeliefSemanticAlias(
                    ChildValue(row, "topicToken"),
                    Phase5CStrings(row, "aliases")));
            }
            builder.eventEvidenceRules.Clear();
            foreach (XElement row in Phase5CRows(beliefDef, "eventEvidenceRules"))
            {
                builder.eventEvidenceRules.Add(new BeliefEventEvidenceRule(
                    ChildValue(row, "key"),
                    ChildValue(row, "sourceDomain"),
                    ChildValue(row, "sourceDefName"),
                    ChildValue(row, "groupKey"),
                    ChildValue(row, "facet"),
                    ChildValue(row, "phase"),
                    ChildValue(row, "povRole"),
                    ChildValue(row, "mutationCauseToken"),
                    Phase5CStrings(row, "addTopics"),
                    Phase5CStrings(row, "addSemanticAliases")));
            }
            builder.foodEvidenceRules.Clear();
            foreach (XElement row in Phase5CRows(beliefDef, "foodEvidenceRules"))
            {
                builder.foodEvidenceRules.Add(new BeliefFoodEvidenceRule(
                    ChildValue(row, "key"),
                    ChildValue(row, "ingredientKind"),
                    ChildValue(row, "groupKey"),
                    ChildValue(row, "matchField")));
            }
            Phase5CCopyBeliefStrings(
                beliefDef, "lexicalExclusions", builder.lexicalExclusions);
            Phase5CCopyBeliefStrings(
                beliefDef, "proselytizingPovRoles", builder.proselytizingPovRoles);
            builder.canonicalEventOwnershipRules.Clear();
            foreach (XElement row in Phase5CRows(
                beliefDef, "canonicalEventOwnershipRules"))
            {
                builder.canonicalEventOwnershipRules.Add(
                    new BeliefCanonicalEventOwnershipRule(
                        ChildValue(row, "sourceDomain"),
                        ChildValue(row, "sourceDefName"),
                        ChildValue(row, "downstreamGroupDefName")));
            }
            builder.mutationEventRules.Clear();
            foreach (XElement row in Phase5CRows(beliefDef, "mutationEventRules"))
            {
                builder.mutationEventRules.Add(new BeliefMutationEventRule(
                    ChildValue(row, "sourceDomain"),
                    ChildValue(row, "sourceDefName"),
                    ChildValue(row, "downstreamGroupDefName"),
                    ChildValue(row, "subjectRole"),
                    ChildValue(row, "evidenceGroupKey"),
                    ChildValue(row, "requiredCauseToken"),
                    ChildValue(row, "conversionResult"),
                    ChildValue(row, "certaintyDirection"),
                    ChildValue(row, "ideologyChange"),
                    Phase5CBool(row, "requireAttemptedIdeology")));
            }
            builder.counselEventRules.Clear();
            foreach (XElement row in Phase5CRows(beliefDef, "counselEventRules"))
            {
                builder.counselEventRules.Add(new CounselEventRule(
                    ChildValue(row, "sourceDefName"),
                    ChildValue(row, "downstreamGroupDefName"),
                    ChildValue(row, "resultToken"),
                    ChildValue(row, "moodEffectToken")));
            }
            builder.correlationOverrides.Clear();
            foreach (XElement row in Phase5CRows(beliefDef, "correlationOverrides"))
            {
                builder.correlationOverrides.Add(new BeliefCorrelationCorrection(
                    ChildValue(row, "key"),
                    ChildValue(row, "action"),
                    ChildValue(row, "preceptDefName"),
                    ChildValue(row, "issueDefName"),
                    ChildValue(row, "memeDefName"),
                    ChildValue(row, "sourceDomain"),
                    ChildValue(row, "sourceDefName"),
                    ChildValue(row, "groupKey"),
                    ChildValue(row, "topicToken")));
            }
            builder.detailBudgets.Clear();
            foreach (XElement row in Phase5CRows(beliefDef, "detailBudgets"))
            {
                builder.detailBudgets.Add(new BeliefDetailBudget(
                    ChildValue(row, "detailLevel"),
                    Phase5CInt(row, "maximumLines"),
                    Phase5CInt(row, "maximumCharacters"),
                    Phase5CBool(row, "includeDescriptions"),
                    Phase5CBool(row, "includeStructure"),
                    Phase5CBool(row, "includeMemes"),
                    Phase5CBool(row, "includeDeity")));
            }
            return builder.Build();
        }

        private static NarrativePolicySnapshot LoadPhase5CNarrativePolicy(
            out XElement narrativeDef)
        {
            XDocument document = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryNarrativeContinuityDefs.xml"));
            narrativeDef = FindDef(
                document,
                "PawnDiary.DiaryNarrativeContinuityDef",
                "Diary_NarrativeContinuity");
            NarrativePolicySnapshot policy = NarrativePolicySnapshot.CreateDefault();
            policy.enabled = Phase5CBool(narrativeDef, "enabled");
            policy.maxEvidencePerPov = Phase5CInt(narrativeDef, "maxEvidencePerPov");
            policy.maxCandidates = Phase5CInt(narrativeDef, "maxCandidates");
            policy.maxSelectedCandidates =
                Phase5CInt(narrativeDef, "maxSelectedCandidates");
            policy.maxRecentSelectedCandidateKeys =
                Phase5CInt(narrativeDef, "maxRecentSelectedCandidateKeys");
            policy.maximumCandidateAgeTicks =
                Phase5CInt(narrativeDef, "maximumCandidateAgeTicks");
            policy.ageDecayWindowTicks =
                Phase5CInt(narrativeDef, "ageDecayWindowTicks");
            policy.ageDecayFloor =
                Phase5CFloat(narrativeDef, "ageDecayFloor");
            policy.repetitionPenalty =
                Phase5CFloat(narrativeDef, "repetitionPenalty");
            policy.exactArcRepetitionPenalty =
                Phase5CFloat(narrativeDef, "exactArcRepetitionPenalty");
            policy.promptFieldLabel = ChildValue(narrativeDef, "promptFieldLabel");
            policy.promptFieldInstruction =
                ChildValue(narrativeDef, "promptFieldInstruction");
            policy.reflectionGlobalCooldownTicks =
                Phase5CInt(narrativeDef, "reflectionGlobalCooldownTicks");
            policy.reflectionMinimumLinkedMemories =
                Phase5CInt(narrativeDef, "reflectionMinimumLinkedMemories");
            policy.reflectionMinimumDistinctPhases =
                Phase5CInt(narrativeDef, "reflectionMinimumDistinctPhases");
            policy.reflectionCandidateScanCap =
                Phase5CInt(narrativeDef, "reflectionCandidateScanCap");
            policy.reflectionMemoryCap =
                Phase5CInt(narrativeDef, "reflectionMemoryCap");
            policy.reflectionMaximumSpanTicks =
                Phase5CInt(narrativeDef, "reflectionMaximumSpanTicks");
            policy.reflectionRequireChangeOrConsequence =
                Phase5CBool(narrativeDef, "reflectionRequireChangeOrConsequence");
            Phase5CCopyNarrativeScores(
                narrativeDef, "relationshipScores", policy.relationshipScores);
            Phase5CCopyNarrativeScores(
                narrativeDef, "facetScores", policy.facetScores);
            Phase5CCopyNarrativeScores(
                narrativeDef, "categoryScores", policy.categoryScores);
            Phase5CCopyNarrativeScores(
                narrativeDef, "salienceScores", policy.salienceScores);
            Phase5CCopyNarrativeScores(
                narrativeDef, "providerScores", policy.providerScores);
            policy.affinityRules.Clear();
            foreach (XElement row in Phase5CRows(narrativeDef, "affinityRules"))
            {
                policy.affinityRules.Add(new NarrativeAffinityRule
                {
                    evidenceToken = ChildValue(row, "evidenceToken"),
                    candidateToken = ChildValue(row, "candidateToken"),
                    score = Phase5CFloat(row, "score")
                });
            }
            policy.categoryCoexistence.Clear();
            foreach (XElement row in Phase5CRows(narrativeDef, "categoryCoexistence"))
            {
                policy.categoryCoexistence.Add(
                    new NarrativeCategoryCoexistenceRule
                    {
                        firstCategory = ChildValue(row, "firstCategory"),
                        secondCategory = ChildValue(row, "secondCategory"),
                        allowed = Phase5CBool(row, "allowed")
                    });
            }
            policy.detailBudgets.Clear();
            foreach (XElement row in Phase5CRows(narrativeDef, "detailBudgets"))
            {
                policy.detailBudgets.Add(new NarrativeDetailBudget
                {
                    detailLevel = ChildValue(row, "detailLevel"),
                    maxLenses = Phase5CInt(row, "maxLenses"),
                    characterBudget = Phase5CInt(row, "characterBudget"),
                    allowExactArcPair = Phase5CBool(row, "allowExactArcPair"),
                    exactArcPairMaxCharacters =
                        Phase5CInt(row, "exactArcPairMaxCharacters")
                });
            }
            policy.reflectionChangeOrConsequenceFacets.Clear();
            policy.reflectionChangeOrConsequenceFacets.AddRange(
                Phase5CStrings(narrativeDef, "reflectionChangeOrConsequenceFacets"));
            policy.reflectionPriorities.Clear();
            foreach (XElement row in Phase5CRows(narrativeDef, "reflectionPriorities"))
            {
                policy.reflectionPriorities.Add(new NarrativeReflectionPriority
                {
                    kind = ChildValue(row, "kind"),
                    priority = Phase5CInt(row, "priority"),
                    cooldownTicks = Phase5CInt(row, "cooldownTicks")
                });
            }
            return policy;
        }

        private static IEnumerable<XElement> Phase5CRows(
            XElement parent,
            string listName)
        {
            XElement list = parent?.Element(listName);
            return list == null
                ? Enumerable.Empty<XElement>()
                : list.Elements("li");
        }

        private static List<string> Phase5CStrings(
            XElement parent,
            string listName)
        {
            return Phase5CRows(parent, listName)
                .Select(row => row.Value)
                .ToList();
        }

        private static void Phase5CCopyBeliefStrings(
            XElement policy,
            string listName,
            List<string> destination)
        {
            destination.Clear();
            destination.AddRange(Phase5CStrings(policy, listName));
        }

        private static void Phase5CCopyBeliefScores(
            XElement policy,
            string listName,
            List<BeliefTokenScore> destination)
        {
            destination.Clear();
            foreach (XElement row in policy.Element(listName)?.Elements("li")
                ?? Enumerable.Empty<XElement>())
            {
                destination.Add(new BeliefTokenScore(
                    ChildValue(row, "token"),
                    Phase5CFloat(row, "score")));
            }
        }

        private static void Phase5CCopyNarrativeScores(
            XElement policy,
            string listName,
            List<NarrativeTokenWeight> destination)
        {
            destination.Clear();
            foreach (XElement row in policy.Element(listName)?.Elements("li")
                ?? Enumerable.Empty<XElement>())
            {
                destination.Add(new NarrativeTokenWeight
                {
                    token = ChildValue(row, "token"),
                    score = Phase5CFloat(row, "score")
                });
            }
        }

        private static string Phase5CDetailValue(
            XElement policy,
            string detailLevel,
            string field)
        {
            XElement row = policy.Element("detailBudgets")?.Elements("li")
                .FirstOrDefault(value =>
                    ChildValue(value, "detailLevel") == detailLevel);
            return ChildValue(row, field);
        }

        private static string Phase5CNarrativeLevel(
            PromptContextDetailLevel level)
        {
            if (level == PromptContextDetailLevel.Full)
                return NarrativeDetailLevelTokens.Full;
            if (level == PromptContextDetailLevel.Compact)
                return NarrativeDetailLevelTokens.Compact;
            return NarrativeDetailLevelTokens.Balanced;
        }

        private static int Phase5CLineCount(string value)
        {
            return string.IsNullOrEmpty(value)
                ? 0
                : value.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static string Phase5CSafeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "phase5c";
            char[] characters = value.ToCharArray();
            for (int i = 0; i < characters.Length; i++)
            {
                if (!char.IsLetterOrDigit(characters[i]) && characters[i] != '_'
                    && characters[i] != '-')
                {
                    characters[i] = '_';
                }
            }
            return new string(characters);
        }

        private static int Phase5CInt(XElement parent, string field)
        {
            return int.Parse(
                ChildValue(parent, field),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);
        }

        private static float Phase5CFloat(XElement parent, string field)
        {
            return float.Parse(
                ChildValue(parent, field),
                NumberStyles.Float,
                CultureInfo.InvariantCulture);
        }

        private static bool Phase5CBool(XElement parent, string field)
        {
            return bool.Parse(ChildValue(parent, field));
        }
    }
}

// Standalone no-game-assembly coverage for Master Wave 10 / Ideology Phase 0. These fixtures use
// arbitrary synthetic identifiers so successful matching proves the resolver is metadata-driven,
// not a hidden catalog of vanilla/DLC doctrine.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    internal static class Program
    {
        private const string ConversionBehaviorTypeName =
            "RimWorld.RitualBehaviorWorker_Conversion";
        private const string ConversionOutcomeTypeName =
            "RimWorld.RitualOutcomeEffectWorker_Conversion";
        private const string SpeechOutcomeTypeName =
            "RimWorld.RitualOutcomeEffectWorker_Speech";
        private static int assertions;

        private static int Main()
        {
            TestPolicyFallbacksAndImmutability();
            TestMissingInactiveEmptyAndKnowledgeGates();
            TestSourcePreceptIdentityPrecedence();
            TestThoughtHistoryAndStructuralPrecedence();
            TestIssueAndMemeIdentityTiers();
            TestLexicalNormalizationAndGuardedMatches();
            TestLexicalCommonConfidenceAndAmbiguityRejection();
            TestLexicalFuzzyAndUnknownTopicBehavior();
            TestAutomaticCoverageDiagnosticsAndPhase5Fixtures();
            TestPhase5CProjectionRestraintAndEventRepetition();
            TestBodyOrganMealRaidAndRitualScenarios();
            TestExactFoodEvidenceClient();
            TestLiveDoctrineIntersectionRedundancyAndCaps();
            TestSecondSlotIndependenceOrderingAndRepetition();
            TestCertaintyBoundariesAndTrends();
            TestFormatterBudgetsSanitationAndWorldviewFacts();
            TestMutationOnlyContextAndFormatting();
            TestEvidenceRulesAndOptionalCorrections();
            TestConfiguredExistingPageEvidenceClients();
            TestObservationBaselineAndReflectionShell();
            TestMalformedUnsafeAndOversizedInputs();
            TestPhase1EvidencePersistenceAndCorrelation();
            TestPhase2MutationCoalescingCorrelationAndOwnership();
            TestPhase2MutationEvidenceSelection();
            TestCounselExactOutcomeContextPolicy();
            TestConversionRitualExactCompletionPolicy();
            TestAuthoritySpeechExactEnrichmentPolicy();
            TestN3IIdeologyInterpretationProvider();
            Console.WriteLine("BeliefContextTests passed " + assertions + " assertions.");
            return 0;
        }

        private static void TestPolicyFallbacksAndImmutability()
        {
            BeliefPolicyBuilder builder = BeliefPolicyBuilder.CreateDefault();
            AssertEqual("default correction list stays empty", 0, builder.correlationOverrides.Count);
            AssertEqual("source tier fallback", 1200f,
                builder.Build().TierScore(BeliefRelevanceTierTokens.SourcePrecept));
            AssertEqual("correlation tier fallback", 1000f,
                builder.Build().TierScore(BeliefRelevanceTierTokens.ExactCorrelation));
            AssertTrue("structural tier outranks lexical fallback",
                builder.Build().TierScore(BeliefRelevanceTierTokens.ExactCorrelation)
                    > builder.Build().TierScore(BeliefRelevanceTierTokens.CorrelationText));

            List<string> aliases = new List<string> { "synthetic concept" };
            builder.semanticAliases.Add(new BeliefSemanticAlias("synthetic_topic", aliases));
            BeliefPolicySnapshot frozen = builder.Build();
            aliases.Add("late mutation");
            builder.semanticAliases.Clear();
            AssertEqual("snapshot deep-copies alias collection", 1, frozen.semanticAliases.Count);
            AssertEqual("snapshot deep-copies alias strings", 1, frozen.semanticAliases[0].aliases.Count);

            BeliefPolicyBuilder malformed = BeliefPolicyBuilder.CreateDefault();
            malformed.maximumSelectedStances = 99;
            malformed.maximumPreceptCandidates = -1;
            malformed.fuzzySimilarityMinimum = float.NaN;
            BeliefPolicySnapshot safe = malformed.Build();
            AssertEqual("hard stance cap survives malformed XML", 2, safe.maximumSelectedStances);
            AssertEqual("candidate fallback survives malformed XML", 128, safe.maximumPreceptCandidates);
            AssertEqual("lexical field hard fallback is bounded", 96, safe.maximumLexicalFieldsPerDocument);
            AssertEqual("lexical token hard fallback is bounded", 256, safe.maximumLexicalTokensPerDocument);
            AssertNear("fuzzy fallback survives NaN", 0.84f, safe.fuzzySimilarityMinimum);
            AssertTrue("pure snapshot exposes shared interpretation category",
                new BeliefStanceResolution().narrativeCategory == NarrativeCategoryTokens.Interpretation);

            BeliefPolicyBuilder missingCompact = BeliefPolicyBuilder.CreateDefault();
            missingCompact.detailBudgets.Clear();
            BeliefDetailBudget compactFallback = missingCompact.Build().DetailBudget(
                NarrativeDetailLevelTokens.Compact);
            AssertTrue("missing Compact budget still omits descriptions", !compactFallback.includeDescriptions);
            AssertTrue("missing Compact budget still omits structure", !compactFallback.includeStructure);
            AssertTrue("missing Compact budget still omits memes", !compactFallback.includeMemes);
            AssertTrue("missing Compact budget retains the shipped deity policy", compactFallback.includeDeity);
            AssertEqual("mutation entry fallback is bounded", 256, safe.maximumMutationCorrelationEntries);
            AssertEqual("mutation window fallback is bounded", 120, safe.mutationCorrelationWindowTicks);
            AssertEqual("belief scan interval fallback is bounded", 250, safe.beliefScanIntervalTicks);
            AssertEqual("belief scan work fallback is bounded", 4, safe.maximumBeliefPawnsPerScan);
            AssertEqual("automatic diagnostic sample fallback is bounded", 4096,
                safe.maximumAutomaticDiagnosticSamples);
            malformed.maximumAutomaticDiagnosticSamples = int.MaxValue;
            AssertEqual("automatic diagnostic sample fallback survives malformed XML", 4096,
                malformed.Build().maximumAutomaticDiagnosticSamples);
            AssertEqual("pending belief age fallback is bounded", 3600000,
                safe.pendingBeliefEvidenceMaxAgeTicks);
            AssertEqual("reflected belief source fallback is bounded", 16,
                safe.maximumReflectedBeliefSourceIds);
            AssertEqual("code fallback suppresses no generic event routes", 0,
                BeliefPolicySnapshot.CreateDefault().canonicalEventOwnershipRules.Count);
            AssertEqual("code fallback enriches no mutation event routes", 0,
                BeliefPolicySnapshot.CreateDefault().mutationEventRules.Count);
        }

        private static void TestAutomaticCoverageDiagnosticsAndPhase5Fixtures()
        {
            BeliefPolicySnapshot policy = BeliefPolicySnapshot.CreateDefault();

            BeliefPreceptFact exactSource = Precept(
                "Mod_Source_5A", "Issue_Source_5A", "quiet source doctrine", 0);
            exactSource.instanceId = "mod-source-instance-5a";
            BeliefPreceptFact lexicalBait = Precept(
                "HighImpact_Lexical_Bait", "Issue_Bait", "luminous orchard authority", 3);
            BeliefEventEvidence exactSourceEvidence = SourceEvidence(
                exactSource.instanceId, exactSource.defName);
            exactSourceEvidence.matchFields.Add(Text("event_label", lexicalBait.displayLabel));
            BeliefStanceResolution exactSourceResult = Resolve(
                Snapshot(exactSource, lexicalBait), exactSourceEvidence, policy);
            AssertSelected("Phase 5A exact source outranks high-impact lexical bait",
                exactSourceResult, exactSource.defName);
            AssertCoverage("Phase 5A exact source diagnostic", exactSourceResult,
                BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation,
                BeliefRelevanceSourceTokens.SourcePrecept,
                BeliefRelevanceTierTokens.SourcePrecept);
            AssertAutomaticMetadataFixture(
                "ordinary social/thought",
                BeliefCorrelationKindTokens.Thought,
                "ModSocialThought_5A",
                "interaction",
                "Interaction_CustomSocial");
            AssertAutomaticMetadataFixture(
                "ritual/apostasy",
                BeliefCorrelationKindTokens.HistoryEvent,
                "ModApostasyHistory_5A",
                "ritual",
                "Ritual_CustomApostasy");
            AssertAutomaticMetadataFixture(
                "Biotech",
                BeliefCorrelationKindTokens.Thought,
                "ModGrowthVatThought_5A",
                "biotech",
                "GrowthVat_Custom");
            AssertAutomaticMetadataFixture(
                "Anomaly",
                BeliefCorrelationKindTokens.HistoryEvent,
                "ModPsychicRitualHistory_5A",
                "anomaly",
                "PsychicRitual_Custom");
            AssertAutomaticMetadataFixture(
                "Odyssey",
                BeliefCorrelationKindTokens.Thought,
                "ModVacuumHabitatThought_5A",
                "odyssey",
                "VacuumHabitat_Custom");
            AssertAutomaticMetadataFixture(
                "arbitrary-ID mod public metadata",
                BeliefCorrelationKindTokens.HistoryEvent,
                "TotallyUnknownHistory_91827",
                "modded",
                "ThirdPartyEvent_7721");
            AssertCrossDlcOrdinaryMetadataFixture(
                "Biotech bloodfeeder",
                "Bloodfeeders_Reviled",
                "Bloodfeeders",
                BeliefCorrelationKindTokens.Thought,
                "Bloodfeeders_Reviled_Opinion_Bloodfeeder");
            AssertCrossDlcOrdinaryMetadataFixture(
                "Anomaly psychic ritual",
                "PsychicRituals_Exalted",
                "PsychicRituals",
                BeliefCorrelationKindTokens.Thought,
                "NoPsychicRituals");
            AssertCrossDlcOrdinaryMetadataFixture(
                "Odyssey space habitat",
                "SpaceHabitat_Preferred",
                "SpaceHabitat",
                BeliefCorrelationKindTokens.Thought,
                "SpaceHabitat_Mood");
            BeliefPreceptFact authority = Precept(
                "ModAuthority_5A", "IssueAuthority_5A", "ranked authority", 0);
            BeliefPreceptFact authorityBait = Precept(
                "AuthorityLexicalBait_5A", "OtherAuthorityIssue_5A", "ceremonial speaker", 3);
            BeliefEventEvidence authorityEvidence = Evidence(true);
            authorityEvidence.narrative.sourceDomain = "ritual";
            authorityEvidence.narrative.povRole = "moral_guide";
            authorityEvidence.issueDefNames.Add("IssueAuthority_5A");
            authorityEvidence.matchFields.Add(Text("ritual_label", "ceremonial speaker"));
            BeliefStanceResolution authorityResult = Resolve(
                Snapshot(authority, authorityBait), authorityEvidence, policy);
            AssertSelected("role/authority structural metadata beats lexical impact",
                authorityResult, authority.defName);
            AssertCoverage("role/authority structural diagnostic", authorityResult,
                BeliefAutomaticCoverageOutcomeTokens.StructuralCorrelation,
                BeliefRelevanceSourceTokens.IssueIdentity,
                BeliefRelevanceTierTokens.DirectIdentity);

            BeliefMemeFact structuralMeme = Meme("ModStructureMeme_5A", "ranked order", 1);
            BeliefPreceptFact memeLinked = Precept(
                "ModMemeLinked_5A", "IssueMeme_5A", "ordered office", 0);
            memeLinked.requiredMemeDefNames.Add(structuralMeme.defName);
            BeliefSnapshot memeSnapshot = Snapshot(memeLinked);
            memeSnapshot.memes.Add(structuralMeme);
            BeliefEventEvidence memeEvidence = Evidence(true);
            memeEvidence.memeDefNames.Add(structuralMeme.defName);
            BeliefStanceResolution memeResult = Resolve(memeSnapshot, memeEvidence, policy);
            AssertCoverage("meme structural diagnostic", memeResult,
                BeliefAutomaticCoverageOutcomeTokens.StructuralCorrelation,
                BeliefRelevanceSourceTokens.MemeAssociation,
                BeliefRelevanceTierTokens.DirectIdentity);

            BeliefPolicyBuilder semanticBuilder = BeliefPolicyBuilder.CreateDefault();
            semanticBuilder.semanticAliases.Add(new BeliefSemanticAlias(
                "authority_speech",
                new List<string> { "ranked authority" }));
            BeliefEventEvidence semanticEvidence = Evidence(true);
            semanticEvidence.semanticAliasTokens.Add("authority_speech");
            BeliefStanceResolution semanticResult = Resolve(
                Snapshot(Precept("SemanticAuthority_5A", "SemanticIssue_5A", "ranked authority", 1)),
                semanticEvidence,
                semanticBuilder.Build());
            AssertCoverage("semantic-alias match diagnostic", semanticResult,
                BeliefAutomaticCoverageOutcomeTokens.SemanticAlias,
                semanticResult.stances[0].relevanceSource,
                semanticResult.stances[0].relevanceTier);
            AssertTrue("semantic-alias winner exposes an XML-relative confidence band",
                semanticResult.automaticCoverage.confidenceBand
                    == BeliefAutomaticCoverageConfidenceBandTokens.Qualified
                || semanticResult.automaticCoverage.confidenceBand
                    == BeliefAutomaticCoverageConfidenceBandTokens.Strong);
            BeliefPolicyBuilder localizedSemanticBuilder = BeliefPolicyBuilder.CreateDefault();
            localizedSemanticBuilder.semanticAliases.Add(new BeliefSemanticAlias(
                "space_habitat",
                new List<string> { "космическая среда обитания" }));
            BeliefEventEvidence localizedSemanticEvidence = Evidence(true);
            localizedSemanticEvidence.semanticAliasTokens.Add("space_habitat");
            BeliefStanceResolution localizedSemanticResult = Resolve(
                Snapshot(Precept("LocalizedSpaceHabitat_5B", "LocalizedSpaceIssue_5B",
                    "КОСМИЧЕСКАЯ СРЕДА ОБИТАНИЯ", 1)),
                localizedSemanticEvidence,
                localizedSemanticBuilder.Build());
            AssertCoverage("localized semantic-alias match diagnostic", localizedSemanticResult,
                BeliefAutomaticCoverageOutcomeTokens.SemanticAlias,
                localizedSemanticResult.stances[0].relevanceSource,
                localizedSemanticResult.stances[0].relevanceTier);
            AssertEqual("localized aliases normalize without an English-only path",
                "космическая среда обитания",
                BeliefLexicalMatcher.Normalize(
                    "КОСМИЧЕСКАЯ СРЕДА ОБИТАНИЯ", 320, 48));

            BeliefStanceResolution lexicalResult = Resolve(
                Snapshot(Precept("LexicalOrchard_5A", "LexicalOrchardIssue_5A",
                    "carefully tended luminous orchards", 1)),
                TextEvidence(true, "carefully tended luminous orchards"),
                policy);
            AssertCoverage("guarded lexical match diagnostic", lexicalResult,
                BeliefAutomaticCoverageOutcomeTokens.GuardedLexical,
                lexicalResult.stances[0].relevanceSource,
                lexicalResult.stances[0].relevanceTier);

            BeliefPolicyBuilder belowBuilder = BeliefPolicyBuilder.CreateDefault();
            belowBuilder.minimumLexicalConfidence = 10000f;
            BeliefStanceResolution below = Resolve(
                Snapshot(Precept("Below_5A", "BelowIssue_5A", "bounded copper orchard", 1)),
                TextEvidence(true, "bounded copper orchard"),
                belowBuilder.Build());
            AssertEmpty("below-confidence diagnostic remains a rejection", below);
            AssertEqual("below-confidence outcome",
                BeliefAutomaticCoverageOutcomeTokens.BelowConfidence,
                below.automaticCoverage.outcome);
            AssertEqual("below-confidence band",
                BeliefAutomaticCoverageConfidenceBandTokens.BelowMinimum,
                below.automaticCoverage.confidenceBand);
            AssertEqual("below-confidence rejection reason",
                BeliefAutomaticCoverageReasonTokens.BelowConfidence,
                below.automaticCoverage.reason);
            BeliefPolicyBuilder gapProbeBuilder = BeliefPolicyBuilder.CreateDefault();
            gapProbeBuilder.minimumLexicalConfidence = 0f;
            gapProbeBuilder.lexicalRunnerUpMargin = 0f;
            gapProbeBuilder.commonTokenMinimumDocuments = 32;
            BeliefSnapshot gapSnapshot = Snapshot(
                Precept("GapWinner_5A", "GapIssueA_5A", "crystal mercy iron duty silver law", 1),
                Precept("GapRunner_5A", "GapIssueB_5A", "crystal mercy iron duty", 1));
            BeliefEventEvidence gapEvidence = TextEvidence(
                true, "crystal mercy iron duty silver law");
            BeliefLexicalMatchResult gapProbe = BeliefLexicalMatcher.Match(
                gapEvidence, gapSnapshot, gapSnapshot.precepts, gapProbeBuilder.Build());
            AssertTrue("runner-up fixture has two independently qualifying candidates",
                gapProbe.topCandidate != null && gapProbe.candidateCount == 2 && gapProbe.runnerUpGap > 0f);

            BeliefPolicyBuilder exactGapBuilder = BeliefPolicyBuilder.CreateDefault();
            exactGapBuilder.minimumLexicalConfidence = 0f;
            exactGapBuilder.commonTokenMinimumDocuments = 32;
            exactGapBuilder.lexicalRunnerUpMargin = gapProbe.runnerUpGap;
            BeliefStanceResolution exactGap = Resolve(
                gapSnapshot, gapEvidence, exactGapBuilder.Build());
            AssertCoverage("runner-up gap equality is accepted", exactGap,
                BeliefAutomaticCoverageOutcomeTokens.GuardedLexical,
                exactGap.stances[0].relevanceSource,
                exactGap.stances[0].relevanceTier);
            AssertNear("accepted diagnostic preserves runner-up gap",
                gapProbe.runnerUpGap, exactGap.automaticCoverage.runnerUpGap);

            BeliefPolicyBuilder overGapBuilder = BeliefPolicyBuilder.CreateDefault();
            overGapBuilder.minimumLexicalConfidence = 0f;
            overGapBuilder.commonTokenMinimumDocuments = 32;
            overGapBuilder.lexicalRunnerUpMargin = gapProbe.runnerUpGap + 0.01f;
            BeliefStanceResolution ambiguous = Resolve(
                gapSnapshot, gapEvidence, overGapBuilder.Build());
            AssertEmpty("runner-up gap just below margin is rejected", ambiguous);
            AssertEqual("ambiguous outcome",
                BeliefAutomaticCoverageOutcomeTokens.Ambiguous,
                ambiguous.automaticCoverage.outcome);
            AssertNear("ambiguous diagnostic preserves runner-up gap",
                gapProbe.runnerUpGap, ambiguous.automaticCoverage.runnerUpGap);
            AssertEqual("ambiguous rejection reason",
                BeliefAutomaticCoverageReasonTokens.RunnerUpAmbiguity,
                ambiguous.automaticCoverage.reason);

            BeliefStanceResolution noCandidate = Resolve(
                Snapshot(Precept("NoCandidate_5A", "NoCandidateIssue_5A", "silent basalt custom", 1)),
                TextEvidence(true, "unrelated copper meadow"),
                policy);
            AssertEmpty("no-candidate fixture remains unchanged", noCandidate);
            AssertEqual("no-candidate outcome", BeliefAutomaticCoverageOutcomeTokens.NoMatch,
                noCandidate.automaticCoverage.outcome);
            AssertEqual("no-candidate reason", BeliefAutomaticCoverageReasonTokens.NoCandidate,
                noCandidate.automaticCoverage.reason);

            BeliefStanceResolution noEvidence = Resolve(
                Snapshot(Precept("NoEvidence_5A", "NoEvidenceIssue_5A", "silent doctrine", 1)),
                Evidence(true),
                policy);
            AssertEqual("no-evidence outcome", BeliefAutomaticCoverageOutcomeTokens.NoMatch,
                noEvidence.automaticCoverage.outcome);
            AssertEqual("no-evidence reason", BeliefAutomaticCoverageReasonTokens.NoEvidence,
                noEvidence.automaticCoverage.reason);

            BeliefStanceResolution hidden = Resolve(
                Snapshot(Precept("Hidden_5A", "HiddenIssue_5A", "secret doctrine", 3)),
                TextEvidence(false, "secret doctrine"),
                policy);
            AssertEmpty("hidden evidence still creates no stance", hidden);
            AssertEqual("hidden evidence rejection is explicit",
                BeliefAutomaticCoverageReasonTokens.UnverifiedEvidence,
                hidden.automaticCoverage.reason);

            BeliefStanceResolution nullInput = EventRelativeStanceResolver.Resolve(null);
            AssertEmpty("null diagnostic input fails closed", nullInput);
            AssertEqual("null diagnostic input reason",
                BeliefAutomaticCoverageReasonTokens.InvalidInput,
                nullInput.automaticCoverage.reason);

            BeliefSnapshot malformedSnapshot = Snapshot(
                Precept("Malformed_5B", "MalformedIssue_5B", "malformed doctrine", 1));
            malformedSnapshot.pawnId = string.Empty;
            BeliefStanceResolution malformed = Resolve(
                malformedSnapshot, TextEvidence(true, "malformed doctrine"), policy);
            AssertEmpty("malformed snapshot identity fails closed", malformed);
            AssertEqual("malformed snapshot identity reason",
                BeliefAutomaticCoverageReasonTokens.InvalidInput,
                malformed.automaticCoverage.reason);

            BeliefEventEvidence mismatchedPovEvidence = TextEvidence(true, "mismatched doctrine");
            mismatchedPovEvidence.narrative.povPawnId = "DifferentPawn";
            BeliefStanceResolution mismatchedPov = Resolve(
                Snapshot(Precept("Mismatched_5B", "MismatchedIssue_5B",
                    "mismatched doctrine", 1)),
                mismatchedPovEvidence,
                policy);
            AssertEmpty("mismatched POV evidence fails closed", mismatchedPov);
            AssertEqual("mismatched POV rejection reason",
                BeliefAutomaticCoverageReasonTokens.MismatchedPov,
                mismatchedPov.automaticCoverage.reason);

            BeliefEventEvidence futureEvidence = TextEvidence(true, "future doctrine");
            futureEvidence.narrative.tick = 12346;
            BeliefStanceResolution future = Resolve(
                Snapshot(Precept("Future_5B", "FutureIssue_5B", "future doctrine", 1)),
                futureEvidence,
                policy);
            AssertEmpty("future evidence fails closed", future);
            AssertEqual("future evidence rejection reason",
                BeliefAutomaticCoverageReasonTokens.FutureEvidence,
                future.automaticCoverage.reason);

            BeliefSnapshot unavailableSnapshot = Snapshot(
                Precept("Unavailable_5B", "UnavailableIssue_5B", "unavailable doctrine", 1));
            unavailableSnapshot.ideologyActive = false;
            BeliefStanceResolution unavailable = Resolve(
                unavailableSnapshot, TextEvidence(true, "unavailable doctrine"), policy);
            AssertEmpty("DLC-unavailable snapshot fails closed", unavailable);
            AssertEqual("DLC-unavailable rejection reason",
                BeliefAutomaticCoverageReasonTokens.UnavailableSnapshot,
                unavailable.automaticCoverage.reason);

            AssertEqual("Phase 5A still ships no exact correction",
                0, policy.correlationOverrides.Count);
            List<BeliefAutomaticCoverageDiagnostic> sequence =
                new List<BeliefAutomaticCoverageDiagnostic>
                {
                    exactSourceResult.automaticCoverage,
                    authorityResult.automaticCoverage,
                    semanticResult.automaticCoverage,
                    lexicalResult.automaticCoverage,
                    below.automaticCoverage,
                    ambiguous.automaticCoverage,
                    noCandidate.automaticCoverage,
                    noEvidence.automaticCoverage,
                    mismatchedPov.automaticCoverage,
                    future.automaticCoverage,
                    unavailable.automaticCoverage
                };
            BeliefAutomaticCoverageAggregate firstAggregate =
                new BeliefAutomaticCoverageAggregate();
            BeliefAutomaticCoverageAggregate secondAggregate =
                new BeliefAutomaticCoverageAggregate();
            for (int i = 0; i < sequence.Count; i++)
            {
                BeliefAutomaticCoverageDiagnostics.Add(firstAggregate, sequence[i], 64);
                BeliefAutomaticCoverageDiagnostics.Add(secondAggregate, sequence[i], 64);
            }
            AssertEqual("aggregate exact count", 1,
                firstAggregate.OutcomeCount(BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation));
            AssertEqual("aggregate structural count", 1,
                firstAggregate.OutcomeCount(BeliefAutomaticCoverageOutcomeTokens.StructuralCorrelation));
            AssertEqual("aggregate semantic count", 1,
                firstAggregate.OutcomeCount(BeliefAutomaticCoverageOutcomeTokens.SemanticAlias));
            AssertEqual("aggregate lexical count", 1,
                firstAggregate.OutcomeCount(BeliefAutomaticCoverageOutcomeTokens.GuardedLexical));
            AssertEqual("aggregate below-confidence count", 1,
                firstAggregate.OutcomeCount(BeliefAutomaticCoverageOutcomeTokens.BelowConfidence));
            AssertEqual("aggregate ambiguous count", 1,
                firstAggregate.OutcomeCount(BeliefAutomaticCoverageOutcomeTokens.Ambiguous));
            AssertEqual("aggregate no-match count", 5,
                firstAggregate.OutcomeCount(BeliefAutomaticCoverageOutcomeTokens.NoMatch));
            AssertTrue("aggregate exposes winner-tier counts",
                firstAggregate.WinnerTierCount(BeliefRelevanceTierTokens.SourcePrecept) == 1
                && firstAggregate.WinnerTierCount(BeliefRelevanceTierTokens.DirectIdentity) == 1);
            AssertTrue("aggregate exposes confidence-band counts",
                firstAggregate.ConfidenceBandCount(
                    BeliefAutomaticCoverageConfidenceBandTokens.Structural) == 2
                && firstAggregate.runnerUpGapCount >= 1);
            AssertTrue("aggregate exposes fixed rejection-reason counts",
                firstAggregate.RejectionReasonCount(
                    BeliefAutomaticCoverageReasonTokens.BelowConfidence) == 1
                && firstAggregate.RejectionReasonCount(
                    BeliefAutomaticCoverageReasonTokens.RunnerUpAmbiguity) == 1
                && firstAggregate.RejectionReasonCount(
                    BeliefAutomaticCoverageReasonTokens.NoCandidate) == 1
                && firstAggregate.RejectionReasonCount(
                    BeliefAutomaticCoverageReasonTokens.NoEvidence) == 1
                && firstAggregate.RejectionReasonCount(
                    BeliefAutomaticCoverageReasonTokens.MismatchedPov) == 1
                && firstAggregate.RejectionReasonCount(
                    BeliefAutomaticCoverageReasonTokens.FutureEvidence) == 1
                && firstAggregate.RejectionReasonCount(
                    BeliefAutomaticCoverageReasonTokens.UnavailableSnapshot) == 1);
            AssertEqual("identical diagnostic input formats deterministically",
                BeliefAutomaticCoverageDiagnostics.Format(firstAggregate),
                BeliefAutomaticCoverageDiagnostics.Format(secondAggregate));

            BeliefAutomaticCoverageAggregate bounded = new BeliefAutomaticCoverageAggregate();
            BeliefAutomaticCoverageDiagnostics.Add(bounded, exactSourceResult.automaticCoverage, 2);
            BeliefAutomaticCoverageDiagnostics.Add(bounded, authorityResult.automaticCoverage, 2);
            BeliefAutomaticCoverageDiagnostics.Add(bounded, lexicalResult.automaticCoverage, 2);
            AssertTrue("automatic diagnostic aggregation is bounded",
                bounded.observedCount == 2 && bounded.droppedCount == 1);
            BeliefAutomaticCoverageDiagnostics.Add(bounded, below.automaticCoverage, 2);
            BeliefAutomaticCoverageDiagnostics.Add(bounded, ambiguous.automaticCoverage, 2);
            BeliefAutomaticCoverageDiagnostics.Add(bounded, noCandidate.automaticCoverage, 2);
            AssertEqual("dropped diagnostic count remains accurate beyond the sample cap",
                4, bounded.droppedCount);

            BeliefAutomaticCoverageAggregate hostile = new BeliefAutomaticCoverageAggregate();
            BeliefAutomaticCoverageDiagnostics.Add(hostile, new BeliefAutomaticCoverageDiagnostic
            {
                outcome = "Player's Secret Doctrine",
                reason = "custom ideology description",
                winnerSource = "authored source label",
                winnerTier = "prompt text",
                confidenceBand = "private name",
                confidence = float.NaN,
                runnerUpGap = float.PositiveInfinity,
                candidateCount = int.MaxValue
            }, 8);
            string hostileFormatted = BeliefAutomaticCoverageDiagnostics.Format(hostile);
            AssertTrue("diagnostic formatter never logs authored/free-form tokens",
                hostileFormatted.IndexOf("Secret", StringComparison.Ordinal) < 0
                && hostileFormatted.IndexOf("custom ideology", StringComparison.Ordinal) < 0
                && hostileFormatted.IndexOf("authored source", StringComparison.Ordinal) < 0
                && hostileFormatted.IndexOf("prompt text", StringComparison.Ordinal) < 0
                && hostileFormatted.IndexOf("private name", StringComparison.Ordinal) < 0);
            AssertContains("diagnostic formatter normalizes unknown outcomes",
                hostileFormatted, "last_outcome=no_match");
            AssertContains("diagnostic formatter exposes only fixed rejection buckets",
                BeliefAutomaticCoverageDiagnostics.Format(firstAggregate),
                "rejection_reasons=invalid_input:0,unavailable_snapshot:1,"
                + "unverified_evidence:0,mismatched_pov:1,future_evidence:1");
        }

        private static void AssertAutomaticMetadataFixture(
            string label,
            string correlationKind,
            string correlationDefName,
            string sourceDomain,
            string sourceDefName)
        {
            string stableSuffix = correlationDefName.Replace("_", string.Empty);
            BeliefPreceptFact correlated = Precept(
                "ArbitraryPrecept_" + stableSuffix,
                "ArbitraryIssue_" + stableSuffix,
                "metadata-poor-looking but structurally linked stance",
                0);
            correlated.correlations.Add(Correlation(
                correlationKind, correlationDefName, BeliefValenceTokens.Neutral));
            BeliefPreceptFact lexicalBait = Precept(
                "UnrelatedHighImpact_" + stableSuffix,
                "UnrelatedIssue_" + stableSuffix,
                "luminous authority ceremony",
                3);
            BeliefEventEvidence evidence = Evidence(true);
            evidence.narrative.sourceDomain = sourceDomain;
            evidence.narrative.sourceDefName = sourceDefName;
            evidence.matchFields.Add(Text("event_label", lexicalBait.displayLabel));
            if (correlationKind == BeliefCorrelationKindTokens.Thought)
                evidence.thoughtDefNames.Add(correlationDefName);
            else
                evidence.historyEventDefNames.Add(correlationDefName);

            BeliefStanceResolution resolved = Resolve(
                Snapshot(correlated, lexicalBait),
                evidence,
                BeliefPolicySnapshot.CreateDefault());
            AssertSelected(label + " fixture selects arbitrary-ID metadata", resolved, correlated.defName);
            AssertCoverage(label + " fixture diagnostic", resolved,
                BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation,
                correlationKind == BeliefCorrelationKindTokens.Thought
                    ? BeliefRelevanceSourceTokens.ThoughtCorrelation
                    : BeliefRelevanceSourceTokens.HistoryCorrelation,
                BeliefRelevanceTierTokens.ExactCorrelation);
            AssertEqual(label + " fixture requires no exact correction", 0,
                BeliefPolicySnapshot.CreateDefault().correlationOverrides.Count);
        }

        private static void AssertCrossDlcOrdinaryMetadataFixture(
            string label,
            string preceptDefName,
            string issueDefName,
            string correlationKind,
            string correlationDefName)
        {
            BeliefPreceptFact correlated = Precept(
                preceptDefName, issueDefName, "ordinary loaded doctrine metadata", 0);
            correlated.correlations.Add(Correlation(
                correlationKind, correlationDefName, BeliefValenceTokens.Neutral));
            BeliefPreceptFact lexicalBait = Precept(
                label.Replace(" ", string.Empty) + "_LexicalBait_5B",
                label.Replace(" ", string.Empty) + "_BaitIssue_5B",
                "luminous authority ceremony", 3);
            BeliefEventEvidence evidence = Evidence(true);
            evidence.narrative.sourceDomain = "ordinary_dlc";
            evidence.narrative.sourceDefName = label.Replace(" ", string.Empty) + "_Event_5B";
            evidence.matchFields.Add(Text("event_label", lexicalBait.displayLabel));
            if (correlationKind == BeliefCorrelationKindTokens.Thought)
                evidence.thoughtDefNames.Add(correlationDefName);
            else
                evidence.historyEventDefNames.Add(correlationDefName);

            BeliefStanceResolution resolved = Resolve(
                Snapshot(correlated, lexicalBait),
                evidence,
                BeliefPolicySnapshot.CreateDefault());
            AssertSelected(label + " ordinary metadata beats lexical bait",
                resolved, correlated.defName);
            AssertCoverage(label + " ordinary metadata diagnostic", resolved,
                BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation,
                correlationKind == BeliefCorrelationKindTokens.Thought
                    ? BeliefRelevanceSourceTokens.ThoughtCorrelation
                    : BeliefRelevanceSourceTokens.HistoryCorrelation,
                BeliefRelevanceTierTokens.ExactCorrelation);
            AssertEqual(label + " ordinary metadata keeps its issue identity",
                issueDefName, resolved.stances[0].precept.issue.defName);
            AssertEqual(label + " ordinary metadata requires no exact correction", 0,
                BeliefPolicySnapshot.CreateDefault().correlationOverrides.Count);
        }

        private static void TestPhase5CProjectionRestraintAndEventRepetition()
        {
            BeliefPolicySnapshot policy = BeliefPolicySnapshot.CreateDefault();
            BeliefPreceptFact target = Precept(
                "Phase5C_Correlated", "Phase5C_Issue",
                "event-bound worldview", 1);
            target.description =
                "Only the detached exact correlation makes this doctrine relevant.";
            target.correlations.Add(Correlation(
                BeliefCorrelationKindTokens.Thought,
                "Phase5C_Thought",
                BeliefValenceTokens.Neutral));
            target.associatedMemeDefNames.Add("Phase5C_Meme");
            BeliefPreceptFact unrelated = Precept(
                "Phase5C_Unrelated", "Phase5C_UnrelatedIssue",
                "unrelated distant doctrine", 3);
            BeliefSnapshot snapshot = Snapshot(target, unrelated);
            snapshot.memes.Add(new BeliefMemeFact
            {
                defName = "Phase5C_Meme",
                label = "Phase 5C supporting meme",
                description = "A bounded supporting fact."
            });
            snapshot.structure = new BeliefMemeFact
            {
                defName = "Phase5C_Structure",
                label = "Phase 5C structure",
                description = "A bounded structure fact.",
                isStructure = true
            };
            snapshot.deities.Add(new BeliefDeityFact
            {
                name = "Phase 5C deity",
                relatedMemeDefName = "Phase5C_Meme"
            });

            BeliefEventEvidence evidence = Evidence(true);
            evidence.thoughtDefNames.Add("Phase5C_Thought");
            evidence.projection = new BeliefContextProjection
            {
                maximumSelectedStances = 1,
                maximumSupportingMemes = 1,
                maximumContextCharacters = 520,
                includeRole = true,
                includeCertainty = true,
                includeStructure = true,
                includeDeity = true,
                includeNarrativeInterpretation = true
            };
            BeliefStanceResolution resolved = Resolve(
                snapshot, evidence, policy, seed: 31);
            AssertSelected("Phase 5C exact correlation selects only the supported doctrine",
                resolved, target.defName);
            AssertCoverage("Phase 5C exact correlation diagnostic", resolved,
                BeliefAutomaticCoverageOutcomeTokens.ExactCorrelation,
                BeliefRelevanceSourceTokens.ThoughtCorrelation,
                BeliefRelevanceTierTokens.ExactCorrelation);

            string full = BeliefContextFormatter.Format(
                resolved, NarrativeDetailLevelTokens.Full, policy);
            string balanced = BeliefContextFormatter.Format(
                resolved, NarrativeDetailLevelTokens.Balanced, policy);
            string compact = BeliefContextFormatter.Format(
                resolved, NarrativeDetailLevelTokens.Compact, policy);
            AssertContains("Phase 5C Full keeps supported structure", full,
                "structure: Phase 5C structure");
            AssertContains("Phase 5C Full keeps a related deity", full,
                "deity: Phase 5C deity");
            AssertContains("Phase 5C Balanced keeps supported structure", balanced,
                "structure: Phase 5C structure");
            AssertTrue("Phase 5C Compact omits structure under the shipped budget",
                !compact.Contains("structure:"));
            AssertContains("Phase 5C Compact may retain the mechanically related deity",
                compact, "deity: Phase 5C deity");
            AssertTrue("Phase 5C bounded blocks exclude unrelated doctrine",
                full.IndexOf(unrelated.displayLabel, StringComparison.Ordinal) < 0
                && balanced.IndexOf(unrelated.displayLabel, StringComparison.Ordinal) < 0
                && compact.IndexOf(unrelated.displayLabel, StringComparison.Ordinal) < 0);
            AssertTrue("Phase 5C event projection enforces its context cap",
                full.Length <= evidence.projection.maximumContextCharacters);

            BeliefEventEvidence suppressed = Evidence(true);
            suppressed.thoughtDefNames.Add("Phase5C_Thought");
            suppressed.projection = new BeliefContextProjection
            {
                maximumSelectedStances = 1,
                maximumSupportingMemes = 1,
                maximumContextCharacters = 520,
                includeRole = true,
                includeCertainty = true,
                includeStructure = false,
                includeDeity = false,
                includeNarrativeInterpretation = true
            };
            string suppressedText = BeliefContextFormatter.Format(
                Resolve(snapshot, suppressed, policy, seed: 31),
                NarrativeDetailLevelTokens.Full,
                policy);
            AssertTrue("Phase 5C source projection suppresses structure and deity together",
                !suppressedText.Contains("structure:")
                && !suppressedText.Contains("deity:"));

            BeliefPreceptFact repeatFirst = Precept(
                "Phase5C_RepeatFirst", "Phase5C_RepeatIssueFirst",
                "first exact repeated stance", 1);
            BeliefPreceptFact repeatSecond = Precept(
                "Phase5C_RepeatSecond", "Phase5C_RepeatIssueSecond",
                "second exact repeated stance", 1);
            repeatFirst.correlations.Add(Correlation(
                BeliefCorrelationKindTokens.Thought,
                "Phase5C_SharedRepeatThought",
                BeliefValenceTokens.Neutral));
            repeatSecond.correlations.Add(Correlation(
                BeliefCorrelationKindTokens.Thought,
                "Phase5C_SharedRepeatThought",
                BeliefValenceTokens.Neutral));
            BeliefSnapshot repetitionSnapshot = Snapshot(
                repeatFirst, repeatSecond);
            BeliefEventEvidence repetitionEvidence = Evidence(true);
            repetitionEvidence.thoughtDefNames.Add(
                "Phase5C_SharedRepeatThought");
            repetitionEvidence.projection = new BeliefContextProjection
            {
                maximumSelectedStances = 1,
                maximumSupportingMemes = 0,
                maximumContextCharacters = 240,
                includeStructure = false,
                includeDeity = false
            };
            bool changed = false;
            for (int seed = 1; seed <= 256 && !changed; seed++)
            {
                BeliefStanceResolution initial = Resolve(
                    repetitionSnapshot, repetitionEvidence, policy,
                    seed: seed);
                BeliefStanceResolution later = Resolve(
                    repetitionSnapshot,
                    repetitionEvidence,
                    policy,
                    seed: seed,
                    recent: new List<string>
                    {
                        initial.stances[0].precept.defName
                    });
                changed = initial.stances[0].precept.defName
                    != later.stances[0].precept.defName;
            }
            AssertTrue("Phase 5C shipped recent-selection penalty changes a later event choice",
                changed);
            AssertEqual("Phase 5C exceptional-correction audit remains empty", 0,
                policy.correlationOverrides.Count);
        }

        private static void AssertCoverage(
            string name,
            BeliefStanceResolution actual,
            string outcome,
            string winnerSource,
            string winnerTier)
        {
            AssertTrue(name + " exists", actual?.automaticCoverage != null);
            AssertEqual(name + " outcome", outcome, actual.automaticCoverage.outcome);
            AssertEqual(name + " winner source", winnerSource, actual.automaticCoverage.winnerSource);
            AssertEqual(name + " winner tier", winnerTier, actual.automaticCoverage.winnerTier);
        }

        private static void TestPhase2MutationCoalescingCorrelationAndOwnership()
        {
            BeliefMutationState start = new BeliefMutationState
            {
                pawnId = "PawnA", capturedTick = 100, ideologyId = "IdeoA",
                ideologyName = "First", hasCertainty = true, certainty = 0.8f
            };
            BeliefMutationState nestedAfter = new BeliefMutationState
            {
                pawnId = "PawnA", capturedTick = 100, ideologyId = "IdeoA",
                ideologyName = "First", hasCertainty = true, certainty = 0.7f
            };
            BeliefMutationState final = new BeliefMutationState
            {
                pawnId = "PawnA", capturedTick = 100, ideologyId = "IdeoB",
                ideologyName = "Second", hasCertainty = true, certainty = 0.5f
            };
            BeliefMutationState attempted = new BeliefMutationState
            {
                ideologyId = "IdeoB", ideologyName = "Second"
            };
            BeliefMutationSnapshot nested = BeliefMutationPolicy.Create(
                start, nestedAfter, null, BeliefMutationCauseTokens.CertaintyOffset,
                null, 2, 3);
            BeliefMutationSnapshot outer = BeliefMutationPolicy.Create(
                start, final, attempted, BeliefMutationCauseTokens.ConversionAttempt,
                true, 1, 4);
            AssertTrue("certainty mutation produces a detached fact", nested != null);
            AssertTrue("conversion mutation produces a detached fact", outer != null);

            BeliefMutationBuffer buffer = new BeliefMutationBuffer();
            buffer.RecordOrMerge(nested, 100, 8, 10);
            buffer.RecordOrMerge(outer, 100, 8, 10);
            AssertEqual("nested mutation calls coalesce to one row", 1, buffer.Count);
            BeliefMutationSnapshot merged = buffer.PeekLatest("PawnA", 101, 10);
            AssertTrue("coalesced mutation remains correlatable", merged != null);
            AssertEqual("coalescing preserves earliest ideology", "IdeoA", merged.beforeIdeologyId);
            AssertNear("coalescing preserves earliest certainty", 0.8f, merged.beforeCertainty);
            AssertEqual("coalescing preserves latest ideology", "IdeoB", merged.afterIdeologyId);
            AssertNear("coalescing preserves latest certainty", 0.5f, merged.afterCertainty);
            AssertEqual("coalescing preserves attempted ideology", "IdeoB", merged.attemptedIdeologyId);
            AssertEqual("coalescing preserves conversion result", true, merged.conversionSucceeded.Value);
            AssertTrue("coalescing unions nested certainty cause",
                Contains(merged.causeTokens, BeliefMutationCauseTokens.CertaintyOffset));
            AssertTrue("coalescing unions outer conversion cause",
                Contains(merged.causeTokens, BeliefMutationCauseTokens.ConversionAttempt));
            buffer.PeekLatest("PawnA", 101, 10);
            AssertEqual("correlation reads are non-consuming", 1, buffer.Count);

            BeliefMutationState later = new BeliefMutationState
            {
                pawnId = "PawnA", capturedTick = 100, ideologyId = "IdeoB",
                ideologyName = "Second", hasCertainty = true, certainty = 0.4f
            };
            buffer.RecordOrMerge(BeliefMutationPolicy.Create(
                final, later, null, BeliefMutationCauseTokens.CertaintyOffset,
                null, 5, 6), 100, 8, 10);
            AssertEqual("sequential same-tick actions do not coalesce", 2, buffer.Count);
            AssertNear("latest sequential action wins correlation", 0.4f,
                buffer.PeekLatest("PawnA", 100, 10).afterCertainty);
            AssertTrue("different pawn cannot see mutation", buffer.PeekLatest("PawnB", 100, 10) == null);
            AssertTrue("stale mutation expires outside window", buffer.PeekLatest("PawnA", 111, 10) == null);
            AssertEqual("stale mutation pruning clears rows", 0, buffer.Count);

            BeliefMutationBuffer writePruned = new BeliefMutationBuffer();
            writePruned.RecordOrMerge(nested, 100, 8, 10);
            writePruned.RecordOrMerge(null, 111, 8, 10);
            AssertEqual("write-side maintenance prunes stale rows without a read", 0,
                writePruned.Count);

            BeliefMutationBuffer regressedClock = new BeliefMutationBuffer();
            BeliefMutationState futureBefore = new BeliefMutationState
            {
                pawnId = "FuturePawn", capturedTick = 200, ideologyId = "IdeoA",
                hasCertainty = true, certainty = 0.8f
            };
            BeliefMutationState futureAfter = new BeliefMutationState
            {
                pawnId = "FuturePawn", capturedTick = 200, ideologyId = "IdeoA",
                hasCertainty = true, certainty = 0.7f
            };
            regressedClock.RecordOrMerge(BeliefMutationPolicy.Create(
                futureBefore, futureAfter, null, BeliefMutationCauseTokens.CertaintyOffset,
                null, 20, 21), 200, 8, 10);
            regressedClock.RecordOrMerge(null, 100, 8, 10);
            AssertEqual("clock regression prunes rows stranded beyond the future window", 0,
                regressedClock.Count);

            BeliefMutationBuffer nearFuture = new BeliefMutationBuffer();
            futureBefore.capturedTick = 105;
            futureAfter.capturedTick = 105;
            nearFuture.RecordOrMerge(BeliefMutationPolicy.Create(
                futureBefore, futureAfter, null, BeliefMutationCauseTokens.CertaintyOffset,
                null, 22, 23), 105, 8, 10);
            nearFuture.RecordOrMerge(null, 100, 8, 10);
            AssertEqual("near-future rows remain inside the symmetric correlation window", 1,
                nearFuture.Count);

            BeliefMutationState secondNestedAfter = new BeliefMutationState
            {
                pawnId = "PawnA", capturedTick = 100, ideologyId = "IdeoB",
                ideologyName = "Second", hasCertainty = true, certainty = 0.5f
            };
            BeliefMutationSnapshot secondSibling = BeliefMutationPolicy.Create(
                nestedAfter, secondNestedAfter, null, BeliefMutationCauseTokens.SetIdeology,
                null, 4, 5);
            BeliefMutationSnapshot spanningOuter = BeliefMutationPolicy.Create(
                start, secondNestedAfter, attempted, BeliefMutationCauseTokens.ConversionAttempt,
                true, 1, 6);
            BeliefMutationBuffer siblings = new BeliefMutationBuffer();
            siblings.RecordOrMerge(nested, 100, 8, 10);
            siblings.RecordOrMerge(secondSibling, 100, 8, 10);
            AssertEqual("completed sibling mutations initially remain separate", 2, siblings.Count);
            siblings.RecordOrMerge(spanningOuter, 100, 8, 10);
            BeliefMutationSnapshot siblingsMerged = siblings.PeekLatest("PawnA", 100, 10);
            AssertEqual("one outer interval absorbs every overlapping sibling", 1, siblings.Count);
            AssertEqual("multi-overlap keeps the outer attempted ideology", "IdeoB",
                siblingsMerged.attemptedIdeologyId);
            AssertEqual("multi-overlap keeps the outer conversion result", true,
                siblingsMerged.conversionSucceeded.Value);
            AssertTrue("multi-overlap unions the first sibling cause",
                Contains(siblingsMerged.causeTokens, BeliefMutationCauseTokens.CertaintyOffset));
            AssertTrue("multi-overlap unions the second sibling cause",
                Contains(siblingsMerged.causeTokens, BeliefMutationCauseTokens.SetIdeology));

            BeliefMutationSnapshot reversingSibling = BeliefMutationPolicy.Create(
                nestedAfter, start, null, BeliefMutationCauseTokens.CertaintyOffset,
                null, 4, 5);
            BeliefMutationSnapshot cancellingOuter = BeliefMutationPolicy.Create(
                start, start, null, BeliefMutationCauseTokens.SetIdeology, null, 1, 6);
            BeliefMutationBuffer siblingCancellation = new BeliefMutationBuffer();
            siblingCancellation.RecordOrMerge(nested, 100, 8, 10);
            siblingCancellation.RecordOrMerge(reversingSibling, 100, 8, 10);
            siblingCancellation.RecordOrMerge(cancellingOuter, 100, 8, 10);
            AssertEqual("no-op outer boundary removes every transient sibling row", 0,
                siblingCancellation.Count);

            BeliefMutationBuffer cancelled = new BeliefMutationBuffer();
            cancelled.RecordOrMerge(nested, 100, 8, 10);
            BeliefMutationSnapshot noNetOuter = BeliefMutationPolicy.Create(
                start, start, null, BeliefMutationCauseTokens.SetIdeology, null, 1, 4);
            cancelled.RecordOrMerge(noNetOuter, 100, 8, 10);
            AssertEqual("no-op outer boundary cancels transient nested change", 0, cancelled.Count);

            BeliefMutationSnapshot failedAttempt = BeliefMutationPolicy.Create(
                start, start, attempted, BeliefMutationCauseTokens.ConversionAttempt, false, 7, 8);
            cancelled.RecordOrMerge(failedAttempt, 100, 8, 10);
            AssertEqual("failed conversion result remains a useful mechanical fact", 1, cancelled.Count);

            BeliefMutationBuffer capped = new BeliefMutationBuffer();
            for (int i = 0; i < 3; i++)
            {
                BeliefMutationState capBefore = new BeliefMutationState
                {
                    pawnId = "CapPawn" + i, capturedTick = 200 + i, ideologyId = "CapIdeo",
                    hasCertainty = true, certainty = 0.8f
                };
                BeliefMutationState capAfter = new BeliefMutationState
                {
                    pawnId = capBefore.pawnId, capturedTick = capBefore.capturedTick,
                    ideologyId = "CapIdeo", hasCertainty = true, certainty = 0.7f
                };
                capped.RecordOrMerge(BeliefMutationPolicy.Create(
                    capBefore, capAfter, null, BeliefMutationCauseTokens.CertaintyOffset,
                    null, 10 + i * 2, 11 + i * 2), 200 + i, 2, 10);
            }
            AssertEqual("mutation buffer enforces XML-sized entry cap", 2, capped.Count);
            AssertTrue("mutation cap removes the oldest row",
                capped.PeekLatest("CapPawn0", 202, 10) == null);
            AssertTrue("mutation cap retains the newest row",
                capped.PeekLatest("CapPawn2", 202, 10) != null);

            BeliefPolicyBuilder policy = BeliefPolicyBuilder.CreateDefault();
            policy.canonicalEventOwnershipRules.Add(new BeliefCanonicalEventOwnershipRule(
                BeliefCanonicalEventSourceTokens.Ability, "Convert", "conversion"));
            policy.canonicalEventOwnershipRules.Add(new BeliefCanonicalEventOwnershipRule(
                BeliefCanonicalEventSourceTokens.Thought,
                "FailedConvertAbilityRecipient", "conversion"));
            policy.canonicalEventOwnershipRules.Add(new BeliefCanonicalEventOwnershipRule(
                BeliefCanonicalEventSourceTokens.Ability, "Counsel", "counsel"));
            policy.canonicalEventOwnershipRules.Add(new BeliefCanonicalEventOwnershipRule(
                BeliefCanonicalEventSourceTokens.Thought, "Counselled", "counsel"));
            policy.canonicalEventOwnershipRules.Add(new BeliefCanonicalEventOwnershipRule(
                BeliefCanonicalEventSourceTokens.Thought, "Counselled_MoodBoost", "counsel"));
            policy.canonicalEventOwnershipRules.Add(new BeliefCanonicalEventOwnershipRule(
                BeliefCanonicalEventSourceTokens.Thought, "CounselFailed", "counsel"));
            BeliefPolicySnapshot ownershipSnapshot = policy.Build();
            IReadOnlyList<BeliefCanonicalEventOwnershipRule> ownership =
                ownershipSnapshot.canonicalEventOwnershipRules;
            policy.canonicalEventOwnershipRules.Clear();
            AssertEqual("ownership snapshot deep-copies XML-style rows", 6, ownership.Count);
            AssertEqual("exact active ability resolves its downstream group", "conversion",
                BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Ability, "Convert", true, true, ownership));
            AssertEqual("Def-name ownership matching is ordinal and case-sensitive", string.Empty,
                BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Ability, "convert", true, true, ownership));
            AssertEqual("substring resemblance never suppresses a modded ability", string.Empty,
                BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Ability,
                    "ConvertNearbySettlement", true, true, ownership));
            AssertEqual("source domains cannot borrow another route's ownership", string.Empty,
                BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Thought, "Convert", true, true, ownership));
            AssertEqual("failed-conversion thought resolves the conversion owner", "conversion",
                BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Thought,
                    "FailedConvertAbilityRecipient", true, true, ownership));
            AssertEqual("exact Counsel ability resolves its downstream owner", "counsel",
                BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Ability, "Counsel", true, true, ownership));
            AssertEqual("exact Counsel success memory resolves its downstream owner", "counsel",
                BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Thought, "Counselled", true, true, ownership));
            AssertEqual("exact Counsel mood-boost memory resolves its downstream owner", "counsel",
                BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Thought,
                    "Counselled_MoodBoost", true, true, ownership));
            AssertEqual("exact Counsel failure memory resolves its downstream owner", "counsel",
                BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Thought, "CounselFailed", true, true, ownership));
            AssertEqual("case-variant Counsel ability remains unsuppressed", string.Empty,
                BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Ability, "counsel", true, true, ownership));
            AssertEqual("modded Counsel-like thought remains unsuppressed", string.Empty,
                BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Thought,
                    "CounselledByMod", true, true, ownership));
            AssertEqual("inactive Ideology never suppresses the generic route", string.Empty,
                BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Ability, "Convert", false, true, ownership));
            AssertEqual("disabled belief policy never suppresses the generic route", string.Empty,
                BeliefCanonicalEventOwnershipPolicy.DownstreamGroupFor(
                    BeliefCanonicalEventSourceTokens.Ability, "Convert", true, false, ownership));
        }

        private static void TestPhase2MutationEvidenceSelection()
        {
            BeliefMutationEventRule successRule = MutationRule(
                "Convert_Success", "conversion", BeliefMutationCauseTokens.ConversionAttempt,
                BeliefMutationConversionResultTokens.Success,
                BeliefMutationCertaintyDirectionTokens.Any,
                BeliefMutationIdeologyChangeTokens.Changed,
                requireAttemptedIdeology: true);
            BeliefMutationEventRule failureRule = MutationRule(
                "Convert_Failure", "conversion", BeliefMutationCauseTokens.ConversionAttempt,
                BeliefMutationConversionResultTokens.Failure,
                BeliefMutationCertaintyDirectionTokens.Decrease,
                BeliefMutationIdeologyChangeTokens.Unchanged,
                requireAttemptedIdeology: true);
            BeliefMutationEventRule reassuranceRule = MutationRule(
                "Reassure", "heartfelt", BeliefMutationCauseTokens.CertaintyOffset,
                BeliefMutationConversionResultTokens.None,
                BeliefMutationCertaintyDirectionTokens.Increase,
                BeliefMutationIdeologyChangeTokens.Unchanged,
                requireAttemptedIdeology: false);
            BeliefMutationEventRule knownResultRule = MutationRule(
                "ConvertIdeoAttempt", "conversion", BeliefMutationCauseTokens.ConversionAttempt,
                BeliefMutationConversionResultTokens.Known,
                BeliefMutationCertaintyDirectionTokens.Any,
                BeliefMutationIdeologyChangeTokens.Any,
                requireAttemptedIdeology: true);
            BeliefMutationEventRule crisisRule = new BeliefMutationEventRule(
                BeliefMutationEventSourceTokens.MentalState,
                "IdeoChange",
                "beliefCrisis",
                BeliefMutationSubjectRoleTokens.Initiator,
                "crisis",
                BeliefMutationCauseTokens.ConversionAttempt,
                BeliefMutationConversionResultTokens.Known,
                BeliefMutationCertaintyDirectionTokens.Any,
                BeliefMutationIdeologyChangeTokens.Any,
                requireAttemptedIdeology: true);
            List<BeliefMutationEventRule> rules = new List<BeliefMutationEventRule>
            {
                successRule, failureRule, reassuranceRule, knownResultRule, crisisRule
            };

            BeliefMutationEventRule exact = BeliefMutationEventSelector.RuleFor(
                BeliefMutationEventSourceTokens.Interaction, "Convert_Success", "conversion",
                true, true, rules);
            AssertTrue("exact enabled interaction rule resolves", ReferenceEquals(successRule, exact));
            AssertTrue("inactive Ideology exposes no mutation consumer",
                BeliefMutationEventSelector.RuleFor(
                    BeliefMutationEventSourceTokens.Interaction, "Convert_Success", "conversion",
                    false, true, rules) == null);
            AssertTrue("disabled belief policy exposes no mutation consumer",
                BeliefMutationEventSelector.RuleFor(
                    BeliefMutationEventSourceTokens.Interaction, "Convert_Success", "conversion",
                    true, false, rules) == null);
            AssertTrue("exact downstream group is required",
                BeliefMutationEventSelector.RuleFor(
                    BeliefMutationEventSourceTokens.Interaction, "Convert_Success", "heartfelt",
                    true, true, rules) == null);
            AssertTrue("Def-name mapping is ordinal and case-sensitive",
                BeliefMutationEventSelector.RuleFor(
                    BeliefMutationEventSourceTokens.Interaction, "convert_success", "conversion",
                    true, true, rules) == null);
            List<BeliefMutationEventRule> duplicateRules = new List<BeliefMutationEventRule>(rules)
            {
                MutationRule("Convert_Success", "conversion",
                    BeliefMutationCauseTokens.ConversionAttempt,
                    BeliefMutationConversionResultTokens.Success,
                    BeliefMutationCertaintyDirectionTokens.Any,
                    BeliefMutationIdeologyChangeTokens.Changed, true)
            };
            AssertTrue("duplicate exact mutation mappings fail closed",
                BeliefMutationEventSelector.RuleFor(
                    BeliefMutationEventSourceTokens.Interaction, "Convert_Success", "conversion",
                    true, true, duplicateRules) == null);
            AssertEqual("recipient rule selects exact recipient identity", "TargetPawn",
                BeliefMutationEventSelector.SubjectPawnId(successRule, "ConverterPawn", "TargetPawn"));
            BeliefMutationEventRule exactCrisis = BeliefMutationEventSelector.RuleFor(
                BeliefMutationEventSourceTokens.MentalState, "IdeoChange", "beliefCrisis",
                true, true, rules);
            AssertTrue("exact IdeoChange rule wins for the crisis group",
                ReferenceEquals(crisisRule, exactCrisis));
            AssertEqual("crisis rule selects the breaking pawn", "BreakingPawn",
                BeliefMutationEventSelector.SubjectPawnId(
                    crisisRule, "BreakingPawn", "UnrelatedPawn"));
            AssertTrue("generic Berserk keeps the ordinary mental-state path",
                BeliefMutationEventSelector.RuleFor(
                    BeliefMutationEventSourceTokens.MentalState, "Berserk",
                    "mentalbreakViolent", true, true, rules) == null);

            BeliefMutationSnapshot success = Mutation(
                "TargetPawn", 100, "OldIdeo", "NewIdeo", 0f, 0.5f,
                BeliefMutationCauseTokens.ConversionAttempt, true, ideologyChanged: true);
            success.attemptedIdeologyId = "NewIdeo";
            success.attemptedIdeologyName = "New faith";
            BeliefEventEvidence selected = BeliefMutationEventSelector.Select(
                successRule, "TargetPawn", 100, 10, success, "Target");
            AssertTrue("matching conversion mutation becomes evidence",
                selected != null && ReferenceEquals(success, selected.mutation));
            AssertEqual("selected evidence keeps exact event source", "Convert_Success",
                selected.narrative.sourceDefName);
            AssertEqual("selected evidence keeps XML group", "conversion", selected.groupKey);
            AssertEqual("selected evidence keeps the visible mutation subject", "Target",
                selected.narrative.subjectLabel);
            AssertContains("conversion evidence appends the critical prompt marker",
                BeliefMutationEventSelector.AppendGameContextMarker("def=Convert_Success", selected),
                BeliefMutationEventSelector.ConversionGameContextMarker);
            AssertEqual("conversion marker append is idempotent",
                "def=Convert_Success; " + BeliefMutationEventSelector.ConversionGameContextMarker,
                BeliefMutationEventSelector.AppendGameContextMarker(
                    "def=Convert_Success; " + BeliefMutationEventSelector.ConversionGameContextMarker,
                    selected));
            BeliefEventEvidence converterPov = BeliefEventEvidenceFactory.ForPov(
                selected, "EventA", 100, "ConverterPawn", "initiator");
            BeliefEventEvidence targetPov = BeliefEventEvidenceFactory.ForPov(
                selected, "EventA", 100, "TargetPawn", "recipient");
            AssertTrue("two authorized POV copies can share one detached mechanical fact",
                converterPov.mutation.conversionSucceeded == true
                    && targetPov.mutation.conversionSucceeded == true);

            AssertTrue("cross-pawn mutation is rejected",
                BeliefMutationEventSelector.Select(successRule, "OtherPawn", 100, 10, success) == null);
            AssertTrue("stale mutation is rejected",
                BeliefMutationEventSelector.Select(successRule, "TargetPawn", 111, 10, success) == null);
            AssertTrue("future-tick mutation is rejected even inside cache maintenance window",
                BeliefMutationEventSelector.Select(successRule, "TargetPawn", 99, 10, success) == null);

            BeliefMutationSnapshot failure = Mutation(
                "TargetPawn", 101, "OldIdeo", "OldIdeo", 0.8f, 0.7f,
                BeliefMutationCauseTokens.ConversionAttempt, false, ideologyChanged: false);
            failure.attemptedIdeologyId = "NewIdeo";
            AssertTrue("exact failed conversion selects falling-certainty evidence",
                BeliefMutationEventSelector.Select(failureRule, "TargetPawn", 101, 10, failure) != null);
            AssertTrue("failure row cannot enrich a success page",
                BeliefMutationEventSelector.Select(successRule, "TargetPawn", 101, 10, failure) == null);
            AssertTrue("success row cannot enrich a failure page",
                BeliefMutationEventSelector.Select(failureRule, "TargetPawn", 100, 10, success) == null);
            AssertTrue("known-result conversion rule accepts a success result",
                BeliefMutationEventSelector.Select(
                    knownResultRule, "TargetPawn", 100, 10, success) != null);
            AssertTrue("known-result conversion rule accepts a failure result",
                BeliefMutationEventSelector.Select(
                    knownResultRule, "TargetPawn", 101, 10, failure) != null);
            BeliefMutationSnapshot unknownResult = Mutation(
                "TargetPawn", 101, "OldIdeo", "OldIdeo", 0.8f, 0.7f,
                BeliefMutationCauseTokens.ConversionAttempt, null, ideologyChanged: false);
            unknownResult.attemptedIdeologyId = "NewIdeo";
            AssertTrue("known-result conversion rule rejects an unknown result",
                BeliefMutationEventSelector.Select(
                    knownResultRule, "TargetPawn", 101, 10, unknownResult) == null);

            BeliefMutationSnapshot reassurance = Mutation(
                "TargetPawn", 102, "SameIdeo", "SameIdeo", 0.4f, 0.55f,
                BeliefMutationCauseTokens.CertaintyOffset, null, ideologyChanged: false);
            AssertTrue("certainty-only increase enriches exact reassurance",
                BeliefMutationEventSelector.Select(
                    reassuranceRule, "TargetPawn", 102, 10, reassurance) != null);
            reassurance.beforeCertainty = 0.6f;
            reassurance.afterCertainty = 0.5f;
            AssertTrue("certainty decrease cannot masquerade as reassurance",
                BeliefMutationEventSelector.Select(
                    reassuranceRule, "TargetPawn", 102, 10, reassurance) == null);

            BeliefMutationBuffer sequential = new BeliefMutationBuffer();
            sequential.RecordOrMerge(success, 100, 8, 10);
            BeliefMutationSnapshot laterCertainty = Mutation(
                "TargetPawn", 100, "NewIdeo", "NewIdeo", 0.5f, 0.4f,
                BeliefMutationCauseTokens.CertaintyOffset, null, ideologyChanged: false);
            laterCertainty.startedSequence = 3;
            laterCertainty.completedSequence = 4;
            sequential.RecordOrMerge(laterCertainty, 100, 8, 10);
            BeliefMutationSnapshot newest = sequential.PeekLatest("TargetPawn", 100, 10);
            AssertEqual("sequential fixture keeps both mechanical actions", 2, sequential.Count);
            AssertTrue("selector never scans backward into an older sequential conversion",
                BeliefMutationEventSelector.Select(
                    successRule, "TargetPawn", 100, 10, newest) == null);
            AssertEqual("failed sequential selection remains non-consuming", 2, sequential.Count);

            BeliefMutationSnapshot malformed = Mutation(
                "TargetPawn", 100, "OldIdeo", "NewIdeo", 0.2f, 0.3f,
                BeliefMutationCauseTokens.ConversionAttempt, true, ideologyChanged: true);
            malformed.startedSequence = 0;
            malformed.attemptedIdeologyId = "NewIdeo";
            AssertTrue("missing cache ordering metadata fails closed",
                BeliefMutationEventSelector.Select(
                    successRule, "TargetPawn", 100, 10, malformed) == null);
            AssertTrue("null and empty selector inputs fail closed",
                BeliefMutationEventSelector.Select(null, string.Empty, -1, 10, null) == null);

            BeliefPolicyBuilder builder = BeliefPolicyBuilder.CreateDefault();
            builder.mutationEventRules.Add(successRule);
            builder.mutationEventRules.Add(new BeliefMutationEventRule(
                "interaction", "Malformed", "conversion", "observer", "conversion",
                "unknown_cause", "maybe", "sideways", "sometimes", false));
            BeliefPolicySnapshot frozen = builder.Build();
            builder.mutationEventRules.Clear();
            AssertEqual("mutation-event snapshot deep-copies only validated XML-style rows", 1,
                frozen.mutationEventRules.Count);

            BeliefEventEvidence sharedTargetMutation = Evidence(true);
            sharedTargetMutation.mutation = success;
            BeliefSnapshot converterSnapshot = Snapshot();
            converterSnapshot.pawnId = "ConverterPawn";
            converterSnapshot.certainty = new BeliefCertaintyFact { hasCurrent = true, current = 0.8f };
            sharedTargetMutation.narrative.povPawnId = converterSnapshot.pawnId;
            BeliefStanceResolution converterResolution = Resolve(
                converterSnapshot, sharedTargetMutation, BeliefPolicySnapshot.CreateDefault());
            AssertNear("recipient mutation does not rewrite converter certainty", 0.8f,
                converterResolution.certainty);
            AssertEqual("recipient mutation does not become converter certainty trend",
                BeliefCertaintyTrendTokens.Unknown, converterResolution.certaintyTrend);
            AssertTrue("converter still receives the shared event mutation",
                converterResolution.mutation != null);

            BeliefMutationSnapshot crisisChanged = Mutation(
                "BreakingPawn", 200, "FormerFaith", "CurrentFaith", 0.2f, 0.5f,
                BeliefMutationCauseTokens.ConversionAttempt, true, ideologyChanged: true);
            crisisChanged.attemptedIdeologyId = "CurrentFaith";
            crisisChanged.attemptedIdeologyName = "Current faith";
            BeliefEventEvidence changedCrisis = BeliefMutationEventSelector.SelectCrisisOrCurrent(
                crisisRule, "BreakingPawn", 200, 10, crisisChanged, "Breaking pawn");
            AssertTrue("changed IdeoChange keeps its observed mutation and current-facts fallback",
                changedCrisis != null && ReferenceEquals(changedCrisis.mutation, crisisChanged)
                    && changedCrisis.currentBeliefFactsRelevant);
            AssertContains("crisis evidence appends the exact priority marker",
                BeliefMutationEventSelector.AppendGameContextMarker(
                    "mental_state=IdeoChange", changedCrisis),
                BeliefMutationEventSelector.CrisisGameContextMarker);

            BeliefSnapshot changedSnapshot = Snapshot();
            changedSnapshot.pawnId = "BreakingPawn";
            changedSnapshot.ideologyId = "CurrentFaith";
            changedSnapshot.ideologyName = "Current faith";
            changedSnapshot.certainty = new BeliefCertaintyFact { hasCurrent = true, current = 0.5f };
            string changedCrisisContext = BeliefContextFormatter.Format(
                Resolve(changedSnapshot, changedCrisis, BeliefPolicySnapshot.CreateDefault()),
                NarrativeDetailLevelTokens.Full, BeliefPolicySnapshot.CreateDefault());
            AssertContains("changed crisis reports observed previous identity",
                changedCrisisContext, "previous ideoligion: FormerFaith name");
            AssertContains("changed crisis reports observed current identity",
                changedCrisisContext, "current ideoligion: CurrentFaith name");
            AssertContains("changed crisis reports observed certainty mechanics",
                changedCrisisContext, "conversion result: success");

            BeliefMutationSnapshot crisisUnchanged = Mutation(
                "BreakingPawn", 201, "SteadyFaith", "SteadyFaith", 0.9f, 0.4f,
                BeliefMutationCauseTokens.ConversionAttempt, false, ideologyChanged: false);
            crisisUnchanged.attemptedIdeologyId = "ChallengerFaith";
            crisisUnchanged.attemptedIdeologyName = "Challenger faith";
            BeliefEventEvidence unchangedCrisis = BeliefMutationEventSelector.SelectCrisisOrCurrent(
                crisisRule, "BreakingPawn", 201, 10, crisisUnchanged, "Breaking pawn");
            BeliefSnapshot unchangedSnapshot = Snapshot();
            unchangedSnapshot.pawnId = "BreakingPawn";
            unchangedSnapshot.ideologyId = "SteadyFaith";
            unchangedSnapshot.ideologyName = "Steady faith";
            unchangedSnapshot.certainty = new BeliefCertaintyFact { hasCurrent = true, current = 0.4f };
            string unchangedCrisisContext = BeliefContextFormatter.Format(
                Resolve(unchangedSnapshot, unchangedCrisis, BeliefPolicySnapshot.CreateDefault()),
                NarrativeDetailLevelTokens.Full, BeliefPolicySnapshot.CreateDefault());
            AssertContains("unchanged crisis reports failure instead of conversion",
                unchangedCrisisContext, "conversion result: failure");
            AssertContains("unchanged crisis reports falling certainty",
                unchangedCrisisContext, "certainty trend: falling");
            AssertContains("unchanged crisis preserves the same observed identity",
                unchangedCrisisContext, "current ideoligion: SteadyFaith name");

            BeliefMutationSnapshot wrongPawnCrisis = Mutation(
                "OtherPawn", 202, "OtherOld", "OtherNew", 0.2f, 0.5f,
                BeliefMutationCauseTokens.ConversionAttempt, true, ideologyChanged: true);
            wrongPawnCrisis.attemptedIdeologyId = "OtherNew";
            BeliefEventEvidence wrongPawnFallback = BeliefMutationEventSelector.SelectCrisisOrCurrent(
                crisisRule, "BreakingPawn", 202, 10, wrongPawnCrisis);
            AssertTrue("wrong-pawn crisis evidence falls back to current facts only",
                wrongPawnFallback?.currentBeliefFactsRelevant == true
                    && wrongPawnFallback.mutation == null);
            BeliefEventEvidence staleFallback = BeliefMutationEventSelector.SelectCrisisOrCurrent(
                crisisRule, "BreakingPawn", 213, 10, crisisUnchanged);
            AssertTrue("stale crisis evidence falls back to current facts only",
                staleFallback?.currentBeliefFactsRelevant == true && staleFallback.mutation == null);
            BeliefEventEvidence futureFallback = BeliefMutationEventSelector.SelectCrisisOrCurrent(
                crisisRule, "BreakingPawn", 200, 10, crisisUnchanged);
            AssertTrue("future crisis evidence falls back to current facts only",
                futureFallback?.currentBeliefFactsRelevant == true && futureFallback.mutation == null);
            BeliefEventEvidence missingFallback = BeliefMutationEventSelector.SelectCrisisOrCurrent(
                crisisRule, "BreakingPawn", 202, 10, null);
            AssertTrue("missing crisis mutation still permits only current visible facts",
                missingFallback?.currentBeliefFactsRelevant == true && missingFallback.mutation == null);

            BeliefSnapshot fallbackSnapshot = Snapshot();
            fallbackSnapshot.pawnId = "BreakingPawn";
            fallbackSnapshot.ideologyName = "Visible current faith";
            fallbackSnapshot.certainty = new BeliefCertaintyFact { hasCurrent = true, current = 0.37f };
            string fallbackContext = BeliefContextFormatter.Format(
                Resolve(fallbackSnapshot, missingFallback, BeliefPolicySnapshot.CreateDefault()),
                NarrativeDetailLevelTokens.Full, BeliefPolicySnapshot.CreateDefault());
            AssertContains("missing mutation fallback reports current ideoligion",
                fallbackContext, "ideoligion: Visible current faith");
            AssertContains("missing mutation fallback reports current certainty",
                fallbackContext, "certainty: 37%");
            AssertTrue("missing mutation fallback never reconstructs an old or attempted ideoligion",
                fallbackContext.IndexOf("previous ideoligion:", StringComparison.Ordinal) < 0
                    && fallbackContext.IndexOf("attempted ideoligion:", StringComparison.Ordinal) < 0
                    && fallbackContext.IndexOf("conversion result:", StringComparison.Ordinal) < 0);
        }

        private static void TestCounselExactOutcomeContextPolicy()
        {
            CounselEventRule successRule = new CounselEventRule(
                "Counsel_Success", "counsel", "success", "relief_or_boost");
            CounselEventRule failureRule = new CounselEventRule(
                "Counsel_Failure", "counsel", "failure", "penalty");
            List<CounselEventRule> rules = new List<CounselEventRule>
            {
                successRule,
                failureRule
            };

            CounselEventRule success = CounselEventPolicy.RuleFor(
                "Counsel_Success", "counsel", true, true, rules);
            AssertTrue("exact Counsel success selects its XML rule",
                object.ReferenceEquals(successRule, success));
            string successContext = CounselEventPolicy.AppendGameContext(
                "interaction=Counsel_Success", success);
            AssertContains("Counsel success appends exact mood context", successContext,
                "counsel_result=success; counsel_mood_effect=relief_or_boost");
            AssertEqual("Counsel success context append is idempotent", successContext,
                CounselEventPolicy.AppendGameContext(successContext, success));
            AssertTrue("Counsel success context contains no conversion or certainty schema",
                successContext.IndexOf("belief_event=conversion", StringComparison.Ordinal) < 0
                    && successContext.IndexOf("certainty", StringComparison.Ordinal) < 0);

            CounselEventRule failure = CounselEventPolicy.RuleFor(
                "Counsel_Failure", "counsel", true, true, rules);
            AssertTrue("exact Counsel failure selects its XML rule",
                object.ReferenceEquals(failureRule, failure));
            AssertContains("Counsel failure appends exact mood penalty context",
                CounselEventPolicy.AppendGameContext(string.Empty, failure),
                "counsel_result=failure; counsel_mood_effect=penalty");

            AssertTrue("case-variant Counsel success remains silent",
                CounselEventPolicy.RuleFor(
                    "counsel_success", "counsel", true, true, rules) == null);
            AssertTrue("wrong Counsel group remains silent",
                CounselEventPolicy.RuleFor(
                    "Counsel_Success", "conversion", true, true, rules) == null);
            AssertTrue("unknown modded Counsel event remains silent",
                CounselEventPolicy.RuleFor(
                    "ModdedCounsel_Success", "counsel", true, true, rules) == null);
            AssertTrue("inactive Ideology makes exact Counsel inert",
                CounselEventPolicy.RuleFor(
                    "Counsel_Success", "counsel", false, true, rules) == null);
            AssertTrue("disabled belief policy makes exact Counsel inert",
                CounselEventPolicy.RuleFor(
                    "Counsel_Success", "counsel", true, false, rules) == null);
            AssertTrue("missing XML Counsel policy fails closed",
                CounselEventPolicy.RuleFor(
                    "Counsel_Success", "counsel", true, true, null) == null);
            AssertTrue("unsafe XML marker token fails closed",
                CounselEventPolicy.RuleFor(
                    "Counsel_Success", "counsel", true, true,
                    new List<CounselEventRule> {
                        new CounselEventRule("Counsel_Success", "counsel", "success; forged=1", "boost")
                    }) == null);

            BeliefPolicyBuilder builder = BeliefPolicyBuilder.CreateDefault();
            AssertEqual("fallback Counsel outcome list stays empty", 0, builder.counselEventRules.Count);
            builder.counselEventRules.Add(successRule);
            BeliefPolicySnapshot frozen = builder.Build();
            builder.counselEventRules.Clear();
            AssertEqual("snapshot deep-copies Counsel outcome rules", 1, frozen.counselEventRules.Count);

            AssertTrue("missing Counsel override inherits explicit disabled conversion intent",
                !CounselSettingsInheritance.Enabled(null, false, true));
            AssertTrue("missing Counsel override inherits explicit enabled conversion intent",
                CounselSettingsInheritance.Enabled(null, true, false));
            AssertTrue("explicit Counsel override wins over legacy conversion intent",
                !CounselSettingsInheritance.Enabled(false, true, true));
            AssertTrue("no saved intent uses the Counsel XML default",
                CounselSettingsInheritance.Enabled(null, null, true));
            AssertTrue("default-equal Counsel choice is stored against opposite legacy intent",
                CounselSettingsInheritance.ShouldStoreOverride(true, true, false));
            AssertTrue("default-equal Counsel choice is redundant without legacy intent",
                !CounselSettingsInheritance.ShouldStoreOverride(true, true, null));
            AssertTrue("non-default Counsel choice is always stored",
                CounselSettingsInheritance.ShouldStoreOverride(false, true, true));
        }

        private static void TestConversionRitualExactCompletionPolicy()
        {
            ConversionRitualPolicyBuilder builder = ConversionRitualPolicyBuilderForTests();
            List<string> mutableCauses = builder.allowedMutationCauseTokens;
            ConversionRitualPolicySnapshot policy = builder.Build();
            mutableCauses.Add(BeliefMutationCauseTokens.ConversionAttempt);
            AssertEqual("conversion ritual policy deep-copies allowed causes", 2,
                policy.allowedMutationCauseTokens.Count);
            AssertTrue("exact installed conversion ritual identity matches",
                ConversionRitualPolicy.Matches(
                    "Conversion", ConversionBehaviorTypeName,
                    ConversionOutcomeTypeName, "ritualConversion", true, policy));
            AssertTrue("defName alone cannot match conversion ritual",
                !ConversionRitualPolicy.Matches(
                    "Conversion", string.Empty, ConversionOutcomeTypeName,
                    "ritualConversion", true, policy));
            AssertTrue("case variant cannot match conversion ritual",
                !ConversionRitualPolicy.Matches(
                    "conversion", ConversionBehaviorTypeName,
                    ConversionOutcomeTypeName, "ritualConversion", true, policy));
            AssertTrue("legacy short worker names cannot claim exact ritual mechanics",
                !ConversionRitualPolicy.Matches(
                    "Conversion", "RitualBehaviorWorker_Conversion",
                    "RitualOutcomeEffectWorker_Conversion", "ritualConversion", true, policy));
            AssertTrue("namespaced behavior collision cannot claim exact ritual mechanics",
                !ConversionRitualPolicy.Matches(
                    "Conversion", "SomeMod.RitualBehaviorWorker_Conversion",
                    ConversionOutcomeTypeName, "ritualConversion", true, policy));
            AssertTrue("namespaced outcome collision cannot claim exact ritual mechanics",
                !ConversionRitualPolicy.Matches(
                    "Conversion", ConversionBehaviorTypeName,
                    "SomeMod.RitualOutcomeEffectWorker_Conversion",
                    "ritualConversion", true, policy));
            AssertTrue("wrong behavior cannot match conversion ritual",
                !ConversionRitualPolicy.Matches(
                    "Conversion", "ModdedConversionWorker",
                    ConversionOutcomeTypeName, "ritualConversion", true, policy));
            AssertTrue("wrong outcome worker cannot match conversion ritual",
                !ConversionRitualPolicy.Matches(
                    "Conversion", ConversionBehaviorTypeName, "ModdedOutcome",
                    "ritualConversion", true, policy));
            AssertTrue("generic ritual group cannot receive exact enrichment",
                !ConversionRitualPolicy.Matches(
                    "Conversion", ConversionBehaviorTypeName,
                    ConversionOutcomeTypeName, "ritualFinished", true, policy));
            AssertTrue("DLC-off exact collision remains inert",
                !ConversionRitualPolicy.Matches(
                    "Conversion", ConversionBehaviorTypeName,
                    ConversionOutcomeTypeName, "ritualConversion", false, policy));

            ConversionRitualPolicyBuilder malformedBuilder = ConversionRitualPolicyBuilderForTests();
            malformedBuilder.enabled = false;
            malformedBuilder.maximumAdditionalContextCharacters = 9999;
            malformedBuilder.certaintyDeltaEpsilon = float.NaN;
            malformedBuilder.mutationCorrelationWindowTicks = 11;
            ConversionRitualPolicySnapshot malformed = malformedBuilder.Build();
            AssertTrue("disabled conversion ritual policy fails closed",
                !ConversionRitualPolicy.Matches(
                    "Conversion", ConversionBehaviorTypeName,
                    ConversionOutcomeTypeName, "ritualConversion", true, malformed));
            AssertEqual("malformed conversion context cap uses bounded fallback", 192,
                malformed.maximumAdditionalContextCharacters);
            AssertNear("malformed conversion epsilon uses bounded fallback", 0.0001f,
                malformed.certaintyDeltaEpsilon);
            AssertEqual("malformed conversion correlation window uses same-tick fallback", 0,
                malformed.mutationCorrelationWindowTicks);

            ConversionRitualPolicyBuilder unsafeIdentityBuilder =
                ConversionRitualPolicyBuilderForTests();
            unsafeIdentityBuilder.behaviorWorkerClassName = "RimWorld.Bad Worker";
            unsafeIdentityBuilder.organizerRoleToken = "bad-token";
            ConversionRitualPolicySnapshot unsafeIdentity = unsafeIdentityBuilder.Build();
            AssertEqual("unsafe conversion worker identity is rejected", string.Empty,
                unsafeIdentity.behaviorWorkerClassName);
            AssertEqual("unsafe conversion role token is rejected", string.Empty,
                unsafeIdentity.organizerRoleToken);

            BeliefMutationSnapshot convertedInput = Mutation(
                "Target", 500, "OldIdeo", "OrganizerIdeo", 0.2f, 0.5f,
                BeliefMutationCauseTokens.SetIdeology, null, true);
            BeliefMutationSnapshot converted = ConversionRitualPolicy.SelectTargetMutation(
                "Target", "OrganizerIdeo", 500, convertedInput, policy);
            AssertTrue("exact target transition into organizer ideology proves ritual conversion",
                converted?.conversionSucceeded == true);
            AssertTrue("conversion selection leaves cached source result untouched",
                !convertedInput.conversionSucceeded.HasValue);
            converted.causeTokens.Add("late_test_mutation");
            AssertEqual("conversion selection deep-copies mutation causes", 1,
                convertedInput.causeTokens.Count);

            BeliefMutationSnapshot certaintyDownInput = Mutation(
                "Target", 500, "OldIdeo", "OldIdeo", 0.8f, 0.5f,
                BeliefMutationCauseTokens.CertaintyOffset, null, false);
            BeliefMutationSnapshot certaintyDown = ConversionRitualPolicy.SelectTargetMutation(
                "Target", "OrganizerIdeo", 500, certaintyDownInput, policy);
            AssertTrue("certainty-only effective ritual is verified non-conversion",
                certaintyDown?.conversionSucceeded == false && certaintyDown.certaintyChanged);
            BeliefMutationSnapshot certaintyUp = ConversionRitualPolicy.SelectTargetMutation(
                "Target", "OrganizerIdeo", 500,
                Mutation("Target", 500, "OldIdeo", "OldIdeo", 0.4f, 0.6f,
                    BeliefMutationCauseTokens.CertaintyOffset, null, false), policy);
            AssertTrue("terrible ritual certainty increase is not called conversion",
                certaintyUp?.conversionSucceeded == false);
            AssertTrue("wrong target cannot borrow adjacent ritual mutation",
                ConversionRitualPolicy.SelectTargetMutation(
                    "Other", "OrganizerIdeo", 500, convertedInput, policy) == null);
            AssertTrue("future mutation cannot explain completed ritual",
                ConversionRitualPolicy.SelectTargetMutation(
                    "Target", "OrganizerIdeo", 499, convertedInput, policy) == null);
            AssertTrue("stale mutation cannot explain completed ritual",
                ConversionRitualPolicy.SelectTargetMutation(
                    "Target", "OrganizerIdeo", 501, convertedInput, policy) == null);
            AssertTrue("transition to unrelated ideology is rejected",
                ConversionRitualPolicy.SelectTargetMutation(
                    "Target", "OrganizerIdeo", 500,
                    Mutation("Target", 500, "OldIdeo", "ThirdIdeo", 0.2f, 0.5f,
                        BeliefMutationCauseTokens.SetIdeology, null, true), policy) == null);
            AssertTrue("adjacent conversion-attempt cause is rejected",
                ConversionRitualPolicy.SelectTargetMutation(
                    "Target", "OrganizerIdeo", 500,
                    Mutation("Target", 500, "OldIdeo", "OrganizerIdeo", 0.2f, 0.5f,
                        BeliefMutationCauseTokens.ConversionAttempt, true, true), policy) == null);
            AssertTrue("quality cannot manufacture a result when no mutation exists",
                ConversionRitualPolicy.SelectTargetMutation(
                    "Target", "OrganizerIdeo", 500, null, policy) == null);

            BeliefSourcePreceptFact role = new BeliefSourcePreceptFact
            {
                instanceId = "PreceptRole_1",
                defName = "IdeoRole_Moralist"
            };
            AssertEqual("conversion organizer uses the live ritual POV schema token", "author",
                ConversionRitualPolicy.PerspectiveOrganizer);
            BeliefEventEvidence organizerEvidence = ConversionRitualPolicy.EvidenceFor(
                "Organizer", 500, "Conversion", "Organizer label",
                "author", role, converted, policy);
            AssertEqual("organizer gets exact proselytizing role identity", "IdeoRole_Moralist",
                organizerEvidence.sourcePreceptDefName);
            AssertTrue("organizer gets no target mutation", organizerEvidence.mutation == null);
            AssertTrue("wrong organizer role identity fails closed",
                ConversionRitualPolicy.EvidenceFor(
                    "Organizer", 500, "Conversion", "Organizer label",
                    ConversionRitualPolicy.PerspectiveOrganizer,
                    new BeliefSourcePreceptFact { instanceId = "x", defName = "OtherRole" },
                    converted, policy) == null);
            AssertTrue("non-schema organizer alias fails closed",
                ConversionRitualPolicy.EvidenceFor(
                    "Organizer", 500, "Conversion", "Organizer label",
                    "organizer", role, converted, policy) == null);

            BeliefEventEvidence targetEvidence = ConversionRitualPolicy.EvidenceFor(
                "Target", 500, "Conversion", "Target label",
                ConversionRitualPolicy.PerspectiveTarget, role, converted, policy);
            AssertTrue("target gets detached before/after mutation", targetEvidence.mutation != null
                && targetEvidence.mutation.beforeIdeologyId == "OldIdeo"
                && targetEvidence.mutation.afterIdeologyId == "OrganizerIdeo");
            AssertTrue("another pawn cannot receive target mutation",
                ConversionRitualPolicy.EvidenceFor(
                    "Participant", 500, "Conversion", "Participant label",
                    ConversionRitualPolicy.PerspectiveTarget, role, converted, policy) == null);
            BeliefEventEvidence participantEvidence = ConversionRitualPolicy.EvidenceFor(
                "Participant", 500, "Conversion", "Participant label",
                ConversionRitualPolicy.PerspectiveParticipant, role, converted, policy);
            AssertTrue("participant receives smaller current-belief context only",
                participantEvidence.currentBeliefFactsRelevant && participantEvidence.mutation == null
                    && participantEvidence.sourcePreceptDefName.Length == 0);
            AssertTrue("spectator receives no belief enrichment",
                ConversionRitualPolicy.EvidenceFor(
                    "Spectator", 500, "Conversion", "Spectator label",
                    ConversionRitualPolicy.PerspectiveSpectator, role, converted, policy) == null);

            string organizerContext = ConversionRitualPolicy.AppendGameContext(
                "ritual=Conversion; quality=masterful", "author", null, policy);
            AssertContains("live author context carries the converter role", organizerContext,
                "conversion_ritual_role=converter");
            AssertTrue("organizer context never receives target result",
                organizerContext.IndexOf("conversion_ritual_result=", StringComparison.Ordinal) < 0);
            string targetContext = ConversionRitualPolicy.AppendGameContext(
                "ritual=Conversion; quality=masterful", ConversionRitualPolicy.PerspectiveTarget,
                converted, policy);
            AssertContains("target context carries exact role", targetContext,
                "conversion_ritual_role=convertee");
            AssertContains("target context carries mechanically verified result", targetContext,
                "conversion_ritual_result=converted");
            AssertEqual("conversion ritual context append is idempotent", targetContext,
                ConversionRitualPolicy.AppendGameContext(
                    targetContext, ConversionRitualPolicy.PerspectiveTarget, converted, policy));
            string participantContext = ConversionRitualPolicy.AppendGameContext(
                "ritual=Conversion; quality=masterful",
                ConversionRitualPolicy.PerspectiveParticipant, converted, policy);
            AssertContains("participant context carries only own role", participantContext,
                "conversion_ritual_role=participant");
            AssertTrue("participant context never receives target result",
                participantContext.IndexOf("conversion_ritual_result=", StringComparison.Ordinal) < 0);
            ConversionRitualPolicyBuilder tinyContextBuilder =
                ConversionRitualPolicyBuilderForTests();
            tinyContextBuilder.maximumAdditionalContextCharacters = 32;
            AssertEqual("small conversion context cap omits an oversized role/result suffix",
                "ritual=Conversion",
                ConversionRitualPolicy.AppendGameContext(
                    "ritual=Conversion", ConversionRitualPolicy.PerspectiveTarget,
                    converted, tinyContextBuilder.Build()));

            AssertTrue("missing exact override inherits disabled generic ritual intent",
                !ConversionRitualSettingsInheritance.Enabled(null, false, true));
            AssertTrue("explicit exact override wins over legacy ritual intent",
                ConversionRitualSettingsInheritance.Enabled(true, false, true));
            AssertTrue("default-equal exact choice is stored against opposite legacy intent",
                ConversionRitualSettingsInheritance.ShouldStoreOverride(true, true, false));
            AssertTrue("default-equal exact choice is redundant without legacy intent",
                !ConversionRitualSettingsInheritance.ShouldStoreOverride(true, true, null));
        }

        private static void TestAuthoritySpeechExactEnrichmentPolicy()
        {
            AuthoritySpeechPolicyBuilder builder = AuthoritySpeechPolicyBuilderForTests();
            List<AuthoritySpeechRouteBuilder> mutableRoutes = builder.routes;
            AuthoritySpeechPolicySnapshot policy = builder.Build();
            mutableRoutes[0].ritualDefName = "LateMutation";
            mutableRoutes.Clear();
            AssertEqual("authority policy deep-copies exact routes", 2, policy.routes.Count);
            AssertEqual("authority policy route copy is isolated", "ThroneSpeech",
                policy.routes[0].ritualDefName);

            AuthoritySpeechRouteSnapshot throne = AuthoritySpeechPolicy.Match(
                "ThroneSpeech", "RimWorld.RitualBehaviorWorker_ThroneSpeech",
                SpeechOutcomeTypeName, "ritualRoyal", "speaker", true, true, policy);
            AssertTrue("exact installed throne speech route matches", throne != null);
            AuthoritySpeechRouteSnapshot leader = AuthoritySpeechPolicy.Match(
                "LeaderSpeech", "RimWorld.RitualBehaviorWorker_Speech",
                SpeechOutcomeTypeName, "ritualFinished", "speaker", true, false, policy);
            AssertTrue("exact installed leader speech route matches without Royalty", leader != null);
            AssertTrue("Royalty-off throne route remains inert",
                AuthoritySpeechPolicy.Match(
                    "ThroneSpeech", "RimWorld.RitualBehaviorWorker_ThroneSpeech",
                    SpeechOutcomeTypeName, "ritualRoyal", "speaker", true, false, policy) == null);
            AssertTrue("Ideology-off speech enrichment remains inert",
                AuthoritySpeechPolicy.Match(
                    "LeaderSpeech", "RimWorld.RitualBehaviorWorker_Speech",
                    SpeechOutcomeTypeName, "ritualFinished", "speaker", false, true, policy) == null);
            AssertTrue("short worker names cannot claim authority speech",
                AuthoritySpeechPolicy.Match(
                    "ThroneSpeech", "RitualBehaviorWorker_ThroneSpeech",
                    "RitualOutcomeEffectWorker_Speech", "ritualRoyal", "speaker",
                    true, true, policy) == null);
            AssertTrue("namespaced behavior collision cannot claim authority speech",
                AuthoritySpeechPolicy.Match(
                    "ThroneSpeech", "SomeMod.RitualBehaviorWorker_ThroneSpeech",
                    SpeechOutcomeTypeName, "ritualRoyal", "speaker", true, true, policy) == null);
            AssertTrue("wrong page owner cannot claim throne enrichment",
                AuthoritySpeechPolicy.Match(
                    "ThroneSpeech", "RimWorld.RitualBehaviorWorker_ThroneSpeech",
                    SpeechOutcomeTypeName, "ritualFinished", "speaker", true, true, policy) == null);
            AssertTrue("wrong assigned organizer role cannot claim speech enrichment",
                AuthoritySpeechPolicy.Match(
                    "ThroneSpeech", "RimWorld.RitualBehaviorWorker_ThroneSpeech",
                    SpeechOutcomeTypeName, "ritualRoyal", "audience", true, true, policy) == null);
            AssertTrue("case variants fail exact authority routing",
                AuthoritySpeechPolicy.Match(
                    "thronespeech", "RimWorld.RitualBehaviorWorker_ThroneSpeech",
                    SpeechOutcomeTypeName, "ritualRoyal", "speaker", true, true, policy) == null);

            AuthoritySpeechPolicyBuilder duplicateBuilder = AuthoritySpeechPolicyBuilderForTests();
            duplicateBuilder.routes.Add(new AuthoritySpeechRouteBuilder
            {
                ritualDefName = "ThroneSpeech",
                behaviorWorkerClassName = "RimWorld.RitualBehaviorWorker_ThroneSpeech",
                outcomeWorkerClassName = SpeechOutcomeTypeName,
                downstreamGroupDefName = "ritualRoyal",
                speakerRoleId = "speaker",
                requiresRoyalty = true
            });
            AssertTrue("duplicate exact authority routes fail closed",
                AuthoritySpeechPolicy.Match(
                    "ThroneSpeech", "RimWorld.RitualBehaviorWorker_ThroneSpeech",
                    SpeechOutcomeTypeName, "ritualRoyal", "speaker", true, true,
                    duplicateBuilder.Build()) == null);

            AuthoritySpeechPolicyBuilder malformedBuilder = AuthoritySpeechPolicyBuilderForTests();
            malformedBuilder.enabled = false;
            malformedBuilder.speakerMaximumSelectedStances = 99;
            malformedBuilder.witnessMaximumSupportingMemes = -4;
            malformedBuilder.witnessMaximumContextCharacters = 9000;
            malformedBuilder.speakerPromptInstruction = "bad\nsecond line";
            AuthoritySpeechPolicySnapshot malformed = malformedBuilder.Build();
            AssertTrue("disabled authority policy fails closed",
                AuthoritySpeechPolicy.Match(
                    "ThroneSpeech", "RimWorld.RitualBehaviorWorker_ThroneSpeech",
                    SpeechOutcomeTypeName, "ritualRoyal", "speaker", true, true, malformed) == null);
            AssertEqual("malformed authority stance cap is bounded", 1,
                malformed.speakerProjection.maximumSelectedStances);
            AssertEqual("malformed authority meme cap is bounded", 0,
                malformed.witnessProjection.maximumSupportingMemes);
            AssertEqual("malformed authority character cap is bounded", 320,
                malformed.witnessProjection.maximumContextCharacters);
            AssertEqual("multiline authority prompt instruction is sanitized", "bad second line",
                malformed.speakerProjection.promptInstruction);
            AssertTrue("missing authority policy is inert",
                AuthoritySpeechPolicy.Match(
                    "ThroneSpeech", "RimWorld.RitualBehaviorWorker_ThroneSpeech",
                    SpeechOutcomeTypeName, "ritualRoyal", "speaker", true, true,
                    AuthoritySpeechPolicySnapshot.CreateDefault()) == null);

            BeliefEventEvidence speaker = AuthoritySpeechPolicy.EvidenceFor(
                "Speaker", 880, "ThroneSpeech", "Speaker label",
                AuthoritySpeechPolicy.PerspectiveSpeaker, throne, policy);
            BeliefEventEvidence witness = AuthoritySpeechPolicy.EvidenceFor(
                "Witness", 880, "ThroneSpeech", "Witness label",
                AuthoritySpeechPolicy.PerspectiveParticipant, throne, policy);
            BeliefEventEvidence spectator = AuthoritySpeechPolicy.EvidenceFor(
                "Spectator", 880, "ThroneSpeech", "Spectator label",
                AuthoritySpeechPolicy.PerspectiveSpectator, throne, policy);
            AssertTrue("speaker receives authority topic", speaker != null
                && Contains(speaker.narrative.beliefTopics, "authority_speech"));
            AssertTrue("participant receives smaller shared authority topic", witness != null
                && Contains(witness.narrative.beliefTopics, "authority_speech"));
            AssertTrue("spectator receives the same bounded witness mode", spectator != null
                && !spectator.projection.includeCertainty);
            AssertTrue("selected target receives no authority enrichment",
                AuthoritySpeechPolicy.EvidenceFor(
                    "Target", 880, "ThroneSpeech", "Target label",
                    AuthoritySpeechPolicy.PerspectiveTarget, throne, policy) == null);
            AssertTrue("unknown POV receives no authority enrichment",
                AuthoritySpeechPolicy.EvidenceFor(
                    "Other", 880, "ThroneSpeech", "Other label",
                    "organizer", throne, policy) == null);

            AuthoritySpeechPolicyBuilder splitModeBuilder = AuthoritySpeechPolicyBuilderForTests();
            splitModeBuilder.spectatorEvidenceMode = AuthoritySpeechEvidenceModeTokens.None;
            AuthoritySpeechPolicySnapshot splitModePolicy = splitModeBuilder.Build();
            AuthoritySpeechRouteSnapshot splitModeRoute = AuthoritySpeechPolicy.Match(
                "ThroneSpeech", "RimWorld.RitualBehaviorWorker_ThroneSpeech",
                SpeechOutcomeTypeName, "ritualRoyal", "speaker", true, true, splitModePolicy);
            AssertTrue("split audience-mode policy still matches the exact route", splitModeRoute != null);
            AssertTrue("participant mode remains active when spectator mode is none",
                AuthoritySpeechPolicy.EvidenceFor(
                    "Witness", 880, "ThroneSpeech", "Witness label",
                    AuthoritySpeechPolicy.PerspectiveParticipant,
                    splitModeRoute, splitModePolicy) != null);
            AssertTrue("spectator mode can disable evidence independently of participant mode",
                AuthoritySpeechPolicy.EvidenceFor(
                    "Spectator", 880, "ThroneSpeech", "Spectator label",
                    AuthoritySpeechPolicy.PerspectiveSpectator,
                    splitModeRoute, splitModePolicy) == null);
            AssertTrue("speaker projection keeps role and certainty",
                speaker.projection.includeRole && speaker.projection.includeCertainty);
            AssertTrue("witness projection cannot inherit speaker role or certainty",
                !witness.projection.includeRole && !witness.projection.includeCertainty);
            speaker.narrative.beliefTopics.Add("late_mutation");
            speaker.projection.maximumContextCharacters = 64;
            AssertTrue("speaker topic mutation cannot leak to witness",
                !Contains(witness.narrative.beliefTopics, "late_mutation"));
            AssertEqual("speaker projection mutation cannot leak to witness", 320,
                witness.projection.maximumContextCharacters);
            AssertEqual("evidence mutation cannot leak back to frozen policy", 640,
                policy.speakerProjection.maximumContextCharacters);

            BeliefPolicyBuilder resolverBuilder = BeliefPolicyBuilder.CreateDefault();
            resolverBuilder.semanticAliases.Add(new BeliefSemanticAlias(
                "authority_speech", new List<string>
                {
                    "authority leadership", "leader social role",
                    "slavery social structure", "social hierarchy",
                    "slavery normal part life"
                }));
            BeliefPolicySnapshot resolverPolicy = resolverBuilder.Build();
            BeliefPreceptFact authority = Precept(
                "Slavery_Acceptable", "Slavery", "acceptable", 2);
            // Mirror the installed 1.6 projection instead of making the synthetic doctrine repeat our
            // alias verbatim. This pins the lexical bridge to vanilla's visible issue/description text.
            authority.issue.label = "slavery";
            authority.issue.description = string.Empty;
            authority.description = "Slavery is a normal, unremarkable part of life.";
            authority.associatedMemeDefNames.Add("HierarchyMeme");
            BeliefPreceptFact unrelated = Precept(
                "TreeWorship", "Trees", "trees forests natural reverence", 3);
            BeliefSnapshot authoritySnapshot = Snapshot(authority, unrelated);
            authoritySnapshot.roleName = "Leader";
            authoritySnapshot.memes.Add(Meme("HierarchyMeme", "Social hierarchy", 2));
            authoritySnapshot.structure = Meme("StructureMeme", "Collectivist structure", 2);
            authoritySnapshot.deities.Add(new BeliefDeityFact { name = "Synthetic deity", isKeyDeity = true });

            BeliefEventEvidence speakerFresh = AuthoritySpeechPolicy.EvidenceFor(
                "Speaker", 880, "ThroneSpeech", "Speaker label",
                AuthoritySpeechPolicy.PerspectiveSpeaker, throne, policy);
            authoritySnapshot.pawnId = speakerFresh.narrative.povPawnId;
            BeliefStanceResolution speakerResolution = Resolve(
                authoritySnapshot, speakerFresh, resolverPolicy);
            AssertSelected("authority speech selects only relevant visible doctrine",
                speakerResolution, "Slavery_Acceptable");
            AssertEqual("speaker retains current role", "Leader", speakerResolution.roleName);
            AssertTrue("speaker retains current certainty", speakerResolution.hasCertainty);
            AssertEqual("speaker stance cap is enforced", 1, speakerResolution.stances.Count);
            AssertEqual("speaker supporting-meme cap is enforced", 1,
                speakerResolution.supportingMemes.Count);
            AssertTrue("speaker optional structure is permitted", speakerResolution.structure != null);
            AssertTrue("speaker deity remains omitted by policy", speakerResolution.deity == null);
            string speakerContext = BeliefContextFormatter.Format(
                speakerResolution, NarrativeDetailLevelTokens.Full, resolverPolicy);
            AssertContains("speaker context contains current role", speakerContext, "role: Leader");
            AssertContains("speaker context contains current certainty", speakerContext, "certainty:");
            AssertTrue("speaker context obeys event character cap", speakerContext.Length <= 640);
            AssertEqual("authority formatting is idempotent", speakerContext,
                BeliefContextFormatter.Format(
                    speakerResolution, NarrativeDetailLevelTokens.Full, resolverPolicy));

            authoritySnapshot.pawnId = witness.narrative.povPawnId;
            BeliefStanceResolution witnessResolution = Resolve(
                authoritySnapshot, witness, resolverPolicy);
            AssertSelected("witness may receive the shared relevant authority doctrine",
                witnessResolution, "Slavery_Acceptable");
            AssertEqual("witness receives no role", string.Empty, witnessResolution.roleName);
            AssertTrue("witness receives no certainty", !witnessResolution.hasCertainty);
            AssertEqual("witness receives no supporting meme", 0,
                witnessResolution.supportingMemes.Count);
            AssertTrue("witness receives no structure", witnessResolution.structure == null);
            AssertTrue("witness receives no deity", witnessResolution.deity == null);
            AssertTrue("witness does not create narrative interpretation",
                !witnessResolution.includeNarrativeInterpretation);
            string witnessContext = BeliefContextFormatter.Format(
                witnessResolution, NarrativeDetailLevelTokens.Full, resolverPolicy);
            AssertTrue("witness context contains no speaker role",
                witnessContext.IndexOf("role:", StringComparison.Ordinal) < 0);
            AssertTrue("witness context contains no speaker certainty",
                witnessContext.IndexOf("certainty:", StringComparison.Ordinal) < 0);
            AssertTrue("witness context obeys smaller event cap", witnessContext.Length <= 320);

            BeliefSnapshot unrelatedSnapshot = Snapshot(unrelated);
            AssertEmpty("unrelated doctrine leaves authority speech unenriched",
                Resolve(unrelatedSnapshot, speakerFresh, resolverPolicy));
            BeliefSnapshot hiddenSnapshot = Snapshot(
                Precept("HiddenAuthority", "Authority", "authority leadership", 3, false));
            AssertEmpty("hidden authority doctrine cannot enrich speech",
                Resolve(hiddenSnapshot, speakerFresh, resolverPolicy));
            BeliefSnapshot secular = new BeliefSnapshot();
            AssertEmpty("secular or unavailable belief snapshot leaves speech ordinary",
                Resolve(secular, speakerFresh, resolverPolicy));
        }

        private static void TestN3IIdeologyInterpretationProvider()
        {
            BeliefPolicySnapshot policy = VocabularyPolicy();
            BeliefPreceptFact precept = Precept(
                "SyntheticBodyRespect", "SyntheticBodyIssue", "Respect the changed body", 2);
            BeliefEventEvidence eventEvidence = SourceEvidence(precept.instanceId, precept.defName);
            eventEvidence.narrative.eventId = "body|SyntheticPawn|12345";
            eventEvidence.narrative.facet = NarrativeFacetTokens.IdentityTransition;
            eventEvidence.narrative.phase = "body_modified";
            eventEvidence.narrative.subjectKind = NarrativeSubjectKindTokens.Pawn;
            eventEvidence.narrative.subjectId = "SyntheticPawn";
            eventEvidence.narrative.beliefTopics.Add("body_modification");

            BeliefStanceResolution resolved = Resolve(Snapshot(precept), eventEvidence, policy);
            IdeologyNarrativeSnapshot snapshot = IdeologyNarrativeSnapshotFactory.Create(
                resolved,
                eventEvidence.narrative,
                policy,
                "Within Synthetic Ideoligion, this event directly engaged Respect the changed body.");
            AssertTrue("N3-I admits an exact source-precept result", snapshot != null);
            AssertEqual("N3-I freezes the shared interpretation category",
                NarrativeCategoryTokens.Interpretation, resolved.narrativeCategory);
            AssertEqual("N3-I snapshot keeps the exact POV", "SyntheticPawn", snapshot.povPawnId);
            AssertTrue("N3-I snapshot retains bounded belief topics",
                snapshot.topicTokens.Contains("body_modification"));

            IdeologyNarrativeSnapshot pageSnapshot = IdeologyNarrativeSnapshotFactory.ForPage(
                snapshot, "CanonicalEvent", 12345, "SyntheticPawn", "initiator");
            List<NarrativeLensCandidate> candidates = IdeologyNarrativeProvider.Build(
                new List<NarrativeEvidence> { pageSnapshot.sourceEvidence }, pageSnapshot);
            AssertEqual("N3-I creates exactly one candidate", 1, candidates.Count);
            AssertEqual("N3-I candidate uses the Ideology provider", NarrativeProviderTokens.Ideology,
                candidates[0].provider);
            AssertEqual("N3-I candidate consumes interpretation", NarrativeCategoryTokens.Interpretation,
                candidates[0].category);
            AssertEqual("N3-I key is stable and prose-free",
                "ideology|interpretation|SyntheticPawn|SyntheticIdeology|instance|"
                    + precept.instanceId,
                candidates[0].candidateKey);
            AssertEqual("N3-I candidate points at the canonical source page", "CanonicalEvent",
                candidates[0].sourceEventId);

            IdeologyNarrativeSnapshot repeated = IdeologyNarrativeSnapshotFactory.Create(
                resolved, eventEvidence.narrative, policy,
                "Localized labels may change without changing the key.");
            repeated = IdeologyNarrativeSnapshotFactory.ForPage(
                repeated, "CanonicalEvent", 12345, "SyntheticPawn", "initiator");
            AssertEqual("localized prose does not change the stable key", candidates[0].candidateKey,
                IdeologyNarrativeProvider.Build(
                    new List<NarrativeEvidence> { repeated.sourceEvidence }, repeated)[0].candidateKey);

            AssertTrue("empty stance resolution yields no N3-I snapshot",
                IdeologyNarrativeSnapshotFactory.Create(
                    new BeliefStanceResolution(), eventEvidence.narrative, policy, "unused") == null);
            AssertTrue("null source evidence yields no N3-I snapshot",
                IdeologyNarrativeSnapshotFactory.Create(resolved, null, policy, "unused") == null);
            AssertTrue("null policy uses safe defaults",
                IdeologyNarrativeSnapshotFactory.Create(
                    resolved, eventEvidence.narrative, null, "safe default policy") != null);
            BeliefStanceResolution mutationOnly = new BeliefStanceResolution
            {
                ideologyId = "SyntheticIdeology",
                ideologyName = "Synthetic Ideoligion",
                mutation = new BeliefMutationSnapshot
                {
                    pawnId = "SyntheticPawn", capturedTick = 12345, observedMutation = true,
                    certaintyChanged = true
                }
            };
            AssertTrue("mutation-only Phase-2 context is not promoted by N3-I",
                IdeologyNarrativeSnapshotFactory.Create(
                    mutationOnly, eventEvidence.narrative, policy, "unused") == null);

            BeliefStanceResolution ambiguous = new BeliefStanceResolution
            {
                ideologyId = "SyntheticIdeology",
                ideologyName = "Synthetic Ideoligion"
            };
            ambiguous.stances.Add(new ResolvedBeliefStance
            {
                precept = precept,
                relevanceTier = BeliefRelevanceTierTokens.GeneralText,
                confidenceScore = policy.minimumLexicalConfidence,
                runnerUpGap = policy.lexicalRunnerUpMargin - 0.01f
            });
            AssertTrue("ambiguous lexical result yields no N3-I snapshot",
                IdeologyNarrativeSnapshotFactory.Create(
                    ambiguous, eventEvidence.narrative, policy, "ambiguous") == null);
            ambiguous.stances[0].runnerUpGap = policy.lexicalRunnerUpMargin;
            ambiguous.stances[0].confidenceScore = policy.minimumLexicalConfidence - 0.01f;
            AssertTrue("below-confidence lexical result yields no N3-I snapshot even with a valid margin",
                IdeologyNarrativeSnapshotFactory.Create(
                    ambiguous, eventEvidence.narrative, policy, "below confidence") == null);
            ambiguous.stances[0].confidenceScore = policy.minimumLexicalConfidence;
            AssertTrue("threshold-and-margin lexical result may enter N3-I",
                IdeologyNarrativeSnapshotFactory.Create(
                    ambiguous, eventEvidence.narrative, policy, "high confidence") != null);

            resolved.narrativeCategory = NarrativeCategoryTokens.Home;
            AssertTrue("non-interpretation belief result cannot consume the N3-I slot",
                IdeologyNarrativeSnapshotFactory.Create(
                    resolved, eventEvidence.narrative, policy, "wrong category") == null);
            resolved.narrativeCategory = NarrativeCategoryTokens.Interpretation;
            precept.visible = false;
            AssertTrue("a hidden top stance is rejected directly by the N3-I factory",
                IdeologyNarrativeSnapshotFactory.Create(
                    resolved, eventEvidence.narrative, policy, "hidden stance") == null);
            precept.visible = true;

            AssertTrue("unknown facet yields no N3-I snapshot",
                IdeologyNarrativeSnapshotFactory.Create(
                    resolved, Evidence(true).narrative, policy, "unused") == null);
            NarrativeEvidence wrongPov = new NarrativeEvidence
            {
                eventId = pageSnapshot.sourceEvidence.eventId,
                tick = pageSnapshot.sourceEvidence.tick,
                povPawnId = "OtherPawn",
                povRole = pageSnapshot.sourceEvidence.povRole,
                facet = pageSnapshot.sourceEvidence.facet,
                phase = pageSnapshot.sourceEvidence.phase,
                subjectKind = pageSnapshot.sourceEvidence.subjectKind,
                subjectId = pageSnapshot.sourceEvidence.subjectId,
                pawnCanKnow = true,
                sourceDomain = pageSnapshot.sourceEvidence.sourceDomain,
                sourceDefName = pageSnapshot.sourceEvidence.sourceDefName
            };
            AssertEqual("wrong POV cannot use the snapshot", 0,
                IdeologyNarrativeProvider.Build(new List<NarrativeEvidence> { wrongPov }, pageSnapshot).Count);
            NarrativeEvidence hidden = CopyNarrative(pageSnapshot.sourceEvidence);
            bool? knowledge = hidden.pawnCanKnow;
            hidden.pawnCanKnow = false;
            AssertEqual("hidden evidence cannot use the snapshot", 0,
                IdeologyNarrativeProvider.Build(new List<NarrativeEvidence> { hidden }, pageSnapshot).Count);
            hidden.pawnCanKnow = knowledge;
            string sourceDef = hidden.sourceDefName;
            hidden.sourceDefName = "UnrelatedSource";
            AssertEqual("unrelated source cannot use the snapshot", 0,
                IdeologyNarrativeProvider.Build(new List<NarrativeEvidence> { hidden }, pageSnapshot).Count);
            hidden.sourceDefName = sourceDef;
            string validText = pageSnapshot.text;
            pageSnapshot.text = "malformed\nsecond line";
            AssertEqual("malformed provider prose yields no candidate", 0,
                IdeologyNarrativeProvider.Build(
                    new List<NarrativeEvidence> { pageSnapshot.sourceEvidence }, pageSnapshot).Count);
            pageSnapshot.text = validText;
            AssertTrue("wrong page POV cannot re-stamp a prepared snapshot",
                IdeologyNarrativeSnapshotFactory.ForPage(
                    snapshot, "CanonicalEvent", 12345, "OtherPawn", "initiator") == null);
            AssertTrue("wrong page tick cannot re-stamp a prepared snapshot",
                IdeologyNarrativeSnapshotFactory.ForPage(
                    snapshot, "CanonicalEvent", 12346, "SyntheticPawn", "initiator") == null);
            AssertTrue("wrong page role cannot re-stamp a prepared snapshot",
                IdeologyNarrativeSnapshotFactory.ForPage(
                    snapshot, "CanonicalEvent", 12345, "SyntheticPawn", "recipient") == null);
            AssertTrue("blank canonical page id cannot re-stamp a prepared snapshot",
                IdeologyNarrativeSnapshotFactory.ForPage(
                    snapshot, " ", 12345, "SyntheticPawn", "initiator") == null);

            string described = IdeologyInterpretationFactFormatter.Format(
                "Within {0}, {1}: {2}", "Within {0}, {1}.",
                "Synthetic Ideoligion", "Body respect", "The exception remains explicit.", 240);
            AssertEqual("N3-I formatter keeps one complete described fact",
                "Within Synthetic Ideoligion, Body respect: The exception remains explicit.", described);
            AssertEqual("N3-I formatter rejects a translation missing its description placeholder",
                string.Empty,
                IdeologyInterpretationFactFormatter.Format(
                    "Within {0}, {1}.", "Within {0}, {1}.",
                    "Synthetic Ideoligion", "Body respect", "Required description", 240));
            AssertEqual("N3-I formatter rejects an out-of-range placeholder",
                string.Empty,
                IdeologyInterpretationFactFormatter.Format(
                    "Within {0}, {1}: {3}", "Within {0}, {1}.",
                    "Synthetic Ideoligion", "Body respect", "Description", 240));
            AssertEqual("N3-I formatter rejects malformed braces",
                string.Empty,
                IdeologyInterpretationFactFormatter.Format(
                    "Within {0, {1}: {2}", "Within {0}, {1}.",
                    "Synthetic Ideoligion", "Body respect", "Description", 240));
            AssertEqual("overlong described fact falls back to a complete concise fact",
                "Within Synthetic Ideoligion, Body respect.",
                IdeologyInterpretationFactFormatter.Format(
                    "Within {0}, {1}: {2}", "Within {0}, {1}.",
                    "Synthetic Ideoligion", "Body respect", new string('x', 240), 80));
            AssertEqual("periods in names do not trigger assembled-sentence clipping",
                "Within St. Synthetic v1.3, Body v2.0: Complete meaning.",
                IdeologyInterpretationFactFormatter.Format(
                    "Within {0}, {1}: {2}", "Within {0}, {1}.",
                    "St. Synthetic v1.3", "Body v2.0", "Complete meaning.", 240));

            BeliefPreceptFact otherPrecept = Precept(
                "SyntheticOther", "SyntheticOtherIssue", "Another current view", 1);
            BeliefSnapshot historySnapshot = Snapshot(precept, otherPrecept);
            List<string> recentKeys = new List<string>
            {
                "odyssey|home|ship|place",
                "ideology|interpretation|SyntheticPawn|SyntheticIdeology|instance|" + precept.instanceId,
                "ideology|interpretation|SyntheticPawn|SyntheticIdeology|def|" + otherPrecept.defName,
                "ideology|interpretation|OtherPawn|SyntheticIdeology|def|Ignored",
                "ideology|interpretation|SyntheticPawn|OldIdeology|def|Ignored"
            };
            List<string> recentDefNames = IdeologyNarrativeSelectionHistory.PreceptDefNames(
                recentKeys, historySnapshot, policy.maximumRecentSelections);
            AssertEqual("saved N3-I keys recover two current precept DefNames", 2, recentDefNames.Count);
            AssertEqual("instance key resolves through current doctrine", precept.defName, recentDefNames[0]);
            AssertEqual("def key resolves only when still present", otherPrecept.defName, recentDefNames[1]);
            AssertEqual("recent precept projection honors its requested result cap", 1,
                IdeologyNarrativeSelectionHistory.PreceptDefNames(recentKeys, historySnapshot, 1).Count);

            BeliefPreceptFact quietHistoryA = Precept(
                "HistoryQuiet_A", "HistoryQuiet_Issue_A", "quiet history a", 3);
            BeliefPreceptFact quietHistoryB = Precept(
                "HistoryQuiet_B", "HistoryQuiet_Issue_B", "quiet history b", 3);
            BeliefSnapshot quietHistorySnapshot = Snapshot(quietHistoryA, quietHistoryB);
            BeliefEventEvidence quietHistoryEvidence = Evidence(true);
            BeliefStanceResolution quietHistoryInitial = Resolve(
                quietHistorySnapshot,
                quietHistoryEvidence,
                BeliefPolicySnapshot.CreateDefault(),
                BeliefResolutionModeTokens.QuietReflection,
                9);
            string savedQuietKey = "ideology|interpretation|SyntheticPawn|SyntheticIdeology|instance|"
                + quietHistoryInitial.stances[0].precept.instanceId;
            List<string> projectedQuietHistory = IdeologyNarrativeSelectionHistory.PreceptDefNames(
                new List<string> { savedQuietKey }, quietHistorySnapshot, 1);
            BeliefStanceResolution quietHistoryRepeated = Resolve(
                quietHistorySnapshot,
                quietHistoryEvidence,
                BeliefPolicySnapshot.CreateDefault(),
                BeliefResolutionModeTokens.QuietReflection,
                9,
                projectedQuietHistory);
            AssertTrue("saved N3-I key activates resolver-level doctrine diversity",
                quietHistoryRepeated.stances[0].precept.defName
                    != quietHistoryInitial.stances[0].precept.defName);
            historySnapshot.ideologyId = "ChangedIdeology";
            AssertEqual("ideology change naturally resets N3-I resolver repetition", 0,
                IdeologyNarrativeSelectionHistory.PreceptDefNames(
                    recentKeys, historySnapshot, policy.maximumRecentSelections).Count);

            BeliefEventEvidence thought = BeliefEventEvidenceFactory.ForThought(
                "SyntheticPawn", 5, "ThoughtDef", "Thought", null);
            BeliefEventEvidence body = BeliefEventEvidenceFactory.ForBodyModification(
                "SyntheticPawn", 5, "HediffDef", "Implant", "arm", "artificial", "simple");
            AssertEqual("Phase-1 thought evidence owns an ambient-pressure facet",
                NarrativeFacetTokens.AmbientPressure, thought.narrative.facet);
            AssertEqual("Phase-1 body-mod evidence owns an identity-transition facet",
                NarrativeFacetTokens.IdentityTransition, body.narrative.facet);
        }

        private static void TestMissingInactiveEmptyAndKnowledgeGates()
        {
            BeliefPolicySnapshot policy = BeliefPolicySnapshot.CreateDefault();
            AssertEmpty("null request fails closed", EventRelativeStanceResolver.Resolve(null));
            AssertEmpty("missing snapshot fails closed", EventRelativeStanceResolver.Resolve(new BeliefResolutionRequest
            {
                evidence = Evidence(true), policy = policy
            }));

            BeliefSnapshot inactive = Snapshot(Precept("A", "IssueA", "alpha doctrine", 1));
            inactive.ideologyActive = false;
            AssertEmpty("inactive Ideology fails closed", Resolve(inactive, TextEvidence(true, "alpha doctrine"), policy));
            BeliefSnapshot emptyIdeology = Snapshot(Precept("A", "IssueA", "alpha doctrine", 1));
            emptyIdeology.ideologyId = string.Empty;
            AssertEmpty("empty ideoligion fails closed", Resolve(emptyIdeology, TextEvidence(true, "alpha doctrine"), policy));

            BeliefSnapshot live = Snapshot(Precept("A", "IssueA", "alpha doctrine", 1));
            AssertEmpty("unknown POV knowledge fails closed", Resolve(live, TextEvidence(null, "alpha doctrine"), policy));
            AssertEmpty("false POV knowledge fails closed", Resolve(live, TextEvidence(false, "alpha doctrine"), policy));
            AssertEmpty("empty event enrichment evidence fails closed", Resolve(live, Evidence(true), policy));
            AssertEmpty("hidden precept remains unavailable", Resolve(
                Snapshot(Precept("Hidden", "HiddenIssue", "hidden doctrine", 3, visible: false)),
                SourceEvidence("hidden-instance", "Hidden"), policy));
        }

        private static void TestSourcePreceptIdentityPrecedence()
        {
            BeliefPreceptFact first = Precept("Synthetic_First", "Issue_First", "first doctrine", 3);
            first.instanceId = "instance-first";
            first.correlations.Add(Correlation(BeliefCorrelationKindTokens.Thought, "Thought_First",
                BeliefValenceTokens.Positive));
            BeliefPreceptFact second = Precept("Synthetic_Second", "Issue_Second", "second doctrine", 0);
            second.instanceId = "instance-second";
            BeliefSnapshot snapshot = Snapshot(first, second);

            BeliefEventEvidence evidence = SourceEvidence("instance-second", "Synthetic_Second");
            evidence.thoughtDefNames.Add("Thought_First");
            evidence.issueDefNames.Add("Issue_First");
            evidence.matchFields.Add(Text("event_label", "first doctrine"));
            BeliefStanceResolution resolved = Resolve(snapshot, evidence, BeliefPolicySnapshot.CreateDefault());
            AssertSelected("source instance wins over every other signal", resolved, "Synthetic_Second");
            AssertEqual("source instance reason", BeliefRelevanceSourceTokens.SourcePrecept,
                resolved.stances[0].relevanceSource);

            BeliefEventEvidence exactWithThought = SourceEvidence("instance-first", "Synthetic_First");
            exactWithThought.thoughtDefNames.Add("Thought_First");
            BeliefStanceResolution exactWithValence = Resolve(snapshot, exactWithThought,
                BeliefPolicySnapshot.CreateDefault());
            AssertEqual("exact source keeps its matching thought's mechanical valence",
                BeliefValenceTokens.Positive, exactWithValence.stances[0].correlationValence);

            BeliefEventEvidence fallback = SourceEvidence(string.Empty, "Synthetic_First");
            AssertSelected("unique source def fallback", Resolve(snapshot, fallback,
                BeliefPolicySnapshot.CreateDefault()), "Synthetic_First");

            BeliefPreceptFact duplicate = Precept("Synthetic_First", "Issue_Duplicate", "duplicate", 3);
            duplicate.instanceId = "instance-duplicate";
            AssertEmpty("duplicate def fallback is ambiguous", Resolve(Snapshot(first, duplicate), fallback,
                BeliefPolicySnapshot.CreateDefault()));
            AssertEmpty("source identity cannot select doctrine absent from live snapshot", Resolve(snapshot,
                SourceEvidence("missing-instance", "Missing_Precept"), BeliefPolicySnapshot.CreateDefault()));
        }

        private static void TestThoughtHistoryAndStructuralPrecedence()
        {
            BeliefPreceptFact thought = Precept("Mod_ThoughtStance", "Issue_Thought", "quiet modest custom stance", 0);
            thought.correlations.Add(Correlation(BeliefCorrelationKindTokens.Thought, "Modded_Thought_X9",
                BeliefValenceTokens.Negative));
            BeliefPreceptFact history = Precept("Mod_HistoryStance", "Issue_History", "archive custom stance", 0);
            history.correlations.Add(Correlation(BeliefCorrelationKindTokens.HistoryEvent, "Modded_History_Y7",
                BeliefValenceTokens.Positive));
            BeliefPreceptFact loud = Precept("Unrelated_HighImpact", "Issue_Unrelated", "modded thought x9", 3);
            BeliefSnapshot snapshot = Snapshot(thought, history, loud);

            BeliefEventEvidence thoughtEvidence = Evidence(true);
            thoughtEvidence.thoughtDefNames.Add("Modded_Thought_X9");
            thoughtEvidence.issueDefNames.Add("Issue_Unrelated");
            thoughtEvidence.matchFields.Add(Text("event_label", "modded thought x9"));
            BeliefStanceResolution thoughtResult = Resolve(snapshot, thoughtEvidence,
                BeliefPolicySnapshot.CreateDefault());
            AssertSelected("exact thought correlation beats identity/text/impact", thoughtResult, "Mod_ThoughtStance");
            AssertEqual("thought relevance source", BeliefRelevanceSourceTokens.ThoughtCorrelation,
                thoughtResult.stances[0].relevanceSource);
            AssertEqual("thought valence stays mechanical", BeliefValenceTokens.Negative,
                thoughtResult.stances[0].correlationValence);

            BeliefEventEvidence historyEvidence = Evidence(true);
            historyEvidence.historyEventDefNames.Add("Modded_History_Y7");
            BeliefStanceResolution historyResult = Resolve(snapshot, historyEvidence,
                BeliefPolicySnapshot.CreateDefault());
            AssertSelected("arbitrary mod history correlation resolves", historyResult, "Mod_HistoryStance");
            AssertEqual("history relevance source", BeliefRelevanceSourceTokens.HistoryCorrelation,
                historyResult.stances[0].relevanceSource);
            AssertEqual("history valence stays mechanical", BeliefValenceTokens.Positive,
                historyResult.stances[0].correlationValence);

            AssertTrue("lexical prose never creates polarity",
                Resolve(snapshot, TextEvidence(true, "quiet modest custom stance"),
                    BeliefPolicySnapshot.CreateDefault()).stances[0].correlationValence
                    == BeliefValenceTokens.Unknown);
        }

        private static void TestIssueAndMemeIdentityTiers()
        {
            BeliefPreceptFact issue = Precept("Exact_Issue_Stance", "Synthetic_Issue_404", "unassuming", 0);
            BeliefPreceptFact unrelated = Precept("Unrelated_Strong", "Other_Issue", "synthetic issue 404", 3);
            BeliefEventEvidence issueEvidence = Evidence(true);
            issueEvidence.issueDefNames.Add("Synthetic_Issue_404");
            BeliefStanceResolution issueResult = Resolve(Snapshot(issue, unrelated), issueEvidence,
                BeliefPolicySnapshot.CreateDefault());
            AssertSelected("direct issue identity beats unrelated high impact", issueResult, "Exact_Issue_Stance");
            AssertEqual("issue identity tier", BeliefRelevanceTierTokens.DirectIdentity,
                issueResult.stances[0].relevanceTier);

            BeliefMemeFact meme = Meme("Synthetic_Meme", "Synthetic worldview", 2);
            BeliefPreceptFact linked = Precept("Meme_Linked_Stance", "Meme_Issue", "linked stance", 1);
            linked.associatedMemeDefNames.Add("Synthetic_Meme");
            BeliefSnapshot withMeme = Snapshot(linked);
            withMeme.memes.Add(meme);
            BeliefEventEvidence memeEvidence = Evidence(true);
            memeEvidence.memeDefNames.Add("Synthetic_Meme");
            BeliefStanceResolution memeResult = Resolve(withMeme, memeEvidence,
                BeliefPolicySnapshot.CreateDefault());
            AssertSelected("direct live meme association selects linked precept", memeResult, "Meme_Linked_Stance");
            AssertEqual("supporting meme selected once", 1, memeResult.supportingMemes.Count);

            BeliefSnapshot memeOnly = Snapshot();
            memeOnly.memes.Add(meme);
            BeliefStanceResolution memeOnlyResult = Resolve(memeOnly, memeEvidence,
                BeliefPolicySnapshot.CreateDefault());
            AssertEqual("direct live meme can be sole doctrinal result", 0, memeOnlyResult.stances.Count);
            AssertEqual("sole direct meme remains useful", 1, memeOnlyResult.supportingMemes.Count);
            BeliefSnapshot memeBeatsLexical = Snapshot(
                Precept("Lexical_Unrelated", "Lexical_Issue", "tempting lexical doctrine", 3));
            memeBeatsLexical.memes.Add(meme);
            BeliefEventEvidence memeAndText = Evidence(true);
            memeAndText.memeDefNames.Add("Synthetic_Meme");
            memeAndText.matchFields.Add(Text("event_label", "tempting lexical doctrine"));
            BeliefStanceResolution memePrecedence = Resolve(memeBeatsLexical, memeAndText,
                BeliefPolicySnapshot.CreateDefault());
            AssertEqual("direct meme identity prevents unrelated lexical fallback", 0,
                memePrecedence.stances.Count);
            AssertEqual("direct meme identity remains selected", 1, memePrecedence.supportingMemes.Count);
            AssertTrue("meme absent from live doctrine returns empty",
                !Resolve(Snapshot(), memeEvidence, BeliefPolicySnapshot.CreateDefault()).HasUsefulContext);
        }

        private static void TestLexicalNormalizationAndGuardedMatches()
        {
            string normalized = BeliefLexicalMatcher.Normalize(
                "<b>Body_Modification</b>\r\n  ÉLAN", 100, 10);
            AssertEqual("markup/case/underscore/whitespace normalization", "body modification élan", normalized);
            AssertEqual("CamelCase Def normalization", "organ use history event",
                BeliefLexicalMatcher.Normalize("OrganUseHistoryEvent", 100, 10));

            BeliefPolicySnapshot policy = VocabularyPolicy();
            BeliefPreceptFact phrase = Precept("Synthetic_Cannibal", "Issue_Cannibal", "human meat", 1);
            BeliefPreceptFact tokens = Precept("Synthetic_Other", "Issue_Other", "human crops", 3);
            BeliefEventEvidence eventEvidence = Evidence(true);
            eventEvidence.narrative.beliefTopics.Add("cannibalism");
            BeliefStanceResolution result = Resolve(Snapshot(phrase, tokens), eventEvidence, policy);
            AssertSelected("semantic alias exact phrase wins", result, "Synthetic_Cannibal");
            AssertEqual("phrase relevance diagnostic", BeliefRelevanceSourceTokens.LexicalPhrase,
                result.stances[0].relevanceSource);

            BeliefPreceptFact twoTokens = Precept("Synthetic_TwoTokens", "Issue_TwoTokens",
                "luminous orchard stewardship", 1);
            BeliefEventEvidence tokenEvidence = TextEvidence(true, "orchard luminous gathering");
            BeliefStanceResolution tokenResult = Resolve(Snapshot(twoTokens), tokenEvidence, policy);
            AssertSelected("two distinctive tokens qualify", tokenResult, "Synthetic_TwoTokens");
            AssertEqual("token relevance diagnostic", BeliefRelevanceSourceTokens.LexicalTokens,
                tokenResult.stances[0].relevanceSource);

            BeliefPreceptFact unique = Precept("Synthetic_Unique", "Issue_Unique", "xenoharvesting", 1);
            BeliefEventEvidence uniqueEvidence = Evidence(true);
            uniqueEvidence.matchFields.Add(Text("ingredient_label", "xenoharvesting"));
            AssertSelected("one sufficiently long unique token qualifies at boundary",
                Resolve(Snapshot(unique), uniqueEvidence, policy), "Synthetic_Unique");
        }

        private static void TestLexicalCommonConfidenceAndAmbiguityRejection()
        {
            BeliefPolicySnapshot policy = VocabularyPolicy();
            BeliefPreceptFact first = Precept("Common_A", "Issue_A", "sacred ritual", 1);
            BeliefPreceptFact second = Precept("Common_B", "Issue_B", "sacred ritual", 3);
            AssertEmpty("all-common phrase/tokens are suppressed", Resolve(Snapshot(first, second),
                TextEvidence(true, "sacred ritual"), policy));

            AssertEmpty("single generic token is rejected", Resolve(
                Snapshot(Precept("Generic", "Issue_Generic", "sacred", 3)),
                TextEvidence(true, "sacred"), policy));
            AssertEmpty("configured schema-token exclusion is rejected", Resolve(
                Snapshot(Precept("Schema", "Issue_Schema", "belief", 3)),
                TextEvidence(true, "belief"), policy));

            BeliefPolicyBuilder highThreshold = BeliefPolicyBuilder.CreateDefault();
            highThreshold.minimumLexicalConfidence = 500f;
            BeliefPolicySnapshot highThresholdPolicy = highThreshold.Build();
            BeliefSnapshot belowSnapshot = Snapshot(
                Precept("Below", "Issue_Below", "distinctive orchard", 1));
            BeliefEventEvidence belowEvidence = TextEvidence(true, "distinctive orchard");
            BeliefLexicalMatchResult belowResult = BeliefLexicalMatcher.Match(
                belowEvidence, belowSnapshot, belowSnapshot.precepts, highThresholdPolicy);
            AssertTrue("below minimum confidence exposes its diagnostic", belowResult.rejectedBelowConfidence);
            AssertTrue("below minimum confidence exposes no winner", belowResult.winner == null);
            AssertEmpty("below minimum confidence is rejected", Resolve(
                belowSnapshot, belowEvidence, highThresholdPolicy));

            BeliefPolicyBuilder ambiguity = BeliefPolicyBuilder.CreateDefault();
            ambiguity.commonTokenDocumentFraction = 1f;
            ambiguity.lexicalRunnerUpMargin = 1f;
            BeliefSnapshot tied = Snapshot(
                Precept("Tie_A", "Issue_Tie_A", "crimson doctrine", 1),
                Precept("Tie_B", "Issue_Tie_B", "crimson doctrine", 1),
                Precept("Tie_C", "Issue_Tie_C", "azure orchard", 1));
            BeliefLexicalMatchResult tiedResult = BeliefLexicalMatcher.Match(
                TextEvidence(true, "crimson doctrine"), tied, tied.precepts, ambiguity.Build());
            AssertTrue("near runner-up is explicitly ambiguous", tiedResult.rejectedAsAmbiguous);
            AssertTrue("ambiguous matcher exposes no winner", tiedResult.winner == null);
            AssertEmpty("resolver remains silent on lexical tie", Resolve(tied,
                TextEvidence(true, "crimson doctrine"), ambiguity.Build()));

            // Mirror the loaded RimTest fixture with the shipped/default thresholds. Two long unique
            // tokens per candidate clear the 65-point floor equally, so rejection must happen at the
            // runner-up guard rather than the earlier below-confidence guard.
            BeliefPreceptFact loadedFirst = Precept(
                "FixtureDoctrineA91", "FixtureIssueA91", "obsidian orchards covenant mercy", 1);
            BeliefPreceptFact loadedSecond = Precept(
                "FixtureDoctrineB72", "FixtureIssueB72", "goldleaf lanterns covenant mercy", 1);
            BeliefSnapshot loadedTie = Snapshot(loadedFirst, loadedSecond);
            BeliefEventEvidence loadedEvidence = TextEvidence(
                true, "obsidian orchards covenant mercy goldleaf lanterns");
            BeliefStanceResolution loadedResolution = Resolve(
                loadedTie, loadedEvidence, BeliefPolicySnapshot.CreateDefault());
            AssertEmpty("shipped-threshold lexical tie remains silent", loadedResolution);
            AssertEqual("shipped-threshold tie reports ambiguous",
                BeliefAutomaticCoverageOutcomeTokens.Ambiguous,
                loadedResolution.automaticCoverage.outcome);
            AssertEqual("shipped-threshold tie reports runner-up ambiguity",
                BeliefAutomaticCoverageReasonTokens.RunnerUpAmbiguity,
                loadedResolution.automaticCoverage.reason);
            AssertEqual("shipped-threshold tie retains both candidates", 2,
                loadedResolution.automaticCoverage.candidateCount);
        }

        private static void TestLexicalFuzzyAndUnknownTopicBehavior()
        {
            BeliefPolicyBuilder fuzzyBuilder = BeliefPolicyBuilder.CreateDefault();
            fuzzyBuilder.fuzzySimilarityMinimum = 0.72f;
            fuzzyBuilder.fuzzyMatchScore = 30f;
            fuzzyBuilder.minimumLexicalConfidence = 55f;
            fuzzyBuilder.fuzzyRunnerUpMargin = 20f;
            BeliefPolicySnapshot fuzzyPolicy = fuzzyBuilder.Build();
            BeliefPreceptFact fuzzy = Precept("Fuzzy_Target", "Fuzzy_Issue",
                "cybernetiks transhumanizm", 1);
            BeliefStanceResolution fuzzyResult = Resolve(Snapshot(fuzzy),
                TextEvidence(true, "cybernetics transhumanism"), fuzzyPolicy);
            AssertSelected("two conservative fuzzy tokens qualify", fuzzyResult, "Fuzzy_Target");
            AssertEqual("fuzzy diagnostic", BeliefRelevanceSourceTokens.LexicalFuzzy,
                fuzzyResult.stances[0].relevanceSource);
            AssertEmpty("one fuzzy token remains insufficient", Resolve(
                Snapshot(Precept("Fuzzy_Weak", "Fuzzy_Weak_Issue", "cybernetiks", 3)),
                TextEvidence(true, "cybernetics"), fuzzyPolicy));

            BeliefPreceptFact future = Precept("Future_Mod_Precept", "Future_Issue",
                "quasar husbandry", 1);
            BeliefEventEvidence unknown = Evidence(true);
            unknown.narrative.beliefTopics.Add("quasar_husbandry");
            AssertSelected("unknown safe topic can match future mod text without code changes",
                Resolve(Snapshot(future), unknown, BeliefPolicySnapshot.CreateDefault()), "Future_Mod_Precept");
            BeliefSnapshot futureSnapshot = Snapshot(future);
            ExpandedBeliefEvidence preExpanded = new ExpandedBeliefEvidence();
            preExpanded.topics.Add("quasar_husbandry");
            BeliefLexicalMatchResult reusedExpansion = BeliefLexicalMatcher.Match(
                Evidence(true), futureSnapshot, futureSnapshot.precepts,
                BeliefPolicySnapshot.CreateDefault(), preExpanded);
            AssertEqual("resolver-owned expansion can be reused without recomputation", "Future_Mod_Precept",
                reusedExpansion.winner == null ? string.Empty : reusedExpansion.winner.precept.defName);
            BeliefEventEvidence unrelatedUnknown = Evidence(true);
            unrelatedUnknown.narrative.beliefTopics.Add("totally_unknown_topic");
            AssertEmpty("unknown topic with no live match is a no-op",
                Resolve(Snapshot(future), unrelatedUnknown, BeliefPolicySnapshot.CreateDefault()));
        }

        private static void TestBodyOrganMealRaidAndRitualScenarios()
        {
            BeliefPolicySnapshot policy = VocabularyPolicy();

            BeliefPreceptFact bodyApprove = Precept("Body_Approve", "Body_Issue", "prosthetic acceptance", 3);
            bodyApprove.correlations.Add(Correlation(BeliefCorrelationKindTokens.HistoryEvent,
                "SyntheticBodyOperation", BeliefValenceTokens.Positive));
            BeliefPreceptFact bodyDespise = Precept("Body_Despise", "Body_Issue", "prosthetic rejection", 0);
            bodyDespise.correlations.Add(Correlation(BeliefCorrelationKindTokens.HistoryEvent,
                "SyntheticBodyOperation", BeliefValenceTokens.Negative));
            BeliefEventEvidence bodyEvidence = GroupEvidence("medical", "body_modification");
            bodyEvidence.historyEventDefNames.Add("SyntheticBodyOperation");
            BeliefStanceResolution bodyResult = Resolve(Snapshot(bodyApprove, bodyDespise), bodyEvidence, policy);
            AssertSelected("body modification keeps despise precedence on reliable conflict",
                bodyResult, "Body_Despise");
            AssertEqual("body mechanical polarity retained", BeliefValenceTokens.Negative,
                bodyResult.stances[0].correlationValence);

            BeliefPreceptFact organ = Precept("Organ_Stance", "Organ_Issue", "organ harvesting", 1);
            AssertSelected("organ-use vocabulary selects matching live stance",
                Resolve(Snapshot(organ), GroupEvidence("medical", "organ_use"), policy), "Organ_Stance");

            BeliefPreceptFact cannibal = Precept("Cannibal_Stance", "Cannibal_Issue", "human meat", 1);
            BeliefPreceptFact slavery = Precept("Slavery_Unrelated", "Slavery_Issue", "slavery bondage", 3);
            AssertSelected("known cannibal meal ignores stronger unrelated doctrine",
                Resolve(Snapshot(cannibal, slavery), GroupEvidence("food", "cannibal_meal"), policy),
                "Cannibal_Stance");
            AssertEmpty("generic meal does not invent cannibal ingredients",
                Resolve(Snapshot(cannibal), GroupEvidence("food", "meal"), policy));
            BeliefEventEvidence knownIngredient = GroupEvidence("food", "meal");
            knownIngredient.matchFields.Add(Text("ingredient_label", "human meat"));
            AssertSelected("known meal ingredient can resolve cannibalism",
                Resolve(Snapshot(cannibal), knownIngredient, policy), "Cannibal_Stance");

            ExpandedBeliefEvidence raidExpanded = BeliefEventEvidencePolicy.Expand(
                GroupEvidence("combat", "raid"), policy);
            AssertTrue("raid adds violence", Contains(raidExpanded.topics, "violence"));
            AssertTrue("raid never invents charity", !Contains(raidExpanded.topics, "charity"));
            BeliefPreceptFact violence = Precept("Violence_Stance", "Violence_Issue", "violence", 1);
            BeliefPreceptFact charity = Precept("Charity_Stance", "Charity_Issue", "charity aid", 3);
            AssertSelected("raid selects violence rather than charity",
                Resolve(Snapshot(violence, charity), GroupEvidence("combat", "raid"), policy), "Violence_Stance");

            BeliefPreceptFact ritual = Precept("Ritual_Stance", "Ritual_Issue", "ritual ceremony", 1);
            AssertSelected("visible ritual vocabulary resolves matching doctrine",
                Resolve(Snapshot(ritual), GroupEvidence("ritual", "ritual"), policy), "Ritual_Stance");
        }

        private static void TestLiveDoctrineIntersectionRedundancyAndCaps()
        {
            BeliefPolicySnapshot policy = BeliefPolicySnapshot.CreateDefault();
            BeliefPreceptFact variantA = Precept("Variant_A", "Shared_Issue", "variant a", 3);
            variantA.correlations.Add(Correlation(BeliefCorrelationKindTokens.Thought, "SharedThought",
                BeliefValenceTokens.Positive));
            BeliefPreceptFact variantB = Precept("Variant_B", "Shared_Issue", "variant b", 0);
            variantB.correlations.Add(Correlation(BeliefCorrelationKindTokens.Thought, "SharedThought",
                BeliefValenceTokens.Negative));
            BeliefEventEvidence shared = Evidence(true);
            shared.thoughtDefNames.Add("SharedThought");
            BeliefStanceResolution collapsed = Resolve(Snapshot(variantA, variantB), shared, policy);
            AssertEqual("same-issue variants collapse", 1, collapsed.stances.Count);
            AssertSelected("same evidence uses reliable negative precedence", collapsed, "Variant_B");

            BeliefPreceptFact first = CorrelatedPrecept("First", "Issue_First", "Thought_First");
            BeliefPreceptFact second = CorrelatedPrecept("Second", "Issue_Second", "Thought_Second");
            BeliefPreceptFact third = CorrelatedPrecept("Third", "Issue_Third", "Thought_Third");
            BeliefEventEvidence three = Evidence(true);
            three.thoughtDefNames.AddRange(new[] { "Thought_First", "Thought_Second", "Thought_Third" });
            BeliefStanceResolution capped = Resolve(Snapshot(first, second, third), three, policy);
            AssertEqual("resolver hard-caps at two", 2, capped.stances.Count);
            AssertTrue("two selected stances have separate issues",
                capped.stances[0].precept.issue.defName != capped.stances[1].precept.issue.defName);

            BeliefEventEvidence absent = Evidence(true);
            absent.thoughtDefNames.Add("Thought_Not_Live");
            AssertEmpty("correlation cannot escape live-doctrine intersection",
                Resolve(Snapshot(first), absent, policy));
        }

        private static void TestSecondSlotIndependenceOrderingAndRepetition()
        {
            BeliefPreceptFact first = CorrelatedPrecept("SecondSlot_A", "SecondSlot_Issue_A", "Fact_A");
            BeliefPreceptFact second = CorrelatedPrecept("SecondSlot_B", "SecondSlot_Issue_B", "Fact_B");
            BeliefEventEvidence evidence = Evidence(true);
            evidence.thoughtDefNames.AddRange(new[] { "Fact_A", "Fact_B" });
            BeliefStanceResolution two = Resolve(Snapshot(first, second), evidence,
                BeliefPolicySnapshot.CreateDefault(), seed: 77);
            AssertEqual("independent structural facts permit second slot", 2, two.stances.Count);
            AssertTrue("second slot records independent evidence",
                two.stances[0].independentEvidenceKey != two.stances[1].independentEvidenceKey);

            BeliefPolicyBuilder strictSecond = BeliefPolicyBuilder.CreateDefault();
            strictSecond.secondSlotMinimumScore = 2000f;
            AssertEqual("independent second-slot threshold is enforced", 1,
                Resolve(Snapshot(first, second), evidence, strictSecond.Build()).stances.Count);
            strictSecond.defaultSelectedStances = 2;
            AssertEqual("default-two policy admits a second independent stance without the exceptional threshold", 2,
                Resolve(Snapshot(first, second), evidence, strictSecond.Build()).stances.Count);

            BeliefStanceResolution deterministicA = Resolve(Snapshot(first, second), evidence,
                BeliefPolicySnapshot.CreateDefault(), seed: 1942);
            BeliefStanceResolution deterministicB = Resolve(Snapshot(second, first), evidence,
                BeliefPolicySnapshot.CreateDefault(), seed: 1942);
            AssertEqual("input ordering cannot change first selection",
                deterministicA.stances[0].precept.defName, deterministicB.stances[0].precept.defName);
            AssertEqual("input ordering cannot change second selection",
                deterministicA.stances[1].precept.defName, deterministicB.stances[1].precept.defName);

            BeliefPreceptFact quietA = Precept("Quiet_A", "Quiet_Issue_A", "quiet a", 3);
            BeliefPreceptFact quietB = Precept("Quiet_B", "Quiet_Issue_B", "quiet b", 3);
            BeliefEventEvidence quietEvidence = Evidence(true);
            HashSet<string> diverse = new HashSet<string>(StringComparer.Ordinal);
            for (int seed = 0; seed < 64; seed++)
                diverse.Add(Resolve(Snapshot(quietA, quietB), quietEvidence,
                    BeliefPolicySnapshot.CreateDefault(), BeliefResolutionModeTokens.QuietReflection,
                    seed).stances[0].precept.defName);
            AssertTrue("different deterministic seeds can diversify equal relevant doctrine", diverse.Count > 1);
            BeliefStanceResolution initial = Resolve(Snapshot(quietA, quietB), quietEvidence,
                BeliefPolicySnapshot.CreateDefault(), BeliefResolutionModeTokens.QuietReflection, 9);
            AssertEqual("quiet fallback normally selects one", 1, initial.stances.Count);
            List<string> recent = new List<string> { initial.stances[0].precept.defName };
            BeliefStanceResolution repeated = Resolve(Snapshot(quietA, quietB), quietEvidence,
                BeliefPolicySnapshot.CreateDefault(), BeliefResolutionModeTokens.QuietReflection, 9, recent);
            AssertTrue("repetition penalty prefers fresh quiet doctrine",
                repeated.stances[0].precept.defName != initial.stances[0].precept.defName);
            AssertEmpty("ordinary event enrichment never uses quiet fallback",
                Resolve(Snapshot(quietA, quietB), quietEvidence, BeliefPolicySnapshot.CreateDefault()));

            BeliefPreceptFact unrelatedHigh = Precept("Unrelated_High", "Unrelated_High_Issue", "night darkness", 3);
            unrelatedHigh.requiredByCurrentMeme = true;
            AssertEmpty("unrelated high-impact/required doctrine remains silent",
                Resolve(Snapshot(unrelatedHigh), TextEvidence(true, "shared lunch"),
                    BeliefPolicySnapshot.CreateDefault()));

            BeliefPreceptFact role = CorrelatedPrecept("Role_Fit", "Role_Issue", "RoleFact");
            role.proselytizes = true;
            BeliefPreceptFact ordinary = CorrelatedPrecept("Role_Ordinary", "Role_Other", "RoleFact");
            BeliefEventEvidence roleEvidence = Evidence(true);
            roleEvidence.narrative.povRole = "converter";
            roleEvidence.thoughtDefNames.Add("RoleFact");
            AssertSelected("role fit only reorders already-correlated doctrine",
                Resolve(Snapshot(role, ordinary), roleEvidence, BeliefPolicySnapshot.CreateDefault()), "Role_Fit");
        }

        private static void TestCertaintyBoundariesAndTrends()
        {
            BeliefPolicySnapshot policy = BeliefPolicySnapshot.CreateDefault();
            AssertEqual("below conflicted boundary", "doubtful", BeliefCertaintyPolicy.BandFor(0.149f, policy).token);
            AssertEqual("at conflicted boundary", "conflicted", BeliefCertaintyPolicy.BandFor(0.15f, policy).token);
            AssertEqual("below uneasy boundary", "conflicted", BeliefCertaintyPolicy.BandFor(0.349f, policy).token);
            AssertEqual("at uneasy boundary", "uneasy", BeliefCertaintyPolicy.BandFor(0.35f, policy).token);
            AssertEqual("below confident boundary", "uneasy", BeliefCertaintyPolicy.BandFor(0.599f, policy).token);
            AssertEqual("at confident boundary", "confident", BeliefCertaintyPolicy.BandFor(0.60f, policy).token);
            AssertEqual("below fervent boundary", "confident", BeliefCertaintyPolicy.BandFor(0.849f, policy).token);
            AssertEqual("at fervent boundary", "fervent", BeliefCertaintyPolicy.BandFor(0.85f, policy).token);
            AssertEqual("above fervent boundary", "fervent", BeliefCertaintyPolicy.BandFor(1.2f, policy).token);

            string trend;
            string magnitude;
            BeliefCertaintyPolicy.Trend(0.5f, 0.5f, policy, out trend, out magnitude);
            AssertEqual("zero delta stable", BeliefCertaintyTrendTokens.Stable, trend);
            AssertEqual("zero delta minor", BeliefCertaintyMagnitudeTokens.Minor, magnitude);
            BeliefCertaintyPolicy.Trend(0.50f, 0.55f, policy, out trend, out magnitude);
            AssertEqual("meaningful boundary rises", BeliefCertaintyTrendTokens.Rising, trend);
            AssertEqual("meaningful boundary class", BeliefCertaintyMagnitudeTokens.Meaningful, magnitude);
            BeliefCertaintyPolicy.Trend(0.60f, 0.45f, policy, out trend, out magnitude);
            AssertEqual("major boundary falls", BeliefCertaintyTrendTokens.Falling, trend);
            AssertEqual("major boundary class", BeliefCertaintyMagnitudeTokens.Major, magnitude);
        }

        private static void TestFormatterBudgetsSanitationAndWorldviewFacts()
        {
            BeliefPolicyBuilder builder = BeliefPolicyBuilder.CreateDefault();
            builder.certaintyBands.Clear();
            builder.certaintyBands.Add(new BeliefCertaintyBand("steady", 0f, "XML-owned certainty phrase"));
            BeliefPolicySnapshot policy = builder.Build();
            BeliefPreceptFact precept = Precept("Format_Precept", "Format_Issue", "<b>Visible stance</b>", 1);
            precept.description = "First line\nsecond line; hostile <i>tag</i> tail";
            BeliefStanceResolution resolution = new BeliefStanceResolution
            {
                ideologyName = "The <b>Quiet</b> Flame",
                roleName = "Guide\nInjected",
                hasCertainty = true,
                certainty = 0.62f,
                certaintyBand = "steady",
                certaintyPhrase = "XML-owned certainty phrase",
                certaintyTrend = BeliefCertaintyTrendTokens.Rising,
                certaintyMagnitude = BeliefCertaintyMagnitudeTokens.Meaningful,
                structure = Meme("Structure_Meme", "Abstract structure", 1, isStructure: true),
                deity = new BeliefDeityFact { name = "Auralis", isKeyDeity = true }
            };
            resolution.structure.description = "Structure description stays factual";
            resolution.stances.Add(new ResolvedBeliefStance { precept = precept });
            resolution.supportingMemes.Add(Meme("Support_Meme", "Proselytizer", 2));
            string full = BeliefContextFormatter.Format(resolution, NarrativeDetailLevelTokens.Full, policy);
            AssertContains("full formatter includes ideoligion", full, "ideoligion: The Quiet Flame");
            AssertContains("full formatter includes certainty percentage", full, "certainty: 62% (steady)");
            AssertContains("full formatter includes XML certainty phrase", full, "XML-owned certainty phrase");
            AssertContains("full formatter includes precept", full, "relevant precept: Visible stance");
            AssertContains("full formatter includes structure", full, "structure: Abstract structure");
            AssertContains("full formatter includes deity", full, "deity: Auralis");
            AssertTrue("formatter strips markup", full.IndexOf('<') < 0 && full.IndexOf('>') < 0);
            AssertTrue("formatter collapses hostile field newlines", full.IndexOf("Guide\nInjected", StringComparison.Ordinal) < 0);
            AssertTrue("formatter replaces semicolon data", full.IndexOf(';') < 0);

            string balanced = BeliefContextFormatter.Format(resolution, NarrativeDetailLevelTokens.Balanced, policy);
            AssertTrue("balanced omits descriptions", balanced.IndexOf("precept meaning:", StringComparison.Ordinal) < 0);
            AssertContains("balanced keeps supporting meme", balanced, "relevant meme: Proselytizer");
            string compact = BeliefContextFormatter.Format(resolution, NarrativeDetailLevelTokens.Compact, policy);
            AssertTrue("compact omits structure", compact.IndexOf("structure:", StringComparison.Ordinal) < 0);
            AssertTrue("compact omits memes", compact.IndexOf("relevant meme:", StringComparison.Ordinal) < 0);
            AssertContains("compact keeps top doctrine", compact, "relevant precept: Visible stance");
            AssertContains("saved Compact projection keeps certainty trend",
                BeliefContextFormatter.ForDetail(full, NarrativeDetailLevelTokens.Compact, policy),
                "certainty trend: rising (meaningful)");
            AssertEqual("whole-word trim", "alpha", BeliefContextFormatter.WholeWord("alpha beta gamma", 10));
            AssertEqual("unmatched angle delimiter preserves following plain text", "alpha beta tail",
                BeliefContextFormatter.Clean("alpha < beta tail", 100));
            AssertEmpty("empty resolution formats empty", new BeliefStanceResolution(), policy);

            BeliefMemeFact relatedMeme = Meme("Related_Meme", "Related meme", 1);
            BeliefPreceptFact linked = Precept("Deity_Precept", "Deity_Issue", "deity stance", 1);
            linked.associatedMemeDefNames.Add("Related_Meme");
            BeliefSnapshot deitySnapshot = Snapshot(linked);
            deitySnapshot.memes.Add(relatedMeme);
            deitySnapshot.structure = Meme("Structure_Only", "Structure", 1, isStructure: true);
            deitySnapshot.deities.Add(new BeliefDeityFact { name = "Key One", isKeyDeity = true });
            deitySnapshot.deities.Add(new BeliefDeityFact { name = "Related One", relatedMemeDefName = "Related_Meme" });
            BeliefStanceResolution deityResult = Resolve(deitySnapshot,
                SourceEvidence(linked.instanceId, linked.defName), policy);
            AssertEqual("related-meme deity outranks key deity", "Related One", deityResult.deity.name);
            AssertTrue("structure stays separate from ordinary memes",
                deityResult.structure != null && !ContainsMeme(deityResult.supportingMemes, "Structure_Only"));

            BeliefPolicyBuilder alternativeBuilder = BeliefPolicyBuilder.CreateDefault();
            alternativeBuilder.includeRelatedDeity = false;
            alternativeBuilder.includeKeyDeity = false;
            alternativeBuilder.allowDeterministicAlternativeDeity = true;
            BeliefPolicySnapshot alternativePolicy = alternativeBuilder.Build();
            BeliefPreceptFact alternativePrecept = Precept(
                "Alternative_Deity_Precept", "Alternative_Deity_Issue", "alternative deity stance", 1);
            BeliefSnapshot alternativeSnapshot = Snapshot(alternativePrecept);
            BeliefDeityFact alternativeA = new BeliefDeityFact { name = "Alternative A" };
            BeliefDeityFact alternativeB = new BeliefDeityFact { name = "Alternative B" };
            alternativeSnapshot.deities.Add(alternativeA);
            alternativeSnapshot.deities.Add(alternativeB);
            BeliefStanceResolution alternativeFirst = Resolve(alternativeSnapshot,
                SourceEvidence(alternativePrecept.instanceId, alternativePrecept.defName), alternativePolicy, seed: 91);
            AssertTrue("deterministic alternative deity branch selects a valid deity",
                alternativeFirst.deity != null);
            alternativeSnapshot.deities.Clear();
            alternativeSnapshot.deities.Add(alternativeB);
            alternativeSnapshot.deities.Add(alternativeA);
            BeliefStanceResolution alternativeReordered = Resolve(alternativeSnapshot,
                SourceEvidence(alternativePrecept.instanceId, alternativePrecept.defName), alternativePolicy, seed: 91);
            AssertEqual("alternative deity selection is independent of source ordering",
                alternativeFirst.deity.name, alternativeReordered.deity.name);
        }

        private static void TestMutationOnlyContextAndFormatting()
        {
            BeliefEventEvidence evidence = Evidence(true);
            evidence.narrative.subjectLabel = "Target";
            evidence.mutation = new BeliefMutationSnapshot
            {
                pawnId = "SyntheticPawn",
                beforeIdeologyId = "Before_Ideo",
                beforeIdeologyName = "Before Ideoligion",
                afterIdeologyId = "After_Ideo",
                afterIdeologyName = "After Ideoligion",
                attemptedIdeologyId = "Attempted_Ideo",
                attemptedIdeologyName = "Attempted Ideoligion",
                hasBeforeCertainty = true,
                beforeCertainty = 0.2f,
                hasAfterCertainty = true,
                afterCertainty = 0.5f,
                certaintyChanged = true,
                ideologyChanged = true,
                conversionSucceeded = true
            };
            evidence.mutation.causeTokens.Add(BeliefMutationCauseTokens.ConversionAttempt);
            evidence.mutation.causeTokens.Add(BeliefMutationCauseTokens.SetIdeology);
            BeliefPolicySnapshot policy = BeliefPolicySnapshot.CreateDefault();
            BeliefSnapshot converterSnapshot = Snapshot();
            converterSnapshot.pawnId = "ConverterPawn";
            // Runtime pair-page orchestration re-stamps the source row for each POV before resolving.
            // Mirror that contract here while retaining SyntheticPawn as the mutation subject.
            evidence.narrative.povPawnId = converterSnapshot.pawnId;
            BeliefStanceResolution result = Resolve(converterSnapshot, evidence, policy);
            AssertTrue("mutation-only resolution is useful context", result.HasUsefulContext);
            AssertEqual("mutation-only resolution needs no selected stance", 0, result.stances.Count);
            AssertTrue("mutation-only resolution retains the observed mutation", result.mutation != null);
            AssertEqual("mutation-only resolution retains the visible mutation subject", "Target",
                result.mutationSubjectLabel);

            string full = BeliefContextFormatter.Format(result, NarrativeDetailLevelTokens.Full, policy);
            AssertContains("full mutation format labels the changed pawn", full,
                "belief change subject: Target");
            AssertContains("full mutation format includes previous ideoligion", full,
                "previous ideoligion: Before Ideoligion");
            AssertContains("full mutation format includes current ideoligion", full,
                "current ideoligion: After Ideoligion");
            AssertContains("full mutation format includes attempted ideoligion", full,
                "attempted ideoligion: Attempted Ideoligion");
            AssertContains("full mutation format includes before certainty", full,
                "certainty before: 20%");
            AssertContains("full mutation format includes after certainty", full,
                "certainty after: 50%");
            AssertContains("full mutation format includes numeric certainty delta", full,
                "certainty delta: +30%");
            AssertContains("full mutation format includes exact conversion result", full,
                "conversion result: success");
            AssertContains("full mutation format includes mechanical cause tokens", full,
                "mutation cause: conversion_attempt,set_ideology");
            string compact = BeliefContextFormatter.Format(result, NarrativeDetailLevelTokens.Compact, policy);
            AssertContains("compact mutation format labels the changed pawn", compact,
                "belief change subject: Target");
            AssertTrue("compact mutation format omits transition lines",
                compact.IndexOf("previous ideoligion:", StringComparison.Ordinal) < 0
                    && compact.IndexOf("current ideoligion:", StringComparison.Ordinal) < 0
                    && compact.IndexOf("attempted ideoligion:", StringComparison.Ordinal) < 0);
            AssertContains("compact mutation format keeps certainty delta", compact,
                "certainty delta: +30%");
            AssertContains("compact mutation format keeps conversion result", compact,
                "conversion result: success");
            AssertTrue("compact mutation format omits low-level cause tokens",
                compact.IndexOf("mutation cause:", StringComparison.Ordinal) < 0);

            BeliefSnapshot targetSnapshot = Snapshot();
            targetSnapshot.pawnId = "SyntheticPawn";
            evidence.narrative.povPawnId = targetSnapshot.pawnId;
            BeliefStanceResolution targetResult = Resolve(targetSnapshot, evidence, policy);
            AssertTrue("mutation owner is identified as the current POV",
                targetResult.mutationSubjectIsPov);
            string targetCompact = BeliefContextFormatter.Format(
                targetResult, NarrativeDetailLevelTokens.Compact, policy);
            AssertTrue("mutation owner does not receive a redundant third-person subject line",
                targetCompact.IndexOf("belief change subject:", StringComparison.Ordinal) < 0);
            AssertContains("mutation owner still receives the exact compact result", targetCompact,
                "conversion result: success");
        }

        private static void TestPhase1EvidencePersistenceAndCorrelation()
        {
            BeliefPolicySnapshot policy = BeliefPolicySnapshot.CreateDefault();
            BeliefPreceptFact approves = Precept(
                "Synthetic_Approval", "Synthetic_Body_Issue", "welcomes crafted replacements", 1);
            approves.correlations.Add(Correlation(BeliefCorrelationKindTokens.HistoryEvent,
                "SyntheticBodyChanged", BeliefValenceTokens.Positive));
            BeliefPreceptFact despises = Precept(
                "Synthetic_Despise", "Synthetic_Body_Issue", "rejects crafted replacements", 1);
            despises.correlations.Add(Correlation(BeliefCorrelationKindTokens.HistoryEvent,
                "SyntheticBodyChanged", BeliefValenceTokens.Negative));
            BeliefEventEvidence sameEvent = BeliefEventEvidenceFactory.ForBodyModification(
                "SyntheticPawn", 250, "SyntheticArm", "crafted arm", "left arm", "added", "bionic");
            sameEvent.historyEventDefNames.Add("SyntheticBodyChanged");

            BeliefStanceResolution approvingResult = Resolve(Snapshot(approves), sameEvent, policy);
            BeliefStanceResolution despisingResult = Resolve(Snapshot(despises), sameEvent, policy);
            AssertSelected("same event resolves approving ideology", approvingResult, "Synthetic_Approval");
            AssertSelected("same event resolves despising ideology", despisingResult, "Synthetic_Despise");
            AssertEqual("approving ideology keeps mechanical polarity", BeliefValenceTokens.Positive,
                approvingResult.stances[0].correlationValence);
            AssertEqual("despising ideology keeps mechanical polarity", BeliefValenceTokens.Negative,
                despisingResult.stances[0].correlationValence);

            BeliefPreceptFact situational = Precept(
                "Synthetic_Situational", "Synthetic_Body_Issue", "body change stance", 1);
            situational.correlations.Add(Correlation(BeliefCorrelationKindTokens.Thought,
                "HasNoReplacement", BeliefValenceTokens.Negative));
            situational.correlations.Add(Correlation(BeliefCorrelationKindTokens.Thought,
                "HasReplacement", BeliefValenceTokens.Positive));
            BeliefStanceResolution situationalResolution = new BeliefStanceResolution();
            situationalResolution.stances.Add(new ResolvedBeliefStance
            {
                precept = situational,
                correlationValence = BeliefValenceTokens.Mixed
            });
            AssertEqual("active positive situational correlation resolves approval",
                BeliefValenceTokens.Positive,
                BeliefActiveThoughtPolicy.ResolveValence(situationalResolution, new[] { "HasReplacement" }));
            AssertEqual("active negative situational correlation resolves rejection",
                BeliefValenceTokens.Negative,
                BeliefActiveThoughtPolicy.ResolveValence(situationalResolution, new[] { "HasNoReplacement" }));
            AssertEqual("negative active correlation wins a conflicting worker result",
                BeliefValenceTokens.Negative,
                BeliefActiveThoughtPolicy.ResolveValence(
                    situationalResolution, new[] { "HasReplacement", "HasNoReplacement" }));
            AssertEqual("unrelated active thought does not infer doctrine polarity",
                BeliefValenceTokens.Unknown,
                BeliefActiveThoughtPolicy.ResolveValence(situationalResolution, new[] { "AteFineMeal" }));

            BeliefEventEvidence ordinary = BeliefEventEvidenceFactory.ForEvent(
                "SyntheticPawn", 251, "work", "SyntheticSweeping", "initiator", "swept the floor", "ordinary");
            AssertEmpty("unmatched ordinary Phase 1 evidence remains neutral",
                Resolve(Snapshot(approves), ordinary, policy));

            BeliefSnapshot lexicalSnapshot = Snapshot(approves);
            approves.correlations[0].description = "crafted replacement ceremony";
            BeliefStanceResolution lexical = Resolve(lexicalSnapshot,
                TextEvidence(true, "crafted replacement ceremony"), policy);
            AssertSelected("high-confidence correlation text resolves", lexical, "Synthetic_Approval");
            AssertEqual("correlation-text fallback carries typed companion valence",
                BeliefValenceTokens.Positive, lexical.stances[0].correlationValence);

            string full = BeliefContextFormatter.Format(approvingResult,
                NarrativeDetailLevelTokens.Full, policy);
            AssertEqual("event-time full block survives save/load normalization byte-identically",
                full, BeliefContextFormatter.NormalizeSaved(full, policy));
            string hostile = full + "\r\n<script>: injected\r\nrelevant precept: <b>safe</b>\u0007 tail";
            string normalized = BeliefContextFormatter.NormalizeSaved(hostile, policy);
            AssertTrue("save normalization drops unknown labels", !normalized.Contains("script"));
            AssertTrue("save normalization strips markup and controls",
                normalized.IndexOf('<') < 0 && normalized.IndexOf('\u0007') < 0);
            AssertTrue("saved context remains bounded", normalized.Length <= policy.maximumTotalCharacters);
            AssertContains("balanced detail retains event stance",
                BeliefContextFormatter.ForDetail(full, NarrativeDetailLevelTokens.Balanced, policy),
                "relevant precept:");
            AssertContains("compact detail retains event stance",
                BeliefContextFormatter.ForDetail(full, NarrativeDetailLevelTokens.Compact, policy),
                "relevant precept:");

            BeliefSourcePreceptFact source = new BeliefSourcePreceptFact
            {
                instanceId = "Exact#1",
                defName = "Exact_Precept"
            };
            BeliefEventEvidence thought = BeliefEventEvidenceFactory.ForThought(
                "SyntheticPawn", 300, "SyntheticThought", "synthetic thought", source);
            AssertEqual("thought evidence freezes exact source-precept instance", "Exact#1",
                thought.sourcePreceptInstanceId);
            AssertEqual("thought evidence freezes exact source-precept Def", "Exact_Precept",
                thought.sourcePreceptDefName);
            BeliefEventEvidence cloned = BeliefEventEvidenceFactory.ForPov(
                thought, "Event#2", 301, "OtherPawn", "recipient");
            thought.thoughtDefNames.Clear();
            AssertEqual("POV evidence is detached from its source list", 1, cloned.thoughtDefNames.Count);
            AssertEqual("POV evidence replaces event identity", "Event#2", cloned.narrative.eventId);
            AssertEqual("POV evidence replaces pawn identity", "OtherPawn", cloned.narrative.povPawnId);

            BeliefHistoryCorrelationBuffer buffer = new BeliefHistoryCorrelationBuffer();
            for (int i = 0; i < 5; i++)
                buffer.Observe(new BeliefHistoryObservation
                {
                    tick = 400 + i,
                    historyEventDefName = "History_" + i,
                    visiblePawnIds = new List<string> { "SyntheticPawn" }
                }, 400 + i, 3, 20);
            AssertEqual("history cache obeys its entry cap", 3, buffer.Count);
            AssertEqual("history lookup is exact-pawn only", 0,
                buffer.NearbyDefNames("OtherPawn", 404, 20).Count);
            AssertEqual("history lookup returns all bounded nearby exact identities", 3,
                buffer.NearbyDefNames("SyntheticPawn", 404, 20).Count);
            AssertEqual("history lookup does not consume evidence", 3,
                buffer.NearbyDefNames("SyntheticPawn", 404, 20).Count);
            AssertEqual("stale history rows expire", 0,
                buffer.NearbyDefNames("SyntheticPawn", 500, 20).Count);
            AssertEqual("stale history pruning releases storage", 0, buffer.Count);
            buffer.Observe(new BeliefHistoryObservation
            {
                tick = 510,
                historyEventDefName = "Bad Event Name",
                visiblePawnIds = new List<string> { "SyntheticPawn" }
            }, 510, 3, 20);
            AssertEqual("malformed history identities fail closed", 0, buffer.Count);
        }

        private static void TestEvidenceRulesAndOptionalCorrections()
        {
            BeliefPolicySnapshot vocabulary = VocabularyPolicy();
            ExpandedBeliefEvidence body = BeliefEventEvidencePolicy.Expand(
                GroupEvidence("medical", "body_modification"), vocabulary);
            AssertTrue("body rule adds topic", Contains(body.topics, "body_modification"));
            AssertTrue("body rule adds semantic alias key", Contains(body.semanticAliases, "body_modification"));
            AssertTrue("body rule records diagnostic key", Contains(body.matchedRuleKeys, "body_rule"));

            BeliefEventEvidence unknown = Evidence(true);
            unknown.narrative.beliefTopics.Add("future_safe_topic");
            unknown.narrative.beliefTopics.Add("bad topic with spaces");
            ExpandedBeliefEvidence expandedUnknown = BeliefEventEvidencePolicy.Expand(unknown, vocabulary);
            AssertTrue("safe unknown topic preserved", Contains(expandedUnknown.topics, "future_safe_topic"));
            AssertTrue("malformed topic rejected", !Contains(expandedUnknown.topics, "bad topic with spaces"));

            BeliefPolicyBuilder guardedRules = BeliefPolicyBuilder.CreateDefault();
            BeliefEventEvidenceRule selectorless = new BeliefEventEvidenceRule(
                "selectorless", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, new[] { "global_topic" }, new string[0]);
            guardedRules.eventEvidenceRules.Add(selectorless);
            guardedRules.eventEvidenceRules.Add(Rule("scoped", "scoped_domain", "scoped_group",
                new[] { "scoped_topic" }, new string[0]));
            BeliefPolicySnapshot guardedPolicy = guardedRules.Build();
            AssertTrue("selectorless evidence row reports no selector", !selectorless.HasSelector);
            AssertEqual("immutable policy discards selectorless evidence rows", 1,
                guardedPolicy.eventEvidenceRules.Count);
            ExpandedBeliefEvidence unrelated = BeliefEventEvidencePolicy.Expand(Evidence(true), guardedPolicy);
            AssertTrue("selectorless evidence vocabulary cannot enrich unrelated events",
                !Contains(unrelated.topics, "global_topic"));

            AssertEqual("default override list remains empty", 0,
                BeliefPolicySnapshot.CreateDefault().correlationOverrides.Count);
            BeliefPreceptFact target = Precept("Correction_Target", "Correction_Issue", "opaque metadata", 0);
            BeliefPolicyBuilder forceBuilder = BeliefPolicyBuilder.CreateDefault();
            forceBuilder.correlationOverrides.Add(new BeliefCorrelationCorrection(
                "fixture_force", BeliefCorrectionActionTokens.Force, "Correction_Target", string.Empty,
                string.Empty, "synthetic", string.Empty, "opaque", "fixture_topic"));
            BeliefEventEvidence forceEvidence = GroupEvidence("synthetic", "opaque");
            forceEvidence.narrative.beliefTopics.Add("fixture_topic");
            BeliefStanceResolution forced = Resolve(Snapshot(target), forceEvidence, forceBuilder.Build());
            AssertSelected("optional force correction remains live-snapshot intersected", forced, "Correction_Target");
            AssertEqual("force correction key retained", "fixture_force", forced.stances[0].correctionKey);
            AssertCoverage("force correction has its own automatic-coverage outcome", forced,
                BeliefAutomaticCoverageOutcomeTokens.ForcedCorrection,
                BeliefRelevanceSourceTokens.Correction,
                BeliefRelevanceTierTokens.DirectIdentity);
            BeliefAutomaticCoverageAggregate correctionAggregate =
                new BeliefAutomaticCoverageAggregate();
            BeliefAutomaticCoverageDiagnostics.Add(
                correctionAggregate, forced.automaticCoverage, 8);
            AssertEqual("force correction aggregate count", 1,
                correctionAggregate.OutcomeCount(
                    BeliefAutomaticCoverageOutcomeTokens.ForcedCorrection));

            BeliefPolicyBuilder excludeBuilder = BeliefPolicyBuilder.CreateDefault();
            excludeBuilder.correlationOverrides.Add(new BeliefCorrelationCorrection(
                "fixture_exclude", BeliefCorrectionActionTokens.Exclude, "Correction_Target", string.Empty,
                string.Empty, "synthetic", string.Empty, "opaque", "fixture_topic"));
            forceEvidence.issueDefNames.Add("Correction_Issue");
            AssertEmpty("optional exclude correction removes only targeted live candidate",
                Resolve(Snapshot(target), forceEvidence, excludeBuilder.Build()));
        }

        private static void TestConfiguredExistingPageEvidenceClients()
        {
            BeliefPolicyBuilder builder = BeliefPolicyBuilder.CreateDefault();
            builder.eventEvidenceRules.Add(Rule(
                "raid_visible", "combat", "raid", new[] { "raids", "violence" }, new string[0]));
            builder.eventEvidenceRules.Add(new BeliefEventEvidenceRule(
                "enslave_attempt", "interaction", "EnslaveAttempt", "recruit",
                string.Empty, string.Empty, string.Empty, string.Empty,
                new[] { "slavery", "captivity" }, new[] { "slavery" }));
            builder.eventEvidenceRules.Add(new BeliefEventEvidenceRule(
                "mining_work", "work", "Mining", string.Empty,
                string.Empty, string.Empty, string.Empty, string.Empty,
                new[] { "mining" }, new[] { "mining" }));
            builder.eventEvidenceRules.Add(Rule(
                "ritual_visible", "ritual", "ritual", new[] { "rituals" },
                new[] { "rituals" }));
            builder.eventEvidenceRules.Add(new BeliefEventEvidenceRule(
                "dark_condition", "condition", "UnnaturalDarknessActive", "condition",
                string.Empty, string.Empty, string.Empty, string.Empty,
                new[] { "darkness" }, new[] { "darkness" }));
            builder.eventEvidenceRules.Add(new BeliefEventEvidenceRule(
                "rescue_thought", "thought", "RescuedMe", string.Empty,
                string.Empty, string.Empty, string.Empty, string.Empty,
                new[] { "charity" }, new[] { "charity" }));
            BeliefPolicySnapshot policy = builder.Build();

            BeliefEventEvidence raid = ConfiguredBeliefEventPolicy.Capture(
                ConfiguredRequest("combat", "RaidEnemy", "raid", "enemy raid"), true, policy);
            AssertTrue("configured raid client admits an XML-owned existing page", raid != null);
            ExpandedBeliefEvidence raidExpanded = BeliefEventEvidencePolicy.Expand(raid, policy);
            AssertTrue("configured raid client adds only its visible combat topics",
                Contains(raidExpanded.topics, "raids") && Contains(raidExpanded.topics, "violence")
                    && !Contains(raidExpanded.topics, "charity"));

            BeliefEventEvidence slavery = ConfiguredBeliefEventPolicy.Capture(
                ConfiguredRequest("interaction", "EnslaveAttempt", "recruit", "enslave attempt"),
                true, policy);
            AssertTrue("exact slavery interaction route is admitted", slavery != null);
            AssertTrue("exact slavery route retains bounded visible event text",
                slavery.matchFields.Count == 1 && slavery.matchFields[0].field == "event_label");
            AssertTrue("same group with a different interaction stays unchanged",
                ConfiguredBeliefEventPolicy.Capture(
                    ConfiguredRequest("interaction", "RecruitAttempt", "recruit", "recruit attempt"),
                    true, policy) == null);

            BeliefEventEvidence mining = ConfiguredBeliefEventPolicy.Capture(
                ConfiguredRequest("work", "Mining", "workPositive", "mining steel"), true, policy);
            AssertTrue("exact mining work is admitted independently of its diary mood group",
                mining != null);
            BeliefEventEvidence ritual = ConfiguredBeliefEventPolicy.Capture(
                new ConfiguredBeliefEventRequest
                {
                    pawnId = "SyntheticPawn",
                    tick = 500,
                    sourceDomain = "ritual",
                    sourceDefName = "SyntheticRitual",
                    groupKey = "ritual",
                    povRole = "initiator",
                    visibleLabel = "synthetic ceremony",
                    visibleField = "ritual_label",
                    detailLabel = "participant",
                    detailField = "subject_label",
                    phase = "finished"
                }, true, policy);
            AssertTrue("generic completed ritual supplies exact family and visible label evidence",
                ritual != null && ritual.matchFields[0].field == "ritual_label"
                    && ritual.matchFields[1].field == "subject_label"
                    && ritual.matchFields[1].value == "participant");
            BeliefEventEvidence condition = ConfiguredBeliefEventPolicy.Capture(
                ConfiguredRequest("condition", "UnnaturalDarknessActive", "condition",
                    "unnatural darkness began", "condition_label"), true, policy);
            AssertTrue("configured observed condition enriches only its existing transition page",
                condition != null && condition.matchFields[0].field == "condition_label");

            BeliefEventEvidence rescue = BeliefEventEvidenceFactory.ForThought(
                "SyntheticPawn", 500, "RescuedMe", "rescued me", new BeliefSourcePreceptFact());
            AssertTrue("existing thought evidence gains exact aid vocabulary without another hook",
                Contains(BeliefEventEvidencePolicy.Expand(rescue, policy).topics, "charity"));

            AssertTrue("inactive Ideology leaves configured clients inert",
                ConfiguredBeliefEventPolicy.Capture(
                    ConfiguredRequest("combat", "RaidEnemy", "raid", "enemy raid"), false, policy)
                    == null);
            AssertTrue("unconfigured events cannot become belief clients",
                ConfiguredBeliefEventPolicy.Capture(
                    ConfiguredRequest("tale", "BuiltFurniture", string.Empty, "built furniture"),
                    true, policy) == null);
            AssertTrue("missing visible facts fail closed",
                ConfiguredBeliefEventPolicy.Capture(
                    ConfiguredRequest("work", "Mining", string.Empty, string.Empty), true, policy)
                    == null);
            AssertTrue("unsafe field names fail closed",
                ConfiguredBeliefEventPolicy.Capture(
                    ConfiguredRequest("work", "Mining", string.Empty, "mining", "arbitrary_field"),
                    true, policy) == null);
        }

        private static ConfiguredBeliefEventRequest ConfiguredRequest(
            string domain,
            string defName,
            string group,
            string label,
            string field = "event_label")
        {
            return new ConfiguredBeliefEventRequest
            {
                pawnId = "SyntheticPawn",
                tick = 500,
                sourceDomain = domain,
                sourceDefName = defName,
                groupKey = group,
                povRole = "initiator",
                visibleLabel = label,
                visibleField = field,
                phase = "fixture"
            };
        }

        private static void TestObservationBaselineAndReflectionShell()
        {
            BeliefPolicySnapshot policy = BeliefPolicySnapshot.CreateDefault();
            BeliefObservationDecision missing = BeliefReflectionPolicy.Observe(new BeliefObservationRequest
            {
                featureAvailable = false
            }, policy);
            AssertEqual("missing tracker resets pending", BeliefObservationActionTokens.ResetPending, missing.action);
            AssertTrue("missing tracker clears debt", missing.clearPending && !missing.createReflectionDebt);

            BeliefObservationDecision baseline = BeliefReflectionPolicy.Observe(new BeliefObservationRequest
            {
                featureAvailable = true,
                baselineOnNextScan = true,
                hasCurrent = true,
                currentIdeologyId = "SyntheticIdeo",
                currentCertainty = 0.5f
            }, policy);
            AssertEqual("first scan is baseline", BeliefObservationActionTokens.Baseline, baseline.action);
            AssertTrue("first scan emits no debt", baseline.recordBaseline && !baseline.createReflectionDebt);

            BeliefObservationDecision small = ObserveDelta(0.50f, 0.54f, "Same", "Same", policy);
            AssertEqual("minor passive delta remains quiet", BeliefObservationActionTokens.NoChange, small.action);
            BeliefObservationDecision meaningful = ObserveDelta(0.50f, 0.55f, "Same", "Same", policy);
            AssertEqual("meaningful passive delta creates debt", BeliefObservationActionTokens.CertaintyChanged,
                meaningful.action);
            AssertEqual("meaningful passive trigger", BeliefReflectionTriggerTokens.CertaintyShift,
                meaningful.trigger);
            BeliefObservationDecision changed = ObserveDelta(0.50f, 0.50f, "Old", "New", policy);
            AssertEqual("ideology change precedes scalar comparison", BeliefObservationActionTokens.IdeologyChanged,
                changed.action);

            BeliefObservationTransition first = BeliefReflectionPolicy.Advance(
                null, Tracker("IdeoA", "First", 0.50f), true, 100, policy);
            AssertEqual("state reducer first scan baselines", BeliefObservationActionTokens.Baseline,
                first.decision.action);
            AssertTrue("state reducer baseline stores current without debt",
                first.state.hasLastObservation && !first.state.hasPendingCertainty
                    && !first.decision.createReflectionDebt);

            BeliefObservationTransition minor = BeliefReflectionPolicy.Advance(
                first.state, Tracker("IdeoA", "First", 0.47f), true, 200, policy);
            AssertEqual("sub-threshold certainty stays non-emitting", BeliefObservationActionTokens.NoChange,
                minor.decision.action);
            AssertTrue("sub-threshold certainty remains accumulated", minor.state.hasPendingCertainty);
            AssertNear("pending certainty preserves earliest baseline", 0.50f,
                minor.state.pendingCertaintyBefore);
            AssertNear("pending certainty records latest value", 0.47f,
                minor.state.pendingCertaintyAfter);

            BeliefObservationTransition accumulated = BeliefReflectionPolicy.Advance(
                minor.state, Tracker("IdeoA", "First", 0.43f), true, 300, policy);
            AssertEqual("several small moves cross the configured threshold",
                BeliefObservationActionTokens.CertaintyChanged, accumulated.decision.action);
            AssertEqual("accumulated certainty reports falling", BeliefCertaintyTrendTokens.Falling,
                accumulated.decision.certaintyTrend);
            AssertNear("accumulation keeps its original before value", 0.50f,
                accumulated.state.pendingCertaintyBefore);
            AssertNear("accumulation merges its latest after value", 0.43f,
                accumulated.state.pendingCertaintyAfter);
            AssertEqual("accumulation keeps first detection tick", 200,
                accumulated.state.pendingCertaintyFirstTick);
            AssertEqual("accumulation advances last detection tick", 300,
                accumulated.state.pendingCertaintyLastTick);

            BeliefObservationTransition returned = BeliefReflectionPolicy.Advance(
                accumulated.state, Tracker("IdeoA", "First", 0.50f), true, 400, policy);
            AssertTrue("returning to the pending anchor cancels certainty debt",
                !returned.state.hasPendingCertainty && !returned.decision.createReflectionDebt);

            BeliefObservationTransition changedOnce = BeliefReflectionPolicy.Advance(
                returned.state, Tracker("IdeoB", "Second", 0.80f), true, 500, policy);
            BeliefObservationTransition changedTwice = BeliefReflectionPolicy.Advance(
                changedOnce.state, Tracker("IdeoC", "Third", 0.60f), true, 600, policy);
            AssertTrue("ideology changes remain pending", changedTwice.state.pendingIdeologyChange);
            AssertEqual("merged ideology change preserves earliest identity", "IdeoA",
                changedTwice.state.pendingPreviousIdeologyId);
            AssertEqual("merged ideology change preserves latest identity", "IdeoC",
                changedTwice.state.pendingCurrentIdeologyId);
            AssertTrue("ideology changes clear incomparable certainty drift",
                !changedTwice.state.hasPendingCertainty);

            BeliefObservationTransition changedBack = BeliefReflectionPolicy.Advance(
                changedOnce.state, Tracker("IdeoA", "First", 0.50f), true, 600, policy);
            AssertTrue("returning to the pending ideology origin cancels net-zero change debt",
                !changedBack.state.pendingIdeologyChange
                    && !changedBack.decision.createReflectionDebt
                    && changedBack.decision.trigger == BeliefReflectionTriggerTokens.None);

            BeliefScanState persistedNetZero = changedOnce.state;
            persistedNetZero.pendingCurrentIdeologyId = persistedNetZero.pendingPreviousIdeologyId;
            persistedNetZero.pendingCurrentIdeologyName = persistedNetZero.pendingPreviousIdeologyName;
            BeliefObservationTransition normalizedNetZero = BeliefReflectionPolicy.Advance(
                persistedNetZero, Tracker("IdeoB", "Second", 0.80f), true, 600, policy);
            AssertTrue("normalization clears net-zero ideology debt saved by an older build",
                !normalizedNetZero.state.pendingIdeologyChange
                    && !normalizedNetZero.decision.createReflectionDebt);

            BeliefObservationTransition unavailable = BeliefReflectionPolicy.Advance(
                changedTwice.state, null, false, 700, policy);
            AssertEqual("missing tracker resets accumulated state", BeliefObservationActionTokens.ResetPending,
                unavailable.decision.action);
            AssertTrue("missing tracker requires a later baseline and clears all debt",
                unavailable.state.baselineOnNextScan && !unavailable.state.hasLastObservation
                    && !unavailable.state.hasPendingCertainty
                    && !unavailable.state.pendingIdeologyChange);
            BeliefObservationTransition restored = BeliefReflectionPolicy.Advance(
                unavailable.state, Tracker("IdeoC", "Third", 0.20f), true, 800, policy);
            AssertTrue("restored tracker baselines instead of catching up",
                restored.decision.recordBaseline && !restored.decision.createReflectionDebt);

            BeliefPolicyBuilder disabledBuilder = BeliefPolicyBuilder.CreateDefault();
            disabledBuilder.enabled = false;
            BeliefObservationTransition disabled = BeliefReflectionPolicy.Advance(
                accumulated.state, Tracker("IdeoA", "First", 0.40f), true, 900,
                disabledBuilder.Build());
            AssertTrue("disabled policy clears debt and requires a later baseline",
                disabled.state.baselineOnNextScan && !disabled.state.hasPendingCertainty
                    && !disabled.state.pendingIdeologyChange);

            BeliefPolicyBuilder shortAgeBuilder = BeliefPolicyBuilder.CreateDefault();
            shortAgeBuilder.pendingBeliefEvidenceMaxAgeTicks = 60000;
            BeliefPolicySnapshot shortAge = shortAgeBuilder.Build();
            BeliefObservationTransition agedBaseline = BeliefReflectionPolicy.Advance(
                null, Tracker("IdeoA", "First", 0.50f), true, 100, shortAge);
            BeliefObservationTransition agedMinor = BeliefReflectionPolicy.Advance(
                agedBaseline.state, Tracker("IdeoA", "First", 0.47f), true, 200, shortAge);
            BeliefObservationTransition afterAge = BeliefReflectionPolicy.Advance(
                agedMinor.state, Tracker("IdeoA", "First", 0.43f), true, 60301, shortAge);
            AssertNear("stale pending evidence reanchors at the latest observation", 0.47f,
                afterAge.state.pendingCertaintyBefore);
            AssertEqual("stale reanchored movement remains minor", BeliefObservationActionTokens.NoChange,
                afterAge.decision.action);

            BeliefScanState future = new BeliefScanState
            {
                baselineOnNextScan = false,
                hasLastObservation = true,
                lastIdeologyId = "IdeoA",
                lastCertainty = 0.50f,
                lastScanTick = 2000,
                hasPendingCertainty = true,
                pendingCertaintyBefore = 0.50f,
                pendingCertaintyAfter = 0.30f,
                pendingCertaintyFirstTick = 100,
                pendingCertaintyLastTick = 200
            };
            BeliefObservationTransition repairedFuture = BeliefReflectionPolicy.Advance(
                future, Tracker("IdeoA", "First", 0.20f), true, 1000, policy);
            AssertTrue("future scan ticks discard debt and force a silent baseline",
                repairedFuture.decision.recordBaseline && !repairedFuture.decision.createReflectionDebt
                    && !repairedFuture.state.hasPendingCertainty);

            BeliefReflectionRequest request = ReflectionRequest();
            request.pendingIdeologyChange = true;
            request.pendingMajorCertaintyShift = true;
            request.hasRecentRelevantEvent = true;
            request.hasPendingCertaintyDrift = true;
            AssertEqual("reflection priority chooses ideology change", BeliefReflectionTriggerTokens.IdeologyChange,
                BeliefReflectionPolicy.Plan(request, policy).trigger);
            request.pendingIdeologyChange = false;
            AssertEqual("certainty shift is second", BeliefReflectionTriggerTokens.CertaintyShift,
                BeliefReflectionPolicy.Plan(request, policy).trigger);
            request.pendingMajorCertaintyShift = false;
            AssertEqual("recent event is third", BeliefReflectionTriggerTokens.RecentEvent,
                BeliefReflectionPolicy.Plan(request, policy).trigger);
            request.hasRecentRelevantEvent = false;
            AssertEqual("passive drift is fourth", BeliefReflectionTriggerTokens.PassiveDrift,
                BeliefReflectionPolicy.Plan(request, policy).trigger);

            request.hasPendingCertaintyDrift = false;
            request.allowQuietReflection = true;
            request.quietRoll = 0f;
            AssertEqual("quiet roll zero passes", BeliefReflectionTriggerTokens.Quiet,
                BeliefReflectionPolicy.Plan(request, policy).trigger);
            request.quietRoll = 1f;
            AssertTrue("quiet roll one fails", !BeliefReflectionPolicy.Plan(request, policy).allowed);

            BeliefReflectionRequest cooldown = ReflectionRequest();
            cooldown.pendingIdeologyChange = true;
            cooldown.lastReflectionTick = 999999;
            cooldown.nowTick = 1000000;
            AssertEqual("reflection cooldown blocks", "cooldown",
                BeliefReflectionPolicy.Plan(cooldown, policy).blockReason);
            BeliefReflectionRequest cap = ReflectionRequest();
            cap.pendingIdeologyChange = true;
            cap.reflectionsThisQuadrum = policy.maximumBeliefReflectionsPerQuadrum;
            AssertEqual("quadrum cap blocks", "quadrum_cap", BeliefReflectionPolicy.Plan(cap, policy).blockReason);
            BeliefReflectionRequest reused = ReflectionRequest();
            reused.hasRecentRelevantEvent = true;
            reused.recentSourceAlreadyReflected = true;
            AssertEqual("source event reuse blocks", "source_reused",
                BeliefReflectionPolicy.Plan(reused, policy).blockReason);
        }

        private static void TestMalformedUnsafeAndOversizedInputs()
        {
            BeliefPolicySnapshot policy = BeliefPolicySnapshot.CreateDefault();
            string oversizedId = new string('X', policy.maximumIdentifierCharacters + 1);
            BeliefPreceptFact malformed = Precept(oversizedId, "Issue", "oversized doctrine", 3);
            malformed.instanceId = oversizedId;
            AssertEmpty("oversized identities are rejected", Resolve(Snapshot(malformed),
                SourceEvidence(oversizedId, oversizedId), policy));
            AssertEqual("invalid Unicode normalizes empty", string.Empty,
                BeliefLexicalMatcher.Normalize("\ud800", 32, 8));

            List<BeliefPreceptFact> many = new List<BeliefPreceptFact>();
            for (int i = 0; i < policy.maximumPreceptCandidates + 50; i++)
                many.Add(Precept("Bulk_" + i.ToString("D3"), "BulkIssue_" + i.ToString("D3"),
                    "bulk ordinary", 3));
            BeliefSnapshot oversized = Snapshot(many.ToArray());
            AssertEmpty("oversized unrelated candidate pool remains silent",
                Resolve(oversized, TextEvidence(true, new string('z', 10000)), policy));

            BeliefPreceptFact blank = Precept(string.Empty, "BlankIssue", "matching special phrase", 3);
            BeliefPreceptFact valid = Precept("Valid_Precept", "ValidIssue", "matching special phrase", 1);
            BeliefStanceResolution duplicateSafe = Resolve(Snapshot(null, blank, valid, valid),
                TextEvidence(true, "matching special phrase"), policy);
            AssertSelected("blank/null/duplicate facts collapse without suppressing a valid match",
                duplicateSafe, "Valid_Precept");

            BeliefStanceResolution longFormat = new BeliefStanceResolution { ideologyName = new string('A', 5000) };
            longFormat.stances.Add(new ResolvedBeliefStance { precept = valid });
            string formatted = BeliefContextFormatter.Format(longFormat, NarrativeDetailLevelTokens.Full, policy);
            AssertTrue("oversized formatter input respects total cap", formatted.Length <= policy.maximumTotalCharacters);
            AssertTrue("oversized formatter input respects line cap",
                formatted.Split(new[] { '\n' }, StringSplitOptions.None).Length <= policy.maximumTotalLines);
        }

        private static BeliefPolicySnapshot VocabularyPolicy()
        {
            BeliefPolicyBuilder builder = BeliefPolicyBuilder.CreateDefault();
            builder.semanticAliases.Add(new BeliefSemanticAlias("body_modification",
                new List<string> { "body modification", "prosthetic", "augmentation", "artificial body part" }));
            builder.semanticAliases.Add(new BeliefSemanticAlias("organ_use",
                new List<string> { "organ use", "organ harvesting", "organ transplant" }));
            builder.semanticAliases.Add(new BeliefSemanticAlias("cannibalism",
                new List<string> { "cannibalism", "human meat" }));
            builder.semanticAliases.Add(new BeliefSemanticAlias("meals",
                new List<string> { "meal", "food", "ingredient" }));
            builder.semanticAliases.Add(new BeliefSemanticAlias("raids",
                new List<string> { "raid", "raiding" }));
            builder.semanticAliases.Add(new BeliefSemanticAlias("violence",
                new List<string> { "violence", "combat" }));
            builder.semanticAliases.Add(new BeliefSemanticAlias("charity",
                new List<string> { "charity", "aid", "refugee" }));
            builder.semanticAliases.Add(new BeliefSemanticAlias("rituals",
                new List<string> { "ritual", "ceremony" }));
            builder.eventEvidenceRules.Add(Rule("body_rule", "medical", "body_modification",
                new[] { "body_modification" }, new[] { "body_modification" }));
            builder.eventEvidenceRules.Add(Rule("organ_rule", "medical", "organ_use",
                new[] { "organ_use" }, new[] { "organ_use" }));
            builder.eventEvidenceRules.Add(Rule("cannibal_rule", "food", "cannibal_meal",
                new[] { "cannibalism", "meals" }, new[] { "cannibalism" }));
            builder.eventEvidenceRules.Add(Rule("meal_rule", "food", "meal",
                new[] { "meals" }, new string[0]));
            builder.eventEvidenceRules.Add(Rule("raid_rule", "combat", "raid",
                new[] { "raids", "violence" }, new string[0]));
            builder.eventEvidenceRules.Add(Rule("aid_rule", "social", "aid",
                new[] { "charity" }, new[] { "charity" }));
            builder.eventEvidenceRules.Add(Rule("ritual_rule", "ritual", "ritual",
                new[] { "rituals" }, new[] { "rituals" }));
            return builder.Build();
        }

        private static BeliefEventEvidenceRule Rule(
            string key, string domain, string group, string[] topics, string[] aliases)
        {
            return new BeliefEventEvidenceRule(key, domain, string.Empty, group, string.Empty,
                string.Empty, string.Empty, string.Empty, topics, aliases);
        }

        private static BeliefSnapshot Snapshot(params BeliefPreceptFact[] precepts)
        {
            BeliefSnapshot result = new BeliefSnapshot
            {
                ideologyActive = true,
                pawnId = "SyntheticPawn",
                capturedTick = 12345,
                ideologyId = "SyntheticIdeology",
                ideologyName = "Synthetic Ideoligion",
                certainty = new BeliefCertaintyFact { hasCurrent = true, current = 0.62f }
            };
            if (precepts != null) result.precepts.AddRange(precepts);
            return result;
        }

        private static BeliefPreceptFact Precept(
            string defName, string issueDefName, string text, int impact, bool visible = true)
        {
            return new BeliefPreceptFact
            {
                instanceId = (defName ?? string.Empty) + "#instance",
                defName = defName ?? string.Empty,
                issue = new BeliefIssueFact
                {
                    defName = issueDefName ?? string.Empty,
                    label = text ?? string.Empty,
                    description = text ?? string.Empty
                },
                displayLabel = text ?? string.Empty,
                description = text ?? string.Empty,
                impactRank = impact,
                visible = visible
            };
        }

        private static BeliefPreceptFact CorrelatedPrecept(string defName, string issue, string thought)
        {
            BeliefPreceptFact result = Precept(defName, issue, defName, 1);
            result.correlations.Add(Correlation(BeliefCorrelationKindTokens.Thought, thought,
                BeliefValenceTokens.Unknown));
            return result;
        }

        private static BeliefCorrelationFact Correlation(string kind, string defName, string valence)
        {
            return new BeliefCorrelationFact
            {
                kind = kind,
                defName = defName,
                label = defName,
                description = defName,
                sourceComponentKind = "SyntheticComponent",
                sourceFieldToken = kind == BeliefCorrelationKindTokens.Thought ? "thought" : "eventDef",
                valence = valence
            };
        }

        private static BeliefMemeFact Meme(string defName, string label, int impact, bool isStructure = false)
        {
            return new BeliefMemeFact
            {
                defName = defName,
                label = label,
                description = label + " description",
                impactRank = impact,
                isStructure = isStructure
            };
        }

        private static BeliefEventEvidence Evidence(bool? pawnCanKnow)
        {
            return new BeliefEventEvidence
            {
                narrative = new NarrativeEvidence
                {
                    eventId = "SyntheticEvent",
                    tick = 12345,
                    povPawnId = "SyntheticPawn",
                    povRole = "initiator",
                    salience = NarrativeSalienceTokens.Meaningful,
                    pawnCanKnow = pawnCanKnow,
                    sourceDomain = "synthetic",
                    sourceDefName = "SyntheticEventDef"
                }
            };
        }

        private static BeliefEventEvidence TextEvidence(bool? pawnCanKnow, string text)
        {
            BeliefEventEvidence result = Evidence(pawnCanKnow);
            result.matchFields.Add(Text("event_label", text));
            return result;
        }

        private static BeliefEventEvidence SourceEvidence(string instanceId, string defName)
        {
            BeliefEventEvidence result = Evidence(true);
            result.sourcePreceptInstanceId = instanceId;
            result.sourcePreceptDefName = defName;
            return result;
        }

        private static BeliefEventEvidence GroupEvidence(string domain, string group)
        {
            BeliefEventEvidence result = Evidence(true);
            result.narrative.sourceDomain = domain;
            result.groupKey = group;
            return result;
        }

        private static BeliefEvidenceTextFact Text(string field, string value)
        {
            return new BeliefEvidenceTextFact { field = field, value = value };
        }

        private static BeliefStanceResolution Resolve(
            BeliefSnapshot snapshot,
            BeliefEventEvidence evidence,
            BeliefPolicySnapshot policy,
            string mode = BeliefResolutionModeTokens.EventEnrichment,
            int seed = 1,
            List<string> recent = null)
        {
            return EventRelativeStanceResolver.Resolve(new BeliefResolutionRequest
            {
                snapshot = snapshot,
                evidence = evidence,
                policy = policy,
                mode = mode,
                deterministicSeed = seed,
                recentSelectionDefNames = recent ?? new List<string>()
            });
        }

        private static BeliefMutationEventRule MutationRule(
            string sourceDefName,
            string downstreamGroupDefName,
            string requiredCauseToken,
            string conversionResult,
            string certaintyDirection,
            string ideologyChange,
            bool requireAttemptedIdeology)
        {
            return new BeliefMutationEventRule(
                BeliefMutationEventSourceTokens.Interaction,
                sourceDefName,
                downstreamGroupDefName,
                BeliefMutationSubjectRoleTokens.Recipient,
                sourceDefName == "Reassure" ? "reassurance" : "conversion",
                requiredCauseToken,
                conversionResult,
                certaintyDirection,
                ideologyChange,
                requireAttemptedIdeology);
        }

        private static ConversionRitualPolicyBuilder ConversionRitualPolicyBuilderForTests()
        {
            ConversionRitualPolicyBuilder builder = new ConversionRitualPolicyBuilder
            {
                enabled = true,
                ritualDefName = "Conversion",
                behaviorWorkerClassName = ConversionBehaviorTypeName,
                outcomeWorkerClassName = ConversionOutcomeTypeName,
                downstreamGroupDefName = "ritualConversion",
                organizerRoleId = "moralist",
                targetRoleId = "convertee",
                organizerIdeologyRoleDefName = "IdeoRole_Moralist",
                organizerEvidenceMode = ConversionRitualEvidenceModeTokens.OrganizerRole,
                targetEvidenceMode = ConversionRitualEvidenceModeTokens.TargetMutation,
                participantEvidenceMode = ConversionRitualEvidenceModeTokens.CurrentBelief,
                spectatorEvidenceMode = ConversionRitualEvidenceModeTokens.None,
                evidenceGroupKey = "conversion",
                organizerRoleToken = "converter",
                targetRoleToken = "convertee",
                participantRoleToken = "participant",
                spectatorRoleToken = "spectator",
                convertedResultToken = "converted",
                certaintyDecreasedResultToken = "certainty_decreased",
                certaintyIncreasedResultToken = "certainty_increased",
                mutationCorrelationWindowTicks = 0,
                certaintyDeltaEpsilon = 0.0001f,
                maximumAdditionalContextCharacters = 192
            };
            builder.allowedMutationCauseTokens.Add(BeliefMutationCauseTokens.SetIdeology);
            builder.allowedMutationCauseTokens.Add(BeliefMutationCauseTokens.CertaintyOffset);
            return builder;
        }

        private static void TestExactFoodEvidenceClient()
        {
            BeliefPolicyBuilder builder = BeliefPolicyBuilder.CreateDefault();
            builder.semanticAliases.Add(new BeliefSemanticAlias("cannibalism",
                new List<string> { "cannibalism", "human meat" }));
            builder.semanticAliases.Add(new BeliefSemanticAlias("meals",
                new List<string> { "meal", "food", "ingredient" }));
            builder.semanticAliases.Add(new BeliefSemanticAlias("insect_meat",
                new List<string> { "insect meat", "insect flesh" }));
            builder.semanticAliases.Add(new BeliefSemanticAlias("animal_meat",
                new List<string> { "animal meat", "meat eating" }));
            builder.semanticAliases.Add(new BeliefSemanticAlias("fungus",
                new List<string> { "fungus food", "fungus eating" }));
            builder.semanticAliases.Add(new BeliefSemanticAlias("nutrient_paste",
                new List<string> { "nutrient paste", "synthetic meal" }));
            builder.eventEvidenceRules.Add(Rule("cannibal_meal", string.Empty,
                "cannibal_meal", new[] { "cannibalism", "meals" },
                new[] { "cannibalism" }));
            builder.eventEvidenceRules.Add(Rule("insect_meal", string.Empty,
                "insect_meal", new[] { "insect_meat", "meals" },
                new[] { "insect_meat" }));
            builder.eventEvidenceRules.Add(Rule("animal_meat_meal", string.Empty,
                "animal_meat_meal", new[] { "animal_meat", "meals" },
                new[] { "animal_meat" }));
            builder.eventEvidenceRules.Add(Rule("fungus_meal", string.Empty,
                "fungus_meal", new[] { "fungus", "meals" },
                new[] { "fungus" }));
            builder.eventEvidenceRules.Add(Rule("nutrient_paste_meal", string.Empty,
                "nutrient_paste_meal", new[] { "nutrient_paste", "meals" },
                new[] { "nutrient_paste" }));
            builder.foodEvidenceRules.Add(new BeliefFoodEvidenceRule(
                "humanlike_meat", FoodIngestionEvidenceKindTokens.HumanlikeMeat,
                "cannibal_meal", "ingredient_label"));
            builder.foodEvidenceRules.Add(new BeliefFoodEvidenceRule(
                "insect_meat", FoodIngestionEvidenceKindTokens.InsectMeat,
                "insect_meal", "ingredient_label"));
            builder.foodEvidenceRules.Add(new BeliefFoodEvidenceRule(
                "animal_meat", FoodIngestionEvidenceKindTokens.AnimalMeat,
                "animal_meat_meal", "ingredient_label"));
            builder.foodEvidenceRules.Add(new BeliefFoodEvidenceRule(
                "fungus", FoodIngestionEvidenceKindTokens.Fungus,
                "fungus_meal", "ingredient_label"));
            builder.foodEvidenceRules.Add(new BeliefFoodEvidenceRule(
                "nutrient_paste", FoodIngestionEvidenceKindTokens.NutrientPaste,
                "nutrient_paste_meal", "ingredient_label"));
            BeliefPolicySnapshot policy = builder.Build();
            FoodIngestionEvidenceFact exactHumanMeat = new FoodIngestionEvidenceFact
            {
                ingredientKind = FoodIngestionEvidenceKindTokens.HumanlikeMeat,
                ingredientDefName = "Synthetic_Human_Meat",
                ingredientLabel = "human meat"
            };

            // Match the live ThoughtSignal shape: the existing page already carries its generic
            // thought identity/label before the exact ingredient scope enriches that same evidence.
            BeliefEventEvidence exact = BeliefEventEvidenceFactory.ForThought(
                "SyntheticPawn", 12345, "AteFineMeal", "fine meal",
                new BeliefSourcePreceptFact());
            string originalDomain = exact.narrative.sourceDomain;
            AssertTrue("exact human-meat fact activates the XML food mapping",
                FoodBeliefEvidencePolicy.TryEnrich(exact, exactHumanMeat, true, policy));
            AssertEqual("food enrichment keeps thought page source ownership", originalDomain,
                exact.narrative.sourceDomain);
            AssertEqual("exact human-meat mapping supplies the configured group", "cannibal_meal",
                exact.groupKey);
            AssertEqual("exact human-meat mapping preserves the thought field and adds one ingredient field", 2,
                exact.matchFields.Count);
            AssertEqual("exact human-meat field keeps the captured ingredient label", "human meat",
                exact.matchFields[1].value);
            ExpandedBeliefEvidence expanded = BeliefEventEvidencePolicy.Expand(exact, policy);
            AssertTrue("cannibal food rule expands the cannibalism topic",
                Contains(expanded.topics, "cannibalism"));
            AssertTrue("cannibal food rule expands the meal topic",
                Contains(expanded.topics, "meals"));

            // Mirror the installed vanilla Cannibalism_Preferred projection. Its visible label is
            // merely "preferred"; the issue and description carry the food-specific language.
            BeliefPreceptFact cannibal = Precept(
                "Cannibalism_Preferred", "Cannibalism", "preferred", 2);
            cannibal.issue.label = "cannibalism";
            cannibal.issue.description = string.Empty;
            cannibal.description = "To consume human meat is a noble and necessary part of life.";
            BeliefPreceptFact unrelated = Precept(
                "Slavery_Unrelated", "Slavery_Issue", "slavery bondage", 3);
            BeliefPreceptFact genericFood = Precept(
                "MeatEating_NonMeat_Disapproved", "MeatEating", "vegetarian disliked", 2);
            genericFood.issue.label = "meat eating";
            genericFood.description =
                "Eating vegetarian food is distasteful. Every meal should have some meat, eggs, milk, or other animal product.";
            AssertSelected("exact human-meat evidence activates the cannibal rule only",
                Resolve(Snapshot(cannibal, genericFood, unrelated), exact, policy), cannibal.defName);
            AssertEmpty("exact human-meat evidence leaves unrelated doctrine silent",
                Resolve(Snapshot(unrelated), exact, policy));
            BeliefPreceptFact contradictoryCannibal = Precept(
                "Cannibalism_Abhorrent", "Cannibalism", "abhorrent", 3);
            contradictoryCannibal.issue.label = "cannibalism";
            contradictoryCannibal.description = "The thought of eating human meat is abhorrent and horrible.";
            AssertEmpty("contradictory same-issue cannibal stances fail closed",
                Resolve(Snapshot(cannibal, contradictoryCannibal), exact, policy));

            FoodIngestionEvidenceFact exactInsectMeat = new FoodIngestionEvidenceFact
            {
                ingredientKind = FoodIngestionEvidenceKindTokens.InsectMeat,
                ingredientDefName = "Synthetic_Insect_Meat",
                ingredientLabel = "insect meat"
            };
            BeliefEventEvidence exactInsect = BeliefEventEvidenceFactory.ForThought(
                "SyntheticPawn", 12346, "AteFineMeal", "fine meal",
                new BeliefSourcePreceptFact());
            AssertTrue("exact insect-meat fact activates its XML food mapping",
                FoodBeliefEvidencePolicy.TryEnrich(exactInsect, exactInsectMeat, true, policy));
            AssertEqual("exact insect-meat mapping supplies the configured group", "insect_meal",
                exactInsect.groupKey);
            AssertEqual("exact insect-meat mapping preserves the thought field and adds one ingredient field", 2,
                exactInsect.matchFields.Count);
            AssertEqual("exact insect-meat field keeps the captured ingredient label", "insect meat",
                exactInsect.matchFields[1].value);
            ExpandedBeliefEvidence expandedInsect = BeliefEventEvidencePolicy.Expand(
                exactInsect, policy);
            AssertTrue("insect food rule expands the insect-meat topic",
                Contains(expandedInsect.topics, "insect_meat"));
            AssertTrue("insect food rule expands the shared meal topic",
                Contains(expandedInsect.topics, "meals"));

            // Mirror vanilla InsectMeatEating_Loved without treating its DefName as runtime policy.
            // The issue and description, not the generic stance label "loved", carry the vocabulary.
            BeliefPreceptFact insectLoved = Precept(
                "InsectMeatEating_Loved", "InsectMeat", "loved", 2);
            insectLoved.issue.label = "insect meat";
            insectLoved.issue.description = string.Empty;
            insectLoved.description =
                "There is nothing more divine than the succulent, slimy flesh of an insect.";
            BeliefSnapshot insectSnapshot = Snapshot(insectLoved, genericFood, unrelated);
            insectSnapshot.capturedTick = exactInsect.narrative.tick;
            AssertSelected("exact insect-meat evidence activates only the matching live stance",
                Resolve(insectSnapshot, exactInsect, policy),
                insectLoved.defName);
            BeliefSnapshot unrelatedInsectSnapshot = Snapshot(unrelated);
            unrelatedInsectSnapshot.capturedTick = exactInsect.narrative.tick;
            AssertEmpty("exact insect-meat evidence leaves unrelated doctrine silent",
                Resolve(unrelatedInsectSnapshot, exactInsect, policy));

            FoodIngestionEvidenceFact exactAnimalMeat = FoodFact(
                FoodIngestionEvidenceKindTokens.AnimalMeat, "Meat_Muffalo", "muffalo meat");
            FoodIngestionEvidenceFact exactFungus = FoodFact(
                FoodIngestionEvidenceKindTokens.Fungus, "RawFungus", "raw fungus");
            FoodIngestionEvidenceFact exactPaste = FoodFact(
                FoodIngestionEvidenceKindTokens.NutrientPaste,
                "MealNutrientPaste", "nutrient paste meal");
            FoodIngestionEvidenceFact[] remainingFacts =
                { exactAnimalMeat, exactFungus, exactPaste };
            string[] remainingGroups =
                { "animal_meat_meal", "fungus_meal", "nutrient_paste_meal" };
            for (int i = 0; i < remainingFacts.Length; i++)
            {
                BeliefEventEvidence remaining = BeliefEventEvidenceFactory.ForThought(
                    "SyntheticPawn", 12400 + i, "AteFineMeal", "fine meal",
                    new BeliefSourcePreceptFact());
                AssertTrue("remaining exact food kind activates XML mapping " + remainingGroups[i],
                    FoodBeliefEvidencePolicy.TryEnrich(
                        remaining, remainingFacts[i], true, policy));
                AssertEqual("remaining exact food kind supplies its configured group",
                    remainingGroups[i], remaining.groupKey);
            }

            // A meal can contain several exact categories. Selection follows XML policy order,
            // never ingredient registration order, so modded mixed meals remain deterministic.
            FoodIngestionEvidenceFact selected = FoodBeliefEvidencePolicy.SelectFact(
                new List<FoodIngestionEvidenceFact>
                {
                    exactPaste, exactFungus, exactAnimalMeat, exactInsectMeat, exactHumanMeat
                },
                policy);
            AssertEqual("XML food policy order chooses humanlike meat from a mixed meal",
                FoodIngestionEvidenceKindTokens.HumanlikeMeat, selected?.ingredientKind);

            BeliefEventEvidence generic = Evidence(true);
            AssertTrue("generic meal without an exact ingredient stays unchanged",
                !FoodBeliefEvidencePolicy.TryEnrich(generic, null, true, policy));
            FoodIngestionEvidenceFact unknown = new FoodIngestionEvidenceFact
            {
                ingredientKind = "unknown",
                ingredientDefName = "Synthetic_Unknown_Meal",
                ingredientLabel = "mystery meal"
            };
            AssertTrue("unknown ingredient kind stays unchanged",
                !FoodBeliefEvidencePolicy.TryEnrich(generic, unknown, true, policy));
            AssertEqual("generic/unknown meal adds no group", string.Empty, generic.groupKey);
            AssertEqual("generic/unknown meal adds no ingredient field", 0, generic.matchFields.Count);
            AssertEqual("generic/unknown meal adds no topic evidence", 0,
                BeliefEventEvidencePolicy.Expand(generic, policy).topics.Count);

            BeliefEventEvidence sourceWins = Evidence(true);
            AssertTrue("source-precedence fixture receives exact food evidence",
                FoodBeliefEvidencePolicy.TryEnrich(sourceWins, exactHumanMeat, true, policy));
            sourceWins.sourcePreceptInstanceId = unrelated.instanceId;
            sourceWins.sourcePreceptDefName = unrelated.defName;
            BeliefStanceResolution sourceResolution = Resolve(
                Snapshot(cannibal, unrelated), sourceWins, policy);
            AssertSelected("exact source precept retains precedence over food vocabulary",
                sourceResolution, unrelated.defName);
            AssertEqual("food enrichment does not replace source-precept diagnostics",
                BeliefRelevanceSourceTokens.SourcePrecept,
                sourceResolution.stances[0].relevanceSource);

            BeliefPreceptFact correlated = CorrelatedPrecept(
                "Exact_Thought_Food", "Exact_Thought_Issue", "SyntheticFoodThought");
            BeliefEventEvidence correlationWins = Evidence(true);
            correlationWins.thoughtDefNames.Add("SyntheticFoodThought");
            AssertTrue("correlation-precedence fixture receives exact food evidence",
                FoodBeliefEvidencePolicy.TryEnrich(
                    correlationWins, exactHumanMeat, true, policy));
            BeliefStanceResolution correlationResolution = Resolve(
                Snapshot(cannibal, correlated), correlationWins, policy);
            AssertSelected("exact thought correlation retains precedence over food vocabulary",
                correlationResolution, correlated.defName);
            AssertEqual("food enrichment does not replace correlation diagnostics",
                BeliefRelevanceSourceTokens.ThoughtCorrelation,
                correlationResolution.stances[0].relevanceSource);

            BeliefPolicyBuilder malformedBuilder = BeliefPolicyBuilder.CreateDefault();
            malformedBuilder.foodEvidenceRules.Add(new BeliefFoodEvidenceRule(
                "humanlike_meat", FoodIngestionEvidenceKindTokens.HumanlikeMeat,
                string.Empty, "ingredient_label"));
            BeliefEventEvidence malformedEvidence = Evidence(true);
            AssertTrue("malformed exact food policy fails closed",
                !FoodBeliefEvidencePolicy.TryEnrich(
                    malformedEvidence, exactHumanMeat, true, malformedBuilder.Build()));
            AssertEqual("malformed food policy leaves no partial group", string.Empty,
                malformedEvidence.groupKey);
            AssertEqual("malformed food policy leaves no partial field", 0,
                malformedEvidence.matchFields.Count);

            BeliefPolicyBuilder duplicateBuilder = BeliefPolicyBuilder.CreateDefault();
            duplicateBuilder.foodEvidenceRules.Add(new BeliefFoodEvidenceRule(
                "humanlike_meat_first", FoodIngestionEvidenceKindTokens.HumanlikeMeat,
                "cannibal_meal", "ingredient_label"));
            duplicateBuilder.foodEvidenceRules.Add(new BeliefFoodEvidenceRule(
                "humanlike_meat_second", FoodIngestionEvidenceKindTokens.HumanlikeMeat,
                "other_group", "ingredient_label"));
            BeliefEventEvidence duplicateEvidence = Evidence(true);
            AssertTrue("duplicate exact food mappings fail closed",
                !FoodBeliefEvidencePolicy.TryEnrich(
                    duplicateEvidence, exactHumanMeat, true, duplicateBuilder.Build()));
            AssertEqual("duplicate food policy leaves no partial group", string.Empty,
                duplicateEvidence.groupKey);
            AssertEqual("duplicate food policy leaves no partial field", 0,
                duplicateEvidence.matchFields.Count);
            AssertTrue("duplicate food policy also rejects scope selection",
                FoodBeliefEvidencePolicy.SelectFact(
                    new List<FoodIngestionEvidenceFact> { exactHumanMeat },
                    duplicateBuilder.Build()) == null);

            BeliefEventEvidence inactive = Evidence(true);
            AssertTrue("Ideology-inactive food enrichment remains inert",
                !FoodBeliefEvidencePolicy.TryEnrich(inactive, exactHumanMeat, false, policy));
            BeliefPolicyBuilder disabledBuilder = BeliefPolicyBuilder.CreateDefault();
            disabledBuilder.enabled = false;
            disabledBuilder.foodEvidenceRules.Add(new BeliefFoodEvidenceRule(
                "humanlike_meat", FoodIngestionEvidenceKindTokens.HumanlikeMeat,
                "cannibal_meal", "ingredient_label"));
            AssertTrue("disabled belief policy leaves food enrichment inert",
                !FoodBeliefEvidencePolicy.TryEnrich(
                    inactive, exactHumanMeat, true, disabledBuilder.Build()));
            AssertEqual("inactive/disabled food enrichment adds no field", 0,
                inactive.matchFields.Count);
        }

        private static FoodIngestionEvidenceFact FoodFact(
            string kind,
            string defName,
            string label)
        {
            return new FoodIngestionEvidenceFact
            {
                ingredientKind = kind,
                ingredientDefName = defName,
                ingredientLabel = label
            };
        }

        private static AuthoritySpeechPolicyBuilder AuthoritySpeechPolicyBuilderForTests()
        {
            AuthoritySpeechPolicyBuilder builder = new AuthoritySpeechPolicyBuilder
            {
                enabled = true,
                evidenceGroupKey = "authority_speech",
                topicToken = "authority_speech",
                speakerEvidenceMode = AuthoritySpeechEvidenceModeTokens.SpeakerAuthority,
                targetEvidenceMode = AuthoritySpeechEvidenceModeTokens.None,
                participantEvidenceMode = AuthoritySpeechEvidenceModeTokens.SharedAuthority,
                spectatorEvidenceMode = AuthoritySpeechEvidenceModeTokens.SharedAuthority,
                speakerMaximumSelectedStances = 1,
                speakerMaximumSupportingMemes = 1,
                speakerMaximumContextCharacters = 640,
                speakerIncludeRole = true,
                speakerIncludeCertainty = true,
                speakerIncludeStructure = true,
                speakerIncludeDeity = false,
                speakerIncludeNarrativeInterpretation = true,
                witnessMaximumSelectedStances = 1,
                witnessMaximumSupportingMemes = 0,
                witnessMaximumContextCharacters = 320,
                witnessIncludeRole = false,
                witnessIncludeCertainty = false,
                witnessIncludeStructure = false,
                witnessIncludeDeity = false,
                witnessIncludeNarrativeInterpretation = false,
                speakerPromptInstruction = "Use only speaker authority facts.",
                witnessPromptInstruction = "Use only shared witness doctrine."
            };
            builder.routes.Add(new AuthoritySpeechRouteBuilder
            {
                ritualDefName = "ThroneSpeech",
                behaviorWorkerClassName = "RimWorld.RitualBehaviorWorker_ThroneSpeech",
                outcomeWorkerClassName = SpeechOutcomeTypeName,
                downstreamGroupDefName = "ritualRoyal",
                speakerRoleId = "speaker",
                requiresRoyalty = true
            });
            builder.routes.Add(new AuthoritySpeechRouteBuilder
            {
                ritualDefName = "LeaderSpeech",
                behaviorWorkerClassName = "RimWorld.RitualBehaviorWorker_Speech",
                outcomeWorkerClassName = SpeechOutcomeTypeName,
                downstreamGroupDefName = "ritualFinished",
                speakerRoleId = "speaker",
                requiresRoyalty = false
            });
            return builder;
        }

        private static NarrativeEvidence CopyNarrative(NarrativeEvidence source)
        {
            return new NarrativeEvidence
            {
                eventId = source.eventId,
                tick = source.tick,
                povPawnId = source.povPawnId,
                povRole = source.povRole,
                facet = source.facet,
                phase = source.phase,
                subjectKind = source.subjectKind,
                subjectId = source.subjectId,
                subjectLabel = source.subjectLabel,
                arcKey = source.arcKey,
                relatedEventId = source.relatedEventId,
                beliefTopics = new List<string>(source.beliefTopics),
                salience = source.salience,
                pawnCanKnow = source.pawnCanKnow,
                sourceDomain = source.sourceDomain,
                sourceDefName = source.sourceDefName
            };
        }

        private static BeliefMutationSnapshot Mutation(
            string pawnId,
            int tick,
            string beforeIdeologyId,
            string afterIdeologyId,
            float beforeCertainty,
            float afterCertainty,
            string causeToken,
            bool? conversionSucceeded,
            bool ideologyChanged)
        {
            BeliefMutationSnapshot result = new BeliefMutationSnapshot
            {
                pawnId = pawnId,
                capturedTick = tick,
                beforeIdeologyId = beforeIdeologyId,
                beforeIdeologyName = beforeIdeologyId + " name",
                afterIdeologyId = afterIdeologyId,
                afterIdeologyName = afterIdeologyId + " name",
                hasBeforeCertainty = true,
                beforeCertainty = beforeCertainty,
                hasAfterCertainty = true,
                afterCertainty = afterCertainty,
                certaintyChanged = Math.Abs(afterCertainty - beforeCertainty) > 0.0001f,
                ideologyChanged = ideologyChanged,
                conversionSucceeded = conversionSucceeded,
                observedMutation = ideologyChanged
                    || Math.Abs(afterCertainty - beforeCertainty) > 0.0001f
                    || conversionSucceeded.HasValue,
                startedSequence = 1,
                completedSequence = 2
            };
            result.causeTokens.Add(causeToken);
            return result;
        }

        private static BeliefObservationDecision ObserveDelta(
            float before, float after, string beforeIdeology, string afterIdeology, BeliefPolicySnapshot policy)
        {
            return BeliefReflectionPolicy.Observe(new BeliefObservationRequest
            {
                featureAvailable = true,
                baselineOnNextScan = false,
                hasCurrent = true,
                currentIdeologyId = afterIdeology,
                currentCertainty = after,
                hasPrevious = true,
                previousIdeologyId = beforeIdeology,
                previousCertainty = before
            }, policy);
        }

        private static BeliefTrackerObservation Tracker(
            string ideologyId, string ideologyName, float certainty)
        {
            return new BeliefTrackerObservation
            {
                hasCurrent = true,
                ideologyId = ideologyId,
                ideologyName = ideologyName,
                certainty = certainty
            };
        }

        private static BeliefReflectionRequest ReflectionRequest()
        {
            return new BeliefReflectionRequest
            {
                groupEnabled = true,
                signalEnabled = true,
                hasBeliefContext = true,
                nowTick = 1000000,
                lastReflectionTick = -1,
                reflectionsThisQuadrum = 0
            };
        }

        private static bool Contains(IList<string> values, string wanted)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i], wanted, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool ContainsMeme(IList<BeliefMemeFact> values, string wanted)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i].defName, wanted, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void AssertSelected(string name, BeliefStanceResolution actual, string expectedDefName)
        {
            AssertTrue(name + " has one selected stance", actual != null && actual.stances.Count > 0);
            AssertEqual(name, expectedDefName, actual.stances[0].precept.defName);
        }

        private static void AssertEmpty(string name, BeliefStanceResolution actual)
        {
            AssertTrue(name, actual != null && !actual.HasUsefulContext && actual.stances.Count == 0);
        }

        private static void AssertEmpty(string name, BeliefStanceResolution actual, BeliefPolicySnapshot policy)
        {
            AssertEqual(name, string.Empty,
                BeliefContextFormatter.Format(actual, NarrativeDetailLevelTokens.Full, policy));
        }

        private static void AssertContains(string name, string actual, string expected)
        {
            AssertTrue(name, actual != null && actual.IndexOf(expected, StringComparison.Ordinal) >= 0);
        }

        private static void AssertNear(string name, float expected, float actual)
        {
            assertions++;
            if (Math.Abs(expected - actual) > 0.0001f)
                throw new InvalidOperationException(name + ": expected " + expected + ", got " + actual + ".");
        }

        private static void AssertEqual<T>(string name, T expected, T actual)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(name + ": expected '" + expected + "', got '" + actual + "'.");
        }

        private static void AssertTrue(string name, bool condition)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(name + ": expected true.");
        }
    }
}

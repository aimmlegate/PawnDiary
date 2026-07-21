// Standalone, no-RimWorld checks for the shared Narrative Continuity layer through N3-A's visible
// Anomaly provider, N3-B identity, and N3-O's exact-map seasonal-flood pressure. The project
// file links only pure source, making any accidental Verse/Unity/DLC dependency a compile-time failure.
using System;
using System.Collections.Generic;
using PawnDiary;
using PawnDiary.Capture;

namespace NarrativeContinuityTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestHardGatesFailClosed();
            TestScorePrecedenceAndTerminalRelevance();
            TestRepetitionPenaltyAndExactArcExemption();
            TestCategoryCapsAndStableTieBreak();
            TestTopicRedundancyKeepsNewFacts();
            TestDetailCapsAndCompleteFactBudget();
            TestReferenceEqualityAndDeduplication();
            TestPersistenceCapsAndPromptFormatting();
            TestRoyaltyProviderApplicabilityAndBounds();
            TestBiotechProviderApplicabilityAndTruthGates();
            TestAnomalyProviderVisibleMappingsAndGates();
            TestOdysseyProviderEvidenceAndCrossDlcGates();
            TestOdysseyEnvironmentalPressureGatesAndComposition();
            TestReflectionPriorityAndDeferredConsumption();
            Console.WriteLine("NarrativeContinuityTests passed " + assertions + " assertions.");
            return 0;
        }

        private static void TestHardGatesFailClosed()
        {
            NarrativePolicySnapshot policy = NarrativePolicySnapshot.CreateDefault();
            policy.maximumCandidateAgeTicks = 100;
            NarrativeContextRequest request = new NarrativeContextRequest
            {
                policy = policy,
                currentTick = 1000,
                evidence = new List<NarrativeEvidence> { Evidence() },
                candidates = new List<NarrativeLensCandidate>
                {
                    Candidate("unknown", NarrativeCategoryTokens.Identity, NarrativeFacetTokens.IdentityTransition,
                        pawnCanKnow: false),
                    Candidate(string.Empty, NarrativeCategoryTokens.Bond, NarrativeFacetTokens.IdentityTransition),
                    Candidate("empty-text", NarrativeCategoryTokens.Bond, NarrativeFacetTokens.IdentityTransition,
                        text: " "),
                    Candidate("future", NarrativeCategoryTokens.Bond, NarrativeFacetTokens.IdentityTransition,
                        sourceTick: 1001),
                    Candidate("old", NarrativeCategoryTokens.Bond, NarrativeFacetTokens.IdentityTransition,
                        sourceTick: 1),
                    Candidate("primary", NarrativeCategoryTokens.Bond, NarrativeFacetTokens.IdentityTransition,
                        primaryFact: true),
                    Candidate("unrelated", NarrativeCategoryTokens.Bond, NarrativeFacetTokens.JourneyChapter)
                }
            };

            NarrativeContextSelection selection = NarrativeContextSelector.Select(request);
            AssertEqual("hard gates select no invalid candidate", 0, selection.selectedCandidates.Count);
            AssertDiagnostic(selection, "unknown", NarrativeDiagnosticTokens.UnknownKnowledge);
            AssertDiagnostic(selection, string.Empty, NarrativeDiagnosticTokens.EmptyCandidateKey);
            AssertDiagnostic(selection, "empty-text", NarrativeDiagnosticTokens.EmptyCandidateText);
            AssertDiagnostic(selection, "future", NarrativeDiagnosticTokens.FutureSource);
            AssertDiagnostic(selection, "old", NarrativeDiagnosticTokens.TooOld);
            AssertDiagnostic(selection, "primary", NarrativeDiagnosticTokens.PrimaryFactDuplicate);
            AssertDiagnostic(selection, "unrelated", NarrativeDiagnosticTokens.Unrelated);
            AssertEqual("authorized evidence still produces one future reference", 1, selection.references.Count);

            NarrativeContextSelection unknownEvidence = NarrativeContextSelector.Select(new NarrativeContextRequest
            {
                evidence = new List<NarrativeEvidence> { new NarrativeEvidence { facet = NarrativeFacetTokens.IdentityTransition } }
            });
            AssertEqual("unknown evidence knowledge fails closed", 0, unknownEvidence.selectedCandidates.Count);
            AssertTrue("unknown evidence reports no usable evidence",
                unknownEvidence.selectionReasons.Contains(NarrativeDiagnosticTokens.NoEvidence));
        }

        private static void TestBiotechProviderApplicabilityAndTruthGates()
        {
            AssertEqual("known birth is strongest family continuity",
                BiotechNarrativeContinuityTokens.SinceBirth,
                BiotechNarrativeProvider.FamilyContinuity(true, true, true));
            AssertEqual("observed childhood does not require a claimed parent",
                BiotechNarrativeContinuityTokens.ObservedChildhood,
                BiotechNarrativeProvider.FamilyContinuity(false, true, false));
            AssertEqual("exact current parent relation supplies baseline continuity",
                BiotechNarrativeContinuityTokens.BaselineFamily,
                BiotechNarrativeProvider.FamilyContinuity(false, false, true));
            AssertEqual("child-only arc invents no family continuity", string.Empty,
                BiotechNarrativeProvider.FamilyContinuity(false, false, false));

            NarrativeEvidence evidence = Evidence();
            evidence.arcKey = "biotech-family|child-1";
            BiotechNarrativeSnapshot snapshot = new BiotechNarrativeSnapshot
            {
                providerAvailable = true,
                childId = "pawn-1",
                familyArcId = evidence.arcKey,
                familyContinuity = BiotechNarrativeContinuityTokens.ObservedChildhood,
                familyText = "A directly observed childhood continues through this growth.",
                xenotypeDefName = "Yttakin",
                identityText = "The current visible xenotype is Yttakin.",
                sourceTick = 1000,
                pawnCanKnow = true,
                hasVerifiedPovConnection = true
            };

            List<NarrativeLensCandidate> candidates = BiotechNarrativeProvider.Build(
                new List<NarrativeEvidence> { evidence }, snapshot);
            AssertEqual("matching Biotech evidence yields bounded family plus identity candidates", 2,
                candidates.Count);
            AssertEqual("family candidate uses exact saved arc", evidence.arcKey, candidates[0].arcKey);
            AssertEqual("identity candidate uses exact child subject", "pawn-1", candidates[1].subjectId);
            AssertEqual("legacy N2 identity key falls back to the xenotype defName",
                "biotech|identity|pawn-1|Yttakin", candidates[1].candidateKey);
            AssertTrue("provider never emits gene-list text",
                candidates[0].text.IndexOf("gene", StringComparison.OrdinalIgnoreCase) < 0
                && candidates[1].text.IndexOf("gene", StringComparison.OrdinalIgnoreCase) < 0);

            snapshot.identityStableKey = "gene|Gene_FireSpew";
            snapshot.identityText = "Ari's visible identity change centered on fire spew.";
            snapshot.identityTopicTokens = new List<string> { "gene" };
            List<NarrativeLensCandidate> geneCandidates = BiotechNarrativeProvider.Build(
                new List<NarrativeEvidence> { evidence }, snapshot);
            NarrativeLensCandidate geneIdentity = geneCandidates[1];
            AssertEqual("N3-B stable key identifies one salient gene rather than full membership",
                "biotech|identity|pawn-1|gene|Gene_FireSpew", geneIdentity.candidateKey);
            AssertTrue("N3-B gene candidate keeps bounded identity and gene topics",
                geneIdentity.topicTokens.Contains("identity")
                    && geneIdentity.topicTokens.Contains("gene")
                    && geneIdentity.topicTokens.Count == 2);

            NarrativePolicySnapshot repetitionPolicy = NarrativePolicySnapshot.CreateDefault();
            repetitionPolicy.maxSelectedCandidates = 2;
            Budget(repetitionPolicy, NarrativeDetailLevelTokens.Full).maxLenses = 2;
            Budget(repetitionPolicy, NarrativeDetailLevelTokens.Full).characterBudget = 1000;
            Func<List<string>, NarrativeContextSelection> selectGene = recentKeys =>
                NarrativeContextSelector.Select(new NarrativeContextRequest
                {
                    policy = repetitionPolicy,
                    detailLevel = NarrativeDetailLevelTokens.Full,
                    currentTick = 1000,
                    evidence = new List<NarrativeEvidence> { evidence },
                    recentSelectedCandidateKeys = recentKeys ?? new List<string>(),
                    candidates = new List<NarrativeLensCandidate>
                    {
                        geneIdentity,
                        Candidate("fresh-bonding-topic", NarrativeCategoryTokens.Chapter,
                            NarrativeFacetTokens.JourneyChapter,
                            topics: new List<string> { "bonding" }, sourceTick: 1000)
                    }
                });
            AssertEqual("fresh exact gene identity outranks a direct topic",
                geneIdentity.candidateKey, selectGene(null).selectedCandidates[0].candidateKey);
            AssertEqual("persisted exact gene key activates ordinary repetition penalty",
                "fresh-bonding-topic",
                selectGene(new List<string> { geneIdentity.candidateKey })
                    .selectedCandidates[0].candidateKey);

            snapshot.providerAvailable = false;
            AssertEqual("inactive Biotech provider is silent", 0,
                BiotechNarrativeProvider.Build(new List<NarrativeEvidence> { evidence }, snapshot).Count);
            snapshot.providerAvailable = true;
            snapshot.hasVerifiedPovConnection = false;
            AssertEqual("unconnected POV is silent", 0,
                BiotechNarrativeProvider.Build(new List<NarrativeEvidence> { evidence }, snapshot).Count);
            snapshot.hasVerifiedPovConnection = true;
            AssertEqual("unrelated evidence is silent", 0,
                BiotechNarrativeProvider.Build(new List<NarrativeEvidence>
                {
                    new NarrativeEvidence
                    {
                        facet = NarrativeFacetTokens.JourneyChapter,
                        subjectKind = NarrativeSubjectKindTokens.Ship,
                        subjectId = "ship-2",
                        pawnCanKnow = true
                    }
                }, snapshot).Count);

            List<NarrativeLensCandidate> fixedOrder = NarrativeProviderOrchestrator.Collect(
                new List<NarrativeEvidence> { evidence },
                new List<NarrativeLensCandidate> { Candidate("core-first", NarrativeCategoryTokens.Home,
                    NarrativeFacetTokens.IdentityTransition) },
                null,
                snapshot,
                null,
                null);
            AssertEqual("fixed provider list preserves core-first deterministic order", "core-first",
                fixedOrder[0].candidateKey);
            AssertEqual("empty future provider stubs add no candidates", 3, fixedOrder.Count);
        }

        private static void TestAnomalyProviderVisibleMappingsAndGates()
        {
            // Capture and provider layers deliberately stay assembly-independent, so pin their copied
            // source/phase tokens here. A future schema edit must not silently disable a visible lens.
            AssertEqual("ghoul kind token stays aligned", AnomalyKindTokens.GhoulTransformation,
                AnomalyNarrativeContinuityTokens.GhoulTransformation);
            AssertEqual("containment kind token stays aligned", AnomalyKindTokens.ContainmentBreach,
                AnomalyNarrativeContinuityTokens.ContainmentBreach);
            AssertEqual("creepjoiner kind token stays aligned", AnomalyKindTokens.CreepJoinerOutcome,
                AnomalyNarrativeContinuityTokens.CreepJoinerOutcome);
            AssertEqual("ghoul source Def token stays aligned", AnomalyEventDefNames.GhoulTransformation,
                AnomalyNarrativeContinuityTokens.GhoulSourceDefName);
            AssertEqual("containment source Def token stays aligned", AnomalyEventDefNames.ContainmentBreach,
                AnomalyNarrativeContinuityTokens.ContainmentSourceDefName);
            AssertEqual("creepjoiner source Def token stays aligned", AnomalyEventDefNames.CreepJoinerOutcome,
                AnomalyNarrativeContinuityTokens.CreepJoinerSourceDefName);
            AssertEqual("surgical reveal phase stays aligned", AnomalyOutcomeTokens.SurgicalReveal,
                AnomalyNarrativeContinuityTokens.SurgicalReveal);
            AssertEqual("rejected phase stays aligned", AnomalyOutcomeTokens.Rejected,
                AnomalyNarrativeContinuityTokens.Rejected);
            AssertEqual("aggressive phase stays aligned", AnomalyOutcomeTokens.Aggressive,
                AnomalyNarrativeContinuityTokens.Aggressive);
            AssertEqual("departed phase stays aligned", AnomalyOutcomeTokens.Departed,
                AnomalyNarrativeContinuityTokens.Departed);

            AnomalyNarrativeFact ghoul = AnomalyFact(
                AnomalyNarrativeContinuityTokens.GhoulTransformation,
                NarrativeFacetTokens.IdentityTransition,
                AnomalyNarrativeContinuityTokens.Transformed,
                NarrativeSubjectKindTokens.Pawn,
                "pawn-ghoul",
                string.Empty,
                "Ari visibly completed an irreversible ghoul transformation.");
            AssertAnomalyMapping(
                "visible ghoul transformation maps to exact-subject identity",
                ghoul,
                "anomaly|identity|ghoul|pawn-ghoul|transformed",
                NarrativeCategoryTokens.Identity,
                NarrativeRelationshipTokens.ExactSubject);

            AnomalyNarrativeFact containment = AnomalyFact(
                AnomalyNarrativeContinuityTokens.ContainmentBreach,
                NarrativeFacetTokens.AmbientPressure,
                AnomalyNarrativeContinuityTokens.Breached,
                NarrativeSubjectKindTokens.Entity,
                "Thing_77",
                "anomaly-breach|3|900|Thing_77",
                "A visibly contained entity escaped on this map.");
            AssertAnomalyMapping(
                "visible containment breach maps to exact-arc pressure",
                containment,
                "anomaly|pressure|breach|3|900|Thing_77",
                NarrativeCategoryTokens.Pressure,
                NarrativeRelationshipTokens.ExactArc);

            string[] monolithPhases =
            {
                AnomalyNarrativeContinuityTokens.Stirring,
                AnomalyNarrativeContinuityTokens.Waking,
                AnomalyNarrativeContinuityTokens.VoidAwakened
            };
            AssertTrue("Stirring accepts only its shipped window/source identity",
                AnomalyNarrativeContinuityTokens.MatchesVisibleMonolithSource(
                    AnomalyNarrativeContinuityTokens.MonolithStirringWindowDefName,
                    AnomalyNarrativeContinuityTokens.Stirring,
                    AnomalyNarrativeContinuityTokens.MonolithStirringSourceDefName));
            AssertTrue("Waking accepts only its shipped window/source identity",
                AnomalyNarrativeContinuityTokens.MatchesVisibleMonolithSource(
                    AnomalyNarrativeContinuityTokens.MonolithWakingWindowDefName,
                    AnomalyNarrativeContinuityTokens.Waking,
                    AnomalyNarrativeContinuityTokens.MonolithWakingSourceDefName));
            AssertTrue("Void Awakened accepts only its shipped window/source identity",
                AnomalyNarrativeContinuityTokens.MatchesVisibleMonolithSource(
                    AnomalyNarrativeContinuityTokens.MonolithVoidAwakenedWindowDefName,
                    AnomalyNarrativeContinuityTokens.VoidAwakened,
                    AnomalyNarrativeContinuityTokens.MonolithVoidAwakenedSourceDefName));
            AssertTrue("swapped monolith window identity fails closed",
                !AnomalyNarrativeContinuityTokens.MatchesVisibleMonolithSource(
                    AnomalyNarrativeContinuityTokens.MonolithWakingWindowDefName,
                    AnomalyNarrativeContinuityTokens.Stirring,
                    AnomalyNarrativeContinuityTokens.MonolithStirringSourceDefName));
            AssertTrue("all visible monolith phase/source pairs share one exact matcher",
                AnomalyNarrativeContinuityTokens.MatchesVisibleMonolithPhaseSource(
                    AnomalyNarrativeContinuityTokens.Stirring,
                    AnomalyNarrativeContinuityTokens.MonolithStirringSourceDefName)
                && AnomalyNarrativeContinuityTokens.MatchesVisibleMonolithPhaseSource(
                    AnomalyNarrativeContinuityTokens.Waking,
                    AnomalyNarrativeContinuityTokens.MonolithWakingSourceDefName)
                && AnomalyNarrativeContinuityTokens.MatchesVisibleMonolithPhaseSource(
                    AnomalyNarrativeContinuityTokens.VoidAwakened,
                    AnomalyNarrativeContinuityTokens.MonolithVoidAwakenedSourceDefName));
            AssertTrue("Void Awakened rejects a swapped reached-level source",
                !AnomalyNarrativeContinuityTokens.MatchesVisibleMonolithPhaseSource(
                    AnomalyNarrativeContinuityTokens.VoidAwakened,
                    AnomalyNarrativeContinuityTokens.MonolithWakingSourceDefName));
            AssertTrue("blank monolith phase/source identity fails closed",
                !AnomalyNarrativeContinuityTokens.MatchesVisibleMonolithPhaseSource(
                    AnomalyNarrativeContinuityTokens.Stirring, null));
            for (int i = 0; i < monolithPhases.Length; i++)
            {
                string phase = monolithPhases[i];
                AssertAnomalyMapping(
                    "visible monolith chapter maps exactly: " + phase,
                    AnomalyFact(
                        AnomalyNarrativeContinuityTokens.MonolithChapter,
                        NarrativeFacetTokens.JourneyChapter,
                        phase,
                        string.Empty,
                        string.Empty,
                        "anomaly-monolith|2",
                        "The monolith visibly reached " + phase + "."),
                    "anomaly|chapter|monolith|2|" + phase,
                    NarrativeCategoryTokens.Chapter,
                    NarrativeRelationshipTokens.ExactArc);
            }

            string[] creepPhases =
            {
                AnomalyNarrativeContinuityTokens.SurgicalReveal,
                AnomalyNarrativeContinuityTokens.Rejected,
                AnomalyNarrativeContinuityTokens.Aggressive,
                AnomalyNarrativeContinuityTokens.Departed
            };
            for (int i = 0; i < creepPhases.Length; i++)
            {
                string phase = creepPhases[i];
                NarrativeLensCandidate creepCandidate = AssertAnomalyMapping(
                    "verified visible creepjoiner outcome maps exactly: " + phase,
                    AnomalyFact(
                        AnomalyNarrativeContinuityTokens.CreepJoinerOutcome,
                        NarrativeFacetTokens.IdentityTransition,
                        phase,
                        NarrativeSubjectKindTokens.Pawn,
                        "pawn-creep",
                        string.Empty,
                        "The visible creepjoiner result was " + phase + "."),
                    "anomaly|identity|creepjoiner|pawn-creep|" + phase,
                    NarrativeCategoryTokens.Identity,
                    NarrativeRelationshipTokens.ExactSubject);
                AssertEqual("visible creepjoiner salience is phase-specific: " + phase,
                    phase == AnomalyNarrativeContinuityTokens.SurgicalReveal
                        ? NarrativeSalienceTokens.Meaningful
                        : NarrativeSalienceTokens.Major,
                    creepCandidate.salience);
            }

            NarrativeEvidence ghoulEvidence = AnomalyEvidence(ghoul);
            AnomalyNarrativeSnapshot snapshot = AnomalySnapshot(ghoul);
            snapshot.providerAvailable = false;
            AssertEqual("inactive Anomaly provider is silent", 0,
                AnomalyNarrativeProvider.Build(new List<NarrativeEvidence> { ghoulEvidence }, snapshot).Count);
            snapshot.providerAvailable = true;
            snapshot.pawnCanKnow = false;
            AssertEqual("unknown Anomaly snapshot is silent", 0,
                AnomalyNarrativeProvider.Build(new List<NarrativeEvidence> { ghoulEvidence }, snapshot).Count);
            snapshot.pawnCanKnow = true;
            snapshot.hasVerifiedPovConnection = false;
            AssertEqual("unverified Anomaly POV connection is silent", 0,
                AnomalyNarrativeProvider.Build(new List<NarrativeEvidence> { ghoulEvidence }, snapshot).Count);
            snapshot.hasVerifiedPovConnection = true;
            ghoulEvidence.pawnCanKnow = null;
            AssertEqual("unknown evidence knowledge fails closed", 0,
                AnomalyNarrativeProvider.Build(new List<NarrativeEvidence> { ghoulEvidence }, snapshot).Count);
            ghoulEvidence.pawnCanKnow = false;
            AssertEqual("denied evidence knowledge fails closed", 0,
                AnomalyNarrativeProvider.Build(new List<NarrativeEvidence> { ghoulEvidence }, snapshot).Count);
            ghoulEvidence.pawnCanKnow = true;

            Action<string, Action<NarrativeEvidence>> rejectEvidence = (label, mutate) =>
            {
                NarrativeEvidence mismatched = AnomalyEvidence(ghoul);
                mutate(mismatched);
                AssertEqual(label, 0, AnomalyNarrativeProvider.Build(
                    new List<NarrativeEvidence> { mismatched }, AnomalySnapshot(ghoul)).Count);
            };
            rejectEvidence("different POV cannot receive an Anomaly lens", row => row.povPawnId = "other");
            rejectEvidence("mismatched exact subject fails closed", row => row.subjectId = "other");
            rejectEvidence("mismatched facet fails closed", row => row.facet = NarrativeFacetTokens.JourneyChapter);
            rejectEvidence("mismatched phase fails closed", row => row.phase = "other");
            rejectEvidence("mismatched source domain fails closed", row => row.sourceDomain = "test");
            rejectEvidence("mismatched source def fails closed", row => row.sourceDefName = "Other");
            Action<string, AnomalyNarrativeFact> rejectWrongSourceDef = (label, fact) =>
            {
                NarrativeEvidence wrongSource = AnomalyEvidence(fact);
                wrongSource.sourceDefName = "Other";
                AssertEqual(label, 0, AnomalyNarrativeProvider.Build(
                    new List<NarrativeEvidence> { wrongSource }, AnomalySnapshot(fact)).Count);
            };
            rejectWrongSourceDef("containment rejects a mismatched source Def", containment);
            rejectWrongSourceDef("creepjoiner rejects a mismatched source Def", AnomalyFact(
                AnomalyNarrativeContinuityTokens.CreepJoinerOutcome,
                NarrativeFacetTokens.IdentityTransition,
                AnomalyNarrativeContinuityTokens.SurgicalReveal,
                NarrativeSubjectKindTokens.Pawn,
                "pawn-creep",
                string.Empty,
                "A visible disclosure occurred."));
            rejectWrongSourceDef("monolith rejects a mismatched reached-level Def", AnomalyFact(
                AnomalyNarrativeContinuityTokens.MonolithChapter,
                NarrativeFacetTokens.JourneyChapter,
                AnomalyNarrativeContinuityTokens.VoidAwakened,
                string.Empty,
                string.Empty,
                "anomaly-monolith|2",
                "The monolith visibly reached Void Awakened."));
            NarrativeEvidence wrongMonolithArc = AnomalyEvidence(AnomalyFact(
                AnomalyNarrativeContinuityTokens.MonolithChapter,
                NarrativeFacetTokens.JourneyChapter,
                AnomalyNarrativeContinuityTokens.Waking,
                string.Empty,
                string.Empty,
                "anomaly-monolith|2",
                "Visible chapter."));
            wrongMonolithArc.arcKey = "anomaly-monolith|3";
            AssertEqual("mismatched monolith evidence arc fails closed", 0,
                AnomalyNarrativeProvider.Build(
                    new List<NarrativeEvidence> { wrongMonolithArc },
                    AnomalySnapshot(AnomalyFact(
                        AnomalyNarrativeContinuityTokens.MonolithChapter,
                        NarrativeFacetTokens.JourneyChapter,
                        AnomalyNarrativeContinuityTokens.Waking,
                        string.Empty,
                        string.Empty,
                        "anomaly-monolith|2",
                        "Visible chapter."))).Count);
            NarrativeEvidence wrongContainmentArc = AnomalyEvidence(containment);
            wrongContainmentArc.arcKey = "anomaly-breach|3|901|Thing_77";
            AssertEqual("mismatched containment evidence arc fails closed", 0,
                AnomalyNarrativeProvider.Build(
                    new List<NarrativeEvidence> { wrongContainmentArc },
                    AnomalySnapshot(containment)).Count);

            AssertEqual("null Anomaly evidence is harmless", 0,
                AnomalyNarrativeProvider.Build(null, snapshot).Count);
            AssertEqual("null Anomaly snapshot is harmless", 0,
                AnomalyNarrativeProvider.Build(new List<NarrativeEvidence> { ghoulEvidence }, null).Count);
            AssertEqual("empty Anomaly evidence is harmless", 0,
                AnomalyNarrativeProvider.Build(new List<NarrativeEvidence>(), snapshot).Count);
            AnomalyNarrativeSnapshot blankPovSnapshot = AnomalySnapshot(ghoul);
            blankPovSnapshot.povPawnId = " ";
            AssertEqual("blank Anomaly POV identity fails closed", 0,
                AnomalyNarrativeProvider.Build(
                    new List<NarrativeEvidence> { ghoulEvidence }, blankPovSnapshot).Count);
            AnomalyNarrativeSnapshot emptyFactsSnapshot = AnomalySnapshot();
            AssertEqual("empty Anomaly fact list is harmless", 0,
                AnomalyNarrativeProvider.Build(
                    new List<NarrativeEvidence> { ghoulEvidence }, emptyFactsSnapshot).Count);
            snapshot.facts = null;
            AssertEqual("null Anomaly fact list is harmless", 0,
                AnomalyNarrativeProvider.Build(new List<NarrativeEvidence> { ghoulEvidence }, snapshot).Count);

            string[] hiddenCreepPhases =
            {
                "joined", "embraced", "benefit", "downside", "infection", "infiltrator", "unresolved"
            };
            for (int i = 0; i < hiddenCreepPhases.Length; i++)
            {
                AnomalyNarrativeFact hidden = AnomalyFact(
                    AnomalyNarrativeContinuityTokens.CreepJoinerOutcome,
                    NarrativeFacetTokens.IdentityTransition,
                    hiddenCreepPhases[i],
                    NarrativeSubjectKindTokens.Pawn,
                    "pawn-creep",
                    string.Empty,
                    "Hidden state must not become a candidate.");
                AssertEqual("hidden creepjoiner phase is silent: " + hidden.phase, 0,
                    AnomalyNarrativeProvider.Build(
                        new List<NarrativeEvidence> { AnomalyEvidence(hidden) },
                        AnomalySnapshot(hidden)).Count);
            }
            string[] terminalOrUnsupportedMonolith = { "gleaming", "embraced", "disrupted", "terminal" };
            for (int i = 0; i < terminalOrUnsupportedMonolith.Length; i++)
            {
                AnomalyNarrativeFact hidden = AnomalyFact(
                    AnomalyNarrativeContinuityTokens.MonolithChapter,
                    NarrativeFacetTokens.JourneyChapter,
                    terminalOrUnsupportedMonolith[i],
                    string.Empty,
                    string.Empty,
                    "anomaly-monolith|0",
                    "Unsupported terminal state.");
                AssertEqual("unsupported or terminal monolith phase is silent: " + hidden.phase, 0,
                    AnomalyNarrativeProvider.Build(
                        new List<NarrativeEvidence> { AnomalyEvidence(hidden) },
                        AnomalySnapshot(hidden)).Count);
            }

            Action<string, AnomalyNarrativeFact> rejectFact = (label, fact) => AssertEqual(label, 0,
                AnomalyNarrativeProvider.Build(
                    new List<NarrativeEvidence> { AnomalyEvidence(fact) },
                    AnomalySnapshot(fact)).Count);
            rejectFact("ghoul requires a pawn subject kind", AnomalyFact(
                ghoul.sourceKind, ghoul.facet, ghoul.phase, NarrativeSubjectKindTokens.Entity,
                ghoul.subjectId, ghoul.arcKey, ghoul.text));
            rejectFact("containment requires an entity subject kind", AnomalyFact(
                containment.sourceKind, containment.facet, containment.phase,
                NarrativeSubjectKindTokens.Pawn, containment.subjectId, containment.arcKey,
                containment.text));
            rejectFact("creepjoiner requires a pawn subject kind", AnomalyFact(
                AnomalyNarrativeContinuityTokens.CreepJoinerOutcome,
                NarrativeFacetTokens.IdentityTransition,
                AnomalyNarrativeContinuityTokens.Rejected,
                NarrativeSubjectKindTokens.Entity,
                "pawn-creep",
                string.Empty,
                "A visible rejection occurred."));
            rejectFact("monolith requires an empty subject kind", AnomalyFact(
                AnomalyNarrativeContinuityTokens.MonolithChapter,
                NarrativeFacetTokens.JourneyChapter,
                AnomalyNarrativeContinuityTokens.Stirring,
                NarrativeSubjectKindTokens.Pawn,
                string.Empty,
                "anomaly-monolith|0",
                "The monolith visibly reached Stirring."));
            rejectFact("unknown Anomaly source kind fails closed", AnomalyFact(
                "void_outcome",
                NarrativeFacetTokens.IdentityTransition,
                "terminal",
                NarrativeSubjectKindTokens.Pawn,
                "pawn-void",
                string.Empty,
                "Unsupported terminal state."));
            rejectFact("blank subject key is rejected", AnomalyFact(
                ghoul.sourceKind, ghoul.facet, ghoul.phase, ghoul.subjectKind, " ", "", ghoul.text));
            rejectFact("whitespace subject key is rejected", AnomalyFact(
                ghoul.sourceKind, ghoul.facet, ghoul.phase, ghoul.subjectKind, "pawn bad", "", ghoul.text));
            rejectFact("separator subject key is rejected", AnomalyFact(
                ghoul.sourceKind, ghoul.facet, ghoul.phase, ghoul.subjectKind, "pawn|bad", "", ghoul.text));
            rejectFact("secondary separator subject key is rejected", AnomalyFact(
                ghoul.sourceKind, ghoul.facet, ghoul.phase, ghoul.subjectKind, "pawn;bad", "", ghoul.text));
            rejectFact("control-character subject key is rejected", AnomalyFact(
                ghoul.sourceKind, ghoul.facet, ghoul.phase, ghoul.subjectKind, "pawn\u0001bad", "", ghoul.text));
            rejectFact("overlong subject key is rejected", AnomalyFact(
                ghoul.sourceKind, ghoul.facet, ghoul.phase, ghoul.subjectKind, new string('x', 161), "", ghoul.text));
            rejectFact("blank narrative text is rejected", AnomalyFact(
                ghoul.sourceKind, ghoul.facet, ghoul.phase, ghoul.subjectKind, ghoul.subjectId, "", " "));
            rejectFact("control character narrative text is rejected", AnomalyFact(
                ghoul.sourceKind, ghoul.facet, ghoul.phase, ghoul.subjectKind, ghoul.subjectId, "", "bad\ntext"));
            rejectFact("overlong narrative text is rejected", AnomalyFact(
                ghoul.sourceKind, ghoul.facet, ghoul.phase, ghoul.subjectKind, ghoul.subjectId, "", new string('x', 513)));
            rejectFact("negative source tick is rejected", AnomalyFact(
                ghoul.sourceKind, ghoul.facet, ghoul.phase, ghoul.subjectKind, ghoul.subjectId, "", ghoul.text, -1));
            string[] badMonolithArcs =
            {
                "", "anomaly-monolith|-1", "anomaly-monolith|01", "anomaly-monolith|1|extra",
                "anomaly-monolith|99999999999"
            };
            for (int i = 0; i < badMonolithArcs.Length; i++)
            {
                rejectFact("malformed monolith arc is rejected: " + i, AnomalyFact(
                    AnomalyNarrativeContinuityTokens.MonolithChapter,
                    NarrativeFacetTokens.JourneyChapter,
                    AnomalyNarrativeContinuityTokens.Stirring,
                    string.Empty,
                    string.Empty,
                    badMonolithArcs[i],
                    "Visible chapter."));
            }
            rejectFact("containment arc must repeat exact escaped entity", AnomalyFact(
                containment.sourceKind, containment.facet, containment.phase, containment.subjectKind,
                containment.subjectId, "anomaly-breach|3|900|Thing_88", containment.text));
            string[] badContainmentArcs =
            {
                "anomaly-breach|03|900|Thing_77",
                "anomaly-breach|3|0900|Thing_77",
                "anomaly-breach|3|99999999999|Thing_77"
            };
            for (int i = 0; i < badContainmentArcs.Length; i++)
            {
                rejectFact("containment arc requires canonical map/tick integers: " + i, AnomalyFact(
                    containment.sourceKind,
                    containment.facet,
                    containment.phase,
                    containment.subjectKind,
                    containment.subjectId,
                    badContainmentArcs[i],
                    containment.text));
            }

            List<AnomalyNarrativeFact> manyFacts = new List<AnomalyNarrativeFact>();
            List<NarrativeEvidence> manyEvidence = new List<NarrativeEvidence>();
            for (int i = 6; i >= 1; i--)
            {
                AnomalyNarrativeFact fact = AnomalyFact(
                    AnomalyNarrativeContinuityTokens.GhoulTransformation,
                    NarrativeFacetTokens.IdentityTransition,
                    AnomalyNarrativeContinuityTokens.Transformed,
                    NarrativeSubjectKindTokens.Pawn,
                    "pawn-" + i,
                    string.Empty,
                    "Visible identity transition " + i + ".");
                manyFacts.Add(fact);
                manyEvidence.Add(AnomalyEvidence(fact));
            }
            AnomalyNarrativeSnapshot manySnapshot = AnomalySnapshot();
            manySnapshot.facts = manyFacts;
            List<NarrativeLensCandidate> bounded =
                AnomalyNarrativeProvider.Build(manyEvidence, manySnapshot);
            AssertEqual("Anomaly identity candidates are defensively capped", 4, bounded.Count);
            AssertEqual("Anomaly output order ignores snapshot order",
                "anomaly|identity|ghoul|pawn-1|transformed", bounded[0].candidateKey);
            AssertEqual("Anomaly cap keeps deterministic lowest stable key",
                "anomaly|identity|ghoul|pawn-4|transformed", bounded[3].candidateKey);
            AnomalyNarrativeFact duplicateFirst = AnomalyFact(
                ghoul.sourceKind, ghoul.facet, ghoul.phase, ghoul.subjectKind, "pawn-duplicate", "",
                "Z duplicate text.");
            AnomalyNarrativeFact duplicateSecond = AnomalyFact(
                ghoul.sourceKind, ghoul.facet, ghoul.phase, ghoul.subjectKind, "pawn-duplicate", "",
                "A duplicate text.");
            AnomalyNarrativeSnapshot duplicateSnapshot = AnomalySnapshot(
                duplicateFirst, duplicateSecond);
            List<NarrativeLensCandidate> duplicate = AnomalyNarrativeProvider.Build(
                new List<NarrativeEvidence> { AnomalyEvidence(duplicateFirst) }, duplicateSnapshot);
            AssertEqual("duplicate Anomaly stable keys collapse", 1, duplicate.Count);
            AssertEqual("duplicate collapse is deterministic", "A duplicate text.", duplicate[0].text);

            List<AnomalyNarrativeFact> categoryFacts = new List<AnomalyNarrativeFact>();
            List<NarrativeEvidence> categoryEvidence = new List<NarrativeEvidence>();
            for (int i = 4; i >= 0; i--)
            {
                AnomalyNarrativeFact chapter = AnomalyFact(
                    AnomalyNarrativeContinuityTokens.MonolithChapter,
                    NarrativeFacetTokens.JourneyChapter,
                    AnomalyNarrativeContinuityTokens.Stirring,
                    string.Empty,
                    string.Empty,
                    "anomaly-monolith|" + i,
                    "Visible monolith chapter " + i + ".");
                categoryFacts.Add(chapter);
                categoryEvidence.Add(AnomalyEvidence(chapter));
            }
            for (int i = 3; i >= 1; i--)
            {
                string entityId = "Thing_" + i;
                AnomalyNarrativeFact pressure = AnomalyFact(
                    AnomalyNarrativeContinuityTokens.ContainmentBreach,
                    NarrativeFacetTokens.AmbientPressure,
                    AnomalyNarrativeContinuityTokens.Breached,
                    NarrativeSubjectKindTokens.Entity,
                    entityId,
                    "anomaly-breach|" + i + "|900|" + entityId,
                    "Visible containment pressure " + i + ".");
                categoryFacts.Add(pressure);
                categoryEvidence.Add(AnomalyEvidence(pressure));
            }
            AnomalyNarrativeSnapshot categorySnapshot = AnomalySnapshot();
            categorySnapshot.facts = categoryFacts;
            List<NarrativeLensCandidate> categoryBounded = AnomalyNarrativeProvider.Build(
                categoryEvidence, categorySnapshot);
            AssertEqual("chapter and pressure category caps compose", 4, categoryBounded.Count);
            AssertEqual("chapter cap keeps the first stable epoch",
                "anomaly|chapter|monolith|0|stirring", categoryBounded[0].candidateKey);
            AssertEqual("chapter cap retains exactly three deterministic epochs",
                "anomaly|chapter|monolith|2|stirring", categoryBounded[2].candidateKey);
            AssertEqual("pressure cap retains exactly one deterministic breach",
                "anomaly|pressure|breach|1|900|Thing_1", categoryBounded[3].candidateKey);

            NarrativeLensCandidate anomalyIdentity = AnomalyNarrativeProvider.Build(
                new List<NarrativeEvidence> { AnomalyEvidence(ghoul) }, AnomalySnapshot(ghoul))[0];
            NarrativePolicySnapshot repetitionPolicy = NarrativePolicySnapshot.CreateDefault();
            repetitionPolicy.maxSelectedCandidates = 2;
            Budget(repetitionPolicy, NarrativeDetailLevelTokens.Full).maxLenses = 2;
            Budget(repetitionPolicy, NarrativeDetailLevelTokens.Full).characterBudget = 1000;
            Func<List<string>, NarrativeContextSelection> selectAnomaly = recent =>
                NarrativeContextSelector.Select(new NarrativeContextRequest
                {
                    policy = repetitionPolicy,
                    detailLevel = NarrativeDetailLevelTokens.Full,
                    currentTick = 1000,
                    evidence = new List<NarrativeEvidence> { AnomalyEvidence(ghoul) },
                    recentSelectedCandidateKeys = recent ?? new List<string>(),
                    candidates = new List<NarrativeLensCandidate>
                    {
                        anomalyIdentity,
                        Candidate("fresh-body-topic", NarrativeCategoryTokens.Chapter,
                            NarrativeFacetTokens.JourneyChapter,
                            topics: new List<string> { "body" }, sourceTick: 1000)
                    }
                });
            AssertEqual("fresh exact Anomaly identity outranks a direct topic",
                anomalyIdentity.candidateKey, selectAnomaly(null).selectedCandidates[0].candidateKey);
            AssertEqual("persisted Anomaly key activates ordinary repetition penalty",
                "fresh-body-topic",
                selectAnomaly(new List<string> { anomalyIdentity.candidateKey })
                    .selectedCandidates[0].candidateKey);

            OdysseyNarrativeSnapshot odyssey = new OdysseyNarrativeSnapshot
            {
                providerAvailable = true,
                povPawnId = "pawn-1",
                shipStableId = "WorldObject_12",
                shipName = "Wayfarer",
                journeyId = "odyssey-journey|WorldObject_12|800",
                locationKey = "planet-layer-1-tile-42",
                locationLabel = "Frozen plain",
                homeText = "At this event, the writer was aboard Wayfarer at the frozen plain.",
                sourceTick = 1000,
                pawnCanKnow = true,
                hasVerifiedPovConnection = true
            };
            List<NarrativeLensCandidate> composed = NarrativeProviderOrchestrator.Collect(
                new List<NarrativeEvidence> { AnomalyEvidence(ghoul) },
                new List<NarrativeLensCandidate>(), null, null, AnomalySnapshot(ghoul), odyssey);
            AssertEqual("Anomaly composes with another available provider", 2, composed.Count);
            AssertEqual("fixed provider order keeps Anomaly before Odyssey",
                NarrativeProviderTokens.Anomaly, composed[0].provider);
            AssertEqual("Odyssey remains independently available after Anomaly",
                NarrativeProviderTokens.Odyssey, composed[1].provider);

            NarrativeContextSelection emptyProviderSelection = NarrativeContextSelector.Select(
                new NarrativeContextRequest
                {
                    policy = NarrativePolicySnapshot.CreateDefault(),
                    currentTick = 1000,
                    detailLevel = NarrativeDetailLevelTokens.Full,
                    evidence = new List<NarrativeEvidence> { AnomalyEvidence(ghoul) },
                    candidates = AnomalyNarrativeProvider.Build(
                        new List<NarrativeEvidence> { AnomalyEvidence(ghoul) }, null)
                });
            AssertEqual("empty Anomaly provider output selects no lens", 0,
                emptyProviderSelection.selectedCandidates.Count);
            AssertEqual("empty Anomaly provider output preserves source reference", 1,
                emptyProviderSelection.references.Count);
        }

        private static void TestRoyaltyProviderApplicabilityAndBounds()
        {
            NarrativeEvidence personaEvidence = Evidence();
            personaEvidence.povPawnId = "pawn-1";
            personaEvidence.facet = NarrativeFacetTokens.BondLifecycle;
            personaEvidence.subjectKind = NarrativeSubjectKindTokens.Weapon;
            personaEvidence.subjectId = "weapon-2";
            personaEvidence.arcKey = "royalty-persona|weapon-2|1";
            personaEvidence.beliefTopics = new List<string> { "weapons", "bonding", "loyalty" };

            RoyaltyNarrativeSnapshot snapshot = new RoyaltyNarrativeSnapshot
            {
                providerAvailable = true,
                povPawnId = "pawn-1",
                pawnCanKnow = true,
                hasVerifiedPovConnection = true,
                personaBonds = new List<RoyaltyPersonaNarrativeFact>
                {
                    PersonaFact("weapon-3", 1),
                    PersonaFact("weapon-2", 1),
                    PersonaFact("weapon-1", 1)
                },
                titles = new List<RoyaltyTitleNarrativeFact>
                {
                    TitleFact("Faction_2", "Baron", new List<string>
                        { "speech", "apparel", "speech", null })
                }
            };

            List<NarrativeLensCandidate> persona = RoyaltyNarrativeProvider.Build(
                new List<NarrativeEvidence> { personaEvidence }, snapshot);
            AssertEqual("persona provider admits only the exact weapon/arc", 1, persona.Count);
            AssertEqual("persona candidate preserves frozen Royalty arc grammar",
                personaEvidence.arcKey, persona[0].arcKey);
            AssertEqual("persona candidate key owns the exact bond epoch",
                "royalty|persona|weapon-2|1", persona[0].candidateKey);
            personaEvidence.arcKey = string.Empty;
            AssertEqual("weapon subject without the exact bond arc is insufficient", 0,
                RoyaltyNarrativeProvider.Build(new List<NarrativeEvidence> { personaEvidence }, snapshot).Count);
            personaEvidence.arcKey = "royalty-persona|weapon-2|1";
            snapshot.personaBonds.Add(new RoyaltyPersonaNarrativeFact
            {
                weaponThingId = "weapon-2",
                bondEpoch = 2,
                arcKey = "royalty-persona|different-weapon|2",
                text = "Malformed mismatched arc."
            });
            AssertEqual("mismatched persona arc grammar is rejected", 1,
                RoyaltyNarrativeProvider.Build(new List<NarrativeEvidence> { personaEvidence }, snapshot).Count);

            NarrativeEvidence titleEvidence = Evidence();
            titleEvidence.povPawnId = "pawn-1";
            titleEvidence.facet = NarrativeFacetTokens.IdentityTransition;
            titleEvidence.subjectKind = NarrativeSubjectKindTokens.Pawn;
            titleEvidence.subjectId = "pawn-1";
            titleEvidence.sourceDomain = "royalty_title";
            titleEvidence.beliefTopics = new List<string> { "authority", "status", "duty" };
            snapshot.titles.Add(TitleFact("Faction_2", "Baron", new List<string> { "speech" }));
            List<NarrativeLensCandidate> title = RoyaltyNarrativeProvider.Build(
                new List<NarrativeEvidence> { titleEvidence }, snapshot);
            AssertEqual("exact Royalty identity evidence admits one deduplicated current title", 1, title.Count);
            AssertEqual("title candidate is faction-specific and stable",
                "royalty|title|pawn-1|Faction_2|Baron", title[0].candidateKey);
            AssertTrue("title duties are deduplicated and deterministically ordered",
                title[0].topicTokens.Count == 5
                    && title[0].topicTokens[3] == "apparel"
                    && title[0].topicTokens[4] == "speech");

            NarrativeEvidence biotechIdentity = Evidence();
            biotechIdentity.povPawnId = "pawn-1";
            biotechIdentity.facet = NarrativeFacetTokens.IdentityTransition;
            biotechIdentity.subjectKind = NarrativeSubjectKindTokens.Pawn;
            biotechIdentity.subjectId = "pawn-1";
            biotechIdentity.sourceDomain = "biotech_gene";
            biotechIdentity.beliefTopics = new List<string> { "identity", "genes", "body" };
            AssertEqual("generic Biotech identity cannot pull unrelated Royalty title context", 0,
                RoyaltyNarrativeProvider.Build(new List<NarrativeEvidence> { biotechIdentity }, snapshot).Count);

            RoyaltyNarrativeSnapshot pressureSnapshot = new RoyaltyNarrativeSnapshot
            {
                providerAvailable = true,
                povPawnId = "pawn-1",
                pawnCanKnow = true,
                hasVerifiedPovConnection = true,
                courtPressure = new RoyaltyCourtPressureNarrativeFact
                {
                    arcPrefix = "court-ascent",
                    arcKey = "court-ascent|Quest_41",
                    text = "Exact active court pressure.",
                    sourceTick = 900
                }
            };
            AssertEqual("active pressure without exact Ascent or authority context stays silent", 0,
                RoyaltyNarrativeProvider.Build(
                    new List<NarrativeEvidence> { biotechIdentity }, pressureSnapshot).Count);
            NarrativeEvidence ascentEvidence = Evidence();
            ascentEvidence.povPawnId = "pawn-1";
            ascentEvidence.facet = NarrativeFacetTokens.JourneyChapter;
            ascentEvidence.subjectKind = NarrativeSubjectKindTokens.Colony;
            ascentEvidence.subjectId = "royal_ascent";
            ascentEvidence.arcKey = "court-ascent|Quest_41";
            ascentEvidence.beliefTopics = new List<string> { "authority", "duty", "hospitality" };
            List<NarrativeLensCandidate> exactPressure = RoyaltyNarrativeProvider.Build(
                new List<NarrativeEvidence> { ascentEvidence }, pressureSnapshot);
            AssertEqual("matching Ascent arc admits one bounded pressure lens", 1, exactPressure.Count);
            AssertEqual("Ascent pressure candidate category", NarrativeCategoryTokens.Pressure,
                exactPressure[0].category);
            AssertEqual("Ascent pressure exact-arc relationship", NarrativeRelationshipTokens.ExactArc,
                exactPressure[0].relationship);
            NarrativeEvidence authorityEvidence = Evidence();
            authorityEvidence.povPawnId = "pawn-1";
            authorityEvidence.beliefTopics = new List<string> { "authority" };
            List<NarrativeLensCandidate> authorityPressure = RoyaltyNarrativeProvider.Build(
                new List<NarrativeEvidence> { authorityEvidence }, pressureSnapshot);
            AssertEqual("authority-relevant page admits current court pressure", 1, authorityPressure.Count);
            AssertEqual("authority pressure uses direct-topic relationship",
                NarrativeRelationshipTokens.DirectTopic, authorityPressure[0].relationship);
            pressureSnapshot.courtPressure.arcKey = "court-ascent|unsafe|extra";
            AssertEqual("malformed pressure arc fails closed", 0,
                RoyaltyNarrativeProvider.Build(
                    new List<NarrativeEvidence> { authorityEvidence }, pressureSnapshot).Count);

            personaEvidence.povPawnId = "other-pawn";
            AssertEqual("different POV cannot receive a persona bond", 0,
                RoyaltyNarrativeProvider.Build(new List<NarrativeEvidence> { personaEvidence }, snapshot).Count);
            personaEvidence.povPawnId = "pawn-1";
            snapshot.hasVerifiedPovConnection = false;
            AssertEqual("unverified Royalty snapshot fails closed", 0,
                RoyaltyNarrativeProvider.Build(new List<NarrativeEvidence> { personaEvidence }, snapshot).Count);
            snapshot.hasVerifiedPovConnection = true;
            snapshot.providerAvailable = false;
            AssertEqual("absent Royalty provider is independently silent", 0,
                RoyaltyNarrativeProvider.Build(new List<NarrativeEvidence> { personaEvidence }, snapshot).Count);
            snapshot.providerAvailable = true;

            snapshot.personaBonds = new List<RoyaltyPersonaNarrativeFact>();
            List<NarrativeEvidence> manyEvidence = new List<NarrativeEvidence>();
            for (int i = 8; i >= 1; i--)
            {
                snapshot.personaBonds.Add(PersonaFact("weapon-" + i, 1));
                NarrativeEvidence row = Evidence();
                row.povPawnId = "pawn-1";
                row.facet = NarrativeFacetTokens.BondLifecycle;
                row.subjectKind = NarrativeSubjectKindTokens.Weapon;
                row.subjectId = "weapon-" + i;
                row.arcKey = "royalty-persona|weapon-" + i + "|1";
                manyEvidence.Add(row);
            }
            List<NarrativeLensCandidate> bounded = RoyaltyNarrativeProvider.Build(manyEvidence, snapshot);
            AssertEqual("malformed many-bond snapshot is defensively capped", 4, bounded.Count);
            AssertEqual("provider sorting is independent of saved list order",
                "royalty|persona|weapon-1|1", bounded[0].candidateKey);
            AssertEqual("null evidence is harmless", 0, RoyaltyNarrativeProvider.Build(null, snapshot).Count);
            AssertEqual("null snapshot is harmless", 0,
                RoyaltyNarrativeProvider.Build(manyEvidence, null).Count);
        }

        private static RoyaltyPersonaNarrativeFact PersonaFact(string weaponId, int epoch)
        {
            return new RoyaltyPersonaNarrativeFact
            {
                weaponThingId = weaponId,
                weaponName = "Persona weapon",
                bondEpoch = epoch,
                arcKey = "royalty-persona|" + weaponId + "|" + epoch,
                text = "This exact persona-weapon bond remains part of the moment.",
                sourceTick = 1000
            };
        }

        private static RoyaltyTitleNarrativeFact TitleFact(
            string factionId,
            string titleDefName,
            List<string> duties)
        {
            return new RoyaltyTitleNarrativeFact
            {
                factionId = factionId,
                titleDefName = titleDefName,
                text = "The writer currently holds this exact royal title and its active duties.",
                dutyCategoryTokens = duties,
                sourceTick = 1000
            };
        }

        private static void TestOdysseyProviderEvidenceAndCrossDlcGates()
        {
            NarrativeEvidence familyEvidence = Evidence();
            familyEvidence.facet = NarrativeFacetTokens.BondLifecycle;
            familyEvidence.beliefTopics = new List<string> { "family" };
            OdysseyNarrativeSnapshot snapshot = OdysseySnapshot();

            List<NarrativeLensCandidate> candidates = OdysseyNarrativeProvider.Build(
                new List<NarrativeEvidence> { familyEvidence }, snapshot);
            AssertEqual("exact occupied Odyssey home yields one bounded candidate", 1, candidates.Count);
            AssertEqual("Odyssey candidate owns home category", NarrativeCategoryTokens.Home,
                candidates[0].category);
            AssertEqual("Odyssey candidate carries committed journey arc", snapshot.journeyId,
                candidates[0].arcKey);
            AssertEqual("Odyssey candidate identifies exact ship", snapshot.shipStableId,
                candidates[0].subjectId);

            NarrativeContextSelection familySelection = NarrativeContextSelector.Select(
                new NarrativeContextRequest
                {
                    policy = NarrativePolicySnapshot.CreateDefault(),
                    currentTick = 1000,
                    evidence = new List<NarrativeEvidence> { familyEvidence },
                    candidates = candidates,
                    detailLevel = NarrativeDetailLevelTokens.Full
                });
            AssertEqual("family event aboard exact gravship selects one home lens", 1,
                familySelection.selectedCandidates.Count);
            AssertTrue("cross-DLC family/home relevance stays verified ambient",
                familySelection.selectionReasons.Contains(NarrativeRelationshipTokens.Ambient));

            snapshot.providerAvailable = false;
            AssertEqual("inactive Odyssey provider is silent", 0,
                OdysseyNarrativeProvider.Build(new List<NarrativeEvidence> { familyEvidence }, snapshot).Count);
            snapshot.providerAvailable = true;
            snapshot.pawnCanKnow = false;
            AssertEqual("unknown Odyssey home fails closed", 0,
                OdysseyNarrativeProvider.Build(new List<NarrativeEvidence> { familyEvidence }, snapshot).Count);
            snapshot.pawnCanKnow = true;
            snapshot.hasVerifiedPovConnection = false;
            AssertEqual("pawn outside exact grav field receives no Odyssey lens", 0,
                OdysseyNarrativeProvider.Build(new List<NarrativeEvidence> { familyEvidence }, snapshot).Count);
            snapshot.hasVerifiedPovConnection = true;
            snapshot.povPawnId = "different-pawn";
            AssertEqual("provider snapshot must match the exact evidence POV", 0,
                OdysseyNarrativeProvider.Build(new List<NarrativeEvidence> { familyEvidence }, snapshot).Count);
            snapshot.povPawnId = "pawn-1";
            string location = snapshot.locationKey;
            snapshot.locationKey = string.Empty;
            AssertEqual("unknown location receives no Odyssey lens", 0,
                OdysseyNarrativeProvider.Build(new List<NarrativeEvidence> { familyEvidence }, snapshot).Count);
            snapshot.locationKey = location;

            List<NarrativeEvidence> departure = OdysseyNarrativeEvidenceFactory.Departure(
                "departure-event", 800, "pawn-1", "pilot", snapshot.journeyId,
                snapshot.shipStableId, snapshot.shipName, "Ritual", true);
            AssertEqual("committed departure creates one ship evidence row", 1, departure.Count);
            AssertEqual("departure phase is frozen", OdysseyNarrativePhaseTokens.Departed,
                departure[0].phase);
            AssertEqual("departure evidence keeps exact journey arc", snapshot.journeyId,
                departure[0].arcKey);
            NarrativeReference departureReference = NarrativeReferencePolicy.FromEvidence(departure[0]);
            AssertEqual("departure reference keeps exact ship subject", NarrativeSubjectKindTokens.Ship,
                departureReference.subjectKind);

            List<NarrativeEvidence> landing = OdysseyNarrativeEvidenceFactory.Landing(
                "landing-event", 1200, "pawn-1", "pilot", snapshot.journeyId,
                snapshot.shipStableId, snapshot.shipName, snapshot.locationKey, snapshot.locationLabel,
                "departure-event", "GravshipJourney", true, true);
            AssertEqual("landing creates exact ship and visible-place evidence", 2, landing.Count);
            AssertEqual("homecoming phase is returned", OdysseyNarrativePhaseTokens.Returned,
                landing[0].phase);
            AssertEqual("landing place reference is exact", NarrativeSubjectKindTokens.Place,
                landing[1].subjectKind);
            AssertEqual("landing links the correlated departure", "departure-event",
                landing[0].relatedEventId);

            NarrativeContextSelection landingSelection = NarrativeContextSelector.Select(
                new NarrativeContextRequest
                {
                    policy = NarrativePolicySnapshot.CreateDefault(),
                    currentTick = 1200,
                    evidence = landing,
                    candidates = candidates,
                    detailLevel = NarrativeDetailLevelTokens.Full
                });
            AssertEqual("landing selects matching journey home once", 1,
                landingSelection.selectedCandidates.Count);
            AssertTrue("landing/home match is exact arc",
                landingSelection.selectionReasons.Contains(NarrativeRelationshipTokens.ExactArc));

            AssertEqual("missing ship identity creates no departure evidence", 0,
                OdysseyNarrativeEvidenceFactory.Departure(
                    "bad", 1, "pawn-1", "crew", snapshot.journeyId,
                    string.Empty, string.Empty, "Ritual", true).Count);
        }

        private static void TestOdysseyEnvironmentalPressureGatesAndComposition()
        {
            OdysseyNarrativeSnapshot snapshot = OdysseySnapshot();
            OdysseyEnvironmentalNarrativeFact flood = SeasonalFloodFact(
                snapshot,
                999,
                "Seasonal floodwater remained present on this exact gravship map.");
            snapshot.environmentalPressures.Add(flood);

            List<NarrativeEvidence> landing = OdysseyNarrativeEvidenceFactory.Landing(
                "landing-event", 1000, "pawn-1", "pilot", snapshot.journeyId,
                snapshot.shipStableId, snapshot.shipName, snapshot.locationKey, snapshot.locationLabel,
                "departure-event", "GravshipJourney", false, true);
            List<NarrativeLensCandidate> candidates = OdysseyNarrativeProvider.Build(landing, snapshot);
            AssertEqual("exact home plus active seasonal flood yields two bounded Odyssey candidates",
                2, candidates.Count);
            AssertEqual("Odyssey home remains first inside the fixed provider", NarrativeCategoryTokens.Home,
                candidates[0].category);
            AssertEqual("seasonal flood uses the existing pressure category", NarrativeCategoryTokens.Pressure,
                candidates[1].category);
            AssertEqual("seasonal flood identifies the exact visible place", snapshot.locationKey,
                candidates[1].subjectId);
            AssertEqual("seasonal flood never claims the journey caused it", string.Empty,
                candidates[1].arcKey);
            AssertEqual("seasonal flood candidate key is stable and prose-free",
                "odyssey|pressure|seasonal_flood|planet-layer-1-tile-42",
                candidates[1].candidateKey);

            NarrativePolicySnapshot detailPolicy = NarrativePolicySnapshot.CreateDefault();
            Budget(detailPolicy, NarrativeDetailLevelTokens.Full).characterBudget = 1000;
            Budget(detailPolicy, NarrativeDetailLevelTokens.Balanced).characterBudget = 1000;
            Budget(detailPolicy, NarrativeDetailLevelTokens.Compact).characterBudget = 1000;
            NarrativeContextSelection full = NarrativeContextSelector.Select(new NarrativeContextRequest
            {
                policy = detailPolicy,
                detailLevel = NarrativeDetailLevelTokens.Full,
                currentTick = 1000,
                evidence = landing,
                candidates = candidates
            });
            NarrativeContextSelection balanced = NarrativeContextSelector.Select(new NarrativeContextRequest
            {
                policy = detailPolicy,
                detailLevel = NarrativeDetailLevelTokens.Balanced,
                currentTick = 1000,
                evidence = landing,
                candidates = candidates
            });
            NarrativeContextSelection compact = NarrativeContextSelector.Select(new NarrativeContextRequest
            {
                policy = detailPolicy,
                detailLevel = NarrativeDetailLevelTokens.Compact,
                currentTick = 1000,
                evidence = landing,
                candidates = candidates
            });
            AssertEqual("Full keeps the non-redundant exact home and place pressure", 2,
                full.selectedCandidates.Count);
            AssertEqual("Balanced preserves the global one-lens budget", 1,
                balanced.selectedCandidates.Count);
            AssertEqual("Compact preserves the global one-lens budget", 1,
                compact.selectedCandidates.Count);

            OdysseyNarrativeSnapshot pressureOnly = OdysseySnapshot();
            pressureOnly.homeText = string.Empty;
            pressureOnly.environmentalPressures.Add(SeasonalFloodFact(
                pressureOnly, 900, "Older flood fact."));
            pressureOnly.environmentalPressures.Add(SeasonalFloodFact(
                pressureOnly, 950, "Newest flood fact."));
            pressureOnly.environmentalPressures.Add(SeasonalFloodFact(
                pressureOnly, 925, "Middle flood fact."));
            List<NarrativeLensCandidate> bounded = OdysseyNarrativeProvider.Build(landing, pressureOnly);
            AssertEqual("duplicate source rows collapse to the one environmental cap", 1, bounded.Count);
            AssertEqual("duplicate source rows deterministically keep the newest fact", "Newest flood fact.",
                bounded[0].text);
            pressureOnly.environmentalPressures.Reverse();
            List<NarrativeLensCandidate> reversed = OdysseyNarrativeProvider.Build(landing, pressureOnly);
            AssertEqual("environmental input order cannot change the selected fact", bounded[0].text,
                reversed[0].text);

            NarrativeLensCandidate environmental = bounded[0];
            NarrativeLensCandidate freshAmbient = Candidate(
                "zz-fresh-ambient", NarrativeCategoryTokens.Home, NarrativeFacetTokens.AmbientPressure,
                text: "A different current home fact.", relationship: NarrativeRelationshipTokens.Ambient,
                sourceTick: 950);
            Func<List<string>, NarrativeContextSelection> selectRepeat = recent =>
                NarrativeContextSelector.Select(new NarrativeContextRequest
                {
                    policy = detailPolicy,
                    detailLevel = NarrativeDetailLevelTokens.Full,
                    currentTick = 1000,
                    evidence = new List<NarrativeEvidence> { Evidence() },
                    recentSelectedCandidateKeys = recent ?? new List<string>(),
                    candidates = new List<NarrativeLensCandidate> { environmental, freshAmbient }
                });
            AssertEqual("fresh and environmental ambient facts start in stable key order",
                environmental.candidateKey, selectRepeat(null).selectedCandidates[0].candidateKey);
            AssertEqual("persisted Odyssey pressure key receives the ordinary repetition penalty",
                freshAmbient.candidateKey,
                selectRepeat(new List<string> { environmental.candidateKey })
                    .selectedCandidates[0].candidateKey);

            AnomalyNarrativeFact ghoul = GhoulFact();
            List<NarrativeLensCandidate> composed = NarrativeProviderOrchestrator.Collect(
                new List<NarrativeEvidence> { AnomalyEvidence(ghoul) },
                new List<NarrativeLensCandidate>(), null, null, AnomalySnapshot(ghoul), pressureOnly);
            AssertEqual("fixed list collects Anomaly plus one bounded Odyssey pressure", 2, composed.Count);
            AssertEqual("fixed provider order still keeps Anomaly before Odyssey",
                NarrativeProviderTokens.Anomaly, composed[0].provider);
            NarrativeContextSelection composedSelection = NarrativeContextSelector.Select(
                new NarrativeContextRequest
                {
                    policy = detailPolicy,
                    detailLevel = NarrativeDetailLevelTokens.Full,
                    currentTick = 1000,
                    evidence = new List<NarrativeEvidence> { AnomalyEvidence(ghoul) },
                    candidates = composed
                });
            AssertEqual("two-provider composition stays inside the global two-lens cap", 2,
                composedSelection.selectedCandidates.Count);

            Action<string, Action<OdysseyNarrativeSnapshot, OdysseyEnvironmentalNarrativeFact>> reject =
                (label, mutate) =>
                {
                    OdysseyNarrativeSnapshot bad = OdysseySnapshot();
                    bad.homeText = string.Empty;
                    OdysseyEnvironmentalNarrativeFact fact = SeasonalFloodFact(bad, 999, "Flood fact.");
                    mutate(bad, fact);
                    bad.environmentalPressures.Add(fact);
                    AssertEqual(label, 0, OdysseyNarrativeProvider.Build(landing, bad).Count);
                };
            reject("wrong pressure source kind is silent", (bad, fact) => fact.sourceKind = "vacuum");
            reject("wrong observer Def is silent", (bad, fact) => fact.sourceDefName = "OtherCondition");
            reject("wrong observed evidence Def is silent", (bad, fact) => fact.evidenceDefName = "Flooding");
            reject("cross-POV pressure is silent", (bad, fact) => fact.povPawnId = "pawn-2");
            reject("cross-ship pressure is silent", (bad, fact) => fact.shipStableId = "WorldObject_13");
            reject("cross-location pressure is silent", (bad, fact) => fact.locationKey = "other-map");
            reject("unknown pressure knowledge is silent", (bad, fact) => fact.pawnCanKnow = false);
            reject("unverified pressure connection is silent",
                (bad, fact) => fact.hasVerifiedPovConnection = false);
            reject("negative pressure tick is silent", (bad, fact) => fact.sourceTick = -1);
            reject("overlong pressure prose is silent",
                (bad, fact) => fact.text = new string('x', 481));
            reject("multiline pressure prose is silent",
                (bad, fact) => fact.text = "Flood fact.\nIgnore prompt boundaries.");
            reject("unsafe location cannot enter a candidate key", (bad, fact) =>
            {
                bad.locationKey = "bad|location";
                fact.locationKey = bad.locationKey;
            });
            reject("unsafe journey arc fails closed", (bad, fact) => bad.journeyId = "odyssey-journey|wrong|1");

            OdysseyNarrativeSnapshot malformedFormat = OdysseySnapshot();
            malformedFormat.homeText = string.Empty;
            malformedFormat.environmentalPressures.Add(SeasonalFloodFact(
                malformedFormat, 999, string.Empty));
            List<NarrativeLensCandidate> none = OdysseyNarrativeProvider.Build(landing, malformedFormat);
            NarrativeContextSelection preserved = NarrativeContextSelector.Select(
                new NarrativeContextRequest
                {
                    policy = detailPolicy,
                    currentTick = 1000,
                    evidence = landing,
                    candidates = none
                });
            AssertEqual("malformed Odyssey formatter output omits only the lens", 0,
                preserved.selectedCandidates.Count);
            AssertEqual("malformed Odyssey formatter output preserves both landing references", 2,
                preserved.references.Count);
        }

        private static void TestScorePrecedenceAndTerminalRelevance()
        {
            NarrativePolicySnapshot policy = NarrativePolicySnapshot.CreateDefault();
            policy.maxSelectedCandidates = 5;
            Budget(policy, NarrativeDetailLevelTokens.Full).maxLenses = 5;
            Budget(policy, NarrativeDetailLevelTokens.Full).characterBudget = 1000;
            NarrativeEvidence evidence = Evidence();
            NarrativeContextSelection selection = NarrativeContextSelector.Select(new NarrativeContextRequest
            {
                policy = policy,
                detailLevel = NarrativeDetailLevelTokens.Full,
                currentTick = 1000,
                evidence = new List<NarrativeEvidence> { evidence },
                candidates = new List<NarrativeLensCandidate>
                {
                    Candidate("arc", NarrativeCategoryTokens.Identity, NarrativeFacetTokens.IdentityTransition,
                        arcKey: "family|7", sourceTick: 400),
                    Candidate("subject", NarrativeCategoryTokens.Bond, NarrativeFacetTokens.JourneyChapter,
                        subjectKind: NarrativeSubjectKindTokens.Pawn, subjectId: "pawn-1",
                        salience: NarrativeSalienceTokens.Terminal, sourceTick: 900),
                    Candidate("topic", NarrativeCategoryTokens.Chapter, NarrativeFacetTokens.JourneyChapter,
                        topics: new List<string> { "bonding" }, sourceTick: 950),
                    Candidate("facet", NarrativeCategoryTokens.Pressure, NarrativeFacetTokens.IdentityTransition,
                        sourceTick: 990),
                    Candidate("ambient-terminal", NarrativeCategoryTokens.Home, NarrativeFacetTokens.JourneyChapter,
                        relationship: NarrativeRelationshipTokens.Ambient,
                        salience: NarrativeSalienceTokens.Terminal, sourceTick: 999)
                }
            });

            AssertEqual("all distinct relevant categories are selected", 5, selection.selectedCandidates.Count);
            AssertEqual("exact arc beats all other relationships", "arc", selection.selectedCandidates[0].candidateKey);
            AssertEqual("exact subject beats topic despite terminal salience", "subject",
                selection.selectedCandidates[1].candidateKey);
            AssertEqual("direct topic beats direct facet", "topic", selection.selectedCandidates[2].candidateKey);
            AssertEqual("direct facet beats unrelated terminal ambient pressure", "facet",
                selection.selectedCandidates[3].candidateKey);
            AssertEqual("ambient is lowest verified relevance", "ambient-terminal",
                selection.selectedCandidates[4].candidateKey);
        }

        /// <summary>
        /// Pins the live anti-repetition contract: a recently selected key drops its candidate by
        /// exactly the XML repetition penalty (with defaults, ExactSubject 115 falls below DirectTopic
        /// 94), exact-arc continuations are exempt via exactArcRepetitionPenalty=0, comparison is
        /// ordinal (a case-twiddled key does not penalize), and a penalized sole candidate is still
        /// selected — the penalty reorders, it never empties a selection.
        /// </summary>
        private static void TestRepetitionPenaltyAndExactArcExemption()
        {
            NarrativePolicySnapshot policy = NarrativePolicySnapshot.CreateDefault();
            policy.maxSelectedCandidates = 3;
            Budget(policy, NarrativeDetailLevelTokens.Full).maxLenses = 3;
            Budget(policy, NarrativeDetailLevelTokens.Full).characterBudget = 1000;

            Func<List<string>, NarrativeContextSelection> select = recentKeys =>
                NarrativeContextSelector.Select(new NarrativeContextRequest
                {
                    policy = policy,
                    detailLevel = NarrativeDetailLevelTokens.Full,
                    currentTick = 1000,
                    evidence = new List<NarrativeEvidence> { Evidence() },
                    recentSelectedCandidateKeys = recentKeys ?? new List<string>(),
                    candidates = new List<NarrativeLensCandidate>
                    {
                        Candidate("arc-lens", NarrativeCategoryTokens.Identity,
                            NarrativeFacetTokens.IdentityTransition, arcKey: "family|7"),
                        Candidate("subject-lens", NarrativeCategoryTokens.Bond,
                            NarrativeFacetTokens.IdentityTransition,
                            subjectKind: NarrativeSubjectKindTokens.Pawn, subjectId: "pawn-1"),
                        Candidate("topic-lens", NarrativeCategoryTokens.Chapter,
                            NarrativeFacetTokens.JourneyChapter,
                            topics: new List<string> { "bonding" })
                    }
                });

            NarrativeContextSelection baseline = select(null);
            AssertEqual("baseline keeps subject above topic", "subject-lens",
                baseline.selectedCandidates[1].candidateKey);
            AssertEqual("baseline keeps topic last", "topic-lens",
                baseline.selectedCandidates[2].candidateKey);

            NarrativeContextSelection penalized = select(new List<string> { "subject-lens" });
            AssertEqual("repeated subject lens drops below the fresh topic lens", "topic-lens",
                penalized.selectedCandidates[1].candidateKey);
            AssertEqual("repeated subject lens is reordered, not dropped", "subject-lens",
                penalized.selectedCandidates[2].candidateKey);

            NarrativeContextSelection exactArcRepeat = select(new List<string> { "arc-lens" });
            AssertEqual("exact-arc continuation is exempt from the repetition penalty", "arc-lens",
                exactArcRepeat.selectedCandidates[0].candidateKey);
            AssertEqual("exempt arc repeat changes nothing else", "subject-lens",
                exactArcRepeat.selectedCandidates[1].candidateKey);

            NarrativeContextSelection caseTwiddled = select(new List<string> { "SUBJECT-LENS" });
            AssertEqual("recent-key comparison is ordinal, so a case-twiddled key never penalizes",
                "subject-lens", caseTwiddled.selectedCandidates[1].candidateKey);

            NarrativeContextSelection sole = NarrativeContextSelector.Select(new NarrativeContextRequest
            {
                policy = policy,
                detailLevel = NarrativeDetailLevelTokens.Full,
                currentTick = 1000,
                evidence = new List<NarrativeEvidence> { Evidence() },
                recentSelectedCandidateKeys = new List<string> { "subject-lens" },
                candidates = new List<NarrativeLensCandidate>
                {
                    Candidate("subject-lens", NarrativeCategoryTokens.Bond,
                        NarrativeFacetTokens.IdentityTransition,
                        subjectKind: NarrativeSubjectKindTokens.Pawn, subjectId: "pawn-1")
                }
            });
            AssertEqual("a penalized sole candidate is still selected", 1,
                sole.selectedCandidates.Count);
        }

        private static void TestCategoryCapsAndStableTieBreak()
        {
            NarrativePolicySnapshot policy = NarrativePolicySnapshot.CreateDefault();
            policy.maxSelectedCandidates = 4;
            Budget(policy, NarrativeDetailLevelTokens.Full).maxLenses = 4;
            Budget(policy, NarrativeDetailLevelTokens.Full).characterBudget = 1000;
            NarrativeContextSelection capped = NarrativeContextSelector.Select(new NarrativeContextRequest
            {
                policy = policy,
                detailLevel = NarrativeDetailLevelTokens.Full,
                currentTick = 1000,
                evidence = new List<NarrativeEvidence> { Evidence() },
                candidates = new List<NarrativeLensCandidate>
                {
                    Candidate("identity-first", NarrativeCategoryTokens.Identity, NarrativeFacetTokens.IdentityTransition,
                        sourceTick: 9),
                    Candidate("identity-second", NarrativeCategoryTokens.Identity, NarrativeFacetTokens.IdentityTransition,
                        sourceTick: 8),
                    Candidate("interpretation-first", NarrativeCategoryTokens.Interpretation,
                        NarrativeFacetTokens.IdentityTransition, sourceTick: 7),
                    Candidate("interpretation-second", NarrativeCategoryTokens.Interpretation,
                        NarrativeFacetTokens.IdentityTransition, sourceTick: 6)
                }
            });

            AssertEqual("category and interpretation caps keep two candidates", 2, capped.selectedCandidates.Count);
            AssertDiagnostic(capped, "identity-second", NarrativeDiagnosticTokens.CategoryCap);
            AssertDiagnostic(capped, "interpretation-second", NarrativeDiagnosticTokens.InterpretationCap);
            AssertTrue("selected interpretation is recorded", capped.selectedInterpretation);

            NarrativePolicySnapshot tiePolicy = NarrativePolicySnapshot.CreateDefault();
            tiePolicy.maxSelectedCandidates = 2;
            Budget(tiePolicy, NarrativeDetailLevelTokens.Full).maxLenses = 2;
            Budget(tiePolicy, NarrativeDetailLevelTokens.Full).characterBudget = 1000;
            NarrativeContextSelection tied = NarrativeContextSelector.Select(new NarrativeContextRequest
            {
                policy = tiePolicy,
                detailLevel = NarrativeDetailLevelTokens.Full,
                currentTick = 1000,
                evidence = new List<NarrativeEvidence> { Evidence() },
                candidates = new List<NarrativeLensCandidate>
                {
                    Candidate("z-key", NarrativeCategoryTokens.Bond, NarrativeFacetTokens.IdentityTransition,
                        sourceTick: 10),
                    Candidate("a-key", NarrativeCategoryTokens.Identity, NarrativeFacetTokens.IdentityTransition,
                        sourceTick: 10)
                }
            });

            AssertEqual("stable ordinal tie-break chooses a-key first", "a-key", tied.selectedCandidates[0].candidateKey);
            AssertEqual("stable ordinal tie-break keeps z-key second", "z-key", tied.selectedCandidates[1].candidateKey);
        }

        private static void TestDetailCapsAndCompleteFactBudget()
        {
            NarrativePolicySnapshot policy = NarrativePolicySnapshot.CreateDefault();
            Budget(policy, NarrativeDetailLevelTokens.Full).characterBudget = 18;
            Budget(policy, NarrativeDetailLevelTokens.Full).maxLenses = 2;
            policy.maxSelectedCandidates = 2;
            NarrativeContextSelection budgeted = NarrativeContextSelector.Select(new NarrativeContextRequest
            {
                policy = policy,
                detailLevel = NarrativeDetailLevelTokens.Full,
                evidence = new List<NarrativeEvidence> { Evidence() },
                candidates = new List<NarrativeLensCandidate>
                {
                    Candidate("too-long", NarrativeCategoryTokens.Identity, NarrativeFacetTokens.IdentityTransition,
                        arcKey: "family|7", text: "A factual statement that cannot fit."),
                    Candidate("short", NarrativeCategoryTokens.Bond, NarrativeFacetTokens.IdentityTransition,
                        text: "short fact")
                }
            });

            AssertEqual("budget keeps the lower-ranked complete fact", 1, budgeted.selectedCandidates.Count);
            AssertEqual("budget never truncates factual text", "short fact", budgeted.narrativeContext);
            AssertDiagnostic(budgeted, "too-long", NarrativeDiagnosticTokens.CharacterBudget);

            NarrativePolicySnapshot balanced = NarrativePolicySnapshot.CreateDefault();
            NarrativeContextSelection balancedPair = NarrativeContextSelector.Select(new NarrativeContextRequest
            {
                policy = balanced,
                detailLevel = NarrativeDetailLevelTokens.Balanced,
                evidence = new List<NarrativeEvidence> { Evidence() },
                candidates = new List<NarrativeLensCandidate>
                {
                    Candidate("arc-identity", NarrativeCategoryTokens.Identity, NarrativeFacetTokens.IdentityTransition,
                        arcKey: "family|7", text: "The family bond remains close."),
                    Candidate("arc-bond", NarrativeCategoryTokens.Bond, NarrativeFacetTokens.BondLifecycle,
                        arcKey: "family|7", text: "The same family chapter still matters.")
                }
            });

            AssertEqual("Balanced allows the configured short exact-arc pair", 2,
                balancedPair.selectedCandidates.Count);

            NarrativeContextSelection compact = NarrativeContextSelector.Select(new NarrativeContextRequest
            {
                policy = balanced,
                detailLevel = NarrativeDetailLevelTokens.Compact,
                evidence = new List<NarrativeEvidence> { Evidence() },
                candidates = new List<NarrativeLensCandidate>
                {
                    Candidate("compact-one", NarrativeCategoryTokens.Identity, NarrativeFacetTokens.IdentityTransition),
                    Candidate("compact-two", NarrativeCategoryTokens.Bond, NarrativeFacetTokens.IdentityTransition)
                }
            });

            AssertEqual("Compact permits at most one lens", 1, compact.selectedCandidates.Count);
            AssertDiagnostic(compact, "compact-two", NarrativeDiagnosticTokens.DetailCap);
        }

        private static void TestTopicRedundancyKeepsNewFacts()
        {
            NarrativePolicySnapshot policy = NarrativePolicySnapshot.CreateDefault();
            policy.maxSelectedCandidates = 3;
            Budget(policy, NarrativeDetailLevelTokens.Full).maxLenses = 3;
            Budget(policy, NarrativeDetailLevelTokens.Full).characterBudget = 1000;
            NarrativeContextSelection selection = NarrativeContextSelector.Select(new NarrativeContextRequest
            {
                policy = policy,
                detailLevel = NarrativeDetailLevelTokens.Full,
                evidence = new List<NarrativeEvidence> { Evidence() },
                candidates = new List<NarrativeLensCandidate>
                {
                    Candidate("first-topic", NarrativeCategoryTokens.Identity,
                        NarrativeFacetTokens.IdentityTransition, text: "first fact",
                        topics: new List<string> { "bonding" }, salience: NarrativeSalienceTokens.Terminal),
                    Candidate("adds-topic", NarrativeCategoryTokens.Bond,
                        NarrativeFacetTokens.IdentityTransition, text: "second fact",
                        topics: new List<string> { "bonding", "loss" }),
                    Candidate("duplicate-topic", NarrativeCategoryTokens.Chapter,
                        NarrativeFacetTokens.IdentityTransition, text: "third fact",
                        topics: new List<string> { "bonding" })
                }
            });

            AssertEqual("candidate with an added topic remains distinct", 2, selection.selectedCandidates.Count);
            AssertTrue("added-topic candidate survives subset comparison",
                HasSelected(selection, "adds-topic"));
            AssertDiagnostic(selection, "duplicate-topic", NarrativeDiagnosticTokens.Redundant);
        }

        private static void TestReferenceEqualityAndDeduplication()
        {
            NarrativeEvidence evidence = Evidence();
            NarrativeReference first = NarrativeReferencePolicy.FromEvidence(evidence);
            NarrativeReference copy = NarrativeReferencePolicy.FromEvidence(evidence);
            NarrativeReference caseChanged = NarrativeReferencePolicy.FromEvidence(evidence);
            caseChanged.arcKey = "Family|7";

            AssertTrue("same normalized source evidence creates equal reference",
                NarrativeReferencePolicy.AreEquivalent(first, copy));
            AssertTrue("arc key comparison is ordinal/case-sensitive",
                !NarrativeReferencePolicy.AreEquivalent(first, caseChanged));
            AssertTrue("same subject survives reference comparison", NarrativeReferencePolicy.SameSubject(first, copy));
            AssertTrue("case-changed arc is not the same arc", !NarrativeReferencePolicy.SameArc(first, caseChanged));

            List<NarrativeReference> unique = NarrativeReferencePolicy.Unique(new List<NarrativeReference>
            {
                first,
                copy,
                caseChanged
            });
            AssertEqual("reference dedup preserves case-distinct arcs", 2, unique.Count);
        }

        private static void TestPersistenceCapsAndPromptFormatting()
        {
            List<NarrativeEvidence> normalized = NarrativePersistencePolicy.NormalizeEvidence(
                new List<NarrativeEvidence>
                {
                    new NarrativeEvidence
                    {
                        facet = NarrativeFacetTokens.IdentityTransition,
                        pawnCanKnow = true,
                        beliefTopics = new List<string> { "bonding", "bonding", "loss" }
                    },
                    new NarrativeEvidence
                    {
                        facet = NarrativeFacetTokens.BondLifecycle,
                        pawnCanKnow = false
                    },
                    new NarrativeEvidence
                    {
                        facet = NarrativeFacetTokens.JourneyChapter,
                        pawnCanKnow = true
                    },
                    new NarrativeEvidence
                    {
                        facet = NarrativeFacetTokens.AmbientPressure,
                        pawnCanKnow = true
                    },
                    new NarrativeEvidence
                    {
                        facet = NarrativeFacetTokens.IdentityTransition,
                        pawnCanKnow = true
                    }
                },
                "event-fallback",
                55,
                "pawn-fallback",
                "initiator",
                99);

            AssertEqual("persistence hard-caps evidence", NarrativePersistencePolicy.HardEvidenceCap,
                normalized.Count);
            AssertEqual("persistence stamps missing event id", "event-fallback", normalized[0].eventId);
            AssertEqual("persistence stamps missing POV pawn", "pawn-fallback", normalized[0].povPawnId);
            AssertEqual("persistence strips duplicate topic tokens", 2, normalized[0].beliefTopics.Count);
            AssertTrue("hidden evidence fails closed before persistence",
                !ContainsFacet(normalized, NarrativeFacetTokens.BondLifecycle));

            NarrativeReference reference = NarrativeReferencePolicy.FromEvidence(normalized[0]);
            List<NarrativeReference> references = NarrativePersistencePolicy.NormalizeReferences(
                new List<NarrativeReference>
                {
                    reference,
                    NarrativeReferencePolicy.FromEvidence(normalized[0]),
                    new NarrativeReference { arcKey = "other|arc", sourceEventId = "event-2", sourceTick = 2 },
                    new NarrativeReference { arcKey = "third|arc", sourceEventId = "event-3", sourceTick = 3 },
                    new NarrativeReference { arcKey = "fourth|arc", sourceEventId = "event-4", sourceTick = 4 }
                });
            AssertEqual("persistence deduplicates and caps references", NarrativePersistencePolicy.HardReferenceCap,
                references.Count);

            List<string> keys = NarrativePersistencePolicy.NormalizeSelectedCandidateKeys(
                new List<string> { "first", "first", " second ", "third" }, 2);
            AssertEqual("selection-key policy applies requested bounded cap", 2, keys.Count);
            AssertEqual("selection-key policy trims whitespace", "second", keys[1]);
            AssertEqual("subject index needs both identity parts", string.Empty,
                NarrativePersistencePolicy.SubjectIndexKey(NarrativeSubjectKindTokens.Pawn, string.Empty));
            AssertEqual("subject index is stable", "pawn|pawn-1",
                NarrativePersistencePolicy.SubjectIndexKey(NarrativeSubjectKindTokens.Pawn, "pawn-1"));

            AssertEqual("empty narrative context emits no field", string.Empty,
                NarrativeContextPrompt.Compose(" ", "Use facts."));
            AssertEqual("narrative context keeps policy instruction and whole fact", "Use facts.\nA complete fact.",
                NarrativeContextPrompt.Compose("A complete fact.", "Use facts."));
        }

        private static void TestReflectionPriorityAndDeferredConsumption()
        {
            NarrativePolicySnapshot policy = NarrativePolicySnapshot.CreateDefault();
            ReflectionOpportunity major = Opportunity(NarrativeReflectionKindTokens.MajorArc);
            ReflectionOpportunity cross = Opportunity(NarrativeReflectionKindTokens.CrossArc);
            ReflectionOpportunity belief = Opportunity(NarrativeReflectionKindTokens.Belief);
            ReflectionPlan plan = ReflectionCoordinator.Plan(new ReflectionPlanningRequest
            {
                policy = policy,
                currentTick = 200000,
                opportunities = new List<ReflectionOpportunity> { belief, cross, major }
            });

            AssertEqual("major source-owned arc wins reflection priority", NarrativeReflectionKindTokens.MajorArc,
                plan.selectedOpportunity.kind);
            AssertTrue("selected reflection consumes only after dispatch succeeds",
                plan.consumption.consumeAfterSuccessfulDispatch);
            AssertEqual("selected reflection carries one state instruction", 1, plan.stateInstructions.Count);
            AssertEqual("only one reflection is selected", 1, CountSelected(plan.diagnostics));

            major.alreadyWritten = true;
            ReflectionPlan fallback = ReflectionCoordinator.Plan(new ReflectionPlanningRequest
            {
                policy = policy,
                currentTick = 200000,
                opportunities = new List<ReflectionOpportunity> { belief, cross, major }
            });
            AssertEqual("cross-arc wins after major arc is unavailable", NarrativeReflectionKindTokens.CrossArc,
                fallback.selectedOpportunity.kind);
            AssertDiagnostic(fallback, NarrativeReflectionKindTokens.MajorArc,
                NarrativeDiagnosticTokens.ReflectionAlreadyWritten);

            ReflectionPlan cooldown = ReflectionCoordinator.Plan(new ReflectionPlanningRequest
            {
                policy = policy,
                currentTick = 200000,
                lastReflectionTick = 199999,
                opportunities = new List<ReflectionOpportunity> { belief }
            });
            AssertTrue("global cooldown selects no second reflection", cooldown.selectedOpportunity == null);
            AssertDiagnostic(cooldown, NarrativeReflectionKindTokens.Belief,
                NarrativeDiagnosticTokens.ReflectionCooldown);

            ReflectionOpportunity disabled = Opportunity(NarrativeReflectionKindTokens.Day);
            disabled.groupEnabled = false;
            ReflectionPlan disabledPlan = ReflectionCoordinator.Plan(new ReflectionPlanningRequest
            {
                policy = policy,
                currentTick = 200000,
                opportunities = new List<ReflectionOpportunity> { disabled }
            });
            AssertTrue("disabled group creates no reflection", disabledPlan.selectedOpportunity == null);
            AssertEqual("disabled group receives one debt-bounding instruction", 1,
                disabledPlan.stateInstructions.Count);
            AssertTrue("disabled group instruction advances bounded debt",
                disabledPlan.stateInstructions[0].advanceDebtWhenGroupDisabled);
            AssertTrue("disabled group instruction never claims a successful dispatch",
                !disabledPlan.stateInstructions[0].consumeAfterSuccessfulDispatch);
        }

        private static NarrativeLensCandidate AssertAnomalyMapping(
            string label,
            AnomalyNarrativeFact fact,
            string expectedKey,
            string expectedCategory,
            string expectedRelationship)
        {
            List<NarrativeLensCandidate> candidates = AnomalyNarrativeProvider.Build(
                new List<NarrativeEvidence> { AnomalyEvidence(fact) },
                AnomalySnapshot(fact));
            AssertEqual(label + " candidate count", 1, candidates.Count);
            AssertEqual(label + " stable key", expectedKey, candidates[0].candidateKey);
            AssertEqual(label + " provider", NarrativeProviderTokens.Anomaly, candidates[0].provider);
            AssertEqual(label + " category", expectedCategory, candidates[0].category);
            AssertEqual(label + " relationship", expectedRelationship, candidates[0].relationship);
            AssertEqual(label + " exact subject", fact.subjectId, candidates[0].subjectId);
            AssertEqual(label + " exact arc", fact.arcKey, candidates[0].arcKey);
            return candidates[0];
        }

        private static AnomalyNarrativeSnapshot AnomalySnapshot(params AnomalyNarrativeFact[] facts)
        {
            return new AnomalyNarrativeSnapshot
            {
                providerAvailable = true,
                povPawnId = "pawn-1",
                pawnCanKnow = true,
                hasVerifiedPovConnection = true,
                facts = facts == null
                    ? new List<AnomalyNarrativeFact>()
                    : new List<AnomalyNarrativeFact>(facts)
            };
        }

        private static AnomalyNarrativeFact GhoulFact()
        {
            return AnomalyFact(
                AnomalyNarrativeContinuityTokens.GhoulTransformation,
                NarrativeFacetTokens.IdentityTransition,
                AnomalyNarrativeContinuityTokens.Transformed,
                NarrativeSubjectKindTokens.Pawn,
                "pawn-ghoul",
                string.Empty,
                "Ari visibly completed an irreversible ghoul transformation.");
        }

        private static OdysseyNarrativeSnapshot OdysseySnapshot()
        {
            return new OdysseyNarrativeSnapshot
            {
                providerAvailable = true,
                povPawnId = "pawn-1",
                shipStableId = "WorldObject_12",
                shipName = "Wayfarer",
                journeyId = "odyssey-journey|WorldObject_12|800",
                locationKey = "planet-layer-1-tile-42",
                locationLabel = "Frozen plain",
                homeText = "At this event, the writer was aboard Wayfarer at the frozen plain.",
                sourceTick = 1000,
                pawnCanKnow = true,
                hasVerifiedPovConnection = true
            };
        }

        private static OdysseyEnvironmentalNarrativeFact SeasonalFloodFact(
            OdysseyNarrativeSnapshot snapshot,
            int sourceTick,
            string text)
        {
            return new OdysseyEnvironmentalNarrativeFact
            {
                sourceKind = OdysseyEnvironmentalNarrativeTokens.SeasonalFlood,
                sourceDefName = OdysseyEnvironmentalNarrativeTokens.SeasonalFloodConditionDefName,
                evidenceDefName = OdysseyEnvironmentalNarrativeTokens.SeasonalFloodEvidenceDefName,
                povPawnId = snapshot.povPawnId,
                shipStableId = snapshot.shipStableId,
                locationKey = snapshot.locationKey,
                text = text,
                sourceTick = sourceTick,
                pawnCanKnow = true,
                hasVerifiedPovConnection = true
            };
        }

        private static AnomalyNarrativeFact AnomalyFact(
            string sourceKind,
            string facet,
            string phase,
            string subjectKind,
            string subjectId,
            string arcKey,
            string text,
            int sourceTick = 1000)
        {
            return new AnomalyNarrativeFact
            {
                sourceKind = sourceKind,
                facet = facet,
                phase = phase,
                subjectKind = subjectKind,
                subjectId = subjectId,
                arcKey = arcKey,
                text = text,
                sourceTick = sourceTick
            };
        }

        private static NarrativeEvidence AnomalyEvidence(AnomalyNarrativeFact fact)
        {
            string sourceDomain;
            string sourceDefName;
            if (fact.sourceKind == AnomalyNarrativeContinuityTokens.GhoulTransformation)
            {
                sourceDomain = AnomalyNarrativeContinuityTokens.GhoulSourceDomain;
                sourceDefName = AnomalyNarrativeContinuityTokens.GhoulSourceDefName;
            }
            else if (fact.sourceKind == AnomalyNarrativeContinuityTokens.ContainmentBreach)
            {
                sourceDomain = AnomalyNarrativeContinuityTokens.ContainmentSourceDomain;
                sourceDefName = AnomalyNarrativeContinuityTokens.ContainmentSourceDefName;
            }
            else if (fact.sourceKind == AnomalyNarrativeContinuityTokens.CreepJoinerOutcome)
            {
                sourceDomain = AnomalyNarrativeContinuityTokens.CreepJoinerSourceDomain;
                sourceDefName = AnomalyNarrativeContinuityTokens.CreepJoinerSourceDefName;
            }
            else
            {
                sourceDomain = AnomalyNarrativeContinuityTokens.MonolithSourceDomain;
                sourceDefName = fact.phase == AnomalyNarrativeContinuityTokens.Stirring
                    ? AnomalyNarrativeContinuityTokens.MonolithStirringSourceDefName
                    : fact.phase == AnomalyNarrativeContinuityTokens.Waking
                        ? AnomalyNarrativeContinuityTokens.MonolithWakingSourceDefName
                        : fact.phase == AnomalyNarrativeContinuityTokens.VoidAwakened
                            ? AnomalyNarrativeContinuityTokens.MonolithVoidAwakenedSourceDefName
                            : fact.phase;
            }

            return new NarrativeEvidence
            {
                eventId = "anomaly-event",
                tick = fact.sourceTick,
                povPawnId = "pawn-1",
                povRole = "initiator",
                facet = fact.facet,
                phase = fact.phase,
                subjectKind = fact.subjectKind,
                subjectId = fact.subjectId,
                arcKey = fact.arcKey,
                beliefTopics = new List<string> { "identity", "body" },
                salience = NarrativeSalienceTokens.Major,
                pawnCanKnow = true,
                sourceDomain = sourceDomain,
                sourceDefName = sourceDefName
            };
        }

        private static NarrativeEvidence Evidence()
        {
            return new NarrativeEvidence
            {
                eventId = "event-1",
                tick = 100,
                povPawnId = "pawn-1",
                povRole = "initiator",
                facet = NarrativeFacetTokens.IdentityTransition,
                phase = "changed",
                subjectKind = NarrativeSubjectKindTokens.Pawn,
                subjectId = "pawn-1",
                subjectLabel = "Ari",
                arcKey = "family|7",
                beliefTopics = new List<string> { "bonding" },
                salience = NarrativeSalienceTokens.Meaningful,
                pawnCanKnow = true,
                sourceDomain = "test",
                sourceDefName = "TestEvent"
            };
        }

        private static NarrativeLensCandidate Candidate(
            string key,
            string category,
            string facet,
            bool pawnCanKnow = true,
            string text = null,
            string arcKey = "",
            string subjectKind = "",
            string subjectId = "",
            List<string> topics = null,
            string relationship = NarrativeRelationshipTokens.None,
            string salience = NarrativeSalienceTokens.Meaningful,
            int sourceTick = 0,
            bool primaryFact = false)
        {
            return new NarrativeLensCandidate
            {
                candidateKey = key,
                provider = NarrativeProviderTokens.Core,
                category = category,
                // Different facts must remain independently eligible. Tests that intentionally exercise
                // duplicate-text collapse pass an explicit matching text value instead.
                text = text ?? "a factual note " + key,
                facet = facet,
                subjectKind = subjectKind,
                subjectId = subjectId,
                arcKey = arcKey,
                topicTokens = topics ?? new List<string>(),
                sourceEventId = "event-source-" + key,
                sourceTick = sourceTick,
                salience = salience,
                relationship = relationship,
                pawnCanKnow = pawnCanKnow,
                isPrimaryEventFact = primaryFact
            };
        }

        private static ReflectionOpportunity Opportunity(string kind)
        {
            return new ReflectionOpportunity
            {
                kind = kind,
                pawnId = "pawn-1",
                nowTick = 200000,
                sourceEventIds = new List<string> { "event-1", "event-2" },
                arcKeys = new List<string> { "family|7" },
                candidateMemoryCount = 2,
                linkedMemoryCount = 2,
                importance = NarrativeSalienceTokens.Major,
                due = true,
                hasCoherentLink = true,
                hasPhaseChange = true
            };
        }

        private static NarrativeDetailBudget Budget(NarrativePolicySnapshot policy, string level)
        {
            for (int i = 0; i < policy.detailBudgets.Count; i++)
            {
                if (policy.detailBudgets[i].detailLevel == level)
                {
                    return policy.detailBudgets[i];
                }
            }

            throw new InvalidOperationException("Missing detail budget " + level);
        }

        private static int CountSelected(List<NarrativeCandidateDiagnostic> diagnostics)
        {
            int count = 0;
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].selected)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool HasSelected(NarrativeContextSelection selection, string candidateKey)
        {
            for (int i = 0; i < selection.selectedCandidates.Count; i++)
            {
                if (selection.selectedCandidates[i].candidateKey == candidateKey)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsFacet(List<NarrativeEvidence> evidence, string facet)
        {
            if (evidence == null)
            {
                return false;
            }

            for (int i = 0; i < evidence.Count; i++)
            {
                if (evidence[i] != null && string.Equals(evidence[i].facet, facet, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertDiagnostic(NarrativeContextSelection selection, string key, string reason)
        {
            for (int i = 0; i < selection.diagnostics.Count; i++)
            {
                NarrativeCandidateDiagnostic diagnostic = selection.diagnostics[i];
                if (diagnostic.candidateKey == key && diagnostic.reason == reason)
                {
                    assertions++;
                    return;
                }
            }

            throw new InvalidOperationException("Expected diagnostic " + key + " / " + reason);
        }

        private static void AssertDiagnostic(ReflectionPlan plan, string key, string reason)
        {
            for (int i = 0; i < plan.diagnostics.Count; i++)
            {
                NarrativeCandidateDiagnostic diagnostic = plan.diagnostics[i];
                if (diagnostic.candidateKey == key && diagnostic.reason == reason)
                {
                    assertions++;
                    return;
                }
            }

            throw new InvalidOperationException("Expected reflection diagnostic " + key + " / " + reason);
        }

        private static void AssertEqual<T>(string label, T expected, T actual)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(label + ": expected " + expected + ", got " + actual);
            }
        }

        private static void AssertTrue(string label, bool value)
        {
            assertions++;
            if (!value)
            {
                throw new InvalidOperationException(label);
            }
        }
    }
}

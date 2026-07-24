// Standalone, no-RimWorld checks for the shared Narrative Continuity layer through N3-A's visible
// Anomaly provider, N3-B identity, N3-O's exact-map seasonal-flood pressure, and N3-R's exact
// succession/permit/Ascent applicability. The project links only pure source, making any accidental
// Verse/Unity/DLC dependency a compile-time failure.
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
            TestIdeologyInterpretationCompositionAndIsolation();
            TestCrossArcMemorySelection();
            TestTerminalReflectionQualification();
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

            string bondArc = "biotech-psychic-bond|pawn-1|pawn-2|3";
            NarrativeEvidence bondEvidence = Evidence();
            bondEvidence.povPawnId = "pawn-1";
            bondEvidence.facet = NarrativeFacetTokens.BondLifecycle;
            bondEvidence.subjectKind = NarrativeSubjectKindTokens.Pawn;
            bondEvidence.subjectId = "pawn-2";
            bondEvidence.arcKey = bondArc;
            BiotechNarrativeSnapshot bondSnapshot = new BiotechNarrativeSnapshot
            {
                providerAvailable = true,
                povPawnId = "pawn-1",
                bondPartnerId = "pawn-2",
                bondArcKey = bondArc,
                bondPhase = PsychicBondPhaseTokens.Ruptured,
                bondText = "A verified psychic bond ruptured.",
                sourceTick = 1000,
                pawnCanKnow = true,
                hasVerifiedPovConnection = true
            };
            List<NarrativeLensCandidate> bondCandidates = BiotechNarrativeProvider.Build(
                new List<NarrativeEvidence> { bondEvidence },
                bondSnapshot);
            AssertEqual("exact psychic-bond arc yields one N3-B candidate", 1,
                bondCandidates.Count);
            AssertEqual("psychic-bond candidate uses exact partner subject", "pawn-2",
                bondCandidates[0].subjectId);
            AssertEqual("psychic-bond candidate uses BondLifecycle facet",
                NarrativeFacetTokens.BondLifecycle, bondCandidates[0].facet);
            bondEvidence.arcKey = "biotech-psychic-bond|pawn-1|pawn-2|4";
            AssertEqual("different psychic-bond epoch cannot pull continuity", 0,
                BiotechNarrativeProvider.Build(
                    new List<NarrativeEvidence> { bondEvidence },
                    bondSnapshot).Count);
            bondEvidence.arcKey = bondArc;
            bondEvidence.povPawnId = "pawn-9";
            AssertEqual("different POV cannot receive psychic-bond continuity", 0,
                BiotechNarrativeProvider.Build(
                    new List<NarrativeEvidence> { bondEvidence },
                    bondSnapshot).Count);

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

            NarrativeEvidence successionEvidence = Evidence();
            successionEvidence.povPawnId = "pawn-1";
            successionEvidence.facet = NarrativeFacetTokens.IdentityTransition;
            successionEvidence.phase = "succession";
            successionEvidence.subjectKind = NarrativeSubjectKindTokens.Pawn;
            successionEvidence.subjectId = "pawn-1";
            successionEvidence.sourceDomain = "royalty_succession";
            successionEvidence.beliefTopics = new List<string>
                { "authority", "status", "duty", "death" };
            List<NarrativeLensCandidate> succession = RoyaltyNarrativeProvider.Build(
                new List<NarrativeEvidence> { successionEvidence }, snapshot);
            AssertEqual("exact heir-POV succession evidence reuses one current-title candidate",
                1, succession.Count);
            AssertEqual("succession identity keeps the stable faction-title candidate key",
                "royalty|title|pawn-1|Faction_2|Baron", succession[0].candidateKey);
            successionEvidence.subjectId = "pawn-2";
            AssertEqual("succession evidence for a different heir cannot pull this POV title", 0,
                RoyaltyNarrativeProvider.Build(
                    new List<NarrativeEvidence> { successionEvidence }, snapshot).Count);
            successionEvidence.subjectId = "pawn-1";

            NarrativeEvidence permitEvidence = Evidence();
            permitEvidence.povPawnId = "pawn-1";
            permitEvidence.facet = NarrativeFacetTokens.IdentityTransition;
            permitEvidence.phase = "military_aid";
            permitEvidence.subjectKind = NarrativeSubjectKindTokens.Pawn;
            permitEvidence.subjectId = "pawn-1";
            permitEvidence.sourceDomain = "royalty_permit";
            permitEvidence.sourceDefName = "RoyalPermitMilitaryAid";
            permitEvidence.beliefTopics = new List<string> { "authority", "service", "violence" };
            List<NarrativeLensCandidate> permit = RoyaltyNarrativeProvider.Build(
                new List<NarrativeEvidence> { permitEvidence }, snapshot);
            AssertEqual("exact caller-POV permit evidence reuses one current-title authority candidate",
                1, permit.Count);
            AssertEqual("permit authority keeps the stable faction-title candidate key",
                "royalty|title|pawn-1|Faction_2|Baron", permit[0].candidateKey);
            permitEvidence.povPawnId = "other-pawn";
            AssertEqual("permit evidence for a different POV cannot pull this pawn's title", 0,
                RoyaltyNarrativeProvider.Build(
                    new List<NarrativeEvidence> { permitEvidence }, snapshot).Count);

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

        private static void TestIdeologyInterpretationCompositionAndIsolation()
        {
            NarrativeEvidence evidence = Evidence();
            evidence.arcKey = string.Empty;
            IdeologyNarrativeSnapshot ideology = new IdeologyNarrativeSnapshot
            {
                providerAvailable = true,
                pawnCanKnow = true,
                hasVerifiedPovConnection = true,
                povPawnId = evidence.povPawnId,
                ideologyId = "Ideo_17",
                preceptKeyKind = "instance",
                preceptStableId = "Precept_42",
                text = "Within Ari's worldview, this event directly engaged bodily change.",
                sourceEvidence = evidence,
                topicTokens = new List<string> { "body_modification" }
            };
            List<NarrativeLensCandidate> ideologyCandidates = IdeologyNarrativeProvider.Build(
                new List<NarrativeEvidence> { evidence }, ideology);
            AssertEqual("N3-I provider creates one exact candidate", 1, ideologyCandidates.Count);
            string ideologyKey = ideologyCandidates[0].candidateKey;

            BiotechNarrativeSnapshot biotech = new BiotechNarrativeSnapshot
            {
                providerAvailable = true,
                pawnCanKnow = true,
                hasVerifiedPovConnection = true,
                povPawnId = evidence.povPawnId,
                childId = evidence.subjectId,
                identityStableKey = "gene|SyntheticGene",
                identityText = "A visible inherited trait changed.",
                identityTopicTokens = new List<string> { "identity" },
                sourceTick = evidence.tick
            };
            List<NarrativeLensCandidate> composed = NarrativeProviderOrchestrator.Collect(
                new List<NarrativeEvidence> { evidence }, null, null, biotech, null, null, ideology);
            AssertEqual("Ideology composes with an existing provider", 2, composed.Count);
            AssertEqual("fixed provider order places Ideology before Biotech", ideologyKey,
                composed[0].candidateKey);

            OdysseyNarrativeSnapshot odyssey = OdysseySnapshot();
            odyssey.povPawnId = evidence.povPawnId;
            odyssey.sourceTick = evidence.tick;
            List<NarrativeLensCandidate> ideologyAtHome = NarrativeProviderOrchestrator.Collect(
                new List<NarrativeEvidence> { evidence }, null, null, null, null, odyssey, ideology);
            AssertEqual("Ideology composes with verified Odyssey home context", 2,
                ideologyAtHome.Count);
            AssertEqual("fixed provider order places Ideology before Odyssey", ideologyKey,
                ideologyAtHome[0].candidateKey);
            AssertEqual("Odyssey home remains the second category",
                NarrativeCategoryTokens.Home, ideologyAtHome[1].category);

            NarrativePolicySnapshot policy = NarrativePolicySnapshot.CreateDefault();
            policy.maxSelectedCandidates = 2;
            Budget(policy, NarrativeDetailLevelTokens.Full).maxLenses = 2;
            Budget(policy, NarrativeDetailLevelTokens.Full).characterBudget = 1000;
            NarrativeContextRequest request = new NarrativeContextRequest
            {
                evidence = new List<NarrativeEvidence> { evidence },
                candidates = composed,
                policy = policy,
                currentTick = evidence.tick,
                detailLevel = NarrativeDetailLevelTokens.Full,
                deterministicSeed = 91
            };
            NarrativeContextSelection first = NarrativeContextSelector.Select(request);
            NarrativeContextSelection second = NarrativeContextSelector.Select(request);
            AssertEqual("N3-I selection is deterministic", first.narrativeContext,
                second.narrativeContext);
            AssertTrue("Full detail can use Ideology beside another category",
                first.selectedCandidates.Count == 2 && HasSelected(first, ideologyKey));
            AssertEqual("shared global lens budget remains two", 2, first.selectedCandidates.Count);

            request.candidates = ideologyAtHome;
            NarrativeContextSelection selectedAtHome = NarrativeContextSelector.Select(request);
            AssertTrue("Full detail selects Ideology and Odyssey from the same request",
                selectedAtHome.selectedCandidates.Count == 2
                    && HasSelected(selectedAtHome, ideologyKey)
                    && HasSelected(selectedAtHome, ideologyAtHome[1].candidateKey));
            AssertTrue("composed prompt retains the selected mobile-home fact",
                selectedAtHome.narrativeContext.IndexOf(odyssey.homeText, StringComparison.Ordinal) >= 0);

            NarrativeLensCandidate secondInterpretation = Candidate(
                "synthetic-second-interpretation", NarrativeCategoryTokens.Interpretation,
                NarrativeFacetTokens.IdentityTransition, sourceTick: evidence.tick);
            request.candidates = new List<NarrativeLensCandidate>
            {
                ideologyCandidates[0], secondInterpretation, composed[1]
            };
            NarrativeContextSelection capped = NarrativeContextSelector.Select(request);
            int interpretationCount = 0;
            for (int i = 0; i < capped.selectedCandidates.Count; i++)
                if (capped.selectedCandidates[i].category == NarrativeCategoryTokens.Interpretation)
                    interpretationCount++;
            AssertEqual("shared category budget admits one interpretation", 1, interpretationCount);
            AssertTrue("category cap still leaves room for another provider",
                capped.selectedCandidates.Count <= 2 && HasSelected(capped, composed[1].candidateKey));

            NarrativeLensCandidate fresh = Candidate(
                "fresh-chapter", NarrativeCategoryTokens.Chapter,
                NarrativeFacetTokens.IdentityTransition,
                topics: new List<string> { "bonding" },
                sourceTick: evidence.tick);
            Budget(policy, NarrativeDetailLevelTokens.Full).maxLenses = 1;
            AssertEqual("N3-I repetition fixture uses the shipped XML/default penalty",
                45, (int)policy.repetitionPenalty);
            request.candidates = new List<NarrativeLensCandidate> { ideologyCandidates[0], fresh };
            request.recentSelectedCandidateKeys = new List<string>();
            NarrativeContextSelection unrepeated = NarrativeContextSelector.Select(request);
            request.recentSelectedCandidateKeys = new List<string> { ideologyKey };
            NarrativeContextSelection repeated = NarrativeContextSelector.Select(request);
            AssertEqual("N3-I wins before repetition", ideologyKey,
                unrepeated.selectedCandidates.Count == 0
                    ? string.Empty
                    : unrepeated.selectedCandidates[0].candidateKey);
            AssertEqual("N3-I key participates in ordinary repetition history", fresh.candidateKey,
                repeated.selectedCandidates.Count == 0
                    ? string.Empty
                    : repeated.selectedCandidates[0].candidateKey);

            List<NarrativeLensCandidate> withoutIdeology = NarrativeProviderOrchestrator.Collect(
                new List<NarrativeEvidence> { evidence }, null, null, biotech, null, null, null);
            ideology.providerAvailable = false;
            List<NarrativeLensCandidate> inactiveIdeology = NarrativeProviderOrchestrator.Collect(
                new List<NarrativeEvidence> { evidence }, null, null, biotech, null, null, ideology);
            AssertEqual("inactive Ideology leaves other provider count unchanged",
                withoutIdeology.Count, inactiveIdeology.Count);
            AssertEqual("inactive Ideology leaves other provider ordering unchanged",
                withoutIdeology[0].candidateKey, inactiveIdeology[0].candidateKey);

            List<NarrativeLensCandidate> isolated = new List<NarrativeLensCandidate>
            {
                Candidate("before-failure", NarrativeCategoryTokens.Home,
                    NarrativeFacetTokens.IdentityTransition)
            };
            string failedProvider = string.Empty;
            NarrativeProviderOrchestrator.AddSafely(
                isolated,
                NarrativeProviderTokens.Ideology,
                () => throw new InvalidOperationException("synthetic provider failure"),
                (provider, exception) => failedProvider = provider);
            NarrativeProviderOrchestrator.AddSafely(
                isolated,
                NarrativeProviderTokens.Biotech,
                () => new List<NarrativeLensCandidate>
                {
                    Candidate("after-failure", NarrativeCategoryTokens.Identity,
                        NarrativeFacetTokens.IdentityTransition)
                });
            AssertEqual("provider failure callback identifies Ideology",
                NarrativeProviderTokens.Ideology, failedProvider);
            AssertEqual("provider failure preserves prior and later candidates", 2, isolated.Count);
            AssertEqual("provider failure preserves deterministic continuation", "after-failure",
                isolated[1].candidateKey);
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

            OdysseyNarrativeSnapshot malformedHome = OdysseySnapshot();
            malformedHome.environmentalPressures.Add(SeasonalFloodFact(
                malformedHome, 999, "Flood fact survives malformed home metadata."));
            malformedHome.journeyId = "odyssey-journey|wrong|1";
            List<NarrativeLensCandidate> malformedArc = OdysseyNarrativeProvider.Build(landing, malformedHome);
            AssertEqual("unsafe journey arc omits only the mobile-home lens", 1, malformedArc.Count);
            AssertEqual("unsafe journey arc preserves independent exact-map pressure",
                NarrativeCategoryTokens.Pressure, malformedArc[0].category);
            malformedHome.journeyId = string.Empty;
            malformedHome.sourceTick = -1;
            List<NarrativeLensCandidate> negativeHomeTick = OdysseyNarrativeProvider.Build(
                landing, malformedHome);
            AssertEqual("negative mobile-home tick omits only the mobile-home lens", 1,
                negativeHomeTick.Count);
            AssertEqual("negative mobile-home tick keeps the pressure fact's own valid tick", 999,
                negativeHomeTick[0].sourceTick);

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
            AssertEqual("recent-key store scan uses at least one slot", 1,
                NarrativePersistencePolicy.RecentSelectionKeyScanCap(0));
            AssertEqual("recent-key store scan honors a smaller configured cap", 4,
                NarrativePersistencePolicy.RecentSelectionKeyScanCap(4));
            AssertEqual("recent-key store scan never exceeds the persistence hard cap",
                NarrativePersistencePolicy.HardSelectedCandidateKeyCap,
                NarrativePersistencePolicy.RecentSelectionKeyScanCap(int.MaxValue));
            AssertEqual("subject index needs both identity parts", string.Empty,
                NarrativePersistencePolicy.SubjectIndexKey(NarrativeSubjectKindTokens.Pawn, string.Empty));
            AssertEqual("subject index is stable", "pawn|pawn-1",
                NarrativePersistencePolicy.SubjectIndexKey(NarrativeSubjectKindTokens.Pawn, "pawn-1"));

            AssertEqual("empty narrative context emits no field", string.Empty,
                NarrativeContextPrompt.Compose(" ", "Use facts."));
            AssertEqual("narrative context keeps policy instruction and whole fact", "Use facts.\nA complete fact.",
                NarrativeContextPrompt.Compose("A complete fact.", "Use facts."));
        }

        private static void TestTerminalReflectionQualification()
        {
            NarrativeEvidence evidence = new NarrativeEvidence
            {
                eventId = "terminal-event",
                tick = 700,
                povPawnId = "Pawn_7",
                povRole = "initiator",
                facet = NarrativeFacetTokens.JourneyChapter,
                phase = "defeated",
                subjectKind = NarrativeSubjectKindTokens.Entity,
                subjectId = "Boss_A",
                arcKey = "biotech-mechanitor|Pawn_7|600",
                salience = NarrativeSalienceTokens.Terminal,
                pawnCanKnow = true,
                sourceDomain = "progression",
                sourceDefName = "BiotechBossDefeated"
            };
            NarrativeReference reference = NarrativeReferencePolicy.FromEvidence(evidence);
            TerminalReflectionRequest request = new TerminalReflectionRequest
            {
                canonicalEventId = evidence.eventId,
                canonicalEventTick = evidence.tick,
                povPawnId = evidence.povPawnId,
                povRole = evidence.povRole,
                contract = new TerminalReflectionContract
                {
                    ownershipCorrelated = true,
                    phase = evidence.phase,
                    arcKey = evidence.arcKey,
                    sourceDomain = evidence.sourceDomain,
                    sourceDefName = evidence.sourceDefName
                },
                evidence = new List<NarrativeEvidence> { evidence },
                references = new List<NarrativeReference> { reference }
            };

            TerminalReflectionDecision valid = TerminalReflectionPolicy.Evaluate(request);
            AssertTrue("exact terminal evidence queues one major arc", valid.queueMajorArc);
            AssertEqual("terminal queue preserves canonical avoid id",
                evidence.eventId, valid.avoidRelatedEventId);

            AssertTrue("null terminal request fails closed",
                !TerminalReflectionPolicy.Evaluate(null).queueMajorArc);
            TerminalReflectionContract contract = request.contract;
            request.contract = null;
            AssertTrue("null terminal contract fails closed",
                !TerminalReflectionPolicy.Evaluate(request).queueMajorArc);
            request.contract = contract;

            request.contract.ownershipCorrelated = false;
            AssertTrue("failed terminal ownership cannot queue",
                !TerminalReflectionPolicy.Evaluate(request).queueMajorArc);
            request.contract.ownershipCorrelated = true;

            string phase = evidence.phase;
            evidence.phase = "called";
            AssertTrue("mismatched terminal phase cannot queue",
                !TerminalReflectionPolicy.Evaluate(request).queueMajorArc);
            evidence.phase = phase;

            string arc = evidence.arcKey;
            evidence.arcKey = "unrelated|arc";
            AssertTrue("mismatched terminal arc cannot queue",
                !TerminalReflectionPolicy.Evaluate(request).queueMajorArc);
            evidence.arcKey = arc;

            evidence.salience = NarrativeSalienceTokens.Major;
            AssertTrue("non-terminal salience cannot queue",
                !TerminalReflectionPolicy.Evaluate(request).queueMajorArc);
            evidence.salience = NarrativeSalienceTokens.Terminal;

            evidence.pawnCanKnow = false;
            AssertTrue("hidden terminal evidence cannot queue",
                !TerminalReflectionPolicy.Evaluate(request).queueMajorArc);
            evidence.pawnCanKnow = true;

            string facet = evidence.facet;
            evidence.facet = NarrativeFacetTokens.IdentityTransition;
            AssertTrue("wrong terminal facet cannot queue",
                !TerminalReflectionPolicy.Evaluate(request).queueMajorArc);
            evidence.facet = facet;

            evidence.povPawnId = "Pawn_other";
            AssertTrue("wrong terminal actor or witness cannot queue",
                !TerminalReflectionPolicy.Evaluate(request).queueMajorArc);
            evidence.povPawnId = request.povPawnId;

            evidence.sourceDefName = "BiotechBossCalled";
            AssertTrue("wrong terminal source cannot queue",
                !TerminalReflectionPolicy.Evaluate(request).queueMajorArc);
            evidence.sourceDefName = request.contract.sourceDefName;

            int canonicalTick = request.canonicalEventTick;
            request.canonicalEventTick = canonicalTick + 1;
            AssertTrue("mismatched canonical terminal tick cannot queue",
                !TerminalReflectionPolicy.Evaluate(request).queueMajorArc);
            request.canonicalEventTick = -1;
            AssertTrue("negative canonical terminal tick fails closed",
                !TerminalReflectionPolicy.Evaluate(request).queueMajorArc);
            request.canonicalEventTick = canonicalTick;

            List<NarrativeEvidence> evidenceRows = request.evidence;
            request.evidence = null;
            AssertTrue("null terminal evidence list fails closed",
                !TerminalReflectionPolicy.Evaluate(request).queueMajorArc);
            request.evidence = evidenceRows;

            request.references.Clear();
            AssertTrue("missing saved terminal reference cannot queue",
                !TerminalReflectionPolicy.Evaluate(request).queueMajorArc);
            request.references.Add(reference);

            reference.sourceEventId = "other-event";
            AssertTrue("mismatched saved terminal reference cannot queue",
                !TerminalReflectionPolicy.Evaluate(request).queueMajorArc);
            reference.sourceEventId = evidence.eventId;

            List<NarrativeReference> referenceRows = request.references;
            request.references = null;
            AssertTrue("null terminal reference list fails closed",
                !TerminalReflectionPolicy.Evaluate(request).queueMajorArc);
            request.references = referenceRows;

            request.evidence.Insert(0, null);
            request.evidence.Insert(1, new NarrativeEvidence
            {
                eventId = evidence.eventId,
                tick = evidence.tick,
                povPawnId = evidence.povPawnId,
                povRole = evidence.povRole,
                facet = NarrativeFacetTokens.IdentityTransition
            });
            AssertTrue("malformed terminal rows do not hide a later exact row",
                TerminalReflectionPolicy.Evaluate(request).queueMajorArc);

            int retryMax = 3600000;
            AssertTrue("terminal debt retries at its exact age ceiling",
                TerminalReflectionRetryPolicy.CanRetry(
                    true, evidence.eventId, 100, 100 + retryMax, retryMax));
            AssertTrue("terminal debt expires after its age ceiling",
                TerminalReflectionRetryPolicy.IsExpired(
                    evidence.eventId, 100, 101 + retryMax, retryMax));
            AssertTrue("ordinary major debt does not gain terminal retries",
                !TerminalReflectionRetryPolicy.CanRetry(
                    true, string.Empty, 100, 101, retryMax));
            AssertTrue("a defensive clock rewind preserves terminal debt",
                TerminalReflectionRetryPolicy.CanRetry(
                    true, evidence.eventId, 200, 100, retryMax));
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

            ReflectionOpportunity quadrum = Opportunity(NarrativeReflectionKindTokens.Quadrum);
            ReflectionOpportunity day = Opportunity(NarrativeReflectionKindTokens.Day);
            ReflectionPlan existingSources = ReflectionCoordinator.Plan(new ReflectionPlanningRequest
            {
                policy = policy,
                currentTick = 200000,
                opportunities = new List<ReflectionOpportunity> { day, quadrum, major }
            });
            AssertEqual("major arc beats simultaneous quadrum and day work",
                NarrativeReflectionKindTokens.MajorArc, existingSources.selectedOpportunity.kind);
            AssertEqual("major/quadrum/day arbitration still selects exactly one", 1,
                CountSelected(existingSources.diagnostics));

            ReflectionPlan shorterCadences = ReflectionCoordinator.Plan(new ReflectionPlanningRequest
            {
                policy = policy,
                currentTick = 200000,
                opportunities = new List<ReflectionOpportunity> { day, quadrum }
            });
            AssertEqual("quadrum beats a simultaneous day reflection",
                NarrativeReflectionKindTokens.Quadrum, shorterCadences.selectedOpportunity.kind);
            AssertEqual("quadrum/day arbitration still selects exactly one", 1,
                CountSelected(shorterCadences.diagnostics));

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

            ReflectionPlan cooldownBoundaryBlocked = ReflectionCoordinator.Plan(new ReflectionPlanningRequest
            {
                policy = policy,
                currentTick = 199999,
                lastReflectionTick = 140000,
                opportunities = new List<ReflectionOpportunity> { belief }
            });
            AssertTrue("global reflection cooldown remains active one tick before its exact boundary",
                cooldownBoundaryBlocked.selectedOpportunity == null);

            ReflectionPlan cooldownBoundaryOpen = ReflectionCoordinator.Plan(new ReflectionPlanningRequest
            {
                policy = policy,
                currentTick = 200000,
                lastReflectionTick = 140000,
                opportunities = new List<ReflectionOpportunity> { belief }
            });
            AssertEqual("global reflection cooldown opens exactly at its XML-owned boundary",
                NarrativeReflectionKindTokens.Belief, cooldownBoundaryOpen.selectedOpportunity.kind);

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

            ReflectionPlan disabledDuringGlobalCooldown = ReflectionCoordinator.Plan(
                new ReflectionPlanningRequest
                {
                    policy = policy,
                    currentTick = 200000,
                    lastReflectionTick = 199999,
                    opportunities = new List<ReflectionOpportunity> { disabled }
                });
            AssertTrue("global cooldown still bounds a disabled group's debt",
                HasDebtInstruction(disabledDuringGlobalCooldown, NarrativeReflectionKindTokens.Day));

            NarrativePolicySnapshot disabledPolicy = NarrativePolicySnapshot.CreateDefault();
            disabledPolicy.enabled = false;
            ReflectionPlan policyDisabled = ReflectionCoordinator.Plan(new ReflectionPlanningRequest
            {
                policy = disabledPolicy,
                currentTick = 200000,
                opportunities = new List<ReflectionOpportunity>
                {
                    Opportunity(NarrativeReflectionKindTokens.Day)
                }
            });
            AssertTrue("disabling the shared reflection policy also bounds cadence debt",
                HasDebtInstruction(policyDisabled, NarrativeReflectionKindTokens.Day));

            ReflectionOpportunity disabledMajor = Opportunity(NarrativeReflectionKindTokens.MajorArc);
            disabledMajor.groupEnabled = false;
            ReflectionOpportunity eligibleDay = Opportunity(NarrativeReflectionKindTokens.Day);
            ReflectionPlan disabledHigherFallback = ReflectionCoordinator.Plan(new ReflectionPlanningRequest
            {
                policy = policy,
                currentTick = 200000,
                opportunities = new List<ReflectionOpportunity> { disabledMajor, eligibleDay }
            });
            AssertEqual("disabled higher priority does not block an eligible lower reflection",
                NarrativeReflectionKindTokens.Day, disabledHigherFallback.selectedOpportunity.kind);
            AssertTrue("disabled higher priority still receives its debt-bounding instruction",
                HasDebtInstruction(disabledHigherFallback, NarrativeReflectionKindTokens.MajorArc));

            ReflectionPlan perKindCooldown = ReflectionCoordinator.Plan(new ReflectionPlanningRequest
            {
                policy = policy,
                currentTick = 200000,
                opportunities = new List<ReflectionOpportunity> { quadrum, day },
                history = new List<ReflectionHistoryEntry>
                {
                    new ReflectionHistoryEntry
                    {
                        kind = NarrativeReflectionKindTokens.Quadrum,
                        writtenTick = 199999
                    }
                }
            });
            AssertEqual("quadrum kind cooldown allows the eligible day fallback",
                NarrativeReflectionKindTokens.Day, perKindCooldown.selectedOpportunity.kind);
            AssertDiagnostic(perKindCooldown, NarrativeReflectionKindTokens.Quadrum,
                NarrativeDiagnosticTokens.ReflectionCooldown);

            ReflectionPlan crossKindCooldown = ReflectionCoordinator.Plan(new ReflectionPlanningRequest
            {
                policy = policy,
                currentTick = 200000,
                opportunities = new List<ReflectionOpportunity> { cross, belief, quadrum, day },
                history = new List<ReflectionHistoryEntry>
                {
                    new ReflectionHistoryEntry
                    {
                        kind = NarrativeReflectionKindTokens.CrossArc,
                        writtenTick = 199999
                    }
                }
            });
            AssertEqual("cross-arc kind cooldown falls through to belief before shorter cadences",
                NarrativeReflectionKindTokens.Belief, crossKindCooldown.selectedOpportunity.kind);
            AssertDiagnostic(crossKindCooldown, NarrativeReflectionKindTokens.CrossArc,
                NarrativeDiagnosticTokens.ReflectionCooldown);

            AssertTrue("failed dispatch cannot acknowledge selected cadence state",
                !ReflectionCoordinator.CanConsumeAfterDispatch(shorterCadences, false));
            AssertTrue("successful dispatch acknowledges only the selected cadence state",
                ReflectionCoordinator.CanConsumeAfterDispatch(shorterCadences, true));
        }

        private static void TestCrossArcMemorySelection()
        {
            CrossArcMemorySelection linked = CrossArcReflectionMemorySelector.Select(
                CrossRequest(
                    CrossMemory("event-start", 100, "family|7", "began"),
                    CrossMemory("event-change", 200, "family|7", "changed")));
            AssertTrue("two exact linked phases qualify", linked.qualified);
            AssertEqual("linked selection records two distinct phases", 2, linked.distinctPhaseCount);
            AssertEqual("linked selection keeps oldest-to-newest order", "event-start",
                linked.sourceEventIds[0]);

            CrossArcMemorySelection unrelatedDlcPages = CrossArcReflectionMemorySelector.Select(
                CrossRequest(
                    CrossMemory("royalty-page", 100, "royal|pawn-1", "gained"),
                    CrossMemory("biotech-page", 200, "gene|pawn-1", "changed")));
            AssertTrue("different DLC memories without an exact shared arc or subject do not qualify",
                !unrelatedDlcPages.qualified);

            CrossArcMemorySelection selfSubjectOnly = CrossArcReflectionMemorySelector.Select(
                CrossRequest(
                    CrossSubjectMemory(
                        "royalty-self", 100, NarrativeSubjectKindTokens.Pawn, "pawn-1", "granted"),
                    CrossSubjectMemory(
                        "biotech-self", 200, NarrativeSubjectKindTokens.Pawn, "pawn-1", "implanted")));
            AssertTrue("the POV pawn's own subject id cannot link otherwise unrelated DLC memories",
                !selfSubjectOnly.qualified);

            CrossArcMemorySelection sharedOtherSubject = CrossArcReflectionMemorySelector.Select(
                CrossRequest(
                    CrossSubjectMemory(
                        "bond-start", 100, NarrativeSubjectKindTokens.Pawn, "pawn-2", "formed"),
                    CrossSubjectMemory(
                        "bond-end", 200, NarrativeSubjectKindTokens.Pawn, "pawn-2", "ruptured")));
            AssertTrue("an exact non-self subject still links two genuine phases",
                sharedOtherSubject.qualified);

            CrossArcMemoryCandidate priorReflection =
                CrossMemory("reflection-page", 200, "family|7", "changed");
            priorReflection.reflection = true;
            CrossArcMemorySelection reflectionsExcluded = CrossArcReflectionMemorySelector.Select(
                CrossRequest(
                    CrossMemory("event-start", 100, "family|7", "began"),
                    priorReflection));
            AssertTrue("prior reflection pages are excluded from linked memory selection",
                !reflectionsExcluded.qualified);

            CrossArcMemoryCandidate priorRecap =
                CrossMemory("recap-page", 200, "family|7", "changed");
            priorRecap.recap = true;
            AssertTrue("unrelated recap pages are excluded from linked memory selection",
                !CrossArcReflectionMemorySelector.Select(
                    CrossRequest(
                        CrossMemory("event-start", 100, "family|7", "began"),
                        priorRecap)).qualified);

            AssertTrue("future memories are excluded",
                !CrossArcReflectionMemorySelector.Select(
                    CrossRequest(
                        CrossMemory("event-start", 100, "family|7", "began"),
                        CrossMemory("future-change", 1001, "family|7", "changed"))).qualified);

            CrossArcMemorySelectionRequest boundaryRequest = CrossRequest(
                CrossMemory("boundary-start", 100, "family|7", "began"),
                CrossMemory("later-change", 200, "family|7", "changed"));
            boundaryRequest.eligibleAfterTick = 100;
            AssertTrue("the prior cross-arc boundary is exclusive",
                !CrossArcReflectionMemorySelector.Select(boundaryRequest).qualified);

            CrossArcMemoryCandidate missingReference =
                CrossMemory("missing-ref", 200, "family|7", "changed");
            missingReference.references.Clear();
            AssertTrue("a page without an exact saved reference is excluded",
                !CrossArcReflectionMemorySelector.Select(
                    CrossRequest(
                        CrossMemory("event-start", 100, "family|7", "began"),
                        missingReference)).qualified);

            CrossArcMemorySelectionRequest cappedRequest = CrossRequest(
                CrossMemory("cap-old", 100, "family|7", "began"),
                CrossMemory("cap-middle", 200, "family|7", "changed"),
                CrossMemory("cap-new", 300, "family|7", "settled"));
            cappedRequest.candidateScanCap = 2;
            CrossArcMemorySelection capped = CrossArcReflectionMemorySelector.Select(cappedRequest);
            AssertTrue("candidate scan cap keeps the newest bounded linked set",
                capped.qualified && capped.candidateCount == 2);
            AssertEqual("candidate scan cap drops the oldest row", "cap-middle",
                capped.sourceEventIds[0]);

            AssertTrue("configured change-or-consequence gate rejects neutral linked phases",
                !CrossArcReflectionMemorySelector.Select(
                    CrossRequest(
                        CrossMemory("neutral-start", 100, "family|7", "began", "neutral"),
                        CrossMemory("neutral-end", 200, "family|7", "ended", "neutral"))).qualified);

            CrossArcMemoryCandidate multiReference =
                CrossMemory("multi-reference", 200, "family|7", "began", "neutral");
            multiReference.references.Add(new NarrativeReference
            {
                arcKey = "family|7",
                phase = "changed",
                facet = NarrativeFacetTokens.IdentityTransition,
                sourceEventId = "multi-reference",
                sourceTick = 200
            });
            AssertTrue("all facts on duplicate exact-link rows contribute to phase and change gates",
                CrossArcReflectionMemorySelector.Select(
                    CrossRequest(
                        CrossMemory("single-phase", 100, "family|7", "began", "neutral"),
                        multiReference)).qualified);

            // Runtime archive rows project their saved NarrativeReference into this same DTO. No hot
            // DiaryEvent or provider object is required for the exact link to remain selectable.
            CrossArcMemorySelection archiveOnly = CrossArcReflectionMemorySelector.Select(
                CrossRequest(
                    CrossMemory("archive-start", 100, "journey|ship-2", "departed"),
                    CrossMemory("archive-end", 300, "journey|ship-2", "landed")));
            AssertTrue("archive-only exact saved references remain eligible", archiveOnly.qualified);
            AssertEqual("archive-only selection preserves both source ids", 2,
                archiveOnly.sourceEventIds.Count);

            CrossArcMemorySelection stableA = CrossArcReflectionMemorySelector.Select(
                CrossRequest(
                    CrossMemory("z-old", 100, "z-arc", "began"),
                    CrossMemory("z-new", 300, "z-arc", "ended"),
                    CrossMemory("a-old", 100, "a-arc", "began"),
                    CrossMemory("a-new", 300, "a-arc", "ended")));
            CrossArcMemorySelection stableB = CrossArcReflectionMemorySelector.Select(
                CrossRequest(
                    CrossMemory("a-new", 300, "a-arc", "ended"),
                    CrossMemory("z-new", 300, "z-arc", "ended"),
                    CrossMemory("a-old", 100, "a-arc", "began"),
                    CrossMemory("z-old", 100, "z-arc", "began")));
            AssertEqual("equal linked groups use lexical arc tie-breaking", "a-old",
                stableA.sourceEventIds[0]);
            AssertEqual("candidate input order cannot change linked selection",
                string.Join("|", stableA.sourceEventIds), string.Join("|", stableB.sourceEventIds));
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
                hasPhaseChange = true,
                hasChangeOrConsequence = true
            };
        }

        private static CrossArcMemorySelectionRequest CrossRequest(
            params CrossArcMemoryCandidate[] candidates)
        {
            return new CrossArcMemorySelectionRequest
            {
                pawnId = "pawn-1",
                currentTick = 1000,
                candidateScanCap = 16,
                memoryCap = 8,
                minimumLinkedMemories = 2,
                minimumDistinctPhases = 2,
                maximumSpanTicks = 1000,
                requireChangeOrConsequence = true,
                changeOrConsequenceFacets = new List<string>
                {
                    NarrativeFacetTokens.IdentityTransition,
                    NarrativeFacetTokens.BondLifecycle,
                    NarrativeFacetTokens.JourneyChapter,
                    NarrativeFacetTokens.AmbientPressure
                },
                candidates = new List<CrossArcMemoryCandidate>(candidates)
            };
        }

        private static CrossArcMemoryCandidate CrossMemory(
            string eventId,
            int tick,
            string arcKey,
            string phase,
            string facet = NarrativeFacetTokens.IdentityTransition)
        {
            return new CrossArcMemoryCandidate
            {
                eventId = eventId,
                pawnId = "pawn-1",
                tick = tick,
                text = eventId,
                salience = NarrativeSalienceTokens.Meaningful,
                references = new List<NarrativeReference>
                {
                    new NarrativeReference
                    {
                        arcKey = arcKey,
                        phase = phase,
                        facet = facet,
                        sourceEventId = eventId,
                        sourceTick = tick
                    }
                }
            };
        }

        private static CrossArcMemoryCandidate CrossSubjectMemory(
            string eventId,
            int tick,
            string subjectKind,
            string subjectId,
            string phase)
        {
            CrossArcMemoryCandidate candidate = CrossMemory(eventId, tick, string.Empty, phase);
            candidate.references[0].subjectKind = subjectKind;
            candidate.references[0].subjectId = subjectId;
            return candidate;
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

        private static bool HasDebtInstruction(ReflectionPlan plan, string kind)
        {
            for (int i = 0; i < plan.stateInstructions.Count; i++)
            {
                ReflectionStateConsumption instruction = plan.stateInstructions[i];
                if (instruction != null
                    && instruction.kind == kind
                    && instruction.advanceDebtWhenGroupDisabled
                    && !instruction.consumeAfterSuccessfulDispatch)
                {
                    return true;
                }
            }

            return false;
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

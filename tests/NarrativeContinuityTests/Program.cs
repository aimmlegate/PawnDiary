// Standalone, no-RimWorld checks for the shared Narrative Continuity layer through the first N3-B
// gene-identity extension. The project
// file links only pure source, making any accidental Verse/Unity/DLC dependency a compile-time failure.
using System;
using System.Collections.Generic;
using PawnDiary;

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
            TestOdysseyProviderEvidenceAndCrossDlcGates();
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
                null);
            AssertEqual("fixed provider list preserves core-first deterministic order", "core-first",
                fixedOrder[0].candidateKey);
            AssertEqual("empty future provider stubs add no candidates", 3, fixedOrder.Count);
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
            OdysseyNarrativeSnapshot snapshot = new OdysseyNarrativeSnapshot
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

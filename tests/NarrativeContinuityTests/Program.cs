// Standalone, no-RimWorld checks for Master Wave 0 / Narrative Continuity N0. The project file links
// only pure source, which makes any accidental Verse/Unity/DLC dependency a compile-time failure.
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
            TestCategoryCapsAndStableTieBreak();
            TestTopicRedundancyKeepsNewFacts();
            TestDetailCapsAndCompleteFactBudget();
            TestReferenceEqualityAndDeduplication();
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

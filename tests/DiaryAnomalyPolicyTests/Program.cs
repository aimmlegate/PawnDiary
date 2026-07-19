// Standalone no-RimWorld tests for Master Wave 7 / Anomaly Phase A1.0. Linking only the plain DTOs
// and pure policies makes an accidental Verse, Unity, Harmony, DLC, or live-settings dependency a
// compile-time failure.
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PawnDiary.Capture;

namespace DiaryAnomalyPolicyTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestFrozenEventAndPromptSchema();
            TestPolicyDefaults();
            TestShippedPolicyAndLocalization();
            TestInvalidAndNonProgressingStudy();
            TestFirstBreakthroughAndEligibility();
            TestCompletionHistory();
            TestPromotionsAndMultiNoteJump();
            TestMonolithStateOnlyEnrichment();
            TestTaleOwnershipExactness();
            TestTaleOwnershipExpiryAndConsumeOnce();
            TestInvalidContainmentScopes();
            TestContainmentAggregationAndBounds();
            TestContainmentWriterRanking();
            TestContainmentRadiusAndCaps();
            TestContainmentDedupAndDeterminism();
            Console.WriteLine("DiaryAnomalyPolicyTests passed " + assertions + " assertions.");
            return 0;
        }

        private static void TestFrozenEventAndPromptSchema()
        {
            AssertEqual("study event", "PawnDiary_AnomalyStudyBreakthrough",
                AnomalyEventDefNames.StudyBreakthrough);
            AssertEqual("breach event", "PawnDiary_ContainmentBreach",
                AnomalyEventDefNames.ContainmentBreach);
            AssertEqual("creepjoiner event", "PawnDiary_CreepJoinerOutcome",
                AnomalyEventDefNames.CreepJoinerOutcome);
            AssertEqual("ghoul event", "PawnDiary_GhoulTransformation",
                AnomalyEventDefNames.GhoulTransformation);
            AssertEqual("void event", "PawnDiary_VoidOutcome", AnomalyEventDefNames.VoidOutcome);
            AssertEqual("kind key", "anomaly_kind", AnomalyContextKeys.Kind);
            AssertEqual("stage key", "study_stage", AnomalyContextKeys.StudyStage);
            AssertEqual("studied def key", "studied_def", AnomalyContextKeys.StudiedDef);
            AssertEqual("category key", "knowledge_category", AnomalyContextKeys.KnowledgeCategory);
            AssertEqual("previous monolith key", "monolith_previous_level",
                AnomalyContextKeys.MonolithPreviousLevel);
            AssertEqual("reached monolith key", "monolith_reached_level",
                AnomalyContextKeys.MonolithReachedLevel);
            AssertEqual("activatable monolith key", "monolith_became_activatable",
                AnomalyContextKeys.MonolithBecameActivatable);
            AssertEqual("escape count key", "escaped_count", AnomalyContextKeys.EscapedCount);
            AssertEqual("escaped entities key", "escaped_entities", AnomalyContextKeys.EscapedEntities);
            AssertEqual("additional escape key", "additional_escaped_count",
                AnomalyContextKeys.AdditionalEscapedCount);
            AssertEqual("witness key", "witness_role", AnomalyContextKeys.WitnessRole);
            AssertEqual("creepjoiner phase key", "creepjoiner_phase",
                AnomalyContextKeys.CreepJoinerPhase);
            AssertEqual("visible result key", "visible_result", AnomalyContextKeys.VisibleResult);
            AssertEqual("transformation key", "transformation", AnomalyContextKeys.Transformation);
            AssertEqual("void outcome key", "void_outcome", AnomalyContextKeys.VoidOutcome);
            AssertEqual("terminal key", "terminal", AnomalyContextKeys.Terminal);
            AssertEqual("study kind", "study_breakthrough", AnomalyKindTokens.StudyBreakthrough);
            AssertEqual("breach kind", "containment_breach", AnomalyKindTokens.ContainmentBreach);
            AssertEqual("creepjoiner kind", "creepjoiner_outcome",
                AnomalyKindTokens.CreepJoinerOutcome);
            AssertEqual("ghoul kind", "ghoul_transformation", AnomalyKindTokens.GhoulTransformation);
            AssertEqual("void kind", "void_outcome", AnomalyKindTokens.VoidOutcome);
            AssertEqual("first stage", "first_breakthrough",
                AnomalyStudyStageTokens.FirstBreakthrough);
            AssertEqual("completion stage", "completed_kind", AnomalyStudyStageTokens.CompletedKind);
            AssertEqual("promotion stage", "promoted", AnomalyStudyStageTokens.Promoted);
            AssertEqual("nearby role", "nearby", AnomalyWitnessRoleTokens.Nearby);
            AssertEqual("recent studier role", "recent_studier",
                AnomalyWitnessRoleTokens.RecentStudier);
            AssertEqual("colony role", "colony_witness", AnomalyWitnessRoleTokens.ColonyWitness);
            AssertEqual("future visible reject token", "rejected", AnomalyOutcomeTokens.Rejected);
            AssertEqual("future surgical token", "surgical_reveal",
                AnomalyOutcomeTokens.SurgicalReveal);
            AssertEqual("future aggressive token", "aggressive", AnomalyOutcomeTokens.Aggressive);
            AssertEqual("future departed token", "departed", AnomalyOutcomeTokens.Departed);
            AssertEqual("future ghoul token", "ghoul", AnomalyOutcomeTokens.Ghoul);
            AssertEqual("future embraced token", "embraced", AnomalyOutcomeTokens.Embraced);
            AssertEqual("future visible void token", "disrupted", AnomalyOutcomeTokens.Disrupted);
        }

        private static void TestPolicyDefaults()
        {
            AnomalyPolicySnapshot policy = AnomalyPolicySnapshot.CreateDefault();
            AssertTrue("study enabled", policy.studyEnabled);
            AssertTrue("first enabled", policy.recordFirstStudyBreakthrough);
            AssertTrue("completion enabled", policy.recordCompletedEntityKind);
            AssertEqual("no hardcoded promotions", 0, policy.promotedStudyMilestones.Count);
            AssertEqual("monolith age", 60000, policy.monolithKnowledgeMaxAgeTicks);
            AssertEqual("study Tale expiry", 2500, policy.studyTaleSuppressionTicks);
            AssertTrue("containment enabled", policy.containmentEnabled);
            AssertEqual("witness radius", 12, policy.containmentWitnessRadius);
            AssertEqual("writer default", 2, policy.containmentMaxWriters);
            AssertEqual("entity-label default", 3, policy.containmentMaxEntityLabelsInContext);
            AssertEqual("breach dedup", 2500, policy.containmentDedupTicks);
            AssertEqual("recent studier age", 60000, policy.recentStudierMaxAgeTicks);
            AssertTrue("future creepjoiner enabled", policy.creepJoinerEnabled);
            AssertTrue("future ghoul enabled", policy.ghoulTransformationEnabled);
            AssertTrue("future void enabled", policy.voidOutcomeEnabled);
            AssertEqual("ownership depth", 8, policy.taleOwnershipMaxDepth);
            AssertEqual("ownership expiry", 2500, policy.taleOwnershipExpiryTicks);
        }

        private static void TestShippedPolicyAndLocalization()
        {
            string root = RepositoryRoot();
            string policyPath = Path.Combine(root, "1.6", "Defs", "DiaryAnomalyPolicyDefs.xml");
            XDocument document = XDocument.Load(policyPath);
            XElement def = document.Descendants("PawnDiary.DiaryAnomalyPolicyDef").Single();
            AssertEqual("XML policy defName", "Diary_AnomalyPolicy", Value(def, "defName"));
            AssertEqual("XML study enabled", "true", Value(def, "studyEnabled"));
            AssertEqual("XML first enabled", "true", Value(def, "recordFirstStudyBreakthrough"));
            AssertEqual("XML completion enabled", "true", Value(def, "recordCompletedEntityKind"));
            AssertEqual("XML promotion list starts empty", 0,
                def.Element("promotedStudyMilestones").Elements("li").Count());
            AssertEqual("XML monolith age", "60000", Value(def, "monolithKnowledgeMaxAgeTicks"));
            AssertEqual("XML study Tale window", "2500", Value(def, "studyTaleSuppressionTicks"));
            AssertEqual("XML containment radius", "12", Value(def, "containmentWitnessRadius"));
            AssertEqual("XML writer cap", "2", Value(def, "containmentMaxWriters"));
            AssertEqual("XML entity-label cap", "3",
                Value(def, "containmentMaxEntityLabelsInContext"));
            AssertEqual("XML containment dedup", "2500", Value(def, "containmentDedupTicks"));
            AssertEqual("XML recent-studier age", "60000", Value(def, "recentStudierMaxAgeTicks"));
            AssertEqual("XML ownership depth", "8", Value(def, "taleOwnershipMaxDepth"));
            AssertEqual("XML ownership expiry", "2500", Value(def, "taleOwnershipExpiryTicks"));
            AssertTrue("policy makes no conditional DLC Def reference",
                !document.Descendants().Any(element => element.Attribute("MayRequire") != null));

            XDocument about = XDocument.Load(Path.Combine(root, "About", "About.xml"));
            AssertTrue("About adds no Anomaly dependency",
                !about.Descendants("packageId").Any(element =>
                    string.Equals(element.Value.Trim(), "Ludeon.RimWorld.Anomaly",
                        StringComparison.OrdinalIgnoreCase)));

            AssertEqual("English DefInjected label", "Anomaly narrative policy",
                LocalizedLabel(root, "English"));
            AssertEqual("Russian DefInjected label", "Политика повествования Anomaly",
                LocalizedLabel(root, "Russian (Русский)"));
        }

        private static void TestInvalidAndNonProgressingStudy()
        {
            AnomalyStudyPlan plan = AnomalyStudyPolicy.Plan(null, null, null);
            AssertEqual("null study drops", AnomalyStudyDisposition.DropInvalid, plan.disposition);

            AnomalyStudyFacts facts = Study();
            facts.studiedDefName = " ";
            plan = AnomalyStudyPolicy.Plan(facts, null, null);
            AssertEqual("blank studied def drops", AnomalyStudyDisposition.DropInvalid, plan.disposition);

            facts = Study();
            facts.oldProgress = -1;
            plan = AnomalyStudyPolicy.Plan(facts, null, null);
            AssertEqual("negative progress drops", AnomalyStudyDisposition.DropInvalid, plan.disposition);

            facts = Study();
            facts.noteCount = -1;
            plan = AnomalyStudyPolicy.Plan(facts, null, null);
            AssertEqual("negative note count drops", AnomalyStudyDisposition.DropInvalid,
                plan.disposition);

            facts = Study();
            facts.newProgress = facts.oldProgress;
            plan = AnomalyStudyPolicy.Plan(facts, null, null);
            AssertEqual("no progress is state-only", AnomalyStudyDisposition.StateOnly, plan.disposition);
            AssertTrue("no progress has no first mutation", !plan.historyMutation.observeFirstBreakthrough);

            facts.newProgress = facts.oldProgress - 1;
            plan = AnomalyStudyPolicy.Plan(facts, null, null);
            AssertEqual("backward progress is state-only", AnomalyStudyDisposition.StateOnly, plan.disposition);

            facts = Study();
            facts.noteCount = 0;
            plan = AnomalyStudyPolicy.Plan(facts, null, null);
            AssertEqual("ordinary point increase is state-only", AnomalyStudyDisposition.StateOnly,
                plan.disposition);
        }

        private static void TestFirstBreakthroughAndEligibility()
        {
            AnomalyStudyFacts facts = Study();
            AnomalyStudyPlan plan = AnomalyStudyPolicy.Plan(facts, null, null);
            AssertEqual("first breakthrough generates", AnomalyStudyDisposition.Generate, plan.disposition);
            AssertEqual("first stage selected", AnomalyStudyStageTokens.FirstBreakthrough, plan.stageToken);
            AssertTrue("first history advances", plan.historyMutation.observeFirstBreakthrough);

            AnomalyStudyHistorySnapshot observed = new AnomalyStudyHistorySnapshot();
            observed.firstBreakthroughObserved = true;
            plan = AnomalyStudyPolicy.Plan(facts, observed, null);
            AssertEqual("already-observed first stays state-only", AnomalyStudyDisposition.StateOnly,
                plan.disposition);

            AnomalyPolicySnapshot disabled = AnomalyPolicySnapshot.CreateDefault();
            disabled.studyEnabled = false;
            plan = AnomalyStudyPolicy.Plan(facts, null, disabled);
            AssertEqual("disabled output is state-only", AnomalyStudyDisposition.StateOnly, plan.disposition);
            AssertTrue("disabled output still advances history", plan.historyMutation.observeFirstBreakthrough);

            facts.studierEligible = false;
            plan = AnomalyStudyPolicy.Plan(facts, null, null);
            AssertEqual("ineligible studier is state-only", AnomalyStudyDisposition.StateOnly, plan.disposition);
            AssertTrue("ineligible studier still advances history", plan.historyMutation.observeFirstBreakthrough);

            facts.studierEligible = true;
            facts.studierPawnId = " ";
            plan = AnomalyStudyPolicy.Plan(facts, null, null);
            AssertEqual("missing exact author is state-only", AnomalyStudyDisposition.StateOnly,
                plan.disposition);

            AnomalyPolicySnapshot noFirst = AnomalyPolicySnapshot.CreateDefault();
            noFirst.recordFirstStudyBreakthrough = false;
            plan = AnomalyStudyPolicy.Plan(Study(), null, noFirst);
            AssertEqual("first toggle suppresses page", AnomalyStudyDisposition.StateOnly, plan.disposition);
            AssertTrue("first toggle does not suppress history", plan.historyMutation.observeFirstBreakthrough);
        }

        private static void TestCompletionHistory()
        {
            AnomalyStudyFacts facts = Study();
            facts.noteCount = 0;
            facts.completedAfter = true;
            AnomalyStudyHistorySnapshot history = new AnomalyStudyHistorySnapshot();
            history.firstBreakthroughObserved = true;
            AnomalyStudyPlan plan = AnomalyStudyPolicy.Plan(facts, history, null);
            AssertEqual("first kind completion generates", AnomalyStudyDisposition.Generate, plan.disposition);
            AssertEqual("completion stage", AnomalyStudyStageTokens.CompletedKind, plan.stageToken);
            AssertEqual("completion mutation", "Entity_Fleshbeast",
                plan.historyMutation.completedStudyDefName);

            history.completedStudyDefNames.Add("Entity_Fleshbeast");
            plan = AnomalyStudyPolicy.Plan(facts, history, null);
            AssertEqual("second instance of same def is state-only", AnomalyStudyDisposition.StateOnly,
                plan.disposition);
            AssertEqual("repeat completion has no mutation", string.Empty,
                plan.historyMutation.completedStudyDefName);

            AnomalyPolicySnapshot noCompletion = AnomalyPolicySnapshot.CreateDefault();
            noCompletion.recordCompletedEntityKind = false;
            history.completedStudyDefNames.Clear();
            plan = AnomalyStudyPolicy.Plan(facts, history, noCompletion);
            AssertEqual("completion toggle suppresses page", AnomalyStudyDisposition.StateOnly,
                plan.disposition);
            AssertEqual("completion toggle still observes kind", "Entity_Fleshbeast",
                plan.historyMutation.completedStudyDefName);

            facts.completedBefore = true;
            plan = AnomalyStudyPolicy.Plan(facts, history, null);
            AssertEqual("already-completed facts do not replay", AnomalyStudyDisposition.StateOnly,
                plan.disposition);
        }

        private static void TestPromotionsAndMultiNoteJump()
        {
            AnomalyPolicySnapshot policy = AnomalyPolicySnapshot.CreateDefault();
            policy.promotedStudyMilestones.Add(Promotion("Entity_Fleshbeast", 80, "deep_notes"));
            policy.promotedStudyMilestones.Add(Promotion("Entity_Fleshbeast", 40, "early_notes"));
            policy.promotedStudyMilestones.Add(Promotion("Entity_Fleshbeast", 40, "early_notes"));
            policy.promotedStudyMilestones.Add(Promotion("bad|def", 10, "bad"));
            policy.promotedStudyMilestones.Add(Promotion("Entity_Fleshbeast", 10, " "));

            AnomalyStudyFacts facts = Study();
            facts.oldProgress = 10;
            facts.newProgress = 100;
            facts.noteCount = 3;
            AnomalyStudyHistorySnapshot history = new AnomalyStudyHistorySnapshot();
            history.firstBreakthroughObserved = true;
            AnomalyStudyPlan plan = AnomalyStudyPolicy.Plan(facts, history, policy);
            AssertEqual("multi-note jump emits once", AnomalyStudyDisposition.Generate, plan.disposition);
            AssertEqual("promotion stage", AnomalyStudyStageTokens.Promoted, plan.stageToken);
            AssertEqual("earliest crossed promotion", "early_notes", plan.promotionToken);
            AssertEqual("stable promotion key", "Entity_Fleshbeast|40|early_notes",
                plan.historyMutation.observedPromotionKeys[0]);
            AssertEqual("all crossed promotions observed", 2,
                plan.historyMutation.observedPromotionKeys.Count);

            history.observedPromotionKeys.AddRange(plan.historyMutation.observedPromotionKeys);
            plan = AnomalyStudyPolicy.Plan(facts, history, policy);
            AssertEqual("multi-threshold retry does not replay", AnomalyStudyDisposition.StateOnly,
                plan.disposition);

            AssertEqual("malformed promotion has no key", string.Empty,
                AnomalyStudyPolicy.PromotionKey(Promotion("bad|def", 1, "x")));
            AssertEqual("zero threshold has no key", string.Empty,
                AnomalyStudyPolicy.PromotionKey(Promotion("Entity_Fleshbeast", 0, "x")));
        }

        private static void TestMonolithStateOnlyEnrichment()
        {
            AnomalyStudyFacts facts = Study();
            facts.studiedDefName = "VoidMonolith";
            facts.isMonolith = true;
            facts.monolithActivatableAfter = true;
            AnomalyStudyPlan plan = AnomalyStudyPolicy.Plan(facts, null, null);
            AssertEqual("monolith note is state-only", AnomalyStudyDisposition.StateOnly,
                plan.disposition);
            AssertTrue("monolith still advances first history", plan.historyMutation.observeFirstBreakthrough);
            AssertTrue("activation-ready transition preserved", plan.monolithBecameActivatable);

            facts.monolithActivatableBefore = true;
            plan = AnomalyStudyPolicy.Plan(facts, null, null);
            AssertTrue("already-activatable does not claim transition", !plan.monolithBecameActivatable);
        }

        private static void TestTaleOwnershipExactness()
        {
            AnomalyStudyTaleClaim claim = Claim();
            AnomalyStudiedTaleFacts tale = Tale();
            AnomalyTaleOwnershipDecision decision =
                AnomalyTaleOwnershipPolicy.Decide(claim, tale, 60);
            AssertTrue("exact pair suppresses", decision.suppress);
            AssertTrue("exact pair consumes", decision.removeClaim && decision.nextClaim.consumed);
            AssertTrue("input claim remains immutable", !claim.consumed);

            tale.studierPawnId = "Pawn_B";
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, 60);
            AssertTrue("researcher mismatch fails open", !decision.suppress && !decision.removeClaim);
            tale = Tale();
            tale.studiedEntityId = "Entity_B";
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, 60);
            AssertTrue("entity mismatch fails open", !decision.suppress && !decision.removeClaim);

            // A stable entity ID is stronger than a matching defName; a wrong entity cannot borrow
            // another instance's dedicated-page ownership.
            tale.studiedDefName = claim.studiedDefName;
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, 60);
            AssertTrue("matching def cannot override wrong stable id", !decision.suppress);

            claim.studiedEntityId = string.Empty;
            tale.studiedEntityId = string.Empty;
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, 60);
            AssertTrue("defName fallback matches when both lack entity id", decision.suppress);
        }

        private static void TestTaleOwnershipExpiryAndConsumeOnce()
        {
            AnomalyStudyTaleClaim claim = Claim();
            AnomalyStudiedTaleFacts tale = Tale();
            tale.tick = 160;
            AnomalyTaleOwnershipDecision decision =
                AnomalyTaleOwnershipPolicy.Decide(claim, tale, 60);
            AssertTrue("expiry boundary matches", decision.suppress);

            decision = AnomalyTaleOwnershipPolicy.Decide(decision.nextClaim, tale, 60);
            AssertTrue("consumed claim cannot suppress twice", !decision.suppress && decision.removeClaim);

            tale.tick = 161;
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, 60);
            AssertTrue("expired claim releases fallback", !decision.suppress && decision.removeClaim);
            tale.tick = 100;
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, 0);
            AssertTrue("zero window permits exact same tick", decision.suppress);
            tale.tick = 101;
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, 0);
            AssertTrue("zero window expires next tick", !decision.suppress && decision.removeClaim);
            tale.tick = 99;
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, 60);
            AssertTrue("Tale before claim fails open without consuming", !decision.suppress
                && !decision.removeClaim);

            claim.studierPawnId = " ";
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, Tale(), 60);
            AssertTrue("malformed claim removed fail-open", !decision.suppress && decision.removeClaim);
            decision = AnomalyTaleOwnershipPolicy.Decide(Claim(), null, 60);
            AssertTrue("missing Tale leaves valid claim", !decision.suppress && !decision.removeClaim);
        }

        private static void TestInvalidContainmentScopes()
        {
            AssertTrue("null breach invalid", !ContainmentBreachPolicy.Plan(null, null).valid);
            ContainmentEscapeFacts facts = Breach();
            facts.outerEscape = false;
            AssertTrue("nested scope cannot emit", !ContainmentBreachPolicy.Plan(facts, null).valid);
            facts = Breach();
            facts.escapeId = " ";
            AssertTrue("blank outer identity invalid", !ContainmentBreachPolicy.Plan(facts, null).valid);
            facts = Breach();
            facts.entities[0].escaped = false;
            AssertTrue("unverified escape invalid", !ContainmentBreachPolicy.Plan(facts, null).valid);
            facts = Breach();
            facts.entities[0].entityId = " ";
            AssertTrue("entity without stable id invalid", !ContainmentBreachPolicy.Plan(facts, null).valid);
            facts = Breach();
            facts.escapeId = "bad|id";
            AssertTrue("separator injection invalid", !ContainmentBreachPolicy.Plan(facts, null).valid);
        }

        private static void TestContainmentAggregationAndBounds()
        {
            ContainmentEscapeFacts facts = Breach();
            facts.entities.Add(Entity("Entity_B", true));
            facts.entities.Add(Entity("Entity_A", true));
            facts.entities.Add(Entity("Entity_C", true));
            facts.entities.Add(Entity("Entity_D", false));
            AnomalyPolicySnapshot policy = AnomalyPolicySnapshot.CreateDefault();
            policy.containmentMaxEntityLabelsInContext = 2;
            ContainmentBreachPlan plan = ContainmentBreachPolicy.Plan(facts, policy);
            AssertTrue("verified breach valid", plan.valid);
            AssertTrue("verified breach writes", plan.writePage);
            AssertEqual("duplicate and nonescape removed", 3, plan.escapedCount);
            AssertEqual("context label cap", 2, plan.contextEntities.Count);
            AssertEqual("additional escaped count", 1, plan.additionalEscapedCount);
            AssertEqual("outer entity remains first", "Entity_A", plan.contextEntities[0].entityId);
            AssertTrue("context entity copied", !object.ReferenceEquals(facts.entities[0],
                plan.contextEntities[0]));

            policy.containmentEnabled = false;
            plan = ContainmentBreachPolicy.Plan(facts, policy);
            AssertTrue("disabled breach retains verified facts", plan.valid && plan.escapedCount == 3);
            AssertTrue("disabled breach emits no page", !plan.writePage && plan.selectedWriters.Count == 0);
        }

        private static void TestContainmentWriterRanking()
        {
            ContainmentEscapeFacts facts = Breach();
            facts.witnesses.Clear();
            facts.witnesses.Add(Writer("Colony_A", false, false, true, 4, "a"));
            facts.witnesses.Add(Writer("Recent", false, true, true, 400, "z"));
            facts.witnesses.Add(Writer("Nearby_B", true, false, true, 25, "b"));
            facts.witnesses.Add(Writer("Nearby_A", true, false, true, 25, "a"));
            facts.witnesses.Add(Writer("OffMap", true, true, true, 1, "0", onMap: false));
            facts.witnesses.Add(Writer("Ineligible", true, true, true, 1, "0", eligible: false));
            facts.witnesses.Add(Writer("Nearby_A", false, true, true, 1, "0"));

            ContainmentBreachPlan plan = ContainmentBreachPolicy.Plan(facts, null);
            AssertEqual("default two-writer cap", 2, plan.selectedWriters.Count);
            AssertEqual("nearby tie stable", "Nearby_A", plan.selectedWriters[0].pawnId);
            AssertEqual("nearby role truthful", AnomalyWitnessRoleTokens.Nearby,
                plan.selectedWriters[0].roleToken);
            AssertEqual("second nearby chosen before recent", "Nearby_B",
                plan.selectedWriters[1].pawnId);

            AnomalyPolicySnapshot one = AnomalyPolicySnapshot.CreateDefault();
            one.containmentMaxWriters = 1;
            plan = ContainmentBreachPolicy.Plan(facts, one);
            AssertEqual("one-writer cap", 1, plan.selectedWriters.Count);
            AssertEqual("one writer remains best nearby", "Nearby_A", plan.selectedWriters[0].pawnId);

            facts.witnesses.RemoveAll(row => row.pawnId.StartsWith("Nearby", StringComparison.Ordinal));
            plan = ContainmentBreachPolicy.Plan(facts, null);
            AssertEqual("recent studier outranks colony fallback", "Recent",
                plan.selectedWriters[0].pawnId);
            AssertEqual("recent role truthful", AnomalyWitnessRoleTokens.RecentStudier,
                plan.selectedWriters[0].roleToken);
        }

        private static void TestContainmentRadiusAndCaps()
        {
            ContainmentEscapeFacts facts = Breach();
            facts.witnesses.Clear();
            facts.witnesses.Add(Writer("Boundary", true, false, false, 144, "a"));
            ContainmentBreachPlan plan = ContainmentBreachPolicy.Plan(facts, null);
            AssertTrue("radius boundary included", plan.writePage);

            facts.witnesses[0].distanceSquared = 145;
            plan = ContainmentBreachPolicy.Plan(facts, null);
            AssertTrue("outside radius excluded without guessed fallback", !plan.writePage);

            AssertEqual("zero writer cap falls back", 2,
                ContainmentBreachPolicy.NormalizeWriterMaximum(0));
            AssertEqual("one writer preserved", 1, ContainmentBreachPolicy.NormalizeWriterMaximum(1));
            AssertEqual("two writers preserved", 2, ContainmentBreachPolicy.NormalizeWriterMaximum(2));
            AssertEqual("large cap falls back", 2,
                ContainmentBreachPolicy.NormalizeWriterMaximum(99));
            AssertEqual("negative cap falls back", 2,
                ContainmentBreachPolicy.NormalizeWriterMaximum(-1));

            facts = Breach();
            facts.witnesses[0].eligible = false;
            plan = ContainmentBreachPolicy.Plan(facts, null);
            AssertTrue("no eligible writer means no page", plan.valid && !plan.writePage);
        }

        private static void TestContainmentDedupAndDeterminism()
        {
            ContainmentEscapeFacts facts = Breach();
            facts.witnesses.Add(Writer("Pawn_B", true, false, true, 4, "Pawn_B"));
            ContainmentBreachPlan first = ContainmentBreachPolicy.Plan(facts, null);
            ContainmentBreachPlan second = ContainmentBreachPolicy.Plan(facts, null);
            AssertEqual("dedup key", "anomaly-breach|7|escape-a", first.dedupKey);
            AssertEqual("same input same key", first.dedupKey, second.dedupKey);
            AssertEqual("same input same author", first.selectedWriters[0].pawnId,
                second.selectedWriters[0].pawnId);
            AssertEqual("same input same role", first.selectedWriters[0].roleToken,
                second.selectedWriters[0].roleToken);
            facts.witnesses.Reverse();
            ContainmentBreachPlan reordered = ContainmentBreachPolicy.Plan(facts, null);
            AssertEqual("candidate input order does not change first author",
                first.selectedWriters[0].pawnId, reordered.selectedWriters[0].pawnId);
            AssertEqual("candidate input order does not change second author",
                first.selectedWriters[1].pawnId, reordered.selectedWriters[1].pawnId);

            ContainmentEscapeFacts otherMap = Breach();
            otherMap.mapId = 8;
            AssertTrue("different map changes dedup",
                ContainmentBreachPolicy.DedupKey(facts) != ContainmentBreachPolicy.DedupKey(otherMap));
            ContainmentEscapeFacts otherOuter = Breach();
            otherOuter.escapeId = "escape-b";
            AssertTrue("different outer escape changes dedup",
                ContainmentBreachPolicy.DedupKey(facts) != ContainmentBreachPolicy.DedupKey(otherOuter));
        }

        private static AnomalyStudyFacts Study()
        {
            return new AnomalyStudyFacts
            {
                studiedEntityId = "Entity_1",
                studiedDefName = "Entity_Fleshbeast",
                studiedLabel = "fleshbeast",
                studierPawnId = "Pawn_A",
                studierEligible = true,
                oldProgress = 10,
                newProgress = 20,
                noteCount = 1
            };
        }

        private static AnomalyStudyMilestoneRule Promotion(string defName, int progress, string token)
        {
            return new AnomalyStudyMilestoneRule
            {
                studiedDefName = defName,
                minimumProgress = progress,
                token = token
            };
        }

        private static AnomalyStudyTaleClaim Claim()
        {
            return new AnomalyStudyTaleClaim
            {
                studierPawnId = "Pawn_A",
                studiedEntityId = "Entity_1",
                studiedDefName = "Entity_Fleshbeast",
                acceptedTick = 100
            };
        }

        private static AnomalyStudiedTaleFacts Tale()
        {
            return new AnomalyStudiedTaleFacts
            {
                studierPawnId = "Pawn_A",
                studiedEntityId = "Entity_1",
                studiedDefName = "Entity_Fleshbeast",
                tick = 101
            };
        }

        private static ContainmentEscapeFacts Breach()
        {
            ContainmentEscapeFacts facts = new ContainmentEscapeFacts
            {
                escapeId = "escape-a",
                tick = 100,
                mapId = 7,
                outerEscape = true
            };
            facts.entities.Add(Entity("Entity_A", true));
            facts.witnesses.Add(Writer("Pawn_A", true, false, true, 4, "Pawn_A"));
            return facts;
        }

        private static ContainedEntityFact Entity(string id, bool escaped)
        {
            return new ContainedEntityFact
            {
                entityId = id,
                visibleLabel = id + " label",
                defName = "EntityDef",
                platformX = 10,
                platformZ = 20,
                escaped = escaped
            };
        }

        private static AnomalyWriterCandidate Writer(
            string id,
            bool nearby,
            bool recent,
            bool colony,
            int distanceSquared,
            string tieBreak,
            bool onMap = true,
            bool eligible = true)
        {
            return new AnomalyWriterCandidate
            {
                pawnId = id,
                eligible = eligible,
                onAffectedMap = onMap,
                nearbyCandidate = nearby,
                recentExactStudier = recent,
                freeColonistOnHomeMap = colony,
                distanceSquared = distanceSquared,
                distanceBucket = distanceSquared / 25,
                tieBreakKey = tieBreak
            };
        }

        private static string LocalizedLabel(string root, string language)
        {
            XDocument document = XDocument.Load(Path.Combine(root, "Languages", language,
                "DefInjected", "PawnDiary.DiaryAnomalyPolicyDef", "DiaryAnomalyPolicyDefs.xml"));
            return document.Root.Element("Diary_AnomalyPolicy.label").Value.Trim();
        }

        private static string Value(XElement parent, string name)
        {
            XElement element = parent == null ? null : parent.Element(name);
            return element == null ? string.Empty : element.Value.Trim();
        }

        private static string RepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName,
                    "ANOMALY_SUPPORT_IMPLEMENTATION_PLAN.md")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root.");
        }

        private static void AssertTrue(string name, bool value)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("Assertion failed (" + name + ").");
        }

        private static void AssertEqual<T>(string name, T expected, T actual)
        {
            assertions++;
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    "Assertion failed (" + name + "): expected '" + expected + "', got '" + actual + "'.");
            }
        }
    }
}

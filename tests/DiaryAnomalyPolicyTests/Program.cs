// Standalone no-RimWorld tests for Master Wave 7 / Anomaly Phases A1.0-A2.1. Linking only plain DTOs
// and pure policies makes an accidental Verse, Unity, Harmony, DLC, or live-settings dependency a
// compile-time failure.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PawnDiary;
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
            TestPolicyNormalization();
            TestShippedPolicyAndLocalization();
            TestShippedCatalogGroupsAndFallbackLocalization();
            TestInvalidAndNonProgressingStudy();
            TestFirstBreakthroughAndEligibility();
            TestCompletionHistory();
            TestPromotionsAndMultiNoteJump();
            TestMonolithStateOnlyEnrichment();
            TestStudyContextProjection();
            TestMonolithKnowledgeOwnership();
            TestTaleOwnershipExactness();
            TestTaleOwnershipExpiryAndConsumeOnce();
            TestBoundedStudySuppressionCache();
            TestRecentStudyCorrelationCache();
            TestInvalidContainmentScopes();
            TestContainmentAggregationAndBounds();
            TestContainmentWriterRanking();
            TestContainmentRadiusAndCaps();
            TestContainmentDedupAndDeterminism();
            TestContainmentContextFormatter();
            TestCreepJoinerArrivalContinuity();
            TestCreepJoinerVisibleOutcomes();
            TestCreepJoinerWriterSelectionAndBounds();
            TestCreepJoinerContextFirewall();
            TestCreepJoinerSurgicalDisclosurePolicy();
            TestCreepJoinerSurgicalWriterAndContextFirewall();
            TestCreepJoinerSurgeryTaleOwnership();
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
            AssertEqual("promotion key", "study_promotion", AnomalyContextKeys.StudyPromotion);
            AssertEqual("studied def key", "studied_def", AnomalyContextKeys.StudiedDef);
            AssertEqual("studied label key", "studied_label", AnomalyContextKeys.StudiedLabel);
            AssertEqual("codex key", "codex_entry", AnomalyContextKeys.CodexEntry);
            AssertEqual("codex label key", "codex_entry_label", AnomalyContextKeys.CodexEntryLabel);
            AssertEqual("category key", "knowledge_category", AnomalyContextKeys.KnowledgeCategory);
            AssertEqual("category label key", "knowledge_category_label",
                AnomalyContextKeys.KnowledgeCategoryLabel);
            AssertEqual("contained key", "contained_entity", AnomalyContextKeys.ContainedEntity);
            AssertEqual("monolith key", "monolith", AnomalyContextKeys.Monolith);
            AssertEqual("setting key", "setting", AnomalyContextKeys.Setting);
            AssertEqual("previous monolith key", "monolith_previous_level",
                AnomalyContextKeys.MonolithPreviousLevel);
            AssertEqual("reached monolith key", "monolith_reached_level",
                AnomalyContextKeys.MonolithReachedLevel);
            AssertEqual("activatable monolith key", "monolith_became_activatable",
                AnomalyContextKeys.MonolithBecameActivatable);
            AssertEqual("researcher monolith key", "monolith_last_researcher",
                AnomalyContextKeys.MonolithLastResearcher);
            AssertEqual("study-stage monolith key", "monolith_study_stage",
                AnomalyContextKeys.MonolithStudyStage);
            AssertEqual("escape count key", "escaped_count", AnomalyContextKeys.EscapedCount);
            AssertEqual("escaped entities key", "escaped_entities", AnomalyContextKeys.EscapedEntities);
            AssertEqual("additional escape key", "additional_escaped_count",
                AnomalyContextKeys.AdditionalEscapedCount);
            AssertEqual("witness key", "witness_role", AnomalyContextKeys.WitnessRole);
            AssertEqual("initiator witness key", "initiator_witness_role",
                AnomalyContextKeys.InitiatorWitnessRole);
            AssertEqual("surgical reveal phase", "surgical_reveal",
                AnomalyOutcomeTokens.SurgicalReveal);
            AssertEqual("surgical disclosed result", "disclosed",
                CreepJoinerVisibleResultTokens.Disclosed);
            AssertEqual("surgeon role", "surgeon", AnomalyWitnessRoleTokens.Surgeon);
            AssertEqual("surgeon id key", "creepjoiner_surgeon_id",
                AnomalyContextKeys.CreepJoinerSurgeonId);
            AssertEqual("surgeon label key", "creepjoiner_surgeon_label",
                AnomalyContextKeys.CreepJoinerSurgeonLabel);
            AssertEqual("recipient witness key", "recipient_witness_role",
                AnomalyContextKeys.RecipientWitnessRole);
            AssertEqual("cascade key", "same_room_cascade", AnomalyContextKeys.SameRoomCascade);
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
            AssertEqual("creepjoiner joined phase", "joined", CreepJoinerPhaseTokens.Joined);
            AssertEqual("creepjoiner rejected result", "rejected",
                CreepJoinerVisibleResultTokens.Rejected);
            AssertEqual("creepjoiner hostile result", "hostile",
                CreepJoinerVisibleResultTokens.Hostile);
            AssertEqual("creepjoiner leaving result", "leaving",
                CreepJoinerVisibleResultTokens.Leaving);
            AssertEqual("creepjoiner speaker role", "speaker", AnomalyWitnessRoleTokens.Speaker);
            AssertEqual("creepjoiner subject role", "subject", AnomalyWitnessRoleTokens.Subject);
            AssertEqual("creepjoiner subject id key", "creepjoiner_subject_id",
                AnomalyContextKeys.CreepJoinerSubjectId);
            AssertEqual("creepjoiner subject label key", "creepjoiner_subject_label",
                AnomalyContextKeys.CreepJoinerSubjectLabel);
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
            AssertEqual("creepjoiner witness radius", 12, policy.creepJoinerWitnessRadius);
            AssertTrue("future ghoul enabled", policy.ghoulTransformationEnabled);
            AssertTrue("future void enabled", policy.voidOutcomeEnabled);
            AssertEqual("ownership depth", 8, policy.taleOwnershipMaxDepth);
            AssertEqual("ownership expiry", 2500, policy.taleOwnershipExpiryTicks);
        }

        private static void TestPolicyNormalization()
        {
            AnomalyPolicySnapshot raw = new AnomalyPolicySnapshot
            {
                studyEnabled = false,
                recordFirstStudyBreakthrough = false,
                recordCompletedEntityKind = false,
                monolithKnowledgeMaxAgeTicks = -1,
                studyTaleSuppressionTicks = -1,
                containmentEnabled = false,
                containmentWitnessRadius = 0,
                containmentMaxWriters = 99,
                containmentMaxEntityLabelsInContext = 99,
                containmentDedupTicks = -1,
                recentStudierMaxAgeTicks = -1,
                creepJoinerEnabled = false,
                creepJoinerOutcomeDedupTicks = -1,
                creepJoinerArcRetentionTicks = -1,
                creepJoinerMaxWitnesses = 99,
                creepJoinerWitnessRadius = 0,
                ghoulTransformationEnabled = false,
                voidOutcomeEnabled = false,
                taleOwnershipMaxDepth = 0,
                taleOwnershipExpiryTicks = -1
            };
            raw.promotedStudyMilestones.Add(Promotion(" Entity_Fleshbeast ", 40, " early_notes "));
            raw.promotedStudyMilestones.Add(Promotion("Entity_Fleshbeast", 40, "early_notes"));
            raw.promotedStudyMilestones.Add(null);
            raw.promotedStudyMilestones.Add(Promotion("bad|def", 10, "bad"));
            raw.promotedStudyMilestones.Add(Promotion("Entity_Fleshbeast", 0, "zero"));

            AnomalyPolicySnapshot normalized = AnomalyPolicyNormalization.Normalize(raw);
            AssertTrue("normalization returns detached snapshot", !object.ReferenceEquals(raw, normalized));
            AssertTrue("normalization preserves disabled study", !normalized.studyEnabled);
            AssertTrue("normalization preserves disabled first", !normalized.recordFirstStudyBreakthrough);
            AssertTrue("normalization preserves disabled completion", !normalized.recordCompletedEntityKind);
            AssertEqual("negative monolith age falls back", 60000,
                normalized.monolithKnowledgeMaxAgeTicks);
            AssertEqual("negative study Tale window falls back",
                AnomalyPolicyLimits.DefaultStudyTaleSuppressionTicks,
                normalized.studyTaleSuppressionTicks);
            AssertTrue("normalization preserves disabled containment", !normalized.containmentEnabled);
            AssertEqual("invalid radius falls back", AnomalyPolicyLimits.DefaultWitnessRadius,
                normalized.containmentWitnessRadius);
            AssertEqual("invalid writer cap falls back", AnomalyPolicyLimits.DefaultContainmentWriters,
                normalized.containmentMaxWriters);
            AssertEqual("invalid label cap falls back", AnomalyPolicyLimits.DefaultEntityLabels,
                normalized.containmentMaxEntityLabelsInContext);
            AssertEqual("negative breach dedup falls back", 2500, normalized.containmentDedupTicks);
            AssertEqual("negative recent studier age falls back", 60000,
                normalized.recentStudierMaxAgeTicks);
            AssertTrue("normalization preserves disabled creepjoiner", !normalized.creepJoinerEnabled);
            AssertEqual("negative creepjoiner dedup falls back", 2500,
                normalized.creepJoinerOutcomeDedupTicks);
            AssertEqual("negative creepjoiner retention falls back", 3600000,
                normalized.creepJoinerArcRetentionTicks);
            AssertEqual("invalid creepjoiner witness cap falls back",
                AnomalyPolicyLimits.MaximumCreepJoinerWitnesses,
                normalized.creepJoinerMaxWitnesses);
            AssertEqual("invalid creepjoiner radius falls back",
                AnomalyPolicyLimits.DefaultCreepJoinerWitnessRadius,
                normalized.creepJoinerWitnessRadius);
            AssertTrue("normalization preserves disabled ghoul", !normalized.ghoulTransformationEnabled);
            AssertTrue("normalization preserves disabled void", !normalized.voidOutcomeEnabled);
            AssertEqual("invalid ownership depth falls back",
                AnomalyPolicyLimits.DefaultTaleOwnershipDepth,
                normalized.taleOwnershipMaxDepth);
            AssertEqual("negative ownership expiry falls back",
                AnomalyPolicyLimits.DefaultTaleOwnershipExpiryTicks,
                normalized.taleOwnershipExpiryTicks);
            AssertEqual("normalization filters and deduplicates promotions", 1,
                normalized.promotedStudyMilestones.Count);
            AssertEqual("normalization trims promotion defName", "Entity_Fleshbeast",
                normalized.promotedStudyMilestones[0].studiedDefName);
            AssertEqual("normalization trims promotion token", "early_notes",
                normalized.promotedStudyMilestones[0].token);
            AssertTrue("normalization clones promotion row", !object.ReferenceEquals(
                raw.promotedStudyMilestones[0], normalized.promotedStudyMilestones[0]));

            AnomalyPolicySnapshot oversized = new AnomalyPolicySnapshot();
            for (int i = 0; i < AnomalyPolicyLimits.MaximumStudyMilestones + 1; i++)
                oversized.promotedStudyMilestones.Add(Promotion("Entity_" + i, i + 1, "stage_" + i));
            AssertEqual("promotion normalization enforces defensive row cap",
                AnomalyPolicyLimits.MaximumStudyMilestones,
                AnomalyPolicyNormalization.Normalize(oversized).promotedStudyMilestones.Count);
            AssertTrue("null policy normalizes to defaults",
                AnomalyPolicyNormalization.Normalize(null).studyEnabled);
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
            AssertEqual("XML creepjoiner radius", "12", Value(def, "creepJoinerWitnessRadius"));
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
            facts.noteThresholdsCrossed = -1;
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
            facts.noteThresholdsCrossed = 0;
            plan = AnomalyStudyPolicy.Plan(facts, null, null);
            AssertEqual("ordinary point increase is state-only", AnomalyStudyDisposition.StateOnly,
                plan.disposition);
        }

        private static void TestShippedCatalogGroupsAndFallbackLocalization()
        {
            string root = RepositoryRoot();
            XDocument groups = XDocument.Load(Path.Combine(
                root, "1.6", "Defs", "DiaryInteractionGroupDefs.xml"));
            string[] names =
            {
                "anomalyStudyBreakthrough",
                "anomalyContainmentBreach",
                "anomalyCreepJoinerOutcome",
                "anomalyGhoulTransformation",
                "anomalyVoidOutcome"
            };
            string[] defNames =
            {
                AnomalyEventDefNames.StudyBreakthrough,
                AnomalyEventDefNames.ContainmentBreach,
                AnomalyEventDefNames.CreepJoinerOutcome,
                AnomalyEventDefNames.GhoulTransformation,
                AnomalyEventDefNames.VoidOutcome
            };
            for (int i = 0; i < names.Length; i++)
            {
                XElement group = groups.Descendants("PawnDiary.DiaryInteractionGroupDef")
                    .Single(row => Value(row, "defName") == names[i]);
                AssertEqual("Anomaly group reuses Interaction domain: " + names[i],
                    "Interaction", Value(group, "domain"));
                AssertEqual("Anomaly group order is frozen: " + names[i],
                    (61 + i).ToString(), Value(group, "order"));
                AssertEqual("Anomaly group exact synthetic matcher: " + names[i], defNames[i],
                    group.Element("matchDefNames").Elements("li").Single().Value.Trim());
                AssertEqual("Anomaly group package gate: " + names[i],
                    "Ludeon.RimWorld.Anomaly",
                    group.Element("enableWhenPackageIdsLoaded").Elements("li").Single().Value.Trim());
            }
            XElement broad = groups.Descendants("PawnDiary.DiaryInteractionGroupDef")
                .Single(row => Value(row, "defName") == "anomaly");
            AssertTrue("exact Anomaly groups precede broad interaction matcher",
                names.Select(name => groups.Descendants("PawnDiary.DiaryInteractionGroupDef")
                    .Single(row => Value(row, "defName") == name))
                    .All(row => int.Parse(Value(row, "order")) < int.Parse(Value(broad, "order"))));

            foreach (string language in new[] { "English", "Russian (Русский)" })
            {
                XDocument injected = XDocument.Load(Path.Combine(root, "Languages", language,
                    "DefInjected", "PawnDiary.DiaryInteractionGroupDef",
                    "DiaryInteractionGroupDefs.xml"));
                for (int i = 0; i < names.Length; i++)
                {
                    foreach (string suffix in new[]
                        { ".label", ".instruction", ".tone", ".tones.0", ".tones.1" })
                    {
                        AssertTrue(language + " Anomaly DefInjected value exists: " + names[i] + suffix,
                            !string.IsNullOrWhiteSpace(
                                injected.Root.Element(names[i] + suffix)?.Value));
                    }
                }

                XDocument keyed = XDocument.Load(Path.Combine(
                    root, "Languages", language, "Keyed", "PawnDiary.xml"));
                foreach (string key in new[]
                {
                    "PawnDiary.Event.Anomaly.Study.Label",
                    "PawnDiary.Event.Anomaly.Study.Fallback",
                    "PawnDiary.Event.Anomaly.Containment.Label",
                    "PawnDiary.Event.Anomaly.Containment.Fallback",
                    "PawnDiary.Event.Anomaly.CreepJoiner.Label",
                    "PawnDiary.Event.Anomaly.CreepJoiner.Fallback",
                    "PawnDiary.Event.Anomaly.CreepJoiner.SubjectDeparted",
                    "PawnDiary.Event.Anomaly.CreepJoiner.Result.Rejected",
                    "PawnDiary.Event.Anomaly.CreepJoiner.Result.Aggressive",
                    "PawnDiary.Event.Anomaly.CreepJoiner.Result.Departed",
                    "PawnDiary.Event.Anomaly.CreepJoiner.Surgical.SurgeonFallback",
                    "PawnDiary.Event.Anomaly.CreepJoiner.Surgical.SubjectFallback",
                    "PawnDiary.Event.Anomaly.Ghoul.Label",
                    "PawnDiary.Event.Anomaly.Ghoul.SubjectFallback",
                    "PawnDiary.Event.Anomaly.Ghoul.SurgeonFallback",
                    "PawnDiary.Event.Anomaly.Void.Label",
                    "PawnDiary.Event.Anomaly.Void.EmbracedFallback",
                    "PawnDiary.Event.Anomaly.Void.DisruptedFallback"
                })
                {
                    AssertTrue(language + " Anomaly Keyed fallback exists: " + key,
                        !string.IsNullOrWhiteSpace(keyed.Root.Element(key)?.Value));
                }
            }
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
            facts.noteThresholdsCrossed = 0;
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
            plan = AnomalyStudyPolicy.Plan(facts, history, noCompletion);
            AssertEqual("disabled completion toggle cannot revive an already-completed fact",
                string.Empty, plan.historyMutation.completedStudyDefName);
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
            facts.noteThresholdsCrossed = 3;
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

            AnomalyPolicySnapshot exactPolicy = AnomalyPolicySnapshot.CreateDefault();
            exactPolicy.promotedStudyMilestones.Add(
                Promotion("Entity_Fleshbeast", 40, "exact_threshold"));
            AnomalyStudyHistorySnapshot exactHistory = new AnomalyStudyHistorySnapshot();
            exactHistory.firstBreakthroughObserved = true;
            AnomalyStudyFacts exactFacts = Study();
            exactFacts.noteThresholdsCrossed = 0;
            exactFacts.oldProgress = 39;
            exactFacts.newProgress = 40;
            plan = AnomalyStudyPolicy.Plan(exactFacts, exactHistory, exactPolicy);
            AssertEqual("new progress equal to threshold crosses", AnomalyStudyDisposition.Generate,
                plan.disposition);
            AssertEqual("equal-threshold promotion selected", "exact_threshold", plan.promotionToken);

            exactFacts.oldProgress = 40;
            exactFacts.newProgress = 41;
            plan = AnomalyStudyPolicy.Plan(exactFacts, exactHistory, exactPolicy);
            AssertEqual("old progress equal to threshold is already crossed",
                AnomalyStudyDisposition.StateOnly, plan.disposition);

            exactHistory.observedPromotionKeys.Add(" Entity_Fleshbeast|40|exact_threshold ");
            exactFacts.oldProgress = 39;
            exactFacts.newProgress = 40;
            plan = AnomalyStudyPolicy.Plan(exactFacts, exactHistory, exactPolicy);
            AssertEqual("trimmed saved promotion key prevents replay",
                AnomalyStudyDisposition.StateOnly, plan.disposition);

            AnomalyStudyFacts simultaneous = Study();
            simultaneous.oldProgress = 10;
            simultaneous.newProgress = 100;
            simultaneous.noteThresholdsCrossed = 3;
            simultaneous.completedAfter = true;
            plan = AnomalyStudyPolicy.Plan(simultaneous, null, policy);
            AssertEqual("coincident milestone emits one first stage",
                AnomalyStudyStageTokens.FirstBreakthrough, plan.stageToken);
            AssertEqual("unselected promotion token stays hidden", string.Empty, plan.promotionToken);
            AssertEqual("coincident completion still enters history", "Entity_Fleshbeast",
                plan.historyMutation.completedStudyDefName);
            AssertEqual("coincident promotions still enter history", 2,
                plan.historyMutation.observedPromotionKeys.Count);

            AnomalyStudyHistorySnapshot afterFirst = new AnomalyStudyHistorySnapshot
            {
                firstBreakthroughObserved = true
            };
            plan = AnomalyStudyPolicy.Plan(simultaneous, afterFirst, policy);
            AssertEqual("completion outranks a coincident promotion after first history",
                AnomalyStudyStageTokens.CompletedKind, plan.stageToken);
            AssertEqual("completion precedence hides the unselected promotion token",
                string.Empty, plan.promotionToken);
            AssertEqual("completion precedence still observes every crossed promotion", 2,
                plan.historyMutation.observedPromotionKeys.Count);
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
            AssertEqual("monolith preserves semantic stage for activation",
                AnomalyStudyStageTokens.FirstBreakthrough, plan.stageToken);

            facts.monolithActivatableBefore = true;
            plan = AnomalyStudyPolicy.Plan(facts, null, null);
            AssertTrue("already-activatable does not claim transition", !plan.monolithBecameActivatable);

            facts.monolithActivatableBefore = false;
            facts.newProgress = facts.oldProgress;
            plan = AnomalyStudyPolicy.Plan(facts, null, null);
            AssertEqual("no-progress monolith transition remains state-only",
                AnomalyStudyDisposition.StateOnly, plan.disposition);
            AssertTrue("no-progress activation-ready transition preserved",
                plan.monolithBecameActivatable);
        }

        private static void TestStudyContextProjection()
        {
            AnomalyStudyFacts facts = Study();
            facts.codexEntryDefName = "EntityCodex_Fleshbeast";
            facts.codexEntryLabel = "Fleshbeast entry";
            facts.knowledgeCategoryDefName = "Basic";
            facts.knowledgeCategoryLabel = "Basic knowledge";
            facts.isContainedEntity = true;
            facts.setting = "laboratory; hidden=never";
            AnomalyStudyPlan plan = AnomalyStudyPolicy.Plan(facts, null, null);

            AssertEqual("stable study source key",
                "Entity_Fleshbeast|Entity_1|first_breakthrough||100",
                AnomalyStudyContextFormatter.SourceKey(facts, plan));
            string context = AnomalyStudyContextFormatter.FormatStudy(facts, plan);
            AssertTrue("study context has exact kind",
                context.Contains("anomaly_kind=study_breakthrough"));
            AssertTrue("study context has semantic stage",
                context.Contains("study_stage=first_breakthrough"));
            AssertTrue("study context has exact studied def",
                context.Contains("studied_def=Entity_Fleshbeast"));
            AssertTrue("study context has discovered codex identity",
                context.Contains("codex_entry=EntityCodex_Fleshbeast"));
            AssertTrue("study context has category",
                context.Contains("knowledge_category=Basic"));
            AssertTrue("study context has containment truth",
                context.Contains("contained_entity=true"));
            AssertTrue("context delimiters are sanitized",
                context.Contains("setting=laboratory, hidden-never"));
            AssertTrue("raw progress is absent",
                !context.Contains("oldProgress") && !context.Contains("newProgress")
                && !context.Contains("noteThresholdsCrossed"));

            facts.studiedDefName = "bad|def";
            AssertEqual("unsafe stable source identity fails closed", string.Empty,
                AnomalyStudyContextFormatter.SourceKey(facts, plan));
        }

        private static void TestMonolithKnowledgeOwnership()
        {
            AnomalyMonolithKnowledgeSnapshot snapshot = new AnomalyMonolithKnowledgeSnapshot
            {
                researcherPawnId = "Pawn_A",
                studyStage = AnomalyStudyStageTokens.Promoted,
                tick = 100,
                reachedProgress = 2
            };
            AnomalyMonolithActivationFacts activation = new AnomalyMonolithActivationFacts
            {
                tick = 150,
                previousLevelDefName = "Awakened",
                reachedLevelDefName = "Twisted"
            };
            AnomalyMonolithKnowledgeDecision decision = AnomalyMonolithKnowledgePolicy.Decide(
                snapshot, activation, 100);
            AssertTrue("next exact activation consumes current study", decision.consume);
            AssertTrue("current study attaches", decision.attach);
            AssertEqual("researcher identity survives", "Pawn_A", decision.researcherPawnId);
            AssertEqual("study stage survives", AnomalyStudyStageTokens.Promoted,
                decision.studyStage);
            string context = AnomalyStudyContextFormatter.FormatMonolithActivation(
                decision, "Ari; researcher=exact");
            AssertTrue("monolith context has exact boundary",
                context.Contains("monolith_previous_level=Awakened")
                && context.Contains("monolith_reached_level=Twisted"));
            AssertTrue("monolith researcher label is sanitized",
                context.Contains("monolith_last_researcher=Ari, researcher-exact"));

            decision = AnomalyMonolithKnowledgePolicy.Decide(snapshot, activation, 20);
            AssertTrue("expired study is consumed", decision.consume);
            AssertTrue("expired study does not attach", !decision.attach);

            activation.tick = 90;
            decision = AnomalyMonolithKnowledgePolicy.Decide(snapshot, activation, 100);
            AssertTrue("future snapshot is not consumed", !decision.consume && !decision.attach);

            activation.tick = 150;
            activation.reachedLevelDefName = activation.previousLevelDefName;
            decision = AnomalyMonolithKnowledgePolicy.Decide(snapshot, activation, 100);
            AssertTrue("non-transition is ignored", !decision.consume && !decision.attach);

            activation.reachedLevelDefName = "Twisted";
            snapshot.consumed = true;
            decision = AnomalyMonolithKnowledgePolicy.Decide(snapshot, activation, 100);
            AssertTrue("consumed snapshot cannot replay", !decision.consume && !decision.attach);

            snapshot.consumed = false;
            snapshot.studyStage = string.Empty;
            snapshot.becameActivatable = true;
            decision = AnomalyMonolithKnowledgePolicy.Decide(snapshot, activation, 100);
            AssertTrue("activation-ready fact attaches without a stage",
                decision.consume && decision.attach && decision.becameActivatable);
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

            claim = Claim();
            claim.studiedEntityId = string.Empty;
            tale = Tale();
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, 60);
            AssertTrue("claim missing id cannot suppress a Tale with a stable id",
                !decision.suppress && !decision.removeClaim);

            claim = Claim();
            tale = Tale();
            tale.studiedEntityId = string.Empty;
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, 60);
            AssertTrue("Tale missing id cannot satisfy a stable-id claim",
                !decision.suppress && !decision.removeClaim);
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

            claim.studyJobId = "Job_42";
            tale.studyJobId = "Job_42";
            tale.tick = 10000;
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, 60);
            AssertTrue("exact slow study job overrides tick expiry",
                decision.suppress && decision.removeClaim);

            tale.studyJobId = "Job_43";
            tale.tick = 101;
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, 60);
            AssertTrue("different current job cannot borrow exact study ownership",
                !decision.suppress && !decision.removeClaim);
            tale.tick = 10000;
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, 60);
            AssertTrue("expired different-job claim is removed fail-open",
                !decision.suppress && decision.removeClaim);

            claim.studyJobId = string.Empty;
            tale.studyJobId = string.Empty;
            tale.tick = 100;
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, 0);
            AssertTrue("zero window permits exact same tick", decision.suppress);
            tale.tick = 101;
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, 0);
            AssertTrue("zero window expires next tick", !decision.suppress && decision.removeClaim);
            tale.tick = 100 + AnomalyPolicyLimits.DefaultStudyTaleSuppressionTicks;
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, -1);
            AssertTrue("negative window uses the default inclusive boundary", decision.suppress);
            tale.tick++;
            decision = AnomalyTaleOwnershipPolicy.Decide(claim, tale, -1);
            AssertTrue("negative window fallback expires after its default",
                !decision.suppress && decision.removeClaim);
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
            facts = Breach();
            facts.tick = -1;
            AssertTrue("negative breach tick invalid", !ContainmentBreachPolicy.Plan(facts, null).valid);
            facts = Breach();
            facts.mapId = -1;
            AssertTrue("negative breach map invalid", !ContainmentBreachPolicy.Plan(facts, null).valid);
        }

        private static void TestBoundedStudySuppressionCache()
        {
            AnomalyStudySuppressionCache.Clear();
            AssertTrue("valid study claim enters transient cache",
                AnomalyStudySuppressionCache.Register(Claim(), 100, 60));
            AssertEqual("one study claim cached", 1,
                AnomalyStudySuppressionCache.CountForTests);

            AnomalyStudiedTaleFacts mismatch = Tale();
            mismatch.studiedEntityId = "Entity_Other";
            AssertTrue("mismatched Tale remains unsuppressed",
                !AnomalyStudySuppressionCache.TryConsume(mismatch, 60));
            AssertEqual("mismatched Tale retains exact claim", 1,
                AnomalyStudySuppressionCache.CountForTests);
            AssertTrue("exact Tale consumes and suppresses once",
                AnomalyStudySuppressionCache.TryConsume(Tale(), 60));
            AssertEqual("consumed claim leaves cache", 0,
                AnomalyStudySuppressionCache.CountForTests);
            AssertTrue("same Tale cannot consume twice",
                !AnomalyStudySuppressionCache.TryConsume(Tale(), 60));

            for (int i = 0; i < 3; i++)
            {
                AnomalyStudyTaleClaim claim = Claim();
                claim.studiedEntityId = "Entity_" + i;
                claim.acceptedTick = 100 + i;
                AssertTrue("bounded claim registers " + i,
                    AnomalyStudySuppressionCache.Register(claim, 102, 60, 2));
            }
            AssertEqual("study cache evicts oldest above cap", 2,
                AnomalyStudySuppressionCache.CountForTests);

            AnomalyStudyTaleClaim fresh = Claim();
            fresh.studiedEntityId = "Entity_Fresh";
            fresh.acceptedTick = 200;
            AssertTrue("new registration prunes expired claims",
                AnomalyStudySuppressionCache.Register(fresh, 200, 10));
            AssertEqual("only fresh claim remains after expiry prune", 1,
                AnomalyStudySuppressionCache.CountForTests);

            AnomalyStudyTaleClaim slowJob = Claim();
            slowJob.studyJobId = "Job_42";
            AssertTrue("exact-job claim enters transient cache",
                AnomalyStudySuppressionCache.Register(slowJob, 200, 10));
            AnomalyStudyTaleClaim muchLater = Claim();
            muchLater.studiedEntityId = "Entity_Later";
            muchLater.acceptedTick = 10000;
            AssertTrue("later registration preserves bounded exact-job ownership",
                AnomalyStudySuppressionCache.Register(muchLater, 10000, 10));
            AnomalyStudiedTaleFacts slowTale = Tale();
            slowTale.studyJobId = "Job_42";
            slowTale.tick = 10000;
            AssertTrue("cache consumes a delayed Tale from the exact study job",
                AnomalyStudySuppressionCache.TryConsume(slowTale, 10));
            AnomalyStudySuppressionCache.Clear();
            AssertEqual("explicit study cleanup clears all transient claims", 0,
                AnomalyStudySuppressionCache.CountForTests);
        }

        private static void TestRecentStudyCorrelationCache()
        {
            AnomalyRecentStudyCache.Clear();
            AnomalyRecentStudyFact study = new AnomalyRecentStudyFact
            {
                studierPawnId = "  Pawn_A  ",
                studiedEntityId = "  Entity_A  ",
                studiedDefName = "  EntityDef_A  ",
                studiedTick = 100
            };
            AssertTrue("recent exact study registers",
                AnomalyRecentStudyCache.Register(study, 100, 60));
            AssertTrue("recent exact study matches without consumption",
                AnomalyRecentStudyCache.Matches("Entity_A", "Pawn_A", 160, 60));
            AssertTrue("recent lookup remains non-consuming",
                AnomalyRecentStudyCache.Matches("Entity_A", "Pawn_A", 160, 60));
            List<AnomalyRecentStudyFact> normalized =
                AnomalyRecentStudyCache.SnapshotForTests();
            AssertEqual("recent cache normalizes studier identity once", "Pawn_A",
                normalized[0].studierPawnId);
            AssertEqual("recent cache normalizes entity identity once", "Entity_A",
                normalized[0].studiedEntityId);
            AssertEqual("recent cache normalizes optional Def identity once", "EntityDef_A",
                normalized[0].studiedDefName);
            HashSet<string> matchingStudierIds =
                AnomalyRecentStudyCache.MatchingStudierPawnIds(" Entity_A ", 160, 60);
            AssertEqual("batched recent lookup returns one exact researcher", 1,
                matchingStudierIds.Count);
            AssertTrue("batched recent lookup normalizes identity and remains non-consuming",
                matchingStudierIds.Contains("Pawn_A")
                    && AnomalyRecentStudyCache.CountForTests == 1);
            AssertEqual("batched recent lookup rejects a different entity", 0,
                AnomalyRecentStudyCache.MatchingStudierPawnIds("Entity_B", 160, 60).Count);
            AssertTrue("recent entity mismatch fails",
                !AnomalyRecentStudyCache.Matches("Entity_B", "Pawn_A", 160, 60));
            AssertTrue("recent studier mismatch fails",
                !AnomalyRecentStudyCache.Matches("Entity_A", "Pawn_B", 160, 60));
            AssertTrue("recent study expires after exact boundary",
                !AnomalyRecentStudyCache.Matches("Entity_A", "Pawn_A", 161, 60));
            AssertEqual("expired recent study pruned", 0, AnomalyRecentStudyCache.CountForTests);
            AssertEqual("batched recent lookup rejects a malformed query", 0,
                AnomalyRecentStudyCache.MatchingStudierPawnIds(" ", 161, 60).Count);

            for (int i = 0; i < 3; i++)
            {
                AssertTrue("bounded recent row registers " + i,
                    AnomalyRecentStudyCache.Register(new AnomalyRecentStudyFact
                    {
                        studierPawnId = "Pawn_" + i,
                        studiedEntityId = "Entity_" + i,
                        studiedTick = 200 + i
                    }, 202, 60, 2));
            }
            AssertEqual("recent cache evicts oldest above cap", 2,
                AnomalyRecentStudyCache.CountForTests);
            AssertTrue("evicted recent row no longer matches",
                !AnomalyRecentStudyCache.Matches("Entity_0", "Pawn_0", 202, 60));
            AssertTrue("malformed recent row rejected",
                !AnomalyRecentStudyCache.Register(new AnomalyRecentStudyFact(), 202, 60));
            AnomalyRecentStudyCache.Clear();
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
            AssertEqual("context entity platform identity copied", "Platform_A",
                plan.contextEntities[0].platformId);
            AssertEqual("context entity map identity copied", 7,
                plan.contextEntities[0].mapId);

            policy.containmentEnabled = false;
            plan = ContainmentBreachPolicy.Plan(facts, policy);
            AssertTrue("disabled breach retains verified facts", plan.valid && plan.escapedCount == 3);
            AssertTrue("disabled breach emits no page", !plan.writePage && plan.selectedWriters.Count == 0);

            facts = Breach();
            for (int i = 1; i < 100; i++) facts.entities.Add(Entity("Entity_" + i, true));
            policy = AnomalyPolicySnapshot.CreateDefault();
            policy.containmentMaxEntityLabelsInContext = AnomalyPolicyLimits.MaximumEntityLabels;
            plan = ContainmentBreachPolicy.Plan(facts, policy);
            AssertEqual("escaped count has defensive hard bound",
                AnomalyPolicyLimits.MaximumContainmentEntities, plan.escapedCount);
            AssertEqual("additional count reflects bounded verified set",
                AnomalyPolicyLimits.MaximumContainmentEntities
                    - AnomalyPolicyLimits.MaximumEntityLabels,
                plan.additionalEscapedCount);
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

            List<AnomalyWriterCandidate> oversized = new List<AnomalyWriterCandidate>
            {
                Writer("Pawn_A_LowIdFallback", true, false, true, 400, "a"),
                Writer("Pawn_Z_Recent", true, true, true, 225, "z"),
                Writer("Pawn_Y_Nearby", true, false, true, 4, "y")
            };
            List<AnomalyWriterCandidate> bounded =
                ContainmentBreachPolicy.BoundWriterCandidates(oversized, 12, 2);
            AssertEqual("candidate cap retains requested size", 2, bounded.Count);
            AssertEqual("candidate cap retains nearby before low-ID fallback",
                "Pawn_Y_Nearby", bounded[0].pawnId);
            AssertEqual("candidate cap retains recent studier before low-ID fallback",
                "Pawn_Z_Recent", bounded[1].pawnId);

            List<AnomalyWriterCandidate> nonHomePool =
                ContainmentBreachPolicy.BoundCandidatePool(
                    new List<AnomalyWriterCandidate>
                    {
                        Writer("Farther", true, false, false, 400, "z"),
                        Writer("Closer", true, false, false, 225, "a")
                    },
                    12,
                    1);
            AssertEqual("nested pool retains an otherwise-unqualified eligible pawn",
                1, nonHomePool.Count);
            AssertEqual("nested pool uses distance before stable ID for unqualified pawns",
                "Closer", nonHomePool[0].pawnId);

            List<AnomalyWriterCandidate> largeRoster = new List<AnomalyWriterCandidate>();
            for (int i = 0; i < 600; i++)
            {
                bool qualified = i % 10 != 0;
                largeRoster.Add(Writer(
                    "LargeRoster_" + i.ToString("D3"),
                    qualified,
                    false,
                    false,
                    qualified ? 4 : 400,
                    i.ToString("D3")));
            }
            List<AnomalyWriterCandidate> directLargeWriters =
                ContainmentBreachPolicy.BoundWriterCandidates(
                    largeRoster, 12, AnomalyPolicyLimits.MaximumContainmentCandidates);
            List<AnomalyWriterCandidate> largePool =
                ContainmentBreachPolicy.BoundCandidatePool(
                    largeRoster, 12, AnomalyPolicyLimits.MaximumContainmentCandidates);
            List<AnomalyWriterCandidate> writersFromLargePool =
                ContainmentBreachPolicy.BoundWriterCandidates(
                    largePool, 12, AnomalyPolicyLimits.MaximumContainmentCandidates);
            AssertEqual("large nested candidate pool obeys the hard cap",
                AnomalyPolicyLimits.MaximumContainmentCandidates, largePool.Count);
            AssertTrue("deriving writers from the capped ranked pool preserves direct ordering",
                directLargeWriters.Select(row => row.pawnId).SequenceEqual(
                    writersFromLargePool.Select(row => row.pawnId)));

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
            AssertEqual("dedup key", "anomaly-breach|7|100|escape-a", first.dedupKey);
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
            ContainmentEscapeFacts otherTick = Breach();
            otherTick.tick++;
            AssertTrue("different start tick changes dedup",
                ContainmentBreachPolicy.DedupKey(facts) != ContainmentBreachPolicy.DedupKey(otherTick));
        }

        private static void TestContainmentContextFormatter()
        {
            ContainmentEscapeFacts facts = Breach();
            facts.sameRoomCascade = true;
            facts.preEjectionSetting = "laboratory; indoors=visible";
            facts.entities.Add(Entity("Entity_B", true));
            AnomalyPolicySnapshot policy = AnomalyPolicySnapshot.CreateDefault();
            policy.containmentMaxEntityLabelsInContext = 1;
            ContainmentBreachPlan plan = ContainmentBreachPolicy.Plan(facts, policy);
            string context = ContainmentBreachContextFormatter.Format(plan);
            AssertEqual("bounded containment context",
                "anomaly_kind=containment_breach; escaped_count=2; "
                    + "escaped_entities=Entity_A label [EntityDef]; additional_escaped_count=1; "
                    + "witness_role=nearby; setting=laboratory, indoors-visible; "
                    + "same_room_cascade=true",
                context);
            AssertTrue("containment context omits exact platform and position",
                context.IndexOf("Platform_A", StringComparison.Ordinal) < 0
                    && context.IndexOf("platformX", StringComparison.Ordinal) < 0
                    && context.IndexOf("10,20", StringComparison.Ordinal) < 0);
            AssertTrue("containment context omits hidden mutant mechanics",
                context.IndexOf("mutant", StringComparison.OrdinalIgnoreCase) < 0);
            AssertEqual("visible fallback summary omits raw Def identity",
                "Entity_A label", ContainmentBreachContextFormatter.VisibleEntitySummary(plan));

            facts.entities[0].visibleLabel = new string('L', 160);
            plan = ContainmentBreachPolicy.Plan(facts, policy);
            string visible = ContainmentBreachContextFormatter.VisibleEntitySummary(plan);
            AssertEqual("visible entity label is actually truncated", 120, visible.Length);
            AssertTrue("prompt retains stable Def while visible summary does not",
                ContainmentBreachContextFormatter.Format(plan).Contains("[EntityDef]")
                    && !visible.Contains("EntityDef"));
            facts.entities[0].visibleLabel = "Entity_A label";

            facts.witnesses.Add(Writer("Pawn_B", true, false, true, 9, "Pawn_B"));
            plan = ContainmentBreachPolicy.Plan(facts, null);
            context = ContainmentBreachContextFormatter.Format(plan);
            AssertTrue("pair context names both exact POV roles",
                context.Contains("initiator_witness_role=nearby")
                    && context.Contains("recipient_witness_role=nearby")
                    && !context.StartsWith("witness_role=nearby;", StringComparison.Ordinal)
                    && !context.Contains("; witness_role=nearby;"));

            facts.sameRoomCascade = false;
            facts.entities.RemoveAt(1);
            plan = ContainmentBreachPolicy.Plan(facts, null);
            AssertTrue("single escape reports no same-room cascade",
                ContainmentBreachContextFormatter.Format(plan)
                    .Contains("same_room_cascade=false"));
        }

        private static void TestCreepJoinerArrivalContinuity()
        {
            AssertTrue("exact creepjoiner arrival marker accepted",
                CreepJoinerOutcomePolicy.IsCreepJoinerArrivalContext(
                    "arrival_kind=wanderer; creepjoiner=true; setting=outdoors"));
            AssertTrue("creepjoiner marker is case-insensitive and trimmed",
                CreepJoinerOutcomePolicy.IsCreepJoinerArrivalContext(" CREEPJOINER = TRUE "));
            AssertTrue("near-match creepjoiner key rejected",
                !CreepJoinerOutcomePolicy.IsCreepJoinerArrivalContext("notcreepjoiner=true"));
            AssertTrue("near-match creepjoiner value rejected",
                !CreepJoinerOutcomePolicy.IsCreepJoinerArrivalContext("creepjoiner=true-ish"));

            CreepJoinerArcSnapshot joined = CreepJoinerOutcomePolicy.UpsertArrival(
                null, " Pawn_Creep ", 123, string.Empty);
            AssertEqual("arrival creates normalized pawn identity", "Pawn_Creep", joined.pawnId);
            AssertEqual("arrival creates joined tick", 123, joined.joinedTick);
            AssertEqual("arrival creates joined phase", CreepJoinerPhaseTokens.Joined,
                joined.lastVisiblePhase);
            AssertTrue("arrival history is not terminal", !joined.terminal);
            AssertEqual("arrival uses current record schema",
                AnomalyPersistencePolicy.CurrentCreepJoinerArcSchemaVersion, joined.schemaVersion);

            CreepJoinerArcSnapshot enriched = CreepJoinerOutcomePolicy.UpsertArrival(
                joined, "Pawn_Creep", 124, "Arrival_1");
            AssertEqual("arrival event id may be attached after page creation", "Arrival_1",
                enriched.arrivalEventId);
            AssertEqual("canonical first joined tick is retained", 123, enriched.joinedTick);
            AssertTrue("arrival upsert returns a detached row", !object.ReferenceEquals(joined, enriched));

            CreepJoinerArcSnapshot terminal = Arc(AnomalyOutcomeTokens.Departed, true);
            terminal.arrivalEventId = string.Empty;
            enriched = CreepJoinerOutcomePolicy.UpsertArrival(
                terminal, "Pawn_Creep", 999, "Arrival_Late");
            AssertEqual("late canonical arrival id can enrich terminal continuity", "Arrival_Late",
                enriched.arrivalEventId);
            AssertEqual("arrival never downgrades terminal phase", AnomalyOutcomeTokens.Departed,
                enriched.lastVisiblePhase);
            AssertTrue("arrival never clears terminal marker", enriched.terminal);

            CreepJoinerArcSnapshot preJoinTerminal = Arc(AnomalyOutcomeTokens.Rejected, true);
            preJoinTerminal.joinedTick = 0;
            CreepJoinerArcSnapshot laterJoined = CreepJoinerOutcomePolicy.UpsertArrival(
                preJoinTerminal, "Pawn_Creep", 999, "Arrival_AfterRejection");
            AssertEqual("later arrival preserves the pre-join terminal tick sentinel", 0,
                laterJoined.joinedTick);
            AssertTrue("verified later arrival still preserves the terminal replay barrier",
                laterJoined.terminal);

            CreepJoinerArcSnapshot future = Arc(AnomalyOutcomeTokens.Aggressive, true);
            future.schemaVersion = 9;
            CreepJoinerArcSnapshot futureEnriched = CreepJoinerOutcomePolicy.UpsertArrival(
                future, "Pawn_Creep", 999, "Arrival_Future");
            AssertEqual("arrival upsert never downgrades a future row schema", 9,
                futureEnriched.schemaVersion);
            AssertTrue("arrival upsert preserves a future terminal barrier", futureEnriched.terminal);
        }

        private static void TestCreepJoinerVisibleOutcomes()
        {
            foreach (string phase in new[]
            {
                AnomalyOutcomeTokens.Rejected,
                AnomalyOutcomeTokens.Aggressive,
                AnomalyOutcomeTokens.Departed
            })
            {
                CreepJoinerOutcomeFacts facts = CreepOutcome(phase);
                facts.writers.Add(CreepWriter("Nearby", nearby: true, distanceSquared: 4));
                CreepJoinerOutcomePlan plan = CreepJoinerOutcomePolicy.Plan(facts, null, null);
                AssertTrue(phase + " verified transition is valid", plan.valid);
                AssertTrue(phase + " advances terminal continuity", plan.advanceArc
                    && plan.nextArc.terminal && plan.nextArc.lastVisiblePhase == phase);
                AssertEqual(phase + " maps to visible-only result",
                    CreepJoinerVisibleResultTokens.ForPhase(phase), plan.visibleResultToken);
                AssertEqual(phase + " source key is deterministic",
                    "Pawn_Creep|" + phase + "|200", plan.sourceKey);
            }

            CreepJoinerOutcomeFacts invalid = CreepOutcome(AnomalyOutcomeTokens.Rejected);
            invalid.transitioned = false;
            AssertTrue("unchanged tracker flag drops", !CreepJoinerOutcomePolicy.Plan(invalid, null, null).valid);
            invalid = CreepOutcome(AnomalyOutcomeTokens.Aggressive);
            invalid.transitionVerified = false;
            CreepJoinerOutcomePlan barrier = CreepJoinerOutcomePolicy.Plan(invalid, null, null);
            AssertTrue("unverified aggressive commit becomes a silent terminal barrier",
                barrier.valid && barrier.advanceArc && barrier.nextArc.terminal
                    && barrier.nextArc.lastVisiblePhase.Length == 0 && !barrier.writePage);
            invalid = CreepOutcome(AnomalyOutcomeTokens.Departed);
            invalid.playerVisible = false;
            barrier = CreepJoinerOutcomePolicy.Plan(invalid, null, null);
            AssertTrue("invisible committed departure becomes a silent terminal barrier",
                barrier.valid && barrier.advanceArc && barrier.nextArc.terminal
                    && barrier.nextArc.lastVisiblePhase.Length == 0 && !barrier.writePage);
            invalid = CreepOutcome(AnomalyOutcomeTokens.Aggressive);
            invalid.visibleResultToken = CreepJoinerVisibleResultTokens.Leaving;
            AssertTrue("phase/result mismatch drops", !CreepJoinerOutcomePolicy.Plan(invalid, null, null).valid);
            invalid = CreepOutcome(AnomalyOutcomeTokens.Departed);
            invalid.nestedOutcomeOwnedByRejection = true;
            AssertTrue("nested rejection outcome is owned by outer exact callback",
                !CreepJoinerOutcomePolicy.Plan(invalid, null, null).valid);

            CreepJoinerOutcomeFacts disabledFacts = CreepOutcome(AnomalyOutcomeTokens.Aggressive);
            disabledFacts.writers.Add(CreepWriter("Nearby", nearby: true, distanceSquared: 4));
            AnomalyPolicySnapshot disabled = AnomalyPolicySnapshot.CreateDefault();
            disabled.creepJoinerEnabled = false;
            CreepJoinerOutcomePlan disabledPlan = CreepJoinerOutcomePolicy.Plan(
                disabledFacts, null, disabled);
            AssertTrue("disabled output still advances visible history",
                disabledPlan.valid && disabledPlan.advanceArc && disabledPlan.nextArc.terminal);
            AssertTrue("disabled output writes no page", !disabledPlan.writePage);

            CreepJoinerOutcomePlan replay = CreepJoinerOutcomePolicy.Plan(
                CreepOutcome(AnomalyOutcomeTokens.Departed),
                Arc(AnomalyOutcomeTokens.Rejected, true), null);
            AssertTrue("terminal arc suppresses repeat callback",
                replay.valid && replay.replaySuppressed && !replay.advanceArc && !replay.writePage);
        }

        private static void TestCreepJoinerWriterSelectionAndBounds()
        {
            CreepJoinerOutcomeFacts rejection = CreepOutcome(AnomalyOutcomeTokens.Rejected);
            rejection.joinedBefore = false;
            rejection.writers.Add(CreepWriter("Nearby", nearby: true, distanceSquared: 1));
            rejection.writers.Add(CreepWriter(
                "Speaker", exactSpeaker: true, onSubjectMap: true, distanceSquared: 400));
            CreepJoinerOutcomePlan plan = CreepJoinerOutcomePolicy.Plan(rejection, null, null);
            AssertEqual("pre-join rejection uses exact speaker", "Speaker", plan.selectedWriter.pawnId);
            AssertEqual("pre-join rejection role is speaker", AnomalyWitnessRoleTokens.Speaker,
                plan.selectedWriter.roleToken);

            rejection.writers.Clear();
            rejection.writers.Add(CreepWriter("OffMapSpeaker", exactSpeaker: true));
            rejection.writers.Add(CreepWriter("TruthfulNearby", nearby: true, distanceSquared: 4));
            plan = CreepJoinerOutcomePolicy.Plan(rejection, null, null);
            AssertEqual("off-map speaker cannot claim witnessed POV", "TruthfulNearby",
                plan.selectedWriter.pawnId);
            rejection.writers.RemoveAt(1);
            AssertTrue("off-map speaker without a nearby witness produces no page",
                !CreepJoinerOutcomePolicy.Plan(rejection, null, null).writePage);

            CreepJoinerOutcomeFacts aggressiveRejection = CreepOutcome(
                AnomalyOutcomeTokens.Aggressive);
            aggressiveRejection.aggressionFollowedRejection = true;
            aggressiveRejection.writers.Add(CreepWriter(
                "RejectionSpeaker", exactSpeaker: true, onSubjectMap: true));
            aggressiveRejection.writers.Add(CreepWriter(
                "NearbyAfterRejection", nearby: true, distanceSquared: 1));
            plan = CreepJoinerOutcomePolicy.Plan(aggressiveRejection, null, null);
            AssertEqual("aggressive rejection keeps the truthful same-map speaker", "RejectionSpeaker",
                plan.selectedWriter.pawnId);
            AssertEqual("aggressive rejection persists the strongest visible phase",
                AnomalyOutcomeTokens.Aggressive, plan.nextArc.lastVisiblePhase);

            CreepJoinerOutcomeFacts departure = CreepOutcome(AnomalyOutcomeTokens.Departed);
            departure.joinedBefore = true;
            departure.subjectEligibleBefore = true;
            departure.writers.Add(CreepWriter("Subject", subject: true));
            departure.writers.Add(CreepWriter("Nearer", nearby: true, distanceSquared: 1));
            plan = CreepJoinerOutcomePolicy.Plan(departure, null, null);
            AssertEqual("joined departure uses eligible subject snapshot", "Subject",
                plan.selectedWriter.pawnId);
            AssertEqual("joined departure role is subject", AnomalyWitnessRoleTokens.Subject,
                plan.selectedWriter.roleToken);

            CreepJoinerOutcomeFacts aggression = CreepOutcome(AnomalyOutcomeTokens.Aggressive);
            aggression.writers.Add(CreepWriter("SpeakerOnly", exactSpeaker: true, distanceSquared: 1));
            aggression.writers.Add(CreepWriter("TieB", nearby: true, distanceSquared: 9, tie: "b"));
            aggression.writers.Add(CreepWriter("TieA", nearby: true, distanceSquared: 9, tie: "a"));
            plan = CreepJoinerOutcomePolicy.Plan(aggression, null, null);
            AssertEqual("aggression uses deterministic nearby witness only", "TieA",
                plan.selectedWriter.pawnId);
            AssertEqual("aggression role is nearby", AnomalyWitnessRoleTokens.Nearby,
                plan.selectedWriter.roleToken);

            aggression.writers.Clear();
            aggression.writers.Add(CreepWriter("Boundary", nearby: true, distanceSquared: 144));
            AssertTrue("creepjoiner radius boundary included",
                CreepJoinerOutcomePolicy.Plan(aggression, null, null).writePage);
            aggression.writers[0].distanceSquared = 145;
            AssertTrue("outside creepjoiner radius has no invented POV",
                !CreepJoinerOutcomePolicy.Plan(aggression, null, null).writePage);

            aggression.writers.Clear();
            for (int i = 0; i < 600; i++)
                aggression.writers.Add(CreepWriter("Pawn_" + i.ToString("D3"), false, 900));
            aggression.writers.Add(CreepWriter("Pawn_ZClosest", true, 1));
            plan = CreepJoinerOutcomePolicy.Plan(aggression, null, null);
            AssertEqual("candidate cap retains strongest nearby evidence", "Pawn_ZClosest",
                plan.selectedWriter.pawnId);

            CreepJoinerOutcomeFacts cappedInput = CreepOutcome(AnomalyOutcomeTokens.Aggressive);
            for (int i = 0; i < AnomalyPolicyLimits.MaximumCreepJoinerCandidateInputs; i++)
                cappedInput.writers.Add(CreepWriter("Far_" + i.ToString("D4"), false, 900));
            cappedInput.writers.Add(CreepWriter("BeyondInputCap", true, 1));
            AssertTrue("input beyond hard scan ceiling is ignored",
                !CreepJoinerOutcomePolicy.Plan(cappedInput, null, null).writePage);
        }

        private static void TestCreepJoinerContextFirewall()
        {
            CreepJoinerOutcomeFacts facts = CreepOutcome(AnomalyOutcomeTokens.Aggressive);
            facts.subjectLabel = "Visible Name; future_downside=never";
            facts.writers.Add(CreepWriter("Nearby", nearby: true, distanceSquared: 4));
            CreepJoinerOutcomePlan plan = CreepJoinerOutcomePolicy.Plan(facts, null, null);
            string context = CreepJoinerOutcomeContextFormatter.Format(facts, plan);
            AssertTrue("context contains visible phase/result/subject/role/terminal",
                context.Contains("anomaly_kind=creepjoiner_outcome")
                    && context.Contains("creepjoiner_phase=aggressive")
                    && context.Contains("visible_result=hostile")
                    && context.Contains("creepjoiner_subject_id=Pawn_Creep")
                    && context.Contains("witness_role=nearby")
                    && context.Contains("terminal=true"));
            AssertTrue("context sanitizes field injection",
                !context.Contains("; future_downside=") && context.Contains("future_downside-never"));
            AssertTrue("context schema cannot expose secret tracker configuration",
                context.IndexOf("benefit=", StringComparison.OrdinalIgnoreCase) < 0
                    && context.IndexOf("downside=", StringComparison.OrdinalIgnoreCase) < 0
                    && context.IndexOf("trigger=", StringComparison.OrdinalIgnoreCase) < 0
                    && context.IndexOf("infection=", StringComparison.OrdinalIgnoreCase) < 0);
            AssertEqual("invalid plan has no prompt context", string.Empty,
                CreepJoinerOutcomeContextFormatter.Format(facts, new CreepJoinerOutcomePlan()));

            facts.aggressionFollowedRejection = true;
            context = CreepJoinerOutcomeContextFormatter.Format(facts, plan);
            AssertTrue("aggressive rejection context preserves visible rejection provenance",
                context.Contains("rejection_response=true"));
        }

        private static void TestCreepJoinerSurgicalDisclosurePolicy()
        {
            CreepJoinerSurgicalDisclosureFacts facts = SurgicalDisclosure();
            CreepJoinerSurgicalDisclosurePlan plan =
                CreepJoinerSurgicalDisclosurePolicy.Plan(facts, null, null);
            AssertTrue("verified visible surgical disclosure is valid", plan.valid);
            AssertTrue("surgical disclosure advances non-terminal history",
                plan.advanceArc && !plan.nextArc.terminal
                    && plan.nextArc.lastVisiblePhase == AnomalyOutcomeTokens.SurgicalReveal);
            AssertEqual("surgical disclosure uses generic visible result",
                CreepJoinerVisibleResultTokens.Disclosed, plan.visibleResultToken);
            AssertEqual("surgical source key is stable",
                "Pawn_Creep|surgical_reveal|300", plan.sourceKey);

            foreach (Action<CreepJoinerSurgicalDisclosureFacts> invalidate in new Action<CreepJoinerSurgicalDisclosureFacts>[]
            {
                row => row.surgeryCompleted = false,
                row => row.trackerDisclosureAppended = false,
                row => row.playerVisible = false,
                row => row.subjectPawnId = " ",
                row => row.surgeonPawnId = "bad|surgeon"
            })
            {
                CreepJoinerSurgicalDisclosureFacts invalid = SurgicalDisclosure();
                invalidate(invalid);
                AssertTrue("false, unverified, invisible, or malformed disclosure drops",
                    !CreepJoinerSurgicalDisclosurePolicy.Plan(invalid, null, null).valid);
            }

            CreepJoinerArcSnapshot revealed = plan.nextArc;
            revealed.lastVisibleEventId = "Reveal_1";
            CreepJoinerSurgicalDisclosurePlan replay =
                CreepJoinerSurgicalDisclosurePolicy.Plan(facts, revealed, null);
            AssertTrue("repeat surgical disclosure is suppressed without rewriting history",
                replay.valid && replay.replaySuppressed && !replay.advanceArc
                    && replay.nextArc.lastVisibleEventId == "Reveal_1");

            CreepJoinerOutcomeFacts terminalFacts = CreepOutcome(AnomalyOutcomeTokens.Departed);
            terminalFacts.joinedBefore = true;
            terminalFacts.subjectEligibleBefore = true;
            terminalFacts.writers.Add(CreepWriter("Pawn_Creep", subject: true));
            CreepJoinerOutcomePlan terminal = CreepJoinerOutcomePolicy.Plan(
                terminalFacts, plan.nextArc, null);
            AssertTrue("later terminal outcome remains possible after surgical disclosure",
                terminal.valid && terminal.advanceArc && terminal.nextArc.terminal
                    && terminal.nextArc.lastVisiblePhase == AnomalyOutcomeTokens.Departed);

            AnomalyPolicySnapshot disabled = AnomalyPolicySnapshot.CreateDefault();
            disabled.creepJoinerEnabled = false;
            CreepJoinerSurgicalDisclosurePlan disabledPlan =
                CreepJoinerSurgicalDisclosurePolicy.Plan(facts, null, disabled);
            AssertTrue("disabled disclosure output still consumes reveal history",
                disabledPlan.valid && disabledPlan.advanceArc && !disabledPlan.writePage
                    && disabledPlan.nextArc.lastVisiblePhase == AnomalyOutcomeTokens.SurgicalReveal);

            CreepJoinerArcSnapshot terminalExisting = Arc(AnomalyOutcomeTokens.Rejected, true);
            CreepJoinerSurgicalDisclosurePlan afterTerminal =
                CreepJoinerSurgicalDisclosurePolicy.Plan(facts, terminalExisting, null);
            AssertTrue("terminal continuity blocks a later surgical replay",
                afterTerminal.replaySuppressed && !afterTerminal.advanceArc);

            CreepJoinerArcSnapshot mismatched = Arc(CreepJoinerPhaseTokens.Joined, false);
            mismatched.pawnId = "Pawn_Other";
            CreepJoinerSurgicalDisclosurePlan mismatchedPlan =
                CreepJoinerSurgicalDisclosurePolicy.Plan(facts, mismatched, null);
            AssertTrue("mismatched continuity cannot rewrite another pawn's arc",
                mismatchedPlan.valid && mismatchedPlan.replaySuppressed
                    && !mismatchedPlan.advanceArc && mismatchedPlan.nextArc.pawnId == "Pawn_Other");

            CreepJoinerArcSnapshot preservedByArrival = CreepJoinerOutcomePolicy.UpsertArrival(
                plan.nextArc, "Pawn_Creep", 400, "Arrival_AfterReveal");
            AssertTrue("later canonical arrival preserves non-terminal reveal history",
                !preservedByArrival.terminal
                    && preservedByArrival.lastVisiblePhase == AnomalyOutcomeTokens.SurgicalReveal
                    && preservedByArrival.arrivalEventId == "Arrival_AfterReveal");
        }

        private static void TestCreepJoinerSurgicalWriterAndContextFirewall()
        {
            CreepJoinerSurgicalDisclosureFacts facts = SurgicalDisclosure();
            CreepJoinerSurgicalDisclosurePlan plan =
                CreepJoinerSurgicalDisclosurePolicy.Plan(facts, null, null);
            AssertEqual("eligible surgeon is first disclosure writer", "Pawn_Surgeon",
                plan.selectedWriters[0].pawnId);
            AssertEqual("first disclosure role is surgeon", AnomalyWitnessRoleTokens.Surgeon,
                plan.selectedWriters[0].roleToken);
            AssertEqual("eligible subject is second disclosure writer", "Pawn_Creep",
                plan.selectedWriters[1].pawnId);
            AssertEqual("second disclosure role is subject", AnomalyWitnessRoleTokens.Subject,
                plan.selectedWriters[1].roleToken);

            AnomalyPolicySnapshot oneWriter = AnomalyPolicySnapshot.CreateDefault();
            oneWriter.creepJoinerMaxWitnesses = 1;
            plan = CreepJoinerSurgicalDisclosurePolicy.Plan(facts, null, oneWriter);
            AssertEqual("XML one-writer cap retains exact surgeon first", 1,
                plan.selectedWriters.Count);
            AssertEqual("one-writer cap keeps surgeon role", AnomalyWitnessRoleTokens.Surgeon,
                plan.selectedWriters[0].roleToken);

            facts = SurgicalDisclosure();
            facts.subjectEligible = false;
            plan = CreepJoinerSurgicalDisclosurePolicy.Plan(facts, null, null);
            AssertTrue("surgeon-only eligibility emits one surgeon POV",
                plan.writePage && plan.selectedWriters.Count == 1
                    && plan.selectedWriters[0].roleToken == AnomalyWitnessRoleTokens.Surgeon);

            facts = SurgicalDisclosure();
            facts.surgeonEligible = false;
            plan = CreepJoinerSurgicalDisclosurePolicy.Plan(facts, null, null);
            AssertTrue("subject-only eligibility emits one subject POV",
                plan.writePage && plan.selectedWriters.Count == 1
                    && plan.selectedWriters[0].roleToken == AnomalyWitnessRoleTokens.Subject);

            facts = SurgicalDisclosure();
            facts.surgeonEligible = false;
            facts.subjectEligible = false;
            plan = CreepJoinerSurgicalDisclosurePolicy.Plan(facts, null, null);
            AssertTrue("no exact eligible role advances history without inventing a witness",
                plan.valid && plan.advanceArc && !plan.writePage
                    && plan.selectedWriters.Count == 0);

            facts = SurgicalDisclosure();
            facts.subjectPawnId = facts.surgeonPawnId;
            plan = CreepJoinerSurgicalDisclosurePolicy.Plan(facts, null, null);
            AssertTrue("same-pawn identity cannot create duplicate writer roles",
                plan.writePage && plan.selectedWriters.Count == 1
                    && plan.selectedWriters[0].roleToken == AnomalyWitnessRoleTokens.Surgeon);

            facts = SurgicalDisclosure();
            facts.subjectLabel = "Patient; injected=blocked";
            facts.surgeonLabel = "Doctor; other=blocked";
            plan = CreepJoinerSurgicalDisclosurePolicy.Plan(facts, null, null);
            string context = CreepJoinerSurgicalDisclosureContextFormatter.Format(facts, plan);
            AssertTrue("surgical context distinguishes examiner and patient roles",
                context.Contains("creepjoiner_phase=surgical_reveal")
                    && context.Contains("visible_result=disclosed")
                    && context.Contains("creepjoiner_subject_id=Pawn_Creep")
                    && context.Contains("creepjoiner_surgeon_id=Pawn_Surgeon")
                    && context.Contains("initiator_witness_role=surgeon")
                    && context.Contains("recipient_witness_role=subject"));
            AssertTrue("surgical context sanitizes role-label field injection",
                !context.Contains("; injected=") && !context.Contains("; other="));
            AssertTrue("surgical context has no hidden identifiers, letter text, or terminal claim",
                context.IndexOf("PsychicAgony", StringComparison.OrdinalIgnoreCase) < 0
                    && context.IndexOf("Metalhorror", StringComparison.OrdinalIgnoreCase) < 0
                    && context.IndexOf("surgicalInspectionLetterExtra", StringComparison.OrdinalIgnoreCase) < 0
                    && context.IndexOf("terminal=true", StringComparison.OrdinalIgnoreCase) < 0);
            AssertEqual("invalid surgical plan has no context", string.Empty,
                CreepJoinerSurgicalDisclosureContextFormatter.Format(
                    facts, new CreepJoinerSurgicalDisclosurePlan()));
        }

        private static void TestCreepJoinerSurgeryTaleOwnership()
        {
            CreepJoinerSurgeryTaleClaim claim = SurgeryClaim();
            CreepJoinerSurgeryTaleFacts tale = SurgeryTale();
            AssertTrue("exact active DidSurgery pair is deferred",
                CreepJoinerSurgeryTaleOwnershipPolicy.CanDefer(claim, tale, 10));

            tale.firstPawnId = "Pawn_Other";
            AssertTrue("surgeon mismatch fails open",
                !CreepJoinerSurgeryTaleOwnershipPolicy.CanDefer(claim, tale, 10));
            tale = SurgeryTale();
            tale.secondPawnId = "Pawn_Other";
            AssertTrue("subject mismatch fails open",
                !CreepJoinerSurgeryTaleOwnershipPolicy.CanDefer(claim, tale, 10));
            tale = SurgeryTale();
            tale.taleDefName = "DidOperation";
            AssertTrue("non-DidSurgery Tale fails open",
                !CreepJoinerSurgeryTaleOwnershipPolicy.CanDefer(claim, tale, 10));
            tale = SurgeryTale();
            tale.tick = 110;
            AssertTrue("DidSurgery ownership includes the exact expiry boundary",
                CreepJoinerSurgeryTaleOwnershipPolicy.CanDefer(claim, tale, 10));
            tale = SurgeryTale();
            tale.tick = 111;
            AssertTrue("expired DidSurgery ownership fails open",
                !CreepJoinerSurgeryTaleOwnershipPolicy.CanDefer(claim, tale, 10));
            tale.tick = 99;
            AssertTrue("pre-scope Tale fails open",
                !CreepJoinerSurgeryTaleOwnershipPolicy.CanDefer(claim, tale, 10));
            claim.active = false;
            AssertTrue("closed ownership cannot defer",
                !CreepJoinerSurgeryTaleOwnershipPolicy.CanDefer(claim, SurgeryTale(), 10));

            CreepJoinerSurgicalDisclosurePlan success =
                CreepJoinerSurgicalDisclosurePolicy.Plan(SurgicalDisclosure(), null, null);
            AssertTrue("dedicated event success suppresses one deferred generic signal",
                CreepJoinerSurgeryTaleOwnershipPolicy.ShouldSuppress(success, true, true));
            AssertTrue("failed dedicated event releases generic signal",
                !CreepJoinerSurgeryTaleOwnershipPolicy.ShouldSuppress(success, false, true));
            AssertTrue("unverified disclosure releases generic signal",
                !CreepJoinerSurgeryTaleOwnershipPolicy.ShouldSuppress(
                    new CreepJoinerSurgicalDisclosurePlan(), true, true));
            AssertTrue("missing deferred ownership cannot suppress",
                !CreepJoinerSurgeryTaleOwnershipPolicy.ShouldSuppress(success, true, false));
            CreepJoinerSurgicalDisclosurePlan replay =
                CreepJoinerSurgicalDisclosurePolicy.Plan(
                    SurgicalDisclosure(), success.nextArc, null);
            AssertTrue("replayed reveal releases generic signal",
                !CreepJoinerSurgeryTaleOwnershipPolicy.ShouldSuppress(replay, true, true));
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
                tick = 100,
                oldProgress = 10,
                newProgress = 20,
                noteThresholdsCrossed = 1
            };
        }

        private static CreepJoinerOutcomeFacts CreepOutcome(string phase)
        {
            return new CreepJoinerOutcomeFacts
            {
                pawnId = "Pawn_Creep",
                subjectLabel = "Aster",
                phase = phase,
                visibleResultToken = CreepJoinerVisibleResultTokens.ForPhase(phase),
                tick = 200,
                transitioned = true,
                transitionVerified = true,
                playerVisible = true
            };
        }

        private static CreepJoinerSurgicalDisclosureFacts SurgicalDisclosure()
        {
            return new CreepJoinerSurgicalDisclosureFacts
            {
                subjectPawnId = "Pawn_Creep",
                subjectLabel = "Aster",
                surgeonPawnId = "Pawn_Surgeon",
                surgeonLabel = "Mara",
                tick = 300,
                surgeryCompleted = true,
                trackerDisclosureAppended = true,
                playerVisible = true,
                surgeonEligible = true,
                subjectEligible = true
            };
        }

        private static CreepJoinerSurgeryTaleClaim SurgeryClaim()
        {
            return new CreepJoinerSurgeryTaleClaim
            {
                subjectPawnId = "Pawn_Creep",
                surgeonPawnId = "Pawn_Surgeon",
                openedTick = 100,
                active = true
            };
        }

        private static CreepJoinerSurgeryTaleFacts SurgeryTale()
        {
            return new CreepJoinerSurgeryTaleFacts
            {
                taleDefName = CreepJoinerSurgeryTaleOwnershipPolicy.DidSurgeryDefName,
                firstPawnId = "Pawn_Surgeon",
                secondPawnId = "Pawn_Creep",
                tick = 101
            };
        }

        private static CreepJoinerWriterCandidate CreepWriter(
            string id,
            bool nearby = false,
            int distanceSquared = -1,
            bool exactSpeaker = false,
            bool subject = false,
            string tie = null,
            bool onSubjectMap = false)
        {
            return new CreepJoinerWriterCandidate
            {
                pawnId = id,
                eligible = true,
                exactSpeaker = exactSpeaker,
                subject = subject,
                onSubjectMap = onSubjectMap || nearby || subject,
                nearby = nearby,
                distanceSquared = distanceSquared,
                tieBreakKey = tie ?? id
            };
        }

        private static CreepJoinerArcSnapshot Arc(string phase, bool terminal)
        {
            return new CreepJoinerArcSnapshot
            {
                pawnId = "Pawn_Creep",
                joinedTick = 100,
                lastVisiblePhase = phase,
                terminal = terminal,
                schemaVersion = AnomalyPersistencePolicy.CurrentCreepJoinerArcSchemaVersion
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
                platformId = "Platform_A",
                mapId = 7,
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

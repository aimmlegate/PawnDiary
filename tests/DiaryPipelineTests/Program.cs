using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Xml.Linq;
using PawnDiary;
using PawnDiary.Integration;

namespace DiaryPipelineTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestCombatPromptPlan();
            TestSoloPromptPlan();
            TestOwnedPromptTextIsNotSentenceCapped();
            TestPromptTemplatePolicyReachesPromptPlan();
            TestQuestPromptPlanFields();
            TestProgressionPromptPlanFields();
            TestDualPovPromptPlans();
            TestRecipientFollowupPlan();
            TestNeutralGenerationPlans();
            TestTitlePromptPlan();
            TestSoloSelection();
            TestSoloBatchSelection();
            TestPromptEnchantmentPlanner();
            TestPromptEnchantmentCandidateSnapshot();
            TestPromptEnchantmentDecayPolicy();
            TestHediffPersonaOverridePolicy();
            TestDiaryEntryTitleFilter();
            TestDiaryEntryStatsAccumulator();
            TestMemoryDecayXmlPolicy();
            TestObservedConditionDecayXmlPolicy();
            TestColorCueXmlPolicy();
            TestPromptTextSanitizer();
            TestPromptContextLines();
            TestExternalEventRequestText();
            TestExternalPromptContextText();
            TestExternalEntryAttribution();
            TestExternalApiBudgetPolicy();
            TestListenerRegistry();
            TestDiaryListText();
            TestContextProviderRegistry();
            TestEventWindowPolicy();
            TestProgressionMilestonePolicy();
            TestPsylinkProgressionLevelPolicy();
            TestArcReflectionSchedulePolicy();
            TestArcReflectionMemorySelector();
            TestEventPromptKeyCandidates();
            TestDomainClassifier();
            TestContextFields();
            TestResponsePostprocessorRules();
            TestDirectSpeechParser();
            TestGeneratedTextKeepsTrailingSpeech();
            TestPromptCaptureFormatting();
            TestApiLaneSelector();
            TestApiLaneIdentityAndLabels();
            TestApiEndpointPolicy();
            TestApiRequestAuth();
            TestPromptSettingsMenuPolicy();
            TestTuningOverrideMigration();
            TestAdvancedRawSyntax();
            TestPromptTextTemplate();
            TestLlmRequestJsonBuilder();
            TestModelReasoningCapability();
            TestArchivedPendingReloadFallbackStatus();
            TestArchiveEligibility();
            TestArchiveFallbackFact();
            TestArchiveOverflowSelection();
            TestLifeBoundaryPolicy();
            TestDiarySentenceExcerpt();
            TestExternalDirectEntryText();
            TestExternalWritingStyleOverrideText();
            TestSurrogateSafeTruncation();
            TestRedactSecrets();
            TestWritingStyleReachesSystemPrompt();
            TestShippedTemplatesWritingStyleContract();

            Console.WriteLine("DiaryPipelineTests passed " + assertions + " assertions.");
            return 0;
        }

        private static void TestPromptSettingsMenuPolicy()
        {
            AssertTrue(
                "template system prompt is a raw override field",
                PromptSettingsMenuPolicy.IsTemplateTextOverrideField("systemPrompt"));
            AssertTrue(
                "template final instruction is a raw override field",
                PromptSettingsMenuPolicy.IsTemplateTextOverrideField("finalInstruction"));
            AssertTrue(
                "template recipient instruction is a raw override field",
                PromptSettingsMenuPolicy.IsTemplateTextOverrideField("recipientFinalInstruction"));
            AssertTrue(
                "template field list is not prompt text",
                !PromptSettingsMenuPolicy.IsTemplateTextOverrideField("fields"));

            AssertTrue(
                "prompt policy does not mirror inherited shared system prompts",
                !PromptSettingsMenuPolicy.TemplateFieldShouldShowInheritedFallback("systemPrompt"));
            AssertTrue(
                "prompt policy does not mirror inherited final instructions",
                !PromptSettingsMenuPolicy.TemplateFieldShouldShowInheritedFallback("finalInstruction"));
            AssertTrue(
                "prompt policy does not mirror inherited recipient instructions",
                !PromptSettingsMenuPolicy.TemplateFieldShouldShowInheritedFallback("recipientFinalInstruction"));
        }

        private static void TestTuningOverrideMigration()
        {
            // Overrides saved under the removed translation-key editors are dropped; live literal-text and
            // unrelated scalar overrides are kept. Keys are "defName.fieldName" (nested policy fields keep
            // their batch./hediff. prefix).
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                { "Diary_SomeEnchantment.intensityKey", "PawnDiary.Prompt.Intensity.High" },
                { "Diary_SomeEnchantment.intensityText", "acutely" },
                { "Diary_SomeEnchantment.frequency", "0.25" },
                { "Diary_SomeWindow.startTextKey", "PawnDiary.Event.Foo.start" },
                { "Diary_SomeGroup.batch.labelKey", "PawnDiary.Event.BatchLabel" },
                { "Diary_SomeGroup.batch.labelText", "The colonists chatted" },
                { "Diary_SomeGroup.hediff.appearedTextKey", "PawnDiary.Event.HediffAppeared" },
                { "Diary_Ctx.textKey", "PawnDiary.Ctx.ActiveConditions" },
                { "Diary_Tuning.socialFightDedupTicks", "1250" },
            };

            int removed = TuningOverrideMigration.PruneRemovedFieldKeys(overrides);

            AssertTrue("prunes exactly the six orphaned removed-editor overrides", removed == 6);
            AssertTrue("keeps literal intensity text override",
                overrides.ContainsKey("Diary_SomeEnchantment.intensityText"));
            AssertTrue("keeps literal batch label override",
                overrides.ContainsKey("Diary_SomeGroup.batch.labelText"));
            AssertTrue("keeps unrelated scalar override",
                overrides.ContainsKey("Diary_Tuning.socialFightDedupTicks"));
            AssertTrue("drops orphaned enchantment intensityKey",
                !overrides.ContainsKey("Diary_SomeEnchantment.intensityKey"));
            AssertTrue("drops orphaned prompt-enchantment frequency alias",
                !overrides.ContainsKey("Diary_SomeEnchantment.frequency"));
            AssertTrue("drops orphaned nested batch.labelKey",
                !overrides.ContainsKey("Diary_SomeGroup.batch.labelKey"));

            // The retained *Text field names must never be mistaken for removed *Key fields.
            AssertTrue("promptPriorityText is not treated as a removed field",
                !TuningOverrideMigration.IsRemovedFieldKey("Diary_SomeWindow.promptPriorityText"));
            AssertTrue("batch.labelText is not treated as a removed field",
                !TuningOverrideMigration.IsRemovedFieldKey("Diary_SomeGroup.batch.labelText"));
            AssertTrue("raw translation-key field names are hidden from node settings",
                TuningOverrideMigration.IsRemovedFieldName("startTextKey"));
            AssertTrue("nested raw translation-key field names are hidden from node settings",
                TuningOverrideMigration.IsRemovedFieldName("batch.labelKey"));
            AssertTrue("prompt-enchantment frequency alias is hidden from node settings",
                TuningOverrideMigration.IsRemovedFieldName("frequency"));
            AssertTrue("literal text field names remain editable in node settings",
                !TuningOverrideMigration.IsRemovedFieldName("batch.labelText"));

            AssertTrue("second pass is a no-op",
                TuningOverrideMigration.PruneRemovedFieldKeys(overrides) == 0);
        }

        private static void TestAdvancedRawSyntax()
        {
            AdvancedRawSyntaxCheck valid = AdvancedRawSyntax.CheckThoughtProgressionRules(
                "food|NeedFood|2:1,3:2,4:3\nrest|NeedRest|1:1");
            AssertTrue("valid thought progression syntax passes", valid.valid);
            AssertTrue("valid syntax parsed two rows", valid.lines.Count == 2);
            AssertTrue("valid syntax parsed three stages on first row", valid.lines[0].stages.Count == 3);

            AdvancedRawSyntaxCheck empty = AdvancedRawSyntax.CheckThoughtProgressionRules(" \n ");
            AssertTrue("blank thought progression table is allowed", empty.valid && empty.empty);

            AdvancedRawSyntaxCheck nullSentinel = AdvancedRawSyntax.CheckThoughtProgressionRules("<null>");
            AssertTrue("null sentinel is accepted", nullSentinel.valid && nullSentinel.nullSentinel);

            AdvancedRawSyntaxCheck missingColumn = AdvancedRawSyntax.CheckThoughtProgressionRules("food|NeedFood");
            AssertTrue("missing stage column fails", !missingColumn.valid);
            AssertTrue("missing stage column reports column issue",
                missingColumn.firstError.issue == AdvancedRawSyntaxIssue.ExpectedThoughtProgressionColumns);

            AdvancedRawSyntaxCheck badStage = AdvancedRawSyntax.CheckThoughtProgressionRules("food|NeedFood|x:1");
            AssertTrue("bad stage index fails", !badStage.valid);
            AssertTrue("bad stage index reports stage issue",
                badStage.firstError.issue == AdvancedRawSyntaxIssue.BadStageIndex);

            AdvancedRawSyntaxCheck schemaIntegers = AdvancedRawSyntax.CheckThoughtProgressionRules("food|NeedFood|-1:0,2:-3,2:5");
            AssertTrue("schema integer fields accept any integer values", schemaIntegers.valid);
            AssertTrue("schema list entries do not impose uniqueness beyond XML", schemaIntegers.lines[0].stages.Count == 3);

            AdvancedRawSyntaxCheck weather = AdvancedRawSyntax.CheckWeatherMentionRules("Rain=0.35\nFog:0.1");
            AssertTrue("weather mention chance table accepts equals and colon pairs", weather.valid);
            AssertTrue("weather mention chance table parses pair columns", weather.lines[0].columns[0] == "Rain");
            AdvancedRawSyntaxCheck badWeather = AdvancedRawSyntax.CheckWeatherMentionRules("Rain=heavy");
            AssertTrue("weather mention chance table rejects non-float chance", !badWeather.valid);
            AssertTrue("weather mention chance table reports float issue",
                badWeather.firstError.issue == AdvancedRawSyntaxIssue.BadFloat);

            AdvancedRawSyntaxCheck ritual = AdvancedRawSyntax.CheckRitualQualityBands("0.4=quiet\n0.8=stirring");
            AssertTrue("ritual quality band table accepts numeric thresholds", ritual.valid);
            AdvancedRawSyntaxCheck badRitual = AdvancedRawSyntax.CheckRitualQualityBands("low=quiet");
            AssertTrue("ritual quality band table rejects non-float threshold", !badRitual.valid);
            AssertTrue("ritual quality band table reports float issue",
                badRitual.firstError.issue == AdvancedRawSyntaxIssue.BadFloat);

            AdvancedRawSyntaxCheck fields = AdvancedRawSyntax.CheckPromptFields(
                "true|mood|Mood|mood_key\nfalse|entry|EntryText");
            AssertTrue("prompt field table accepts three or four columns", fields.valid);
            AssertTrue("prompt field table pads missing context key", fields.lines[1].columns[3] == string.Empty);
            AdvancedRawSyntaxCheck badFieldBool = AdvancedRawSyntax.CheckPromptFields("maybe|mood|Mood");
            AssertTrue("prompt field table rejects non-bool enabled value", !badFieldBool.valid);
            AssertTrue("prompt field table reports bool issue",
                badFieldBool.firstError.issue == AdvancedRawSyntaxIssue.BadBool);

            AdvancedRawSyntaxCheck severity = AdvancedRawSyntax.CheckSeverityTiers("major|0.5|-1|2|1.2\nminor");
            AssertTrue("severity tier table accepts optional numeric columns", severity.valid);
            AdvancedRawSyntaxCheck badSeverity = AdvancedRawSyntax.CheckSeverityTiers("minor|often");
            AssertTrue("severity tier table rejects non-float numeric columns", !badSeverity.valid);
            AssertTrue("severity tier table reports float issue",
                badSeverity.firstError.issue == AdvancedRawSyntaxIssue.BadFloat);

            AdvancedRawSyntaxCheck milestones = AdvancedRawSyntax.CheckIntList("3\n6\n9", "List<int>");
            AssertTrue("integer milestone list accepts one int per line", milestones.valid);
            AdvancedRawSyntaxCheck badMilestone = AdvancedRawSyntax.CheckIntList("3\nsix", "List<int>");
            AssertTrue("integer milestone list rejects non-int rows", !badMilestone.valid);
            AssertTrue("integer milestone list reports int issue",
                badMilestone.firstError.issue == AdvancedRawSyntaxIssue.BadInt);
        }

        private static void TestPromptTextTemplate()
        {
            AssertEqual(
                "literal settings text formats placeholders",
                "Alice noticed smoke.",
                PromptTextTemplate.Format("{0} noticed {1}.", "Alice", "smoke"));
            AssertEqual(
                "literal settings text tolerates malformed braces",
                "{0 noticed smoke.",
                PromptTextTemplate.Format("{0 noticed smoke.", "Alice"));
            AssertEqual(
                "blank literal settings text stays blank",
                string.Empty,
                PromptTextTemplate.Format("   ", "Alice"));
        }

        // Pins the shared fallback-fact picker that keeps a stale/failed page's body and title identical
        // before and after compaction. Compaction bakes ResolveFact(prompt, rawText) into the archived
        // text; the live card later re-resolves from that text (prompt gone) and must land on the same
        // string. The final assertion locks that round-trip.
        private static void TestArchiveFallbackFact()
        {
            AssertEqual(
                "fallback prefers what-happened over raw text",
                "Alice insulted Bob.",
                DiaryArchiveFallback.ResolveFact("event: argument\nwhat happened: Alice insulted Bob.", "Bob was nearby."));
            AssertEqual(
                "fallback prefers what-you-saw over what-happened",
                "the freezer door ajar",
                DiaryArchiveFallback.ResolveFact("what you saw: the freezer door ajar\nwhat happened: ignored", "raw"));
            AssertEqual(
                "fallback reads death facts",
                "damage=Cut; weapon=knife",
                DiaryArchiveFallback.ResolveFact("event: death\ndeath facts: damage=Cut; weapon=knife", "Alice died."));
            AssertEqual(
                "fallback uses raw text when prompt has no fact field",
                "Alice repaired the generator.",
                DiaryArchiveFallback.ResolveFact("event: quiet work\npov: Alice", "Alice repaired the generator."));
            AssertEqual(
                "archived row with no prompt falls back to baked text",
                "damage=Cut; weapon=knife",
                DiaryArchiveFallback.ResolveFact(string.Empty, "damage=Cut; weapon=knife"));
            AssertEqual(
                "fallback uses first prompt line as last resort",
                "event: strange dream",
                DiaryArchiveFallback.ResolveFact("event: strange dream\npov: Alice", string.Empty));
            AssertEqual(
                "fully blank fallback is empty",
                string.Empty,
                DiaryArchiveFallback.ResolveFact(string.Empty, string.Empty));

            // The fix-A invariant: bake the hot fact into text, then re-resolve with no prompt -> same fact.
            string hotPrompt = "event: death\ndeath facts: damage=Cut; weapon=knife; surroundings=near the freezer";
            string hotRaw = "Alice died from blood loss.";
            string baked = DiaryArchiveFallback.ResolveFact(hotPrompt, hotRaw);
            AssertEqual(
                "compaction fact round-trips through baked text",
                baked,
                DiaryArchiveFallback.ResolveFact(string.Empty, baked));
        }

        // Pins the pure overflow-trim selection: oldest-first, capped at the budget, skipping refs that
        // must stay hot without consuming budget.
        private static void TestArchiveOverflowSelection()
        {
            AssertEqual(
                "overflow selection caps at budget",
                "0,1",
                JoinInts(DiaryArchiveCompactionPlanner.SelectOverflowRemovals(new List<bool> { true, true, true }, 2)));
            AssertEqual(
                "overflow selection skips kept refs",
                "1,3",
                JoinInts(DiaryArchiveCompactionPlanner.SelectOverflowRemovals(new List<bool> { false, true, false, true }, 2)));
            AssertEqual(
                "overflow selection stops when removable runs out",
                "0",
                JoinInts(DiaryArchiveCompactionPlanner.SelectOverflowRemovals(new List<bool> { true, false, false }, 5)));
            AssertEqual(
                "overflow selection drops nothing at zero budget",
                string.Empty,
                JoinInts(DiaryArchiveCompactionPlanner.SelectOverflowRemovals(new List<bool> { true, true }, 0)));
            AssertEqual(
                "overflow selection tolerates null list",
                string.Empty,
                JoinInts(DiaryArchiveCompactionPlanner.SelectOverflowRemovals(null, 3)));
            AssertEqual(
                "overflow selection drops nothing when none removable",
                string.Empty,
                JoinInts(DiaryArchiveCompactionPlanner.SelectOverflowRemovals(new List<bool> { false, false }, 2)));
        }

        private static string JoinInts(List<int> values)
        {
            return values == null ? string.Empty : string.Join(",", values);
        }

        private static void TestLifeBoundaryPolicy()
        {
            AssertTrue(
                "no death means no final boundary",
                !DiaryLifeBoundaryPolicy.FinalDeathBoundaryApplies(false, false));
            AssertTrue(
                "death remains terminal while pawn is not alive",
                DiaryLifeBoundaryPolicy.FinalDeathBoundaryApplies(true, false));
            AssertTrue(
                "resurrected live pawn ignores old final death boundary",
                !DiaryLifeBoundaryPolicy.FinalDeathBoundaryApplies(true, true));
        }

        private static void TestArchiveEligibility()
        {
            DiaryArchiveEligibilityDecision generated = DiaryArchiveEligibility.Evaluate(
                titlePending: false,
                generatedText: "I wrote the page.",
                archivedGenerationStale: false,
                status: DiaryGenerationStatus.Complete,
                fallbackText: "Alice chatted with Bob.");
            AssertTrue("generated old page can archive", generated.CanArchive);
            AssertTrue("generated old page keeps generated text", !generated.ForceFallback);

            DiaryArchiveEligibilityDecision titlePending = DiaryArchiveEligibility.Evaluate(
                titlePending: true,
                generatedText: "I wrote the page.",
                archivedGenerationStale: false,
                status: DiaryGenerationStatus.Complete,
                fallbackText: "Alice chatted with Bob.");
            AssertTrue("title-pending page stays hot", !titlePending.CanArchive);

            DiaryArchiveEligibilityDecision stale = DiaryArchiveEligibility.Evaluate(
                titlePending: false,
                generatedText: string.Empty,
                archivedGenerationStale: true,
                status: DiaryGenerationStatus.NotGenerated,
                fallbackText: "Alice chatted with Bob.");
            AssertTrue("stale attempted page can archive", stale.CanArchive);
            AssertTrue("stale attempted page forces fallback", stale.ForceFallback);

            DiaryArchiveEligibilityDecision failedWithFallback = DiaryArchiveEligibility.Evaluate(
                titlePending: false,
                generatedText: string.Empty,
                archivedGenerationStale: false,
                status: DiaryGenerationStatus.Failed,
                fallbackText: "Alice chatted with Bob.");
            AssertTrue("failed page with raw text can archive", failedWithFallback.CanArchive);
            AssertTrue("failed page with raw text forces fallback", failedWithFallback.ForceFallback);

            DiaryArchiveEligibilityDecision failedWithoutFallback = DiaryArchiveEligibility.Evaluate(
                titlePending: false,
                generatedText: string.Empty,
                archivedGenerationStale: false,
                status: DiaryGenerationStatus.Failed,
                fallbackText: string.Empty);
            AssertTrue("failed page without raw text stays unarchiveable", !failedWithoutFallback.CanArchive);

            AssertTrue(
                "active scan undisplayable page stays hot",
                !DiaryArchiveEligibility.ShouldDropColdUndisplayableRef(
                    archivedForScans: false,
                    titlePending: false,
                    status: DiaryGenerationStatus.NotGenerated,
                    generatedText: string.Empty,
                    archivedGenerationStale: false));
            AssertTrue(
                "cold prompt-only page stays hot for dev export",
                !DiaryArchiveEligibility.ShouldDropColdUndisplayableRef(
                    archivedForScans: true,
                    titlePending: false,
                    status: DiaryGenerationStatus.PromptOnly,
                    generatedText: string.Empty,
                    archivedGenerationStale: false));
            AssertTrue(
                "cold title-pending page stays hot",
                !DiaryArchiveEligibility.ShouldDropColdUndisplayableRef(
                    archivedForScans: true,
                    titlePending: true,
                    status: DiaryGenerationStatus.NotGenerated,
                    generatedText: string.Empty,
                    archivedGenerationStale: false));
            AssertTrue(
                "cold blank undisplayable page drops hot ref",
                DiaryArchiveEligibility.ShouldDropColdUndisplayableRef(
                    archivedForScans: true,
                    titlePending: false,
                    status: DiaryGenerationStatus.NotGenerated,
                    generatedText: string.Empty,
                    archivedGenerationStale: false));
        }

        private static void TestArchivedPendingReloadFallbackStatus()
        {
            string loadedStatus = DiaryGenerationStatus.NormalizeLoadedMainStatus(
                DiaryGenerationStatus.Pending,
                string.Empty);

            AssertEqual("pending status resets on load", DiaryGenerationStatus.NotGenerated, loadedStatus);
            AssertTrue(
                "load-normalized attempted archived page stays visible",
                DiaryGenerationStatus.IsArchivedGenerationStale(
                    archivedForScans: true,
                    status: loadedStatus,
                    generatedText: string.Empty,
                    prompt: "event: argument\nwhat happened: Alice insulted Bob."));
            AssertTrue(
                "never-attempted archived page stays hidden",
                !DiaryGenerationStatus.IsArchivedGenerationStale(
                    archivedForScans: true,
                    status: loadedStatus,
                    generatedText: string.Empty,
                    prompt: string.Empty));
            AssertTrue(
                "hot pending page remains active writing",
                !DiaryGenerationStatus.IsArchivedGenerationStale(
                    archivedForScans: false,
                    status: DiaryGenerationStatus.Pending,
                    generatedText: string.Empty,
                    prompt: "event: argument"));
            AssertTrue(
                "completed archived page uses generated text",
                !DiaryGenerationStatus.IsArchivedGenerationStale(
                    archivedForScans: true,
                    status: DiaryGenerationStatus.Complete,
                    generatedText: "I wrote the page.",
                    prompt: "event: argument"));
        }

        private static void TestCombatPromptPlan()
        {
            DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = PairPayload("e-combat", "social exchange", "Alice swung a knife at Bob.", "Bob ducked."),
                policy = Policy(combat: true, important: true),
                povRole = DiaryPipelineRoles.Initiator,
                personaVoiceBlock = "Write like Alice.",
                promptEnchantment = "Her scar aches.",
                directSpeechInstruction = "Use direct speech only for exact lines.",
                maxTokens = 40
            });

            AssertEqual("combat template", DiaryPipelineTemplates.PairCombat, plan.templateKey);
            AssertEqual("combat system", "System PairCombat\n\nWrite like Alice.", plan.systemPrompt);
            AssertContains("combat user event", plan.userPrompt, "event: social exchange");
            AssertContains("combat user weapon", plan.userPrompt, "weapon: knife");
            AssertContains("combat direct speech instruction", plan.userPrompt, "Use direct speech only for exact lines.");
            AssertEqual("combat rule role", DiaryPipelineRoles.Initiator, plan.responseRules.targetRole);
            AssertEqual("combat max tokens", 40, plan.responseRules.maxTokens);
        }

        private static void TestSoloPromptPlan()
        {
            DiaryPolicySnapshot policy = Policy(combat: false, important: true);
            policy.group.forcedModelName = "story-model";
            DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = SoloPayload("e-solo", "quiet work", "Alice repaired the generator alone."),
                policy = policy,
                povRole = DiaryPipelineRoles.Initiator,
                personaVoiceBlock = "Write like Alice.",
                promptEnchantment = "Her hands shake.",
                directSpeechInstruction = "Use direct speech only for exact lines.",
                maxTokens = 30
            });

            AssertEqual("solo important template", DiaryPipelineTemplates.SoloImportant, plan.templateKey);
            AssertEqual("solo system includes persona", "System SoloImportant\n\nWrite like Alice.", plan.systemPrompt);
            AssertContains("solo pov", plan.userPrompt, "pov: Alice");
            AssertContains("solo text", plan.userPrompt, "what happened: Alice repaired the generator alone.");
            AssertContains("solo event prompt", plan.userPrompt, "event prompt: Write this event type.");
            AssertContains("solo event enhancement", plan.userPrompt, "event enhancement: Keep the event focused.");
            AssertContains("solo context", plan.userPrompt, "important context: Her hands shake.");
            AssertContains("solo previous ending", plan.userPrompt,
                "previous diary ending (continue from this): I stopped before the door. The rain was still coming down.");
            AssertEqual("solo rule role", DiaryPipelineRoles.Initiator, plan.responseRules.targetRole);
            AssertEqual("solo forced model carried", "story-model", plan.forcedModelName);
            AssertTrue("solo forced model not prompt text", !plan.userPrompt.Contains("story-model"));
        }

        private static void TestOwnedPromptTextIsNotSentenceCapped()
        {
            DiaryPolicySnapshot policy = Policy(combat: false, important: true);
            policy.group.eventPrompt = "Owned prompt one. Owned prompt two. Owned prompt three.";
            policy.group.eventEnhancement = "Owned enhancement one. Owned enhancement two. Owned enhancement three.";
            DiaryTemplatePolicy template = policy.Template(DiaryPipelineTemplates.SoloImportant);
            template.finalInstruction = "Owned final one. Owned final two. Owned final three.";

            DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = SoloPayload("e-owned-prompt", "quiet work", "Alice repaired the generator alone."),
                policy = policy,
                povRole = DiaryPipelineRoles.Initiator,
                personaVoiceBlock = "Owned style one. Owned style two. Owned style three.",
                maxTokens = 30
            });

            AssertContains("owned event prompt not capped", plan.userPrompt, "Owned prompt three.");
            AssertContains("owned event enhancement not capped", plan.userPrompt, "Owned enhancement three.");
            AssertContains("owned final instruction not capped", plan.userPrompt, "Owned final three.");
            AssertContains("owned persona block not capped", plan.systemPrompt, "Owned style three.");
        }

        private static void TestPromptTemplatePolicyReachesPromptPlan()
        {
            DiaryPolicySnapshot soloPolicy = Policy(combat: false, important: true);
            DiaryTemplatePolicy soloTemplate = soloPolicy.Template(DiaryPipelineTemplates.SoloImportant);
            soloTemplate.systemPrompt = "Override system prompt.";
            soloTemplate.finalInstruction = "Override final instruction.";
            soloTemplate.fields = Fields(Field("override pov", "PovName"));

            DiaryPromptPlan soloPlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = SoloPayload("e-template-override", "quiet work", "Alice repaired the generator alone."),
                policy = soloPolicy,
                povRole = DiaryPipelineRoles.Initiator,
                personaVoiceBlock = "Override voice.",
                maxTokens = 30
            });

            AssertEqual("template override system reaches system prompt",
                "Override system prompt.\n\nOverride voice.",
                soloPlan.systemPrompt);
            AssertContains("template override field label reaches user prompt", soloPlan.userPrompt, "override pov: Alice");
            AssertTrue("template override field list replaces default field list",
                !soloPlan.userPrompt.Contains("what happened: Alice repaired the generator alone."));
            AssertContains("template override final instruction reaches user prompt",
                soloPlan.userPrompt,
                "Override final instruction.");

            DiaryPolicySnapshot pairPolicy = Policy(combat: false, important: true);
            DiaryTemplatePolicy pairTemplate = pairPolicy.Template(DiaryPipelineTemplates.PairImportant);
            pairTemplate.recipientFinalInstruction = "Override recipient follow-up.";

            DiaryPromptPlan recipientPlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = PairPayload("e-recipient-template-override", "social exchange", "Alice spoke.", "Bob listened."),
                policy = pairPolicy,
                povRole = DiaryPipelineRoles.Recipient,
                priorInitiatorEntry = "Alice already wrote her side.",
                maxTokens = 30
            });

            AssertContains("template override recipient instruction reaches recipient prompt",
                recipientPlan.userPrompt,
                "Override recipient follow-up.");
        }

        private static void TestQuestPromptPlanFields()
        {
            DiaryEventPayload payload = SoloPayload(
                "e-quest",
                "quest",
                "Alice accepted a quest: Opportunity Friendlies.");
            payload.gameContext = "quest=OpportunityQuest_Friendlies; signal=accepted; label=Opportunity Friendlies; faction=Outlander; rewards=Silver x100"
                + "; quest_label=Opportunity Friendlies; quest_signal=accepted; quest_faction=Outlander; quest_rewards=Silver x100";

            DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = payload,
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Initiator,
                personaVoiceBlock = "Write like Alice.",
                maxTokens = 30
            });

            AssertEqual("quest uses important solo template", DiaryPipelineTemplates.SoloImportant, plan.templateKey);
            AssertContains("quest prompt name field", plan.userPrompt, "quest name: Opportunity Friendlies");
            AssertContains("quest prompt lifecycle field", plan.userPrompt, "quest lifecycle: accepted");
            AssertContains("quest prompt faction field", plan.userPrompt, "quest faction: Outlander");
            AssertContains("quest prompt rewards field", plan.userPrompt, "quest rewards: Silver x100");
            AssertTrue("quest raw defName is not rendered", !plan.userPrompt.Contains("OpportunityQuest_Friendlies"));
        }

        private static void TestProgressionPromptPlanFields()
        {
            DiaryEventPayload payload = SoloPayload(
                "e-progression",
                "skill milestone",
                "Alice reached Construction 12, one of her passion skills.");
            payload.gameContext = "progression=SkillMilestone; progression_kind=skill; skill=Construction; skill_level=12; previous_skill_milestone=8; passion=major";

            DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = payload,
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Initiator,
                personaVoiceBlock = "Write like Alice.",
                maxTokens = 30
            });

            AssertEqual("progression uses important solo template", DiaryPipelineTemplates.SoloImportant, plan.templateKey);
            AssertContains("progression kind prompt field", plan.userPrompt, "progression kind: skill");
            AssertContains("progression skill prompt field", plan.userPrompt, "skill: Construction");
            AssertContains("progression level prompt field", plan.userPrompt, "skill level: 12");
            AssertContains("progression passion prompt field", plan.userPrompt, "passion: major");
        }

        private static void TestDualPovPromptPlans()
        {
            DiaryEventPayload payload = PairPayload("e-dual", "argument", "Alice accused Bob of stealing medicine.", "Bob denied it.");

            DiaryPromptPlan initiator = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = payload,
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Initiator,
                personaVoiceBlock = "Write like Alice.",
                maxTokens = 35
            });

            DiaryPromptPlan recipient = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = payload,
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Recipient,
                personaVoiceBlock = "Write like Bob.",
                priorInitiatorEntry = "I knew Bob had taken it.",
                maxTokens = 35
            });

            AssertEqual("dual initiator template", DiaryPipelineTemplates.PairImportant, initiator.templateKey);
            AssertContains("dual initiator pov", initiator.userPrompt, "pov: Alice");
            AssertContains("dual initiator text", initiator.userPrompt, "what happened: Alice accused Bob of stealing medicine.");
            AssertEqual("dual initiator rules", DiaryPipelineRoles.Initiator, initiator.responseRules.targetRole);

            AssertEqual("dual recipient template", DiaryPipelineTemplates.PairImportant, recipient.templateKey);
            AssertContains("dual recipient pov", recipient.userPrompt, "pov: Bob");
            AssertContains("dual recipient text", recipient.userPrompt, "what happened: Bob denied it.");
            AssertContains("dual recipient context", recipient.userPrompt, "initiator entry: I knew Bob had taken it.");
            AssertEqual("dual recipient rules", DiaryPipelineRoles.Recipient, recipient.responseRules.targetRole);
        }

        private static void TestRecipientFollowupPlan()
        {
            DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = PairPayload("e-recipient", "argument", "Alice insulted Bob.", "Bob glared at Alice."),
                policy = Policy(combat: false, important: false),
                povRole = DiaryPipelineRoles.Recipient,
                priorInitiatorEntry = "Alice wrote first.",
                directSpeechInstruction = "Keep quoted speech brief.",
                maxTokens = 32
            });

            AssertEqual("recipient default template", DiaryPipelineTemplates.PairDefault, plan.templateKey);
            AssertContains("recipient followup entry", plan.userPrompt, "initiator entry: Alice wrote first.");
            AssertContains("recipient followup instruction", plan.userPrompt, "Recipient followup. Keep quoted speech brief.");
        }

        private static void TestNeutralGenerationPlans()
        {
            DiaryPromptPlan death = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = DeathPayload(),
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Neutral,
                personaVoiceBlock = "This must not appear.",
                promptEnchantment = "This must not appear.",
                maxTokens = 45
            });

            DiaryPromptPlan arrival = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = ArrivalPayload(),
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Neutral,
                personaVoiceBlock = "This must not appear.",
                promptEnchantment = "This must not appear.",
                maxTokens = 45
            });

            AssertEqual("death template", DiaryPipelineTemplates.DeathDescription, death.templateKey);
            AssertEqual("death system excludes persona", "System DeathDescription", death.systemPrompt);
            AssertContains("death neutral text", death.userPrompt, "what happened: Alice died from blood loss.");
            AssertContains("death victim", death.userPrompt, "deceased: Alice");
            AssertContains("death facts", death.userPrompt, "death facts: damage=Cut; weapon=knife; surroundings=near the freezer");
            AssertContains("death pawn summary", death.userPrompt, "deceased pawn: Alice was the colony doctor.");
            AssertEqual("death neutral role", DiaryPipelineRoles.Neutral, death.responseRules.targetRole);

            AssertEqual("arrival template", DiaryPipelineTemplates.ArrivalDescription, arrival.templateKey);
            AssertEqual("arrival system excludes persona", "System ArrivalDescription", arrival.systemPrompt);
            AssertContains("arrival neutral text", arrival.userPrompt, "what happened: Alice joined the colony from a crash pod.");
            AssertContains("arrival pawn", arrival.userPrompt, "colonist: Alice");
            AssertContains("arrival facts", arrival.userPrompt, "arrival facts: source=game_start; scenario=Crashlanded; scenario detail=The three survivors awoke among wreckage; childhood=Urbworld urchin; childhood description=Alice grew up among alley markets. She learned whom to trust and when to run.; childhood effects=skill bonuses: Social +3, Melee +2 | disabled work tags: Intellectual; adulthood=Field medic; adulthood description=Alice spent years patching workers after industrial accidents. The sound of alarms became routine.; adulthood effects=skill bonuses: Medical +6 | disabled work: Artistic; surroundings=rainy field");
            AssertContains("arrival pawn summary", arrival.userPrompt, "colonist pawn: Alice was a careful doctor.");
            AssertEqual("arrival neutral role", DiaryPipelineRoles.Neutral, arrival.responseRules.targetRole);
        }

        private static void TestTitlePromptPlan()
        {
            DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = PairPayload("e-title", "rescue", "Alice rescued Bob.", "Bob survived."),
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Initiator,
                titleRequest = true,
                personaVoiceBlock = "This should not appear.",
                entryText = "I pulled Bob out of the smoke.",
                maxTokens = 8
            });

            AssertEqual("title template", DiaryPipelineTemplates.Title, plan.templateKey);
            AssertEqual("title system excludes persona", "Title system", plan.systemPrompt);
            AssertEqual("title user", "entry: I pulled Bob out of the smoke.\n\nMake title.", plan.userPrompt);
            AssertTrue("title rule", plan.responseRules.isTitle);
            AssertTrue("title keeps fragments", !plan.responseRules.trimIncompleteSentence);
            AssertEqual("title tokens", 8, plan.responseRules.maxTokens);
        }

        private static void TestSoloSelection()
        {
            DiaryEventPayload thoughtPayload = new DiaryEventPayload
            {
                eventId = "e-thought",
                solo = true,
                eventNoun = "thought",
                gameContext = "thought=ate without table",
                initiator = new DiaryPovPayload
                {
                    role = DiaryPipelineRoles.Initiator,
                    name = "Alice",
                    rawText = "Alice remembered the bad table."
                }
            };

            DiaryPromptPlan thoughtPlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = thoughtPayload,
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Initiator
            });
            AssertEqual("internal state selection", DiaryPipelineTemplates.SoloInternalState, thoughtPlan.templateKey);

            thoughtPayload.dayReflection = true;
            DiaryPromptPlan reflectionPlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = thoughtPayload,
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Initiator
            });
            AssertEqual("reflection wins over internal state", DiaryPipelineTemplates.SoloDayReflection, reflectionPlan.templateKey);

            thoughtPayload.quadrumReflection = true;
            DiaryPromptPlan quadrumPlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = thoughtPayload,
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Initiator
            });
            AssertEqual("quadrum reflection wins over day reflection", DiaryPipelineTemplates.SoloQuadrumReflection, quadrumPlan.templateKey);
            AssertEqual("quadrum reflection template token cap", 350, quadrumPlan.responseRules.maxTokens);

            thoughtPayload.arcReflection = true;
            DiaryPromptPlan arcPlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = thoughtPayload,
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Initiator
            });
            AssertEqual("arc reflection wins over quadrum reflection", DiaryPipelineTemplates.SoloArcReflection, arcPlan.templateKey);
            AssertEqual("arc reflection template token cap", 420, arcPlan.responseRules.maxTokens);
        }

        private static void TestSoloBatchSelection()
        {
            DiaryEventPayload quietBatchPayload = SoloPayload("e-batch", "passing conversations",
                "Several conversations colored the day.");
            quietBatchPayload.gameContext = "batch=ambient_day_note; events=4";
            DiaryPromptPlan quietBatchPlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = quietBatchPayload,
                policy = Policy(combat: false, important: false),
                povRole = DiaryPipelineRoles.Initiator
            });
            AssertEqual("solo noncombat batch selection", DiaryPipelineTemplates.SoloBatched, quietBatchPlan.templateKey);

            DiaryEventPayload combatBatchPayload = SoloPayload("e-combat-batch", "combat aftermath",
                "Several combat moments landed.");
            combatBatchPayload.gameContext = "tale=TaleCombatBatch; batch=tale; events=4";
            DiaryPromptPlan combatBatchPlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = combatBatchPayload,
                policy = Policy(combat: true, important: true),
                povRole = DiaryPipelineRoles.Initiator
            });
            AssertEqual("solo combat batch selection", DiaryPipelineTemplates.SoloImportant, combatBatchPlan.templateKey);

            DiaryEventPayload singleFlushedInteraction = PairPayload("e-single-flush", "social exchange",
                "Alice snapped at Bob.", "Bob went quiet.");
            singleFlushedInteraction.gameContext = "group=insults; events=1; first_tick=100; last_tick=100";
            DiaryPromptPlan singleFlushPlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = singleFlushedInteraction,
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Initiator
            });
            AssertEqual("single flushed interaction stays standalone",
                DiaryPipelineTemplates.PairImportant, singleFlushPlan.templateKey);
        }

        private static void TestPromptEnchantmentPlanner()
        {
            PromptEnchantmentTuning tuning = new PromptEnchantmentTuning { maxImpactCues = 2 };
            List<PromptEnchantmentCandidate> candidates = new List<PromptEnchantmentCandidate>
            {
                PromptCandidate(
                    "urgent",
                    "major cut on hand",
                    1f,
                    new string[] { "bleeding", "pain", "weakens body" },
                    new string[] { "keep it physical", "avoid medical dump", "third cue" }),
                PromptCandidate(
                    "important",
                    "royal title: yeoman",
                    3f,
                    null,
                    new string[] { "respect if relevant" })
            };

            AssertEqual(
                "prompt enchantment first roll",
                "urgent; major cut on hand; bleeding, pain; keep it physical, avoid medical dump",
                PromptEnchantmentPlanner.Build(candidates, tuning, 0f));
            AssertEqual(
                "prompt enchantment boundary roll",
                "urgent; major cut on hand; bleeding, pain; keep it physical, avoid medical dump",
                PromptEnchantmentPlanner.Build(candidates, tuning, 0.25f));
            AssertEqual(
                "prompt enchantment weighted second",
                "important; royal title: yeoman; respect if relevant",
                PromptEnchantmentPlanner.Build(candidates, tuning, 0.26f));

            AssertEqual(
                "prompt enchantment skips nonpositive",
                "valid; condition",
                PromptEnchantmentPlanner.Build(
                    new List<PromptEnchantmentCandidate>
                    {
                        PromptCandidate("ignored", "zero", 0f, null, null),
                        PromptCandidate("valid", "condition", 2f, null, null)
                    },
                    tuning,
                    0f));
            AssertEqual(
                "prompt enchantment empty",
                string.Empty,
                PromptEnchantmentPlanner.Build(new List<PromptEnchantmentCandidate>(), tuning, 0.5f));
            AssertEqual(
                "prompt enchantment zero cue cap",
                "urgent; major cut on hand",
                PromptEnchantmentPlanner.Build(
                    new List<PromptEnchantmentCandidate> { candidates[0] },
                    new PromptEnchantmentTuning { maxImpactCues = 0 },
                    0f));

            List<PromptEnchantmentCandidate> filtered = PromptEnchantmentPlanner.WithoutSuppressedHediffSources(
                new List<PromptEnchantmentCandidate>
                {
                    PromptCandidate(
                        "persona-covered",
                        "inhumanized",
                        5f,
                        null,
                        null,
                        "Inhumanized"),
                    PromptCandidate(
                        "kept",
                        "flu",
                        1f,
                        null,
                        null,
                        "Flu"),
                    PromptCandidate(
                        "status",
                        "royal title",
                        1f,
                        null,
                        null)
                },
                new List<string> { "inhumanized" });
            AssertEqual("prompt enchantment suppressed source count", 2, filtered.Count);
            AssertEqual("prompt enchantment kept unsuppressed source", "Flu", filtered[0].sourceHediffDefName);
            AssertEqual("prompt enchantment kept non-hediff source", string.Empty, filtered[1].sourceHediffDefName ?? string.Empty);

            List<PromptEnchantmentCandidate> normal = new List<PromptEnchantmentCandidate>
            {
                PromptCandidate("covered", "already voiced", 5f, null, null, "Inhumanized"),
                PromptCandidate("normal", "ordinary pain", 4f, null, null, "Flu")
            };
            List<PromptEnchantmentCandidate> extra = new List<PromptEnchantmentCandidate>
            {
                PromptCandidate("event-window", "gray flesh found", 7f, null, null),
                null
            };
            List<PromptEnchantmentCandidate> prepared = PromptEnchantmentPlanner.PrepareCandidatesForBuild(
                normal,
                extra,
                0.25f,
                new List<string> { "inhumanized" });
            AssertEqual("prepared candidates suppress covered normal candidate", 2, prepared.Count);
            AssertEqual("prepared candidates keep normal first", "Flu", prepared[0].sourceHediffDefName);
            AssertNear("prepared candidates multiply normal weight", 1f, prepared[0].weight);
            AssertEqual("prepared candidates append extra unchanged", "event-window", prepared[1].priorityText);
            AssertNear("prepared candidates do not multiply extra weight", 7f, prepared[1].weight);

            List<PromptEnchantmentCandidate> zeroMultiplier = PromptEnchantmentPlanner.PrepareCandidatesForBuild(
                new List<PromptEnchantmentCandidate> { PromptCandidate("normal", "ordinary pain", 4f, null, null, "Flu") },
                new List<PromptEnchantmentCandidate> { PromptCandidate("extra", "condition", 3f, null, null) },
                0f,
                null);
            AssertEqual("zero multiplier keeps normal candidate in exported pool", 2, zeroMultiplier.Count);
            AssertNear("zero multiplier makes normal candidate unpickable", 0f, zeroMultiplier[0].weight);
            AssertNear("zero multiplier preserves extra candidate weight", 3f, zeroMultiplier[1].weight);
        }

        private static void TestListenerRegistry()
        {
            ListenerRegistry<string> registry = new ListenerRegistry<string>(2);
            List<string> calls = new List<string>();
            List<string> failures = new List<string>();

            AssertTrue("listener registry rejects blank id", !registry.Register(" ", payload => calls.Add(payload)));
            AssertTrue("listener registry rejects null listener", !registry.Register("a", null));
            AssertTrue("listener registry accepts first listener", registry.Register("a", payload => calls.Add("a:" + payload)));
            AssertTrue("listener registry accepts second listener", registry.Register("b", payload => calls.Add("b:" + payload)));
            AssertTrue("listener registry cap rejects third id", !registry.Register("c", payload => calls.Add("c:" + payload)));

            AssertEqual("listener registry delivers in order", 2, registry.Notify("one", null));
            AssertEqual("listener registry first call order", "a:one,b:one", string.Join(",", calls.ToArray()));

            AssertTrue("listener registry replaces existing id beyond cap",
                registry.Register("a", payload => calls.Add("a2:" + payload)));
            calls.Clear();
            AssertEqual("listener registry replacement keeps order", 2, registry.Notify("two", null));
            AssertEqual("listener registry replacement output", "a2:two,b:two", string.Join(",", calls.ToArray()));

            AssertTrue("listener registry unregister removes id", registry.Unregister("b"));
            calls.Clear();
            AssertEqual("listener registry notifies remaining after unregister", 1, registry.Notify("three", null));
            AssertEqual("listener registry remaining output", "a2:three", string.Join(",", calls.ToArray()));
            AssertTrue("listener registry can reuse slot after unregister",
                registry.Register("c", payload => calls.Add("c:" + payload)));

            AssertTrue("listener registry throwing replacement accepted",
                registry.Register("c", payload => { throw new InvalidOperationException("boom"); }));
            calls.Clear();
            AssertEqual("listener registry disables throwing listener", 1,
                registry.Notify("four", (id, exception) => failures.Add(id + ":" + exception.GetType().Name)));
            AssertEqual("listener registry reports one failure", "c:InvalidOperationException",
                string.Join(",", failures.ToArray()));
            AssertEqual("listener registry skips disabled listener later", 1, registry.Notify("five", null));
        }

        // DiaryPromptEnchantmentCandidateSnapshot.From is the internal mapping point between the
        // PromptEnchantment machinery and the public integration DTO (API v6, C-CTX-3). Cover it
        // directly so the contract holds without loading RimWorld: null input, field copy, list
        // independence (the snapshot must not alias the source's lists), and order/weight preserved
        // across a multi-candidate mapping.
        private static void TestPromptEnchantmentCandidateSnapshot()
        {
            AssertTrue("snapshot From(null) returns null", DiaryPromptEnchantmentCandidateSnapshot.From(null) == null);

            PromptEnchantmentCandidate source = PromptCandidate(
                "high-priority context",
                "in agony (left leg)",
                3.5f,
                new[] { "life-threatening", "heavy bleeding" },
                new[] { "description: a deep wound" },
                "GunshotWound");

            DiaryPromptEnchantmentCandidateSnapshot snapshot = DiaryPromptEnchantmentCandidateSnapshot.From(source);
            AssertTrue("snapshot From populates the DTO", snapshot != null);
            AssertNear("snapshot carries weight", 3.5f, snapshot.weight);
            AssertEqual("snapshot carries source hediff def name", "GunshotWound", snapshot.sourceHediffDefName);
            AssertEqual("snapshot carries priority text", "high-priority context", snapshot.priorityText);
            AssertEqual("snapshot carries condition text", "in agony (left leg)", snapshot.conditionText);
            AssertEqual("snapshot carries impact cues", 2, snapshot.impactCues.Count);
            AssertEqual("snapshot impact cue 0", "life-threatening", snapshot.impactCues[0]);
            AssertEqual("snapshot impact cue 1", "heavy bleeding", snapshot.impactCues[1]);
            AssertEqual("snapshot carries configured cues", 1, snapshot.configuredCues.Count);
            AssertEqual("snapshot configured cue 0", "description: a deep wound", snapshot.configuredCues[0]);

            // The snapshot's lists must be independent copies: mutating the source after From() must
            // not change the snapshot the caller already holds. This is the additive-only stability
            // promise for the integration surface.
            source.impactCues.Add("late cue");
            source.configuredCues.Clear();
            AssertEqual("snapshot impact cues stay independent after source mutation", 2, snapshot.impactCues.Count);
            AssertEqual("snapshot configured cues stay independent after source clear", 1, snapshot.configuredCues.Count);

            // A null sourceHediffDefName / null cue list must not throw nor leak nulls into the DTO.
            PromptEnchantmentCandidate nullish = new PromptEnchantmentCandidate
            {
                weight = 1f,
                sourceHediffDefName = null,
                priorityText = null,
                conditionText = null,
                impactCues = null,
                configuredCues = null
            };
            DiaryPromptEnchantmentCandidateSnapshot nullishSnapshot = DiaryPromptEnchantmentCandidateSnapshot.From(nullish);
            AssertTrue("snapshot From tolerates null fields", nullishSnapshot != null);
            AssertEqual("snapshot From nulls become empty strings", string.Empty, nullishSnapshot.sourceHediffDefName);
            AssertEqual("snapshot From null cue list becomes empty list", 0, nullishSnapshot.impactCues.Count);
            AssertEqual("snapshot From null configured cue list becomes empty list", 0, nullishSnapshot.configuredCues.Count);

            // Order and weight are preserved across multiple candidates — the export is a 1:1 mirror
            // of the collected set, not a re-sorted or deduplicated view.
            List<PromptEnchantmentCandidate> candidates = new List<PromptEnchantmentCandidate>
            {
                PromptCandidate("first", "a", 1f, null, null, "HediffA"),
                PromptCandidate("second", "b", 2f, null, null, "HediffB"),
                PromptCandidate("third", "c", 0.5f, null, null, "HediffC")
            };
            List<DiaryPromptEnchantmentCandidateSnapshot> snapshots = new List<DiaryPromptEnchantmentCandidateSnapshot>();
            for (int i = 0; i < candidates.Count; i++)
            {
                DiaryPromptEnchantmentCandidateSnapshot mapped = DiaryPromptEnchantmentCandidateSnapshot.From(candidates[i]);
                if (mapped != null)
                {
                    snapshots.Add(mapped);
                }
            }

            AssertEqual("snapshot list preserves order and count", 3, snapshots.Count);
            AssertEqual("snapshot list entry 0 source", "HediffA", snapshots[0].sourceHediffDefName);
            AssertEqual("snapshot list entry 1 source", "HediffB", snapshots[1].sourceHediffDefName);
            AssertNear("snapshot list entry 2 weight", 0.5f, snapshots[2].weight);
        }

        private static void TestPromptEnchantmentDecayPolicy()
        {
            AssertNear(
                "prompt decay disabled stays full strength",
                1f,
                PromptEnchantmentDecayPolicy.AgeFactor(1000, 0, 0, 0.2f));
            AssertNear(
                "prompt decay starts full strength",
                1f,
                PromptEnchantmentDecayPolicy.AgeFactor(1000, 1000, 60000, 0.2f));
            AssertNear(
                "prompt decay interpolates toward floor",
                0.6f,
                PromptEnchantmentDecayPolicy.AgeFactor(30000, 0, 60000, 0.2f));
            AssertNear(
                "prompt decay reaches floor",
                0.2f,
                PromptEnchantmentDecayPolicy.AgeFactor(90000, 0, 60000, 0.2f));
            AssertNear(
                "prompt decay lowers weight",
                6f,
                PromptEnchantmentDecayPolicy.DecayedWeight(10f, 0.6f));
            AssertNear(
                "prompt decay relaxes normal suppression toward one",
                0.4f,
                PromptEnchantmentDecayPolicy.RelaxedNormalMultiplier(0f, 0.6f));
            AssertNear(
                "prompt decay relaxes normal amplification toward one",
                1.6f,
                PromptEnchantmentDecayPolicy.RelaxedNormalMultiplier(2f, 0.6f));
        }

        private static void TestHediffPersonaOverridePolicy()
        {
            List<HediffPersonaOverrideFact> hediffs = new List<HediffPersonaOverrideFact>
            {
                new HediffPersonaOverrideFact
                {
                    defName = "Inhumanized",
                    label = "inhumanized",
                    severity = 0f,
                    visible = false
                },
                new HediffPersonaOverrideFact
                {
                    defName = "Flu",
                    label = "flu",
                    severity = 0.42f,
                    visible = true
                }
            };

            AssertEqual(
                "hediff persona override can include hidden hediff",
                "DiaryPersona_InhumanizedVoid",
                HediffPersonaOverridePolicy.SelectPersonaDefName(
                    new List<HediffPersonaOverrideRule>
                    {
                        new HediffPersonaOverrideRule
                        {
                            personaDefName = "DiaryPersona_InhumanizedVoid",
                            visibleOnly = false,
                            hediffDefNames = new List<string> { "Inhumanized" }
                        }
                    },
                    hediffs));

            AssertEqual(
                "hediff persona override visible-only rejects hidden hediff",
                string.Empty,
                HediffPersonaOverridePolicy.SelectPersonaDefName(
                    new List<HediffPersonaOverrideRule>
                    {
                        new HediffPersonaOverrideRule
                        {
                            personaDefName = "DiaryPersona_InhumanizedVoid",
                            visibleOnly = true,
                            hediffDefNames = new List<string> { "Inhumanized" }
                        }
                    },
                    hediffs));

            AssertEqual(
                "hediff persona override higher priority wins",
                "DiaryPersona_HighPriority",
                HediffPersonaOverridePolicy.SelectPersonaDefName(
                    new List<HediffPersonaOverrideRule>
                    {
                        new HediffPersonaOverrideRule
                        {
                            priority = 1,
                            personaDefName = "DiaryPersona_LowPriority",
                            hediffDefNames = new List<string> { "Flu" }
                        },
                        new HediffPersonaOverrideRule
                        {
                            priority = 10,
                            personaDefName = "DiaryPersona_HighPriority",
                            hediffDefNameContains = new List<string> { "fl" },
                            minSeverity = 0.25f
                        }
                    },
                    hediffs));

            HediffPersonaOverrideSelection selectedOverride = HediffPersonaOverridePolicy.SelectOverride(
                new List<HediffPersonaOverrideRule>
                {
                    new HediffPersonaOverrideRule
                    {
                        priority = 1,
                        personaDefName = "DiaryPersona_LowPriority",
                        hediffDefNames = new List<string> { "Flu" }
                    },
                    new HediffPersonaOverrideRule
                    {
                        priority = 10,
                        personaDefName = "DiaryPersona_InhumanizedVoid",
                        visibleOnly = false,
                        hediffDefNames = new List<string> { "Inhumanized" }
                    }
                },
                new List<HediffPersonaOverrideFact>
                {
                    hediffs[0],
                    new HediffPersonaOverrideFact
                    {
                        defName = "Inhumanized",
                        label = "inhumanized duplicate",
                        severity = 0f,
                        visible = false
                    },
                    hediffs[1]
                });
            AssertEqual("hediff persona override selection persona", "DiaryPersona_InhumanizedVoid", selectedOverride.personaDefName);
            AssertEqual("hediff persona override matched source unique count", 2, selectedOverride.matchedHediffDefNames.Count);
            AssertEqual("hediff persona override suppresses lower-priority matched source", "Flu", selectedOverride.matchedHediffDefNames[0]);
            AssertEqual("hediff persona override suppresses selected matched source", "Inhumanized", selectedOverride.matchedHediffDefNames[1]);
        }

        private static void TestMemoryDecayXmlPolicy()
        {
            string[] memoryDecayHediffs = { "Alzheimers", "Dementia", "CrumblingMind" };
            XDocument overrides = XDocument.Load(RepoPath("1.6", "Defs", "DiaryHediffPersonaOverrideDefs.xml"));
            foreach (XElement def in overrides.Descendants("PawnDiary.DiaryHediffPersonaOverrideDef"))
            {
                string defName = ChildValue(def, "defName");
                for (int i = 0; i < memoryDecayHediffs.Length; i++)
                {
                    AssertTrue(
                        "memory decay is not a persona override: " + defName + " excludes " + memoryDecayHediffs[i],
                        !HasHediffDefName(def, memoryDecayHediffs[i]));
                }
            }

            XDocument enchantments = XDocument.Load(RepoPath("1.6", "Defs", "DiaryPromptEnchantmentDefs.xml"));
            XElement memoryDecay = FindDef(enchantments, "PawnDiary.DiaryPromptEnchantmentDef", "DiaryEnchant_MemoryDecay");
            AssertTrue("memory decay prompt enchantment exists", memoryDecay != null);
            if (memoryDecay == null)
            {
                return;
            }

            for (int i = 0; i < memoryDecayHediffs.Length; i++)
            {
                AssertTrue(
                    "memory decay prompt enchantment includes " + memoryDecayHediffs[i],
                    HasHediffDefName(memoryDecay, memoryDecayHediffs[i]));
            }
        }

        private static void TestObservedConditionDecayXmlPolicy()
        {
            XDocument observedConditions = XDocument.Load(RepoPath("1.6", "Defs", "DiaryObservedConditionDefs.xml"));
            XElement grayFlesh = FindDef(
                observedConditions,
                "PawnDiary.DiaryObservedConditionDef",
                "AnomalyGrayFleshEvidence");
            AssertTrue("gray flesh observed condition exists", grayFlesh != null);
            if (grayFlesh == null)
            {
                return;
            }

            AssertEqual("gray flesh max-active force stop", "120000", ChildValue(grayFlesh, "maxActiveTicks"));
            AssertEqual("gray flesh restart cooldown", "120000", ChildValue(grayFlesh, "restartCooldownTicks"));
            AssertEqual("gray flesh decay duration", "60000", ChildValue(grayFlesh, "promptDecayTicks"));
            AssertEqual("gray flesh decay floor", "0.15", ChildValue(grayFlesh, "promptDecayMinMultiplier"));
            AssertEqual("gray flesh evidence label hidden", "0", ChildValue(grayFlesh, "maxEvidenceLabels"));
            AssertTrue("gray flesh suppresses on metalhorror", HasListValue(grayFlesh, "suppressWhenThingDefNames", "Metalhorror"));

            // The revealed-threat handoff. MetalhorrorEmergence intentionally has NO maxActiveTicks /
            // restartCooldownTicks: its end trigger (the live Metalhorror ThingDef leaving listerThings) is
            // reliable, because a dead metalhorror becomes a Corpse_Entity (a different def) and stops
            // matching. That lets a multi-day rampage keep the override live until the metalhorror actually
            // dies. The cap-and-cooldown safety net stays on gray-flesh only, where samples can linger as
            // plain items. Regression guard for the "override cut off mid-rampage" regression.
            XElement metalhorror = FindDef(
                observedConditions,
                "PawnDiary.DiaryObservedConditionDef",
                "MetalhorrorEmergence");
            AssertTrue("metalhorror emergence observed condition exists", metalhorror != null);
            if (metalhorror != null)
            {
                AssertEqual("metalhorror emergence has NO artificial cap", string.Empty, ChildValue(metalhorror, "maxActiveTicks"));
                AssertEqual("metalhorror emergence has NO restart cooldown", string.Empty, ChildValue(metalhorror, "restartCooldownTicks"));
                AssertEqual("metalhorror emergence matches the live entity", "Metalhorror", ChildValue(metalhorror, "matchDefNames"));
            }

            // Post-emergence insurance: while any colonist still carries the hidden MetalhorrorImplant
            // hediff, a tone-only dread keeps coloring prompts even after the visible metalhorror is dead.
            // This is a MapHiddenHediff observer (senses hidden state as a map-level boolean) and, like the
            // emergence override, has NO cap — a cured-or-dead host's hediffSet is genuinely empty, so the
            // natural end trigger is reliable and the override must not be artificially cut off.
            XElement infection = FindDef(
                observedConditions,
                "PawnDiary.DiaryObservedConditionDef",
                "MetalhorrorInfection");
            AssertTrue("metalhorror infection observed condition exists", infection != null);
            if (infection != null)
            {
                AssertEqual("metalhorror infection uses the hidden-hediff observer", "MapHiddenHediff", ChildValue(infection, "observerType"));
                AssertEqual("metalhorror infection matches the implant hediff", "MetalhorrorImplant", ChildValue(infection, "matchDefNames"));
                AssertEqual("metalhorror infection is tone-only (no evidence label)", "0", ChildValue(infection, "maxEvidenceLabels"));
                AssertEqual("metalhorror infection has NO artificial cap", string.Empty, ChildValue(infection, "maxActiveTicks"));
            }

            XDocument englishKeyed = XDocument.Load(RepoPath("Languages", "English", "Keyed", "PawnDiary.xml"));
            string description = KeyedValue(
                englishKeyed,
                "PawnDiary.Prompt.ObservedCondition.AnomalyGrayFleshEvidence.Description");
            AssertTrue(
                "gray flesh prompt avoids item name",
                description.IndexOf("gray flesh", StringComparison.OrdinalIgnoreCase) < 0);
            AssertTrue(
                "gray flesh prompt names imitator fear",
                description.IndexOf("imitat", StringComparison.OrdinalIgnoreCase) >= 0);

            // The infection tone must never name the hediff/host (reveal-protection lives in the prompt
            // text, not just the empty evidence label), in BOTH shipped languages. The contract is
            // language-independent: whichever localization the LLM sees, it must not be told the mechanic.
            string infectionDescription = KeyedValue(
                englishKeyed,
                "PawnDiary.Prompt.ObservedCondition.MetalhorrorInfection.Description");
            AssertTrue(
                "infection prompt (EN) avoids naming the implant/host",
                infectionDescription.IndexOf("implant", StringComparison.OrdinalIgnoreCase) < 0
                    && infectionDescription.IndexOf("host", StringComparison.OrdinalIgnoreCase) < 0);
            XDocument russianKeyed = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "Keyed", "PawnDiary.xml"));
            string infectionDescriptionRu = KeyedValue(
                russianKeyed,
                "PawnDiary.Prompt.ObservedCondition.MetalhorrorInfection.Description");
            AssertTrue(
                "infection prompt has Russian mirror",
                !string.IsNullOrWhiteSpace(infectionDescriptionRu));
            AssertTrue(
                "infection prompt (RU) avoids naming the implant/host",
                infectionDescriptionRu.IndexOf("имплант", StringComparison.OrdinalIgnoreCase) < 0
                    && infectionDescriptionRu.IndexOf("носител", StringComparison.OrdinalIgnoreCase) < 0);
        }

        private static void TestColorCueXmlPolicy()
        {
            XDocument groups = XDocument.Load(RepoPath("1.6", "Defs", "DiaryInteractionGroupDefs.xml"));
            AssertEqual("anomalous body parts use their own cue", "bodyPartAnomalous",
                GroupColorCue(groups, "hediffPartGainedAnomalous"));
            AssertEqual("artificial body parts use their own cue", "bodyPartArtificial",
                GroupColorCue(groups, "hediffPartGainedArtificial"));
            AssertEqual("lost body parts use their own cue", "bodyPartLost",
                GroupColorCue(groups, "hediffPartLostNatural"));
            AssertEqual("psylink progression stays psychic", "psychic",
                GroupColorCue(groups, "progressionPsylink"));
            AssertEqual("psycast abilities stay psychic", "psychic",
                GroupColorCue(groups, "abilityPsycast"));
            AssertEqual("royal title progression stays royalty", "royalty",
                GroupColorCue(groups, "progressionRoyalTitle"));
            AssertEqual("royal rituals stay royalty", "royalty",
                GroupColorCue(groups, "ritualRoyal"));

            XDocument style = XDocument.Load(RepoPath("1.6", "Defs", "DiaryUiStyleDef.xml"));
            string[] expectedCues =
            {
                "bodyPartAnomalous",
                "bodyPartArtificial",
                "bodyPartLost",
                "psychic",
                "royalty"
            };
            for (int i = 0; i < expectedCues.Length; i++)
            {
                AssertTrue("UI style defines color cue " + expectedCues[i],
                    HasCueColor(style, expectedCues[i]));
            }
        }

        private static void TestPromptTextSanitizer()
        {
            AssertEqual(
                "external prompt text keeps first two sentences",
                "First sentence. Second sentence!",
                PromptTextSanitizer.LocalizedPromptText("First sentence. Second sentence! Third sentence?"));
            AssertEqual(
                "external prompt text guards line breaks and rich text",
                "First bold line. Second line.",
                PromptTextSanitizer.LocalizedPromptText("First <b>bold</b> line.\r\nSecond line.\nThird line."));
            AssertEqual(
                "external prompt text strips control characters inside line",
                "Alpha Beta. Gamma.",
                PromptTextSanitizer.LocalizedPromptText("Alpha\tBeta.\u0007Gamma. Delta."));
            AssertEqual(
                "external prompt text includes closing quote",
                "\"First.\" Second.",
                PromptTextSanitizer.LocalizedPromptText("\"First.\" Second. Third."));
            AssertEqual(
                "external prompt text handles sentence without terminal punctuation",
                "One long localized label without punctuation",
                PromptTextSanitizer.LocalizedPromptText("One long localized label without punctuation"));
        }

        private static void TestDiaryEntryTitleFilter()
        {
            DiaryEntryTitleFilterFacts facts = new DiaryEntryTitleFilterFacts
            {
                tick = 1200,
                date = "3rd of Aprimay, 5501",
                povRole = "initiator",
                domain = "External",
                atmosphereCue = "unsettled",
                sourceId = "adapter.mod",
                eventKey = "adapter_confession",
                partnerPawnId = "Thing_Pawn_2",
                important = true,
                hasTitle = true,
                hasGeneratedText = true,
                archived = false
            };

            AssertTrue("title filter null query matches", DiaryEntryTitleFilter.Matches(facts, null));
            AssertTrue("title filter empty query matches", DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery()));
            AssertTrue("title filter matches domain case-insensitive",
                DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { domain = "external" }));
            AssertTrue("title filter rejects wrong domain",
                !DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { domain = "Thought" }));
            AssertTrue("title filter matches atmosphere cue",
                DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { atmosphereCue = "Unsettled" }));
            AssertTrue("title filter rejects wrong atmosphere cue",
                !DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { atmosphereCue = "memorial" }));
            AssertTrue("title filter matches pov role",
                DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { povRole = "INITIATOR" }));
            AssertTrue("title filter rejects wrong pov role",
                !DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { povRole = "recipient" }));
            AssertTrue("title filter matches source id",
                DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { sourceId = "ADAPTER.MOD" }));
            AssertTrue("title filter rejects wrong source id",
                !DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { sourceId = "other.mod" }));
            AssertTrue("title filter matches event key",
                DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { eventKey = "Adapter_Confession" }));
            AssertTrue("title filter rejects wrong event key",
                !DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { eventKey = "adapter_other" }));
            AssertTrue("title filter matches partner pawn id",
                DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { partnerPawnId = "Thing_Pawn_2" }));
            AssertTrue("title filter rejects wrong partner pawn id",
                !DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { partnerPawnId = "Thing_Pawn_3" }));
            AssertTrue("title filter matches inclusive tick range",
                DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { minTick = 1200, maxTick = 1200 }));
            AssertTrue("title filter rejects below min tick",
                !DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { minTick = 1201 }));
            AssertTrue("title filter rejects above max tick",
                !DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { maxTick = 1199 }));
            AssertTrue("title filter matches date fragment case-insensitive",
                DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { dateContains = "aprimay" }));
            AssertTrue("title filter rejects missing date fragment",
                !DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { dateContains = "Jugust" }));
            AssertTrue("title filter excludes active entries when requested",
                !DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { includeActive = false }));
            AssertTrue("title filter matches important positive tri-state",
                DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { important = 1 }));
            AssertTrue("title filter rejects important false tri-state",
                !DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { important = 0 }));
            AssertTrue("title filter matches has-title positive tri-state",
                DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { hasTitle = 1 }));
            AssertTrue("title filter rejects has-title false tri-state",
                !DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { hasTitle = 0 }));
            AssertTrue("title filter matches generated positive tri-state",
                DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { hasGeneratedText = 1 }));
            AssertTrue("title filter rejects generated false tri-state",
                !DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { hasGeneratedText = 0 }));

            facts.archived = true;
            AssertTrue("title filter includes archived by default",
                DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery()));
            AssertTrue("title filter excludes archived when requested",
                !DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { includeArchived = false }));

            facts.important = false;
            facts.hasTitle = false;
            facts.hasGeneratedText = false;
            AssertTrue("title filter matches important false tri-state",
                DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { important = 0 }));
            AssertTrue("title filter matches has-title false tri-state",
                DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { hasTitle = 0 }));
            AssertTrue("title filter matches generated false tri-state",
                DiaryEntryTitleFilter.Matches(facts, new DiaryEntryTitleQuery { hasGeneratedText = 0 }));
        }

        private static void TestDiaryEntryStatsAccumulator()
        {
            DiaryEntryStatsSnapshot stats = new DiaryEntryStatsSnapshot();
            DiaryEntryStatsAccumulator.Add(stats, new DiaryEntryTitleFilterFacts
            {
                tick = 100,
                date = "1st of Aprimay, 5501",
                status = DiaryGenerationStatus.Complete,
                hasTitle = true,
                hasGeneratedText = true,
                archived = false
            });
            DiaryEntryStatsAccumulator.Add(stats, new DiaryEntryTitleFilterFacts
            {
                tick = 200,
                date = "2nd of Aprimay, 5501",
                status = DiaryGenerationStatus.Pending,
                archivedGenerationStale = true,
                archived = true
            });
            DiaryEntryStatsAccumulator.Add(stats, new DiaryEntryTitleFilterFacts
            {
                tick = 150,
                date = "1st of Aprimay evening, 5501",
                status = DiaryGenerationStatus.Pending,
                archived = false
            });
            DiaryEntryStatsAccumulator.Add(stats, new DiaryEntryTitleFilterFacts
            {
                tick = 175,
                date = "1st of Aprimay night, 5501",
                status = DiaryGenerationStatus.Skipped,
                archived = false
            });
            DiaryEntryStatsAccumulator.Add(stats, new DiaryEntryTitleFilterFacts
            {
                tick = 125,
                date = "1st of Aprimay afternoon, 5501",
                status = DiaryGenerationStatus.PromptOnly,
                archived = false
            });
            DiaryEntryStatsAccumulator.Add(stats, new DiaryEntryTitleFilterFacts
            {
                tick = 90,
                date = "15th of Decembary, 5500",
                status = DiaryGenerationStatus.NotGenerated,
                archived = false
            });

            AssertEqual("entry stats total count", 6, stats.total);
            AssertEqual("entry stats active count", 5, stats.active);
            AssertEqual("entry stats archived count", 1, stats.archived);
            AssertEqual("entry stats complete count", 1, stats.complete);
            AssertEqual("entry stats pending count", 1, stats.pending);
            AssertEqual("entry stats failed count", 1, stats.failed);
            AssertEqual("entry stats skipped count", 1, stats.skipped);
            AssertEqual("entry stats prompt-only count", 1, stats.promptOnly);
            AssertEqual("entry stats not-generated count", 1, stats.notGenerated);
            AssertEqual("entry stats title count", 1, stats.withTitle);
            AssertEqual("entry stats generated text count", 1, stats.withGeneratedText);
            AssertEqual("entry stats newest tick", 200, stats.newestTick);
            AssertEqual("entry stats newest date", "2nd of Aprimay, 5501", stats.newestDate);
            AssertEqual("entry stats oldest tick", 90, stats.oldestTick);
            AssertEqual("entry stats oldest date", "15th of Decembary, 5500", stats.oldestDate);

            DiaryEntryStatsSnapshot empty = new DiaryEntryStatsSnapshot();
            DiaryEntryStatsAccumulator.Add(null, new DiaryEntryTitleFilterFacts { tick = 1 });
            AssertEqual("empty entry stats range defaults", 0, empty.newestTick);
        }

        private static void TestPromptContextLines()
        {
            AssertEqual(
                "context line one-lines rich text and controls",
                "personality=bold curious",
                PromptContextLines.CleanLine(" personality=<b>bold</b>\r\ncurious\u0007 ", 80));
            AssertEqual(
                "context line flattens semicolon separators",
                "personality=blunt, curious",
                PromptContextLines.CleanLine("personality=blunt; curious", 80));
            AssertEqual(
                "context line length cap is exact",
                "abcdef",
                PromptContextLines.CleanLine("abcdefghi", 6));
            AssertEqual(
                "context line non-positive cap drops text",
                string.Empty,
                PromptContextLines.CleanLine("abc", 0));
            AssertEqual(
                "context line join skips blanks and caps kept lines",
                "a=1; b=2",
                PromptContextLines.Join(new List<string> { "", "a=1", "b=2", "c=3" }, 2, 80));
            AssertEqual(
                "context line join caps each line",
                "abcdef; second",
                PromptContextLines.Join(new List<string> { "abcdefghi", "second" }, 2, 6));
        }

        private static void TestExternalEventRequestText()
        {
            AssertEqual(
                "external event summary is one prompt line",
                "First line. Second line.",
                ExternalEventRequestText.CleanSummary(" First <b>line</b>.\r\nSecond line.\u0007 "));

            string splitSurrogateSummary = new string('a', ExternalEventRequestText.MaxSummaryChars - 1)
                + char.ConvertFromUtf32(0x1F600);
            AssertEqual(
                "external event summary cap does not split surrogate pairs",
                new string('a', ExternalEventRequestText.MaxSummaryChars - 1),
                ExternalEventRequestText.CleanSummary(splitSurrogateSummary));

            AssertEqual(
                "external event extra context uses prompt context cleanup",
                "mood=grim, focused; place=rec room; ignored=over-cap",
                ExternalEventRequestText.JoinExtraContext(new List<string>
                {
                    "mood=grim; focused",
                    "place=<b>rec room</b>\r\n",
                    "ignored=over-cap"
                }));
        }

        private static void TestExternalPromptContextText()
        {
            AssertEqual(
                "external prompt fragment uses prompt context cleanup",
                "Remember the rescued child, avoid omniscience",
                ExternalEventRequestText.CleanPromptFragment(
                    " Remember the <b>rescued child</b>;\r\navoid omniscience\u0007 ",
                    80));
            AssertEqual(
                "external prompt instruction uses prompt context cleanup",
                "Write from the pawn's uncertainty, avoid omniscience",
                ExternalEventRequestText.CleanPromptInstruction(
                    " Write from the <b>pawn's uncertainty</b>;\r\navoid omniscience\u0007 "));

            List<string> cleanedCandidates = ExternalEventRequestText.CleanPromptEnchantmentCandidates(
                new List<string>
                {
                    "left arm aches; keep it subtle",
                    " ",
                    "voice shaking",
                    "over cap"
                },
                2,
                80);
            AssertEqual("external prompt enchantment candidate cap", 2, cleanedCandidates.Count);
            AssertEqual(
                "external prompt enchantment candidate semicolon cleanup",
                "left arm aches, keep it subtle",
                cleanedCandidates[0]);
            AssertEqual(
                "external prompt enchantment candidate skips blanks",
                "voice shaking",
                cleanedCandidates[1]);

            string context = ExternalEventRequestText.JoinRequestContext(
                "describe the favor owed; keep it uneasy",
                "adapter fragment; protected",
                new List<string>
                {
                    "scar aches; keep it small",
                    "voice shaking"
                },
                true,
                new List<string>
                {
                    "external_prompt_instruction=spoofed",
                    "external_prompt_fragment=spoofed",
                    "location=rec room"
                },
                80,
                1,
                80);

            AssertEqual(
                "protected external prompt instruction wins over extraContext duplicate",
                "describe the favor owed, keep it uneasy",
                DiaryContextFields.Value(context, ExternalEventRequestText.PromptInstructionContextKey));
            AssertEqual(
                "protected external prompt fragment wins over extraContext duplicate",
                "adapter fragment, protected",
                DiaryContextFields.Value(context, ExternalEventRequestText.PromptFragmentContextKey));
            AssertContains(
                "ordinary external context is appended after protected fields",
                context,
                "location=rec room");
            AssertTrue(
                "replace mode is saved when candidates survive",
                ExternalEventRequestText.PromptEnchantmentsReplaceNormal(context));

            List<PromptEnchantmentCandidate> parsed =
                ExternalEventRequestText.PromptEnchantmentCandidatesFromContext(context, 6, 2.5f);
            AssertEqual("external prompt enchantment context parses capped candidates", 1, parsed.Count);
            AssertEqual(
                "external prompt enchantment candidate text round-trips",
                "scar aches, keep it small",
                parsed[0].conditionText);
            AssertNear("external prompt enchantment candidate weight applies", 2.5f, parsed[0].weight);

            string blankReplacementContext = ExternalEventRequestText.JoinRequestContext(
                null,
                string.Empty,
                new List<string> { " " },
                true,
                null,
                80,
                6,
                80);
            AssertTrue(
                "replace mode is not saved without valid candidates",
                !ExternalEventRequestText.PromptEnchantmentsReplaceNormal(blankReplacementContext));

            string spoofedContext = ExternalEventRequestText.JoinRequestContext(
                null,
                string.Empty,
                null,
                false,
                new List<string>
                {
                    "external_prompt_instruction=spoofed instruction",
                    "external_prompt_fragment=spoofed",
                    "external_prompt_enchantment_mode=replace",
                    "external_prompt_enchantment_1=spoofed candidate",
                    "ordinary=kept"
                },
                80,
                6,
                80);
            AssertEqual(
                "extraContext cannot spoof protected prompt instruction",
                string.Empty,
                DiaryContextFields.Value(spoofedContext, ExternalEventRequestText.PromptInstructionContextKey));
            AssertEqual(
                "extraContext cannot spoof protected prompt fragment",
                string.Empty,
                DiaryContextFields.Value(spoofedContext, ExternalEventRequestText.PromptFragmentContextKey));
            AssertTrue(
                "extraContext cannot spoof replace mode",
                !ExternalEventRequestText.PromptEnchantmentsReplaceNormal(spoofedContext));
            AssertEqual(
                "extraContext cannot spoof external prompt enchantments",
                0,
                ExternalEventRequestText.PromptEnchantmentCandidatesFromContext(
                    spoofedContext,
                    6,
                    1f).Count);
            AssertContains(
                "non-reserved extraContext survives protected filtering",
                spoofedContext,
                "ordinary=kept");

            // SECURITY: adapter extraContext must not be able to set an INTERNAL game-context key —
            // structural POV/reflection markers, event-domain markers, classifier value keys, or a
            // prompt ContextField. First-match means a smuggled "death_description=true" would force a
            // death/neutral page and "quest_label=..." would inject prompt content the API never
            // sanctioned. Free-form adapter keys (location, weather, ...) stay untouched.
            string reservedContext = ExternalEventRequestText.JoinRequestContext(
                null,
                string.Empty,
                null,
                false,
                new List<string>
                {
                    "death_description=true",
                    "arrival_description=true",
                    "quest_label=Fake Quest",
                    "external=spoof",
                    "signal=accepted",
                    "work=mining",
                    "location=rec room",
                    "weather=clear"
                },
                80,
                6,
                80);
            AssertTrue(
                "extraContext cannot forge death_description",
                !DiaryContextFields.IsTrue(reservedContext, "death_description"));
            AssertTrue(
                "extraContext cannot forge arrival_description",
                !DiaryContextFields.IsTrue(reservedContext, "arrival_description"));
            AssertEqual(
                "extraContext cannot forge a quest prompt ContextField",
                string.Empty,
                DiaryContextFields.Value(reservedContext, "quest_label"));
            AssertEqual(
                "extraContext cannot forge an event-domain marker",
                string.Empty,
                DiaryContextFields.Value(reservedContext, "external"));
            AssertEqual(
                "extraContext cannot forge a classifier value key",
                string.Empty,
                DiaryContextFields.Value(reservedContext, "signal"));
            AssertContains(
                "free-form adapter key survives reserved filtering",
                reservedContext,
                "location=rec room");
            AssertContains(
                "second free-form adapter key survives reserved filtering",
                reservedContext,
                "weather=clear");

            // CAP: a tuning author may raise the XML-backed enchantment-candidate cap. The protected
            // field block must never grow saved gameContext without bound — an absolute code-level
            // ceiling (MaxRequestContextLines) backs the XML per-source caps, mirroring MaxListeners.
            List<string> manyCandidates = new List<string>();
            List<string> extraContextOverflow = new List<string>();
            for (int i = 0; i < 200; i++)
            {
                manyCandidates.Add("candidate_" + i);
                extraContextOverflow.Add("free_" + i);
            }

            string cappedContext = ExternalEventRequestText.JoinRequestContext(
                "instruction text",
                "fragment text",
                manyCandidates,
                true,
                extraContextOverflow,
                80,
                200,
                80);

            int totalFields = CountContextFields(cappedContext);
            AssertTrue(
                "total context fields stay under the absolute ceiling even with a huge XML cap",
                totalFields <= ExternalEventRequestText.MaxRequestContextLines);
            AssertEqual(
                "the ceiling constant is the documented parser limit",
                64,
                ExternalEventRequestText.MaxRequestContextLines);
            AssertTrue(
                "ordinary context overflow is dropped after protected fields fill the ceiling",
                cappedContext.IndexOf("free_0=", StringComparison.Ordinal) < 0);
        }

        private static int CountContextFields(string context)
        {
            if (string.IsNullOrWhiteSpace(context))
            {
                return 0;
            }

            int count = 0;
            foreach (string part in context.Split(';'))
            {
                if (!string.IsNullOrWhiteSpace(part) && part.IndexOf('=') > 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static void TestExternalEntryAttribution()
        {
            string context = "external=rjw_casual_encounter; source=adapter.mod; mood=grim";
            AssertEqual(
                "external attribution source parses",
                "adapter.mod",
                ExternalEntryAttribution.SourceIdForContext(context));
            AssertTrue(
                "external attribution flag requires source",
                ExternalEntryAttribution.IsExternallyAuthored(context));
            AssertEqual(
                "native source field is ignored without external marker",
                string.Empty,
                ExternalEntryAttribution.SourceIdForContext("source=adapter.mod; tale=RaidArrived"));
            AssertTrue(
                "blank external source is not attributed",
                !ExternalEntryAttribution.IsExternallyAuthored("external=event; source= "));

            string splitSurrogateSource = new string('s', ExternalWritingStyleOverrideText.MaxSourceIdChars - 1)
                + char.ConvertFromUtf32(0x1F600);
            AssertEqual(
                "external source cap does not split surrogate pairs",
                new string('s', ExternalWritingStyleOverrideText.MaxSourceIdChars - 1),
                ExternalEntryAttribution.SourceIdForContext("external=event; source=" + splitSurrogateSource));
        }

        private static void TestExternalApiBudgetPolicy()
        {
            ExternalApiBudgetTuning tuning = new ExternalApiBudgetTuning
            {
                enabled = true,
                windowTicks = 100,
                maxRequestsPerSource = 2,
                maxRequestsGlobal = 3,
                maxTokensPerSource = 500,
                maxTokensGlobal = 800
            };
            List<ExternalApiBudgetReservation> recent = new List<ExternalApiBudgetReservation>
            {
                new ExternalApiBudgetReservation { tick = 90, sourceId = "adapter.a", estimatedTokens = 100 },
                new ExternalApiBudgetReservation { tick = 80, sourceId = "adapter.b", estimatedTokens = 200 },
                new ExternalApiBudgetReservation { tick = -1, sourceId = "adapter.a", estimatedTokens = 900 }
            };

            ExternalApiBudgetDecision allowed = ExternalApiBudgetPolicy.Evaluate(
                recent, tuning, 100, " adapter.a ", 300);
            AssertTrue("budget allows request inside all caps", allowed.allowed);
            AssertEqual("budget counts in-window source requests", 1, allowed.sourceRequests);
            AssertEqual("budget counts in-window global requests", 2, allowed.globalRequests);
            AssertEqual("budget counts in-window source tokens", 100, allowed.sourceTokens);
            AssertEqual("budget counts in-window global tokens", 300, allowed.globalTokens);
            AssertEqual("budget stores requested tokens", 300, allowed.requestedTokens);

            ExternalApiBudgetTuning sourceRequestTuning = new ExternalApiBudgetTuning
            {
                enabled = true,
                windowTicks = 100,
                maxRequestsPerSource = 1,
                maxRequestsGlobal = 3,
                maxTokensPerSource = 500,
                maxTokensGlobal = 800
            };
            ExternalApiBudgetDecision sourceRequestCap = ExternalApiBudgetPolicy.Evaluate(
                recent, sourceRequestTuning, 100, "adapter.a", 100);
            AssertTrue("budget rejects source request cap", !sourceRequestCap.allowed);
            AssertEqual("source request cap reason",
                ExternalApiBudgetPolicy.SourceRequestCapReason,
                sourceRequestCap.blockReason);

            ExternalApiBudgetTuning globalTokenTuning = new ExternalApiBudgetTuning
            {
                enabled = true,
                windowTicks = 100,
                maxRequestsPerSource = 2,
                maxRequestsGlobal = 3,
                maxTokensPerSource = 1000,
                maxTokensGlobal = 800
            };
            ExternalApiBudgetDecision globalTokenCap = ExternalApiBudgetPolicy.Evaluate(
                recent, globalTokenTuning, 100, "adapter.c", 600);
            AssertTrue("budget rejects global token cap", !globalTokenCap.allowed);
            AssertEqual("global token cap reason",
                ExternalApiBudgetPolicy.GlobalTokenCapReason,
                globalTokenCap.blockReason);

            ExternalApiBudgetTuning uncapped = new ExternalApiBudgetTuning
            {
                enabled = true,
                windowTicks = 100,
                maxRequestsPerSource = 0,
                maxRequestsGlobal = 0,
                maxTokensPerSource = 0,
                maxTokensGlobal = 0
            };
            ExternalApiBudgetDecision disabledCaps = ExternalApiBudgetPolicy.Evaluate(
                recent, uncapped, 100, "adapter.a", 50000);
            AssertTrue("zero budget caps are disabled", disabledCaps.allowed);

            AssertTrue("window boundary is expired",
                !ExternalApiBudgetPolicy.IsInsideWindow(
                    new ExternalApiBudgetReservation { tick = 0, sourceId = "adapter", estimatedTokens = 1 },
                    100,
                    100));
            AssertEqual("blank budget source normalizes",
                "unknown-source",
                ExternalApiBudgetPolicy.NormalizeSourceId(" \t "));
        }

        private static void TestDiaryListText()
        {
            List<string> entries = new List<string>
            {
                "gunshot (bleeding, infected)",
                "chemical damage, moderate"
            };
            AssertEqual(
                "comma-bearing entries join without being split",
                "gunshot (bleeding, infected), chemical damage, moderate",
                DiaryListText.JoinComma(entries));

            List<string> withBlank = new List<string> { "first", " ", null, "second, still one item" };
            AssertEqual(
                "join skips blank/null entries but preserves comma text",
                "first, second, still one item",
                DiaryListText.JoinComma(withBlank));

            List<string> copy = DiaryListText.CopyNonNull(withBlank);
            AssertEqual("copy removes only null entries", 3, copy.Count);
            AssertEqual("copy preserves blank strings for DTO fidelity", " ", copy[1]);
            withBlank[0] = "changed";
            AssertEqual("copy is independent from source list", "first", copy[0]);

            List<string> target = new List<string>();
            DiaryListText.AddNonBlank(target, "kept, with comma");
            DiaryListText.AddNonBlank(target, " ");
            AssertEqual("add nonblank keeps comma-bearing entry only", 1, target.Count);
            AssertEqual("add nonblank preserves text", "kept, with comma", target[0]);
        }

        private static void TestContextProviderRegistry()
        {
            ContextProviderRegistry<string> registry = new ContextProviderRegistry<string>(32);
            List<string> failures = new List<string>();

            AssertTrue("registry rejects blank id", !registry.Register(" ", context => "bad=1"));
            AssertTrue("registry rejects null provider", !registry.Register("mod.null", null));
            AssertTrue("registry accepts provider", registry.Register("mod.personality", context => "personality=" + context));
            AssertEqual(
                "registry builds registered provider line",
                "personality=guarded",
                registry.BuildContextLines("guarded", 8, 80, (id, e) => failures.Add(id)));

            AssertTrue("registry replacement succeeds", registry.Register("mod.personality", context => "voice=" + context));
            AssertEqual(
                "registry replacement keeps id and changes output",
                "voice=plain",
                registry.BuildContextLines("plain", 8, 80, (id, e) => failures.Add(id)));

            AssertTrue("registry accepts empty provider", registry.Register("mod.empty", context => "  "));
            AssertTrue("registry accepts provider after empty", registry.Register("mod.after_empty", context => "after_empty=kept"));
            AssertEqual(
                "empty provider does not consume kept-line cap",
                "voice=cap; after_empty=kept",
                registry.BuildContextLines("cap", 2, 80, (id, e) => failures.Add(id)));

            AssertTrue("registry accepts throwing provider", registry.Register("mod.throwing", context =>
            {
                throw new InvalidOperationException("boom");
            }));
            AssertTrue("registry accepts later provider", registry.Register("mod.later", context => "later=kept"));

            AssertEqual(
                "throwing provider is skipped while later provider still runs",
                "voice=first; after_empty=kept; later=kept",
                registry.BuildContextLines("first", 8, 80, (id, e) => failures.Add(id)));
            AssertEqual("throwing provider failure logged once", 1, failures.Count);
            AssertEqual("throwing provider id is reported", "mod.throwing", failures[0]);

            AssertEqual(
                "disabled provider is not invoked again",
                "voice=second; after_empty=kept; later=kept",
                registry.BuildContextLines("second", 8, 80, (id, e) => failures.Add(id)));
            AssertEqual("disabled provider does not report a second failure", 1, failures.Count);

            AssertEqual(
                "registry caps provider contributions",
                "voice=capped",
                registry.BuildContextLines("capped", 1, 80, (id, e) => failures.Add(id)));

            // A churning-id adapter must not grow the registry without bound: new ids past the cap are
            // refused, but replacing an id already registered stays allowed even at the cap.
            ContextProviderRegistry<string> capped = new ContextProviderRegistry<string>(2);
            AssertTrue("capped registry accepts first id", capped.Register("a", context => "a=" + context));
            AssertTrue("capped registry accepts second id", capped.Register("b", context => "b=" + context));
            AssertTrue("capped registry rejects a new id beyond the cap", !capped.Register("c", context => "c=" + context));
            AssertTrue("capped registry still replaces an existing id at the cap", capped.Register("a", context => "a2=" + context));
            AssertEqual(
                "over-cap provider never contributes a line",
                "a2=x; b=x",
                capped.BuildContextLines("x", 8, 80, (id, e) => failures.Add(id)));

            // REENTRANCY: a provider that registers a new id from inside its own callback must not
            // mutate the live `order` list mid-iteration. The walk snapshots the ids first, so the
            // newly-registered id does not appear in this pass and the original set completes cleanly.
            // Without the snapshot, List.Add inside the foreach index loop would either grow the walk
            // unpredictably or, once the loop is refactored to foreach, throw on mutation.
            ContextProviderRegistry<string> reentrant = new ContextProviderRegistry<string>(32);
            bool firstCall = true;
            reentrant.Register("mod.first", context =>
            {
                if (firstCall)
                {
                    firstCall = false;
                    // Re-enter Register from inside the provider callback.
                    reentrant.Register("mod.added_mid_callback", ctx => "added=" + ctx);
                }

                return "first=" + context;
            });
            reentrant.Register("mod.second", context => "second=" + context);
            List<string> reentrantFailures = new List<string>();
            AssertEqual(
                "reentrant registration does not extend the current walk",
                "first=pass; second=pass",
                reentrant.BuildContextLines("pass", 8, 80, (id, e) => reentrantFailures.Add(id)));
            AssertEqual(
                "mid-callback registration lands for the next pass (appended to order)",
                "first=again; second=again; added=again",
                reentrant.BuildContextLines("again", 8, 80, (id, e) => reentrantFailures.Add(id)));
        }

        private static void TestEventWindowPolicy()
        {
            EventWindowSignalFacts grayFlesh = new EventWindowSignalFacts
            {
                source = "ThingSpawned",
                signal = "spawned",
                defName = "GrayFleshSample",
                label = "gray flesh sample"
            };

            AssertTrue(
                "event window exact def match",
                EventWindowPolicy.Matches(
                    new EventWindowTriggerRule
                    {
                        source = "ThingSpawned",
                        signal = "spawned",
                        matchDefNames = new List<string> { "GrayFleshSample" }
                    },
                    grayFlesh));
            AssertTrue(
                "event window rejects wrong source",
                !EventWindowPolicy.Matches(
                    new EventWindowTriggerRule
                    {
                        source = "Incident",
                        signal = "spawned",
                        matchDefNames = new List<string> { "GrayFleshSample" }
                    },
                    grayFlesh));
            AssertTrue(
                "event window token match",
                EventWindowPolicy.Matches(
                    new EventWindowTriggerRule
                    {
                        source = "ThingSpawned",
                        matchTokens = new List<string> { "flesh sample" }
                    },
                    grayFlesh));
            AssertTrue(
                "event window source signal only",
                EventWindowPolicy.Matches(
                    new EventWindowTriggerRule
                    {
                        source = "Quest",
                        signal = "accepted"
                    },
                    new EventWindowSignalFacts { source = "Quest", signal = "accepted" }));
            EventWindowSignalFacts ancientDanger = new EventWindowSignalFacts
            {
                source = "Letter",
                signal = "received",
                defName = "AncientShrineWarning",
                label = "Ancient danger",
                subjectPawnId = "Pawn_Alice_1",
                subjectLabel = "Alice"
            };
            EventWindowSignalFacts voidMonolith = new EventWindowSignalFacts
            {
                source = "ProximityLetter",
                signal = "received",
                defName = "VoidMonolith",
                label = "Fallen monolith",
                subjectPawnId = "Pawn_Bruno_2",
                subjectLabel = "Bruno"
            };
            EventWindowSignalFacts voidMonolithActivation = new EventWindowSignalFacts
            {
                source = "VoidMonolith",
                signal = "activated",
                defName = "Waking",
                label = "Level 2: Pulsing with psychic energy",
                subjectPawnId = "Pawn_Bruno_2",
                subjectLabel = "Bruno"
            };
            EventWindowSignalFacts birthday = new EventWindowSignalFacts
            {
                source = "PawnAge",
                signal = "birthday",
                defName = "Birthday",
                label = "73",
                subjectPawnId = "Pawn_Cora_3",
                subjectLabel = "Cora"
            };
            EventWindowSignalFacts heartAttack = new EventWindowSignalFacts
            {
                source = "Hediff",
                signal = "added",
                defName = "HeartAttack",
                label = "heart attack",
                subjectPawnId = "Pawn_Dan_4",
                subjectLabel = "Dan"
            };
            EventWindowSignalFacts prisonBreak = new EventWindowSignalFacts
            {
                source = "PrisonBreak",
                signal = "started",
                defName = "PrisonBreak",
                label = "Prison break"
            };
            AssertTrue(
                "event window letter exact def match",
                EventWindowPolicy.Matches(
                    new EventWindowTriggerRule
                    {
                        source = "Letter",
                        signal = "received",
                        matchDefNames = new List<string> { "AncientShrineWarning" }
                    },
                    ancientDanger));
            AssertTrue(
                "event window proximity letter exact def match",
                EventWindowPolicy.Matches(
                    new EventWindowTriggerRule
                    {
                        source = "ProximityLetter",
                        signal = "received",
                        matchDefNames = new List<string> { "VoidMonolith" }
                    },
                    voidMonolith));
            AssertTrue(
                "event window void monolith activation match",
                EventWindowPolicy.Matches(
                    new EventWindowTriggerRule
                    {
                        source = "VoidMonolith",
                        signal = "activated"
                    },
                    voidMonolithActivation));
            AssertTrue(
                "event window void monolith activation level match",
                EventWindowPolicy.Matches(
                    new EventWindowTriggerRule
                    {
                        source = "VoidMonolith",
                        signal = "activated",
                        matchDefNames = new List<string> { "Waking" }
                    },
                    voidMonolithActivation));
            AssertTrue(
                "event window birthday match",
                EventWindowPolicy.Matches(
                    new EventWindowTriggerRule
                    {
                        source = "PawnAge",
                        signal = "birthday",
                        matchDefNames = new List<string> { "Birthday" }
                    },
                    birthday));
            AssertTrue(
                "event window hediff added match",
                EventWindowPolicy.Matches(
                    new EventWindowTriggerRule
                    {
                        source = "Hediff",
                        signal = "added",
                        matchDefNames = new List<string> { "HeartAttack" }
                    },
                    heartAttack));
            AssertTrue(
                "event window prison break match",
                EventWindowPolicy.Matches(
                    new EventWindowTriggerRule
                    {
                        source = "PrisonBreak",
                        signal = "started",
                        matchDefNames = new List<string> { "PrisonBreak" }
                    },
                    prisonBreak));
            AssertTrue(
                "event window subject token match",
                EventWindowPolicy.Matches(
                    new EventWindowTriggerRule
                    {
                        source = "Letter",
                        matchTokens = new List<string> { "Alice" }
                    },
                    ancientDanger));
            AssertTrue(
                "event window blank trigger rejected",
                !EventWindowPolicy.Matches(new EventWindowTriggerRule(), grayFlesh));
            AssertTrue(
                "event window empty rules do not match",
                !EventWindowPolicy.MatchesAny(new List<EventWindowTriggerRule>(), grayFlesh));

            // CouldMatchByDefName: cheap, label-free pre-filter used on hot signal paths (every
            // spawned Thing / added Hediff). It must be a strict superset of Matches over
            // source+defName, so it never yields a false negative — only "maybe, run the full check".
            List<EventWindowTriggerRule> thingExactRules = new List<EventWindowTriggerRule>
            {
                new EventWindowTriggerRule
                {
                    source = "ThingSpawned",
                    signal = "spawned",
                    matchDefNames = new List<string> { "GrayFleshSample", "Filth_GrayFleshNoticeable" }
                }
            };
            AssertTrue(
                "could-match rejects unknown spawned defName",
                !EventWindowPolicy.CouldMatchByDefName(thingExactRules, "ThingSpawned", "Bullet_Revolver"));
            AssertTrue(
                "could-match accepts known spawned defName (case-insensitive)",
                EventWindowPolicy.CouldMatchByDefName(thingExactRules, "ThingSpawned", "grayfleshsample"));
            AssertTrue(
                "could-match rejects wrong source",
                !EventWindowPolicy.CouldMatchByDefName(thingExactRules, "Hediff", "GrayFleshSample"));
            AssertTrue(
                "could-match is a superset of Matches",
                EventWindowPolicy.CouldMatchByDefName(thingExactRules, grayFlesh.source, grayFlesh.defName));

            List<EventWindowTriggerRule> hediffExactRules = new List<EventWindowTriggerRule>
            {
                new EventWindowTriggerRule
                {
                    source = "Hediff",
                    signal = "added",
                    matchDefNames = new List<string> { "HeartAttack" }
                }
            };
            AssertTrue(
                "could-match ignores signal and over-approximates by defName",
                EventWindowPolicy.CouldMatchByDefName(hediffExactRules, "Hediff", "HeartAttack"));
            AssertTrue(
                "could-match rejects non-listed hediff defName",
                !EventWindowPolicy.CouldMatchByDefName(hediffExactRules, "Hediff", "Flu"));

            List<EventWindowTriggerRule> tokenRules = new List<EventWindowTriggerRule>
            {
                new EventWindowTriggerRule { source = "Letter", matchTokens = new List<string> { "Alice" } }
            };
            AssertTrue(
                "could-match forces full check for token matchers",
                EventWindowPolicy.CouldMatchByDefName(tokenRules, "Letter", "AnyDefName"));

            List<EventWindowTriggerRule> sourceOnlyRules = new List<EventWindowTriggerRule>
            {
                new EventWindowTriggerRule { source = "VoidMonolith", signal = "activated" }
            };
            AssertTrue(
                "could-match accepts source/signal-only rule for any defName",
                EventWindowPolicy.CouldMatchByDefName(sourceOnlyRules, "VoidMonolith", "MonolithLevelWhatever"));

            List<EventWindowTriggerRule> blankSourceExactRules = new List<EventWindowTriggerRule>
            {
                new EventWindowTriggerRule { matchDefNames = new List<string> { "SomeDef" } }
            };
            AssertTrue(
                "could-match honors blank (any) source",
                EventWindowPolicy.CouldMatchByDefName(blankSourceExactRules, "AnySource", "SomeDef"));
            AssertTrue(
                "could-match blank trigger rejected",
                !EventWindowPolicy.CouldMatchByDefName(
                    new List<EventWindowTriggerRule> { new EventWindowTriggerRule() }, "ThingSpawned", "Bullet"));
            AssertTrue(
                "could-match empty rules do not match",
                !EventWindowPolicy.CouldMatchByDefName(new List<EventWindowTriggerRule>(), "ThingSpawned", "Bullet"));

            XDocument eventWindows = XDocument.Load(RepoPath("1.6", "Defs", "DiaryEventWindowDefs.xml"));
            foreach (XElement def in eventWindows.Descendants("PawnDiary.DiaryEventWindowDef"))
            {
                string defName = ChildValue(def, "defName");
                string keepActiveText = ChildValue(def, "keepActive");
                bool keepActive = string.IsNullOrWhiteSpace(keepActiveText)
                    || string.Equals(keepActiveText, "true", StringComparison.OrdinalIgnoreCase);
                int timeoutTicks;
                if (!int.TryParse(ChildValue(def, "timeoutTicks"), out timeoutTicks))
                {
                    timeoutTicks = -1;
                }

                AssertTrue(
                    "active event window has a close path: " + defName,
                    !keepActive || timeoutTicks > 0 || HasUsableEventWindowTrigger(def.Element("endSignals")));
            }
        }

        private static void TestProgressionMilestonePolicy()
        {
            List<int> milestones = new List<int> { 20, 8, 12, 12, -1, 16 };

            ProgressionMilestoneDecision noList =
                ProgressionMilestonePolicy.EvaluateSkillMilestone(12, true, null, 0, false);
            AssertTrue("progression skill null milestones do not emit", !noList.shouldEmit);

            ProgressionMilestoneDecision noPassion =
                ProgressionMilestonePolicy.EvaluateSkillMilestone(12, false, milestones, 0, false);
            AssertTrue("progression skill no passion does not emit", !noPassion.shouldEmit);

            ProgressionMilestoneDecision below =
                ProgressionMilestonePolicy.EvaluateSkillMilestone(7, true, milestones, 0, false);
            AssertTrue("progression skill below milestone does not emit", !below.shouldEmit);

            ProgressionMilestoneDecision first =
                ProgressionMilestonePolicy.EvaluateSkillMilestone(8, true, milestones, 0, false);
            AssertTrue("progression skill crossing emits", first.shouldEmit);
            AssertEqual("progression skill crossing emits reached milestone", 8, first.milestoneToEmit);

            ProgressionMilestoneDecision jumped =
                ProgressionMilestonePolicy.EvaluateSkillMilestone(12, true, milestones, 7, false);
            AssertTrue("progression skill jump emits one highest milestone", jumped.shouldEmit);
            AssertEqual("progression skill jump emits 12", 12, jumped.milestoneToEmit);

            ProgressionMilestoneDecision repeated =
                ProgressionMilestonePolicy.EvaluateSkillMilestone(12, true, milestones, 12, false);
            AssertTrue("progression skill already recorded does not repeat", !repeated.shouldEmit);

            ProgressionMilestoneDecision baseline =
                ProgressionMilestonePolicy.EvaluateSkillMilestone(16, true, milestones, 0, true);
            AssertTrue("progression skill baseline suppresses emit", !baseline.shouldEmit);
            AssertEqual("progression skill baseline records highest reached", 16, baseline.newHighestMilestone);
        }

        private static void TestPsylinkProgressionLevelPolicy()
        {
            ProgressionLevelDecision baseline =
                ProgressionMilestonePolicy.EvaluateLevelIncrease(3, 0, true, 1, 6);
            AssertTrue("progression level baseline suppresses emit", !baseline.shouldEmit);
            AssertEqual("progression level baseline records observed", 3, baseline.newHighestLevel);

            ProgressionLevelDecision increased =
                ProgressionMilestonePolicy.EvaluateLevelIncrease(4, 3, false, 1, 6);
            AssertTrue("progression level increase emits", increased.shouldEmit);
            AssertEqual("progression level emits current level", 4, increased.levelToEmit);

            ProgressionLevelDecision same =
                ProgressionMilestonePolicy.EvaluateLevelIncrease(4, 4, false, 1, 6);
            AssertTrue("progression level same value does not emit", !same.shouldEmit);

            ProgressionLevelDecision lower =
                ProgressionMilestonePolicy.EvaluateLevelIncrease(3, 4, false, 1, 6);
            AssertTrue("progression level lower value does not emit", !lower.shouldEmit);
            AssertEqual("progression level lower keeps highest", 4, lower.newHighestLevel);

            ProgressionLevelDecision clamped =
                ProgressionMilestonePolicy.EvaluateLevelIncrease(9, 5, false, 1, 6);
            AssertTrue("progression level high value clamps and emits", clamped.shouldEmit);
            AssertEqual("progression level clamps to max", 6, clamped.levelToEmit);
        }

        private static void TestArcReflectionSchedulePolicy()
        {
            ArcReflectionScheduleTuning tuning = new ArcReflectionScheduleTuning
            {
                enabled = true,
                maxEntriesPerYear = 2,
                allowSecondMajorEntry = true,
                secondEntryMinGapDays = 30,
                forceAfterYearDay = 45,
                ticksPerDay = 60000,
            };

            ArcReflectionScheduleDecision beforeForce = ArcReflectionSchedulePolicy.Evaluate(
                new ArcReflectionScheduleSnapshot(), tuning, currentTick: 44 * 60000,
                currentYear: 5504, dayOfYear: 44, majorEventTrigger: false);
            AssertTrue("arc schedule blocks forced entry before force day", !beforeForce.allowed);
            AssertEqual("arc schedule before force reason", "not_due", beforeForce.blockReason);

            ArcReflectionScheduleDecision forced = ArcReflectionSchedulePolicy.Evaluate(
                new ArcReflectionScheduleSnapshot(), tuning, currentTick: 45 * 60000,
                currentYear: 5504, dayOfYear: 45, majorEventTrigger: false);
            AssertTrue("arc schedule allows yearly forced entry", forced.allowed);
            AssertTrue("arc schedule marks forced entry", forced.forced);

            ArcReflectionScheduleSnapshot oneEntry = new ArcReflectionScheduleSnapshot
            {
                lastArcEntryTick = 10 * 60000,
                lastArcEntryYear = 5504,
                arcEntriesThisYear = 1,
            };
            ArcReflectionScheduleDecision ordinary = ArcReflectionSchedulePolicy.Evaluate(
                oneEntry, tuning, currentTick: 50 * 60000,
                currentYear: 5504, dayOfYear: 50, majorEventTrigger: false);
            AssertTrue("arc schedule blocks ordinary trigger after one entry", !ordinary.allowed);
            AssertEqual("arc schedule ordinary block reason", "not_due", ordinary.blockReason);

            ArcReflectionScheduleDecision secondTooSoon = ArcReflectionSchedulePolicy.Evaluate(
                oneEntry, tuning, currentTick: 39 * 60000,
                currentYear: 5504, dayOfYear: 39, majorEventTrigger: true);
            AssertTrue("arc schedule blocks second major before gap", !secondTooSoon.allowed);
            AssertEqual("arc schedule gap block reason", "gap", secondTooSoon.blockReason);

            ArcReflectionScheduleDecision secondAllowed = ArcReflectionSchedulePolicy.Evaluate(
                oneEntry, tuning, currentTick: 41 * 60000,
                currentYear: 5504, dayOfYear: 41, majorEventTrigger: true);
            AssertTrue("arc schedule allows second major after gap", secondAllowed.allowed);
            AssertTrue("arc schedule second major is not forced", !secondAllowed.forced);

            ArcReflectionScheduleDecision capped = ArcReflectionSchedulePolicy.Evaluate(
                new ArcReflectionScheduleSnapshot
                {
                    lastArcEntryTick = 50 * 60000,
                    lastArcEntryYear = 5504,
                    arcEntriesThisYear = 2,
                },
                tuning, currentTick: 59 * 60000, currentYear: 5504,
                dayOfYear: 59, majorEventTrigger: true);
            AssertTrue("arc schedule blocks third entry", !capped.allowed);
            AssertEqual("arc schedule cap reason", "year_cap", capped.blockReason);

            ArcReflectionScheduleTuning onePerYear = new ArcReflectionScheduleTuning
            {
                enabled = true,
                maxEntriesPerYear = 1,
                allowSecondMajorEntry = true,
                secondEntryMinGapDays = 30,
                forceAfterYearDay = 45,
                ticksPerDay = 60000,
            };
            ArcReflectionScheduleDecision secondBlockedByMax = ArcReflectionSchedulePolicy.Evaluate(
                oneEntry, onePerYear, currentTick: 41 * 60000,
                currentYear: 5504, dayOfYear: 41, majorEventTrigger: true);
            AssertTrue("arc schedule honors one-entry yearly cap", !secondBlockedByMax.allowed);
            AssertEqual("arc schedule one-entry cap reason", "year_cap", secondBlockedByMax.blockReason);

            ArcReflectionScheduleTuning fourPerYear = new ArcReflectionScheduleTuning
            {
                enabled = true,
                maxEntriesPerYear = 4,
                allowSecondMajorEntry = true,
                secondEntryMinGapDays = 30,
                forceAfterYearDay = 45,
                ticksPerDay = 60000,
            };
            ArcReflectionScheduleDecision thirdAllowed = ArcReflectionSchedulePolicy.Evaluate(
                new ArcReflectionScheduleSnapshot
                {
                    lastArcEntryTick = 41 * 60000,
                    lastArcEntryYear = 5504,
                    arcEntriesThisYear = 2,
                },
                fourPerYear, currentTick: 72 * 60000, currentYear: 5504,
                dayOfYear: 72, majorEventTrigger: true);
            AssertTrue("arc schedule allows configured third major entry", thirdAllowed.allowed);
            AssertEqual("arc schedule reports configured yearly cap", 4, thirdAllowed.maxAllowedThisYear);

            ArcReflectionScheduleDecision highCapReached = ArcReflectionSchedulePolicy.Evaluate(
                new ArcReflectionScheduleSnapshot
                {
                    lastArcEntryTick = 100 * 60000,
                    lastArcEntryYear = 5504,
                    arcEntriesThisYear = 4,
                },
                fourPerYear, currentTick: 131 * 60000, currentYear: 5504,
                dayOfYear: 131, majorEventTrigger: true);
            AssertTrue("arc schedule blocks entries at configured high cap", !highCapReached.allowed);
            AssertEqual("arc schedule high cap reason", "year_cap", highCapReached.blockReason);

            ArcReflectionScheduleDecision newYear = ArcReflectionSchedulePolicy.Evaluate(
                new ArcReflectionScheduleSnapshot
                {
                    lastArcEntryTick = 50 * 60000,
                    lastArcEntryYear = 5503,
                    arcEntriesThisYear = 2,
                },
                tuning, currentTick: 65 * 60000, currentYear: 5504,
                dayOfYear: 5, majorEventTrigger: true);
            AssertTrue("arc schedule resets count in new year", newYear.allowed);
            AssertEqual("arc schedule new year normalized count", 0, newYear.normalizedEntriesThisYear);
        }

        private static void TestArcReflectionMemorySelector()
        {
            List<ArcMemoryCandidate> candidates = new List<ArcMemoryCandidate>
            {
                ArcCandidate("e1", 3000, "Progression", "skill", true, false, false, "reached Construction 12"),
                ArcCandidate("e1", 3500, "Progression", "skill", true, false, false, "duplicate should be ignored"),
                ArcCandidate("e2", 1000, "Raid", "raidDropPod", true, false, false, "drop pods hit the workshop"),
                ArcCandidate("e3", 2000, "Romance", "romanceMilestone", true, false, false, "married under torchlight"),
                ArcCandidate("e-reflection", 2500, "Interaction", "dayreflection", true, true, false, "reflection"),
                ArcCandidate("e-death", 2600, "Tale", "death", true, false, true, "death"),
                ArcCandidate("e-recent", 2700, "Quest", "questCompleted", true, false, false, "quest done"),
                ArcCandidate("e-old", 2800, "Work", "workPassion", true, false, false, "old work", 5503),
            };

            ArcMemorySelectionResult selected = ArcReflectionMemorySelector.Select(new ArcMemorySelectionRequest
            {
                candidates = candidates,
                recentlyUsedEventIds = new List<string> { "e-recent" },
                currentYear = 5504,
                maxMemories = 8,
                minMemories = 3,
                seed = 42,
            });
            AssertEqual("arc selector filters and sorts selected ids", "e2,e3,e1",
                JoinCandidateIds(selected.selected));
            AssertEqual("arc selector candidate count after filters", 3, selected.candidateCount);
            AssertTrue("arc selector has enough selected memories", selected.hasEnoughMemories);

            List<ArcMemoryCandidate> sameGroup = new List<ArcMemoryCandidate>
            {
                ArcCandidate("g1", 1000, "Progression", "skill", true, false, false, "one"),
                ArcCandidate("g2", 2000, "Progression", "skill", true, false, false, "two"),
                ArcCandidate("g3", 3000, "Progression", "skill", true, false, false, "three"),
                ArcCandidate("g4", 4000, "Progression", "skill", true, false, false, "four"),
            };
            ArcMemorySelectionResult capped = ArcReflectionMemorySelector.Select(new ArcMemorySelectionRequest
            {
                candidates = sameGroup,
                currentYear = 5504,
                maxMemories = 4,
                minMemories = 1,
                seed = 7,
            });
            AssertEqual("arc selector caps repeated domain/group", 2, capped.selected.Count);

            List<ArcMemoryCandidate> deterministic = new List<ArcMemoryCandidate>
            {
                ArcCandidate("d1", 1000, "Progression", "skill", true, false, false, "one"),
                ArcCandidate("d2", 2000, "Raid", "raid", true, false, false, "two"),
                ArcCandidate("d3", 3000, "Romance", "romance", true, false, false, "three"),
                ArcCandidate("d4", 4000, "Quest", "quest", true, false, false, "four"),
            };
            ArcMemorySelectionResult deterministicA = ArcReflectionMemorySelector.Select(new ArcMemorySelectionRequest
            {
                candidates = deterministic,
                currentYear = 5504,
                maxMemories = 3,
                minMemories = 1,
                seed = 99,
            });
            ArcMemorySelectionResult deterministicB = ArcReflectionMemorySelector.Select(new ArcMemorySelectionRequest
            {
                candidates = deterministic,
                currentYear = 5504,
                maxMemories = 3,
                minMemories = 1,
                seed = 99,
            });
            AssertEqual("arc selector deterministic for fixed seed",
                JoinCandidateIds(deterministicA.selected),
                JoinCandidateIds(deterministicB.selected));

            ArcMemorySelectionResult empty = ArcReflectionMemorySelector.Select(new ArcMemorySelectionRequest
            {
                candidates = new List<ArcMemoryCandidate>(),
                currentYear = 5504,
                maxMemories = 8,
                minMemories = 3,
            });
            AssertTrue("arc selector empty candidates are insufficient", !empty.hasEnoughMemories);

            string longText = "01234567890123456789";
            AssertEqual("arc selector memory text truncates", "0123456789...",
                ArcReflectionMemorySelector.MemoryText(ArcCandidate("clip", 1, "Work", "work", false, false, false, longText), 10));
        }

        private static void TestPromptCaptureFormatting()
        {
            string captured = DiaryPromptCapture.Format("System text.", "event: test\npov: Alice");
            AssertEqual(
                "prompt capture combined",
                "SYSTEM PROMPT\nSystem text.\n\nUSER PROMPT\nevent: test\npov: Alice",
                captured);

            string blankSystem = DiaryPromptCapture.Format(null, "user only");
            AssertEqual("prompt capture null system", "SYSTEM PROMPT\n\nUSER PROMPT\nuser only", blankSystem);
        }

        private static void TestApiLaneSelector()
        {
            AssertEqual("balanced first", 0,
                ApiLaneSelector.SelectPrimaryIndex(3, ApiLaneRoutingMode.Balanced, 0, Ready(true, true, true)));
            AssertEqual("balanced second", 1,
                ApiLaneSelector.SelectPrimaryIndex(3, ApiLaneRoutingMode.Balanced, 1, Ready(true, true, true)));
            AssertEqual("balanced wraps", 0,
                ApiLaneSelector.SelectPrimaryIndex(3, ApiLaneRoutingMode.Balanced, 3, Ready(true, true, true)));
            AssertEqual("balanced skips cooling", 2,
                ApiLaneSelector.SelectPrimaryIndex(3, ApiLaneRoutingMode.Balanced, 1, Ready(false, true, true)));

            AssertEqual("prefer top row share 1", 0,
                ApiLaneSelector.SelectPrimaryIndex(3, ApiLaneRoutingMode.PreferTopRows, 0, Ready(true, true, true)));
            AssertEqual("prefer top row share 2", 0,
                ApiLaneSelector.SelectPrimaryIndex(3, ApiLaneRoutingMode.PreferTopRows, 1, Ready(true, true, true)));
            AssertEqual("prefer top row share 3", 0,
                ApiLaneSelector.SelectPrimaryIndex(3, ApiLaneRoutingMode.PreferTopRows, 2, Ready(true, true, true)));
            AssertEqual("prefer second row share", 1,
                ApiLaneSelector.SelectPrimaryIndex(3, ApiLaneRoutingMode.PreferTopRows, 3, Ready(true, true, true)));
            AssertEqual("prefer last row share", 2,
                ApiLaneSelector.SelectPrimaryIndex(3, ApiLaneRoutingMode.PreferTopRows, 5, Ready(true, true, true)));
            AssertEqual("prefer skips cooling top", 1,
                ApiLaneSelector.SelectPrimaryIndex(3, ApiLaneRoutingMode.PreferTopRows, 0, Ready(false, true, true)));

            AssertEqual("failover only first ready", 1,
                ApiLaneSelector.SelectPrimaryIndex(3, ApiLaneRoutingMode.FailoverOnly, 10, Ready(false, true, true)));
            AssertEqual("all cooling falls back to first", 0,
                ApiLaneSelector.SelectPrimaryIndex(3, ApiLaneRoutingMode.FailoverOnly, 10, Ready(false, false, false)));
            AssertEqual("forced model matches by trimmed case-insensitive model", 1,
                ApiLaneSelector.SelectForcedModelIndex(new List<string> { "fast", " Story-Model ", "fallback" }, "story-model"));
            AssertEqual("blank forced model ignored", -1,
                ApiLaneSelector.SelectForcedModelIndex(new List<string> { "fast" }, " "));
            AssertEqual("unknown forced model ignored", -1,
                ApiLaneSelector.SelectForcedModelIndex(new List<string> { "fast" }, "missing"));
        }

        private static void TestApiEndpointPolicy()
        {
            AssertEqual("normalize chat compatibility", ApiCompatibilityMode.OpenAIChatCompletions,
                ApiEndpointPolicy.NormalizeApiMode(ApiCompatibilityMode.OpenAIChatCompletions));
            AssertEqual("normalize responses compatibility", ApiCompatibilityMode.OpenAIResponses,
                ApiEndpointPolicy.NormalizeApiMode(ApiCompatibilityMode.OpenAIResponses));
            AssertEqual("normalize invalid compatibility", ApiCompatibilityMode.OpenAIChatCompletions,
                ApiEndpointPolicy.NormalizeApiMode((ApiCompatibilityMode)999));

            AssertEqual("normalize bearer auth", ApiAuthMode.BearerToken,
                ApiEndpointPolicy.NormalizeAuthMode(ApiAuthMode.BearerToken));
            AssertEqual("normalize query auth", ApiAuthMode.QueryParameterKey,
                ApiEndpointPolicy.NormalizeAuthMode(ApiAuthMode.QueryParameterKey));
            AssertEqual("normalize legacy api-key auth", ApiAuthMode.CustomHeader,
                ApiEndpointPolicy.NormalizeAuthMode(ApiAuthMode.ApiKeyHeader));
            AssertEqual("normalize invalid auth", ApiAuthMode.BearerToken,
                ApiEndpointPolicy.NormalizeAuthMode((ApiAuthMode)999));
            AssertEqual("effective key trims authenticated lanes", "secret",
                ApiEndpointPolicy.EffectiveApiKey(ApiAuthMode.BearerToken, " secret "));
            AssertEqual("effective key ignores no-auth stale key", string.Empty,
                ApiEndpointPolicy.EffectiveApiKey(ApiAuthMode.None, "stale"));
            AssertEqual("custom header default", "x-goog-api-key",
                ApiEndpointPolicy.NormalizeCustomHeaderName(""));
            AssertEqual("custom header trims valid name", "x-api-key",
                ApiEndpointPolicy.NormalizeCustomHeaderName(" x-api-key "));
            AssertEqual("custom header rejects invalid spaces", "x-goog-api-key",
                ApiEndpointPolicy.NormalizeCustomHeaderName("bad header"));
            AssertEqual("reasoning trims and lowercases", "xhigh",
                ApiEndpointPolicy.NormalizeReasoningEffort(" XHIGH "));
            AssertEqual("reasoning invalid falls back", "default",
                ApiEndpointPolicy.NormalizeReasoningEffort("extreme"));

            AssertEqual("cooldown zero is first failure", 10, ApiEndpointPolicy.CooldownSecondsForFailures(0));
            AssertEqual("cooldown first failure", 10, ApiEndpointPolicy.CooldownSecondsForFailures(1));
            AssertEqual("cooldown second failure", 20, ApiEndpointPolicy.CooldownSecondsForFailures(2));
            AssertEqual("cooldown third failure", 40, ApiEndpointPolicy.CooldownSecondsForFailures(3));
            AssertEqual("cooldown fifth failure", 160, ApiEndpointPolicy.CooldownSecondsForFailures(5));
            AssertEqual("cooldown caps", 300, ApiEndpointPolicy.CooldownSecondsForFailures(6));
            AssertEqual("cooldown remains capped", 300, ApiEndpointPolicy.CooldownSecondsForFailures(20));

            // Retry-After combines with the exponential backoff: the longer of the two wins, a
            // missing/negative header keeps the local backoff, and a garbage value is capped.
            AssertEqual("no retry-after uses local backoff", 10, ApiEndpointPolicy.EffectiveCooldownSeconds(1, 0));
            AssertEqual("negative retry-after uses local backoff", 20, ApiEndpointPolicy.EffectiveCooldownSeconds(2, -5));
            AssertEqual("retry-after shorter than backoff keeps backoff", 10, ApiEndpointPolicy.EffectiveCooldownSeconds(1, 3));
            AssertEqual("retry-after longer than backoff wins", 60, ApiEndpointPolicy.EffectiveCooldownSeconds(1, 60));
            AssertEqual("retry-after is capped at one hour", 3600, ApiEndpointPolicy.EffectiveCooldownSeconds(1, 999999));
        }

        private static void TestApiLaneIdentityAndLabels()
        {
            AssertEqual("gate normalizes endpoint suffix and trims model",
                ApiLaneIdentity.ForGate(
                    "HTTPS://EXAMPLE.test/v1/chat/completions/",
                    " model ",
                    ApiCompatibilityMode.OpenAIChatCompletions,
                    ApiAuthMode.BearerToken,
                    string.Empty,
                    " secret "),
                ApiLaneIdentity.ForGate(
                    "https://example.test/v1",
                    "model",
                    ApiCompatibilityMode.OpenAIChatCompletions,
                    ApiAuthMode.BearerToken,
                    string.Empty,
                    "secret"));
            AssertEqual("gate ignores stale no-auth key",
                ApiLaneIdentity.ForGate("https://example.test/v1", "m", ApiCompatibilityMode.OpenAIChatCompletions, ApiAuthMode.None, string.Empty, "old"),
                ApiLaneIdentity.ForGate("https://example.test/v1", "m", ApiCompatibilityMode.OpenAIChatCompletions, ApiAuthMode.None, string.Empty, "new"));
            AssertTrue("attempt preserves raw model spacing",
                ApiLaneIdentity.ForAttempt("https://example.test/v1", "m ", ApiCompatibilityMode.OpenAIChatCompletions, ApiAuthMode.BearerToken, string.Empty, "k")
                != ApiLaneIdentity.ForAttempt("https://example.test/v1", "m", ApiCompatibilityMode.OpenAIChatCompletions, ApiAuthMode.BearerToken, string.Empty, "k"));
            AssertEqual("generation pin matches recorded full URL",
                ApiLaneIdentity.ForGeneration("https://example.test/v1", "m", ApiCompatibilityMode.OpenAIResponses),
                ApiLaneIdentity.ForGeneration("https://EXAMPLE.test/v1/responses", "m", ApiCompatibilityMode.OpenAIResponses));
            AssertTrue("generation auth includes effective key",
                ApiLaneIdentity.ForGenerationWithAuth("https://example.test/v1", "m", ApiCompatibilityMode.OpenAIResponses, ApiAuthMode.BearerToken, string.Empty, "a")
                != ApiLaneIdentity.ForGenerationWithAuth("https://example.test/v1", "m", ApiCompatibilityMode.OpenAIResponses, ApiAuthMode.BearerToken, string.Empty, "b"));
            AssertTrue("fetch target keeps raw URL exact",
                ApiLaneIdentity.ForFetchTarget("https://example.test/v1/", "k", ApiAuthMode.BearerToken, string.Empty, ApiCompatibilityMode.OpenAIChatCompletions)
                != ApiLaneIdentity.ForFetchTarget("https://example.test/v1", "k", ApiAuthMode.BearerToken, string.Empty, ApiCompatibilityMode.OpenAIChatCompletions));
            AssertEqual("connection test normalizes mode and reasoning",
                ApiLaneIdentity.ForConnectionTest("https://example.test/v1", "k", "m", ApiAuthMode.CustomHeader, "x-api-key", (ApiCompatibilityMode)999, " HIGH "),
                ApiLaneIdentity.ForConnectionTest("https://example.test/v1", "k", "m", ApiAuthMode.CustomHeader, "x-api-key", ApiCompatibilityMode.OpenAIChatCompletions, "high"));
            AssertEqual("label strips query and fragment",
                "m [OpenAIResponses] @ https://example.test/v1/responses",
                ApiLaneLabels.Label("https://example.test/v1?key=secret#frag", "m", ApiCompatibilityMode.OpenAIResponses));
            AssertEqual("label blank placeholders",
                "<blank-model> [OpenAIChatCompletions] @ <blank-url>",
                ApiLaneLabels.Label(" ", " ", ApiCompatibilityMode.OpenAIChatCompletions));
            AssertEqual("trim log makes one line", "first second third",
                ApiLaneLabels.TrimForLog(" first\nsecond\tthird "));
        }

        private static void TestApiRequestAuth()
        {
            AssertEqual("query auth appends key",
                "https://example.test/v1/chat/completions?existing=1&key=a%20b",
                ApiRequestAuth.ApplyQueryAuth(
                    "https://example.test/v1/chat/completions?existing=1",
                    "a b",
                    ApiAuthMode.QueryParameterKey));
            AssertEqual("query auth replaces existing key and preserves fragment",
                "https://example.test/v1/chat/completions?existing=1&key=new%20key#frag",
                ApiRequestAuth.ApplyQueryAuth(
                    "https://example.test/v1/chat/completions?key=old&existing=1&key=older#frag",
                    "new key",
                    ApiAuthMode.QueryParameterKey));
            AssertEqual("query auth unchanged without key",
                "https://example.test/v1/chat/completions",
                ApiRequestAuth.ApplyQueryAuth(
                    "https://example.test/v1/chat/completions",
                    "",
                    ApiAuthMode.QueryParameterKey));

            using (HttpRequestMessage bearer = new HttpRequestMessage(HttpMethod.Post, "https://example.test"))
            {
                ApiRequestAuth.ApplyHeaders(bearer, "secret", ApiAuthMode.BearerToken);
                AssertEqual("bearer scheme", "Bearer", bearer.Headers.Authorization.Scheme);
                AssertEqual("bearer parameter", "secret", bearer.Headers.Authorization.Parameter);
            }

            using (HttpRequestMessage customKey = new HttpRequestMessage(HttpMethod.Post, "https://example.test"))
            {
                ApiRequestAuth.ApplyHeaders(customKey, "secret", ApiAuthMode.CustomHeader, "x-goog-api-key");
                AssertHeader("custom x-goog-api-key header", customKey, "x-goog-api-key", "secret");
                AssertTrue("custom header has no bearer", customKey.Headers.Authorization == null);
            }

            using (HttpRequestMessage xApiKey = new HttpRequestMessage(HttpMethod.Post, "https://example.test"))
            {
                ApiRequestAuth.ApplyHeaders(xApiKey, "secret", ApiAuthMode.XApiKeyHeader, "");
                AssertHeader("x-api-key header", xApiKey, "x-api-key", "secret");
            }

            using (HttpRequestMessage none = new HttpRequestMessage(HttpMethod.Post, "https://example.test"))
            {
                ApiRequestAuth.ApplyHeaders(none, "secret", ApiAuthMode.None);
                AssertTrue("none has no bearer", none.Headers.Authorization == null);
                IEnumerable<string> values;
                AssertTrue("none has no api-key", !none.Headers.TryGetValues("api-key", out values));
            }
        }

        private static void TestLlmRequestJsonBuilder()
        {
            string chat = LlmRequestJsonBuilder.Build(new LlmRequestJsonInput
            {
                apiMode = ApiCompatibilityMode.OpenAIChatCompletions,
                modelName = "chat \"model\"",
                systemPrompt = "  System\nprompt  ",
                rawText = "User line\twith \"quote\" and \\slash" + (char)1,
                temperature = 0.7f,
                maxTokens = 64,
                reasoningEffort = " HIGH "
            });
            AssertEqual(
                "chat request json",
                "{\"model\":\"chat \\\"model\\\"\",\"messages\":[{\"role\":\"system\",\"content\":\"System\\nprompt\"},{\"role\":\"user\",\"content\":\"User line\\twith \\\"quote\\\" and \\\\slash\\u0001\"}],\"temperature\":0.7,\"max_tokens\":64,\"reasoning_effort\":\"high\"}",
                chat);

            string chatDefault = LlmRequestJsonBuilder.Build(new LlmRequestJsonInput
            {
                apiMode = (ApiCompatibilityMode)999,
                modelName = "chat",
                rawText = "Only user",
                temperature = 0.25f,
                maxTokens = 8,
                reasoningEffort = "default"
            });
            AssertEqual(
                "chat request default reasoning",
                "{\"model\":\"chat\",\"messages\":[{\"role\":\"user\",\"content\":\"Only user\"}],\"temperature\":0.25,\"max_tokens\":8}",
                chatDefault);

            // "none" means reasoning OFF for Chat Completions. It must be expressed by omitting
            // the field, not by sending reasoning_effort:"none" -- OpenAI-compatible gateways
            // (notably Google's, for Gemma) reject that with HTTP 400 "Thinking budget is not
            // supported for this model." since they try to apply a thinking budget to a
            // non-reasoning model.
            string chatNone = LlmRequestJsonBuilder.Build(new LlmRequestJsonInput
            {
                apiMode = ApiCompatibilityMode.OpenAIChatCompletions,
                modelName = "models/gemma-4-26b-a4b-it",
                rawText = "ping",
                temperature = 0.5f,
                maxTokens = 16,
                reasoningEffort = "none"
            });
            AssertEqual(
                "chat request none reasoning omits field",
                "{\"model\":\"models/gemma-4-26b-a4b-it\",\"messages\":[{\"role\":\"user\",\"content\":\"ping\"}],\"temperature\":0.5,\"max_tokens\":16}",
                chatNone);

            string responses = LlmRequestJsonBuilder.Build(new LlmRequestJsonInput
            {
                apiMode = ApiCompatibilityMode.OpenAIResponses,
                modelName = "o3",
                systemPrompt = " Instructions ",
                rawText = "Entry",
                temperature = 1f,
                maxTokens = 64,
                reasoningEffort = "medium"
            });
            AssertEqual(
                "responses request json",
                "{\"model\":\"o3\",\"input\":\"Entry\",\"temperature\":1,\"max_output_tokens\":192,\"instructions\":\"Instructions\",\"reasoning\":{\"effort\":\"medium\"}}",
                responses);

            string responsesNone = LlmRequestJsonBuilder.Build(new LlmRequestJsonInput
            {
                apiMode = ApiCompatibilityMode.OpenAIResponses,
                modelName = "o3",
                systemPrompt = " ",
                rawText = string.Empty,
                temperature = 0f,
                maxTokens = 64,
                reasoningEffort = "none"
            });
            AssertEqual(
                "responses request none reasoning",
                "{\"model\":\"o3\",\"input\":\"\",\"temperature\":0,\"max_output_tokens\":64,\"reasoning\":{\"effort\":\"none\"}}",
                responsesNone);

            // A non-finite temperature must never serialize to "NaN"/"Infinity": JSON has no such
            // literal, so it would invalidate the whole body and every endpoint would 400. The
            // builder substitutes a neutral, valid temperature (1) instead.
            string nanTemp = LlmRequestJsonBuilder.Build(new LlmRequestJsonInput
            {
                apiMode = ApiCompatibilityMode.OpenAIChatCompletions,
                modelName = "m",
                rawText = "ping",
                temperature = float.NaN,
                maxTokens = 16,
                reasoningEffort = "default"
            });
            AssertTrue("NaN temperature never reaches the request body", !nanTemp.Contains("NaN"));
            AssertTrue("NaN temperature falls back to a valid value", nanTemp.Contains("\"temperature\":1,"));

            string infTemp = LlmRequestJsonBuilder.Build(new LlmRequestJsonInput
            {
                apiMode = ApiCompatibilityMode.OpenAIResponses,
                modelName = "m",
                rawText = "ping",
                temperature = float.PositiveInfinity,
                maxTokens = 16,
                reasoningEffort = "default"
            });
            AssertTrue("Infinity temperature never reaches the request body", !infTemp.Contains("Infinity"));
            AssertTrue("Infinity temperature falls back to a valid value", infTemp.Contains("\"temperature\":1,"));
        }

        // ModelReasoningCapability: parse the OpenRouter-shape "reasoning" object from a /models
        // entry, and the effort-clamping policy that keeps the outgoing request from carrying an
        // effort the model rejects (the root cause of "400 Thinking budget not supported").
        private static void TestModelReasoningCapability()
        {
            // OpenRouter-style entry with an enumerated effort list.
            Dictionary<string, object> supported = MiniJson.Deserialize(
                "{\"id\":\"o3-mini\",\"reasoning\":{\"default_enabled\":true,\"default_effort\":\"medium\",\"supported_efforts\":[\"low\",\"medium\",\"high\"],\"supports_max_tokens\":true}}")
                as Dictionary<string, object>;
            ModelReasoningCapability cap = ModelReasoningCapability.FromModelEntry(supported);
            AssertTrue("supported model parsed", cap != null);
            AssertTrue("supported flag true", cap.Supported);
            AssertTrue("supports max tokens", cap.SupportsMaxTokens);
            AssertTrue("three efforts listed", cap.SupportedEfforts.Count == 3);
            AssertEqual("default effort medium", "medium", cap.DefaultEffort);

            // Entry without a reasoning object -> null (provider does not advertise capability).
            Dictionary<string, object> plain = MiniJson.Deserialize(
                "{\"id\":\"gpt-4o\"}") as Dictionary<string, object>;
            AssertTrue("plain model yields null capability", ModelReasoningCapability.FromModelEntry(plain) == null);

            // Non-reasoning model explicitly marked unsupported.
            Dictionary<string, object> unsupported = MiniJson.Deserialize(
                "{\"id\":\"gemma-3\",\"reasoning\":{\"default_enabled\":false}}") as Dictionary<string, object>;
            ModelReasoningCapability unsup = ModelReasoningCapability.FromModelEntry(unsupported);
            AssertTrue("unsupported flag false", unsup != null && !unsup.Supported);

            // Effort implied by presence of a supported_efforts list even when default_enabled omitted.
            Dictionary<string, object> implied = MiniJson.Deserialize(
                "{\"id\":\"deepseek-r1\",\"reasoning\":{\"supported_efforts\":[\"high\"]}}") as Dictionary<string, object>;
            ModelReasoningCapability impliedCap = ModelReasoningCapability.FromModelEntry(implied);
            AssertTrue("effort list implies supported", impliedCap != null && impliedCap.Supported);

            // ---- EffectiveReasoningEffort clamping policy ----

            // Unknown capability (null): chosen effort passes through unchanged (graceful degrade).
            AssertEqual("null capability passthrough", "high",
                ModelReasoningCapability.EffectiveReasoningEffort("high", null));

            // "default" (no override) stays default regardless of capability.
            AssertEqual("default stays default with capability", "default",
                ModelReasoningCapability.EffectiveReasoningEffort("default", cap));

            // "none" means explicitly off and must not be clamped back to provider reasoning.
            AssertEqual("none stays none with capability", "none",
                ModelReasoningCapability.EffectiveReasoningEffort("none", cap));

            // Unsupported model forces "none" -- the direct fix for the Gemma 400 error.
            AssertEqual("unsupported forces none", "none",
                ModelReasoningCapability.EffectiveReasoningEffort("high", unsup));

            // Explicit effort the model lists as supported is kept.
            AssertEqual("supported explicit kept", "low",
                ModelReasoningCapability.EffectiveReasoningEffort("low", cap));

            // Explicit effort the model does NOT list clamps to the provider default.
            AssertEqual("unsupported explicit clamps to default", "medium",
                ModelReasoningCapability.EffectiveReasoningEffort("xhigh", cap));

            // No default effort and no list but supported -> default (omit reasoning object).
            Dictionary<string, object> bareSupported = MiniJson.Deserialize(
                "{\"id\":\"r1\",\"reasoning\":{\"default_enabled\":true}}") as Dictionary<string, object>;
            ModelReasoningCapability bare = ModelReasoningCapability.FromModelEntry(bareSupported);
            AssertEqual("bare supported explicit clamps to default", "default",
                ModelReasoningCapability.EffectiveReasoningEffort("high", bare));

            // List with no default: clamp to the highest supported effort (player asked for reasoning).
            Dictionary<string, object> noDefault = MiniJson.Deserialize(
                "{\"id\":\"q\",\"reasoning\":{\"supported_efforts\":[\"low\",\"medium\"]}}") as Dictionary<string, object>;
            ModelReasoningCapability nd = ModelReasoningCapability.FromModelEntry(noDefault);
            AssertEqual("no default clamps to highest", "medium",
                ModelReasoningCapability.EffectiveReasoningEffort("xhigh", nd));
        }

        private static void TestDomainClassifier()
        {
            AssertEqual("romance marker domain", "Romance",
                DiaryEventDomainClassifier.DomainForContext("romance=Spouse; label=spouse; kind=married"));
            AssertEqual("mental marker domain", "MentalState",
                DiaryEventDomainClassifier.DomainForContext("mental_state=SocialFighting; label=social fight"));
            AssertEqual("interaction fallback domain", "Interaction",
                DiaryEventDomainClassifier.DomainForContext("def=Chat; label=chat"));
            AssertEqual("raid marker domain", "Raid",
                DiaryEventDomainClassifier.DomainForContext("raid=RaidEnemy; label=enemy raid; faction=Pirate; points=350"));
            AssertEqual("quest marker domain", "Quest",
                DiaryEventDomainClassifier.DomainForContext("quest=OpportunityQuest; signal=accepted; label=cache; faction=Outlander; rewards=Silver x100"));
            AssertEqual("quest classifier uses lifecycle signal",
                "completed",
                DiaryEventDomainClassifier.GroupClassifierKey(
                    "Quest",
                    "quest=OpportunityQuest; signal=completed; label=cache; faction=Outlander; rewards=Silver x100",
                    "OpportunityQuest"));
            AssertEqual("quest classifier falls back without signal",
                "OpportunityQuest",
                DiaryEventDomainClassifier.GroupClassifierKey(
                    "Quest",
                    "quest=OpportunityQuest; label=cache; faction=Outlander; rewards=Silver x100",
                    "OpportunityQuest"));
            AssertEqual("ritual marker domain", "Ritual",
                DiaryEventDomainClassifier.DomainForContext("ritual=Ritual_Speech; ritual_title=Leader's address; ritual_role=author"));
            AssertEqual("psychic ritual marker domain", "Ritual",
                DiaryEventDomainClassifier.DomainForContext("psychic_ritual=VoidProvocation; psychic_ritual_perspective=invoker"));
            AssertEqual("ability marker domain", "Ability",
                DiaryEventDomainClassifier.DomainForContext("ability=Stun; ability_label=stun; ability_category=Psycast"));
            AssertEqual("progression marker domain", "Progression",
                DiaryEventDomainClassifier.DomainForContext("progression=SkillMilestone; progression_kind=skill"));
            AssertEqual("arc reflection marker domain", "Reflection",
                DiaryEventDomainClassifier.DomainForContext("arc_reflection=true; arc_year=5504"));
            AssertEqual("external marker wins over adapter context markers", "External",
                DiaryEventDomainClassifier.DomainForContext("external=mod_key; source=author.adapter; thought=Inspired; work=Mining"));
            AssertEqual("ritual classifier includes behavior when present",
                "Ritual_Speech;RitualBehaviorWorker_ThroneSpeech",
                DiaryEventDomainClassifier.GroupClassifierKey(
                    "Ritual",
                    "ritual=Ritual_Speech; ritual_title=Leader's address; ritual_behavior=RitualBehaviorWorker_ThroneSpeech",
                    "Ritual_Speech"));
            AssertEqual("ritual classifier falls back without behavior",
                "Ritual_Speech",
                DiaryEventDomainClassifier.GroupClassifierKey(
                    "Ritual",
                    "ritual=Ritual_Speech; ritual_title=Leader's address",
                    "Ritual_Speech"));
            AssertEqual("psychic ritual classifier uses psychic prefix",
                "PsychicRitual;VoidProvocation",
                DiaryEventDomainClassifier.GroupClassifierKey(
                    "Ritual",
                    "psychic_ritual=VoidProvocation; psychic_ritual_perspective=invoker",
                    "VoidProvocation"));
            AssertEqual("ability classifier includes category when present",
                "Stun;Psycast",
                DiaryEventDomainClassifier.GroupClassifierKey(
                    "Ability",
                    "ability=Stun; ability_label=stun; ability_category=Psycast",
                    "Stun"));
            AssertEqual("hediff classifier includes added-part token",
                "BionicArm_addedpart",
                DiaryEventDomainClassifier.GroupClassifierKey(
                    "Hediff",
                    "hediff=BionicArm; label=bionic arm; part_kind=addedpart; part_tier=bionic",
                    "BionicArm"));
            AssertEqual("hediff classifier includes organic added-part token",
                "Tentacle_addedpart_organicpart",
                DiaryEventDomainClassifier.GroupClassifierKey(
                    "Hediff",
                    "hediff=Tentacle; label=tentacle; part_kind=addedpart_organicpart; part_tier=anomalous",
                    "Tentacle"));
            AssertEqual("hediff classifier includes missing-part token",
                "MissingBodyPart_missingpart",
                DiaryEventDomainClassifier.GroupClassifierKey(
                    "Hediff",
                    "hediff=MissingBodyPart; label=missing arm; part_kind=missingpart; body_attitude=grieving",
                    "MissingBodyPart"));
            AssertEqual("hediff classifier falls back without part kind",
                "Flu",
                DiaryEventDomainClassifier.GroupClassifierKey(
                    "Hediff",
                    "hediff=Flu; label=flu",
                    "Flu"));
            AssertEqual("progression classifier keeps synthetic defName",
                "SkillMilestone",
                DiaryEventDomainClassifier.GroupClassifierKey(
                    "Progression",
                    "progression=SkillMilestone; progression_kind=skill",
                    "SkillMilestone"));
            AssertEqual("raid classifier keeps incident defName",
                "RaidEnemy",
                DiaryEventDomainClassifier.GroupClassifierKey(
                    "Raid",
                    "raid=RaidEnemy; label=enemy raid; faction=Pirate; points=350",
                    "RaidEnemy"));
            AssertTrue("romance marker is not interaction prompt",
                DiaryEventDomainClassifier.HasNonInteractionSourceMarker("romance=Lover; label=lover"));
            AssertTrue("raid marker is not interaction prompt",
                DiaryEventDomainClassifier.HasNonInteractionSourceMarker("raid=RaidEnemy; label=enemy raid"));
            AssertTrue("quest marker is not interaction prompt",
                DiaryEventDomainClassifier.HasNonInteractionSourceMarker("quest=QuestDef; signal=completed; label=name"));
            AssertTrue("ritual marker is not interaction prompt",
                DiaryEventDomainClassifier.HasNonInteractionSourceMarker("ritual=Ritual_Speech; ritual_title=Leader's address"));
            AssertTrue("psychic ritual marker is not interaction prompt",
                DiaryEventDomainClassifier.HasNonInteractionSourceMarker("psychic_ritual=VoidProvocation; psychic_ritual_perspective=invoker"));
            AssertTrue("ability marker is not interaction prompt",
                DiaryEventDomainClassifier.HasNonInteractionSourceMarker("ability=Stun; ability_label=stun"));
            AssertTrue("progression marker is not interaction prompt",
                DiaryEventDomainClassifier.HasNonInteractionSourceMarker("progression=SkillMilestone; progression_kind=skill"));
            AssertTrue("arc reflection marker is not interaction prompt",
                DiaryEventDomainClassifier.HasNonInteractionSourceMarker("arc_reflection=true; arc_year=5504"));
            AssertTrue("plain interaction stays interaction prompt",
                !DiaryEventDomainClassifier.HasNonInteractionSourceMarker("def=Chat; label=chat"));
        }

        // Pins the exact key/value/trimming semantics of DiaryContextFields, which the Diary tab's
        // sliced history indexer and the per-entry view builder call several times per event. The
        // implementation was rewritten to scan in place instead of context.Split(';') on every call;
        // these tests guard the observable behavior (including the legacy quirks) against drift.
        private static void TestContextFields()
        {
            AssertEqual("basic value", "Pawn_12",
                DiaryContextFields.Value("death_victim_id=Pawn_12; death_description=true", "death_victim_id"));
            AssertEqual("last segment value", "true",
                DiaryContextFields.Value("death_victim_id=Pawn_12; death_description=true", "death_description"));
            AssertEqual("missing key is empty", string.Empty,
                DiaryContextFields.Value("death_victim_id=Pawn_12; death_description=true", "arrival_victim_id"));
            AssertEqual("blank context is empty", string.Empty,
                DiaryContextFields.Value(string.Empty, "k"));
            AssertEqual("blank key is empty", string.Empty,
                DiaryContextFields.Value("k=1", "   "));

            // Case-insensitive key match (saved context keys are lower-case; lookups vary).
            AssertEqual("case-insensitive key", "V",
                DiaryContextFields.Value("Key=V", "key"));

            // Values are trimmed; surrounding segment whitespace and whitespace around '=' collapse.
            AssertEqual("value trimmed", "v",
                DiaryContextFields.Value("k =  v  ", "k"));
            AssertEqual("value with internal equals keeps them", "http://x?y=1",
                DiaryContextFields.Value("url=http://x?y=1; next=2", "url"));

            // Empty / whitespace-only segments and doubled separators are skipped, not matched.
            AssertEqual("empty segments skipped", "2",
                DiaryContextFields.Value("a=1;;b=2;;;", "b"));
            AssertEqual("whitespace-only segment skipped", "2",
                DiaryContextFields.Value("a=1;   ;b=2", "b"));

            // A segment with no key (no '=' or leading '=') is skipped, never matched as a key.
            AssertEqual("segment without equals skipped", string.Empty,
                DiaryContextFields.Value("novalue; k=1", "novalue"));
            AssertEqual("leading equals treated as empty key", string.Empty,
                DiaryContextFields.Value("=v; k=1", "=v"));

            // Exact key match avoids the Pawn_1 / Pawn_12 substring trap.
            AssertEqual("exact key prevents substring trap (Pawn_1)", "Pawn_1",
                DiaryContextFields.Value("a=Pawn_1; b=Pawn_12", "a"));
            AssertEqual("exact key prevents substring trap (Pawn_12)", "Pawn_12",
                DiaryContextFields.Value("a=Pawn_1; b=Pawn_12", "b"));

            // HasField: non-empty value present.
            AssertTrue("has field when present",
                DiaryContextFields.HasField("death_description=true; label=x", "death_description"));
            AssertTrue("has field false when absent",
                !DiaryContextFields.HasField("death_description=true; label=x", "arrival_description"));

            // FieldEquals: case-insensitive value compare; the EXPECTED value is not trimmed.
            AssertTrue("field equals case-insensitive value",
                DiaryContextFields.FieldEquals("death_description=TRUE", "death_description", "true"));
            AssertTrue("field equals does not trim expected",
                !DiaryContextFields.FieldEquals("k=true", "k", " true "));
            AssertTrue("field equals missing key is false",
                !DiaryContextFields.FieldEquals("k=true", "absent", "true"));

            // IsTrue convenience.
            AssertTrue("is true matches", DiaryContextFields.IsTrue("death_description=true", "death_description"));
            AssertTrue("is true rejects other text", !DiaryContextFields.IsTrue("death_description=yes", "death_description"));

            // HasMarker: "key=" form, "key=value" form, and plain substring fallback.
            AssertTrue("marker key= form present",
                DiaryContextFields.HasMarker("death_description=true; label=x", "death_description="));
            AssertTrue("marker key= form absent",
                !DiaryContextFields.HasMarker("label=x", "death_description="));
            AssertTrue("marker key=value form present",
                DiaryContextFields.HasMarker("signal=accepted; label=x", "signal=accepted"));
            AssertTrue("marker key=value form rejects mismatch",
                !DiaryContextFields.HasMarker("signal=completed; label=x", "signal=accepted"));
            AssertTrue("marker plain substring fallback",
                DiaryContextFields.HasMarker("someTaleData", "Tale"));
            AssertTrue("marker plain substring respects case-insensitive",
                DiaryContextFields.HasMarker("someTaleData", "tale"));
        }

        private static void TestEventPromptKeyCandidates()
        {
            List<string> keys = DiaryEventPromptKeys.CandidateKeys(
                new DiaryEventPayload { defName = "ModdedDinnerTalk" },
                "modded_feast",
                "ModdedDinnerTalk",
                "Interaction");
            AssertEqual("event prompt key exact first", "ModdedDinnerTalk", keys[0]);
            AssertEqual("event prompt key group second", "modded_feast", keys[1]);
            AssertEqual("event prompt key broad last", "Interaction", keys[2]);
            AssertEqual("event prompt key duplicate removed", 3, keys.Count);

            List<string> ritualKeys = DiaryEventPromptKeys.CandidateKeys(
                new DiaryEventPayload { defName = "Ritual_Speech" },
                "ritualRoyal",
                "Ritual_Speech;RitualBehaviorWorker_ThroneSpeech",
                "Ritual");
            AssertEqual("event prompt key classifier retained", "Ritual_Speech;RitualBehaviorWorker_ThroneSpeech", ritualKeys[2]);
            AssertEqual("event prompt key ritual fallback", "Ritual", ritualKeys[3]);
        }

        private static void TestResponsePostprocessorRules()
        {
            DiaryResponsePlan main = DiaryResponsePostprocessor.ApplySuccess(
                "One sentence. Two words without",
                DiaryResponseRules.ForRequest("e-main", DiaryPipelineRoles.Initiator, false, 20));
            AssertEqual("main trims fragment", "One sentence.", main.generatedText);
            AssertEqual("main raw response", "One sentence. Two words without", main.rawVisibleResponse);

            DiaryResponsePlan title = DiaryResponsePostprocessor.ApplySuccess(
                "A title without period",
                DiaryResponseRules.ForRequest("e-title", DiaryPipelineRoles.Initiator, true, 8));
            AssertEqual("title keeps fragment", "A title without period", title.generatedText);
            AssertEqual("title text", "A title without period", title.titleText);
        }

        private static void TestDirectSpeechParser()
        {
            List<DiaryDirectSpeechLine> allowed = new List<DiaryDirectSpeechLine>(
                DiaryDirectSpeechParser.Lines(
                    "I leaned closer.\n[[speech]]Enough.[[/speech]]\nThen I walked away.",
                    true,
                    "[[speech]]",
                    "[[/speech]]"));
            AssertEqual("direct speech line count", 3, allowed.Count);
            AssertTrue("direct speech parsed", allowed[1].directSpeech);
            AssertEqual("direct speech text", "Enough.", allowed[1].line);

            List<DiaryDirectSpeechLine> blocked = new List<DiaryDirectSpeechLine>(
                DiaryDirectSpeechParser.Lines(
                    "[[speech]]Enough.[[/speech]]",
                    false,
                    "[[speech]]",
                    "[[/speech]]"));
            AssertEqual("blocked line count", 1, blocked.Count);
            AssertTrue("blocked direct speech stripped to prose", !blocked[0].directSpeech);
            AssertEqual("blocked marker removal", "Enough.", blocked[0].line);

            string firstSpeech = DiaryDirectSpeechParser.FirstDirectSpeechBlock(
                "Before [[speech]]First.[[/speech]] after\n[[speech]]Second.[[/speech]]",
                "[[speech]]",
                "[[/speech]]");
            AssertEqual("first direct speech", "First.", firstSpeech);

            List<DiaryDirectSpeechLine> multipleSameLine = new List<DiaryDirectSpeechLine>(
                DiaryDirectSpeechParser.Lines(
                    "Before [[speech]]First.[[/speech]] between [[speech]]Second.[[/speech]] after",
                    true,
                    "[[speech]]",
                    "[[/speech]]"));
            AssertEqual("same-line speech block count", 5, multipleSameLine.Count);
            AssertTrue("same-line first speech parsed", multipleSameLine[1].directSpeech);
            AssertEqual("same-line first speech", "First.", multipleSameLine[1].line);
            AssertTrue("same-line second speech parsed", multipleSameLine[3].directSpeech);
            AssertEqual("same-line second speech", "Second.", multipleSameLine[3].line);

            List<DiaryDirectSpeechLine> unclosed = new List<DiaryDirectSpeechLine>(
                DiaryDirectSpeechParser.Lines(
                    "[[speech]]Unclosed",
                    true,
                    "[[speech]]",
                    "[[/speech]]"));
            AssertEqual("unclosed line count", 1, unclosed.Count);
            AssertTrue("unclosed marker is prose", !unclosed[0].directSpeech);
            AssertEqual("unclosed marker stripped", "Unclosed", unclosed[0].line);
        }

        private static void TestGeneratedTextKeepsTrailingSpeech()
        {
            // Regression: the incomplete-sentence trimmer used to delete a speech block on the final
            // line (it ends in "]]", not sentence punctuation), so neither the diary tab nor the
            // Social-log injection could find it. All three layouts must now keep the block.
            AssertEqual("trailing speech survives cleaning", "Enough.", FirstSpeechAfterClean(
                "I told Bob to stop.\n[[speech]]Enough.[[/speech]]"));
            AssertEqual("speech-only survives cleaning", "Enough.", FirstSpeechAfterClean(
                "[[speech]]Enough.[[/speech]]"));
            AssertEqual("speech then prose survives cleaning", "Enough.", FirstSpeechAfterClean(
                "I told Bob to stop.\n[[speech]]Enough.[[/speech]]\nThen I walked away."));

            // A genuinely truncated trailing fragment (no speech block) is still trimmed back to the
            // last complete sentence, so the speech guard does not weaken normal cleanup.
            AssertEqual("incomplete tail still trimmed", "It was calm.",
                LlmResponseParser.CleanGeneratedText("It was calm. Then suddenly the", 200, false));

            AssertEqual("closed speech before truncated prose is not a cut point",
                "[[speech]]Hi[[/speech]] He waved",
                LlmResponseParser.CleanGeneratedText("[[speech]]Hi[[/speech]] He waved [[speech>", 200, false));
        }

        private static string FirstSpeechAfterClean(string raw)
        {
            string cleaned = LlmResponseParser.CleanGeneratedText(raw, 200, false);
            return DiaryDirectSpeechParser.FirstDirectSpeechBlock(cleaned, "[[speech]]", "[[/speech]]");
        }

        private static DiaryEventPayload PairPayload(string eventId, string eventNoun, string initiatorText, string recipientText)
        {
            return new DiaryEventPayload
            {
                eventId = eventId,
                solo = false,
                eventNoun = eventNoun,
                instruction = "Remember the colony.",
                gameContext = string.Empty,
                initiator = new DiaryPovPayload
                {
                    role = DiaryPipelineRoles.Initiator,
                    name = "Alice",
                    rawText = initiatorText,
                    pawnSummary = "Alice is brave.",
                    surroundings = "cold hallway",
                    continuity = "rivals",
                    lastOpener = "I should have waited.",
                    weapon = "knife"
                },
                recipient = new DiaryPovPayload
                {
                    role = DiaryPipelineRoles.Recipient,
                    name = "Bob",
                    rawText = recipientText,
                    pawnSummary = "Bob is careful.",
                    surroundings = "cold hallway",
                    continuity = "rivals",
                    weapon = "club"
                },
                display = new DiaryDisplayPayload { important = true }
            };
        }

        private static DiaryEventPayload SoloPayload(string eventId, string eventNoun, string text)
        {
            return new DiaryEventPayload
            {
                eventId = eventId,
                solo = true,
                eventNoun = eventNoun,
                gameContext = string.Empty,
                initiator = new DiaryPovPayload
                {
                    role = DiaryPipelineRoles.Initiator,
                    name = "Alice",
                    rawText = text,
                    pawnSummary = "Alice is brave.",
                    surroundings = "workshop",
                    lastOpener = "The workshop was quiet.",
                    previousEntryEnding = "I stopped before the door. The rain was still coming down."
                },
                display = new DiaryDisplayPayload { important = true }
            };
        }

        private static DiaryEventPayload DeathPayload()
        {
            return new DiaryEventPayload
            {
                eventId = "e-death",
                solo = true,
                eventNoun = "death",
                neutralText = "Alice died from blood loss.",
                gameContext = "death_victim_role=initiator; death_victim=Alice; damage=Cut; weapon=knife; death_surroundings=near the freezer",
                hasDeathDescription = true,
                initiator = new DiaryPovPayload
                {
                    role = DiaryPipelineRoles.Initiator,
                    name = "Alice",
                    pawnSummary = "Alice was the colony doctor.",
                    surroundings = "near the freezer"
                },
                display = new DiaryDisplayPayload { important = true }
            };
        }

        private static DiaryEventPayload ArrivalPayload()
        {
            return new DiaryEventPayload
            {
                eventId = "e-arrival",
                solo = true,
                eventNoun = "arrival",
                neutralText = "Alice joined the colony from a crash pod.",
                gameContext = "arrival_pawn=Alice; arrival_source=game_start; scenario_name=Crashlanded; scenario_description=The three survivors awoke among wreckage; childhood_backstory=Urbworld urchin; childhood_backstory_description=Alice grew up among alley markets. She learned whom to trust and when to run.; childhood_backstory_effects=skill bonuses: Social +3, Melee +2 | disabled work tags: Intellectual; adulthood_backstory=Field medic; adulthood_backstory_description=Alice spent years patching workers after industrial accidents. The sound of alarms became routine.; adulthood_backstory_effects=skill bonuses: Medical +6 | disabled work: Artistic; arrival_surroundings=rainy field",
                hasArrivalDescription = true,
                initiator = new DiaryPovPayload
                {
                    role = DiaryPipelineRoles.Initiator,
                    name = "Alice",
                    pawnSummary = "Alice was a careful doctor.",
                    surroundings = "rainy field"
                },
                display = new DiaryDisplayPayload { important = true }
            };
        }

        private static DiaryPolicySnapshot Policy(bool combat, bool important)
        {
            DiaryPolicySnapshot policy = new DiaryPolicySnapshot
            {
                group = new DiaryGroupPolicy
                {
                    combat = combat,
                    important = important,
                    tone = "tense",
                    eventPrompt = "Write this event type.",
                    eventEnhancement = "Keep the event focused."
                }
            };

            AddTemplate(policy, DiaryPipelineTemplates.PairDefault, includePersona: true);
            AddTemplate(policy, DiaryPipelineTemplates.PairImportant, includePersona: true);
            AddTemplate(policy, DiaryPipelineTemplates.PairCombat, includePersona: true);
            AddTemplate(policy, DiaryPipelineTemplates.PairBatched, includePersona: true);
            AddTemplate(policy, DiaryPipelineTemplates.SoloDefault, includePersona: true);
            AddTemplate(policy, DiaryPipelineTemplates.SoloImportant, includePersona: true);
            AddTemplate(policy, DiaryPipelineTemplates.SoloInternalState, includePersona: true);
            AddTemplate(policy, DiaryPipelineTemplates.SoloBatched, includePersona: true);
            AddTemplate(policy, DiaryPipelineTemplates.SoloDayReflection, includePersona: true);
            AddTemplate(policy, DiaryPipelineTemplates.SoloQuadrumReflection, includePersona: true);
            AddTemplate(policy, DiaryPipelineTemplates.SoloArcReflection, includePersona: true);
            AddTemplate(policy, DiaryPipelineTemplates.DeathDescription, includePersona: false);
            AddTemplate(policy, DiaryPipelineTemplates.ArrivalDescription, includePersona: false);
            AddTemplate(policy, DiaryPipelineTemplates.Title, includePersona: false);
            return policy;
        }

        private static void AddTemplate(DiaryPolicySnapshot policy, string key, bool includePersona)
        {
            List<DiaryPromptFieldPolicy> fields;
            if (key == DiaryPipelineTemplates.Title)
            {
                fields = Fields(Field("entry", "EntryText"));
            }
            else if (key == DiaryPipelineTemplates.DeathDescription)
            {
                fields = Fields(
                    Field("event", "EventNoun"),
                    Field("event prompt", "EventPrompt"),
                    Field("event enhancement", "EventEnhancement"),
                    Field("deceased", "DeathVictim"),
                    Field("what happened", "NeutralText"),
                    Field("death facts", "DeathFacts"),
                    Field("deceased pawn", "DeathPawnSummary"),
                    Field("setting", "DeathSetting"));
            }
            else if (key == DiaryPipelineTemplates.ArrivalDescription)
            {
                fields = Fields(
                    Field("event", "EventNoun"),
                    Field("event prompt", "EventPrompt"),
                    Field("event enhancement", "EventEnhancement"),
                    Field("colonist", "ArrivalPawn"),
                    Field("what happened", "NeutralText"),
                    Field("arrival facts", "ArrivalFacts"),
                    Field("colonist pawn", "PawnSummary"),
                    Field("setting", "Setting"));
            }
            else
            {
                fields = Fields(
                    Field("event", "EventNoun"),
                    Field("pov", "PovName"),
                    Field("what happened", "PovText"),
                    Field("event prompt", "EventPrompt"),
                    Field("event enhancement", "EventEnhancement"),
                    ContextField("quest name", "quest_label"),
                    ContextField("quest lifecycle", "quest_signal"),
                    ContextField("quest faction", "quest_faction"),
                    ContextField("quest rewards", "quest_rewards"),
                    ContextField("progression kind", "progression_kind"),
                    ContextField("skill", "skill"),
                    ContextField("skill level", "skill_level"),
                    ContextField("passion", "passion"),
                    ContextField("psylink level", "psylink_level"),
                    ContextField("previous psylink level", "previous_psylink_level"),
                    ContextField("xenotype", "xenotype"),
                    ContextField("previous xenotype", "previous_xenotype"),
                    ContextField("major_xenotype", "major_xenotype"),
                    ContextField("royal title", "title"),
                    ContextField("previous royal title", "previous_title"),
                    ContextField("arc year", "arc_year"),
                    ContextField("selected memories", "selected_memories"),
                    Field("weapon", "Weapon"),
                    Field("important context", "PromptEnchantment"),
                    Field("previous diary ending (continue from this)", "PreviousEntryEnding"),
                    Field("initiator entry", "HiddenInitiatorEntry"),
                    Field("entry", "EntryText"));
            }

            DiaryTemplatePolicy template = new DiaryTemplatePolicy
            {
                templateKey = key,
                systemPrompt = key == DiaryPipelineTemplates.Title ? "Title system" : "System " + key,
                finalInstruction = key == DiaryPipelineTemplates.Title ? "Make title." : "Write entry.",
                recipientFinalInstruction = "Recipient followup.",
                includePersona = includePersona,
                includePromptEnchantment = key != DiaryPipelineTemplates.Title,
                appendDirectSpeechInstruction = key != DiaryPipelineTemplates.Title
                    && key != DiaryPipelineTemplates.SoloArcReflection,
                maxTokens = key == DiaryPipelineTemplates.SoloQuadrumReflection ? 350
                    : key == DiaryPipelineTemplates.SoloArcReflection ? 420 : 0,
                fields = fields
            };
            policy.templates.Add(template);
        }

        private static List<DiaryPromptFieldPolicy> Fields(params DiaryPromptFieldPolicy[] fields)
        {
            return new List<DiaryPromptFieldPolicy>(fields);
        }

        private static DiaryPromptFieldPolicy Field(string label, string source)
        {
            return new DiaryPromptFieldPolicy { label = label, source = source, enabled = true };
        }

        private static DiaryPromptFieldPolicy ContextField(string label, string contextKey)
        {
            return new DiaryPromptFieldPolicy
            {
                label = label,
                source = "GameContext",
                contextKey = contextKey,
                enabled = true
            };
        }

        private static ArcMemoryCandidate ArcCandidate(string eventId, int tick, string domain,
            string groupKey, bool important, bool reflection, bool deathDescription, string text,
            int year = 5504)
        {
            return new ArcMemoryCandidate
            {
                eventId = eventId,
                pawnId = "P1",
                povRole = DiaryPipelineRoles.Initiator,
                tick = tick,
                year = year,
                date = tick.ToString(),
                defName = groupKey,
                domain = domain,
                groupKey = groupKey,
                label = text,
                text = text,
                generatedText = text,
                title = string.Empty,
                important = important,
                reflection = reflection,
                deathDescription = deathDescription,
                sameQuadrum = false,
                progression = string.Equals(domain, "Progression", StringComparison.OrdinalIgnoreCase),
                highStakes = string.Equals(domain, "Raid", StringComparison.OrdinalIgnoreCase),
            };
        }

        private static string JoinCandidateIds(List<ArcMemoryCandidate> values)
        {
            List<string> ids = new List<string>();
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    ids.Add(values[i]?.eventId ?? string.Empty);
                }
            }

            return string.Join(",", ids.ToArray());
        }

        private static List<bool> Ready(params bool[] values)
        {
            return new List<bool>(values);
        }

        private static PromptEnchantmentCandidate PromptCandidate(string priorityText, string conditionText,
            float weight, string[] impactCues, string[] configuredCues, string sourceHediffDefName = null)
        {
            return new PromptEnchantmentCandidate
            {
                priorityText = priorityText,
                conditionText = conditionText,
                weight = weight,
                sourceHediffDefName = sourceHediffDefName,
                impactCues = impactCues == null
                    ? new List<string>()
                    : new List<string>(impactCues),
                configuredCues = configuredCues == null
                    ? new List<string>()
                    : new List<string>(configuredCues)
            };
        }

        private static string RepoPath(params string[] parts)
        {
            string directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "DOCUMENTATION.md"))
                    && Directory.Exists(Path.Combine(directory, "1.6")))
                {
                    string path = directory;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        path = Path.Combine(path, parts[i]);
                    }

                    return path;
                }

                DirectoryInfo parent = Directory.GetParent(directory);
                directory = parent?.FullName;
            }

            throw new InvalidOperationException("Could not locate repository root from " + AppContext.BaseDirectory);
        }

        private static XElement FindDef(XDocument document, string elementName, string defName)
        {
            foreach (XElement element in document.Descendants(elementName))
            {
                if (string.Equals(ChildValue(element, "defName"), defName, StringComparison.OrdinalIgnoreCase))
                {
                    return element;
                }
            }

            return null;
        }

        private static bool HasHediffDefName(XElement def, string hediffDefName)
        {
            if (def == null)
            {
                return false;
            }

            foreach (XElement list in def.Descendants("hediffDefNames"))
            {
                foreach (XElement item in list.Elements("li"))
                {
                    if (string.Equals((item.Value ?? string.Empty).Trim(), hediffDefName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasListValue(XElement def, string listName, string expectedValue)
        {
            if (def == null)
            {
                return false;
            }

            foreach (XElement list in def.Descendants(listName))
            {
                foreach (XElement item in list.Elements("li"))
                {
                    string value = (item.Value ?? string.Empty).Trim();
                    if (expectedValue == null)
                    {
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return true;
                        }
                    }
                    else if (string.Equals(value, expectedValue, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string GroupColorCue(XDocument document, string defName)
        {
            return ChildValue(
                FindDef(document, "PawnDiary.DiaryInteractionGroupDef", defName),
                "colorCue");
        }

        private static bool HasCueColor(XDocument document, string cue)
        {
            foreach (XElement item in document.Descendants("cueColors").Elements("li"))
            {
                if (string.Equals(ChildValue(item, "cue"), cue, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasUsableEventWindowTrigger(XElement triggerList)
        {
            if (triggerList == null)
            {
                return false;
            }

            foreach (XElement trigger in triggerList.Elements("li"))
            {
                if (!string.IsNullOrWhiteSpace(ChildValue(trigger, "source"))
                    || !string.IsNullOrWhiteSpace(ChildValue(trigger, "signal"))
                    || HasListValue(trigger, "matchDefNames", null)
                    || HasListValue(trigger, "matchTokens", null))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ChildValue(XElement element, string childName)
        {
            if (element == null)
            {
                return string.Empty;
            }

            XElement child = element.Element(childName);
            return child == null ? string.Empty : (child.Value ?? string.Empty).Trim();
        }

        private static string KeyedValue(XDocument document, string key)
        {
            XElement element = document?.Root?.Element(key);
            return element == null ? string.Empty : (element.Value ?? string.Empty).Trim();
        }

        // B1: SafePrefix caps length without ever splitting a UTF-16 surrogate pair. "\U0001F600" is a
        // grinning-face emoji = one high + one low surrogate (two chars).
        private static void TestDiarySentenceExcerpt()
        {
            AssertEqual("first sentence selected",
                "First sentence.",
                DiarySentenceExcerpt.FirstSentence("First sentence. Second sentence! Third sentence?", 200));

            AssertEqual("first sentence cleans rich text and whitespace",
                "Before alarm.",
                DiarySentenceExcerpt.FirstSentence("<b>Before</b>\n\talarm. I kept walking.", 200));

            AssertEqual("first sentence without punctuation uses capped prefix",
                "I kept...",
                DiarySentenceExcerpt.FirstSentence("I kept walking toward the light", 9));

            AssertEqual("last two sentences selected",
                "Second sentence! Third sentence?",
                DiarySentenceExcerpt.LastSentences("First sentence. Second sentence! Third sentence?", 2, 200));

            AssertEqual("rich text and newlines cleaned",
                "After the alarm. I kept walking.",
                DiarySentenceExcerpt.LastSentences("<b>Before</b>.\nAfter the alarm. I kept walking.", 2, 200));

            AssertEqual("long excerpt keeps the ending",
                "...kept walking toward the light.",
                DiarySentenceExcerpt.LastSentences("I waited. Then I kept walking toward the light.", 2, 33));
        }

        private static void TestSurrogateSafeTruncation()
        {
            string emoji = "\U0001F600"; // 2 UTF-16 chars
            AssertEqual("plain ASCII truncates exactly", "abc", TextTruncation.SafePrefix("abcdef", 3));
            AssertEqual("short string is returned whole", "ab", TextTruncation.SafePrefix("ab", 5));
            AssertEqual("empty input yields empty", string.Empty, TextTruncation.SafePrefix(null, 4));
            AssertEqual("non-positive cap yields empty", string.Empty, TextTruncation.SafePrefix("abc", 0));

            // "ab" + emoji = chars [a][b][hi][lo]; a cap of 3 would slice between hi and lo, so SafePrefix
            // must back off to "ab" (2 chars) rather than emit a lone high surrogate.
            string withEmoji = "ab" + emoji;
            string capped = TextTruncation.SafePrefix(withEmoji, 3);
            AssertEqual("cut between surrogates backs off to whole pair", "ab", capped);
            AssertTrue("result never ends on a lone high surrogate",
                capped.Length == 0 || !char.IsHighSurrogate(capped[capped.Length - 1]));

            // A cap landing exactly after the pair keeps the whole emoji.
            AssertEqual("cap after the pair keeps the emoji", withEmoji, TextTruncation.SafePrefix(withEmoji, 4));

            string suffixCapped = TextTruncation.SafeSuffix(emoji + "ab", 3);
            AssertEqual("suffix cut between surrogates starts after pair", "ab", suffixCapped);
            AssertTrue("suffix result never starts on a lone low surrogate",
                suffixCapped.Length == 0 || !char.IsLowSurrogate(suffixCapped[0]));
        }

        private static void TestExternalDirectEntryText()
        {
            AssertEqual("direct prose preserves one blank paragraph",
                "First line.\n\nSecond line.",
                ExternalDirectEntryText.CleanProse("  First line.  \r\n\r\n\tSecond line.\u0007  ", 200));
            AssertEqual("direct prose collapses repeated blank paragraphs",
                "First.\n\nSecond.",
                ExternalDirectEntryText.CleanProse("First.\n\n\n\nSecond.", 200));
            AssertEqual("direct prose cap is surrogate-safe",
                "ab",
                ExternalDirectEntryText.CleanProse("ab\U0001F600cd", 3));
            AssertEqual("direct title is one line and capped",
                "Title next",
                ExternalDirectEntryText.CleanTitle(" Title\r\nnext page ", 10));
            AssertEqual("blank direct prose cleans to empty",
                string.Empty,
                ExternalDirectEntryText.CleanProse(" \r\n\t ", 200));
        }

        private static void TestExternalWritingStyleOverrideText()
        {
            AssertEqual("external style override rule is one prompt line",
                "Write plain. No tags.",
                ExternalWritingStyleOverrideText.CleanRule("<b>Write</b>\nplain.\tNo tags."));
            AssertEqual("external style override source id is one line",
                "adapter.source",
                ExternalWritingStyleOverrideText.CleanSourceId(" adapter.source\r\n"));

            string splitSurrogateRule = new string('a', ExternalWritingStyleOverrideText.MaxRuleChars - 1)
                + char.ConvertFromUtf32(0x1F600);
            AssertEqual("external style override rule cap does not split surrogate pairs",
                new string('a', ExternalWritingStyleOverrideText.MaxRuleChars - 1),
                ExternalWritingStyleOverrideText.CleanRule(splitSurrogateRule));
            AssertEqual("external style override source id is capped",
                new string('s', ExternalWritingStyleOverrideText.MaxSourceIdChars),
                ExternalWritingStyleOverrideText.CleanSourceId(
                    new string('s', ExternalWritingStyleOverrideText.MaxSourceIdChars + 10)));
        }

        // S1: RedactSecrets masks key=/token= query parameters and Bearer tokens in arbitrary text.
        private static void TestRedactSecrets()
        {
            AssertEqual("query key is redacted",
                "GET https://api.test/v1?key=<redacted> failed",
                ApiLaneLabels.RedactSecrets("GET https://api.test/v1?key=sk-SECRET123 failed"));
            AssertEqual("token query param is redacted",
                "url?model=x&token=<redacted>",
                ApiLaneLabels.RedactSecrets("url?model=x&token=abc.def-123"));
            AssertEqual("bearer token is redacted",
                "Authorization: Bearer <redacted>",
                ApiLaneLabels.RedactSecrets("Authorization: Bearer sk-LIVE_aB.cD-12"));
            // A token can carry base64 padding/separators (+ / = ~); the whole token must be masked,
            // not just the leading allow-listed run (the old [A-Za-z0-9._-]+ pattern leaked the tail).
            AssertEqual("bearer base64 token is fully redacted",
                "Authorization: Bearer <redacted>",
                ApiLaneLabels.RedactSecrets("Authorization: Bearer aB+cd/Ef12=="));
            AssertEqual("bearer token with mixed separators stops at whitespace",
                "Bearer <redacted> failed",
                ApiLaneLabels.RedactSecrets("Bearer eyJhbG.c-iOi_Jz~I1 failed"));
            AssertEqual("text without secrets is unchanged",
                "HTTP 500: upstream timeout",
                ApiLaneLabels.RedactSecrets("HTTP 500: upstream timeout"));
        }

        // The pawn's writing-style rule must reach the SYSTEM prompt for every first-person entry. It
        // must never be a field in the USER prompt (deliberate design — see DiaryPromptTemplateDefs.xml
        // header comment), and it must be dropped only for the three neutral chronicle/title shapes.
        // These tests pin that contract at the two load-bearing seams:
        //   1. PromptAssembler.ComposeSystem — the single pure line that joins the voice block to the
        //      base system prompt (so any future refactor of the join cannot silently drop style).
        //   2. The shipped template XML — so adding a new first-person shape with the wrong
        //      includePersona flag is caught here instead of shipping silent style loss.

        private static void TestWritingStyleReachesSystemPrompt()
        {
            const string voiceBlock = "How this pawn tends to write: spare-iceberg: short concrete sentences.";
            const string baseSystem = "Write 1-3 first-person diary sentences.";

            // Happy path: a non-empty voice block on an includePersona=true template is appended as a
            // trailing paragraph after a blank line. This is the shape every first-person entry must have.
            AssertEqual("persona appended to base system prompt",
                baseSystem + "\n\n" + voiceBlock,
                PromptAssembler.ComposeSystem(baseSystem, voiceBlock, includePersona: true));

            // A blank/whitespace voice block must NOT leave a dangling blank line in the system prompt;
            // the base prompt is returned unchanged. (Pawn had no style resolved — rare, but must be clean.)
            AssertEqual("blank voice block leaves base system prompt unchanged",
                baseSystem,
                PromptAssembler.ComposeSystem(baseSystem, "  \n ", includePersona: true));
            AssertEqual("null voice block leaves base system prompt unchanged",
                baseSystem,
                PromptAssembler.ComposeSystem(baseSystem, null, includePersona: true));

            // Neutral chronicle/title shapes opt out via includePersona=false; the voice block (even a
            // real one) must never appear there. This is the death/arrival/title carve-out.
            AssertEqual("includePersona=false drops the voice block",
                baseSystem,
                PromptAssembler.ComposeSystem(baseSystem, voiceBlock, includePersona: false));

            // If a template has no base system prompt text, the voice block stands alone rather than
            // producing an empty leading line. (Edge case, but it guards the TrimEnd/append algorithm.)
            AssertEqual("voice block stands alone when base system prompt is empty",
                voiceBlock,
                PromptAssembler.ComposeSystem(string.Empty, voiceBlock, includePersona: true));

            // End-to-end through the planner: a normal solo entry's finished system prompt MUST contain
            // the voice block. This catches a regression anywhere in the request -> planner -> assembler
            // chain (e.g. personaVoiceBlock no longer being copied onto DiaryPromptRequest).
            DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = SoloPayload("e-style", "quiet work", "Alice repaired the generator alone."),
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Initiator,
                personaVoiceBlock = voiceBlock,
                maxTokens = 30
            });
            AssertContains("planner folds persona voice block into system prompt",
                plan.systemPrompt, voiceBlock);
            AssertTrue("persona never appears as a user-prompt field",
                !plan.userPrompt.Contains("How this pawn tends to write"));
            AssertTrue("persona never appears as a user-prompt field by source token",
                !ContainsUserPromptField(plan.userPrompt, "writing style"));
        }

        private static void TestShippedTemplatesWritingStyleContract()
        {
            // Reads the SHIPPED template defs and pins the writing-style policy for every shape:
            // first-person templates (the ones the game writes diary entries in) MUST keep persona, and
            // the neutral chronicle + title templates MUST opt out. The C# default for includePersona is
            // true, so an absent <includePersona> tag is treated as true (the first-person default) —
            // matching DiaryPromptTemplateDef.includePersona = true in PromptArchitectureDefs.cs.
            XDocument doc = XDocument.Load(RepoPath("1.6", "Defs", "DiaryPromptTemplateDefs.xml"));

            // Keys that must NOT carry writing style: the factual death/arrival notes and the title
            // follow-up call are intentionally style- and enchantment-free (see the file header comment).
            HashSet<string> styleFreeKeys = new HashSet<string>
            {
                DiaryPipelineTemplates.DeathDescription,
                DiaryPipelineTemplates.ArrivalDescription,
                DiaryPipelineTemplates.Title
            };

            // Every other shipped key writes first-person diary text and therefore MUST include the
            // pawn's writing style. If a new first-person shape is added without the right flag, or an
            // existing one is flipped, this fails before the silent style loss can ship.
            List<string> seenKeys = new List<string>();
            foreach (XElement def in doc.Descendants("PawnDiary.DiaryPromptTemplateDef"))
            {
                string key = ChildValue(def, "templateKey");
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                seenKeys.Add(key);
                bool includePersona = ReadBool(def, "includePersona", defaultValue: true);
                if (styleFreeKeys.Contains(key))
                {
                    AssertTrue("template " + key + " must opt out of writing style (includePersona=false)",
                        !includePersona);
                }
                else
                {
                    AssertTrue("template " + key + " must keep writing style (includePersona=true)",
                        includePersona);
                }
            }

            // Guard against a silent rename of the template-key vocabulary: every DiaryPipelineTemplates
            // constant must be present in the shipped XML, otherwise the planner would fall through to
            // a default template and the assertions above would not actually cover the real shapes.
            foreach (string expectedKey in AllPipelineTemplateKeys())
            {
                AssertTrue("shipped XML defines template key " + expectedKey, seenKeys.Contains(expectedKey));
            }
        }

        private static bool ReadBool(XElement def, string elementName, bool defaultValue)
        {
            string value = ChildValue(def, elementName);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> AllPipelineTemplateKeys()
        {
            return new List<string>
            {
                DiaryPipelineTemplates.PairDefault,
                DiaryPipelineTemplates.PairImportant,
                DiaryPipelineTemplates.PairCombat,
                DiaryPipelineTemplates.PairBatched,
                DiaryPipelineTemplates.SoloDefault,
                DiaryPipelineTemplates.SoloImportant,
                DiaryPipelineTemplates.SoloInternalState,
                DiaryPipelineTemplates.SoloBatched,
                DiaryPipelineTemplates.SoloDayReflection,
                DiaryPipelineTemplates.SoloQuadrumReflection,
                DiaryPipelineTemplates.SoloArcReflection,
                DiaryPipelineTemplates.DeathDescription,
                DiaryPipelineTemplates.ArrivalDescription,
                DiaryPipelineTemplates.Title
            };
        }

        private static bool ContainsUserPromptField(string userPrompt, string labelFragment)
        {
            return userPrompt != null
                && userPrompt.IndexOf(labelFragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AssertHeader(string name, HttpRequestMessage request, string headerName, string expected)
        {
            IEnumerable<string> values;
            if (!request.Headers.TryGetValues(headerName, out values))
            {
                throw new InvalidOperationException(name + " failed.\nMissing header: " + headerName);
            }

            foreach (string value in values)
            {
                assertions++;
                if (string.Equals(value, expected, StringComparison.Ordinal))
                {
                    return;
                }
            }

            throw new InvalidOperationException(name + " failed.\nExpected header value: [" + expected + "]");
        }

        private static void AssertEqual(string name, ApiAuthMode expected, ApiAuthMode actual)
        {
            assertions++;
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    name + " failed.\nExpected: [" + expected + "]\nActual:   [" + actual + "]");
            }
        }

        private static void AssertEqual(string name, ApiCompatibilityMode expected, ApiCompatibilityMode actual)
        {
            assertions++;
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    name + " failed.\nExpected: [" + expected + "]\nActual:   [" + actual + "]");
            }
        }

        private static void AssertEqual(string name, ApiLaneIdentity expected, ApiLaneIdentity actual)
        {
            assertions++;
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    name + " failed.\nExpected: [" + expected + "]\nActual:   [" + actual + "]");
            }
        }

        private static void AssertEqual(string name, string expected, string actual)
        {
            assertions++;
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    name + " failed.\nExpected: [" + expected + "]\nActual:   [" + actual + "]");
            }
        }

        private static void AssertEqual(string name, int expected, int actual)
        {
            assertions++;
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    name + " failed.\nExpected: [" + expected + "]\nActual:   [" + actual + "]");
            }
        }

        private static void AssertNear(string name, float expected, float actual)
        {
            assertions++;
            if (Math.Abs(expected - actual) > 0.0001f)
            {
                throw new InvalidOperationException(
                    name + " failed.\nExpected: [" + expected + "]\nActual:   [" + actual + "]");
            }
        }

        private static void AssertContains(string name, string text, string expectedFragment)
        {
            assertions++;
            if (text == null || text.IndexOf(expectedFragment, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    name + " failed.\nExpected fragment: [" + expectedFragment + "]\nActual: [" + text + "]");
            }
        }

        private static void AssertTrue(string name, bool condition)
        {
            assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(name + " failed.");
            }
        }
    }
}

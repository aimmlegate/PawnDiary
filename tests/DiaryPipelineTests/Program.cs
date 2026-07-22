using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            TestNarrativeContextPromptField();
            TestBeliefContextPromptField();
            TestPromptContextDetailSelection();
            TestPromptContextDetailOverrideResolution();
            TestOwnedPromptTextIsNotSentenceCapped();
            TestPromptTemplatePolicyReachesPromptPlan();
            TestQuestPromptPlanFields();
            TestProgressionPromptPlanFields();
            TestBiotechPromptPlanFields();
            TestBiotechPromptTemplateXmlContract();
            TestOdysseyPromptPlanFields();
            TestRoyaltyPersonaPromptPlanFields();
            TestRoyaltyPermitPromptPlanFields();
            TestDualPovPromptPlans();
            TestRecipientFollowupPlan();
            TestNeutralGenerationPlans();
            TestTitlePromptPlan();
            TestSoloSelection();
            TestSoloBatchSelection();
            TestPromptEnchantmentPlanner();
            TestPromptEnchantmentCandidateSnapshot();
            TestPromptEnchantmentDecayPolicy();
            TestHumorChancePolicy();
            TestPromptSimilarity();
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
            TestCaptureCapabilityRegistry();
            TestDiaryListText();
            TestContextProviderRegistry();
            TestEventWindowPolicy();
            TestDiaryEntryFilterPolicy();
            TestAnomalySemanticPrecisionXmlPolicy();
            TestOdysseyExistingIntegrationXmlPolicy();
            TestOdysseyJourneyFoundationXmlContract();
            TestRoyaltyNarrativeProviderXmlContract();
            TestRoyaltyPersonaLifecycleXmlContract();
            TestRoyaltyPermitXmlContract();
            TestRoyaltyAscentXmlContract();
            TestIdeologyCrisisXmlContract();
            TestIdeologyCounselXmlContract();
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
            TestApiLaneImport();
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
            TestExternalOverrideArbitration();
            TestPlayerWritingStyleText();
            TestWritingStyleResolutionPolicy();
            TestPsychotypeText();
            TestPsychotypeResolutionPolicy();
            TestPsychotypeRollProfile();
            TestPsychotypeNormalizeFamily();
            TestPsychotypeFamilyWeights();
            TestPsychotypeMemberWeights();
            TestPsychotypeRollBranches();
            TestPsychotypeRollDistribution();
            TestPsychotypeTraitKeys();
            TestPsychotypeTraitWeights();
            TestPsychotypeTraitGating();
            TestSurrogateSafeTruncation();
            TestRedactSecrets();
            TestWritingStyleReachesSystemPrompt();
            TestPsychotypeVoiceReachesSystemPrompt();
            TestShippedTemplatesWritingStyleContract();
            TestErrorScrubRemovesPersonalData();
            TestErrorFingerprintGroupsAndDistinguishes();
            TestErrorReportPayloadIsValidPiiFreeJson();
            TestInstallSourceClassifier();
            TestModErrorPrefixPolicyCoversSubmods();

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

            // Signal-mirror tuning rows (Diary_Tuning.thought*/work*/progression*) were removed from the
            // Advanced tab because DiarySignalPolicies reads the policy def first, masking them. Their
            // overrides prune by EXACT key so the live DiarySignalPolicy_*.<field> rows — which can share
            // a trailing field name (thoughtProgressionRules) — and unrelated Diary_Tuning.* rows are
            // never dropped.
            Dictionary<string, string> signalMirror = new Dictionary<string, string>
            {
                { "Diary_Tuning.workBaseChance", "0.5" },
                { "Diary_Tuning.thoughtProgressionRules", "food|NeedFood|2:1" },
                { "DiarySignalPolicy_ThoughtProgression.thoughtProgressionRules", "food|NeedFood|2:1" },
                { "Diary_Tuning.socialFightDedupTicks", "1250" },
            };

            int signalRemoved = TuningOverrideMigration.PruneRemovedFieldKeys(signalMirror);

            AssertTrue("prunes exactly the two dead signal-mirror tuning overrides", signalRemoved == 2);
            AssertTrue("drops dead Diary_Tuning.workBaseChance",
                !signalMirror.ContainsKey("Diary_Tuning.workBaseChance"));
            AssertTrue("drops dead Diary_Tuning.thoughtProgressionRules",
                !signalMirror.ContainsKey("Diary_Tuning.thoughtProgressionRules"));
            AssertTrue("keeps the live signal-policy thoughtProgressionRules override (same field name)",
                signalMirror.ContainsKey("DiarySignalPolicy_ThoughtProgression.thoughtProgressionRules"));
            AssertTrue("keeps the unrelated live Diary_Tuning.socialFightDedupTicks override",
                signalMirror.ContainsKey("Diary_Tuning.socialFightDedupTicks"));
            AssertTrue("signal-mirror second pass is a no-op",
                TuningOverrideMigration.PruneRemovedFieldKeys(signalMirror) == 0);
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

        private static void TestNarrativeContextPromptField()
        {
            DiaryEventPayload payload = SoloPayload("e-narrative", "quiet work", "Alice repaired the generator.");
            payload.initiator.narrativeContext =
                "Seasonal floodwater is present on the writer's exact gravship map.";
            DiaryPolicySnapshot policy = Policy(combat: false, important: true);
            policy.narrativeContextInstruction = "Use only these frozen facts.";
            policy.Template(DiaryPipelineTemplates.SoloImportant).fields.Add(
                Field("narrative context", NarrativeContextPrompt.Source));

            PromptContextDetailLevel[] levels =
            {
                PromptContextDetailLevel.Full,
                PromptContextDetailLevel.Balanced,
                PromptContextDetailLevel.Compact
            };
            for (int i = 0; i < levels.Length; i++)
            {
                DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
                {
                    payload = payload,
                    policy = policy,
                    povRole = DiaryPipelineRoles.Initiator,
                    contextDetailLevel = levels[i],
                    maxTokens = 30
                });

                AssertContains("frozen narrative-context text reaches the " + levels[i] + " prompt",
                    plan.userPrompt,
                    "narrative context: Use only these frozen facts.\n"
                    + "Seasonal floodwater is present on the writer's exact gravship map.");
            }

            payload.initiator.narrativeContext = string.Empty;
            DiaryPromptPlan empty = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = payload,
                policy = policy,
                povRole = DiaryPipelineRoles.Initiator,
                maxTokens = 30
            });
            AssertTrue("empty narrative context omits its field", !empty.userPrompt.Contains("narrative context:"));
        }

        private static void TestPromptContextDetailSelection()
        {
            DiaryEventPayload combatPayload = PairPayload(
                "e-context-combat",
                "raid",
                "Alice fired at raiders by the north wall.",
                "Bob ducked behind the sandbags.");
            combatPayload.domain = "Raid";
            combatPayload.gameContext = "raid=RaidEnemy; arrival_mode=EdgeWalkIn; strategy=ImmediateAttack; points=850";
            combatPayload.initiator.pawnSummary = "sex=female; mood=stressed; health=bleeding cut; thoughts=slept in cold";
            combatPayload.initiator.surroundings = "outdoors, cold rain, recent threat: Raid";
            combatPayload.initiator.continuity = "Bob: friendly";
            combatPayload.initiator.previousEntryEnding =
                "I stopped before the door. The rain was still coming down. "
                + "The rest of the thought circles the same fear long enough that Compact should spend its budget elsewhere.";
            string severeLongContext = "bleeding " + new string('x', 520);

            DiaryPromptRequest fullRequest = new DiaryPromptRequest
            {
                payload = combatPayload,
                policy = Policy(combat: true, important: true),
                povRole = DiaryPipelineRoles.Initiator,
                promptEnchantment = severeLongContext,
                contextDetailLevel = PromptContextDetailLevel.Full
            };
            DiaryPromptPlan full = DiaryPromptPlanner.Build(fullRequest);

            DiaryPromptRequest compactRequest = new DiaryPromptRequest
            {
                payload = combatPayload,
                policy = Policy(combat: true, important: true),
                povRole = DiaryPipelineRoles.Initiator,
                promptEnchantment = severeLongContext,
                contextDetailLevel = PromptContextDetailLevel.Compact
            };
            DiaryPromptPlan compact = DiaryPromptPlanner.Build(compactRequest);

            AssertContains("full includes continuity", full.userPrompt,
                "previous diary ending (continue from this): I stopped before the door.");
            AssertContains("compact keeps core event", compact.userPrompt,
                "what happened: Alice fired at raiders by the north wall.");
            AssertContains("compact keeps combat weapon", compact.userPrompt, "weapon: knife");
            AssertTrue("compact cuts previous ending", !compact.userPrompt.Contains("previous diary ending"));
            AssertTrue("compact report contains cuts", compact.contextSelectionReport.cut.Count > 0);
            AssertTrue("compact report cut reasons are populated",
                !string.IsNullOrWhiteSpace(compact.contextSelectionReport.cut[0].reason));

            // XML-backed budgets are injected through the request; a tiny budget must cut strictly more
            // than the default. This proves ContextDetailPolicy.Budgets() reaches the pure selector.
            DiaryPromptPlan balancedDefaultBudget = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = combatPayload,
                policy = Policy(combat: true, important: true),
                povRole = DiaryPipelineRoles.Initiator,
                promptEnchantment = severeLongContext,
                contextDetailLevel = PromptContextDetailLevel.Balanced
            });
            DiaryPromptPlan balancedTinyBudget = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = combatPayload,
                policy = Policy(combat: true, important: true),
                povRole = DiaryPipelineRoles.Initiator,
                promptEnchantment = severeLongContext,
                contextDetailLevel = PromptContextDetailLevel.Balanced,
                contextBudgets = new PromptContextBudgets { balancedDefault = 1 }
            });
            AssertTrue("injected budgets reach the selector",
                balancedDefaultBudget.contextSelectionReport.budgetChars == 650);
            AssertTrue("tiny injected budget cuts more than default balanced",
                balancedTinyBudget.contextSelectionReport.cut.Count > balancedDefaultBudget.contextSelectionReport.cut.Count);

            DiaryEventPayload progressionPayload = SoloPayload(
                "e-context-progression",
                "skill milestone",
                "Alice reached Construction 12.");
            progressionPayload.gameContext = "progression=SkillMilestone; progression_kind=skill; skill=Construction; skill_level=12; previous_skill_milestone=8; passion=major";
            DiaryPromptPlan progressionCompact = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = progressionPayload,
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Initiator,
                contextDetailLevel = PromptContextDetailLevel.Compact
            });

            AssertContains("compact keeps progression kind", progressionCompact.userPrompt, "progression kind: skill");
            AssertContains("compact keeps progression skill", progressionCompact.userPrompt, "skill: Construction");
            AssertContains("compact keeps progression level", progressionCompact.userPrompt, "skill level: 12");

            DiaryEventPayload royaltyPayload = SoloPayload(
                "e-context-royalty", "royal promotion", "Alice was promoted by the Empire.");
            royaltyPayload.domain = "Progression";
            royaltyPayload.gameContext =
                "progression=RoyalTitlePromoted; progression_kind=royal_title; "
                + "royal_mutation_pawn=Alice; royal_cause=unknown; royal_transition=promotion; "
                + "royal_faction=Empire; previous_title=Yeoman; title=Acolyte; "
                + "previous_psylink_level=1; psylink_level=2; psylink_cause=imperial_bestowing; "
                + "royal_duty_changes=throne_room, apparel";
            DiaryPromptPlan royaltyCompact = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = royaltyPayload,
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Initiator,
                contextDetailLevel = PromptContextDetailLevel.Compact,
                contextBudgets = new PromptContextBudgets { compactDefault = 1 }
            });
            AssertContains("compact keeps exact royal mutation pawn", royaltyCompact.userPrompt,
                "royal mutation pawn: Alice");
            AssertContains("compact keeps exact royal faction", royaltyCompact.userPrompt,
                "royal faction: Empire");
            AssertContains("compact keeps previous title", royaltyCompact.userPrompt,
                "previous royal title: Yeoman");
            AssertContains("compact keeps new title", royaltyCompact.userPrompt,
                "royal title: Acolyte");
            AssertContains("compact keeps previous psylink", royaltyCompact.userPrompt,
                "previous psylink level: 1");
            AssertContains("compact keeps new psylink", royaltyCompact.userPrompt,
                "psylink level: 2");
            AssertTrue("compact removes optional royal duty prose first",
                !royaltyCompact.userPrompt.Contains("new royal duties:"));
        }

        private static void TestBeliefContextPromptField()
        {
            DiaryEventPayload payload = SoloPayload(
                "e-belief", "body modification", "Alice received a crafted arm.");
            payload.initiator.beliefContext =
                "ideoligion: Ember Path\n"
                + "certainty: 62% (steady)\n"
                + "certainty trend: rising (meaningful)\n"
                + "relevant precept: crafted replacements are welcomed\n"
                + "precept meaning: replacing a weak limb is an honored act\n"
                + "relevant meme: Transhumanist\n"
                + "structure: Abstract Theist";
            DiaryPolicySnapshot policy = Policy(combat: false, important: true);
            policy.beliefContextInstruction = "Interpret only through these event-relevant beliefs.";
            policy.beliefPolicy = BeliefPolicySnapshot.CreateDefault();
            policy.Template(DiaryPipelineTemplates.SoloImportant).fields.Add(
                Field("belief context", BeliefContextPrompt.Source));

            DiaryPromptPlan full = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = payload,
                policy = policy,
                povRole = DiaryPipelineRoles.Initiator,
                contextDetailLevel = PromptContextDetailLevel.Full
            });
            AssertContains("full prompt carries frozen belief guidance", full.userPrompt,
                "belief context: Interpret only through these event-relevant beliefs.\nideoligion: Ember Path");
            AssertContains("full prompt carries saved belief description", full.userPrompt,
                "precept meaning: replacing a weak limb is an honored act");

            DiaryPromptPlan balanced = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = payload,
                policy = policy,
                povRole = DiaryPipelineRoles.Initiator,
                contextDetailLevel = PromptContextDetailLevel.Balanced
            });
            AssertContains("balanced prompt keeps the event-relative stance", balanced.userPrompt,
                "relevant precept: crafted replacements are welcomed");
            AssertTrue("balanced belief context omits descriptions",
                !balanced.userPrompt.Contains("precept meaning:"));

            DiaryPromptPlan compact = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = payload,
                policy = policy,
                povRole = DiaryPipelineRoles.Initiator,
                contextDetailLevel = PromptContextDetailLevel.Compact
            });
            AssertContains("compact prompt keeps the event-relative stance", compact.userPrompt,
                "relevant precept: crafted replacements are welcomed");
            AssertContains("compact prompt keeps certainty trend", compact.userPrompt,
                "certainty trend: rising (meaningful)");
            AssertTrue("compact belief context omits structure and meme",
                !compact.userPrompt.Contains("structure:")
                    && !compact.userPrompt.Contains("relevant meme:"));

            AssertEqual("generic belief context has medium selector priority", 76,
                BeliefContextScore("Hediff", "part_kind=addedpart"));
            AssertEqual("ordinary social belief context has lower selector priority", 72,
                BeliefContextScore("Interaction", "worker=Interaction_Chat"));
            AssertEqual("ordinary thought belief context has lower selector priority", 68,
                BeliefContextScore("Thought", "thought_def=AteFineMeal"));
            AssertEqual("conversion belief context has critical selector priority", 94,
                BeliefContextScore("Social", "belief_event=conversion"));
            AssertEqual("belief crisis context has critical selector priority", 94,
                BeliefContextScore("MentalState", "belief_crisis=certainty_break"));
            AssertEqual("conversion ritual context has critical selector priority", 94,
                BeliefContextScore("Ritual", "conversion_ritual=Conversion"));

            payload.initiator.beliefContext = string.Empty;
            DiaryPromptPlan empty = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = payload,
                policy = policy,
                povRole = DiaryPipelineRoles.Initiator
            });
            AssertTrue("empty belief context costs no prompt field",
                !empty.userPrompt.Contains("belief context:"));

            XDocument templates = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryPromptTemplateDefs.xml"));
            string[] firstPersonTemplates =
            {
                "PairDefault", "PairImportant", "PairCombat", "PairBatched",
                "SoloDefault", "SoloImportant", "SoloInternalState", "SoloBatched",
                "SoloDayReflection", "SoloQuadrumReflection", "SoloArcReflection"
            };
            for (int i = 0; i < firstPersonTemplates.Length; i++)
            {
                XElement template = templates.Root?.Elements("PawnDiary.DiaryPromptTemplateDef")
                    .FirstOrDefault(row => ChildValue(row, "templateKey") == firstPersonTemplates[i]);
                AssertEqual("belief source appears exactly once in " + firstPersonTemplates[i], 1,
                    template?.Element("fields")?.Elements("li")
                        .Count(row => ChildValue(row, "source") == BeliefContextPrompt.Source) ?? 0);
            }
        }

        private static int BeliefContextScore(string domain, string gameContext)
        {
            DiaryEventPayload payload = SoloPayload(
                "e-belief-score", "belief score fixture", "Alice considered what happened.");
            payload.domain = domain;
            payload.gameContext = gameContext;
            payload.initiator.beliefContext = "ideoligion: Ember Path";
            DiaryPolicySnapshot policy = Policy(combat: false, important: true);
            policy.beliefPolicy = BeliefPolicySnapshot.CreateDefault();
            policy.Template(DiaryPipelineTemplates.SoloImportant).fields.Add(
                Field("belief context", BeliefContextPrompt.Source));
            DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = payload,
                policy = policy,
                povRole = DiaryPipelineRoles.Initiator,
                contextDetailLevel = PromptContextDetailLevel.Balanced,
                contextBudgets = new PromptContextBudgets { balancedDefault = 10000 }
            });
            PromptContextFieldReport report = plan.contextSelectionReport.kept
                .FirstOrDefault(row => row.source == BeliefContextPrompt.Source);
            AssertTrue("belief score fixture keeps the belief field", report != null);
            return report == null ? -1 : report.score;
        }

        private static void TestPromptContextDetailOverrideResolution()
        {
            AssertTrue("invalid global detail normalizes to Full",
                PromptContextSelector.Normalize((PromptContextDetailLevel)999) == PromptContextDetailLevel.Full);
            AssertTrue("invalid lane override normalizes to Inherit",
                PromptContextSelector.NormalizeOverride((PromptContextDetailOverride)999) == PromptContextDetailOverride.Inherit);
            AssertTrue("inherit uses global",
                PromptContextSelector.Resolve(PromptContextDetailLevel.Balanced, PromptContextDetailOverride.Inherit)
                == PromptContextDetailLevel.Balanced);
            AssertTrue("lane override wins",
                PromptContextSelector.Resolve(PromptContextDetailLevel.Full, PromptContextDetailOverride.Compact)
                == PromptContextDetailLevel.Compact);
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

            PromptContextDetailLevel[] levels =
            {
                PromptContextDetailLevel.Full,
                PromptContextDetailLevel.Balanced,
                PromptContextDetailLevel.Compact
            };
            for (int i = 0; i < levels.Length; i++)
            {
                DiaryEventPayload ascent = SoloPayload(
                    "e-ascent-" + i,
                    "Royal Ascent",
                    "Alice felt the colony's Royal Ascent chapter reach its proven outcome.");
                ascent.defName = "EndGame_RoyalAscent";
                ascent.domain = DiaryEventDomainClassifier.Quest;
                ascent.gameContext = "quest=EndGame_RoyalAscent; signal=completed; "
                    + "quest_label=Royal Ascent; quest_signal=completed; quest_faction=Empire; "
                    + "quest_rewards=none";
                DiaryPromptPlan ascentPlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
                {
                    payload = ascent,
                    policy = Policy(combat: false, important: true),
                    povRole = DiaryPipelineRoles.Initiator,
                    contextDetailLevel = levels[i],
                    contextBudgets = new PromptContextBudgets
                    {
                        balancedDefault = 1,
                        compactDefault = 1
                    }
                });
                string suffix = " (" + levels[i] + ")";
                AssertContains("ascent prompt retains lifecycle" + suffix,
                    ascentPlan.userPrompt, "quest lifecycle: completed");
                AssertContains("ascent prompt retains visible quest label" + suffix,
                    ascentPlan.userPrompt, "quest name: Royal Ascent");
                AssertTrue("ascent prompt redacts root/correlation/arc" + suffix,
                    !ascentPlan.userPrompt.Contains("EndGame_RoyalAscent")
                    && !ascentPlan.userPrompt.Contains("Quest_41")
                    && !ascentPlan.userPrompt.Contains("royalty-ascent|"));
            }
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

            DiaryEventPayload traitPayload = SoloPayload(
                "e-trait-progression",
                "new trait",
                "Alice became nervous.");
            traitPayload.gameContext = "progression=TraitGained; progression_kind=trait; trait=Nervous; "
                + "trait_description=Prone to anxiety and tension.";
            DiaryPromptPlan traitPlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = traitPayload,
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Initiator,
                personaVoiceBlock = "Write like Alice.",
                maxTokens = 30
            });
            AssertContains("trait progression name reaches assembled prompt", traitPlan.userPrompt,
                "trait: Nervous");
            AssertContains("trait progression description reaches assembled prompt", traitPlan.userPrompt,
                "trait description: Prone to anxiety and tension.");
        }

        // Biotech B1 records exact machine-readable context for save correlation, but only the useful
        // story facts should cross the prompt-template boundary. This pins both important-event shapes
        // and deliberately gives Balanced/Compact almost no optional budget: required growth/birth facts
        // must still survive, while IDs and raw diagnostic bands must never appear even under Full.
        private static void TestBiotechPromptPlanFields()
        {
            PromptContextBudgets tinyBudgets = new PromptContextBudgets
            {
                balancedDefault = 1,
                compactDefault = 1
            };
            PromptContextDetailLevel[] levels =
            {
                PromptContextDetailLevel.Full,
                PromptContextDetailLevel.Balanced,
                PromptContextDetailLevel.Compact
            };

            DiaryEventPayload growth = PairPayload(
                "e-biotech-growth",
                "growth moment",
                "Alice made a lasting childhood choice with Bob nearby.",
                "Bob watched Alice make a lasting childhood choice.");
            growth.gameContext = "growth_moment=true; child_id=Thing_Child_42; birthday_age=10; "
                + "growth_stage=Teenager; family_arc_id=biotech-family|Thing_Child_42; "
                + "opportunity_band=broad; opportunity_description=several meaningful choices were available; "
                + "observed_upbringing_band=strong; observed_upbringing_description=regular lessons shaped the child; "
                + "selected_trait=Kind; selected_trait_description=inclined to help others; "
                + "new_interest_1=Medicine; interest_change_1=new passion; "
                + "previous_name=Alice Child; current_name=Alice Stone; "
                + "new_responsibilities=adult work and combat; supporter_id=Thing_Adult_9; "
                + "supporter_name=Bob; supporter_role=teacher; "
                + "initiator_family_role=child; recipient_family_role=teacher";

            for (int i = 0; i < levels.Length; i++)
            {
                PromptContextDetailLevel level = levels[i];
                string suffix = " (" + level + ")";
                DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
                {
                    payload = growth,
                    policy = Policy(combat: false, important: true),
                    povRole = DiaryPipelineRoles.Initiator,
                    contextDetailLevel = level,
                    contextBudgets = tinyBudgets
                });

                AssertEqual("Biotech growth uses pair-important template" + suffix,
                    DiaryPipelineTemplates.PairImportant, plan.templateKey);
                AssertContains("growth birthday survives detail budget" + suffix, plan.userPrompt,
                    "birthday age: 10");
                AssertContains("localized growth opportunity survives detail budget" + suffix, plan.userPrompt,
                    "opportunity at growth: several meaningful choices were available");
                AssertContains("growth trait survives detail budget" + suffix, plan.userPrompt,
                    "chosen trait: Kind");
                AssertContains("growth interest survives detail budget" + suffix, plan.userPrompt,
                    "interest 1: Medicine");
                AssertContains("growth interest change survives detail budget" + suffix, plan.userPrompt,
                    "interest 1 change: new passion");
                AssertContains("growth renamed value survives detail budget" + suffix, plan.userPrompt,
                    "current name: Alice Stone");
                AssertContains("growth responsibilities survive detail budget" + suffix, plan.userPrompt,
                    "new responsibilities: adult work and combat");
                AssertContains("growth supporter role survives detail budget" + suffix, plan.userPrompt,
                    "supporting adult role: teacher");
                AssertContains("growth child role survives detail budget" + suffix, plan.userPrompt,
                    "initiator family role: child");
                AssertTrue("growth child ID never reaches prompt" + suffix,
                    !plan.userPrompt.Contains("Thing_Child_42"));
                AssertTrue("growth family arc ID never reaches prompt" + suffix,
                    !plan.userPrompt.Contains("biotech-family|"));
                AssertTrue("growth raw upbringing band never reaches prompt" + suffix,
                    !plan.userPrompt.Contains("observed_upbringing_band"));

                if (level == PromptContextDetailLevel.Full)
                {
                    AssertContains("Full growth keeps optional trait meaning", plan.userPrompt,
                        "chosen trait meaning: inclined to help others");
                    AssertContains("Full growth keeps optional upbringing description", plan.userPrompt,
                        "observed upbringing: regular lessons shaped the child");
                    AssertContains("Full growth keeps optional supporter name", plan.userPrompt,
                        "supporting adult: Bob");
                }
            }

            DiaryEventPayload birth = SoloPayload(
                "e-biotech-birth",
                "family birth",
                "Alice gave birth to Rowan while the family gathered nearby.");
            birth.gameContext = "biotech_birth=true; child_id=Thing_Child_77; "
                + "family_arc_id=biotech-family|Thing_Child_77; child_name=Rowan; "
                + "birth_outcome=healthy; birth_method=pregnancy; "
                + "birther_id=Thing_Birther_1; birther_name=Alice; "
                + "genetic_mother_id=Thing_Mother_1; genetic_mother_name=Alice; "
                + "father_id=Thing_Father_2; father_name=Bob; doctor_id=Thing_Doctor_3; doctor_name=Cara; "
                + "birther_died=false; ritual_birth=false; "
                + "initiator_family_role=birther; recipient_family_role=father";

            for (int i = 0; i < levels.Length; i++)
            {
                PromptContextDetailLevel level = levels[i];
                string suffix = " (" + level + ")";
                DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
                {
                    payload = birth,
                    policy = Policy(combat: false, important: true),
                    povRole = DiaryPipelineRoles.Initiator,
                    contextDetailLevel = level,
                    contextBudgets = tinyBudgets
                });

                AssertEqual("Biotech birth uses solo-important template" + suffix,
                    DiaryPipelineTemplates.SoloImportant, plan.templateKey);
                AssertContains("birth child survives detail budget" + suffix, plan.userPrompt,
                    "child: Rowan");
                AssertContains("birth outcome survives detail budget" + suffix, plan.userPrompt,
                    "birth outcome: healthy");
                AssertContains("birth method survives detail budget" + suffix, plan.userPrompt,
                    "birth method: pregnancy");
                AssertContains("birth writer role survives detail budget" + suffix, plan.userPrompt,
                    "initiator family role: birther");
                AssertContains("birth related role survives detail budget" + suffix, plan.userPrompt,
                    "recipient family role: father");
                AssertContains("birth mortality fact survives detail budget" + suffix, plan.userPrompt,
                    "birther died: false");
                AssertContains("birth ritual fact survives detail budget" + suffix, plan.userPrompt,
                    "ritual birth: false");
                AssertTrue("birth child ID never reaches prompt" + suffix,
                    !plan.userPrompt.Contains("Thing_Child_77"));
                AssertTrue("birth participant IDs never reach prompt" + suffix,
                    !plan.userPrompt.Contains("Thing_Birther_1")
                    && !plan.userPrompt.Contains("Thing_Mother_1")
                    && !plan.userPrompt.Contains("Thing_Father_2")
                    && !plan.userPrompt.Contains("Thing_Doctor_3"));

                if (level == PromptContextDetailLevel.Full)
                {
                    AssertContains("Full birth keeps optional birther name", plan.userPrompt,
                        "birther: Alice");
                    AssertContains("Full birth keeps optional father name", plan.userPrompt,
                        "father: Bob");
                    AssertContains("Full birth keeps optional doctor name", plan.userPrompt,
                        "doctor: Cara");
                }
            }

        }

        // The template-field lists are DefInjected by numeric index. Appending a field to the wrong
        // template or inserting one in the middle can therefore silently pair a translated label with
        // the wrong value. This fixture locks the shipped projection and every English/Russian index.
        private static void TestBiotechPromptTemplateXmlContract()
        {
            XDocument templates = XDocument.Load(RepoPath("1.6", "Defs", "DiaryPromptTemplateDefs.xml"));
            XDocument english = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryPromptTemplateDef",
                "DiaryPromptTemplateDefs.xml"));
            XDocument russian = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryPromptTemplateDef",
                "DiaryPromptTemplateDefs.xml"));

            string[] requiredContextKeys =
            {
                "birthday_age", "opportunity_description", "selected_trait",
                "selected_trait_description", "new_interest_1", "interest_change_1",
                "new_interest_2", "interest_change_2", "new_interest_3", "interest_change_3",
                "new_interest_4", "interest_change_4", "observed_upbringing_description",
                "previous_name", "current_name", "new_responsibilities", "supporter_name",
                "supporter_role", "initiator_family_role", "recipient_family_role", "child_name",
                "birth_outcome", "birth_method", "birther_name", "genetic_mother_name",
                "father_name", "doctor_name", "birther_died", "ritual_birth"
            };
            string[] templateDefNames =
            {
                "DiaryPromptTemplate_PairImportant",
                "DiaryPromptTemplate_SoloImportant"
            };

            for (int templateIndex = 0; templateIndex < templateDefNames.Length; templateIndex++)
            {
                string defName = templateDefNames[templateIndex];
                XElement template = FindDef(
                    templates, "PawnDiary.DiaryPromptTemplateDef", defName);
                AssertTrue("Biotech prompt template exists: " + defName, template != null);

                List<XElement> fields = new List<XElement>(template.Element("fields").Elements("li"));
                HashSet<string> contextKeys = new HashSet<string>(StringComparer.Ordinal);
                for (int fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
                {
                    XElement field = fields[fieldIndex];
                    if (ReadBool(field, "enabled", defaultValue: true)
                        && string.Equals(ChildValue(field, "source"), "GameContext", StringComparison.Ordinal))
                    {
                        string contextKey = ChildValue(field, "contextKey");
                        contextKeys.Add(contextKey);
                        AssertTrue(defName + " excludes machine IDs from prompt fields at index " + fieldIndex,
                            !contextKey.EndsWith("_id", StringComparison.Ordinal));
                    }

                    string localizationKey = defName + ".fields." + fieldIndex + ".label";
                    string baseLabel = ChildValue(field, "label");
                    AssertEqual("English indexed prompt label stays aligned: " + localizationKey,
                        baseLabel, ChildValue(english.Root, localizationKey));
                    AssertTrue("Russian indexed prompt label exists: " + localizationKey,
                        !string.IsNullOrWhiteSpace(ChildValue(russian.Root, localizationKey)));
                }

                for (int keyIndex = 0; keyIndex < requiredContextKeys.Length; keyIndex++)
                {
                    string contextKey = requiredContextKeys[keyIndex];
                    AssertTrue(defName + " projects Biotech context key " + contextKey,
                        contextKeys.Contains(contextKey));
                }

                AssertTrue(defName + " excludes raw upbringing band",
                    !contextKeys.Contains("observed_upbringing_band"));
                AssertTrue(defName + " excludes raw opportunity band token",
                    !contextKeys.Contains("opportunity_band"));
                AssertTrue(defName + " excludes family arc correlation token",
                    !contextKeys.Contains("family_arc_id"));
            }

            // B1 events are important by policy. Keeping these fields off SoloDefault prevents them from
            // accidentally expanding ordinary-event prompts and catches append-to-the-wrong-list mistakes.
            XElement soloDefault = FindDef(
                templates, "PawnDiary.DiaryPromptTemplateDef", "DiaryPromptTemplate_SoloDefault");
            foreach (XElement field in soloDefault.Element("fields").Elements("li"))
            {
                AssertTrue("SoloDefault does not carry Biotech B1 projection",
                    !string.Equals(ChildValue(field, "contextKey"), "birthday_age", StringComparison.Ordinal));
            }
        }

        // Odyssey O1.5: a landing event is useless to the model if its frozen journey context never
        // crosses the prompt-template boundary. Pin both important shapes and deliberately shrink the
        // Balanced/Compact budget to one character: the minimum journey truth must still survive, while
        // stable correlation IDs and ticks remain unavailable to every prompt preset.
        private static void TestOdysseyPromptPlanFields()
        {
            PromptContextBudgets tinyBudgets = new PromptContextBudgets
            {
                balancedDefault = 1,
                compactDefault = 1
            };
            PromptContextDetailLevel[] levels =
            {
                PromptContextDetailLevel.Full,
                PromptContextDetailLevel.Balanced,
                PromptContextDetailLevel.Compact
            };
            string sharedContext = "odyssey_journey=true; journey_id=odyssey-journey|Ship_7|500; "
                + "ship_stable_id=WorldObject_7; departure_tick=500; journey_phase=landing; "
                + "journey_reason=major_destination; journey_secondary_reason=long_journey; "
                + "journey_duration=long; odyssey_initiator_role=pilot; odyssey_recipient_role=copilot; "
                + "rough_landing=true; landing_outcome=minor gravship crash; "
                + "launch_quality=excellent; "
                + "ship_name=Wayfarer; origin=Temperate forest; origin_key=planet-layer-0-tile-10; "
                + "destination=Orbital mechhive; destination_key=planet-layer-1-tile-20; "
                + "destination_layer=orbit; destination_biome=Orbit; "
                + "destination_site=Orbital mechhive; pilot=Alice; copilot=Bob; crew_count=2";

            DiaryEventPayload pair = PairPayload(
                "e-odyssey-pair",
                "gravship landing",
                "Alice and Bob brought Wayfarer down at the orbital mechhive.",
                "Bob and Alice brought Wayfarer down at the orbital mechhive.");
            pair.gameContext = sharedContext;
            DiaryEventPayload solo = SoloPayload(
                "e-odyssey-solo",
                "gravship landing",
                "Alice brought Wayfarer down at the orbital mechhive as pilot.");
            solo.gameContext = sharedContext + "; pov_journey_role=pilot";

            for (int i = 0; i < levels.Length; i++)
            {
                PromptContextDetailLevel level = levels[i];
                string suffix = " (" + level + ")";
                DiaryPromptPlan pairPlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
                {
                    payload = pair,
                    policy = Policy(combat: false, important: true),
                    povRole = DiaryPipelineRoles.Initiator,
                    contextDetailLevel = level,
                    contextBudgets = tinyBudgets
                });
                DiaryPromptPlan soloPlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
                {
                    payload = solo,
                    policy = Policy(combat: false, important: true),
                    povRole = DiaryPipelineRoles.Initiator,
                    contextDetailLevel = level,
                    contextBudgets = tinyBudgets
                });
                DiaryPromptPlan recipientPlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
                {
                    payload = pair,
                    policy = Policy(combat: false, important: true),
                    povRole = DiaryPipelineRoles.Recipient,
                    contextDetailLevel = level,
                    contextBudgets = tinyBudgets
                });

                AssertEqual("Odyssey pair uses pair-important template" + suffix,
                    DiaryPipelineTemplates.PairImportant, pairPlan.templateKey);
                AssertEqual("Odyssey solo uses solo-important template" + suffix,
                    DiaryPipelineTemplates.SoloImportant, soloPlan.templateKey);
                AssertContains("Odyssey phase survives detail budget" + suffix, pairPlan.userPrompt,
                    "journey phase: landing");
                AssertContains("Odyssey reason survives detail budget" + suffix, pairPlan.userPrompt,
                    "journey reason: major_destination");
                AssertContains("Odyssey secondary reason survives detail budget" + suffix, pairPlan.userPrompt,
                    "secondary journey reason: long_journey");
                AssertContains("Odyssey duration survives detail budget" + suffix, pairPlan.userPrompt,
                    "journey duration: long");
                AssertContains("Odyssey ship survives detail budget" + suffix, pairPlan.userPrompt,
                    "ship: Wayfarer");
                AssertContains("Odyssey origin survives detail budget" + suffix, pairPlan.userPrompt,
                    "origin: Temperate forest");
                AssertContains("Odyssey destination survives detail budget" + suffix, pairPlan.userPrompt,
                    "destination: Orbital mechhive");
                AssertContains("Odyssey exact landing outcome survives detail budget" + suffix,
                    pairPlan.userPrompt, "landing outcome: minor gravship crash");
                AssertContains("Odyssey solo POV role survives detail budget" + suffix, soloPlan.userPrompt,
                    "journey role: pilot");
                AssertContains("Odyssey pair initiator role survives detail budget" + suffix,
                    pairPlan.userPrompt, "journey role: pilot");
                AssertContains("Odyssey pair recipient role survives detail budget" + suffix,
                    recipientPlan.userPrompt, "journey role: copilot");
                AssertTrue("Odyssey internal pair-role facts never reach either prompt" + suffix,
                    !pairPlan.userPrompt.Contains("odyssey_initiator_role")
                    && !pairPlan.userPrompt.Contains("odyssey_recipient_role")
                    && !recipientPlan.userPrompt.Contains("odyssey_initiator_role")
                    && !recipientPlan.userPrompt.Contains("odyssey_recipient_role"));
                AssertTrue("Odyssey stable IDs and ticks stay out of prompt" + suffix,
                    !pairPlan.userPrompt.Contains("WorldObject_7")
                    && !pairPlan.userPrompt.Contains("odyssey-journey|")
                    && !pairPlan.userPrompt.Contains("planet-layer-")
                    && !pairPlan.userPrompt.Contains("departure_tick"));

                if (level == PromptContextDetailLevel.Full)
                {
                    AssertContains("Full Odyssey prompt keeps destination site", pairPlan.userPrompt,
                        "destination site: Orbital mechhive");
                    AssertContains("Full Odyssey prompt keeps pilot", pairPlan.userPrompt, "pilot: Alice");
                    AssertContains("Full Odyssey prompt keeps copilot", pairPlan.userPrompt, "copilot: Bob");
                    AssertContains("Full Odyssey prompt keeps launch quality", pairPlan.userPrompt,
                        "launch quality: excellent");
                }
            }
        }

        private static void TestRoyaltyPersonaPromptPlanFields()
        {
            DiaryEventPayload payload = SoloPayload(
                "e-persona-separated",
                "separated from persona weapon",
                "Alice had gone a full day without wielding Quiet Edge.");
            payload.gameContext = "persona_weapon=bond_separated; persona_weapon_id=Weapon_Test; "
                + "persona_weapon_def=PersonaMonosword; persona_weapon_name=Quiet Edge; bond_epoch=1; "
                + "bond_previous_state=separation_pending; bond_new_state=separated; "
                + "bond_separation_duration=one day; persona_trait_1=Jealous; "
                + "persona_trait_description_1=Resents being set aside for another weapon.";
            PromptContextDetailLevel[] levels =
            {
                PromptContextDetailLevel.Full,
                PromptContextDetailLevel.Balanced,
                PromptContextDetailLevel.Compact
            };
            PromptContextBudgets tiny = new PromptContextBudgets
            {
                balancedDefault = 1,
                compactDefault = 1
            };
            for (int i = 0; i < levels.Length; i++)
            {
                PromptContextDetailLevel level = levels[i];
                DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
                {
                    payload = payload,
                    policy = Policy(combat: false, important: true),
                    povRole = DiaryPipelineRoles.Initiator,
                    contextDetailLevel = level,
                    contextBudgets = tiny
                });
                string suffix = " (" + level + ")";
                AssertEqual("persona lifecycle uses solo-important template" + suffix,
                    DiaryPipelineTemplates.SoloImportant, plan.templateKey);
                AssertContains("persona weapon survives detail budget" + suffix,
                    plan.userPrompt, "persona weapon: Quiet Edge");
                AssertContains("persona event survives detail budget" + suffix,
                    plan.userPrompt, "bond event: bond_separated");
                AssertContains("persona previous state survives detail budget" + suffix,
                    plan.userPrompt, "previous bond state: separation_pending");
                AssertContains("persona new state survives detail budget" + suffix,
                    plan.userPrompt, "new bond state: separated");
                AssertContains("persona duration survives detail budget" + suffix,
                    plan.userPrompt, "separation duration: one day");
                AssertTrue("persona Thing ID never reaches prompt" + suffix,
                    !plan.userPrompt.Contains("Weapon_Test"));
                AssertTrue("persona epoch never reaches prompt" + suffix,
                    !plan.userPrompt.Contains("bond_epoch"));
                if (level == PromptContextDetailLevel.Full)
                {
                    AssertContains("Full persona prompt keeps structural trait", plan.userPrompt,
                        "persona trait 1: Jealous");
                    AssertContains("Full persona prompt keeps structural trait meaning", plan.userPrompt,
                        "persona trait 1 meaning: Resents being set aside for another weapon.");
                }
            }

            DiaryEventPayload milestonePayload = SoloPayload(
                "e-persona-kill",
                "first consequential persona-weapon kill",
                "Alice made her first consequential kill with Quiet Edge, killing a centipede.");
            milestonePayload.gameContext = "tale=PersonaWeaponFirstConsequentialKill; "
                + "persona_milestone=first_consequential_kill; tale_source_def=KilledMajorThreat; "
                + "tale_source_label=killed a major threat; tale_killer_role=initiator; "
                + "tale_victim_role=recipient; persona_weapon_name=Quiet Edge; "
                + "persona_weapon_id=Weapon_Test; bond_epoch=1; bond_previous_state=active; "
                + "bond_new_state=active";
            DiaryPromptPlan milestonePlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = milestonePayload,
                policy = Policy(combat: true, important: true),
                povRole = DiaryPipelineRoles.Initiator,
                contextDetailLevel = PromptContextDetailLevel.Compact,
                contextBudgets = tiny
            });
            AssertContains("Compact persona milestone keeps exact phase", milestonePlan.userPrompt,
                "persona milestone: first_consequential_kill");
            AssertContains("Compact persona milestone keeps source Tale", milestonePlan.userPrompt,
                "source tale: KilledMajorThreat");
            AssertContains("Compact persona milestone keeps source label", milestonePlan.userPrompt,
                "source tale name: killed a major threat");
            AssertContains("Compact persona milestone keeps exact killer role", milestonePlan.userPrompt,
                "killer tale role: initiator");
            AssertContains("Compact persona milestone keeps exact victim role", milestonePlan.userPrompt,
                "victim tale role: recipient");
            AssertTrue("persona milestone internal identity stays out of prompt",
                !milestonePlan.userPrompt.Contains("Weapon_Test")
                && !milestonePlan.userPrompt.Contains("bond_epoch"));

            DiaryEventPayload deathPayload = DeathPayload();
            deathPayload.gameContext += "; persona_milestone=wielder_death; "
                + "persona_weapon_name=Quiet Edge; bond_previous_state=active; "
                + "bond_new_state=ended; bond_end_cause=pawn_death; persona_trait_1=Jealous";
            DiaryPromptPlan deathPlan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = deathPayload,
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Neutral,
                contextDetailLevel = PromptContextDetailLevel.Compact,
                contextBudgets = tiny
            });
            AssertContains("wielder death keeps persona weapon", deathPlan.userPrompt,
                "persona weapon: Quiet Edge");
            AssertContains("wielder death keeps relationship ending", deathPlan.userPrompt,
                "persona milestone: wielder_death");
            AssertContains("wielder death keeps ending cause", deathPlan.userPrompt,
                "bond ending cause: pawn_death");
        }

        private static void TestRoyaltyPermitPromptPlanFields()
        {
            DiaryEventPayload payload = SoloPayload(
                "e-royal-permit",
                "royal military aid called",
                "Alice called in military aid from the Empire through a royal permit.");
            payload.domain = "RoyalPermit";
            payload.gameContext = "royal_permit=military_aid; permit_def=CallMilitaryAidLarge; "
                + "permit_label=Military aid; permit_family=military_aid; permit_faction=Empire; "
                + "permit_title=Knight; permit_setting=Home; used_during_cooldown=true; "
                + "permit_map_id=Map_1; permit_tick=500";
            PromptContextDetailLevel[] levels =
            {
                PromptContextDetailLevel.Full,
                PromptContextDetailLevel.Balanced,
                PromptContextDetailLevel.Compact
            };
            PromptContextBudgets tiny = new PromptContextBudgets
            {
                balancedDefault = 1,
                compactDefault = 1
            };
            for (int i = 0; i < levels.Length; i++)
            {
                PromptContextDetailLevel level = levels[i];
                DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
                {
                    payload = payload,
                    policy = Policy(combat: false, important: true),
                    povRole = DiaryPipelineRoles.Initiator,
                    contextDetailLevel = level,
                    contextBudgets = tiny
                });
                string suffix = " (" + level + ")";
                AssertEqual("permit uses solo-important template" + suffix,
                    DiaryPipelineTemplates.SoloImportant, plan.templateKey);
                AssertContains("permit label survives detail budget" + suffix,
                    plan.userPrompt, "permit: Military aid");
                AssertContains("permit family survives detail budget" + suffix,
                    plan.userPrompt, "permit family: military_aid");
                AssertContains("permit faction survives detail budget" + suffix,
                    plan.userPrompt, "permit faction: Empire");
                AssertContains("permit title survives detail budget" + suffix,
                    plan.userPrompt, "permit title: Knight");
                AssertContains("permit cooldown truth survives detail budget" + suffix,
                    plan.userPrompt, "used during cooldown: true");
                AssertTrue("permit Def ID is redacted from prompt" + suffix,
                    !plan.userPrompt.Contains("CallMilitaryAidLarge"));
                AssertTrue("permit map ID/tick are redacted from prompt" + suffix,
                    !plan.userPrompt.Contains("Map_1") && !plan.userPrompt.Contains("permit_tick"));
                if (level == PromptContextDetailLevel.Full)
                    AssertContains("Full permit prompt keeps optional setting", plan.userPrompt,
                        "permit setting: Home");
                else
                    AssertTrue("budgeted permit prompt trims optional setting first" + suffix,
                        !plan.userPrompt.Contains("permit setting:"));
            }
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

        private static void TestCaptureCapabilityRegistry()
        {
            CaptureCapabilityRegistry registry = new CaptureCapabilityRegistry(2);

            AssertTrue("capture capability registry rejects blank id", !registry.SetReady(" ", true));
            AssertTrue("capture capability registry starts unavailable", !registry.IsReady("adapter.rich"));
            AssertTrue("capture capability registry accepts ready id", registry.SetReady(" adapter.rich ", true));
            AssertTrue("capture capability registry trims and compares case-insensitively",
                registry.IsReady("ADAPTER.RICH"));
            AssertTrue("capture capability registry accepts duplicate ready report",
                registry.SetReady("adapter.rich", true));
            AssertTrue("capture capability registry accepts second id", registry.SetReady("adapter.off", true));
            AssertTrue("capture capability registry enforces defensive cap",
                !registry.SetReady("adapter.third", true));
            AssertTrue("capture capability registry detects any ready XML id",
                registry.AnyReady(new[] { "unknown", "ADAPTER.OFF" }));
            AssertTrue("capture capability registry handles null XML list", !registry.AnyReady(null));
            AssertTrue("capture capability registry clears case-insensitively",
                registry.SetReady("Adapter.Rich", false));
            AssertTrue("capture capability registry reports cleared id unavailable",
                !registry.IsReady("adapter.rich"));
            AssertTrue("capture capability registry reuses capacity after clear",
                registry.SetReady("adapter.third", true));
            AssertTrue("capture capability registry clearing absent id is idempotent",
                registry.SetReady("adapter.missing", false));
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

        private static void TestHumorChancePolicy()
        {
            AssertNear("humor neutral temperament uses base rate", 1f,
                HumorChancePolicy.Multiplier(false, false, false, 2f, 0.5f));
            AssertNear("humor upbeat trait elevates once", 2f,
                HumorChancePolicy.Multiplier(true, false, false, 2f, 0.5f));
            AssertNear("humor social passion elevates once", 2f,
                HumorChancePolicy.Multiplier(false, true, false, 2f, 0.5f));
            AssertNear("humor elevated qualifiers do not stack", 2f,
                HumorChancePolicy.Multiplier(true, true, false, 2f, 0.5f));
            AssertNear("humor dour trait reduces once", 0.5f,
                HumorChancePolicy.Multiplier(false, false, true, 2f, 0.5f));
            AssertNear("humor opposing traits cancel to base", 1f,
                HumorChancePolicy.Multiplier(true, false, true, 2f, 0.5f));
            AssertNear("humor social passion and dour trait cancel to base", 1f,
                HumorChancePolicy.Multiplier(false, true, true, 2f, 0.5f));
            AssertEqual("humor seed is deterministic",
                HumorChancePolicy.StableSeed("event", "pawn"),
                HumorChancePolicy.StableSeed("event", "pawn"));
            AssertTrue("humor seed separates POV writers",
                HumorChancePolicy.StableSeed("event", "pawn-a")
                    != HumorChancePolicy.StableSeed("event", "pawn-b"));
            // Anti-repetition reroll salt: salt 0 must reproduce the legacy two-field seed exactly,
            // so entries untouched by the guard keep their original humor decision.
            AssertEqual("humor seed salt 0 equals legacy seed",
                HumorChancePolicy.StableSeed("event", "pawn"),
                HumorChancePolicy.StableSeed("event", "pawn", 0));
            AssertTrue("humor seed salt changes the seed",
                HumorChancePolicy.StableSeed("event", "pawn", 1)
                    != HumorChancePolicy.StableSeed("event", "pawn", 0));
            AssertTrue("humor seed salt is per-value",
                HumorChancePolicy.StableSeed("event", "pawn", 1)
                    != HumorChancePolicy.StableSeed("event", "pawn", 2));
            AssertEqual("humor salted seed is deterministic",
                HumorChancePolicy.StableSeed("event", "pawn", 2),
                HumorChancePolicy.StableSeed("event", "pawn", 2));
        }

        private static void TestPromptSimilarity()
        {
            AssertNear("identical prompts score 1", 1f,
                PromptSimilarity.Similarity("event: raid\ninstruction: write about it",
                    "event: raid\ninstruction: write about it"));
            AssertNear("disjoint prompts score 0", 0f,
                PromptSimilarity.Similarity("alpha beta gamma", "delta epsilon zeta"));
            AssertNear("empty candidate scores 0", 0f,
                PromptSimilarity.Similarity("", "event: raid"));
            AssertNear("empty both sides scores 0", 0f,
                PromptSimilarity.Similarity(null, "   "));
            // Case and punctuation are normalized away; "label: value" schema tokens compare as words.
            AssertNear("case/punctuation normalize",
                1f, PromptSimilarity.Similarity("Event: Raid!", "event raid"));
            // Half the tokens shared: {a,b} vs {b,c} -> 1 shared / 3 union.
            AssertNear("partial overlap is jaccard", 1f / 3f,
                PromptSimilarity.Similarity("a b", "b c"));

            List<string> recent = new List<string>
            {
                "event: raid\ninstruction: fought off the raid",
                "event: birthday\ninstruction: write about the party",
            };
            float strongest;
            AssertTrue("similar prompt flagged",
                PromptSimilarity.TooSimilar(
                    "event: raid\ninstruction: fought off the raid", recent, 0.9f, out strongest));
            AssertNear("strongest picks the best match", 1f, strongest);
            AssertTrue("dissimilar prompt passes",
                !PromptSimilarity.TooSimilar(
                    "event: eclipse\ninstruction: watched the sky go dark", recent, 0.9f, out strongest));
            AssertTrue("threshold 1 only flags exact token match",
                !PromptSimilarity.TooSimilar(
                    "event: raid\ninstruction: fought off the raid bravely", recent, 1f, out strongest));
            AssertTrue("invalid threshold fails open",
                !PromptSimilarity.TooSimilar("a", recent, float.NaN, out strongest));
            AssertTrue("out-of-range threshold fails open",
                !PromptSimilarity.TooSimilar("a", recent, 1.5f, out strongest));
            AssertNear("empty recent list scores 0", 0f,
                PromptSimilarity.StrongestSimilarity("anything", null));
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

            // Multiline (player-authored) path: keep free-form angle brackets, strip only known tags.
            AssertEqual(
                "multiline keeps non-tag angle brackets",
                "write <5> words",
                PromptTextSanitizer.Multiline("write <5> words"));
            AssertEqual(
                "multiline still strips known rich-text tags",
                "be bold",
                PromptTextSanitizer.Multiline("be <b>bold</b>"));
            AssertEqual(
                "multiline preserves paragraph line breaks",
                "line one\nline two",
                PromptTextSanitizer.Multiline("line one\r\nline two"));
            AssertEqual(
                "player custom rule keeps <short> instruction",
                "write <short> sentences",
                PlayerWritingStyleText.CleanRule("write <short> sentences"));
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

        private static void TestDiaryEntryFilterPolicy()
        {
            HashSet<string> tags = new HashSet<string> { "Social", "Raid" };

            AssertTrue(
                "no filters passes everything",
                DiaryEntryFilterPolicy.Passes(false, false, "Social", null));
            AssertTrue(
                "favorites-only keeps a starred page",
                DiaryEntryFilterPolicy.Passes(true, true, "Social", null));
            AssertTrue(
                "favorites-only rejects an unstarred page",
                !DiaryEntryFilterPolicy.Passes(true, false, "Social", null));
            AssertTrue(
                "active tags keep a matching label",
                DiaryEntryFilterPolicy.Passes(false, false, "Raid", tags));
            AssertTrue(
                "active tags reject a non-matching label",
                !DiaryEntryFilterPolicy.Passes(false, false, "Medical", tags));
            AssertTrue(
                "active tags reject an untagged page",
                !DiaryEntryFilterPolicy.Passes(false, false, string.Empty, tags));
            AssertTrue(
                "active tags reject a null label",
                !DiaryEntryFilterPolicy.Passes(false, false, null, tags));
            AssertTrue(
                "favorites and tags combine: starred matching page passes",
                DiaryEntryFilterPolicy.Passes(true, true, "Social", tags));
            AssertTrue(
                "favorites and tags combine: unstarred matching page fails",
                !DiaryEntryFilterPolicy.Passes(true, false, "Social", tags));
            AssertTrue(
                "favorites and tags combine: starred non-matching page fails",
                !DiaryEntryFilterPolicy.Passes(true, true, "Medical", tags));
            AssertTrue(
                "an empty tag set behaves like no tag filter",
                DiaryEntryFilterPolicy.Passes(false, false, null, new HashSet<string>()));

            List<string> normalized = DiaryEntryFilterPolicy.NormalizeFavoriteEntryKeys(
                new[] { "evt_a|initiator", string.Empty, "evt_a|initiator", "evt_b|recipient" });
            AssertEqual("favorite normalization removes blanks and exact duplicates", 2, normalized.Count);
            AssertEqual("favorite normalization preserves first-seen order", "evt_a|initiator", normalized[0]);
            AssertEqual("favorite normalization preserves later unique keys", "evt_b|recipient", normalized[1]);
            AssertEqual("favorite normalization treats key casing as significant", 2,
                DiaryEntryFilterPolicy.NormalizeFavoriteEntryKeys(
                    new[] { "evt|initiator", "EVT|initiator" }).Count);
            AssertEqual("null favorite saves normalize to an empty list", 0,
                DiaryEntryFilterPolicy.NormalizeFavoriteEntryKeys(null).Count);

            List<string> oversized = new List<string>();
            for (int i = 0; i < DiaryEntryFilterPolicy.MaximumFavoriteEntryKeys + 8; i++)
            {
                oversized.Add("evt_" + i + "|initiator");
            }
            List<string> bounded = DiaryEntryFilterPolicy.NormalizeFavoriteEntryKeys(oversized);
            AssertEqual("favorite normalization enforces the shared defensive cap",
                DiaryEntryFilterPolicy.MaximumFavoriteEntryKeys, bounded.Count);
            AssertEqual("favorite normalization keeps the first keys when clamping",
                "evt_0|initiator", bounded[0]);
            AssertEqual("favorite normalization keeps the last in-bound key",
                "evt_4095|initiator", bounded[bounded.Count - 1]);
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

        // Master Wave 2 / Anomaly A0.0-A0.2: prove the shipped XML owns every exact psychic-ritual
        // classifier key and every player-driven monolith chapter. This stays in the pure harness: the
        // test parses our XML as data and exercises the same first-match/exact-trigger semantics without
        // loading RimWorld, Anomaly, Verse, or any paid-DLC assembly.
        private static void TestAnomalySemanticPrecisionXmlPolicy()
        {
            const string anomalyPackageId = "Ludeon.RimWorld.Anomaly";
            string[] groupDefNames =
            {
                "ritualAnomalyInvitation",
                "ritualAnomalyFleshAndWeather",
                "ritualAnomalyPredation",
                "ritualAnomalyMind",
                "ritualAnomalyAbduction",
                "ritualAnomalyDeathRefusal"
            };
            string[][] classifierKeys =
            {
                new[]
                {
                    "PsychicRitual;VoidProvocation",
                    "PsychicRitual;SummonAnimals",
                    "PsychicRitual;SummonShamblers"
                },
                new[]
                {
                    "PsychicRitual;SummonPitGate",
                    "PsychicRitual;SummonFleshbeasts",
                    "PsychicRitual;SummonFleshbeastsPlayer",
                    "PsychicRitual;BloodRain"
                },
                new[]
                {
                    "PsychicRitual;Philophagy",
                    "PsychicRitual;Chronophagy",
                    "PsychicRitual;Psychophagy"
                },
                new[]
                {
                    "PsychicRitual;Brainwipe",
                    "PsychicRitual;PleasurePulse",
                    "PsychicRitual;NeurosisPulse"
                },
                new[]
                {
                    "PsychicRitual;SkipAbduction",
                    "PsychicRitual;SkipAbductionPlayer"
                },
                new[] { "PsychicRitual;ImbueDeathRefusal" }
            };

            XDocument groups = XDocument.Load(RepoPath("1.6", "Defs", "DiaryInteractionGroupDefs.xml"));
            XDocument englishGroups = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryInteractionGroupDef",
                "DiaryInteractionGroupDefs.xml"));
            XDocument russianGroups = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryInteractionGroupDef",
                "DiaryInteractionGroupDefs.xml"));

            HashSet<int> ritualOrders = new HashSet<int>();
            foreach (XElement def in groups.Descendants("PawnDiary.DiaryInteractionGroupDef"))
            {
                if (!string.Equals(ChildValue(def, "domain"), "Ritual", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int order;
                AssertTrue("ritual group has numeric order: " + ChildValue(def, "defName"),
                    int.TryParse(ChildValue(def, "order"), out order));
                AssertTrue("ritual group order is unique: " + order, ritualOrders.Add(order));
            }

            for (int familyIndex = 0; familyIndex < groupDefNames.Length; familyIndex++)
            {
                string groupDefName = groupDefNames[familyIndex];
                XElement group = FindDef(groups, "PawnDiary.DiaryInteractionGroupDef", groupDefName);
                AssertTrue("Anomaly ritual family exists: " + groupDefName, group != null);
                if (group == null)
                {
                    continue;
                }

                AssertEqual("Anomaly ritual family domain: " + groupDefName, "Ritual", ChildValue(group, "domain"));
                AssertEqual("Anomaly ritual family order: " + groupDefName,
                    (770 + familyIndex).ToString(), ChildValue(group, "order"));
                AssertTrue("Anomaly ritual family is package-gated: " + groupDefName,
                    HasListValue(group, "enableWhenPackageIdsLoaded", anomalyPackageId));
                AssertTrue("Anomaly ritual family uses exact keys only: " + groupDefName,
                    HasListValue(group, "matchDefNames", null)
                    && !HasListValue(group, "matchTokens", null));

                for (int keyIndex = 0; keyIndex < classifierKeys[familyIndex].Length; keyIndex++)
                {
                    string classifierKey = classifierKeys[familyIndex][keyIndex];
                    AssertTrue("Anomaly family contains exact classifier key: " + classifierKey,
                        HasListValue(group, "matchDefNames", classifierKey));
                    AssertEqual("Anomaly classifier resolves exactly once: " + classifierKey,
                        groupDefName, ResolveInteractionGroup(groups, "Ritual", classifierKey, true));
                }

                string lowerCaseKey = classifierKeys[familyIndex][0].ToLowerInvariant();
                AssertEqual("Anomaly exact classifier is case-insensitive: " + lowerCaseKey,
                    groupDefName, ResolveInteractionGroup(groups, "Ritual", lowerCaseKey, true));
                AssertTrue("Anomaly family is unavailable without its package: " + groupDefName,
                    !InteractionGroupAvailable(group, false));

                string[] localizedFields = { ".label", ".instruction", ".tone", ".tones.0", ".tones.1" };
                for (int fieldIndex = 0; fieldIndex < localizedFields.Length; fieldIndex++)
                {
                    string key = groupDefName + localizedFields[fieldIndex];
                    AssertTrue("English Anomaly ritual DefInjected value exists: " + key,
                        !string.IsNullOrWhiteSpace(KeyedValue(englishGroups, key)));
                    AssertTrue("Russian Anomaly ritual DefInjected value exists: " + key,
                        !string.IsNullOrWhiteSpace(KeyedValue(russianGroups, key)));
                }
            }

            XElement fallback = FindDef(
                groups, "PawnDiary.DiaryInteractionGroupDef", "ritualAnomalyPsychic");
            AssertTrue("generic Anomaly psychic fallback remains", fallback != null);
            if (fallback != null)
            {
                AssertEqual("generic Anomaly psychic fallback order", "776", ChildValue(fallback, "order"));
                AssertTrue("generic Anomaly psychic fallback keeps token matching",
                    HasListValue(fallback, "matchTokens", "PsychicRitual"));
                AssertTrue("generic Anomaly psychic fallback is package-gated",
                    HasListValue(fallback, "enableWhenPackageIdsLoaded", anomalyPackageId));
            }

            AssertEqual("unknown psychic ritual reaches generic fallback",
                "ritualAnomalyPsychic",
                ResolveInteractionGroup(groups, "Ritual", "PsychicRitual;SomethingUnknown", true));
            AssertEqual("Ideology ritual stays outside Anomaly families",
                "ritualRoyal",
                ResolveInteractionGroup(
                    groups,
                    "Ritual",
                    "Ritual_Speech;RitualBehaviorWorker_ThroneSpeech",
                    true));

            XDocument windows = XDocument.Load(RepoPath("1.6", "Defs", "DiaryEventWindowDefs.xml"));
            XDocument englishWindows = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryEventWindowDef",
                "DiaryEventWindowDefs.xml"));
            XDocument russianWindows = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryEventWindowDef",
                "DiaryEventWindowDefs.xml"));
            XDocument englishKeyed = XDocument.Load(RepoPath("Languages", "English", "Keyed", "PawnDiary.xml"));
            XDocument russianKeyed = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "Keyed", "PawnDiary.xml"));

            string[] activationWindowDefNames =
            {
                "VoidMonolithActivation",
                "VoidMonolithWaking",
                "VoidMonolithVoidAwakened"
            };
            string[] monolithLevels = { "Stirring", "Waking", "VoidAwakened" };
            string[] narrativePhases = { "stirring", "waking", "void_awakened" };
            for (int windowIndex = 0; windowIndex < activationWindowDefNames.Length; windowIndex++)
            {
                string windowDefName = activationWindowDefNames[windowIndex];
                XElement window = FindDef(windows, "PawnDiary.DiaryEventWindowDef", windowDefName);
                AssertTrue("exact monolith chapter window exists: " + windowDefName, window != null);
                if (window == null)
                {
                    continue;
                }

                AssertTrue("monolith chapter window is package-gated: " + windowDefName,
                    HasListValue(window, "enableWhenPackageIdsLoaded", anomalyPackageId));
                AssertEqual("one exact trigger owns monolith level: " + monolithLevels[windowIndex],
                    1, CountMatchingEventWindowStarts(
                        windows, "VoidMonolith", "activated", monolithLevels[windowIndex]));

                XElement evidence = window.Element("narrativeEvidence");
                AssertTrue("monolith chapter emits Narrative Continuity evidence: " + windowDefName,
                    evidence != null);
                AssertEqual("monolith chapter evidence facet: " + windowDefName,
                    "journey_chapter", ChildValue(evidence, "facet"));
                AssertEqual("monolith chapter evidence phase: " + windowDefName,
                    narrativePhases[windowIndex], ChildValue(evidence, "phase"));
                AssertEqual("monolith chapter evidence arc: " + windowDefName,
                    "anomaly-monolith|0", ChildValue(evidence, "arcKey"));
                AssertEqual("monolith chapter evidence salience: " + windowDefName,
                    "major", ChildValue(evidence, "salience"));

                string[] localizedFields = { ".label", ".instruction" };
                for (int fieldIndex = 0; fieldIndex < localizedFields.Length; fieldIndex++)
                {
                    string key = windowDefName + localizedFields[fieldIndex];
                    AssertTrue("English monolith-window DefInjected value exists: " + key,
                        !string.IsNullOrWhiteSpace(KeyedValue(englishWindows, key)));
                    AssertTrue("Russian monolith-window DefInjected value exists: " + key,
                        !string.IsNullOrWhiteSpace(KeyedValue(russianWindows, key)));
                }

                string startTextKey = ChildValue(window, "startTextKey");
                AssertTrue("English monolith-window fallback exists: " + startTextKey,
                    !string.IsNullOrWhiteSpace(KeyedValue(englishKeyed, startTextKey)));
                AssertTrue("Russian monolith-window fallback exists: " + startTextKey,
                    !string.IsNullOrWhiteSpace(KeyedValue(russianKeyed, startTextKey)));
            }

            AssertEqual("automatic Gleaming level creates no activation page",
                0, CountMatchingEventWindowStarts(windows, "VoidMonolith", "activated", "Gleaming"));

            XElement monolithGroup = FindDef(
                groups, "PawnDiary.DiaryInteractionGroupDef", "eventWindowVoidMonolith");
            AssertTrue("void-monolith settings group is hidden without Anomaly",
                HasListValue(monolithGroup, "enableWhenPackageIdsLoaded", anomalyPackageId));
            AssertTrue("void-monolith interaction group includes discovery",
                HasListValue(monolithGroup, "matchDefNames", "VoidMonolithDiscovery"));
            for (int i = 0; i < activationWindowDefNames.Length; i++)
            {
                AssertTrue("void-monolith interaction group includes chapter: " + activationWindowDefNames[i],
                    HasListValue(monolithGroup, "matchDefNames", activationWindowDefNames[i]));
            }
        }

        // Pins Odyssey's departure-only Ritual group, canonical landing group, and earlier generic
        // orbital/vacuum/weather integration. Both Odyssey-owned settings rows are package-gated.
        private static void TestOdysseyExistingIntegrationXmlPolicy()
        {
            XDocument groups = XDocument.Load(RepoPath("1.6", "Defs", "DiaryInteractionGroupDefs.xml"));

            XElement gravship = FindDef(groups, "PawnDiary.DiaryInteractionGroupDef", "ritualGravship");
            AssertTrue("Odyssey gravship ritual group exists", gravship != null);
            AssertEqual("Odyssey gravship ritual domain", "Ritual", ChildValue(gravship, "domain"));
            AssertTrue("Odyssey gravship ritual matches launch identity",
                HasListValue(gravship, "matchTokens", "GravshipLaunch"));
            AssertTrue("Odyssey gravship ritual matches behavior identity",
                HasListValue(gravship, "matchTokens", "RitualBehaviorWorker_GravshipLaunch"));
            AssertTrue("Odyssey launch settings are package-gated",
                HasListValue(gravship, "enableWhenPackageIdsLoaded", "Ludeon.RimWorld.Odyssey"));
            AssertTrue("Odyssey launch wording forbids premature destination/landing claims",
                ChildValue(gravship, "instruction").IndexOf("landing occurred", StringComparison.OrdinalIgnoreCase) >= 0
                && ChildValue(gravship, "instruction").IndexOf("destination was chosen", StringComparison.OrdinalIgnoreCase) >= 0);
            AssertEqual("Odyssey launch classifier reaches gravship ritual",
                "ritualGravship",
                ResolveInteractionGroup(
                    groups,
                    "Ritual",
                    "Ritual_GravshipLaunch;RitualBehaviorWorker_GravshipLaunch",
                    true));
            AssertEqual("Odyssey launch classifier is case-insensitive",
                "ritualGravship",
                ResolveInteractionGroup(
                    groups,
                    "Ritual",
                    "ritual_gravshiplaunch;ritualbehaviorworker_gravshiplaunch",
                    true));

            XElement landing = FindDef(
                groups, "PawnDiary.DiaryInteractionGroupDef", "odysseyGravshipLanding");
            AssertTrue("Odyssey landing group exists", landing != null);
            AssertEqual("Odyssey landing group domain", "GravshipJourney", ChildValue(landing, "domain"));
            AssertTrue("Odyssey landing exact Def match",
                HasListValue(landing, "matchDefNames", "OdysseyGravshipLanding"));
            AssertTrue("Odyssey landing settings are package-gated",
                HasListValue(landing, "enableWhenPackageIdsLoaded", "Ludeon.RimWorld.Odyssey"));
            AssertEqual("Odyssey landing classifier reaches exact group",
                "odysseyGravshipLanding",
                ResolveInteractionGroup(groups, "GravshipJourney", "OdysseyGravshipLanding", true));

            AssertEqual("Odyssey orbital debris Tale routes to incident",
                "taleincident", ResolveInteractionGroup(groups, "Tale", "OrbitalDebris", false));
            AssertEqual("Odyssey vacuum reveal Tale routes to health",
                "talehealth", ResolveInteractionGroup(groups, "Tale", "VacuumExposureRevealed", false));
            AssertEqual("Odyssey volcanic ash routes to weather hardship",
                "moodeventWeatherHardship",
                ResolveInteractionGroup(groups, "MoodEvent", "VolcanicAsh", false));
            AssertEqual("stale Odyssey Flooding mood identity uses only the generic fallback",
                "moodeventOther",
                ResolveInteractionGroup(groups, "MoodEvent", "Flooding", false));

            XDocument observed = XDocument.Load(RepoPath("1.6", "Defs", "DiaryObservedConditionDefs.xml"));
            XElement volcanic = FindDef(
                observed, "PawnDiary.DiaryObservedConditionDef", "VolcanicWinterActive");
            AssertTrue("Odyssey volcanic ash observed-condition row exists", volcanic != null);
            AssertEqual("Odyssey volcanic atmosphere uses game-condition observer",
                "GameCondition", ChildValue(volcanic, "observerType"));
            AssertTrue("Odyssey volcanic atmosphere matches VolcanicAsh",
                HasListValue(volcanic, "matchDefNames", "VolcanicAsh"));
            AssertEqual("Odyssey volcanic atmosphere remains prompt-only",
                "false", ChildValue(volcanic, "recordStartEvent"));

            XElement seasonalFlood = FindDef(
                observed, "PawnDiary.DiaryObservedConditionDef", "SeasonalFloodActive");
            AssertTrue("Odyssey seasonal-flood observed-condition row exists", seasonalFlood != null);
            AssertEqual("Odyssey seasonal-flood row is package-gated",
                "Ludeon.RimWorld.Odyssey", seasonalFlood?.Attribute("MayRequire")?.Value ?? string.Empty);
            AssertEqual("Odyssey seasonal flood keeps the adapter's exact condition key",
                "SeasonalFloodActive", ChildValue(seasonalFlood, "conditionKey"));
            AssertEqual("Odyssey seasonal flood remains enabled for observation",
                "true", ChildValue(seasonalFlood, "enabled"));
            AssertEqual("Odyssey seasonal flood remains map-scoped",
                "Map", ChildValue(seasonalFlood, "scope"));
            AssertEqual("Odyssey seasonal flood uses thing-presence observer",
                "ThingPresent", ChildValue(seasonalFlood, "observerType"));
            AssertTrue("Odyssey seasonal flood matches exact installed thing identity",
                HasListValue(seasonalFlood, "matchDefNames", "SeasonalFlood"));
            int seasonalFloodMatcherCount = 0;
            XElement seasonalFloodMatchers = seasonalFlood?.Element("matchDefNames");
            if (seasonalFloodMatchers != null)
            {
                foreach (XElement ignored in seasonalFloodMatchers.Elements("li"))
                    seasonalFloodMatcherCount++;
            }
            AssertEqual("Odyssey seasonal flood admits exactly one evidence identity", 1,
                seasonalFloodMatcherCount);
            AssertEqual("Odyssey seasonal flood has scan-gap end hysteresis",
                "5000", ChildValue(seasonalFlood, "endDebounceTicks"));
            AssertEqual("Odyssey seasonal flood has restart cooldown",
                "15000", ChildValue(seasonalFlood, "restartCooldownTicks"));
            AssertEqual("Odyssey seasonal flood never records a start page",
                "false", ChildValue(seasonalFlood, "recordStartEvent"));
            AssertEqual("Odyssey seasonal flood never records an end page",
                "false", ChildValue(seasonalFlood, "recordEndEvent"));
            AssertEqual("Odyssey seasonal flood can shade authorized prompts",
                "true", ChildValue(seasonalFlood, "promptEnabled"));

            XDocument enchantments = XDocument.Load(RepoPath(
                "1.6", "Defs", "DiaryPromptEnchantmentDefs.xml"));
            XElement vacuum = FindDef(
                enchantments, "PawnDiary.DiaryPromptEnchantmentDef", "DiaryEnchant_VacuumExposure");
            AssertTrue("Odyssey vacuum enchantment row exists", vacuum != null);
            AssertTrue("Odyssey vacuum enchantment matches VacuumExposure",
                HasListValue(vacuum, "hediffDefNames", "VacuumExposure"));
            AssertTrue("Odyssey vacuum enchantment matches VacuumBurn fallback",
                HasListValue(vacuum, "hediffDefNames", "VacuumBurn"));

            XElement gravNausea = FindDef(
                enchantments, "PawnDiary.DiaryPromptEnchantmentDef", "DiaryEnchant_GravNausea");
            AssertTrue("Odyssey grav-nausea enchantment row exists", gravNausea != null);
            AssertTrue("Odyssey grav-nausea enchantment matches exact installed hediff identity",
                HasListValue(gravNausea, "hediffDefNames", "GravNausea"));
            AssertEqual("Odyssey grav-nausea chance is XML-owned", "0.7", ChildValue(gravNausea, "chance"));
            AssertEqual("Odyssey grav-nausea weight is XML-owned", "1.1", ChildValue(gravNausea, "weight"));
            AssertEqual("Odyssey grav-nausea severity is XML-owned", "1.2", ChildValue(gravNausea, "severity"));

            XDocument englishKeyed = XDocument.Load(RepoPath("Languages", "English", "Keyed", "PawnDiary.xml"));
            XDocument russianKeyed = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "Keyed", "PawnDiary.xml"));
            string[] seasonalPromptKeys =
            {
                "PawnDiary.Prompt.ObservedCondition.SeasonalFloodActive.Priority",
                "PawnDiary.Prompt.ObservedCondition.SeasonalFloodActive.Condition",
                "PawnDiary.Prompt.ObservedCondition.SeasonalFloodActive.Description",
                "PawnDiary.Prompt.ObservedCondition.SeasonalFloodActive.Cue.Paths",
                "PawnDiary.Prompt.ObservedCondition.SeasonalFloodActive.Cue.Recede"
            };
            for (int i = 0; i < seasonalPromptKeys.Length; i++)
            {
                string key = seasonalPromptKeys[i];
                AssertTrue("English Odyssey seasonal-flood prompt text exists: " + key,
                    !string.IsNullOrWhiteSpace(KeyedValue(englishKeyed, key)));
                AssertTrue("Russian Odyssey seasonal-flood prompt text exists: " + key,
                    !string.IsNullOrWhiteSpace(KeyedValue(russianKeyed, key)));
            }

            XDocument englishObserved = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryObservedConditionDef",
                "DiaryObservedConditionDefs.xml"));
            XDocument russianObserved = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryObservedConditionDef",
                "DiaryObservedConditionDefs.xml"));
            XDocument englishEnchantments = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryPromptEnchantmentDef",
                "DiaryPromptEnchantmentDefs.xml"));
            XDocument russianEnchantments = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryPromptEnchantmentDef",
                "DiaryPromptEnchantmentDefs.xml"));
            AssertTrue("English Odyssey seasonal-flood Def label exists",
                !string.IsNullOrWhiteSpace(englishObserved.Root?.Element("SeasonalFloodActive.label")?.Value));
            AssertTrue("Russian Odyssey seasonal-flood Def label exists",
                !string.IsNullOrWhiteSpace(russianObserved.Root?.Element("SeasonalFloodActive.label")?.Value));
            AssertTrue("English Odyssey grav-nausea Def label exists",
                !string.IsNullOrWhiteSpace(englishEnchantments.Root?.Element("DiaryEnchant_GravNausea.label")?.Value));
            AssertTrue("Russian Odyssey grav-nausea Def label exists",
                !string.IsNullOrWhiteSpace(russianEnchantments.Root?.Element("DiaryEnchant_GravNausea.label")?.Value));
        }

        // O1.2 adds a no-DLC-safe policy Def and two mobile-home prompt strings. This fixture locks
        // their serialized identities because later journey hooks and old saves depend on them.
        private static void TestOdysseyJourneyFoundationXmlContract()
        {
            XDocument policyDocument = XDocument.Load(RepoPath("1.6", "Defs", "DiaryOdysseyPolicyDefs.xml"));
            XElement policy = FindDef(
                policyDocument, "PawnDiary.DiaryOdysseyPolicyDef", "Diary_Odyssey");
            AssertTrue("Odyssey journey policy row exists", policy != null);
            AssertEqual("Odyssey package identity is frozen",
                "Ludeon.RimWorld.Odyssey", ChildValue(policy, "packageId"));
            AssertEqual("Odyssey launch group identity is frozen",
                "ritualGravship", ChildValue(policy, "launchGroupKey"));
            AssertEqual("Odyssey landing group identity is frozen",
                "odysseyGravshipLanding", ChildValue(policy, "landingGroupKey"));
            AssertEqual("Odyssey O1.4 novelty-gated landing pages are XML-enabled",
                "true", ChildValue(policy, "landingPageEnabled"));
            AssertTrue("Odyssey N2-O mobile-home format is XML-owned",
                !string.IsNullOrWhiteSpace(ChildValue(policy, "mobileHomeNarrativeFormat")));
            AssertTrue("Odyssey N3-O seasonal-flood format is XML-owned",
                !string.IsNullOrWhiteSpace(ChildValue(policy, "seasonalFloodNarrativeFormat")));
            XDocument englishOdyssey = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryOdysseyPolicyDef",
                "DiaryOdysseyPolicyDefs.xml"));
            XDocument russianOdyssey = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryOdysseyPolicyDef",
                "DiaryOdysseyPolicyDefs.xml"));
            string englishHome = englishOdyssey.Root?.Element(
                "Diary_Odyssey.mobileHomeNarrativeFormat")?.Value ?? string.Empty;
            string russianHome = russianOdyssey.Root?.Element(
                "Diary_Odyssey.mobileHomeNarrativeFormat")?.Value ?? string.Empty;
            string englishFlood = englishOdyssey.Root?.Element(
                "Diary_Odyssey.seasonalFloodNarrativeFormat")?.Value ?? string.Empty;
            string russianFlood = russianOdyssey.Root?.Element(
                "Diary_Odyssey.seasonalFloodNarrativeFormat")?.Value ?? string.Empty;
            AssertTrue("Odyssey N2-O English format keeps both placeholders",
                englishHome.Contains("{0}") && englishHome.Contains("{1}"));
            AssertTrue("Odyssey N2-O Russian format keeps both placeholders",
                russianHome.Contains("{0}") && russianHome.Contains("{1}"));
            AssertTrue("Odyssey N3-O English pressure format keeps both placeholders",
                englishFlood.Contains("{0}") && englishFlood.Contains("{1}"));
            AssertTrue("Odyssey N3-O Russian pressure format keeps both placeholders",
                russianFlood.Contains("{0}") && russianFlood.Contains("{1}"));
            AssertEqual("Odyssey launch writer cap", "2", ChildValue(policy, "maximumLaunchWriters"));
            AssertEqual("Odyssey landing writer cap", "2", ChildValue(policy, "maximumLandingWriters"));

            string[] requiredReasons =
            {
                "first_orbit",
                "new_biome_category",
                "major_destination",
                "homecoming",
                "long_journey",
                "rough_landing"
            };
            HashSet<string> actualReasons = new HashSet<string>(StringComparer.Ordinal);
            XElement reasonRules = policy?.Element("reasonRules");
            if (reasonRules != null)
            {
                foreach (XElement rule in reasonRules.Elements("li"))
                {
                    actualReasons.Add(ChildValue(rule, "reasonToken"));
                }
            }

            AssertEqual("Odyssey has exactly six frozen landing reasons", 6, actualReasons.Count);
            for (int i = 0; i < requiredReasons.Length; i++)
            {
                AssertTrue("Odyssey landing reason exists: " + requiredReasons[i],
                    actualReasons.Contains(requiredReasons[i]));
            }

            AssertTrue("Odyssey biome categories include orbit",
                HasStructuredListRow(policy, "biomeCategories", "defName", "Space", "categoryToken", "orbit"));
            AssertTrue("Odyssey site categories include major mechhive",
                HasStructuredListRow(policy, "siteCategories", "defName", "OrbitalMechhive",
                    "categoryToken", "mechhive"));
            AssertTrue("Odyssey policy contains no DLC XML dependency references",
                policyDocument.ToString().IndexOf("MayRequire", StringComparison.OrdinalIgnoreCase) < 0);
            string about = File.ReadAllText(RepoPath("About", "About.xml"));
            AssertTrue("Odyssey remains optional in About.xml",
                about.IndexOf("Ludeon.RimWorld.Odyssey", StringComparison.OrdinalIgnoreCase) < 0);

            XDocument narrativePolicy = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryNarrativeContinuityDefs.xml"));
            XElement narrative = FindDef(
                narrativePolicy, "PawnDiary.DiaryNarrativeContinuityDef", "Diary_NarrativeContinuity");
            AssertTrue("N3-O home and pressure coexistence is XML-owned",
                HasStructuredListRow(narrative, "categoryCoexistence",
                    "firstCategory", "home", "secondCategory", "pressure"));

            XDocument promptTemplates = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryPromptTemplateDefs.xml"));
            XElement pairImportant = FindDef(
                promptTemplates, "PawnDiary.DiaryPromptTemplateDef", "DiaryPromptTemplate_PairImportant");
            XElement soloImportant = FindDef(
                promptTemplates, "PawnDiary.DiaryPromptTemplateDef", "DiaryPromptTemplate_SoloImportant");
            string[] requiredPromptKeys =
            {
                "journey_phase",
                "journey_reason",
                "journey_secondary_reason",
                "journey_duration",
                "pov_journey_role",
                "ship_name",
                "origin",
                "destination",
                "landing_outcome"
            };
            for (int i = 0; i < requiredPromptKeys.Length; i++)
            {
                string key = requiredPromptKeys[i];
                AssertTrue("Odyssey pair-important prompt field exists: " + key,
                    HasPromptContextField(pairImportant, key));
                AssertTrue("Odyssey solo-important prompt field exists: " + key,
                    HasPromptContextField(soloImportant, key));
            }
            XDocument englishPromptLabels = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryPromptTemplateDef",
                "DiaryPromptTemplateDefs.xml"));
            XDocument russianPromptLabels = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryPromptTemplateDef",
                "DiaryPromptTemplateDefs.xml"));
            string[] outcomeLabelKeys =
            {
                "DiaryPromptTemplate_PairImportant.fields.74.label",
                "DiaryPromptTemplate_SoloImportant.fields.89.label"
            };
            for (int i = 0; i < outcomeLabelKeys.Length; i++)
            {
                string key = outcomeLabelKeys[i];
                AssertTrue("English Odyssey landing-outcome prompt label exists: " + key,
                    !string.IsNullOrWhiteSpace(englishPromptLabels.Root?.Element(key)?.Value));
                AssertTrue("Russian Odyssey landing-outcome prompt label exists: " + key,
                    !string.IsNullOrWhiteSpace(russianPromptLabels.Root?.Element(key)?.Value));
            }

            XDocument englishKeyed = XDocument.Load(RepoPath("Languages", "English", "Keyed", "PawnDiary.xml"));
            XDocument russianKeyed = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "Keyed", "PawnDiary.xml"));
            string[] mobileHomeKeys = { "PawnDiary.Ctx.GravshipHome", "PawnDiary.Ctx.GravshipHomeAt" };
            for (int i = 0; i < mobileHomeKeys.Length; i++)
            {
                AssertTrue("English Odyssey mobile-home text exists: " + mobileHomeKeys[i],
                    !string.IsNullOrWhiteSpace(KeyedValue(englishKeyed, mobileHomeKeys[i])));
                AssertTrue("Russian Odyssey mobile-home text exists: " + mobileHomeKeys[i],
                    !string.IsNullOrWhiteSpace(KeyedValue(russianKeyed, mobileHomeKeys[i])));
            }
            string[] promptFixtureKeys =
            {
                "PawnDiary.Dev.PromptSuite.OdysseyLanding.Label",
                "PawnDiary.Dev.PromptSuite.OdysseyLanding.Markers",
                "PawnDiary.Dev.PromptSuite.OdysseyLanding.Initiator",
                "PawnDiary.Dev.PromptSuite.OdysseyLanding.Recipient"
            };
            for (int i = 0; i < promptFixtureKeys.Length; i++)
            {
                string key = promptFixtureKeys[i];
                AssertTrue("English Odyssey prompt fixture text exists: " + key,
                    !string.IsNullOrWhiteSpace(KeyedValue(englishKeyed, key)));
                AssertTrue("Russian Odyssey prompt fixture text exists: " + key,
                    !string.IsNullOrWhiteSpace(KeyedValue(russianKeyed, key)));
            }
            AssertTrue("English Odyssey prompt markers retain both pawn placeholders",
                KeyedValue(englishKeyed, "PawnDiary.Dev.PromptSuite.OdysseyLanding.Markers").Contains("{0}")
                && KeyedValue(englishKeyed, "PawnDiary.Dev.PromptSuite.OdysseyLanding.Markers").Contains("{1}"));
            AssertTrue("Russian Odyssey prompt markers retain both pawn placeholders",
                KeyedValue(russianKeyed, "PawnDiary.Dev.PromptSuite.OdysseyLanding.Markers").Contains("{0}")
                && KeyedValue(russianKeyed, "PawnDiary.Dev.PromptSuite.OdysseyLanding.Markers").Contains("{1}"));

            XDocument englishDefInjected = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryOdysseyPolicyDef",
                "DiaryOdysseyPolicyDefs.xml"));
            XDocument russianDefInjected = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryOdysseyPolicyDef",
                "DiaryOdysseyPolicyDefs.xml"));
            AssertTrue("English Odyssey policy label exists",
                !string.IsNullOrWhiteSpace(KeyedValue(englishDefInjected, "Diary_Odyssey.label")));
            AssertTrue("Russian Odyssey policy label exists",
                !string.IsNullOrWhiteSpace(KeyedValue(russianDefInjected, "Diary_Odyssey.label")));
        }

        private static void TestRoyaltyNarrativeProviderXmlContract()
        {
            XDocument policyDocument = XDocument.Load(RepoPath("1.6", "Defs", "DiaryRoyaltyPolicyDefs.xml"));
            XElement policy = FindDef(
                policyDocument, "PawnDiary.DiaryRoyaltyPolicyDef", "Diary_Royalty");
            AssertTrue("Royalty N3-R policy row exists", policy != null);
            string persona = ChildValue(policy, "personaNarrativeFormat");
            string title = ChildValue(policy, "titleNarrativeFormat");
            string titleDuties = ChildValue(policy, "titleWithDutiesNarrativeFormat");
            AssertTrue("Royalty persona provider prose is XML-owned",
                persona.Contains("{0}") && persona.Contains("{1}"));
            AssertTrue("Royalty title provider prose keeps pawn/title/faction placeholders",
                title.Contains("{0}") && title.Contains("{1}") && title.Contains("{2}"));
            AssertTrue("Royalty duty provider prose keeps pawn/title/faction placeholders",
                titleDuties.Contains("{0}") && titleDuties.Contains("{1}") && titleDuties.Contains("{2}"));
            AssertTrue("Royalty policy has no paid-DLC XML reference",
                policyDocument.ToString().IndexOf("MayRequire", StringComparison.OrdinalIgnoreCase) < 0
                    && policyDocument.ToString().IndexOf("Ludeon.RimWorld.Royalty", StringComparison.OrdinalIgnoreCase) < 0);

            XDocument english = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryRoyaltyPolicyDef",
                "DiaryRoyaltyPolicyDefs.xml"));
            XDocument russian = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryRoyaltyPolicyDef",
                "DiaryRoyaltyPolicyDefs.xml"));
            string[] fields =
            {
                "personaNarrativeFormat",
                "titleNarrativeFormat",
                "titleWithDutiesNarrativeFormat"
            };
            for (int i = 0; i < fields.Length; i++)
            {
                string key = "Diary_Royalty." + fields[i];
                AssertTrue("Royalty English DefInjected prose exists: " + fields[i],
                    !string.IsNullOrWhiteSpace(english.Root?.Element(key)?.Value));
                AssertTrue("Royalty Russian DefInjected prose exists: " + fields[i],
                    !string.IsNullOrWhiteSpace(russian.Root?.Element(key)?.Value));
            }
        }

        private static void TestRoyaltyPersonaLifecycleXmlContract()
        {
            const string royaltyPackage = "Ludeon.RimWorld.Royalty";
            XDocument groups = XDocument.Load(RepoPath("1.6", "Defs", "DiaryInteractionGroupDefs.xml"));
            XElement persona = FindDef(
                groups, "PawnDiary.DiaryInteractionGroupDef", "personaWeaponLifecycle");
            AssertTrue("Royalty persona lifecycle group exists", persona != null);
            AssertEqual("Royalty persona lifecycle domain", "PersonaWeapon", ChildValue(persona, "domain"));
            AssertEqual("Royalty persona lifecycle is important", "true", ChildValue(persona, "important"));
            AssertTrue("Royalty persona lifecycle settings are package-gated",
                HasListValue(persona, "enableWhenPackageIdsLoaded", royaltyPackage));
            string[] eventNames =
            {
                "PersonaWeaponBondFormed",
                "PersonaWeaponBondSeparated",
                "PersonaWeaponBondRecovered",
                "PersonaWeaponBondEnded"
            };
            for (int i = 0; i < eventNames.Length; i++)
            {
                AssertTrue("persona group exact match exists: " + eventNames[i],
                    HasListValue(persona, "matchDefNames", eventNames[i]));
                AssertEqual("persona exact classifier resolves: " + eventNames[i],
                    "personaWeaponLifecycle",
                    ResolveInteractionGroup(groups, "PersonaWeapon", eventNames[i], true));
            }
            XElement milestoneGroup = FindDef(
                groups, "PawnDiary.DiaryInteractionGroupDef", "personaWeaponMilestone");
            AssertTrue("Royalty persona milestone group exists", milestoneGroup != null);
            AssertEqual("Royalty persona milestone remains Tale-domain", "Tale",
                ChildValue(milestoneGroup, "domain"));
            AssertEqual("Royalty persona milestone is important", "true",
                ChildValue(milestoneGroup, "important"));
            AssertTrue("Royalty persona milestone settings are package-gated",
                HasListValue(milestoneGroup, "enableWhenPackageIdsLoaded", royaltyPackage));
            AssertTrue("Royalty persona milestone exact match exists",
                HasListValue(milestoneGroup, "matchDefNames",
                    "PersonaWeaponFirstConsequentialKill"));
            AssertEqual("Royalty persona milestone exact Tale classifier resolves",
                "personaWeaponMilestone",
                ResolveInteractionGroup(
                    groups, "Tale", "PersonaWeaponFirstConsequentialKill", true));
            string[] gatedExisting = { "ritualRoyal", "progressionPsylink", "progressionRoyalTitle" };
            for (int i = 0; i < gatedExisting.Length; i++)
            {
                XElement group = FindDef(
                    groups, "PawnDiary.DiaryInteractionGroupDef", gatedExisting[i]);
                AssertTrue("existing Royalty group is package-gated: " + gatedExisting[i],
                    HasListValue(group, "enableWhenPackageIdsLoaded", royaltyPackage));
            }

            XDocument policyDocument = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryRoyaltyPolicyDefs.xml"));
            XElement policy = FindDef(
                policyDocument, "PawnDiary.DiaryRoyaltyPolicyDef", "Diary_Royalty");
            AssertEqual("persona separation reconciliation cadence is XML-owned",
                "2500", ChildValue(policy, "reconciliationCadenceTicks"));
            AssertEqual("persona kill-thought correlation is XML-owned",
                "60", ChildValue(policy, "killThoughtCorrelationTicks"));
            AssertEqual("Royalty R4 title correlation is XML-owned",
                "2500", ChildValue(policy, "titleCorrelationTicks"));
            AssertEqual("Royalty R4 psylink correlation is XML-owned",
                "2500", ChildValue(policy, "psylinkCorrelationTicks"));
            AssertEqual("Royalty R4 title-thought correlation is XML-owned",
                "2500", ChildValue(policy, "titleThoughtCorrelationTicks"));
            AssertEqual("Royalty R4 mutation queue cap is XML-owned",
                "64", ChildValue(policy, "maximumPendingRoyalMutations"));
            AssertEqual("Royalty R4 thought queue cap is XML-owned",
                "128", ChildValue(policy, "maximumPendingTitleThoughts"));
            AssertEqual("Royalty R5 succession correlation is XML-owned",
                "2500", ChildValue(policy, "successionCorrelationTicks"));
            AssertEqual("Royalty R5 committed succession cap is XML-owned",
                "64", ChildValue(policy, "maximumPendingSuccessions"));
            AssertTrue("bestowing route is an exact plain string",
                HasListValue(policy, "bestowingRitualDefNames", "BestowingCeremony"));
            AssertTrue("anima route is an exact plain string",
                HasListValue(policy, "animaRitualDefNames", "AnimaTreeLinking"));
            AssertTrue("neuroformer route is an exact plain string",
                HasListValue(policy, "neuroformerThingDefNames", "PsychicAmplifier"));

            XElement titleGroup = FindDef(
                groups, "PawnDiary.DiaryInteractionGroupDef", "progressionRoyalTitle");
            string[] titleEdges =
            {
                "RoyalTitleGained", "RoyalTitlePromoted", "RoyalTitleDemoted", "RoyalTitleLost",
                "RoyalSuccession", "RoyalHeirAppointed"
            };
            for (int i = 0; i < titleEdges.Length; i++)
                AssertTrue("title group owns exact R4 edge: " + titleEdges[i],
                    HasListValue(titleGroup, "matchDefNames", titleEdges[i]));
            XElement ritualRoyal = FindDef(
                groups, "PawnDiary.DiaryInteractionGroupDef", "ritualRoyal");
            AssertTrue("Royal ritual group matches bestowing defName",
                HasListValue(ritualRoyal, "matchTokens", "BestowingCeremony"));
            AssertTrue("Royal ritual group matches bestowing worker",
                HasListValue(ritualRoyal, "matchTokens", "RitualOutcomeEffectWorker_Bestowing"));
            List<XElement> taleRules = new List<XElement>(
                policy.Element("qualifyingTales").Elements("li"));
            AssertEqual("Royalty policy keeps one verified qualifying Tale row", 1, taleRules.Count);
            AssertEqual("Royalty milestone qualifier is the vanilla major-threat Tale",
                "KilledMajorThreat", ChildValue(taleRules[0], "taleDefName"));
            for (int i = 0; i < taleRules.Count; i++)
            {
                AssertEqual("qualifying Tale killer role is exact", "initiator",
                    ChildValue(taleRules[i], "killerRoleToken"));
                AssertEqual("qualifying Tale victim role is exact", "recipient",
                    ChildValue(taleRules[i], "victimRoleToken"));
            }
            List<XElement> companionRules = new List<XElement>(
                policy.Element("personaKillCompanionTales").Elements("li"));
            string[] expectedCompanionNames =
            {
                "KilledChild", "KilledColonist", "KilledColonyAnimal", "KilledMortar",
                "KilledLongRange", "KilledMelee", "DefeatedHostileFactionLeader", "KilledCapacity"
            };
            AssertEqual("Royalty policy owns eight exact same-kill companion Tales",
                expectedCompanionNames.Length, companionRules.Count);
            for (int i = 0; i < companionRules.Count; i++)
            {
                AssertEqual("companion Tale name is a verified vanilla def",
                    expectedCompanionNames[i], ChildValue(companionRules[i], "taleDefName"));
                AssertEqual("companion Tale killer role is exact", "initiator",
                    ChildValue(companionRules[i], "killerRoleToken"));
                AssertEqual("companion Tale victim role is exact", "recipient",
                    ChildValue(companionRules[i], "victimRoleToken"));
            }
            XElement combatTales = FindDef(
                groups, "PawnDiary.DiaryInteractionGroupDef", "talecombat");
            AssertTrue("qualifier remains the only major-threat death recipient",
                HasListValue(combatTales, "deathVictimRecipientDefNames", "KilledMajorThreat"));

            XDocument templates = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryPromptTemplateDefs.xml"));
            XElement solo = FindDef(
                templates, "PawnDiary.DiaryPromptTemplateDef", "DiaryPromptTemplate_SoloImportant");
            XElement pair = FindDef(
                templates, "PawnDiary.DiaryPromptTemplateDef", "DiaryPromptTemplate_PairImportant");
            string[] contextKeys =
            {
                "persona_weapon_name", "persona_weapon", "bond_previous_state", "bond_new_state",
                "bond_separation_duration", "bond_duration", "bond_previous_pawn", "bond_end_cause",
                "persona_trait_1", "persona_trait_description_1", "persona_trait_2",
                "persona_trait_description_2", "persona_milestone", "tale_source_def",
                "tale_source_label", "tale_killer_role", "tale_victim_role"
            };
            AssertEqual("SoloImportant Ideology Phase 1 projection remains append-only at 125 fields",
                125, new List<XElement>(solo.Element("fields").Elements("li")).Count);
            for (int i = 0; i < contextKeys.Length; i++)
            {
                AssertTrue("SoloImportant persona prompt field exists: " + contextKeys[i],
                    HasPromptContextField(solo, contextKeys[i]));
                AssertTrue("PairImportant does not acquire Phase-2 solo persona field: " + contextKeys[i],
                    !HasPromptContextField(pair, contextKeys[i]));
            }

            XDocument englishLabels = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryPromptTemplateDef",
                "DiaryPromptTemplateDefs.xml"));
            XDocument russianLabels = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryPromptTemplateDef",
                "DiaryPromptTemplateDefs.xml"));

            string[] phase4ContextKeys =
            {
                "royal_mutation_pawn", "royal_cause", "royal_transition", "royal_faction",
                "psylink_cause", "royal_duty_changes"
            };
            for (int i = 0; i < phase4ContextKeys.Length; i++)
            {
                AssertTrue("SoloImportant Royalty R4 prompt field exists: " + phase4ContextKeys[i],
                    HasPromptContextField(solo, phase4ContextKeys[i]));
                string key = "DiaryPromptTemplate_SoloImportant.fields." + (107 + i) + ".label";
                AssertTrue("English Royalty R4 prompt label exists: " + key,
                    !string.IsNullOrWhiteSpace(englishLabels.Root?.Element(key)?.Value));
                AssertTrue("Russian Royalty R4 prompt label exists: " + key,
                    !string.IsNullOrWhiteSpace(russianLabels.Root?.Element(key)?.Value));
            }

            string[] phase5ContextKeys =
            {
                "succession_deceased", "succession_heir", "succession_title", "succession_faction"
            };
            for (int i = 0; i < phase5ContextKeys.Length; i++)
            {
                AssertTrue("SoloImportant Royalty R5 prompt field exists: " + phase5ContextKeys[i],
                    HasPromptContextField(solo, phase5ContextKeys[i]));
                string key = "DiaryPromptTemplate_SoloImportant.fields." + (113 + i) + ".label";
                AssertTrue("English Royalty R5 prompt label exists: " + key,
                    !string.IsNullOrWhiteSpace(englishLabels.Root?.Element(key)?.Value));
                AssertTrue("Russian Royalty R5 prompt label exists: " + key,
                    !string.IsNullOrWhiteSpace(russianLabels.Root?.Element(key)?.Value));
            }

            XElement death = FindDef(
                templates, "PawnDiary.DiaryPromptTemplateDef", "DiaryPromptTemplate_DeathDescription");
            string[] deathContextKeys =
            {
                "persona_weapon_name", "persona_milestone", "bond_previous_state",
                "bond_new_state", "bond_end_cause", "persona_trait_1", "persona_trait_2"
            };
            AssertEqual("DeathDescription persona ending fields append at 15 total",
                15, new List<XElement>(death.Element("fields").Elements("li")).Count);
            for (int i = 0; i < deathContextKeys.Length; i++)
                AssertTrue("DeathDescription persona ending field exists: " + deathContextKeys[i],
                    HasPromptContextField(death, deathContextKeys[i]));

            for (int i = 0; i < contextKeys.Length; i++)
            {
                string key = "DiaryPromptTemplate_SoloImportant.fields." + (90 + i) + ".label";
                AssertTrue("English persona prompt label exists: " + key,
                    !string.IsNullOrWhiteSpace(englishLabels.Root?.Element(key)?.Value));
                AssertTrue("Russian persona prompt label exists: " + key,
                    !string.IsNullOrWhiteSpace(russianLabels.Root?.Element(key)?.Value));
            }

            XDocument prompts = XDocument.Load(RepoPath("1.6", "Defs", "DiaryEventPromptDefs.xml"));
            XDocument englishPrompts = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryEventPromptDef",
                "DiaryEventPromptDefs.xml"));
            XDocument russianPrompts = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryEventPromptDef",
                "DiaryEventPromptDefs.xml"));
            string[] promptSuffixes =
            {
                "PersonaWeapon", "PersonaWeaponBondFormed", "PersonaWeaponBondSeparated",
                "PersonaWeaponBondRecovered", "PersonaWeaponBondEnded",
                "PersonaWeaponFirstConsequentialKill"
            };
            for (int i = 0; i < promptSuffixes.Length; i++)
            {
                string defName = "DiaryEventPrompt_" + promptSuffixes[i];
                XElement prompt = FindDef(prompts, "PawnDiary.DiaryEventPromptDef", defName);
                AssertTrue("persona event prompt exists: " + defName, prompt != null);
                AssertEqual("persona event prompt key is exact: " + defName,
                    promptSuffixes[i], ChildValue(prompt, "eventType"));
                AssertTrue("English persona event prompt is localized: " + defName,
                    !string.IsNullOrWhiteSpace(englishPrompts.Root?.Element(defName + ".prompt")?.Value));
                AssertTrue("Russian persona event prompt is localized: " + defName,
                    !string.IsNullOrWhiteSpace(russianPrompts.Root?.Element(defName + ".prompt")?.Value));
            }

            string[] phase4PromptSuffixes =
            {
                "RoyalTitleGained", "RoyalTitlePromoted", "RoyalTitleDemoted", "RoyalTitleLost",
                "PsylinkLevel", "BestowingCeremony", "AnimaTreeLinking"
            };
            for (int i = 0; i < phase4PromptSuffixes.Length; i++)
            {
                string defName = "DiaryEventPrompt_" + phase4PromptSuffixes[i];
                XElement prompt = FindDef(prompts, "PawnDiary.DiaryEventPromptDef", defName);
                AssertTrue("Royalty R4 event prompt exists: " + defName, prompt != null);
                AssertEqual("Royalty R4 prompt key is exact: " + defName,
                    phase4PromptSuffixes[i], ChildValue(prompt, "eventType"));
                AssertTrue("English Royalty R4 event prompt localized: " + defName,
                    !string.IsNullOrWhiteSpace(englishPrompts.Root?.Element(defName + ".prompt")?.Value));
                AssertTrue("Russian Royalty R4 event prompt localized: " + defName,
                    !string.IsNullOrWhiteSpace(russianPrompts.Root?.Element(defName + ".prompt")?.Value));
            }

            string[] phase5PromptSuffixes = { "RoyalSuccession", "RoyalHeirAppointed" };
            for (int i = 0; i < phase5PromptSuffixes.Length; i++)
            {
                string defName = "DiaryEventPrompt_" + phase5PromptSuffixes[i];
                XElement prompt = FindDef(prompts, "PawnDiary.DiaryEventPromptDef", defName);
                AssertTrue("Royalty R5 event prompt exists: " + defName, prompt != null);
                AssertEqual("Royalty R5 prompt key is exact: " + defName,
                    phase5PromptSuffixes[i], ChildValue(prompt, "eventType"));
                AssertTrue("English Royalty R5 event prompt localized: " + defName,
                    !string.IsNullOrWhiteSpace(englishPrompts.Root?.Element(defName + ".prompt")?.Value));
                AssertTrue("Russian Royalty R5 event prompt localized: " + defName,
                    !string.IsNullOrWhiteSpace(russianPrompts.Root?.Element(defName + ".prompt")?.Value));
            }

            XDocument englishKeyed = XDocument.Load(
                RepoPath("Languages", "English", "Keyed", "PawnDiary.xml"));
            XDocument russianKeyed = XDocument.Load(
                RepoPath("Languages", "Russian (Русский)", "Keyed", "PawnDiary.xml"));
            string[] fixtureKeys =
            {
                "PawnDiary.Dev.PromptSuite.PersonaBondFormed.Markers",
                "PawnDiary.Dev.PromptSuite.PersonaBondSeparated.Markers",
                "PawnDiary.Dev.PromptSuite.PersonaBondRecovered.Markers",
                "PawnDiary.Dev.PromptSuite.PersonaBondEnded.Markers",
                "PawnDiary.Dev.PromptSuite.PersonaFirstConsequentialKill.Markers"
            };
            for (int i = 0; i < fixtureKeys.Length; i++)
            {
                AssertTrue("English persona prompt fixture exists: " + fixtureKeys[i],
                    !string.IsNullOrWhiteSpace(KeyedValue(englishKeyed, fixtureKeys[i])));
                AssertTrue("Russian persona prompt fixture exists: " + fixtureKeys[i],
                    !string.IsNullOrWhiteSpace(KeyedValue(russianKeyed, fixtureKeys[i])));
            }
            string[] phase4FixturePrefixes =
            {
                "RoyalBestowing", "RoyalAnimaLinking", "ProgressionPsylinkNeuroformer",
                "RoyalTitleGained", "RoyalTitlePromoted", "RoyalTitleDemoted", "RoyalTitleLost"
            };
            for (int i = 0; i < phase4FixturePrefixes.Length; i++)
            {
                string prefix = "PawnDiary.Dev.PromptSuite." + phase4FixturePrefixes[i];
                string[] suffixes = { ".Label", ".Markers", ".Text" };
                for (int j = 0; j < suffixes.Length; j++)
                {
                    string key = prefix + suffixes[j];
                    AssertTrue("English Royalty R4 fixture text exists: " + key,
                        !string.IsNullOrWhiteSpace(KeyedValue(englishKeyed, key)));
                    AssertTrue("Russian Royalty R4 fixture text exists: " + key,
                        !string.IsNullOrWhiteSpace(KeyedValue(russianKeyed, key)));
                }
            }
            string[] phase5FixturePrefixes = { "RoyalSuccession", "RoyalHeirAppointed" };
            for (int i = 0; i < phase5FixturePrefixes.Length; i++)
            {
                string prefix = "PawnDiary.Dev.PromptSuite." + phase5FixturePrefixes[i];
                string[] suffixes = { ".Label", ".Markers", ".Text" };
                for (int j = 0; j < suffixes.Length; j++)
                {
                    string key = prefix + suffixes[j];
                    AssertTrue("English Royalty R5 fixture text exists: " + key,
                        !string.IsNullOrWhiteSpace(KeyedValue(englishKeyed, key)));
                    AssertTrue("Russian Royalty R5 fixture text exists: " + key,
                        !string.IsNullOrWhiteSpace(KeyedValue(russianKeyed, key)));
                }
            }
            string[] phase4UiKeys =
            {
                "PawnDiary.Event.RoyalTitle.None", "PawnDiary.Event.RoyalTitle.UnknownFaction",
                "PawnDiary.Event.RoyalTitle.Gained.Label", "PawnDiary.Event.RoyalTitle.Gained.Text",
                "PawnDiary.Event.RoyalTitle.Promoted.Label", "PawnDiary.Event.RoyalTitle.Promoted.Text",
                "PawnDiary.Event.RoyalTitle.Demoted.Label", "PawnDiary.Event.RoyalTitle.Demoted.Text",
                "PawnDiary.Event.RoyalTitle.Lost.Label", "PawnDiary.Event.RoyalTitle.Lost.Text"
            };
            for (int i = 0; i < phase4UiKeys.Length; i++)
            {
                AssertTrue("English Royalty R4 UI text exists: " + phase4UiKeys[i],
                    !string.IsNullOrWhiteSpace(KeyedValue(englishKeyed, phase4UiKeys[i])));
                AssertTrue("Russian Royalty R4 UI text exists: " + phase4UiKeys[i],
                    !string.IsNullOrWhiteSpace(KeyedValue(russianKeyed, phase4UiKeys[i])));
            }
            string[] phase5UiKeys =
            {
                "PawnDiary.Event.RoyalSuccession.Label", "PawnDiary.Event.RoyalSuccession.Text",
                "PawnDiary.Event.RoyalHeirAppointed.Label", "PawnDiary.Event.RoyalHeirAppointed.Text"
            };
            for (int i = 0; i < phase5UiKeys.Length; i++)
            {
                AssertTrue("English Royalty R5 UI text exists: " + phase5UiKeys[i],
                    !string.IsNullOrWhiteSpace(KeyedValue(englishKeyed, phase5UiKeys[i])));
                AssertTrue("Russian Royalty R5 UI text exists: " + phase5UiKeys[i],
                    !string.IsNullOrWhiteSpace(KeyedValue(russianKeyed, phase5UiKeys[i])));
            }
        }

        private static void TestRoyaltyPermitXmlContract()
        {
            const string royaltyPackage = "Ludeon.RimWorld.Royalty";
            XDocument groups = XDocument.Load(RepoPath("1.6", "Defs", "DiaryInteractionGroupDefs.xml"));
            XElement group = FindDef(
                groups, "PawnDiary.DiaryInteractionGroupDef", "royalPermitDramatic");
            AssertTrue("Royalty dramatic-permit group exists", group != null);
            AssertEqual("Royalty dramatic-permit domain", "RoyalPermit", ChildValue(group, "domain"));
            AssertEqual("Royalty dramatic-permit group is important", "true", ChildValue(group, "important"));
            AssertTrue("Royalty dramatic-permit settings are package-gated",
                HasListValue(group, "enableWhenPackageIdsLoaded", royaltyPackage));
            string[] events =
            {
                "RoyalPermitMilitaryAid", "RoyalPermitTransportShuttle",
                "RoyalPermitOrbitalStrike", "RoyalPermitOrbitalSalvo"
            };
            for (int i = 0; i < events.Length; i++)
                AssertTrue("permit group exact match exists: " + events[i],
                    HasListValue(group, "matchDefNames", events[i]));
            string[] excluded =
            {
                "TradeSettlement", "TradeOrbital", "TradeCaravan", "SteelDrop", "FoodDrop",
                "SilverDrop", "GlitterMedDrop", "CallLaborerTeam", "CallLaborerGang"
            };
            for (int i = 0; i < excluded.Length; i++)
                AssertTrue("routine permit has no group fallback: " + excluded[i],
                    !InteractionGroupMatches(group, excluded[i]));

            XDocument policyDocument = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryRoyaltyPolicyDefs.xml"));
            XElement policy = FindDef(
                policyDocument, "PawnDiary.DiaryRoyaltyPolicyDef", "Diary_Royalty");
            string[] numericKeys =
            {
                "permitOwnerCacheTicks", "quickAidCorrelationTicks", "permitRepeatSuppressionTicks",
                "maximumPermitMappings", "maximumPermitOwnerSessions", "maximumPermitOwnersPerSession",
                "maximumPermitFallbackPawns", "maximumPendingQuickAid", "maximumRecentQuickAidOwners",
                "maximumPermitLabelCharacters", "maximumPermitSettingCharacters"
            };
            for (int i = 0; i < numericKeys.Length; i++)
                AssertTrue("permit threshold/cap is XML-owned: " + numericKeys[i],
                    !string.IsNullOrWhiteSpace(ChildValue(policy, numericKeys[i])));
            List<XElement> rows = new List<XElement>(policy.Element("permitFamilyRules").Elements("li"));
            AssertEqual("Royalty permit XML has six exact reviewed mappings", 6, rows.Count);
            string[] permitDefs =
            {
                "CallMilitaryAidSmall", "CallMilitaryAidLarge", "CallMilitaryAidGrand",
                "CallTransportShuttle", "CallOrbitalStrike", "CallOrbitalSalvo"
            };
            string[] families =
            {
                "military_aid", "military_aid", "military_aid",
                "transport_shuttle", "orbital_strike", "orbital_salvo"
            };
            for (int i = 0; i < rows.Count; i++)
            {
                AssertEqual("permit mapping order/def " + i, permitDefs[i],
                    ChildValue(rows[i], "permitDefName"));
                AssertEqual("permit mapping order/family " + i, families[i],
                    ChildValue(rows[i], "familyToken"));
            }

            XDocument templates = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryPromptTemplateDefs.xml"));
            XElement solo = FindDef(
                templates, "PawnDiary.DiaryPromptTemplateDef", "DiaryPromptTemplate_SoloImportant");
            string[] contextKeys =
            {
                "permit_label", "permit_family", "permit_faction", "permit_title",
                "permit_setting", "used_during_cooldown"
            };
            XDocument englishLabels = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryPromptTemplateDef",
                "DiaryPromptTemplateDefs.xml"));
            XDocument russianLabels = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryPromptTemplateDef",
                "DiaryPromptTemplateDefs.xml"));
            for (int i = 0; i < contextKeys.Length; i++)
            {
                AssertTrue("SoloImportant permit prompt field exists: " + contextKeys[i],
                    HasPromptContextField(solo, contextKeys[i]));
                string labelKey = "DiaryPromptTemplate_SoloImportant.fields." + (117 + i) + ".label";
                AssertTrue("English permit prompt label exists: " + labelKey,
                    !string.IsNullOrWhiteSpace(englishLabels.Root?.Element(labelKey)?.Value));
                AssertTrue("Russian permit prompt label exists: " + labelKey,
                    !string.IsNullOrWhiteSpace(russianLabels.Root?.Element(labelKey)?.Value));
            }
            AssertTrue("permit Def ID is not projected", !HasPromptContextField(solo, "permit_def"));
            AssertTrue("permit map ID is not projected", !HasPromptContextField(solo, "permit_map_id"));

            XDocument prompts = XDocument.Load(RepoPath("1.6", "Defs", "DiaryEventPromptDefs.xml"));
            XDocument englishPrompts = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryEventPromptDef",
                "DiaryEventPromptDefs.xml"));
            XDocument russianPrompts = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryEventPromptDef",
                "DiaryEventPromptDefs.xml"));
            string[] promptSuffixes =
            {
                "RoyalPermit", "RoyalPermitMilitaryAid", "RoyalPermitTransportShuttle",
                "RoyalPermitOrbitalStrike", "RoyalPermitOrbitalSalvo"
            };
            for (int i = 0; i < promptSuffixes.Length; i++)
            {
                string defName = "DiaryEventPrompt_" + promptSuffixes[i];
                XElement prompt = FindDef(prompts, "PawnDiary.DiaryEventPromptDef", defName);
                AssertTrue("Royalty permit event prompt exists: " + defName, prompt != null);
                AssertEqual("Royalty permit prompt key is exact: " + defName,
                    promptSuffixes[i], ChildValue(prompt, "eventType"));
                AssertTrue("Royalty permit Prompt Studio row is package-gated: " + defName,
                    HasListValue(prompt, "enableWhenPackageIdsLoaded", royaltyPackage));
                AssertTrue("English Royalty permit prompt localized: " + defName,
                    !string.IsNullOrWhiteSpace(englishPrompts.Root?.Element(defName + ".prompt")?.Value));
                AssertTrue("Russian Royalty permit prompt localized: " + defName,
                    !string.IsNullOrWhiteSpace(russianPrompts.Root?.Element(defName + ".prompt")?.Value));
            }

            XDocument englishKeyed = XDocument.Load(
                RepoPath("Languages", "English", "Keyed", "PawnDiary.xml"));
            XDocument russianKeyed = XDocument.Load(
                RepoPath("Languages", "Russian (Русский)", "Keyed", "PawnDiary.xml"));
            string[] fixturePrefixes =
            {
                "RoyalPermitMilitaryAid", "RoyalPermitTransportShuttle",
                "RoyalPermitOrbitalStrike", "RoyalPermitOrbitalSalvo"
            };
            for (int i = 0; i < fixturePrefixes.Length; i++)
            {
                string prefix = "PawnDiary.Dev.PromptSuite." + fixturePrefixes[i];
                string[] suffixes = { ".Label", ".Markers", ".Text" };
                for (int j = 0; j < suffixes.Length; j++)
                {
                    string key = prefix + suffixes[j];
                    AssertTrue("English Royalty permit fixture exists: " + key,
                        !string.IsNullOrWhiteSpace(KeyedValue(englishKeyed, key)));
                    AssertTrue("Russian Royalty permit fixture exists: " + key,
                        !string.IsNullOrWhiteSpace(KeyedValue(russianKeyed, key)));
                }
            }

            XDocument windows = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryEventWindowDefs.xml"));
            bool raidFriendlyWindow = false;
            foreach (XElement item in windows.Descendants("li"))
                if (string.Equals((item.Value ?? string.Empty).Trim(), "RaidFriendly",
                    StringComparison.OrdinalIgnoreCase)) raidFriendlyWindow = true;
            AssertTrue("RaidFriendly has no competing generic event-window page", !raidFriendlyWindow);
            AssertEqual("permit marker recovers RoyalPermit domain", "RoyalPermit",
                DiaryEventDomainClassifier.DomainForContext("royal_permit=military_aid"));
        }

        private static void TestRoyaltyAscentXmlContract()
        {
            const string royaltyPackage = "Ludeon.RimWorld.Royalty";
            const string rootDefName = "EndGame_RoyalAscent";
            XDocument groups = XDocument.Load(RepoPath("1.6", "Defs", "DiaryInteractionGroupDefs.xml"));
            XElement group = FindDef(groups, "PawnDiary.DiaryInteractionGroupDef", "questRoyalAscent");
            XElement acceptedGroup = FindDef(groups, "PawnDiary.DiaryInteractionGroupDef", "questAccepted");
            AssertTrue("Royal Ascent exact Quest group exists", group != null);
            AssertEqual("Royal Ascent group domain", "Quest", ChildValue(group, "domain"));
            AssertEqual("Royal Ascent fanout uses one stable witness", "MapWitness",
                ChildValue(group, "questFanoutScope"));
            AssertTrue("Royal Ascent group is exact non-catch-all",
                HasListValue(group, "matchDefNames", rootDefName)
                && !string.Equals(ChildValue(group, "catchAll"), "true", StringComparison.OrdinalIgnoreCase));
            AssertTrue("Royal Ascent root wins before generic accepted signal",
                InteractionGroupOrder(group) < InteractionGroupOrder(acceptedGroup));
            AssertTrue("Royal Ascent group is package-gated",
                HasListValue(group, "enableWhenPackageIdsLoaded", royaltyPackage));
            string groupInstruction = ChildValue(group, "instruction").ToLowerInvariant();
            AssertTrue("Royal Ascent instruction branches on saved signal",
                groupInstruction.Contains("quest_signal") && groupInstruction.Contains("completed")
                && groupInstruction.Contains("failed"));
            AssertTrue("Royal Ascent instruction forbids guessed arrival/escape",
                groupInstruction.Contains("does not prove the stellarch arrived")
                && groupInstruction.Contains("does not prove who boarded or escaped"));

            XDocument windows = XDocument.Load(RepoPath("1.6", "Defs", "DiaryEventWindowDefs.xml"));
            XElement window = FindDef(windows, "PawnDiary.DiaryEventWindowDef", "RoyalAscent");
            AssertTrue("Royal Ascent start window exists", window != null);
            AssertTrue("Royal Ascent window is package-gated",
                HasListValue(window, "enableWhenPackageIdsLoaded", royaltyPackage));
            AssertEqual("Royal Ascent window timeout is bounded", "1200000",
                ChildValue(window, "timeoutTicks"));
            AssertEqual("Royal Ascent start page enabled", "true", ChildValue(window, "recordStartEvent"));
            AssertEqual("Royal Ascent window-end duplicate disabled", "false", ChildValue(window, "recordEndEvent"));
            AssertEqual("Royal Ascent orphan terminal window page disabled", "false",
                ChildValue(window, "recordEndWithoutActive"));
            AssertEqual("Royal Ascent timeout page disabled", "false", ChildValue(window, "recordTimeoutEvent"));
            AssertEqual("Royal Ascent active pressure retained", "true", ChildValue(window, "keepActive"));
            AssertEqual("Royal Ascent start owner uses stable witness", "MapWitness",
                ChildValue(window, "recordScope"));
            XElement start = window.Element("startSignals")?.Element("li");
            AssertTrue("Royal Ascent exact accepted start trigger",
                ChildValue(start, "source") == "Quest" && ChildValue(start, "signal") == "accepted"
                && HasListValue(start, "matchDefNames", rootDefName));
            List<XElement> ends = new List<XElement>(window.Element("endSignals").Elements("li"));
            AssertEqual("Royal Ascent has exactly completion/failure closure", 2, ends.Count);
            AssertTrue("Royal Ascent completion closure is exact",
                ChildValue(ends[0], "signal") == "completed"
                && HasListValue(ends[0], "matchDefNames", rootDefName));
            AssertTrue("Royal Ascent failure closure is exact",
                ChildValue(ends[1], "signal") == "failed"
                && HasListValue(ends[1], "matchDefNames", rootDefName));
            XElement evidence = window.Element("narrativeEvidence");
            AssertEqual("Royal Ascent start evidence facet", "journey_chapter", ChildValue(evidence, "facet"));
            AssertEqual("Royal Ascent start evidence phase", "started", ChildValue(evidence, "phase"));
            AssertEqual("Royal Ascent evidence subject", "royal_ascent", ChildValue(evidence, "subjectId"));
            AssertTrue("Royal Ascent pressure owns prompt shading",
                ChildValue(window, "promptEnabled") == "true"
                && int.Parse(ChildValue(window, "promptWeight")) > 0);

            XDocument policyDoc = XDocument.Load(RepoPath("1.6", "Defs", "DiaryRoyaltyPolicyDefs.xml"));
            XElement policy = FindDef(policyDoc, "PawnDiary.DiaryRoyaltyPolicyDef", "Diary_Royalty");
            AssertEqual("Royal Ascent root mapping is XML-owned", rootDefName,
                ChildValue(policy, "royalAscentQuestDefName"));
            AssertEqual("Royal Ascent arc grammar is XML-owned", "royalty-ascent",
                ChildValue(policy, "royalAscentArcPrefix"));
            AssertTrue("Royal Ascent correlation cap is XML-owned",
                !string.IsNullOrWhiteSpace(ChildValue(policy, "maximumRoyalAscentCorrelationCharacters")));
            AssertTrue("Royal Ascent arc cap is XML-owned",
                !string.IsNullOrWhiteSpace(ChildValue(policy, "maximumRoyalAscentArcCharacters")));
            AssertTrue("Royal Ascent pressure prose is XML-owned",
                !string.IsNullOrWhiteSpace(ChildValue(policy, "royalAscentPressureNarrativeFormat")));

            XDocument prompts = XDocument.Load(RepoPath("1.6", "Defs", "DiaryEventPromptDefs.xml"));
            XElement prompt = FindDef(prompts, "PawnDiary.DiaryEventPromptDef", "DiaryEventPrompt_RoyalAscent");
            AssertTrue("Royal Ascent Prompt Studio policy exists", prompt != null);
            AssertEqual("Royal Ascent prompt resolves through exact group", "questRoyalAscent",
                ChildValue(prompt, "eventType"));
            AssertTrue("Royal Ascent Prompt Studio row is package-gated",
                HasListValue(prompt, "enableWhenPackageIdsLoaded", royaltyPackage));
            string enhancement = ChildValue(prompt, "enhancement").ToLowerInvariant();
            AssertTrue("Royal Ascent prompt forbids unproved terminal semantics",
                enhancement.Contains("never claim that the stellarch arrived")
                && enhancement.Contains("exact failure cause") && enhancement.Contains("who boarded")
                && enhancement.Contains("colony escaped"));

            XDocument englishPrompt = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryEventPromptDef",
                "DiaryEventPromptDefs.xml"));
            XDocument englishGroup = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryInteractionGroupDef",
                "DiaryInteractionGroupDefs.xml"));
            XDocument englishWindow = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryEventWindowDef",
                "DiaryEventWindowDefs.xml"));
            XDocument englishPolicy = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryRoyaltyPolicyDef",
                "DiaryRoyaltyPolicyDefs.xml"));
            XDocument englishKeyed = XDocument.Load(
                RepoPath("Languages", "English", "Keyed", "PawnDiary.xml"));
            XDocument russianPrompt = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryEventPromptDef",
                "DiaryEventPromptDefs.xml"));
            XDocument russianGroup = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryInteractionGroupDef", "DiaryInteractionGroupDefs.xml"));
            XDocument russianWindow = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryEventWindowDef",
                "DiaryEventWindowDefs.xml"));
            XDocument russianPolicy = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected", "PawnDiary.DiaryRoyaltyPolicyDef",
                "DiaryRoyaltyPolicyDefs.xml"));
            XDocument russianKeyed = XDocument.Load(
                RepoPath("Languages", "Russian (Русский)", "Keyed", "PawnDiary.xml"));
            AssertTrue("Royal Ascent exact prompt is DefInjected",
                !string.IsNullOrWhiteSpace(
                    englishPrompt.Root?.Element("DiaryEventPrompt_RoyalAscent.prompt")?.Value));
            AssertTrue("Royal Ascent group instruction is DefInjected",
                !string.IsNullOrWhiteSpace(
                    englishGroup.Root?.Element("questRoyalAscent.instruction")?.Value));
            AssertTrue("Royal Ascent window instruction is DefInjected",
                !string.IsNullOrWhiteSpace(englishWindow.Root?.Element("RoyalAscent.instruction")?.Value));
            AssertTrue("Royal Ascent pressure prose is DefInjected",
                !string.IsNullOrWhiteSpace(
                    englishPolicy.Root?.Element("Diary_Royalty.royalAscentPressureNarrativeFormat")?.Value));
            AssertTrue("Royal Ascent Russian exact prompt is DefInjected",
                !string.IsNullOrWhiteSpace(
                    russianPrompt.Root?.Element("DiaryEventPrompt_RoyalAscent.prompt")?.Value));
            AssertTrue("Royal Ascent Russian group instruction is DefInjected",
                !string.IsNullOrWhiteSpace(
                    russianGroup.Root?.Element("questRoyalAscent.instruction")?.Value));
            AssertTrue("Royal Ascent Russian window instruction is DefInjected",
                !string.IsNullOrWhiteSpace(russianWindow.Root?.Element("RoyalAscent.instruction")?.Value));
            AssertTrue("Royal Ascent Russian pressure prose is DefInjected",
                !string.IsNullOrWhiteSpace(
                    russianPolicy.Root?.Element("Diary_Royalty.royalAscentPressureNarrativeFormat")?.Value));
            string[] keyed =
            {
                "PawnDiary.Event.EventWindow.RoyalAscent.Start",
                "PawnDiary.Prompt.EventWindow.RoyalAscent.Priority",
                "PawnDiary.Prompt.EventWindow.RoyalAscent.Condition",
                "PawnDiary.Prompt.EventWindow.RoyalAscent.Description",
                "PawnDiary.Prompt.EventWindow.RoyalAscent.Cue.Duty",
                "PawnDiary.Dev.PromptSuite.RoyalAscentStarted.Label",
                "PawnDiary.Dev.PromptSuite.RoyalAscentStarted.Markers",
                "PawnDiary.Dev.PromptSuite.RoyalAscentStarted.Text",
                "PawnDiary.Dev.PromptSuite.RoyalAscentCompleted.Label",
                "PawnDiary.Dev.PromptSuite.RoyalAscentCompleted.Markers",
                "PawnDiary.Dev.PromptSuite.RoyalAscentCompleted.Text",
                "PawnDiary.Dev.PromptSuite.RoyalAscentFailed.Label",
                "PawnDiary.Dev.PromptSuite.RoyalAscentFailed.Markers",
                "PawnDiary.Dev.PromptSuite.RoyalAscentFailed.Text"
            };
            for (int i = 0; i < keyed.Length; i++)
            {
                AssertTrue("Royal Ascent English keyed text exists: " + keyed[i],
                    !string.IsNullOrWhiteSpace(KeyedValue(englishKeyed, keyed[i])));
                AssertTrue("Royal Ascent Russian keyed text exists: " + keyed[i],
                    !string.IsNullOrWhiteSpace(KeyedValue(russianKeyed, keyed[i])));
            }

            string about = File.ReadAllText(RepoPath("About", "About.xml"));
            AssertTrue("Royalty remains optional in About.xml", !about.Contains(royaltyPackage));
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

        private static void TestApiLaneImport()
        {
            // Auth-mode token round-trips, including the legacy header modes that normalize to custom.
            AssertEqual("auth token bearer", "bearer", ApiLaneImport.AuthModeToken(ApiAuthMode.BearerToken));
            AssertEqual("auth token none", "none", ApiLaneImport.AuthModeToken(ApiAuthMode.None));
            AssertEqual("auth token custom", "customHeader", ApiLaneImport.AuthModeToken(ApiAuthMode.CustomHeader));
            AssertEqual("auth token query", "queryParam", ApiLaneImport.AuthModeToken(ApiAuthMode.QueryParameterKey));
            AssertEqual("legacy api-key header token maps to custom", "customHeader",
                ApiLaneImport.AuthModeToken(ApiAuthMode.ApiKeyHeader));

            AssertTrue("parse bearer default", ApiLaneImport.ParseAuthMode("bearer") == ApiAuthMode.BearerToken);
            AssertTrue("parse none", ApiLaneImport.ParseAuthMode(" NONE ") == ApiAuthMode.None);
            AssertTrue("parse custom synonym", ApiLaneImport.ParseAuthMode("custom") == ApiAuthMode.CustomHeader);
            AssertTrue("parse query synonym", ApiLaneImport.ParseAuthMode("queryParameterKey") == ApiAuthMode.QueryParameterKey);
            AssertTrue("parse unknown falls back to bearer", ApiLaneImport.ParseAuthMode("mystery") == ApiAuthMode.BearerToken);

            // Compatibility-mode tokens.
            AssertEqual("api mode chat token", "chatCompletions", ApiLaneImport.ApiModeToken(ApiCompatibilityMode.OpenAIChatCompletions));
            AssertEqual("api mode responses token", "responses", ApiLaneImport.ApiModeToken(ApiCompatibilityMode.OpenAIResponses));
            AssertTrue("parse responses", ApiLaneImport.ParseApiMode(" Responses ") == ApiCompatibilityMode.OpenAIResponses);
            AssertTrue("parse api mode default", ApiLaneImport.ParseApiMode("something") == ApiCompatibilityMode.OpenAIChatCompletions);

            // Routing + context-detail override tokens.
            AssertEqual("routing balanced token", "balanced", ApiLaneImport.RoutingModeToken(ApiLaneRoutingMode.Balanced));
            AssertEqual("routing prefer-top token", "preferTopRows", ApiLaneImport.RoutingModeToken(ApiLaneRoutingMode.PreferTopRows));
            AssertEqual("routing failover token", "failoverOnly", ApiLaneImport.RoutingModeToken(ApiLaneRoutingMode.FailoverOnly));
            AssertEqual("context inherit token", "inherit", ApiLaneImport.ContextDetailOverrideToken(PromptContextDetailOverride.Inherit));
            AssertEqual("context compact token", "compact", ApiLaneImport.ContextDetailOverrideToken(PromptContextDetailOverride.Compact));
            AssertTrue("parse context balanced", ApiLaneImport.ParseContextDetailOverride("balanced") == PromptContextDetailOverride.Balanced);
            AssertTrue("parse context unknown inherits", ApiLaneImport.ParseContextDetailOverride("weird") == PromptContextDetailOverride.Inherit);

            // Add-request validation: url and model are both required.
            AssertEqual("validate blank url", ApiLaneImport.ReasonMissingUrl, ApiLaneImport.ValidateAddRequest("  ", "gpt-4o-mini"));
            AssertEqual("validate blank model", ApiLaneImport.ReasonMissingModel, ApiLaneImport.ValidateAddRequest("https://x/v1", " "));
            AssertEqual("validate ok", ApiLaneImport.ReasonOk, ApiLaneImport.ValidateAddRequest("https://x/v1", "gpt-4o-mini"));

            // Duplicate identity check the add-lane path relies on (ApiLaneIdentity.ForGate): the
            // normalized endpoint + trimmed model + apiMode + effective auth/key collapse a trailing
            // slash, a /chat/completions suffix, host casing, model spacing, and a stale no-auth key
            // into one identity, so two equivalent lanes compare equal.
            AssertEqual("dup identity ignores url suffix, model spacing, and stale no-auth key",
                ApiLaneIdentity.ForGate("https://example.test/v1/chat/completions", " gpt ",
                    ApiCompatibilityMode.OpenAIChatCompletions, ApiAuthMode.None, string.Empty, "old"),
                ApiLaneIdentity.ForGate("https://EXAMPLE.test/v1/", "gpt",
                    ApiCompatibilityMode.OpenAIChatCompletions, ApiAuthMode.None, string.Empty, "new"));
            AssertTrue("different model on the same endpoint is a distinct lane",
                ApiLaneIdentity.ForGate("https://example.test/v1", "gpt-a",
                    ApiCompatibilityMode.OpenAIChatCompletions, ApiAuthMode.BearerToken, string.Empty, "k")
                != ApiLaneIdentity.ForGate("https://example.test/v1", "gpt-b",
                    ApiCompatibilityMode.OpenAIChatCompletions, ApiAuthMode.BearerToken, string.Empty, "k"));
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
            AssertEqual("quest root classifier preserves exact saved root", "EndGame_RoyalAscent",
                DiaryEventDomainClassifier.QuestRootClassifierKey(
                    "Quest",
                    "quest=EndGame_RoyalAscent; signal=completed; label=Royal Ascent",
                    "RoyalAscent"));
            AssertEqual("quest root classifier migrates to saved defName when marker is missing",
                "EndGame_RoyalAscent",
                DiaryEventDomainClassifier.QuestRootClassifierKey(
                    "Quest", "signal=failed; label=Royal Ascent", "EndGame_RoyalAscent"));
            AssertEqual("non-Quest domain has no quest root classifier", string.Empty,
                DiaryEventDomainClassifier.QuestRootClassifierKey(
                    "Raid", "quest=EndGame_RoyalAscent; signal=completed", "RaidEnemy"));
            AssertEqual("ritual marker domain", "Ritual",
                DiaryEventDomainClassifier.DomainForContext("ritual=Ritual_Speech; ritual_title=Leader's address; ritual_role=author"));
            AssertEqual("psychic ritual marker domain", "Ritual",
                DiaryEventDomainClassifier.DomainForContext("psychic_ritual=VoidProvocation; psychic_ritual_perspective=invoker"));
            AssertEqual("ability marker domain", "Ability",
                DiaryEventDomainClassifier.DomainForContext("ability=Stun; ability_label=stun; ability_category=Psycast"));
            AssertEqual("progression marker domain", "Progression",
                DiaryEventDomainClassifier.DomainForContext("progression=SkillMilestone; progression_kind=skill"));
            AssertEqual("Odyssey journey marker domain", "GravshipJourney",
                DiaryEventDomainClassifier.DomainForContext(
                    "odyssey_journey=true; journey_phase=landing; journey_reason=homecoming"));
            AssertEqual("persona weapon marker domain", "PersonaWeapon",
                DiaryEventDomainClassifier.DomainForContext(
                    "persona_weapon=bond_recovered; persona_weapon_name=Quiet Edge"));
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

        private static void TestIdeologyCrisisXmlContract()
        {
            XDocument groups = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryInteractionGroupDefs.xml"));
            XElement crisis = FindDef(
                groups, "PawnDiary.DiaryInteractionGroupDef", "beliefCrisis");
            XElement fallback = FindDef(
                groups, "PawnDiary.DiaryInteractionGroupDef", "mentalbreak");
            XElement berserk = FindDef(
                groups, "PawnDiary.DiaryInteractionGroupDef", "mentalbreakViolent");
            AssertTrue("IdeoChange crisis group exists", crisis != null);
            AssertEqual("IdeoChange crisis domain", "MentalState", ChildValue(crisis, "domain"));
            AssertTrue("IdeoChange crisis uses one exact Def matcher",
                crisis.Element("matchDefNames")?.Elements("li").Count() == 1
                    && HasListValue(crisis, "matchDefNames", "IdeoChange")
                    && !HasListValue(crisis, "matchPrefixes", null)
                    && !HasListValue(crisis, "matchSegments", null)
                    && !HasListValue(crisis, "matchTokens", null));
            AssertTrue("IdeoChange crisis precedes the generic mental-state fallback",
                InteractionGroupOrder(crisis) < InteractionGroupOrder(fallback));
            AssertTrue("ordinary Berserk remains owned by the violent mental-state group",
                !HasListValue(crisis, "matchDefNames", "Berserk")
                    && HasListValue(berserk, "matchDefNames", "Berserk"));
            AssertTrue("IdeoChange crisis is package-gated to optional Ideology",
                HasListValue(crisis, "enableWhenPackageIdsLoaded", "Ludeon.RimWorld.Ideology"));

            XDocument prompts = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryEventPromptDefs.xml"));
            XElement exactPrompt = FindDef(
                prompts, "PawnDiary.DiaryEventPromptDef", "DiaryEventPrompt_IdeoChange");
            AssertTrue("exact IdeoChange event prompt exists", exactPrompt != null);
            AssertEqual("exact IdeoChange prompt key", "IdeoChange",
                ChildValue(exactPrompt, "eventType"));
            AssertTrue("exact IdeoChange prompt is package-gated to optional Ideology",
                HasListValue(exactPrompt, "enableWhenPackageIdsLoaded", "Ludeon.RimWorld.Ideology"));

            List<string> keys = DiaryEventPromptKeys.CandidateKeys(
                new DiaryEventPayload { defName = "IdeoChange" },
                "beliefCrisis", "IdeoChange", "MentalState");
            AssertEqual("IdeoChange prompt exact key wins first", "IdeoChange", keys[0]);
            AssertEqual("IdeoChange group prompt remains second", "beliefCrisis", keys[1]);
            AssertEqual("IdeoChange mental-state fallback remains last", "MentalState", keys[2]);
            AssertEqual("duplicate classifier key is removed", 3, keys.Count);

            XDocument policy = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryBeliefPolicyDef.xml"));
            List<XElement> crisisRules = policy.Descendants("mutationEventRules")
                .Elements("li")
                .Where(row => string.Equals(ChildValue(row, "sourceDomain"), "mental_state",
                        StringComparison.Ordinal)
                    && string.Equals(ChildValue(row, "sourceDefName"), "IdeoChange",
                        StringComparison.Ordinal))
                .ToList();
            AssertEqual("one exact IdeoChange mutation rule ships", 1, crisisRules.Count);
            AssertEqual("IdeoChange mutation rule requires crisis ownership", "beliefCrisis",
                ChildValue(crisisRules[0], "downstreamGroupDefName"));
            AssertEqual("IdeoChange mutation rule keeps crisis evidence key", "crisis",
                ChildValue(crisisRules[0], "evidenceGroupKey"));

            XDocument englishGroups = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryInteractionGroupDef",
                "DiaryInteractionGroupDefs.xml"));
            XDocument russianGroups = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryInteractionGroupDef", "DiaryInteractionGroupDefs.xml"));
            XDocument englishPrompts = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryEventPromptDef",
                "DiaryEventPromptDefs.xml"));
            XDocument russianPrompts = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryEventPromptDef", "DiaryEventPromptDefs.xml"));
            XDocument englishBelief = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryBeliefPolicyDef",
                "DiaryBeliefPolicyDef.xml"));
            XDocument russianBelief = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryBeliefPolicyDef", "DiaryBeliefPolicyDef.xml"));
            AssertTrue("English crisis group text is localized",
                !string.IsNullOrWhiteSpace(ChildValue(englishGroups.Root, "beliefCrisis.instruction")));
            AssertTrue("Russian crisis group text is localized",
                !string.IsNullOrWhiteSpace(ChildValue(russianGroups.Root, "beliefCrisis.instruction")));
            AssertTrue("English exact crisis prompt is localized",
                !string.IsNullOrWhiteSpace(ChildValue(
                    englishPrompts.Root, "DiaryEventPrompt_IdeoChange.enhancement")));
            AssertTrue("Russian exact crisis prompt is localized",
                !string.IsNullOrWhiteSpace(ChildValue(
                    russianPrompts.Root, "DiaryEventPrompt_IdeoChange.enhancement")));

            string englishInterpretation = ChildValue(
                englishBelief.Root, "Diary_BeliefPolicy.interpretationFactFormat");
            string russianInterpretation = ChildValue(
                russianBelief.Root, "Diary_BeliefPolicy.interpretationFactFormat");
            string englishConciseInterpretation = ChildValue(
                englishBelief.Root, "Diary_BeliefPolicy.interpretationFactWithoutDescriptionFormat");
            string russianConciseInterpretation = ChildValue(
                russianBelief.Root, "Diary_BeliefPolicy.interpretationFactWithoutDescriptionFormat");
            AssertTrue("English N3-I described format keeps all three placeholders",
                englishInterpretation.Contains("{0}") && englishInterpretation.Contains("{1}")
                    && englishInterpretation.Contains("{2}") && !englishInterpretation.Contains("{3}"));
            AssertTrue("Russian N3-I described format keeps all three placeholders",
                russianInterpretation.Contains("{0}") && russianInterpretation.Contains("{1}")
                    && russianInterpretation.Contains("{2}") && !russianInterpretation.Contains("{3}"));
            AssertTrue("English N3-I concise format keeps exactly the identity placeholders",
                englishConciseInterpretation.Contains("{0}")
                    && englishConciseInterpretation.Contains("{1}")
                    && !englishConciseInterpretation.Contains("{2}"));
            AssertTrue("Russian N3-I concise format keeps exactly the identity placeholders",
                russianConciseInterpretation.Contains("{0}")
                    && russianConciseInterpretation.Contains("{1}")
                    && !russianConciseInterpretation.Contains("{2}"));

            string fallbackCrisisText = (ChildValue(crisis, "instruction") + " "
                + ChildValue(exactPrompt, "enhancement")).ToLowerInvariant();
            string englishCrisisText = (ChildValue(englishGroups.Root, "beliefCrisis.label") + " "
                + ChildValue(englishGroups.Root, "beliefCrisis.instruction") + " "
                + ChildValue(englishPrompts.Root, "DiaryEventPrompt_IdeoChange.label") + " "
                + ChildValue(englishPrompts.Root, "DiaryEventPrompt_IdeoChange.enhancement"))
                .ToLowerInvariant();
            string russianCrisisText = (ChildValue(russianGroups.Root, "beliefCrisis.label") + " "
                + ChildValue(russianGroups.Root, "beliefCrisis.instruction") + " "
                + ChildValue(russianPrompts.Root, "DiaryEventPrompt_IdeoChange.label") + " "
                + ChildValue(russianPrompts.Root, "DiaryEventPrompt_IdeoChange.prompt") + " "
                + ChildValue(russianPrompts.Root, "DiaryEventPrompt_IdeoChange.enhancement"))
                .ToLowerInvariant();
            AssertTrue("fallback crisis prose stays neutral for secular ideoligions",
                fallbackCrisisText.IndexOf("faith", StringComparison.Ordinal) < 0);
            AssertTrue("English crisis prose stays neutral for secular ideoligions",
                englishCrisisText.IndexOf("faith", StringComparison.Ordinal) < 0);
            AssertTrue("Russian crisis prose avoids standalone faith framing",
                !russianCrisisText.StartsWith("вер", StringComparison.Ordinal)
                    && russianCrisisText.IndexOf(" вер", StringComparison.Ordinal) < 0);
        }

        private static void TestIdeologyCounselXmlContract()
        {
            XDocument groups = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryInteractionGroupDefs.xml"));
            XElement counsel = FindDef(
                groups, "PawnDiary.DiaryInteractionGroupDef", "counsel");
            XElement conversion = FindDef(
                groups, "PawnDiary.DiaryInteractionGroupDef", "conversion");
            AssertTrue("exact Counsel interaction group exists", counsel != null);
            AssertEqual("Counsel interaction domain", "Interaction", ChildValue(counsel, "domain"));
            AssertTrue("Counsel group uses only the two ordinal-exact interaction DefNames",
                counsel.Element("matchOrdinalDefNames")?.Elements("li").Count() == 2
                    && HasListValue(counsel, "matchOrdinalDefNames", "Counsel_Success")
                    && HasListValue(counsel, "matchOrdinalDefNames", "Counsel_Failure")
                    && counsel.Element("matchDefNames") == null
                    && counsel.Element("matchTokens") == null
                    && counsel.Element("matchPrefixes") == null
                    && counsel.Element("matchSegments") == null);
            AssertTrue("Counsel group supplies tone-rotation parity",
                counsel.Element("tones")?.Elements("li").Count() >= 2);
            AssertTrue("Counsel group precedes conversion", InteractionGroupOrder(counsel)
                < InteractionGroupOrder(conversion));
            AssertTrue("Counsel group is package-gated to optional Ideology",
                HasListValue(counsel, "enableWhenPackageIdsLoaded", "Ludeon.RimWorld.Ideology"));
            AssertTrue("conversion group no longer claims Counsel names or token",
                !HasListValue(conversion, "matchDefNames", "Counsel_Success")
                    && !HasListValue(conversion, "matchDefNames", "Counsel_Failure")
                    && !HasListValue(conversion, "matchTokens", "Counsel"));

            XDocument prompts = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryEventPromptDefs.xml"));
            string[] sources = { "Counsel_Success", "Counsel_Failure" };
            string[] promptDefs = { "DiaryEventPrompt_CounselSuccess", "DiaryEventPrompt_CounselFailure" };
            for (int i = 0; i < sources.Length; i++)
            {
                XElement prompt = FindDef(
                    prompts, "PawnDiary.DiaryEventPromptDef", promptDefs[i]);
                AssertTrue("exact Counsel prompt exists: " + sources[i], prompt != null);
                AssertEqual("exact Counsel prompt key: " + sources[i], sources[i],
                    ChildValue(prompt, "eventType"));
                AssertTrue("exact Counsel prompt is package-gated: " + sources[i],
                    HasListValue(prompt, "enableWhenPackageIdsLoaded", "Ludeon.RimWorld.Ideology"));
                List<string> keys = DiaryEventPromptKeys.CandidateKeys(
                    new DiaryEventPayload { defName = sources[i] },
                    "counsel", sources[i], "Interaction");
                AssertEqual("exact Counsel prompt key wins: " + sources[i], sources[i], keys[0]);
                AssertEqual("Counsel group prompt remains second: " + sources[i], "counsel", keys[1]);
                AssertEqual("Counsel Interaction fallback remains last: " + sources[i],
                    "Interaction", keys[2]);
            }

            XDocument policy = XDocument.Load(
                RepoPath("1.6", "Defs", "DiaryBeliefPolicyDef.xml"));
            List<XElement> ownership = policy.Descendants("canonicalEventOwnershipRules")
                .Elements("li")
                .Where(row => string.Equals(
                    ChildValue(row, "downstreamGroupDefName"), "counsel", StringComparison.Ordinal))
                .ToList();
            AssertEqual("Counsel ships four exact downstream ownership rows", 4, ownership.Count);
            AssertTrue("Counsel ability ownership is exact",
                HasOwnership(ownership, "ability", "Counsel"));
            AssertTrue("Counsel success-memory ownership is exact",
                HasOwnership(ownership, "thought", "Counselled"));
            AssertTrue("Counsel mood-boost ownership is exact",
                HasOwnership(ownership, "thought", "Counselled_MoodBoost"));
            AssertTrue("Counsel failure-memory ownership is exact",
                HasOwnership(ownership, "thought", "CounselFailed"));
            AssertTrue("Counsel has no ideology mutation rule",
                !policy.Descendants("mutationEventRules").Elements("li")
                    .Any(row => ChildValue(row, "sourceDefName")
                        .StartsWith("Counsel", StringComparison.Ordinal)));
            List<XElement> counselRules = policy.Descendants("counselEventRules")
                .Elements("li").ToList();
            AssertEqual("Counsel ships two exact context-only outcome rules", 2, counselRules.Count);
            AssertTrue("Counsel success context rule is XML-owned",
                counselRules.Any(row => ChildValue(row, "sourceDefName") == "Counsel_Success"
                    && ChildValue(row, "downstreamGroupDefName") == "counsel"
                    && ChildValue(row, "resultToken") == "success"
                    && ChildValue(row, "moodEffectToken") == "relief_or_boost"));
            AssertTrue("Counsel failure context rule is XML-owned",
                counselRules.Any(row => ChildValue(row, "sourceDefName") == "Counsel_Failure"
                    && ChildValue(row, "downstreamGroupDefName") == "counsel"
                    && ChildValue(row, "resultToken") == "failure"
                    && ChildValue(row, "moodEffectToken") == "penalty"));

            XDocument englishGroups = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryInteractionGroupDef",
                "DiaryInteractionGroupDefs.xml"));
            XDocument russianGroups = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryInteractionGroupDef", "DiaryInteractionGroupDefs.xml"));
            XDocument englishPrompts = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected", "PawnDiary.DiaryEventPromptDef",
                "DiaryEventPromptDefs.xml"));
            XDocument russianPrompts = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryEventPromptDef", "DiaryEventPromptDefs.xml"));
            AssertPlaceholderParity("Counsel group label",
                ChildValue(englishGroups.Root, "counsel.label"),
                ChildValue(russianGroups.Root, "counsel.label"));
            AssertPlaceholderParity("Counsel group instruction",
                ChildValue(englishGroups.Root, "counsel.instruction"),
                ChildValue(russianGroups.Root, "counsel.instruction"));
            AssertTrue("English Counsel tone variants are localized",
                !string.IsNullOrWhiteSpace(ChildValue(englishGroups.Root, "counsel.tones.0"))
                    && !string.IsNullOrWhiteSpace(ChildValue(englishGroups.Root, "counsel.tones.1")));
            AssertTrue("Russian Counsel tone variants are localized",
                !string.IsNullOrWhiteSpace(ChildValue(russianGroups.Root, "counsel.tones.0"))
                    && !string.IsNullOrWhiteSpace(ChildValue(russianGroups.Root, "counsel.tones.1")));
            for (int i = 0; i < promptDefs.Length; i++)
            {
                string key = promptDefs[i];
                AssertTrue("English Counsel prompt is localized: " + key,
                    !string.IsNullOrWhiteSpace(ChildValue(englishPrompts.Root, key + ".prompt")));
                AssertTrue("Russian Counsel prompt is localized: " + key,
                    !string.IsNullOrWhiteSpace(ChildValue(russianPrompts.Root, key + ".prompt")));
                AssertPlaceholderParity(key + " prompt",
                    ChildValue(englishPrompts.Root, key + ".prompt"),
                    ChildValue(russianPrompts.Root, key + ".prompt"));
                AssertPlaceholderParity(key + " enhancement",
                    ChildValue(englishPrompts.Root, key + ".enhancement"),
                    ChildValue(russianPrompts.Root, key + ".enhancement"));
            }

            string fallbackText = (ChildValue(counsel, "instruction") + " "
                + string.Join(" ", promptDefs.Select(key =>
                    ChildValue(FindDef(prompts, "PawnDiary.DiaryEventPromptDef", key), "enhancement"))))
                .ToLowerInvariant();
            string englishText = (ChildValue(englishGroups.Root, "counsel.instruction") + " "
                + string.Join(" ", promptDefs.Select(key =>
                    ChildValue(englishPrompts.Root, key + ".enhancement"))))
                .ToLowerInvariant();
            string russianText = (ChildValue(russianGroups.Root, "counsel.instruction") + " "
                + string.Join(" ", promptDefs.Select(key =>
                    ChildValue(russianPrompts.Root, key + ".enhancement"))))
                .ToLowerInvariant();
            AssertTrue("fallback Counsel prose does not inherit conversion framing",
                fallbackText.IndexOf("battle of belief", StringComparison.Ordinal) < 0
                    && fallbackText.IndexOf("conversion", StringComparison.Ordinal) < 0
                    && fallbackText.IndexOf("faith on faith", StringComparison.Ordinal) < 0);
            AssertTrue("English Counsel prose does not inherit conversion framing",
                englishText.IndexOf("battle of belief", StringComparison.Ordinal) < 0
                    && englishText.IndexOf("conversion", StringComparison.Ordinal) < 0
                    && englishText.IndexOf("faith on faith", StringComparison.Ordinal) < 0);
            AssertTrue("Russian Counsel prose does not inherit conversion framing",
                russianText.IndexOf("обращен", StringComparison.Ordinal) < 0
                    && russianText.IndexOf("битва убеждений", StringComparison.Ordinal) < 0);
            AssertTrue("Counsel prose is explicitly grounded in mood",
                fallbackText.IndexOf("mood", StringComparison.Ordinal) >= 0
                    && englishText.IndexOf("mood", StringComparison.Ordinal) >= 0
                    && russianText.IndexOf("настроен", StringComparison.Ordinal) >= 0);
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
                    Field("setting", "DeathSetting"),
                    ContextField("persona weapon", "persona_weapon_name"),
                    ContextField("persona milestone", "persona_milestone"),
                    ContextField("previous bond state", "bond_previous_state"),
                    ContextField("new bond state", "bond_new_state"),
                    ContextField("bond ending cause", "bond_end_cause"),
                    ContextField("persona trait 1", "persona_trait_1"),
                    ContextField("persona trait 2", "persona_trait_2"));
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
                    ContextField("trait", "trait"),
                    ContextField("trait description", "trait_description"),
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
                    ContextField("birthday age", "birthday_age"),
                    ContextField("opportunity at growth", "opportunity_description"),
                    ContextField("chosen trait", "selected_trait"),
                    ContextField("chosen trait meaning", "selected_trait_description"),
                    ContextField("interest 1", "new_interest_1"),
                    ContextField("interest 1 change", "interest_change_1"),
                    ContextField("interest 2", "new_interest_2"),
                    ContextField("interest 2 change", "interest_change_2"),
                    ContextField("interest 3", "new_interest_3"),
                    ContextField("interest 3 change", "interest_change_3"),
                    ContextField("interest 4", "new_interest_4"),
                    ContextField("interest 4 change", "interest_change_4"),
                    ContextField("observed upbringing", "observed_upbringing_description"),
                    ContextField("previous name", "previous_name"),
                    ContextField("current name", "current_name"),
                    ContextField("new responsibilities", "new_responsibilities"),
                    ContextField("supporting adult", "supporter_name"),
                    ContextField("supporting adult role", "supporter_role"),
                    ContextField("initiator family role", "initiator_family_role"),
                    ContextField("recipient family role", "recipient_family_role"),
                    ContextField("child", "child_name"),
                    ContextField("birth outcome", "birth_outcome"),
                    ContextField("birth method", "birth_method"),
                    ContextField("birther", "birther_name"),
                    ContextField("genetic mother", "genetic_mother_name"),
                    ContextField("father", "father_name"),
                    ContextField("doctor", "doctor_name"),
                    ContextField("birther died", "birther_died"),
                    ContextField("ritual birth", "ritual_birth"),
                    ContextField("journey phase", "journey_phase"),
                    ContextField("journey reason", "journey_reason"),
                    ContextField("secondary journey reason", "journey_secondary_reason"),
                    ContextField("journey duration", "journey_duration"),
                    ContextField("journey role", "pov_journey_role"),
                    ContextField("ship", "ship_name"),
                    ContextField("origin", "origin"),
                    ContextField("destination", "destination"),
                    ContextField("destination layer", "destination_layer"),
                    ContextField("destination biome", "destination_biome"),
                    ContextField("destination site", "destination_site"),
                    ContextField("pilot", "pilot"),
                    ContextField("copilot", "copilot"),
                    ContextField("crew count", "crew_count"),
                    ContextField("rough landing", "rough_landing"),
                    ContextField("launch quality", "launch_quality"),
                    ContextField("landing outcome", "landing_outcome"),
                    ContextField("persona weapon", "persona_weapon_name"),
                    ContextField("bond event", "persona_weapon"),
                    ContextField("previous bond state", "bond_previous_state"),
                    ContextField("new bond state", "bond_new_state"),
                    ContextField("separation duration", "bond_separation_duration"),
                    ContextField("bond duration", "bond_duration"),
                    ContextField("previous bonded pawn", "bond_previous_pawn"),
                    ContextField("bond ending cause", "bond_end_cause"),
                    ContextField("persona trait 1", "persona_trait_1"),
                    ContextField("persona trait 1 meaning", "persona_trait_description_1"),
                    ContextField("persona trait 2", "persona_trait_2"),
                    ContextField("persona trait 2 meaning", "persona_trait_description_2"),
                    ContextField("persona milestone", "persona_milestone"),
                    ContextField("source tale", "tale_source_def"),
                    ContextField("source tale name", "tale_source_label"),
                    ContextField("killer tale role", "tale_killer_role"),
                    ContextField("victim tale role", "tale_victim_role"),
                    ContextField("royal mutation pawn", "royal_mutation_pawn"),
                    ContextField("royal cause", "royal_cause"),
                    ContextField("royal transition", "royal_transition"),
                    ContextField("royal faction", "royal_faction"),
                    ContextField("psylink cause", "psylink_cause"),
                    ContextField("new royal duties", "royal_duty_changes"),
                    ContextField("deceased title holder", "succession_deceased"),
                    ContextField("royal heir", "succession_heir"),
                    ContextField("inherited title", "succession_title"),
                    ContextField("succession faction", "succession_faction"),
                    ContextField("permit", "permit_label"),
                    ContextField("permit family", "permit_family"),
                    ContextField("permit faction", "permit_faction"),
                    ContextField("permit title", "permit_title"),
                    ContextField("permit setting", "permit_setting"),
                    ContextField("used during cooldown", "used_during_cooldown"),
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

        // Test the exact shipped XML policy rather than duplicating its tunable numbers in C# fixtures.
        private static PsychotypeTraitAffinityPolicy LoadPsychotypeTraitPolicy()
        {
            XDocument document = XDocument.Load(RepoPath(
                "1.6", "Defs", "DiaryPsychotypeTraitPolicyDefs.xml"));
            XElement def = FindDef(document, "PawnDiary.DiaryPsychotypeTraitPolicyDef",
                "Diary_PsychotypeTraitPolicy");
            PsychotypeTraitAffinityPolicy policy = new PsychotypeTraitAffinityPolicy
            {
                gatedTakeoverChance = float.Parse(ChildValue(def, "gatedTakeoverChance"),
                    System.Globalization.CultureInfo.InvariantCulture)
            };

            XElement rules = def?.Element("rules");
            if (rules == null)
            {
                return policy;
            }

            foreach (XElement item in rules.Elements("li"))
            {
                PsychotypeTraitAffinityRule rule = new PsychotypeTraitAffinityRule
                {
                    traitDefName = ChildValue(item, "traitDefName"),
                    key = ChildValue(item, "key"),
                    matchDegree = string.Equals(ChildValue(item, "matchDegree"), "true",
                        StringComparison.OrdinalIgnoreCase)
                };
                int.TryParse(ChildValue(item, "degree"), out rule.degree);
                LoadTraitBonuses(item.Element("familyBonuses"), rule.familyBonuses);
                LoadTraitBonuses(item.Element("memberBonuses"), rule.memberBonuses);
                policy.rules.Add(rule);
            }

            return policy;
        }

        private static void LoadTraitBonuses(XElement source, Dictionary<string, float> destination)
        {
            if (source == null)
            {
                return;
            }

            foreach (XElement item in source.Elements("li"))
            {
                string target = ChildValue(item, "target");
                if (float.TryParse(ChildValue(item, "bonus"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float bonus))
                {
                    destination[target] = bonus;
                }
            }
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

        private static string ResolveInteractionGroup(
            XDocument document,
            string domain,
            string classifierKey,
            bool anomalyPackageLoaded)
        {
            List<XElement> candidates = new List<XElement>();
            foreach (XElement def in document.Descendants("PawnDiary.DiaryInteractionGroupDef"))
            {
                if (string.Equals(ChildValue(def, "domain"), domain, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(def);
                }
            }

            candidates.Sort((left, right) => InteractionGroupOrder(left).CompareTo(InteractionGroupOrder(right)));
            for (int i = 0; i < candidates.Count; i++)
            {
                XElement candidate = candidates[i];
                if (InteractionGroupAvailable(candidate, anomalyPackageLoaded)
                    && InteractionGroupMatches(candidate, classifierKey))
                {
                    return ChildValue(candidate, "defName");
                }
            }

            return string.Empty;
        }

        private static int InteractionGroupOrder(XElement def)
        {
            int order;
            return int.TryParse(ChildValue(def, "order"), out order) ? order : int.MaxValue;
        }

        private static bool InteractionGroupAvailable(XElement def, bool anomalyPackageLoaded)
        {
            XElement packages = def?.Element("enableWhenPackageIdsLoaded");
            if (packages == null || !HasListValue(def, "enableWhenPackageIdsLoaded", null))
            {
                return true;
            }

            foreach (XElement item in packages.Elements("li"))
            {
                string packageId = (item.Value ?? string.Empty).Trim();
                if (string.Equals(packageId, "Ludeon.RimWorld.Anomaly", StringComparison.OrdinalIgnoreCase))
                {
                    return anomalyPackageLoaded;
                }
            }

            // This focused fixture models the Anomaly gate only. Other package-gated groups are not
            // candidates for the psychic-ritual keys under test and remain available in this tiny model.
            return true;
        }

        private static bool InteractionGroupMatches(XElement def, string classifierKey)
        {
            if (string.Equals(ChildValue(def, "catchAll"), "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            XElement exact = def?.Element("matchDefNames");
            if (exact != null)
            {
                foreach (XElement item in exact.Elements("li"))
                {
                    if (string.Equals((item.Value ?? string.Empty).Trim(), classifierKey,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            XElement tokens = def?.Element("matchTokens");
            if (tokens != null && !string.IsNullOrWhiteSpace(classifierKey))
            {
                foreach (XElement item in tokens.Elements("li"))
                {
                    string token = (item.Value ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(token)
                        && classifierKey.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int CountMatchingEventWindowStarts(
            XDocument document,
            string source,
            string signal,
            string defName)
        {
            int matches = 0;
            foreach (XElement def in document.Descendants("PawnDiary.DiaryEventWindowDef"))
            {
                XElement startSignals = def.Element("startSignals");
                if (startSignals == null)
                {
                    continue;
                }

                bool defMatched = false;
                foreach (XElement trigger in startSignals.Elements("li"))
                {
                    if (EventWindowTriggerMatches(trigger, source, signal, defName))
                    {
                        defMatched = true;
                        break;
                    }
                }

                if (defMatched)
                {
                    matches++;
                }
            }

            return matches;
        }

        private static bool EventWindowTriggerMatches(
            XElement trigger,
            string source,
            string signal,
            string defName)
        {
            string expectedSource = ChildValue(trigger, "source");
            string expectedSignal = ChildValue(trigger, "signal");
            if ((!string.IsNullOrWhiteSpace(expectedSource)
                    && !string.Equals(expectedSource, source, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(expectedSignal)
                    && !string.Equals(expectedSignal, signal, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            bool hasExact = HasListValue(trigger, "matchDefNames", null);
            bool hasTokens = HasListValue(trigger, "matchTokens", null);
            if (!hasExact && !hasTokens)
            {
                return !string.IsNullOrWhiteSpace(expectedSource) || !string.IsNullOrWhiteSpace(expectedSignal);
            }

            if (hasExact && HasListValue(trigger, "matchDefNames", defName))
            {
                return true;
            }

            XElement tokens = trigger.Element("matchTokens");
            if (tokens != null)
            {
                string searchable = (source ?? string.Empty) + ";" + (signal ?? string.Empty) + ";"
                    + (defName ?? string.Empty);
                foreach (XElement item in tokens.Elements("li"))
                {
                    string token = (item.Value ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(token)
                        && searchable.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
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

        private static bool HasOwnership(
            IEnumerable<XElement> rows,
            string sourceDomain,
            string sourceDefName)
        {
            return rows != null && rows.Any(row =>
                string.Equals(ChildValue(row, "sourceDomain"), sourceDomain, StringComparison.Ordinal)
                && string.Equals(ChildValue(row, "sourceDefName"), sourceDefName,
                    StringComparison.Ordinal));
        }

        private static bool HasStructuredListRow(
            XElement def,
            string listName,
            string firstField,
            string firstValue,
            string secondField,
            string secondValue)
        {
            XElement list = def?.Element(listName);
            if (list == null)
            {
                return false;
            }

            foreach (XElement row in list.Elements("li"))
            {
                if (string.Equals(ChildValue(row, firstField), firstValue, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ChildValue(row, secondField), secondValue, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasPromptContextField(XElement template, string contextKey)
        {
            XElement fields = template?.Element("fields");
            if (fields == null)
            {
                return false;
            }

            foreach (XElement field in fields.Elements("li"))
            {
                if (string.Equals(ChildValue(field, "source"), "GameContext", StringComparison.Ordinal)
                    && string.Equals(ChildValue(field, "contextKey"), contextKey, StringComparison.Ordinal))
                {
                    return true;
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

        private static void AssertPlaceholderParity(string name, string english, string russian)
        {
            AssertTrue(name + " has English text", !string.IsNullOrWhiteSpace(english));
            AssertTrue(name + " has Russian text", !string.IsNullOrWhiteSpace(russian));
            AssertEqual(name + " keeps EN/RU placeholder parity",
                PlaceholderSignature(english), PlaceholderSignature(russian));
        }

        private static string PlaceholderSignature(string value)
        {
            string text = value ?? string.Empty;
            List<string> found = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                string token = "{" + i + "}";
                int offset = 0;
                int count = 0;
                while ((offset = text.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
                {
                    count++;
                    offset += token.Length;
                }
                if (count > 0) found.Add(token + "x" + count);
            }
            return string.Join(",", found);
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

        // Two adapters targeting the same external override slot: when both sourceIds resolve to
        // active mods, the later-loading mod wins; any unresolved side falls back to last-writer-wins
        // (the pre-arbitration contract, and how a stale owner from an uninstalled mod is displaced).
        private static void TestExternalOverrideArbitration()
        {
            AssertTrue("later-loading caller displaces earlier-loading owner",
                ExternalOverrideArbitration.MayDisplace(7, 3));
            AssertTrue("earlier-loading caller cannot displace later-loading owner",
                !ExternalOverrideArbitration.MayDisplace(3, 7));
            AssertTrue("same load-order index (same mod) may update its own slot",
                ExternalOverrideArbitration.MayDisplace(5, 5));
            AssertTrue("unresolvable caller keeps last-writer-wins",
                ExternalOverrideArbitration.MayDisplace(ExternalOverrideArbitration.UnknownLoadOrder, 4));
            AssertTrue("unresolvable owner (e.g. uninstalled mod) is displaceable",
                ExternalOverrideArbitration.MayDisplace(4, ExternalOverrideArbitration.UnknownLoadOrder));
            AssertTrue("both unresolvable keeps last-writer-wins",
                ExternalOverrideArbitration.MayDisplace(
                    ExternalOverrideArbitration.UnknownLoadOrder,
                    ExternalOverrideArbitration.UnknownLoadOrder));
            AssertTrue("any negative index counts as unresolved, not just the sentinel",
                ExternalOverrideArbitration.MayDisplace(-2, 0));
            AssertTrue("index 0 (first mod in the list) is a valid resolved position",
                ExternalOverrideArbitration.MayDisplace(0, 0));
        }

        // Player-authored custom writing-style prompts keep their line breaks (so the editor stays
        // readable) but still strip rich-text tags, control chars (except newlines), trailing
        // whitespace per line, and excessive blank runs, with a surrogate-safe length cap.
        private static void TestPlayerWritingStyleText()
        {
            AssertEqual("blank player rule cleans to empty",
                string.Empty,
                PlayerWritingStyleText.CleanRule("   \r\n\t  "));
            AssertEqual("player rule preserves intended line breaks",
                "First line.\nSecond line.\n\nParagraph two.",
                PlayerWritingStyleText.CleanRule("First line.\r\nSecond line.\r\n\r\n\r\nParagraph two."));
            AssertEqual("player rule strips rich-text tags",
                "Write plain. No tags.",
                PlayerWritingStyleText.CleanRule("<b>Write</b> plain. No tags."));
            AssertEqual("player rule removes non-linebreak control chars",
                "No bell here",
                PlayerWritingStyleText.CleanRule("No bell\u0007 here"));
            AssertEqual("player rule trims trailing/leading whitespace per line",
                "Keep left only\ntrailing gone too",
                PlayerWritingStyleText.CleanRule("Keep left only   \n  trailing gone too   "));

            // Surrogate pair at the cap boundary: must not split the emoji, mirroring the external rule.
            string splitSurrogate = new string('a', PlayerWritingStyleText.MaxRuleChars - 1)
                + char.ConvertFromUtf32(0x1F600);
            AssertEqual("player rule cap does not split surrogate pairs",
                new string('a', PlayerWritingStyleText.MaxRuleChars - 1),
                PlayerWritingStyleText.CleanRule(splitSurrogate));
        }

        // The effective writing-style priority is External API override > Hediff override > Pawn
        // custom prompt > Base style. Each layer is absent when its candidate string is blank, and the
        // resolver reports which layer won so the UI can explain an inactive custom prompt.
        private static void TestWritingStyleResolutionPolicy()
        {
            const string baseRule = "base-rule";
            const string customRule = "custom-rule";
            const string hediffRule = "hediff-rule";
            const string externalRule = "external-rule";

            WritingStyleResolution external = WritingStyleResolutionPolicy.Resolve(
                baseRule, customRule, "DiaryPersona_Flu", "flu", hediffRule, "adapter.source", externalRule);
            AssertEqual("external API override wins over hediff/custom/base",
                externalRule, external.rule);
            AssertTrue("external override is reported as the source",
                external.source == WritingStyleRuleSource.ExternalApiOverride);

            WritingStyleResolution hediff = WritingStyleResolutionPolicy.Resolve(
                baseRule, customRule, "DiaryPersona_Flu", "flu", hediffRule, "adapter.source", null);
            AssertEqual("hediff override wins over pawn custom and base",
                hediffRule, hediff.rule);
            AssertTrue("hediff override is reported as the source",
                hediff.source == WritingStyleRuleSource.HediffOverride);

            WritingStyleResolution custom = WritingStyleResolutionPolicy.Resolve(
                baseRule, customRule, null, null, null, null, null);
            AssertEqual("pawn custom prompt wins over base",
                customRule, custom.rule);
            AssertTrue("pawn custom is reported as the source",
                custom.source == WritingStyleRuleSource.PawnCustom);

            WritingStyleResolution blankCustom = WritingStyleResolutionPolicy.Resolve(
                baseRule, "   ", null, null, null, null, null);
            AssertEqual("blank custom prompt falls back to base",
                baseRule, blankCustom.rule);
            AssertTrue("base style is reported when custom is blank",
                blankCustom.source == WritingStyleRuleSource.BaseStyle);

            WritingStyleResolution blankExternal = WritingStyleResolutionPolicy.Resolve(
                baseRule, null, null, null, null, "adapter.source", "   ");
            AssertEqual("blank external override rule is ignored",
                baseRule, blankExternal.rule);
            AssertTrue("blank external falls through to base",
                blankExternal.source == WritingStyleRuleSource.BaseStyle);

            WritingStyleResolution blankHediff = WritingStyleResolutionPolicy.Resolve(
                baseRule, customRule, "  ", "label", "   ", null, null);
            AssertEqual("blank hediff rule falls through to custom",
                customRule, blankHediff.rule);
            AssertTrue("blank hediff falls through to custom source",
                blankHediff.source == WritingStyleRuleSource.PawnCustom);

            // Custom suppression helper: an active override shadows a non-empty custom prompt.
            AssertTrue("custom is reported suppressed by external override",
                WritingStyleResolutionPolicy.CustomSuppressedByOverride(external));
            AssertTrue("custom is reported suppressed by hediff override",
                WritingStyleResolutionPolicy.CustomSuppressedByOverride(hediff));
            AssertTrue("custom is not suppressed when it is the active source",
                !WritingStyleResolutionPolicy.CustomSuppressedByOverride(custom));
            AssertTrue("custom suppression is false with no custom prompt",
                !WritingStyleResolutionPolicy.CustomSuppressedByOverride(blankCustom));
        }

        // ---- Psychotype layer (the second, semantic per-pawn voice) ----

        // The psychotype text sanitizers mirror the writing-style pair: the player-authored custom rule
        // keeps line breaks, and the external override rule/source id collapse to one capped line.
        private static void TestPsychotypeText()
        {
            AssertEqual("psychotype custom rule preserves intended line breaks",
                "First line.\nSecond line.\n\nParagraph two.",
                PsychotypeText.CleanRule("First line.\r\nSecond line.\r\n\r\n\r\nParagraph two."));
            AssertEqual("psychotype custom rule strips rich-text tags",
                "Sees threats everywhere.",
                PsychotypeText.CleanRule("<b>Sees</b> threats everywhere."));
            string splitSurrogate = new string('a', PsychotypeText.MaxCustomRuleChars - 1)
                + char.ConvertFromUtf32(0x1F600);
            AssertEqual("psychotype custom rule cap does not split surrogate pairs",
                new string('a', PsychotypeText.MaxCustomRuleChars - 1),
                PsychotypeText.CleanRule(splitSurrogate));

            AssertEqual("psychotype external rule is one prompt line",
                "Weighs every kindness for motive.",
                PsychotypeText.CleanExternalRule("<b>Weighs</b>\nevery kindness\tfor motive."));
            AssertEqual("psychotype external source id is one line",
                "adapter.source",
                PsychotypeText.CleanSourceId(" adapter.source\r\n"));
            AssertEqual("psychotype external source id is capped",
                new string('s', PsychotypeText.MaxSourceIdChars),
                PsychotypeText.CleanSourceId(new string('s', PsychotypeText.MaxSourceIdChars + 10)));
        }

        // The effective psychotype priority is External API override > Pawn custom rule > Base type.
        // There is no hediff psychotype layer in v1 (hediff *style* overrides already cover altered states).
        private static void TestPsychotypeResolutionPolicy()
        {
            const string baseRule = "base-lens";
            const string customRule = "custom-lens";
            const string externalRule = "external-lens";

            PsychotypeResolution external = PsychotypeResolutionPolicy.Resolve(
                baseRule, customRule, "adapter.source", externalRule);
            AssertEqual("external psychotype override wins", externalRule, external.rule);
            AssertTrue("external psychotype override is the source",
                external.source == PsychotypeRuleSource.ExternalApiOverride);

            PsychotypeResolution custom = PsychotypeResolutionPolicy.Resolve(baseRule, customRule, null, null);
            AssertEqual("pawn custom psychotype wins over base", customRule, custom.rule);
            AssertTrue("pawn custom psychotype is the source",
                custom.source == PsychotypeRuleSource.PawnCustom);

            PsychotypeResolution blankCustom = PsychotypeResolutionPolicy.Resolve(baseRule, "   ", null, null);
            AssertEqual("blank custom psychotype falls back to base", baseRule, blankCustom.rule);
            AssertTrue("base type reported when custom blank",
                blankCustom.source == PsychotypeRuleSource.BaseType);

            PsychotypeResolution blankExternal = PsychotypeResolutionPolicy.Resolve(baseRule, null, "adapter.source", "  ");
            AssertEqual("blank external psychotype override ignored", baseRule, blankExternal.rule);

            AssertTrue("custom is suppressed by external psychotype override",
                PsychotypeResolutionPolicy.CustomSuppressedByOverride(external));
            AssertTrue("custom is not suppressed when it is the active source",
                !PsychotypeResolutionPolicy.CustomSuppressedByOverride(custom));
        }

        // Stage 0: the 12 skill passions fold into five domains plus summary signals (minor = 1 point,
        // burning = 2), and focus is the share of points in the single top domain.
        private static void TestPsychotypeRollProfile()
        {
            PsychotypeProfile profile = PsychotypeRollPolicy.BuildProfile(new List<PsychotypeSkillPassion>
            {
                new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillShooting, level = 2 },
                new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillMelee, level = 1 },
                new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillSocial, level = 1 },
            });
            AssertEqual("violence points sum shooting(2)+melee(1)", 3, profile.violence);
            AssertEqual("people points sum social(1)", 1, profile.people);
            AssertEqual("total points", 4, profile.total);
            AssertEqual("burning count", 1, profile.burningCount);
            AssertEqual("passion count", 3, profile.passionCount);
            AssertEqual("domains with points", 2, profile.domainsWithPoints);
            AssertNear("focus is top-domain share (3/4)", 0.75f, profile.focus);

            PsychotypeProfile empty = PsychotypeRollPolicy.BuildProfile(new List<PsychotypeSkillPassion>());
            AssertEqual("no passions => zero total", 0, empty.total);
            AssertNear("no passions => zero focus", 0f, empty.focus);
        }

        // NormalizeFamily maps blank/unknown input to grounded and preserves each known family
        // case-insensitively, so a hand-edited or custom family never falls outside a roll bucket.
        private static void TestPsychotypeNormalizeFamily()
        {
            AssertEqual("null family => grounded", PsychotypeRollPolicy.FamilyGrounded,
                PsychotypeRollPolicy.NormalizeFamily(null));
            AssertEqual("blank family => grounded", PsychotypeRollPolicy.FamilyGrounded,
                PsychotypeRollPolicy.NormalizeFamily("   "));
            AssertEqual("unknown family => grounded", PsychotypeRollPolicy.FamilyGrounded,
                PsychotypeRollPolicy.NormalizeFamily("sunshine"));
            AssertEqual("grounded preserved", PsychotypeRollPolicy.FamilyGrounded,
                PsychotypeRollPolicy.NormalizeFamily("grounded"));
            AssertEqual("inward preserved", PsychotypeRollPolicy.FamilyInward,
                PsychotypeRollPolicy.NormalizeFamily("inward"));
            AssertEqual("intense preserved", PsychotypeRollPolicy.FamilyIntense,
                PsychotypeRollPolicy.NormalizeFamily("intense"));
            AssertEqual("anxious preserved", PsychotypeRollPolicy.FamilyAnxious,
                PsychotypeRollPolicy.NormalizeFamily("anxious"));
            AssertEqual("mixed case + whitespace normalized", PsychotypeRollPolicy.FamilyIntense,
                PsychotypeRollPolicy.NormalizeFamily("  Intense "));
        }

        // Stage 1: family weights follow the plan's table. Tested deterministically (no jitter/pick here).
        private static void TestPsychotypeFamilyWeights()
        {
            // Zero passions: inward gets the +4 no-passion lean; grounded keeps its base (no no-burning
            // bonus because there are no passions at all).
            Dictionary<string, float> zero = PsychotypeRollPolicy.FamilyWeights(
                PsychotypeRollPolicy.BuildProfile(new List<PsychotypeSkillPassion>()),
                new PsychotypeRollInput());
            AssertNear("zero-passion grounded base", 6f, zero[PsychotypeRollPolicy.FamilyGrounded]);
            AssertNear("zero-passion inward +4", 6f, zero[PsychotypeRollPolicy.FamilyInward]);
            AssertNear("zero-passion intense base", 2f, zero[PsychotypeRollPolicy.FamilyIntense]);

            // Creepjoiner stacks another +4 onto inward.
            Dictionary<string, float> creep = PsychotypeRollPolicy.FamilyWeights(
                PsychotypeRollPolicy.BuildProfile(new List<PsychotypeSkillPassion>()),
                new PsychotypeRollInput { isCreepJoiner = true });
            AssertNear("creepjoiner inward +4 more", 10f, creep[PsychotypeRollPolicy.FamilyInward]);

            // Passions but none burning => grounded +1.
            Dictionary<string, float> settled = PsychotypeRollPolicy.FamilyWeights(
                PsychotypeRollPolicy.BuildProfile(new List<PsychotypeSkillPassion>
                {
                    new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillPlants, level = 1 },
                }),
                new PsychotypeRollInput());
            AssertNear("no-burning grounded +1 over nurture", 8f, settled[PsychotypeRollPolicy.FamilyGrounded]);

            // Making-heavy, one dominant domain, >= 3 points => anxious +2.
            Dictionary<string, float> anxious = PsychotypeRollPolicy.FamilyWeights(
                PsychotypeRollPolicy.BuildProfile(new List<PsychotypeSkillPassion>
                {
                    new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillConstruction, level = 2 },
                    new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillMining, level = 1 },
                }),
                new PsychotypeRollInput());
            AssertNear("anxious base + making(3) + focus(2)", 7f, anxious[PsychotypeRollPolicy.FamilyAnxious]);

            // Three burning passions => intense +2.
            Dictionary<string, float> intense = PsychotypeRollPolicy.FamilyWeights(
                PsychotypeRollPolicy.BuildProfile(new List<PsychotypeSkillPassion>
                {
                    new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillShooting, level = 2 },
                    new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillMelee, level = 2 },
                    new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillSocial, level = 2 },
                }),
                new PsychotypeRollInput());
            AssertNear("intense base + violence(4) + people(2) + burning(2)", 10f, intense[PsychotypeRollPolicy.FamilyIntense]);
        }

        // Stage 2: member weights = base + skill nudges + combo signatures + continuity, times duplicate
        // penalty. Vetoed candidates are dropped. Tested deterministically (no jitter/pick).
        private static void TestPsychotypeMemberWeights()
        {
            List<PsychotypeCandidate> catalog = BuildPsychotypeCatalog();

            // Artistic + Social fires the Theatrical combo (intense family).
            PsychotypeRollInput theatrical = new PsychotypeRollInput
            {
                passions = new List<PsychotypeSkillPassion>
                {
                    new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillArtistic, level = 1 },
                    new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillSocial, level = 1 },
                }
            };
            Dictionary<string, float> intenseWeights = PsychotypeRollPolicy.MemberWeights(
                PsychotypeRollPolicy.FamilyIntense, PsychotypeRollPolicy.BuildProfile(theatrical.passions),
                theatrical, catalog);
            AssertNear("Theatrical = base(1) + Artistic nudge(1) + combo(2)",
                4f, intenseWeights["DiaryPsychotype_Theatrical"]);
            AssertNear("Narcissistic = base(1) + Social nudge(1), no combo",
                2f, intenseWeights["DiaryPsychotype_Narcissistic"]);

            // Kind pawn never rolls Ruthless: it is dropped from the intense family entirely.
            Dictionary<string, float> vetoed = PsychotypeRollPolicy.MemberWeights(
                PsychotypeRollPolicy.FamilyIntense, new PsychotypeProfile(),
                new PsychotypeRollInput { blockRuthless = true }, catalog);
            AssertTrue("blockRuthless removes Ruthless candidate",
                !vetoed.ContainsKey("DiaryPsychotype_Ruthless"));
            AssertTrue("blockRuthless keeps other intense members",
                vetoed.ContainsKey("DiaryPsychotype_Volatile"));

            // Duplicate penalty compounds per existing holder (0.25^2 on a Theatrical weight of 4).
            PsychotypeRollInput dup = new PsychotypeRollInput
            {
                passions = theatrical.passions,
                usedCounts = new Dictionary<string, int> { { "DiaryPsychotype_Theatrical", 2 } }
            };
            Dictionary<string, float> dupWeights = PsychotypeRollPolicy.MemberWeights(
                PsychotypeRollPolicy.FamilyIntense, PsychotypeRollPolicy.BuildProfile(dup.passions), dup, catalog);
            AssertNear("duplicate penalty compounds (4 * 0.25^2)", 0.25f, dupWeights["DiaryPsychotype_Theatrical"]);

            // Child-continuity nudge: a WideEyed child steers the adult roll toward Content/Superstitious.
            PsychotypeRollInput continuity = new PsychotypeRollInput
            {
                childPsychotypeDefName = "DiaryPsychotype_WideEyed"
            };
            Dictionary<string, float> grounded = PsychotypeRollPolicy.MemberWeights(
                PsychotypeRollPolicy.FamilyGrounded, new PsychotypeProfile(), continuity, catalog);
            AssertNear("WideEyed continuity gives Content +1", 2f, grounded["DiaryPsychotype_Content"]);
            AssertNear("non-continuity grounded member stays at base", 1f, grounded["DiaryPsychotype_Pragmatic"]);
        }

        // Roll branches: children roll flat over the child catalog; adults roll an adult defName; vetoes
        // hold across many rolls; empty input yields empty.
        private static void TestPsychotypeRollBranches()
        {
            List<PsychotypeCandidate> catalog = BuildPsychotypeCatalog();
            HashSet<string> childDefs = new HashSet<string>
            {
                "DiaryPsychotype_WideEyed", "DiaryPsychotype_BraveFront", "DiaryPsychotype_ShyWatcher",
                "DiaryPsychotype_WildThing", "DiaryPsychotype_LittleAdult"
            };

            // Children ignore skill signals entirely and always land in the child catalog.
            PsychotypeRollInput childInput = new PsychotypeRollInput
            {
                stageBand = PsychotypeRollPolicy.StageChild,
                passions = new List<PsychotypeSkillPassion>
                {
                    new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillShooting, level = 2 },
                }
            };
            bool allChild = true;
            Func<float> childRand = SeededRand01(7);
            for (int i = 0; i < 200; i++)
            {
                if (!childDefs.Contains(PsychotypeRollPolicy.Roll(childInput, catalog, childRand)))
                {
                    allChild = false;
                    break;
                }
            }
            AssertTrue("child band only rolls child-catalog psychotypes", allChild);

            // Adults roll an adult defName; a Kind veto is honored across many rolls.
            PsychotypeRollInput adultInput = new PsychotypeRollInput { blockRuthless = true };
            Func<float> adultRand = SeededRand01(11);
            bool everRuthless = false;
            bool everChild = false;
            for (int i = 0; i < 500; i++)
            {
                string result = PsychotypeRollPolicy.Roll(adultInput, catalog, adultRand);
                if (result == "DiaryPsychotype_Ruthless") everRuthless = true;
                if (childDefs.Contains(result)) everChild = true;
            }
            AssertTrue("blockRuthless is honored across adult rolls", !everRuthless);
            AssertTrue("adult band never leaks a child-catalog psychotype", !everChild);

            AssertEqual("empty catalog rolls empty", string.Empty,
                PsychotypeRollPolicy.Roll(adultInput, new List<PsychotypeCandidate>(), adultRand));
        }

        // Distribution smoke: 10k seeded rolls over diverse synthetic pawns. Grounded is the broad
        // default, every adult psychotype is reachable, and the wildcard branch fires near its constant.
        private static void TestPsychotypeRollDistribution()
        {
            List<PsychotypeCandidate> catalog = BuildPsychotypeCatalog();
            Dictionary<string, string> familyByDef = new Dictionary<string, string>();
            List<string> adultDefs = new List<string>();
            for (int i = 0; i < catalog.Count; i++)
            {
                PsychotypeCandidate c = catalog[i];
                if (c.stage == PsychotypeRollPolicy.StageAdult)
                {
                    familyByDef[c.defName] = c.family;
                    // Trait-gated psychotypes are unreachable for the trait-less synthetic pawns rolled
                    // here, so they stay out of the >=1% reachability assertion (gating is covered by
                    // TestPsychotypeTraitGating).
                    if (string.IsNullOrEmpty(c.requiredTraitKey))
                    {
                        adultDefs.Add(c.defName);
                    }
                }
            }

            string[] skills =
            {
                PsychotypeRollPolicy.SkillShooting, PsychotypeRollPolicy.SkillMelee,
                PsychotypeRollPolicy.SkillConstruction, PsychotypeRollPolicy.SkillMining,
                PsychotypeRollPolicy.SkillCrafting, PsychotypeRollPolicy.SkillCooking,
                PsychotypeRollPolicy.SkillPlants, PsychotypeRollPolicy.SkillAnimals,
                PsychotypeRollPolicy.SkillMedicine, PsychotypeRollPolicy.SkillIntellectual,
                PsychotypeRollPolicy.SkillArtistic, PsychotypeRollPolicy.SkillSocial
            };

            Random seed = new Random(2026);
            int wildcardCount = 0;
            bool sampleFirst = false;
            // The roll's wildcard threshold now lives on the input's weights (XML-owned); the test input
            // below uses default weights, so read the same default here to count wildcard draws by hand.
            float wildcardChance = new PsychotypeRollWeights().wildcardChance;
            Func<float> rand = () =>
            {
                double d = seed.NextDouble();
                if (sampleFirst)
                {
                    if (d < wildcardChance) wildcardCount++;
                    sampleFirst = false;
                }

                return (float)d;
            };

            Dictionary<string, int> defCounts = new Dictionary<string, int>();
            int grounded = 0;
            const int rolls = 10000;
            for (int i = 0; i < rolls; i++)
            {
                // Typical colonists carry 1-4 passions; bias toward having some so grounded stays dominant.
                int passionCount = 1 + seed.Next(4);
                List<PsychotypeSkillPassion> passions = new List<PsychotypeSkillPassion>();
                for (int p = 0; p < passionCount; p++)
                {
                    passions.Add(new PsychotypeSkillPassion
                    {
                        skillDefName = skills[seed.Next(skills.Length)],
                        level = seed.Next(3) == 0 ? 2 : 1
                    });
                }

                PsychotypeRollInput input = new PsychotypeRollInput { passions = passions };
                sampleFirst = true;
                string result = PsychotypeRollPolicy.Roll(input, catalog, rand);
                if (!defCounts.ContainsKey(result)) defCounts[result] = 0;
                defCounts[result]++;
                if (familyByDef.TryGetValue(result, out string fam) && fam == PsychotypeRollPolicy.FamilyGrounded)
                {
                    grounded++;
                }
            }

            float groundedShare = (float)grounded / rolls;
            AssertTrue("grounded family share lands 45-70% (was " + groundedShare.ToString("0.000") + ")",
                groundedShare >= 0.45f && groundedShare <= 0.70f);

            float wildcardShare = (float)wildcardCount / rolls;
            AssertTrue("wildcard branch fires 10-14% (was " + wildcardShare.ToString("0.000") + ")",
                wildcardShare >= 0.10f && wildcardShare <= 0.14f);

            for (int i = 0; i < adultDefs.Count; i++)
            {
                defCounts.TryGetValue(adultDefs[i], out int count);
                float share = (float)count / rolls;
                AssertTrue("adult psychotype " + adultDefs[i] + " is reachable >=1% (was " + share.ToString("0.000") + ")",
                    share >= 0.01f);
            }
        }

        // Trait canonicalization: simple traits map to their defName, spectrum traits (NaturalMood /
        // Nerves / Neurotic) key each mapped degree separately, everything else contributes nothing.
        private static void TestPsychotypeTraitKeys()
        {
            PsychotypeTraitAffinityPolicy policy = LoadPsychotypeTraitPolicy();
            AssertEqual("simple trait maps to its defName", PsychotypeTraitAffinities.KeyPsychopath,
                PsychotypeTraitAffinities.CanonicalTraitKey("Psychopath", 0, policy));
            AssertEqual("simple trait key is whitespace-tolerant", PsychotypeTraitAffinities.KeyTooSmart,
                PsychotypeTraitAffinities.CanonicalTraitKey(" TooSmart ", 0, policy));
            AssertEqual("NaturalMood -2 => Depressive", PsychotypeTraitAffinities.KeyDepressive,
                PsychotypeTraitAffinities.CanonicalTraitKey("NaturalMood", -2, policy));
            AssertEqual("NaturalMood -1 => Pessimist", PsychotypeTraitAffinities.KeyPessimist,
                PsychotypeTraitAffinities.CanonicalTraitKey("NaturalMood", -1, policy));
            AssertEqual("NaturalMood +1 => Optimist", PsychotypeTraitAffinities.KeyOptimist,
                PsychotypeTraitAffinities.CanonicalTraitKey("NaturalMood", 1, policy));
            AssertEqual("NaturalMood +2 => Sanguine", PsychotypeTraitAffinities.KeySanguine,
                PsychotypeTraitAffinities.CanonicalTraitKey("NaturalMood", 2, policy));
            AssertEqual("NaturalMood degree 0 carries no key", string.Empty,
                PsychotypeTraitAffinities.CanonicalTraitKey("NaturalMood", 0, policy));
            AssertEqual("Nerves -1 => Nervous", PsychotypeTraitAffinities.KeyNervous,
                PsychotypeTraitAffinities.CanonicalTraitKey("Nerves", -1, policy));
            AssertEqual("Nerves -2 => Volatile", PsychotypeTraitAffinities.KeyVolatile,
                PsychotypeTraitAffinities.CanonicalTraitKey("Nerves", -2, policy));
            AssertEqual("iron-willed side of Nerves carries no key", string.Empty,
                PsychotypeTraitAffinities.CanonicalTraitKey("Nerves", 2, policy));
            AssertEqual("Neurotic 1 => Neurotic", PsychotypeTraitAffinities.KeyNeurotic,
                PsychotypeTraitAffinities.CanonicalTraitKey("Neurotic", 1, policy));
            AssertEqual("Neurotic 2 => VeryNeurotic", PsychotypeTraitAffinities.KeyVeryNeurotic,
                PsychotypeTraitAffinities.CanonicalTraitKey("Neurotic", 2, policy));
            AssertEqual("unknown trait carries no key", string.Empty,
                PsychotypeTraitAffinities.CanonicalTraitKey("NightOwl", 0, policy));
            AssertEqual("blank trait carries no key", string.Empty,
                PsychotypeTraitAffinities.CanonicalTraitKey("  ", 0, policy));
        }

        // Trait pull: family bonuses land in stage 1, member bonuses in stage 2, and multiple trait
        // keys stack additively on top of the passion signals.
        private static void TestPsychotypeTraitWeights()
        {
            PsychotypeTraitAffinityPolicy policy = LoadPsychotypeTraitPolicy();
            List<PsychotypeCandidate> catalog = BuildPsychotypeCatalog();
            PsychotypeProfile emptyProfile = PsychotypeRollPolicy.BuildProfile(new List<PsychotypeSkillPassion>());

            // Very neurotic pawn: anxious family = skewed base(2) + StrongBonus(6).
            Dictionary<string, float> veryNeurotic = PsychotypeRollPolicy.FamilyWeights(emptyProfile,
                new PsychotypeRollInput
                {
                    passions = new List<PsychotypeSkillPassion>
                    {
                        new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillSocial, level = 1 },
                    },
                    traitKeys = new List<string> { PsychotypeTraitAffinities.KeyVeryNeurotic },
                    traitPolicy = policy
                });
            AssertNear("VeryNeurotic adds +6 anxious family weight", 8f,
                veryNeurotic[PsychotypeRollPolicy.FamilyAnxious]);

            // Sanguine + Kind stack on the grounded family: base(6) + 6 + 4.
            Dictionary<string, float> sunny = PsychotypeRollPolicy.FamilyWeights(emptyProfile,
                new PsychotypeRollInput
                {
                    traitKeys = new List<string>
                    {
                        PsychotypeTraitAffinities.KeySanguine, PsychotypeTraitAffinities.KeyKind
                    },
                    traitPolicy = policy
                });
            AssertNear("Sanguine + Kind stack on grounded family", 16f,
                sunny[PsychotypeRollPolicy.FamilyGrounded]);

            // Psychopath member weights inside intense: the gated Hollow dominates (base 1 + 6), the
            // compatible spillovers get their bonuses, unrelated members stay at base.
            PsychotypeRollInput psychopath = new PsychotypeRollInput
            {
                traitKeys = new List<string> { PsychotypeTraitAffinities.KeyPsychopath },
                traitPolicy = policy
            };
            Dictionary<string, float> hollow = PsychotypeRollPolicy.MemberWeights(
                PsychotypeRollPolicy.FamilyIntense, emptyProfile, psychopath, catalog);
            AssertNear("Psychopath: Hollow = base(1) + gated bonus(6)", 7f, hollow["DiaryPsychotype_Hollow"]);
            AssertNear("Psychopath: Ruthless spillover = base(1) + 4", 5f, hollow["DiaryPsychotype_Ruthless"]);
            AssertNear("Psychopath: unrelated intense member stays at base", 1f, hollow["DiaryPsychotype_Theatrical"]);

            // Jealous pawn: Resentful (anxious) +6, Narcissistic (intense) +4.
            PsychotypeRollInput jealous = new PsychotypeRollInput
            {
                traitKeys = new List<string> { PsychotypeTraitAffinities.KeyJealous },
                traitPolicy = policy
            };
            Dictionary<string, float> jealousAnxious = PsychotypeRollPolicy.MemberWeights(
                PsychotypeRollPolicy.FamilyAnxious, emptyProfile, jealous, catalog);
            AssertNear("Jealous: Resentful = base(1) + 6", 7f, jealousAnxious["DiaryPsychotype_Resentful"]);
            Dictionary<string, float> jealousIntense = PsychotypeRollPolicy.MemberWeights(
                PsychotypeRollPolicy.FamilyIntense, emptyProfile, jealous, catalog);
            AssertNear("Jealous: Narcissistic = base(1) + 4", 5f, jealousIntense["DiaryPsychotype_Narcissistic"]);

            // Volatile (Nerves -2) steers toward the Volatile psychotype and stacks with a Melee nudge:
            // base(1) + StrongBonus(6) + nudge(1). The trait term alone outweighs any skill nudge.
            PsychotypeRollInput volatileTrait = new PsychotypeRollInput
            {
                passions = new List<PsychotypeSkillPassion>
                {
                    new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillMelee, level = 1 },
                },
                traitKeys = new List<string> { PsychotypeTraitAffinities.KeyVolatile },
                traitPolicy = policy
            };
            Dictionary<string, float> volatileWeights = PsychotypeRollPolicy.MemberWeights(
                PsychotypeRollPolicy.FamilyIntense, PsychotypeRollPolicy.BuildProfile(volatileTrait.passions),
                volatileTrait, catalog);
            AssertNear("Volatile trait + Melee nudge stack on the Volatile psychotype", 8f,
                volatileWeights["DiaryPsychotype_Volatile"]);

            // Dominance pin: the trait channel outweighs a CONTRARY skill profile. A Sanguine pawn
            // with burning violence passions must still roll Content more often than any other
            // psychotype across many seeded rolls.
            PsychotypeRollInput sanguineBrawler = new PsychotypeRollInput
            {
                passions = new List<PsychotypeSkillPassion>
                {
                    new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillShooting, level = 2 },
                    new PsychotypeSkillPassion { skillDefName = PsychotypeRollPolicy.SkillMelee, level = 2 },
                },
                traitKeys = new List<string> { PsychotypeTraitAffinities.KeySanguine },
                traitPolicy = policy
            };
            Func<float> dominanceRand = SeededRand01(73);
            Dictionary<string, int> dominanceCounts = new Dictionary<string, int>();
            const int dominanceRolls = 1000;
            for (int i = 0; i < dominanceRolls; i++)
            {
                string result = PsychotypeRollPolicy.Roll(sanguineBrawler, catalog, dominanceRand);
                if (!dominanceCounts.ContainsKey(result)) dominanceCounts[result] = 0;
                dominanceCounts[result]++;
            }

            dominanceCounts.TryGetValue("DiaryPsychotype_Content", out int contentCount);
            int runnerUp = 0;
            foreach (KeyValuePair<string, int> pair in dominanceCounts)
            {
                if (pair.Key != "DiaryPsychotype_Content" && pair.Value > runnerUp)
                {
                    runnerUp = pair.Value;
                }
            }
            AssertTrue("Sanguine beats a contrary violence profile: Content is the top roll (Content "
                + contentCount + " vs runner-up " + runnerUp + " of " + dominanceRolls + ")",
                contentCount > runnerUp && contentCount >= dominanceRolls * 0.15f);
        }

        // Trait gating: Hollow/Ravenous/Bloodthirsty are unreachable without their trait on every
        // branch (profile, wildcard, flat), and reachable — indeed likely — with it.
        private static void TestPsychotypeTraitGating()
        {
            PsychotypeTraitAffinityPolicy policy = LoadPsychotypeTraitPolicy();
            List<PsychotypeCandidate> catalog = BuildPsychotypeCatalog();
            HashSet<string> gatedDefs = new HashSet<string>
            {
                "DiaryPsychotype_Hollow", "DiaryPsychotype_Ravenous", "DiaryPsychotype_Bloodthirsty"
            };

            // No traits: the gated members never even enter the intense member weighting.
            Dictionary<string, float> traitless = PsychotypeRollPolicy.MemberWeights(
                PsychotypeRollPolicy.FamilyIntense, new PsychotypeProfile(), new PsychotypeRollInput(), catalog);
            AssertTrue("gated psychotypes are absent from trait-less member weights",
                !traitless.ContainsKey("DiaryPsychotype_Hollow")
                && !traitless.ContainsKey("DiaryPsychotype_Ravenous")
                && !traitless.ContainsKey("DiaryPsychotype_Bloodthirsty"));

            // 600 trait-less adult rolls (wildcard branch included): a gated psychotype never wins.
            Func<float> traitlessRand = SeededRand01(23);
            bool everGated = false;
            for (int i = 0; i < 600; i++)
            {
                if (gatedDefs.Contains(PsychotypeRollPolicy.Roll(new PsychotypeRollInput(), catalog, traitlessRand)))
                {
                    everGated = true;
                    break;
                }
            }
            AssertTrue("gated psychotypes never roll without their trait", !everGated);

            // A Bloodlust pawn lands on Bloodthirsty dominantly (takeover branch + gate open + intense
            // family pull + member bonus), and never on the OTHER two gated psychotypes.
            PsychotypeRollInput bloodlust = new PsychotypeRollInput
            {
                traitKeys = new List<string> { PsychotypeTraitAffinities.KeyBloodlust },
                traitPolicy = policy
            };
            Func<float> bloodlustRand = SeededRand01(41);
            int bloodthirstyCount = 0;
            bool everForeignGated = false;
            const int bloodlustRolls = 600;
            for (int i = 0; i < bloodlustRolls; i++)
            {
                string result = PsychotypeRollPolicy.Roll(bloodlust, catalog, bloodlustRand);
                if (result == "DiaryPsychotype_Bloodthirsty") bloodthirstyCount++;
                else if (gatedDefs.Contains(result)) everForeignGated = true;
            }
            AssertTrue("Bloodlust never unlocks the other gated psychotypes", !everForeignGated);
            float bloodthirstyShare = (float)bloodthirstyCount / bloodlustRolls;
            AssertTrue("Bloodlust rolls Bloodthirsty a dominant-but-not-total share (was "
                + bloodthirstyShare.ToString("0.000") + ")",
                bloodthirstyShare >= 0.30f && bloodthirstyShare <= 0.80f);

            // The gate holds on the child branch too: a psychopathic child still rolls the child catalog.
            PsychotypeRollInput psychoChild = new PsychotypeRollInput
            {
                stageBand = PsychotypeRollPolicy.StageChild,
                traitKeys = new List<string> { PsychotypeTraitAffinities.KeyPsychopath }
            };
            Func<float> childRand = SeededRand01(59);
            bool childEverGated = false;
            for (int i = 0; i < 200; i++)
            {
                if (gatedDefs.Contains(PsychotypeRollPolicy.Roll(psychoChild, catalog, childRand)))
                {
                    childEverGated = true;
                    break;
                }
            }
            AssertTrue("child band never rolls an adult trait-gated psychotype", !childEverGated);
        }

        // A seeded [0,1) source so the roll's randomness is reproducible in tests.
        private static Func<float> SeededRand01(int seed)
        {
            Random random = new Random(seed);
            return () => (float)random.NextDouble();
        }

        // The synthetic catalog mirroring the shipped defs: 17 ordinary adult psychotypes across four
        // families, 3 trait-gated adult psychotypes (Hollow/Ravenous/Bloodthirsty), and 5 child options,
        // with the same skill affinities and trait gates the shipped XML declares. Kept in the test so
        // the pure roll can be exercised without loading XML.
        private static List<PsychotypeCandidate> BuildPsychotypeCatalog()
        {
            List<PsychotypeCandidate> catalog = new List<PsychotypeCandidate>();

            void Adult(string defName, string family, params string[] affinitySkills)
            {
                Dictionary<string, int> affinities = new Dictionary<string, int>();
                for (int i = 0; i < affinitySkills.Length; i++)
                {
                    affinities[affinitySkills[i]] = 1;
                }

                catalog.Add(new PsychotypeCandidate
                {
                    defName = defName,
                    family = family,
                    stage = PsychotypeRollPolicy.StageAdult,
                    skillAffinities = affinities
                });
            }

            Adult("DiaryPsychotype_Content", PsychotypeRollPolicy.FamilyGrounded, PsychotypeRollPolicy.SkillCooking);
            Adult("DiaryPsychotype_Ambitious", PsychotypeRollPolicy.FamilyGrounded);
            Adult("DiaryPsychotype_Dutiful", PsychotypeRollPolicy.FamilyGrounded, PsychotypeRollPolicy.SkillConstruction);
            Adult("DiaryPsychotype_Nostalgic", PsychotypeRollPolicy.FamilyGrounded, PsychotypeRollPolicy.SkillPlants);
            Adult("DiaryPsychotype_Pragmatic", PsychotypeRollPolicy.FamilyGrounded, PsychotypeRollPolicy.SkillMining);
            Adult("DiaryPsychotype_Wry", PsychotypeRollPolicy.FamilyGrounded);

            Adult("DiaryPsychotype_Paranoid", PsychotypeRollPolicy.FamilyInward, PsychotypeRollPolicy.SkillShooting);
            Adult("DiaryPsychotype_Detached", PsychotypeRollPolicy.FamilyInward, PsychotypeRollPolicy.SkillIntellectual);
            Adult("DiaryPsychotype_Superstitious", PsychotypeRollPolicy.FamilyInward, PsychotypeRollPolicy.SkillArtistic);

            Adult("DiaryPsychotype_Ruthless", PsychotypeRollPolicy.FamilyIntense);
            Adult("DiaryPsychotype_Volatile", PsychotypeRollPolicy.FamilyIntense, PsychotypeRollPolicy.SkillMelee);
            Adult("DiaryPsychotype_Theatrical", PsychotypeRollPolicy.FamilyIntense, PsychotypeRollPolicy.SkillArtistic);
            Adult("DiaryPsychotype_Narcissistic", PsychotypeRollPolicy.FamilyIntense, PsychotypeRollPolicy.SkillSocial);

            Adult("DiaryPsychotype_Resentful", PsychotypeRollPolicy.FamilyAnxious);
            Adult("DiaryPsychotype_Avoidant", PsychotypeRollPolicy.FamilyAnxious, PsychotypeRollPolicy.SkillAnimals);
            Adult("DiaryPsychotype_Dependent", PsychotypeRollPolicy.FamilyAnxious, PsychotypeRollPolicy.SkillMedicine);
            Adult("DiaryPsychotype_Perfectionist", PsychotypeRollPolicy.FamilyAnxious, PsychotypeRollPolicy.SkillCrafting);

            void Gated(string defName, string requiredTraitKey)
            {
                catalog.Add(new PsychotypeCandidate
                {
                    defName = defName,
                    family = PsychotypeRollPolicy.FamilyIntense,
                    stage = PsychotypeRollPolicy.StageAdult,
                    skillAffinities = new Dictionary<string, int>(),
                    requiredTraitKey = requiredTraitKey
                });
            }

            Gated("DiaryPsychotype_Hollow", PsychotypeTraitAffinities.KeyPsychopath);
            Gated("DiaryPsychotype_Ravenous", PsychotypeTraitAffinities.KeyCannibal);
            Gated("DiaryPsychotype_Bloodthirsty", PsychotypeTraitAffinities.KeyBloodlust);

            string[] children =
            {
                "DiaryPsychotype_WideEyed", "DiaryPsychotype_BraveFront", "DiaryPsychotype_ShyWatcher",
                "DiaryPsychotype_WildThing", "DiaryPsychotype_LittleAdult"
            };
            for (int i = 0; i < children.Length; i++)
            {
                catalog.Add(new PsychotypeCandidate
                {
                    defName = children[i],
                    family = PsychotypeRollPolicy.StageChild,
                    stage = PsychotypeRollPolicy.StageChild,
                    skillAffinities = new Dictionary<string, int>()
                });
            }

            return catalog;
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

        // The psychotype (outlook) block travels the SAME seam as the writing style: it is merged into
        // the combined voice block upstream (adapters) and reaches the SYSTEM prompt only, in the order
        // psychotype -> style, and is dropped whole for neutral (includePersona=false) templates. The
        // adapter's Translate() calls are main-thread-only, so this test pins the load-bearing planner
        // seam with a pre-composed voice block rather than re-running the adapter.
        private static void TestPsychotypeVoiceReachesSystemPrompt()
        {
            const string psychotypeBlock = "How this pawn tends to see things: assumes nothing is harmless.";
            const string styleBlock = "How this pawn tends to write: spare-iceberg: short concrete sentences.";
            // The adapter's CombinedVoiceBlock joins outlook first, then style, with a blank line between.
            string voiceBlock = psychotypeBlock + "\n\n" + styleBlock;
            const string baseSystem = "Write 1-3 first-person diary sentences.";

            DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = SoloPayload("e-psy", "quiet work", "Alice repaired the generator alone."),
                policy = Policy(combat: false, important: true),
                povRole = DiaryPipelineRoles.Initiator,
                personaVoiceBlock = voiceBlock,
                maxTokens = 30
            });
            AssertContains("planner folds the psychotype lens into the system prompt",
                plan.systemPrompt, psychotypeBlock);
            AssertContains("planner keeps the writing style in the system prompt too",
                plan.systemPrompt, styleBlock);
            AssertTrue("psychotype lens precedes the writing style in the system prompt",
                plan.systemPrompt.IndexOf(psychotypeBlock, StringComparison.Ordinal)
                    < plan.systemPrompt.IndexOf(styleBlock, StringComparison.Ordinal));
            AssertTrue("psychotype never appears as a user-prompt field",
                !plan.userPrompt.Contains("How this pawn tends to see things"));

            // Neutral chronicle/title shapes opt out via includePersona=false: the whole combined block,
            // psychotype included, is dropped.
            AssertEqual("includePersona=false drops the psychotype block too",
                baseSystem,
                PromptAssembler.ComposeSystem(baseSystem, voiceBlock, includePersona: false));

            // An empty psychotype (Neutral / disabled) contributes no text: the combined block is just
            // the style, and the outlook wrapper phrase never appears.
            AssertTrue("empty psychotype leaves no outlook wrapper in the style-only block",
                !styleBlock.Contains("How this pawn tends to see things"));
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

        // ---- Error reporter (ErrorScrub / ErrorFingerprint / ErrorReportPayload) ----

        private static void TestErrorScrubRemovesPersonalData()
        {
            // Windows username segment is masked, rest of the path is kept for grouping.
            string win = ErrorScrub.Scrub(
                @"at Foo.Bar () in C:\Users\Alice\AppData\Mods\PawnDiary\Thing.cs:line 42",
                null,
                ErrorScrub.DefaultMaxChars);
            AssertContains("scrub keeps path shape", win, @"C:\Users\~\");
            AssertTrue("scrub drops windows username", win.IndexOf("Alice", StringComparison.Ordinal) < 0);

            // Unix home segment is masked too.
            string nix = ErrorScrub.Scrub("stack /home/bob/rimworld/mod.cs", null, ErrorScrub.DefaultMaxChars);
            AssertContains("scrub keeps unix home shape", nix, "/home/~/");
            AssertTrue("scrub drops unix username", nix.IndexOf("bob", StringComparison.Ordinal) < 0);

            // Bearer tokens and bare sk- keys are masked by shape (no secrets list needed).
            string bearer = ErrorScrub.Scrub("Authorization: Bearer sk-livesecrettoken0099", null, ErrorScrub.DefaultMaxChars);
            AssertTrue("scrub masks bearer token", bearer.IndexOf("livesecrettoken", StringComparison.Ordinal) < 0);
            string sk = ErrorScrub.Scrub("using key sk-ABCDEF1234567890 now", null, ErrorScrub.DefaultMaxChars);
            AssertContains("scrub masks sk key", sk, "sk-<redacted>");
            AssertTrue("scrub drops sk key tail", sk.IndexOf("ABCDEF1234567890", StringComparison.Ordinal) < 0);

            // Exact configured secrets (an API key and an endpoint URL) are redacted verbatim.
            List<string> secrets = new List<string> { "myLocalApiKey55", "https://api.example.com/v1" };
            string configured = ErrorScrub.Scrub(
                "POST https://api.example.com/v1 failed with key myLocalApiKey55",
                secrets,
                ErrorScrub.DefaultMaxChars);
            AssertTrue("scrub redacts configured api key", configured.IndexOf("myLocalApiKey55", StringComparison.Ordinal) < 0);
            AssertTrue("scrub redacts configured endpoint", configured.IndexOf("api.example.com", StringComparison.Ordinal) < 0);

            // Length cap applies (and appends an ellipsis marker).
            string big = new string('x', 5000);
            string capped = ErrorScrub.Scrub(big, null, 100);
            AssertTrue("scrub caps length", capped.Length <= 103);
            AssertContains("scrub marks truncation", capped, "...");

            AssertEqual("scrub null is empty", string.Empty, ErrorScrub.Scrub(null, null, ErrorScrub.DefaultMaxChars));
        }

        private static void TestErrorFingerprintGroupsAndDistinguishes()
        {
            // Same call path, different line numbers -> same fingerprint (numbers are normalized away).
            string a = ErrorFingerprint.Compute("NullRef in Diary.Foo\n at Diary.Foo:line 42\n at Bar:line 9");
            string b = ErrorFingerprint.Compute("NullRef in Diary.Foo\n at Diary.Foo:line 88\n at Bar:line 123");
            AssertEqual("fingerprint groups by normalized stack", a, b);

            // Genuinely different errors -> different fingerprints.
            string c = ErrorFingerprint.Compute("IndexOutOfRange in Diary.Baz\n at Diary.Baz:line 5");
            AssertTrue("fingerprint distinguishes different errors", !string.Equals(a, c, StringComparison.Ordinal));

            // Deterministic and fixed width (16 hex chars).
            AssertEqual("fingerprint is deterministic", a, ErrorFingerprint.Compute("NullRef in Diary.Foo\n at Diary.Foo:line 42\n at Bar:line 9"));
            AssertEqual("fingerprint width", 16, a.Length);
        }

        private static void TestErrorReportPayloadIsValidPiiFreeJson()
        {
            ErrorReport report = new ErrorReport
            {
                schemaVersion = ErrorReportPayload.SchemaVersion,
                modVersion = "1.2.3.4",
                rimworldVersion = "1.6.4241",
                os = "Microsoft Windows NT 10.0",
                installSource = "workshop",
                installId = "abc123def456",
                fingerprint = "00ff00ff00ff00ff",
                timestampUtc = "2026-07-07T12:00:00.0000000Z",
                activeDlc = new List<string> { "Royalty", "Anomaly" },
                message = "Line \"one\"\nLine two\tindented"
            };

            string json = ErrorReportPayload.ToJson(report);

            // Round-trips through the real MiniJson parser -> proves it is well-formed JSON.
            object parsedObj = MiniJson.Deserialize(json);
            Dictionary<string, object> parsed = parsedObj as Dictionary<string, object>;
            AssertTrue("payload is a json object", parsed != null);

            AssertEqual("payload schemaVersion", ErrorReportPayload.SchemaVersion, (int)(double)parsed["schemaVersion"]);
            AssertEqual("payload modVersion", "1.2.3.4", (string)parsed["modVersion"]);
            AssertEqual("payload installId", "abc123def456", (string)parsed["installId"]);
            AssertEqual("payload installSource", "workshop", (string)parsed["installSource"]);
            AssertEqual("payload fingerprint", "00ff00ff00ff00ff", (string)parsed["fingerprint"]);
            // Message escaping survives the round trip exactly (quotes, newline, tab).
            AssertEqual("payload message round-trips", "Line \"one\"\nLine two\tindented", (string)parsed["message"]);

            object[] dlc = parsed["activeDlc"] as object[];
            AssertTrue("payload dlc array present", dlc != null && dlc.Length == 2);
            AssertEqual("payload dlc first", "Royalty", (string)dlc[0]);

            // Privacy contract: the wire object exposes no personal-data fields.
            AssertTrue("payload has no username field", !parsed.ContainsKey("username") && !parsed.ContainsKey("userName"));
            AssertTrue("payload has no machine field", !parsed.ContainsKey("machineName") && !parsed.ContainsKey("hostname"));
            AssertTrue("payload has no path field", !parsed.ContainsKey("path") && !parsed.ContainsKey("filePath"));
            AssertTrue("payload has no apiKey field", !parsed.ContainsKey("apiKey"));
        }

        private static void TestInstallSourceClassifier()
        {
            AssertEqual("workshop windows path", InstallSource.Workshop,
                InstallSource.FromRootDir(@"D:\SteamLibrary\steamapps\workshop\content\294100\2891407721\PawnDiary"));
            AssertEqual("workshop unix path", InstallSource.Workshop,
                InstallSource.FromRootDir("/home/u/.steam/steam/steamapps/workshop/content/294100/2891407721"));
            AssertEqual("local mods path", InstallSource.Local,
                InstallSource.FromRootDir(@"D:\SteamLibrary\steamapps\common\RimWorld\Mods\aimmlegate.pawndiary"));
            // A different game's Workshop folder (not app 294100) must not read as ours.
            AssertEqual("other-game workshop is local", InstallSource.Local,
                InstallSource.FromRootDir(@"D:\Steam\steamapps\workshop\content\999999\123\Mod"));
            AssertEqual("blank is unknown", InstallSource.Unknown, InstallSource.FromRootDir(""));
            AssertEqual("null is unknown", InstallSource.Unknown, InstallSource.FromRootDir(null));
        }

        // The error reporter only forwards messages this policy calls "ours". This locks in that the
        // main mod AND its first-party integration submods are covered, while the copy-me example
        // adapter template, other mods, and base-game lines are not. Add a row here with each new submod.
        private static void TestModErrorPrefixPolicyCoversSubmods()
        {
            // The main mod's own lines are ours.
            AssertTrue("main mod prefix is ours",
                ModErrorPrefixPolicy.IsModErrorMessage("[Pawn Diary] something broke: System.NullReferenceException"));

            // First-party integration submods are ours too — the point of this change. The spaced
            // "[Pawn Diary: X]" root covers every such bridge (Personalities, VSIE, ...) with no
            // per-submod entry; the RimTalk bridge uses the no-space root.
            AssertTrue("1-2-3 Personalities bridge prefix is ours",
                ModErrorPrefixPolicy.IsModErrorMessage("[Pawn Diary: 1-2-3 Personalities] failed to register the psychotype generator"));
            AssertTrue("VSIE bridge prefix is ours",
                ModErrorPrefixPolicy.IsModErrorMessage("[Pawn Diary: VSIE] failed to install the gathering hook"));
            AssertTrue("RimTalk bridge prefix is ours",
                ModErrorPrefixPolicy.IsModErrorMessage("[PawnDiary: RimTalk bridge] failed to install Harmony patches"));

            // The example adapter is a copy-me template for third parties, so its lines are NOT ours.
            AssertTrue("example adapter template is not ours",
                !ModErrorPrefixPolicy.IsModErrorMessage("[Pawn Diary Example Adapter] PreviewPrompt returned null"));

            // Other mods and unprefixed base-game lines are never ours.
            AssertTrue("other mod prefix is not ours",
                !ModErrorPrefixPolicy.IsModErrorMessage("[Some Other Mod] null reference in OnGUI"));
            AssertTrue("unprefixed base-game line is not ours",
                !ModErrorPrefixPolicy.IsModErrorMessage("Root level exception in OnGUI(): System.Exception"));

            // Guard inputs.
            AssertTrue("null is not ours", !ModErrorPrefixPolicy.IsModErrorMessage(null));
            AssertTrue("empty is not ours", !ModErrorPrefixPolicy.IsModErrorMessage(string.Empty));
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

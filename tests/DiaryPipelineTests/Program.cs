using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Xml.Linq;
using PawnDiary;

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
            TestQuestPromptPlanFields();
            TestDualPovPromptPlans();
            TestRecipientFollowupPlan();
            TestNeutralGenerationPlans();
            TestTitlePromptPlan();
            TestSoloSelection();
            TestSoloBatchSelection();
            TestPromptEnchantmentPlanner();
            TestHediffPersonaOverridePolicy();
            TestMemoryDecayXmlPolicy();
            TestPromptTextSanitizer();
            TestEventWindowPolicy();
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
            TestLlmRequestJsonBuilder();
            TestArchivedPendingReloadFallbackStatus();
            TestArchiveEligibility();
            TestArchiveFallbackFact();
            TestArchiveOverflowSelection();

            Console.WriteLine("DiaryPipelineTests passed " + assertions + " assertions.");
            return 0;
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
            AssertContains("arrival facts", arrival.userPrompt, "arrival facts: source=crash pod; scenario=Crashlanded; surroundings=rainy field");
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
            AssertEqual("hediff persona override matched source unique count", 1, selectedOverride.matchedHediffDefNames.Count);
            AssertEqual("hediff persona override matched source", "Inhumanized", selectedOverride.matchedHediffDefNames[0]);
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
                    lastOpener = "The workshop was quiet."
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
                gameContext = "arrival_pawn=Alice; arrival_source=crash pod; scenario_name=Crashlanded; arrival_surroundings=rainy field",
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
                    Field("weapon", "Weapon"),
                    Field("important context", "PromptEnchantment"),
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
                appendDirectSpeechInstruction = key != DiaryPipelineTemplates.Title,
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

        private static string ChildValue(XElement element, string childName)
        {
            if (element == null)
            {
                return string.Empty;
            }

            XElement child = element.Element(childName);
            return child == null ? string.Empty : (child.Value ?? string.Empty).Trim();
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

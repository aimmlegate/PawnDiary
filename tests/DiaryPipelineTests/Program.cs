using System;
using System.Collections.Generic;
using System.Net.Http;
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
            TestDualPovPromptPlans();
            TestRecipientFollowupPlan();
            TestNeutralGenerationPlans();
            TestTitlePromptPlan();
            TestSoloSelection();
            TestSoloBatchSelection();
            TestDomainClassifier();
            TestResponsePostprocessorRules();
            TestDirectSpeechParser();
            TestGeneratedTextKeepsTrailingSpeech();
            TestPromptCaptureFormatting();
            TestApiLaneSelector();
            TestApiEndpointPolicy();
            TestApiRequestAuth();

            Console.WriteLine("DiaryPipelineTests passed " + assertions + " assertions.");
            return 0;
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
            DiaryPromptPlan plan = DiaryPromptPlanner.Build(new DiaryPromptRequest
            {
                payload = SoloPayload("e-solo", "quiet work", "Alice repaired the generator alone."),
                policy = Policy(combat: false, important: true),
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
            AssertContains("solo health", plan.userPrompt, "important health: Her hands shake.");
            AssertEqual("solo rule role", DiaryPipelineRoles.Initiator, plan.responseRules.targetRole);
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
        }

        private static void TestApiEndpointPolicy()
        {
            AssertEqual("normalize bearer auth", ApiAuthMode.BearerToken,
                ApiEndpointPolicy.NormalizeAuthMode(ApiAuthMode.BearerToken));
            AssertEqual("normalize query auth", ApiAuthMode.QueryParameterKey,
                ApiEndpointPolicy.NormalizeAuthMode(ApiAuthMode.QueryParameterKey));
            AssertEqual("normalize invalid auth", ApiAuthMode.BearerToken,
                ApiEndpointPolicy.NormalizeAuthMode((ApiAuthMode)999));

            AssertEqual("cooldown zero is first failure", 10, ApiEndpointPolicy.CooldownSecondsForFailures(0));
            AssertEqual("cooldown first failure", 10, ApiEndpointPolicy.CooldownSecondsForFailures(1));
            AssertEqual("cooldown second failure", 20, ApiEndpointPolicy.CooldownSecondsForFailures(2));
            AssertEqual("cooldown third failure", 40, ApiEndpointPolicy.CooldownSecondsForFailures(3));
            AssertEqual("cooldown fifth failure", 160, ApiEndpointPolicy.CooldownSecondsForFailures(5));
            AssertEqual("cooldown caps", 300, ApiEndpointPolicy.CooldownSecondsForFailures(6));
            AssertEqual("cooldown remains capped", 300, ApiEndpointPolicy.CooldownSecondsForFailures(20));
        }

        private static void TestApiRequestAuth()
        {
            AssertEqual("query auth appends key",
                "https://example.test/v1/chat/completions?existing=1&key=a%20b",
                ApiRequestAuth.ApplyQueryAuth(
                    "https://example.test/v1/chat/completions?existing=1",
                    "a b",
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

            using (HttpRequestMessage apiKey = new HttpRequestMessage(HttpMethod.Post, "https://example.test"))
            {
                ApiRequestAuth.ApplyHeaders(apiKey, "secret", ApiAuthMode.ApiKeyHeader);
                AssertHeader("api-key header", apiKey, "api-key", "secret");
                AssertTrue("api-key has no bearer", apiKey.Headers.Authorization == null);
            }

            using (HttpRequestMessage xApiKey = new HttpRequestMessage(HttpMethod.Post, "https://example.test"))
            {
                ApiRequestAuth.ApplyHeaders(xApiKey, "secret", ApiAuthMode.XApiKeyHeader);
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
            AssertTrue("plain interaction stays interaction prompt",
                !DiaryEventDomainClassifier.HasNonInteractionSourceMarker("def=Chat; label=chat"));
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
                    Field("weapon", "Weapon"),
                    Field("important health", "PromptEnchantment"),
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

        private static List<bool> Ready(params bool[] values)
        {
            return new List<bool>(values);
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

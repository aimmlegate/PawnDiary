using System;
using System.Collections.Generic;
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
            TestResponsePostprocessorRules();

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
            AssertTrue("combat allows direct blocks", plan.responseRules.allowDirectSpeechBlocks);
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
            AssertContains("solo health", plan.userPrompt, "important health: Her hands shake.");
            AssertEqual("solo rule role", DiaryPipelineRoles.Initiator, plan.responseRules.targetRole);
            AssertTrue("solo allows direct speech blocks", plan.responseRules.allowDirectSpeechBlocks);
            AssertEqual("solo staggered rule", 3, plan.responseRules.staggeredIntensity);
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
            AssertTrue("dual initiator speech allowed", initiator.responseRules.allowDirectSpeechBlocks);

            AssertEqual("dual recipient template", DiaryPipelineTemplates.PairImportant, recipient.templateKey);
            AssertContains("dual recipient pov", recipient.userPrompt, "pov: Bob");
            AssertContains("dual recipient text", recipient.userPrompt, "what happened: Bob denied it.");
            AssertContains("dual recipient context", recipient.userPrompt, "initiator entry: I knew Bob had taken it.");
            AssertEqual("dual recipient rules", DiaryPipelineRoles.Recipient, recipient.responseRules.targetRole);
            AssertTrue("dual recipient plain prose", recipient.responseRules.recipientPlainProseOnly);
            AssertTrue("dual recipient speech blocked", !recipient.responseRules.allowDirectSpeechBlocks);
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
            AssertTrue("recipient plain prose", plan.responseRules.recipientPlainProseOnly);
            AssertTrue("recipient disallows direct blocks", !plan.responseRules.allowDirectSpeechBlocks);
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
                    weapon = "knife",
                    staggeredIntensity = 2
                },
                recipient = new DiaryPovPayload
                {
                    role = DiaryPipelineRoles.Recipient,
                    name = "Bob",
                    rawText = recipientText,
                    pawnSummary = "Bob is careful.",
                    surroundings = "cold hallway",
                    continuity = "rivals",
                    weapon = "club",
                    staggeredIntensity = 1
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
                    staggeredIntensity = 3
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
                    tone = "tense"
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

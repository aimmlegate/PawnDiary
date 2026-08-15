// Standalone exhaustive tests for the pure provider protocol foundation.
// No HTTP server, RimWorld assembly, live credential, or paid provider call is used here.
using System;
using System.Collections.Generic;
using System.Text;

namespace PawnDiary
{
    internal static class Program
    {
        private static int assertions;

        private static void Main()
        {
            TestModeContract();
            TestUrls();
            TestHeadersAndAuthRecommendations();
            TestOpenAIRequestBaselines();
            TestNativeRequests();
            TestOpenAIResponses();
            TestAnthropicResponses();
            TestGeminiResponses();
            TestOllamaResponses();
            TestRuntimeFailureDetail();
            TestMalformedAndDisposition();
            TestOpenAIAndAnthropicModels();
            TestGeminiModels();
            TestOllamaModelsAndLimits();

            Console.WriteLine("Llm protocol tests passed (" + assertions + " assertions).");
        }

        private static void TestModeContract()
        {
            AssertEqual("chat ordinal", 0, (int)LlmProtocolMode.OpenAIChatCompletions);
            AssertEqual("responses ordinal", 1, (int)LlmProtocolMode.OpenAIResponses);
            AssertEqual("reserved ordinal", 2, (int)LlmProtocolMode.ReservedLegacyNativeOllama);
            AssertEqual("anthropic ordinal", 3, (int)LlmProtocolMode.AnthropicMessages);
            AssertEqual("gemini ordinal", 4, (int)LlmProtocolMode.GeminiGenerateContent);
            AssertEqual("ollama ordinal", 5, (int)LlmProtocolMode.OllamaChat);
            AssertEqual("reserved normalizes chat", LlmProtocolMode.OpenAIChatCompletions,
                LlmProtocolDispatcher.NormalizeMode(2));
            AssertEqual("future normalizes chat", LlmProtocolMode.OpenAIChatCompletions,
                LlmProtocolDispatcher.NormalizeMode(999));
            AssertEqual("anthropic survives normalization", LlmProtocolMode.AnthropicMessages,
                LlmProtocolDispatcher.NormalizeMode(3));

            AssertEqual("chat token", "chatCompletions",
                LlmProtocolDispatcher.StableToken(LlmProtocolMode.OpenAIChatCompletions));
            AssertEqual("responses token", "responses",
                LlmProtocolDispatcher.StableToken(LlmProtocolMode.OpenAIResponses));
            AssertEqual("anthropic token", "anthropicMessages",
                LlmProtocolDispatcher.StableToken(LlmProtocolMode.AnthropicMessages));
            AssertEqual("gemini token", "geminiGenerateContent",
                LlmProtocolDispatcher.StableToken(LlmProtocolMode.GeminiGenerateContent));
            AssertEqual("ollama token", "ollamaChat",
                LlmProtocolDispatcher.StableToken(LlmProtocolMode.OllamaChat));
            AssertEqual("token parse case insensitive", LlmProtocolMode.GeminiGenerateContent,
                LlmProtocolDispatcher.FromStableToken(" GEMINIGENERATECONTENT "));
            AssertEqual("obsolete token never native", LlmProtocolMode.OpenAIChatCompletions,
                LlmProtocolDispatcher.FromStableToken("ollamaNativeChat"));
            AssertEqual("unknown token safe", LlmProtocolMode.OpenAIChatCompletions,
                LlmProtocolDispatcher.FromStableToken("future"));
            AssertEqual("gemini canonical prefix/trim", "gemini-x",
                LlmProtocolDispatcher.CanonicalModelName(
                    "  models/gemini-x  ", LlmProtocolMode.GeminiGenerateContent));
            AssertEqual("openai canonical preserves raw", "  model/raw  ",
                LlmProtocolDispatcher.CanonicalModelName(
                    "  model/raw  ", LlmProtocolMode.OpenAIChatCompletions));
        }

        private static void TestUrls()
        {
            string openAi = "https://gateway.example/v1?tenant=alpha#settings";
            AssertEqual("openai chat URL unchanged",
                EndpointUtility.BuildGenerationUrl(openAi, ApiCompatibilityMode.OpenAIChatCompletions),
                LlmProtocolDispatcher.BuildGenerationUrl(openAi, "gpt", LlmProtocolMode.OpenAIChatCompletions));
            AssertEqual("openai responses URL unchanged",
                EndpointUtility.BuildGenerationUrl(openAi, ApiCompatibilityMode.OpenAIResponses),
                LlmProtocolDispatcher.BuildGenerationUrl(openAi, "gpt", LlmProtocolMode.OpenAIResponses));
            AssertEqual("openai models URL unchanged",
                EndpointUtility.BuildModelsUrl(openAi, ApiCompatibilityMode.OpenAIResponses),
                LlmProtocolDispatcher.BuildModelsUrl(openAi, LlmProtocolMode.OpenAIResponses));

            AssertEqual("anthropic default generation", "https://api.anthropic.com/v1/messages",
                LlmProtocolDispatcher.BuildGenerationUrl(null, "claude", LlmProtocolMode.AnthropicMessages));
            AssertEqual("anthropic default models", "https://api.anthropic.com/v1/models",
                LlmProtocolDispatcher.BuildModelsUrl(" ", LlmProtocolMode.AnthropicMessages));
            AssertEqual("anthropic full generation idempotent", "https://api.anthropic.com/v1/messages",
                LlmProtocolDispatcher.BuildGenerationUrl(
                    "https://api.anthropic.com/v1/messages", "claude", LlmProtocolMode.AnthropicMessages));
            AssertEqual("anthropic models converted to generation", "https://api.anthropic.com/v1/messages",
                LlmProtocolDispatcher.BuildGenerationUrl(
                    "https://api.anthropic.com/v1/models", "claude", LlmProtocolMode.AnthropicMessages));
            AssertEqual("anthropic generation converted to models", "https://api.anthropic.com/v1/models",
                LlmProtocolDispatcher.BuildModelsUrl(
                    "https://api.anthropic.com/v1/messages", LlmProtocolMode.AnthropicMessages));
            AssertEqual("anthropic proxy version root", "https://proxy.example/claude/v1/messages",
                LlmProtocolDispatcher.BuildGenerationUrl(
                    "https://proxy.example/claude/v1", "claude", LlmProtocolMode.AnthropicMessages));
            AssertEqual("anthropic generation query/fragment preserved",
                "https://api.anthropic.com/v1/messages?tenant=a#f",
                LlmProtocolDispatcher.BuildGenerationUrl(
                    "https://api.anthropic.com/v1/messages?tenant=a#f",
                    "claude",
                    LlmProtocolMode.AnthropicMessages));

            AssertEqual("gemini default generation",
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash:generateContent",
                LlmProtocolDispatcher.BuildGenerationUrl(
                    null, "gemini-3-flash", LlmProtocolMode.GeminiGenerateContent));
            AssertEqual("gemini prefix normalized",
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash:generateContent",
                LlmProtocolDispatcher.BuildGenerationUrl(
                    null, "models/gemini-3-flash", LlmProtocolMode.GeminiGenerateContent));
            AssertEqual("gemini complete URL idempotent",
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash:generateContent",
                LlmProtocolDispatcher.BuildGenerationUrl(
                    "https://generativelanguage.googleapis.com/v1beta/models/old:generateContent",
                    "gemini-3-flash",
                    LlmProtocolMode.GeminiGenerateContent));
            AssertEqual("gemini full action query/fragment preserved",
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash:generateContent?tenant=a#f",
                LlmProtocolDispatcher.BuildGenerationUrl(
                    "https://generativelanguage.googleapis.com/v1beta/models/old:generateContent?tenant=a#f",
                    "models/gemini-3-flash",
                    LlmProtocolMode.GeminiGenerateContent));
            AssertEqual("gemini encoded model path",
                "https://generativelanguage.googleapis.com/v1beta/models/gemini%20custom%2Fvariant:generateContent",
                LlmProtocolDispatcher.BuildGenerationUrl(
                    null, "models/gemini custom/variant", LlmProtocolMode.GeminiGenerateContent));
            AssertEqual("gemini models from full action",
                "https://generativelanguage.googleapis.com/v1beta/models",
                LlmProtocolDispatcher.BuildModelsUrl(
                    "https://generativelanguage.googleapis.com/v1beta/models/old:generateContent",
                    LlmProtocolMode.GeminiGenerateContent));

            AssertEqual("ollama default chat", "http://localhost:11434/api/chat",
                LlmProtocolDispatcher.BuildGenerationUrl(null, "llama", LlmProtocolMode.OllamaChat));
            AssertEqual("ollama default tags", "http://localhost:11434/api/tags",
                LlmProtocolDispatcher.BuildModelsUrl(null, LlmProtocolMode.OllamaChat));
            AssertEqual("ollama chat idempotent", "http://localhost:11434/api/chat",
                LlmProtocolDispatcher.BuildGenerationUrl(
                    "http://localhost:11434/api/chat", "llama", LlmProtocolMode.OllamaChat));
            AssertEqual("ollama tags converted", "http://localhost:11434/api/chat",
                LlmProtocolDispatcher.BuildGenerationUrl(
                    "http://localhost:11434/api/tags", "llama", LlmProtocolMode.OllamaChat));
            AssertEqual("ollama generation query/fragment preserved",
                "http://localhost:11434/api/chat?tenant=a#f",
                LlmProtocolDispatcher.BuildGenerationUrl(
                    "http://localhost:11434/api/tags?tenant=a#f",
                    "llama",
                    LlmProtocolMode.OllamaChat));
            AssertEqual("host-only fallback keeps tail",
                "localhost:11434/api/chat?tenant=a#f",
                LlmProtocolDispatcher.BuildGenerationUrl(
                    "localhost:11434/api/tags?tenant=a#f",
                    "llama",
                    LlmProtocolMode.OllamaChat));

            string cursorUrl = LlmProtocolDispatcher.BuildModelsUrl(
                "https://api.anthropic.com/v1/models?tenant=a#pick",
                LlmProtocolMode.AnthropicMessages,
                "model cursor/2");
            AssertContains("anthropic cursor preserves query", cursorUrl, "tenant=a");
            AssertContains("anthropic cursor escaped", cursorUrl, "after_id=model%20cursor%2F2");
            AssertTrue("anthropic cursor preserves fragment", cursorUrl.EndsWith("#pick", StringComparison.Ordinal));

            string replaced = LlmProtocolDispatcher.BuildModelsUrl(
                "https://generativelanguage.googleapis.com/v1beta/models?pageToken=old&x=1#f",
                LlmProtocolMode.GeminiGenerateContent,
                "new token");
            AssertContains("gemini cursor replaced", replaced, "pageToken=new%20token");
            AssertNotContains("old gemini cursor removed", replaced, "pageToken=old");
            AssertContains("gemini unrelated query survives", replaced, "x=1");
            AssertTrue("gemini fragment survives", replaced.EndsWith("#f", StringComparison.Ordinal));

            string longCursor = new string('x', 1025);
            AssertNotContains("oversized cursor not sent",
                LlmProtocolDispatcher.BuildModelsUrl(null, LlmProtocolMode.AnthropicMessages, longCursor),
                "after_id=");
        }

        private static void TestHeadersAndAuthRecommendations()
        {
            LlmProtocolHeadersPlan openAi = LlmProtocolDispatcher.HeadersFor(
                LlmProtocolMode.OpenAIChatCompletions, ApiAuthMode.BearerToken, string.Empty);
            AssertEqual("openai fixed headers empty", 0, openAi.RequiredHeaders.Count);
            AssertEqual("openai bearer recommendation", ApiAuthMode.BearerToken, openAi.RecommendedAuthMode);

            LlmProtocolHeadersPlan anthropic = LlmProtocolDispatcher.HeadersFor(
                LlmProtocolMode.AnthropicMessages, ApiAuthMode.CustomHeader, "x-api-key");
            AssertEqual("anthropic fixed header count", 1, anthropic.RequiredHeaders.Count);
            AssertEqual("anthropic version name", "anthropic-version", anthropic.RequiredHeaders[0].Name);
            AssertEqual("anthropic version value", "2023-06-01", anthropic.RequiredHeaders[0].Value);
            AssertEqual("anthropic auth recommendation", ApiAuthMode.CustomHeader, anthropic.RecommendedAuthMode);
            AssertEqual("anthropic key header recommendation", "x-api-key", anthropic.RecommendedCustomHeaderName);
            AssertFalse("anthropic x-api-key does not collide", anthropic.HasSecretHeaderCollision);

            LlmProtocolHeadersPlan collision = LlmProtocolDispatcher.HeadersFor(
                LlmProtocolMode.AnthropicMessages, ApiAuthMode.CustomHeader, " Anthropic-Version ");
            AssertTrue("fixed/secret collision detected", collision.HasSecretHeaderCollision);
            AssertEqual("collision canonical name", "anthropic-version", collision.CollisionHeaderName);

            LlmProtocolHeadersPlan gemini = LlmProtocolDispatcher.HeadersFor(
                LlmProtocolMode.GeminiGenerateContent, ApiAuthMode.CustomHeader, "x-goog-api-key");
            AssertEqual("gemini no fixed headers", 0, gemini.RequiredHeaders.Count);
            AssertEqual("gemini custom header auth", ApiAuthMode.CustomHeader, gemini.RecommendedAuthMode);
            AssertEqual("gemini header recommendation", "x-goog-api-key", gemini.RecommendedCustomHeaderName);

            LlmProtocolHeadersPlan ollama = LlmProtocolDispatcher.HeadersFor(
                LlmProtocolMode.OllamaChat, ApiAuthMode.None, string.Empty);
            AssertEqual("ollama no auth recommendation", ApiAuthMode.None, ollama.RecommendedAuthMode);
            AssertEqual("ollama no fixed headers", 0, ollama.RequiredHeaders.Count);
        }

        private static void TestOpenAIRequestBaselines()
        {
            LlmProtocolRequestInput input = Request(LlmProtocolMode.OpenAIChatCompletions);
            string expectedChat = "{\"model\":\"model\\\"one\",\"messages\":[{\"role\":\"system\",\"content\":\"System\\nline\"},{\"role\":\"user\",\"content\":\"User\\ttext\"}],\"temperature\":0.7,\"max_tokens\":123,\"reasoning_effort\":\"high\"}";
            AssertEqual("chat literal baseline", expectedChat, LlmProtocolRequestJson.Build(input));
            AssertEqual("chat delegates byte exact", ExistingOpenAIJson(input, ApiCompatibilityMode.OpenAIChatCompletions),
                LlmProtocolRequestJson.Build(input));

            input.Mode = LlmProtocolMode.OpenAIResponses;
            string expectedResponses = "{\"model\":\"model\\\"one\",\"input\":\"User\\ttext\",\"temperature\":0.7,\"max_output_tokens\":369,\"instructions\":\"System\\nline\",\"reasoning\":{\"effort\":\"high\"}}";
            AssertEqual("responses literal baseline", expectedResponses, LlmProtocolRequestJson.Build(input));
            AssertEqual("responses delegates byte exact", ExistingOpenAIJson(input, ApiCompatibilityMode.OpenAIResponses),
                LlmProtocolRequestJson.Build(input));

            input.ProviderMaximumOutputTokens = 4;
            input.ProviderMaximumTemperature = 0.1d;
            AssertEqual("provider metadata ignored by OpenAI", expectedResponses, LlmProtocolRequestJson.Build(input));

            input.Mode = LlmProtocolMode.OpenAIChatCompletions;
            input.SystemPrompt = "  ";
            input.ReasoningEffort = "none";
            AssertEqual("chat no-system/none byte baseline",
                "{\"model\":\"model\\\"one\",\"messages\":[{\"role\":\"user\",\"content\":\"User\\ttext\"}],\"temperature\":0.7,\"max_tokens\":123}",
                LlmProtocolRequestJson.Build(input));
        }

        private static void TestNativeRequests()
        {
            LlmProtocolRequestInput anthropic = Request(LlmProtocolMode.AnthropicMessages);
            AssertEqual("anthropic exact body",
                "{\"model\":\"model\\\"one\",\"max_tokens\":123,\"system\":\"System\\nline\",\"messages\":[{\"role\":\"user\",\"content\":\"User\\ttext\"}]}",
                LlmProtocolRequestJson.Build(anthropic));
            AssertNotContains("anthropic omits temperature", LlmProtocolRequestJson.Build(anthropic), "temperature");
            AssertNotContains("anthropic omits thinking", LlmProtocolRequestJson.Build(anthropic), "thinking");
            anthropic.SystemPrompt = " ";
            anthropic.ProviderMaximumOutputTokens = 50;
            AssertEqual("anthropic no system + provider cap",
                "{\"model\":\"model\\\"one\",\"max_tokens\":50,\"messages\":[{\"role\":\"user\",\"content\":\"User\\ttext\"}]}",
                LlmProtocolRequestJson.Build(anthropic));
            anthropic.ProviderMaximumOutputTokens = 0;
            AssertContains("zero provider cap ignored", LlmProtocolRequestJson.Build(anthropic), "\"max_tokens\":123");

            LlmProtocolRequestInput gemini = Request(LlmProtocolMode.GeminiGenerateContent);
            AssertEqual("gemini unknown max omits temperature",
                "{\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"User\\ttext\"}]}],\"systemInstruction\":{\"parts\":[{\"text\":\"System\\nline\"}]},\"generationConfig\":{\"maxOutputTokens\":123}}",
                LlmProtocolRequestJson.Build(gemini));
            gemini.ProviderMaximumTemperature = 0.4d;
            gemini.ProviderMaximumOutputTokens = 100;
            AssertEqual("gemini clamps fetched bounds",
                "{\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"User\\ttext\"}]}],\"systemInstruction\":{\"parts\":[{\"text\":\"System\\nline\"}]},\"generationConfig\":{\"maxOutputTokens\":100,\"temperature\":0.4}}",
                LlmProtocolRequestJson.Build(gemini));
            gemini.SystemPrompt = null;
            gemini.ProviderMaximumTemperature = double.NaN;
            AssertNotContains("gemini corrupt max omits temperature", LlmProtocolRequestJson.Build(gemini), "temperature");
            AssertNotContains("gemini blank system omitted", LlmProtocolRequestJson.Build(gemini), "systemInstruction");

            LlmProtocolRequestInput geminiFlash = Request(LlmProtocolMode.GeminiGenerateContent);
            geminiFlash.ModelName = "models/GEMINI-2.5-FLASH-preview-09-2025";
            geminiFlash.MaxTokens = 32;
            AssertEqual("gemini 2.5 flash disables thinking without stealing visible budget",
                "{\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"User\\ttext\"}]}],"
                    + "\"systemInstruction\":{\"parts\":[{\"text\":\"System\\nline\"}]},"
                    + "\"generationConfig\":{\"maxOutputTokens\":32,"
                    + "\"thinkingConfig\":{\"thinkingBudget\":0}}}",
                LlmProtocolRequestJson.Build(geminiFlash));

            LlmProtocolRequestInput geminiFlashLite = Request(LlmProtocolMode.GeminiGenerateContent);
            geminiFlashLite.ModelName = "gemini-2.5-flash-lite";
            geminiFlashLite.MaxTokens = 40;
            AssertContains("gemini 2.5 flash-lite disables thinking",
                LlmProtocolRequestJson.Build(geminiFlashLite),
                "\"maxOutputTokens\":40,\"thinkingConfig\":{\"thinkingBudget\":0}");

            LlmProtocolRequestInput geminiPro = Request(LlmProtocolMode.GeminiGenerateContent);
            geminiPro.ModelName = "gemini-2.5-pro";
            geminiPro.MaxTokens = 32;
            AssertContains("gemini 2.5 pro reserves minimum thinking headroom",
                LlmProtocolRequestJson.Build(geminiPro),
                "\"maxOutputTokens\":160,\"thinkingConfig\":{\"thinkingBudget\":128}");

            LlmProtocolRequestInput gemini3 = Request(LlmProtocolMode.GeminiGenerateContent);
            gemini3.ModelName = "gemini-3.6-flash";
            gemini3.MaxTokens = 40;
            AssertContains("gemini 3 uses low thinking with headroom",
                LlmProtocolRequestJson.Build(gemini3),
                "\"maxOutputTokens\":1064,\"thinkingConfig\":{\"thinkingLevel\":\"low\"}");
            gemini3.ProviderMaximumOutputTokens = 500;
            AssertContains("gemini thinking headroom respects provider cap",
                LlmProtocolRequestJson.Build(gemini3),
                "\"maxOutputTokens\":500,\"thinkingConfig\":{\"thinkingLevel\":\"low\"}");
            LlmProtocolRequestInput gemini3Tuned = Request(LlmProtocolMode.GeminiGenerateContent);
            gemini3Tuned.ModelName = "gemini-3.6-flash";
            gemini3Tuned.MaxTokens = 40;
            gemini3Tuned.LowThinkingHeadroomTokens = 96;
            AssertContains("gemini uses detached XML headroom snapshot",
                LlmProtocolRequestJson.Build(gemini3Tuned),
                "\"maxOutputTokens\":136,\"thinkingConfig\":{\"thinkingLevel\":\"low\"}");

            LlmProtocolRequestInput geminiImage = Request(LlmProtocolMode.GeminiGenerateContent);
            geminiImage.ModelName = "gemini-2.5-flash-image";
            AssertNotContains("gemini 2.5 image omits unsupported thinking config",
                LlmProtocolRequestJson.Build(geminiImage), "thinkingConfig");
            AssertContains("gemini 2.5 image keeps visible token cap",
                LlmProtocolRequestJson.Build(geminiImage), "\"maxOutputTokens\":123");

            LlmProtocolRequestInput gemini3Image = Request(LlmProtocolMode.GeminiGenerateContent);
            gemini3Image.ModelName = "models/gemini-3.1-flash-lite-image";
            gemini3Image.MaxTokens = 32;
            AssertContains("gemini 3 flash image uses supported minimal level",
                LlmProtocolRequestJson.Build(gemini3Image),
                "\"maxOutputTokens\":1056,\"thinkingConfig\":{\"thinkingLevel\":\"minimal\"}");

            LlmProtocolRequestInput gemini3ProImage = Request(LlmProtocolMode.GeminiGenerateContent);
            gemini3ProImage.ModelName = "gemini-3-pro-image";
            AssertNotContains("gemini 3 pro image omits unsupported low level",
                LlmProtocolRequestJson.Build(gemini3ProImage), "thinkingConfig");

            LlmProtocolRequestInput robotics = Request(LlmProtocolMode.GeminiGenerateContent);
            robotics.ModelName = "models/GEMINI-ROBOTICS-ER-1.6-PREVIEW";
            robotics.MaxTokens = 32;
            AssertContains("gemini robotics disables thinking without stealing visible budget",
                LlmProtocolRequestJson.Build(robotics),
                "\"maxOutputTokens\":32,\"thinkingConfig\":{\"thinkingBudget\":0}");

            string[] latestAliases =
            {
                "gemini-flash-latest",
                "models/GEMINI-FLASH-LITE-LATEST",
                "gemini-pro-latest"
            };
            for (int i = 0; i < latestAliases.Length; i++)
            {
                LlmProtocolRequestInput latest = Request(LlmProtocolMode.GeminiGenerateContent);
                latest.ModelName = latestAliases[i];
                latest.MaxTokens = 32;
                string latestJson = LlmProtocolRequestJson.Build(latest);
                AssertContains("gemini latest alias low thinking " + latestAliases[i], latestJson,
                    "\"maxOutputTokens\":1056");
                AssertContains("gemini latest alias thinking level " + latestAliases[i], latestJson,
                    "\"thinkingConfig\":{\"thinkingLevel\":\"low\"}");
            }

            LlmProtocolRequestInput ollama = Request(LlmProtocolMode.OllamaChat);
            AssertEqual("ollama exact body",
                "{\"model\":\"model\\\"one\",\"messages\":[{\"role\":\"system\",\"content\":\"System\\nline\"},{\"role\":\"user\",\"content\":\"User\\ttext\"}],\"stream\":false,\"think\":false,\"options\":{\"temperature\":0.7,\"num_predict\":123}}",
                LlmProtocolRequestJson.Build(ollama));
            ollama.Temperature = 99f;
            ollama.ProviderMaximumOutputTokens = 20;
            AssertContains("ollama temperature bounded", LlmProtocolRequestJson.Build(ollama), "\"temperature\":2");
            AssertContains("ollama model token cap", LlmProtocolRequestJson.Build(ollama), "\"num_predict\":20");
            ollama.SystemPrompt = string.Empty;
            ollama.Temperature = float.NaN;
            AssertNotContains("ollama blank system omitted", LlmProtocolRequestJson.Build(ollama), "\"role\":\"system\"");
            AssertContains("ollama nonfinite temp safe", LlmProtocolRequestJson.Build(ollama), "\"temperature\":1");

            LlmProtocolRequestInput gptOss = Request(LlmProtocolMode.OllamaChat);
            gptOss.ModelName = "registry.local/team/GPT-OSS:20b";
            gptOss.MaxTokens = 32;
            AssertEqual("ollama gpt-oss uses supported low thinking with headroom",
                "{\"model\":\"registry.local/team/GPT-OSS:20b\","
                    + "\"messages\":[{\"role\":\"system\",\"content\":\"System\\nline\"},"
                    + "{\"role\":\"user\",\"content\":\"User\\ttext\"}],"
                    + "\"stream\":false,\"think\":\"low\","
                    + "\"options\":{\"temperature\":0.7,\"num_predict\":1056}}",
                LlmProtocolRequestJson.Build(gptOss));
            gptOss.ProviderMaximumOutputTokens = 500;
            AssertContains("ollama gpt-oss headroom respects provider cap",
                LlmProtocolRequestJson.Build(gptOss), "\"num_predict\":500");
            LlmProtocolRequestInput tunedGptOss = Request(LlmProtocolMode.OllamaChat);
            tunedGptOss.ModelName = "gpt-oss:20b";
            tunedGptOss.MaxTokens = 32;
            tunedGptOss.LowThinkingHeadroomTokens = 96;
            AssertContains("ollama gpt-oss uses detached XML headroom snapshot",
                LlmProtocolRequestJson.Build(tunedGptOss), "\"num_predict\":128");
            tunedGptOss.LowThinkingHeadroomTokens = -1;
            AssertContains("corrupt low-thinking snapshot uses safe fallback",
                LlmProtocolRequestJson.Build(tunedGptOss), "\"num_predict\":1056");

            LlmProtocolRequestInput aliasedGptOss = Request(LlmProtocolMode.OllamaChat);
            aliasedGptOss.ModelName = "diary-writer:latest";
            aliasedGptOss.ProviderModelFamily = "gptoss";
            aliasedGptOss.MaxTokens = 32;
            string aliasedGptOssJson = LlmProtocolRequestJson.Build(aliasedGptOss);
            AssertContains("ollama renamed gpt-oss uses discovered family level",
                aliasedGptOssJson, "\"think\":\"low\"");
            AssertContains("ollama renamed gpt-oss receives thinking headroom",
                aliasedGptOssJson, "\"num_predict\":1056");
        }

        private static void TestOpenAIResponses()
        {
            string chatJson = "{\"choices\":[{\"message\":{\"content\":\"Visible\"},\"finish_reason\":\"length\"}]}";
            LlmProtocolParseResult chat = LlmProtocolResponseCodec.ParseGeneration(
                chatJson, LlmProtocolMode.OpenAIChatCompletions);
            AssertTrue("chat parsed success", chat.Success);
            AssertEqual("chat text exact", "Visible", chat.Text);
            AssertEqual("chat finish reason", "length", chat.FinishReason);
            AssertTrue("chat length truncated", chat.Truncated);

            LlmProtocolParseResult boundedChat = LlmProtocolResponseCodec.ParseGeneration(
                "{\"choices\":[{\"message\":{\"content\":\"Visible\"},"
                    + "\"finish_reason\":\"custom\\r\\n\\u0001" + new string('c', 700) + "\"}]}",
                LlmProtocolMode.OpenAIChatCompletions);
            AssertEqual("chat finish reason bounded", 512, boundedChat.FinishReason.Length);
            AssertNotContains("chat finish reason strips newlines", boundedChat.FinishReason, "\n");
            AssertNotContains("chat finish reason strips controls", boundedChat.FinishReason, "\u0001");

            string responsesJson = "{\"status\":\"completed\",\"output\":["
                + "{\"type\":\"reasoning\",\"content\":[{\"text\":\"secret\"}]},"
                + "{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"One\"},{\"type\":\"output_text\",\"text\":\"Two\"}]}]}";
            LlmProtocolParseResult responses = LlmProtocolResponseCodec.ParseGeneration(
                responsesJson, LlmProtocolMode.OpenAIResponses);
            AssertEqual("responses uses existing block parser", "One\nTwo", responses.Text);
            AssertNotContains("responses reasoning excluded", responses.Text, "secret");
            AssertEqual("responses status finish", "completed", responses.FinishReason);

            LlmProtocolParseResult boundedResponses = LlmProtocolResponseCodec.ParseGeneration(
                "{\"status\":\"custom\\n\\u0001" + new string('s', 700) + "\","
                    + "\"output\":[{\"type\":\"message\",\"content\":["
                    + "{\"type\":\"output_text\",\"text\":\"Visible\"}]}]}",
                LlmProtocolMode.OpenAIResponses);
            AssertEqual("responses finish reason bounded", 512, boundedResponses.FinishReason.Length);
            AssertNotContains("responses finish reason strips newlines", boundedResponses.FinishReason, "\n");
            AssertNotContains("responses finish reason strips controls", boundedResponses.FinishReason, "\u0001");

            LlmProtocolParseResult refusal = LlmProtocolResponseCodec.ParseGeneration(
                "{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"No.\"}]}]}",
                LlmProtocolMode.OpenAIResponses);
            AssertTrue("responses refusal flagged", refusal.Refused);
            AssertContains("responses refusal surfaced", refusal.ProviderError, "No.");
        }

        private static void TestAnthropicResponses()
        {
            LlmProtocolParseResult success = LlmProtocolResponseCodec.ParseGeneration(
                "{\"content\":[{\"type\":\"thinking\",\"thinking\":\"secret\"},{\"type\":\"text\",\"text\":\"One\"},{\"type\":\"redacted_thinking\",\"data\":\"x\"},{\"type\":\"text\",\"text\":\"Two\"}],\"stop_reason\":\"end_turn\"}",
                LlmProtocolMode.AnthropicMessages);
            AssertTrue("anthropic success", success.Success);
            AssertEqual("anthropic text blocks", "One\nTwo", success.Text);
            AssertNotContains("anthropic thinking excluded", success.Text, "secret");
            AssertEqual("anthropic finish", "end_turn", success.FinishReason);

            LlmProtocolParseResult truncated = LlmProtocolResponseCodec.ParseGeneration(
                "{\"content\":[{\"type\":\"text\",\"text\":\"Partial\"}],\"stop_reason\":\"max_tokens\"}",
                LlmProtocolMode.AnthropicMessages);
            AssertTrue("anthropic max tokens truncated", truncated.Truncated);
            AssertTrue("anthropic partial text usable", truncated.Success);

            LlmProtocolParseResult thinkingOnly = LlmProtocolResponseCodec.ParseGeneration(
                "{\"content\":[{\"type\":\"thinking\",\"thinking\":\"secret\"}],\"stop_reason\":\"end_turn\"}",
                LlmProtocolMode.AnthropicMessages);
            AssertContains("anthropic thinking-only no content", thinkingOnly.ProviderError, "no text content");

            LlmProtocolParseResult refusal = LlmProtocolResponseCodec.ParseGeneration(
                "{\"content\":[{\"type\":\"text\",\"text\":\"I cannot.\"}],\"stop_reason\":\"refusal\","
                    + "\"stop_details\":{\"type\":\"refusal\",\"category\":\"cyber\","
                    + "\"explanation\":\"Policy\\nexplanation " + new string('e', 700) + "\","
                    + "\"message\":\"obsolete fixture field must not surface\"}}",
                LlmProtocolMode.AnthropicMessages);
            AssertTrue("anthropic refusal flag", refusal.Refused);
            AssertFalse("anthropic refusal is not success", refusal.Success);
            AssertContains("anthropic refusal type surfaced", refusal.ProviderError, "type=refusal");
            AssertContains("anthropic refusal category surfaced", refusal.ProviderError, "category=cyber");
            AssertContains("anthropic refusal explanation surfaced", refusal.ProviderError, "Policy explanation");
            AssertNotContains("anthropic refusal stays one line", refusal.ProviderError, "\n");
            AssertNotContains("anthropic obsolete refusal message ignored", refusal.ProviderError,
                "obsolete fixture field");
            AssertTrue("anthropic refusal detail bounded", refusal.ProviderError.Length <= 512);

            LlmProtocolParseResult minimalRefusal = LlmProtocolResponseCodec.ParseGeneration(
                "{\"content\":[],\"stop_reason\":\"refusal\","
                    + "\"stop_details\":{\"type\":\"refusal\"}}",
                LlmProtocolMode.AnthropicMessages);
            AssertContains("anthropic sparse refusal safely falls back", minimalRefusal.ProviderError,
                "type=refusal");

            LlmProtocolParseResult boundedStop = LlmProtocolResponseCodec.ParseGeneration(
                "{\"content\":[{\"type\":\"text\",\"text\":\"Visible\"}],\"stop_reason\":\"custom\\n\\u0001"
                    + new string('x', 700) + "\"}",
                LlmProtocolMode.AnthropicMessages);
            AssertEqual("anthropic finish reason bounded", 512, boundedStop.FinishReason.Length);
            AssertNotContains("anthropic finish reason stays one line", boundedStop.FinishReason, "\n");
            AssertNotContains("anthropic finish reason strips controls", boundedStop.FinishReason, "\u0001");

            LlmProtocolParseResult overloaded = LlmProtocolResponseCodec.ParseGeneration(
                "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Busy\"},\"request_id\":\"req_123\"}",
                LlmProtocolMode.AnthropicMessages,
                529);
            AssertContains("anthropic error type", overloaded.ProviderError, "overloaded_error");
            AssertContains("anthropic request id", overloaded.ProviderError, "req_123");
            AssertEqual("anthropic overloaded transient", LlmProtocolFailureDisposition.Transient,
                overloaded.FailureDisposition);

            LlmProtocolParseResult conflict = LlmProtocolResponseCodec.ParseGeneration(
                "{\"error\":{\"type\":\"conflict_error\",\"message\":\"Try later\"}}",
                LlmProtocolMode.AnthropicMessages,
                409);
            AssertEqual("anthropic 409 transient", LlmProtocolFailureDisposition.Transient,
                conflict.FailureDisposition);
        }

        private static void TestGeminiResponses()
        {
            LlmProtocolParseResult success = LlmProtocolResponseCodec.ParseGeneration(
                "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"secret\",\"thought\":true},{\"text\":\"One\"},{\"text\":\"Two\"}]},\"finishReason\":\"MAX_TOKENS\",\"finishMessage\":\"Token cap reached\"}]}",
                LlmProtocolMode.GeminiGenerateContent);
            AssertTrue("gemini partial success", success.Success);
            AssertEqual("gemini visible parts", "One\nTwo", success.Text);
            AssertNotContains("gemini thought excluded", success.Text, "secret");
            AssertTrue("gemini max tokens truncated", success.Truncated);
            AssertEqual("gemini finish reason", "MAX_TOKENS", success.FinishReason);
            AssertEqual("gemini finish message", "Token cap reached", success.FinishMessage);

            string longMessage = new string('z', 700);
            LlmProtocolParseResult bounded = LlmProtocolResponseCodec.ParseGeneration(
                "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Text\"}]},\"finishReason\":\"STOP\",\"finishMessage\":\""
                    + longMessage + "\"}]}",
                LlmProtocolMode.GeminiGenerateContent);
            AssertEqual("gemini finish message bounded", 512, bounded.FinishMessage.Length);

            LlmProtocolParseResult safety = LlmProtocolResponseCodec.ParseGeneration(
                "{\"candidates\":[{\"finishReason\":\"SAFETY\",\"finishMessage\":\"unsafe\"}]}",
                LlmProtocolMode.GeminiGenerateContent);
            AssertTrue("gemini safety refusal", safety.Refused);
            AssertContains("gemini safety surfaced", safety.ProviderError, "SAFETY");
            AssertContains("gemini safety message surfaced", safety.ProviderError, "unsafe");

            string[] newlyFilteredReasons =
            {
                "LANGUAGE",
                "ESCALATION",
                "IMAGE_PROHIBITED_CONTENT",
                "IMAGE_RECITATION"
            };
            for (int i = 0; i < newlyFilteredReasons.Length; i++)
            {
                string reason = newlyFilteredReasons[i];
                LlmProtocolParseResult filtered = LlmProtocolResponseCodec.ParseGeneration(
                    "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"partial filtered text\"}]},"
                        + "\"finishReason\":\"" + reason + "\"}]}",
                    LlmProtocolMode.GeminiGenerateContent);
                AssertTrue("gemini filtered reason refusal " + reason, filtered.Refused);
                AssertFalse("gemini filtered partial text rejected " + reason, filtered.Success);
                AssertContains("gemini filtered reason surfaced " + reason, filtered.ProviderError, reason);
            }

            string[] technicalReasons = { "MALFORMED_RESPONSE", "UNEXPECTED_TOOL_CALL" };
            for (int i = 0; i < technicalReasons.Length; i++)
            {
                string reason = technicalReasons[i];
                LlmProtocolParseResult technical = LlmProtocolResponseCodec.ParseGeneration(
                    "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"partial technical text\"}]},"
                        + "\"finishReason\":\"" + reason + "\"}]}",
                    LlmProtocolMode.GeminiGenerateContent);
                AssertFalse("gemini technical reason is not refusal " + reason, technical.Refused);
                AssertTrue("gemini technical partial text remains usable " + reason, technical.Success);
            }

            LlmProtocolParseResult boundedReason = LlmProtocolResponseCodec.ParseGeneration(
                "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Text\"}]},"
                    + "\"finishReason\":\"custom\\n\\u0001" + new string('r', 700) + "\"}]}",
                LlmProtocolMode.GeminiGenerateContent);
            AssertEqual("gemini finish reason bounded", 512, boundedReason.FinishReason.Length);
            AssertNotContains("gemini finish reason stays one line", boundedReason.FinishReason, "\n");
            AssertNotContains("gemini finish reason strips controls", boundedReason.FinishReason, "\u0001");

            LlmProtocolParseResult thoughtOnly = LlmProtocolResponseCodec.ParseGeneration(
                "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"secret\",\"thought\":true}]},\"finishReason\":\"STOP\"}]}",
                LlmProtocolMode.GeminiGenerateContent);
            AssertContains("gemini thought-only no content", thoughtOnly.ProviderError, "no text content");
            AssertNotContains("gemini thought-only secret hidden", thoughtOnly.ProviderError, "secret");

            LlmProtocolParseResult promptBlock = LlmProtocolResponseCodec.ParseGeneration(
                "{\"promptFeedback\":{\"blockReason\":\"PROHIBITED_CONTENT\"}}",
                LlmProtocolMode.GeminiGenerateContent);
            AssertTrue("gemini prompt block refusal", promptBlock.Refused);
            AssertContains("gemini prompt block reason", promptBlock.ProviderError, "PROHIBITED_CONTENT");

            LlmProtocolParseResult unavailable = LlmProtocolResponseCodec.ParseGeneration(
                "{\"error\":{\"code\":503,\"status\":\"UNAVAILABLE\",\"message\":\"Back soon\"}}",
                LlmProtocolMode.GeminiGenerateContent,
                503);
            AssertContains("gemini top error status", unavailable.ProviderError, "UNAVAILABLE");
            AssertEqual("gemini unavailable transient", LlmProtocolFailureDisposition.Transient,
                unavailable.FailureDisposition);

            LlmProtocolParseResult invalidField = LlmProtocolResponseCodec.ParseGeneration(
                "{\"error\":{\"code\":400,\"status\":\"INVALID_ARGUMENT\",\"message\":\"Bad request\","
                    + "\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.ErrorInfo\","
                    + "\"reason\":\"INVALID_FIELD\",\"domain\":\"googleapis.com\",\"metadata\":{"
                    + "\"service\":\"generativelanguage.googleapis.com\","
                    + "\"method\":\"google.ai.generativelanguage.v1beta.GenerativeService.GenerateContent\","
                    + "\"raw\":\"must-not-surface\"}},{"
                    + "\"@type\":\"type.googleapis.com/google.rpc.BadRequest\","
                    + "\"fieldViolations\":[{\"field\":\"generationConfig.maxOutputTokens\","
                    + "\"description\":\"must be positive\"},{\"field\":\"contents[0].parts[0].text\","
                    + "\"description\":\"must not be blank\\nfor generation\"}],"
                    + "\"ignored\":\"also-must-not-surface\"}]}}",
                LlmProtocolMode.GeminiGenerateContent,
                400);
            AssertContains("gemini structured detail reason", invalidField.ProviderError, "INVALID_FIELD");
            AssertContains("gemini structured detail domain", invalidField.ProviderError, "googleapis.com");
            AssertContains("gemini structured detail service", invalidField.ProviderError,
                "generativelanguage.googleapis.com");
            AssertContains("gemini structured detail method", invalidField.ProviderError,
                "GenerativeService.GenerateContent");
            AssertContains("gemini structured detail field", invalidField.ProviderError,
                "generationConfig.maxOutputTokens");
            AssertContains("gemini structured detail description", invalidField.ProviderError,
                "must be positive");
            AssertContains("gemini second field violation", invalidField.ProviderError,
                "contents[0].parts[0].text");
            AssertContains("gemini second description normalized", invalidField.ProviderError,
                "must not be blank for generation");
            AssertNotContains("gemini structured details one line", invalidField.ProviderError, "\n");
            AssertTrue("gemini structured details bounded", invalidField.ProviderError.Length <= 512);
            AssertNotContains("gemini arbitrary metadata hidden", invalidField.ProviderError,
                "must-not-surface");
            AssertNotContains("gemini arbitrary detail hidden", invalidField.ProviderError,
                "also-must-not-surface");
        }

        private static void TestOllamaResponses()
        {
            LlmProtocolParseResult success = LlmProtocolResponseCodec.ParseGeneration(
                "{\"message\":{\"role\":\"assistant\",\"content\":\"Visible\",\"thinking\":\"secret\"},\"done\":true,\"done_reason\":\"stop\"}",
                LlmProtocolMode.OllamaChat);
            AssertTrue("ollama success", success.Success);
            AssertEqual("ollama content", "Visible", success.Text);
            AssertNotContains("ollama thinking excluded", success.Text, "secret");
            AssertEqual("ollama finish", "stop", success.FinishReason);

            LlmProtocolParseResult length = LlmProtocolResponseCodec.ParseGeneration(
                "{\"message\":{\"content\":\"Partial\"},\"done\":true,\"done_reason\":\"length\"}",
                LlmProtocolMode.OllamaChat);
            AssertTrue("ollama length truncated", length.Truncated);

            LlmProtocolParseResult error = LlmProtocolResponseCodec.ParseGeneration(
                "{\"error\":\"model not found\"}", LlmProtocolMode.OllamaChat, 404);
            AssertContains("ollama error surfaced", error.ProviderError, "model not found");
            AssertEqual("ollama 404 permanent", LlmProtocolFailureDisposition.Permanent,
                error.FailureDisposition);

            LlmProtocolParseResult incomplete = LlmProtocolResponseCodec.ParseGeneration(
                "{\"message\":{\"content\":\"chunk\"},\"done\":false}",
                LlmProtocolMode.OllamaChat);
            AssertContains("ollama unexpected streaming rejected", incomplete.ProviderError, "incomplete");

            LlmProtocolParseResult thinkingOnly = LlmProtocolResponseCodec.ParseGeneration(
                "{\"message\":{\"thinking\":\"secret\",\"content\":\"\"},\"done\":true,\"done_reason\":\"stop\"}",
                LlmProtocolMode.OllamaChat);
            AssertContains("ollama thinking-only no content", thinkingOnly.ProviderError, "no message content");
            AssertNotContains("ollama thinking-only secret hidden", thinkingOnly.ProviderError, "secret");

            LlmProtocolParseResult boundedReason = LlmProtocolResponseCodec.ParseGeneration(
                "{\"message\":{\"content\":\"Visible\"},\"done\":true,"
                    + "\"done_reason\":\"custom\\n\\u0001" + new string('o', 700) + "\"}",
                LlmProtocolMode.OllamaChat);
            AssertEqual("ollama finish reason bounded", 512, boundedReason.FinishReason.Length);
            AssertNotContains("ollama finish reason strips newlines", boundedReason.FinishReason, "\n");
            AssertNotContains("ollama finish reason strips controls", boundedReason.FinishReason, "\u0001");
        }

        private static void TestRuntimeFailureDetail()
        {
            LlmProtocolParseResult normal = new LlmProtocolParseResult
            {
                Text = "Visible diary text",
                FinishReason = "end_turn"
            };
            AssertEqual("normal native finish is not an error", string.Empty,
                LlmProtocolRuntimePolicy.NativeProviderFailureDetail(normal));

            LlmProtocolParseResult failed = new LlmProtocolParseResult
            {
                ProviderError = "Gemini blocked the response.",
                FinishReason = "SAFETY",
                FinishMessage = "Policy stop"
            };
            string detail = LlmProtocolRuntimePolicy.NativeProviderFailureDetail(failed);
            AssertContains("native failure retains bounded provider error", detail, "blocked");
            AssertContains("native failure includes finish reason", detail, "SAFETY");
            AssertContains("native failure includes finish message", detail, "Policy stop");

            LlmProtocolParseResult noText = new LlmProtocolParseResult
            {
                FinishReason = "MAX_TOKENS"
            };
            AssertContains("no-text finish gets safe fallback",
                LlmProtocolRuntimePolicy.NativeProviderFailureDetail(noText),
                "no usable message content");

            string hostile = "provider\r\n\t\u0001" + new string('x', 1200);
            string bounded = LlmProtocolRuntimePolicy.NativeProviderFailureDetail(
                new LlmProtocolParseResult
                {
                    ProviderError = "Provider\r\nfailed.\u0000",
                    FinishReason = "SAFETY\r\n\u0001",
                    FinishMessage = hostile
                });
            AssertTrue("native final failure detail bounded", bounded.Length <= 512);
            AssertNotContains("native final failure strips carriage returns", bounded, "\r");
            AssertNotContains("native final failure strips newlines", bounded, "\n");
            AssertNotContains("native final failure strips controls", bounded, "\u0001");
            AssertNotContains("native final failure strips nul", bounded, "\u0000");
            AssertContains("native final failure keeps finish label", bounded, "finish_reason=SAFETY");
            AssertContains("native final failure keeps message label", bounded, "message=provider");

            AssertEqual("control-only error does not hide usable text", string.Empty,
                LlmProtocolRuntimePolicy.NativeProviderFailureDetail(
                    new LlmProtocolParseResult
                    {
                        ProviderError = "\r\n\t\u0000\u0001",
                        Text = "Visible"
                    }));
            AssertContains("control-only empty result gets fallback",
                LlmProtocolRuntimePolicy.NativeProviderFailureDetail(
                    new LlmProtocolParseResult { ProviderError = "\r\n\t\u0000\u0001" }),
                "no usable message content");

            string surrogateBoundary = LlmProtocolRuntimePolicy.NativeProviderFailureDetail(
                new LlmProtocolParseResult
                {
                    ProviderError = new string('z', 511) + "\uD800"
                });
            AssertEqual("native cap removes dangling high surrogate", 511, surrogateBoundary.Length);
            AssertFalse("native result ends on complete UTF-16 boundary",
                char.IsHighSurrogate(surrogateBoundary[surrogateBoundary.Length - 1]));
        }

        private static void TestMalformedAndDisposition()
        {
            LlmProtocolParseResult malformed = LlmProtocolResponseCodec.ParseGeneration(
                "{broken", LlmProtocolMode.AnthropicMessages, 500);
            AssertFalse("malformed not parsed", malformed.ParsedJsonObject);
            AssertContains("malformed sanitized", malformed.ProviderError, "malformed JSON");
            AssertNotContains("malformed raw hidden", malformed.ProviderError, "broken");
            AssertEqual("malformed HTTP 500 remains transient", LlmProtocolFailureDisposition.Transient,
                malformed.FailureDisposition);

            LlmProtocolParseResult malformedConflict = LlmProtocolResponseCodec.ParseGeneration(
                "plain-text conflict", LlmProtocolMode.AnthropicMessages, 409);
            AssertEqual("malformed Anthropic 409 remains transient", LlmProtocolFailureDisposition.Transient,
                malformedConflict.FailureDisposition);

            LlmProtocolParseResult malformedSuccess = LlmProtocolResponseCodec.ParseGeneration(
                "plain-text success", LlmProtocolMode.AnthropicMessages, 200);
            AssertEqual("malformed HTTP success is permanent", LlmProtocolFailureDisposition.Permanent,
                malformedSuccess.FailureDisposition);

            LlmProtocolParseResult array = LlmProtocolResponseCodec.ParseGeneration(
                "[]", LlmProtocolMode.GeminiGenerateContent);
            AssertContains("nonobject sanitized", array.ProviderError, "JSON object");

            LlmProtocolParseResult server = LlmProtocolResponseCodec.ParseGeneration(
                "{\"error\":{\"status\":\"INTERNAL\",\"message\":\"failed\"}}",
                LlmProtocolMode.GeminiGenerateContent,
                500);
            AssertEqual("HTTP 500 transient", LlmProtocolFailureDisposition.Transient,
                server.FailureDisposition);

            LlmProtocolParseResult geminiConflict = LlmProtocolResponseCodec.ParseGeneration(
                "{\"error\":{\"status\":\"CONFLICT\",\"message\":\"no\"}}",
                LlmProtocolMode.GeminiGenerateContent,
                409);
            AssertEqual("non-anthropic 409 permanent", LlmProtocolFailureDisposition.Permanent,
                geminiConflict.FailureDisposition);

            string longError = new string('x', 900);
            LlmProtocolParseResult bounded = LlmProtocolResponseCodec.ParseGeneration(
                "{\"error\":{\"type\":\"bad\",\"message\":\"" + longError + "\"}}",
                LlmProtocolMode.AnthropicMessages,
                400);
            AssertTrue("provider error bounded", bounded.ProviderError.Length <= 512);
        }

        private static void TestOpenAIAndAnthropicModels()
        {
            LlmProtocolModelPageResult openAi = LlmProtocolModelListCodec.ParsePage(
                "{\"data\":["
                    + "{\"id\":\"zeta\"},"
                    + "{\"id\":\"alpha\",\"reasoning\":{\"default_enabled\":true,\"supported_efforts\":[\"low\",\"high\"]}},"
                    + "{\"id\":\"alpha\"},{}]}",
                LlmProtocolMode.OpenAIChatCompletions);
            AssertTrue("openai model page parsed", openAi.ParsedJsonObject);
            AssertEqual("openai model dedup count", 2, openAi.Models.Count);
            AssertEqual("openai models sorted first", "alpha", openAi.Models[0].Id);
            AssertEqual("openai models sorted second", "zeta", openAi.Models[1].Id);
            AssertTrue("openai reasoning metadata retained", openAi.Models[0].ReasoningCapability != null);
            AssertTrue("openai reasoning supported", openAi.Models[0].ReasoningCapability.Supported);

            LlmProtocolModelPageResult rawOpenAi = LlmProtocolModelListCodec.ParsePage(
                "{\"data\":[{\"id\":\" model-with-spaces \"}]}",
                LlmProtocolMode.OpenAIChatCompletions);
            AssertEqual("openai model id preserves legacy raw text", " model-with-spaces ",
                rawOpenAi.Models[0].Id);

            LlmProtocolModelPageResult anthropic = LlmProtocolModelListCodec.ParsePage(
                "{\"data\":[{\"id\":\"claude-z\",\"max_tokens\":64000},{\"id\":\"claude-a\"}],\"has_more\":true,\"last_id\":\"claude-z\"}",
                LlmProtocolMode.AnthropicMessages);
            AssertEqual("anthropic models sorted", "claude-a", anthropic.Models[0].Id);
            AssertEqual("anthropic max output metadata", 64000, anthropic.Models[1].MaxOutputTokens);
            AssertTrue("anthropic next page", anthropic.HasNextPage);
            AssertEqual("anthropic cursor parameter", "after_id", anthropic.NextPageParameterName);
            AssertEqual("anthropic cursor", "claude-z", anthropic.NextPageCursor);

            string oversized = new string('c', 1025);
            LlmProtocolModelPageResult bounded = LlmProtocolModelListCodec.ParsePage(
                "{\"data\":[],\"has_more\":true,\"last_id\":\"" + oversized + "\"}",
                LlmProtocolMode.AnthropicMessages);
            AssertFalse("oversized anthropic cursor dropped", bounded.HasNextPage);
        }

        private static void TestGeminiModels()
        {
            LlmProtocolModelPageResult gemini = LlmProtocolModelListCodec.ParsePage(
                "{\"models\":["
                    + "{\"name\":\"models/embed-only\",\"supportedGenerationMethods\":[\"embedContent\"]},"
                    + "{\"name\":\"models/gemini-z\",\"outputTokenLimit\":8192,\"maxTemperature\":1.5,\"supportedGenerationMethods\":[\"generateContent\"]},"
                    + "{\"baseModelId\":\" models/gemini-a \",\"supportedGenerationMethods\":[\"GENERATECONTENT\"]},"
                    + "{\"name\":\"models/gemini-z\",\"supportedGenerationMethods\":[\"generateContent\"]}],"
                    + "\"nextPageToken\":\"next/one\"}",
                LlmProtocolMode.GeminiGenerateContent);
            AssertEqual("gemini filters/dedups", 2, gemini.Models.Count);
            AssertEqual("gemini base id sorted", "gemini-a", gemini.Models[0].Id);
            AssertEqual("gemini prefix removed", "gemini-z", gemini.Models[1].Id);
            AssertEqual("gemini output cap metadata", 8192, gemini.Models[1].MaxOutputTokens);
            AssertEqual("gemini temperature metadata", 1.5d, gemini.Models[1].MaxTemperature.Value);
            AssertTrue("gemini next page", gemini.HasNextPage);
            AssertEqual("gemini cursor parameter", "pageToken", gemini.NextPageParameterName);
            AssertEqual("gemini cursor", "next/one", gemini.NextPageCursor);

            LlmProtocolModelPageResult noMethods = LlmProtocolModelListCodec.ParsePage(
                "{\"models\":[{\"name\":\"models/no-methods\"}]}",
                LlmProtocolMode.GeminiGenerateContent);
            AssertEqual("gemini missing methods filtered", 0, noMethods.Models.Count);
        }

        private static void TestOllamaModelsAndLimits()
        {
            LlmProtocolModelPageResult ollama = LlmProtocolModelListCodec.ParsePage(
                "{\"models\":[{\"name\":\"zeta:latest\",\"model\":\"ignored\"},"
                    + "{\"model\":\"alpha:q4\"},{\"name\":\"zeta:latest\","
                    + "\"details\":{\"family\":\"gptoss\"}},{}]}",
                LlmProtocolMode.OllamaChat);
            AssertEqual("ollama models dedup", 2, ollama.Models.Count);
            AssertEqual("ollama fallback model sorted", "alpha:q4", ollama.Models[0].Id);
            AssertEqual("ollama name preferred", "zeta:latest", ollama.Models[1].Id);
            AssertEqual("ollama duplicate retains discovered family", "gptoss",
                ollama.Models[1].ProviderFamily);

            LlmProtocolModelPageResult malformed = LlmProtocolModelListCodec.ParsePage(
                "{bad", LlmProtocolMode.OllamaChat);
            AssertFalse("model malformed not parsed", malformed.ParsedJsonObject);
            AssertContains("model malformed sanitized", malformed.ProviderError, "malformed JSON");

            LlmProtocolModelPageResult error = LlmProtocolModelListCodec.ParsePage(
                "{\"error\":{\"status\":\"UNAUTHENTICATED\",\"message\":\"bad key\"}}",
                LlmProtocolMode.GeminiGenerateContent);
            AssertContains("model provider error structured", error.ProviderError, "UNAUTHENTICATED");
            AssertContains("model provider error message", error.ProviderError, "bad key");
            LlmProtocolModelPageResult whitespaceError = LlmProtocolModelListCodec.ParsePage(
                "{\"error\":{\"status\":\"BAD\",\"message\":\"one\\n\\t two\"}}",
                LlmProtocolMode.GeminiGenerateContent);
            AssertNotContains("model error newline collapsed", whitespaceError.ProviderError, "\n");
            AssertNotContains("model error tab collapsed", whitespaceError.ProviderError, "\t");
            AssertContains("model error words preserved", whitespaceError.ProviderError, "one two");

            StringBuilder many = new StringBuilder("{\"models\":[");
            for (int i = 0; i < 1001; i++)
            {
                if (i > 0)
                {
                    many.Append(',');
                }
                many.Append("{\"name\":\"m").Append(i).Append("\"}");
            }
            many.Append("]}");
            LlmProtocolModelPageResult limited = LlmProtocolModelListCodec.ParsePage(
                many.ToString(), LlmProtocolMode.OllamaChat);
            AssertEqual("model page hard cap", 1000, limited.Models.Count);
            AssertTrue("model page cap reported", limited.ModelLimitReached);
        }

        private static LlmProtocolRequestInput Request(LlmProtocolMode mode)
        {
            return new LlmProtocolRequestInput
            {
                Mode = mode,
                ModelName = "model\"one",
                SystemPrompt = "  System\nline  ",
                UserText = "User\ttext",
                ReasoningEffort = "high",
                MaxTokens = 123,
                Temperature = 0.7f
            };
        }

        private static string ExistingOpenAIJson(
            LlmProtocolRequestInput input,
            ApiCompatibilityMode mode)
        {
            return LlmRequestJsonBuilder.Build(new LlmRequestJsonInput
            {
                apiMode = mode,
                modelName = input.ModelName,
                systemPrompt = input.SystemPrompt,
                rawText = input.UserText,
                reasoningEffort = input.ReasoningEffort,
                maxTokens = input.MaxTokens,
                temperature = input.Temperature
            });
        }

        private static void AssertContains(string name, string actual, string expectedFragment)
        {
            assertions++;
            if (actual == null || actual.IndexOf(expectedFragment, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(name + " failed. Missing [" + expectedFragment
                    + "] in [" + actual + "].");
            }
        }

        private static void AssertNotContains(string name, string actual, string forbiddenFragment)
        {
            assertions++;
            if (actual != null && actual.IndexOf(forbiddenFragment, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(name + " failed. Found [" + forbiddenFragment
                    + "] in [" + actual + "].");
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

        private static void AssertFalse(string name, bool condition)
        {
            AssertTrue(name, !condition);
        }

        private static void AssertEqual<T>(string name, T expected, T actual)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(name + " failed. Expected [" + expected
                    + "] but got [" + actual + "].");
            }
        }
    }
}

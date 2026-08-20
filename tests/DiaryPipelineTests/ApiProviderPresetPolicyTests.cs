// Focused standalone coverage for provider preset validation, lane creation, provider-mode UI policy,
// and the shipped XML/localization catalog. These tests run without RimWorld/Verse/Unity assemblies.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using PawnDiary;

namespace DiaryPipelineTests
{
    internal static partial class Program
    {
        private static void TestApiProviderPresetPolicy()
        {
            List<ApiProviderPresetSnapshot> fallback =
                ApiProviderPresetPolicy.BuildCatalog(null);
            AssertEqual("provider preset fallback count", 5, fallback.Count);
            AssertEqual("provider preset custom first",
                ApiProviderPresetPolicy.CustomOpenAiPresetKey, fallback[0].presetKey);
            AssertEqual("provider preset responses second",
                ApiProviderPresetPolicy.OpenAiResponsesPresetKey, fallback[1].presetKey);
            AssertEqual("provider preset anthropic third",
                ApiProviderPresetPolicy.AnthropicPresetKey, fallback[2].presetKey);
            AssertEqual("provider preset gemini fourth",
                ApiProviderPresetPolicy.GeminiPresetKey, fallback[3].presetKey);
            AssertEqual("provider preset ollama fifth",
                ApiProviderPresetPolicy.OllamaPresetKey, fallback[4].presetKey);
            AssertEqual("custom preset endpoint", "http://localhost:1234/v1",
                fallback[0].baseUrl);
            AssertEqual("custom preset mode", ApiCompatibilityMode.OpenAIChatCompletions,
                fallback[0].apiMode);
            AssertEqual("custom preset auth", ApiAuthMode.BearerToken, fallback[0].authMode);
            AssertEqual("responses preset endpoint", "https://api.openai.com/v1",
                fallback[1].baseUrl);
            AssertEqual("responses preset mode", ApiCompatibilityMode.OpenAIResponses,
                fallback[1].apiMode);
            AssertEqual("responses preset auth", ApiAuthMode.BearerToken, fallback[1].authMode);

            ApiProviderLaneDefaults anthropic = ApiProviderPresetPolicy.CreateLane(fallback[2]);
            AssertEqual("anthropic preset endpoint", "https://api.anthropic.com", anthropic.url);
            AssertEqual("anthropic preset mode", ApiCompatibilityMode.AnthropicMessages,
                anthropic.apiMode);
            AssertEqual("anthropic preset auth", ApiAuthMode.CustomHeader, anthropic.authMode);
            AssertEqual("anthropic preset header", "x-api-key", anthropic.customAuthHeaderName);
            AssertEqual("preset lane leaves model blank", string.Empty, anthropic.model);
            AssertEqual("preset lane leaves key blank", string.Empty, anthropic.apiKey);
            AssertTrue("preset lane starts enabled", anthropic.enabled);

            ApiProviderAuthDefaults openAiAuth = ApiProviderPresetPolicy.AuthDefaultsForMode(
                ApiCompatibilityMode.OpenAIResponses);
            AssertEqual("OpenAI Responses defaults to bearer auth",
                ApiAuthMode.BearerToken, openAiAuth.authMode);
            AssertEqual("OpenAI Responses clears custom auth header", string.Empty,
                openAiAuth.customAuthHeaderName);
            ApiProviderAuthDefaults anthropicAuth = ApiProviderPresetPolicy.AuthDefaultsForMode(
                ApiCompatibilityMode.AnthropicMessages);
            AssertEqual("Anthropic defaults to custom-header auth",
                ApiAuthMode.CustomHeader, anthropicAuth.authMode);
            AssertEqual("Anthropic defaults to x-api-key", "x-api-key",
                anthropicAuth.customAuthHeaderName);
            ApiProviderAuthDefaults geminiAuth = ApiProviderPresetPolicy.AuthDefaultsForMode(
                ApiCompatibilityMode.GeminiGenerateContent);
            AssertEqual("Gemini defaults to custom-header auth",
                ApiAuthMode.CustomHeader, geminiAuth.authMode);
            AssertEqual("Gemini defaults to x-goog-api-key", "x-goog-api-key",
                geminiAuth.customAuthHeaderName);
            ApiProviderAuthDefaults ollamaAuth = ApiProviderPresetPolicy.AuthDefaultsForMode(
                ApiCompatibilityMode.OllamaChat);
            AssertEqual("Ollama defaults to no auth", ApiAuthMode.None, ollamaAuth.authMode);
            AssertEqual("Ollama clears custom auth header", string.Empty,
                ollamaAuth.customAuthHeaderName);

            ApiProviderPresetSnapshot validCustom = PresetFixture(
                ApiProviderPresetPolicy.CustomOpenAiPresetKey,
                5,
                ApiCompatibilityMode.OpenAIChatCompletions,
                "https://proxy.example/v1",
                ApiAuthMode.BearerToken,
                string.Empty);
            ApiProviderPresetSnapshot duplicateCustom = PresetFixture(
                ApiProviderPresetPolicy.CustomOpenAiPresetKey,
                1,
                ApiCompatibilityMode.OpenAIChatCompletions,
                "https://ignored.example/v1",
                ApiAuthMode.BearerToken,
                string.Empty);
            ApiProviderPresetSnapshot collidingAnthropic = PresetFixture(
                ApiProviderPresetPolicy.AnthropicPresetKey,
                30,
                ApiCompatibilityMode.AnthropicMessages,
                "https://malformed.example",
                ApiAuthMode.CustomHeader,
                "anthropic-version");
            ApiProviderPresetSnapshot malformedGemini = PresetFixture(
                ApiProviderPresetPolicy.GeminiPresetKey,
                40,
                ApiCompatibilityMode.GeminiGenerateContent,
                "not a URL",
                ApiAuthMode.CustomHeader,
                "x-goog-api-key");
            ApiProviderPresetSnapshot userInfoResponses = PresetFixture(
                ApiProviderPresetPolicy.OpenAiResponsesPresetKey,
                20,
                ApiCompatibilityMode.OpenAIResponses,
                "https://user:secret@api.openai.com/v1",
                ApiAuthMode.BearerToken,
                string.Empty);
            ApiProviderPresetSnapshot wrongAnthropicMode = PresetFixture(
                ApiProviderPresetPolicy.AnthropicPresetKey,
                30,
                ApiCompatibilityMode.GeminiGenerateContent,
                "https://wrong-mode.example",
                ApiAuthMode.CustomHeader,
                "x-api-key");
            ApiProviderPresetSnapshot wrongOllamaAuth = PresetFixture(
                ApiProviderPresetPolicy.OllamaPresetKey,
                50,
                ApiCompatibilityMode.OllamaChat,
                "http://localhost:11434",
                ApiAuthMode.BearerToken,
                string.Empty);

            List<ApiProviderPresetSnapshot> repaired = ApiProviderPresetPolicy.BuildCatalog(
                new[]
                {
                    validCustom,
                    duplicateCustom,
                    collidingAnthropic,
                    wrongAnthropicMode,
                    malformedGemini,
                    userInfoResponses,
                    wrongOllamaAuth
                });
            AssertEqual("valid XML endpoint override survives", "https://proxy.example/v1",
                repaired[0].baseUrl);
            AssertEqual("duplicate preset keeps first accepted row", 5, repaired[0].displayOrder);
            ApiProviderPresetSnapshot repairedAnthropic = repaired.Single(
                row => row.presetKey == ApiProviderPresetPolicy.AnthropicPresetKey);
            AssertEqual("fixed-header collision falls back endpoint", "https://api.anthropic.com",
                repairedAnthropic.baseUrl);
            AssertEqual("fixed-header collision falls back recommended header", "x-api-key",
                repairedAnthropic.customAuthHeaderName);
            ApiProviderPresetSnapshot repairedResponses = repaired.Single(
                row => row.presetKey == ApiProviderPresetPolicy.OpenAiResponsesPresetKey);
            AssertEqual("userinfo preset URL falls back", "https://api.openai.com/v1",
                repairedResponses.baseUrl);
            ApiProviderPresetSnapshot repairedGemini = repaired.Single(
                row => row.presetKey == ApiProviderPresetPolicy.GeminiPresetKey);
            AssertEqual("invalid Gemini URL falls back",
                "https://generativelanguage.googleapis.com/v1beta", repairedGemini.baseUrl);
            ApiProviderPresetSnapshot repairedOllama = repaired.Single(
                row => row.presetKey == ApiProviderPresetPolicy.OllamaPresetKey);
            AssertEqual("wrong Ollama auth falls back", ApiAuthMode.None, repairedOllama.authMode);
            List<ApiProviderPresetSnapshot> missingRows =
                ApiProviderPresetPolicy.BuildCatalog(new[] { validCustom });
            AssertEqual("missing Anthropic row falls back", "https://api.anthropic.com",
                missingRows.Single(row =>
                    row.presetKey == ApiProviderPresetPolicy.AnthropicPresetKey).baseUrl);
            List<ApiProviderPresetSnapshot> wrongModeRows =
                ApiProviderPresetPolicy.BuildCatalog(new[] { wrongAnthropicMode });
            AssertEqual("wrong provider mode falls back", "https://api.anthropic.com",
                wrongModeRows.Single(row =>
                    row.presetKey == ApiProviderPresetPolicy.AnthropicPresetKey).baseUrl);

            AssertTrue("OpenAI chat shows generic reasoning",
                ApiProviderPresetPolicy.ModeUi(ApiCompatibilityMode.OpenAIChatCompletions)
                    .showOpenAiReasoningControls);
            AssertTrue("OpenAI responses shows generic reasoning",
                ApiProviderPresetPolicy.ModeUi(ApiCompatibilityMode.OpenAIResponses)
                    .showOpenAiReasoningControls);
            AssertTrue("Anthropic hides generic reasoning",
                !ApiProviderPresetPolicy.ModeUi(ApiCompatibilityMode.AnthropicMessages)
                    .showOpenAiReasoningControls);
            AssertTrue("Anthropic omits temperature",
                ApiProviderPresetPolicy.ModeUi(ApiCompatibilityMode.AnthropicMessages)
                    .temperatureBehavior == ApiTemperatureBehavior.Omitted);
            AssertTrue("Gemini temperature is capability conditional",
                ApiProviderPresetPolicy.ModeUi(ApiCompatibilityMode.GeminiGenerateContent)
                    .temperatureBehavior == ApiTemperatureBehavior.ConditionalModelCapability);
            AssertTrue("Ollama sends temperature",
                ApiProviderPresetPolicy.ModeUi(ApiCompatibilityMode.OllamaChat)
                    .temperatureBehavior == ApiTemperatureBehavior.Sent);
            AssertTrue("all native modes hide generic reasoning",
                !ApiProviderPresetPolicy.ModeUi(ApiCompatibilityMode.GeminiGenerateContent)
                    .showOpenAiReasoningControls
                && !ApiProviderPresetPolicy.ModeUi(ApiCompatibilityMode.OllamaChat)
                    .showOpenAiReasoningControls);

            TestApiProviderPresetXmlContract();
        }

        private static void TestApiProviderPresetXmlContract()
        {
            XDocument defs = XDocument.Load(RepoPath(
                "1.6", "Defs", "DiaryApiProviderPresetDefs.xml"));
            List<XElement> rows = defs.Root
                .Elements("PawnDiary.DiaryApiProviderPresetDef")
                .ToList();
            AssertEqual("provider preset XML row count", 5, rows.Count);
            AssertEqual("provider preset XML unique def names", 5,
                rows.Select(row => row.Element("defName")?.Value).Distinct().Count());
            AssertTrue("provider preset XML never owns volatile model/key",
                rows.All(row => row.Element("model") == null && row.Element("apiKey") == null));

            List<ApiProviderPresetSnapshot> xmlSnapshots = rows.Select(row => PresetFixture(
                row.Element("defName")?.Value,
                int.Parse(row.Element("displayOrder")?.Value ?? "0"),
                (ApiCompatibilityMode)Enum.Parse(
                    typeof(ApiCompatibilityMode), row.Element("apiMode")?.Value ?? string.Empty),
                row.Element("baseUrl")?.Value,
                (ApiAuthMode)Enum.Parse(
                    typeof(ApiAuthMode), row.Element("authMode")?.Value ?? string.Empty),
                row.Element("customAuthHeaderName")?.Value)).ToList();
            List<ApiProviderPresetSnapshot> catalog =
                ApiProviderPresetPolicy.BuildCatalog(xmlSnapshots);
            AssertEqual("XML catalog preserves five validated rows", 5, catalog.Count);
            AssertEqual("XML catalog Anthropic header", "x-api-key",
                catalog.Single(row => row.presetKey == ApiProviderPresetPolicy.AnthropicPresetKey)
                    .customAuthHeaderName);
            AssertEqual("XML catalog Gemini header", "x-goog-api-key",
                catalog.Single(row => row.presetKey == ApiProviderPresetPolicy.GeminiPresetKey)
                    .customAuthHeaderName);
            AssertEqual("XML catalog Ollama has no auth", ApiAuthMode.None,
                catalog.Single(row => row.presetKey == ApiProviderPresetPolicy.OllamaPresetKey)
                    .authMode);

            XDocument englishDefs = XDocument.Load(RepoPath(
                "Languages", "English", "DefInjected",
                "PawnDiary.DiaryApiProviderPresetDef", "DiaryApiProviderPresetDefs.xml"));
            XDocument russianDefs = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "DefInjected",
                "PawnDiary.DiaryApiProviderPresetDef", "DiaryApiProviderPresetDefs.xml"));
            XDocument englishKeyed = XDocument.Load(RepoPath(
                "Languages", "English", "Keyed", "PawnDiary.xml"));
            XDocument russianKeyed = XDocument.Load(RepoPath(
                "Languages", "Russian (Русский)", "Keyed", "PawnDiary.xml"));
            AssertEqual("English provider DefInjected key parity", 10,
                englishDefs.Root.Elements().Count());
            AssertEqual("Russian provider DefInjected key parity", 10,
                russianDefs.Root.Elements().Count());

            foreach (XElement row in rows)
            {
                string key = row.Element("defName")?.Value ?? string.Empty;
                string label = row.Element("label")?.Value ?? string.Empty;
                string description = row.Element("description")?.Value ?? string.Empty;
                AssertEqual("English preset label mirrors base " + key, label,
                    englishDefs.Root?.Element(key + ".label")?.Value);
                AssertEqual("English preset description mirrors base " + key, description,
                    englishDefs.Root?.Element(key + ".description")?.Value);
                AssertTrue("Russian preset label exists " + key,
                    !string.IsNullOrWhiteSpace(russianDefs.Root?.Element(key + ".label")?.Value));
                AssertTrue("Russian preset description exists " + key,
                    !string.IsNullOrWhiteSpace(russianDefs.Root?.Element(key + ".description")?.Value));
            }

            string[] fallbackKeySuffixes =
            {
                "CustomOpenAi", "OpenAiResponses", "Anthropic", "Gemini", "Ollama"
            };
            for (int i = 0; i < fallbackKeySuffixes.Length; i++)
            {
                string labelKey = "PawnDiary.Settings.ApiPreset." + fallbackKeySuffixes[i];
                AssertTrue("English fallback preset label exists " + labelKey,
                    !string.IsNullOrWhiteSpace(englishKeyed.Root?.Element(labelKey)?.Value));
                AssertTrue("English fallback preset description exists " + labelKey,
                    !string.IsNullOrWhiteSpace(
                        englishKeyed.Root?.Element(labelKey + ".Description")?.Value));
                AssertTrue("Russian fallback preset label exists " + labelKey,
                    !string.IsNullOrWhiteSpace(russianKeyed.Root?.Element(labelKey)?.Value));
                AssertTrue("Russian fallback preset description exists " + labelKey,
                    !string.IsNullOrWhiteSpace(
                        russianKeyed.Root?.Element(labelKey + ".Description")?.Value));
            }

            string anthropicHelp = rows.Single(row =>
                row.Element("defName")?.Value == ApiProviderPresetPolicy.AnthropicPresetKey)
                .Element("description")?.Value ?? string.Empty;
            string geminiHelp = rows.Single(row =>
                row.Element("defName")?.Value == ApiProviderPresetPolicy.GeminiPresetKey)
                .Element("description")?.Value ?? string.Empty;
            string ollamaHelp = rows.Single(row =>
                row.Element("defName")?.Value == ApiProviderPresetPolicy.OllamaPresetKey)
                .Element("description")?.Value ?? string.Empty;
            AssertContains("Anthropic help explains omitted temperature", anthropicHelp,
                "omits the global temperature");
            AssertContains("Gemini help explains conditional temperature", geminiHelp,
                "only after model discovery");
            AssertContains("Gemini help explains automatic thinking", geminiHelp,
                "thinking is managed automatically");
            AssertContains("Ollama help explains configured output cap", ollamaHelp,
                "configured output-token cap");
            AssertContains("Ollama help explains automatic thinking", ollamaHelp,
                "thinking is managed automatically");
        }

        private static ApiProviderPresetSnapshot PresetFixture(
            string key,
            int order,
            ApiCompatibilityMode mode,
            string url,
            ApiAuthMode authMode,
            string customHeader)
        {
            return new ApiProviderPresetSnapshot
            {
                presetKey = key,
                displayOrder = order,
                apiMode = mode,
                baseUrl = url,
                authMode = authMode,
                customAuthHeaderName = customHeader
            };
        }
    }
}

// Standalone scripted-HTTP regression tests for Pawn Diary's two impure provider adapters.
//
// The delegates below return in-memory HttpResponseMessage objects; no socket, provider, RimWorld
// process, save, Steam state, or API credential is touched. The fixture calls the real compiled
// LlmClient.TestConnection and ModelListClient entry points so URL/header/body wiring cannot drift
// away from the pure protocol codecs unnoticed.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PawnDiary;

namespace LlmProtocolHttpFixtureTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            try
            {
                Run().GetAwaiter().GetResult();
                Console.WriteLine(
                    "LlmProtocolHttpFixtureTests passed " + assertions + " assertions.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
            finally
            {
                LlmClient.SendAsyncOverrideForTests = null;
                ModelListClient.SendAsyncOverrideForTests = null;
            }
        }

        private static async Task Run()
        {
            await TestGenerationPhysicalExchange();
            await TestGenerationNativeErrorRedactionAndCollision();
            await TestSingleCompletionRetryCapAndCancellation();
            await TestOpenAiModelDiscoveryCompatibility();
            await TestAnthropicModelPaginationAndHeaders();
            await TestGeminiModelPaginationAndCapabilities();
            await TestOllamaModelDiscovery();
            await TestModelDiscoveryGuardsAndRedaction();
            TestCapabilityCacheIdentityAndSecretExclusion();
            TestConnectionSignatureIncludesProtocolMode();
            TestNativeFinishMetadataPolicy();
        }

        private static async Task TestGenerationPhysicalExchange()
        {
            const string prompt = "Connection probe";
            const float temperature = 0.7f;

            ScriptedExchange openAi = new ScriptedExchange(Response(
                HttpStatusCode.OK,
                "{\"choices\":[{\"message\":{\"content\":\"OpenAI OK\"}}]}"));
            ApiEndpointConfig openAiEndpoint = Endpoint(
                "https://api.example.test/v1/chat/completions?tenant=one",
                "openai-secret",
                "gpt-test",
                ApiCompatibilityMode.OpenAIChatCompletions,
                ApiAuthMode.BearerToken,
                string.Empty);
            // Provider metadata must never alter the established OpenAI serializer bytes.
            ModelCapabilityCache.Update(
                openAiEndpoint.url,
                openAiEndpoint.apiMode,
                openAiEndpoint.model,
                new ModelProtocolCapability(null, 1, 0.1d));
            string openAiText = await SendConnectionTest(openAiEndpoint, prompt, temperature, openAi);
            AssertEqual("OpenAI generation text", "OpenAI OK", openAiText);
            AssertEqual("OpenAI generation sends once", 1, openAi.Requests.Count);
            CapturedRequest openAiRequest = openAi.Requests[0];
            AssertEqual("OpenAI generation method", "POST", openAiRequest.Method);
            AssertEqual(
                "OpenAI generation URL",
                "https://api.example.test/v1/chat/completions?tenant=one",
                openAiRequest.Url);
            AssertEqual("OpenAI bearer header", "Bearer openai-secret", Header(openAiRequest, "Authorization"));
            AssertFalse("OpenAI has no Anthropic version", openAiRequest.Headers.ContainsKey("anthropic-version"));
            AssertFalse("OpenAI has no Gemini key header", openAiRequest.Headers.ContainsKey("x-goog-api-key"));
            AssertEqual(
                "OpenAI body remains byte-compatible with legacy serializer",
                LlmRequestJsonBuilder.Build(new LlmRequestJsonInput
                {
                    apiMode = ApiCompatibilityMode.OpenAIChatCompletions,
                    modelName = "gpt-test",
                    systemPrompt = string.Empty,
                    rawText = prompt,
                    reasoningEffort = ApiEndpointPolicy.DefaultReasoningEffort,
                    maxTokens = 32,
                    temperature = temperature
                }),
                openAiRequest.Body);

            ScriptedExchange anthropic = new ScriptedExchange(Response(
                HttpStatusCode.OK,
                "{\"content\":[{\"type\":\"text\",\"text\":\"Anthropic OK\"}],"
                    + "\"stop_reason\":\"end_turn\"}"));
            ApiEndpointConfig anthropicEndpoint = Endpoint(
                "https://api.anthropic.com/v1/models?tenant=one",
                "anthropic-secret",
                "claude-test",
                ApiCompatibilityMode.AnthropicMessages,
                ApiAuthMode.CustomHeader,
                "x-api-key");
            string anthropicText = await SendConnectionTest(
                anthropicEndpoint,
                prompt,
                temperature,
                anthropic);
            AssertEqual("Anthropic normal stop is successful", "Anthropic OK", anthropicText);
            CapturedRequest anthropicRequest = anthropic.Requests[0];
            AssertEqual(
                "Anthropic generation URL",
                "https://api.anthropic.com/v1/messages?tenant=one",
                anthropicRequest.Url);
            AssertEqual("Anthropic version header", "2023-06-01", Header(anthropicRequest, "anthropic-version"));
            AssertEqual("Anthropic key header", "anthropic-secret", Header(anthropicRequest, "x-api-key"));
            AssertEqual(
                "Anthropic generation body",
                NativeBody(
                    LlmProtocolMode.AnthropicMessages,
                    "claude-test",
                    prompt,
                    temperature),
                anthropicRequest.Body);

            ScriptedExchange gemini = new ScriptedExchange(Response(
                HttpStatusCode.OK,
                "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Gemini OK\"}]},"
                    + "\"finishReason\":\"STOP\"}]}"));
            ApiEndpointConfig geminiEndpoint = Endpoint(
                "https://generativelanguage.googleapis.com/v1beta/models",
                "gemini-secret",
                "models/gemini-test",
                ApiCompatibilityMode.GeminiGenerateContent,
                ApiAuthMode.CustomHeader,
                "x-goog-api-key");
            // Exercise the runtime cache-to-request binding, not only the pure serializer: the
            // connection probe's fixed 32-token budget and temperature must clamp to discovery data.
            ModelCapabilityCache.Update(
                geminiEndpoint.url,
                geminiEndpoint.apiMode,
                geminiEndpoint.model,
                new ModelProtocolCapability(null, 8, 0.3d));
            string geminiText = await SendConnectionTest(geminiEndpoint, prompt, temperature, gemini);
            AssertEqual("Gemini normal finish is successful", "Gemini OK", geminiText);
            CapturedRequest geminiRequest = gemini.Requests[0];
            AssertEqual(
                "Gemini generation URL canonicalizes model prefix",
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-test:generateContent",
                geminiRequest.Url);
            AssertEqual("Gemini key header", "gemini-secret", Header(geminiRequest, "x-goog-api-key"));
            AssertFalse("Gemini has no bearer", geminiRequest.Headers.ContainsKey("Authorization"));
            AssertEqual(
                "Gemini generation body",
                NativeBody(
                    LlmProtocolMode.GeminiGenerateContent,
                    "models/gemini-test",
                    prompt,
                    temperature,
                    8,
                    0.3d),
                geminiRequest.Body);

            ScriptedExchange ollama = new ScriptedExchange(Response(
                HttpStatusCode.OK,
                "{\"message\":{\"role\":\"assistant\",\"content\":\"Ollama OK\"},"
                    + "\"done\":true,\"done_reason\":\"stop\"}"));
            ApiEndpointConfig ollamaEndpoint = Endpoint(
                "http://localhost:11434/api/tags",
                "stale-unused-secret",
                "llama-test:latest",
                ApiCompatibilityMode.OllamaChat,
                ApiAuthMode.None,
                string.Empty);
            string ollamaText = await SendConnectionTest(ollamaEndpoint, prompt, temperature, ollama);
            AssertEqual("Ollama normal done reason is successful", "Ollama OK", ollamaText);
            CapturedRequest ollamaRequest = ollama.Requests[0];
            AssertEqual("Ollama generation URL", "http://localhost:11434/api/chat", ollamaRequest.Url);
            AssertFalse("Ollama sends no auth", ollamaRequest.Headers.ContainsKey("Authorization"));
            AssertEqual(
                "Ollama generation body",
                NativeBody(
                    LlmProtocolMode.OllamaChat,
                    "llama-test:latest",
                    prompt,
                    temperature),
                ollamaRequest.Body);
        }

        private static async Task TestGenerationNativeErrorRedactionAndCollision()
        {
            const string secret = "native-generation-secret";
            ScriptedExchange errorExchange = new ScriptedExchange(Response(
                HttpStatusCode.BadRequest,
                "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\","
                    + "\"message\":\"denied " + secret + "\"},"
                    + "\"untrusted\":\"RAW_NATIVE_BODY_MUST_NOT_LEAK\"}"));
            ApiEndpointConfig endpoint = Endpoint(
                "https://api.anthropic.com/v1",
                secret,
                "claude-error",
                ApiCompatibilityMode.AnthropicMessages,
                ApiAuthMode.CustomHeader,
                "x-api-key");
            InvalidOperationException error = await ExpectThrowsAsync<InvalidOperationException>(
                "native generation HTTP error",
                () => SendConnectionTest(endpoint, "probe", 0.2f, errorExchange));
            AssertNotContains("native generation error redacts exact key", error.Message, secret);
            AssertContains("native generation error shows redaction marker", error.Message, "<redacted>");
            AssertNotContains(
                "native generation error excludes unstructured raw body",
                error.Message,
                "RAW_NATIVE_BODY_MUST_NOT_LEAK");

            int sends = 0;
            LlmClient.SendAsyncOverrideForTests = (request, option, token) =>
            {
                sends++;
                return Task.FromResult(Response(HttpStatusCode.OK, "{}"));
            };
            ApiEndpointConfig collision = Endpoint(
                "https://api.anthropic.com/v1",
                "must-never-be-attached",
                "claude-collision",
                ApiCompatibilityMode.AnthropicMessages,
                ApiAuthMode.CustomHeader,
                "ANTHROPIC-VERSION");
            InvalidOperationException collisionError = await ExpectThrowsAsync<InvalidOperationException>(
                "generation fixed-header collision",
                () => LlmClient.TestConnection(collision, "probe", 30, 0.2f));
            AssertEqual("generation collision preflights before send", 0, sends);
            AssertNotContains(
                "generation collision error excludes secret",
                collisionError.Message,
                "must-never-be-attached");
            LlmClient.SendAsyncOverrideForTests = null;
        }

        private static async Task TestSingleCompletionRetryCapAndCancellation()
        {
            ApiEndpointConfig endpoint = Endpoint(
                "https://api.anthropic.com/v1",
                "retry-secret",
                "claude-retry",
                ApiCompatibilityMode.AnthropicMessages,
                ApiAuthMode.CustomHeader,
                "x-api-key");
            LlmClient.BeginSession();
            try
            {
                ScriptedExchange retry = new ScriptedExchange(
                    Response(
                        HttpStatusCode.Conflict,
                        "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\","
                            + "\"message\":\"try again\"}}"),
                    Response(
                        HttpStatusCode.OK,
                        "{\"content\":[{\"type\":\"text\",\"text\":\"Retry succeeded.\"}],"
                            + "\"stop_reason\":\"end_turn\"}"));
                LlmClient.SendAsyncOverrideForTests = retry.SendAsync;
                string retriedText = await LlmClient.SendSingleCompletion(
                    endpoint,
                    "System fixture",
                    "Retry fixture",
                    64,
                    10,
                    0.4f,
                    2,
                    0.01d,
                    CancellationToken.None);
                AssertEqual("native transient retry eventually succeeds", "Retry succeeded.", retriedText);
                AssertEqual("native transient retry uses two physical sends", 2, retry.Requests.Count);
                AssertEqual("native retry URL stays stable", retry.Requests[0].Url, retry.Requests[1].Url);

                string oversized = new string('x', (1024 * 1024) + 1);
                ScriptedExchange responseCap = new ScriptedExchange(Response(HttpStatusCode.OK, oversized));
                LlmClient.SendAsyncOverrideForTests = responseCap.SendAsync;
                Exception capError = await ExpectThrowsAsync<Exception>(
                    "generation response byte cap",
                    () => LlmClient.SendSingleCompletion(
                        endpoint,
                        string.Empty,
                        "Cap fixture",
                        64,
                        10,
                        0.4f,
                        1,
                        0.01d,
                        CancellationToken.None));
                AssertContains("generation response cap diagnostic", capError.Message, "too large");
                AssertEqual("generation response cap stops after one send", 1, responseCap.Requests.Count);

                int cancellationSends = 0;
                LlmClient.SendAsyncOverrideForTests = async (request, option, cancellationToken) =>
                {
                    cancellationSends++;
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return Response(HttpStatusCode.OK, "{}");
                };
                using (CancellationTokenSource callerCancellation = new CancellationTokenSource())
                {
                    callerCancellation.CancelAfter(25);
                    await ExpectThrowsAsync<OperationCanceledException>(
                        "single completion caller cancellation",
                        () => LlmClient.SendSingleCompletion(
                            endpoint,
                            string.Empty,
                            "Cancellation fixture",
                            64,
                            10,
                            0.4f,
                            callerCancellation.Token));
                }
                AssertEqual("caller cancellation reached one physical send", 1, cancellationSends);

                ScriptedExchange afterCancellation = new ScriptedExchange(Response(
                    HttpStatusCode.OK,
                    "{\"content\":[{\"type\":\"text\",\"text\":\"Still ready.\"}],"
                        + "\"stop_reason\":\"end_turn\"}"));
                LlmClient.SendAsyncOverrideForTests = afterCancellation.SendAsync;
                string afterCancellationText = await LlmClient.SendSingleCompletion(
                    endpoint,
                    string.Empty,
                    "After cancellation fixture",
                    64,
                    10,
                    0.4f,
                    CancellationToken.None);
                AssertEqual(
                    "external cancellation does not poison lane cooldown",
                    "Still ready.",
                    afterCancellationText);
                AssertEqual("post-cancellation request sends immediately", 1, afterCancellation.Requests.Count);
            }
            finally
            {
                LlmClient.SendAsyncOverrideForTests = null;
                LlmClient.EndSession();
            }
        }

        private static async Task TestOpenAiModelDiscoveryCompatibility()
        {
            ScriptedExchange exchange = new ScriptedExchange(Response(
                HttpStatusCode.OK,
                "{\"data\":["
                    + "{\"id\":\"z-model\"},"
                    + "{\"id\":\"a-model\",\"reasoning\":{\"default_enabled\":true,"
                    + "\"supported_efforts\":[\"low\",\"high\"],\"default_effort\":\"low\"}},"
                    + "{\"id\":\"a-model\"}]}"));
            ModelListResult result = await FetchModels(
                "https://api.example.test/v1/chat/completions?tenant=one",
                "openai-list-secret",
                ApiAuthMode.BearerToken,
                string.Empty,
                ApiCompatibilityMode.OpenAIChatCompletions,
                exchange);
            AssertSequence("OpenAI models distinct and sorted", result.Models, "a-model", "z-model");
            AssertTrue("OpenAI advertised reasoning retained", result.Capabilities.ContainsKey("a-model"));
            AssertTrue("OpenAI protocol map includes reasoning row", result.ProtocolCapabilities.ContainsKey("a-model"));
            AssertFalse("OpenAI empty metadata row stays uncached", result.ProtocolCapabilities.ContainsKey("z-model"));
            AssertEqual("OpenAI model fetch sends once", 1, exchange.Requests.Count);
            CapturedRequest request = exchange.Requests[0];
            AssertEqual(
                "OpenAI model URL remains compatible",
                "https://api.example.test/v1/models?tenant=one",
                request.Url);
            AssertEqual("OpenAI model bearer", "Bearer openai-list-secret", Header(request, "Authorization"));
            AssertFalse("OpenAI model request has no native header", request.Headers.ContainsKey("anthropic-version"));
        }

        private static async Task TestAnthropicModelPaginationAndHeaders()
        {
            ScriptedExchange exchange = new ScriptedExchange(
                Response(
                    HttpStatusCode.OK,
                    "{\"data\":[{\"id\":\"claude-b\",\"max_tokens\":4096}],"
                        + "\"has_more\":true,\"last_id\":\"cursor/one\"}"),
                Response(
                    HttpStatusCode.OK,
                    "{\"data\":[{\"id\":\"claude-a\"}],\"has_more\":false}"));
            ModelListResult result = await FetchModels(
                "https://api.anthropic.com/v1/messages?tenant=one",
                "anthropic-list-secret",
                ApiAuthMode.CustomHeader,
                "x-api-key",
                ApiCompatibilityMode.AnthropicMessages,
                exchange);
            AssertSequence("Anthropic pages aggregate", result.Models, "claude-a", "claude-b");
            AssertEqual("Anthropic model page count", 2, exchange.Requests.Count);
            AssertEqual(
                "Anthropic first model URL",
                "https://api.anthropic.com/v1/models?tenant=one",
                exchange.Requests[0].Url);
            AssertContains(
                "Anthropic cursor URL",
                exchange.Requests[1].Url,
                "after_id=cursor%2Fone");
            for (int i = 0; i < exchange.Requests.Count; i++)
            {
                AssertEqual(
                    "Anthropic version on page " + i,
                    "2023-06-01",
                    Header(exchange.Requests[i], "anthropic-version"));
                AssertEqual(
                    "Anthropic key on page " + i,
                    "anthropic-list-secret",
                    Header(exchange.Requests[i], "x-api-key"));
            }
            AssertEqual(
                "Anthropic output limit retained",
                4096,
                result.ProtocolCapabilities["claude-b"].MaxOutputTokens);
            AssertTrue(
                "Anthropic unknown metadata row cached",
                result.ProtocolCapabilities.ContainsKey("claude-a"));
        }

        private static async Task TestGeminiModelPaginationAndCapabilities()
        {
            ScriptedExchange exchange = new ScriptedExchange(
                Response(
                    HttpStatusCode.OK,
                    "{\"models\":["
                        + "{\"name\":\"models/gemini-pro\",\"supportedGenerationMethods\":[\"generateContent\"],"
                        + "\"outputTokenLimit\":8192,\"maxTemperature\":2},"
                        + "{\"name\":\"models/embed-only\",\"supportedGenerationMethods\":[\"embedContent\"]}],"
                        + "\"nextPageToken\":\"next page\"}"),
                Response(
                    HttpStatusCode.OK,
                    "{\"models\":[{\"baseModelId\":\"models/gemini-flash\","
                        + "\"supportedGenerationMethods\":[\"generateContent\"],"
                        + "\"outputTokenLimit\":2048,\"maxTemperature\":1.5}]}"));
            ModelListResult result = await FetchModels(
                "https://generativelanguage.googleapis.com/v1beta/models?tenant=one",
                "gemini-list-secret",
                ApiAuthMode.CustomHeader,
                "x-goog-api-key",
                ApiCompatibilityMode.GeminiGenerateContent,
                exchange);
            AssertSequence("Gemini filters and canonicalizes models", result.Models, "gemini-flash", "gemini-pro");
            AssertEqual("Gemini page count", 2, exchange.Requests.Count);
            AssertContains("Gemini page token", exchange.Requests[1].Url, "pageToken=next%20page");
            AssertEqual("Gemini key page one", "gemini-list-secret", Header(exchange.Requests[0], "x-goog-api-key"));
            AssertEqual("Gemini key page two", "gemini-list-secret", Header(exchange.Requests[1], "x-goog-api-key"));
            AssertFalse("Gemini embed-only model filtered", result.Models.Contains("embed-only"));
            AssertEqual(
                "Gemini output limit retained",
                8192,
                result.ProtocolCapabilities["gemini-pro"].MaxOutputTokens);
            AssertEqual(
                "Gemini temperature limit retained",
                2d,
                result.ProtocolCapabilities["gemini-pro"].MaxTemperature.Value);
        }

        private static async Task TestOllamaModelDiscovery()
        {
            ScriptedExchange exchange = new ScriptedExchange(Response(
                HttpStatusCode.OK,
                "{\"models\":[{\"name\":\"llama3:latest\"},{\"model\":\"qwen:7b\"}]}"));
            ModelListResult result = await FetchModels(
                "http://localhost:11434/api/chat",
                "stale-unused-key",
                ApiAuthMode.None,
                string.Empty,
                ApiCompatibilityMode.OllamaChat,
                exchange);
            AssertSequence("Ollama name/model fallback", result.Models, "llama3:latest", "qwen:7b");
            AssertEqual("Ollama tags URL", "http://localhost:11434/api/tags", exchange.Requests[0].Url);
            AssertFalse("Ollama model fetch sends no auth", exchange.Requests[0].Headers.ContainsKey("Authorization"));
            AssertTrue("Ollama empty metadata cached", result.ProtocolCapabilities.ContainsKey("llama3:latest"));
        }

        private static async Task TestModelDiscoveryGuardsAndRedaction()
        {
            int collisionSends = 0;
            ModelListClient.SendAsyncOverrideForTests = (request, option, token) =>
            {
                collisionSends++;
                return Task.FromResult(Response(HttpStatusCode.OK, "{}"));
            };
            InvalidOperationException collision = await ExpectThrowsAsync<InvalidOperationException>(
                "model fixed-header collision",
                () => ModelListClient.FetchModels(
                    "https://api.anthropic.com/v1",
                    "collision-secret",
                    ApiAuthMode.CustomHeader,
                    "anthropic-version",
                    ApiCompatibilityMode.AnthropicMessages,
                    30));
            AssertEqual("model collision preflights before send", 0, collisionSends);
            AssertNotContains("model collision excludes secret", collision.Message, "collision-secret");
            ModelListClient.SendAsyncOverrideForTests = null;

            const string errorSecret = "model-native-secret";
            ScriptedExchange nativeError = new ScriptedExchange(Response(
                HttpStatusCode.BadRequest,
                "{\"error\":{\"message\":\"denied " + errorSecret + "\"},"
                    + "\"untrusted\":\"RAW_MODEL_BODY_MUST_NOT_LEAK\"}"));
            InvalidOperationException error = await ExpectThrowsAsync<InvalidOperationException>(
                "native model error",
                () => FetchModels(
                    "https://generativelanguage.googleapis.com/v1beta",
                    errorSecret,
                    ApiAuthMode.CustomHeader,
                    "x-goog-api-key",
                    ApiCompatibilityMode.GeminiGenerateContent,
                    nativeError));
            AssertNotContains("native model error redacts key", error.Message, errorSecret);
            AssertContains("native model error redaction marker", error.Message, "<redacted>");
            AssertNotContains("native model error excludes raw body field", error.Message, "RAW_MODEL_BODY_MUST_NOT_LEAK");

            ScriptedExchange repeatedCursor = new ScriptedExchange(
                Response(HttpStatusCode.OK, "{\"data\":[],\"has_more\":true,\"last_id\":\"repeat\"}"),
                Response(HttpStatusCode.OK, "{\"data\":[],\"has_more\":true,\"last_id\":\"repeat\"}"));
            InvalidOperationException repeated = await ExpectThrowsAsync<InvalidOperationException>(
                "repeated model cursor",
                () => FetchModels(
                    "https://api.anthropic.com/v1",
                    string.Empty,
                    ApiAuthMode.None,
                    string.Empty,
                    ApiCompatibilityMode.AnthropicMessages,
                    repeatedCursor));
            AssertContains("repeated cursor diagnostic", repeated.Message, "repeated a pagination cursor");
            AssertEqual("repeated cursor stops after two pages", 2, repeatedCursor.Requests.Count);

            string oversized = new string(' ', (1024 * 512) + 1);
            ScriptedExchange oversizedExchange = new ScriptedExchange(Response(HttpStatusCode.OK, oversized));
            InvalidOperationException tooLarge = await ExpectThrowsAsync<InvalidOperationException>(
                "cumulative model bytes",
                () => FetchModels(
                    "http://localhost:11434/api",
                    string.Empty,
                    ApiAuthMode.None,
                    string.Empty,
                    ApiCompatibilityMode.OllamaChat,
                    oversizedExchange));
            AssertContains("model byte cap diagnostic", tooLarge.Message, "too large");

            string firstPadding = new string('a', 300 * 1024);
            string secondPadding = new string('b', 300 * 1024);
            ScriptedExchange cumulative = new ScriptedExchange(
                Response(
                    HttpStatusCode.OK,
                    "{\"data\":[],\"has_more\":true,\"last_id\":\"second\",\"padding\":\""
                        + firstPadding + "\"}"),
                Response(
                    HttpStatusCode.OK,
                    "{\"data\":[],\"has_more\":false,\"padding\":\""
                        + secondPadding + "\"}"));
            InvalidOperationException cumulativeTooLarge = await ExpectThrowsAsync<InvalidOperationException>(
                "cumulative paginated model bytes",
                () => FetchModels(
                    "https://api.anthropic.com/v1",
                    string.Empty,
                    ApiAuthMode.None,
                    string.Empty,
                    ApiCompatibilityMode.AnthropicMessages,
                    cumulative));
            AssertContains("cumulative byte cap diagnostic", cumulativeTooLarge.Message, "too large");
            AssertEqual("cumulative byte cap reads two bounded pages", 2, cumulative.Requests.Count);

            List<HttpResponseMessage> pageLimitResponses = new List<HttpResponseMessage>();
            for (int i = 1; i <= 20; i++)
            {
                pageLimitResponses.Add(Response(
                    HttpStatusCode.OK,
                    "{\"data\":[],\"has_more\":true,\"last_id\":\"cursor-" + i + "\"}"));
            }
            ScriptedExchange pageLimit = new ScriptedExchange(pageLimitResponses.ToArray());
            InvalidOperationException tooManyPages = await ExpectThrowsAsync<InvalidOperationException>(
                "model pagination page cap",
                () => FetchModels(
                    "https://api.anthropic.com/v1",
                    string.Empty,
                    ApiAuthMode.None,
                    string.Empty,
                    ApiCompatibilityMode.AnthropicMessages,
                    pageLimit));
            AssertContains("model page cap diagnostic", tooManyPages.Message, "too many pagination pages");
            AssertEqual("model page cap sends no twenty-first request", 20, pageLimit.Requests.Count);

            ScriptedExchange queryReplacement = new ScriptedExchange(Response(
                HttpStatusCode.OK,
                "{\"data\":[]}"));
            await FetchModels(
                "https://api.example.test/v1?KEY=old-secret&tenant=one",
                "new-secret",
                ApiAuthMode.QueryParameterKey,
                string.Empty,
                ApiCompatibilityMode.OpenAIChatCompletions,
                queryReplacement);
            string queryUrl = queryReplacement.Requests[0].Url;
            AssertNotContains("query auth replaces differently-cased old key", queryUrl, "old-secret");
            AssertContains("query auth carries new key", queryUrl, "key=new-secret");
            AssertEqual("query auth leaves one case-insensitive key", 1, CountQueryName(queryUrl, "key"));
        }

        private static void TestCapabilityCacheIdentityAndSecretExclusion()
        {
            ModelProtocolCapability geminiCapability = new ModelProtocolCapability(null, 777, 1.25d);
            string baseEndpoint =
                "https://generativelanguage.googleapis.com/v1beta?tenant=alpha&x-goog-api-key=first-secret";
            ModelCapabilityCache.Update(
                baseEndpoint,
                ApiCompatibilityMode.GeminiGenerateContent,
                "models/gemini-cache",
                geminiCapability);
            ModelProtocolCapability same = ModelCapabilityCache.Get(
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-cache:generateContent"
                    + "?tenant=alpha&x-goog-api-key=rotated-secret",
                ApiCompatibilityMode.GeminiGenerateContent,
                "gemini-cache");
            AssertTrue("Gemini cache canonicalizes URL/model and excludes query credential", same != null);
            AssertEqual("Gemini cached output limit", 777, same.MaxOutputTokens);
            AssertTrue(
                "cache preserves nonsecret tenant identity",
                ModelCapabilityCache.Get(
                    "https://generativelanguage.googleapis.com/v1beta?tenant=beta&x-goog-api-key=rotated-secret",
                    ApiCompatibilityMode.GeminiGenerateContent,
                    "gemini-cache") == null);
            AssertTrue(
                "cache isolates protocol mode",
                ModelCapabilityCache.Get(
                    baseEndpoint,
                    ApiCompatibilityMode.AnthropicMessages,
                    "models/gemini-cache") == null);

            ModelProtocolCapability rawModel = new ModelProtocolCapability(null, 333, null);
            string openAiEndpoint = "https://api.example.test/v1?access_token=first";
            ModelCapabilityCache.Update(
                openAiEndpoint,
                ApiCompatibilityMode.OpenAIChatCompletions,
                " raw-model ",
                rawModel);
            AssertTrue(
                "OpenAI cache excludes token query value",
                ModelCapabilityCache.Get(
                    "https://api.example.test/v1?access_token=second",
                    ApiCompatibilityMode.OpenAIChatCompletions,
                    " raw-model ") != null);
            AssertTrue(
                "OpenAI cache preserves raw model text",
                ModelCapabilityCache.Get(
                    openAiEndpoint,
                    ApiCompatibilityMode.OpenAIChatCompletions,
                    "raw-model") == null);
        }

        private static void TestNativeFinishMetadataPolicy()
        {
            AssertNativeFinishSuccess(
                "Anthropic normal stop policy",
                LlmProtocolResponseCodec.ParseGeneration(
                    "{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"stop_reason\":\"end_turn\"}",
                    LlmProtocolMode.AnthropicMessages,
                    200));
            AssertNativeFinishSuccess(
                "Gemini normal finish policy",
                LlmProtocolResponseCodec.ParseGeneration(
                    "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"ok\"}]},\"finishReason\":\"STOP\"}]}",
                    LlmProtocolMode.GeminiGenerateContent,
                    200));
            AssertNativeFinishSuccess(
                "Ollama normal done policy",
                LlmProtocolResponseCodec.ParseGeneration(
                    "{\"message\":{\"content\":\"ok\"},\"done_reason\":\"stop\"}",
                    LlmProtocolMode.OllamaChat,
                    200));

            LlmProtocolParseResult noText = new LlmProtocolParseResult
            {
                ParsedJsonObject = true,
                FinishReason = "SAFETY",
                FinishMessage = "bounded explanation"
            };
            string detail = LlmProtocolRuntimePolicy.NativeProviderFailureDetail(noText);
            AssertContains("no-text finish reason is diagnostic", detail, "SAFETY");
            AssertContains("no-text finish message is diagnostic", detail, "bounded explanation");
        }

        private static void TestConnectionSignatureIncludesProtocolMode()
        {
            MethodInfo signature = typeof(PawnDiaryMod).GetMethod(
                "RowConnectionSignature",
                BindingFlags.NonPublic | BindingFlags.Static);
            AssertTrue("row connection signature helper is discoverable", signature != null);
            ApiEndpointConfig chat = Endpoint(
                "https://api.example.test/v1",
                "k",
                "m",
                ApiCompatibilityMode.OpenAIChatCompletions,
                ApiAuthMode.BearerToken,
                string.Empty);
            ApiEndpointConfig responses = chat.Copy();
            responses.apiMode = ApiCompatibilityMode.OpenAIResponses;
            string chatSignature = (string)signature.Invoke(null, new object[] { chat });
            string responsesSignature = (string)signature.Invoke(null, new object[] { responses });
            AssertTrue("row connection signature changes with protocol mode", chatSignature != responsesSignature);
        }

        private static void AssertNativeFinishSuccess(string label, LlmProtocolParseResult parsed)
        {
            AssertTrue(label + " parses text", parsed != null && !string.IsNullOrWhiteSpace(parsed.Text));
            AssertEqual(
                label + " does not synthesize an error",
                string.Empty,
                LlmProtocolRuntimePolicy.NativeProviderFailureDetail(parsed));
        }

        private static async Task<string> SendConnectionTest(
            ApiEndpointConfig endpoint,
            string prompt,
            float temperature,
            ScriptedExchange exchange)
        {
            LlmClient.SendAsyncOverrideForTests = exchange.SendAsync;
            try
            {
                return await LlmClient.TestConnection(endpoint, prompt, 30, temperature);
            }
            finally
            {
                LlmClient.SendAsyncOverrideForTests = null;
            }
        }

        private static async Task<ModelListResult> FetchModels(
            string endpoint,
            string apiKey,
            ApiAuthMode authMode,
            string customHeaderName,
            ApiCompatibilityMode mode,
            ScriptedExchange exchange)
        {
            ModelListClient.SendAsyncOverrideForTests = exchange.SendAsync;
            try
            {
                return await ModelListClient.FetchModels(
                    endpoint,
                    apiKey,
                    authMode,
                    customHeaderName,
                    mode,
                    30);
            }
            finally
            {
                ModelListClient.SendAsyncOverrideForTests = null;
            }
        }

        private static ApiEndpointConfig Endpoint(
            string url,
            string apiKey,
            string model,
            ApiCompatibilityMode mode,
            ApiAuthMode authMode,
            string customHeaderName)
        {
            return new ApiEndpointConfig(url, apiKey, model)
            {
                apiMode = mode,
                authMode = authMode,
                customAuthHeaderName = customHeaderName,
                reasoningEffort = ApiEndpointPolicy.DefaultReasoningEffort,
                reasoningTag = ApiEndpointPolicy.DefaultReasoningTag
            };
        }

        private static string NativeBody(
            LlmProtocolMode mode,
            string model,
            string prompt,
            float temperature,
            int? providerMaximumOutputTokens = null,
            double? providerMaximumTemperature = null)
        {
            return LlmProtocolRequestJson.Build(new LlmProtocolRequestInput
            {
                Mode = mode,
                ModelName = model,
                SystemPrompt = string.Empty,
                UserText = prompt,
                ReasoningEffort = ApiEndpointPolicy.DefaultReasoningEffort,
                MaxTokens = 32,
                Temperature = temperature,
                ProviderMaximumOutputTokens = providerMaximumOutputTokens,
                ProviderMaximumTemperature = providerMaximumTemperature
            });
        }

        private static int CountQueryName(string url, string expectedName)
        {
            Uri uri = new Uri(url);
            int count = 0;
            foreach (string field in uri.Query.TrimStart('?').Split('&'))
            {
                int equals = field.IndexOf('=');
                string rawName = equals < 0 ? field : field.Substring(0, equals);
                if (string.Equals(
                    Uri.UnescapeDataString(rawName),
                    expectedName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }
            return count;
        }

        private static HttpResponseMessage Response(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body ?? string.Empty, Encoding.UTF8, "application/json")
            };
        }

        private static string Header(CapturedRequest request, string name)
        {
            return request.Headers.TryGetValue(name, out string value) ? value : string.Empty;
        }

        private static async Task<TException> ExpectThrowsAsync<TException>(
            string label,
            Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException exception)
            {
                assertions++;
                return exception;
            }

            throw new InvalidOperationException(label + ": expected " + typeof(TException).Name);
        }

        private static void AssertSequence(string label, IList<string> actual, params string[] expected)
        {
            AssertEqual(label, string.Join("|", expected), string.Join("|", actual ?? new List<string>()));
        }

        private static void AssertContains(string label, string actual, string expectedFragment)
        {
            if (actual == null || actual.IndexOf(expectedFragment, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    label + ": expected fragment <" + expectedFragment + "> in <" + actual + ">");
            }
            assertions++;
        }

        private static void AssertNotContains(string label, string actual, string forbiddenFragment)
        {
            if (actual != null && actual.IndexOf(forbiddenFragment, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(
                    label + ": forbidden fragment <" + forbiddenFragment + "> in <" + actual + ">");
            }
            assertions++;
        }

        private static void AssertTrue(string label, bool value)
        {
            if (!value)
            {
                throw new InvalidOperationException(label + ": expected true");
            }
            assertions++;
        }

        private static void AssertFalse(string label, bool value)
        {
            AssertTrue(label, !value);
        }

        private static void AssertEqual<T>(string label, T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    label + ": expected <" + expected + "> but got <" + actual + ">");
            }
            assertions++;
        }

        private sealed class ScriptedExchange
        {
            private readonly Queue<HttpResponseMessage> responses;
            public readonly List<CapturedRequest> Requests = new List<CapturedRequest>();

            public ScriptedExchange(params HttpResponseMessage[] responses)
            {
                this.responses = new Queue<HttpResponseMessage>(
                    responses ?? new HttpResponseMessage[0]);
            }

            public async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                HttpCompletionOption completionOption,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string body = request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync();
                Dictionary<string, string> headers = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
                {
                    headers[header.Key] = string.Join(" ", header.Value.ToArray());
                }

                Requests.Add(new CapturedRequest(
                    request.Method.Method,
                    request.RequestUri.AbsoluteUri,
                    body,
                    headers));
                if (responses.Count == 0)
                {
                    throw new InvalidOperationException("The scripted exchange has no response left.");
                }

                return responses.Dequeue();
            }
        }

        private sealed class CapturedRequest
        {
            public readonly string Method;
            public readonly string Url;
            public readonly string Body;
            public readonly Dictionary<string, string> Headers;

            public CapturedRequest(
                string method,
                string url,
                string body,
                Dictionary<string, string> headers)
            {
                Method = method;
                Url = url;
                Body = body;
                Headers = headers;
            }
        }
    }
}

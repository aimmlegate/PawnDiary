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
            await TestPersistedOllamaFamilyOnFreshRequest();
            await TestGenerationNativeErrorRedactionAndCollision();
            await TestSingleCompletionRetryCapAndCancellation();
            await TestConfiguredDeadlineExpiresPhysicalSend();
            await TestQueuedMultiLaneFailover();
            await TestOpenAiModelDiscoveryCompatibility();
            await TestAnthropicModelPaginationAndHeaders();
            await TestGeminiModelPaginationAndCapabilities();
            await TestOllamaModelDiscovery();
            await TestModelDiscoveryGuardsAndRedaction();
            TestModelPageMetadataMerge();
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
                "models/gemini-3.6-flash",
                ApiCompatibilityMode.GeminiGenerateContent,
                ApiAuthMode.CustomHeader,
                "x-goog-api-key");
            // Exercise the runtime cache-to-request binding, not only the pure serializer: the
            // 32-token visible budget gets thinking headroom while temperature honors discovery.
            ModelCapabilityCache.Update(
                geminiEndpoint.url,
                geminiEndpoint.apiMode,
                geminiEndpoint.model,
                new ModelProtocolCapability(null, 2048, 0.3d));
            string geminiText = await SendConnectionTest(geminiEndpoint, prompt, temperature, gemini);
            AssertEqual("Gemini normal finish is successful", "Gemini OK", geminiText);
            CapturedRequest geminiRequest = gemini.Requests[0];
            AssertEqual(
                "Gemini generation URL canonicalizes model prefix",
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.6-flash:generateContent",
                geminiRequest.Url);
            AssertEqual("Gemini key header", "gemini-secret", Header(geminiRequest, "x-goog-api-key"));
            AssertFalse("Gemini has no bearer", geminiRequest.Headers.ContainsKey("Authorization"));
            AssertEqual(
                "Gemini generation body",
                NativeBody(
                    LlmProtocolMode.GeminiGenerateContent,
                    "models/gemini-3.6-flash",
                    prompt,
                    temperature,
                    2048,
                    0.3d),
                geminiRequest.Body);
            AssertContains("Gemini physical request reserves thinking headroom", geminiRequest.Body,
                "\"maxOutputTokens\":1056");
            AssertContains("Gemini physical request selects low thinking", geminiRequest.Body,
                "\"thinkingConfig\":{\"thinkingLevel\":\"low\"}");

            ScriptedExchange ollama = new ScriptedExchange(Response(
                HttpStatusCode.OK,
                "{\"message\":{\"role\":\"assistant\",\"content\":\"Ollama OK\"},"
                    + "\"done\":true,\"done_reason\":\"stop\"}"));
            ApiEndpointConfig ollamaEndpoint = Endpoint(
                "http://localhost:11434/api/tags",
                "stale-unused-secret",
                "diary-writer:latest",
                ApiCompatibilityMode.OllamaChat,
                ApiAuthMode.None,
                string.Empty);
            ModelCapabilityCache.Update(
                ollamaEndpoint.url,
                ollamaEndpoint.apiMode,
                ollamaEndpoint.model,
                new ModelProtocolCapability(null, 0, null, "gptoss"));
            string ollamaText = await SendConnectionTest(ollamaEndpoint, prompt, temperature, ollama);
            AssertEqual("Ollama normal done reason is successful", "Ollama OK", ollamaText);
            CapturedRequest ollamaRequest = ollama.Requests[0];
            AssertEqual("Ollama generation URL", "http://localhost:11434/api/chat", ollamaRequest.Url);
            AssertFalse("Ollama sends no auth", ollamaRequest.Headers.ContainsKey("Authorization"));
            AssertEqual(
                "Ollama generation body",
                NativeBody(
                    LlmProtocolMode.OllamaChat,
                    "diary-writer:latest",
                    prompt,
                    temperature,
                    null,
                    null,
                    "gptoss"),
                ollamaRequest.Body);
            AssertContains("Ollama physical request uses family-aware thinking level", ollamaRequest.Body,
                "\"think\":\"low\"");
            AssertContains("Ollama physical request reserves thinking headroom", ollamaRequest.Body,
                "\"num_predict\":1056");
        }

        private static async Task TestPersistedOllamaFamilyOnFreshRequest()
        {
            const string prompt = "Fresh process probe";
            ApiEndpointConfig endpoint = Endpoint(
                "http://fresh-provider-family.invalid:11434/api/tags",
                string.Empty,
                "renamed-diary-writer:latest",
                ApiCompatibilityMode.OllamaChat,
                ApiAuthMode.None,
                string.Empty);

            endpoint.RememberProviderModelFamily(" \t gpt\0\r\noss ");
            AssertEqual(
                "persisted provider family collapses controls and whitespace",
                "gpt oss",
                endpoint.ProviderModelFamilyForCurrentLane());
            endpoint.RememberProviderModelFamily(new string('x', 127) + "\uD800");
            string surrogateSafeFamily = endpoint.ProviderModelFamilyForCurrentLane();
            AssertFalse(
                "persisted provider family drops a dangling high surrogate",
                surrogateSafeFamily.Length > 0
                    && char.IsHighSurrogate(surrogateSafeFamily[surrogateSafeFamily.Length - 1]));
            endpoint.RememberProviderModelFamily(new string('x', 200));
            AssertEqual(
                "persisted provider family is bounded",
                128,
                endpoint.ProviderModelFamilyForCurrentLane().Length);
            endpoint.RememberProviderModelFamily("gptoss");
            AssertTrue(
                "fresh request starts without process capability cache",
                ModelCapabilityCache.Get(endpoint.url, endpoint.apiMode, endpoint.model) == null);
            AssertEqual(
                "endpoint copy preserves exact-lane provider family",
                "gptoss",
                endpoint.Copy().ProviderModelFamilyForCurrentLane());

            ApiEndpointConfig edited = endpoint.Copy();
            edited.url = "http://different-provider.invalid:11434";
            AssertEqual(
                "URL edit invalidates persisted provider family",
                string.Empty,
                edited.ProviderModelFamilyForCurrentLane());
            edited = endpoint.Copy();
            edited.model = "different-model:latest";
            AssertEqual(
                "model edit invalidates persisted provider family",
                string.Empty,
                edited.ProviderModelFamilyForCurrentLane());
            edited = endpoint.Copy();
            edited.apiMode = ApiCompatibilityMode.OpenAIChatCompletions;
            AssertEqual(
                "protocol edit invalidates persisted provider family",
                string.Empty,
                edited.ProviderModelFamilyForCurrentLane());

            ScriptedExchange persistedFallback = new ScriptedExchange(Response(
                HttpStatusCode.OK,
                "{\"message\":{\"content\":\"persisted family ok\"},\"done\":true}"));
            await SendConnectionTest(endpoint, prompt, 0.4f, persistedFallback);
            AssertContains(
                "cache-empty request uses persisted GPT-OSS low thinking",
                persistedFallback.Requests[0].Body,
                "\"think\":\"low\"");
            AssertContains(
                "cache-empty request reserves persisted GPT-OSS headroom",
                persistedFallback.Requests[0].Body,
                "\"num_predict\":1056");

            // A successful fresh discovery that reports no family is authoritative: it must disable
            // the older persisted classification rather than falling back to it again.
            ModelCapabilityCache.Update(
                endpoint.url,
                endpoint.apiMode,
                endpoint.model,
                new ModelProtocolCapability(null, 0, null, string.Empty));
            ScriptedExchange freshMetadata = new ScriptedExchange(Response(
                HttpStatusCode.OK,
                "{\"message\":{\"content\":\"fresh metadata ok\"},\"done\":true}"));
            await SendConnectionTest(endpoint, prompt, 0.4f, freshMetadata);
            AssertContains(
                "fresh empty family overrides persisted GPT-OSS thinking",
                freshMetadata.Requests[0].Body,
                "\"think\":false");
            AssertContains(
                "fresh empty family removes persisted GPT-OSS headroom",
                freshMetadata.Requests[0].Body,
                "\"num_predict\":32");

            ApiEndpointConfig pickerEndpoint = Endpoint(
                "http://picker-provider-family.invalid:11434",
                string.Empty,
                string.Empty,
                ApiCompatibilityMode.OllamaChat,
                ApiAuthMode.None,
                string.Empty);
            const string pickedModel = "post-fetch-alias:latest";
            ModelCapabilityCache.Update(
                pickerEndpoint.url,
                pickerEndpoint.apiMode,
                pickedModel,
                new ModelProtocolCapability(null, 0, null, "gptoss"));
            pickerEndpoint.model = pickedModel;
            MethodInfo rememberCachedFamily = typeof(PawnDiaryMod).GetMethod(
                "RememberCachedProviderModelFamily",
                BindingFlags.NonPublic | BindingFlags.Static);
            AssertTrue("post-fetch family sync helper is discoverable", rememberCachedFamily != null);
            rememberCachedFamily.Invoke(null, new object[] { pickerEndpoint });
            AssertEqual(
                "post-fetch model selection persists its cached provider family",
                "gptoss",
                pickerEndpoint.ProviderModelFamilyForCurrentLane());
        }

        private static void TestModelPageMetadataMerge()
        {
            MethodInfo mergePage = typeof(ModelListClient).GetMethod(
                "MergePage",
                BindingFlags.NonPublic | BindingFlags.Static);
            AssertTrue("model page merge helper is discoverable", mergePage != null);

            Dictionary<string, LlmProtocolModelEntry> entries =
                new Dictionary<string, LlmProtocolModelEntry>(StringComparer.Ordinal);
            LlmProtocolModelPageResult first = new LlmProtocolModelPageResult();
            first.Models.Add(new LlmProtocolModelEntry { Id = "duplicate-alias:latest" });
            LlmProtocolModelPageResult second = new LlmProtocolModelPageResult();
            second.Models.Add(new LlmProtocolModelEntry
            {
                Id = "duplicate-alias:latest",
                ProviderFamily = "gptoss"
            });
            mergePage.Invoke(null, new object[] { entries, first });
            mergePage.Invoke(null, new object[] { entries, second });
            AssertEqual(
                "later duplicate model page fills missing provider family",
                "gptoss",
                entries["duplicate-alias:latest"].ProviderFamily);
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

            ScriptedExchange geminiFiltered = new ScriptedExchange(Response(
                HttpStatusCode.OK,
                "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"partial must be discarded\"}]},"
                    + "\"finishReason\":\"ESCALATION\"}]}"));
            ApiEndpointConfig geminiFilteredEndpoint = Endpoint(
                "https://generativelanguage.googleapis.com/v1beta",
                "gemini-filter-secret",
                "gemini-3.6-flash",
                ApiCompatibilityMode.GeminiGenerateContent,
                ApiAuthMode.CustomHeader,
                "x-goog-api-key");
            InvalidOperationException geminiFilteredError =
                await ExpectThrowsAsync<InvalidOperationException>(
                    "Gemini filtered partial response",
                    () => SendConnectionTest(
                        geminiFilteredEndpoint,
                        "probe",
                        0.2f,
                        geminiFiltered));
            AssertContains("Gemini filtered HTTP response surfaces reason",
                geminiFilteredError.Message, "ESCALATION");
            AssertNotContains("Gemini filtered HTTP response discards partial text",
                geminiFilteredError.Message, "partial must be discarded");

            ScriptedExchange anthropicRefusal = new ScriptedExchange(Response(
                HttpStatusCode.OK,
                "{\"content\":[{\"type\":\"text\",\"text\":\"partial must be discarded\"}],"
                    + "\"stop_reason\":\"refusal\",\"stop_details\":{\"type\":\"refusal\","
                    + "\"category\":\"cyber\",\"explanation\":\"Policy stop\"}}"));
            ApiEndpointConfig anthropicRefusalEndpoint = Endpoint(
                "https://api.anthropic.com/v1",
                "anthropic-refusal-secret",
                "claude-refusal",
                ApiCompatibilityMode.AnthropicMessages,
                ApiAuthMode.CustomHeader,
                "x-api-key");
            InvalidOperationException anthropicRefusalError =
                await ExpectThrowsAsync<InvalidOperationException>(
                    "Anthropic structured refusal response",
                    () => SendConnectionTest(
                        anthropicRefusalEndpoint,
                        "probe",
                        0.2f,
                        anthropicRefusal));
            AssertContains("Anthropic HTTP refusal surfaces documented category",
                anthropicRefusalError.Message, "category=cyber");
            AssertNotContains("Anthropic HTTP refusal discards partial text",
                anthropicRefusalError.Message, "partial must be discarded");

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

                const string malformedSecret = "malformed-generation-secret";
                ApiEndpointConfig malformedEndpoint = Endpoint(
                    "https://malformed.api.anthropic.com/v1",
                    malformedSecret,
                    "claude-malformed",
                    ApiCompatibilityMode.AnthropicMessages,
                    ApiAuthMode.CustomHeader,
                    "x-api-key");
                ScriptedExchange malformed = new ScriptedExchange(Response(
                    HttpStatusCode.OK,
                    "{broken " + malformedSecret + " RAW_MALFORMED_BODY_MUST_NOT_LEAK"));
                LlmClient.SendAsyncOverrideForTests = malformed.SendAsync;
                Exception malformedError = await ExpectThrowsAsync<Exception>(
                    "malformed successful native generation response",
                    () => LlmClient.SendSingleCompletion(
                        malformedEndpoint,
                        string.Empty,
                        "Malformed fixture",
                        64,
                        10,
                        0.4f,
                        3,
                        0.01d,
                        CancellationToken.None));
                AssertEqual("malformed successful generation is not retried", 1, malformed.Requests.Count);
                AssertContains("malformed successful generation has sanitized diagnostic",
                    malformedError.Message, "malformed JSON");
                AssertNotContains("malformed successful generation excludes exact key",
                    malformedError.Message, malformedSecret);
                AssertNotContains("malformed successful generation excludes raw body",
                    malformedError.Message, "RAW_MALFORMED_BODY_MUST_NOT_LEAK");

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

        private static async Task TestConfiguredDeadlineExpiresPhysicalSend()
        {
            ApiEndpointConfig endpoint = Endpoint(
                "https://deadline-fixture.invalid/v1",
                "deadline-secret",
                "claude-deadline",
                ApiCompatibilityMode.AnthropicMessages,
                ApiAuthMode.CustomHeader,
                "x-api-key");
            int physicalSends = 0;
            bool transportTokenCancelled = false;
            string physicalUrl = string.Empty;
            DateTime startedUtc = DateTime.UtcNow;
            LlmClient.BeginSession();
            try
            {
                LlmClient.SendAsyncOverrideForTests = async (request, option, cancellationToken) =>
                {
                    physicalSends++;
                    physicalUrl = request.RequestUri.AbsoluteUri;
                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        transportTokenCancelled = cancellationToken.IsCancellationRequested;
                        throw;
                    }

                    return Response(HttpStatusCode.OK, "{}");
                };

                Exception deadlineError = await ExpectThrowsAsync<Exception>(
                    "configured generation deadline",
                    () => LlmClient.SendSingleCompletion(
                        endpoint,
                        string.Empty,
                        "Deadline fixture",
                        64,
                        0,
                        0.4f,
                        1,
                        0.01d,
                        CancellationToken.None));

                double elapsedSeconds = (DateTime.UtcNow - startedUtc).TotalSeconds;
                AssertContains(
                    "configured deadline returns timeout diagnostic",
                    deadlineError.Message,
                    "Timed out waiting for the model");
                AssertNotContains(
                    "configured deadline excludes exact key",
                    deadlineError.Message,
                    endpoint.apiKey);
                AssertEqual("configured deadline reaches one physical send", 1, physicalSends);
                AssertEqual(
                    "configured deadline physical URL",
                    "https://deadline-fixture.invalid/v1/messages",
                    physicalUrl);
                AssertTrue("configured deadline cancels transport token", transportTokenCancelled);
                AssertTrue("configured deadline honors five-second minimum", elapsedSeconds >= 4d);
                AssertTrue("configured deadline remains bounded", elapsedSeconds < 15d);
            }
            finally
            {
                LlmClient.SendAsyncOverrideForTests = null;
                LlmClient.EndSession();
            }
        }

        private static async Task TestQueuedMultiLaneFailover()
        {
            ApiEndpointConfig primary = Endpoint(
                "https://failover-primary.invalid/v1",
                "primary-secret",
                "claude-primary",
                ApiCompatibilityMode.AnthropicMessages,
                ApiAuthMode.CustomHeader,
                "x-api-key");
            ApiEndpointConfig secondary = Endpoint(
                "https://failover-secondary.invalid/v1beta",
                "secondary-secret",
                "models/gemini-secondary",
                ApiCompatibilityMode.GeminiGenerateContent,
                ApiAuthMode.CustomHeader,
                "x-goog-api-key");
            ScriptedExchange exchange = new ScriptedExchange(
                Response(
                    HttpStatusCode.BadRequest,
                    "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\","
                        + "\"message\":\"primary rejected fixture\"}}"),
                Response(
                    HttpStatusCode.OK,
                    "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":"
                        + "\"Failover succeeded.\"}]},\"finishReason\":\"STOP\"}]}"));
            LlmGenerationRequest request = new LlmGenerationRequest
            {
                eventId = "fixture-failover-event",
                povRole = "primary",
                systemPrompt = "Failover system fixture",
                rawText = "Failover user fixture",
                endpointUrl = primary.url,
                modelName = primary.model,
                providerModelFamily = primary.ProviderModelFamilyForCurrentLane(),
                apiKey = primary.apiKey,
                authMode = primary.authMode,
                customAuthHeaderName = primary.customAuthHeaderName,
                apiMode = primary.apiMode,
                reasoningEffort = primary.reasoningEffort,
                reasoningTag = primary.reasoningTag,
                failoverTargets = new List<ApiEndpointConfig> { secondary },
                timeoutSeconds = 10,
                maxTokens = 64,
                temperature = 0.4f
            };

            LlmClient.BeginSession();
            try
            {
                LlmClient.SendAsyncOverrideForTests = exchange.SendAsync;
                LlmClient.Enqueue(request);

                LlmGenerationResult completed = null;
                DateTime pollDeadlineUtc = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < pollDeadlineUtc
                    && !LlmClient.TryDequeueCompleted(out completed))
                {
                    await Task.Delay(10);
                }

                AssertTrue("queued failover publishes a bounded result", completed != null);
                AssertEqual("queued failover makes two physical sends", 2, exchange.Requests.Count);
                AssertEqual(
                    "queued failover primary URL",
                    "https://failover-primary.invalid/v1/messages",
                    exchange.Requests[0].Url);
                AssertEqual(
                    "queued failover secondary URL",
                    "https://failover-secondary.invalid/v1beta/models/gemini-secondary:generateContent",
                    exchange.Requests[1].Url);
                AssertContains(
                    "queued failover primary uses Anthropic body",
                    exchange.Requests[0].Body,
                    "\"model\":\"claude-primary\"");
                AssertContains(
                    "queued failover secondary uses Gemini body",
                    exchange.Requests[1].Body,
                    "\"generationConfig\"");
                AssertTrue("queued failover result succeeds", completed.success);
                AssertEqual(
                    "queued failover result text",
                    "Failover succeeded.",
                    completed.generatedText);
                AssertEqual(
                    "queued failover result endpoint is secondary",
                    secondary.url,
                    completed.endpointUrl);
                AssertEqual(
                    "queued failover result model is secondary",
                    secondary.model,
                    completed.modelName);
                AssertEqual(
                    "queued failover result mode is secondary",
                    ApiCompatibilityMode.GeminiGenerateContent,
                    completed.apiMode);
                AssertEqual(
                    "queued failover result auth mode is secondary",
                    ApiAuthMode.CustomHeader,
                    completed.authMode);
                AssertEqual(
                    "queued failover result custom header is secondary",
                    "x-goog-api-key",
                    completed.customAuthHeaderName);
                AssertEqual(
                    "queued failover result preserves sent prompt",
                    "Failover user fixture",
                    completed.sentRawText);
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
                "{\"models\":[{\"name\":\"llama3:latest\","
                    + "\"details\":{\"family\":\"gptoss\"}},{\"model\":\"qwen:7b\"}]}"));
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
            AssertTrue("Ollama metadata cached", result.ProtocolCapabilities.ContainsKey("llama3:latest"));
            AssertEqual("Ollama family metadata cached", "gptoss",
                result.ProtocolCapabilities["llama3:latest"].ProviderFamily);
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

            int cancellationPages = 0;
            using (CancellationTokenSource callerCancellation = new CancellationTokenSource())
            {
                ModelListClient.SendAsyncOverrideForTests = async (request, option, token) =>
                {
                    cancellationPages++;
                    if (cancellationPages == 1)
                    {
                        return Response(
                            HttpStatusCode.OK,
                            "{\"data\":[],\"has_more\":true,\"last_id\":\"next-page\"}");
                    }

                    callerCancellation.Cancel();
                    await Task.Delay(Timeout.Infinite, token);
                    return Response(HttpStatusCode.OK, "{}");
                };
                try
                {
                    await ExpectThrowsAsync<OperationCanceledException>(
                        "paginated model-list caller cancellation",
                        () => ModelListClient.FetchModels(
                            "https://api.anthropic.com/v1",
                            string.Empty,
                            ApiAuthMode.None,
                            string.Empty,
                            ApiCompatibilityMode.AnthropicMessages,
                            30,
                            callerCancellation.Token));
                }
                finally
                {
                    ModelListClient.SendAsyncOverrideForTests = null;
                }
            }
            AssertEqual("paginated model-list cancellation reaches second page", 2, cancellationPages);

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

            const string malformedSecret = "malformed-model-secret";
            ScriptedExchange malformed = new ScriptedExchange(Response(
                HttpStatusCode.OK,
                "{broken " + malformedSecret + " RAW_MALFORMED_MODEL_BODY_MUST_NOT_LEAK"));
            InvalidOperationException malformedError = await ExpectThrowsAsync<InvalidOperationException>(
                "malformed native model response",
                () => FetchModels(
                    "https://generativelanguage.googleapis.com/v1beta",
                    malformedSecret,
                    ApiAuthMode.CustomHeader,
                    "x-goog-api-key",
                    ApiCompatibilityMode.GeminiGenerateContent,
                    malformed));
            AssertEqual("malformed native model response sends once", 1, malformed.Requests.Count);
            AssertContains("malformed native model response has sanitized diagnostic",
                malformedError.Message, "malformed JSON");
            AssertNotContains("malformed native model response excludes exact key",
                malformedError.Message, malformedSecret);
            AssertNotContains("malformed native model response excludes raw body",
                malformedError.Message, "RAW_MALFORMED_MODEL_BODY_MUST_NOT_LEAK");

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

            const string sigSecret = "arbitrary-sig-query-value-must-not-remain";
            const string fragmentSecret = "fragment-value-must-not-remain";
            const string opaqueModel = "opaque-cache-model";
            string opaqueEndpoint = "https://opaque-cache.example/v1?sig="
                + sigSecret + "#" + fragmentSecret;
            ModelCapabilityCache.Update(
                opaqueEndpoint,
                ApiCompatibilityMode.OpenAIChatCompletions,
                opaqueModel,
                new ModelProtocolCapability(null, 444, null));
            AssertTrue(
                "opaque cache fingerprint preserves exact arbitrary query identity",
                ModelCapabilityCache.Get(
                    opaqueEndpoint,
                    ApiCompatibilityMode.OpenAIChatCompletions,
                    opaqueModel) != null);
            AssertTrue(
                "opaque cache fingerprint distinguishes arbitrary sig changes",
                ModelCapabilityCache.Get(
                    "https://opaque-cache.example/v1?sig=rotated#" + fragmentSecret,
                    ApiCompatibilityMode.OpenAIChatCompletions,
                    opaqueModel) == null);
            AssertTrue(
                "opaque cache fingerprint distinguishes fragment changes",
                ModelCapabilityCache.Get(
                    "https://opaque-cache.example/v1?sig=" + sigSecret + "#rotated",
                    ApiCompatibilityMode.OpenAIChatCompletions,
                    opaqueModel) == null);

            MethodInfo cacheKey = typeof(ModelCapabilityCache).GetMethod(
                "CacheKey",
                BindingFlags.NonPublic | BindingFlags.Static);
            AssertTrue("capability cache key helper is discoverable", cacheKey != null);
            string opaqueKey = (string)cacheKey.Invoke(
                null,
                new object[]
                {
                    opaqueEndpoint,
                    ApiCompatibilityMode.OpenAIChatCompletions,
                    opaqueModel
                });
            AssertNotContains("capability cache key excludes raw URL host",
                opaqueKey, "opaque-cache.example");
            AssertNotContains("capability cache key excludes arbitrary sig value",
                opaqueKey, sigSecret);
            AssertNotContains("capability cache key excludes fragment value",
                opaqueKey, fragmentSecret);
            AssertNotContains("capability cache key excludes raw model",
                opaqueKey, opaqueModel);
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
            double? providerMaximumTemperature = null,
            string providerModelFamily = null)
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
                ProviderModelFamily = providerModelFamily,
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

// Standalone no-RimWorld regression tests for endpoint URI rewriting and telemetry session
// admission. These exercise the exact production helpers without making any network requests.
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using PawnDiary;

namespace NetworkAuxiliaryTests
{
    internal static class Program
    {
        private static int assertions;

        private static int Main()
        {
            TestEndpointDefaultsAndSuffixes();
            TestEndpointQueryAndFragmentPlacement();
            TestEndpointPathOnlyNormalization();
            TestProviderAwareBaseNormalization();
            TestMalformedEndpointFallback();
            TestTelemetryDedupeAndCompletion();
            TestTelemetryCapsAndRollback();
            TestTelemetrySessionIsolation();
            TestTelemetryConcurrentAdmission();

            Console.WriteLine("NetworkAuxiliaryTests passed " + assertions + " assertions.");
            return 0;
        }

        private static void TestEndpointDefaultsAndSuffixes()
        {
            AssertEqual(
                "blank endpoint falls back",
                ApiEndpointPolicy.DefaultEndpointUrl,
                EndpointUtility.NormalizeBaseEndpoint("  "));
            AssertUri(
                "chat generation path",
                EndpointUtility.BuildGenerationUrl(
                    "https://api.example.test/v1/", ApiCompatibilityMode.OpenAIChatCompletions),
                "/v1/chat/completions",
                string.Empty,
                string.Empty);
            AssertUri(
                "responses generation path",
                EndpointUtility.BuildGenerationUrl(
                    "https://api.example.test/v1/chat/completions",
                    ApiCompatibilityMode.OpenAIResponses),
                "/v1/responses",
                string.Empty,
                string.Empty);
            AssertUri(
                "models replaces generation suffix case-insensitively",
                EndpointUtility.BuildModelsUrl(
                    "https://api.example.test/v1/RESPONSES/", ApiCompatibilityMode.OpenAIResponses),
                "/v1/models",
                string.Empty,
                string.Empty);
        }

        private static void TestEndpointQueryAndFragmentPlacement()
        {
            AssertUri(
                "query follows appended models path",
                EndpointUtility.BuildModelsUrl(
                    "https://api.example.test/v1?api-version=2026-07-01",
                    ApiCompatibilityMode.OpenAIChatCompletions),
                "/v1/models",
                "?api-version=2026-07-01",
                string.Empty);
            AssertUri(
                "fragment follows appended generation path",
                EndpointUtility.BuildChatCompletionsUrl(
                    "https://api.example.test/v1#local-section"),
                "/v1/chat/completions",
                string.Empty,
                "#local-section");
            AssertUri(
                "query and fragment survive suffix replacement",
                EndpointUtility.BuildGenerationUrl(
                    "https://api.example.test/v1/models?tenant=a%2Fb#local-section",
                    ApiCompatibilityMode.OpenAIResponses),
                "/v1/responses",
                "?tenant=a%2Fb",
                "#local-section");
        }

        private static void TestEndpointPathOnlyNormalization()
        {
            AssertUri(
                "suffix-looking query text is not removed",
                EndpointUtility.NormalizeBaseEndpoint(
                    "https://api.example.test/v1/?redirect=/models#responses"),
                "/v1",
                "?redirect=/models",
                "#responses");
            AssertUri(
                "known path suffix is removed before query",
                EndpointUtility.NormalizeBaseEndpoint(
                    "https://api.example.test/v1/chat/completions/?key=value"),
                "/v1",
                "?key=value",
                string.Empty);
            AssertUri(
                "root endpoint gets one leading slash",
                EndpointUtility.BuildModelsUrl(
                    "https://api.example.test/", ApiCompatibilityMode.OpenAIChatCompletions),
                "/models",
                string.Empty,
                string.Empty);
        }

        private static void TestProviderAwareBaseNormalization()
        {
            const string openAiFull = "https://api.example.test/v1/responses?tenant=a#pick";
            AssertEqual(
                "provider-aware OpenAI normalization preserves legacy bytes",
                EndpointUtility.NormalizeBaseEndpoint(openAiFull),
                EndpointUtility.NormalizeBaseEndpoint(
                    openAiFull,
                    "gpt-test",
                    ApiCompatibilityMode.OpenAIResponses));
            AssertEqual(
                "unknown provider mode uses OpenAI normalization",
                EndpointUtility.NormalizeBaseEndpoint(openAiFull),
                EndpointUtility.NormalizeBaseEndpoint(
                    openAiFull,
                    "gpt-test",
                    (ApiCompatibilityMode)999));

            string anthropicDefault = EndpointUtility.NormalizeBaseEndpoint(
                " ",
                "claude-test",
                ApiCompatibilityMode.AnthropicMessages);
            AssertEqual("blank Anthropic endpoint uses provider host",
                "api.anthropic.com", new Uri(anthropicDefault).Host);
            AssertUri(
                "Anthropic full message path becomes versioned base",
                EndpointUtility.NormalizeBaseEndpoint(
                    "https://proxy.example/claude/v1/messages?tenant=a#pick",
                    "claude-test",
                    ApiCompatibilityMode.AnthropicMessages),
                "/claude/v1",
                "?tenant=a",
                "#pick");
            AssertUri(
                "Anthropic model-list path becomes versioned base",
                EndpointUtility.NormalizeBaseEndpoint(
                    "https://api.anthropic.com/v1/models",
                    "claude-test",
                    ApiCompatibilityMode.AnthropicMessages),
                "/v1",
                string.Empty,
                string.Empty);

            AssertUri(
                "Gemini matching full model action becomes base",
                EndpointUtility.NormalizeBaseEndpoint(
                    "https://generativelanguage.googleapis.com/v1beta/models/gemini-pro:generateContent?tenant=a#pick",
                    "models/gemini-pro",
                    ApiCompatibilityMode.GeminiGenerateContent),
                "/v1beta",
                "?tenant=a",
                "#pick");
            AssertUri(
                "Gemini model-list path becomes base",
                EndpointUtility.NormalizeBaseEndpoint(
                    "https://proxy.example/gemini/v1beta/models",
                    "gemini-pro",
                    ApiCompatibilityMode.GeminiGenerateContent),
                "/gemini/v1beta",
                string.Empty,
                string.Empty);
            AssertUri(
                "Gemini canonical action suffix strips without URL mode inference",
                EndpointUtility.NormalizeBaseEndpoint(
                    "https://proxy.example/v1beta/models/other:generateContent",
                    "gemini-pro",
                    ApiCompatibilityMode.GeminiGenerateContent),
                "/v1beta",
                string.Empty,
                string.Empty);

            AssertUri(
                "Ollama chat path becomes API base",
                EndpointUtility.NormalizeBaseEndpoint(
                    "http://localhost:11434/api/chat?tenant=a#pick",
                    "llama-test",
                    ApiCompatibilityMode.OllamaChat),
                "/api",
                "?tenant=a",
                "#pick");
            AssertEqual(
                "host-only Ollama tags path keeps query and fragment",
                "localhost:11434/api?tenant=a#pick",
                EndpointUtility.NormalizeBaseEndpoint(
                    "localhost:11434/api/tags?tenant=a#pick",
                    "llama-test",
                    ApiCompatibilityMode.OllamaChat));

            AssertUri(
                "Chat mode never infers Anthropic from URL",
                EndpointUtility.NormalizeBaseEndpoint(
                    "https://api.anthropic.com/v1/messages",
                    "claude-test",
                    ApiCompatibilityMode.OpenAIChatCompletions),
                "/v1/messages",
                string.Empty,
                string.Empty);
        }

        private static void TestMalformedEndpointFallback()
        {
            AssertEqual(
                "host-only fallback preserves query and fragment order",
                "localhost:1234/v1/models?key=value#local",
                EndpointUtility.BuildModelsUrl(
                    "localhost:1234/v1?key=value#local",
                    ApiCompatibilityMode.OpenAIChatCompletions));
            AssertEqual(
                "fallback strips known suffix from path only",
                "localhost:1234/v1?next=/responses#models",
                EndpointUtility.NormalizeBaseEndpoint(
                    "localhost:1234/v1/chat/completions/?next=/responses#models"));
        }

        private static void TestTelemetryDedupeAndCompletion()
        {
            ErrorReportSessionState state = new ErrorReportSessionState(3, 2);
            AssertTrue("first fingerprint admitted", state.TryAdmit("fingerprint-a", out ErrorReportAdmission first));
            AssertEqual("one unique after admission", 1, state.UniqueDispatched);
            AssertEqual("one in flight after admission", 1, state.InFlight);
            AssertTrue("duplicate fingerprint rejected", !state.TryAdmit("fingerprint-a", out _));

            first.Complete();
            first.Complete();
            first.RollBack();
            AssertEqual("completion is one-shot and never negative", 0, state.InFlight);
            AssertEqual("completed fingerprint remains deduplicated", 1, state.SeenCount);
            AssertEqual("completed report retains unique count", 1, state.UniqueDispatched);
        }

        private static void TestTelemetryCapsAndRollback()
        {
            ErrorReportSessionState state = new ErrorReportSessionState(2, 1);
            AssertTrue("first slot admitted", state.TryAdmit("first", out ErrorReportAdmission first));
            AssertTrue("in-flight cap rejects another", !state.TryAdmit("second", out _));
            AssertEqual("failed concurrency claim rolls unique count back", 1, state.UniqueDispatched);
            AssertEqual("failed concurrency claim rolls fingerprint back", 1, state.SeenCount);

            first.RollBack();
            first.RollBack();
            AssertEqual("rollback releases in-flight once", 0, state.InFlight);
            AssertEqual("rollback releases unique once", 0, state.UniqueDispatched);
            AssertEqual("rollback releases fingerprint", 0, state.SeenCount);

            AssertTrue("rolled-back fingerprint can retry", state.TryAdmit("first", out ErrorReportAdmission retry));
            retry.Complete();
            AssertTrue("second distinct report admitted", state.TryAdmit("second", out ErrorReportAdmission second));
            second.Complete();
            AssertTrue("session report cap is exact", !state.TryAdmit("third", out _));
            AssertEqual("session cap does not overshoot", 2, state.UniqueDispatched);
            AssertEqual("blank fingerprint rejected", 2, state.UniqueDispatched);
            AssertTrue("blank fingerprint has no admission", !state.TryAdmit(" ", out _));
        }

        private static void TestTelemetrySessionIsolation()
        {
            ErrorReportSessionState oldSession = new ErrorReportSessionState(2, 2);
            ErrorReportSessionState newSession = new ErrorReportSessionState(2, 2);
            AssertTrue("old session admission", oldSession.TryAdmit("old", out ErrorReportAdmission oldAdmission));
            AssertTrue("new session admission", newSession.TryAdmit("new", out ErrorReportAdmission newAdmission));

            oldAdmission.Complete();
            oldAdmission.Complete();
            AssertEqual("old completion does not touch new in-flight count", 1, newSession.InFlight);
            AssertEqual("old session reaches zero independently", 0, oldSession.InFlight);

            newAdmission.Complete();
            AssertEqual("new session reaches zero independently", 0, newSession.InFlight);
        }

        private static void TestTelemetryConcurrentAdmission()
        {
            const int maxReports = 25;
            const int maxInFlight = 4;
            ErrorReportSessionState state = new ErrorReportSessionState(maxReports, maxInFlight);
            ConcurrentBag<ErrorReportAdmission> admissions = new ConcurrentBag<ErrorReportAdmission>();

            Parallel.For(0, 500, i =>
            {
                if (state.TryAdmit("parallel-" + i, out ErrorReportAdmission admission))
                {
                    admissions.Add(admission);
                }
            });

            AssertTrue("parallel report cap never exceeded", state.UniqueDispatched <= maxReports);
            AssertTrue("parallel in-flight cap never exceeded", state.InFlight <= maxInFlight);
            foreach (ErrorReportAdmission admission in admissions)
            {
                admission.Complete();
            }

            AssertEqual("parallel completions return in-flight count to zero", 0, state.InFlight);
            int suffix = 0;
            while (state.UniqueDispatched < maxReports
                && state.TryAdmit("fill-" + suffix++, out ErrorReportAdmission admission))
            {
                admission.Complete();
            }

            AssertEqual("sequential follow-up reaches exact report cap", maxReports, state.UniqueDispatched);
            AssertEqual("completed concurrent state never goes negative", 0, state.InFlight);
        }

        private static void AssertUri(
            string label,
            string value,
            string expectedPath,
            string expectedQuery,
            string expectedFragment)
        {
            Uri uri = new Uri(value, UriKind.Absolute);
            AssertEqual(label + " path", expectedPath, uri.AbsolutePath);
            AssertEqual(label + " query", expectedQuery, uri.Query);
            AssertEqual(label + " fragment", expectedFragment, uri.Fragment);
        }

        private static void AssertTrue(string label, bool condition)
        {
            assertions++;
            if (!condition)
            {
                throw new InvalidOperationException("Assertion failed: " + label);
            }
        }

        private static void AssertEqual<T>(string label, T expected, T actual)
        {
            assertions++;
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    "Assertion failed: " + label + " expected=[" + expected + "] actual=[" + actual + "]");
            }
        }
    }
}

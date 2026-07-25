// Small HTTP client for settings-time model discovery. Runtime diary generation uses LlmClient;
// this class only backs the "Fetch models" button in the settings window.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PawnDiary
{
    /// <summary>
    /// Async HTTP client that fetches available model IDs from a compatible endpoint and parses the
    /// JSON response with the mod's Mono-safe MiniJson helper.
    /// </summary>
    internal static class ModelListClient
    {
        // Shared HttpClient with no built-in timeout; per-request timeouts are set via CancellationTokenSource.
        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        private const int MaxModelListResponseBytes = 1024 * 512;

        /// <summary>
        /// Sends a GET request to the OpenAI-style model-list endpoint, authenticates with the given
        /// API key when present, and returns a sorted, deduplicated list of model IDs plus any
        /// per-model reasoning capability the provider advertised (OpenRouter and some gateways).
        /// </summary>
        public static Task<ModelListResult> FetchModels(string endpoint, string apiKey, ApiAuthMode authMode,
            string customAuthHeaderName, ApiCompatibilityMode mode, int timeoutSeconds)
        {
            return FetchModels(endpoint, apiKey, authMode, customAuthHeaderName, mode, timeoutSeconds,
                CancellationToken.None);
        }

        /// <summary>
        /// Fetches models with a caller-owned cancellation token in addition to the normal request
        /// timeout. The settings controller uses this overload to stop an obsolete fetch immediately
        /// when the player starts another fetch, removes a row, or resets connection settings.
        /// </summary>
        public static async Task<ModelListResult> FetchModels(string endpoint, string apiKey, ApiAuthMode authMode,
            string customAuthHeaderName, ApiCompatibilityMode mode, int timeoutSeconds,
            CancellationToken callerCancellation)
        {
            using (CancellationTokenSource timeoutCancellation =
                new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds))))
            using (CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCancellation.Token, callerCancellation))
            using (HttpRequestMessage request = new HttpRequestMessage(
                HttpMethod.Get,
                ApiRequestAuth.ApplyQueryAuth(EndpointUtility.BuildModelsUrl(endpoint, mode), apiKey, authMode)))
            {
                ApiRequestAuth.ApplyHeaders(request, apiKey, authMode, customAuthHeaderName);

                using (HttpResponseMessage response = await Client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token).ConfigureAwait(false))
                {
                    string json = await ReadCappedResponseString(
                        response.Content, MaxModelListResponseBytes, cancellation.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(
                            $"HTTP {(int)response.StatusCode}: "
                            + TrimForStatus(json, apiKey, customAuthHeaderName));
                    }

                    return ParseModels(json);
                }
            }
        }

        /// <summary>
        /// Reads model-list JSON with a hard byte cap so a misconfigured endpoint cannot allocate an
        /// unbounded response string in the settings window.
        /// </summary>
        private static async Task<string> ReadCappedResponseString(HttpContent content, int maxBytes, CancellationToken cancellationToken)
        {
            if (content == null)
            {
                return string.Empty;
            }

            using (Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[8192];
                int total = 0;
                while (true)
                {
                    int read = await stream.ReadAsync(
                        chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > maxBytes)
                    {
                        throw new InvalidOperationException("The endpoint returned a model list that was too large.");
                    }

                    buffer.Write(chunk, 0, read);
                }

                return Encoding.UTF8.GetString(buffer.ToArray());
            }
        }

        /// <summary>
        /// Extracts the "id" strings and any per-model reasoning capability from the "data" array of
        /// an OpenAI-style /models JSON response. IDs are distinct and sorted; capabilities are keyed
        /// by model id and only include entries that actually advertised a reasoning object.
        /// </summary>
        private static ModelListResult ParseModels(string json)
        {
            List<string> models = new List<string>();
            Dictionary<string, ModelReasoningCapability> capabilities = new Dictionary<string, ModelReasoningCapability>();
            Dictionary<string, object> root = MiniJson.Deserialize(json ?? string.Empty) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue("data", out object dataObject))
            {
                return new ModelListResult(models, capabilities);
            }

            object[] data = dataObject as object[];
            if (data == null)
            {
                return new ModelListResult(models, capabilities);
            }

            for (int i = 0; i < data.Length; i++)
            {
                Dictionary<string, object> model = data[i] as Dictionary<string, object>;
                if (model == null || !model.TryGetValue("id", out object idObject))
                {
                    continue;
                }

                string id = idObject as string;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    models.Add(id);

                    // Optional: providers like OpenRouter attach a "reasoning" object describing what
                    // the model supports. When present we capture it so the UI can guide effort choice
                    // and the request can be clamped. Absent reasoning object -> null -> degrade.
                    ModelReasoningCapability capability = ModelReasoningCapability.FromModelEntry(model);
                    if (capability != null)
                    {
                        capabilities[id] = capability;
                    }
                }
            }

            return new ModelListResult(models.Distinct().OrderBy(model => model).ToList(), capabilities);
        }

        /// <summary>
        /// Redacts generic auth patterns plus this request's exact credentials, then truncates to
        /// 120 characters for display in error status messages.
        /// </summary>
        private static string TrimForStatus(string value, string apiKey, string customAuthHeaderName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            // A model-list error body can echo the request URL (query-param key) back to us; this
            // string ends up in the settings window, so mask any secret before it is displayed.
            value = ApiLaneLabels.RedactSecrets(value, apiKey, customAuthHeaderName).Trim();
            return value.Length <= 120 ? value : value.Substring(0, 120) + "...";
        }
    }

    /// <summary>
    /// Outcome of one <c>/models</c> fetch: the sorted model-id list plus any per-model reasoning
    /// capability the provider advertised. The capability map only contains entries that included a
    /// reasoning object; models without one are simply absent and treated as "unknown" by callers.
    /// </summary>
    internal sealed class ModelListResult
    {
        /// <summary>Distinct, sorted model IDs returned by the endpoint.</summary>
        public readonly List<string> Models;

        /// <summary>Per-model reasoning capability, keyed by model id. Only models that advertised a
        /// reasoning object appear here; absence means "capability unknown".</summary>
        public readonly Dictionary<string, ModelReasoningCapability> Capabilities;

        public ModelListResult(List<string> models, Dictionary<string, ModelReasoningCapability> capabilities)
        {
            Models = models ?? new List<string>();
            Capabilities = capabilities ?? new Dictionary<string, ModelReasoningCapability>();
        }
    }
}

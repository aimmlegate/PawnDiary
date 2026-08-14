// Bounded settings-time HTTP adapter for provider model discovery.
//
// Provider URL/header/schema decisions are pure (`LlmProtocol*`). This class owns the impure GET
// loop, one shared deadline, response-byte/page/model caps, auth attachment, and exact-key redaction.
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
    /// <summary>Fetches and combines one bounded provider model-list operation.</summary>
    internal static class ModelListClient
    {
        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        // Defensive transport caps, not player tuning. All pages share these totals.
        private const int MaxModelListResponseBytes = 1024 * 512;
        private const int MaxModelListPages = 20;
        private const int MaxModels = 5000;

#if DEBUG
        // Standalone fixtures replace only the physical send. Production Debug builds leave this
        // null, and Release builds do not contain the seam.
        internal static Func<HttpRequestMessage, HttpCompletionOption, CancellationToken,
            Task<HttpResponseMessage>> SendAsyncOverrideForTests;
#endif

        public static Task<ModelListResult> FetchModels(
            string endpoint,
            string apiKey,
            ApiAuthMode authMode,
            string customAuthHeaderName,
            ApiCompatibilityMode mode,
            int timeoutSeconds)
        {
            return FetchModels(
                endpoint,
                apiKey,
                authMode,
                customAuthHeaderName,
                mode,
                timeoutSeconds,
                CancellationToken.None);
        }

        /// <summary>
        /// Fetches models with caller cancellation and one deadline across every native-provider page.
        /// OpenAI and Ollama schemas have no continuation cursor and therefore retain one-GET behavior.
        /// </summary>
        public static async Task<ModelListResult> FetchModels(
            string endpoint,
            string apiKey,
            ApiAuthMode authMode,
            string customAuthHeaderName,
            ApiCompatibilityMode mode,
            int timeoutSeconds,
            CancellationToken callerCancellation)
        {
            LlmProtocolMode protocolMode = EndpointUtility.ProtocolModeFor(mode);
            bool openAiProtocol = protocolMode == LlmProtocolMode.OpenAIChatCompletions
                || protocolMode == LlmProtocolMode.OpenAIResponses;

            // Collision preflight happens before query/header auth is attached to any request.
            LlmProtocolHeadersPlan protocolHeaders = ApiRequestAuth.PrepareProtocolHeaders(
                mode,
                authMode,
                customAuthHeaderName);

            Dictionary<string, LlmProtocolModelEntry> entries =
                new Dictionary<string, LlmProtocolModelEntry>(StringComparer.Ordinal);
            HashSet<string> seenCursors = new HashSet<string>(StringComparer.Ordinal);
            string cursor = string.Empty;
            int totalBytes = 0;

            using (CancellationTokenSource timeoutCancellation =
                new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds))))
            using (CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCancellation.Token,
                callerCancellation))
            {
                for (int pageNumber = 1; pageNumber <= MaxModelListPages; pageNumber++)
                {
                    string pageUrl = EndpointUtility.BuildModelsUrl(endpoint, mode, cursor);
                    string authenticatedUrl = ApiRequestAuth.ApplyQueryAuth(
                        pageUrl,
                        apiKey,
                        authMode);
                    using (HttpRequestMessage request = new HttpRequestMessage(
                        HttpMethod.Get,
                        authenticatedUrl))
                    {
                        ApiRequestAuth.ApplyHeaders(
                            request,
                            apiKey,
                            authMode,
                            customAuthHeaderName);
                        ApiRequestAuth.ApplyProtocolHeaders(request, protocolHeaders);

                        using (HttpResponseMessage response = await SendHttpAsync(
                            request,
                            HttpCompletionOption.ResponseHeadersRead,
                            cancellation.Token).ConfigureAwait(false))
                        {
                            if (response == null)
                            {
                                throw new InvalidOperationException(
                                    "The model endpoint returned no HTTP response.");
                            }

                            CappedBody body = await ReadCappedResponseString(
                                response.Content,
                                MaxModelListResponseBytes - totalBytes,
                                cancellation.Token).ConfigureAwait(false);
                            totalBytes += body.ByteCount;
                            LlmProtocolModelPageResult page = LlmProtocolModelListCodec.ParsePage(
                                body.Text,
                                protocolMode);

                            if (!response.IsSuccessStatusCode)
                            {
                                // Keep historical OpenAI status detail exact. Native providers expose
                                // only bounded structured fields; their raw bodies never enter status UI.
                                string detail = openAiProtocol
                                    ? body.Text
                                    : page.ProviderError;
                                if (string.IsNullOrWhiteSpace(detail))
                                {
                                    detail = "The provider returned an HTTP error without structured details.";
                                }

                                throw new InvalidOperationException(
                                    $"HTTP {(int)response.StatusCode}: "
                                    + TrimForStatus(detail, apiKey, customAuthHeaderName));
                            }

                            if (!page.ParsedJsonObject || !string.IsNullOrWhiteSpace(page.ProviderError))
                            {
                                throw new InvalidOperationException(
                                    string.IsNullOrWhiteSpace(page.ProviderError)
                                        ? "The model endpoint did not return a JSON object."
                                        : TrimForStatus(
                                            page.ProviderError,
                                            apiKey,
                                            customAuthHeaderName));
                            }

                            bool aggregateLimitReached = MergePage(entries, page);
                            if (aggregateLimitReached || page.ModelLimitReached || !page.HasNextPage)
                            {
                                break;
                            }

                            string nextCursor = page.NextPageCursor;
                            if (!seenCursors.Add(nextCursor))
                            {
                                throw new InvalidOperationException(
                                    "The model endpoint repeated a pagination cursor.");
                            }

                            cursor = nextCursor;
                            if (pageNumber == MaxModelListPages)
                            {
                                throw new InvalidOperationException(
                                    "The model endpoint returned too many pagination pages.");
                            }
                        }
                    }
                }
            }

            List<string> models = openAiProtocol
                // Preserve the established OpenAI model-list ordering comparer exactly.
                ? entries.Keys.OrderBy(id => id).ToList()
                : entries.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
            Dictionary<string, ModelProtocolCapability> protocolCapabilities =
                new Dictionary<string, ModelProtocolCapability>(StringComparer.Ordinal);
            Dictionary<string, ModelReasoningCapability> reasoningCapabilities =
                new Dictionary<string, ModelReasoningCapability>(StringComparer.Ordinal);
            foreach (string id in models)
            {
                ModelProtocolCapability capability = ModelProtocolCapability.FromEntry(entries[id]);
                if (capability == null)
                {
                    continue;
                }

                // Preserve the established OpenAI behavior: a model without advertised reasoning
                // metadata remains unknown and may be refreshed later. Native rows cache even empty
                // metadata so a provider that omits optional limits does not refetch every draw.
                if (!openAiProtocol || capability.ReasoningCapability != null)
                {
                    protocolCapabilities[id] = capability;
                }
                if (capability.ReasoningCapability != null)
                {
                    reasoningCapabilities[id] = capability.ReasoningCapability;
                }
            }

            return new ModelListResult(models, reasoningCapabilities, protocolCapabilities);
        }

        private static bool MergePage(
            Dictionary<string, LlmProtocolModelEntry> entries,
            LlmProtocolModelPageResult page)
        {
            if (page?.Models == null)
            {
                return false;
            }

            for (int i = 0; i < page.Models.Count; i++)
            {
                LlmProtocolModelEntry incoming = page.Models[i];
                if (incoming == null || string.IsNullOrWhiteSpace(incoming.Id))
                {
                    continue;
                }

                if (entries.TryGetValue(incoming.Id, out LlmProtocolModelEntry existing))
                {
                    if (existing.MaxOutputTokens <= 0)
                    {
                        existing.MaxOutputTokens = incoming.MaxOutputTokens;
                    }
                    if (!existing.MaxTemperature.HasValue)
                    {
                        existing.MaxTemperature = incoming.MaxTemperature;
                    }
                    if (existing.ReasoningCapability == null)
                    {
                        existing.ReasoningCapability = incoming.ReasoningCapability;
                    }
                    if (string.IsNullOrEmpty(existing.ProviderFamily))
                    {
                        existing.ProviderFamily = incoming.ProviderFamily;
                    }
                    continue;
                }

                if (entries.Count >= MaxModels)
                {
                    return true;
                }
                entries.Add(incoming.Id, incoming);
            }

            return entries.Count >= MaxModels;
        }

        private static Task<HttpResponseMessage> SendHttpAsync(
            HttpRequestMessage request,
            HttpCompletionOption completionOption,
            CancellationToken cancellationToken)
        {
#if DEBUG
            Func<HttpRequestMessage, HttpCompletionOption, CancellationToken,
                Task<HttpResponseMessage>> scripted = SendAsyncOverrideForTests;
            if (scripted != null)
            {
                return scripted(request, completionOption, cancellationToken);
            }
#endif
            return Client.SendAsync(request, completionOption, cancellationToken);
        }

        private static async Task<CappedBody> ReadCappedResponseString(
            HttpContent content,
            int remainingBytes,
            CancellationToken cancellationToken)
        {
            if (content == null)
            {
                return new CappedBody(string.Empty, 0);
            }

            using (Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[8192];
                int total = 0;
                while (true)
                {
                    int read = await stream.ReadAsync(
                        chunk,
                        0,
                        chunk.Length,
                        cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    total += read;
                    if (remainingBytes < 0 || total > remainingBytes)
                    {
                        throw new InvalidOperationException(
                            "The endpoint returned a model list that was too large.");
                    }
                    buffer.Write(chunk, 0, read);
                }

                return new CappedBody(Encoding.UTF8.GetString(buffer.ToArray()), total);
            }
        }

        private static string TrimForStatus(
            string value,
            string apiKey,
            string customAuthHeaderName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = ApiLaneLabels.RedactSecrets(value, apiKey, customAuthHeaderName).Trim();
            return value.Length <= 120 ? value : value.Substring(0, 120) + "...";
        }

        private sealed class CappedBody
        {
            public readonly string Text;
            public readonly int ByteCount;

            public CappedBody(string text, int byteCount)
            {
                Text = text ?? string.Empty;
                ByteCount = Math.Max(0, byteCount);
            }
        }
    }

    /// <summary>
    /// Combined model-list outcome. The reasoning-only map preserves the established settings API;
    /// the immutable protocol map also carries native output/sampling limits.
    /// </summary>
    internal sealed class ModelListResult
    {
        public readonly List<string> Models;
        public readonly Dictionary<string, ModelReasoningCapability> Capabilities;
        public readonly Dictionary<string, ModelProtocolCapability> ProtocolCapabilities;

        public ModelListResult(
            List<string> models,
            Dictionary<string, ModelReasoningCapability> capabilities,
            Dictionary<string, ModelProtocolCapability> protocolCapabilities)
        {
            Models = models ?? new List<string>();
            Capabilities = capabilities
                ?? new Dictionary<string, ModelReasoningCapability>(StringComparer.Ordinal);
            ProtocolCapabilities = protocolCapabilities
                ?? new Dictionary<string, ModelProtocolCapability>(StringComparer.Ordinal);
        }
    }
}

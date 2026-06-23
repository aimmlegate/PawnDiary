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
    public static class ModelListClient
    {
        // Shared HttpClient with no built-in timeout; per-request timeouts are set via CancellationTokenSource.
        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        private const int MaxModelListResponseBytes = 1024 * 512;

        /// <summary>
        /// Sends a GET request to the selected mode's model-list endpoint, authenticates with the
        /// given API key when present, and returns a sorted, deduplicated list of model IDs.
        /// </summary>
        public static async Task<List<string>> FetchModels(string endpoint, string apiKey, ApiCompatibilityMode mode, int timeoutSeconds)
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds))))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, EndpointUtility.BuildModelsUrl(endpoint, mode)))
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey.Trim());
                }

                using (HttpResponseMessage response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token))
                {
                    string json = await ReadCappedResponseString(response.Content, MaxModelListResponseBytes, cancellation.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {TrimForStatus(json)}");
                    }

                    return mode == ApiCompatibilityMode.OllamaNativeChat
                        ? ParseOllamaModelNames(json)
                        : ParseOpenAIModelIds(json);
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

            using (Stream stream = await content.ReadAsStreamAsync())
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[8192];
                int total = 0;
                while (true)
                {
                    int read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken);
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
        /// Extracts the "id" strings from the "data" array of an OpenAI-style /models JSON response,
        /// returning distinct, sorted model IDs.
        /// </summary>
        private static List<string> ParseOpenAIModelIds(string json)
        {
            List<string> models = new List<string>();
            Dictionary<string, object> root = MiniJson.Deserialize(json ?? string.Empty) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue("data", out object dataObject))
            {
                return models;
            }

            object[] data = dataObject as object[];
            if (data == null)
            {
                return models;
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
                }
            }

            return models.Distinct().OrderBy(model => model).ToList();
        }

        /// <summary>
        /// Extracts model names from Ollama's native /api/tags shape: { models: [{ name: ... }] }.
        /// </summary>
        private static List<string> ParseOllamaModelNames(string json)
        {
            List<string> models = new List<string>();
            Dictionary<string, object> root = MiniJson.Deserialize(json ?? string.Empty) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue("models", out object modelsObject))
            {
                return models;
            }

            object[] data = modelsObject as object[];
            if (data == null)
            {
                return models;
            }

            for (int i = 0; i < data.Length; i++)
            {
                Dictionary<string, object> model = data[i] as Dictionary<string, object>;
                if (model == null)
                {
                    continue;
                }

                string name = null;
                if (model.TryGetValue("name", out object nameObject))
                {
                    name = nameObject as string;
                }

                if (string.IsNullOrWhiteSpace(name) && model.TryGetValue("model", out object modelObject))
                {
                    name = modelObject as string;
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    models.Add(name);
                }
            }

            return models.Distinct().OrderBy(model => model).ToList();
        }

        /// <summary>Truncates a string to 120 chars for display in error status messages.</summary>
        private static string TrimForStatus(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = value.Trim();
            return value.Length <= 120 ? value : value.Substring(0, 120) + "...";
        }
    }
}

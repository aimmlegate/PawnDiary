// Small HTTP client for settings-time model discovery. Runtime diary generation uses LlmClient;
// this class only backs the "Fetch models" button in the settings window.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PawnDiary
{
    /// <summary>
    /// Async HTTP client that fetches available model IDs from an OpenAI-compatible /models endpoint
    /// and parses the JSON response with the mod's Mono-safe MiniJson helper.
    /// </summary>
    public static class ModelListClient
    {
        // Shared HttpClient with no built-in timeout; per-request timeouts are set via CancellationTokenSource.
        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        /// <summary>
        /// Sends a GET request to the /models endpoint, authenticates with the given API key, and
        /// returns a sorted, deduplicated list of model IDs.
        /// </summary>
        public static async Task<List<string>> FetchModels(string endpoint, string apiKey, int timeoutSeconds)
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds))))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, EndpointUtility.BuildModelsUrl(endpoint)))
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey.Trim());
                }

                using (HttpResponseMessage response = await Client.SendAsync(request, cancellation.Token))
                {
                    string json = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {TrimForStatus(json)}");
                    }

                    return ParseModelIds(json);
                }
            }
        }

        /// <summary>
        /// Extracts the "id" strings from the "data" array of an OpenAI-style /models JSON response,
        /// returning distinct, sorted model IDs.
        /// </summary>
        private static List<string> ParseModelIds(string json)
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

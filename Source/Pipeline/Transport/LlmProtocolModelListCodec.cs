// Pure decoding of provider model-list pages.
//
// The HTTP settings client owns byte/page/deadline caps. This helper owns only the provider schema,
// a defensive per-page model limit, ID normalization, optional capability metadata, and bounded
// pagination cursors.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Parses one provider model-list page into normalized model rows.</summary>
    internal static class LlmProtocolModelListCodec
    {
        private const int MaximumModelsPerPage = 1000;
        private const int MaximumPaginationCursorChars = 1024;
        private const int MaximumProviderErrorChars = 512;

        /// <summary>Decodes a model-list page for the selected protocol.</summary>
        public static LlmProtocolModelPageResult ParsePage(string responseJson, LlmProtocolMode mode)
        {
            LlmProtocolModelPageResult result = new LlmProtocolModelPageResult();
            Dictionary<string, object> root;
            try
            {
                root = MiniJson.Deserialize(responseJson ?? string.Empty) as Dictionary<string, object>;
            }
            catch
            {
                result.ProviderError = "The model endpoint returned malformed JSON.";
                return result;
            }

            if (root == null)
            {
                result.ProviderError = "The model endpoint did not return a JSON object.";
                return result;
            }

            result.ParsedJsonObject = true;
            result.ProviderError = StructuredError(root);
            Dictionary<string, LlmProtocolModelEntry> distinct =
                new Dictionary<string, LlmProtocolModelEntry>(StringComparer.Ordinal);
            switch (LlmProtocolDispatcher.NormalizeMode(mode))
            {
                case LlmProtocolMode.AnthropicMessages:
                    ParseDataRows(root, distinct, result, true);
                    SetAnthropicCursor(root, result);
                    break;
                case LlmProtocolMode.GeminiGenerateContent:
                    ParseGeminiRows(root, distinct, result);
                    SetCursor(result, "pageToken", StringField(root, "nextPageToken"));
                    break;
                case LlmProtocolMode.OllamaChat:
                    ParseOllamaRows(root, distinct, result);
                    break;
                default:
                    ParseDataRows(root, distinct, result, false);
                    break;
            }

            List<string> ids = new List<string>(distinct.Keys);
            // Preserve the existing OpenAI model picker ordering exactly: ModelListClient used
            // LINQ OrderBy(string), whose default comparer is current-culture string ordering.
            ids.Sort();
            for (int i = 0; i < ids.Count; i++)
            {
                result.Models.Add(distinct[ids[i]]);
            }
            return result;
        }

        private static void ParseDataRows(
            Dictionary<string, object> root,
            Dictionary<string, LlmProtocolModelEntry> distinct,
            LlmProtocolModelPageResult result,
            bool anthropic)
        {
            object[] data = ArrayField(root, "data");
            for (int i = 0; i < data.Length; i++)
            {
                Dictionary<string, object> row = data[i] as Dictionary<string, object>;
                string rawId = StringField(row, "id");
                if (string.IsNullOrWhiteSpace(rawId))
                {
                    continue;
                }

                // Existing OpenAI-compatible discovery preserved model IDs byte-for-byte. Native
                // Anthropic rows are new and may safely normalize accidental surrounding whitespace.
                string id = anthropic ? rawId.Trim() : rawId;

                LlmProtocolModelEntry entry = new LlmProtocolModelEntry
                {
                    Id = id,
                    MaxOutputTokens = PositiveIntField(row, anthropic ? "max_tokens" : "max_output_tokens"),
                    ReasoningCapability = anthropic ? null : ModelReasoningCapability.FromModelEntry(row)
                };
                if (!AddModel(distinct, entry, result))
                {
                    break;
                }
            }
        }

        private static void ParseGeminiRows(
            Dictionary<string, object> root,
            Dictionary<string, LlmProtocolModelEntry> distinct,
            LlmProtocolModelPageResult result)
        {
            object[] models = ArrayField(root, "models");
            for (int i = 0; i < models.Length; i++)
            {
                Dictionary<string, object> row = models[i] as Dictionary<string, object>;
                if (row == null || !ContainsMethod(row, "generateContent"))
                {
                    continue;
                }

                string id = LlmProtocolDispatcher.CanonicalModelName(
                    StringField(row, "name"),
                    LlmProtocolMode.GeminiGenerateContent);
                if (string.IsNullOrEmpty(id))
                {
                    id = LlmProtocolDispatcher.CanonicalModelName(
                        StringField(row, "baseModelId"),
                        LlmProtocolMode.GeminiGenerateContent);
                }
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                LlmProtocolModelEntry entry = new LlmProtocolModelEntry
                {
                    Id = id,
                    MaxOutputTokens = PositiveIntField(row, "outputTokenLimit"),
                    MaxTemperature = NonNegativeFiniteDoubleField(row, "maxTemperature")
                };
                if (!AddModel(distinct, entry, result))
                {
                    break;
                }
            }
        }

        private static void ParseOllamaRows(
            Dictionary<string, object> root,
            Dictionary<string, LlmProtocolModelEntry> distinct,
            LlmProtocolModelPageResult result)
        {
            object[] models = ArrayField(root, "models");
            for (int i = 0; i < models.Length; i++)
            {
                Dictionary<string, object> row = models[i] as Dictionary<string, object>;
                string id = StringField(row, "name").Trim();
                if (string.IsNullOrEmpty(id))
                {
                    id = StringField(row, "model").Trim();
                }
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                if (!AddModel(distinct, new LlmProtocolModelEntry { Id = id }, result))
                {
                    break;
                }
            }
        }

        private static bool AddModel(
            Dictionary<string, LlmProtocolModelEntry> distinct,
            LlmProtocolModelEntry entry,
            LlmProtocolModelPageResult result)
        {
            if (distinct.TryGetValue(entry.Id, out LlmProtocolModelEntry existing))
            {
                // Prefer any useful metadata found on a duplicate row without changing its ID.
                if (existing.MaxOutputTokens <= 0)
                {
                    existing.MaxOutputTokens = entry.MaxOutputTokens;
                }
                if (!existing.MaxTemperature.HasValue)
                {
                    existing.MaxTemperature = entry.MaxTemperature;
                }
                if (entry.ReasoningCapability != null)
                {
                    // Existing OpenAI parsing assigned capabilities[id] for every capable duplicate,
                    // so the last non-null advertised capability wins.
                    existing.ReasoningCapability = entry.ReasoningCapability;
                }
                return true;
            }

            if (distinct.Count >= MaximumModelsPerPage)
            {
                result.ModelLimitReached = true;
                return false;
            }

            distinct.Add(entry.Id, entry);
            return true;
        }

        private static void SetAnthropicCursor(
            Dictionary<string, object> root,
            LlmProtocolModelPageResult result)
        {
            if (!BoolField(root, "has_more"))
            {
                return;
            }
            SetCursor(result, "after_id", StringField(root, "last_id"));
        }

        private static void SetCursor(
            LlmProtocolModelPageResult result,
            string parameterName,
            string rawCursor)
        {
            string cursor = (rawCursor ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(cursor) || cursor.Length > MaximumPaginationCursorChars)
            {
                return;
            }
            result.NextPageParameterName = parameterName;
            result.NextPageCursor = cursor;
        }

        private static bool ContainsMethod(Dictionary<string, object> row, string method)
        {
            object[] methods = ArrayField(row, "supportedGenerationMethods");
            for (int i = 0; i < methods.Length; i++)
            {
                string value = methods[i] as string;
                if (string.Equals(value, method, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static int PositiveIntField(Dictionary<string, object> fields, string name)
        {
            if (fields == null || !fields.TryGetValue(name, out object value) || value == null)
            {
                return 0;
            }

            long integral;
            if (value is long)
            {
                integral = (long)value;
            }
            else if (value is double)
            {
                double number = (double)value;
                if (double.IsNaN(number) || double.IsInfinity(number) || number <= 0d)
                {
                    return 0;
                }
                integral = number >= int.MaxValue ? int.MaxValue : (long)number;
            }
            else
            {
                return 0;
            }

            if (integral <= 0)
            {
                return 0;
            }
            return integral > int.MaxValue ? int.MaxValue : (int)integral;
        }

        private static double? NonNegativeFiniteDoubleField(
            Dictionary<string, object> fields,
            string name)
        {
            if (fields == null || !fields.TryGetValue(name, out object value) || value == null)
            {
                return null;
            }

            double number;
            if (value is double)
            {
                number = (double)value;
            }
            else if (value is long)
            {
                number = (long)value;
            }
            else
            {
                return null;
            }
            return double.IsNaN(number) || double.IsInfinity(number) || number < 0d
                ? (double?)null
                : number;
        }

        private static string StructuredError(Dictionary<string, object> root)
        {
            if (root == null || !root.TryGetValue("error", out object errorObject) || errorObject == null)
            {
                return string.Empty;
            }

            string direct = errorObject as string;
            if (direct != null)
            {
                return Bound("API error: " + direct);
            }

            Dictionary<string, object> error = errorObject as Dictionary<string, object>;
            string type = StringField(error, "type");
            if (string.IsNullOrWhiteSpace(type))
            {
                type = StringField(error, "status");
            }
            string message = StringField(error, "message");
            string detail = !string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(message)
                ? type.Trim() + ": " + message.Trim()
                : (string.IsNullOrWhiteSpace(message) ? type : message);
            return Bound("API error: " + (string.IsNullOrWhiteSpace(detail) ? "unspecified provider error" : detail));
        }

        private static string Bound(string value)
        {
            string raw = value ?? string.Empty;
            System.Text.StringBuilder builder = new System.Text.StringBuilder(
                Math.Min(raw.Length, MaximumProviderErrorChars));
            bool pendingSpace = false;
            for (int i = 0; i < raw.Length && builder.Length < MaximumProviderErrorChars; i++)
            {
                char c = raw[i];
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }
                if (pendingSpace && builder.Length < MaximumProviderErrorChars)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }
                if (builder.Length < MaximumProviderErrorChars)
                {
                    builder.Append(c);
                }
            }
            return builder.ToString().Trim();
        }

        private static object[] ArrayField(Dictionary<string, object> fields, string name)
        {
            if (fields == null || !fields.TryGetValue(name, out object value))
            {
                return new object[0];
            }
            return value as object[] ?? new object[0];
        }

        private static string StringField(Dictionary<string, object> fields, string name)
        {
            if (fields == null || !fields.TryGetValue(name, out object value))
            {
                return string.Empty;
            }
            return value as string ?? string.Empty;
        }

        private static bool BoolField(Dictionary<string, object> fields, string name)
        {
            if (fields == null || !fields.TryGetValue(name, out object value))
            {
                return false;
            }
            if (value is bool)
            {
                return (bool)value;
            }
            return string.Equals(value as string, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}

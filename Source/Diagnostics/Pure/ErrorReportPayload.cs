// Pure builder for the JSON body of one error report. Kept separate from the reporter transport so
// the exact wire shape can be tested without RimWorld or a live endpoint. Like LlmRequestJsonBuilder,
// it hand-builds JSON because RimWorld's Mono runtime has no bundled JSON serializer.
//
// PRIVACY CONTRACT: this DTO has NO field for a username, machine name, file path, save/colony/pawn
// name, prompt text, API key, or endpoint URL. The only per-install identifier is `installId`, an
// anonymous random GUID. If you add a field here, it must not carry personal data. See DiaryErrorReporter.
using System.Collections.Generic;
using System.Text;

namespace PawnDiary
{
    /// <summary>
    /// Plain, primitive-only snapshot of one error report. Populated by the impure reporter (which
    /// reads game/version state) and serialized by <see cref="ErrorReportPayload.ToJson"/>.
    /// </summary>
    internal sealed class ErrorReport
    {
        /// <summary>Wire schema version so the endpoint can evolve the shape safely.</summary>
        public int schemaVersion;

        /// <summary>Pawn Diary build/version string.</summary>
        public string modVersion;

        /// <summary>Running RimWorld version.</summary>
        public string rimworldVersion;

        /// <summary>Coarse OS string (no machine or user name).</summary>
        public string os;

        /// <summary>Anonymous per-install random GUID. Not a machine or hardware id.</summary>
        public string installId;

        /// <summary>Stable dedupe/grouping key from <see cref="ErrorFingerprint"/>.</summary>
        public string fingerprint;

        /// <summary>UTC timestamp in ISO-8601 (round-trip) format.</summary>
        public string timestampUtc;

        /// <summary>Active paid DLC names (e.g. "Royalty"); empty for a base-game install.</summary>
        public List<string> activeDlc = new List<string>();

        /// <summary>The scrubbed error message + stack. Must already be PII-free.</summary>
        public string message;
    }

    /// <summary>
    /// Serializes an <see cref="ErrorReport"/> to a compact JSON object string.
    /// </summary>
    internal static class ErrorReportPayload
    {
        /// <summary>Current wire schema version.</summary>
        public const int SchemaVersion = 1;

        /// <summary>Returns the JSON body for one report.</summary>
        public static string ToJson(ErrorReport report)
        {
            StringBuilder sb = new StringBuilder(512);
            sb.Append('{');
            AppendInt(sb, "schemaVersion", report.schemaVersion);
            sb.Append(',');
            AppendString(sb, "modVersion", report.modVersion);
            sb.Append(',');
            AppendString(sb, "rimworldVersion", report.rimworldVersion);
            sb.Append(',');
            AppendString(sb, "os", report.os);
            sb.Append(',');
            AppendString(sb, "installId", report.installId);
            sb.Append(',');
            AppendString(sb, "fingerprint", report.fingerprint);
            sb.Append(',');
            AppendString(sb, "timestampUtc", report.timestampUtc);
            sb.Append(',');
            AppendStringArray(sb, "activeDlc", report.activeDlc);
            sb.Append(',');
            // Message goes last because it is by far the largest field.
            AppendString(sb, "message", report.message);
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendInt(StringBuilder sb, string key, int value)
        {
            sb.Append('"').Append(key).Append("\":").Append(value);
        }

        private static void AppendString(StringBuilder sb, string key, string value)
        {
            sb.Append('"').Append(key).Append("\":\"").Append(Escape(value)).Append('"');
        }

        private static void AppendStringArray(StringBuilder sb, string key, List<string> values)
        {
            sb.Append('"').Append(key).Append("\":[");
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append('"').Append(Escape(values[i])).Append('"');
                }
            }

            sb.Append(']');
        }

        /// <summary>
        /// Minimal JSON string escaper, matching LlmRequestJsonBuilder's behavior (control chars are
        /// \u-escaped) so the endpoint sees the same well-formed encoding the LLM client produces.
        /// </summary>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length + 16);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(c))
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            return builder.ToString();
        }
    }
}

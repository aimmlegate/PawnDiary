// Pure scrubber for outgoing error-report text. The error reporter must never let personal data or
// API secrets leave the machine, so every string is run through here before it is queued or sent.
//
// This is deliberately pure (no Verse/Unity/Environment reads) so it can be unit-tested without the
// game. Machine-specific inputs the caller already knows (the configured API keys and endpoint URLs)
// are passed in as `secrets`; everything else is masked by shape, not by knowing the actual value.
// It layers on top of the existing ApiLaneLabels.RedactSecrets (bearer tokens + key=/token= query
// params) and TextTruncation.SafePrefix (surrogate-safe length cap) rather than duplicating them.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PawnDiary
{
    /// <summary>
    /// Removes personal data and API secrets from an error string before it is reported to a remote
    /// endpoint. Broad on purpose: a false positive only ever hides a value, never leaks one.
    /// </summary>
    internal static class ErrorScrub
    {
        // A Windows path in a stack trace is "C:\Users\<realname>\..." and a Unix one is
        // "/home/<realname>/..." or "/Users/<realname>/..." — the username segment is personal data.
        // These mask only that one segment and keep the rest of the path (which is useful for
        // grouping crashes) — and they need no knowledge of the actual machine, so they stay pure.
        private static readonly Regex WindowsUserPath = new Regex(
            @"(?i)([A-Za-z]:\\Users\\)[^\\/\s""']+",
            RegexOptions.Compiled);
        private static readonly Regex UnixUserPath = new Regex(
            @"(/(?:home|Users)/)[^/\s""']+",
            RegexOptions.Compiled);

        // A raw OpenAI-style key can appear in text that is not behind "Bearer " (e.g. a header value
        // echoed into an error). ApiLaneLabels.RedactSecrets catches the "Bearer <t>" / "key=<t>"
        // shapes; this catches the bare "sk-..." shape. The {6,} floor avoids masking short tokens
        // that merely start with "sk-".
        private static readonly Regex OpenAiKey = new Regex(
            @"(?i)\bsk-[A-Za-z0-9_\-]{6,}",
            RegexOptions.Compiled);

        /// <summary>Default hard cap on reported message length, so one giant stack cannot bloat the payload.</summary>
        public const int DefaultMaxChars = 4000;

        /// <summary>
        /// Returns a copy of <paramref name="message"/> with usernames, configured API keys/URLs, and
        /// common secret shapes removed, capped to <paramref name="maxChars"/> characters.
        /// </summary>
        /// <param name="message">Raw error text (message + stack trace).</param>
        /// <param name="secrets">Exact values to redact — the caller passes the configured API keys and
        /// endpoint URLs so those can never leak even in an unusual message shape. May be null.</param>
        /// <param name="maxChars">Length cap; values &lt;= 0 fall back to <see cref="DefaultMaxChars"/>.</param>
        public static string Scrub(string message, IEnumerable<string> secrets, int maxChars)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            string value = message;

            // 1) Exact configured secrets first, longest-first so a URL that contains a key does not
            //    leave the key behind as an un-replaced substring.
            if (secrets != null)
            {
                List<string> ordered = new List<string>();
                foreach (string secret in secrets)
                {
                    if (!string.IsNullOrWhiteSpace(secret) && secret.Length >= 4)
                    {
                        ordered.Add(secret);
                    }
                }

                ordered.Sort((a, b) => b.Length.CompareTo(a.Length));
                foreach (string secret in ordered)
                {
                    value = value.Replace(secret, "<redacted>");
                }
            }

            // 2) Shape-based secret masking shared with the rest of the mod (bearer + key=/token=).
            value = ApiLaneLabels.RedactSecrets(value);
            value = OpenAiKey.Replace(value, "sk-<redacted>");

            // 3) Personal data: mask the username segment of any file path in the text.
            value = WindowsUserPath.Replace(value, "$1~");
            value = UnixUserPath.Replace(value, "$1~");

            // 4) Length cap last, so truncation can never strip the redaction that precedes it.
            int cap = maxChars > 0 ? maxChars : DefaultMaxChars;
            if (value.Length > cap)
            {
                value = TextTruncation.SafePrefix(value, cap) + "...";
            }

            return value;
        }
    }
}

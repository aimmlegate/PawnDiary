// Optional, opt-out error reporter. When enabled (default), errors the mod itself raises are scrubbed
// of all personal data and POSTed to a remote endpoint so we can see what breaks in the wild.
//
// Design notes for anyone new to this (and to C#/RimWorld):
//   * This mirrors LlmClient's transport shape: a shared static HttpClient and fire-and-forget
//     `Task.Run` sends (`Task` ≈ Promise) so the game thread never blocks on the network.
//   * DiaryLogReportPatch feeds us here from a Harmony postfix on Verse.Log.Error, which can run on
//     ANY thread — so everything here is thread-safe and reads no Unity API off the main thread.
//   * It is INERT by default: `ErrorReportEndpoint` ships empty, so Report() no-ops until a real URL
//     is compiled in. "On by default" therefore sends nothing until the endpoint exists.
//   * It must never throw into the game and never log its own failures (logging an error would come
//     straight back through the capture patch — an infinite loop). Every path swallows silently.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Sends scrubbed, deduplicated reports of the mod's own errors to a remote endpoint. Opt-out via
    /// <see cref="PawnDiarySettings.enableErrorReporting"/>; inert until <see cref="ErrorReportEndpoint"/>
    /// is set.
    /// </summary>
    internal static class DiaryErrorReporter
    {
        // The deployed Cloudflare Worker ingest endpoint (services/error-endpoint/, see
        // DOCUMENTATION.md "Error reporting"). Compile-time, not a setting, so it is never
        // player-editable — players cannot redirect it. Set to "" to make the reporter inert again.
        private const string ErrorReportEndpoint = "https://pawndiary-error-endpoint.pawn-diary-aimm-error-reports.workers.dev";

        /// <summary>Max distinct errors reported per game session, so a spamming bug cannot flood the endpoint.</summary>
        private const int MaxReportsPerSession = 25;

        /// <summary>Upper bound on concurrent in-flight POSTs; extra reports are dropped, not queued unbounded.</summary>
        private const int MaxInFlight = 4;

        /// <summary>Hard per-request send deadline.</summary>
        private const int SendTimeoutSeconds = 20;

        /// <summary>Shared client with no built-in timeout; each send uses a per-request cancellation deadline.</summary>
        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        // Fingerprints already handled this session — each distinct error is sent at most once.
        private static readonly ConcurrentDictionary<string, byte> seenFingerprints = new ConcurrentDictionary<string, byte>();

        // Interlocked counters (no lock needed): distinct reports dispatched, and sends in flight.
        private static int uniqueDispatched;
        private static int inFlight;

        /// <summary>
        /// Scrubs, deduplicates, and (best-effort) sends one raw error string. Safe to call from any
        /// thread. No-ops when reporting is off, the endpoint is unset, or caps are hit. Never throws.
        /// </summary>
        public static void Report(string rawMessage)
        {
            try
            {
                if (string.IsNullOrEmpty(ErrorReportEndpoint) || string.IsNullOrWhiteSpace(rawMessage))
                {
                    return;
                }

                PawnDiarySettings settings = PawnDiaryMod.Settings;
                if (settings == null || !settings.enableErrorReporting)
                {
                    return;
                }

                string scrubbed = ErrorScrub.Scrub(rawMessage, GatherSecrets(settings), ErrorScrub.DefaultMaxChars);
                string fingerprint = ErrorFingerprint.Compute(scrubbed);

                // Dedupe: the first sighting of a fingerprint claims the slot; repeats no-op.
                if (!seenFingerprints.TryAdd(fingerprint, 0))
                {
                    return;
                }

                // Per-session cap on distinct errors.
                if (Interlocked.Increment(ref uniqueDispatched) > MaxReportsPerSession)
                {
                    return;
                }

                // Bounded concurrency: drop rather than grow an unbounded backlog.
                if (Interlocked.Increment(ref inFlight) > MaxInFlight)
                {
                    Interlocked.Decrement(ref inFlight);
                    return;
                }

                string json = ErrorReportPayload.ToJson(BuildReport(settings, scrubbed, fingerprint));
                Task.Run(() => SendAsync(json));
            }
            catch
            {
                // Telemetry must never destabilize the game, and must never re-log (that would recurse
                // through the capture patch). Swallow everything.
            }
        }

        /// <summary>Clears per-session dedupe state and caps. Called once per loaded game.</summary>
        public static void ResetSession()
        {
            seenFingerprints.Clear();
            Interlocked.Exchange(ref uniqueDispatched, 0);
            Interlocked.Exchange(ref inFlight, 0);
        }

        /// <summary>
        /// Collects the exact secret values (API keys + endpoint URLs) to redact from report text.
        /// Reading the settings list off-thread can race a rare concurrent edit; a miss only means one
        /// value is not redacted by exact match (shape-based redaction still applies), so tolerate it.
        /// </summary>
        private static List<string> GatherSecrets(PawnDiarySettings settings)
        {
            List<string> secrets = new List<string>();
            try
            {
                List<ApiEndpointConfig> lanes = settings.apiEndpoints;
                if (lanes != null)
                {
                    for (int i = 0; i < lanes.Count; i++)
                    {
                        ApiEndpointConfig lane = lanes[i];
                        if (lane == null)
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(lane.apiKey))
                        {
                            secrets.Add(lane.apiKey);
                        }

                        if (!string.IsNullOrWhiteSpace(lane.url))
                        {
                            secrets.Add(lane.url);
                        }
                    }
                }
            }
            catch
            {
                // Ignore — shape-based redaction in ErrorScrub still masks bearer tokens/keys.
            }

            return secrets;
        }

        /// <summary>Assembles the PII-free report envelope from scrubbed text plus coarse environment info.</summary>
        private static ErrorReport BuildReport(PawnDiarySettings settings, string scrubbedMessage, string fingerprint)
        {
            return new ErrorReport
            {
                schemaVersion = ErrorReportPayload.SchemaVersion,
                modVersion = SafeModVersion(),
                rimworldVersion = SafeRimWorldVersion(),
                os = SafeOsString(),
                installId = SafeInstallId(settings),
                fingerprint = fingerprint,
                timestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                activeDlc = ActiveDlc(),
                message = scrubbedMessage
            };
        }

        private static string SafeModVersion()
        {
            try
            {
                // Assembly version; wire a richer build string here later if needed.
                return typeof(DiaryErrorReporter).Assembly.GetName().Version?.ToString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string SafeRimWorldVersion()
        {
            try
            {
                return VersionControl.CurrentVersionString ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string SafeOsString()
        {
            try
            {
                // Environment.OSVersion is plain BCL (thread-safe, unlike Unity's SystemInfo) and coarse:
                // platform + version number only, never a machine or user name.
                return Environment.OSVersion != null ? Environment.OSVersion.ToString() : "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string SafeInstallId(PawnDiarySettings settings)
        {
            try
            {
                return settings.EnsureErrorReportInstallId();
            }
            catch
            {
                return "unknown";
            }
        }

        private static List<string> ActiveDlc()
        {
            List<string> dlc = new List<string>();
            try
            {
                if (ModsConfig.RoyaltyActive)
                {
                    dlc.Add("Royalty");
                }

                if (ModsConfig.IdeologyActive)
                {
                    dlc.Add("Ideology");
                }

                if (ModsConfig.BiotechActive)
                {
                    dlc.Add("Biotech");
                }

                if (ModsConfig.AnomalyActive)
                {
                    dlc.Add("Anomaly");
                }
            }
            catch
            {
                // Leave the list as-is; DLC info is best-effort.
            }

            return dlc;
        }

        private static async Task SendAsync(string json)
        {
            try
            {
                using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(SendTimeoutSeconds)))
                {
                    await Client.PostAsync(ErrorReportEndpoint, content, cts.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                // A failed report is silent by design — never retry, never log (would recurse).
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        }
    }
}

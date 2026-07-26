// Impure adapter for the plain runtime telemetry ledger.
//
// This is the only telemetry helper that knows about RimWorld's clock or the optional HTTP error
// reporter. Capture/transport code records stable labels through DiaryTelemetry; exception boundaries
// use this adapter to retain a fingerprint and type without retaining the exception message itself.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Bridges privacy-safe telemetry to the game clock, RimWorld error dedupe, and opt-in reporting.
    /// Every method is best-effort and must never destabilize the caller.
    /// </summary>
    internal static class DiaryTelemetryReporter
    {
        /// <summary>
        /// Records one exception using only its type and a stable hash in the local anomaly ring.
        /// The caller should still write its normal <c>[Pawn Diary]</c> Log.Error/ErrorOnce line; the
        /// existing log-report patch owns optional remote delivery and message scrubbing.
        /// </summary>
        public static string RecordException(
            DiaryTelemetryOutcome outcome,
            string stage,
            string source,
            string eventType,
            Exception exception,
            int tick = -1,
            long count = 1)
        {
            try
            {
                string fingerprint = FingerprintException(stage, source, eventType, exception);
                DiaryTelemetry.Record(
                    outcome,
                    stage,
                    source,
                    eventType,
                    tick,
                    count,
                    exception == null ? "unknown exception" : exception.GetType().FullName,
                    fingerprint);
                return fingerprint;
            }
            catch
            {
                // Diagnostics must never replace the original failure.
                return string.Empty;
            }
        }

        /// <summary>
        /// Records an identifier-free invariant summary and sends it through the existing opt-in,
        /// scrubbed, deduplicated error channel. Use only for impossible persistence/lifecycle states,
        /// not ordinary policy drops.
        /// </summary>
        public static void ReportInvariant(
            DiaryTelemetryOutcome outcome,
            string stage,
            string source,
            string eventType,
            string safeSummary,
            int tick = -1,
            long count = 1)
        {
            try
            {
                string identity = outcome + "\n" + (stage ?? string.Empty) + "\n"
                    + (source ?? string.Empty) + "\n" + (eventType ?? string.Empty) + "\n"
                    + (safeSummary ?? string.Empty);
                string fingerprint = ErrorFingerprint.Compute(identity);
                DiaryTelemetry.Record(
                    outcome,
                    stage,
                    source,
                    eventType,
                    tick,
                    count,
                    safeSummary,
                    fingerprint);

                DiaryErrorReporter.Report(
                    "[Pawn Diary] Diagnostic invariant: outcome=" + outcome
                    + " stage=" + (stage ?? string.Empty)
                    + " source=" + (source ?? string.Empty)
                    + " event_type=" + (eventType ?? string.Empty)
                    + " summary=" + (safeSummary ?? string.Empty));
            }
            catch
            {
                // Neither local nor remote diagnostics may affect gameplay.
            }
        }

        /// <summary>
        /// Replays the startup hook manifest into the newly created game session. This makes degraded
        /// hooks visible in the developer export even though patch registration happened before a Game
        /// (and therefore before the session telemetry reset) existed.
        /// </summary>
        public static void RecordHookManifestForSession()
        {
            try
            {
                List<DiaryPatchManifest.Entry> entries = DiaryPatchManifest.Snapshot();
                for (int i = 0; i < entries.Count; i++)
                {
                    DiaryPatchManifest.Entry entry = entries[i];
                    DiaryTelemetryOutcome outcome = HookOutcome(entry.status);
                    DiaryTelemetry.Record(
                        outcome,
                        "harmony.registration",
                        entry.area,
                        entry.target,
                        -1,
                        1,
                        null,
                        null);
                }

                if (!DiaryPatchManifest.AllHealthy())
                {
                    // BuildDetail contains only code target names and registrar errors; the reporter
                    // still runs its normal path/secret/name scrub before optional network delivery.
                    DiaryErrorReporter.Report(
                        "[Pawn Diary] Hook manifest unhealthy: "
                        + DiaryPatchManifest.BuildSummary() + " "
                        + DiaryPatchManifest.BuildDetail());
                }
            }
            catch
            {
                // Startup-health telemetry is supplementary; the startup log remains authoritative.
            }
        }

        /// <summary>
        /// Stable ErrorOnce key that distinguishes different exception fingerprints at one boundary.
        /// <c>string.GetHashCode</c> only needs to be session-stable for Verse.Log.ErrorOnce.
        /// </summary>
        public static int ErrorOnceKey(string context, string fingerprint)
        {
            return ((context ?? string.Empty) + "|" + (fingerprint ?? string.Empty)).GetHashCode();
        }

        /// <summary>Reads the current game tick on the main thread, returning -1 outside play.</summary>
        public static int CurrentGameTick()
        {
            try
            {
                return Find.TickManager == null ? -1 : Find.TickManager.TicksGame;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Computes the same code-path fingerprint used by <see cref="RecordException"/> without
        /// recording a second counter. Used when a downstream ErrorOnce callback needs the key.
        /// </summary>
        public static string FingerprintException(
            string stage,
            string source,
            string eventType,
            Exception exception)
        {
            string identity = (stage ?? string.Empty) + "\n"
                + (source ?? string.Empty) + "\n"
                + (eventType ?? string.Empty) + "\n"
                + (exception == null ? "unknown exception" : exception.ToString());
            return ErrorFingerprint.Compute(identity);
        }

        private static DiaryTelemetryOutcome HookOutcome(DiaryPatchManifest.HookStatus status)
        {
            switch (status)
            {
                case DiaryPatchManifest.HookStatus.Applied:
                    return DiaryTelemetryOutcome.HookApplied;
                case DiaryPatchManifest.HookStatus.Skipped:
                    return DiaryTelemetryOutcome.HookSkipped;
                case DiaryPatchManifest.HookStatus.Degraded:
                    return DiaryTelemetryOutcome.HookDegraded;
                default:
                    return DiaryTelemetryOutcome.HookFailed;
            }
        }
    }
}

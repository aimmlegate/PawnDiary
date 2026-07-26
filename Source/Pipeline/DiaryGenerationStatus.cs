// Pure helpers for diary generation status tokens. The save model owns when these helpers are called,
// but the token decisions stay here so reload/archive edge cases can be tested without RimWorld.
using System;

namespace PawnDiary
{
    /// <summary>
    /// Stable generation-status strings and pure status decisions shared by the save model and tests.
    /// </summary>
    internal static class DiaryGenerationStatus
    {
        // Defensive ceiling for malformed XML/save values. The authored limit remains XML-owned.
        private const int MaximumAutomaticRetryLimit = 10;

        public const string NotGenerated = "not_generated";
        public const string Pending = "pending";
        public const string Complete = "complete";
        public const string Failed = "failed";
        public const string Skipped = "skipped";
        public const string PromptOnly = "prompt_only";

        /// <summary>
        /// Normalizes the saved main-entry status after load. In-flight requests are not persisted, so
        /// pending rows become not-generated and the hot-window scanner can requeue them.
        /// </summary>
        public static string NormalizeLoadedMainStatus(string status, string generatedText)
        {
            if (!string.IsNullOrWhiteSpace(generatedText))
            {
                return Complete;
            }

            if (StatusEquals(status, Pending) || string.IsNullOrWhiteSpace(status))
            {
                return NotGenerated;
            }

            return status;
        }

        /// <summary>
        /// Normalizes a live (non-archived) entry after load. Unlike compact archive rows, a failed
        /// live entry is still eligible for the background generation scan, so it becomes
        /// not-generated and can rejoin its bounded retry sequence after the game reloads.
        /// </summary>
        public static string NormalizeLoadedRetryableMainStatus(string status, string generatedText)
        {
            string normalized = NormalizeLoadedMainStatus(status, generatedText);
            return StatusEquals(normalized, Failed) ? NotGenerated : normalized;
        }

        /// <summary>
        /// Clamps the XML-owned number of automatic regenerations to a defensive runtime range.
        /// </summary>
        public static int NormalizeAutomaticRetryLimit(int configuredLimit)
        {
            if (configuredLimit <= 0)
            {
                return 0;
            }

            return Math.Min(configuredLimit, MaximumAutomaticRetryLimit);
        }

        /// <summary>
        /// Clamps a saved automatic-regeneration counter so corrupt data cannot bypass the limit.
        /// </summary>
        public static int NormalizeAutomaticRetryAttempts(int attempts)
        {
            if (attempts <= 0)
            {
                return 0;
            }

            return Math.Min(attempts, MaximumAutomaticRetryLimit);
        }

        /// <summary>
        /// True when another automatic regeneration may be scheduled after a failed full request.
        /// The counter is the number of regenerations already scheduled, not the initial request.
        /// </summary>
        public static bool CanScheduleAutomaticRetry(int attemptsAlreadyScheduled, int configuredLimit)
        {
            return NormalizeAutomaticRetryAttempts(attemptsAlreadyScheduled)
                < NormalizeAutomaticRetryLimit(configuredLimit);
        }

        /// <summary>
        /// Normalizes the saved title status after load. Title follow-up work is opportunistic, so a
        /// stale pending title is cleared rather than shown as active writing.
        /// </summary>
        public static string NormalizeLoadedTitleStatus(string status, string title)
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                return Complete;
            }

            if (StatusEquals(status, Pending))
            {
                return string.Empty;
            }

            return status ?? string.Empty;
        }

        /// <summary>
        /// True when an older archived entry should render the saved prompt/raw facts as a failed
        /// archive fallback instead of disappearing or showing an endless active-writing state.
        /// </summary>
        public static bool IsArchivedGenerationStale(
            bool archivedForScans,
            string status,
            string generatedText,
            string prompt)
        {
            if (!archivedForScans || !string.IsNullOrWhiteSpace(generatedText))
            {
                return false;
            }

            if (StatusEquals(status, Pending))
            {
                return true;
            }

            // A saved pending request reloads as not_generated because the background HTTP work is gone.
            // The prompt is our durable proof that this page was actually attempted, not a never-queued
            // raw event that should stay hidden in production UI.
            return StatusEquals(status, NotGenerated) && !string.IsNullOrWhiteSpace(prompt);
        }

        public static bool StatusEquals(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}

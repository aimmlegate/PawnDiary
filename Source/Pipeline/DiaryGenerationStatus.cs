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

// Pure helper for dev prompt-capture mode. The live generation adapter uses this to show the exact
// system and user prompt text that would have been sent, without calling any LLM transport.
namespace PawnDiary
{
    /// <summary>
    /// Formats assembled prompt text for in-game inspection when prompt test mode is enabled.
    /// </summary>
    internal static class DiaryPromptCapture
    {
        public const string SystemHeader = "SYSTEM PROMPT";
        public const string UserHeader = "USER PROMPT";

        /// <summary>
        /// Returns a stable, human-readable combined prompt. Null parts are shown as blank so the
        /// card still proves which side of the request was empty.
        /// </summary>
        public static string Format(string systemPrompt, string userPrompt)
        {
            string system = systemPrompt ?? string.Empty;
            string separator = string.IsNullOrEmpty(system) ? "\n" : "\n\n";
            return SystemHeader
                + "\n"
                + system
                + separator
                + UserHeader
                + "\n"
                + (userPrompt ?? string.Empty);
        }
    }
}

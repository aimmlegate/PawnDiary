// Small formatter for literal prompt/UI text entered through settings. XML usually stores Keyed
// translation keys and lets Verse format {0}/{1}, but player overrides are plain strings on Defs.
// This helper gives those plain strings the same simple placeholder behavior without letting a bad
// brace in a hand-edited setting crash diary generation.
using System;
using System.Globalization;

namespace PawnDiary
{
    /// <summary>Formats literal settings text with numbered placeholders, falling back safely.</summary>
    internal static class PromptTextTemplate
    {
        public static string Format(string template, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return string.Empty;
            }

            try
            {
                return string.Format(CultureInfo.InvariantCulture, template, args ?? new object[0]);
            }
            catch (FormatException)
            {
                return template;
            }
        }
    }
}

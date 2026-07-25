// Pure escaping for values stored in DiaryEvent.gameContext. The save format is a compact
// semicolon-delimited list of key=value fields, so any value copied from a pawn name, Def label,
// mod-provided text, or localized prose must be flattened before it enters that structure.
using System.Text;

namespace PawnDiary
{
    /// <summary>
    /// Keeps one value inside its own semicolon-delimited <c>key=value</c> game-context field.
    /// </summary>
    internal static class GameContextValue
    {
        /// <summary>
        /// Replaces field delimiters and control characters with readable one-line alternatives.
        /// Keys and the separators between fields are schema owned and must not pass through here.
        /// </summary>
        public static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder cleaned = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (character == ';')
                {
                    cleaned.Append(',');
                }
                else if (character == '=')
                {
                    cleaned.Append('-');
                }
                else if (char.IsControl(character))
                {
                    cleaned.Append(' ');
                }
                else
                {
                    cleaned.Append(character);
                }
            }

            return cleaned.ToString().Trim();
        }
    }
}

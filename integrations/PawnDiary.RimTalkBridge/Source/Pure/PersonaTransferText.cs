// Pure formatting for the Pawn Diary -> RimTalk persona payload.
namespace PawnDiaryRimTalkBridge.Pure
{
    /// <summary>Combines independent outlook and writing-style rules without game dependencies.</summary>
    internal static class PersonaTransferText
    {
        public static string Combine(string outlook, string style)
        {
            string left = (outlook ?? string.Empty).Trim();
            string right = (style ?? string.Empty).Trim();
            if (left.Length == 0) return right;
            if (right.Length == 0) return left;
            return left + "\n" + right;
        }
    }
}

// Pure builder for the compact transcript/context lines attached to a conversation diary entry.
// NO RimWorld / RimTalk usings — file-linked into the pure test project.
//
// Output is a small list of "key=value" lines. The KEYS are a fixed English schema (talk_type=,
// exchanges=, said_N=) — a machine-readable carve-out, NOT prose — while the VALUES are game/chat
// text. Pawn Diary reads these as factual evidence when it writes the entry.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System.Collections.Generic;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>Turns a finished conversation into compact prompt-context lines.</summary>
    public static class ConversationContext
    {
        // Per-quoted-line character cap. Defensive limit so one long chat line cannot blow the prompt
        // budget; kept in code as a parser-style safety value (AGENTS.md), not player tuning.
        private const int MaxQuotedLineChars = 160;

        /// <summary>
        /// Builds context lines: the dominant talk kind, the exchange count, then up to
        /// <paramref name="transcriptLineCap"/> quoted lines "said_N=&lt;speaker&gt;: &lt;text&gt;".
        /// </summary>
        public static List<string> BuildExtraContext(Conversation conversation, int transcriptLineCap)
        {
            List<string> context = new List<string>();
            if (conversation == null || conversation.Lines == null)
            {
                return context;
            }

            context.Add("talk_type=" + DominantKind(conversation).ToString().ToLowerInvariant());
            context.Add("exchanges=" + conversation.Lines.Count);

            int quoted = 0;
            for (int i = 0; i < conversation.Lines.Count; i++)
            {
                if (transcriptLineCap > 0 && quoted >= transcriptLineCap)
                {
                    break;
                }

                ConversationLine line = conversation.Lines[i];
                if (line == null)
                {
                    continue;
                }

                string text = ContextFormat.CapAtWord(CleanOneLine(line.Text), MaxQuotedLineChars);
                if (text.Length == 0)
                {
                    continue;
                }

                string speaker = string.IsNullOrEmpty(line.SpeakerName) ? "?" : line.SpeakerName;
                context.Add("said_" + (quoted + 1) + "=" + speaker + ": " + text);
                quoted++;
            }

            return context;
        }

        /// <summary>Returns the most frequent talk kind in the conversation (ties: first seen).</summary>
        public static BridgeTalkKind DominantKind(Conversation conversation)
        {
            if (conversation == null || conversation.Lines == null || conversation.Lines.Count == 0)
            {
                return BridgeTalkKind.Other;
            }

            Dictionary<BridgeTalkKind, int> counts = new Dictionary<BridgeTalkKind, int>();
            BridgeTalkKind best = BridgeTalkKind.Other;
            int bestCount = -1;

            for (int i = 0; i < conversation.Lines.Count; i++)
            {
                ConversationLine line = conversation.Lines[i];
                if (line == null)
                {
                    continue;
                }

                int count;
                counts.TryGetValue(line.Kind, out count);
                count++;
                counts[line.Kind] = count;

                if (count > bestCount)
                {
                    bestCount = count;
                    best = line.Kind;
                }
            }

            return best;
        }

        private static string CleanOneLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
        }
    }
}

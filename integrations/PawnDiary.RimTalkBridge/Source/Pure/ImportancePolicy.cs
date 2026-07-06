// Pure importance policy: decides which finished conversations deserve their own diary entry, so the
// bridge records standout moments and leaves ordinary chatter to the ambient day-note. NO RimWorld /
// RimTalk usings — file-linked into the pure test project.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Rules for "was this conversation important enough to write down?" A monologue never is. Beyond
    /// that, a conversation qualifies if any line carries weighty news or social charge, or if it simply
    /// ran long. Thresholds are passed in (settings-backed), not hardcoded here.
    /// </summary>
    public static class ImportancePolicy
    {
        /// <summary>
        /// True when the conversation is worth an explicit diary entry: at least two participants AND
        /// (any weighty talk kind, OR any charged social line, OR at least <paramref name="minReplies"/>
        /// lines).
        /// </summary>
        public static bool IsImportant(Conversation conversation, int minReplies)
        {
            return Explain(conversation, minReplies).Length > 0;
        }

        /// <summary>
        /// Returns the first matched reason (for developer logging), or "" when the conversation is not
        /// important. Kept in lockstep with <see cref="IsImportant"/> so one implementation drives both.
        /// </summary>
        public static string Explain(Conversation conversation, int minReplies)
        {
            if (conversation == null || conversation.Lines == null || conversation.Lines.Count == 0)
            {
                return string.Empty;
            }

            // Monologues (a single participant) are never important, however long they run.
            if (conversation.ParticipantIds().Count < 2)
            {
                return string.Empty;
            }

            for (int i = 0; i < conversation.Lines.Count; i++)
            {
                ConversationLine line = conversation.Lines[i];
                if (line == null)
                {
                    continue;
                }

                if (IsWeightyKind(line.Kind))
                {
                    return "talk_kind=" + line.Kind;
                }

                if (IsChargedSocial(line.Social))
                {
                    return "social=" + line.Social;
                }
            }

            if (conversation.Lines.Count >= minReplies)
            {
                return "length=" + conversation.Lines.Count + ">=" + minReplies;
            }

            return string.Empty;
        }

        // Talk categories that always matter: urgent news, in-world events, and quest beats.
        private static bool IsWeightyKind(BridgeTalkKind kind)
        {
            return kind == BridgeTalkKind.Urgent
                || kind == BridgeTalkKind.Event
                || kind == BridgeTalkKind.QuestOffer
                || kind == BridgeTalkKind.QuestEnd;
        }

        // Social lines with emotional charge: an insult, a slight, or a kindness lands harder than
        // neutral chat, so even a short exchange containing one is worth recording.
        private static bool IsChargedSocial(BridgeSocialKind social)
        {
            return social == BridgeSocialKind.Insult
                || social == BridgeSocialKind.Slight
                || social == BridgeSocialKind.Kind;
        }
    }
}

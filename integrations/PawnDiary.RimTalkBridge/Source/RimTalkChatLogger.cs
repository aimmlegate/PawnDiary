// Logging adapter for RimTalk chat observations. The bridge is intentionally diagnostic for now:
// prove we can see RimTalk chat and read recent Pawn Diary context before we generate any new diary
// entries from chat or register prompt variables.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Text;
using PawnDiary.Integration;
using RimTalk.Data;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Formats RimTalk chat facts and related Pawn Diary context snapshots into developer logs.
    /// </summary>
    public static class RimTalkChatLogger
    {
        private const int RecentContextCount = 3;
        private const int MaxLoggedChatText = 500;
        private const int MaxLoggedTitle = 160;

        public static void LogDisplayedChat(Pawn speaker, TalkResponse talk)
        {
            if (PawnDiaryRimTalkBridgeMod.Settings == null
                || !PawnDiaryRimTalkBridgeMod.Settings.enabled
                || talk == null)
            {
                return;
            }

            try
            {
                Pawn target = SafeTarget(talk);
                Log.Message(
                    PawnDiaryRimTalkBridgeMod.LogPrefix
                    + " RimTalk chat: speaker=" + PawnLabel(speaker, SafeString(talk.Name))
                    + " target=" + PawnLabel(target, SafeString(talk.TargetName))
                    + " talkType=" + SafeString(talk.TalkType.ToString())
                    + " interactionRaw=" + CleanForLog(talk.InteractionRaw, MaxLoggedTitle)
                    + " interaction=" + SafeInteractionDefName(talk)
                    + " parentTalkId=" + SafeString(talk.ParentTalkId.ToString())
                    + " id=" + SafeString(talk.Id.ToString())
                    + " text=\"" + CleanForLog(SafeText(talk), MaxLoggedChatText) + "\"");

                LogRecentDiaryContext("speaker", speaker);
                LogWritingStyle("speaker", speaker);
                if (!SamePawn(speaker, target))
                {
                    LogRecentDiaryContext("target", target);
                    LogWritingStyle("target", target);
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce(
                    PawnDiaryRimTalkBridgeMod.LogPrefix + " failed while logging RimTalk chat: " + e,
                    "PawnDiaryRimTalkBridge.LogChat.Exception".GetHashCode());
            }
        }

        private static void LogRecentDiaryContext(string role, Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            DiaryContextSnapshot snapshot = PawnDiaryApi.GetContextSnapshot(pawn, RecentContextCount);
            if (snapshot == null || snapshot.entries == null || snapshot.entries.Count == 0)
            {
                Log.Message(
                    PawnDiaryRimTalkBridgeMod.LogPrefix
                    + " recent Pawn Diary context for " + role + "=" + PawnLabel(pawn, string.Empty)
                    + ": none");
                return;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < snapshot.entries.Count; i++)
            {
                DiaryEntryProseSnapshot entry = snapshot.entries[i];
                if (i > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(i + 1);
                builder.Append(". ");
                builder.Append(TitleOrGroup(entry));
                if (!string.IsNullOrWhiteSpace(entry.date))
                {
                    builder.Append(" (");
                    builder.Append(CleanForLog(entry.date, MaxLoggedTitle));
                    builder.Append(")");
                }

                if (!string.IsNullOrWhiteSpace(entry.summary))
                {
                    builder.Append(": ");
                    builder.Append(CleanForLog(entry.summary, MaxLoggedChatText));
                }
            }

            Log.Message(
                PawnDiaryRimTalkBridgeMod.LogPrefix
                + " recent Pawn Diary context for " + role + "=" + PawnLabel(pawn, string.Empty)
                + ": " + builder);
        }

        private static void LogWritingStyle(string role, Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            // Publish-only proof step: Pawn Diary exposes the pawn's base writing style; the bridge
            // just logs it. It never feeds the style back into RimTalk — syncing voices is the
            // player's own choice.
            DiaryWritingStyleSnapshot style = PawnDiaryApi.GetWritingStyle(pawn);
            if (style == null)
            {
                Log.Message(
                    PawnDiaryRimTalkBridgeMod.LogPrefix
                    + " Pawn Diary writing style for " + role + "=" + PawnLabel(pawn, string.Empty)
                    + ": none");
                return;
            }

            Log.Message(
                PawnDiaryRimTalkBridgeMod.LogPrefix
                + " Pawn Diary writing style for " + role + "=" + PawnLabel(pawn, string.Empty)
                + ": style=" + CleanForLog(style.styleDefName, MaxLoggedTitle)
                + " label=" + CleanForLog(style.label, MaxLoggedTitle)
                + " rule=\"" + CleanForLog(style.rule, MaxLoggedChatText) + "\"");
        }

        private static string TitleOrGroup(DiaryEntryProseSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.title))
            {
                return CleanForLog(snapshot.title, MaxLoggedTitle);
            }

            if (!string.IsNullOrWhiteSpace(snapshot.groupLabel))
            {
                return CleanForLog(snapshot.groupLabel, MaxLoggedTitle);
            }

            return CleanForLog(snapshot.eventId, MaxLoggedTitle);
        }

        private static Pawn SafeTarget(TalkResponse talk)
        {
            try
            {
                return talk.GetTarget();
            }
            catch
            {
                return null;
            }
        }

        private static string SafeText(TalkResponse talk)
        {
            try
            {
                return talk.GetText();
            }
            catch
            {
                return talk.Text;
            }
        }

        private static string SafeInteractionDefName(TalkResponse talk)
        {
            try
            {
                object interaction = talk.GetInteractionType();
                return interaction == null ? string.Empty : SafeString(interaction.ToString());
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool SamePawn(Pawn left, Pawn right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return string.Equals(left.GetUniqueLoadID(), right.GetUniqueLoadID(), StringComparison.Ordinal);
        }

        private static string PawnLabel(Pawn pawn, string fallback)
        {
            if (pawn != null)
            {
                return CleanForLog(pawn.LabelShortCap, MaxLoggedTitle);
            }

            return CleanForLog(fallback, MaxLoggedTitle);
        }

        private static string SafeString(string value)
        {
            return value ?? string.Empty;
        }

        private static string CleanForLog(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string cleaned = text
                .Replace("\r\n", " ")
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Replace("\"", "'");
            cleaned = cleaned.Trim();

            if (maxLength > 0 && cleaned.Length > maxLength)
            {
                return cleaned.Substring(0, maxLength) + "...";
            }

            return cleaned;
        }
    }
}

// Logging adapter for RimTalk chat observations. The first bridge milestone is intentionally
// diagnostic: prove we can see RimTalk chat and read recent Pawn Diary titles before we generate
// any new diary entries from chat.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Generic;
using System.Text;
using PawnDiary.Integration;
using RimTalk.Data;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Formats RimTalk chat facts and related Pawn Diary title snapshots into developer logs.
    /// </summary>
    public static class RimTalkChatLogger
    {
        private const int RecentTitleCount = 3;
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

                LogRecentDiaryTitles("speaker", speaker);
                if (!SamePawn(speaker, target))
                {
                    LogRecentDiaryTitles("target", target);
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce(
                    PawnDiaryRimTalkBridgeMod.LogPrefix + " failed while logging RimTalk chat: " + e,
                    "PawnDiaryRimTalkBridge.LogChat.Exception".GetHashCode());
            }
        }

        private static void LogRecentDiaryTitles(string role, Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            List<DiaryEntryTitleSnapshot> snapshots = PawnDiaryApi.GetRecentEntryTitles(pawn, RecentTitleCount);
            if (snapshots == null || snapshots.Count == 0)
            {
                Log.Message(
                    PawnDiaryRimTalkBridgeMod.LogPrefix
                    + " recent Pawn Diary titles for " + role + "=" + PawnLabel(pawn, string.Empty)
                    + ": none");
                return;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < snapshots.Count; i++)
            {
                DiaryEntryTitleSnapshot snapshot = snapshots[i];
                if (i > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(i + 1);
                builder.Append(". ");
                builder.Append(TitleOrGroup(snapshot));
                if (!string.IsNullOrWhiteSpace(snapshot.date))
                {
                    builder.Append(" (");
                    builder.Append(CleanForLog(snapshot.date, MaxLoggedTitle));
                    builder.Append(")");
                }
            }

            Log.Message(
                PawnDiaryRimTalkBridgeMod.LogPrefix
                + " recent Pawn Diary titles for " + role + "=" + PawnLabel(pawn, string.Empty)
                + ": " + builder);
        }

        private static string TitleOrGroup(DiaryEntryTitleSnapshot snapshot)
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

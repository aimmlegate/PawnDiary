// Payload + pure decision for a quest outcome event (Quest.End hook). Quests are colony-wide: when
// an accepted quest ends in success or failure, every eligible colonist gets their own solo diary
// entry so each survivor can react to the shared outcome.
//
// One DiaryEventType.Quest value covers all three signals; the Signal field ("accepted",
// "completed", "failed") routes the event to the right XML group via ClassifyQuest(signal), which
// is exactly how ArrivalEventData carries a synthetic defName so one Decide can route many cases.
//
// Accepted quests are only marked as seen / offered to generic event-window policy. They do not
// generate diary pages because the mod cannot reliably know which pawn actually worked the quest.
// Only completed and failed outcomes become diary entries, framed as colony effort.
//
// Rich context (quest description, issuer faction defName, rewards summary) is captured upstream
// and embedded in the localized event text (description) and the game-context marker (rewards +
// faction). The event carries a "quest=" marker so the UI classifies it into the Quest domain.
// "unknown"/"none" are the English sentinels for absent faction/rewards (AGENTS.md section 12).
using System;
using System.Text;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one colonist reacting to a completed or failed quest. Filled by
    /// DiaryGameComponent.RecordQuestEnded inside the per-pawn fan-out loop.
    /// </summary>
    public class QuestEventData : DiaryEventData
    {
        /// <summary>Signal for a freshly accepted quest. Also the XML group classifier key.</summary>
        public const string SignalAccepted = "accepted";
        /// <summary>Signal for a quest that ended in QuestEndOutcome.Success.</summary>
        public const string SignalCompleted = "completed";
        /// <summary>Signal for a quest that ended in QuestEndOutcome.Fail.</summary>
        public const string SignalFailed = "failed";

        /// <summary>Sentinel substituted when the quest exposes no issuer faction.</summary>
        public const string FactionUnknown = "unknown";
        /// <summary>Sentinel substituted when the quest exposes no scannable rewards.</summary>
        public const string RewardsNone = "none";

        public override DiaryEventType EventType => DiaryEventType.Quest;

        /// <summary>The lifecycle signal: "accepted", "completed", or "failed". Also the key passed
        /// to InteractionGroups.ClassifyQuest so the right prompt group is selected.</summary>
        public string Signal;

        /// <summary>The quest's defName (QuestScriptDef.defName).</summary>
        public string DefName;

        /// <summary>The quest's cleaned display label, pre-resolved by the caller.</summary>
        public string Label;

        /// <summary>The issuer/requester faction's defName, or <see cref="FactionUnknown"/>.</summary>
        public string FactionDefName;

        /// <summary>Short cleaned reward summary, or <see cref="RewardsNone"/>.</summary>
        public string Rewards;

        /// <summary>
        /// Chooses the model/UI-facing quest label from the values exposed by RimWorld. Some quests
        /// can surface placeholder or defName-style labels such as "QuestName" or
        /// "OpportunityQuest_Friendlies"; those are rejected or humanized before reaching prompts.
        /// </summary>
        public static string BuildDisplayLabel(string generatedName, string rootLabel, string defName)
        {
            string label = NormalizeLabelCandidate(generatedName);
            if (!string.IsNullOrEmpty(label))
            {
                return label;
            }

            label = NormalizeLabelCandidate(rootLabel);
            if (!string.IsNullOrEmpty(label))
            {
                return label;
            }

            label = HumanizeQuestToken(defName);
            return string.IsNullOrEmpty(label) ? FactionUnknown : label;
        }

        /// <summary>
        /// Pure decision for one colonist's quest entry. Empty or accepted-only signals drop; only
        /// completed and failed outcomes generate diary pages.
        /// </summary>
        public static CaptureDecision Decide(QuestEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null)
            {
                return CaptureDecision.Drop;
            }

            // Accepted quests are useful to mark as seen and for generic event-window policy, but not
            // as diary pages. We only trust completed/failed outcomes enough to write shared-effort
            // entries for every eligible colonist.
            if (!IsDiaryOutcomeSignal(data.Signal))
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled)
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSolo;
        }

        /// <summary>
        /// Returns true for quest lifecycle signals that are allowed to become diary pages.
        /// </summary>
        public static bool IsDiaryOutcomeSignal(string signal)
        {
            return string.Equals(signal, SignalCompleted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(signal, SignalFailed, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Pure assembly of the quest game-context marker. The leading "quest=" marker is
        /// load-bearing: the UI parses it to classify the event into the Quest domain. The signal
        /// field routes prompt group selection (accepted/completed/failed). The duplicated
        /// quest_* fields are model-facing prompt fields with names that cannot collide with generic
        /// labels from other event types. The quest's prose
        /// description is intentionally NOT embedded here — it is prose, so it lives in the localized
        /// event text instead. Field order is locked by tests in DiaryCapturePolicyTests.
        /// </summary>
        public static string BuildGameContext(string defName, string signal, string label, string factionDefName, string rewards)
        {
            return "quest=" + defName
                + "; signal=" + signal
                + "; label=" + label
                + "; faction=" + factionDefName
                + "; rewards=" + rewards
                + "; quest_label=" + label
                + "; quest_signal=" + signal
                + "; quest_faction=" + factionDefName
                + "; quest_rewards=" + rewards;
        }

        private static string NormalizeLabelCandidate(string value)
        {
            string cleaned = CleanOneLine(value);
            if (string.IsNullOrEmpty(cleaned) || IsQuestNamePlaceholder(cleaned))
            {
                return string.Empty;
            }

            return LooksLikeCodeToken(cleaned) ? HumanizeQuestToken(cleaned) : cleaned;
        }

        private static string HumanizeQuestToken(string value)
        {
            string cleaned = CleanOneLine(value);
            if (string.IsNullOrEmpty(cleaned) || IsQuestNamePlaceholder(cleaned))
            {
                return string.Empty;
            }

            string withWordBreaks = AddWordBreaks(cleaned);
            string withoutQuestWord = RemoveQuestWord(withWordBreaks);
            return CleanOneLine(withoutQuestWord);
        }

        private static bool IsQuestNamePlaceholder(string value)
        {
            string cleaned = CleanOneLine(value);
            if (string.IsNullOrEmpty(cleaned))
            {
                return false;
            }

            StringBuilder compact = new StringBuilder();
            for (int i = 0; i < cleaned.Length; i++)
            {
                char c = cleaned[i];
                if (char.IsLetterOrDigit(c))
                {
                    compact.Append(c);
                }
            }

            return string.Equals(compact.ToString(), "QuestName", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeCodeToken(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (value.IndexOf('_') >= 0 || value.IndexOf('-') >= 0)
            {
                return true;
            }

            if (value.IndexOf(' ') >= 0)
            {
                return false;
            }

            if (value.IndexOf("Quest", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            for (int i = 1; i < value.Length; i++)
            {
                if (ShouldInsertWordBreak(value[i - 1], value[i], i + 1 < value.Length ? value[i + 1] : '\0'))
                {
                    return true;
                }
            }

            return false;
        }

        private static string AddWordBreaks(string value)
        {
            StringBuilder sb = new StringBuilder();
            char previousOutput = '\0';
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (current == '_' || current == '-' || current == '.' || current == '/')
                {
                    AppendSpace(sb);
                    previousOutput = ' ';
                    continue;
                }

                char next = i + 1 < value.Length ? value[i + 1] : '\0';
                if (sb.Length > 0 && ShouldInsertWordBreak(previousOutput, current, next))
                {
                    AppendSpace(sb);
                }

                sb.Append(current);
                previousOutput = current;
            }

            return CleanOneLine(sb.ToString());
        }

        private static bool ShouldInsertWordBreak(char previous, char current, char next)
        {
            if (previous == '\0' || char.IsWhiteSpace(previous) || char.IsWhiteSpace(current))
            {
                return false;
            }

            if (char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous)))
            {
                return true;
            }

            if (char.IsUpper(previous) && char.IsUpper(current) && next != '\0' && char.IsLower(next))
            {
                return true;
            }

            if (char.IsDigit(current) && char.IsLetter(previous))
            {
                return true;
            }

            return char.IsLetter(current) && char.IsDigit(previous);
        }

        private static string RemoveQuestWord(string value)
        {
            string[] parts = CleanOneLine(value).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i], "Quest", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(parts[i]);
            }

            return sb.ToString();
        }

        private static string CleanOneLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            bool insideTag = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '<')
                {
                    insideTag = true;
                    continue;
                }

                if (c == '>' && insideTag)
                {
                    insideTag = false;
                    continue;
                }

                if (!insideTag)
                {
                    sb.Append(char.IsWhiteSpace(c) ? ' ' : c);
                }
            }

            return CollapseSpaces(sb.ToString());
        }

        private static string CollapseSpaces(string value)
        {
            StringBuilder sb = new StringBuilder();
            bool pendingSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static void AppendSpace(StringBuilder sb)
        {
            if (sb.Length > 0 && sb[sb.Length - 1] != ' ')
            {
                sb.Append(' ');
            }
        }
    }
}

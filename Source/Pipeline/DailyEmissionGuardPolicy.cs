// Pure reconstruction policy for transient one-page-per-day guards. Ambient interaction and thought
// notes are saved as ordinary DiaryEvent/ArchivedDiaryEntry rows, while their fast guard sets are not
// serialized. The lifecycle adapter projects saved strings/ticks into this helper after load so a
// save/reload during the same in-game day cannot write the same ambient page twice.
using System;
using System.Globalization;

namespace PawnDiary
{
    /// <summary>Recognizes saved ambient-day rows and recreates their exact runtime guard keys.</summary>
    internal static class DailyEmissionGuardPolicy
    {
        public const string AmbientBatchToken = "ambient_day_note";
        public const string AmbientThoughtDefName = "ThoughtAmbientDay";

        /// <summary>Builds the exact interaction-note key used while the game is running.</summary>
        public static string InteractionKey(string groupKey, string pawnId, int dayIndex)
        {
            return groupKey + "|ambient|" + pawnId + "|" + dayIndex;
        }

        /// <summary>Builds the exact thought-note key used while the game is running.</summary>
        public static string ThoughtKey(string pawnId, int dayIndex)
        {
            return "thoughtAmbient|" + pawnId + "|" + dayIndex;
        }

        /// <summary>True when an exact ambient-interaction guard belongs to the requested day.</summary>
        public static bool IsInteractionKeyForDay(string key, int dayIndex)
        {
            if (string.IsNullOrWhiteSpace(key)
                || key.IndexOf("|ambient|", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            int separator = key.LastIndexOf('|');
            int parsedDay;
            return separator >= 0
                && separator + 1 < key.Length
                && int.TryParse(
                    key.Substring(separator + 1),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out parsedDay)
                && parsedDay == dayIndex;
        }

        /// <summary>
        /// Recognizes one hot or archived history row for the current day. The saved <c>day=</c>
        /// marker is authoritative because a previous-day note may be flushed after midnight; the
        /// tick-derived day is only a backward-compatible fallback for incomplete older rows.
        /// </summary>
        public static bool TryBuildCurrentDayKeys(
            string pawnId,
            string interactionDefName,
            string gameContext,
            int fallbackDay,
            int currentDay,
            out string interactionKey,
            out string thoughtKey)
        {
            interactionKey = string.Empty;
            thoughtKey = string.Empty;
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            int savedDay = fallbackDay;
            string dayValue = DiaryContextFields.Value(gameContext, "day");
            int parsedDay;
            if (!string.IsNullOrWhiteSpace(dayValue)
                && int.TryParse(
                    dayValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out parsedDay))
            {
                savedDay = parsedDay;
            }

            if (savedDay != currentDay)
            {
                return false;
            }

            bool isThought = string.Equals(
                    interactionDefName,
                    AmbientThoughtDefName,
                    StringComparison.OrdinalIgnoreCase)
                || DiaryContextFields.FieldEquals(
                    gameContext,
                    "thought",
                    AmbientThoughtDefName);
            if (isThought)
            {
                thoughtKey = ThoughtKey(pawnId, savedDay);
                return true;
            }

            if (!DiaryContextFields.FieldEquals(
                    gameContext,
                    "batch",
                    AmbientBatchToken))
            {
                return false;
            }

            string groupKey = DiaryContextFields.Value(gameContext, "group");
            if (string.IsNullOrWhiteSpace(groupKey))
            {
                return false;
            }

            interactionKey = InteractionKey(groupKey, pawnId, savedDay);
            return true;
        }
    }
}

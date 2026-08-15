// Pure reconstruction policy for transient one-page-per-day guards. Ambient interaction and thought
// notes are saved as ordinary DiaryEvent/ArchivedDiaryEntry rows, while their fast guard sets are not
// serialized. The lifecycle adapter projects saved strings/ticks into this helper after load so a
// save/reload during the same in-game day cannot write the same ambient page twice. It also owns the
// exact composite-key check used to re-baseline one pawn's transient day-start opinions on Brainwipe.
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
        /// True when an exact ambient-interaction guard belongs to one pawn. The separators are part
        /// of the match so similarly-prefixed load IDs (for example Pawn_1 and Pawn_10) never collide.
        /// </summary>
        public static bool IsInteractionKeyForPawn(string key, string pawnId)
        {
            return !string.IsNullOrWhiteSpace(key)
                && !string.IsNullOrWhiteSpace(pawnId)
                && key.IndexOf(
                    "|ambient|" + pawnId + "|",
                    StringComparison.Ordinal) >= 0;
        }

        /// <summary>True when an exact ambient-thought guard belongs to one pawn.</summary>
        public static bool IsThoughtKeyForPawn(string key, string pawnId)
        {
            return !string.IsNullOrWhiteSpace(key)
                && !string.IsNullOrWhiteSpace(pawnId)
                && key.StartsWith(
                    "thoughtAmbient|" + pawnId + "|",
                    StringComparison.Ordinal);
        }

        /// <summary>
        /// True when a pawn/day key belongs to one pawn. Day-reflection and pending-Hediff stores use
        /// this compact shape; the exact separator prevents prefix collisions between pawn IDs.
        /// </summary>
        public static bool IsPawnDayKey(string key, string pawnId)
        {
            return !string.IsNullOrWhiteSpace(key)
                && !string.IsNullOrWhiteSpace(pawnId)
                && key.StartsWith(pawnId + "|", StringComparison.Ordinal);
        }

        /// <summary>
        /// True when a day-start opinion key's first composite segment is the exact requested pawn.
        /// Brainwipe re-baselines only that pawn's outbound opinions; inbound <c>other|pawn</c> rows
        /// remain another colonist's independent observation.
        /// </summary>
        public static bool IsOutboundOpinionKeyForPawn(string key, string pawnId)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            int separator = key.IndexOf('|');
            return separator > 0
                && separator + 1 < key.Length
                && string.Equals(
                    key.Substring(0, separator),
                    pawnId,
                    StringComparison.Ordinal);
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

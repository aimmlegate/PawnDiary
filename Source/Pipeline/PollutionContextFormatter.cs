// Pure qualitative prompt context for map-pollution transitions. Raw fractions, cells, ticks,
// map IDs, and severity ranks have no output field, so they cannot leak into a saved prompt.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Stable schema tokens for pollution context stored on diary events.</summary>
    internal static class PollutionContextTokens
    {
        public const string Meaningful = "meaningful";
        public const string Severe = "severe";
        public const string Critical = "critical";
        public const string Start = "start";
        public const string Escalated = "escalated";
        public const string Reclaimed = "reclaimed";
        public const string AmbientPressure = "ambient_pressure";
    }

    /// <summary>Formats only qualitative, bounded pollution facts for full/balanced/compact prompts.</summary>
    internal static class PollutionContextFormatter
    {
        public static string BandForSeverity(int severityRank)
        {
            if (severityRank >= 3)
            {
                return PollutionContextTokens.Critical;
            }

            return severityRank == 2
                ? PollutionContextTokens.Severe
                : PollutionContextTokens.Meaningful;
        }

        public static string TransitionFor(int severityRank, bool isStart)
        {
            if (!isStart)
            {
                return PollutionContextTokens.Reclaimed;
            }

            return severityRank <= 1
                ? PollutionContextTokens.Start
                : PollutionContextTokens.Escalated;
        }

        public static string Format(string band, string transition, string mapLabel, string detailLevel)
        {
            string cleanBand = KnownBand(band) ? band.Trim().ToLowerInvariant() : string.Empty;
            string cleanTransition = KnownTransition(transition)
                ? transition.Trim().ToLowerInvariant()
                : string.Empty;
            if (cleanBand.Length == 0 || cleanTransition.Length == 0)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>
            {
                "pollution_band=" + cleanBand,
                "pollution_transition=" + cleanTransition
            };
            if (!string.Equals(NarrativeDetailLevelTokens.Normalize(detailLevel),
                NarrativeDetailLevelTokens.Compact, StringComparison.Ordinal))
            {
                string cleanMap = Clean(mapLabel, 120);
                if (cleanMap.Length > 0)
                {
                    parts.Add("map_label=" + cleanMap);
                }
            }

            parts.Add("facet=" + PollutionContextTokens.AmbientPressure);
            return string.Join("; ", parts.ToArray());
        }

        /// <summary>
        /// Reprojects a stored full pollution snapshot for one request's prompt-detail preset.
        /// Other event contexts pass through byte-for-byte.
        /// </summary>
        public static string ProjectForDetail(string gameContext, string detailLevel)
        {
            if (!string.Equals(
                DiaryContextFields.Value(gameContext, "facet"),
                PollutionContextTokens.AmbientPressure,
                StringComparison.OrdinalIgnoreCase))
            {
                return gameContext ?? string.Empty;
            }

            return Format(
                DiaryContextFields.Value(gameContext, "pollution_band"),
                DiaryContextFields.Value(gameContext, "pollution_transition"),
                DiaryContextFields.Value(gameContext, "map_label"),
                detailLevel);
        }

        private static bool KnownBand(string value)
        {
            string token = (value ?? string.Empty).Trim();
            return string.Equals(token, PollutionContextTokens.Meaningful, StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, PollutionContextTokens.Severe, StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, PollutionContextTokens.Critical, StringComparison.OrdinalIgnoreCase);
        }

        private static bool KnownTransition(string value)
        {
            string token = (value ?? string.Empty).Trim();
            return string.Equals(token, PollutionContextTokens.Start, StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, PollutionContextTokens.Escalated, StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, PollutionContextTokens.Reclaimed, StringComparison.OrdinalIgnoreCase);
        }

        private static string Clean(string value, int maximum)
        {
            string cleaned = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ')
                .Replace(';', ',').Replace('=', '-').Trim();
            return cleaned.Length <= maximum
                ? cleaned
                : cleaned.Substring(0, maximum).TrimEnd();
        }
    }
}

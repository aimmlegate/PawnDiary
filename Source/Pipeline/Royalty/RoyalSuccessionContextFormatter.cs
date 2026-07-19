// Pure prompt-context projection for Royalty succession. Only truthful player-readable facts cross
// this boundary; correlation IDs, ticks, internal flags, and save tokens never enter the prompt.
using System.Text;

namespace PawnDiary
{
    /// <summary>Formats the four append-only Phase-5 succession context keys.</summary>
    internal static class RoyalSuccessionContextFormatter
    {
        public static string Format(RoyalSuccessionFact fact, int maximumCharacters)
        {
            if (fact == null) return string.Empty;
            StringBuilder builder = new StringBuilder();
            Append(builder, "succession_deceased", Clean(fact.deceasedPawnName, maximumCharacters));
            Append(builder, "succession_heir", Clean(fact.heirPawnName, maximumCharacters));
            Append(builder, "succession_title", Clean(fact.inheritedTitleLabel, maximumCharacters));
            Append(builder, "succession_faction", Clean(fact.factionName, maximumCharacters));
            return builder.ToString();
        }

        public static string FormatAppointment(RoyalHeirAppointmentSnapshot fact, int maximumCharacters)
        {
            if (!RoyalSuccessionPolicy.ValidAppointment(fact)) return string.Empty;
            StringBuilder builder = new StringBuilder();
            Append(builder, "succession_heir", Clean(fact.heirPawnName, maximumCharacters));
            Append(builder, "succession_title", Clean(fact.titleLabel, maximumCharacters));
            Append(builder, "succession_faction", Clean(fact.factionName, maximumCharacters));
            return builder.ToString();
        }

        private static string Clean(string value, int requestedCap)
        {
            string cleaned = (value ?? string.Empty).Replace(";", ",").Replace("\r", " ")
                .Replace("\n", " ").Trim();
            int cap = requestedCap < 1 || requestedCap > 512 ? 120 : requestedCap;
            return cleaned.Length <= cap ? cleaned : cleaned.Substring(0, cap).TrimEnd();
        }

        private static void Append(StringBuilder builder, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (builder.Length > 0) builder.Append("; ");
            builder.Append(key).Append('=').Append(value.Trim());
        }
    }
}

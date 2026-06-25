// Pure codec for saved diary text-decoration pawn facts.
//
// DiaryEvent already stores event metadata. This codec handles only the small hediff/trait snapshot
// attached to an event so decoration rules can be re-selected after save/load without reading a live
// Pawn. Tokens stay deliberately simple because they are saved data.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PawnDiary
{
    /// <summary>
    /// Serializes and restores hediff/trait facts used by decoration rule matching.
    /// </summary>
    internal static class DiaryTextDecorationFactCodec
    {
        internal static string SerializePawnFacts(DiaryTextDecorationContext context)
        {
            if (context == null)
            {
                return string.Empty;
            }

            StringBuilder result = new StringBuilder();
            if (context.hediffs != null)
            {
                for (int i = 0; i < context.hediffs.Count; i++)
                {
                    DiaryTextDecorationHediffFact hediff = context.hediffs[i];
                    if (hediff == null)
                    {
                        continue;
                    }

                    result.Append("h|");
                    result.Append(CleanSerializedToken(hediff.defName));
                    result.Append("|");
                    result.Append(CleanSerializedToken(hediff.label));
                    result.Append("|");
                    result.Append(hediff.severity.ToString(CultureInfo.InvariantCulture));
                    result.Append("|");
                    result.Append(hediff.visible ? "1" : "0");
                    result.Append("\n");
                }
            }

            if (context.traits != null)
            {
                for (int i = 0; i < context.traits.Count; i++)
                {
                    DiaryTextDecorationTraitFact trait = context.traits[i];
                    if (trait == null)
                    {
                        continue;
                    }

                    result.Append("t|");
                    result.Append(CleanSerializedToken(trait.defName));
                    result.Append("|");
                    result.Append(CleanSerializedToken(trait.label));
                    result.Append("|");
                    result.Append(trait.degree.ToString(CultureInfo.InvariantCulture));
                    result.Append("\n");
                }
            }

            return result.ToString().TrimEnd();
        }

        internal static void AddSerializedPawnFacts(DiaryTextDecorationContext context, string serialized)
        {
            if (context == null || string.IsNullOrWhiteSpace(serialized))
            {
                return;
            }

            if (context.hediffs == null)
            {
                context.hediffs = new List<DiaryTextDecorationHediffFact>();
            }

            if (context.traits == null)
            {
                context.traits = new List<DiaryTextDecorationTraitFact>();
            }

            string normalized = serialized.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split('|');
                if (parts.Length >= 5 && string.Equals(parts[0], "h", StringComparison.Ordinal))
                {
                    float severity;
                    if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out severity))
                    {
                        severity = 0f;
                    }

                    context.hediffs.Add(new DiaryTextDecorationHediffFact
                    {
                        defName = parts[1],
                        label = parts[2],
                        severity = severity,
                        visible = !string.Equals(parts[4], "0", StringComparison.Ordinal)
                    });
                    continue;
                }

                if (parts.Length >= 4 && string.Equals(parts[0], "t", StringComparison.Ordinal))
                {
                    int degree;
                    if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out degree))
                    {
                        degree = 0;
                    }

                    context.traits.Add(new DiaryTextDecorationTraitFact
                    {
                        defName = parts[1],
                        label = parts[2],
                        degree = degree
                    });
                }
            }
        }

        private static string CleanSerializedToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}

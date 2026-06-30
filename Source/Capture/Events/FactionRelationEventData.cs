// Payload + pure decision for diplomacy moments where the player's relation kind with another
// faction changes. The live Faction.SetRelationDirect hook supplies primitive before/after facts;
// this class owns the "is this a real transition?" decision and saved game-context format.
using System;
using System.Globalization;

namespace PawnDiary.Capture
{
    /// <summary>
    /// Captured facts for one player-facing faction relation transition.
    /// </summary>
    public class FactionRelationEventData : DiaryEventData
    {
        public const string Ally = "ally";
        public const string Hostile = "hostile";
        public const string Neutral = "neutral";

        public override DiaryEventType EventType => DiaryEventType.FactionRelation;

        public string DefName;
        public string Label;
        public string OldRelationKind;
        public string NewRelationKind;
        public string Reason;
        public int OldGoodwill;
        public int NewGoodwill;

        public static CaptureDecision Decide(FactionRelationEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.DefName))
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled || !ctx.SignalEnabled)
            {
                return CaptureDecision.Drop;
            }

            string oldKind = NormalizeRelationKind(data.OldRelationKind);
            string newKind = NormalizeRelationKind(data.NewRelationKind);
            if (string.IsNullOrEmpty(newKind) || string.Equals(oldKind, newKind, StringComparison.OrdinalIgnoreCase))
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSolo;
        }

        public static string NormalizeRelationKind(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            if (string.Equals(trimmed, Ally, StringComparison.OrdinalIgnoreCase))
            {
                return Ally;
            }

            if (string.Equals(trimmed, Hostile, StringComparison.OrdinalIgnoreCase))
            {
                return Hostile;
            }

            if (string.Equals(trimmed, Neutral, StringComparison.OrdinalIgnoreCase))
            {
                return Neutral;
            }

            return string.Empty;
        }

        public static string BuildGameContext(
            string defName,
            string label,
            string oldRelationKind,
            string newRelationKind,
            int oldGoodwill,
            int newGoodwill,
            string reason)
        {
            string context = "faction_relation=" + Clean(defName)
                + "; faction_label=" + Fallback(label, defName)
                + "; previous_relation_kind=" + Fallback(NormalizeRelationKind(oldRelationKind), "unknown")
                + "; relation_kind=" + Fallback(NormalizeRelationKind(newRelationKind), "unknown")
                + "; old_goodwill=" + oldGoodwill.ToString(CultureInfo.InvariantCulture)
                + "; goodwill=" + newGoodwill.ToString(CultureInfo.InvariantCulture);

            string cleanReason = Clean(reason);
            if (!string.IsNullOrWhiteSpace(cleanReason))
            {
                context += "; reason=" + cleanReason;
            }

            return context;
        }

        private static string Fallback(string value, string fallback)
        {
            string clean = Clean(value);
            return string.IsNullOrWhiteSpace(clean) ? Clean(fallback) : clean;
        }

        private static string Clean(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}

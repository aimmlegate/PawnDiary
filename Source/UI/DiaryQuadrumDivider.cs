// Season/quadrum dividers for the Diary tab. Presentation-only: this helper decides where a
// "quadrum · season · year" separator sits between the year's ordered entry cards and builds its
// localized label. It changes nothing about the saved history, the sort order, or the cards.
//
// New to C#/RimWorld? (JS/TS analogy) Think of this as a tiny pure-ish module the tab imports.
// RimWorld measures time in "ticks"; a stored entry keeps the in-game tick it happened on. RimWorld
// splits each 60-day year into four "quadrums" (Aprimay, Jugust, Septober, Decembary) — the diary
// groups entries under the quadrum header they fall in, the same way a blog groups posts by month.
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Computes the quadrum/season grouping key and divider label for a diary entry. Kept separate
    /// from the large tab file so the divider rules read in one place.
    /// </summary>
    internal static class DiaryQuadrumDivider
    {
        // Diary dates are formatted at longitude 0 (GenDate.DateFullStringAt with Vector2.zero), so
        // the quadrum/year we derive here uses the same longitude and therefore always agrees with the
        // month name already printed in each entry's date line.
        private const float Longitude = 0f;

        // Sentinel returned for entries whose tick cannot be placed on the calendar (very old saves
        // that only stored a display string). Two undated entries share this key, so no divider is
        // drawn between them.
        internal const int UndatedKey = int.MinValue;

        /// <summary>
        /// A stable key that is identical for every entry in the same (year, quadrum) and different
        /// across quadrums. Adjacent entries with different keys get a divider between them.
        /// </summary>
        internal static int QuadrumKey(DiaryEntryView entry)
        {
            // Tick 0 is the valid game-start tick (the arrival page can carry it), so only a negative
            // tick is treated as "no calendar position". This keeps the divider grouping consistent
            // with the year pager, which places any entry with a real date on its real-year page.
            if (entry == null || entry.Tick < 0)
            {
                return UndatedKey;
            }

            long absTick = GenDate.TickGameToAbs(entry.Tick);
            int year = GenDate.Year(absTick, Longitude);
            int quadrum = (int)GenDate.Quadrum(absTick, Longitude);
            // Four quadrums per year; year * 4 + quadrum is unique and monotonic in calendar order.
            return (year * 4) + quadrum;
        }

        /// <summary>
        /// True when a divider header should be drawn immediately above <paramref name="index"/>.
        /// The first visible entry always opens its quadrum group; later entries open a new group only
        /// when their quadrum key differs from the entry above them. Undated entries never get one.
        /// </summary>
        internal static bool HasDividerAbove(int previousKey, int currentKey, bool isFirst)
        {
            if (currentKey == UndatedKey)
            {
                return false;
            }

            return isFirst || currentKey != previousKey;
        }

        /// <summary>
        /// Builds the localized "Aprimay · Spring · 5500" header text for the quadrum that
        /// <paramref name="entry"/> belongs to. Returns empty for undated entries.
        /// </summary>
        internal static string Label(DiaryEntryView entry)
        {
            if (entry == null || entry.Tick < 0)
            {
                return string.Empty;
            }

            long absTick = GenDate.TickGameToAbs(entry.Tick);
            Quadrum quadrum = GenDate.Quadrum(absTick, Longitude);
            int year = GenDate.Year(absTick, Longitude);
            string quadrumLabel = quadrum.Label().CapitalizeFirst();
            string seasonLabel = NominalSeason(quadrum).LabelCap();
            return "PawnDiary.Tab.QuadrumDivider".Translate(quadrumLabel, seasonLabel, year);
        }

        /// <summary>
        /// The season conventionally paired with each quadrum at temperate northern latitudes
        /// (Aprimay = spring, and so on). This is the mapping shown on the reference mockup and keeps
        /// the divider label deterministic and map-independent. A latitude-aware season — which would
        /// invert for southern-hemisphere colonies — is a possible later refinement. Season labels
        /// themselves come from RimWorld so they stay localized.
        /// </summary>
        private static Season NominalSeason(Quadrum quadrum)
        {
            switch (quadrum)
            {
                case Quadrum.Aprimay:
                    return Season.Spring;
                case Quadrum.Jugust:
                    return Season.Summer;
                case Quadrum.Septober:
                    return Season.Fall;
                case Quadrum.Decembary:
                    return Season.Winter;
                default:
                    return Season.Spring;
            }
        }
    }
}

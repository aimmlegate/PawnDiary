// Season/quadrum dividers for the Diary tab. Presentation-only: this helper decides where a
// "quadrum · season · year" separator sits between the year's ordered entry cards and builds its
// localized label. It changes nothing about the saved history, the sort order, or the cards.
//
// New to C#/RimWorld? (JS/TS analogy) Think of this as a tiny pure-ish module the tab imports.
// RimWorld splits each 60-day year into four "quadrums" (Aprimay, Jugust, Septober, Decembary) — the
// diary groups entries under the quadrum header they fall in, the same way a blog groups posts by
// month. The grouping is read from each entry's DISPLAY date string (the same "15th of Decembary,
// 5500" line shown on the card), NOT from its raw sort tick: real pages format that date from their
// own tick so the two agree, but dev-mock stress pages deliberately spread their display dates across
// a fake multi-year history while clamping their sort tick to the pawn's real lifetime — there only
// the display date is meaningful, and it is what the year pager groups by too.
using System;
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
        // Sentinel returned for entries whose display date cannot be placed on the calendar (very old
        // saves that only stored a malformed string). Two undated entries share this key, so no
        // divider is drawn between them.
        internal const int UndatedKey = int.MinValue;

        // The four real quadrums in calendar order. RimWorld's full date string embeds the quadrum by
        // name, so each entry's quadrum is recovered by matching these back against its date line.
        private static readonly Quadrum[] CalendarQuadrums =
        {
            Quadrum.Aprimay, Quadrum.Jugust, Quadrum.Septober, Quadrum.Decembary,
        };

        /// <summary>
        /// A stable key that is identical for every entry in the same (year, quadrum) and different
        /// across quadrums. Adjacent entries with different keys get a divider between them.
        /// </summary>
        internal static int QuadrumKey(DiaryEntryView entry)
        {
            if (!TryResolveCalendar(entry, out int year, out Quadrum quadrum))
            {
                return UndatedKey;
            }

            // Four quadrums per year; year * 4 + quadrum is unique and monotonic in calendar order.
            return (year * 4) + (int)quadrum;
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
            if (!TryResolveCalendar(entry, out int year, out Quadrum quadrum))
            {
                return string.Empty;
            }

            string quadrumLabel = quadrum.Label().CapitalizeFirst();
            string seasonLabel = NominalSeason(quadrum).LabelCap();
            return "PawnDiary.Tab.QuadrumDivider".Translate(quadrumLabel, seasonLabel, year);
        }

        /// <summary>
        /// The nominal season for the quadrum an entry belongs to — the same season shown in the
        /// divider label — or <see cref="Season.Undefined"/> for an entry whose date cannot be placed.
        /// Drives the small season glyph drawn beside the divider label.
        /// </summary>
        internal static Season SeasonFor(DiaryEntryView entry)
        {
            if (!TryResolveCalendar(entry, out _, out Quadrum quadrum))
            {
                return Season.Undefined;
            }

            return NominalSeason(quadrum);
        }

        /// <summary>
        /// Resolves the (year, quadrum) an entry belongs to from its DISPLAY date string — the same
        /// source the year pager groups by — rather than its sort tick. Returns false for entries whose
        /// date cannot be placed on the calendar, which then get no divider (matching the pager's
        /// "undated" page). See the file header for why the date, not the tick, is authoritative here.
        /// </summary>
        private static bool TryResolveCalendar(DiaryEntryView entry, out int year, out Quadrum quadrum)
        {
            year = 0;
            quadrum = Quadrum.Undefined;
            string date = entry?.Date;
            if (string.IsNullOrWhiteSpace(date))
            {
                return false;
            }

            return TryExtractYear(date, out year) && TryMatchQuadrum(date, out quadrum);
        }

        /// <summary>
        /// Finds the final run of digits in RimWorld's full date string (for example the 5500 in
        /// "15th of Decembary, 5500"). Reading from the end keeps this tolerant of localized month
        /// names and day ordinals earlier in the string, and mirrors the year pager's own parsing so
        /// the divider and the pager always agree on an entry's year.
        /// </summary>
        private static bool TryExtractYear(string date, out int year)
        {
            year = 0;
            int end = -1;
            for (int i = date.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(date[i]))
                {
                    end = i;
                    break;
                }
            }

            if (end < 0)
            {
                return false;
            }

            int start = end;
            while (start > 0 && char.IsDigit(date[start - 1]))
            {
                start--;
            }

            return int.TryParse(date.Substring(start, end - start + 1), out year);
        }

        /// <summary>
        /// Recovers the quadrum from a full date string by matching each quadrum's own localized label
        /// (the same one GenDate printed into the string) back against it. Using RimWorld's labels keeps
        /// this correct under localization; the first matching quadrum in calendar order wins.
        /// </summary>
        private static bool TryMatchQuadrum(string date, out Quadrum quadrum)
        {
            for (int i = 0; i < CalendarQuadrums.Length; i++)
            {
                string label = CalendarQuadrums[i].Label();
                if (!string.IsNullOrEmpty(label)
                    && date.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    quadrum = CalendarQuadrums[i];
                    return true;
                }
            }

            quadrum = Quadrum.Undefined;
            return false;
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

// Guarded reads of live Hediff display labels. A Hediff label looks like plain data, but RimWorld
// assembles it through virtual properties and HediffComp callbacks that optional mods can override.
// One broken override must not abort an otherwise unrelated diary event or periodic scan.
using System;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Reads optional hediff display text at the impure game-state boundary. A throwing third-party
    /// label getter degrades to an empty label so callers can use their existing Def-name fallback.
    /// </summary>
    internal static class HediffLabelCapture
    {
        /// <summary>Returns <see cref="Hediff.Label"/>, or empty when the live getter fails.</summary>
        public static string ReadLabel(Hediff hediff)
        {
            try
            {
                return hediff?.Label ?? string.Empty;
            }
            catch (Exception)
            {
                // Do not log here: scanners and prompt builders may revisit the same broken hediff
                // many times, which would turn one compatibility fault into an unbounded log flood.
                return string.Empty;
            }
        }

        /// <summary>Returns <see cref="Hediff.LabelCap"/>, or empty when the live getter fails.</summary>
        public static string ReadLabelCap(Hediff hediff)
        {
            try
            {
                return hediff?.LabelCap ?? string.Empty;
            }
            catch (Exception)
            {
                // LabelCap ultimately consults Label, so it needs the same third-party boundary.
                return string.Empty;
            }
        }
    }
}

// Shared pure ownership rules for the ordinary DidSurgery diary signal observed during one exact
// Anomaly surgery transaction. Live Tales and Pawns remain in patch/scope adapters; only stable
// role IDs and ticks reach this comparison.
using System;

namespace PawnDiary.Capture
{
    /// <summary>Decides exact, bounded deferral and final suppression for Anomaly surgical Tales.</summary>
    internal static class AnomalySurgeryTaleOwnershipPolicy
    {
        internal const string DidSurgeryDefName = "DidSurgery";

        /// <summary>True only for the exact surgeon/subject Tale inside the active, unexpired scope.</summary>
        public static bool CanDefer(
            AnomalySurgeryTaleClaim claim,
            AnomalySurgeryTaleFacts tale,
            int expiryTicks)
        {
            if (claim == null || tale == null || !claim.active || claim.openedTick < 0
                || tale.tick < claim.openedTick || expiryTicks < 0
                || !string.Equals(
                    (tale.taleDefName ?? string.Empty).Trim(),
                    DidSurgeryDefName,
                    StringComparison.Ordinal)) return false;
            string subject = CleanStable(claim.subjectPawnId);
            string surgeon = CleanStable(claim.surgeonPawnId);
            if (subject.Length == 0 || surgeon.Length == 0) return false;
            long age = (long)tale.tick - claim.openedTick;
            return age <= expiryTicks
                && string.Equals(surgeon, CleanStable(tale.firstPawnId), StringComparison.Ordinal)
                && string.Equals(subject, CleanStable(tale.secondPawnId), StringComparison.Ordinal);
        }

        /// <summary>
        /// The staged generic signal is discarded only after its source truth is verified and a
        /// dedicated replacement event was actually created. Every other result releases it.
        /// </summary>
        public static bool ShouldSuppress(
            bool verifiedReplacement,
            bool dedicatedEventCreated,
            bool taleWasDeferred)
        {
            return taleWasDeferred && dedicatedEventCreated && verifiedReplacement;
        }

        private static string CleanStable(string value)
        {
            string clean = (value ?? string.Empty).Trim();
            return clean.Length > 0 && clean.Length <= 200
                && clean.IndexOf('|') < 0 && clean.IndexOf(';') < 0 && clean.IndexOf('=') < 0
                && clean.IndexOf('\r') < 0 && clean.IndexOf('\n') < 0
                ? clean
                : string.Empty;
        }
    }
}

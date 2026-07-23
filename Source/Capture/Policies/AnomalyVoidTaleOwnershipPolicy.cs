// Pure ownership rules for the single-pawn terminal-void Tale (EmbracedTheVoid / ClosedTheVoid)
// observed inside one exact VoidAwakeningUtility method. Live Tales and Pawns stay in the patch/scope
// adapters; only the exact actor identity, the expected Tale defName, and ticks reach this comparison.
// Final suppression reuses the shared AnomalySurgeryTaleOwnershipPolicy.ShouldSuppress predicate so a
// deferred Tale is discarded only after a verified dedicated page actually exists.
using System;

namespace PawnDiary.Capture
{
    /// <summary>Decides exact, bounded deferral for one single-pawn terminal-void Tale.</summary>
    internal static class AnomalyVoidTaleOwnershipPolicy
    {
        /// <summary>Vanilla single-pawn Tale recorded when a pawn embraces the void.</summary>
        internal const string EmbracedTheVoidDefName = "EmbracedTheVoid";

        /// <summary>Vanilla single-pawn Tale recorded when a pawn disrupts the void link.</summary>
        internal const string ClosedTheVoidDefName = "ClosedTheVoid";

        /// <summary>Returns whether a Tale defName is one of the two terminal-void Tales.</summary>
        public static bool IsTerminalVoidTale(string taleDefName)
        {
            string clean = (taleDefName ?? string.Empty).Trim();
            return string.Equals(clean, EmbracedTheVoidDefName, StringComparison.Ordinal)
                || string.Equals(clean, ClosedTheVoidDefName, StringComparison.Ordinal);
        }

        /// <summary>
        /// True only for the exact expected single-pawn Tale of the exact actor inside the active,
        /// unexpired scope. Any other Tale, actor, or an expired/closed claim fails open.
        /// </summary>
        public static bool CanDefer(
            AnomalyVoidTaleClaim claim,
            AnomalyVoidTaleFacts tale,
            int expiryTicks)
        {
            if (claim == null || tale == null || !claim.active || claim.openedTick < 0
                || tale.tick < claim.openedTick || expiryTicks < 0) return false;
            string expected = (claim.expectedTaleDefName ?? string.Empty).Trim();
            if (!IsTerminalVoidTale(expected)
                || !string.Equals(
                    (tale.taleDefName ?? string.Empty).Trim(), expected, StringComparison.Ordinal))
            {
                return false;
            }
            string actor = CleanStable(claim.actorPawnId);
            if (actor.Length == 0) return false;
            long age = (long)tale.tick - claim.openedTick;
            return age <= expiryTicks
                && string.Equals(actor, CleanStable(tale.actorPawnId), StringComparison.Ordinal);
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

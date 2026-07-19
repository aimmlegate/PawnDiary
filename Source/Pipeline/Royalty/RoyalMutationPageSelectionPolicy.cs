// Pure output selection for a completed Royalty mutation batch. Exact hooks may capture both a title
// transition and a psylink increase in one action; this policy chooses one enabled Progression owner
// without depending on live settings, Pawns, Defs, or RimWorld state.
namespace PawnDiary
{
    /// <summary>Selects the single enabled Progression page kind for a Royalty mutation batch.</summary>
    internal static class RoyalMutationPageSelectionPolicy
    {
        /// <summary>
        /// Chooses the one enabled output route for a completed mutation. A meaningful title wins;
        /// otherwise an enabled psylink change may own the page.
        /// </summary>
        public static string Select(
            bool policyEnabled,
            bool titleChanged,
            bool titleOutputEnabled,
            bool psylinkChanged,
            bool psylinkOutputEnabled)
        {
            if (!policyEnabled) return string.Empty;
            // A title page is the richer owner when both routes are enabled. If it is filtered out,
            // retain the independently enabled psylink truth instead of dropping the whole batch.
            if (titleChanged && titleOutputEnabled) return RoyalMutationKindTokens.Title;
            if (psylinkChanged && psylinkOutputEnabled) return RoyalMutationKindTokens.Psylink;
            return string.Empty;
        }
    }
}

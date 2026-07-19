// Resettable Royalty correlation state. These short-lived ownership caches bridge exact same-action
// title, mutation, persona, succession, permit, and raid callbacks without becoming saved truth.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Process-local Royalty ownership caches cleared on every new game/save load.</summary>
    internal static class RoyaltyTransientState
    {
        private static readonly Dictionary<string, RoyalMutationCauseScope> activeCauseScopes =
            new Dictionary<string, RoyalMutationCauseScope>();
        private static readonly List<RoyalMutationFact> unclaimedMutations = new List<RoyalMutationFact>();
        private static readonly Dictionary<string, string> personaEndCauses = new Dictionary<string, string>();
        private static readonly HashSet<string> talePersonaOwners = new HashSet<string>();
        private static readonly Dictionary<string, string> personaTraitThoughtOwners =
            new Dictionary<string, string>();
        private static readonly Dictionary<string, string> royalTitleThoughtOwners =
            new Dictionary<string, string>();

        /// <summary>Drops every process-local correlation so one colony can never leak into another.</summary>
        public static void Reset()
        {
            PersonaKillThoughtCorrelation.Clear();
            RoyalMutationCorrelation.Clear();
            RoyalSuccessionCorrelation.Clear();
            RoyalTitleThoughtCorrelation.Clear();
            RoyalPermitOwnerCache.Reset();
            PawnDiary.Ingestion.QuickMilitaryAidRaidCorrelation.Reset();
            RaidExecutePatch.SetRaidSubmitOverrideForTests(null);
            activeCauseScopes.Clear();
            unclaimedMutations.Clear();
            personaEndCauses.Clear();
            talePersonaOwners.Clear();
            personaTraitThoughtOwners.Clear();
            royalTitleThoughtOwners.Clear();
        }
    }
}

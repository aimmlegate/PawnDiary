// Resettable Royalty correlation state. Most caches remain reserved Phase-1 shells for later
// title, mutation, permit, and combat/death ownership phases.
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Process-local Royalty ownership caches cleared on every new game/save load.</summary>
    internal static class RoyaltyTransientState
    {
        private static readonly Dictionary<string, RoyalMutationCauseScope> activeCauseScopes =
            new Dictionary<string, RoyalMutationCauseScope>();
        private static readonly List<RoyalMutationFact> unclaimedMutations = new List<RoyalMutationFact>();
        private static readonly Dictionary<string, string> permitOwners = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> quickAidOwners = new Dictionary<string, string>();
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
            RoyalTitleThoughtCorrelation.Clear();
            activeCauseScopes.Clear();
            unclaimedMutations.Clear();
            permitOwners.Clear();
            quickAidOwners.Clear();
            personaEndCauses.Clear();
            talePersonaOwners.Clear();
            personaTraitThoughtOwners.Clear();
            royalTitleThoughtOwners.Clear();
        }
    }
}

// Resettable Royalty correlation state. These short-lived ownership caches bridge exact same-action
// title, mutation, persona, succession, permit, and raid callbacks without becoming saved truth.
using System;
using System.Collections.Generic;
using Verse;

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
            // Each owner is isolated deliberately: a broken mod interaction in one cleanup must not
            // prevent later caches from clearing and leaking the previous colony into the next one.
            ResetOne("PersonaKillThoughtCorrelation", PersonaKillThoughtCorrelation.Clear);
            ResetOne("RoyalMutationCorrelation", RoyalMutationCorrelation.Clear);
            ResetOne("RoyalSuccessionCorrelation", RoyalSuccessionCorrelation.Clear);
            ResetOne("RoyalTitleThoughtCorrelation", RoyalTitleThoughtCorrelation.Clear);
            ResetOne("RoyalPermitOwnerCache", RoyalPermitOwnerCache.Reset);
            ResetOne("QuickMilitaryAidRaidCorrelation",
                PawnDiary.Ingestion.QuickMilitaryAidRaidCorrelation.Reset);
            ResetOne("RaidExecutePatchTestOverride",
                () => RaidExecutePatch.SetRaidSubmitOverrideForTests(null));
            ResetOne("activeCauseScopes", activeCauseScopes.Clear);
            ResetOne("unclaimedMutations", unclaimedMutations.Clear);
            ResetOne("personaEndCauses", personaEndCauses.Clear);
            ResetOne("talePersonaOwners", talePersonaOwners.Clear);
            ResetOne("personaTraitThoughtOwners", personaTraitThoughtOwners.Clear);
            ResetOne("royalTitleThoughtOwners", royalTitleThoughtOwners.Clear);
        }

        private static void ResetOne(string owner, Action reset)
        {
            try
            {
                reset();
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(
                    "[Pawn Diary] Could not clear Royalty transient owner " + owner
                        + "; later owners were still cleared. " + ex,
                    ("PawnDiary.Royalty.TransientReset." + owner).GetHashCode());
            }
        }
    }
}

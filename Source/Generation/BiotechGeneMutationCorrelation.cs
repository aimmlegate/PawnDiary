// Transient Biotech gene-mutation ownership. Reimplantation runs inside Ability.Activate, so the
// exact GeneUtility hook marks the active ability scope only after a canonical progression page
// commits. The outer ability postfix then suppresses its generic duplicate. Nothing here is saved.
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary
{
    /// <summary>Before-state held only for the duration of one vanilla GeneUtility call.</summary>
    internal sealed class BiotechGeneMutationCallState
    {
        public Pawn recipient;
        public Pawn otherPawn;
        public string causeToken = string.Empty;
        public GeneIdentitySnapshot before;
    }

    /// <summary>One nested local ability activation that may receive canonical gene ownership.</summary>
    internal sealed class BiotechGeneAbilityScope
    {
        public string targetPawnId = string.Empty;
        public bool canonicalClaimed;
        public bool closed;
    }

    /// <summary>Main-thread scope stack for exact owner-before-secondary arbitration.</summary>
    internal static class BiotechGeneMutationCorrelation
    {
        private static readonly List<BiotechGeneAbilityScope> AbilityScopes =
            new List<BiotechGeneAbilityScope>();

        /// <summary>Opens a local ability scope; null/non-Biotech targets require no correlation.</summary>
        public static BiotechGeneAbilityScope BeginAbility(Pawn target)
        {
            if (!ModsConfig.BiotechActive || target == null) return null;
            BiotechGeneAbilityScope scope = new BiotechGeneAbilityScope
            {
                targetPawnId = target.GetUniqueLoadID()
            };
            AbilityScopes.Add(scope);
            return scope;
        }

        /// <summary>Marks the innermost matching ability only after the exact page commits.</summary>
        public static void ClaimCurrentAbility(Pawn recipient)
        {
            string pawnId = recipient?.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(pawnId)) return;
            for (int i = AbilityScopes.Count - 1; i >= 0; i--)
            {
                BiotechGeneAbilityScope scope = AbilityScopes[i];
                if (scope == null || scope.closed) continue;
                if (string.Equals(scope.targetPawnId, pawnId, System.StringComparison.Ordinal))
                {
                    scope.canonicalClaimed = true;
                    return;
                }
            }
        }

        /// <summary>Closes one scope and reports whether its generic ability signal is now secondary.</summary>
        public static bool CloseAbility(BiotechGeneAbilityScope scope)
        {
            if (scope == null) return false;
            bool claimed = scope.canonicalClaimed;
            if (!scope.closed)
            {
                scope.closed = true;
                AbilityScopes.Remove(scope);
            }
            return claimed;
        }

        /// <summary>Clears static state at every new-game/load boundary.</summary>
        public static void Clear()
        {
            AbilityScopes.Clear();
        }
    }
}

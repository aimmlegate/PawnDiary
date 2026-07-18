// Pure ownership for the first consequential persona-weapon kill. The existing Tale/death adapter
// must resolve participants exactly; this policy never treats Tale list order as killer semantics.
using System;

namespace PawnDiary
{
    /// <summary>Qualifies and consumes at most one consequential-kill milestone per bond epoch.</summary>
    internal static class PersonaMilestonePolicy
    {
        public static PersonaMilestoneDecision Evaluate(
            PersonaMilestoneObservation observation,
            RoyaltyPolicySnapshot policy)
        {
            PersonaMilestoneDecision decision = new PersonaMilestoneDecision();
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            if (!effective.enabled || observation == null || observation.bond == null
                || !PersonaBondPhaseTokens.IsLive(observation.bond.phaseToken)
                || observation.bond.firstConsequentialKillObserved
                || observation.currentWeapon == null
                || !Same(observation.bond.weaponThingId, observation.currentWeapon.weaponThingId)
                || !Same(observation.bond.currentPawnId, observation.currentWeapon.codedPawnId)
                || !observation.currentWeapon.isCurrentlyPrimary)
            {
                return decision;
            }

            RoyaltyTaleQualificationRule rule = MatchingRule(
                observation.taleDefName,
                effective.qualifyingTales);
            if (rule == null || observation.significance < Math.Max(0, rule.minimumSignificance)
                || !observation.victimPresent || (rule.requireVictimDeath && !observation.victimDied)
                || !ValidRole(observation.resolvedKillerRoleToken)
                || !ValidRole(observation.deathVictimRoleToken)
                || !string.Equals(Clean(rule.killerRoleToken), Clean(observation.resolvedKillerRoleToken),
                    StringComparison.Ordinal)
                || !string.Equals(Clean(rule.victimRoleToken), Clean(observation.deathVictimRoleToken),
                    StringComparison.Ordinal)
                || string.Equals(Clean(observation.deathVictimRoleToken),
                    Clean(observation.resolvedKillerRoleToken), StringComparison.Ordinal)
                || !observation.hasDeathContext || !observation.deathContextMatchesKiller)
            {
                return decision;
            }

            decision.qualifies = true;
            // Gameplay truth is consumed even when settings reject output. This prevents a later kill
            // from being mislabeled as the bond's first consequential moment.
            decision.markObserved = true;
            decision.markEventRecorded = observation.personaGroupEnabled && observation.pageAccepted;
            decision.enrichTale = decision.markEventRecorded;
            decision.forceSoloKillerPov = decision.markEventRecorded;
            decision.selectedTraits = PersonaTraitPolicy.Select(
                observation.currentWeapon.traits,
                PersonaTraitEventTokens.Kill,
                observation.eventIdentity,
                effective);
            return decision;
        }

        /// <summary>
        /// Resolves the XML-owned killer and victim roles for one exact Tale. Both roles must be
        /// present and distinct; callers never infer combat semantics from Tale pawn-list order.
        /// </summary>
        public static bool TryResolveRoles(
            string taleDefName,
            RoyaltyPolicySnapshot policy,
            out string killerRoleToken,
            out string victimRoleToken)
        {
            killerRoleToken = string.Empty;
            victimRoleToken = string.Empty;
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            RoyaltyTaleQualificationRule rule = MatchingRule(taleDefName, effective.qualifyingTales);
            if (rule == null) return false;
            string killer = Clean(rule.killerRoleToken);
            string victim = Clean(rule.victimRoleToken);
            if (!RoyaltyTaleRoleTokens.IsKnown(killer) || !RoyaltyTaleRoleTokens.IsKnown(victim)
                || string.Equals(killer, victim, StringComparison.Ordinal)) return false;
            killerRoleToken = killer;
            victimRoleToken = victim;
            return true;
        }

        private static RoyaltyTaleQualificationRule MatchingRule(
            string taleDefName,
            System.Collections.Generic.IList<RoyaltyTaleQualificationRule> rules)
        {
            string key = (taleDefName ?? string.Empty).Trim();
            if (key.Length == 0 || rules == null) return null;
            for (int i = 0; i < rules.Count; i++)
            {
                RoyaltyTaleQualificationRule row = rules[i];
                if (row != null && string.Equals(key, (row.taleDefName ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase))
                    return row;
            }
            return null;
        }

        private static bool ValidRole(string role)
        {
            string value = (role ?? string.Empty).Trim();
            return value == RoyaltyTaleRoleTokens.Initiator || value == RoyaltyTaleRoleTokens.Recipient;
        }

        private static bool Same(string left, string right)
        {
            string a = (left ?? string.Empty).Trim();
            string b = (right ?? string.Empty).Trim();
            return a.Length > 0 && string.Equals(a, b, StringComparison.Ordinal);
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}

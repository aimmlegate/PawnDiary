// Pure structured-context formatter for Royalty Phase-3 Tale and death enrichment. It deliberately
// does not emit the standalone `persona_weapon=` marker: these remain Tale/death pages, so their
// existing source-domain classifier, prompt template, and death ownership stay intact.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Builds bounded persona relationship facts for an existing Tale or death page.</summary>
    internal static class PersonaMilestoneContextFormatter
    {
        public const string FirstKillDefName = "PersonaWeaponFirstConsequentialKill";

        /// <summary>Appends the first-kill milestone while preserving the leading Tale marker.</summary>
        public static string FormatFirstKill(
            string baseTaleContext,
            PersonaWeaponSnapshot weapon,
            PersonaBondStateSnapshot bond,
            IList<PersonaTraitFact> selectedTraits,
            string sourceTaleDefName,
            string sourceTaleLabel,
            string killerRoleToken,
            string victimRoleToken,
            RoyaltyPolicySnapshot policy)
        {
            if (weapon == null || bond == null || bond.bondEpoch < 1) return baseTaleContext ?? string.Empty;
            List<string> parts = Begin(baseTaleContext);
            Add(parts, "persona_milestone", PersonaNarrativePhaseTokens.FirstConsequentialKill, 80);
            Add(parts, "tale_source_def", sourceTaleDefName, 120);
            Add(parts, "tale_source_label", sourceTaleLabel, 160);
            Add(parts, "tale_killer_role", killerRoleToken, 40);
            Add(parts, "tale_victim_role", victimRoleToken, 40);
            AppendBond(parts, weapon, bond, PersonaBondPhaseTokens.Active, PersonaEndCauseTokens.None,
                selectedTraits, policy);
            return string.Join("; ", parts.ToArray());
        }

        /// <summary>Builds relationship facts captured before vanilla uncodes a dead wielder.</summary>
        public static string FormatWielderDeath(
            PersonaWeaponSnapshot weapon,
            PersonaBondStateSnapshot bond,
            IList<PersonaTraitFact> selectedTraits,
            RoyaltyPolicySnapshot policy)
        {
            if (weapon == null || bond == null || bond.bondEpoch < 1) return string.Empty;
            List<string> parts = new List<string>();
            Add(parts, "persona_milestone", "wielder_death", 80);
            AppendBond(parts, weapon, bond, PersonaBondPhaseTokens.Ended, PersonaEndCauseTokens.PawnDeath,
                selectedTraits, policy);
            return string.Join("; ", parts.ToArray());
        }

        private static void AppendBond(
            List<string> parts,
            PersonaWeaponSnapshot weapon,
            PersonaBondStateSnapshot bond,
            string newState,
            string endCause,
            IList<PersonaTraitFact> selectedTraits,
            RoyaltyPolicySnapshot policy)
        {
            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            Add(parts, "persona_weapon_id", weapon.weaponThingId, 120);
            Add(parts, "persona_weapon_def", weapon.weaponDefName, 120);
            Add(parts, "persona_weapon_name", weapon.displayName, effective.maximumTraitLabelCharacters);
            Add(parts, "bond_epoch", bond.bondEpoch.ToString(), 16);
            Add(parts, "bond_previous_state", bond.phaseToken, 40);
            Add(parts, "bond_new_state", newState, 40);
            if (endCause != PersonaEndCauseTokens.None) Add(parts, "bond_end_cause", endCause, 80);
            int cap = Math.Max(1, Math.Min(2, effective.maximumSelectedTraits));
            for (int i = 0; i < (selectedTraits == null ? 0 : selectedTraits.Count) && i < cap; i++)
            {
                PersonaTraitFact trait = selectedTraits[i];
                if (trait == null) continue;
                Add(parts, "persona_trait_" + (i + 1), trait.label,
                    effective.maximumTraitLabelCharacters);
                Add(parts, "persona_trait_description_" + (i + 1), trait.description,
                    effective.maximumTraitDescriptionCharacters);
            }
        }

        private static List<string> Begin(string context)
        {
            List<string> parts = new List<string>();
            string value = (context ?? string.Empty).Trim().TrimEnd(';').Trim();
            if (value.Length > 0) parts.Add(value);
            return parts;
        }

        private static void Add(List<string> parts, string key, string value, int maximumCharacters)
        {
            string cleaned = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ')
                .Replace(';', ',').Trim();
            int cap = Math.Max(1, maximumCharacters);
            if (cleaned.Length > cap) cleaned = cleaned.Substring(0, cap).TrimEnd();
            if (cleaned.Length > 0) parts.Add(key + "=" + cleaned);
        }
    }
}

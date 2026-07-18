// Pure prompt-context formatter for persona lifecycle pages. All prose values arrive already
// localized on RimWorld's main thread; this file emits only stable structured schema labels.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Builds bounded exact context for one persona bond lifecycle edge.</summary>
    internal static class PersonaWeaponContextFormatter
    {
        public const string MarkerKey = "persona_weapon";

        public static string Format(
            PersonaWeaponSnapshot weapon,
            PersonaBondStateSnapshot previous,
            PersonaLifecycleDecision decision,
            IList<PersonaTraitFact> selectedTraits,
            string localizedDuration,
            RoyaltyPolicySnapshot policy)
        {
            if (weapon == null || decision == null || decision.nextState == null
                || !PersonaNarrativePhaseTokens.IsKnown(decision.narrativePhase)) return string.Empty;

            RoyaltyPolicySnapshot effective = policy ?? RoyaltyPolicySnapshot.CreateDefault();
            PersonaBondStateSnapshot next = decision.nextState;
            List<string> lines = new List<string>
            {
                Pair(MarkerKey, decision.narrativePhase, 80),
                Pair("persona_weapon_id", weapon.weaponThingId, 120),
                Pair("persona_weapon_def", weapon.weaponDefName, 120),
                Pair("persona_weapon_name", weapon.displayName, effective.maximumTraitLabelCharacters),
                Pair("bond_epoch", Math.Max(1, next.bondEpoch).ToString(), 16),
                Pair("bond_previous_state", previous == null ? PersonaBondPhaseTokens.Untracked : previous.phaseToken, 40),
                Pair("bond_new_state", next.phaseToken, 40)
            };

            string durationKey = decision.narrativePhase == PersonaNarrativePhaseTokens.BondSeparated
                || decision.narrativePhase == PersonaNarrativePhaseTokens.BondRecovered
                    ? "bond_separation_duration"
                    : "bond_duration";
            Add(lines, durationKey, localizedDuration, 120);
            if (decision.includesExactPreviousBond)
                Add(lines, "bond_previous_pawn", previous?.currentPawnName, 120);
            if (decision.narrativePhase == PersonaNarrativePhaseTokens.BondEnded)
                Add(lines, "bond_end_cause", next.endCauseToken, 80);

            int cap = Math.Max(1, Math.Min(2, effective.maximumSelectedTraits));
            for (int i = 0; i < (selectedTraits == null ? 0 : selectedTraits.Count) && i < cap; i++)
            {
                PersonaTraitFact trait = selectedTraits[i];
                if (trait == null) continue;
                Add(lines, "persona_trait_" + (i + 1), trait.label,
                    effective.maximumTraitLabelCharacters);
                Add(lines, "persona_trait_description_" + (i + 1), trait.description,
                    effective.maximumTraitDescriptionCharacters);
            }
            return Join(lines);
        }

        private static void Add(List<string> lines, string key, string value, int cap)
        {
            string pair = Pair(key, value, cap);
            if (pair.Length > 0) lines.Add(pair);
        }

        private static string Pair(string key, string value, int cap)
        {
            string cleaned = Clean(value, cap);
            return cleaned.Length == 0 ? string.Empty : key + "=" + cleaned;
        }

        private static string Clean(string value, int cap)
        {
            string cleaned = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ')
                .Replace(';', ',').Trim();
            int maximum = Math.Max(1, cap);
            return cleaned.Length <= maximum ? cleaned : cleaned.Substring(0, maximum).TrimEnd();
        }

        private static string Join(List<string> lines)
        {
            List<string> present = new List<string>();
            for (int i = 0; i < lines.Count; i++) if (!string.IsNullOrWhiteSpace(lines[i])) present.Add(lines[i]);
            return string.Join(";", present.ToArray());
        }
    }
}

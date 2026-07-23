// One pawn's persisted lore-seed planning state (design/LORE_MEMORY_SEED_PLAN.md §6). The target
// roster is HISTORY, not preference (§16 G3): it is persisted before the first deposit attempt and
// never resampled — catalog additions, weight changes, localization changes, or save/reload cannot
// reshuffle an established pawn. Retries only deposit targets still missing from the repository; a
// removed Def stays in the roster and is skipped without replacement.
//
// All Scribe keys are additive: saves from before the lore layer load with empty lists.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable" / Scribe).
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Persisted per-pawn lore rosters and bounded lifetime histories. Owned and scribed by
    /// DiaryGameComponent.Memory.cs beside the fragment repository; the pure planner only ever
    /// sees these lists copied into <see cref="LoreSeedPawnFacts"/>.
    /// </summary>
    public class PawnLoreSeedState : IExposable
    {
        public string pawnId = string.Empty;
        // The one-time initial roster (max 4 by default). Empty means planning never happened.
        public List<string> initialTargetDefNames = new List<string>();
        // Exact progression seeds actually deposited (deferred L5; persisted now so the save
        // contract is stable before the feature lands).
        public List<string> progressionDefNamesEverDeposited = new List<string>();
        // Exact core seeds actually deposited across initial + progression. Lifetime allocation:
        // an evicted core row is never replaced by a new identity fact (§4).
        public List<string> coreDefNamesEverDeposited = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Collections.Look(ref initialTargetDefNames, "initialTargets", LookMode.Value);
            Scribe_Collections.Look(ref progressionDefNamesEverDeposited, "progressionDeposited", LookMode.Value);
            Scribe_Collections.Look(ref coreDefNamesEverDeposited, "coreDeposited", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawnId = string.IsNullOrWhiteSpace(pawnId) ? string.Empty : pawnId.Trim();
                initialTargetDefNames = CleanList(initialTargetDefNames);
                progressionDefNamesEverDeposited = CleanList(progressionDefNamesEverDeposited);
                coreDefNamesEverDeposited = CleanList(coreDefNamesEverDeposited);
            }
        }

        /// <summary>True when a Def name appears in any roster or lifetime history (§6).</summary>
        public bool HasSeedDefName(string seedDefName)
        {
            return ContainsName(initialTargetDefNames, seedDefName)
                || ContainsName(progressionDefNamesEverDeposited, seedDefName)
                || ContainsName(coreDefNamesEverDeposited, seedDefName);
        }

        private static bool ContainsName(List<string> values, string name)
        {
            if (values == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> CleanList(List<string> values)
        {
            List<string> cleaned = new List<string>();
            if (values == null)
            {
                return cleaned;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]) && !ContainsName(cleaned, values[i].Trim()))
                {
                    cleaned.Add(values[i].Trim());
                }
            }

            return cleaned;
        }
    }
}

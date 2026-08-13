// XML boundary for the compact Rare / Reduced / Normal / Increased choices on the Events tab.
// The player-visible wording and every numeric display band live in Def XML; this adapter freezes
// those mutable RimWorld objects into the pure DiaryFrequencyChoicePolicy contract.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>One named absolute multiplier choice and its inclusive display-band ceiling.</summary>
    public class DiaryFrequencyChoiceDef : Def
    {
        public string token;
        public float multiplier = DiaryFrequencyPolicy.StandardMultiplier;
        public float displayMaxMultiplier = DiaryFrequencyPolicy.StandardMultiplier;
        public int order;
    }

    /// <summary>Loads, validates, and orders the XML-backed frequency choices for settings UI.</summary>
    internal static class DiaryFrequencyChoices
    {
        /// <summary>Returns valid loaded Def rows in their XML-owned player-facing order.</summary>
        public static List<DiaryFrequencyChoiceDef> All()
        {
            List<DiaryFrequencyChoiceDef> loaded =
                DefDatabase<DiaryFrequencyChoiceDef>.AllDefsListForReading;
            Dictionary<string, DiaryFrequencyChoiceDef> byKey =
                new Dictionary<string, DiaryFrequencyChoiceDef>(System.StringComparer.OrdinalIgnoreCase);
            List<DiaryFrequencyChoiceSnapshot> snapshots =
                new List<DiaryFrequencyChoiceSnapshot>();

            for (int i = 0; i < loaded.Count; i++)
            {
                DiaryFrequencyChoiceDef def = loaded[i];
                string key = (def?.token ?? string.Empty).Trim();
                if (key.Length == 0 || byKey.ContainsKey(key))
                {
                    continue;
                }

                byKey[key] = def;
                snapshots.Add(ToSnapshot(def));
            }

            List<DiaryFrequencyChoiceSnapshot> ordered =
                DiaryFrequencyChoicePolicy.NormalizeForMenu(snapshots);
            List<DiaryFrequencyChoiceDef> result = new List<DiaryFrequencyChoiceDef>();
            for (int i = 0; i < ordered.Count; i++)
            {
                DiaryFrequencyChoiceDef def;
                if (byKey.TryGetValue(ordered[i].choiceKey, out def))
                {
                    result.Add(def);
                }
            }

            return result;
        }

        /// <summary>Returns the loaded choice whose XML display band contains the multiplier.</summary>
        public static DiaryFrequencyChoiceDef ForMultiplier(float multiplier)
        {
            List<DiaryFrequencyChoiceDef> defs = All();
            List<DiaryFrequencyChoiceSnapshot> snapshots = new List<DiaryFrequencyChoiceSnapshot>();
            Dictionary<string, DiaryFrequencyChoiceDef> byKey =
                new Dictionary<string, DiaryFrequencyChoiceDef>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < defs.Count; i++)
            {
                DiaryFrequencyChoiceDef def = defs[i];
                DiaryFrequencyChoiceSnapshot snapshot = ToSnapshot(def);
                snapshots.Add(snapshot);
                byKey[snapshot.choiceKey] = def;
            }

            DiaryFrequencyChoiceSnapshot selected =
                DiaryFrequencyChoicePolicy.ChoiceForMultiplier(snapshots, multiplier);
            DiaryFrequencyChoiceDef result;
            return selected != null && byKey.TryGetValue(selected.choiceKey, out result)
                ? result
                : null;
        }

        /// <summary>Copies a live Def into the plain choice-policy contract.</summary>
        internal static DiaryFrequencyChoiceSnapshot ToSnapshot(DiaryFrequencyChoiceDef def)
        {
            return new DiaryFrequencyChoiceSnapshot
            {
                choiceKey = (def?.token ?? string.Empty).Trim(),
                multiplier = def?.multiplier ?? DiaryFrequencyPolicy.StandardMultiplier,
                displayMaxMultiplier = def?.displayMaxMultiplier
                    ?? DiaryFrequencyPolicy.StandardMultiplier,
                order = def?.order ?? 0
            };
        }
    }
}

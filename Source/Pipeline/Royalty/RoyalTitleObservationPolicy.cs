// Pure per-faction Royalty title observation updates. Exact hooks and the slow scanner share this
// policy so they cannot disagree about faction identity, disappearing titles, or baseline advance.
// It owns only detached snapshots: no Pawn, Faction, Def, Scribe, settings, or localization reads.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Advances one exact edge and diffs the saved per-faction baseline against live copies.</summary>
    internal static class RoyalTitleObservationPolicy
    {
        /// <summary>
        /// Replaces one faction row after an exact hook. A subsequent scanner observation of the same
        /// current title therefore produces no edge; passing <paramref name="current"/> as null records
        /// complete loss by removing that faction row.
        /// </summary>
        public static List<RoyalTitleObservationSnapshot> Advance(
            IList<RoyalTitleObservationSnapshot> observed,
            RoyalTitleSnapshot previous,
            RoyalTitleSnapshot current,
            int tick)
        {
            List<RoyalTitleObservationSnapshot> rows = Copy(observed);
            string factionId = SafeId((current ?? previous)?.factionId);
            if (factionId.Length == 0) return RoyaltyStatePersistence.NormalizeTitleObservations(
                rows, RoyaltyStatePersistence.HardMaximumTitleObservations);

            for (int i = rows.Count - 1; i >= 0; i--)
                if (string.Equals(rows[i]?.factionId, factionId, StringComparison.Ordinal))
                    rows.RemoveAt(i);

            if (current != null)
            {
                List<RoyalTitleObservationSnapshot> replacement =
                    RoyaltyStatePersistence.BaselineTitles(
                        new List<RoyalTitleSnapshot> { current }, tick);
                if (replacement.Count > 0) rows.Add(replacement[0]);
            }
            return RoyaltyStatePersistence.NormalizeTitleObservations(
                rows, RoyaltyStatePersistence.HardMaximumTitleObservations);
        }

        /// <summary>
        /// Returns every exact faction edge. The union of before/after faction IDs deliberately keeps
        /// a disappeared saved row, producing a mutation whose new title is null (complete loss).
        /// </summary>
        public static List<RoyalTitleMutationSnapshot> Diff(
            string pawnId,
            IList<RoyalTitleObservationSnapshot> observed,
            IList<RoyalTitleSnapshot> current,
            int tick)
        {
            string pawn = SafeId(pawnId);
            List<RoyalTitleMutationSnapshot> result = new List<RoyalTitleMutationSnapshot>();
            if (pawn.Length == 0) return result;

            Dictionary<string, RoyalTitleSnapshot> before =
                IndexObserved(pawn, observed);
            Dictionary<string, RoyalTitleSnapshot> after = IndexCurrent(current);
            List<string> factions = new List<string>(before.Keys);
            foreach (string faction in after.Keys)
                if (!before.ContainsKey(faction)) factions.Add(faction);
            factions.Sort(StringComparer.Ordinal.Compare);

            for (int i = 0; i < factions.Count; i++)
            {
                RoyalTitleSnapshot oldTitle;
                RoyalTitleSnapshot newTitle;
                before.TryGetValue(factions[i], out oldTitle);
                after.TryGetValue(factions[i], out newTitle);
                if (SameTitle(oldTitle, newTitle)) continue;
                result.Add(new RoyalTitleMutationSnapshot
                {
                    pawnId = pawn,
                    factionId = factions[i],
                    previousTitle = oldTitle,
                    newTitle = newTitle,
                    causeToken = RoyalMutationCauseTokens.Unknown,
                    tick = Math.Max(0, tick)
                });
            }
            return result;
        }

        private static Dictionary<string, RoyalTitleSnapshot> IndexObserved(
            string pawnId,
            IList<RoyalTitleObservationSnapshot> source)
        {
            Dictionary<string, RoyalTitleSnapshot> result =
                new Dictionary<string, RoyalTitleSnapshot>(StringComparer.Ordinal);
            List<RoyalTitleObservationSnapshot> normalized =
                RoyaltyStatePersistence.NormalizeTitleObservations(
                    source, RoyaltyStatePersistence.HardMaximumTitleObservations);
            for (int i = 0; i < normalized.Count; i++)
            {
                RoyalTitleObservationSnapshot row = normalized[i];
                result[row.factionId] = new RoyalTitleSnapshot
                {
                    pawnId = pawnId,
                    factionId = row.factionId,
                    factionName = row.factionName,
                    titleDefName = row.titleDefName,
                    titleLabel = row.titleLabel,
                    seniority = row.seniority
                };
            }
            return result;
        }

        private static Dictionary<string, RoyalTitleSnapshot> IndexCurrent(
            IList<RoyalTitleSnapshot> source)
        {
            Dictionary<string, RoyalTitleSnapshot> result =
                new Dictionary<string, RoyalTitleSnapshot>(StringComparer.Ordinal);
            List<RoyalTitleObservationSnapshot> normalized =
                RoyaltyStatePersistence.BaselineTitles(source, 0);
            for (int i = 0; i < normalized.Count; i++)
            {
                RoyalTitleObservationSnapshot row = normalized[i];
                RoyalTitleSnapshot matching = null;
                for (int j = 0; j < (source?.Count ?? 0); j++)
                    if (string.Equals(source[j]?.factionId, row.factionId, StringComparison.Ordinal)
                        && string.Equals(source[j]?.titleDefName, row.titleDefName, StringComparison.Ordinal))
                    {
                        matching = source[j];
                        break;
                    }
                if (matching != null) result[row.factionId] = matching;
            }
            return result;
        }

        private static List<RoyalTitleObservationSnapshot> Copy(
            IList<RoyalTitleObservationSnapshot> source)
        {
            List<RoyalTitleObservationSnapshot> result = new List<RoyalTitleObservationSnapshot>();
            for (int i = 0; i < (source?.Count ?? 0); i++)
            {
                RoyalTitleObservationSnapshot row = source[i];
                if (row == null) continue;
                result.Add(new RoyalTitleObservationSnapshot
                {
                    factionId = row.factionId,
                    factionName = row.factionName,
                    titleDefName = row.titleDefName,
                    titleLabel = row.titleLabel,
                    seniority = row.seniority,
                    lastObservedTick = row.lastObservedTick
                });
            }
            return result;
        }

        private static bool SameTitle(RoyalTitleSnapshot left, RoyalTitleSnapshot right)
        {
            return string.Equals(left?.titleDefName ?? string.Empty,
                right?.titleDefName ?? string.Empty, StringComparison.Ordinal);
        }

        private static string SafeId(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return cleaned.Length > 0 && cleaned.IndexOf('|') < 0 && cleaned.IndexOf(';') < 0
                ? cleaned
                : string.Empty;
        }
    }
}

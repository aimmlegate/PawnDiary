// Captures how a humanlike pawn became a colonist at the Pawn.SetFaction boundary, then hands
// those facts to DiaryGameComponent. This catches many vanilla, quest, DLC, and modded join paths
// without referencing DLC-specific defs or incident workers.
// New to C#/RimWorld? See AGENTS.md ("Harmony patches").
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Stores one short-lived, unsaved summary of the faction transition that made a pawn join the
    /// colony. If the postfix cannot consume the cache, the periodic free-colonist scan still records
    /// a generic arrival entry later.
    /// </summary>
    public static class ArrivalContextCache
    {
        private static readonly Dictionary<string, string> CachedByPawnId = new Dictionary<string, string>();
        private static readonly Queue<string> CachedPawnOrder = new Queue<string>();

        // Arrival facts are consumed in the Pawn.SetFaction postfix right after Capture, so a
        // leftover entry only happens when a join path doesn't reach the postfix. Cap the cache so
        // any such strays can't accumulate over a long game.
        private const int MaxCachedEntries = 64;

        /// <summary>
        /// Clears stale, unsaved arrival facts when RimWorld starts or loads a different Game.
        /// </summary>
        public static void Clear()
        {
            CachedByPawnId.Clear();
            CachedPawnOrder.Clear();
        }

        /// <summary>
        /// Runs before Pawn.SetFaction mutates the pawn, so we can see the prior faction and whether
        /// this is a real transition into the player colony.
        /// </summary>
        public static void Capture(Pawn pawn, Faction newFaction, Pawn recruiter)
        {
            if (pawn == null || newFaction != Faction.OfPlayer || !IsHumanlike(pawn))
            {
                return;
            }

            if (pawn.Faction == Faction.OfPlayer || pawn.IsColonist)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return;
            }

            List<string> parts = new List<string>
            {
                "arrival_source=set_faction"
            };

            AppendFaction(parts, pawn.Faction);
            AppendPawnKind(parts, pawn);

            if (recruiter != null)
            {
                parts.Add("recruiter=" + DiaryContextBuilder.CleanLine(recruiter.LabelShortCap));
            }

            if (DlcContext.IsCreepJoiner(pawn))
            {
                parts.Add("creepjoiner=true");
            }

            string surroundings = DiaryContextBuilder.BuildSurroundingsSummary(pawn);
            if (!string.IsNullOrWhiteSpace(surroundings) && surroundings != "unknown" && surroundings != "none")
            {
                parts.Add("arrival_surroundings=" + surroundings);
            }

            Store(pawnId, string.Join("; ", parts.ToArray()));
        }

        /// <summary>
        /// Returns and removes the cached join facts for a pawn, or builds a small fallback when the
        /// faction transition happened outside the exact patch window.
        /// </summary>
        public static string ConsumeOrBuild(Pawn pawn, string fallbackSource)
        {
            if (pawn == null)
            {
                return string.Empty;
            }

            string pawnId = pawn.GetUniqueLoadID();
            string cached;
            if (!string.IsNullOrWhiteSpace(pawnId) && CachedByPawnId.TryGetValue(pawnId, out cached))
            {
                CachedByPawnId.Remove(pawnId);
                CompactOrderIfNeeded();
                return cached;
            }

            List<string> parts = new List<string>
            {
                "arrival_source=" + (string.IsNullOrWhiteSpace(fallbackSource) ? "unknown" : fallbackSource)
            };

            AppendPawnKind(parts, pawn);
            if (DlcContext.IsCreepJoiner(pawn))
            {
                parts.Add("creepjoiner=true");
            }

            return string.Join("; ", parts.ToArray());
        }

        private static void Store(string pawnId, string context)
        {
            if (!CachedByPawnId.ContainsKey(pawnId))
            {
                CachedPawnOrder.Enqueue(pawnId);
            }

            CachedByPawnId[pawnId] = context;
            PruneOldestEntries();
            CompactOrderIfNeeded();
        }

        private static void PruneOldestEntries()
        {
            while (CachedByPawnId.Count > MaxCachedEntries && CachedPawnOrder.Count > 0)
            {
                string oldestPawnId = CachedPawnOrder.Dequeue();
                CachedByPawnId.Remove(oldestPawnId);
            }
        }

        private static void CompactOrderIfNeeded()
        {
            if (CachedPawnOrder.Count <= MaxCachedEntries * 2)
            {
                return;
            }

            CachedPawnOrder.Clear();
            foreach (string pawnId in CachedByPawnId.Keys)
            {
                CachedPawnOrder.Enqueue(pawnId);
            }
        }

        private static void AppendFaction(List<string> parts, Faction faction)
        {
            if (faction == null)
            {
                parts.Add("priorFaction=none");
                return;
            }

            string label = DiaryContextBuilder.CleanLine(faction.Name);
            if (string.IsNullOrWhiteSpace(label) && faction.def != null)
            {
                label = faction.def.defName;
            }

            if (!string.IsNullOrWhiteSpace(label))
            {
                parts.Add("priorFaction=" + label);
            }
        }

        private static void AppendPawnKind(List<string> parts, Pawn pawn)
        {
            if (pawn?.kindDef == null)
            {
                return;
            }

            parts.Add("pawnKind=" + pawn.kindDef.defName);
        }

        private static bool IsHumanlike(Pawn pawn)
        {
            return pawn != null && pawn.RaceProps != null && pawn.RaceProps.Humanlike;
        }
    }
}

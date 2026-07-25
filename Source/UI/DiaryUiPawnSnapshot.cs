// Shared, one-frame pawn enumeration for diary UI adapters. The standalone reader and name
// highlighter deliberately refresh on the same bounded cadence; sharing this snapshot prevents both
// from scanning every map and world pawn independently during that refresh frame.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Provides session-safe pawn snapshots shared by diary UI work performed in one Unity frame.
    /// </summary>
    internal static class DiaryUiPawnSnapshot
    {
        // Weak references are intentional: if the player returns to the main menu after the final diary
        // draw, this static one-frame cache must not keep the old Game/Pawn graph alive indefinitely.
        private static readonly List<WeakReference> nameHighlightPawns = new List<WeakReference>();
        private static readonly List<WeakReference> resolvedReaderPawns = new List<WeakReference>();
        private static WeakReference cachedGame;
        private static int cachedFrame = -1;
        private static bool nameSnapshotBuilt;
        private static bool readerSnapshotBuilt;

        /// <summary>
        /// Returns map pawns plus living world pawns, matching the name-highlighter's original scope.
        /// </summary>
        public static IReadOnlyList<WeakReference> NameHighlightPawns()
        {
            BeginFrame();
            BuildNameSnapshotIfNeeded();
            return nameHighlightPawns;
        }

        /// <summary>
        /// Returns the reader's broader resolution set, reusing the name snapshot's map/world-live scan.
        /// </summary>
        public static IReadOnlyList<WeakReference> ResolvedReaderPawns()
        {
            BeginFrame();
            if (readerSnapshotBuilt)
            {
                return resolvedReaderPawns;
            }

            BuildNameSnapshotIfNeeded();
            resolvedReaderPawns.AddRange(nameHighlightPawns);

            List<Map> maps = Find.Maps;
            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    Map map = maps[i];
                    AddSpawnedCorpses(map, resolvedReaderPawns);
                    AddCasketCorpses(map, resolvedReaderPawns);
                }
            }

            IEnumerable<Pawn> travelling =
                PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;
            if (travelling != null)
            {
                foreach (Pawn pawn in travelling)
                {
                    AddPawn(resolvedReaderPawns, pawn);
                }
            }

            if (Find.WorldPawns?.AllPawnsDead != null)
            {
                foreach (Pawn pawn in Find.WorldPawns.AllPawnsDead)
                {
                    AddPawn(resolvedReaderPawns, pawn);
                }
            }

            readerSnapshotBuilt = true;
            return resolvedReaderPawns;
        }

        private static void BeginFrame()
        {
            Game game = Current.Game;
            Game previousGame = cachedGame == null ? null : cachedGame.Target as Game;
            int frame = Time.frameCount;
            if (!DiaryUiPolicy.SessionChanged(previousGame, game) && cachedFrame == frame)
            {
                return;
            }

            cachedGame = game == null ? null : new WeakReference(game);
            cachedFrame = frame;
            nameSnapshotBuilt = false;
            readerSnapshotBuilt = false;
            nameHighlightPawns.Clear();
            resolvedReaderPawns.Clear();
        }

        private static void BuildNameSnapshotIfNeeded()
        {
            if (nameSnapshotBuilt)
            {
                return;
            }

            List<Map> maps = Find.Maps;
            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    List<Pawn> pawns = maps[i]?.mapPawns?.AllPawns;
                    if (pawns == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < pawns.Count; j++)
                    {
                        AddPawn(nameHighlightPawns, pawns[j]);
                    }
                }
            }

            if (Find.WorldPawns?.AllPawnsAlive != null)
            {
                foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive)
                {
                    AddPawn(nameHighlightPawns, pawn);
                }
            }

            nameSnapshotBuilt = true;
        }

        private static void AddSpawnedCorpses(Map map, List<WeakReference> target)
        {
            List<Thing> corpses = map?.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
            if (corpses == null)
            {
                return;
            }

            for (int i = 0; i < corpses.Count; i++)
            {
                AddPawn(target, (corpses[i] as Corpse)?.InnerPawn);
            }
        }

        private static void AddCasketCorpses(Map map, List<WeakReference> target)
        {
            if (map?.listerBuildings == null)
            {
                return;
            }

            AddCasketCorpses(map.listerBuildings.allBuildingsColonist, target);
            AddCasketCorpses(map.listerBuildings.allBuildingsNonColonist, target);
        }

        private static void AddCasketCorpses(IEnumerable<Building> buildings, List<WeakReference> target)
        {
            if (buildings == null)
            {
                return;
            }

            foreach (Building building in buildings)
            {
                Building_Casket casket = building as Building_Casket;
                AddPawn(target, (casket?.ContainedThing as Corpse)?.InnerPawn);
            }
        }

        private static void AddPawn(List<WeakReference> target, Pawn pawn)
        {
            if (pawn != null)
            {
                target.Add(new WeakReference(pawn));
            }
        }
    }
}

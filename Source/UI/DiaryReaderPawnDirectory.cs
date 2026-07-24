// Throttled RimWorld adapter that combines saved diary records with live/dead pawn resolution.
// World/map scans belong here at the impure UI edge; ordering and inclusion stay in the System-only
// DiaryReaderListPolicy so they are independently testable.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Render-ready row for the standalone reader's left pawn directory.
    /// </summary>
    internal struct DiaryReaderPawnRow
    {
        public DiaryReaderSubject Subject;
        public int EntryCount;
        public bool Departed;
    }

    /// <summary>
    /// Owns a throttled snapshot of reader subjects and their optional Pawn objects.
    /// </summary>
    internal sealed class DiaryReaderPawnDirectory
    {
        private const int RefreshTicks = 250;
        private const float RefreshRealtimeSeconds = 0.5f;

        private readonly List<DiaryGameComponent.DiaryReaderPawnInfo> savedInfo =
            new List<DiaryGameComponent.DiaryReaderPawnInfo>();
        private readonly Dictionary<string, Pawn> resolvedPawns =
            new Dictionary<string, Pawn>(StringComparer.Ordinal);
        private readonly HashSet<string> currentLivingColonistIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, DiaryReaderListRow> policyRowsByPawnId =
            new Dictionary<string, DiaryReaderListRow>(StringComparer.Ordinal);
        private readonly List<DiaryReaderPawnRow> rows = new List<DiaryReaderPawnRow>();

        private int departedDividerIndex;
        private int lastDataCount = -1;
        private int lastBuildTick = int.MinValue;
        private float lastBuildRealtime = -1f;

        public IReadOnlyList<DiaryReaderPawnRow> Rows
        {
            get { return rows; }
        }

        public int DepartedDividerIndex
        {
            get { return departedDividerIndex; }
        }

        /// <summary>
        /// Rebuilds on saved-data count changes or at the bounded world-resolution cadence.
        /// </summary>
        public void RefreshIfNeeded(
            DiaryGameComponent component,
            string unknownName,
            bool force)
        {
            if (component == null)
            {
                rows.Clear();
                departedDividerIndex = 0;
                return;
            }

            int dataCount = component.DiaryReaderDirectoryDataCount;
            int nowTick = Find.TickManager?.TicksGame ?? 0;
            float nowRealtime = Time.realtimeSinceStartup;
            bool dataChanged = dataCount != lastDataCount;
            bool tickElapsed = lastBuildTick == int.MinValue || nowTick - lastBuildTick >= RefreshTicks;
            bool realtimeElapsed = lastBuildRealtime < 0f
                || nowRealtime - lastBuildRealtime >= RefreshRealtimeSeconds;
            if (!force && !dataChanged && !(tickElapsed && realtimeElapsed))
            {
                return;
            }

            Rebuild(component, unknownName);
            lastDataCount = dataCount;
            lastBuildTick = nowTick;
            lastBuildRealtime = nowRealtime;
        }

        private void Rebuild(DiaryGameComponent component, string unknownName)
        {
            resolvedPawns.Clear();
            CollectResolvedPawns();
            CollectCurrentLivingColonists();
            component.CollectDiaryReaderPawns(savedInfo);
            policyRowsByPawnId.Clear();

            // Any current living colonist can open a diary, including a new colonist with zero pages.
            foreach (string pawnId in currentLivingColonistIds)
            {
                Pawn pawn;
                if (!resolvedPawns.TryGetValue(pawnId, out pawn) || pawn == null || pawn.Dead)
                {
                    continue;
                }

                policyRowsByPawnId[pawnId] = PolicyRow(pawn, pawnId, 0, true);
            }

            for (int i = 0; i < savedInfo.Count; i++)
            {
                DiaryGameComponent.DiaryReaderPawnInfo info = savedInfo[i];
                Pawn pawn;
                resolvedPawns.TryGetValue(info.pawnId, out pawn);
                DiaryReaderListRow row;
                if (!policyRowsByPawnId.TryGetValue(info.pawnId, out row))
                {
                    row = PolicyRow(
                        pawn,
                        info.pawnId,
                        info.EntryCount,
                        currentLivingColonistIds.Contains(info.pawnId));
                    policyRowsByPawnId[info.pawnId] = row;
                }

                row.entryCount = info.EntryCount;
                if (string.IsNullOrWhiteSpace(row.name))
                {
                    row.name = info.cachedName;
                }
            }

            DiaryReaderListResult ordered = DiaryReaderListPolicy.Order(
                policyRowsByPawnId.Values,
                unknownName);
            rows.Clear();
            departedDividerIndex = ordered.departedDividerIndex;
            for (int i = 0; i < ordered.rows.Count; i++)
            {
                DiaryReaderListRow policyRow = ordered.rows[i];
                Pawn pawn;
                resolvedPawns.TryGetValue(policyRow.pawnId, out pawn);
                rows.Add(new DiaryReaderPawnRow
                {
                    Subject = new DiaryReaderSubject
                    {
                        Pawn = pawn,
                        PawnId = policyRow.pawnId,
                        DisplayName = policyRow.name,
                        Alive = policyRow.alive
                    },
                    EntryCount = policyRow.entryCount,
                    Departed = i >= departedDividerIndex
                });
            }
        }

        private static DiaryReaderListRow PolicyRow(
            Pawn pawn,
            string pawnId,
            int entryCount,
            bool isCurrentColonist)
        {
            return new DiaryReaderListRow
            {
                pawnId = pawnId,
                name = pawn?.LabelShortCap ?? string.Empty,
                alive = pawn != null && !pawn.Dead,
                isCurrentColonist = isCurrentColonist,
                entryCount = entryCount
            };
        }

        /// <summary>
        /// Marks only pawns in vanilla's current free-colonist roster as living reader subjects.
        /// World pawns can retain the player faction and report IsColonist after leaving the active
        /// roster, so deriving this flag from every resolved pawn leaks outsiders into "Colonists".
        /// </summary>
        private void CollectCurrentLivingColonists()
        {
            currentLivingColonistIds.Clear();
            IEnumerable<Pawn> colonists =
                PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists;
            if (colonists == null)
            {
                return;
            }

            foreach (Pawn pawn in colonists)
            {
                if (pawn == null || pawn.Dead)
                {
                    continue;
                }

                AddPawn(pawn);
                string pawnId = pawn.GetUniqueLoadID();
                if (!string.IsNullOrWhiteSpace(pawnId))
                {
                    currentLivingColonistIds.Add(pawnId);
                }
            }
        }

        private void CollectResolvedPawns()
        {
            List<Map> maps = Find.Maps;
            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    Map map = maps[i];
                    List<Pawn> mapPawns = map?.mapPawns?.AllPawns;
                    if (mapPawns != null)
                    {
                        for (int j = 0; j < mapPawns.Count; j++)
                        {
                            AddPawn(mapPawns[j]);
                        }
                    }

                    CollectSpawnedCorpses(map);
                    CollectCasketCorpses(map);
                }
            }

            IEnumerable<Pawn> travelling = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;
            if (travelling != null)
            {
                foreach (Pawn pawn in travelling)
                {
                    AddPawn(pawn);
                }
            }

            if (Find.WorldPawns != null)
            {
                foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive)
                {
                    AddPawn(pawn);
                }

                foreach (Pawn pawn in Find.WorldPawns.AllPawnsDead)
                {
                    AddPawn(pawn);
                }
            }
        }

        private void CollectSpawnedCorpses(Map map)
        {
            List<Thing> corpses = map?.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
            if (corpses == null)
            {
                return;
            }

            for (int i = 0; i < corpses.Count; i++)
            {
                AddPawn((corpses[i] as Corpse)?.InnerPawn);
            }
        }

        private void CollectCasketCorpses(Map map)
        {
            if (map?.listerBuildings == null)
            {
                return;
            }

            CollectCasketCorpses(map.listerBuildings.allBuildingsColonist);
            CollectCasketCorpses(map.listerBuildings.allBuildingsNonColonist);
        }

        private void CollectCasketCorpses(IEnumerable<Building> buildings)
        {
            if (buildings == null)
            {
                return;
            }

            foreach (Building building in buildings)
            {
                Building_Casket casket = building as Building_Casket;
                AddPawn((casket?.ContainedThing as Corpse)?.InnerPawn);
            }
        }

        private void AddPawn(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            if (!string.IsNullOrWhiteSpace(pawnId) && !resolvedPawns.ContainsKey(pawnId))
            {
                resolvedPawns[pawnId] = pawn;
            }
        }
    }
}

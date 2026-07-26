// Throttled RimWorld adapter that combines saved diary records with live/dead pawn resolution.
// World/map scans stay at the impure UI edge and are coordinated through DiaryUiPawnSnapshot so the
// reader and name highlighter do not enumerate the same pawn lists twice in one frame. Ordering and
// inclusion stay in the System-only DiaryReaderListPolicy so they are independently testable.
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
        public bool HasLatestEntry;
        public string LatestEntryDate;
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

        private int eligibleRowCount;
        private bool groupedByDeparture;
        private int lastDataCount = -1;
        private int lastDiaryStateVersion = -1;
        private int lastUnreadStateVersion = -1;
        private int lastBuildTick = int.MinValue;
        private float lastBuildRealtime = -1f;
        private DiaryReaderSortMode lastSortMode = DiaryReaderSortMode.NewestPage;
        private string lastSearchQuery = string.Empty;
        private Game cachedGame;
        private DiaryGameComponent cachedComponent;

        public IReadOnlyList<DiaryReaderPawnRow> Rows
        {
            get { return rows; }
        }

        public int EligibleRowCount
        {
            get { return eligibleRowCount; }
        }

        public bool GroupedByDeparture
        {
            get { return groupedByDeparture; }
        }

        /// <summary>
        /// Rebuilds when the session, saved data, activity/unread state, or search/sort controls change,
        /// and periodically refreshes live pawn resolution on a bounded cadence.
        /// </summary>
        public void RefreshIfNeeded(
            DiaryGameComponent component,
            string unknownName,
            DiaryReaderSortMode sortMode,
            string searchQuery,
            bool force)
        {
            Game game = Current.Game;
            bool sessionChanged = DiaryUiPolicy.ReaderDirectorySessionChanged(
                cachedGame,
                game,
                cachedComponent,
                component);
            if (sessionChanged)
            {
                // Data count, ticks, and pawn load IDs can all repeat in another save. Clear every
                // strong Pawn reference before considering the throttle, then force the new session's
                // first directory snapshot even when its record count happens to be identical.
                ResetForSession(game, component);
            }

            if (game == null || component == null)
            {
                return;
            }

            int dataCount = component.DiaryReaderDirectoryDataCount;
            int diaryStateVersion = DiaryStateVersion.Current;
            int unreadStateVersion = component.DiaryReaderUnreadStateVersion;
            int nowTick = Find.TickManager?.TicksGame ?? 0;
            float nowRealtime = Time.realtimeSinceStartup;
            bool dataChanged = dataCount != lastDataCount;
            bool stateChanged = diaryStateVersion != lastDiaryStateVersion
                || unreadStateVersion != lastUnreadStateVersion;
            string normalizedSearch = searchQuery ?? string.Empty;
            bool controlsChanged = sortMode != lastSortMode
                || !string.Equals(normalizedSearch, lastSearchQuery, StringComparison.Ordinal);
            bool tickElapsed = lastBuildTick == int.MinValue || nowTick - lastBuildTick >= RefreshTicks;
            bool realtimeElapsed = lastBuildRealtime < 0f
                || nowRealtime - lastBuildRealtime >= RefreshRealtimeSeconds;
            if (!force
                && !sessionChanged
                && !dataChanged
                && !stateChanged
                && !controlsChanged
                && !(tickElapsed && realtimeElapsed))
            {
                return;
            }

            Rebuild(component, unknownName, sortMode, normalizedSearch);
            lastDataCount = dataCount;
            lastDiaryStateVersion = diaryStateVersion;
            lastUnreadStateVersion = unreadStateVersion;
            lastBuildTick = nowTick;
            lastBuildRealtime = nowRealtime;
            lastSortMode = sortMode;
            lastSearchQuery = normalizedSearch;
        }

        private void ResetForSession(Game game, DiaryGameComponent component)
        {
            cachedGame = game;
            cachedComponent = component;
            savedInfo.Clear();
            resolvedPawns.Clear();
            currentLivingColonistIds.Clear();
            policyRowsByPawnId.Clear();
            rows.Clear();
            eligibleRowCount = 0;
            groupedByDeparture = false;
            lastDataCount = -1;
            lastDiaryStateVersion = -1;
            lastUnreadStateVersion = -1;
            lastBuildTick = int.MinValue;
            lastBuildRealtime = -1f;
            lastSortMode = DiaryReaderSortMode.NewestPage;
            lastSearchQuery = string.Empty;
        }

        private void Rebuild(
            DiaryGameComponent component,
            string unknownName,
            DiaryReaderSortMode sortMode,
            string searchQuery)
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
                row.unreadCount = info.unreadCount;
                row.hasLatestEntry = info.hasLatestEntry;
                row.latestEntryTick = info.latestEntryTick;
                row.latestEntryDate = info.latestEntryDate;
                if (string.IsNullOrWhiteSpace(row.name))
                {
                    row.name = info.cachedName;
                }
            }

            DiaryReaderListResult ordered = DiaryReaderListPolicy.Order(
                policyRowsByPawnId.Values,
                unknownName,
                sortMode,
                searchQuery);
            rows.Clear();
            eligibleRowCount = ordered.eligibleRowCount;
            groupedByDeparture = ordered.groupedByDeparture;
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
                    HasLatestEntry = policyRow.hasLatestEntry,
                    LatestEntryDate = policyRow.latestEntryDate ?? string.Empty,
                    Departed = DiaryReaderListPolicy.IsDeparted(policyRow)
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
            IReadOnlyList<WeakReference> pawns = DiaryUiPawnSnapshot.ResolvedReaderPawns();
            for (int i = 0; i < pawns.Count; i++)
            {
                AddPawn(pawns[i].Target as Pawn);
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

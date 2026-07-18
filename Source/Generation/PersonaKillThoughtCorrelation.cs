// Short-lived exact ownership bridge for persona-weapon kill thoughts. Vanilla grants a trait's
// kill memory before it records the matching Tale inside Pawn.Kill. We stage only the exact killer
// and ThoughtDef pair, let a durable first-kill page claim it, and otherwise submit it unchanged when
// Pawn.Kill closes. This class stores no save data and is reset at every game/load boundary.
using System;
using System.Collections.Generic;
using PawnDiary.Ingestion;
using Verse;

namespace PawnDiary
{
    /// <summary>One nested Pawn.Kill scope for an exact persona wielder and victim.</summary>
    internal sealed class PersonaKillCorrelationScope
    {
        public string killerPawnId = string.Empty;
        public string victimPawnId = string.Empty;
        public int openedTick;
        public int correlationTicks = 60;
        public bool claimed;
        public List<string> thoughtDefNames = new List<string>();
        public List<ThoughtSignal> stagedSignals = new List<ThoughtSignal>();
    }

    /// <summary>Stages, claims, or releases exact persona kill-memory signals.</summary>
    internal static class PersonaKillThoughtCorrelation
    {
        private sealed class RecentOwner
        {
            public string pawnId = string.Empty;
            public int claimedTick;
            public int correlationTicks;
            public List<string> thoughtDefNames = new List<string>();
        }

        private const int MaximumActiveScopes = 16;
        private const int MaximumRecentOwners = 32;
        private const int MaximumStagedSignalsPerScope = 8;
        private static readonly List<PersonaKillCorrelationScope> Active =
            new List<PersonaKillCorrelationScope>();
        private static readonly List<RecentOwner> Recent = new List<RecentOwner>();

        /// <summary>Opens an exact scope after DlcContext proved the killer's current primary bond.</summary>
        public static PersonaKillCorrelationScope Begin(
            Pawn victim,
            Pawn killer,
            IList<string> thoughtDefNames,
            int tick,
            int correlationTicks)
        {
            string killerId = killer?.GetUniqueLoadID() ?? string.Empty;
            string victimId = victim?.GetUniqueLoadID() ?? string.Empty;
            if (!ModsConfig.RoyaltyActive || killerId.Length == 0 || victimId.Length == 0) return null;
            PersonaKillCorrelationScope scope = new PersonaKillCorrelationScope
            {
                killerPawnId = killerId,
                victimPawnId = victimId,
                openedTick = Math.Max(0, tick),
                correlationTicks = Math.Max(1, correlationTicks),
                thoughtDefNames = CopyThoughtNames(thoughtDefNames)
            };
            Active.Add(scope);
            if (Active.Count > MaximumActiveScopes) Active.RemoveAt(0);
            return scope;
        }

        /// <summary>Returns the innermost exact active kill scope; no tick-only matching is allowed.</summary>
        public static bool TryMatchActiveKill(
            Pawn killer,
            Pawn victim,
            out PersonaKillCorrelationScope scope)
        {
            scope = null;
            string killerId = killer?.GetUniqueLoadID() ?? string.Empty;
            string victimId = victim?.GetUniqueLoadID() ?? string.Empty;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                PersonaKillCorrelationScope row = Active[i];
                if (row != null && string.Equals(row.killerPawnId, killerId, StringComparison.Ordinal)
                    && string.Equals(row.victimPawnId, victimId, StringComparison.Ordinal))
                {
                    scope = row;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Claims a matching thought signal. True means the caller must not submit it normally;
        /// false means it was unrelated and remains on the ordinary Thought path.
        /// </summary>
        public static bool TryStageOrSuppress(Pawn pawn, string thoughtDefName, ThoughtSignal signal, int tick)
        {
            // This hook sees every memory in every game. Make the no-Royalty path one cheap flag
            // check before touching either correlation list.
            if (!ModsConfig.RoyaltyActive) return false;
            string pawnId = pawn?.GetUniqueLoadID() ?? string.Empty;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                PersonaKillCorrelationScope scope = Active[i];
                if (!PersonaThoughtOwnershipPolicy.Matches(
                    scope?.killerPawnId, scope?.thoughtDefNames, pawnId, thoughtDefName)) continue;
                if (!scope.claimed && signal != null
                    && scope.stagedSignals.Count < MaximumStagedSignalsPerScope)
                    scope.stagedSignals.Add(signal);
                return true;
            }

            PruneRecent(tick);
            for (int i = Recent.Count - 1; i >= 0; i--)
                if (PersonaThoughtOwnershipPolicy.Matches(
                    Recent[i].pawnId, Recent[i].thoughtDefNames, pawnId, thoughtDefName)) return true;
            return false;
        }

        /// <summary>Promotes the exact active scope only after the canonical Tale page exists.</summary>
        public static void Claim(PersonaKillCorrelationScope scope, int tick)
        {
            if (scope == null || !Active.Contains(scope)) return;
            scope.claimed = true;
            scope.stagedSignals.Clear();
            if (scope.thoughtDefNames.Count == 0) return;
            Recent.Add(new RecentOwner
            {
                pawnId = scope.killerPawnId,
                claimedTick = Math.Max(0, tick),
                correlationTicks = Math.Max(1, scope.correlationTicks),
                thoughtDefNames = CopyThoughtNames(scope.thoughtDefNames)
            });
            while (Recent.Count > MaximumRecentOwners) Recent.RemoveAt(0);
        }

        /// <summary>Closes one exact scope and releases unclaimed thoughts to their normal pipeline.</summary>
        public static void End(PersonaKillCorrelationScope scope)
        {
            if (scope == null) return;
            Active.Remove(scope);
            if (scope.claimed) return;
            List<ThoughtSignal> staged = new List<ThoughtSignal>(scope.stagedSignals);
            scope.stagedSignals.Clear();
            for (int i = 0; i < staged.Count; i++) DiaryEvents.Submit(staged[i]);
        }

        /// <summary>Clears all unsaved ownership state at a new-game or load boundary.</summary>
        public static void Clear()
        {
            Active.Clear();
            Recent.Clear();
        }

        private static void PruneRecent(int tick)
        {
            for (int i = Recent.Count - 1; i >= 0; i--)
            {
                RecentOwner row = Recent[i];
                long elapsed = (long)Math.Max(0, tick) - row.claimedTick;
                if (elapsed < 0 || elapsed > Math.Max(1, row.correlationTicks)) Recent.RemoveAt(i);
            }
        }

        private static List<string> CopyThoughtNames(IList<string> source)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < (source == null ? 0 : source.Count); i++)
            {
                string value = (source[i] ?? string.Empty).Trim();
                if (value.Length > 0 && seen.Add(value)) result.Add(value);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}

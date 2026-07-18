// Synchronous persona-bond thought ownership. CodeFor can grant a normal mood memory while the same
// action also creates the higher-value lifecycle page. We stage only exact pawn+ThoughtDef matches,
// then either let the accepted lifecycle page own them or release them through the ordinary thought
// pipeline. All caches are transient and reset between games.
using System;
using System.Collections.Generic;
using PawnDiary.Ingestion;
using Verse;

namespace PawnDiary
{
    /// <summary>Stages exact persona-trait memories around one synchronous coding action.</summary>
    internal static class RoyaltyPersonaThoughtCorrelation
    {
        private const int HardMaximumRecentOwners = 128;

        internal sealed class Scope
        {
            public string pawnId = string.Empty;
            public List<string> thoughtDefNames = new List<string>();
            public List<DiarySignal> stagedSignals = new List<DiarySignal>();
            public bool closed;
        }

        private static readonly List<Scope> activeScopes = new List<Scope>();
        private static readonly Dictionary<string, int> recentOwners = new Dictionary<string, int>();

        /// <summary>Opens an exact owner scope, or returns null when no persona memory can be claimed.</summary>
        public static Scope Begin(string pawnId, List<string> thoughtDefNames)
        {
            string cleanedPawn = Clean(pawnId);
            if (cleanedPawn.Length == 0 || thoughtDefNames == null || thoughtDefNames.Count == 0)
                return null;
            Scope scope = new Scope { pawnId = cleanedPawn, thoughtDefNames = new List<string>(thoughtDefNames) };
            activeScopes.Add(scope);
            return scope;
        }

        /// <summary>
        /// Claims a thought for an active/recent lifecycle owner. Active matches are staged; recent
        /// matches are simply suppressed because their accepted lifecycle page already owns them.
        /// </summary>
        public static bool TryStage(string pawnId, string thoughtDefName, DiarySignal signal)
        {
            if (signal == null) return false;
            int now = Find.TickManager?.TicksGame ?? 0;
            Prune(now);
            string key = Key(pawnId, thoughtDefName);
            int expires;
            if (key.Length > 0 && recentOwners.TryGetValue(key, out expires) && now <= expires)
                return true;

            for (int i = activeScopes.Count - 1; i >= 0; i--)
            {
                Scope scope = activeScopes[i];
                if (scope == null || scope.closed || !PersonaThoughtOwnershipPolicy.Matches(
                    scope.pawnId, scope.thoughtDefNames, pawnId, thoughtDefName)) continue;
                signal.PreserveHistoricalOrdering(now);
                scope.stagedSignals.Add(signal);
                return true;
            }
            return false;
        }

        /// <summary>Closes a coding scope; accepted pages consume staged memories, otherwise release them.</summary>
        public static void Close(Scope scope, bool lifecyclePageAccepted)
        {
            if (scope == null || scope.closed) return;
            scope.closed = true;
            activeScopes.Remove(scope);
            int now = Find.TickManager?.TicksGame ?? 0;
            if (lifecyclePageAccepted)
            {
                int window = Math.Max(1, DiaryRoyaltyPolicy.Snapshot().personaThoughtCorrelationTicks);
                int expiry = now > int.MaxValue - window ? int.MaxValue : now + window;
                for (int i = 0; i < scope.thoughtDefNames.Count; i++)
                {
                    string key = Key(scope.pawnId, scope.thoughtDefNames[i]);
                    if (key.Length > 0) recentOwners[key] = expiry;
                }
                TrimRecentOwners();
                scope.stagedSignals.Clear();
                return;
            }

            List<DiarySignal> release = new List<DiarySignal>(scope.stagedSignals);
            scope.stagedSignals.Clear();
            for (int i = 0; i < release.Count; i++)
            {
                try
                {
                    DiaryEvents.Submit(release[i]);
                }
                catch (Exception exception)
                {
                    // One malformed ordinary Thought route must not strand the remaining staged
                    // memories or escape back through vanilla's CodeFor callback.
                    Log.ErrorOnce(
                        "[Pawn Diary] Releasing a persona-bond thought to ordinary capture failed: "
                        + exception,
                        "PawnDiary.PersonaThought.Release".GetHashCode());
                }
            }
        }

        /// <summary>Clears active and recent ownership on game/session boundaries.</summary>
        public static void Reset()
        {
            activeScopes.Clear();
            recentOwners.Clear();
        }

        private static void Prune(int now)
        {
            if (recentOwners.Count == 0) return;
            List<string> expired = new List<string>();
            foreach (KeyValuePair<string, int> pair in recentOwners)
                if (now > pair.Value) expired.Add(pair.Key);
            for (int i = 0; i < expired.Count; i++) recentOwners.Remove(expired[i]);
        }

        private static void TrimRecentOwners()
        {
            while (recentOwners.Count > HardMaximumRecentOwners)
            {
                string oldestKey = null;
                int oldestExpiry = int.MaxValue;
                foreach (KeyValuePair<string, int> pair in recentOwners)
                    if (pair.Value < oldestExpiry
                        || pair.Value == oldestExpiry && string.CompareOrdinal(pair.Key, oldestKey) < 0)
                    {
                        oldestKey = pair.Key;
                        oldestExpiry = pair.Value;
                    }
                if (oldestKey == null) break;
                recentOwners.Remove(oldestKey);
            }
        }

        private static string Key(string pawnId, string thoughtDefName)
        {
            string pawn = Clean(pawnId);
            string thought = Clean(thoughtDefName).ToLowerInvariant();
            return pawn.Length == 0 || thought.Length == 0 || pawn.IndexOf('|') >= 0
                || thought.IndexOf('|') >= 0 ? string.Empty : pawn + "|" + thought;
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}

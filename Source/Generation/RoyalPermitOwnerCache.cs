// Transient ownership bridge for Royalty permits. Vanilla's successful-use callback carries the
// FactionPermit instance but not its pawn, while Pawn_RoyaltyTracker.GetPermit carries both. This
// bounded cache joins those exact references without retaining a Pawn, permit, Def, Faction, or Map
// strongly across play. A rare bounded live-pawn scan recovers when another mod bypassed the normal
// lookup path; ambiguous ownership fails closed.
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>One live owner plus its detached, main-thread snapshot at successful use.</summary>
    internal sealed class RoyalPermitOwnerResolution
    {
        public Pawn pawn;
        public RoyalPermitOwnerCandidate candidate;
    }

    /// <summary>Bounded weak-reference cache joining GetPermit to FactionPermit.Notify_Used.</summary>
    internal static class RoyalPermitOwnerCache
    {
        private sealed class OwnerObservation
        {
            public WeakReference pawn;
            public string pawnId = string.Empty;
            public int observedTick;
        }

        private sealed class PermitSession
        {
            public WeakReference permit;
            public int observedTick;
            public bool ownerOverflowed;
            public readonly List<OwnerObservation> owners = new List<OwnerObservation>();
        }

        private static readonly List<PermitSession> sessions = new List<PermitSession>();

        /// <summary>
        /// Remembers that one tracker returned this exact permit. Repeated UI reads for the same
        /// pawn update integers only, so the common GetPermit hot path stays allocation-free.
        /// </summary>
        public static void Observe(FactionPermit permit, Pawn pawn, int tick, RoyaltyPolicySnapshot policy)
        {
            if (!ModsConfig.RoyaltyActive || permit == null || pawn == null || policy == null) return;
            int now = Math.Max(0, tick);
            PermitSession session = FindSession(permit);
            if (session != null)
            {
                session.observedTick = now;
                for (int i = 0; i < session.owners.Count; i++)
                {
                    OwnerObservation owner = session.owners[i];
                    if (ReferenceEquals(owner.pawn?.Target, pawn))
                    {
                        owner.observedTick = now;
                        return;
                    }
                }
                AddOwner(session, pawn, now, policy.maximumPermitOwnersPerSession);
                return;
            }

            Prune(now, policy.permitOwnerCacheTicks);
            int sessionCap = Clamp(policy.maximumPermitOwnerSessions, 1, 256, 64);
            if (sessions.Count >= sessionCap) RemoveOldestSession();
            session = new PermitSession { permit = new WeakReference(permit), observedTick = now };
            sessions.Add(session);
            AddOwner(session, pawn, now, policy.maximumPermitOwnersPerSession);
        }

        /// <summary>
        /// Resolves one exact eligible owner and refreshes all prompt facts at the success edge.
        /// Multiple distinct eligible owners are deliberately ambiguous and produce no page.
        /// </summary>
        public static RoyalPermitOwnerResolution Resolve(
            FactionPermit permit,
            int tick,
            RoyaltyPolicySnapshot policy)
        {
            if (!ModsConfig.RoyaltyActive || permit == null || policy == null) return null;
            int now = Math.Max(0, tick);
            Prune(now, policy.permitOwnerCacheTicks);

            List<RoyalPermitOwnerCandidate> candidates = new List<RoyalPermitOwnerCandidate>();
            Dictionary<string, Pawn> liveById = new Dictionary<string, Pawn>(StringComparer.Ordinal);
            PermitSession session = FindSession(permit);
            if (session != null)
            {
                if (session.ownerOverflowed) return null;
                for (int i = 0; i < session.owners.Count; i++)
                {
                    Pawn owner = session.owners[i].pawn?.Target as Pawn;
                    AddExactCandidate(permit, owner, now, candidates, liveById);
                }
            }

            // A callback from a compatibility mod can reach Notify_Used without first asking the
            // tracker for its permit. Scan the bounded live-pawn view only when the cheap cache missed.
            if (candidates.Count == 0)
            {
                int scanned = 0;
                int scanCap = Clamp(policy.maximumPermitFallbackPawns, 1, 2048, 256);
                IEnumerable<Pawn> pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;
                if (pawns != null)
                {
                    foreach (Pawn pawn in pawns)
                    {
                        if (scanned++ >= scanCap) break;
                        AddExactCandidate(permit, pawn, now, candidates, liveById);
                    }
                }
            }

            RoyalPermitOwnerCandidate selected = RoyalPermitPolicy.SelectOwner(
                candidates, policy.maximumPermitOwnersPerSession);
            if (selected == null) return null;
            Pawn selectedPawn;
            return liveById.TryGetValue(selected.ownerPawnId, out selectedPawn)
                ? new RoyalPermitOwnerResolution { pawn = selectedPawn, candidate = selected }
                : null;
        }

        /// <summary>Drops all cross-game weak references at load, new-game, and menu boundaries.</summary>
        public static void Reset()
        {
            sessions.Clear();
        }

        internal static int SessionCountForTests => sessions.Count;

        private static void AddExactCandidate(
            FactionPermit permit,
            Pawn pawn,
            int tick,
            List<RoyalPermitOwnerCandidate> candidates,
            Dictionary<string, Pawn> liveById)
        {
            if (pawn == null || !DiaryGameComponent.IsDiaryEligible(pawn)) return;
            RoyalPermitOwnerCandidate captured;
            if (!DlcContext.TryCaptureRoyalPermitOwnerCandidate(pawn, permit, tick, out captured)) return;
            candidates.Add(captured);
            liveById[captured.ownerPawnId] = pawn;
        }

        private static PermitSession FindSession(FactionPermit permit)
        {
            for (int i = 0; i < sessions.Count; i++)
                if (ReferenceEquals(sessions[i].permit?.Target, permit)) return sessions[i];
            return null;
        }

        private static void AddOwner(PermitSession session, Pawn pawn, int tick, int configuredCap)
        {
            int cap = Clamp(configuredCap, 1, 16, 4);
            if (session.owners.Count >= cap)
            {
                session.ownerOverflowed = true;
                return;
            }
            session.owners.Add(new OwnerObservation
            {
                pawn = new WeakReference(pawn),
                pawnId = pawn.GetUniqueLoadID() ?? string.Empty,
                observedTick = tick
            });
        }

        private static void Prune(int now, int configuredLifetime)
        {
            int lifetime = Clamp(configuredLifetime, 1, 60000, 2500);
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                PermitSession session = sessions[i];
                long elapsed = (long)now - session.observedTick;
                if (session.permit?.IsAlive != true || elapsed < 0 || elapsed > lifetime)
                {
                    sessions.RemoveAt(i);
                    continue;
                }
                for (int j = session.owners.Count - 1; j >= 0; j--)
                    if (session.owners[j].pawn?.IsAlive != true) session.owners.RemoveAt(j);
                if (session.owners.Count == 0) sessions.RemoveAt(i);
            }
        }

        private static void RemoveOldestSession()
        {
            if (sessions.Count == 0) return;
            int oldest = 0;
            for (int i = 1; i < sessions.Count; i++)
                if (sessions[i].observedTick < sessions[oldest].observedTick) oldest = i;
            sessions.RemoveAt(oldest);
        }

        private static int Clamp(int value, int minimum, int maximum, int fallback)
        {
            return value >= minimum && value <= maximum ? value : fallback;
        }
    }
}

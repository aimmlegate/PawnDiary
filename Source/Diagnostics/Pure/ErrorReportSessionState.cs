// Pure, thread-safe admission state for optional error telemetry. The HTTP-facing reporter swaps
// one whole instance per loaded game, so a request finishing from the previous game can release only
// its own session's counter. This file deliberately has no RimWorld/Verse/Unity dependencies and is
// covered by a standalone test project.
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace PawnDiary
{
    /// <summary>
    /// Deduplicates error fingerprints and enforces exact per-session and in-flight report caps.
    /// Every successful admission returns a one-shot handle that owns the matching in-flight slot.
    /// </summary>
    internal sealed class ErrorReportSessionState
    {
        private readonly ConcurrentDictionary<string, byte> seenFingerprints =
            new ConcurrentDictionary<string, byte>();
        private readonly int maxReports;
        private readonly int maxInFlight;
        private int uniqueDispatched;
        private int inFlight;

        /// <summary>Creates one independent session with defensive positive limits.</summary>
        public ErrorReportSessionState(int maxReports, int maxInFlight)
        {
            this.maxReports = Math.Max(1, maxReports);
            this.maxInFlight = Math.Max(1, maxInFlight);
        }

        /// <summary>
        /// Attempts to reserve both the fingerprint and one in-flight slot. Failed attempts roll
        /// back every partial claim, keeping the counters and dedupe set bounded under contention.
        /// </summary>
        public bool TryAdmit(string fingerprint, out ErrorReportAdmission admission)
        {
            admission = null;
            if (string.IsNullOrWhiteSpace(fingerprint)
                || !seenFingerprints.TryAdd(fingerprint, 0))
            {
                return false;
            }

            if (!TryIncrementBelow(ref uniqueDispatched, maxReports))
            {
                seenFingerprints.TryRemove(fingerprint, out _);
                return false;
            }

            if (!TryIncrementBelow(ref inFlight, maxInFlight))
            {
                DecrementWithoutGoingNegative(ref uniqueDispatched);
                seenFingerprints.TryRemove(fingerprint, out _);
                return false;
            }

            admission = new ErrorReportAdmission(this, fingerprint);
            return true;
        }

        /// <summary>Number of distinct reports successfully admitted during this session.</summary>
        public int UniqueDispatched
        {
            get { return Volatile.Read(ref uniqueDispatched); }
        }

        /// <summary>Number of admitted reports whose completion handle is still outstanding.</summary>
        public int InFlight
        {
            get { return Volatile.Read(ref inFlight); }
        }

        /// <summary>Number of retained fingerprints; exposed for deterministic standalone tests.</summary>
        public int SeenCount
        {
            get { return seenFingerprints.Count; }
        }

        /// <summary>Releases only the in-flight slot after a scheduled send finishes.</summary>
        internal void Complete()
        {
            DecrementWithoutGoingNegative(ref inFlight);
        }

        /// <summary>
        /// Rolls back an admission when payload construction or task scheduling fails before a send
        /// owns it. The fingerprint can then be attempted again later.
        /// </summary>
        internal void RollBack(string fingerprint)
        {
            seenFingerprints.TryRemove(fingerprint ?? string.Empty, out _);
            DecrementWithoutGoingNegative(ref uniqueDispatched);
            DecrementWithoutGoingNegative(ref inFlight);
        }

        private static bool TryIncrementBelow(ref int value, int limit)
        {
            while (true)
            {
                int observed = Volatile.Read(ref value);
                if (observed >= limit)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref value, observed + 1, observed) == observed)
                {
                    return true;
                }
            }
        }

        private static void DecrementWithoutGoingNegative(ref int value)
        {
            while (true)
            {
                int observed = Volatile.Read(ref value);
                if (observed <= 0)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref value, observed - 1, observed) == observed)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// One-shot ownership handle for a successful telemetry admission. Completing or rolling back
    /// twice is harmless, which prevents an exception path from decrementing a counter below zero.
    /// </summary>
    internal sealed class ErrorReportAdmission
    {
        private readonly ErrorReportSessionState owner;
        private readonly string fingerprint;
        private int released;

        internal ErrorReportAdmission(ErrorReportSessionState owner, string fingerprint)
        {
            this.owner = owner;
            this.fingerprint = fingerprint;
        }

        /// <summary>Marks a scheduled send finished and releases its in-flight slot exactly once.</summary>
        public void Complete()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                owner.Complete();
            }
        }

        /// <summary>Undo all claims when no send was successfully scheduled.</summary>
        public void RollBack()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                owner.RollBack(fingerprint);
            }
        }
    }
}

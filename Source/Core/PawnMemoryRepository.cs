// The memory-fragment store (design/MEMORY_SYSTEM_DESIGN.md §6), following the exact mold of
// DiaryEventRepository: a saved master list plus non-serialized indexes rebuilt on load. Every
// deposit funnels through Register (idempotent per pawn+source-event, so double emission and
// staged replays can never duplicate a fragment) and every per-pawn read through ForPawn, so
// lookups stay constant-time instead of linear-scanning the whole store on every recall.
//
// STATUS: constructed and driven by DiaryGameComponent memory appliers that do not exist yet —
// this class is deliberately unused until the capture/recall wiring lands. All Scribe keys it
// will use are additive, so old saves load with an empty store and no errors.
//
// New to C#/RimWorld? See AGENTS.md ("IExposable" / Scribe). Scribe_Collections.Look is
// RimWorld's save/load helper for collections; LookMode.Deep means "each element saves/loads
// itself via its own ExposeData". A `ref` parameter is required by Look so it can substitute a
// loaded list for the field — that is why the list lives here.
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// The saved store of every <see cref="MemoryFragment"/> across all pawns, plus the per-pawn
    /// and deposit-key indexes that mirror it. The indexes are never serialized — they are rebuilt
    /// from the master list after load (<see cref="RebuildIndex"/>) and kept in sync on add/remove.
    /// </summary>
    internal sealed class PawnMemoryRepository
    {
        // All memory fragments across every pawn, in insertion order. Persisted via ExposeMemories.
        private List<MemoryFragment> fragments = new List<MemoryFragment>();

        // NOT saved — rebuilt in RebuildIndex(). Ordinal-ignore-case so lookups agree with the
        // rest of the save layer (pawn ids / event ids are machine-generated but compared
        // defensively). The deposit key is "pawnId|sourceEventId": one event deposits at most one
        // fragment per pawn, which is what makes replayed signals idempotent.
        private readonly Dictionary<string, List<MemoryFragment>> fragmentsByPawnId
            = new Dictionary<string, List<MemoryFragment>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> depositKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The number of stored fragments.</summary>
        public int Count
        {
            get { return fragments.Count; }
        }

        /// <summary>
        /// Read-only interface over one pawn's LIVE repository-owned rows in insertion order.
        /// Future recall wiring must copy each row through MemoryFragment.ToSnapshot before calling
        /// pure code, then use the original rows only to apply lastRecalledTick/recallCount on the
        /// main thread. Do not retain this view across Register/Remove calls. Unknown or blank pawn
        /// ids yield a shared empty list — never null — so callers need no guard.
        /// </summary>
        public IReadOnlyList<MemoryFragment> ForPawn(string pawnId)
        {
            EnsureIndexReady();
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return EmptyList.List;
            }

            List<MemoryFragment> owned;
            return fragmentsByPawnId.TryGetValue(pawnId, out owned) && owned != null
                ? owned
                : (IReadOnlyList<MemoryFragment>)EmptyList.List;
        }

        /// <summary>Every pawn id that owns at least one fragment (eviction/dead-owner scans).</summary>
        public List<string> OwnerPawnIds()
        {
            EnsureIndexReady();
            return new List<string>(fragmentsByPawnId.Keys);
        }

        /// <summary>True when this pawn already deposited a fragment from this event.</summary>
        public bool HasDeposit(string pawnId, string sourceEventId)
        {
            EnsureIndexReady();
            if (string.IsNullOrWhiteSpace(pawnId) || string.IsNullOrWhiteSpace(sourceEventId))
            {
                return false;
            }

            return depositKeys.Contains(DepositKey(pawnId, sourceEventId));
        }

        /// <summary>
        /// Adds a freshly extracted fragment to the master list and both indexes. No-op on null,
        /// on blank memoryId/pawnId (RebuildIndex would drop them anyway), and on a duplicate
        /// deposit key — first deposit wins, matching DiaryEventRepository.Register's id rule.
        /// </summary>
        public void Register(MemoryFragment fragment)
        {
            if (fragment == null || string.IsNullOrWhiteSpace(fragment.memoryId)
                || string.IsNullOrWhiteSpace(fragment.pawnId))
            {
                return;
            }

            // Normally DiaryGameComponent calls RebuildIndex during PostLoadInit and the deposit
            // seam calls HasDeposit first. Register still owns the final idempotency guarantee, so
            // make the lazy defensive path ready BEFORE probing depositKeys. Otherwise the first
            // replay registered against a just-loaded repository could slip through once.
            EnsureIndexReady();

            if (!string.IsNullOrWhiteSpace(fragment.sourceEventId)
                && depositKeys.Contains(DepositKey(fragment.pawnId, fragment.sourceEventId)))
            {
                return;
            }

            fragments.Add(fragment);
            List<MemoryFragment> owned;
            if (!fragmentsByPawnId.TryGetValue(fragment.pawnId, out owned) || owned == null)
            {
                owned = new List<MemoryFragment>();
                fragmentsByPawnId[fragment.pawnId] = owned;
            }

            owned.Add(fragment);
            if (!string.IsNullOrWhiteSpace(fragment.sourceEventId))
            {
                depositKeys.Add(DepositKey(fragment.pawnId, fragment.sourceEventId));
            }
        }

        /// <summary>
        /// Removes every fragment whose id is in <paramref name="memoryIds"/>; returns the number
        /// removed. Used by the eviction applier. No-op on null/empty input.
        /// </summary>
        public int RemoveByIds(HashSet<string> memoryIds)
        {
            if (memoryIds == null || memoryIds.Count == 0)
            {
                return 0;
            }

            int before = fragments.Count;
            fragments.RemoveAll(f => f != null && !string.IsNullOrWhiteSpace(f.memoryId)
                && memoryIds.Contains(f.memoryId));
            int removed = before - fragments.Count;
            if (removed > 0)
            {
                RebuildIndex();
            }

            return removed;
        }

        /// <summary>
        /// Removes one pawn's entire store (dead-owner cleanup after the grace period); returns
        /// the number removed. Fragments OTHER pawns hold about this pawn are untouched — the
        /// dead resurfacing in survivors' entries is a feature (design §10.2).
        /// </summary>
        public int RemoveOwner(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return 0;
            }

            int before = fragments.Count;
            fragments.RemoveAll(f => f != null
                && string.Equals(f.pawnId, pawnId, StringComparison.OrdinalIgnoreCase));
            int removed = before - fragments.Count;
            if (removed > 0)
            {
                RebuildIndex();
            }

            return removed;
        }

        /// <summary>
        /// Rebuilds both indexes from the master list. Called once after a save loads (the
        /// indexes are never serialized). Malformed rows — null, blank memoryId, blank pawnId —
        /// are dropped from the master list itself so a corrupt save self-repairs on load.
        /// </summary>
        public void RebuildIndex()
        {
            fragments.RemoveAll(f => f == null || string.IsNullOrWhiteSpace(f.memoryId)
                || string.IsNullOrWhiteSpace(f.pawnId));
            fragmentsByPawnId.Clear();
            depositKeys.Clear();
            for (int i = 0; i < fragments.Count; i++)
            {
                MemoryFragment fragment = fragments[i];
                List<MemoryFragment> owned;
                if (!fragmentsByPawnId.TryGetValue(fragment.pawnId, out owned) || owned == null)
                {
                    owned = new List<MemoryFragment>();
                    fragmentsByPawnId[fragment.pawnId] = owned;
                }

                owned.Add(fragment);
                if (!string.IsNullOrWhiteSpace(fragment.sourceEventId))
                {
                    depositKeys.Add(DepositKey(fragment.pawnId, fragment.sourceEventId));
                }
            }
        }

        /// <summary>
        /// Defensively rebuilds the indexes only if a caller needs them before the normal
        /// PostLoadInit rebuild has run. In the usual load path this is a no-op; it only does work
        /// when the per-pawn index is empty but the master list is not.
        /// </summary>
        public void EnsureIndexReady()
        {
            if (fragmentsByPawnId.Count == 0 && fragments.Count > 0)
            {
                RebuildIndex();
            }
        }

        /// <summary>
        /// Serializes the master fragment list under the given Scribe key. The indexes are never
        /// saved. On PostLoadInit a missing/null list (old or corrupt save) is restored to empty
        /// so the rest of the load path sees a non-null store.
        /// </summary>
        /// <param name="label">The Scribe key; additive — "pawnMemoryFragments" in new saves.</param>
        public void ExposeMemories(string label)
        {
            Scribe_Collections.Look(ref fragments, label, LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && fragments == null)
            {
                fragments = new List<MemoryFragment>();
            }
        }

        private static string DepositKey(string pawnId, string sourceEventId)
        {
            return pawnId + "|" + sourceEventId;
        }

        private static class EmptyList
        {
            public static readonly IReadOnlyList<MemoryFragment> List = new MemoryFragment[0];
        }
    }
}

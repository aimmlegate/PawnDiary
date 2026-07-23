// Main-thread transient ownership for recursive psychic-bond calls and interrupted deathrest.
// Exact nested generic signals wait for the richer owner; verified ownership consumes them, while
// exceptions or failed verification release them unchanged in capture order.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    internal sealed class BiotechBondCallState
    {
        internal BiotechBondCorrelationScope scope;
        internal bool isRoot;
        internal Pawn firstPawn;
        internal Pawn secondPawn;
        internal PsychicBondMutationSnapshot snapshot;
    }

    internal sealed class BiotechBondCorrelationScope
    {
        internal string pairKey = string.Empty;
        internal string phase = string.Empty;
        internal int epoch;
        internal bool committed;
        internal readonly List<DiarySignal> stagedSignals = new List<DiarySignal>();
    }

    internal sealed class BiotechDeathrestCallState
    {
        internal Pawn pawn;
        internal DeathrestMutationSnapshot snapshot;
        internal BiotechDeathrestCorrelationScope scope;
    }

    internal sealed class BiotechDeathrestCorrelationScope
    {
        internal string pawnId = string.Empty;
        internal bool committed;
        internal readonly List<DiarySignal> stagedSignals = new List<DiarySignal>();
    }

    /// <summary>Tracks exact gene-removal nesting so rupture causes are never guessed.</summary>
    internal static class BiotechPsychicBondGeneRemovalScope
    {
        private static readonly Dictionary<string, int> Depth =
            new Dictionary<string, int>(StringComparer.Ordinal);

        internal static string Begin(Pawn pawn)
        {
            string pawnId = pawn?.GetUniqueLoadID() ?? string.Empty;
            if (pawnId.Length == 0) return string.Empty;
            int depth;
            Depth.TryGetValue(pawnId, out depth);
            Depth[pawnId] = depth + 1;
            return pawnId;
        }

        internal static void End(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId)) return;
            int depth;
            if (!Depth.TryGetValue(pawnId, out depth)) return;
            if (depth <= 1) Depth.Remove(pawnId);
            else Depth[pawnId] = depth - 1;
        }

        internal static bool Owns(PsychicBondPair pair)
        {
            return pair != null
                && (Depth.ContainsKey(pair.firstPawnId) || Depth.ContainsKey(pair.secondPawnId));
        }

        internal static void Clear()
        {
            Depth.Clear();
        }
    }

    /// <summary>Coordinates recursive pair ownership plus exact thought/hediff/mental companions.</summary>
    internal static class BiotechPsychicBondCorrelation
    {
        private static readonly List<BiotechBondCorrelationScope> Scopes =
            new List<BiotechBondCorrelationScope>();

        internal static BiotechBondCallState Begin(
            Pawn first,
            Pawn second,
            PsychicBondMutationSnapshot snapshot)
        {
            PsychicBondPair pair = snapshot?.Pair;
            if (!ModsConfig.BiotechActive || pair == null) return null;
            for (int i = Scopes.Count - 1; i >= 0; i--)
            {
                BiotechBondCorrelationScope active = Scopes[i];
                if (PsychicBondLifecyclePolicy.IsRecursiveSecondary(
                    pair,
                    snapshot.phase,
                    active.pairKey,
                    active.phase))
                {
                    return new BiotechBondCallState
                    {
                        scope = active,
                        isRoot = false,
                        firstPawn = first,
                        secondPawn = second,
                        snapshot = snapshot
                    };
                }
            }

            BiotechBondCorrelationScope root = new BiotechBondCorrelationScope
            {
                pairKey = pair.Key,
                phase = snapshot.phase,
                epoch = snapshot.bondEpoch
            };
            Scopes.Add(root);
            return new BiotechBondCallState
            {
                scope = root,
                isRoot = true,
                firstPawn = first,
                secondPawn = second,
                snapshot = snapshot
            };
        }

        internal static void Commit(BiotechBondCallState state, int tick, int expiryTicks)
        {
            if (state?.scope == null || !state.isRoot) return;
            state.scope.committed = true;
            state.scope.stagedSignals.Clear();
        }

        internal static void Close(BiotechBondCallState state)
        {
            if (state?.scope == null || !state.isRoot) return;
            Scopes.Remove(state.scope);
            if (!state.scope.committed) Release(state.scope.stagedSignals);
        }

        internal static bool TryStageThought(
            Pawn pawn,
            Thought_Memory thought,
            ThoughtSignal signal,
            int tick,
            int expiryTicks)
        {
            if (pawn == null || thought?.def?.defName != "PsychicBondTorn" || signal == null)
                return false;
            Pawn other = thought.otherPawn;
            return TryStageExact(
                pawn,
                other,
                PsychicBondPhaseTokens.Ruptured,
                signal,
                tick,
                expiryTicks);
        }

        internal static bool TryStageHediff(
            Pawn pawn,
            Hediff hediff,
            HediffSignal signal,
            int tick,
            int expiryTicks)
        {
            HediffWithTarget targeted = hediff as HediffWithTarget;
            Pawn other = targeted?.target as Pawn;
            string sourceDefName = hediff?.def?.defName ?? string.Empty;
            string phase = PsychicBondLifecyclePolicy.OwnsNestedSignalDef(
                PsychicBondPhaseTokens.Formed,
                sourceDefName)
                    ? PsychicBondPhaseTokens.Formed
                    : PsychicBondLifecyclePolicy.OwnsNestedSignalDef(
                        PsychicBondPhaseTokens.Ruptured,
                        sourceDefName)
                        ? PsychicBondPhaseTokens.Ruptured
                        : string.Empty;
            return phase.Length > 0
                && TryStageExact(pawn, other, phase, signal, tick, expiryTicks);
        }

        internal static bool TryStageMentalState(
            Pawn pawn,
            MentalStateSignal signal,
            int tick,
            int expiryTicks)
        {
            if (pawn == null || signal == null) return false;
            string pawnId = pawn.GetUniqueLoadID();
            for (int i = Scopes.Count - 1; i >= 0; i--)
            {
                BiotechBondCorrelationScope scope = Scopes[i];
                if (scope.phase == PsychicBondPhaseTokens.Ruptured
                    && PairContains(scope.pairKey, pawnId))
                {
                    PreserveAndAdd(scope.stagedSignals, signal, tick);
                    return true;
                }
            }
            return false;
        }

        internal static void FlushPending()
        {
            for (int i = 0; i < Scopes.Count; i++) Release(Scopes[i].stagedSignals);
            Scopes.Clear();
        }

        internal static void Clear()
        {
            Scopes.Clear();
            BiotechPsychicBondGeneRemovalScope.Clear();
        }

        private static bool TryStageExact(
            Pawn pawn,
            Pawn other,
            string phase,
            DiarySignal signal,
            int tick,
            int expiryTicks)
        {
            PsychicBondPair pair = PsychicBondPairPolicy.Create(
                pawn?.GetUniqueLoadID(),
                other?.GetUniqueLoadID());
            if (pair == null) return false;
            for (int i = Scopes.Count - 1; i >= 0; i--)
            {
                BiotechBondCorrelationScope scope = Scopes[i];
                if (scope.phase == phase && scope.pairKey == pair.Key)
                {
                    PreserveAndAdd(scope.stagedSignals, signal, tick);
                    return true;
                }
            }
            return false;
        }

        private static bool PairContains(string pairKey, string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pairKey) || string.IsNullOrWhiteSpace(pawnId)) return false;
            string[] ids = pairKey.Split('|');
            return ids.Length == 2 && (ids[0] == pawnId || ids[1] == pawnId);
        }

        private static void PreserveAndAdd(List<DiarySignal> destination, DiarySignal signal, int tick)
        {
            if (signal == null) return;
            signal.PreserveHistoricalOrdering(signal.Payload?.Tick ?? Math.Max(0, tick));
            destination.Add(signal);
        }

        private static void Release(List<DiarySignal> staged)
        {
            if (staged == null) return;
            for (int i = 0; i < staged.Count; i++)
            {
                DiarySignal signal = staged[i];
                DiaryPatchSafety.Run(
                    "BiotechPsychicBondCorrelation.Release",
                    () => DiaryEvents.Submit(signal));
            }
            staged.Clear();
        }

    }

    /// <summary>Coordinates one Wake owner and its exact nested InterruptedDeathrest hediff signal.</summary>
    internal static class BiotechDeathrestCorrelation
    {
        private static readonly List<BiotechDeathrestCorrelationScope> Scopes =
            new List<BiotechDeathrestCorrelationScope>();

        internal static BiotechDeathrestCallState Begin(Pawn pawn, DeathrestMutationSnapshot snapshot)
        {
            if (!ModsConfig.BiotechActive || pawn == null || snapshot == null) return null;
            BiotechDeathrestCorrelationScope scope = new BiotechDeathrestCorrelationScope
            {
                pawnId = snapshot.pawnId
            };
            Scopes.Add(scope);
            return new BiotechDeathrestCallState { pawn = pawn, snapshot = snapshot, scope = scope };
        }

        internal static bool TryStageHediff(
            Pawn pawn,
            Hediff hediff,
            HediffSignal signal,
            int tick)
        {
            if (pawn == null || hediff?.def?.defName != "InterruptedDeathrest" || signal == null)
                return false;
            string pawnId = pawn.GetUniqueLoadID();
            for (int i = Scopes.Count - 1; i >= 0; i--)
            {
                if (Scopes[i].pawnId != pawnId) continue;
                signal.PreserveHistoricalOrdering(signal.Payload?.Tick ?? Math.Max(0, tick));
                Scopes[i].stagedSignals.Add(signal);
                return true;
            }
            return false;
        }

        internal static void Commit(
            BiotechDeathrestCallState state,
            int tick,
            int expiryTicks)
        {
            if (state?.scope == null) return;
            state.scope.committed = true;
            state.scope.stagedSignals.Clear();
        }

        internal static void Close(BiotechDeathrestCallState state)
        {
            if (state?.scope == null) return;
            Scopes.Remove(state.scope);
            if (!state.scope.committed)
            {
                for (int i = 0; i < state.scope.stagedSignals.Count; i++)
                {
                    DiarySignal signal = state.scope.stagedSignals[i];
                    DiaryPatchSafety.Run(
                        "BiotechDeathrestCorrelation.Release",
                        () => DiaryEvents.Submit(signal));
                }
            }
            state.scope.stagedSignals.Clear();
        }

        internal static void FlushPending()
        {
            for (int i = Scopes.Count - 1; i >= 0; i--)
                Close(new BiotechDeathrestCallState { scope = Scopes[i] });
        }

        internal static void Clear()
        {
            Scopes.Clear();
        }
    }
}

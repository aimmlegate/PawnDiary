// Synchronous reentrancy-safe aggregation for CompHoldingPlatformTarget.Escape(bool). Every hooked
// call gets its own frame; initiating calls own a scope, nested non-initiating calls append to the
// active scope, and postfix/finalizer close the exact frame. No live object survives scope closure.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using Verse;

namespace PawnDiary
{
    /// <summary>Per-call Harmony state used to close exactly the frame its prefix opened.</summary>
    internal sealed class ContainmentEscapeCallState
    {
        internal ContainmentEscapeScope scope;
        internal AnomalyContainmentEscapeCapture capture;
        internal ContainedEntityFact scopeEntityFact;
        internal bool ownsScope;
        internal bool completed;
    }

    /// <summary>Closed outer-call data handed synchronously to pure policy and signal emission.</summary>
    internal sealed class ContainmentEscapeCompletion
    {
        internal ContainmentEscapeFacts facts;
        internal AnomalyPolicySnapshot policy;
        internal Dictionary<string, Pawn> writerPawns;
        internal Dictionary<string, Pawn> escapedPawns;

        /// <summary>Resolves selected detached writer IDs without scanning changed live game state.</summary>
        internal List<Pawn> ResolveWriters(ContainmentBreachPlan plan)
        {
            List<Pawn> result = new List<Pawn>();
            if (plan?.selectedWriters == null || writerPawns == null) return result;
            for (int i = 0; i < plan.selectedWriters.Count; i++)
            {
                string id = plan.selectedWriters[i]?.pawnId ?? string.Empty;
                Pawn pawn;
                if (writerPawns.TryGetValue(id, out pawn) && pawn != null) result.Add(pawn);
            }
            return result;
        }

        /// <summary>Returns the first bounded verified escaped pawn for the solo event subject.</summary>
        internal Pawn FirstEscapedPawn(ContainmentBreachPlan plan)
        {
            if (plan?.contextEntities == null || escapedPawns == null) return null;
            for (int i = 0; i < plan.contextEntities.Count; i++)
            {
                string id = plan.contextEntities[i]?.entityId ?? string.Empty;
                Pawn pawn;
                if (escapedPawns.TryGetValue(id, out pawn) && pawn != null) return pawn;
            }
            return null;
        }
    }

    /// <summary>One active outer Escape(true) transaction and its nested Escape(false) calls.</summary>
    internal sealed class ContainmentEscapeScope
    {
        internal readonly ContainmentEscapeFacts facts = new ContainmentEscapeFacts();
        internal readonly AnomalyPolicySnapshot policy;
        internal readonly Dictionary<string, ContainedEntityFact> entities =
            new Dictionary<string, ContainedEntityFact>(StringComparer.Ordinal);
        internal readonly Dictionary<string, AnomalyWriterCandidate> candidates =
            new Dictionary<string, AnomalyWriterCandidate>(StringComparer.Ordinal);
        internal readonly Dictionary<string, Pawn> writerPawns =
            new Dictionary<string, Pawn>(StringComparer.Ordinal);
        internal readonly Dictionary<string, Pawn> escapedPawns =
            new Dictionary<string, Pawn>(StringComparer.Ordinal);

        internal ContainmentEscapeScope(AnomalyPolicySnapshot policy)
        {
            this.policy = policy ?? AnomalyPolicySnapshot.CreateDefault();
        }
    }

    /// <summary>Owns the process-static call/scope stacks and guarantees bounded lifecycle cleanup.</summary>
    internal static class ContainmentEscapeScopeStack
    {
        private static readonly List<ContainmentEscapeCallState> Calls =
            new List<ContainmentEscapeCallState>();
        private static readonly List<ContainmentEscapeScope> Scopes =
            new List<ContainmentEscapeScope>();
        private static Func<AnomalyWriterCandidate, bool> candidateFilterForTests;

        /// <summary>Opens one outer scope or appends one nested call to the active scope.</summary>
        internal static ContainmentEscapeCallState Begin(object targetObject, bool initiator)
        {
            ContainmentEscapeScope scope = null;
            AnomalyPolicySnapshot policy;
            if (initiator)
            {
                policy = DiaryAnomalyPolicy.Snapshot();
            }
            else
            {
                if (Scopes.Count == 0) return null;
                scope = Scopes[Scopes.Count - 1];
                policy = scope.policy;
            }

            AnomalyContainmentEscapeCapture capture;
            if (!DlcContext.TryCaptureAnomalyContainmentBefore(
                    targetObject,
                    policy.recentStudierMaxAgeTicks,
                    out capture))
            {
                return null;
            }

            if (initiator)
            {
                scope = new ContainmentEscapeScope(policy);
                scope.facts.escapeId = capture.entityFact.entityId;
                scope.facts.tick = capture.capturedTick;
                scope.facts.mapId = capture.entityFact.mapId;
                scope.facts.outerEscape = true;
                scope.facts.preEjectionSetting = capture.preEjectionSetting ?? string.Empty;
                Scopes.Add(scope);
            }
            else if (capture.entityFact.mapId != scope.facts.mapId)
            {
                // A modded cross-map reentrant call is not part of vanilla's same-room cascade.
                return null;
            }

            ContainmentEscapeCallState state = new ContainmentEscapeCallState
            {
                scope = scope,
                capture = capture,
                ownsScope = initiator
            };
            try
            {
                Merge(scope, capture, state);
                Calls.Add(state);
                return state;
            }
            catch
            {
                if (initiator) Scopes.Remove(scope);
                throw;
            }
        }

        /// <summary>
        /// Verifies one call, closes its exact frame, and returns data only for a healthy outer close.
        /// Nested calls always return null.
        /// </summary>
        internal static ContainmentEscapeCompletion Complete(ContainmentEscapeCallState state)
        {
            if (state == null || state.completed) return null;
            bool verified = DlcContext.VerifyAnomalyContainmentEscape(state.capture);
            if (verified && state.scopeEntityFact != null)
            {
                state.scopeEntityFact.escaped = true;
                state.scope.escapedPawns[state.scopeEntityFact.entityId] = state.capture.entity;
                if (!state.ownsScope) state.scope.facts.sameRoomCascade = true;
            }

            bool healthyOuterClose = Close(state);
            if (!state.ownsScope || !healthyOuterClose) return null;
            return new ContainmentEscapeCompletion
            {
                facts = state.scope.facts,
                policy = state.scope.policy,
                writerPawns = new Dictionary<string, Pawn>(
                    state.scope.writerPawns, StringComparer.Ordinal),
                escapedPawns = new Dictionary<string, Pawn>(
                    state.scope.escapedPawns, StringComparer.Ordinal)
            };
        }

        /// <summary>Removes an unfinished frame/scope when vanilla or another patch throws.</summary>
        internal static void Abort(ContainmentEscapeCallState state)
        {
            if (state == null || state.completed) return;
            Close(state);
        }

        /// <summary>Clears all live references at every game/new-game/load boundary.</summary>
        internal static void Clear()
        {
            for (int i = 0; i < Calls.Count; i++) Calls[i].completed = true;
            Calls.Clear();
            Scopes.Clear();
            candidateFilterForTests = null;
        }

        internal static int ActiveCallDepthForTests => Calls.Count;
        internal static int ActiveScopeDepthForTests => Scopes.Count;

        /// <summary>Test-only detached evidence filter; null restores the production candidate set.</summary>
        internal static void SetCandidateFilterForTests(
            Func<AnomalyWriterCandidate, bool> filter)
        {
            candidateFilterForTests = filter;
        }

        private static void Merge(
            ContainmentEscapeScope scope,
            AnomalyContainmentEscapeCapture capture,
            ContainmentEscapeCallState state)
        {
            string entityId = capture.entityFact.entityId;
            ContainedEntityFact entity;
            if (!scope.entities.TryGetValue(entityId, out entity)
                && scope.entities.Count < AnomalyPolicyLimits.MaximumContainmentEntities)
            {
                entity = capture.entityFact;
                scope.entities.Add(entityId, entity);
                scope.facts.entities.Add(entity);
            }
            state.scopeEntityFact = entity;

            for (int i = 0; i < capture.writerFacts.Count; i++)
            {
                AnomalyWriterCandidate incoming = capture.writerFacts[i];
                if (incoming == null
                    || (candidateFilterForTests != null && !candidateFilterForTests(incoming)))
                {
                    continue;
                }

                string pawnId = (incoming.pawnId ?? string.Empty).Trim();
                if (pawnId.Length == 0) continue;
                AnomalyWriterCandidate existing;
                if (scope.candidates.TryGetValue(pawnId, out existing))
                {
                    existing.eligible |= incoming.eligible;
                    existing.onAffectedMap |= incoming.onAffectedMap;
                    existing.nearbyCandidate |= incoming.nearbyCandidate;
                    existing.recentExactStudier |= incoming.recentExactStudier;
                    existing.freeColonistOnHomeMap |= incoming.freeColonistOnHomeMap;
                    if (existing.distanceSquared < 0
                        || (incoming.distanceSquared >= 0
                            && incoming.distanceSquared < existing.distanceSquared))
                    {
                        existing.distanceSquared = incoming.distanceSquared;
                        existing.distanceBucket = incoming.distanceBucket;
                    }
                }
                else if (scope.candidates.Count < AnomalyPolicyLimits.MaximumContainmentCandidates)
                {
                    existing = incoming;
                    scope.candidates.Add(pawnId, existing);
                    scope.facts.witnesses.Add(existing);
                }

                if (existing != null && i < capture.writerPawns.Count)
                    scope.writerPawns[pawnId] = capture.writerPawns[i];
            }
        }

        private static bool Close(ContainmentEscapeCallState state)
        {
            bool wasTop = Calls.Count > 0 && ReferenceEquals(Calls[Calls.Count - 1], state);
            int index = Calls.LastIndexOf(state);
            if (index >= 0) Calls.RemoveAt(index);
            state.completed = true;

            if (!state.ownsScope) return wasTop;
            bool hadDescendants = false;
            for (int i = Calls.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(Calls[i].scope, state.scope)) continue;
                Calls[i].completed = true;
                Calls.RemoveAt(i);
                hadDescendants = true;
            }
            Scopes.Remove(state.scope);
            return wasTop && !hadDescendants;
        }
    }
}

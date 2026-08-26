// Pure arbitration for future cross-DLC reflections. N0 intentionally creates no event and mutates no
// save state: the later N4 runtime scheduler will dispatch the selected opportunity, then consume it
// only after dispatch succeeds.
//
// New to C#/RimWorld? See AGENTS.md ("persistence & ticking").
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>
    /// Chooses at most one eligible normal/meaningful/quiet/summary opportunity. Fixed class order is
    /// enforced here; XML-copied priorities operate only inside a class.
    /// </summary>
    internal static class ReflectionCoordinator
    {
        private sealed class RankedOpportunity
        {
            public ReflectionOpportunity opportunity;
            public int workClassRank;
            public int priority;
            public int salience;
            public long changeTick;
            public string opportunityKey = string.Empty;
        }

        /// <summary>
        /// Reports whether the shared natural-rest pass has anything to arbitrate. This is a pure wake
        /// check only: it does not repair, expire, consume, or otherwise inspect an opportunity row.
        /// </summary>
        public static bool HasPendingCoordinatorWork(ReflectionCoordinatorWakeRequest request)
        {
            if (request == null)
            {
                return false;
            }

            if (request.hasNormalReflectionSource
                || request.hasPendingMajorReflection
                || request.pendingAmbientInteractionCount > 0
                || request.pendingAmbientThoughtCount > 0
                || request.pendingDayHediffCount > 0)
            {
                return true;
            }

            // Summary wording is allowed to wake this pass even when the ordinary reflection policy is
            // Off, but only after the impure adapter has applied every effective memory/request gate.
            return request.optionalMemoryRequestsEffective
                && (request.pendingSummaryWordingCount > 0
                    || request.hasOptionalMemoryCandidateSource);
        }

        /// <summary>
        /// Produces one deferred dispatch/consumption plan. Failed dispatch must leave the plan's state
        /// unconsumed; the impure N4 scheduler owns that final acknowledgement.
        /// </summary>
        public static ReflectionPlan Plan(ReflectionPlanningRequest request)
        {
            ReflectionPlan result = new ReflectionPlan();
            if (request == null)
            {
                return result;
            }

            NarrativePolicySnapshot policy = request.policy ?? NarrativePolicySnapshot.CreateDefault();
            List<RankedOpportunity> eligible = new List<RankedOpportunity>();
            List<ReflectionOpportunity> opportunities = request.opportunities ?? new List<ReflectionOpportunity>();
            for (int i = 0; i < opportunities.Count; i++)
            {
                ReflectionOpportunity opportunity = opportunities[i];
                string rejection;
                if (!IsEligible(opportunity, request, policy, out rejection))
                {
                    AddDiagnostic(result, opportunity, rejection);
                    if (ShouldAdvanceDisabledDebt(opportunity, policy))
                    {
                        result.stateInstructions.Add(DisabledGroupInstruction(opportunity));
                    }
                    continue;
                }

                eligible.Add(Rank(opportunity, policy));
            }

            eligible.Sort(CompareRanked);
            if (eligible.Count == 0)
            {
                return result;
            }

            ReflectionOpportunity selected = eligible[0].opportunity;
            result.selectedOpportunity = selected;
            result.consumption = new ReflectionStateConsumption
            {
                kind = selected.kind ?? string.Empty,
                workClass = selected.workClass ?? string.Empty,
                opportunityKey = StableOpportunityKey(selected),
                sourceEventIds = Copy(selected.sourceEventIds),
                arcKeys = Copy(selected.arcKeys),
                consumeAfterSuccessfulDispatch = true,
                // Disabled groups are rejected above and get a separate non-dispatch instruction.
                advanceDebtWhenGroupDisabled = false,
                producesPage = selected.workClass != ReflectionWorkClassTokens.SummaryWording,
                advancesNarrativeCooldown =
                    selected.workClass != ReflectionWorkClassTokens.SummaryWording,
                consumesQuietQuadrumOnActivation =
                    selected.workClass == ReflectionWorkClassTokens.QuietMemory
            };
            result.stateInstructions.Add(result.consumption);
            result.diagnostics.Add(new NarrativeCandidateDiagnostic
            {
                candidateKey = selected.kind ?? string.Empty,
                selected = true,
                reason = NarrativeDiagnosticTokens.Selected,
                score = ReflectionWorkClassTokens.Rank(selected.workClass)
            });
            return result;
        }

        /// <summary>
        /// Confirms whether the impure scheduler may acknowledge the selected state instruction. Keeping
        /// this check pure makes the dispatch-failure invariant explicit and independently testable.
        /// </summary>
        public static bool CanConsumeAfterDispatch(ReflectionPlan plan, bool dispatchSucceeded)
        {
            return SettleAfterActivation(plan, dispatchSucceeded, false).coordinatorSlotSettled;
        }

        /// <summary>
        /// Separates committed coordinator activation from visible page registration. A summary can
        /// settle successfully while both page/cooldown outputs remain false.
        /// </summary>
        public static ReflectionSettlementOutcome SettleAfterActivation(
            ReflectionPlan plan,
            bool committedActivation,
            bool pageRegistered)
        {
            ReflectionSettlementOutcome outcome = new ReflectionSettlementOutcome();
            ReflectionStateConsumption consumption = plan?.consumption;
            if (!committedActivation
                || plan?.selectedOpportunity == null
                || consumption == null
                || !consumption.consumeAfterSuccessfulDispatch)
            {
                return outcome;
            }

            outcome.coordinatorSlotSettled = true;
            outcome.pageRegistered = consumption.producesPage && pageRegistered;
            outcome.advanceNarrativeCooldown = consumption.advancesNarrativeCooldown;
            outcome.consumeQuietQuadrum = consumption.consumesQuietQuadrumOnActivation;
            return outcome;
        }

        /// <summary>Reports the global reflection cooldown using only detached tick/policy data.</summary>
        public static bool IsGlobalCooldownActive(
            int currentTick,
            int lastReflectionTick,
            NarrativePolicySnapshot policy)
        {
            NarrativePolicySnapshot effective = policy ?? NarrativePolicySnapshot.CreateDefault();
            return lastReflectionTick >= 0 && currentTick >= lastReflectionTick
                && currentTick - lastReflectionTick
                    < Math.Max(0, effective.reflectionGlobalCooldownTicks);
        }

        /// <summary>Reports one kind's cooldown so the runtime adapter can skip expensive collection.</summary>
        public static bool IsKindCooldownActive(
            string kind,
            int currentTick,
            List<ReflectionHistoryEntry> history,
            NarrativePolicySnapshot policy)
        {
            NarrativePolicySnapshot effective = policy ?? NarrativePolicySnapshot.CreateDefault();
            int cooldown = CooldownFor(kind, effective);
            if (cooldown <= 0 || history == null)
            {
                return false;
            }

            for (int i = 0; i < history.Count; i++)
            {
                ReflectionHistoryEntry prior = history[i];
                if (prior != null && prior.writtenTick >= 0 && EqualsOrdinal(prior.kind, kind)
                    && currentTick >= prior.writtenTick
                    && currentTick - prior.writtenTick < cooldown)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsEligible(
            ReflectionOpportunity opportunity,
            ReflectionPlanningRequest request,
            NarrativePolicySnapshot policy,
            out string rejection)
        {
            rejection = string.Empty;
            if (!IsKnownOpportunity(opportunity))
            {
                rejection = NarrativeDiagnosticTokens.CoordinatorOpportunityInvalid;
                return false;
            }

            bool normalReflection = opportunity.workClass == ReflectionWorkClassTokens.Normal
                && NarrativeReflectionKindTokens.IsKnown(opportunity.kind);
            bool optional = opportunity.workClass != ReflectionWorkClassTokens.Normal;
            if ((normalReflection
                    || opportunity.workClass == ReflectionWorkClassTokens.MeaningfulMemory
                    || opportunity.workClass == ReflectionWorkClassTokens.QuietMemory)
                && !policy.enabled)
            {
                rejection = NarrativeDiagnosticTokens.PolicyDisabled;
                return false;
            }

            if (optional && !AllowsOptionalMemoryWork(request))
            {
                rejection = NarrativeDiagnosticTokens.OptionalMemoryDisabled;
                return false;
            }

            if (opportunity.workClass == ReflectionWorkClassTokens.QuietMemory
                && !request.occasionalMemoryReflections)
            {
                rejection = NarrativeDiagnosticTokens.QuietMemoryDisabled;
                return false;
            }

            if (!opportunity.due || !opportunity.groupEnabled)
            {
                rejection = NarrativeDiagnosticTokens.ReflectionNotDue;
                return false;
            }

            if (optional)
            {
                if (!HasValidOptionalWindow(opportunity))
                {
                    rejection = NarrativeDiagnosticTokens.CoordinatorOpportunityInvalid;
                    return false;
                }
                if ((long)request.currentTick < opportunity.dueTick)
                {
                    rejection = NarrativeDiagnosticTokens.ReflectionNotDue;
                    return false;
                }
                if ((long)request.currentTick >= opportunity.expiryTick)
                {
                    rejection = NarrativeDiagnosticTokens.CoordinatorOpportunityExpired;
                    return false;
                }
            }

            if (opportunity.alreadyWritten)
            {
                rejection = NarrativeDiagnosticTokens.ReflectionAlreadyWritten;
                return false;
            }

            bool globalCooldown = opportunity.kind != CoordinatorOpportunityKindTokens.NormalAmbient
                && IsGlobalCooldownActive(
                    request.currentTick, request.lastReflectionTick, policy);
            bool kindCooldown = opportunity.workClass != ReflectionWorkClassTokens.SummaryWording
                && opportunity.kind != CoordinatorOpportunityKindTokens.NormalAmbient
                && IsKindCooldownActive(
                    opportunity.kind, request.currentTick, request.history, policy);
            if (!opportunity.cooldownSatisfied || globalCooldown || kindCooldown)
            {
                rejection = NarrativeDiagnosticTokens.ReflectionCooldown;
                return false;
            }

            // Ambient normal work already has an ordinary diary candidate, while summary wording
            // points at one exact committed Summary row. Neither adapter invents a reflection-memory
            // count merely to enter arbitration. Only page-producing reflection classes own these
            // memory selection/span gates.
            bool requiresReflectionMemories = normalReflection
                || opportunity.workClass == ReflectionWorkClassTokens.MeaningfulMemory
                || opportunity.workClass == ReflectionWorkClassTokens.QuietMemory;
            if (requiresReflectionMemories && opportunity.candidateMemoryCount <= 0)
            {
                rejection = NarrativeDiagnosticTokens.ReflectionNeedsMemories;
                return false;
            }

            if (requiresReflectionMemories
                && opportunity.memorySpanTicks > 0
                && policy.reflectionMaximumSpanTicks > 0
                && opportunity.memorySpanTicks > policy.reflectionMaximumSpanTicks)
            {
                rejection = NarrativeDiagnosticTokens.ReflectionSpanExceeded;
                return false;
            }

            if (opportunity.kind == NarrativeReflectionKindTokens.CrossArc
                && (opportunity.linkedMemoryCount < Math.Max(2, policy.reflectionMinimumLinkedMemories)
                    || !opportunity.hasCoherentLink
                    || !opportunity.hasPhaseChange
                    || (policy.reflectionRequireChangeOrConsequence
                        && !opportunity.hasChangeOrConsequence)))
            {
                rejection = NarrativeDiagnosticTokens.ReflectionNeedsLink;
                return false;
            }

            return true;
        }

        private static bool IsKnownOpportunity(ReflectionOpportunity opportunity)
        {
            if (opportunity == null || !ReflectionWorkClassTokens.IsKnown(opportunity.workClass))
            {
                return false;
            }

            if (opportunity.workClass == ReflectionWorkClassTokens.Normal)
            {
                return string.IsNullOrEmpty(opportunity.timing)
                    && (NarrativeReflectionKindTokens.IsKnown(opportunity.kind)
                        || opportunity.kind == CoordinatorOpportunityKindTokens.NormalAmbient);
            }
            if (opportunity.workClass == ReflectionWorkClassTokens.MeaningfulMemory)
            {
                return opportunity.kind == CoordinatorOpportunityKindTokens.MemoryReflection
                    && MemoryReflectionTimingTokens.IsMeaningful(opportunity.timing);
            }
            if (opportunity.workClass == ReflectionWorkClassTokens.QuietMemory)
            {
                return opportunity.kind == CoordinatorOpportunityKindTokens.MemoryReflection
                    && opportunity.timing == MemoryReflectionTimingTokens.Quiet;
            }
            return opportunity.kind == CoordinatorOpportunityKindTokens.SummaryWording
                && string.IsNullOrEmpty(opportunity.timing);
        }

        private static bool AllowsOptionalMemoryWork(ReflectionPlanningRequest request)
        {
            return request != null
                && request.useMemoriesInWriting
                && request.allowExtraMemoryAiRequests
                && request.optionalRequestInvalidationGeneration > 0
                && request.optionalRequestInvalidationGeneration < long.MaxValue;
        }

        private static bool HasValidOptionalWindow(ReflectionOpportunity opportunity)
        {
            if (opportunity == null || !opportunity.usesBoundedTiming
                || string.IsNullOrWhiteSpace(opportunity.opportunityKey)
                || opportunity.requestedTick < 0
                || opportunity.dueTick < opportunity.requestedTick
                || opportunity.expiryTick <= opportunity.dueTick)
            {
                return false;
            }

            if (opportunity.workClass == ReflectionWorkClassTokens.MeaningfulMemory)
            {
                return opportunity.timing == MemoryReflectionTimingTokens.Immediate
                    ? opportunity.dueTick == opportunity.requestedTick
                    : opportunity.dueTick > opportunity.requestedTick;
            }
            return true;
        }

        private static bool ShouldAdvanceDisabledDebt(
            ReflectionOpportunity opportunity,
            NarrativePolicySnapshot policy)
        {
            return opportunity != null
                && opportunity.workClass == ReflectionWorkClassTokens.Normal
                && NarrativeReflectionKindTokens.IsKnown(opportunity.kind)
                && (!opportunity.groupEnabled || policy?.enabled != true);
        }

        private static RankedOpportunity Rank(
            ReflectionOpportunity opportunity,
            NarrativePolicySnapshot policy)
        {
            bool normal = opportunity.workClass == ReflectionWorkClassTokens.Normal;
            int legacyPriority = normal ? PriorityFor(opportunity.kind, policy) : 0;
            return new RankedOpportunity
            {
                opportunity = opportunity,
                workClassRank = ReflectionWorkClassTokens.Rank(opportunity.workClass),
                priority = normal && legacyPriority != 0
                    ? legacyPriority : opportunity.configuredPriority,
                salience = normal ? SalienceRank(opportunity.importance) : opportunity.salience,
                changeTick = normal ? opportunity.nowTick : opportunity.requestedTick,
                opportunityKey = StableOpportunityKey(opportunity)
            };
        }

        private static int PriorityFor(string kind, NarrativePolicySnapshot policy)
        {
            if (policy.reflectionPriorities != null)
            {
                for (int i = 0; i < policy.reflectionPriorities.Count; i++)
                {
                    NarrativeReflectionPriority row = policy.reflectionPriorities[i];
                    if (row != null && EqualsOrdinal(row.kind, kind))
                    {
                        return row.priority;
                    }
                }
            }

            return 0;
        }

        private static int CooldownFor(string kind, NarrativePolicySnapshot policy)
        {
            if (policy.reflectionPriorities != null)
            {
                for (int i = 0; i < policy.reflectionPriorities.Count; i++)
                {
                    NarrativeReflectionPriority row = policy.reflectionPriorities[i];
                    if (row != null && EqualsOrdinal(row.kind, kind))
                    {
                        return Math.Max(0, row.cooldownTicks);
                    }
                }
            }

            return 0;
        }

        private static int CompareRanked(RankedOpportunity left, RankedOpportunity right)
        {
            int workClass = right.workClassRank.CompareTo(left.workClassRank);
            if (workClass != 0)
            {
                return workClass;
            }

            int priority = right.priority.CompareTo(left.priority);
            if (priority != 0)
            {
                return priority;
            }

            int salience = right.salience.CompareTo(left.salience);
            if (salience != 0)
            {
                return salience;
            }

            int tick = right.changeTick.CompareTo(left.changeTick);
            if (tick != 0) return tick;
            int key = string.Compare(
                left.opportunityKey, right.opportunityKey, StringComparison.Ordinal);
            if (key != 0) return key;
            int kind = string.Compare(
                left.opportunity.kind, right.opportunity.kind, StringComparison.Ordinal);
            return kind != 0
                ? kind
                : string.Compare(
                    left.opportunity.timing, right.opportunity.timing, StringComparison.Ordinal);
        }

        private static int SalienceRank(string salience)
        {
            if (salience == NarrativeSalienceTokens.Terminal) return 4;
            if (salience == NarrativeSalienceTokens.Major) return 3;
            if (salience == NarrativeSalienceTokens.Meaningful) return 2;
            return 1;
        }

        private static ReflectionStateConsumption DisabledGroupInstruction(ReflectionOpportunity opportunity)
        {
            return new ReflectionStateConsumption
            {
                kind = opportunity.kind ?? string.Empty,
                workClass = opportunity.workClass ?? string.Empty,
                opportunityKey = StableOpportunityKey(opportunity),
                sourceEventIds = Copy(opportunity.sourceEventIds),
                arcKeys = Copy(opportunity.arcKeys),
                consumeAfterSuccessfulDispatch = false,
                advanceDebtWhenGroupDisabled = true,
                producesPage = false,
                advancesNarrativeCooldown = false
            };
        }

        private static void AddDiagnostic(ReflectionPlan result, ReflectionOpportunity opportunity, string reason)
        {
            result.diagnostics.Add(new NarrativeCandidateDiagnostic
            {
                candidateKey = opportunity == null ? string.Empty : (opportunity.kind ?? string.Empty),
                selected = false,
                reason = reason ?? string.Empty
            });
        }

        private static List<string> Copy(List<string> source)
        {
            return source == null ? new List<string>() : new List<string>(source);
        }

        private static string StableOpportunityKey(ReflectionOpportunity opportunity)
        {
            if (!string.IsNullOrWhiteSpace(opportunity?.opportunityKey))
            {
                return opportunity.opportunityKey;
            }
            return opportunity?.kind ?? string.Empty;
        }

        private static bool EqualsOrdinal(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
                && string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
        }
    }
}

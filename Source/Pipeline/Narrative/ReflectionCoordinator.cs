// Pure arbitration for future cross-DLC reflections. N0 intentionally creates no event and mutates no
// save state: the later N4 runtime scheduler will dispatch the selected opportunity, then consume it
// only after dispatch succeeds.
//
// New to C#/RimWorld? See AGENTS.md ("persistence & ticking").
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Chooses at most one eligible reflection opportunity using XML-copied priorities.</summary>
    internal static class ReflectionCoordinator
    {
        private sealed class RankedOpportunity
        {
            public ReflectionOpportunity opportunity;
            public int priority;
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
            if (!policy.enabled)
            {
                AddPolicyDisabledDiagnostics(result, request.opportunities);
                return result;
            }

            if (InGlobalCooldown(request, policy))
            {
                AddGlobalCooldownDiagnostics(result, request.opportunities);
                return result;
            }

            List<RankedOpportunity> eligible = new List<RankedOpportunity>();
            List<ReflectionOpportunity> opportunities = request.opportunities ?? new List<ReflectionOpportunity>();
            for (int i = 0; i < opportunities.Count; i++)
            {
                ReflectionOpportunity opportunity = opportunities[i];
                string rejection;
                if (!IsEligible(opportunity, request, policy, out rejection))
                {
                    AddDiagnostic(result, opportunity, rejection);
                    if (opportunity != null && !opportunity.groupEnabled)
                    {
                        result.stateInstructions.Add(DisabledGroupInstruction(opportunity));
                    }
                    continue;
                }

                eligible.Add(new RankedOpportunity
                {
                    opportunity = opportunity,
                    priority = PriorityFor(opportunity.kind, policy)
                });
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
                sourceEventIds = Copy(selected.sourceEventIds),
                arcKeys = Copy(selected.arcKeys),
                consumeAfterSuccessfulDispatch = true,
                // Disabled groups are rejected above and get a separate non-dispatch instruction.
                advanceDebtWhenGroupDisabled = false
            };
            result.stateInstructions.Add(result.consumption);
            result.diagnostics.Add(new NarrativeCandidateDiagnostic
            {
                candidateKey = selected.kind ?? string.Empty,
                selected = true,
                reason = NarrativeDiagnosticTokens.Selected,
                score = PriorityFor(selected.kind, policy)
            });
            return result;
        }

        private static bool IsEligible(
            ReflectionOpportunity opportunity,
            ReflectionPlanningRequest request,
            NarrativePolicySnapshot policy,
            out string rejection)
        {
            rejection = string.Empty;
            if (opportunity == null || !NarrativeReflectionKindTokens.IsKnown(opportunity.kind))
            {
                rejection = NarrativeDiagnosticTokens.ReflectionNotDue;
                return false;
            }

            if (!opportunity.due || !opportunity.groupEnabled)
            {
                rejection = NarrativeDiagnosticTokens.ReflectionNotDue;
                return false;
            }

            if (opportunity.alreadyWritten)
            {
                rejection = NarrativeDiagnosticTokens.ReflectionAlreadyWritten;
                return false;
            }

            if (!opportunity.cooldownSatisfied || InKindCooldown(opportunity.kind, request, policy))
            {
                rejection = NarrativeDiagnosticTokens.ReflectionCooldown;
                return false;
            }

            if (opportunity.candidateMemoryCount <= 0)
            {
                rejection = NarrativeDiagnosticTokens.ReflectionNeedsMemories;
                return false;
            }

            if (opportunity.memorySpanTicks > 0 && policy.reflectionMaximumSpanTicks > 0
                && opportunity.memorySpanTicks > policy.reflectionMaximumSpanTicks)
            {
                rejection = NarrativeDiagnosticTokens.ReflectionSpanExceeded;
                return false;
            }

            if (opportunity.kind == NarrativeReflectionKindTokens.CrossArc
                && (opportunity.linkedMemoryCount < Math.Max(2, policy.reflectionMinimumLinkedMemories)
                    || !opportunity.hasCoherentLink || !opportunity.hasPhaseChange))
            {
                rejection = NarrativeDiagnosticTokens.ReflectionNeedsLink;
                return false;
            }

            return true;
        }

        private static bool InGlobalCooldown(ReflectionPlanningRequest request, NarrativePolicySnapshot policy)
        {
            return request.lastReflectionTick >= 0 && request.currentTick >= request.lastReflectionTick
                && request.currentTick - request.lastReflectionTick < Math.Max(0, policy.reflectionGlobalCooldownTicks);
        }

        private static bool InKindCooldown(
            string kind,
            ReflectionPlanningRequest request,
            NarrativePolicySnapshot policy)
        {
            int cooldown = CooldownFor(kind, policy);
            if (cooldown <= 0 || request.history == null)
            {
                return false;
            }

            for (int i = 0; i < request.history.Count; i++)
            {
                ReflectionHistoryEntry prior = request.history[i];
                if (prior != null && prior.writtenTick >= 0 && EqualsOrdinal(prior.kind, kind)
                    && request.currentTick >= prior.writtenTick
                    && request.currentTick - prior.writtenTick < cooldown)
                {
                    return true;
                }
            }

            return false;
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
            int priority = right.priority.CompareTo(left.priority);
            if (priority != 0)
            {
                return priority;
            }

            int importance = SalienceRank(right.opportunity.importance).CompareTo(SalienceRank(left.opportunity.importance));
            if (importance != 0)
            {
                return importance;
            }

            int tick = right.opportunity.nowTick.CompareTo(left.opportunity.nowTick);
            return tick != 0 ? tick : string.Compare(left.opportunity.kind, right.opportunity.kind,
                StringComparison.Ordinal);
        }

        private static int SalienceRank(string salience)
        {
            if (salience == NarrativeSalienceTokens.Terminal) return 4;
            if (salience == NarrativeSalienceTokens.Major) return 3;
            if (salience == NarrativeSalienceTokens.Meaningful) return 2;
            return 1;
        }

        private static void AddGlobalCooldownDiagnostics(
            ReflectionPlan result,
            List<ReflectionOpportunity> opportunities)
        {
            if (opportunities == null)
            {
                return;
            }

            for (int i = 0; i < opportunities.Count; i++)
            {
                AddDiagnostic(result, opportunities[i], NarrativeDiagnosticTokens.ReflectionCooldown);
            }
        }

        private static void AddPolicyDisabledDiagnostics(
            ReflectionPlan result,
            List<ReflectionOpportunity> opportunities)
        {
            if (opportunities == null)
            {
                return;
            }

            for (int i = 0; i < opportunities.Count; i++)
            {
                AddDiagnostic(result, opportunities[i], NarrativeDiagnosticTokens.PolicyDisabled);
            }
        }

        private static ReflectionStateConsumption DisabledGroupInstruction(ReflectionOpportunity opportunity)
        {
            return new ReflectionStateConsumption
            {
                kind = opportunity.kind ?? string.Empty,
                sourceEventIds = Copy(opportunity.sourceEventIds),
                arcKeys = Copy(opportunity.arcKeys),
                consumeAfterSuccessfulDispatch = false,
                advanceDebtWhenGroupDisabled = true
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

        private static bool EqualsOrdinal(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
                && string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
        }
    }
}

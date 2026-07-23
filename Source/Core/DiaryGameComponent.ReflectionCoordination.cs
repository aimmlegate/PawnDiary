// Runtime adapter for N4 reflection arbitration. It collects plain opportunities from the existing
// arc/quadrum/day sources, asks the assembly-free ReflectionCoordinator for one winner, dispatches
// only that source's existing signal, and acknowledges cadence state only after Dispatch succeeds.
//
// New to C#/RimWorld? See AGENTS.md ("persistence & ticking" and "architecture barriers").
using System;
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        /// <summary>
        /// Keeps the detached planner row beside impure dispatch/state callbacks without ever exposing
        /// Pawn, Def, Scribe, or signal objects to the pure coordinator.
        /// </summary>
        private sealed class ReflectionRuntimeCandidate
        {
            public ReflectionOpportunity opportunity;
            public Func<bool> dispatch;
            public Action consumeAfterDispatch;
            public Action advanceDisabledDebt;
            public Action settleIneligible;

            public bool Dispatch()
            {
                return dispatch != null && dispatch();
            }

            public void ConsumeAfterDispatch()
            {
                consumeAfterDispatch?.Invoke();
            }

            public void AdvanceDisabledDebt()
            {
                advanceDisabledDebt?.Invoke();
            }

            public void SettleIneligible()
            {
                settleIneligible?.Invoke();
            }
        }

        /// <summary>
        /// Arbitrates one natural rest opportunity for one pawn and returns true only when one existing
        /// reflection page was created.
        /// </summary>
        private bool ArbitrateReflectionsForPawn(Pawn pawn)
        {
            if (pawn == null || !IsDiaryEligible(pawn))
            {
                return false;
            }

            string pawnId = pawn.GetUniqueLoadID();
            PawnDiaryRecord diary = FindDiary(pawn, true);
            if (diary == null || string.IsNullOrWhiteSpace(pawnId))
            {
                return false;
            }

            int day = CurrentDayIndex;
            int nowTick = Find.TickManager.TicksGame;
            PawnReflectionState state = diary.EnsureReflectionState();
            if (state.baselineOnNextOpportunity)
            {
                BaselineReflectionState(diary, pawnId, day, nowTick, state);
                return false;
            }

            NarrativePolicySnapshot policy = DiaryNarrativeContinuityPolicy.Snapshot();
            List<ReflectionHistoryEntry> history = state.HistorySnapshot();
            bool globalCooldown = ReflectionCoordinator.IsGlobalCooldownActive(
                nowTick, state.lastReflectionTick, policy);
            bool collectArcEvidence = policy.enabled && !globalCooldown
                && !ReflectionCoordinator.IsKindCooldownActive(
                NarrativeReflectionKindTokens.MajorArc, nowTick, history, policy);
            bool collectQuadrumEvidence = policy.enabled && !globalCooldown
                && !ReflectionCoordinator.IsKindCooldownActive(
                NarrativeReflectionKindTokens.Quadrum, nowTick, history, policy);
            bool collectDayEvidence = policy.enabled && !globalCooldown
                && !ReflectionCoordinator.IsKindCooldownActive(
                NarrativeReflectionKindTokens.Day, nowTick, history, policy);

            List<ReflectionRuntimeCandidate> runtimeCandidates = new List<ReflectionRuntimeCandidate>
            {
                PrepareArcReflectionCandidate(
                    pawn,
                    diary,
                    pawnId,
                    day,
                    state.pendingMajorArc,
                    state.pendingMajorArcAvoidEventId,
                    state,
                    collectArcEvidence),
                PrepareQuadrumReflectionCandidate(pawn, pawnId, day, collectQuadrumEvidence),
                PrepareDayReflectionCandidate(pawn, pawnId, day, collectDayEvidence)
            };

            List<ReflectionOpportunity> opportunities = new List<ReflectionOpportunity>();
            for (int i = 0; i < runtimeCandidates.Count; i++)
            {
                ReflectionOpportunity opportunity = runtimeCandidates[i]?.opportunity;
                if (opportunity != null)
                {
                    opportunities.Add(opportunity);
                }
            }

            // DefDatabase/settings reads stay on this main-thread adapter; the coordinator receives only
            // the already-copied policy and detached DTO rows.
            ReflectionPlan plan = ReflectionCoordinator.Plan(new ReflectionPlanningRequest
            {
                opportunities = opportunities,
                policy = policy,
                currentTick = nowTick,
                lastReflectionTick = state.lastReflectionTick,
                history = history
            });

            ApplyDisabledReflectionInstructions(runtimeCandidates, plan);
            SettleIneligibleReflectionCandidates(runtimeCandidates);
            if (plan.selectedOpportunity == null)
            {
                return false;
            }

            ReflectionRuntimeCandidate selected = FindRuntimeCandidate(runtimeCandidates, plan.selectedOpportunity);
            bool dispatched = selected != null && selected.Dispatch();
            if (!ReflectionCoordinator.CanConsumeAfterDispatch(plan, dispatched))
            {
                return false;
            }

            selected.ConsumeAfterDispatch();
            state.MarkWritten(plan.selectedOpportunity.kind, nowTick);
            return true;
        }

        /// <summary>
        /// Marks the current cadence windows as observed without creating pages. This is the additive
        /// old-save default, preventing the first rest after an upgrade from dumping historical debt.
        /// </summary>
        private void BaselineReflectionState(
            PawnDiaryRecord diary,
            string pawnId,
            int day,
            int nowTick,
            PawnReflectionState state)
        {
            state.baselineOnNextOpportunity = false;
            state.ClearPendingMajorArc();
            state.MarkWritten(NarrativeReflectionKindTokens.MajorArc, nowTick);
            state.MarkWritten(NarrativeReflectionKindTokens.Quadrum, nowTick);
            state.MarkWritten(NarrativeReflectionKindTokens.Day, nowTick);

            PawnArcScheduleState schedule = diary.EnsureArcSchedule();
            schedule.forcedArcYear = CurrentRimYear();
            writtenDayReflections.Add(DaySummaryKey(pawnId, day));
            writtenQuadrumReflections.Add(QuadrumSummaryKey(pawnId, QuadrumIndexForDay(day)));
            ConsumePawnDayFiller(pawnId, day);
            pendingDayHediffs.Remove(DaySummaryKey(pawnId, day));
        }

        private static ReflectionRuntimeCandidate FindRuntimeCandidate(
            List<ReflectionRuntimeCandidate> candidates,
            ReflectionOpportunity selected)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                ReflectionRuntimeCandidate candidate = candidates[i];
                if (candidate != null && ReferenceEquals(candidate.opportunity, selected))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void ApplyDisabledReflectionInstructions(
            List<ReflectionRuntimeCandidate> candidates,
            ReflectionPlan plan)
        {
            if (plan?.stateInstructions == null)
            {
                return;
            }

            for (int i = 0; i < plan.stateInstructions.Count; i++)
            {
                ReflectionStateConsumption instruction = plan.stateInstructions[i];
                if (instruction == null || !instruction.advanceDebtWhenGroupDisabled)
                {
                    continue;
                }

                for (int j = 0; j < candidates.Count; j++)
                {
                    ReflectionRuntimeCandidate candidate = candidates[j];
                    if (candidate?.opportunity != null
                        && string.Equals(candidate.opportunity.kind, instruction.kind, StringComparison.Ordinal))
                    {
                        candidate.AdvanceDisabledDebt();
                        break;
                    }
                }
            }
        }

        private static void SettleIneligibleReflectionCandidates(List<ReflectionRuntimeCandidate> candidates)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                ReflectionRuntimeCandidate candidate = candidates[i];
                if (candidate?.opportunity != null && !candidate.opportunity.due)
                {
                    candidate.SettleIneligible();
                }
            }
        }

        /// <summary>Checks the existing player-facing Reflection group on the main thread.</summary>
        private static bool IsReflectionGroupEnabled(string defName)
        {
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyDefName(GroupDomain.Reflection, defName);
            return group != null
                && PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsGroupEnabled(group.defName);
        }

        /// <summary>Cheap guard used only when every reflection feature was switched off after queuing.</summary>
        private bool HasPendingMajorReflection()
        {
            for (int i = 0; i < diaries.Count; i++)
            {
                if (diaries[i]?.reflectionState?.pendingMajorArc == true)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

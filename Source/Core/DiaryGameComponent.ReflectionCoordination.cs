// Runtime adapter for N4 reflection arbitration. It collects plain opportunities from the existing
// arc/quadrum/day sources, asks the assembly-free ReflectionCoordinator for one winner, dispatches
// only that source's existing signal, and acknowledges cadence state only after Dispatch succeeds.
//
// New to C#/RimWorld? See AGENTS.md ("persistence & ticking" and "architecture barriers").
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
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
            NarrativePolicySnapshot policy = DiaryNarrativeContinuityPolicy.Snapshot();
            return ArbitrateReflectionsForPawn(pawn, policy, DaySummaryOwnsFiller(policy));
        }

        /// <summary>
        /// Uses the scan's shared policy snapshot so a multi-pawn rest pass does not rebuild identical
        /// XML-backed lists for every colonist.
        /// </summary>
        private bool ArbitrateReflectionsForPawn(
            Pawn pawn,
            NarrativePolicySnapshot policy,
            bool daySummaryOwnsFiller)
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
            policy = policy ?? DiaryNarrativeContinuityPolicy.Snapshot();
            if (state.baselineOnNextOpportunity)
            {
                BaselineReflectionState(
                    diary, pawnId, day, nowTick, state, daySummaryOwnsFiller);
                return false;
            }
            if (state.linkedBaselineOnNextOpportunity)
            {
                BaselineLinkedReflectionState(diary, nowTick, state);
                return false;
            }

            List<ReflectionHistoryEntry> history = state.HistorySnapshot();
            bool globalCooldown = ReflectionCoordinator.IsGlobalCooldownActive(
                nowTick, state.lastReflectionTick, policy);
            bool collectArcEvidence = policy.enabled && !globalCooldown
                && !ReflectionCoordinator.IsKindCooldownActive(
                NarrativeReflectionKindTokens.MajorArc, nowTick, history, policy);
            bool collectCrossArcEvidence = policy.enabled && !globalCooldown
                && !ReflectionCoordinator.IsKindCooldownActive(
                NarrativeReflectionKindTokens.CrossArc, nowTick, history, policy);
            bool collectBeliefEvidence = policy.enabled && !globalCooldown
                && !ReflectionCoordinator.IsKindCooldownActive(
                NarrativeReflectionKindTokens.Belief, nowTick, history, policy);
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
                PrepareCrossArcReflectionCandidate(
                    pawn, diary, pawnId, state, policy, collectCrossArcEvidence),
                PrepareBeliefReflectionCandidate(
                    pawn, diary, pawnId, day, collectBeliefEvidence),
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
            PawnReflectionState state,
            bool consumeDayFiller)
        {
            state.baselineOnNextOpportunity = false;
            state.linkedBaselineOnNextOpportunity = false;
            state.ClearPendingMajorArc();
            state.MarkWritten(NarrativeReflectionKindTokens.MajorArc, nowTick);
            state.MarkWritten(NarrativeReflectionKindTokens.CrossArc, nowTick);
            state.MarkWritten(NarrativeReflectionKindTokens.Belief, nowTick);
            state.MarkWritten(NarrativeReflectionKindTokens.Quadrum, nowTick);
            state.MarkWritten(NarrativeReflectionKindTokens.Day, nowTick);
            diary.EnsureBeliefState().BaselineReflection(
                nowTick, DiaryBeliefPolicy.Snapshot());

            PawnArcScheduleState schedule = diary.EnsureArcSchedule();
            schedule.forcedArcYear = CurrentRimYear();
            writtenDayReflections.Add(DaySummaryKey(pawnId, day));
            writtenQuadrumReflections.Add(QuadrumSummaryKey(pawnId, QuadrumIndexForDay(day)));
            if (consumeDayFiller)
            {
                ConsumePawnDayFiller(pawnId, day);
            }
            pendingDayHediffs.Remove(DaySummaryKey(pawnId, day));
        }

        /// <summary>
        /// Establishes the additive N4.2 boundary on saves whose earlier N4.1 baseline already ran.
        /// Historical linked/belief state is observed and cleared without creating a catch-up page.
        /// </summary>
        private static void BaselineLinkedReflectionState(
            PawnDiaryRecord diary,
            int nowTick,
            PawnReflectionState state)
        {
            state.linkedBaselineOnNextOpportunity = false;
            state.MarkWritten(NarrativeReflectionKindTokens.CrossArc, nowTick);
            state.MarkWritten(NarrativeReflectionKindTokens.Belief, nowTick);
            diary?.EnsureBeliefState()?.BaselineReflection(nowTick, DiaryBeliefPolicy.Snapshot());
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

        /// <summary>
        /// True only when the day-reflection path, rather than the ambient fallback, owns today's
        /// filler batches.
        /// </summary>
        private static bool DaySummaryOwnsFiller(NarrativePolicySnapshot policy)
        {
            return DiaryTuning.Current.daySummaryEnabled
                && policy?.enabled == true
                && IsReflectionGroupEnabled(DayReflectionEventData.DefNameToken);
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

        /// <summary>
        /// Clears saved major debt that the map-only natural-rest scanner can no longer consume. This
        /// runs only when such debt is keeping an otherwise-disabled reflection scan alive.
        /// </summary>
        private void PrunePendingMajorReflectionsWithoutRestOwner(List<Pawn> freeColonists)
        {
            HashSet<string> restOwnerIds = new HashSet<string>(StringComparer.Ordinal);
            if (freeColonists != null)
            {
                for (int i = 0; i < freeColonists.Count; i++)
                {
                    Pawn pawn = freeColonists[i];
                    if (pawn != null && IsDiaryEligible(pawn))
                    {
                        restOwnerIds.Add(pawn.GetUniqueLoadID());
                    }
                }
            }

            for (int i = 0; i < diaries.Count; i++)
            {
                PawnDiaryRecord diary = diaries[i];
                if (diary?.reflectionState?.pendingMajorArc == true
                    && !restOwnerIds.Contains(diary.pawnId ?? string.Empty))
                {
                    diary.reflectionState.ClearPendingMajorArc();
                }
            }
        }

        /// <summary>Cheap rest-scan guard used to clear saved belief debt after policy disablement.</summary>
        private bool HasPendingBeliefReflection()
        {
            for (int i = 0; i < diaries.Count; i++)
            {
                PawnBeliefState belief = diaries[i]?.beliefState;
                if (belief != null
                    && (belief.hasPendingCertainty || belief.pendingIdeologyChange))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

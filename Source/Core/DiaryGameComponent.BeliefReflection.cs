// Ideology Phase 4 / Narrative N4 rest-time reflection adapter. Phase 3 owns passive saved belief
// observations; this file converts that detached debt (or one recent event-time belief block) into a
// coordinator opportunity and consumes it only after the catalog creates a page.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private sealed class RecentBeliefMemory
        {
            public string eventId = string.Empty;
            public string context = string.Empty;
        }

        /// <summary>Builds one detached belief opportunity without changing pending belief state.</summary>
        private ReflectionRuntimeCandidate PrepareBeliefReflectionCandidate(
            Pawn pawn,
            PawnDiaryRecord diary,
            string pawnId,
            int day,
            bool collectEvidence)
        {
            int nowTick = Find.TickManager.TicksGame;
            BeliefPolicySnapshot policy = DiaryBeliefPolicy.Snapshot();
            bool ideologyActive = ModsConfig.IdeologyActive;
            PawnBeliefState belief = diary?.beliefState;
            if (belief == null && ideologyActive && policy.enabled)
            {
                belief = diary.EnsureBeliefState();
            }
            belief?.Normalize(nowTick, policy);
            bool groupEnabled = ideologyActive
                && policy.enabled
                && IsReflectionGroupEnabled(BeliefReflectionEventData.DefNameToken);
            ReflectionRuntimeCandidate runtime = new ReflectionRuntimeCandidate
            {
                opportunity = new ReflectionOpportunity
                {
                    kind = NarrativeReflectionKindTokens.Belief,
                    pawnId = pawnId,
                    nowTick = nowTick,
                    candidateMemoryCount = 1,
                    linkedMemoryCount = 1,
                    importance = belief?.pendingIdeologyChange == true
                        ? NarrativeSalienceTokens.Major
                        : NarrativeSalienceTokens.Meaningful,
                    due = false,
                    cooldownSatisfied = true,
                    groupEnabled = groupEnabled
                }
            };
            if (belief != null)
            {
                runtime.advanceDisabledDebt = () =>
                    belief.AdvanceDisabledReflectionDebt(nowTick, policy);
            }
            if (belief == null || !groupEnabled || !collectEvidence || belief.baselineOnNextScan
                || !belief.hasLastObservation)
            {
                return runtime;
            }

            string trend;
            string magnitude;
            BeliefCertaintyPolicy.Trend(
                belief.pendingCertaintyBefore,
                belief.pendingCertaintyAfter,
                policy,
                out trend,
                out magnitude);
            RecentBeliefMemory recent = FindRecentBeliefMemory(
                diary, pawnId, nowTick, policy.recentBeliefEventWindowTicks, belief.lastReflectedSourceIds);
            BeliefReflectionDecision decision = BeliefReflectionPolicy.Plan(
                new BeliefReflectionRequest
                {
                    groupEnabled = groupEnabled,
                    signalEnabled = ModsConfig.IdeologyActive,
                    hasBeliefContext = true,
                    nowTick = nowTick,
                    lastReflectionTick = belief.lastReflectionTick,
                    reflectionsThisQuadrum = belief.reflectionsThisQuadrum,
                    pendingIdeologyChange = belief.pendingIdeologyChange,
                    pendingMajorCertaintyShift = belief.hasPendingCertainty
                        && magnitude == BeliefCertaintyMagnitudeTokens.Major,
                    hasRecentRelevantEvent = recent != null,
                    recentSourceAlreadyReflected = false,
                    hasPendingCertaintyDrift = belief.hasPendingCertainty,
                    allowQuietReflection = true,
                    quietRoll = StableBeliefQuietRoll(pawnId, day)
                },
                policy);
            runtime.opportunity.due = decision.allowed;
            if (!decision.allowed)
            {
                return runtime;
            }

            string preparedEventId = "belief-reflection|" + pawnId + "|" + nowTick;
            BeliefContextBuildResult prepared = decision.trigger == BeliefReflectionTriggerTokens.RecentEvent
                && recent != null
                ? new BeliefContextBuildResult
                {
                    evaluated = true,
                    fullContext = recent.context,
                    resolution = new BeliefStanceResolution()
                }
                : BeliefContextBuilder.BuildReflection(
                    pawn, belief, preparedEventId, nowTick, decision.trigger, policy);
            if (prepared == null || string.IsNullOrWhiteSpace(prepared.fullContext))
            {
                runtime.opportunity.due = false;
                return runtime;
            }

            List<string> sourceIds = recent == null
                ? new List<string>()
                : new List<string> { recent.eventId };
            runtime.opportunity.sourceEventIds = sourceIds;
            runtime.dispatch = () => DispatchPreparedBeliefReflection(
                pawn, pawnId, nowTick, decision, prepared);
            runtime.consumeAfterDispatch = () =>
                belief.MarkReflection(nowTick, sourceIds, prepared.resolution, policy);
            return runtime;
        }

        private RecentBeliefMemory FindRecentBeliefMemory(
            PawnDiaryRecord diary,
            string pawnId,
            int nowTick,
            int windowTicks,
            List<string> reflectedSourceIds)
        {
            List<string> eventIds = diary?.eventIds;
            if (eventIds == null || windowTicks <= 0) return null;
            HashSet<string> reflected = new HashSet<string>(
                reflectedSourceIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            RecentBeliefMemory best = null;
            int bestTick = int.MinValue;
            for (int i = 0; i < eventIds.Count; i++)
            {
                DiaryEvent diaryEvent = events.FindEvent(eventIds[i]);
                string role;
                if (diaryEvent == null
                    || diaryEvent.tick > nowTick
                    || (long)nowTick - diaryEvent.tick > windowTicks
                    || reflected.Contains(diaryEvent.eventId)
                    || IsReflectionDefName(diaryEvent.interactionDefName)
                    || !diaryEvent.TryGetDisplayRoleForPawn(pawnId, out role))
                {
                    continue;
                }

                string context = diaryEvent.BeliefContextForRole(role);
                if (string.IsNullOrWhiteSpace(context) || diaryEvent.tick < bestTick) continue;
                if (diaryEvent.tick == bestTick && best != null
                    && string.Compare(diaryEvent.eventId, best.eventId, StringComparison.Ordinal) >= 0)
                {
                    continue;
                }
                bestTick = diaryEvent.tick;
                best = new RecentBeliefMemory
                {
                    eventId = diaryEvent.eventId,
                    context = context
                };
            }
            return best;
        }

        private bool DispatchPreparedBeliefReflection(
            Pawn pawn,
            string pawnId,
            int nowTick,
            BeliefReflectionDecision decision,
            BeliefContextBuildResult prepared)
        {
            BeliefReflectionEventData data = new BeliefReflectionEventData
            {
                PawnId = pawnId,
                Tick = nowTick,
                DefName = BeliefReflectionEventData.DefNameToken,
                Trigger = decision.trigger,
                HasBeliefContext = !string.IsNullOrWhiteSpace(prepared?.fullContext),
                AlreadyWritten = false
            };
            string label = "PawnDiary.Event.BeliefReflectionLabel".Translate().Resolve();
            string text = "PawnDiary.Event.BeliefReflectionText".Translate(pawn.LabelShortCap).Resolve();
            string instruction = "PawnDiary.Event.BeliefReflectionInstruction"
                .Translate(pawn.LabelShortCap).Resolve();
            string context = BeliefReflectionEventData.BuildGameContext(decision.trigger);
            return Dispatch(new BeliefReflectionSignal(
                data, pawn, label, text, instruction, context, prepared));
        }

        private static float StableBeliefQuietRoll(string pawnId, int day)
        {
            int seed = HumorChancePolicy.StableSeed(
                "belief-reflection|" + day,
                pawnId ?? string.Empty);
            return Math.Abs(seed % 10000) / 10000f;
        }
    }
}

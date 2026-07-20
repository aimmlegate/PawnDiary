// Anomaly persistence, silent phase baselining, and exact study/visible-creepjoiner transaction
// ownership, including non-terminal surgical disclosure. Live DLC objects are read only by
// DlcContext; this partial commits detached history before it optionally dispatches a page.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private int anomalySupportSchemaVersion;
        private bool anomalyFirstStudyBreakthroughObserved;
        private List<string> anomalyCompletedStudyDefNames = new List<string>();
        private List<string> anomalyPromotedStudyMilestoneKeys = new List<string>();
        private string anomalyMonolithBaselineLevelDefName = string.Empty;
        private AnomalyMonolithKnowledgeState anomalyLastMonolithKnowledgeSnapshot;
        private List<CreepJoinerArcState> anomalyCreepJoinerArcs =
            new List<CreepJoinerArcState>();

        /// <summary>Scribes additive keys and normalizes every loaded primitive collection/row.</summary>
        private void ExposeAnomalyData()
        {
            Scribe_Values.Look(
                ref anomalySupportSchemaVersion,
                AnomalySaveKeys.SupportSchemaVersion,
                0);
            Scribe_Values.Look(
                ref anomalyFirstStudyBreakthroughObserved,
                AnomalySaveKeys.FirstStudyBreakthroughObserved,
                false);
            Scribe_Collections.Look(
                ref anomalyCompletedStudyDefNames,
                AnomalySaveKeys.CompletedStudyDefNames,
                LookMode.Value);
            Scribe_Collections.Look(
                ref anomalyPromotedStudyMilestoneKeys,
                AnomalySaveKeys.PromotedStudyMilestoneKeys,
                LookMode.Value);
            Scribe_Values.Look(
                ref anomalyMonolithBaselineLevelDefName,
                AnomalySaveKeys.MonolithBaselineLevelDefName);
            Scribe_Deep.Look(
                ref anomalyLastMonolithKnowledgeSnapshot,
                AnomalySaveKeys.LastMonolithKnowledgeSnapshot);
            Scribe_Collections.Look(
                ref anomalyCreepJoinerArcs,
                AnomalySaveKeys.CreepJoinerArcs,
                LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ApplyAnomalyState(AnomalyPersistencePolicy.Normalize(AnomalyStateSnapshot()));
            }
        }

        /// <summary>Starts a new colony with trustworthy empty study history and no catch-up page.</summary>
        private void ResetAnomalyForNewGame()
        {
            ApplyAnomalyState(AnomalyPersistencePolicy.NewGame(
                DlcContext.CurrentAnomalyMonolithLevelDefName()));
            ResetAnomalyTransientState();
        }

        /// <summary>
        /// Baselines a pre-A1 save only while Anomaly is active. Loaded-map study comps provide exact
        /// completed-kind facts, but the scan is intentionally marked incomplete because off-map and
        /// despawned subjects are not reconstructed; pure policy therefore suppresses a false first.
        /// </summary>
        private void BootstrapAnomalyForLoadedSave()
        {
            ResetAnomalyTransientState();
            if (!ModsConfig.AnomalyActive
                || anomalySupportSchemaVersion >= AnomalyPersistencePolicy.CurrentSchemaVersion)
            {
                return;
            }

            AnomalyPersistentStateSnapshot state = AnomalyStateSnapshot();
            if (state.schemaVersion < AnomalyPersistencePolicy.StudySchemaVersion)
            {
                state = AnomalyPersistencePolicy.BaselineLegacy(
                    state, DlcContext.CaptureAnomalyLegacyBaseline());
            }
            if (state.schemaVersion < AnomalyPersistencePolicy.CurrentSchemaVersion)
            {
                state = AnomalyPersistencePolicy.BaselineCreepJoiners(
                    state, DlcContext.CaptureCreepJoinerLegacyBaseline());
            }
            ApplyAnomalyState(state);
        }

        private AnomalyPersistentStateSnapshot AnomalyStateSnapshot()
        {
            return new AnomalyPersistentStateSnapshot
            {
                schemaVersion = anomalySupportSchemaVersion,
                firstStudyBreakthroughObserved = anomalyFirstStudyBreakthroughObserved,
                completedStudyDefNames = anomalyCompletedStudyDefNames == null
                    ? new List<string>() : new List<string>(anomalyCompletedStudyDefNames),
                promotedStudyMilestoneKeys = anomalyPromotedStudyMilestoneKeys == null
                    ? new List<string>() : new List<string>(anomalyPromotedStudyMilestoneKeys),
                monolithBaselineLevelDefName = anomalyMonolithBaselineLevelDefName ?? string.Empty,
                lastMonolithKnowledgeSnapshot = anomalyLastMonolithKnowledgeSnapshot?.ToSnapshot(),
                creepJoinerArcs = CreepJoinerArcSnapshots(anomalyCreepJoinerArcs)
            };
        }

        private void ApplyAnomalyState(AnomalyPersistentStateSnapshot source)
        {
            AnomalyPersistentStateSnapshot normalized = AnomalyPersistencePolicy.Normalize(source);
            anomalySupportSchemaVersion = normalized.schemaVersion;
            anomalyFirstStudyBreakthroughObserved = normalized.firstStudyBreakthroughObserved;
            anomalyCompletedStudyDefNames = normalized.completedStudyDefNames;
            anomalyPromotedStudyMilestoneKeys = normalized.promotedStudyMilestoneKeys;
            anomalyMonolithBaselineLevelDefName = normalized.monolithBaselineLevelDefName;
            anomalyLastMonolithKnowledgeSnapshot = AnomalyMonolithKnowledgeState.FromSnapshot(
                normalized.lastMonolithKnowledgeSnapshot);
            anomalyCreepJoinerArcs = CreepJoinerArcStates(normalized.creepJoinerArcs);
        }

        private static void ResetAnomalyTransientState()
        {
            AnomalyTransientState.Reset();
        }

        /// <summary>
        /// Completes one exact study callback, always commits truthful milestone history, then creates
        /// a page only for the exact eligible researcher and only after the catalog accepts the signal.
        /// </summary>
        internal void CompleteAnomalyStudy(
            object studyObject,
            Pawn studier,
            object categoryObject,
            AnomalyStudyFacts before)
        {
            AnomalyStudyFacts facts;
            bool transitioned;
            if (!DlcContext.TryCompleteAnomalyStudy(
                studyObject, studier, categoryObject, before, out facts, out transitioned)) return;
            AnomalyPolicySnapshot policy = DiaryAnomalyPolicy.Snapshot();
            // Recent-study evidence is not Tale ownership. Retain it after every exactly correlated
            // OnStudied call—even below a note threshold—so a later breach can name only the actual
            // recent studier after the consume-once generic Tale claim is gone.
            AnomalyRecentStudyCache.Register(
                new AnomalyRecentStudyFact
                {
                    studierPawnId = before.studierPawnId,
                    studiedEntityId = before.studiedEntityId,
                    studiedDefName = before.studiedDefName,
                    studiedTick = before.tick
                },
                before.tick,
                policy.recentStudierMaxAgeTicks);
            if (!transitioned) return;

            AnomalyStudyHistorySnapshot history = new AnomalyStudyHistorySnapshot
            {
                firstBreakthroughObserved = anomalyFirstStudyBreakthroughObserved
            };
            history.completedStudyDefNames.AddRange(
                anomalyCompletedStudyDefNames ?? new List<string>());
            history.observedPromotionKeys.AddRange(
                anomalyPromotedStudyMilestoneKeys ?? new List<string>());

            AnomalyStudyPlan plan = AnomalyStudyPolicy.Plan(facts, history, policy);
            if (plan.disposition == AnomalyStudyDisposition.DropInvalid) return;

            // Observation owns history even when output is disabled, the exact author is ineligible,
            // or the subject is the monolith. That prevents later retroactive "first" pages.
            AnomalyPersistentStateSnapshot next = AnomalyStateSnapshot();
            if (plan.historyMutation.observeFirstBreakthrough)
                next.firstStudyBreakthroughObserved = true;
            if (!string.IsNullOrWhiteSpace(plan.historyMutation.completedStudyDefName))
                next.completedStudyDefNames.Add(plan.historyMutation.completedStudyDefName);
            next.promotedStudyMilestoneKeys.AddRange(
                plan.historyMutation.observedPromotionKeys);

            if (facts.isMonolith
                && (plan.stageToken.Length > 0 || plan.monolithBecameActivatable))
            {
                next.lastMonolithKnowledgeSnapshot = new AnomalyMonolithKnowledgeSnapshot
                {
                    researcherPawnId = facts.studierPawnId,
                    studyStage = plan.stageToken,
                    tick = facts.tick,
                    reachedProgress = facts.newProgress,
                    becameActivatable = plan.monolithBecameActivatable,
                    consumed = false
                };
            }
            ApplyAnomalyState(next);

            if (plan.disposition != AnomalyStudyDisposition.Generate || studier == null) return;
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyAnomalyEvent(
                AnomalyEventDefNames.StudyBreakthrough);
            AnomalyStudySignal signal = new AnomalyStudySignal(
                studier, facts, plan, policy, group);
            Dispatch(signal);
            if (signal.CreatedEvent == null) return;

            // Ownership begins only after the dedicated page exists. A disabled/missing group or a
            // failed dispatcher therefore leaves vanilla's generic StudiedEntity Tale available.
            AnomalyStudySuppressionCache.Register(
                new AnomalyStudyTaleClaim
                {
                    studierPawnId = facts.studierPawnId,
                    studiedEntityId = facts.studiedEntityId,
                    studiedDefName = facts.studiedDefName,
                    studyJobId = facts.studyJobId,
                    acceptedTick = facts.tick
                },
                facts.tick,
                policy.studyTaleSuppressionTicks);
        }

        /// <summary>
        /// Opens or enriches the visible creepjoiner arc from the existing canonical arrival route.
        /// This runs before arrival page eligibility, so disabled generation/settings still preserve
        /// truthful continuity; an event ID is added only after the arrival page actually exists.
        /// </summary>
        internal void ObserveCreepJoinerArrival(
            Pawn pawn,
            string capturedArrivalContext,
            string createdArrivalEventId = null)
        {
            if (!ModsConfig.AnomalyActive || pawn == null
                || !CreepJoinerOutcomePolicy.IsCreepJoinerArrivalContext(capturedArrivalContext))
                return;

            string pawnId = pawn.GetUniqueLoadID();
            int tick = Find.TickManager?.TicksGame ?? 0;
            AnomalyPersistentStateSnapshot state = AnomalyStateSnapshot();
            CreepJoinerArcSnapshot existing = FindCreepJoinerArc(state.creepJoinerArcs, pawnId);
            CreepJoinerArcSnapshot next = CreepJoinerOutcomePolicy.UpsertArrival(
                existing, pawnId, tick, createdArrivalEventId);
            if (next == null) return;
            ReplaceCreepJoinerArc(state.creepJoinerArcs, next);
            ApplyAnomalyState(state);
        }

        /// <summary>
        /// Verifies one exact public lifecycle transition, commits its terminal history first (using a
        /// blank replay barrier when visibility is untrusted), then optionally dispatches one page.
        /// </summary>
        internal void CompleteCreepJoinerOutcome(
            object trackerObject,
            CreepJoinerOutcomeCapture capture)
        {
            CreepJoinerOutcomeFacts facts;
            if (!DlcContext.TryCompleteCreepJoinerOutcome(
                trackerObject, capture, out facts) || facts == null) return;

            AnomalyPersistentStateSnapshot state = AnomalyStateSnapshot();
            CreepJoinerArcSnapshot existing = FindCreepJoinerArc(
                state.creepJoinerArcs, facts.pawnId);
            AnomalyPolicySnapshot policy = DiaryAnomalyPolicy.Snapshot();
            CreepJoinerOutcomePlan plan = CreepJoinerOutcomePolicy.Plan(facts, existing, policy);
            if (!plan.valid) return;

            if (plan.advanceArc && plan.nextArc != null)
            {
                ReplaceCreepJoinerArc(state.creepJoinerArcs, plan.nextArc);
                ApplyAnomalyState(state);
            }
            if (!plan.writePage || plan.selectedWriter == null) return;

            Pawn writer = capture.ResolveWriter(plan.selectedWriter.pawnId);
            if (writer == null) return;
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyAnomalyEvent(
                AnomalyEventDefNames.CreepJoinerOutcome);
            CreepJoinerOutcomeSignal signal = new CreepJoinerOutcomeSignal(
                facts, plan, policy, group, writer, capture.subject);
            Dispatch(signal);
            if (signal.CreatedEvent == null) return;

            // The transition remains committed even if the group/transport was unavailable. Only
            // attach an event ID after AddSoloEvent actually produced the canonical page.
            AnomalyPersistentStateSnapshot afterEvent = AnomalyStateSnapshot();
            CreepJoinerArcSnapshot arc = FindCreepJoinerArc(
                afterEvent.creepJoinerArcs, facts.pawnId);
            if (arc == null || !arc.terminal
                || !string.Equals(arc.lastVisiblePhase, plan.phase, StringComparison.Ordinal)) return;
            arc.lastVisibleEventId = signal.CreatedEvent.eventId ?? string.Empty;
            ReplaceCreepJoinerArc(afterEvent.creepJoinerArcs, arc);
            ApplyAnomalyState(afterEvent);
        }

        /// <summary>
        /// Verifies one exact returned surgical recipe, commits non-terminal reveal history before
        /// output, then reports whether a dedicated event exists so Tale ownership can decide safely.
        /// </summary>
        internal CreepJoinerSurgicalDisclosurePlan CompleteCreepJoinerSurgicalInspection(
            Pawn subject,
            Pawn surgeon,
            CreepJoinerSurgicalInspectionCapture capture,
            out bool dedicatedEventCreated)
        {
            dedicatedEventCreated = false;
            CreepJoinerSurgicalDisclosureFacts facts;
            if (!DlcContext.TryCompleteCreepJoinerSurgicalInspection(
                    subject, surgeon, capture, out facts)
                || facts == null) return null;

            AnomalyPersistentStateSnapshot state = AnomalyStateSnapshot();
            CreepJoinerArcSnapshot existing = FindCreepJoinerArc(
                state.creepJoinerArcs, facts.subjectPawnId);
            AnomalyPolicySnapshot policy = DiaryAnomalyPolicy.Snapshot();
            CreepJoinerSurgicalDisclosurePlan plan =
                CreepJoinerSurgicalDisclosurePolicy.Plan(facts, existing, policy);
            if (!plan.valid) return plan;

            if (plan.advanceArc && plan.nextArc != null)
            {
                ReplaceCreepJoinerArc(state.creepJoinerArcs, plan.nextArc);
                ApplyAnomalyState(state);
            }
            if (!plan.writePage || plan.selectedWriters.Count == 0) return plan;

            List<Pawn> writers = new List<Pawn>();
            for (int i = 0; i < plan.selectedWriters.Count; i++)
            {
                Pawn writer = capture.ResolveWriter(plan.selectedWriters[i].pawnId);
                if (writer == null) return plan;
                writers.Add(writer);
            }
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyAnomalyEvent(
                AnomalyEventDefNames.CreepJoinerOutcome);
            CreepJoinerSurgicalDisclosureSignal signal =
                new CreepJoinerSurgicalDisclosureSignal(
                    facts, plan, policy, group, writers, subject, surgeon);
            Dispatch(signal);
            if (signal.CreatedEvent == null) return plan;

            // The same seven-field row records the dedicated event only after creation. The row stays
            // non-terminal so later visible lifecycle methods remain eligible to close the arc.
            AnomalyPersistentStateSnapshot afterEvent = AnomalyStateSnapshot();
            CreepJoinerArcSnapshot arc = FindCreepJoinerArc(
                afterEvent.creepJoinerArcs, facts.subjectPawnId);
            if (arc == null || arc.terminal
                || arc.lastVisiblePhase != AnomalyOutcomeTokens.SurgicalReveal) return plan;
            arc.lastVisibleEventId = signal.CreatedEvent.eventId ?? string.Empty;
            ReplaceCreepJoinerArc(afterEvent.creepJoinerArcs, arc);
            ApplyAnomalyState(afterEvent);
            dedicatedEventCreated = true;
            return plan;
        }

        private static List<CreepJoinerArcSnapshot> CreepJoinerArcSnapshots(
            List<CreepJoinerArcState> source)
        {
            List<CreepJoinerArcSnapshot> result = new List<CreepJoinerArcSnapshot>();
            if (source == null) return result;
            int count = Math.Min(source.Count, AnomalyPolicyLimits.MaximumCreepJoinerArcInputs);
            for (int i = 0; i < count; i++)
                result.Add(source[i]?.ToSnapshot());
            return result;
        }

        private static List<CreepJoinerArcState> CreepJoinerArcStates(
            List<CreepJoinerArcSnapshot> source)
        {
            List<CreepJoinerArcState> result = new List<CreepJoinerArcState>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                CreepJoinerArcState row = CreepJoinerArcState.FromSnapshot(source[i]);
                if (row != null) result.Add(row);
            }
            return result;
        }

        private static CreepJoinerArcSnapshot FindCreepJoinerArc(
            List<CreepJoinerArcSnapshot> arcs,
            string pawnId)
        {
            if (arcs == null || string.IsNullOrWhiteSpace(pawnId)) return null;
            for (int i = 0; i < arcs.Count; i++)
            {
                if (arcs[i] != null && string.Equals(
                    arcs[i].pawnId, pawnId, StringComparison.Ordinal)) return arcs[i];
            }
            return null;
        }

        private static void ReplaceCreepJoinerArc(
            List<CreepJoinerArcSnapshot> arcs,
            CreepJoinerArcSnapshot replacement)
        {
            if (arcs == null || replacement == null) return;
            for (int i = 0; i < arcs.Count; i++)
            {
                if (arcs[i] != null && string.Equals(
                    arcs[i].pawnId, replacement.pawnId, StringComparison.Ordinal))
                {
                    arcs[i] = replacement;
                    return;
                }
            }
            arcs.Add(replacement);
        }

        /// <summary>Detached read-only seam for focused loaded-game save/baseline fixtures.</summary>
        internal AnomalyPersistentStateSnapshot AnomalyStateSnapshotForTests()
        {
            return AnomalyPersistencePolicy.Normalize(AnomalyStateSnapshot());
        }
    }
}

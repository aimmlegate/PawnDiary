// Anomaly persistence, silent legacy baselining, and the A1.2 study transaction owner. Live DLC
// objects are read only by DlcContext; this partial commits detached history before it optionally
// dispatches one exact researcher-owned study page.
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

        /// <summary>Scribes six additive keys and normalizes every loaded primitive collection/row.</summary>
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

            AnomalyLegacyBaselineFacts facts = DlcContext.CaptureAnomalyLegacyBaseline();
            ApplyAnomalyState(AnomalyPersistencePolicy.BaselineLegacy(
                AnomalyStateSnapshot(), facts));
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
                lastMonolithKnowledgeSnapshot = anomalyLastMonolithKnowledgeSnapshot?.ToSnapshot()
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

        /// <summary>Detached read-only seam for focused loaded-game save/baseline fixtures.</summary>
        internal AnomalyPersistentStateSnapshot AnomalyStateSnapshotForTests()
        {
            return AnomalyPersistencePolicy.Normalize(AnomalyStateSnapshot());
        }
    }
}

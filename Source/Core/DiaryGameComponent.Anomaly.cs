// Anomaly A1.1 persistence and silent legacy baselining. This partial owns only stable study history,
// one optional monolith knowledge snapshot, and lifecycle reset. It registers no Harmony source and
// cannot create a diary page; A1.2/A1.3 will consume these contracts after their live hooks are tested.
using System.Collections.Generic;
using PawnDiary.Capture;
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

        /// <summary>Detached read-only seam for focused loaded-game save/baseline fixtures.</summary>
        internal AnomalyPersistentStateSnapshot AnomalyStateSnapshotForTests()
        {
            return AnomalyPersistencePolicy.Normalize(AnomalyStateSnapshot());
        }
    }
}

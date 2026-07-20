// Anomaly A1.1 persistence and silent legacy baselining. This partial owns only stable study history,
// one optional monolith knowledge snapshot, and lifecycle reset. It registers no Harmony source and
// cannot create a diary page; A1.2/A1.3 will consume these contracts after their live hooks are tested.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
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
            ApplyAnomalyState(AnomalyPersistencePolicy.NewGame(CurrentAnomalyMonolithLevelDefName()));
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

            AnomalyLegacyBaselineFacts facts = CaptureAnomalyLegacyBaseline();
            ApplyAnomalyState(AnomalyPersistencePolicy.BaselineLegacy(
                AnomalyStateSnapshot(), facts));
        }

        private static AnomalyLegacyBaselineFacts CaptureAnomalyLegacyBaseline()
        {
            AnomalyLegacyBaselineFacts facts = new AnomalyLegacyBaselineFacts
            {
                anomalyAvailable = ModsConfig.AnomalyActive,
                // Loaded maps cannot prove the history of despawned/off-map studied subjects. This
                // false value is deliberate: the old save stays silent instead of claiming a new first.
                historyComplete = false,
                currentMonolithLevelDefName = CurrentAnomalyMonolithLevelDefName()
            };
            if (!facts.anomalyAvailable || Find.Maps == null) return facts;

            for (int mapIndex = 0; mapIndex < Find.Maps.Count; mapIndex++)
            {
                Map map = Find.Maps[mapIndex];
                List<Thing> things = map?.listerThings?.AllThings;
                if (things == null) continue;
                for (int thingIndex = 0; thingIndex < things.Count; thingIndex++)
                {
                    Thing thing = things[thingIndex];
                    CompStudyUnlocks study = null;
                    try
                    {
                        study = thing?.TryGetComp<CompStudyUnlocks>();
                        if (study == null) continue;
                        if (study.Progress > 0) facts.anyCommittedStudyProgress = true;
                        if (study.Completed && thing?.def != null)
                            facts.completedStudyDefNames.Add(thing.def.defName);
                    }
                    catch (Exception exception)
                    {
                        // Modded comp getters may throw. The baseline is already conservative, so one
                        // broken subject is omitted and the save remains silent rather than failing load.
                        Log.WarningOnce(
                            "[Pawn Diary] Anomaly legacy study baseline skipped a broken studied subject: "
                            + exception.GetType().Name + ": " + exception.Message,
                            "PawnDiary.Anomaly.LegacyStudyBaseline".GetHashCode());
                    }
                }
            }

            return facts;
        }

        private static string CurrentAnomalyMonolithLevelDefName()
        {
            if (!ModsConfig.AnomalyActive) return string.Empty;
            try
            {
                return Find.Anomaly?.LevelDef?.defName ?? string.Empty;
            }
            catch (Exception exception)
            {
                Log.WarningOnce(
                    "[Pawn Diary] Could not baseline the current Anomaly monolith level: "
                    + exception.GetType().Name + ": " + exception.Message,
                    "PawnDiary.Anomaly.MonolithBaseline".GetHashCode());
                return string.Empty;
            }
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

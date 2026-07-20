// DLC-safe live Anomaly state capture. This adapter is the only place A1.1 reads monolith or
// CompStudyUnlocks state; it converts optional-DLC objects into detached primitive baseline facts
// before the pure persistence policy sees them.
//
// New to C#/RimWorld? See AGENTS.md ("DLC-safety") and the DlcContext.cs file header.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>Guarded Anomaly accessors for new-game and pre-A1 save baselines.</summary>
    internal static partial class DlcContext
    {
        /// <summary>
        /// Captures the incomplete loaded-map study baseline used by the one-time pre-A1 migration.
        /// This deliberately scans all loaded things because vanilla exposes no indexed collection for
        /// CompStudyUnlocks; the scan runs only while a legacy schema is being advanced.
        /// </summary>
        internal static AnomalyLegacyBaselineFacts CaptureAnomalyLegacyBaseline()
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
                    try
                    {
                        CompStudyUnlocks study = thing?.TryGetComp<CompStudyUnlocks>();
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

        /// <summary>Returns the guarded current monolith level, or empty without usable Anomaly state.</summary>
        internal static string CurrentAnomalyMonolithLevelDefName()
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
    }
}

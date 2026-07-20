// DLC-safe live Anomaly state capture. This adapter is the only place A1 reads monolith,
// CompStudyUnlocks, codex, knowledge-category, and containment state; it converts optional-DLC
// objects into detached primitive facts before pure policy or persistence sees them.
//
// New to C#/RimWorld? See AGENTS.md ("DLC-safety") and the DlcContext.cs file header.
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>Guarded Anomaly accessors for save baselines and exact study callbacks.</summary>
    internal static partial class DlcContext
    {
        /// <summary>
        /// Copies only the cheap exact before-state for one live <c>CompStudyUnlocks.OnStudied</c>
        /// call. Visible labels and surroundings are intentionally deferred until vanilla proves a
        /// milestone, so ordinary study work does not build prompt context twice.
        /// </summary>
        internal static bool TryCaptureAnomalyStudyBefore(
            object studyObject,
            Pawn studier,
            out AnomalyStudyFacts facts)
        {
            facts = null;
            if (!ModsConfig.AnomalyActive || studier == null) return false;

            CompStudyUnlocks study = studyObject as CompStudyUnlocks;
            Thing studied = study?.parent;
            if (study == null || studied?.def == null) return false;

            Building_VoidMonolith monolith = studied as Building_VoidMonolith;
            facts = new AnomalyStudyFacts
            {
                studiedEntityId = studied.GetUniqueLoadID(),
                studiedDefName = studied.def.defName,
                studierPawnId = studier.GetUniqueLoadID(),
                studyJobId = studier.CurJob?.GetUniqueLoadID() ?? string.Empty,
                tick = Find.TickManager?.TicksGame ?? 0,
                oldProgress = study.Progress,
                completedBefore = study.Completed,
                isMonolith = monolith != null,
                monolithActivatableBefore = CanActivateMonolith(monolith)
            };
            return !string.IsNullOrWhiteSpace(facts.studiedEntityId)
                && !string.IsNullOrWhiteSpace(facts.studiedDefName)
                && !string.IsNullOrWhiteSpace(facts.studierPawnId);
        }

        /// <summary>
        /// Completes one before/after callback only when every exact identity still matches. The
        /// return value proves correlation; <paramref name="transitioned"/> separately says whether
        /// policy work is needed. That lets ordinary calls retain exact Tale ownership cheaply.
        /// </summary>
        internal static bool TryCompleteAnomalyStudy(
            object studyObject,
            Pawn studier,
            object categoryObject,
            AnomalyStudyFacts before,
            out AnomalyStudyFacts committed,
            out bool transitioned)
        {
            committed = null;
            transitioned = false;
            if (!ModsConfig.AnomalyActive || before == null || studier == null) return false;

            CompStudyUnlocks study = studyObject as CompStudyUnlocks;
            Thing studied = study?.parent;
            if (study == null || studied?.def == null
                || !string.Equals(studied.GetUniqueLoadID(), before.studiedEntityId,
                    StringComparison.Ordinal)
                || !string.Equals(studied.def.defName, before.studiedDefName,
                    StringComparison.Ordinal)
                || !string.Equals(studier.GetUniqueLoadID(), before.studierPawnId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            int newProgress = study.Progress;
            bool completedAfter = study.Completed;
            Building_VoidMonolith monolith = studied as Building_VoidMonolith;
            bool activatableAfter = CanActivateMonolith(monolith);
            transitioned = newProgress > before.oldProgress
                || (before.isMonolith && !before.monolithActivatableBefore && activatableAfter);

            // Ordinary callbacks need no DTO beyond the prefix state. Exact-job claims are already
            // retained by the bounded cache, so avoid a second allocation until policy has a real
            // transition to inspect.
            if (!transitioned) return true;

            committed = new AnomalyStudyFacts
            {
                studiedEntityId = before.studiedEntityId,
                studiedDefName = before.studiedDefName,
                studierPawnId = before.studierPawnId,
                studyJobId = before.studyJobId,
                tick = before.tick,
                oldProgress = before.oldProgress,
                newProgress = newProgress,
                noteThresholdsCrossed = Math.Max(0, newProgress - before.oldProgress),
                completedBefore = before.completedBefore,
                completedAfter = completedAfter,
                isMonolith = monolith != null,
                monolithActivatableBefore = before.monolithActivatableBefore,
                monolithActivatableAfter = activatableAfter
            };

            KnowledgeCategoryDef category = categoryObject as KnowledgeCategoryDef
                ?? studied.TryGetComp<CompStudiable>()?.KnowledgeCategory;
            EntityCodexEntryDef codex = studied.def.entityCodexEntry;
            bool codexVisible = codex != null && codex.Discovered;
            committed.studiedLabel = DiaryLineCleaner.CleanLine(studied.LabelShortCap);
            committed.codexEntryDefName = codexVisible ? codex.defName : string.Empty;
            committed.codexEntryLabel = codexVisible
                ? DiaryLineCleaner.CleanLine(codex.LabelCap.Resolve()) : string.Empty;
            committed.knowledgeCategoryDefName = category?.defName ?? string.Empty;
            committed.knowledgeCategoryLabel = category == null
                ? string.Empty : DiaryLineCleaner.CleanLine(category.LabelCap.Resolve());
            committed.studierEligible = DiaryGameComponent.IsDiaryEligible(studier);
            committed.isContainedEntity = IsAnomalyEntityContained(studied);
            // This is ordinary visible surroundings (room/weather/caravan context), not hidden
            // Anomaly state. It is captured at the committed callback, before any LLM queue runs.
            committed.setting = DiaryContextBuilder.BuildSurroundingsSummary(studier);
            return true;
        }

        /// <summary>Copies the exact two-pawn identity from a vanilla StudiedEntity Tale.</summary>
        internal static bool TryCaptureAnomalyStudiedTale(
            Pawn studier,
            Pawn studied,
            out AnomalyStudiedTaleFacts facts)
        {
            facts = null;
            if (!ModsConfig.AnomalyActive || studier == null || studied?.def == null) return false;
            facts = new AnomalyStudiedTaleFacts
            {
                studierPawnId = studier.GetUniqueLoadID(),
                studiedEntityId = studied.GetUniqueLoadID(),
                studiedDefName = studied.def.defName,
                studyJobId = studier.CurJob?.GetUniqueLoadID() ?? string.Empty,
                tick = Find.TickManager?.TicksGame ?? 0
            };
            return !string.IsNullOrWhiteSpace(facts.studierPawnId)
                && !string.IsNullOrWhiteSpace(facts.studiedEntityId);
        }

        /// <summary>Resolves a saved exact researcher ID only for bounded monolith context naming.</summary>
        internal static string AnomalyResearcherLabel(string pawnId)
        {
            if (!ModsConfig.AnomalyActive || string.IsNullOrWhiteSpace(pawnId)) return string.Empty;
            IEnumerable<Pawn> pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;
            if (pawns != null)
            {
                foreach (Pawn pawn in pawns)
                {
                    if (pawn != null && string.Equals(
                        pawn.GetUniqueLoadID(), pawnId, StringComparison.Ordinal))
                    {
                        return DiaryLineCleaner.CleanLine(pawn.LabelShortCap);
                    }
                }
            }

            if (Find.WorldPawns?.AllPawnsAlive != null)
            {
                foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive)
                {
                    if (pawn != null && string.Equals(
                        pawn.GetUniqueLoadID(), pawnId, StringComparison.Ordinal))
                    {
                        return DiaryLineCleaner.CleanLine(pawn.LabelShortCap);
                    }
                }
            }

            return string.Empty;
        }

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

        private static bool IsAnomalyEntityContained(Thing studied)
        {
            Pawn pawn = studied as Pawn;
            CompHoldingPlatformTarget target = pawn?.TryGetComp<CompHoldingPlatformTarget>();
            return target != null && target.CurrentlyHeldOnPlatform;
        }

        private static bool CanActivateMonolith(Building_VoidMonolith monolith)
        {
            if (monolith == null) return false;
            string reason;
            string reasonShort;
            return monolith.CanActivate(out reason, out reasonShort);
        }
    }
}

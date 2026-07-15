// Biotech Phase 2 family continuity. This component persists detached arcs, observes exact family
// activity before ordinary page-generation gates, selects a truthful growth supporter, and performs
// coarse retention. All live DLC reads remain inside DlcContext; all decisions remain pure policies.
//
// New to C#/RimWorld? See AGENTS.md ("DLC-safety", "IExposable", and "architecture barriers").
using System;
using System.Collections.Generic;
using PawnDiary.Capture;
using RimWorld;
using Verse;

namespace PawnDiary
{
    public partial class DiaryGameComponent
    {
        private const int CurrentFamilyObservationVersion = 1;
        private const int MaximumTransientFamilyDedupRows = 256;

        private List<BiotechFamilyArcState> biotechFamilyArcs = new List<BiotechFamilyArcState>();
        private int familyObservationVersion;

        // PlayLog and accepted social memories can describe the same lesson. This transient cache
        // collapses the pair/kind inside the XML-owned window and is reset at every game/load boundary.
        private static readonly Dictionary<string, int> RecentBiotechFamilyActivities =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Observes exact parent-bearing pregnancy/labor state after SetParents commits.</summary>
        internal void ObserveBiotechFamilyHediff(HediffWithParents hediff)
        {
            if (!ModsConfig.BiotechActive || !GamePlaying || hediff?.def == null)
            {
                return;
            }

            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            string kind = FamilyArcPolicy.ClassifyFamilyHediff(hediff.def.defName, policy);
            FamilyHediffSnapshot snapshot;
            if (kind.Length == 0 || !DlcContext.TryCaptureFamilyHediff(hediff, kind, out snapshot))
            {
                return;
            }

            EnsureBiotechFamilyList();
            FamilyArcPolicy.ObserveFamilyHediff(
                biotechFamilyArcs,
                snapshot,
                policy.maximumSupporterRows);
        }

        /// <summary>
        /// Observes exact BabyPlay/Lesson interactions independently of teaching-page settings.
        /// </summary>
        internal void ObserveBiotechFamilyInteraction(Pawn initiator, Pawn recipient, string defName)
        {
            if (!ModsConfig.BiotechActive || !GamePlaying)
            {
                return;
            }

            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            string kind = FamilyArcPolicy.ClassifyInteraction(defName, policy);
            FamilyActivityObservation observation;
            if (kind.Length > 0
                && DlcContext.TryCaptureFamilyActivity(initiator, recipient, kind, out observation))
            {
                RecordBiotechFamilyActivity(observation, policy);
            }
        }

        /// <summary>
        /// Observes only accepted GaveLesson/WasTaught memories. The pure classifier determines which
        /// pawn is the adult and which is the child; conflicting or incomplete roles fail closed.
        /// </summary>
        internal void ObserveBiotechFamilyMemory(Pawn owner, Thought_Memory memory)
        {
            if (!ModsConfig.BiotechActive || !GamePlaying || owner == null || memory?.def == null
                || memory.otherPawn == null)
            {
                return;
            }

            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            string memoryKind = FamilyArcPolicy.ClassifyLessonMemory(memory.def.defName, policy);
            Pawn adult = memoryKind == BiotechFamilyMemoryKindTokens.AdultRememberedLesson
                ? owner
                : memoryKind == BiotechFamilyMemoryKindTokens.ChildRememberedLesson
                    ? memory.otherPawn
                    : null;
            Pawn child = memoryKind == BiotechFamilyMemoryKindTokens.AdultRememberedLesson
                ? memory.otherPawn
                : memoryKind == BiotechFamilyMemoryKindTokens.ChildRememberedLesson
                    ? owner
                    : null;
            FamilyActivityObservation observation;
            if (adult != null && child != null
                && DlcContext.TryCaptureFamilyActivity(
                    adult,
                    child,
                    BiotechFamilyActivityKindTokens.Lesson,
                    out observation))
            {
                RecordBiotechFamilyActivity(observation, policy);
            }
        }

        /// <summary>
        /// Ensures child identity/parent baselines, attaches the shared family key, and selects at most
        /// one currently eligible supporter for a canonical growth page.
        /// </summary>
        internal BiotechFamilyArcState PrepareBiotechGrowthFamily(
            Pawn child,
            GrowthMomentMutation mutation,
            out Pawn supporterPawn)
        {
            supporterPawn = null;
            if (!ModsConfig.BiotechActive || child == null || mutation == null)
            {
                return null;
            }

            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            BiotechFamilyArcState arc = EnsureBiotechFamilyArcForGrowth(child, mutation.childId, policy);
            if (arc == null)
            {
                return null;
            }

            mutation.familyArcId = arc.familyArcId;
            List<FamilySupportCandidate> candidates = new List<FamilySupportCandidate>();
            if (arc.supporters != null)
            {
                for (int i = 0; i < arc.supporters.Count; i++)
                {
                    FamilySupportObservationState saved = arc.supporters[i];
                    if (saved == null || string.IsNullOrWhiteSpace(saved.adultId)) continue;
                    Pawn adult = FindLivePawnByLoadId(saved.adultId);
                    candidates.Add(new FamilySupportCandidate
                    {
                        adultId = saved.adultId,
                        displayName = adult == null
                            ? saved.lastDisplayName ?? string.Empty
                            : DiaryLineCleaner.CleanLine(adult.LabelShortCap),
                        relationToken = saved.relationToken,
                        lessonCount = saved.lessonCount,
                        babyPlayCount = saved.babyPlayCount,
                        careCount = saved.careCount,
                        unsummarizedEvidenceCount = FamilyArcPolicy.UnsummarizedEvidence(saved),
                        lastObservedTick = saved.lastObservedTick,
                        eligible = IsDiaryEligible(adult),
                        sameMap = adult != null && child.MapHeld != null && adult.MapHeld == child.MapHeld
                    });
                }
            }

            mutation.supporter = FamilySupportPolicy.Select(candidates, policy);
            if (mutation.supporter != null)
            {
                supporterPawn = FindLivePawnByLoadId(mutation.supporter.adultId);
            }
            return arc;
        }

        /// <summary>Gets/creates a stable child family key early in the birthday call stack.</summary>
        internal string EnsureBiotechGrowthFamilyArcId(Pawn child, string childId)
        {
            if (!ModsConfig.BiotechActive || child == null)
            {
                return string.Empty;
            }

            BiotechFamilyArcState arc = EnsureBiotechFamilyArcForGrowth(
                child,
                childId,
                DiaryBiotechPolicy.Snapshot());
            return arc?.familyArcId ?? string.Empty;
        }

        /// <summary>Marks current family observations consumed by this growth page or suppressed owner.</summary>
        internal void MarkBiotechGrowthFamilySummarized(BiotechFamilyArcState arc, int age)
        {
            FamilyArcPolicy.MarkGrowthSummarized(
                arc,
                age,
                Find.TickManager?.TicksGame ?? 0);
        }

        /// <summary>Appends a shared family ID to an already-captured pregnancy/labor event context.</summary>
        internal string AppendBiotechFamilyContext(Hediff hediff, string gameContext)
        {
            if (!ModsConfig.BiotechActive || hediff == null || biotechFamilyArcs == null)
            {
                return gameContext ?? string.Empty;
            }

            string hediffId = hediff.GetUniqueLoadID();
            for (int i = 0; i < biotechFamilyArcs.Count; i++)
            {
                BiotechFamilyArcState arc = biotechFamilyArcs[i];
                if (arc == null || (arc.pregnancyHediffId != hediffId && arc.laborHediffId != hediffId))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(DiaryContextFields.Value(
                    gameContext,
                    BiotechContextKeys.FamilyArcId)))
                {
                    return gameContext ?? string.Empty;
                }

                string prefix = string.IsNullOrWhiteSpace(gameContext)
                    ? string.Empty
                    : gameContext.Trim().TrimEnd(';') + "; ";
                return prefix + BiotechContextKeys.FamilyArcId + "="
                    + BiotechContextText.Clean(arc.familyArcId);
            }

            return gameContext ?? string.Empty;
        }

        /// <summary>Exposes additive family state and normalizes it after all deep rows load.</summary>
        private void ExposeBiotechFamilyData()
        {
            Scribe_Collections.Look(
                ref biotechFamilyArcs,
                BiotechSaveKeys.FamilyArcs,
                LookMode.Deep);
            Scribe_Values.Look(
                ref familyObservationVersion,
                BiotechSaveKeys.FamilyObservationVersion,
                0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
                int now = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
                biotechFamilyArcs = FamilyArcPolicy.Normalize(
                    biotechFamilyArcs,
                    now,
                    policy.maximumSupporterRows);
                familyObservationVersion = Math.Max(0,
                    Math.Min(CurrentFamilyObservationVersion, familyObservationVersion));
            }
        }

        /// <summary>Clears saved family state for a brand-new colony.</summary>
        private void ResetBiotechFamilyForNewGame()
        {
            biotechFamilyArcs = new List<BiotechFamilyArcState>();
            familyObservationVersion = CurrentFamilyObservationVersion;
            RecentBiotechFamilyActivities.Clear();
        }

        /// <summary>Clears transient cross-source activity dedup without touching loaded arcs.</summary>
        private static void ResetBiotechFamilyTransientState()
        {
            RecentBiotechFamilyActivities.Clear();
        }

        /// <summary>
        /// Silently seeds exact living-player child/parent baselines when an older save has no family
        /// observation version. It never invents pregnancy, birther, lesson, or birth facts.
        /// </summary>
        private void BootstrapBiotechFamilyArcsForLoadedSave()
        {
            if (familyObservationVersion >= CurrentFamilyObservationVersion
                || !ModsConfig.BiotechActive)
            {
                return;
            }

            EnsureBiotechFamilyList();
            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            IEnumerable<Pawn> pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;
            if (pawns != null)
            {
                foreach (Pawn pawn in pawns)
                {
                    FamilyChildSnapshot child;
                    if (DlcContext.TryCaptureFamilyChild(pawn, out child))
                    {
                        FamilyArcPolicy.EnsureChildArc(
                            biotechFamilyArcs,
                            child,
                            policy.maximumSupporterRows);
                    }
                }
            }

            familyObservationVersion = CurrentFamilyObservationVersion;
        }

        /// <summary>Runs bounded family compaction/removal on the existing coarse progression cadence.</summary>
        private void MaintainBiotechFamilyArcs()
        {
            if (biotechFamilyArcs == null || biotechFamilyArcs.Count == 0)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            biotechFamilyArcs = FamilyArcPolicy.Normalize(
                biotechFamilyArcs,
                now,
                policy.maximumSupporterRows);
            for (int i = biotechFamilyArcs.Count - 1; i >= 0; i--)
            {
                BiotechFamilyArcState arc = biotechFamilyArcs[i];
                Pawn child = FindLivePawnByLoadId(arc.childId);
                FamilyArcRetentionAction action = FamilyArcPolicy.DecideRetention(
                    arc,
                    new FamilyArcRetentionInput
                    {
                        childAliveAndDeveloping = child != null && !child.Dead
                            && (child.DevelopmentalStage == DevelopmentalStage.Baby
                                || child.DevelopmentalStage == DevelopmentalStage.Child),
                        hasPendingReference = PendingGrowthReferencesFamilyArc(arc.familyArcId),
                        hasSavedEventReference = SavedDiaryReferencesFamilyArc(arc.familyArcId)
                    },
                    now,
                    policy.familyArcRetentionTicks);
                if (action == FamilyArcRetentionAction.Compact)
                {
                    FamilyArcPolicy.Compact(arc);
                }
                else if (action == FamilyArcRetentionAction.Remove)
                {
                    biotechFamilyArcs.RemoveAt(i);
                }
            }
        }

        private BiotechFamilyArcState EnsureBiotechFamilyArcForGrowth(
            Pawn child,
            string childId,
            BiotechPolicySnapshot policy)
        {
            EnsureBiotechFamilyList();
            FamilyChildSnapshot snapshot;
            if (!DlcContext.TryCaptureFamilyChild(child, out snapshot))
            {
                string stableId = string.IsNullOrWhiteSpace(childId)
                    ? child.GetUniqueLoadID()
                    : childId.Trim();
                snapshot = new FamilyChildSnapshot
                {
                    childId = stableId,
                    childName = DiaryLineCleaner.CleanLine(child.LabelShortCap),
                    observedTick = Find.TickManager?.TicksGame ?? 0
                };
            }

            return FamilyArcPolicy.EnsureChildArc(
                biotechFamilyArcs,
                snapshot,
                policy?.maximumSupporterRows ?? 12);
        }

        private void RecordBiotechFamilyActivity(
            FamilyActivityObservation observation,
            BiotechPolicySnapshot policy)
        {
            if (observation == null) return;
            int now = Math.Max(0, observation.observedTick);
            string key = observation.kindToken + "|" + observation.adultId + "|" + observation.childId;
            int lastTick;
            int window = Math.Max(0, policy?.familyActivityPairDedupTicks ?? 2500);
            if (RecentBiotechFamilyActivities.TryGetValue(key, out lastTick)
                && now >= lastTick && now - lastTick < window)
            {
                return;
            }

            RecentBiotechFamilyActivities[key] = now;
            PruneTransientFamilyDedup(now, window);
            EnsureBiotechFamilyList();
            FamilyArcPolicy.ObserveActivity(
                biotechFamilyArcs,
                observation,
                policy?.maximumSupporterRows ?? 12);
        }

        private static void PruneTransientFamilyDedup(int now, int window)
        {
            if (RecentBiotechFamilyActivities.Count <= MaximumTransientFamilyDedupRows) return;
            string oldestKey = null;
            int oldestTick = int.MaxValue;
            List<string> expired = new List<string>();
            foreach (KeyValuePair<string, int> pair in RecentBiotechFamilyActivities)
            {
                if (now >= pair.Value && now - pair.Value >= Math.Max(1, window)) expired.Add(pair.Key);
                if (pair.Value < oldestTick)
                {
                    oldestTick = pair.Value;
                    oldestKey = pair.Key;
                }
            }
            for (int i = 0; i < expired.Count; i++) RecentBiotechFamilyActivities.Remove(expired[i]);
            if (RecentBiotechFamilyActivities.Count > MaximumTransientFamilyDedupRows && oldestKey != null)
            {
                RecentBiotechFamilyActivities.Remove(oldestKey);
            }
        }

        private bool PendingGrowthReferencesFamilyArc(string familyArcId)
        {
            if (string.IsNullOrWhiteSpace(familyArcId) || pendingBiotechGrowthMoments == null) return false;
            for (int i = 0; i < pendingBiotechGrowthMoments.Count; i++)
            {
                if (pendingBiotechGrowthMoments[i]?.familyArcId == familyArcId) return true;
            }
            return false;
        }

        private bool SavedDiaryReferencesFamilyArc(string familyArcId)
        {
            if (string.IsNullOrWhiteSpace(familyArcId)) return false;
            IReadOnlyList<DiaryEvent> live = events.AllEvents;
            for (int i = 0; i < live.Count; i++)
            {
                if (DiaryContextFields.Value(live[i]?.gameContext, BiotechContextKeys.FamilyArcId) == familyArcId)
                {
                    return true;
                }
            }
            IReadOnlyList<ArchivedDiaryEntry> archived = archive.AllEntries;
            for (int i = 0; i < archived.Count; i++)
            {
                if (DiaryContextFields.Value(
                    archived[i]?.decorationGameContext,
                    BiotechContextKeys.FamilyArcId) == familyArcId)
                {
                    return true;
                }
            }
            return false;
        }

        private void EnsureBiotechFamilyList()
        {
            if (biotechFamilyArcs == null)
            {
                biotechFamilyArcs = new List<BiotechFamilyArcState>();
            }
        }
    }
}

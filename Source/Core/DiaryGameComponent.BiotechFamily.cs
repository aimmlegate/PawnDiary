// Biotech Phases 2–3 family continuity and canonical birth ownership. This component persists detached
// arcs, observes exact family activity before ordinary page-generation gates, selects truthful growth
// and birth writers, polls saved newborn naming, and performs coarse retention. All live DLC reads
// remain inside DlcContext; all decisions remain pure policies.
//
// New to C#/RimWorld? See AGENTS.md ("DLC-safety", "IExposable", and "architecture barriers").
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
        private const int CurrentFamilyObservationVersion = 1;
        private const int MaximumTransientFamilyDedupRows = 256;

        private List<BiotechFamilyArcState> biotechFamilyArcs = new List<BiotechFamilyArcState>();
        private List<PendingBiotechBirthState> pendingBiotechBirths =
            new List<PendingBiotechBirthState>();
        private int familyObservationVersion;
        private int nextBiotechBirthNamingPollTick;

        // PlayLog and accepted social memories can describe the same lesson. This transient cache
        // collapses the pair/kind inside the XML-owned window and is reset at every game/load boundary.
        private readonly Dictionary<string, int> RecentBiotechFamilyActivities =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Begins exact birth capture before vanilla emits nested Tale/Thought signals.</summary>
        internal BiotechBirthCallState BeginBiotechBirth(
            Pawn geneticMother,
            Thing birtherThing,
            Pawn father,
            Pawn doctor,
            bool ritualBirth,
            LordJob_Ritual ritualJob)
        {
            BirthMutationSnapshot snapshot;
            bool birtherAliveBefore;
            if (!ModsConfig.BiotechActive || !GamePlaying
                || !DlcContext.TryCaptureBirthStart(
                    geneticMother,
                    birtherThing,
                    father,
                    doctor,
                    ritualBirth,
                    out snapshot,
                    out birtherAliveBefore))
            {
                return null;
            }

            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            return new BiotechBirthCallState
            {
                snapshot = snapshot,
                birtherAliveBefore = birtherAliveBefore,
                birther = birtherThing as Pawn,
                geneticMother = geneticMother,
                father = father,
                correlationScope = BiotechBirthCorrelation.BeginBirth(ritualJob, policy)
            };
        }

        /// <summary>
        /// Commits exact birth facts/family state and either emits immediately or saves naming
        /// ownership. True means the canonical owner safely claimed mature nested signals.
        /// </summary>
        internal bool CompleteBiotechBirth(
            BiotechBirthCallState state,
            Thing result,
            Thing birtherThing,
            int positivityIndex)
        {
            Pawn child;
            if (state?.snapshot == null
                || !DlcContext.TryCompleteBirthSnapshot(
                    state.snapshot,
                    result,
                    birtherThing,
                    positivityIndex,
                    state.birtherAliveBefore,
                    out child))
            {
                return false;
            }

            EnsureBiotechFamilyList();
            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            BiotechFamilyArcState arc = FamilyArcPolicy.AttachBirth(
                biotechFamilyArcs,
                state.snapshot,
                policy.maximumSupporterRows,
                out _);
            if (arc == null)
            {
                return false;
            }

            state.snapshot.correlationId = "birth|" + state.snapshot.familyArcId;
            if (HasRecordedBiotechBirth(state.snapshot.familyArcId, state.snapshot.childId)
                || HasPendingBiotechBirth(state.snapshot.familyArcId))
            {
                // The first canonical owner already froze settings/writers. Consume only the repeated
                // mature rows even if the player changed settings before a modded duplicate call.
                return true;
            }

            BirthWriterSelection writers = BirthOwnershipPolicy.SelectWriters(state.snapshot, policy);
            bool canonicalEnabled = PawnDiaryMod.Settings != null
                && PawnDiaryMod.Settings.IsBiotechFamilyBirthEnabled(state.snapshot.ritualBirth);
            if (writers.writers.Count == 0 || !canonicalEnabled)
            {
                return false;
            }

            BirthWriterSelection resolvedWriters;
            List<Pawn> writerPawns = ResolveImmediateBirthWriters(writers, state, out resolvedWriters);
            if (writerPawns.Count == 0)
            {
                return false;
            }

            BirthEventContextSnapshot eventContext = CaptureBiotechBirthEventContext(
                state.snapshot,
                resolvedWriters,
                writerPawns,
                child);

            bool writerBecameUnavailable = writerPawns.Count < writers.writers.Count;
            for (int i = 0; i < writerPawns.Count; i++)
            {
                writerBecameUnavailable |= writerPawns[i] == null || writerPawns[i].Dead;
            }

            if (!state.snapshot.namingResolved && !writerBecameUnavailable)
            {
                EnsurePendingBiotechBirthList();
                pendingBiotechBirths.Add(new PendingBiotechBirthState
                {
                    snapshot = state.snapshot,
                    writers = writers,
                    eventContext = eventContext,
                    createdTick = Find.TickManager?.TicksGame ?? state.snapshot.birthTick
                });
                pendingBiotechBirths = PendingBiotechBirthPolicy.Normalize(
                    pendingBiotechBirths,
                    Find.TickManager?.TicksGame ?? state.snapshot.birthTick,
                    policy.maximumPendingBirthRows);
                return HasPendingBiotechBirth(state.snapshot.familyArcId);
            }

            return DispatchBiotechBirth(
                state.snapshot,
                resolvedWriters,
                writerPawns,
                child,
                eventContext,
                enabledAtBirth: true);
        }

        /// <summary>Begins exact miscarriage context before vanilla creates sibling memories.</summary>
        internal BiotechMiscarriageCallState BeginBiotechMiscarriage(Hediff_Pregnant hediff)
        {
            if (!ModsConfig.BiotechActive || !GamePlaying || hediff == null)
            {
                return null;
            }

            EnsureBiotechFamilyList();
            BiotechFamilyArcState arc = FamilyArcPolicy.FindArcByHediff(
                biotechFamilyArcs,
                hediff.GetUniqueLoadID());
            MiscarriageCorrelationScope scope = BiotechBirthCorrelation.BeginMiscarriage(
                arc,
                DiaryBiotechPolicy.Snapshot());
            return arc == null || scope == null
                ? null
                : new BiotechMiscarriageCallState { arc = arc, correlationScope = scope };
        }

        /// <summary>Closes the exact pregnancy arc only after vanilla completed Miscarry.</summary>
        internal void CompleteBiotechMiscarriage(BiotechMiscarriageCallState state)
        {
            if (state?.arc != null)
            {
                FamilyArcPolicy.ClosePregnancyLoss(
                    state.arc,
                    Find.TickManager?.TicksGame ?? state.arc.lastObservedTick);
            }
        }

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
            Scribe_Collections.Look(
                ref pendingBiotechBirths,
                BiotechSaveKeys.PendingBirths,
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
                pendingBiotechBirths = PendingBiotechBirthPolicy.Normalize(
                    pendingBiotechBirths,
                    now,
                    policy.maximumPendingBirthRows);
                familyObservationVersion = Math.Max(0,
                    Math.Min(CurrentFamilyObservationVersion, familyObservationVersion));
            }
        }

        /// <summary>Clears saved family state for a brand-new colony.</summary>
        private void ResetBiotechFamilyForNewGame()
        {
            biotechFamilyArcs = new List<BiotechFamilyArcState>();
            pendingBiotechBirths = new List<PendingBiotechBirthState>();
            familyObservationVersion = CurrentFamilyObservationVersion;
            nextBiotechBirthNamingPollTick = 0;
            RecentBiotechFamilyActivities.Clear();
            BiotechBirthCorrelation.Clear();
        }

        /// <summary>Clears transient cross-source activity dedup without touching loaded arcs.</summary>
        private void ResetBiotechFamilyTransientState()
        {
            RecentBiotechFamilyActivities.Clear();
            BiotechBirthCorrelation.Clear();
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
            if (!ModsConfig.BiotechActive || biotechFamilyArcs == null || biotechFamilyArcs.Count == 0)
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
                bool familyHediffStillPresent = BiotechFamilyHediffStillPresent(arc);
                if (!familyHediffStillPresent && arc.birthTick <= 0 && !arc.closed)
                {
                    // A missing Hediff alone proves only that the observed pregnancy ended. Exact
                    // miscarriage/termination routes set their own token before this silent fallback.
                    FamilyArcPolicy.CloseUnknown(arc, now);
                }
                FamilyArcRetentionAction action = FamilyArcPolicy.DecideRetention(
                    arc,
                    new FamilyArcRetentionInput
                    {
                        childAliveAndDeveloping = child != null && !child.Dead
                            && (child.DevelopmentalStage == DevelopmentalStage.Baby
                                || child.DevelopmentalStage == DevelopmentalStage.Child),
                        familyHediffStillPresent = familyHediffStillPresent,
                        hasPendingReference = PendingGrowthReferencesFamilyArc(arc.familyArcId)
                            || PendingBirthReferencesFamilyArc(arc.familyArcId),
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

        /// <summary>
        /// Returns whether the exact pregnancy/labor Hediff that opened an unresolved arc is still on
        /// its birthing pawn. This live Verse scan stays in the component; pure retention receives only
        /// the resulting boolean.
        /// </summary>
        private bool BiotechFamilyHediffStillPresent(BiotechFamilyArcState arc)
        {
            if (arc == null || string.IsNullOrWhiteSpace(arc.birtherId)
                || (string.IsNullOrWhiteSpace(arc.pregnancyHediffId)
                    && string.IsNullOrWhiteSpace(arc.laborHediffId)))
            {
                return false;
            }

            Pawn birther = FindLivePawnByLoadId(arc.birtherId);
            List<Hediff> hediffs = birther?.health?.hediffSet?.hediffs;
            if (hediffs == null)
            {
                return false;
            }

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                string hediffId = hediff == null ? string.Empty : hediff.GetUniqueLoadID();
                if (string.Equals(hediffId, arc.pregnancyHediffId, StringComparison.Ordinal)
                    || string.Equals(hediffId, arc.laborHediffId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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

        private void PruneTransientFamilyDedup(int now, int window)
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

        /// <summary>Polls saved canonical births on their XML-owned naming cadence.</summary>
        private void MaintainPendingBiotechBirths()
        {
            if (!ModsConfig.BiotechActive || pendingBiotechBirths == null
                || pendingBiotechBirths.Count == 0)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            BiotechPolicySnapshot policy = DiaryBiotechPolicy.Snapshot();
            pendingBiotechBirths = PendingBiotechBirthPolicy.Normalize(
                pendingBiotechBirths,
                now,
                policy.maximumPendingBirthRows);
            for (int i = pendingBiotechBirths.Count - 1; i >= 0; i--)
            {
                PendingBiotechBirthState pending = pendingBiotechBirths[i];
                BirthMutationSnapshot snapshot = pending?.snapshot;
                if (snapshot == null)
                {
                    pendingBiotechBirths.RemoveAt(i);
                    continue;
                }

                if (HasRecordedBiotechBirth(snapshot.familyArcId, snapshot.childId))
                {
                    pendingBiotechBirths.RemoveAt(i);
                    continue;
                }

                BirthChildNamingState naming;
                Pawn child;
                DlcContext.TryCaptureBirthChildNaming(snapshot.childId, out naming, out child);

                BirthWriterSelection resolvedWriters;
                List<Pawn> writerPawns = ResolvePendingBirthWriters(
                    pending.writers,
                    out resolvedWriters);
                if (!PendingBiotechBirthPolicy.ShouldFlush(
                    pending,
                    naming,
                    now,
                    policy.birthNamingGraceTicks,
                    writerPawns.Count))
                {
                    continue;
                }

                if (naming != null && naming.found)
                {
                    if (!string.IsNullOrWhiteSpace(naming.childName))
                    {
                        snapshot.currentChildName = naming.childName;
                    }
                    snapshot.namingDeadline = naming.namingDeadline;
                    snapshot.namingResolved = naming.namingDeadline == -1 || now >= naming.namingDeadline;
                }

                if (writerPawns.Count == 0)
                {
                    int since = Math.Max(snapshot.birthTick, pending.createdTick);
                    if (now >= since && now - since >= policy.birthNamingGraceTicks)
                    {
                        // The mature sources were already owned by the saved pending row, so there is
                        // nothing truthful left to replay. Drop only after a bounded recovery window.
                        pendingBiotechBirths.RemoveAt(i);
                        Log.WarningOnce(
                            "[Pawn Diary] A pending canonical birth lost every exact adult writer before "
                            + "naming could flush; the orphaned pending row was discarded.",
                            "PawnDiary.BiotechBirth.MissingWriter".GetHashCode());
                    }
                    continue;
                }

                if (DispatchBiotechBirth(
                    snapshot,
                    resolvedWriters,
                    writerPawns,
                    child,
                    pending.eventContext,
                    enabledAtBirth: true))
                {
                    pendingBiotechBirths.RemoveAt(i);
                    BiotechFamilyArcState arc = FindBiotechFamilyArc(snapshot.familyArcId);
                    if (arc != null)
                    {
                        arc.currentChildName = snapshot.currentChildName;
                        arc.namingResolved = snapshot.namingResolved;
                        arc.lastObservedTick = Math.Max(arc.lastObservedTick, now);
                    }
                }
            }
        }

        /// <summary>Runs the naming poll only after the configured elapsed interval.</summary>
        private void TickPendingBiotechBirths(int currentTick)
        {
            if (!ModsConfig.BiotechActive || currentTick < nextBiotechBirthNamingPollTick)
            {
                return;
            }

            int interval = Math.Max(1, DiaryBiotechPolicy.Snapshot().birthNamingPollTicks);
            nextBiotechBirthNamingPollTick = currentTick > int.MaxValue - interval
                ? int.MaxValue
                : currentTick + interval;
            MaintainPendingBiotechBirths();
        }

        private bool DispatchBiotechBirth(
            BirthMutationSnapshot snapshot,
            BirthWriterSelection writers,
            List<Pawn> writerPawns,
            Pawn child,
            BirthEventContextSnapshot eventContext,
            bool enabledAtBirth)
        {
            if (snapshot == null || writers?.writers == null || writerPawns == null
                || writers.writers.Count == 0 || writers.writers.Count != writerPawns.Count)
            {
                return false;
            }

            FamilyBirthEventData payload = new FamilyBirthEventData
            {
                PawnId = writers.writers[0].pawnId,
                Tick = snapshot.birthTick,
                FamilyArcId = snapshot.familyArcId,
                FirstWriterId = writers.writers[0].pawnId,
                SecondWriterId = writers.writers.Count > 1 ? writers.writers[1].pawnId : string.Empty,
                FirstWriterEligible = true,
                SecondWriterEligible = writers.writers.Count > 1,
                HasValidSnapshot = true,
                AlreadyRecorded = HasRecordedBiotechBirth(snapshot.familyArcId, snapshot.childId)
            };
            return Dispatch(new FamilyBirthSignal(
                payload,
                snapshot,
                writers,
                writerPawns,
                child,
                eventContext ?? CaptureBiotechBirthEventContext(
                    snapshot,
                    writers,
                    writerPawns,
                    child),
                enabledAtBirth));
        }

        /// <summary>
        /// Freezes prompt/display facts at the canonical birth boundary so a later naming flush cannot
        /// borrow future surroundings, continuity, pawn names, or handwriting state.
        /// </summary>
        private BirthEventContextSnapshot CaptureBiotechBirthEventContext(
            BirthMutationSnapshot snapshot,
            BirthWriterSelection writers,
            List<Pawn> writerPawns,
            Pawn child)
        {
            if (snapshot == null || writers?.writers == null || writerPawns == null
                || writers.writers.Count != writerPawns.Count)
            {
                return null;
            }

            IReadOnlyList<DiaryEvent> activeEvents = ActiveScanEvents();
            BirthEventContextSnapshot result = new BirthEventContextSnapshot
            {
                birthTick = Math.Max(0, snapshot.birthTick),
                birthDate = GenDate.DateFullStringAt(
                    Find.TickManager?.TicksAbs ?? 0,
                    UnityEngine.Vector2.zero)
            };
            bool pair = writerPawns.Count > 1;
            for (int i = 0; i < writerPawns.Count; i++)
            {
                Pawn pawn = writerPawns[i];
                BirthWriterFact writer = writers.writers[i];
                if (pawn == null || writer == null)
                {
                    continue;
                }

                Pawn otherWriter = pair ? writerPawns[i == 0 ? 1 : 0] : null;
                string soloContinuity = DiaryContextBuilder.BuildContinuitySummary(pawn, child, activeEvents);
                result.writers.Add(new BirthWriterContextSnapshot
                {
                    pawnId = writer.pawnId,
                    displayName = string.IsNullOrWhiteSpace(writer.displayName)
                        ? pawn.LabelShortCap
                        : writer.displayName,
                    pawnSummary = DiaryContextBuilder.BuildPawnSummary(pawn),
                    surroundings = DiaryContextBuilder.BuildSurroundingsSummary(pawn),
                    continuity = soloContinuity,
                    pairContinuity = pair
                        ? DiaryContextBuilder.BuildContinuitySummary(pawn, otherWriter, activeEvents)
                        : soloContinuity,
                    lastOpener = DiaryContextBuilder.LatestDiaryOpener(writer.pawnId, activeEvents),
                    previousEntryEnding = DiaryContextBuilder.LatestDiaryEnding(writer.pawnId, activeEvents),
                    weapon = DiaryContextBuilder.EquippedWeapon(pawn),
                    staggeredIntensity = PawnFactCapture.StaggeredIntensity(pawn),
                    textDecorationFacts = PawnFactCapture.TextDecorationFacts(pawn),
                    skipFirstPersonGeneration = ShouldSkipFirstPersonGenerationForIncapacitation(pawn)
                });
            }

            return result;
        }

        private List<Pawn> ResolveImmediateBirthWriters(
            BirthWriterSelection writers,
            BiotechBirthCallState state,
            out BirthWriterSelection resolved)
        {
            return ResolveBirthWriters(writers, id =>
            {
                if (state?.birther != null && state.birther.GetUniqueLoadID() == id) return state.birther;
                if (state?.geneticMother != null && state.geneticMother.GetUniqueLoadID() == id)
                    return state.geneticMother;
                if (state?.father != null && state.father.GetUniqueLoadID() == id) return state.father;
                return FindLivePawnByLoadId(id);
            }, out resolved);
        }

        private List<Pawn> ResolvePendingBirthWriters(
            BirthWriterSelection writers,
            out BirthWriterSelection resolved)
        {
            return ResolveBirthWriters(writers, FindLivePawnByLoadId, out resolved);
        }

        private static List<Pawn> ResolveBirthWriters(
            BirthWriterSelection writers,
            Func<string, Pawn> resolver,
            out BirthWriterSelection resolved)
        {
            resolved = new BirthWriterSelection();
            List<Pawn> pawns = new List<Pawn>();
            if (writers?.writers == null || resolver == null)
            {
                return pawns;
            }

            for (int i = 0; i < writers.writers.Count && pawns.Count < 2; i++)
            {
                BirthWriterFact writer = writers.writers[i];
                Pawn pawn = string.IsNullOrWhiteSpace(writer?.pawnId)
                    ? null
                    : resolver(writer.pawnId);
                if (pawn == null || !IsDiaryEligible(pawn))
                {
                    continue;
                }

                pawns.Add(pawn);
                resolved.writers.Add(new BirthWriterFact
                {
                    pawnId = writer.pawnId,
                    displayName = writer.displayName,
                    roleToken = writer.roleToken
                });
            }

            return pawns;
        }

        private bool HasPendingBiotechBirth(string familyArcId)
        {
            if (string.IsNullOrWhiteSpace(familyArcId) || pendingBiotechBirths == null)
            {
                return false;
            }

            for (int i = 0; i < pendingBiotechBirths.Count; i++)
            {
                if (pendingBiotechBirths[i]?.snapshot?.familyArcId == familyArcId)
                {
                    return true;
                }
            }
            return false;
        }

        private bool HasRecordedBiotechBirth(string familyArcId, string childId)
        {
            if (string.IsNullOrWhiteSpace(familyArcId) || string.IsNullOrWhiteSpace(childId))
            {
                return false;
            }

            IReadOnlyList<DiaryEvent> live = events.AllEvents;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                DiaryEvent diaryEvent = live[i];
                if (RecordedBiotechBirthMatches(
                    diaryEvent?.interactionDefName,
                    diaryEvent?.gameContext,
                    familyArcId,
                    childId))
                {
                    return true;
                }
            }

            IReadOnlyList<ArchivedDiaryEntry> archived = archive.AllEntries;
            for (int i = archived.Count - 1; i >= 0; i--)
            {
                ArchivedDiaryEntry entry = archived[i];
                if (RecordedBiotechBirthMatches(
                    entry?.interactionDefName,
                    entry?.decorationGameContext,
                    familyArcId,
                    childId))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool RecordedBiotechBirthMatches(
            string interactionDefName,
            string gameContext,
            string familyArcId,
            string childId)
        {
            return BirthRecordPolicy.Matches(
                interactionDefName,
                DiaryContextFields.Value(gameContext, BiotechContextKeys.FamilyArcId),
                DiaryContextFields.Value(gameContext, BiotechContextKeys.ChildId),
                familyArcId,
                childId);
        }

        private BiotechFamilyArcState FindBiotechFamilyArc(string familyArcId)
        {
            if (string.IsNullOrWhiteSpace(familyArcId) || biotechFamilyArcs == null)
            {
                return null;
            }

            for (int i = 0; i < biotechFamilyArcs.Count; i++)
            {
                if (biotechFamilyArcs[i]?.familyArcId == familyArcId)
                {
                    return biotechFamilyArcs[i];
                }
            }
            return null;
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

        private bool PendingBirthReferencesFamilyArc(string familyArcId)
        {
            return HasPendingBiotechBirth(familyArcId);
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

        private void EnsurePendingBiotechBirthList()
        {
            if (pendingBiotechBirths == null)
            {
                pendingBiotechBirths = new List<PendingBiotechBirthState>();
            }
        }
    }
}

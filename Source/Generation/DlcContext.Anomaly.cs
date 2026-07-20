// DLC-safe live Anomaly state capture. This adapter is the only place Pawn Diary reads monolith,
// study, containment, ghoul, or raw creepjoiner tracker/private-field state; it converts optional-
// DLC objects into detached primitive facts before pure policy, persistence, or prompts see them.
//
// New to C#/RimWorld? See AGENTS.md ("DLC-safety") and the DlcContext.cs file header.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using PawnDiary.Capture;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace PawnDiary
{
    /// <summary>
    /// Synchronous live edge-state for one Escape call. It exists only until the matching postfix or
    /// finalizer; the aggregation scope copies its plain facts and never exposes these objects to pure
    /// policy, persistence, or background generation.
    /// </summary>
    internal sealed class AnomalyContainmentEscapeCapture
    {
        internal CompHoldingPlatformTarget target;
        internal Pawn entity;
        internal Building_HoldingPlatform platform;
        internal Map map;
        internal int capturedTick;
        internal ContainedEntityFact entityFact;
        internal string preEjectionSetting = string.Empty;
        internal readonly List<AnomalyWriterCandidate> writerFacts =
            new List<AnomalyWriterCandidate>();
        internal readonly List<Pawn> writerPawns = new List<Pawn>();
        // The broader bounded roster lets nested platforms recompute distance/recent-study roles.
        internal readonly List<Pawn> candidatePawns = new List<Pawn>();
    }

    /// <summary>Independent private-field health for the three exact public creepjoiner hooks.</summary>
    internal sealed class CreepJoinerReflectionHealth
    {
        public bool rejectionReady;
        public bool aggressionReady;
        public bool departureReady;
    }

    /// <summary>
    /// Synchronous live edge-state retained only from one exact prefix to its postfix/finalizer.
    /// Pure code receives <see cref="facts"/>; no tracker/private value enters saves or prompts.
    /// </summary>
    internal sealed class CreepJoinerOutcomeCapture
    {
        internal Pawn_CreepJoinerTracker tracker;
        internal Pawn subject;
        internal readonly List<Pawn> writerPawns = new List<Pawn>();
        internal CreepJoinerOutcomeFacts facts;
        internal bool rejectionCommittedBefore;
        internal bool aggressionCommittedBefore;
        internal bool departureCommittedBefore;
        internal bool methodAllowedBefore;
        internal bool visibleResponseExpected;
        internal bool ownsRejectionScope;

        internal string SubjectPawnId => facts?.pawnId ?? string.Empty;

        internal Pawn ResolveWriter(string pawnId)
        {
            for (int i = 0; i < writerPawns.Count; i++)
            {
                Pawn pawn = writerPawns[i];
                if (pawn != null && string.Equals(
                    pawn.GetUniqueLoadID(), pawnId, StringComparison.Ordinal)) return pawn;
            }
            return null;
        }
    }

    /// <summary>
    /// Live references retained only across one synchronous surgical recipe. The pure facts contain
    /// generic disclosure booleans and exact visible roles; no StringBuilder text or tracker Def state
    /// is copied into this object.
    /// </summary>
    internal sealed class CreepJoinerSurgicalInspectionCapture
    {
        internal Pawn subject;
        internal Pawn surgeon;
        internal Pawn_CreepJoinerTracker tracker;
        internal CreepJoinerSurgicalDisclosureFacts facts;
        // CreepJoinerSurgicalInspectionScope owns this flag; the recipe finalizer reads it to avoid
        // closing an already-completed frame a second time.
        internal bool scopeClosed;

        internal Pawn ResolveWriter(string pawnId)
        {
            if (string.Equals(
                surgeon?.GetUniqueLoadID(), pawnId, StringComparison.Ordinal)) return surgeon;
            return string.Equals(
                subject?.GetUniqueLoadID(), pawnId, StringComparison.Ordinal) ? subject : null;
        }
    }

    /// <summary>Exact tracker call plus only the pre-call builder length, never its text.</summary>
    internal sealed class CreepJoinerSurgicalTrackerCallState
    {
        internal CreepJoinerSurgicalInspectionCapture capture;
        internal int builderLengthBefore;
    }

    /// <summary>
    /// Live references retained only across one synchronous ghoul-infusion recipe. The detached
    /// facts freeze pre-state and exact POV eligibility before vanilla mutates the subject.
    /// </summary>
    internal sealed class GhoulTransformationCapture
    {
        internal Pawn subject;
        internal Pawn surgeon;
        internal GhoulTransformationFacts facts;
        internal bool scopeClosed;

        internal Pawn ResolveWriter(string pawnId)
        {
            if (string.Equals(
                surgeon?.GetUniqueLoadID(), pawnId, StringComparison.Ordinal)) return surgeon;
            return string.Equals(
                subject?.GetUniqueLoadID(), pawnId, StringComparison.Ordinal) ? subject : null;
        }
    }

    /// <summary>Guarded Anomaly accessors for save baselines and exact study callbacks.</summary>
    internal static partial class DlcContext
    {
        private static bool creepJoinerReflectionResolved;
        private static Type creepJoinerTrackerType;
        private static FieldInfo creepJoinerTriggeredRejectionField;
        private static FieldInfo creepJoinerTriggeredAggressiveField;
        private static FieldInfo creepJoinerHasLeftField;

        /// <summary>Returns guarded live ghoul state, or false when Anomaly is unavailable.</summary>
        internal static bool IsGhoul(Pawn pawn)
        {
            return ModsConfig.AnomalyActive && pawn != null && pawn.IsGhoul;
        }

        /// <summary>Captures exact ghoul subject/surgeon pre-state before vanilla can mutate it.</summary>
        internal static bool TryCaptureGhoulTransformation(
            Pawn subject,
            Pawn surgeon,
            out GhoulTransformationCapture capture)
        {
            capture = null;
            if (!ModsConfig.AnomalyActive || subject == null || surgeon == null) return false;
            string subjectId = subject.GetUniqueLoadID();
            string surgeonId = surgeon.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(subjectId) || string.IsNullOrWhiteSpace(surgeonId))
                return false;
            capture = new GhoulTransformationCapture
            {
                subject = subject,
                surgeon = surgeon,
                facts = new GhoulTransformationFacts
                {
                    subjectPawnId = subjectId,
                    subjectLabel = DiaryLineCleaner.CleanLine(subject.LabelShortCap),
                    surgeonPawnId = surgeonId,
                    surgeonLabel = DiaryLineCleaner.CleanLine(surgeon.LabelShortCap),
                    tick = Find.TickManager?.TicksGame ?? 0,
                    wasGhoul = IsGhoul(subject),
                    playerVisible = true,
                    surgeonEligible = DiaryGameComponent.IsDiaryEligible(surgeon),
                    subjectEligible = DiaryGameComponent.IsDiaryEligible(subject)
                }
            };
            return true;
        }

        /// <summary>Clones detached facts and verifies post-state after a normal recipe return.</summary>
        internal static bool TryCompleteGhoulTransformation(
            Pawn subject,
            Pawn surgeon,
            GhoulTransformationCapture capture,
            out GhoulTransformationFacts facts)
        {
            facts = null;
            if (!ModsConfig.AnomalyActive || capture?.facts == null
                || !ReferenceEquals(subject, capture.subject)
                || !ReferenceEquals(surgeon, capture.surgeon)) return false;
            GhoulTransformationFacts source = capture.facts;
            facts = new GhoulTransformationFacts
            {
                subjectPawnId = source.subjectPawnId,
                subjectLabel = source.subjectLabel,
                surgeonPawnId = source.surgeonPawnId,
                surgeonLabel = source.surgeonLabel,
                tick = source.tick,
                methodReturnedNormally = true,
                wasGhoul = source.wasGhoul,
                isGhoul = IsGhoul(subject),
                playerVisible = source.playerVisible,
                surgeonEligible = source.surgeonEligible,
                subjectEligible = source.subjectEligible
            };
            return true;
        }

        /// <summary>
        /// Resolves the three private committed-transition markers once. Rejection also needs the
        /// aggression marker to recognize vanilla's nested AggressiveRejection truth; it does not
        /// depend on the unrelated departure marker.
        /// </summary>
        internal static CreepJoinerReflectionHealth ResolveCreepJoinerReflection(Type trackerType)
        {
            if (!creepJoinerReflectionResolved)
            {
                creepJoinerReflectionResolved = true;
                creepJoinerTrackerType = trackerType;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                creepJoinerTriggeredRejectionField = BooleanField(
                    trackerType, "triggeredRejection", flags);
                creepJoinerTriggeredAggressiveField = BooleanField(
                    trackerType, "triggeredAggressive", flags);
                creepJoinerHasLeftField = BooleanField(trackerType, "hasLeft", flags);
            }

            bool sameType = trackerType != null && trackerType == creepJoinerTrackerType;
            return new CreepJoinerReflectionHealth
            {
                rejectionReady = sameType && creepJoinerTriggeredRejectionField != null
                    && creepJoinerTriggeredAggressiveField != null,
                aggressionReady = sameType && creepJoinerTriggeredAggressiveField != null,
                departureReady = sameType && creepJoinerHasLeftField != null
            };
        }

        /// <summary>Captures the exact patient and surgeon at the outer recipe boundary.</summary>
        internal static bool TryCaptureCreepJoinerSurgicalInspection(
            Pawn subject,
            Pawn surgeon,
            out CreepJoinerSurgicalInspectionCapture capture)
        {
            capture = null;
            Pawn_CreepJoinerTracker tracker = subject?.creepjoiner;
            if (!ModsConfig.AnomalyActive || subject == null || surgeon == null || tracker == null
                || tracker.GetType() != creepJoinerTrackerType
                || !ReferenceEquals(tracker.Pawn, subject)) return false;
            string subjectId = subject.GetUniqueLoadID();
            string surgeonId = surgeon.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(subjectId) || string.IsNullOrWhiteSpace(surgeonId))
                return false;

            capture = new CreepJoinerSurgicalInspectionCapture
            {
                subject = subject,
                surgeon = surgeon,
                tracker = tracker,
                facts = new CreepJoinerSurgicalDisclosureFacts
                {
                    subjectPawnId = subjectId,
                    subjectLabel = DiaryLineCleaner.CleanLine(subject.LabelShortCap),
                    surgeonPawnId = surgeonId,
                    surgeonLabel = DiaryLineCleaner.CleanLine(surgeon.LabelShortCap),
                    tick = Find.TickManager?.TicksGame ?? 0,
                    surgeonEligible = DiaryGameComponent.IsDiaryEligible(surgeon),
                    subjectEligible = DiaryGameComponent.IsDiaryEligible(subject)
                }
            };
            return true;
        }

        /// <summary>
        /// Correlates the tracker call to the active outer recipe and retains only builder length.
        /// </summary>
        internal static bool TryCaptureCreepJoinerSurgicalTrackerCall(
            object trackerObject,
            Pawn surgeon,
            StringBuilder builder,
            CreepJoinerSurgicalInspectionCapture capture,
            out CreepJoinerSurgicalTrackerCallState state)
        {
            state = null;
            Pawn_CreepJoinerTracker tracker = trackerObject as Pawn_CreepJoinerTracker;
            if (!ModsConfig.AnomalyActive || capture?.facts == null || tracker == null
                || builder == null || !ReferenceEquals(tracker, capture.tracker)
                || !ReferenceEquals(tracker.Pawn, capture.subject)
                || !ReferenceEquals(surgeon, capture.surgeon)) return false;
            state = new CreepJoinerSurgicalTrackerCallState
            {
                capture = capture,
                builderLengthBefore = builder.Length
            };
            return true;
        }

        /// <summary>Marks generic disclosure proof only when the exact tracker appended and returned true.</summary>
        internal static void CompleteCreepJoinerSurgicalTrackerCall(
            object trackerObject,
            Pawn surgeon,
            StringBuilder builder,
            bool result,
            CreepJoinerSurgicalTrackerCallState state)
        {
            CreepJoinerSurgicalInspectionCapture capture = state?.capture;
            Pawn_CreepJoinerTracker tracker = trackerObject as Pawn_CreepJoinerTracker;
            if (!ModsConfig.AnomalyActive || capture?.facts == null || tracker == null
                || builder == null || !result || builder.Length <= state.builderLengthBefore
                || !ReferenceEquals(tracker, capture.tracker)
                || !ReferenceEquals(tracker.Pawn, capture.subject)
                || !ReferenceEquals(surgeon, capture.surgeon)) return;
            capture.facts.trackerDisclosureAppended = true;
        }

        /// <summary>
        /// Records whether the whole Pawn inspection returned the letter-visible Detected result. A
        /// different inspectable hediff may force DetectedNoLetter, which must not authorize a page.
        /// </summary>
        internal static void ObserveCreepJoinerSurgicalInspectionResult(
            Pawn subject,
            Pawn surgeon,
            SurgicalInspectionOutcome outcome,
            CreepJoinerSurgicalInspectionCapture capture)
        {
            if (!ModsConfig.AnomalyActive || capture?.facts == null
                || !ReferenceEquals(subject, capture.subject)
                || !ReferenceEquals(surgeon, capture.surgeon)) return;
            // Vanilla calls this once. If a mod re-enters it, the most recent whole-Pawn result owns
            // visibility because that is the result the outer recipe ultimately observes.
            capture.facts.playerVisible = capture.facts.trackerDisclosureAppended
                && outcome == SurgicalInspectionOutcome.Detected;
        }

        /// <summary>Clones detached facts after the exact recipe returns normally.</summary>
        internal static bool TryCompleteCreepJoinerSurgicalInspection(
            Pawn subject,
            Pawn surgeon,
            CreepJoinerSurgicalInspectionCapture capture,
            out CreepJoinerSurgicalDisclosureFacts facts)
        {
            facts = null;
            if (!ModsConfig.AnomalyActive || capture?.facts == null
                || !ReferenceEquals(subject, capture.subject)
                || !ReferenceEquals(surgeon, capture.surgeon)
                || !ReferenceEquals(subject?.creepjoiner, capture.tracker)) return false;
            CreepJoinerSurgicalDisclosureFacts source = capture.facts;
            facts = new CreepJoinerSurgicalDisclosureFacts
            {
                subjectPawnId = source.subjectPawnId,
                subjectLabel = source.subjectLabel,
                surgeonPawnId = source.surgeonPawnId,
                surgeonLabel = source.surgeonLabel,
                tick = source.tick,
                surgeryCompleted = true,
                trackerDisclosureAppended = source.trackerDisclosureAppended,
                playerVisible = source.playerVisible,
                surgeonEligible = source.surgeonEligible,
                subjectEligible = source.subjectEligible
            };
            return true;
        }

        /// <summary>Copies exact ordinary DidSurgery Tale role identities for pure ownership matching.</summary>
        internal static bool TryCaptureAnomalySurgeryTale(
            TaleDef def,
            object[] args,
            int tick,
            out AnomalySurgeryTaleFacts facts)
        {
            facts = null;
            if (!ModsConfig.AnomalyActive || def == null || args == null || args.Length != 2)
                return false;
            Pawn first = args[0] as Pawn;
            Pawn second = args[1] as Pawn;
            if (first == null || second == null) return false;
            facts = new AnomalySurgeryTaleFacts
            {
                taleDefName = def.defName ?? string.Empty,
                firstPawnId = first.GetUniqueLoadID(),
                secondPawnId = second.GetUniqueLoadID(),
                tick = tick
            };
            return true;
        }

        /// <summary>Captures exact event-time facts and a bounded writer roster before vanilla mutates.</summary>
        internal static bool TryCaptureCreepJoinerOutcomeBefore(
            object trackerObject,
            string phase,
            int witnessRadius,
            out CreepJoinerOutcomeCapture capture)
        {
            capture = null;
            Pawn_CreepJoinerTracker tracker = trackerObject as Pawn_CreepJoinerTracker;
            Pawn subject = tracker?.Pawn;
            if (!ModsConfig.AnomalyActive || tracker == null || subject == null
                || tracker.GetType() != creepJoinerTrackerType
                || !CreepJoinerPhaseTokens.IsOutcome(phase)) return false;

            bool rejectionBefore;
            bool aggressionBefore;
            bool departureBefore;
            if (!TryReadCreepJoinerFlags(
                tracker, phase, out rejectionBefore, out aggressionBefore, out departureBefore))
                return false;

            string subjectId = subject.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(subjectId)) return false;
            CreepJoinerOutcomeCapture result = new CreepJoinerOutcomeCapture
            {
                tracker = tracker,
                subject = subject,
                facts = new CreepJoinerOutcomeFacts
                {
                    pawnId = subjectId,
                    subjectLabel = DiaryLineCleaner.CleanLine(subject.LabelShortCap),
                    phase = phase,
                    visibleResultToken = CreepJoinerVisibleResultTokens.ForPhase(phase),
                    tick = Find.TickManager?.TicksGame ?? 0,
                    joinedBefore = tracker.joinedTick > 0
                        || (subject.IsColonist && subject.Faction?.IsPlayer == true),
                    subjectEligibleBefore = DiaryGameComponent.IsDiaryEligible(subject)
                },
                rejectionCommittedBefore = rejectionBefore,
                aggressionCommittedBefore = aggressionBefore,
                departureCommittedBefore = departureBefore
            };

            if (phase == AnomalyOutcomeTokens.Rejected)
            {
                result.methodAllowedBefore = !tracker.Disabled && !rejectionBefore
                    && !aggressionBefore && !departureBefore;
                // Installed rejection defs visibly report via a letter. A modded silent response does
                // not get promoted merely because its private marker changed.
                result.visibleResponseExpected = tracker.rejection?.hasLetter == true;
            }
            else if (phase == AnomalyOutcomeTokens.Aggressive)
            {
                result.methodAllowedBefore = tracker.CanTriggerAggressive;
                result.visibleResponseExpected = tracker.aggressive != null
                    && (tracker.aggressive.hasMessage || tracker.aggressive.hasLetter);
            }
            else
            {
                result.methodAllowedBefore = !tracker.Disabled && !departureBefore;
                result.visibleResponseExpected = true;
            }

            // A disallowed/no-op call can still be compared after vanilla returns so an unexpected
            // private-marker commit becomes a silent replay barrier. It can never emit a page, so do
            // not pay for the event-time map scan or retain writer references.
            if (!result.methodAllowedBefore)
            {
                capture = result;
                return true;
            }

            Dictionary<string, Pawn> pawnsById = new Dictionary<string, Pawn>(StringComparer.Ordinal);
            AddCreepJoinerWriter(result, pawnsById, subject, exactSpeaker: false, isSubject: true,
                onMap: subject.Spawned, nearby: subject.Spawned, distanceSquared: 0);
            Pawn speaker = tracker.speaker;
            AddCreepJoinerWriter(result, pawnsById, speaker, exactSpeaker: true,
                isSubject: ReferenceEquals(speaker, subject),
                onMap: speaker != null && speaker.Spawned && speaker.Map == subject.Map,
                nearby: false,
                distanceSquared: speaker != null && speaker.Spawned && subject.Spawned
                    && speaker.Map == subject.Map
                    ? (speaker.Position - subject.Position).LengthHorizontalSquared : -1);

            Map map = subject.MapHeld;
            IReadOnlyList<Pawn> mapPawns = map?.mapPawns?.AllPawnsSpawned;
            int inspected = Math.Min(
                mapPawns?.Count ?? 0,
                AnomalyPolicyLimits.MaximumCreepJoinerCandidateInputs);
            long radiusSquared = (long)Math.Max(1, witnessRadius) * Math.Max(1, witnessRadius);
            for (int i = 0; i < inspected; i++)
            {
                Pawn candidate = mapPawns[i];
                if (candidate == null || candidate.Map != map || !candidate.Spawned
                    || !DiaryGameComponent.IsDiaryEligible(candidate)) continue;
                int distanceSquared = (candidate.Position - subject.Position).LengthHorizontalSquared;
                AddCreepJoinerWriter(
                    result,
                    pawnsById,
                    candidate,
                    exactSpeaker: ReferenceEquals(candidate, speaker),
                    isSubject: ReferenceEquals(candidate, subject),
                    onMap: true,
                    nearby: distanceSquared >= 0 && distanceSquared <= radiusSquared,
                    distanceSquared: distanceSquared);
            }

            capture = result;
            return true;
        }

        /// <summary>Verifies the private marker and visible post-state after normal vanilla return.</summary>
        internal static bool TryCompleteCreepJoinerOutcome(
            object trackerObject,
            CreepJoinerOutcomeCapture capture,
            out CreepJoinerOutcomeFacts facts)
        {
            facts = null;
            Pawn_CreepJoinerTracker tracker = trackerObject as Pawn_CreepJoinerTracker;
            if (!ModsConfig.AnomalyActive || capture?.facts == null || tracker == null
                || !ReferenceEquals(tracker, capture.tracker)
                || !ReferenceEquals(tracker.Pawn, capture.subject)
                || !string.Equals(
                    tracker.Pawn?.GetUniqueLoadID(), capture.facts.pawnId, StringComparison.Ordinal))
            {
                return false;
            }

            bool rejectionAfter;
            bool aggressionAfter;
            bool departureAfter;
            if (!TryReadCreepJoinerFlags(
                tracker, capture.facts.phase,
                out rejectionAfter, out aggressionAfter, out departureAfter)) return false;

            CreepJoinerOutcomeFacts committed = capture.facts;
            committed.nestedOutcomeOwnedByRejection = !capture.ownsRejectionScope
                && CreepJoinerOutcomeScope.OwnsRejection(committed.pawnId);
            if (committed.phase == AnomalyOutcomeTokens.Rejected)
            {
                committed.transitioned = !capture.rejectionCommittedBefore && rejectionAfter;
                committed.transitionVerified = capture.methodAllowedBefore && committed.transitioned;
                if (committed.transitionVerified
                    && !capture.aggressionCommittedBefore && aggressionAfter)
                {
                    // Vanilla AggressiveRejection visibly performs DoAggressive inside DoRejection.
                    // Keep one outer-owned page, but save and narrate the strongest visible terminal
                    // state instead of permanently describing an attacking pawn as merely rejected.
                    committed.phase = AnomalyOutcomeTokens.Aggressive;
                    committed.visibleResultToken = CreepJoinerVisibleResultTokens.Hostile;
                    committed.aggressionFollowedRejection = true;
                }
            }
            else if (committed.phase == AnomalyOutcomeTokens.Aggressive)
            {
                committed.transitioned = !capture.aggressionCommittedBefore && aggressionAfter;
                committed.transitionVerified = capture.methodAllowedBefore && committed.transitioned;
            }
            else
            {
                committed.transitioned = !capture.departureCommittedBefore && departureAfter;
                Pawn pawn = tracker.Pawn;
                bool leftPlayer = pawn != null && pawn.Faction?.IsPlayer != true && !pawn.IsColonist;
                bool exiting = pawn?.GetLord()?.LordJob is LordJob_ExitMapBest;
                committed.transitionVerified = capture.methodAllowedBefore && committed.transitioned
                    && leftPlayer && exiting;
            }

            committed.playerVisible = capture.visibleResponseExpected;
            facts = committed;
            return true;
        }

        /// <summary>Captures current joined player creepjoiners for a silent one-time A2 baseline.</summary>
        internal static CreepJoinerLegacyBaselineFacts CaptureCreepJoinerLegacyBaseline()
        {
            CreepJoinerLegacyBaselineFacts facts = new CreepJoinerLegacyBaselineFacts
            {
                anomalyAvailable = ModsConfig.AnomalyActive
            };
            if (!facts.anomalyAvailable) return facts;

            IEnumerable<Pawn> pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive;
            if (pawns == null) return facts;
            int inspected = 0;
            foreach (Pawn pawn in pawns)
            {
                if (inspected++ >= AnomalyPolicyLimits.MaximumCreepJoinerArcInputs) break;
                try
                {
                    Pawn_CreepJoinerTracker tracker = pawn?.creepjoiner;
                    if (pawn == null || tracker == null || !pawn.IsCreepJoiner
                        || pawn.Faction?.IsPlayer != true || !pawn.IsColonist
                        || tracker.joinedTick < 0) continue;
                    facts.currentJoined.Add(new CreepJoinerArcSnapshot
                    {
                        pawnId = pawn.GetUniqueLoadID(),
                        joinedTick = tracker.joinedTick,
                        lastVisiblePhase = CreepJoinerPhaseTokens.Joined,
                        schemaVersion = AnomalyPersistencePolicy.CurrentCreepJoinerArcSchemaVersion
                    });
                }
                catch (Exception exception)
                {
                    Log.WarningOnce(
                        "[Pawn Diary] Anomaly creepjoiner baseline skipped one unreadable pawn: "
                            + exception.GetType().Name + ": " + exception.Message,
                        "PawnDiary.Anomaly.CreepJoinerBaseline".GetHashCode());
                }
            }

            return facts;
        }

        private static FieldInfo BooleanField(Type type, string name, BindingFlags flags)
        {
            FieldInfo field = type?.GetField(name, flags);
            return field != null && field.FieldType == typeof(bool) ? field : null;
        }

        private static bool TryReadCreepJoinerFlags(
            Pawn_CreepJoinerTracker tracker,
            string phase,
            out bool rejected,
            out bool aggressive,
            out bool left)
        {
            rejected = false;
            aggressive = false;
            left = false;
            if (tracker == null
                || phase == AnomalyOutcomeTokens.Rejected
                    && (creepJoinerTriggeredRejectionField == null
                        || creepJoinerTriggeredAggressiveField == null)
                || phase == AnomalyOutcomeTokens.Aggressive
                    && creepJoinerTriggeredAggressiveField == null
                || phase == AnomalyOutcomeTokens.Departed && creepJoinerHasLeftField == null)
            {
                return false;
            }
            if (creepJoinerTriggeredRejectionField != null)
                rejected = (bool)creepJoinerTriggeredRejectionField.GetValue(tracker);
            if (creepJoinerTriggeredAggressiveField != null)
                aggressive = (bool)creepJoinerTriggeredAggressiveField.GetValue(tracker);
            if (creepJoinerHasLeftField != null)
                left = (bool)creepJoinerHasLeftField.GetValue(tracker);
            return CreepJoinerPhaseTokens.IsOutcome(phase);
        }

        private static void AddCreepJoinerWriter(
            CreepJoinerOutcomeCapture capture,
            Dictionary<string, Pawn> pawnsById,
            Pawn pawn,
            bool exactSpeaker,
            bool isSubject,
            bool onMap,
            bool nearby,
            int distanceSquared)
        {
            if (capture == null || pawn == null || !DiaryGameComponent.IsDiaryEligible(pawn)) return;
            string pawnId = pawn.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(pawnId)) return;
            capture.facts.writers.Add(new CreepJoinerWriterCandidate
            {
                pawnId = pawnId,
                eligible = true,
                exactSpeaker = exactSpeaker,
                subject = isSubject,
                onSubjectMap = onMap,
                nearby = nearby,
                distanceSquared = distanceSquared,
                tieBreakKey = pawnId
            });
            if (!pawnsById.ContainsKey(pawnId))
            {
                pawnsById.Add(pawnId, pawn);
                capture.writerPawns.Add(pawn);
            }
        }

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
        /// Snapshots an exactly held pawn, platform, map/cell, visible setting, and deterministic writer
        /// evidence before vanilla ejects anything. No optional Anomaly Def is resolved by name.
        /// </summary>
        internal static bool TryCaptureAnomalyContainmentBefore(
            object targetObject,
            int recentStudierMaxAgeTicks,
            out AnomalyContainmentEscapeCapture capture)
        {
            return TryCaptureAnomalyContainmentBefore(
                targetObject,
                recentStudierMaxAgeTicks,
                AnomalyPolicyLimits.DefaultWitnessRadius,
                includePreEjectionSetting: true,
                candidatePool: null,
                out capture);
        }

        /// <summary>
        /// Captures one containment call with the outer policy snapshot. Nested calls reuse the
        /// outer call's already-filtered, bounded pawn pool and skip the outer-only surroundings read;
        /// this keeps a same-room cascade from repeatedly scanning and sorting every spawned pawn.
        /// </summary>
        internal static bool TryCaptureAnomalyContainmentBefore(
            object targetObject,
            int recentStudierMaxAgeTicks,
            int witnessRadius,
            bool includePreEjectionSetting,
            IReadOnlyList<Pawn> candidatePool,
            out AnomalyContainmentEscapeCapture capture)
        {
            capture = null;
            if (!ModsConfig.AnomalyActive) return false;

            CompHoldingPlatformTarget target = targetObject as CompHoldingPlatformTarget;
            Pawn entity = target?.parent as Pawn;
            Building_HoldingPlatform platform = target?.HeldPlatform;
            Map map = platform?.Map;
            if (target == null || entity?.def == null || platform == null || map == null
                || !target.CurrentlyHeldOnPlatform)
            {
                return false;
            }

            string entityId = entity.GetUniqueLoadID();
            string platformId = platform.GetUniqueLoadID();
            if (string.IsNullOrWhiteSpace(entityId) || string.IsNullOrWhiteSpace(platformId))
                return false;

            int tick = Find.TickManager?.TicksGame ?? 0;
            IntVec3 position = platform.Position;
            AnomalyContainmentEscapeCapture result = new AnomalyContainmentEscapeCapture
            {
                target = target,
                entity = entity,
                platform = platform,
                map = map,
                capturedTick = tick,
                entityFact = new ContainedEntityFact
                {
                    entityId = entityId,
                    visibleLabel = DiaryLineCleaner.CleanLine(entity.LabelShortCap),
                    defName = entity.def.defName,
                    platformId = platformId,
                    mapId = map.uniqueID,
                    platformX = position.x,
                    platformZ = position.z,
                    escaped = false
                },
                preEjectionSetting = includePreEjectionSetting
                    ? BuildOptionalContainmentSetting(map, position)
                    : string.Empty
            };

            IReadOnlyList<Pawn> candidates = candidatePool;
            if (candidates == null) candidates = map.mapPawns?.AllPawnsSpawned;
            List<AnomalyWriterCandidate> detachedCandidates =
                new List<AnomalyWriterCandidate>();
            Dictionary<string, Pawn> pawnsById =
                new Dictionary<string, Pawn>(StringComparer.Ordinal);
            HashSet<string> recentStudierPawnIds =
                AnomalyRecentStudyCache.MatchingStudierPawnIds(
                    entityId, tick, recentStudierMaxAgeTicks);
            int candidateCount = candidates?.Count ?? 0;
            for (int i = 0; i < candidateCount; i++)
            {
                Pawn candidate = candidates[i];
                if (candidate == null || !candidate.Spawned || candidate.Map != map
                    || !DiaryGameComponent.IsDiaryEligible(candidate))
                {
                    continue;
                }

                string pawnId = candidate.GetUniqueLoadID();
                if (string.IsNullOrWhiteSpace(pawnId) || pawnsById.ContainsKey(pawnId)) continue;
                int distanceSquared = (candidate.Position - position).LengthHorizontalSquared;
                detachedCandidates.Add(new AnomalyWriterCandidate
                {
                    pawnId = pawnId,
                    eligible = true,
                    onAffectedMap = true,
                    nearbyCandidate = true,
                    recentExactStudier = recentStudierPawnIds.Contains(pawnId),
                    freeColonistOnHomeMap = map.IsPlayerHome && candidate.IsFreeColonist,
                    distanceSquared = distanceSquared,
                    distanceBucket = distanceSquared,
                    tieBreakKey = pawnId
                });
                pawnsById.Add(pawnId, candidate);
            }

            // Rank the full affected-map roster once. The pool puts every qualified writer ahead of
            // unqualified nested-cascade candidates, so deriving writers from the capped pool preserves
            // the exact role/distance/ID order while bounding the second sort to at most 512 rows.
            List<AnomalyWriterCandidate> pooled =
                ContainmentBreachPolicy.BoundCandidatePool(
                    detachedCandidates,
                    witnessRadius,
                    AnomalyPolicyLimits.MaximumContainmentCandidates);
            for (int i = 0; i < pooled.Count; i++)
            {
                Pawn pawn;
                if (pawnsById.TryGetValue(pooled[i].pawnId, out pawn))
                    result.candidatePawns.Add(pawn);
            }

            List<AnomalyWriterCandidate> bounded =
                ContainmentBreachPolicy.BoundWriterCandidates(
                    pooled,
                    witnessRadius,
                    AnomalyPolicyLimits.MaximumContainmentCandidates);
            for (int i = 0; i < bounded.Count; i++)
            {
                AnomalyWriterCandidate candidate = bounded[i];
                Pawn pawn;
                if (!pawnsById.TryGetValue(candidate.pawnId, out pawn)) continue;
                result.writerFacts.Add(candidate);
                result.writerPawns.Add(pawn);
            }

            capture = result;
            return true;
        }

        private static string BuildOptionalContainmentSetting(Map map, IntVec3 position)
        {
            try
            {
                return DiaryContextBuilder.BuildSurroundingsSummaryAt(map, position);
            }
            catch (Exception exception)
            {
                // Surroundings are optional enrichment. A broken modded room/weather/beauty getter
                // must not erase a verified escape; the page simply continues without setting text.
                Log.WarningOnce(
                    "[Pawn Diary] Containment breach surroundings could not be read and were omitted: "
                        + exception.GetType().Name + ": " + exception.Message,
                    "PawnDiary.Anomaly.Containment.OptionalSetting".GetHashCode());
                return string.Empty;
            }
        }

        /// <summary>
        /// Verifies the exact captured pawn left its exact platform, spawned visibly on the same map,
        /// and entered vanilla's explicit escape state. Intentional ejection never sets that state.
        /// </summary>
        internal static bool VerifyAnomalyContainmentEscape(
            AnomalyContainmentEscapeCapture capture)
        {
            if (!ModsConfig.AnomalyActive || capture?.target == null || capture.entity == null
                || capture.platform == null || capture.map == null)
            {
                return false;
            }

            Pawn entity = capture.entity;
            return !capture.target.CurrentlyHeldOnPlatform
                && capture.target.HeldPlatform == null
                && entity.ParentHolder != capture.platform
                && capture.platform.HeldPawn != entity
                && entity.Spawned
                && entity.Map == capture.map
                && !entity.Destroyed
                && !entity.Dead
                && capture.target.isEscaping;
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

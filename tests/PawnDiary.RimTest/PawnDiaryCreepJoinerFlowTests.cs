// Loaded-game Anomaly A2.0/A2.1 fixtures. These create disposable creepjoiners through vanilla's own
// CreepJoinerUtility, then call the exact public lifecycle or surgical-inspection methods Pawn Diary
// observes. All possible writers have generation disabled, so event creation is synchronous and no
// LLM request can leave the game. Teardown restores component state, XML policy values, letters,
// vanilla Tales, transients, and every specialized pawn even if an assertion fails.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace PawnDiary.RimTests
{
    /// <summary>Exercises creepjoiner continuity, terminal outcomes, and surgical disclosure.</summary>
    [TestSuite]
    public static class PawnDiaryCreepJoinerFlowTests
    {
        private const string LogPrefix = "[PawnDiary RimTest Anomaly creepjoiner] ";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo ApplyAnomalyStateMethod =
            typeof(DiaryGameComponent).GetMethod("ApplyAnomalyState", PrivateInstance);
        private static readonly MethodInfo BootstrapAnomalyMethod =
            typeof(DiaryGameComponent).GetMethod("BootstrapAnomalyForLoadedSave", PrivateInstance);
        private static readonly MethodInfo SurgicalRecipePrefixMethod =
            typeof(DiaryAnomalyPatches).GetMethod("SurgicalRecipePrefix", PrivateInstance | BindingFlags.Static);
        private static readonly MethodInfo SurgicalRecipePostfixMethod =
            typeof(DiaryAnomalyPatches).GetMethod("SurgicalRecipePostfix", PrivateInstance | BindingFlags.Static);
        private static readonly MethodInfo SurgicalRecipeFinalizerMethod =
            typeof(DiaryAnomalyPatches).GetMethod("SurgicalRecipeFinalizer", PrivateInstance | BindingFlags.Static);
        private static readonly FieldInfo TalesListField = ResolveTalesListField();

        private static PawnDiaryRimTestScope scope;
        private static DiaryAnomalyPolicyDef policyDef;
        private static AnomalyPersistentStateSnapshot originalState;
        private static bool originalEnabled;
        private static int originalDedupTicks;
        private static int originalRetentionTicks;
        private static int originalMaxWitnesses;
        private static int originalWitnessRadius;
        private static HashSet<Letter> originalLetters;
        private static HashSet<Tale> originalTales;

        /// <summary>Snapshots every component/Def/global value these live lifecycle fixtures mutate.</summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(
                ArrivalSignal.ArrivalGroupKey, "anomalyCreepJoinerOutcome", "talehealth");
            scope.OwnDiaryEventsCreatedAfterThisPoint();
            policyDef = DefDatabase<DiaryAnomalyPolicyDef>.GetNamedSilentFail(
                DiaryAnomalyPolicy.DefName);
            PawnDiaryRimTestScope.Require(policyDef != null && ApplyAnomalyStateMethod != null,
                "The A2.0 policy Def or component state adapter was not loaded.");
            originalEnabled = policyDef.creepJoinerEnabled;
            originalDedupTicks = policyDef.creepJoinerOutcomeDedupTicks;
            originalRetentionTicks = policyDef.creepJoinerArcRetentionTicks;
            originalMaxWitnesses = policyDef.creepJoinerMaxWitnesses;
            originalWitnessRadius = policyDef.creepJoinerWitnessRadius;
            originalState = scope.Component.AnomalyStateSnapshotForTests();
            originalLetters = new HashSet<Letter>(
                Find.LetterStack?.LettersListForReading ?? new List<Letter>());
            originalTales = SnapshotTales();

            policyDef.creepJoinerEnabled = true;
            policyDef.creepJoinerOutcomeDedupTicks = 2500;
            policyDef.creepJoinerArcRetentionTicks = 3600000;
            // A2.0 terminal outcomes still select one POV, while A2.1 may truthfully select the exact
            // surgeon and joined subject. Keep the shared fixture at the supported two-writer ceiling.
            policyDef.creepJoinerMaxWitnesses = AnomalyPolicyLimits.MaximumCreepJoinerWitnesses;
            policyDef.creepJoinerWitnessRadius = 12;
            AnomalyPersistentStateSnapshot clean = scope.Component.AnomalyStateSnapshotForTests();
            clean.schemaVersion = AnomalyPersistencePolicy.CurrentSchemaVersion;
            clean.creepJoinerArcs = new List<CreepJoinerArcSnapshot>();
            ApplyState(clean);
            AnomalyTransientState.Reset();
        }

        /// <summary>Restores the developer's colony and every process-static value changed by a fixture.</summary>
        [AfterEach]
        public static void TearDown()
        {
            Exception firstFailure = null;
            try
            {
                AnomalyTransientState.Reset();
                if (originalState != null) ApplyState(originalState);
                if (policyDef != null)
                {
                    policyDef.creepJoinerEnabled = originalEnabled;
                    policyDef.creepJoinerOutcomeDedupTicks = originalDedupTicks;
                    policyDef.creepJoinerArcRetentionTicks = originalRetentionTicks;
                    policyDef.creepJoinerMaxWitnesses = originalMaxWitnesses;
                    policyDef.creepJoinerWitnessRadius = originalWitnessRadius;
                }
                RemoveFixtureLetters();
                RemoveFixtureTales();
            }
            catch (Exception exception)
            {
                firstFailure = exception;
            }

            try
            {
                scope?.TearDown();
            }
            catch (Exception exception)
            {
                if (firstFailure == null) firstFailure = exception;
            }
            finally
            {
                originalLetters = null;
                originalTales = null;
                originalState = null;
                policyDef = null;
                scope = null;
            }

            if (firstFailure != null) throw firstFailure;
        }

        /// <summary>Proves optional registration is inert off-DLC and exact on every observed method.</summary>
        [Test]
        public static void ExactHookRegistrationMatchesAnomalyAvailability()
        {
            bool active = ModsConfig.AnomalyActive;
            PawnDiaryRimTestScope.Require(
                DiaryAnomalyPatches.CreepJoinerRejectionHookReady == active
                    && DiaryAnomalyPatches.CreepJoinerAggressionHookReady == active
                    && DiaryAnomalyPatches.CreepJoinerDepartureHookReady == active
                    && DiaryAnomalyPatches.CreepJoinerSurgicalHookReady == active,
                "Creepjoiner hook readiness did not match Anomaly availability.");
            if (!active)
            {
                Log.Message(LogPrefix + "exact lifecycle hooks: not applicable (Anomaly inactive).");
                return;
            }

            RequireExactPatch("DoRejection", requireFinalizer: true);
            RequireExactPatch("DoAggressive", requireFinalizer: false);
            RequireExactPatch("DoLeave", requireFinalizer: false);
            RequireExactSurgicalPatches();
            MethodBase downside = AccessTools.DeclaredMethod(
                typeof(Pawn_CreepJoinerTracker), "DoDownside", Type.EmptyTypes);
            Patches downsidePatches = downside == null ? null : Harmony.GetPatchInfo(downside);
            PawnDiaryRimTestScope.Require(downside != null
                    && (downsidePatches == null || !downsidePatches.Prefixes.Any(OwnedPatch)
                        && !downsidePatches.Postfixes.Any(OwnedPatch)
                        && !downsidePatches.Finalizers.Any(OwnedPatch)),
                "A2.0 must not patch Pawn_CreepJoinerTracker.DoDownside().");
        }

        /// <summary>A real visible disclosure writes one surgeon/patient page and owns DidSurgery.</summary>
        [Test]
        public static void VisibleSurgicalDisclosureOwnsGenericTaleAndUsesExactRoles()
        {
            if (!RequireAnomalyOrReport(nameof(VisibleSurgicalDisclosureOwnsGenericTaleAndUsesExactRoles))) return;
            Pawn subject = CreateJoinedCreepJoiner("Departure", "PsychicAgony");
            Pawn surgeon = CreateWriterAt(subject.Position);

            DiaryEvent page = scope.FireAndRequireEvent(
                () => RunSuccessfulSurgicalInspection(subject, surgeon),
                AnomalyEventDefNames.CreepJoinerOutcome,
                surgeon,
                subject,
                rejectOtherTestPawnEvents: true);
            scope.RequirePairRefs(page, surgeon, subject);
            RequireContext(page, "creepjoiner_phase=surgical_reveal");
            RequireContext(page, "visible_result=disclosed");
            RequireContext(page, "initiator_witness_role=surgeon");
            RequireContext(page, "recipient_witness_role=subject");
            RequireContext(page, "creepjoiner_surgeon_id=" + surgeon.GetUniqueLoadID());
            RequireContext(page, "creepjoiner_subject_id=" + subject.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(
                !(page.gameContext ?? string.Empty).Contains("PsychicAgony")
                    && !(page.gameContext ?? string.Empty).Contains("creepjoiner_terminal"),
                "The disclosure page leaked hidden downside identity or mislabeled a non-terminal phase.");
            string surgeonLabel = surgeon.LabelShortCap;
            string subjectLabel = subject.LabelShortCap;
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(page.initiatorText)
                    && !string.IsNullOrWhiteSpace(page.recipientText)
                    && page.initiatorText.Contains(surgeonLabel)
                    && page.initiatorText.Contains(subjectLabel)
                    && page.recipientText.Contains(surgeonLabel)
                    && page.recipientText.Contains(subjectLabel),
                "The localized surgeon/subject fallback text was empty or lost a visible role label.");

            CreepJoinerArcSnapshot arc = RequireOnlyArc(subject);
            PawnDiaryRimTestScope.Require(!arc.terminal
                    && arc.lastVisiblePhase == AnomalyOutcomeTokens.SurgicalReveal
                    && arc.lastVisibleEventId == page.eventId,
                "The visible disclosure did not persist one non-terminal arc step.");
        }

        /// <summary>An unjoined patient cannot write, so the exact eligible surgeon owns a solo page.</summary>
        [Test]
        public static void PreJoinSurgicalDisclosureUsesSurgeonOnly()
        {
            if (!RequireAnomalyOrReport(nameof(PreJoinSurgicalDisclosureUsesSurgeonOnly))) return;
            Pawn subject = CreateCreepJoiner("Departure", "PsychicAgony");
            Pawn surgeon = CreateWriterAt(subject.Position);

            DiaryEvent page = scope.FireAndRequireEvent(
                () => RunSuccessfulSurgicalInspection(subject, surgeon),
                AnomalyEventDefNames.CreepJoinerOutcome,
                surgeon,
                null,
                rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(page, surgeon);
            RequireContext(page, "witness_role=surgeon");
            RequireContext(page, "creepjoiner_subject_id=" + subject.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(string.IsNullOrEmpty(page.recipientPawnId),
                "An ineligible pre-join creepjoiner was assigned a diary POV.");
        }

        /// <summary>A completed inspection with no tracker disclosure keeps ordinary DidSurgery.</summary>
        [Test]
        public static void NothingFoundKeepsGenericSurgeryTaleAndDoesNotAdvanceReveal()
        {
            if (!RequireAnomalyOrReport(nameof(NothingFoundKeepsGenericSurgeryTaleAndDoesNotAdvanceReveal))) return;
            Pawn subject = CreateJoinedCreepJoiner("Departure", "Nothing");
            Pawn surgeon = CreateWriterAt(subject.Position);

            DiaryEvent page = scope.FireAndRequireEvent(
                () => RunSuccessfulSurgicalInspection(subject, surgeon),
                CreepJoinerSurgeryTaleOwnershipPolicy.DidSurgeryDefName,
                surgeon,
                subject,
                rejectOtherTestPawnEvents: true);
            scope.RequirePairRefs(page, surgeon, subject);
            CreepJoinerArcSnapshot arc = RequireOnlyArc(subject);
            PawnDiaryRimTestScope.Require(!arc.terminal
                    && arc.lastVisiblePhase == CreepJoinerPhaseTokens.Joined,
                "A nothing-found inspection manufactured surgical disclosure history.");
        }

        /// <summary>Disabled specialized output commits history but releases the exact generic Tale.</summary>
        [Test]
        public static void DisabledSurgicalOutputCommitsHistoryAndKeepsGenericTale()
        {
            if (!RequireAnomalyOrReport(nameof(DisabledSurgicalOutputCommitsHistoryAndKeepsGenericTale))) return;
            Pawn subject = CreateJoinedCreepJoiner("Departure", "PsychicAgony");
            Pawn surgeon = CreateWriterAt(subject.Position);
            policyDef.creepJoinerEnabled = false;

            DiaryEvent page = scope.FireAndRequireEvent(
                () => RunSuccessfulSurgicalInspection(subject, surgeon),
                CreepJoinerSurgeryTaleOwnershipPolicy.DidSurgeryDefName,
                surgeon,
                subject,
                rejectOtherTestPawnEvents: true);
            scope.RequirePairRefs(page, surgeon, subject);
            CreepJoinerArcSnapshot arc = RequireOnlyArc(subject);
            PawnDiaryRimTestScope.Require(!arc.terminal
                    && arc.lastVisiblePhase == AnomalyOutcomeTokens.SurgicalReveal
                    && arc.lastVisibleEventId.Length == 0,
                "Disabled disclosure output did not consume the non-terminal history step.");
        }

        /// <summary>An early recipe exit never receives tracker evidence and therefore stays silent.</summary>
        [Test]
        public static void FailedSurgicalBoundaryCannotManufactureDisclosure()
        {
            if (!RequireAnomalyOrReport(nameof(FailedSurgicalBoundaryCannotManufactureDisclosure))) return;
            PawnDiaryRimTestScope.Require(
                SurgicalRecipePrefixMethod != null && SurgicalRecipePostfixMethod != null,
                "Could not resolve Pawn Diary's exact surgical boundary callbacks.");
            Pawn subject = CreateCreepJoiner("Departure", "PsychicAgony");
            Pawn surgeon = CreateWriterAt(subject.Position);

            scope.RequireNoNewEvent(() =>
            {
                object[] prefixArgs = { subject, surgeon, null };
                SurgicalRecipePrefixMethod.Invoke(null, prefixArgs);
                CreepJoinerSurgicalInspectionCapture capture =
                    prefixArgs[2] as CreepJoinerSurgicalInspectionCapture;
                PawnDiaryRimTestScope.Require(capture != null
                        && CreepJoinerSurgicalInspectionScope.CountForTests == 1,
                    "The simulated failed recipe did not open its exact outer correlation scope.");
                SurgicalRecipePostfixMethod.Invoke(null, new object[] { subject, surgeon, capture });
            });
            PawnDiaryRimTestScope.Require(
                CreepJoinerSurgicalInspectionScope.CountForTests == 0
                    && scope.Component.AnomalyStateSnapshotForTests().creepJoinerArcs.Count == 0,
                "An early surgery exit leaked correlation state or manufactured visible history.");
        }

        /// <summary>An exceptional close releases its deferred Tale and removes the active frame.</summary>
        [Test]
        public static void SurgicalFinalizerFailsOpenAndClearsScope()
        {
            if (!RequireAnomalyOrReport(nameof(SurgicalFinalizerFailsOpenAndClearsScope))) return;
            PawnDiaryRimTestScope.Require(
                SurgicalRecipePrefixMethod != null && SurgicalRecipeFinalizerMethod != null,
                "Could not resolve Pawn Diary's surgical prefix/finalizer callbacks.");
            Pawn subject = CreateCreepJoiner("Departure", "PsychicAgony");
            Pawn surgeon = CreateWriterAt(subject.Position);
            TaleDef didSurgery = DefDatabase<TaleDef>.GetNamedSilentFail(
                CreepJoinerSurgeryTaleOwnershipPolicy.DidSurgeryDefName);
            PawnDiaryRimTestScope.Require(didSurgery != null,
                "The vanilla DidSurgery Tale was unavailable.");

            object[] prefixArgs = { subject, surgeon, null };
            SurgicalRecipePrefixMethod.Invoke(null, prefixArgs);
            CreepJoinerSurgicalInspectionCapture capture =
                prefixArgs[2] as CreepJoinerSurgicalInspectionCapture;
            scope.RequireNoNewEvent(() => TaleRecorder.RecordTale(didSurgery, surgeon, subject));
            PawnDiaryRimTestScope.Require(capture != null
                    && CreepJoinerSurgicalInspectionScope.HasDeferredTaleForTests,
                "The exact exceptional-surgery fixture did not defer its generic Tale.");

            Exception original = new InvalidOperationException("A2.1 finalizer fixture");
            Exception returned = null;
            DiaryEvent fallback = scope.FireAndRequireEvent(
                () => returned = SurgicalRecipeFinalizerMethod.Invoke(
                    null, new object[] { original, capture }) as Exception,
                CreepJoinerSurgeryTaleOwnershipPolicy.DidSurgeryDefName,
                surgeon,
                null,
                rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(fallback, surgeon);
            PawnDiaryRimTestScope.Require(ReferenceEquals(returned, original)
                    && CreepJoinerSurgicalInspectionScope.CountForTests == 0,
                "The surgical finalizer changed the exception or leaked its owner scope.");
        }

        /// <summary>DidSurgery outside an exact active owner remains on the mature generic route.</summary>
        [Test]
        public static void UnscopedDidSurgeryKeepsGenericPairRoute()
        {
            if (!RequireAnomalyOrReport(nameof(UnscopedDidSurgeryKeepsGenericPairRoute))) return;
            Pawn subject = CreateJoinedCreepJoiner("Departure", "PsychicAgony");
            Pawn surgeon = CreateWriterAt(subject.Position);
            TaleDef didSurgery = DefDatabase<TaleDef>.GetNamedSilentFail(
                CreepJoinerSurgeryTaleOwnershipPolicy.DidSurgeryDefName);
            PawnDiaryRimTestScope.Require(didSurgery != null
                    && CreepJoinerSurgicalInspectionScope.CountForTests == 0,
                "The unscoped Tale fixture lacked DidSurgery or started with stale ownership.");

            DiaryEvent fallback = scope.FireAndRequireEvent(
                () => TaleRecorder.RecordTale(didSurgery, surgeon, subject),
                CreepJoinerSurgeryTaleOwnershipPolicy.DidSurgeryDefName,
                surgeon,
                subject,
                rejectOtherTestPawnEvents: true);
            scope.RequirePairRefs(fallback, surgeon, subject);
        }

        /// <summary>A later real departure remains eligible after the non-terminal disclosure step.</summary>
        [Test]
        public static void TerminalOutcomeStillFollowsSurgicalDisclosure()
        {
            if (!RequireAnomalyOrReport(nameof(TerminalOutcomeStillFollowsSurgicalDisclosure))) return;
            Pawn subject = CreateJoinedCreepJoiner("Departure", "PsychicAgony");
            Pawn surgeon = CreateWriterAt(subject.Position);
            scope.FireAndRequireEvent(
                () => RunSuccessfulSurgicalInspection(subject, surgeon),
                AnomalyEventDefNames.CreepJoinerOutcome,
                surgeon,
                subject,
                rejectOtherTestPawnEvents: true);

            DiaryEvent terminal = scope.FireAndRequireEvent(
                () => subject.creepjoiner.DoLeave(),
                AnomalyEventDefNames.CreepJoinerOutcome,
                subject,
                null,
                rejectOtherTestPawnEvents: true);
            CreepJoinerArcSnapshot arc = RequireOnlyArc(subject);
            PawnDiaryRimTestScope.Require(arc.terminal
                    && arc.lastVisiblePhase == AnomalyOutcomeTokens.Departed
                    && arc.lastVisibleEventId == terminal.eventId,
                "The surgical reveal incorrectly blocked the later terminal departure.");
        }

        /// <summary>The existing arrival page initializes one arc; repeating it creates neither again.</summary>
        [Test]
        public static void CanonicalArrivalInitializesOneArcWithoutSecondPage()
        {
            if (!RequireAnomalyOrReport(nameof(CanonicalArrivalInitializesOneArcWithoutSecondPage))) return;
            Pawn subject = CreateCreepJoiner("Departure");
            scope.Component.SetDiaryGenerationEnabled(subject, false);

            DiaryEvent arrival = scope.FireAndRequireEvent(
                () => subject.SetFaction(scope.PlayerFaction),
                ArrivalSignal.ArrivalDefName,
                subject,
                null,
                rejectOtherTestPawnEvents: true);
            CreepJoinerArcSnapshot arc = RequireOnlyArc(subject);
            PawnDiaryRimTestScope.Require(arc.lastVisiblePhase == CreepJoinerPhaseTokens.Joined
                    && !arc.terminal && arc.arrivalEventId == arrival.eventId,
                "The canonical creepjoiner arrival did not initialize visible joined continuity.");

            scope.RequireNoNewEvent(() => DiaryEvents.Submit(
                new ArrivalSignal(subject, "arrival_source=joined; creepjoiner=true")));
            CreepJoinerArcSnapshot repeated = RequireOnlyArc(subject);
            PawnDiaryRimTestScope.Require(repeated.arrivalEventId == arrival.eventId
                    && repeated.lastVisiblePhase == CreepJoinerPhaseTokens.Joined,
                "A repeated canonical arrival duplicated or rewrote creepjoiner continuity.");
        }

        /// <summary>A real rejection owns its nested DoLeave call and writes once from the exact speaker.</summary>
        [Test]
        public static void VisibleRejectionOwnsNestedDepartureOnce()
        {
            if (!RequireAnomalyOrReport(nameof(VisibleRejectionOwnsNestedDepartureOnce))) return;
            Pawn subject = CreateCreepJoiner("Departure");
            Pawn speaker = CreateWriterAt(subject.Position);
            subject.creepjoiner.speaker = speaker;

            // This is one speaker POV, not a pairwise page: the creepjoiner is frozen in the
            // visible-only context below and therefore must not be expected as recipientPawnId.
            DiaryEvent page = scope.FireAndRequireEvent(
                () => subject.creepjoiner.DoRejection(),
                AnomalyEventDefNames.CreepJoinerOutcome,
                speaker,
                null,
                rejectOtherTestPawnEvents: true);
            RequireContext(page, "creepjoiner_phase=rejected");
            RequireContext(page, "visible_result=rejected");
            RequireContext(page, "witness_role=speaker");
            RequireContext(page, "creepjoiner_subject_id=" + subject.GetUniqueLoadID());
            CreepJoinerArcSnapshot arc = RequireOnlyArc(subject);
            PawnDiaryRimTestScope.Require(arc.terminal
                    && arc.lastVisiblePhase == AnomalyOutcomeTokens.Rejected
                    && arc.lastVisibleEventId == page.eventId
                    && CreepJoinerOutcomeScope.CountForTests == 0,
                "The outer rejection did not exclusively own its nested departure outcome.");

            scope.RequireNoNewEvent(() => subject.creepjoiner.DoRejection());
            PawnDiaryRimTestScope.Require(RequireOnlyArc(subject).lastVisibleEventId == page.eventId,
                "A repeated no-op rejection rewrote the terminal event identity.");
        }

        /// <summary>An aggressive rejection writes once but records the pawn's strongest visible state.</summary>
        [Test]
        public static void AggressiveRejectionRecordsVisibleHostilityOnce()
        {
            if (!RequireAnomalyOrReport(nameof(AggressiveRejectionRecordsVisibleHostilityOnce))) return;
            Pawn subject = CreateCreepJoiner("AggressiveRejection");
            Pawn speaker = CreateWriterAt(subject.Position);
            subject.creepjoiner.speaker = speaker;

            DiaryEvent page = scope.FireAndRequireEvent(
                () => subject.creepjoiner.DoRejection(),
                AnomalyEventDefNames.CreepJoinerOutcome,
                speaker,
                null,
                rejectOtherTestPawnEvents: true);
            RequireContext(page, "creepjoiner_phase=aggressive");
            RequireContext(page, "visible_result=hostile");
            RequireContext(page, "rejection_response=true");
            RequireContext(page, "witness_role=speaker");
            CreepJoinerArcSnapshot arc = RequireOnlyArc(subject);
            PawnDiaryRimTestScope.Require(subject.Faction?.IsPlayer != true
                    && arc.terminal
                    && arc.lastVisiblePhase == AnomalyOutcomeTokens.Aggressive
                    && arc.lastVisibleEventId == page.eventId
                    && CreepJoinerOutcomeScope.CountForTests == 0,
                "Aggressive rejection did not persist its visible hostile terminal state exactly once.");
        }

        /// <summary>A letterless modded rejection cannot suppress its nested visible aggression owner.</summary>
        [Test]
        public static void LetterlessRejectionReleasesNestedVisibleAggression()
        {
            if (!RequireAnomalyOrReport(nameof(LetterlessRejectionReleasesNestedVisibleAggression))) return;
            Pawn subject = CreateCreepJoiner("AggressiveRejection");
            Pawn writer = CreateWriterAt(subject.Position);
            CreepJoinerRejectionDef rejection = subject.creepjoiner.rejection;
            bool originalHasLetter = rejection.hasLetter;
            try
            {
                rejection.hasLetter = false;
                DiaryEvent page = scope.FireAndRequireEvent(
                    () => subject.creepjoiner.DoRejection(),
                    AnomalyEventDefNames.CreepJoinerOutcome,
                    writer,
                    null,
                    rejectOtherTestPawnEvents: true);
                RequireContext(page, "creepjoiner_phase=aggressive");
                RequireContext(page, "visible_result=hostile");
                PawnDiaryRimTestScope.Require(
                    !(page.gameContext ?? string.Empty).Contains("rejection_response=true")
                        && RequireOnlyArc(subject).lastVisibleEventId == page.eventId
                        && CreepJoinerOutcomeScope.CountForTests == 0,
                    "A letterless outer rejection swallowed or mislabeled its nested visible owner.");
            }
            finally
            {
                rejection.hasLetter = originalHasLetter;
            }
        }

        /// <summary>A real hostile transition writes only from the exact nearby eligible witness.</summary>
        [Test]
        public static void VisibleAggressionUsesNearbyWitnessAndDoesNotReplay()
        {
            if (!RequireAnomalyOrReport(nameof(VisibleAggressionUsesNearbyWitnessAndDoesNotReplay))) return;
            Pawn subject = CreateJoinedCreepJoiner("Departure");
            Pawn writer = CreateWriterAt(subject.Position);

            // Hostility removes the subject from eligible POVs. The nearby colonist owns one solo
            // page; the subject identity is asserted from captured context, not a recipient role.
            DiaryEvent page = scope.FireAndRequireEvent(
                () => subject.creepjoiner.DoAggressive(),
                AnomalyEventDefNames.CreepJoinerOutcome,
                writer,
                null,
                rejectOtherTestPawnEvents: true);
            RequireContext(page, "creepjoiner_phase=aggressive");
            RequireContext(page, "visible_result=hostile");
            RequireContext(page, "witness_role=nearby");
            RequireContext(page, "creepjoiner_subject_id=" + subject.GetUniqueLoadID());
            PawnDiaryRimTestScope.Require(subject.Faction?.IsPlayer != true
                    && RequireOnlyArc(subject).lastVisiblePhase == AnomalyOutcomeTokens.Aggressive,
                "The exact aggression method did not commit its visible hostile transition.");
            scope.RequireNoNewEvent(() => subject.creepjoiner.DoAggressive());
        }

        /// <summary>Disabling creepjoiner pages still consumes one committed visible terminal transition.</summary>
        [Test]
        public static void DisabledOutputStillCommitsTerminalHistory()
        {
            if (!RequireAnomalyOrReport(nameof(DisabledOutputStillCommitsTerminalHistory))) return;
            Pawn subject = CreateJoinedCreepJoiner("Departure");
            CreateWriterAt(subject.Position);
            policyDef.creepJoinerEnabled = false;

            scope.RequireNoNewEvent(() => subject.creepjoiner.DoAggressive());
            CreepJoinerArcSnapshot arc = RequireOnlyArc(subject);
            PawnDiaryRimTestScope.Require(arc.terminal
                    && arc.lastVisiblePhase == AnomalyOutcomeTokens.Aggressive
                    && arc.lastVisibleEventId.Length == 0,
                "Disabled creepjoiner output did not consume the committed terminal transition.");
        }

        /// <summary>A joined pawn may narrate its own departure only from the eligible before snapshot.</summary>
        [Test]
        public static void VisibleJoinedDepartureUsesSubjectBeforeSnapshot()
        {
            if (!RequireAnomalyOrReport(nameof(VisibleJoinedDepartureUsesSubjectBeforeSnapshot))) return;
            Pawn subject = CreateJoinedCreepJoiner("Departure");

            DiaryEvent page = scope.FireAndRequireEvent(
                () => subject.creepjoiner.DoLeave(),
                AnomalyEventDefNames.CreepJoinerOutcome,
                subject,
                null,
                rejectOtherTestPawnEvents: true);
            RequireContext(page, "creepjoiner_phase=departed");
            RequireContext(page, "visible_result=leaving");
            RequireContext(page, "witness_role=subject");
            PawnDiaryRimTestScope.Require(subject.Faction?.IsPlayer != true
                    && subject.GetLord()?.LordJob is LordJob_ExitMapBest
                    && RequireOnlyArc(subject).lastVisibleEventId == page.eventId,
                "Departure did not use the eligible subject's exact pre-transition snapshot.");
            scope.RequireNoNewEvent(() => subject.creepjoiner.DoLeave());
        }

        /// <summary>A failed exact gate remains state/page silent instead of inventing a transition.</summary>
        [Test]
        public static void DisabledTrackerNoOpRemainsSilent()
        {
            if (!RequireAnomalyOrReport(nameof(DisabledTrackerNoOpRemainsSilent))) return;
            Pawn subject = CreateCreepJoiner("Departure");
            subject.DeSpawn();
            scope.RequireNoNewEvent(() => subject.creepjoiner.DoLeave());
            PawnDiaryRimTestScope.Require(
                scope.Component.AnomalyStateSnapshotForTests().creepJoinerArcs.Count == 0,
                "A disabled/no-op tracker call manufactured terminal continuity.");
        }

        /// <summary>The real loaded-save scan imports a joined pawn as state only, never as a page.</summary>
        [Test]
        public static void LegacyBootstrapImportsLiveJoinedCreepJoinerStateOnly()
        {
            if (!RequireAnomalyOrReport(nameof(LegacyBootstrapImportsLiveJoinedCreepJoinerStateOnly))) return;
            PawnDiaryRimTestScope.Require(BootstrapAnomalyMethod != null,
                "Could not resolve the real loaded-save Anomaly bootstrap method.");
            Pawn subject = CreateJoinedCreepJoiner("Departure");
            ApplyState(new AnomalyPersistentStateSnapshot
            {
                schemaVersion = AnomalyPersistencePolicy.StudySchemaVersion
            });

            scope.RequireNoNewEvent(() => BootstrapAnomalyMethod.Invoke(scope.Component, null));
            AnomalyPersistentStateSnapshot state = scope.Component.AnomalyStateSnapshotForTests();
            CreepJoinerArcSnapshot arc = FindArc(state.creepJoinerArcs, subject);
            PawnDiaryRimTestScope.Require(
                state.schemaVersion == AnomalyPersistencePolicy.CurrentSchemaVersion
                    && arc != null
                    && arc.lastVisiblePhase == CreepJoinerPhaseTokens.Joined
                    && !arc.terminal
                    && arc.arrivalEventId.Length == 0
                    && arc.lastVisibleEventId.Length == 0,
                "The real old-save scan failed to create one state-only joined creepjoiner baseline.");
        }

        /// <summary>Every existing Anomaly reset boundary clears both bounded creepjoiner owners.</summary>
        [Test]
        public static void LifecycleResetClearsCreepJoinerOwnership()
        {
            CreepJoinerSurgicalInspectionCapture surgical = new CreepJoinerSurgicalInspectionCapture
            {
                facts = new CreepJoinerSurgicalDisclosureFacts
                {
                    subjectPawnId = "Pawn_SurgicalResetFixture",
                    surgeonPawnId = "Pawn_SurgeonResetFixture",
                    tick = Find.TickManager?.TicksGame ?? 0
                }
            };
            PawnDiaryRimTestScope.Require(
                CreepJoinerOutcomeScope.BeginRejection("Pawn_ResetFixture")
                    && CreepJoinerSurgicalInspectionScope.Begin(surgical, 4)
                    && CreepJoinerOutcomeScope.CountForTests == 1
                    && CreepJoinerSurgicalInspectionScope.CountForTests == 1,
                "Could not seed both transient creepjoiner owners before lifecycle reset.");
            AnomalyTransientState.Reset();
            PawnDiaryRimTestScope.Require(CreepJoinerOutcomeScope.CountForTests == 0
                    && CreepJoinerSurgicalInspectionScope.CountForTests == 0,
                "The Anomaly lifecycle reset leaked a creepjoiner owner.");
        }

        private static Pawn CreateJoinedCreepJoiner(string rejectionDefName)
        {
            return CreateJoinedCreepJoiner(rejectionDefName, "Nothing");
        }

        private static Pawn CreateJoinedCreepJoiner(
            string rejectionDefName,
            string downsideDefName)
        {
            Pawn pawn = CreateCreepJoiner(rejectionDefName, downsideDefName);
            scope.Component.SetDiaryGenerationEnabled(pawn, false);
            pawn.SetFaction(scope.PlayerFaction);
            PawnDiaryRimTestScope.Require(DiaryGameComponent.IsDiaryEligible(pawn),
                "The generated joined creepjoiner did not become diary-eligible.");
            return pawn;
        }

        private static Pawn CreateCreepJoiner(string rejectionDefName)
        {
            return CreateCreepJoiner(rejectionDefName, "Nothing");
        }

        private static Pawn CreateCreepJoiner(
            string rejectionDefName,
            string downsideDefName)
        {
            Map map = Find.CurrentMap;
            CreepJoinerFormKindDef form = DefDatabase<CreepJoinerFormKindDef>.AllDefsListForReading
                .OrderBy(def => def.defName, StringComparer.Ordinal).FirstOrDefault();
            CreepJoinerBenefitDef benefit = DefDatabase<CreepJoinerBenefitDef>.AllDefsListForReading
                .OrderBy(def => def.defName, StringComparer.Ordinal).FirstOrDefault();
            CreepJoinerDownsideDef downside = DefDatabase<CreepJoinerDownsideDef>
                .GetNamedSilentFail(downsideDefName);
            CreepJoinerAggressiveDef aggressive = DefDatabase<CreepJoinerAggressiveDef>
                .GetNamedSilentFail("Assault");
            CreepJoinerRejectionDef rejection = DefDatabase<CreepJoinerRejectionDef>
                .GetNamedSilentFail(rejectionDefName);
            PawnDiaryRimTestScope.Require(map != null && form != null && benefit != null
                    && downside != null && aggressive != null && rejection != null,
                "Anomaly is active but a vanilla creepjoiner fixture Def was unavailable.");

            Pawn pawn = CreepJoinerUtility.GenerateAndSpawn(
                form, benefit, downside, aggressive, rejection, map);
            PawnDiaryRimTestScope.Require(pawn?.creepjoiner != null && pawn.Spawned,
                "Vanilla failed to generate and spawn a disposable creepjoiner.");
            scope.TrackSpecializedPawn(pawn);
            return pawn;
        }

        // The live recipe normally rolls an XML-configured failure effect. A2.1 is testing capture and
        // ownership, not RimWorld's medicine RNG, so this helper temporarily removes only that roll,
        // invokes the exact vanilla worker, and restores the shared Def in a finally block.
        private static void RunSuccessfulSurgicalInspection(Pawn subject, Pawn surgeon)
        {
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("SurgicalInspection");
            PawnDiaryRimTestScope.Require(recipe?.Worker is Recipe_SurgicalInspection,
                "The vanilla SurgicalInspection recipe worker was unavailable.");
            SurgeryOutcomeEffectDef originalOutcome = recipe.surgeryOutcomeEffect;
            try
            {
                recipe.surgeryOutcomeEffect = null;
                recipe.Worker.ApplyOnPawn(
                    subject, null, surgeon, new List<Thing>(), null);
            }
            finally
            {
                recipe.surgeryOutcomeEffect = originalOutcome;
            }
        }

        private static Pawn CreateWriterAt(IntVec3 cell)
        {
            Pawn writer = scope.CreateAdultColonist();
            GenSpawn.Spawn(writer, cell, Find.CurrentMap);
            return writer;
        }

        private static CreepJoinerArcSnapshot RequireOnlyArc(Pawn subject)
        {
            List<CreepJoinerArcSnapshot> arcs = scope.Component
                .AnomalyStateSnapshotForTests().creepJoinerArcs;
            string pawnId = subject?.GetUniqueLoadID() ?? string.Empty;
            PawnDiaryRimTestScope.Require(arcs.Count == 1 && arcs[0].pawnId == pawnId,
                "Expected exactly one creepjoiner arc for the disposable subject.");
            return arcs[0];
        }

        private static CreepJoinerArcSnapshot FindArc(
            List<CreepJoinerArcSnapshot> arcs,
            Pawn subject)
        {
            string pawnId = subject?.GetUniqueLoadID() ?? string.Empty;
            return arcs?.FirstOrDefault(row => row != null && row.pawnId == pawnId);
        }

        private static void RequireExactPatch(string methodName, bool requireFinalizer)
        {
            MethodBase method = AccessTools.DeclaredMethod(
                typeof(Pawn_CreepJoinerTracker), methodName, Type.EmptyTypes);
            Patches patches = method == null ? null : Harmony.GetPatchInfo(method);
            PawnDiaryRimTestScope.Require(method is MethodInfo info && info.IsPublic
                    && info.ReturnType == typeof(void) && info.GetParameters().Length == 0
                    && patches != null && patches.Prefixes.Any(OwnedPatch)
                    && patches.Postfixes.Any(OwnedPatch)
                    && (!requireFinalizer || patches.Finalizers.Any(OwnedPatch)),
                "The exact Pawn_CreepJoinerTracker." + methodName
                    + "() method lacks Pawn Diary's expected patch set.");
        }

        private static void RequireExactSurgicalPatches()
        {
            MethodBase recipe = AccessTools.DeclaredMethod(
                typeof(Recipe_SurgicalInspection),
                "ApplyOnPawn",
                new[]
                {
                    typeof(Pawn), typeof(BodyPartRecord), typeof(Pawn),
                    typeof(List<Thing>), typeof(Bill)
                });
            MethodBase tracker = AccessTools.DeclaredMethod(
                typeof(Pawn_CreepJoinerTracker),
                "DoSurgicalInspection",
                new[] { typeof(Pawn), typeof(System.Text.StringBuilder) });
            MethodBase pawn = AccessTools.DeclaredMethod(
                typeof(Pawn),
                "DoSurgicalInspection",
                new[] { typeof(Pawn), typeof(string).MakeByRefType() });
            Patches recipePatches = recipe == null ? null : Harmony.GetPatchInfo(recipe);
            Patches trackerPatches = tracker == null ? null : Harmony.GetPatchInfo(tracker);
            Patches pawnPatches = pawn == null ? null : Harmony.GetPatchInfo(pawn);
            PawnDiaryRimTestScope.Require(
                recipePatches != null
                    && recipePatches.Prefixes.Any(OwnedPatch)
                    && recipePatches.Postfixes.Any(OwnedPatch)
                    && recipePatches.Finalizers.Any(OwnedPatch)
                    && trackerPatches != null
                    && trackerPatches.Prefixes.Any(OwnedPatch)
                    && trackerPatches.Postfixes.Any(OwnedPatch)
                    && pawnPatches != null
                    && pawnPatches.Postfixes.Any(OwnedPatch),
                "The exact recipe/tracker/Pawn surgical correlation lacks Pawn Diary's patch set.");
        }

        private static bool OwnedPatch(Patch patch)
        {
            return patch.owner == "aimml.pawndiary"
                && patch.PatchMethod?.DeclaringType == typeof(DiaryAnomalyPatches);
        }

        private static void RequireContext(DiaryEvent page, string token)
        {
            PawnDiaryRimTestScope.Require(page != null
                    && (page.gameContext ?? string.Empty).Contains(token),
                "Creepjoiner event context omitted exact token '" + token + "'.");
        }

        private static void ApplyState(AnomalyPersistentStateSnapshot state)
        {
            ApplyAnomalyStateMethod.Invoke(scope.Component, new object[] { state });
        }

        private static bool RequireAnomalyOrReport(string testName)
        {
            if (ModsConfig.AnomalyActive) return true;
            PawnDiaryRimTestScope.Require(!DiaryAnomalyPatches.CreepJoinerRejectionHookReady
                    && !DiaryAnomalyPatches.CreepJoinerAggressionHookReady
                    && !DiaryAnomalyPatches.CreepJoinerDepartureHookReady
                    && !DiaryAnomalyPatches.CreepJoinerSurgicalHookReady,
                "A creepjoiner hook remained ready without Anomaly.");
            Log.Message(LogPrefix + testName + ": not applicable (Anomaly inactive).");
            return false;
        }

        private static void RemoveFixtureLetters()
        {
            if (Verse.Current.Game == null || originalLetters == null) return;
            LetterStack stack = Find.LetterStack;
            if (stack?.LettersListForReading == null) return;
            for (int i = stack.LettersListForReading.Count - 1; i >= 0; i--)
            {
                Letter letter = stack.LettersListForReading[i];
                if (!originalLetters.Contains(letter)) stack.RemoveLetter(letter);
            }
        }

        // Verse exposes no public single-Tale removal. Resolve the private List<Tale> by type and
        // remove only rows created after this fixture's setup snapshot, preserving the player's history.
        private static FieldInfo ResolveTalesListField()
        {
            FieldInfo[] fields = typeof(TaleManager).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            for (int i = 0; i < fields.Length; i++)
            {
                Type type = fields[i].FieldType;
                if (typeof(IList).IsAssignableFrom(type)
                    && type.IsGenericType
                    && type.GetGenericArguments()[0] == typeof(Tale))
                {
                    return fields[i];
                }
            }

            return null;
        }

        private static HashSet<Tale> SnapshotTales()
        {
            HashSet<Tale> result = new HashSet<Tale>();
            IList rows = TalesListField?.GetValue(Find.TaleManager) as IList;
            if (rows == null) return result;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] is Tale tale) result.Add(tale);
            }
            return result;
        }

        private static void RemoveFixtureTales()
        {
            if (originalTales == null) return;
            IList rows = TalesListField?.GetValue(Find.TaleManager) as IList;
            if (rows == null) return;
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                if (rows[i] is Tale tale && !originalTales.Contains(tale)) rows.RemoveAt(i);
            }
        }
    }
}

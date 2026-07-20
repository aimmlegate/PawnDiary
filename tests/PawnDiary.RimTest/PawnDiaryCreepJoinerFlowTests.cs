// Loaded-game Anomaly A2.0 fixtures. These create disposable creepjoiners through vanilla's own
// CreepJoinerUtility, then call the exact public DoRejection/DoAggressive/DoLeave lifecycle methods.
// All possible writers have generation disabled, so event creation is synchronous and no LLM request
// can leave the game. Teardown restores component state, XML policy values, letters, transients, and
// every specialized pawn even if an assertion fails.
using System;
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
    /// <summary>Exercises visible creepjoiner continuity and the three exact vanilla outcome seams.</summary>
    [TestSuite]
    public static class PawnDiaryCreepJoinerFlowTests
    {
        private const string LogPrefix = "[PawnDiary RimTest Anomaly creepjoiner] ";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo ApplyAnomalyStateMethod =
            typeof(DiaryGameComponent).GetMethod("ApplyAnomalyState", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static DiaryAnomalyPolicyDef policyDef;
        private static AnomalyPersistentStateSnapshot originalState;
        private static bool originalEnabled;
        private static int originalDedupTicks;
        private static int originalRetentionTicks;
        private static int originalMaxWitnesses;
        private static int originalWitnessRadius;
        private static HashSet<Letter> originalLetters;

        /// <summary>Snapshots every component/Def/global value these live lifecycle fixtures mutate.</summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(
                ArrivalSignal.ArrivalGroupKey, "anomalyCreepJoinerOutcome");
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

            policyDef.creepJoinerEnabled = true;
            policyDef.creepJoinerOutcomeDedupTicks = 2500;
            policyDef.creepJoinerArcRetentionTicks = 3600000;
            policyDef.creepJoinerMaxWitnesses = 1;
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
                originalState = null;
                policyDef = null;
                scope = null;
            }

            if (firstFailure != null) throw firstFailure;
        }

        /// <summary>Proves optional registration is inert off-DLC and exact on all three public methods.</summary>
        [Test]
        public static void ExactHookRegistrationMatchesAnomalyAvailability()
        {
            bool active = ModsConfig.AnomalyActive;
            PawnDiaryRimTestScope.Require(
                DiaryAnomalyPatches.CreepJoinerRejectionHookReady == active
                    && DiaryAnomalyPatches.CreepJoinerAggressionHookReady == active
                    && DiaryAnomalyPatches.CreepJoinerDepartureHookReady == active,
                "Creepjoiner hook readiness did not match Anomaly availability.");
            if (!active)
            {
                Log.Message(LogPrefix + "exact lifecycle hooks: not applicable (Anomaly inactive).");
                return;
            }

            RequireExactPatch("DoRejection", requireFinalizer: true);
            RequireExactPatch("DoAggressive", requireFinalizer: false);
            RequireExactPatch("DoLeave", requireFinalizer: false);
            MethodBase downside = AccessTools.DeclaredMethod(
                typeof(Pawn_CreepJoinerTracker), "DoDownside", Type.EmptyTypes);
            Patches downsidePatches = downside == null ? null : Harmony.GetPatchInfo(downside);
            PawnDiaryRimTestScope.Require(downside != null
                    && (downsidePatches == null || !downsidePatches.Prefixes.Any(OwnedPatch)
                        && !downsidePatches.Postfixes.Any(OwnedPatch)
                        && !downsidePatches.Finalizers.Any(OwnedPatch)),
                "A2.0 must not patch Pawn_CreepJoinerTracker.DoDownside().");
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

            DiaryEvent page = scope.FireAndRequireEvent(
                () => subject.creepjoiner.DoRejection(),
                AnomalyEventDefNames.CreepJoinerOutcome,
                speaker,
                subject,
                rejectOtherTestPawnEvents: true);
            RequireContext(page, "creepjoiner_phase=rejected");
            RequireContext(page, "visible_result=rejected");
            RequireContext(page, "witness_role=speaker");
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

        /// <summary>A real hostile transition writes only from the exact nearby eligible witness.</summary>
        [Test]
        public static void VisibleAggressionUsesNearbyWitnessAndDoesNotReplay()
        {
            if (!RequireAnomalyOrReport(nameof(VisibleAggressionUsesNearbyWitnessAndDoesNotReplay))) return;
            Pawn subject = CreateJoinedCreepJoiner("Departure");
            Pawn writer = CreateWriterAt(subject.Position);

            DiaryEvent page = scope.FireAndRequireEvent(
                () => subject.creepjoiner.DoAggressive(),
                AnomalyEventDefNames.CreepJoinerOutcome,
                writer,
                subject,
                rejectOtherTestPawnEvents: true);
            RequireContext(page, "creepjoiner_phase=aggressive");
            RequireContext(page, "visible_result=hostile");
            RequireContext(page, "witness_role=nearby");
            PawnDiaryRimTestScope.Require(subject.Faction?.IsPlayer != true
                    && RequireOnlyArc(subject).lastVisiblePhase == AnomalyOutcomeTokens.Aggressive,
                "The exact aggression method did not commit its visible hostile transition.");
            scope.RequireNoNewEvent(() => subject.creepjoiner.DoAggressive());
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

        /// <summary>Every existing Anomaly reset boundary clears the bounded rejection owner stack.</summary>
        [Test]
        public static void LifecycleResetClearsCreepJoinerOwnership()
        {
            PawnDiaryRimTestScope.Require(
                CreepJoinerOutcomeScope.BeginRejection("Pawn_ResetFixture")
                    && CreepJoinerOutcomeScope.CountForTests == 1,
                "Could not seed the transient rejection owner before lifecycle reset.");
            AnomalyTransientState.Reset();
            PawnDiaryRimTestScope.Require(CreepJoinerOutcomeScope.CountForTests == 0,
                "The Anomaly lifecycle reset leaked a creepjoiner rejection owner.");
        }

        private static Pawn CreateJoinedCreepJoiner(string rejectionDefName)
        {
            Pawn pawn = CreateCreepJoiner(rejectionDefName);
            scope.Component.SetDiaryGenerationEnabled(pawn, false);
            pawn.SetFaction(scope.PlayerFaction);
            PawnDiaryRimTestScope.Require(DiaryGameComponent.IsDiaryEligible(pawn),
                "The generated joined creepjoiner did not become diary-eligible.");
            return pawn;
        }

        private static Pawn CreateCreepJoiner(string rejectionDefName)
        {
            Map map = Find.CurrentMap;
            CreepJoinerFormKindDef form = DefDatabase<CreepJoinerFormKindDef>.AllDefsListForReading
                .OrderBy(def => def.defName, StringComparer.Ordinal).FirstOrDefault();
            CreepJoinerBenefitDef benefit = DefDatabase<CreepJoinerBenefitDef>.AllDefsListForReading
                .OrderBy(def => def.defName, StringComparer.Ordinal).FirstOrDefault();
            CreepJoinerDownsideDef downside = DefDatabase<CreepJoinerDownsideDef>
                .GetNamedSilentFail("Nothing");
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
                    && !DiaryAnomalyPatches.CreepJoinerDepartureHookReady,
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
    }
}

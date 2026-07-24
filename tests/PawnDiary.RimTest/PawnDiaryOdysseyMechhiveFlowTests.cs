// Loaded-game Odyssey O3 fixtures for the verified Cerebrex-core ending. The real private owner
// pauses the game, ends a quest, destroys or scavenges the core, and shows credits, so these tests do
// not invoke it. They verify the installed signature/patch, then drive Pawn Diary's exact synchronous
// scope with a null-by-default completion seam and disposable pawns. Both branches, deferred Quest
// release, prefix gates, lifecycle reset, persistence, and replay silence are covered.
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimTestRedux;
using RimWorld;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Exercises Odyssey O3 terminal ownership without launching the destructive vanilla ending.</summary>
    [TestSuite]
    public static class PawnDiaryOdysseyMechhiveFlowTests
    {
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly MethodInfo PrefixMethod =
            typeof(OdysseyMechhiveOutcomePatch).GetMethod("Prefix", PrivateStatic);
        private static readonly MethodInfo PostfixMethod =
            typeof(OdysseyMechhiveOutcomePatch).GetMethod("Postfix", PrivateStatic);
        private static readonly MethodInfo FinalizerMethod =
            typeof(OdysseyMechhiveOutcomePatch).GetMethod("Finalizer", PrivateStatic);
        private static readonly MethodInfo ExposeOdysseyDataMethod =
            typeof(DiaryGameComponent).GetMethod("ExposeOdysseyData", PrivateInstance);
        private static readonly MethodInfo BootstrapOdysseyMethod =
            typeof(DiaryGameComponent).GetMethod("BootstrapOdysseyForLoadedSave", PrivateInstance);
        private static readonly FieldInfo OutcomeStateField =
            typeof(DiaryGameComponent).GetField("odysseyMechhiveOutcome", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static DiaryOdysseyPolicyDef policyDef;
        private static bool originalPageEnabled;
        private static object savedOutcomeState;
        private static DiaryGameComponent scribeTarget;

        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin(
                OdysseyGroupDefNames.MechhiveOutcome, "reflection");
            scope.OwnDiaryEventsCreatedAfterThisPoint();
            policyDef = DefDatabase<DiaryOdysseyPolicyDef>.GetNamedSilentFail(
                DiaryOdysseyPolicy.DefName);
            PawnDiaryRimTestScope.Require(policyDef != null,
                "The Odyssey policy Def was not loaded.");
            originalPageEnabled = policyDef.mechhiveOutcomePageEnabled;
            savedOutcomeState = OutcomeStateField?.GetValue(scope.Component);
            policyDef.mechhiveOutcomePageEnabled = true;
            DiaryTuningDef tuning = DiaryTuning.Current;
            bool originalArcReflection = tuning.arcReflectionEnabled;
            tuning.arcReflectionEnabled = true;
            scope.RegisterCleanup(() => tuning.arcReflectionEnabled = originalArcReflection);
            OutcomeStateField?.SetValue(scope.Component, null);
            OdysseyTransientState.Reset();
        }

        [AfterEach]
        public static void TearDown()
        {
            Exception first = null;
            try
            {
                OdysseyTransientState.Reset();
                if (policyDef != null)
                    policyDef.mechhiveOutcomePageEnabled = originalPageEnabled;
                OutcomeStateField?.SetValue(scope.Component, savedOutcomeState);
            }
            catch (Exception exception)
            {
                first = exception;
            }

            try
            {
                scope?.TearDown();
            }
            catch (Exception exception)
            {
                if (first == null) first = exception;
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
                scribeTarget = null;
                savedOutcomeState = null;
                policyDef = null;
                scope = null;
            }
            if (first != null) throw first;
        }

        /// <summary>Proves no-DLC inertness or the exact installed private signature and patch set.</summary>
        [Test]
        public static void ExactRegistrationAndNoDlcGate()
        {
            bool active = ModsConfig.OdysseyActive;
            PawnDiaryRimTestScope.Require(
                OdysseyMechhiveOutcomePatch.HookReady == active,
                "Mechhive hook readiness did not match Odyssey availability.");
            if (!active) return;

            Type compType = AccessTools.TypeByName("RimWorld.CompCerebrexCore");
            MethodInfo method = compType == null ? null : AccessTools.DeclaredMethod(
                compType, "DeactivateCore", new[] { typeof(bool) }) as MethodInfo;
            Patches patches = method == null ? null : Harmony.GetPatchInfo(method);
            PawnDiaryRimTestScope.Require(method != null && method.IsPrivate
                    && !method.IsStatic && method.ReturnType == typeof(void)
                    && method.GetParameters().Length == 1
                    && method.GetParameters()[0].Name == "scavenging"
                    && patches != null
                    && patches.Prefixes.Any(OwnedPatch)
                    && patches.Postfixes.Any(OwnedPatch)
                    && patches.Finalizers.Any(OwnedPatch),
                "The exact private CompCerebrexCore.DeactivateCore(bool scavenging) "
                    + "signature lacks Pawn Diary's O3 patch set.");
        }

        /// <summary>A verified destroy branch creates one operator-authored terminal page.</summary>
        [Test]
        public static void DestroyCreatesExactlyOneTerminalPage()
        {
            if (!RequireOdyssey()) return;
            RunSuccessfulOutcome(OdysseyMechhiveOutcomeTokens.Destroyed);
        }

        /// <summary>A verified scavenge branch creates one operator-authored terminal page.</summary>
        [Test]
        public static void ScavengeCreatesExactlyOneTerminalPage()
        {
            if (!RequireOdyssey()) return;
            RunSuccessfulOutcome(OdysseyMechhiveOutcomeTokens.Scavenged);
        }

        /// <summary>An exceptional unwind releases the exact deferred Quest and clears its frame.</summary>
        [Test]
        public static void FinalizerReleasesDeferredQuest()
        {
            if (!RequireOdyssey()) return;
            Pawn actor = CreateEligiblePawn();
            object marker = new object();
            OdysseyMechhiveOutcomeCapture capture = Capture(marker, actor,
                OdysseyMechhiveOutcomeTokens.Destroyed, 990003);
            capture.suppressesQuestSuccess = true;
            PawnDiaryRimTestScope.Require(
                OdysseyMechhiveOutcomeScope.Begin(capture, 8),
                "The Mechhive finalizer fixture could not open its exact frame.");
            QuestFanoutSignal generic = GenericQuestSignal(capture.facts.questId);
            PawnDiaryRimTestScope.Require(
                OdysseyMechhiveOutcomeScope.TryDeferQuestSuccess(
                    capture.facts.questId, generic),
                "The exact generic Mechhive Quest success was not deferred.");
            Exception original = new InvalidOperationException("O3 finalizer fixture");

            object returned = FinalizerMethod.Invoke(
                null, new object[] { original, capture });

            PawnDiaryRimTestScope.Require(ReferenceEquals(returned, original),
                "The O3 finalizer did not preserve vanilla's exception.");
            PawnDiaryRimTestScope.Require(
                OdysseyMechhiveOutcomeScope.CountForTests == 0
                    && !scope.Component.HasCommittedOdysseyMechhiveOutcome,
                "The O3 finalizer leaked a frame or committed terminal state.");
            scope.RequireNoEventForTestPawns(OdysseyMechhiveEventDefNames.Outcome);
        }

        /// <summary>A failed post-state proof releases Quest ownership and writes no specialized page.</summary>
        [Test]
        public static void FailedVerificationWritesNoPage()
        {
            if (!RequireOdyssey()) return;
            Pawn actor = CreateEligiblePawn();
            object marker = new object();
            OdysseyMechhiveOutcomeCapture capture = Capture(marker, actor,
                OdysseyMechhiveOutcomeTokens.Destroyed, 990004);
            capture.suppressesQuestSuccess = true;
            OdysseyMechhiveOutcomeScope.Begin(capture, 8);
            OdysseyMechhiveOutcomeScope.TryDeferQuestSuccess(
                capture.facts.questId, GenericQuestSignal(capture.facts.questId));
            DlcContext.SetOdysseyMechhiveCompletionOverrideForTests(
                (instance, captured, questObserved) => null);

            PostfixMethod.Invoke(null, new[] { marker, (object)capture });

            scope.RequireNoEventForTestPawns(OdysseyMechhiveEventDefNames.Outcome);
            PawnDiaryRimTestScope.Require(
                !scope.Component.HasCommittedOdysseyMechhiveOutcome
                    && OdysseyMechhiveOutcomeScope.CountForTests == 0,
                "Failed O3 verification committed state or leaked its ownership frame.");
        }

        /// <summary>A committed normalized token survives Scribe and prevents replay capture.</summary>
        [Test]
        public static void SaveRoundTripAndReplayBarrier()
        {
            if (!RequireOdyssey()) return;
            PawnDiaryRimTestScope.Require(
                ExposeOdysseyDataMethod != null && BootstrapOdysseyMethod != null
                    && OutcomeStateField != null && PrefixMethod != null,
                "Could not resolve actual Odyssey persistence/prefix adapters.");
            OutcomeStateField.SetValue(scope.Component,
                OdysseyMechhiveOutcomeState.FromSnapshot(
                    new OdysseyMechhiveOutcomeSnapshot
                    {
                        outcomeToken = OdysseyMechhiveOutcomeTokens.Scavenged,
                        actorPawnId = "Pawn_O3_Save",
                        questId = 990005,
                        committedTick = 4500,
                        eventId = "event-o3-save"
                    }));

            string xml = RoundTripActualOdysseyState();
            OdysseyMechhiveOutcomeSnapshot loaded =
                scope.Component.OdysseyMechhiveOutcomeSnapshotForTests();
            PawnDiaryRimTestScope.Require(
                xml.IndexOf(OdysseySaveKeys.MechhiveOutcome, StringComparison.Ordinal) >= 0
                    && loaded != null
                    && loaded.outcomeToken == OdysseyMechhiveOutcomeTokens.Scavenged
                    && loaded.actorPawnId == "Pawn_O3_Save"
                    && loaded.questId == 990005
                    && loaded.eventId == "event-o3-save",
                "The normalized O3 terminal row did not round-trip exactly.");
            scope.RequireNoNewEvent(() => BootstrapOdysseyMethod.Invoke(scope.Component, null));

            object[] prefixArgs = { new object(), false, null };
            PrefixMethod.Invoke(null, prefixArgs);
            PawnDiaryRimTestScope.Require(prefixArgs[2] == null
                    && OdysseyMechhiveOutcomeScope.CountForTests == 0,
                "A reloaded committed O3 terminal opened a replay ownership frame.");
        }

        /// <summary>Lifecycle reset clears both the scope and the null-by-default test seam.</summary>
        [Test]
        public static void LifecycleResetClearsTransientState()
        {
            if (!RequireOdyssey()) return;
            Pawn actor = CreateEligiblePawn();
            object marker = new object();
            OdysseyMechhiveOutcomeCapture capture = Capture(marker, actor,
                OdysseyMechhiveOutcomeTokens.Destroyed, 990006);
            OdysseyMechhiveOutcomeScope.Begin(capture, 8);
            DlcContext.SetOdysseyMechhiveCompletionOverrideForTests(
                (instance, captured, questObserved) => captured.facts);

            OdysseyTransientState.Reset();

            PawnDiaryRimTestScope.Require(
                OdysseyMechhiveOutcomeScope.CountForTests == 0,
                "Odyssey lifecycle reset leaked a terminal frame.");
            OdysseyMechhiveOutcomeFacts facts;
            PawnDiaryRimTestScope.Require(
                !DlcContext.TryCompleteOdysseyMechhiveOutcome(
                    marker, capture, true, out facts),
                "Odyssey lifecycle reset left its loaded-test completion seam installed.");
        }

        private static void RunSuccessfulOutcome(string outcome)
        {
            Pawn actor = CreateEligiblePawn();
            object marker = new object();
            int questId = outcome == OdysseyMechhiveOutcomeTokens.Destroyed ? 990001 : 990002;
            OdysseyMechhiveOutcomeCapture capture = Capture(marker, actor, outcome, questId);
            capture.suppressesQuestSuccess = true;
            PawnDiaryRimTestScope.Require(
                OdysseyMechhiveOutcomeScope.Begin(capture, 8),
                "The successful O3 fixture could not open its exact frame.");
            PawnDiaryRimTestScope.Require(
                OdysseyMechhiveOutcomeScope.TryDeferQuestSuccess(
                    questId, GenericQuestSignal(questId)),
                "The successful O3 fixture did not defer exact generic Quest success.");
            DlcContext.SetOdysseyMechhiveCompletionOverrideForTests(
                (instance, captured, questObserved) =>
                {
                    OdysseyMechhiveOutcomeFacts facts = CopyFacts(captured.facts);
                    facts.methodReturnedNormally = true;
                    facts.questSuccessObserved = questObserved;
                    return facts;
                });

            DiaryEvent page = scope.FireAndRequireEvent(
                () => PostfixMethod.Invoke(null, new[] { marker, (object)capture }),
                OdysseyMechhiveEventDefNames.Outcome,
                actor,
                null,
                rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(page, actor);
            RequireContext(page, "odyssey_kind=mechhive_outcome");
            RequireContext(page, "mechhive_outcome=" + outcome);
            RequireContext(page, "quest_root=" + OdysseyMechhiveSourceTokens.QuestRootDefName);
            RequireContext(page, "terminal=true");

            OdysseyMechhiveOutcomeSnapshot saved =
                scope.Component.OdysseyMechhiveOutcomeSnapshotForTests();
            PawnDiaryRimTestScope.Require(saved != null
                    && saved.outcomeToken == outcome
                    && saved.actorPawnId == actor.GetUniqueLoadID()
                    && saved.questId == questId
                    && saved.eventId == (page.eventId ?? string.Empty),
                "O3 terminal source, actor, quest, or event identity was not committed exactly.");
            string arcKey = OdysseyArcKeys.MechhiveOutcome(questId);
            System.Collections.Generic.List<NarrativeEvidence> evidence =
                page.NarrativeEvidenceForRole(DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(evidence.Count == 1
                    && evidence[0].phase == outcome
                    && evidence[0].arcKey == arcKey
                    && evidence[0].salience == NarrativeSalienceTokens.Terminal,
                "The Mechhive page lost its exact source-owned reflection evidence.");
            scope.RequirePendingMajorArc(actor, page.eventId);
            PawnDiaryRimTestScope.Require(
                OdysseyMechhiveOutcomeScope.CountForTests == 0,
                "The successful O3 fixture leaked its deferred Quest frame.");
        }

        private static OdysseyMechhiveOutcomeCapture Capture(
            object marker,
            Pawn actor,
            string outcome,
            int questId)
        {
            return new OdysseyMechhiveOutcomeCapture
            {
                coreComp = marker,
                actor = actor,
                facts = new OdysseyMechhiveOutcomeFacts
                {
                    actorPawnId = actor.GetUniqueLoadID(),
                    actorLabel = actor.LabelShortCap,
                    actorSummary = "fixture operator",
                    setting = "fixture core chamber",
                    outcomeToken = outcome,
                    questRootDefName = OdysseyMechhiveSourceTokens.QuestRootDefName,
                    questId = questId,
                    tick = Find.TickManager?.TicksGame ?? 0,
                    playerVisible = true,
                    actorEligible = true
                }
            };
        }

        private static OdysseyMechhiveOutcomeFacts CopyFacts(
            OdysseyMechhiveOutcomeFacts source)
        {
            return new OdysseyMechhiveOutcomeFacts
            {
                actorPawnId = source.actorPawnId,
                actorLabel = source.actorLabel,
                actorSummary = source.actorSummary,
                setting = source.setting,
                outcomeToken = source.outcomeToken,
                questRootDefName = source.questRootDefName,
                questId = source.questId,
                tick = source.tick,
                playerVisible = source.playerVisible,
                actorEligible = source.actorEligible
            };
        }

        private static QuestFanoutSignal GenericQuestSignal(int questId)
        {
            return new QuestFanoutSignal(
                new Quest { id = questId },
                QuestEventData.SignalCompleted,
                "PawnDiary.Event.QuestCompleted");
        }

        private static Pawn CreateEligiblePawn()
        {
            Pawn pawn = scope.CreateAdultColonist();
            scope.SpawnAsLiveColonist(pawn);
            return pawn;
        }

        private static void RequireContext(DiaryEvent page, string token)
        {
            PawnDiaryRimTestScope.Require(page != null
                    && (page.gameContext ?? string.Empty).Contains(token),
                "O3 terminal event context omitted exact token '" + token + "'.");
        }

        private static bool RequireOdyssey()
        {
            if (ModsConfig.OdysseyActive) return true;
            PawnDiaryRimTestScope.Require(!OdysseyMechhiveOutcomePatch.HookReady,
                "O3 terminal capture remained live without Odyssey.");
            return false;
        }

        private static bool OwnedPatch(Patch patch)
        {
            return patch.owner == "aimml.pawndiary"
                && patch.PatchMethod?.DeclaringType == typeof(OdysseyMechhiveOutcomePatch);
        }

        private static string RoundTripActualOdysseyState()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pawndiary_odyssey_mechhive_" + Guid.NewGuid().ToString("N") + ".xml");
            scribeTarget = scope.Component;
            try
            {
                ActualOdysseyStateAdapter save = new ActualOdysseyStateAdapter();
                Scribe.saver.InitSaving(path, "root");
                Scribe_Deep.Look(ref save, "obj");
                Scribe.saver.FinalizeSaving();
                string xml = System.IO.File.ReadAllText(path);

                OutcomeStateField.SetValue(scope.Component, null);
                ActualOdysseyStateAdapter loaded = null;
                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "obj");
                Scribe.loader.FinalizeLoading();
                PawnDiaryRimTestScope.Require(loaded != null,
                    "The actual Odyssey state adapter loaded null.");
                return xml;
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
                scribeTarget = null;
                try
                {
                    if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                }
                catch
                {
                    // Best-effort temp cleanup must not hide the Scribe assertion.
                }
            }
        }

        private sealed class ActualOdysseyStateAdapter : IExposable
        {
            public void ExposeData()
            {
                PawnDiaryRimTestScope.Require(
                    scribeTarget != null && ExposeOdysseyDataMethod != null,
                    "The actual Odyssey Scribe method has no live component target.");
                ExposeOdysseyDataMethod.Invoke(scribeTarget, null);
            }
        }
    }
}

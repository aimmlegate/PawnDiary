// Loaded-game Anomaly A3.0 fixtures for the terminal void answer. The real EmbraceTheVoid /
// DisruptTheLink methods are colony-ending (they show credits, end the monolith quest, and collapse
// the monolith), so these fixtures drive Pawn Diary's exact prefix -> deferred Tale -> postfix path on
// disposable pawns while substituting the reached monolith level through a null-by-default test seam.
// They verify the exact terminal level, one ending page, deferred-Tale ownership, fail-open on
// verification failure, the unscoped fallback route, repeat-call safety, and save round-trip silence.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using RimTestRedux;
using RimWorld;
using RimWorld.Utility;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Exercises the exact A3.0 terminal void ownership transaction.</summary>
    [TestSuite]
    public static class PawnDiaryVoidOutcomeFlowTests
    {
        private const string LogPrefix = "[PawnDiary RimTest Anomaly void] ";
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly MethodInfo EmbracePrefixMethod = typeof(DiaryAnomalyPatches)
            .GetMethod("VoidEmbracePrefix", PrivateStatic);
        private static readonly MethodInfo DisruptPrefixMethod = typeof(DiaryAnomalyPatches)
            .GetMethod("VoidDisruptPrefix", PrivateStatic);
        private static readonly MethodInfo PostfixMethod = typeof(DiaryAnomalyPatches)
            .GetMethod("VoidOutcomePostfix", PrivateStatic);
        private static readonly MethodInfo FinalizerMethod = typeof(DiaryAnomalyPatches)
            .GetMethod("VoidOutcomeFinalizer", PrivateStatic);
        private static readonly MethodInfo ExposeAnomalyDataMethod =
            typeof(DiaryGameComponent).GetMethod("ExposeAnomalyData", PrivateInstance);
        private static readonly MethodInfo BootstrapAnomalyMethod =
            typeof(DiaryGameComponent).GetMethod("BootstrapAnomalyForLoadedSave", PrivateInstance);
        private static readonly FieldInfo TerminalOutcomeField =
            typeof(DiaryGameComponent).GetField("anomalyTerminalOutcome", PrivateInstance);
        private static readonly FieldInfo TerminalActorField =
            typeof(DiaryGameComponent).GetField("anomalyTerminalActorPawnId", PrivateInstance);
        private static readonly FieldInfo TerminalEventIdField =
            typeof(DiaryGameComponent).GetField("anomalyTerminalEventId", PrivateInstance);
        private static readonly FieldInfo TalesListField = ResolveTalesListField();

        private static PawnDiaryRimTestScope scope;
        private static DiaryAnomalyPolicyDef policyDef;
        private static bool originalEnabled;
        private static PromptContextDetailLevel originalContextDetailLevel;
        private static HashSet<Tale> originalTales;
        private static string savedTerminalOutcome;
        private static string savedTerminalActor;
        private static string savedTerminalEventId;
        private static DiaryGameComponent scribeTarget;

        /// <summary>Snapshots shared policy/terminal/Tale state and enables only the void group.</summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("anomalyVoidOutcome", "reflection");
            scope.OwnDiaryEventsCreatedAfterThisPoint();
            policyDef = DefDatabase<DiaryAnomalyPolicyDef>.GetNamedSilentFail(
                DiaryAnomalyPolicy.DefName);
            PawnDiaryRimTestScope.Require(policyDef != null,
                "The Anomaly narrative policy Def was not loaded.");
            originalEnabled = policyDef.voidOutcomeEnabled;
            originalContextDetailLevel = PawnDiaryMod.Settings.contextDetailLevel;
            PawnDiaryMod.Settings.contextDetailLevel = PromptContextDetailLevel.Full;
            originalTales = SnapshotTales();
            // The terminal token lives on the live component; snapshot and restore it so a fixture that
            // commits an ending never leaves a fake terminal outcome in the player's save.
            savedTerminalOutcome = TerminalOutcomeField?.GetValue(scope.Component) as string;
            savedTerminalActor = TerminalActorField?.GetValue(scope.Component) as string;
            savedTerminalEventId = TerminalEventIdField?.GetValue(scope.Component) as string;
            policyDef.voidOutcomeEnabled = true;
            DiaryTuningDef tuning = DiaryTuning.Current;
            bool originalArcReflection = tuning.arcReflectionEnabled;
            tuning.arcReflectionEnabled = true;
            scope.RegisterCleanup(() => tuning.arcReflectionEnabled = originalArcReflection);
            DlcContext.SetMonolithLevelOverrideForTests(null);
            AnomalyTransientState.Reset();
        }

        /// <summary>Restores policy, terminal token, level override, scopes, Tales, and pawns.</summary>
        [AfterEach]
        public static void TearDown()
        {
            Exception firstFailure = null;
            try
            {
                DlcContext.SetMonolithLevelOverrideForTests(null);
                AnomalyTransientState.Reset();
                if (policyDef != null) policyDef.voidOutcomeEnabled = originalEnabled;
                if (PawnDiaryMod.Settings != null)
                    PawnDiaryMod.Settings.contextDetailLevel = originalContextDetailLevel;
                TerminalOutcomeField?.SetValue(scope.Component, savedTerminalOutcome ?? string.Empty);
                TerminalActorField?.SetValue(scope.Component, savedTerminalActor ?? string.Empty);
                TerminalEventIdField?.SetValue(scope.Component, savedTerminalEventId ?? string.Empty);
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
                if (Scribe.mode != LoadSaveMode.Inactive) Scribe.ForceStop();
                scribeTarget = null;
                originalTales = null;
                policyDef = null;
                scope = null;
            }
            if (firstFailure != null) throw firstFailure;
        }

        /// <summary>Proves off-DLC inertness and the exact installed 1.6 terminal signatures/patches.</summary>
        [Test]
        public static void ExactRegistrationAndNoDlcGate()
        {
            bool active = ModsConfig.AnomalyActive;
            PawnDiaryRimTestScope.Require(
                DiaryAnomalyPatches.VoidOutcomeHookReady == active,
                "Terminal void hook readiness did not match Anomaly availability.");
            if (!active)
            {
                Log.Message(LogPrefix + "exact terminal void hooks: not applicable (Anomaly inactive).");
                return;
            }

            RequireTerminalPatched("EmbraceTheVoid");
            RequireTerminalPatched("DisruptTheLink");
        }

        /// <summary>A verified embrace records exactly one actor-authored terminal page.</summary>
        [Test]
        public static void EmbraceCreatesExactlyOneTerminalPage()
        {
            if (!RequireAnomalyOrReport(nameof(EmbraceCreatesExactlyOneTerminalPage))) return;
            RunSuccessfulTerminal(
                EmbracePrefixMethod,
                AnomalyOutcomeTokens.Embraced,
                AnomalyVoidOutcomePolicy.EmbracedLevelDefName,
                AnomalyVoidTaleOwnershipPolicy.EmbracedTheVoidDefName);
        }

        /// <summary>A verified disruption records exactly one actor-authored terminal page.</summary>
        [Test]
        public static void DisruptCreatesExactlyOneTerminalPage()
        {
            if (!RequireAnomalyOrReport(nameof(DisruptCreatesExactlyOneTerminalPage))) return;
            RunSuccessfulTerminal(
                DisruptPrefixMethod,
                AnomalyOutcomeTokens.Disrupted,
                AnomalyVoidOutcomePolicy.DisruptedLevelDefName,
                AnomalyVoidTaleOwnershipPolicy.ClosedTheVoidDefName);
        }

        /// <summary>
        /// A deferred terminal Tale is released and no page is written when vanilla does not reach the
        /// expected terminal level. The colony is never left without its only ending.
        /// </summary>
        [Test]
        public static void FailedVerificationReleasesTaleAndWritesNoPage()
        {
            if (!RequireAnomalyOrReport(nameof(FailedVerificationReleasesTaleAndWritesNoPage))) return;
            Pawn actor = CreateEligiblePawn();
            // Reads the terminal level while the Tale is recorded (so it defers), then a non-terminal
            // level when the postfix verifies (so the deferred Tale must be released fail-open).
            string level = AnomalyVoidOutcomePolicy.EmbracedLevelDefName;
            DlcContext.SetMonolithLevelOverrideForTests(() => level);
            TaleDef tale = RequireTaleDef(AnomalyVoidTaleOwnershipPolicy.EmbracedTheVoidDefName);

            object[] prefixArgs = { actor, null };
            EmbracePrefixMethod.Invoke(null, prefixArgs);
            VoidOutcomeCapture capture = prefixArgs[1] as VoidOutcomeCapture;
            PawnDiaryRimTestScope.Require(capture != null,
                "The terminal fixture did not open an exact void scope.");
            TaleRecorder.RecordTale(tale, actor);
            PawnDiaryRimTestScope.Require(VoidOutcomeScope.HasDeferredTaleForTests,
                "The terminal fixture did not defer its exact single-pawn void Tale.");

            level = "Waking";
            PostfixMethod.Invoke(null, new object[] { actor, capture });

            scope.RequireNoEventForTestPawns(AnomalyEventDefNames.VoidOutcome);
            PawnDiaryRimTestScope.Require(!scope.Component.HasCommittedVoidOutcome,
                "A failed terminal verification wrongly committed the outcome.");
            PawnDiaryRimTestScope.Require(VoidOutcomeScope.CountForTests == 0,
                "The failed terminal verification leaked its ownership scope.");
        }

        /// <summary>An exceptional unwind releases the deferred Tale and clears the frame.</summary>
        [Test]
        public static void FinalizerFailsOpenAndClearsScope()
        {
            if (!RequireAnomalyOrReport(nameof(FinalizerFailsOpenAndClearsScope))) return;
            PawnDiaryRimTestScope.Require(EmbracePrefixMethod != null && FinalizerMethod != null,
                "Could not resolve Pawn Diary's terminal void prefix/finalizer callbacks.");
            Pawn actor = CreateEligiblePawn();
            DlcContext.SetMonolithLevelOverrideForTests(
                () => AnomalyVoidOutcomePolicy.EmbracedLevelDefName);
            TaleDef tale = RequireTaleDef(AnomalyVoidTaleOwnershipPolicy.EmbracedTheVoidDefName);
            Exception original = new InvalidOperationException("A3.0 finalizer fixture");

            object[] prefixArgs = { actor, null };
            EmbracePrefixMethod.Invoke(null, prefixArgs);
            VoidOutcomeCapture capture = prefixArgs[1] as VoidOutcomeCapture;
            PawnDiaryRimTestScope.Require(capture != null,
                "The exceptional fixture did not open an exact void scope.");
            TaleRecorder.RecordTale(tale, actor);
            PawnDiaryRimTestScope.Require(VoidOutcomeScope.HasDeferredTaleForTests,
                "The exceptional fixture did not park its exact terminal Tale.");

            object returned = FinalizerMethod.Invoke(null, new object[] { original, capture });
            PawnDiaryRimTestScope.Require(ReferenceEquals(returned, original),
                "The finalizer did not preserve vanilla's original exception.");
            scope.RequireNoEventForTestPawns(AnomalyEventDefNames.VoidOutcome);
            PawnDiaryRimTestScope.Require(!scope.Component.HasCommittedVoidOutcome
                    && VoidOutcomeScope.CountForTests == 0,
                "The terminal finalizer committed an outcome or leaked its frame.");
        }

        /// <summary>An unscoped terminal Tale (patch unavailable) is never intercepted by A3.0.</summary>
        [Test]
        public static void UnscopedTerminalTaleUsesGenericRoute()
        {
            if (!RequireAnomalyOrReport(nameof(UnscopedTerminalTaleUsesGenericRoute))) return;
            Pawn actor = CreateEligiblePawn();
            TaleDef tale = RequireTaleDef(AnomalyVoidTaleOwnershipPolicy.EmbracedTheVoidDefName);
            // No prefix ran, so no correlation exists. Recording the terminal Tale must neither defer
            // into an A3.0 scope nor manufacture a VoidOutcome page; vanilla's route is untouched.
            TaleRecorder.RecordTale(tale, actor);
            PawnDiaryRimTestScope.Require(
                !VoidOutcomeScope.HasActiveFrame && VoidOutcomeScope.CountForTests == 0,
                "An unscoped terminal Tale opened or fed an A3.0 ownership scope.");
            scope.RequireNoEventForTestPawns(AnomalyEventDefNames.VoidOutcome);
            PawnDiaryRimTestScope.Require(!scope.Component.HasCommittedVoidOutcome,
                "An unscoped terminal Tale wrongly committed a terminal outcome.");
        }

        /// <summary>Once a terminal answer is committed, a repeat call opens no second correlation.</summary>
        [Test]
        public static void RepeatCallCreatesNoSecondPage()
        {
            if (!RequireAnomalyOrReport(nameof(RepeatCallCreatesNoSecondPage))) return;
            Pawn actor = CreateEligiblePawn();
            DlcContext.SetMonolithLevelOverrideForTests(
                () => AnomalyVoidOutcomePolicy.EmbracedLevelDefName);
            DiaryEvent first = scope.FireAndRequireEvent(
                () => DriveTerminal(
                    EmbracePrefixMethod, actor,
                    AnomalyVoidTaleOwnershipPolicy.EmbracedTheVoidDefName),
                AnomalyEventDefNames.VoidOutcome,
                actor,
                null,
                rejectOtherTestPawnEvents: true);
            PawnDiaryRimTestScope.Require(scope.Component.HasCommittedVoidOutcome,
                "The first terminal answer did not commit its outcome.");

            // A second call sees a committed terminal, so its prefix opens no scope and the ordinary
            // terminal Tale route resumes; no second VoidOutcome page can be manufactured.
            object[] prefixArgs = { actor, null };
            EmbracePrefixMethod.Invoke(null, prefixArgs);
            VoidOutcomeCapture repeat = prefixArgs[1] as VoidOutcomeCapture;
            PawnDiaryRimTestScope.Require(repeat == null,
                "A repeat terminal call opened a second correlation after a committed outcome.");
            AnomalyPersistentStateSnapshot snapshot = scope.Component.AnomalyStateSnapshotForTests();
            PawnDiaryRimTestScope.Require(
                snapshot.terminalEventId == (first.eventId ?? string.Empty)
                    && snapshot.terminalActorPawnId == actor.GetUniqueLoadID(),
                "A repeat terminal call changed the committed terminal identity.");
        }

        /// <summary>
        /// An active terminal frame owns the monolith quest-success restatement for its exact quest id
        /// only, RecordQuestEnded emits nothing for that owned success, and a closed scope owns nothing.
        /// </summary>
        [Test]
        public static void QuestSuccessSuppressionIsExactAndScopeBounded()
        {
            if (!RequireAnomalyOrReport(nameof(QuestSuccessSuppressionIsExactAndScopeBounded))) return;
            Pawn actor = CreateEligiblePawn();
            DlcContext.SetMonolithLevelOverrideForTests(
                () => AnomalyVoidOutcomePolicy.EmbracedLevelDefName);

            object[] prefixArgs = { actor, null };
            EmbracePrefixMethod.Invoke(null, prefixArgs);
            VoidOutcomeCapture capture = prefixArgs[1] as VoidOutcomeCapture;
            PawnDiaryRimTestScope.Require(capture != null,
                "The quest-suppression fixture did not open an exact void scope.");
            PawnDiaryRimTestScope.Require(capture.suppressesQuestSuccess,
                "An eligible actor with void output and the group enabled did not predict a page.");

            // The fixture game has no live terminal monolith quest, so inject the exact id the prefix
            // would have captured from Find.Anomaly.monolith.quest into the already-open frame.
            capture.monolithQuestId = 941001;
            PawnDiaryRimTestScope.Require(VoidOutcomeScope.OwnsQuestId(941001),
                "The active terminal frame did not own its exact monolith quest id.");
            PawnDiaryRimTestScope.Require(
                !VoidOutcomeScope.OwnsQuestId(941002) && !VoidOutcomeScope.OwnsQuestId(0),
                "Terminal quest ownership leaked beyond the exact captured quest id.");

            Quest owned = new Quest { id = 941001 };
            scope.RequireNoNewEvent(
                () => scope.Component.RecordQuestEnded(owned, QuestEndOutcome.Success));

            // Fail-open closure: an aborted frame stops owning the quest id immediately.
            FinalizerMethod.Invoke(null, new object[] { null, capture });
            PawnDiaryRimTestScope.Require(VoidOutcomeScope.CountForTests == 0,
                "The quest-suppression fixture leaked its terminal frame.");
            PawnDiaryRimTestScope.Require(!VoidOutcomeScope.OwnsQuestId(941001),
                "A closed terminal scope kept owning the monolith quest id.");
        }

        /// <summary>
        /// A disabled void interaction group predicts no dedicated page at prefix time, so the frame
        /// never claims the monolith quest-success restatement it would not replace.
        /// </summary>
        [Test]
        public static void DisabledGroupNeverClaimsQuestSuccess()
        {
            if (!RequireAnomalyOrReport(nameof(DisabledGroupNeverClaimsQuestSuccess))) return;
            Pawn actor = CreateEligiblePawn();
            DlcContext.SetMonolithLevelOverrideForTests(
                () => AnomalyVoidOutcomePolicy.EmbracedLevelDefName);
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyAnomalyEvent(
                AnomalyEventDefNames.VoidOutcome);
            PawnDiaryRimTestScope.Require(group != null,
                "The void outcome interaction group was not loaded.");
            PawnDiaryMod.Settings.SetGroupEnabled(group.defName, false);

            object[] prefixArgs = { actor, null };
            EmbracePrefixMethod.Invoke(null, prefixArgs);
            VoidOutcomeCapture capture = prefixArgs[1] as VoidOutcomeCapture;
            PawnDiaryRimTestScope.Require(capture != null,
                "A disabled group must still open the Tale-ownership scope (fail-open release).");
            PawnDiaryRimTestScope.Require(!capture.suppressesQuestSuccess,
                "A disabled void group wrongly claimed the monolith quest-success restatement.");
            capture.monolithQuestId = 941003;
            PawnDiaryRimTestScope.Require(!VoidOutcomeScope.OwnsQuestId(941003),
                "An unclaiming terminal frame still owned its monolith quest id.");

            FinalizerMethod.Invoke(null, new object[] { null, capture });
            PawnDiaryRimTestScope.Require(VoidOutcomeScope.CountForTests == 0,
                "The disabled-group fixture leaked its terminal frame.");
        }

        /// <summary>A committed terminal token round-trips and an old-save bootstrap never replays it.</summary>
        [Test]
        public static void SaveRoundTripAndOldSaveBootstrapDoNotReplay()
        {
            if (!RequireAnomalyOrReport(nameof(SaveRoundTripAndOldSaveBootstrapDoNotReplay))) return;
            PawnDiaryRimTestScope.Require(
                ExposeAnomalyDataMethod != null && BootstrapAnomalyMethod != null,
                "Could not resolve the actual Anomaly save/bootstrap adapters.");
            TerminalOutcomeField.SetValue(scope.Component, AnomalyOutcomeTokens.Disrupted);
            TerminalActorField.SetValue(scope.Component, "Pawn_TerminalActorFixture");
            TerminalEventIdField.SetValue(scope.Component, "evt-terminal-fixture");

            string savedXml = RoundTripActualAnomalyState();
            PawnDiaryRimTestScope.Require(
                savedXml.IndexOf(AnomalySaveKeys.TerminalOutcome, StringComparison.Ordinal) >= 0
                    && savedXml.IndexOf(AnomalyOutcomeTokens.Disrupted, StringComparison.Ordinal) >= 0,
                "The committed terminal outcome did not round-trip through the actual Scribe.");
            AnomalyPersistentStateSnapshot loaded = scope.Component.AnomalyStateSnapshotForTests();
            PawnDiaryRimTestScope.Require(
                loaded.terminalOutcome == AnomalyOutcomeTokens.Disrupted
                    && loaded.terminalActorPawnId == "Pawn_TerminalActorFixture"
                    && loaded.terminalEventId == "evt-terminal-fixture",
                "The reloaded terminal outcome lost or corrupted its committed identity.");
            scope.RequireNoNewEvent(() => BootstrapAnomalyMethod.Invoke(scope.Component, null));
        }

        // A successful terminal drives the exact prefix -> deferred Tale -> postfix path and asserts one
        // actor-authored ending page, the committed terminal token, and full generic-Tale suppression.
        private static void RunSuccessfulTerminal(
            MethodInfo prefixMethod,
            string outcome,
            string expectedLevel,
            string taleDefName)
        {
            Pawn actor = CreateEligiblePawn();
            DlcContext.SetMonolithLevelOverrideForTests(() => expectedLevel);

            DiaryEvent page = scope.FireAndRequireEvent(
                () => DriveTerminal(prefixMethod, actor, taleDefName),
                AnomalyEventDefNames.VoidOutcome,
                actor,
                null,
                rejectOtherTestPawnEvents: true);
            scope.RequireSoloRef(page, actor);
            RequireContext(page, "anomaly_kind=void_outcome");
            RequireContext(page, "void_outcome=" + outcome);
            RequireContext(page, "monolith_level=" + expectedLevel);
            RequireContext(page, "terminal=true");
            RequireContext(page, "actor=" + actor.LabelShortCap);
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(page.initiatorText)
                    && page.initiatorText.Contains(actor.LabelShortCap),
                "The localized terminal fallback lost the actor label.");

            AnomalyPersistentStateSnapshot snapshot = scope.Component.AnomalyStateSnapshotForTests();
            PawnDiaryRimTestScope.Require(
                scope.Component.HasCommittedVoidOutcome
                    && snapshot.terminalOutcome == outcome
                    && snapshot.terminalActorPawnId == actor.GetUniqueLoadID()
                    && snapshot.terminalEventId == (page.eventId ?? string.Empty),
                "The terminal outcome, actor, or event id was not committed exactly once.");
            List<NarrativeEvidence> evidence =
                page.NarrativeEvidenceForRole(DiaryEvent.InitiatorRole);
            PawnDiaryRimTestScope.Require(evidence.Count == 1
                    && evidence[0].phase == outcome
                    && evidence[0].arcKey == AnomalyVoidOutcomePolicy.NarrativeArcKey
                    && evidence[0].salience == NarrativeSalienceTokens.Terminal,
                "The terminal void page lost its exact source-owned reflection evidence.");
            scope.RequirePendingMajorArc(actor, page.eventId);
        }

        // Runs the exact prefix, records the single-pawn terminal Tale (which the active scope defers),
        // then runs the postfix. Vanilla's destructive side effects are intentionally not invoked.
        private static void DriveTerminal(MethodInfo prefixMethod, Pawn actor, string taleDefName)
        {
            object[] prefixArgs = { actor, null };
            prefixMethod.Invoke(null, prefixArgs);
            VoidOutcomeCapture capture = prefixArgs[1] as VoidOutcomeCapture;
            PawnDiaryRimTestScope.Require(capture != null,
                "The terminal fixture did not open an exact void scope.");
            TaleDef tale = RequireTaleDef(taleDefName);
            TaleRecorder.RecordTale(tale, actor);
            PawnDiaryRimTestScope.Require(VoidOutcomeScope.HasDeferredTaleForTests,
                "The terminal fixture did not defer its exact single-pawn void Tale.");
            PostfixMethod.Invoke(null, new object[] { actor, capture });
        }

        private static void RequireTerminalPatched(string methodName)
        {
            MethodInfo method = AccessTools.DeclaredMethod(
                typeof(VoidAwakeningUtility), methodName, new[] { typeof(Pawn) }) as MethodInfo;
            Patches patches = method == null ? null : Harmony.GetPatchInfo(method);
            PawnDiaryRimTestScope.Require(method != null && method.IsStatic && method.IsPublic
                    && method.ReturnType == typeof(void)
                    && patches != null
                    && patches.Prefixes.Any(OwnedPatch)
                    && patches.Postfixes.Any(OwnedPatch)
                    && patches.Finalizers.Any(OwnedPatch),
                "The exact VoidAwakeningUtility." + methodName
                    + "(Pawn) signature lacks Pawn Diary's patch set.");
        }

        private static Pawn CreateEligiblePawn()
        {
            Pawn pawn = scope.CreateAdultColonist();
            scope.SpawnAsLiveColonist(pawn);
            return pawn;
        }

        private static TaleDef RequireTaleDef(string defName)
        {
            TaleDef tale = DefDatabase<TaleDef>.GetNamedSilentFail(defName);
            PawnDiaryRimTestScope.Require(tale != null,
                "The terminal void Tale '" + defName + "' was not loaded.");
            return tale;
        }

        private static void RequireContext(DiaryEvent page, string token)
        {
            PawnDiaryRimTestScope.Require(page != null
                    && (page.gameContext ?? string.Empty).Contains(token),
                "Terminal void event context omitted exact token '" + token + "'.");
        }

        private static bool RequireAnomalyOrReport(string testName)
        {
            if (ModsConfig.AnomalyActive) return true;
            PawnDiaryRimTestScope.Require(!DiaryAnomalyPatches.VoidOutcomeHookReady,
                "Terminal void capture remained live without Anomaly.");
            Log.Message(LogPrefix + testName + ": not applicable (Anomaly inactive).");
            return false;
        }

        private static bool OwnedPatch(Patch patch)
        {
            return patch.owner == "aimml.pawndiary"
                && patch.PatchMethod?.DeclaringType == typeof(DiaryAnomalyPatches);
        }

        private static string RoundTripActualAnomalyState()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pawndiary_anomaly_void_" + Guid.NewGuid().ToString("N") + ".xml");
            scribeTarget = scope.Component;
            try
            {
                ActualAnomalyStateAdapter save = new ActualAnomalyStateAdapter();
                Scribe.saver.InitSaving(path, "root");
                Scribe_Deep.Look(ref save, "obj");
                Scribe.saver.FinalizeSaving();
                string xml = System.IO.File.ReadAllText(path);

                ActualAnomalyStateAdapter loaded = null;
                Scribe.loader.InitLoading(path);
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe_Deep.Look(ref loaded, "obj");
                Scribe.loader.FinalizeLoading();
                PawnDiaryRimTestScope.Require(loaded != null,
                    "The actual Anomaly state adapter loaded null.");
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

        private sealed class ActualAnomalyStateAdapter : IExposable
        {
            public void ExposeData()
            {
                PawnDiaryRimTestScope.Require(scribeTarget != null && ExposeAnomalyDataMethod != null,
                    "The actual Anomaly Scribe method has no live component target.");
                ExposeAnomalyDataMethod.Invoke(scribeTarget, null);
            }
        }

        private static FieldInfo ResolveTalesListField()
        {
            FieldInfo[] fields = typeof(TaleManager).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return fields.FirstOrDefault(field => typeof(IList).IsAssignableFrom(field.FieldType)
                && field.FieldType.IsGenericType
                && field.FieldType.GetGenericArguments()[0] == typeof(Tale));
        }

        private static HashSet<Tale> SnapshotTales()
        {
            HashSet<Tale> result = new HashSet<Tale>();
            IList rows = TalesListField?.GetValue(Find.TaleManager) as IList;
            if (rows == null) return result;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] is Tale tale) result.Add(tale);
            return result;
        }

        private static void RemoveFixtureTales()
        {
            if (originalTales == null) return;
            IList rows = TalesListField?.GetValue(Find.TaleManager) as IList;
            if (rows == null) return;
            for (int i = rows.Count - 1; i >= 0; i--)
                if (rows[i] is Tale tale && !originalTales.Contains(tale)) rows.RemoveAt(i);
        }
    }
}

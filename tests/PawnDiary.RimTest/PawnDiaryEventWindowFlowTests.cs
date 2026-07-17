// In-game event-window tests for Pawn Diary's XML-driven narrative windows (EVT-22, TEST_COVERAGE_PLAN.md §3).
//
// Event windows turn broad, source-agnostic game signals (incidents, letters, birthdays, spawned things,
// prison breaks, ...) into either a one-shot diary page or a long-lived active window that biases prompt
// enchantments while it stays open. The production entry point every Harmony patch and periodic recorder
// funnels through is DiaryGameComponent.RecordEventWindowSignal(source, defName, signal, label, map,
// subjectPawn), which runs the pure XML trigger match (EventWindowPolicy), the dedup gate, the active-window
// lifecycle, and the per-pawn page emission. Rather than reproduce the vanilla incident/letter/scanner
// plumbing, these tests submit that exact controlled trigger signal directly — the same per-unit work the
// real hooks do — so the whole match/dedup/lifecycle path runs mapless and deterministic. Matching is pure
// string comparison (case-insensitive), so every case here works on a base-game (no-DLC) install; no DLC def
// is referenced.
//
// This suite drives two of the mod's own shipped base-game windows:
//   - "Birthday" (source=PawnAge, signal=birthday): keepActive=false one-shot, recordScope=SubjectPawn, so a
//     single supplied pawn owns the start page and no active window lingers. Covers start page + scope,
//     dedup, and the one-shot property maplessly (SubjectPawn scope returns the passed pawn directly).
//   - "ShortCircuitAftermath" (source=Incident, signal=executed, defName=ShortCircuit): keepActive=true with a
//     timeout and recordStartEvent=false, so a start opens a decaying prompt-bias window that emits no page.
//     Covers the active-window open + scheduled timeout, timeout close, and no-reopen-while-active.
//
// The active-window list (activeEventWindows) and the event-window dedup map (recentEventWindowEvents) are
// PRIVATE component stores the shared harness does NOT own, so this suite snapshots them in SetUp and removes
// exactly the entries/keys it added in a RegisterCleanup, reported in harnessExtensionsNeeded. No test ever
// enables per-pawn generation, so no LLM request can leave the game.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that a controlled event-window trigger signal, run through the real
    /// <see cref="DiaryGameComponent.RecordEventWindowSignal"/> path, (a) emits a subject-scoped start page
    /// for a one-shot window, (b) opens a persistent active window with the scheduled timeout for a
    /// keepActive window, (c) does not re-fire (one-shot leaves no window; keepActive does not reopen while
    /// active), and (d) deduplicates a repeated signal and closes an expired window on the timeout scan.
    /// These tests require a loaded game because the capture pipeline intentionally ignores events at the
    /// main menu.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryEventWindowFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        // Private component stores the shared harness does not own. Resolved once; a null handle (field or
        // method renamed) fails the suite loudly in SetUp rather than silently skipping its cleanup.
        private static readonly FieldInfo ActiveWindowsField =
            typeof(DiaryGameComponent).GetField("activeEventWindows", PrivateInstance);
        private static readonly FieldInfo RecentWindowEventsField =
            typeof(DiaryGameComponent).GetField("recentEventWindowEvents", PrivateInstance);
        private static readonly MethodInfo ScanTimeoutsMethod =
            typeof(DiaryGameComponent).GetMethod("ScanEventWindowTimeouts", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn firstPawn;
        private static Pawn secondPawn;

        /// <summary>
        /// Opens a fresh scope (event windows are not interaction-group gated, so no groups are enabled),
        /// creates two isolated generation-disabled colonists, verifies the private-store reflection handles
        /// resolved, and registers a cleanup that removes only the active windows and dedup keys this suite
        /// adds — leaving any of the developer's real windows/keys untouched.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();
            firstPawn = scope.CreateAdultColonist();
            secondPawn = scope.CreateAdultColonist();

            PawnDiaryRimTestScope.Require(
                ActiveWindowsField != null, "Event-window suite could not locate private field 'activeEventWindows'.");
            PawnDiaryRimTestScope.Require(
                RecentWindowEventsField != null,
                "Event-window suite could not locate private field 'recentEventWindowEvents'.");
            PawnDiaryRimTestScope.Require(
                ScanTimeoutsMethod != null,
                "Event-window suite could not locate private method 'ScanEventWindowTimeouts'.");

            RegisterEventWindowStoreCleanup();
        }

        /// <summary>
        /// Restores every mutation (including the removal of this suite's active windows and dedup keys) and
        /// audits that no test-owned event, diary, or log row survived — even when a test threw partway.
        /// </summary>
        [AfterEach]
        public static void TearDown()
        {
            try
            {
                scope?.TearDown();
            }
            finally
            {
                scope = null;
                firstPawn = null;
                secondPawn = null;
            }
        }

        /// <summary>
        /// EVT-22. A one-shot, SubjectPawn-scoped window start (Birthday) emits exactly one solo start page,
        /// owned by the single supplied subject pawn, whose context records the window key, the start phase,
        /// and the originating signal source.
        /// </summary>
        [Test]
        public static void WindowStartEmitsSubjectScopedStartPage()
        {
            RequireWindowDef("Birthday");

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => scope.Component.RecordEventWindowSignal("PawnAge", "Birthday", "birthday", "30", null, firstPawn),
                "Birthday",
                firstPawn,
                null);

            // SubjectPawn scope: exactly the supplied pawn owns the page (FireAndRequireEvent already asserts
            // a single new event initiated by firstPawn), and it is a solo page indexed on that pawn's diary.
            scope.RequireSoloRef(diaryEvent, firstPawn);
            RequireContextContains(diaryEvent, "event_window=Birthday");
            RequireContextContains(diaryEvent, "phase=start");
            RequireContextContains(diaryEvent, "source=PawnAge");
        }

        /// <summary>
        /// EVT-22. A one-shot window (keepActive=false) writes its page but never persists an active window,
        /// so nothing is left to re-fire or bias later prompts.
        /// </summary>
        [Test]
        public static void OneShotWindowCreatesNoLingeringActiveWindow()
        {
            RequireWindowDef("Birthday");

            scope.FireAndRequireEvent(
                () => scope.Component.RecordEventWindowSignal("PawnAge", "Birthday", "birthday", "30", null, firstPawn),
                "Birthday",
                firstPawn,
                null);

            List<ActiveEventWindowState> windows = ActiveWindows();
            bool lingered = windows != null
                && windows.Exists(w => w != null && string.Equals(w.windowDefName, "Birthday", StringComparison.Ordinal));
            PawnDiaryRimTestScope.Require(
                !lingered, "A one-shot event window must not leave a lingering active window.");
        }

        /// <summary>
        /// EVT-22. Submitting the identical start signal again inside the window's dedup window is dropped:
        /// the second submission records no new event for the test pawn.
        /// </summary>
        [Test]
        public static void RepeatedStartSignalIsDeduplicated()
        {
            RequireWindowDef("Birthday");

            scope.FireAndRequireEvent(
                () => scope.Component.RecordEventWindowSignal("PawnAge", "Birthday", "birthday", "30", null, firstPawn),
                "Birthday",
                firstPawn,
                null);

            // Same source/signal/def/subject within dedupTicks -> the dedup gate drops it before any page.
            scope.RequireNoNewEvent(
                () => scope.Component.RecordEventWindowSignal("PawnAge", "Birthday", "birthday", "30", null, firstPawn));
        }

        /// <summary>
        /// EVT-22. A keepActive window start (ShortCircuit) opens exactly one persistent active window scoped
        /// to the signal's map (here map-agnostic, id -1) with its timeout scheduled from the def, which is
        /// the state that biases prompt enchantments while the window is open. recordStartEvent=false, so it
        /// emits no page.
        /// </summary>
        [Test]
        public static void KeepActiveWindowStartOpensActiveWindowWithTimeout()
        {
            DiaryEventWindowDef def = RequireWindowDef("ShortCircuitAftermath");
            HashSet<ActiveEventWindowState> baseline = SnapshotActiveWindows();

            scope.Component.RecordEventWindowSignal("Incident", "ShortCircuit", "executed", "short circuit", null, null);

            ActiveEventWindowState opened = FindNewWindow(baseline, "ShortCircuitAftermath");
            PawnDiaryRimTestScope.Require(
                opened != null, "The keepActive ShortCircuit signal did not open an active event window.");
            PawnDiaryRimTestScope.Require(
                opened.mapUniqueId == -1, "A mapless signal should open a map-agnostic (-1) active window.");
            PawnDiaryRimTestScope.Require(
                opened.expiresTick - opened.startedTick == def.timeoutTicks,
                "The active window did not schedule its configured timeout from the start tick.");
        }

        /// <summary>
        /// EVT-22. Once a keepActive window is past its timeout, the real timeout scan closes it (this def
        /// records no timeout page), clearing the prompt-bias state.
        /// </summary>
        [Test]
        public static void KeepActiveWindowTimeoutClosesActiveWindow()
        {
            RequireWindowDef("ShortCircuitAftermath");
            HashSet<ActiveEventWindowState> baseline = SnapshotActiveWindows();

            scope.Component.RecordEventWindowSignal("Incident", "ShortCircuit", "executed", "short circuit", null, null);
            ActiveEventWindowState opened = FindNewWindow(baseline, "ShortCircuitAftermath");
            PawnDiaryRimTestScope.Require(
                opened != null, "Setup: the ShortCircuit signal did not open an active event window to time out.");

            // Force the window past its expiry and run the production timeout scan.
            opened.expiresTick = Find.TickManager.TicksGame - 1;
            ScanTimeoutsMethod.Invoke(scope.Component, null);

            List<ActiveEventWindowState> after = ActiveWindows();
            PawnDiaryRimTestScope.Require(
                after == null || !after.Contains(opened),
                "The timeout scan did not close the expired active event window.");
        }

        /// <summary>
        /// EVT-22. While a keepActive window is already open (restartOnStart=false), a repeated start signal
        /// does not open a second window — the active-window lifecycle is idempotent.
        /// </summary>
        [Test]
        public static void KeepActiveWindowRepeatedStartDoesNotReopen()
        {
            RequireWindowDef("ShortCircuitAftermath");
            HashSet<ActiveEventWindowState> baseline = SnapshotActiveWindows();

            scope.Component.RecordEventWindowSignal("Incident", "ShortCircuit", "executed", "short circuit", null, null);
            scope.Component.RecordEventWindowSignal("Incident", "ShortCircuit", "executed", "short circuit", null, null);

            int opened = CountNewWindows(baseline, "ShortCircuitAftermath");
            PawnDiaryRimTestScope.Require(
                opened == 1,
                "A restartOnStart=false keepActive window must not open a second active window on a repeated start.");
        }

        /// <summary>
        /// EVT-22 / Master Wave 2. Each exact player-driven monolith level owns one one-shot window.
        /// With Anomaly active the page also freezes one source-owned journey-chapter reference while
        /// keeping prompt context empty (there is deliberately no live Anomaly provider in this wave).
        /// Without Anomaly the same plain-string signals cleanly no-op behind the package gate.
        /// </summary>
        [Test]
        public static void ExactMonolithChaptersEmitOnceWithSourceEvidence()
        {
            string[] windowDefNames =
            {
                "VoidMonolithActivation",
                "VoidMonolithWaking",
                "VoidMonolithVoidAwakened"
            };
            string[] levelDefNames = { "Stirring", "Waking", "VoidAwakened" };
            string[] phases = { "stirring", "waking", "void_awakened" };

            // These XML Defs stay loaded when Anomaly is absent, but MissingRequiredPackage() makes
            // their runtime routes inert. Use the loaded-only helper so this test can exercise that
            // no-DLC branch instead of failing before it reaches the intended assertion.
            DiaryEventWindowDef firstWindow = RequireLoadedWindowDef(windowDefNames[0]);
            bool anomalyAvailable = !firstWindow.MissingRequiredPackage();
            for (int i = 0; i < windowDefNames.Length; i++)
            {
                if (anomalyAvailable)
                    RequireWindowDef(windowDefNames[i]);
                else
                    RequireLoadedWindowDef(windowDefNames[i]);
                string levelDefName = levelDefNames[i];
                if (!anomalyAvailable)
                {
                    scope.RequireNoNewEvent(() => scope.Component.RecordEventWindowSignal(
                        "VoidMonolith", levelDefName, "activated", levelDefName, null, firstPawn));
                    continue;
                }

                DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                    () => scope.Component.RecordEventWindowSignal(
                        "VoidMonolith", levelDefName, "activated", levelDefName, null, firstPawn),
                    windowDefNames[i],
                    firstPawn,
                    null);

                List<NarrativeEvidence> evidence =
                    diaryEvent.NarrativeEvidenceForRole(DiaryEvent.InitiatorRole);
                PawnDiaryRimTestScope.Require(
                    evidence.Count == 1,
                    windowDefNames[i] + " should save exactly one source-owned evidence row.");
                PawnDiaryRimTestScope.Require(
                    evidence[0].facet == NarrativeFacetTokens.JourneyChapter
                    && evidence[0].phase == phases[i]
                    && evidence[0].arcKey == "anomaly-monolith|0"
                    && evidence[0].pawnCanKnow == true,
                    windowDefNames[i] + " saved incorrect or knowledge-unsafe narrative evidence.");

                List<NarrativeReference> references =
                    diaryEvent.NarrativeReferencesForRole(DiaryEvent.InitiatorRole);
                PawnDiaryRimTestScope.Require(
                    references.Count == 1
                    && references[0].facet == NarrativeFacetTokens.JourneyChapter
                    && references[0].phase == phases[i]
                    && references[0].arcKey == "anomaly-monolith|0",
                    windowDefNames[i] + " did not freeze the exact monolith chapter reference.");
                PawnDiaryRimTestScope.Require(
                    string.IsNullOrWhiteSpace(diaryEvent.NarrativeContextForRole(DiaryEvent.InitiatorRole)),
                    "Wave 2 source evidence must not invent prompt context without a live Anomaly provider.");
            }

            // Gleaming is an automatic transition with no actor. The exact XML windows must leave it silent.
            scope.RequireNoNewEvent(() => scope.Component.RecordEventWindowSignal(
                "VoidMonolith", "Gleaming", "activated", "Gleaming", null, firstPawn));
        }

        // ----- test helpers -----------------------------------------------------------------------

        /// <summary>
        /// Snapshots the active-window list and dedup map at test start and registers a cleanup that removes
        /// only entries/keys added after the snapshot. The shared harness does not own either store, so this
        /// is the suite's responsibility (see harnessExtensionsNeeded). The developer's own live windows/keys
        /// (present at snapshot time) are left in place.
        /// </summary>
        private static void RegisterEventWindowStoreCleanup()
        {
            HashSet<ActiveEventWindowState> baselineWindows = SnapshotActiveWindows();
            HashSet<string> baselineKeys = SnapshotDedupKeys();

            scope.RegisterCleanup(() =>
            {
                List<ActiveEventWindowState> live = ActiveWindows();
                live?.RemoveAll(w => !baselineWindows.Contains(w));

                IDictionary recent = RecentWindowEvents();
                if (recent != null && recent.Count > 0)
                {
                    List<object> stale = new List<object>();
                    foreach (object key in recent.Keys)
                    {
                        if (!baselineKeys.Contains(key as string))
                        {
                            stale.Add(key);
                        }
                    }

                    for (int i = 0; i < stale.Count; i++)
                    {
                        recent.Remove(stale[i]);
                    }
                }
            });
        }

        private static List<ActiveEventWindowState> ActiveWindows()
        {
            return ActiveWindowsField.GetValue(scope.Component) as List<ActiveEventWindowState>;
        }

        private static IDictionary RecentWindowEvents()
        {
            return RecentWindowEventsField.GetValue(scope.Component) as IDictionary;
        }

        // Reference-identity snapshot (ActiveEventWindowState has no value equality) so cleanup can tell the
        // suite's freshly opened windows apart from any that were already live.
        private static HashSet<ActiveEventWindowState> SnapshotActiveWindows()
        {
            List<ActiveEventWindowState> live = ActiveWindows();
            return live == null
                ? new HashSet<ActiveEventWindowState>()
                : new HashSet<ActiveEventWindowState>(live);
        }

        private static HashSet<string> SnapshotDedupKeys()
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            IDictionary recent = RecentWindowEvents();
            if (recent != null)
            {
                foreach (object key in recent.Keys)
                {
                    if (key is string text)
                    {
                        keys.Add(text);
                    }
                }
            }

            return keys;
        }

        private static ActiveEventWindowState FindNewWindow(HashSet<ActiveEventWindowState> baseline, string defName)
        {
            List<ActiveEventWindowState> live = ActiveWindows();
            if (live == null)
            {
                return null;
            }

            for (int i = 0; i < live.Count; i++)
            {
                ActiveEventWindowState window = live[i];
                if (window != null
                    && !baseline.Contains(window)
                    && string.Equals(window.windowDefName, defName, StringComparison.Ordinal))
                {
                    return window;
                }
            }

            return null;
        }

        private static int CountNewWindows(HashSet<ActiveEventWindowState> baseline, string defName)
        {
            List<ActiveEventWindowState> live = ActiveWindows();
            if (live == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < live.Count; i++)
            {
                ActiveEventWindowState window = live[i];
                if (window != null
                    && !baseline.Contains(window)
                    && string.Equals(window.windowDefName, defName, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        // Asserts one of the mod's own shipped base-game event-window defs is loaded, enabled, and available
        // (its required package, if any, is present). These defs ship with Pawn Diary, so a miss is a real
        // failure, not a DLC-absent skip.
        private static DiaryEventWindowDef RequireWindowDef(string defName)
        {
            DiaryEventWindowDef def = RequireLoadedWindowDef(defName);
            PawnDiaryRimTestScope.Require(
                !def.MissingRequiredPackage(),
                "Event-window def '" + defName + "' must be enabled and available for this test.");
            return def;
        }

        private static DiaryEventWindowDef RequireLoadedWindowDef(string defName)
        {
            DiaryEventWindowDef def = DefDatabase<DiaryEventWindowDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                throw new AssertionException(
                    "Required Pawn Diary event-window def '" + defName + "' was not loaded.");
            }

            PawnDiaryRimTestScope.Require(
                def.enabled,
                "Event-window def '" + defName + "' must be enabled for this test.");
            return def;
        }

        private static void RequireContextContains(DiaryEvent diaryEvent, string expectedFragment)
        {
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf(expectedFragment, StringComparison.Ordinal) >= 0,
                "The event-window context did not contain the expected fragment '" + expectedFragment + "'.");
        }
    }
}

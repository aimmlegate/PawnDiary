// In-game flow tests for Pawn Diary's situational thought-stage PROGRESSION scanner (EVT-04).
//
// Situational need moods (hunger, exhaustion, outdoors deprivation, chemical desire) never pass
// through MemoryThoughtHandler.TryGainMemory; the component tracks them as live thought stages and
// writes a diary page only when a category first worsens to a not-yet-recorded stage. This suite
// proves that state machine end-to-end against the real production sink:
//   (a) the first scan of an active stage BASELINES it (records the stage, emits NO page);
//   (b) once a category worsens to a higher stage, the next scan emits exactly ONE solo progression
//       page with the expected progression context;
//   (c) a stage already recorded (the saved progression key) does not re-emit on a repeat scan.
//
// Why this drives the per-pawn updater and not the top-level scan:
// The top-level scanner (DiaryGameComponent.ScanThoughtProgressionsForDiaryEvents) enumerates
// PawnsFinder.AllMaps_FreeColonists — the player's REAL loaded colony. It cannot see the harness's
// isolated, unspawned test colonist, and invoking it would record real progression pages for the
// developer's actual colonists (state this suite could never clean up). The genuine EVT-04 contract
// (baseline / worsening / saved-key) lives entirely in the private per-pawn updater
// UpdateThoughtProgressionState + the private activeThoughtProgressions episode store, so this suite
// drives that updater directly against the isolated test pawn. Both are private today, so the suite
// reaches them by reflection; see productionSeamNeeded in the integration report for the internal
// test seam that would remove the reflection.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that the situational thought-progression episode tracker baselines the first observed
    /// stage without a page, emits exactly one solo page when a category worsens, and never re-emits a
    /// stage it has already recorded. Requires a loaded game (the capture pipeline ignores the menu).
    /// </summary>
    [TestSuite]
    public static class PawnDiaryThoughtProgressionFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;

        private static PawnDiaryRimTestScope scope;
        private static Pawn pawn;

        // A real, loaded ThoughtDef the progression rules target, and the category key that groups its
        // stages. Resolved from the live rule catalog so the test tracks whatever the mod ships.
        private static ThoughtDef thoughtDef;
        private static string categoryKey;

        // Reflection handles for the production progression state machine (private today).
        private static MethodInfo updateStateMethod;   // void UpdateThoughtProgressionState(Pawn, string, ThoughtProgressionMatch, bool)
        private static Type matchType;                 // private nested ThoughtProgressionMatch
        private static Type stateType;                 // private nested ActiveThoughtProgressionState
        private static FieldInfo activeProgressionsField; // Dictionary<string, ActiveThoughtProgressionState> activeThoughtProgressions
        private static FieldInfo recordedStageKeysField;  // HashSet<string> ActiveThoughtProgressionState.recordedStageKeys

        /// <summary>
        /// Opens a scope, resolves a configured progression ThoughtDef and enables its diary group (so
        /// the thought counts as user-enabled), binds the private progression reflection handles, and
        /// creates one isolated non-generating colonist. Registers cleanup for the episode store the
        /// harness does not own.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin();

            ResolveConfiguredProgressionThought();
            EnableThoughtGroup(thoughtDef);
            BindReflectionHandles();

            pawn = scope.CreateAdultColonist();

            // The episode store lives on the shared component and is keyed by "pawnId|category"; the
            // harness no-leak audit does not know about it, so this test removes its own entries.
            Pawn owner = pawn;
            scope.RegisterCleanup(() => ClearEpisodeStateFor(owner));
        }

        /// <summary>
        /// Restores every mutation and audits for leaks even if a test threw partway through.
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
                pawn = null;
            }
        }

        /// <summary>
        /// EVT-04. The first scan of an active stage baselines it: the episode store records the stage
        /// key but the production sink emits NO diary page (loaded saves must not re-page an already
        /// active need).
        /// </summary>
        [Test]
        public static void FirstScanBaselinesStageWithoutEmitting()
        {
            string stageKey = StageKey(1);

            scope.RequireNoNewEvent(() => UpdateProgression(stageIndex: 1, severity: 1, snapshotOnly: true));

            HashSet<string> recorded = RecordedStageKeys(pawn);
            PawnDiaryRimTestScope.Require(
                recorded != null && recorded.Contains(stageKey),
                "The baseline scan should have recorded the active stage key without emitting a page.");
        }

        /// <summary>
        /// EVT-04. After a category is baselined at a mild stage, a scan that observes a WORSE stage
        /// emits exactly one solo progression page carrying the expected progression context.
        /// </summary>
        [Test]
        public static void WorseningStageEmitsOneProgressionPage()
        {
            // Baseline the mild stage first (no page), exactly as the first post-load scan would.
            UpdateProgression(stageIndex: 1, severity: 1, snapshotOnly: true);

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => UpdateProgression(stageIndex: 2, severity: 2, snapshotOnly: false),
                thoughtDef.defName,
                pawn,
                null);

            scope.RequireSoloRef(diaryEvent, pawn);

            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("thought=" + thoughtDef.defName, StringComparison.Ordinal) >= 0,
                "The progression context did not identify the worsening ThoughtDef.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("thought_progression=" + categoryKey, StringComparison.Ordinal) >= 0,
                "The progression context did not identify the progression category.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("stage_index=2", StringComparison.Ordinal) >= 0,
                "The progression context did not record the worsened stage index.");
            PawnDiaryRimTestScope.Require(
                !string.IsNullOrWhiteSpace(diaryEvent.moodImpact),
                "The progression page did not classify a mood impact.");

            // The saved progression key must now include the emitted stage.
            HashSet<string> recorded = RecordedStageKeys(pawn);
            PawnDiaryRimTestScope.Require(
                recorded != null && recorded.Contains(StageKey(2)),
                "The emitted stage should have been added to the saved progression key set.");
        }

        /// <summary>
        /// EVT-04. Once a worsened stage has emitted a page, a repeat scan at that same stage (the
        /// saved progression key) does not emit a second page.
        /// </summary>
        [Test]
        public static void RecordedStageDoesNotReemit()
        {
            UpdateProgression(stageIndex: 1, severity: 1, snapshotOnly: true);

            // First observation of the worse stage: emits one page (consumed here, asserted above).
            scope.FireAndRequireEvent(
                () => UpdateProgression(stageIndex: 2, severity: 2, snapshotOnly: false),
                thoughtDef.defName,
                pawn,
                null);

            // Same stage observed again on the next scan: the saved key must suppress a re-emit.
            scope.RequireNoNewEvent(() => UpdateProgression(stageIndex: 2, severity: 2, snapshotOnly: false));
        }

        // ----- production-driver helpers ----------------------------------------------------------

        /// <summary>
        /// Invokes the real per-pawn progression updater for the test pawn with a synthesized match at
        /// the given stage/severity. Mirrors exactly what ScanThoughtProgressionsForDiaryEvents does
        /// per colonist, minus the live-colony enumeration this test must not trigger.
        /// </summary>
        private static void UpdateProgression(int stageIndex, int severity, bool snapshotOnly)
        {
            object match = BuildMatch(stageIndex, severity);
            string stateKey = pawn.GetUniqueLoadID() + "|" + categoryKey;
            try
            {
                updateStateMethod.Invoke(scope.Component, new object[] { pawn, stateKey, match, snapshotOnly });
            }
            catch (TargetInvocationException e)
            {
                // Surface the real failure, not the reflection wrapper.
                throw e.InnerException ?? e;
            }
        }

        /// <summary>Builds a private ThoughtProgressionMatch via reflection for the configured thought.</summary>
        private static object BuildMatch(int stageIndex, int severity)
        {
            object match = Activator.CreateInstance(matchType, nonPublic: true);
            SetMatchField(match, "categoryKey", categoryKey);
            SetMatchField(match, "thoughtDef", thoughtDef);
            SetMatchField(match, "thoughtDefName", thoughtDef.defName);
            SetMatchField(match, "stageIndex", stageIndex);
            SetMatchField(match, "severity", severity);
            SetMatchField(match, "label", thoughtDef.label ?? thoughtDef.defName);
            SetMatchField(match, "moodOffset", -8f);
            return match;
        }

        private static void SetMatchField(object match, string fieldName, object value)
        {
            FieldInfo field = matchType.GetField(fieldName, PublicInstance);
            if (field == null)
            {
                throw new AssertionException(
                    "EVT-04 could not bind ThoughtProgressionMatch field '" + fieldName + "'.");
            }

            field.SetValue(match, value);
        }

        /// <summary>Reads the saved progression-key set for the pawn's category, or null if none.</summary>
        private static HashSet<string> RecordedStageKeys(Pawn owner)
        {
            IDictionary store = activeProgressionsField.GetValue(scope.Component) as IDictionary;
            if (store == null)
            {
                return null;
            }

            string stateKey = owner.GetUniqueLoadID() + "|" + categoryKey;
            if (!store.Contains(stateKey))
            {
                return null;
            }

            object state = store[stateKey];
            return state == null ? null : recordedStageKeysField.GetValue(state) as HashSet<string>;
        }

        private static string StageKey(int stageIndex)
        {
            return thoughtDef.defName + "|" + stageIndex.ToString();
        }

        private static void ClearEpisodeStateFor(Pawn owner)
        {
            IDictionary store = activeProgressionsField?.GetValue(scope?.Component) as IDictionary;
            if (store == null || owner == null)
            {
                return;
            }

            string prefix = owner.GetUniqueLoadID() + "|";
            List<object> remove = new List<object>();
            foreach (object key in store.Keys)
            {
                string text = key as string;
                if (text != null && text.StartsWith(prefix, StringComparison.Ordinal))
                {
                    remove.Add(key);
                }
            }

            for (int i = 0; i < remove.Count; i++)
            {
                store.Remove(remove[i]);
            }
        }

        // ----- setup helpers ----------------------------------------------------------------------

        /// <summary>
        /// Picks the first configured thought-progression rule whose ThoughtDef is actually loaded, so
        /// the emitted page carries a real thought defName/label. Fails loudly if none are present.
        /// </summary>
        private static void ResolveConfiguredProgressionThought()
        {
            List<ThoughtProgressionRule> rules = DiarySignalPolicies.ThoughtProgressionRules;
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    ThoughtProgressionRule rule = rules[i];
                    if (rule == null || string.IsNullOrWhiteSpace(rule.thoughtDefName))
                    {
                        continue;
                    }

                    ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail(rule.thoughtDefName);
                    if (def != null)
                    {
                        thoughtDef = def;
                        categoryKey = string.IsNullOrWhiteSpace(rule.categoryKey) ? rule.thoughtDefName : rule.categoryKey;
                        return;
                    }
                }
            }

            throw new AssertionException(
                "EVT-04 requires at least one configured thought-progression ThoughtDef to be loaded.");
        }

        /// <summary>
        /// Enables the diary interaction group the progression thought classifies into, so
        /// PawnDiarySettings.IsThoughtEnabled returns true for it. The harness snapshotted groupEnabled
        /// in Begin and restores it verbatim in teardown, so no extra cleanup is needed here.
        /// </summary>
        private static void EnableThoughtGroup(ThoughtDef def)
        {
            DiaryInteractionGroupDef group = InteractionGroups.ClassifyThought(def);
            if (group != null && PawnDiaryMod.Settings != null)
            {
                PawnDiaryMod.Settings.SetGroupEnabled(group.defName, true);
            }
        }

        private static void BindReflectionHandles()
        {
            updateStateMethod = typeof(DiaryGameComponent).GetMethod("UpdateThoughtProgressionState", PrivateInstance);
            matchType = typeof(DiaryGameComponent).GetNestedType("ThoughtProgressionMatch", BindingFlags.NonPublic);
            stateType = typeof(DiaryGameComponent).GetNestedType("ActiveThoughtProgressionState", BindingFlags.NonPublic);
            activeProgressionsField = typeof(DiaryGameComponent).GetField("activeThoughtProgressions", PrivateInstance);
            recordedStageKeysField = stateType?.GetField("recordedStageKeys", PublicInstance);

            RequireHandle(updateStateMethod, "UpdateThoughtProgressionState");
            RequireHandle(matchType, "ThoughtProgressionMatch");
            RequireHandle(stateType, "ActiveThoughtProgressionState");
            RequireHandle(activeProgressionsField, "activeThoughtProgressions");
            RequireHandle(recordedStageKeysField, "ActiveThoughtProgressionState.recordedStageKeys");
        }

        private static void RequireHandle(object handle, string name)
        {
            if (handle == null)
            {
                throw new AssertionException(
                    "EVT-04 could not bind the production progression member '" + name
                    + "'. If it was renamed or made internal, update this suite (see productionSeamNeeded).");
            }
        }
    }
}

// In-game tale-capture tests for Pawn Diary's TaleRecorder.RecordTale hook. Each test calls the exact
// vanilla API a real event would (TaleRecorder.RecordTale), which the production TaleRecorderPatch
// observes through Harmony, then verifies the persisted DiaryEvent's shape and participant extraction.
// All the fragile scaffolding — isolated non-generating pawns, snapshots, RNG isolation, and
// failure-safe teardown — lives in the shared PawnDiaryRimTestScope harness, so a test body only fires
// a trigger, asserts the outcome, and registers cleanup for the one thing the harness does not own: the
// historical Tale that RecordTale adds to Find.TaleManager.
//
// Coverage-matrix ID (design/TEST_COVERAGE_PLAN.md §3): EVT-09 Tale. This suite covers single-pawn shape,
// two-pawn shape + participant extraction, the XML group toggle, and the base-game positive combat-batch
// route (accumulate, source-dedup, flush exactly once), plus knowledge-only preservation when a
// delayed batch is frequency-rejected. Death tales remain in EVT-10.
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
    /// Proves that vanilla TaleRecorder.RecordTale reaches Pawn Diary's persisted event store with the
    /// correct shape (solo vs pairwise), extracts the right participants, and is suppressed when the
    /// classifying tale group is disabled. These tests require a loaded game because the production
    /// capture pipeline intentionally ignores events at the main menu.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryTaleFlowTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo EventsField =
            typeof(DiaryGameComponent).GetField("events", PrivateInstance);
        private static readonly FieldInfo PendingTaleBatchesField =
            typeof(DiaryGameComponent).GetField("pendingTaleBatches", PrivateInstance);
        private static readonly MethodInfo FlushAllTaleBatchesMethod =
            typeof(DiaryGameComponent).GetMethod("FlushAllTaleBatches", PrivateInstance);

        private static PawnDiaryRimTestScope scope;
        private static Pawn firstPawn;
        private static Pawn secondPawn;

        /// <summary>
        /// Opens a fresh test scope, enables the two tale groups this suite drives (both ship
        /// default-enabled, so this only documents intent), and creates two isolated adult colonists
        /// with generation disabled.
        /// </summary>
        [BeforeEach]
        public static void SetUp()
        {
            scope = PawnDiaryRimTestScope.Begin("talequiet", "talelife", "talecombat");
            firstPawn = scope.CreateAdultColonist();
            secondPawn = scope.CreateAdultColonist();
        }

        /// <summary>
        /// Restores every mutation and audits that no test-owned event, diary, or historical tale
        /// survived — even when the test above threw partway through.
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
        /// EVT-09. Records a single-pawn vanilla tale (Meditated, a Tale_SinglePawn) and verifies the
        /// tale hook produced one solo diary event owned by that pawn.
        /// </summary>
        [Test]
        public static void SinglePawnTaleCreatesSoloEvent()
        {
            TaleDef taleDef = RequireDef<TaleDef>("Meditated");

            // Register the tale-removal cleanup BEFORE firing so a failing assertion inside
            // FireAndRequireEvent can never strand the historical tale in the developer's colony.
            Tale recordedTale = null;
            scope.RegisterCleanup(() => RemoveRecordedTale(recordedTale));

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => { recordedTale = TaleRecorder.RecordTale(taleDef, firstPawn); },
                "Meditated",
                firstPawn,
                null);

            scope.RequireSoloRef(diaryEvent, firstPawn);
            PawnDiaryRimTestScope.Require(
                recordedTale is Tale_SinglePawn,
                "Vanilla did not record the expected single-pawn tale for Meditated.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("tale=Meditated", StringComparison.OrdinalIgnoreCase) >= 0,
                "The tale event context did not identify the Meditated tale.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("taleClass=Tale_SinglePawn", StringComparison.OrdinalIgnoreCase) >= 0,
                "The tale event context did not record the single-pawn tale class.");
        }

        /// <summary>
        /// EVT-09. Records a two-pawn vanilla tale (Recruited, a Tale_DoublePawn) and verifies the tale
        /// hook produced one pairwise diary event whose initiator/recipient are the two supplied pawns
        /// (the first arg is the recruiter/initiator, the second is the joiner/recipient).
        /// </summary>
        [Test]
        public static void DoublePawnTaleCreatesPairEventWithBothParticipants()
        {
            TaleDef taleDef = RequireDef<TaleDef>("Recruited");

            Tale recordedTale = null;
            scope.RegisterCleanup(() => RemoveRecordedTale(recordedTale));

            DiaryEvent diaryEvent = scope.FireAndRequireEvent(
                () => { recordedTale = TaleRecorder.RecordTale(taleDef, firstPawn, secondPawn); },
                "Recruited",
                firstPawn,
                secondPawn);

            scope.RequirePairRefs(diaryEvent, firstPawn, secondPawn);
            PawnDiaryRimTestScope.Require(
                recordedTale is Tale_DoublePawn,
                "Vanilla did not record the expected two-pawn tale for Recruited.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("tale=Recruited", StringComparison.OrdinalIgnoreCase) >= 0,
                "The tale event context did not identify the Recruited tale.");
            PawnDiaryRimTestScope.Require(
                diaryEvent.gameContext != null
                    && diaryEvent.gameContext.IndexOf("taleClass=Tale_DoublePawn", StringComparison.OrdinalIgnoreCase) >= 0,
                "The tale event context did not record the two-pawn tale class.");
        }

        /// <summary>
        /// EVT-09. Disables the tale's classifying group (talequiet) and verifies the capture pipeline
        /// drops the same single-pawn tale that would otherwise become a solo event. The group override
        /// is reverted by the harness's settings snapshot in teardown.
        /// </summary>
        [Test]
        public static void DisabledTaleGroupCreatesNoEvent()
        {
            TaleDef taleDef = RequireDef<TaleDef>("Meditated");

            // Force the classifying group off for this test. talequiet ships default-enabled, so this
            // stores a player override that the scope's group-settings snapshot restores in teardown.
            PawnDiaryMod.Settings.SetGroupEnabled("talequiet", false);
            PawnDiaryRimTestScope.Require(
                !PawnDiaryMod.Settings.IsTaleEnabled(taleDef),
                "Disabling the talequiet group should have turned the Meditated tale off.");

            // RecordTale still creates and files the historical tale even though no diary event results,
            // so it must still be cleaned up.
            Tale recordedTale = null;
            scope.RegisterCleanup(() => RemoveRecordedTale(recordedTale));

            scope.RequireNoNewEvent(
                () => { recordedTale = TaleRecorder.RecordTale(taleDef, firstPawn); });
        }

        /// <summary>
        /// EVT-09. Two reversible base-game combat Tales accumulate into one pending per-pawn batch.
        /// Replaying the exact first Tale inside the source dedup window does not inflate that batch.
        /// An explicit production flush creates one combined solo page with both distinct sources, and
        /// repeating the flush creates nothing. Only removable TaleManager rows are used: no battle,
        /// injury, death, corpse, combat log, or pawn record is created.
        /// </summary>
        [Test]
        public static void CombatTalesAccumulateDedupAndFlushExactlyOnce()
        {
            TaleDef wasOnFire = RequireDef<TaleDef>("WasOnFire");
            TaleDef collapseDodged = RequireDef<TaleDef>("CollapseDodged");
            PawnDiaryRimTestScope.Require(
                DiaryGameComponent.TaleBatchGroupFor(wasOnFire)?.defName == "talecombat"
                    && DiaryGameComponent.TaleBatchGroupFor(collapseDodged)?.defName == "talecombat",
                "The reversible base-game Tales no longer classify to the shipped talecombat batch.");

            // WasOnFire normally has a 50% vanilla ignore chance. Pin both definitions to an accepted
            // RecordTale call and restore the loaded Defs even when a later assertion fails.
            float originalWasOnFireIgnoreChance = wasOnFire.ignoreChance;
            float originalCollapseDodgedIgnoreChance = collapseDodged.ignoreChance;
            wasOnFire.ignoreChance = 0f;
            collapseDodged.ignoreChance = 0f;
            scope.RegisterCleanup(() =>
            {
                wasOnFire.ignoreChance = originalWasOnFireIgnoreChance;
                collapseDodged.ignoreChance = originalCollapseDodgedIgnoreChance;
            });

            // FlushAllTaleBatches is intentionally broad because production calls it before save. Isolate
            // the dictionary first so this fixture cannot flush a real pending batch from the loaded colony.
            IsolatePendingTaleBatches();

            List<Tale> recordedTales = new List<Tale>();
            scope.RegisterCleanup(() =>
            {
                for (int i = 0; i < recordedTales.Count; i++)
                {
                    RemoveRecordedTale(recordedTales[i]);
                }
            });

            int before = TotalEventCount();
            scope.RequireNoNewEvent(() =>
            {
                recordedTales.Add(TaleRecorder.RecordTale(wasOnFire, firstPawn));
                recordedTales.Add(TaleRecorder.RecordTale(wasOnFire, firstPawn));
                recordedTales.Add(TaleRecorder.RecordTale(collapseDodged, firstPawn));
            });
            PawnDiaryRimTestScope.Require(
                recordedTales.Count == 3
                    && recordedTales[0] is Tale_SinglePawn
                    && recordedTales[1] is Tale_SinglePawn
                    && recordedTales[2] is Tale_SinglePawn,
                "Vanilla did not accept all three reversible TaleRecorder calls.");
            PawnDiaryRimTestScope.Require(
                TotalEventCount() == before,
                "A combat Tale wrote a page before its pending batch flushed.");

            DiaryEvent batchPage = scope.FireAndRequireEvent(
                FlushAllTaleBatches,
                "TaleCombatBatch",
                firstPawn,
                null);
            scope.RequireSoloRef(batchPage, firstPawn);
            RequireContextContains(batchPage, "batch=tale");
            RequireContextContains(batchPage, "group=talecombat");
            RequireContextContains(batchPage, "events=2");
            RequireContextContains(
                batchPage, "tale_defs=WasOnFire, CollapseDodged");

            // The first replay was source-deduped, so the batch is now gone and neither another page nor
            // another flushable batch remains.
            scope.RequireNoNewEvent(FlushAllTaleBatches);
            PawnDiaryRimTestScope.Require(
                TotalEventCount() == before + 1,
                "The completed Tale batch did not remain a single durable page.");
        }

        /// <summary>
        /// EVT-09. A delayed Tale owns its frequency decision outside central dispatch. When that
        /// local decision rejects the page, an add-on allowlisted fact must still reach lifelong
        /// knowledge exactly once and the rejected batch must settle without prose.
        /// </summary>
        [Test]
        public static void FrequencyRejectedCombatBatchPreservesAllowlistedKnowledge()
        {
            TaleDef wasOnFire = RequireDef<TaleDef>("WasOnFire");
            PawnDiaryRimTestScope.Require(
                DiaryGameComponent.TaleBatchGroupFor(wasOnFire)?.defName == "talecombat",
                "WasOnFire no longer classifies to the shipped talecombat batch.");

            float originalIgnoreChance = wasOnFire.ignoreChance;
            wasOnFire.ignoreChance = 0f;
            scope.RegisterCleanup(() => { wasOnFire.ignoreChance = originalIgnoreChance; });
            IsolatePendingTaleBatches();

            DiaryInteractionGroupDef group = DiaryGameComponent.TaleBatchGroupFor(wasOnFire);
            PawnDiarySettings settings = PawnDiaryMod.Settings;
            float previousMultiplier;
            bool hadOverride = settings.TryGetGroupFrequencyOverride(
                group.defName, out previousMultiplier);
            float inheritedMultiplier = settings.PresetGroupFrequencyMultiplier(group);
            settings.SetGroupFrequencyOverride(group.defName, 0f);
            scope.RegisterCleanup(() => settings.SetGroupFrequencyOverride(
                group.defName,
                hadOverride ? previousMultiplier : inheritedMultiplier));

            string eventKind = "rimtest.tale.frequency." + Guid.NewGuid().ToString("N");
            ImportantEventRule rule = new ImportantEventRule
            {
                defName = "PawnDiary_RimTest_TaleBatchFrequencyKnowledge",
                eventKind = eventKind,
                topicKey = "rimtest",
                signal = KnowledgeTokens.SignalEvent,
                order = int.MinValue,
                owners = KnowledgeTokens.OwnersInitiator,
                lineTemplate = "remembered a rejected batched Tale"
            };
            rule.matchDefNames.Add(wasOnFire.defName);
            List<ImportantEventRule> rules = DiaryKnowledgePolicy.ImportantEventRules();
            rules.Add(rule);
            scope.RegisterCleanup(() => rules.Remove(rule));

            Tale recordedTale = null;
            scope.RegisterCleanup(() => RemoveRecordedTale(recordedTale));
            scope.RequireNoNewEvent(
                () => { recordedTale = TaleRecorder.RecordTale(wasOnFire, firstPawn); });
            PawnDiaryRimTestScope.Require(
                PendingTaleBatchCount() == 1,
                "The rejected Tale occurrence did not leave exactly one pending owner batch.");

            PawnKnowledgeState knowledge = scope.RequireDiaryRecord(firstPawn).EnsureKnowledgeState();
            PawnDiaryRimTestScope.Require(
                CountKnowledgeKind(knowledge, eventKind) == 1,
                "The locally frequency-rejected Tale did not capture exactly one allowlisted fact.");

            scope.RequireNoNewEvent(FlushAllTaleBatches);
            PawnDiaryRimTestScope.Require(
                PendingTaleBatchCount() == 0,
                "Flushing did not consume the locally rejected Tale batch.");
            PawnDiaryRimTestScope.Require(
                CountKnowledgeKind(knowledge, eventKind) == 1,
                "Flushing the rejected Tale batch duplicated or erased its knowledge-only fact.");
        }

        // ----- helpers ---------------------------------------------------------------------------

        // Verse.TaleManager exposes no public single-tale removal (volatile tales normally expire on
        // their own during RemoveExpiredTales). Test cleanup removes the one tale it filed directly from
        // the manager's private backing list — the same reflection pattern the shared harness uses for
        // private state. Resolved by List<Tale> type, so a field rename does not silently break it.
        private static readonly FieldInfo TalesListField = ResolveTalesListField();

        private static FieldInfo ResolveTalesListField()
        {
            FieldInfo[] fields = typeof(TaleManager).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            for (int i = 0; i < fields.Length; i++)
            {
                Type fieldType = fields[i].FieldType;
                if (typeof(IList).IsAssignableFrom(fieldType)
                    && fieldType.IsGenericType
                    && fieldType.GetGenericArguments()[0] == typeof(Tale))
                {
                    return fields[i];
                }
            }

            return null;
        }

        private static void RemoveRecordedTale(Tale tale)
        {
            if (tale == null || Find.TaleManager == null || TalesListField == null)
            {
                return;
            }

            IList tales = TalesListField.GetValue(Find.TaleManager) as IList;
            tales?.Remove(tale);
        }

        /// <summary>
        /// Temporarily owns the entire transient Tale-batch dictionary. This mirrors the shared harness's
        /// collection snapshots: player rows are retained by reference, removed while the test runs, and
        /// restored exactly after every fixture-owned row has been discarded.
        /// </summary>
        private static void IsolatePendingTaleBatches()
        {
            IDictionary batches =
                PendingTaleBatchesField?.GetValue(scope.Component) as IDictionary;
            if (batches == null)
            {
                throw new AssertionException(
                    "Pawn Diary Tale test could not locate pendingTaleBatches.");
            }

            List<DictionaryEntry> original = new List<DictionaryEntry>();
            foreach (DictionaryEntry entry in batches)
            {
                original.Add(entry);
            }

            scope.RegisterCleanup(() =>
            {
                batches.Clear();
                for (int i = 0; i < original.Count; i++)
                {
                    batches[original[i].Key] = original[i].Value;
                }
            });
            batches.Clear();
        }

        private static void FlushAllTaleBatches()
        {
            if (FlushAllTaleBatchesMethod == null)
            {
                throw new AssertionException(
                    "Pawn Diary Tale test could not locate FlushAllTaleBatches.");
            }

            FlushAllTaleBatchesMethod.Invoke(scope.Component, null);
        }

        private static int PendingTaleBatchCount()
        {
            IDictionary batches = PendingTaleBatchesField?.GetValue(scope.Component) as IDictionary;
            if (batches == null)
            {
                throw new AssertionException(
                    "Pawn Diary Tale test could not locate pendingTaleBatches.");
            }

            return batches.Count;
        }

        private static int CountKnowledgeKind(PawnKnowledgeState state, string eventKind)
        {
            int count = 0;
            if (state?.records == null)
            {
                return count;
            }

            for (int i = 0; i < state.records.Count; i++)
            {
                ImportantMemoryRecord record = state.records[i];
                if (record != null
                    && string.Equals(record.eventKind, eventKind, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static int TotalEventCount()
        {
            DiaryEventRepository repository =
                EventsField?.GetValue(scope.Component) as DiaryEventRepository;
            if (repository == null)
            {
                throw new AssertionException(
                    "Pawn Diary Tale test could not locate the event repository.");
            }

            return repository.AllEvents.Count;
        }

        private static void RequireContextContains(
            DiaryEvent diaryEvent,
            string expectedFragment)
        {
            PawnDiaryRimTestScope.Require(
                diaryEvent?.gameContext != null
                    && diaryEvent.gameContext.IndexOf(
                        expectedFragment, StringComparison.Ordinal) >= 0,
                "The Tale batch context did not contain '" + expectedFragment + "'.");
        }

        private static TDef RequireDef<TDef>(string defName) where TDef : Def
        {
            TDef def = DefDatabase<TDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                throw new AssertionException(
                    "Required vanilla " + typeof(TDef).Name + " '" + defName + "' was not loaded.");
            }

            return def;
        }
    }
}
